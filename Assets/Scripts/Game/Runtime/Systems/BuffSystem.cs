using System;
using System.Collections.Generic;
using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 一个已经获得的 buff 实例。每次抽到同一张 buff 卡就新建一个实例：
    /// 各自有自己的触发层数，BuildController 只限制"这张卡最多拿几次"。
    /// </summary>
    public sealed class BuffRuntime
    {
        public readonly BuffDef Def;

        /// <summary>触发叠层数（击杀叠攻速、受击叠甲、低血加攻的开关）。</summary>
        public int TriggerStacks { get; internal set; }

        internal float IntervalTimer;
        internal float EffectTimer;
        internal bool LowHpActive;
        internal readonly object LowHpSource = new object();

        internal BuffRuntime(BuffDef def)
        {
            Def = def;
        }

        public float GetParam(int index, float fallback) => Def.GetParam(index, fallback);
    }

    /// <summary>
    /// buff 道具的运行时。两类效果：
    ///   1. 获得时写进 StatBlock 的永久属性（Source = 这个 BuffRuntime）；
    ///   2. 挂在 UnitDied / PlayerDamaged / 每个固定步上的触发效果。
    ///
    /// 触发层数用同一份 Source 逐层 Add，ResetForNewRun 时 RemoveBySource 一次退干净。
    /// 低血加攻需要"回到阈值以上就撤掉"，所以它单独用一个 LowHpSource，
    /// 不能用 runtime 本身——那会把同一件 buff 的永久属性一起退掉。
    ///
    /// 只认 Game.Model 与事件，不碰 UI；击杀归属按 Killer.Kind 判断玩家/旅伴，
    /// 敌人互杀不会给玩家叠 buff。
    /// </summary>
    public sealed class BuffSystem
    {
        readonly PlayerModel _player;
        readonly IEventBus _events;
        readonly EnemySystem _enemies;
        readonly UnitId _playerUnit;
        readonly List<BuffRuntime> _buffs = new List<BuffRuntime>(8);
        readonly List<int> _effectTargets = new List<int>(16);

        public BuffSystem(PlayerModel player, IEventBus events, EnemySystem enemies = null,
            UnitId playerUnit = default)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _events = events;
            _enemies = enemies;
            _playerUnit = playerUnit.IsValid ? playerUnit : UnitId.Player;

            if (_events != null)
            {
                _events.Subscribe<UnitDied>(OnUnitDied);
                _events.Subscribe<UnitDamaged>(OnUnitDamaged);
                _events.Subscribe<PlayerDamaged>(OnPlayerDamaged);
            }
        }

        public IReadOnlyList<BuffRuntime> Buffs => _buffs;

        public int Count => _buffs.Count;

        /// <summary>
        /// 同名 buff 拿到几次。只在测试与调试里用：构筑卡的叠层上限由 BuildController 自己计数
        /// （BuffCardsOf，按 UpgradeDef.MaxStacks 算），两边共用一份状态会让“卡面数量”与“实际份数”绑死，
        /// 所以这里保留一个只读查询、不反向驱动构筑。
        /// </summary>
        public int CountOf(string buffId)
        {
            if (string.IsNullOrEmpty(buffId)) return 0;

            int count = 0;
            for (int i = 0; i < _buffs.Count; i++)
            {
                if (_buffs[i].Def.Id == buffId) count++;
            }
            return count;
        }

        /// <summary>获得一件 buff。先写永久属性，再登记触发状态；返回新实例。</summary>
        public BuffRuntime Acquire(BuffDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            var runtime = new BuffRuntime(def);
            ApplyBaseStats(runtime);
            _buffs.Add(runtime);
            return runtime;
        }

        /// <summary>
        /// 每个固定步推进：低血判定与周期回复。
        /// 稳态只做几十次比较与一次定时器减法，不分配。
        /// </summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;

            float maxHp = _player.MaxHP;
            float hpRatio = maxHp > 0f ? _player.HP / maxHp : 0f;

            for (int i = 0; i < _buffs.Count; i++)
            {
                BuffRuntime buff = _buffs[i];
                if (buff.EffectTimer > 0f) buff.EffectTimer -= dt;

                if (buff.Def.Effect == BuffEffectType.OrbitDamage)
                {
                    int procs = AdvanceInterval(ref buff.IntervalTimer, buff.Def.Interval, dt);
                    for (int n = 0; n < procs; n++)
                    {
                        ApplyAreaDamage(_player.Position, buff.GetParam(0, 2.4f), buff.GetParam(1, 8f),
                            _playerUnit, int.MaxValue, CombatAreaEffectKind.Orbit);
                    }
                    continue;
                }

                if (buff.Def.Effect == BuffEffectType.LowHpAttack)
                {
                    float threshold = buff.GetParam(0, 0.35f);
                    bool shouldBeActive = hpRatio < threshold;
                    if (shouldBeActive != buff.LowHpActive)
                    {
                        _player.Stats.RemoveBySource(buff.LowHpSource);
                        buff.LowHpActive = shouldBeActive;
                        if (shouldBeActive)
                        {
                            float attack = buff.GetParam(1, 0.35f);
                            _player.Stats.Add(new StatModifier(StatType.Attack, attack, ModOp.Flat,
                                buff.LowHpSource));
                        }
                    }
                }

                if (buff.Def.Effect != BuffEffectType.PeriodicHeal) continue;

                int heals = AdvanceInterval(ref buff.IntervalTimer, buff.Def.Interval, dt);
                for (int n = 0; n < heals; n++) Heal(buff.GetParam(0, 2f));
            }
        }

        /// <summary>
        /// 周期效果的计时：返回本步该结算几次。dt 大于一个间隔时要连续结算多次（while 语义），
        /// 否则一帧卡顿会把缺失的几发直接丢掉。OrbitDamage 与 PeriodicHeal 共用这一个计时器，
        /// 把“累加 → 扣除整周 → 多周补齐”的技巧只留一份；不引入策略类，因为两者的差异只有“结算什么”。
        /// </summary>
        static int AdvanceInterval(ref float timer, float interval, float dt)
        {
            if (interval <= 0f) return 0;

            timer += dt;
            int fired = 0;
            while (timer >= interval)
            {
                timer -= interval;
                fired++;
            }
            return fired;
        }

        /// <summary>换局清空。所有永久属性与触发属性都按 Source 退掉，队伍/关卡状态不留下上一局的加成。</summary>
        public void ResetForNewRun()
        {
            for (int i = 0; i < _buffs.Count; i++)
            {
                _player.Stats.RemoveBySource(_buffs[i]);
                _player.Stats.RemoveBySource(_buffs[i].LowHpSource);
            }

            _buffs.Clear();
        }

        void ApplyBaseStats(BuffRuntime buff)
        {
            if (buff.Def.HasStatA)
            {
                _player.Stats.Add(new StatModifier(buff.Def.StatA, buff.Def.ValueA,
                    ToModOp(buff.Def.OpA), buff));
            }

            if (buff.Def.HasStatB)
            {
                _player.Stats.Add(new StatModifier(buff.Def.StatB, buff.Def.ValueB,
                    ToModOp(buff.Def.OpB), buff));
            }
        }

        void OnUnitDied(UnitDied e)
        {
            if (e.Unit.Kind != UnitKind.Enemy) return;
            if (e.Killer.Kind != UnitKind.Player && e.Killer.Kind != UnitKind.Companion) return;

            // UnitDied 只带 Id，不带种类之外的强度信息。精英/BOSS 的额外处理归 RunController 的结算统计，
            // 这里所有敌方死亡都算一次击杀触发，避免为了一个 buff 再把 EnemyModel 传进事件里。
            for (int i = 0; i < _buffs.Count; i++)
            {
                BuffRuntime buff = _buffs[i];
                switch (buff.Def.Effect)
                {
                    case BuffEffectType.OnKillHeal:
                        Heal(buff.GetParam(0, 1f));
                        break;

                    case BuffEffectType.OnKillAttackSpeed:
                        if (buff.TriggerStacks >= buff.Def.MaxStacks) break;
                        buff.TriggerStacks++;
                        _player.Stats.Add(new StatModifier(StatType.AttackSpeed,
                            buff.GetParam(0, 0.02f), ModOp.Flat, buff));
                        break;

                    case BuffEffectType.OnKillExplode:
                        ApplyAreaDamage(new Vector2(e.X, e.Z), buff.GetParam(0, 3f),
                            buff.GetParam(1, 10f), e.Killer, 12, CombatAreaEffectKind.Explosion);
                        break;
                }
            }
        }

        /// <summary>
        /// 命中触发。只处理玩家/旅伴造成的、非 buff 连锁产生的伤害：
        ///   - Lifesteal：按实际伤害回血；
        ///   - OnHitChain：向目标附近弹射闪电，有冷却；
        ///   - OnHitExplode：在目标位置引发小范围爆炸，有冷却。
        /// 连锁伤害写 FromEffect=true，避免自己再触发自己。
        /// </summary>
        void OnUnitDamaged(UnitDamaged e)
        {
            if (e.FromEffect || e.Amount <= 0f) return;
            if (e.Source.Kind != UnitKind.Player && e.Source.Kind != UnitKind.Companion) return;
            if (e.Target.Kind != UnitKind.Enemy) return;

            EnemyModel target = ResolveEnemy(e.Target);
            if (target == null) return;

            for (int i = 0; i < _buffs.Count; i++)
            {
                BuffRuntime buff = _buffs[i];
                switch (buff.Def.Effect)
                {
                    case BuffEffectType.Lifesteal:
                        Heal(e.Amount * buff.GetParam(0, 0.1f));
                        break;

                    case BuffEffectType.OnHitChain:
                        if (buff.EffectTimer > 0f) break;
                        buff.EffectTimer = Mathf.Max(0.15f, buff.Def.Interval);
                        ApplyAreaDamage(target.Position, buff.GetParam(0, 4.5f), buff.GetParam(1, 6f),
                            e.Source, Mathf.RoundToInt(buff.GetParam(2, 3f)), CombatAreaEffectKind.Chain);
                        break;

                    case BuffEffectType.OnHitExplode:
                        if (buff.EffectTimer > 0f) break;
                        buff.EffectTimer = Mathf.Max(0.15f, buff.Def.Interval);
                        ApplyAreaDamage(target.Position, buff.GetParam(0, 2.8f), buff.GetParam(1, 10f),
                            e.Source, 12, CombatAreaEffectKind.Explosion);
                        break;
                }
            }
        }

        void OnPlayerDamaged(PlayerDamaged e)
        {
            if (e.Amount <= 0f) return;

            // 受击叠甲与反伤是两类独立触发，一次遍历各自处理：叠甲只改玩家属性、不发事件，
            // 反伤只向事件队列排伤害。两者之间没有可观察的先后依赖，所以合并不会改变结果，
            // 但每次受击从“扫两遍 buff 表”变成一遍。
            for (int i = 0; i < _buffs.Count; i++)
            {
                BuffRuntime buff = _buffs[i];

                if (buff.Def.Effect == BuffEffectType.OnHurtArmor)
                {
                    if (buff.TriggerStacks >= buff.Def.MaxStacks) continue;

                    buff.TriggerStacks++;
                    _player.Stats.Add(new StatModifier(StatType.Armor, buff.GetParam(0, 1f),
                        ModOp.Flat, buff));
                    continue;
                }

                if (buff.Def.Effect != BuffEffectType.OnHurtThorns) continue;
                if (buff.EffectTimer > 0f) continue;

                buff.EffectTimer = Mathf.Max(0.25f, buff.Def.Interval);
                ApplyAreaDamage(_player.Position, buff.GetParam(0, 3f), buff.GetParam(1, 8f),
                    _playerUnit, 12, CombatAreaEffectKind.Thorns);
            }
        }

        EnemyModel ResolveEnemy(UnitId id)
        {
            if (_enemies == null || !_enemies.TryGetSlot(id, out int slot)) return null;
            EnemyModel enemy = _enemies.Get(slot);
            return enemy != null && enemy.IsAlive && !enemy.IsDead ? enemy : null;
        }

        /// <summary>
        /// 对 center 半径内的敌人造成一次来源为 source 的伤害。伤害走 WeaponDamage.ApplyToEnemy，
        /// 所以护甲、击退、击杀归属与武器走同一条路径。maxTargets 用来给链电/爆炸封顶。
        /// </summary>
        void ApplyAreaDamage(Vector2 center, float radius, float damage, UnitId source, int maxTargets,
            CombatAreaEffectKind kind)
        {
            if (_enemies == null || damage <= 0f || radius <= 0f) return;

            _effectTargets.Clear();
            _enemies.Grid.QueryCircle(center, radius, _effectTargets);

            int appliedCount = 0;
            for (int i = 0; i < _effectTargets.Count && appliedCount < maxTargets; i++)
            {
                EnemyModel enemy = _enemies.Get(_effectTargets[i]);
                if (enemy == null || !enemy.IsAlive || enemy.IsDead) continue;

                Vector2 toEnemy = enemy.Position - center;
                if (toEnemy.sqrMagnitude > radius * radius) continue;

                Vector2 direction = toEnemy.sqrMagnitude > 1e-8f ? toEnemy.normalized : Vector2.up;
                var info = new DamageInfo(damage, DamageType.Physical, source, direction,
                    WeaponDamage.KnockbackSpeed(damage), false,
                    DamageFlags.Dodgeable);

                // 连锁伤害的闪避骰固定传 1（敌人表没有闪避来源），FromEffect = true 避免二次触发。
                // 受击事件只在真扣到时发，与武器共用同一个发布入口。
                DamageResult result = WeaponDamage.ApplyAndPublish(_events, enemy, info, 1f, true);
                if (result.Applied <= 0f) continue;

                appliedCount++;
            }

            // 范围伤害命中至少一个目标才发表现事件：空放的爆炸不该在屏幕上亮一圈。
            if (appliedCount > 0)
            {
                _events?.Publish(new CombatAreaEffect(source, center.x, center.y, radius, kind));
            }
        }

        void Heal(float amount)
        {
            if (amount <= 0f || _player.IsDead) return;

            float maxHp = _player.MaxHP;
            if (_player.HP >= maxHp) return;
            _player.HP = Mathf.Min(_player.HP + amount, maxHp);
        }

        static ModOp ToModOp(StatOp op)
        {
            switch (op)
            {
                case StatOp.Percent: return ModOp.Percent;
                case StatOp.Multiply: return ModOp.Multiply;
                default: return ModOp.Flat;
            }
        }
    }
}
