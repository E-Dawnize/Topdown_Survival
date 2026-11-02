using System;
using System.Collections.Generic;
using Game.Core.Events;
using Game.Model;
using UnityEngine;

namespace Game.Systems.Weapons
{
    /// <summary>
    /// 砍刀。扇形判定：半径 = Range，半角 = SpreadDeg（表里 45 ⇒ 90° 扇角，与技术方案 8.3 一致）。
    /// 冷却到点、且扇形里有活目标才开始挥——背后有敌人也空挥的话，"射程外不开火"这条在近战上就失效了。
    ///
    /// 伤害绑挥砍窗口而不是绑冷却：窗口来自 WeaponModel.MeleeWindowStart/End（表里 Params 的前两项），
    /// 只有落在窗口里的那几步才结算。表现层的扇形弧线也读同一个窗口，所以「看到弧线」=「这一下真的在判定」。
    /// 同一次挥砍对同一个敌人只结算一次，靠 _hitThisSwing 记录，挥砍结束时清空。
    /// </summary>
    public sealed class MeleeBehaviour : IWeaponBehaviour
    {
        readonly EnemySystem _enemies;
        readonly IRandom01 _rng;
        readonly UnitId _shooter;

        /// <summary>命中事件往哪发。可空：武器单测不需要事件总线，传 null 就只扣血不发事件。</summary>
        readonly IEventBus _events;

        // 每步的候选与"本次挥砍已经打过谁"都复用同一个容器：近战每 tick 都在查，自己 new 就是稳定 GC
        readonly List<int> _candidates = new List<int>(16);
        readonly List<int> _hitThisSwing = new List<int>(8);

        bool _swinging;
        float _swingTime;
        float _windowStart;
        float _windowEnd;

        // 暴击在挥砍开始就掷好：同一次挥砍打三个敌人应该一起暴或一起不暴
        bool _swingCrit;

        public MeleeBehaviour(EnemySystem enemies, IRandom01 rng, UnitId shooter, IEventBus events = null)
        {
            _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _shooter = shooter;
            _events = events;
        }

        public void Tick(WeaponModel weapon, Vector2 shooterPosition, Vector2 facing, in ShooterStats stats, float dt)
        {
            // 射程每步都写一份：近战的常驻范围指示器在第一次出手之前就要显示
            // 加成后的射程，不能等到 Fire 时才更新。
            weapon.LastRange = stats.EffectiveRange(weapon.Range);

            if (_swinging)
            {
                _swingTime += dt;

                if (_swingTime >= _windowStart && _swingTime <= _windowEnd)
                {
                    ResolveHits(weapon, shooterPosition, facing, stats,
                        stats.EffectiveRange(weapon.Range));
                }

                if (_swingTime > _windowEnd)
                {
                    _swinging = false;
                    _hitThisSwing.Clear();
                }

                // 一次挥砍没走完不会开始下一次：攻速高到间隔短于挥砍时长时，出手频率被挥砍时长卡住。
                // 这是刻意的——判定绑窗口，硬塞第二刀会让同一次挥砍的窗口重叠，去重集合反而互相清掉。
                return;
            }

            if (!weapon.IsReady) return;

            if (!HasTargetInFan(shooterPosition, facing, stats.EffectiveRange(weapon.Range),
                    weapon.SpreadDeg)) return;

            _swinging = true;
            _swingTime = 0f;
            _hitThisSwing.Clear();

            _windowStart = weapon.MeleeWindowStart;
            _windowEnd = weapon.MeleeWindowEnd;

            _swingCrit = WeaponDamage.RollCrit(_rng.Next01(), stats);
            weapon.AimDirection = facing;
            // 表现层的扇形按这个值画：射程加成以后弧线与判定仍然同宽。
            weapon.LastRange = stats.EffectiveRange(weapon.Range);
            weapon.Fire(stats);
        }

        /// <summary>窗口内结算：扇形里的活目标逐个掉血，本次挥砍已经打过的跳过。</summary>
        void ResolveHits(WeaponModel weapon, Vector2 origin, Vector2 facing, in ShooterStats stats,
            float range)
        {
            _candidates.Clear();
            _enemies.Grid.QueryCircle(origin, range, _candidates);

            for (int i = 0; i < _candidates.Count; i++)
            {
                int slot = _candidates[i];
                EnemyModel enemy = _enemies.Get(slot);
                if (enemy == null || !enemy.IsAlive || enemy.IsDead) continue;

                // 同一次挥砍的去重按稳定实体 ID，不按槽位：槽位复用后新敌人不能被当成
                // “这次已经砍过的那个”跳过。
                if (_hitThisSwing.Contains(enemy.Id.Value)) continue;

                Vector2 toEnemy = enemy.Position - origin;
                float distance = toEnemy.magnitude;

                // 网格是快照，敌人可能已经走出半径，用当前位置复核
                if (distance > range) continue;
                if (!IsInsideFan(toEnemy, distance, facing, weapon.SpreadDeg)) continue;

                Vector2 direction = distance > 1e-4f ? toEnemy / distance : facing;
                float falloff = WeaponDamage.DistanceFalloff(distance, range);
                float amount = WeaponDamage.Amount(weapon.BaseDamage, stats, _swingCrit, falloff);

                // 击退强度按本次命中的伤害换算，与远程共用同一个换算（见 WeaponDamage.KnockbackSpeed）
                DamageInfo damage = WeaponDamage.Compose(amount, _shooter, direction, _swingCrit,
                    WeaponDamage.KnockbackSpeed(amount));

                // 和远程一样只在真扣到血时发事件：闪避与无敌帧不该让受击表现亮一下。
                // 不发这个事件，刀砍中就没有定格、震屏和受击变亮，只有远程才有反馈。
                WeaponDamage.ApplyAndPublish(_events, enemy, damage, _rng.Next01());

                // 记在命中之后再写：被闪避掉也算"这次挥砍已经判过它"，否则同一次挥砍会对着闪避目标连骰
                _hitThisSwing.Add(enemy.Id.Value);
            }
        }

        /// <summary>扇形里有没有活目标。挥砍前的索敌，与结算用同一套判定，避免"看着打到了却没判定"。</summary>
        bool HasTargetInFan(Vector2 origin, Vector2 facing, float range, float halfAngleDeg)
        {
            _candidates.Clear();
            _enemies.Grid.QueryCircle(origin, range, _candidates);

            for (int i = 0; i < _candidates.Count; i++)
            {
                int slot = _candidates[i];
                EnemyModel enemy = _enemies.Get(slot);
                if (enemy == null || !enemy.IsAlive || enemy.IsDead) continue;

                Vector2 toEnemy = enemy.Position - origin;
                float distance = toEnemy.magnitude;
                if (distance > range) continue;
                if (IsInsideFan(toEnemy, distance, facing, halfAngleDeg)) return true;
            }
            return false;
        }

        /// <summary>
        /// 扇形判定按夹角算，不按点积阈值：表里填的就是角度，读表能直接对上手感。
        /// 半角 &gt;= 180° 当全向（表里填 180 就是"身边一圈都砍"）；贴身重合时方向没有意义，算命中。
        /// </summary>
        static bool IsInsideFan(Vector2 toEnemy, float distance, Vector2 facing, float halfAngleDeg)
        {
            if (halfAngleDeg >= 180f) return true;
            if (distance < 1e-4f) return true;

            float cosine = Vector2.Dot(toEnemy / distance, facing);
            float angle = Mathf.Acos(Mathf.Clamp(cosine, -1f, 1f)) * Mathf.Rad2Deg;
            return angle <= halfAngleDeg;
        }
    }
}
