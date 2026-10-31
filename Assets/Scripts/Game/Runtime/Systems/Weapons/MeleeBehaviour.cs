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
    /// 伤害绑挥砍窗口而不是绑冷却：武器表里 Params = [0.08, 0.22] 是起手到收招的秒数，
    /// 只有落在窗口里的那几步才结算。绑冷却的话攻速加成会凭空变成伤害——冷却越短判定越频繁，
    /// 等于把"出手更快"偷偷换成了"每次挥砍打更多下"。
    /// 同一次挥砍对同一个敌人只结算一次，靠 _hitThisSwing 记录，挥砍结束时清空。
    /// </summary>
    public sealed class MeleeBehaviour : IWeaponBehaviour
    {
        /// <summary>武器表没填 Params 时退回技术方案 8.3 的窗口（0.08 ~ 0.22 秒）。</summary>
        const float DefaultWindowStart = 0.08f;
        const float DefaultWindowEnd = 0.22f;

        const int WindowStartParam = 0;
        const int WindowEndParam = 1;

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
            if (_swinging)
            {
                _swingTime += dt;

                if (_swingTime >= _windowStart && _swingTime <= _windowEnd)
                {
                    ResolveHits(weapon, shooterPosition, facing, stats);
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

            if (!HasTargetInFan(shooterPosition, facing, weapon.Range, weapon.SpreadDeg)) return;

            _swinging = true;
            _swingTime = 0f;
            _hitThisSwing.Clear();

            _windowStart = weapon.Def.GetParam(WindowStartParam, DefaultWindowStart);
            _windowEnd = weapon.Def.GetParam(WindowEndParam, DefaultWindowEnd);
            if (_windowEnd < _windowStart)
            {
                // 表里写反了：按小的当起点继续结算，而不是让整把刀永远打不出伤害
                float swap = _windowStart;
                _windowStart = _windowEnd;
                _windowEnd = swap;
            }

            _swingCrit = WeaponDamage.RollCrit(_rng.Next01(), stats);
            weapon.Fire(stats);
        }

        /// <summary>窗口内结算：扇形里的活目标逐个掉血，本次挥砍已经打过的跳过。</summary>
        void ResolveHits(WeaponModel weapon, Vector2 origin, Vector2 facing, in ShooterStats stats)
        {
            _candidates.Clear();
            _enemies.Grid.QueryCircle(origin, weapon.Range, _candidates);

            for (int i = 0; i < _candidates.Count; i++)
            {
                int slot = _candidates[i];
                if (_hitThisSwing.Contains(slot)) continue;

                EnemyModel enemy = _enemies.Get(slot);
                if (enemy == null || !enemy.IsAlive || enemy.IsDead) continue;

                Vector2 toEnemy = enemy.Position - origin;
                float distance = toEnemy.magnitude;

                // 网格是快照，敌人可能已经走出半径，用当前位置复核
                if (distance > weapon.Range) continue;
                if (!IsInsideFan(toEnemy, distance, facing, weapon.SpreadDeg)) continue;

                Vector2 direction = distance > 1e-4f ? toEnemy / distance : facing;
                float falloff = WeaponDamage.DistanceFalloff(distance, weapon.Range);
                float amount = WeaponDamage.Amount(weapon.BaseDamage, stats, _swingCrit, falloff);

                // 击退强度按本次命中的伤害换算，与远程共用同一个换算（见 WeaponDamage.KnockbackSpeed）
                DamageInfo damage = WeaponDamage.Compose(amount, _shooter, direction, _swingCrit,
                    WeaponDamage.KnockbackSpeed(amount));
                DamageResult result = WeaponDamage.ApplyToEnemy(enemy, damage, _rng.Next01());

                // 和远程一样只在真扣到血时发：闪避与无敌帧不该让受击表现亮一下。
                // 不发这个事件，刀砍中就没有定格、震屏和受击变亮，只有远程才有反馈。
                if (result.Applied > 0f)
                {
                    _events?.Publish(new UnitDamaged(EnemySystem.UnitIdOf(slot), _shooter,
                        result.Applied, _swingCrit, result.RemainingHp));
                }

                // 记在命中之后再写：被闪避掉也算"这次挥砍已经判过它"，否则同一次挥砍会对着闪避目标连骰
                _hitThisSwing.Add(slot);
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
