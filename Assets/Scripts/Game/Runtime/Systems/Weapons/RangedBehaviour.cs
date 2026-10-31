using System;
using System.Collections.Generic;
using Game.Core.Events;
using Game.Model;
using UnityEngine;

namespace Game.Systems.Weapons
{
    /// <summary>
    /// 弓箭。冷却到点、且射程内有活着的目标才开火：朝最近的一个铺开一串弹道。
    /// 射程外不开火也不吃冷却——先打空枪再等 CD 的话，边追边打时永远开不出火。
    /// 一次开火只掷一次暴击：同一次扳机打出的多发弹道同生同暴，否则一枪的伤害数字自相矛盾。
    /// </summary>
    public sealed class RangedBehaviour : IWeaponBehaviour
    {
        /// <summary>目标与射手几乎重合时的方向兜底距离阈值。</summary>
        const float MinAimDistance = 1e-4f;

        /// <summary>GDD 5.1：弹道数上限 12。加成来自道具/羁绊，夹一下就不能靠堆属性把屏幕刷满弹道。</summary>
        const int MaxProjectiles = 12;

        readonly EnemySystem _enemies;
        readonly ProjectileSystem _projectiles;
        readonly IRandom01 _rng;
        readonly UnitId _shooter;

        // 索敌结果复用同一个容器：每把武器每 tick 都要查，自己 new 就是稳定 GC
        readonly List<int> _candidates = new List<int>(16);

        public RangedBehaviour(EnemySystem enemies, ProjectileSystem projectiles, IRandom01 rng, UnitId shooter)
        {
            _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            _projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _shooter = shooter;
        }

        public void Tick(WeaponModel weapon, Vector2 shooterPosition, Vector2 facing, in ShooterStats stats, float dt)
        {
            if (!weapon.IsReady) return;

            // 表里速度填 0 的远程武器永远打不中，是导表该拦的数据错误；
            // 这里不开火也不吃冷却，至少不会把一件坏数据变成"这把武器哑了 0.85 秒"
            if (weapon.ProjectileSpeed <= 0f) return;

            int slot = FindNearestAlive(shooterPosition, weapon.Range);
            if (slot < 0) return;

            EnemyModel target = _enemies.Get(slot);
            Vector2 toTarget = target.Position - shooterPosition;
            float distance = toTarget.magnitude;

            // 网格里存的是上一次 Rebuild 的位置，目标可能这一 tick 已经跑出射程，用当前位置复核一遍
            if (distance > weapon.Range) return;

            // 贴身重合时方向没有意义，退回射手朝向：距离为 0，取哪个方向都不影响命中判定
            Vector2 aim = distance > MinAimDistance ? toTarget / distance : facing;

            bool isCrit = WeaponDamage.RollCrit(_rng.Next01(), stats);
            float falloff = WeaponDamage.DistanceFalloff(distance, weapon.Range);
            float amount = WeaponDamage.Amount(weapon.BaseDamage, stats, isCrit, falloff);

            int count = (weapon.Projectiles > 0 ? weapon.Projectiles : 1) + stats.BonusProjectiles;
            if (count > MaxProjectiles) count = MaxProjectiles;
            if (count < 1) count = 1;

            // 整束以瞄准方向为中心对称展开：3 发 10° 就是 -10° / 0° / +10°，
            // 不是从瞄准方向往一边铺，否则多发弹道的平均落点会偏，玩家会觉得"瞄准没用"
            float stepRad = weapon.SpreadDeg * Mathf.Deg2Rad;
            float baseAngle = Mathf.Atan2(aim.y, aim.x);
            float firstAngle = baseAngle - (count - 1) * stepRad * 0.5f;

            // 最远飞行距离 = 射程：箭飞到射程尽头就回收，让箭继续飞只会打到锁定范围外、本来就不该被打到的目标
            float maxDistance = weapon.Range;
            float speed = weapon.ProjectileSpeed;

            for (int i = 0; i < count; i++)
            {
                float angle = firstAngle + i * stepRad;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                // 击退强度按本次命中的伤害换算：大伤害推得远（换算在 WeaponDamage.KnockbackSpeed）
                DamageInfo damage = WeaponDamage.Compose(amount, _shooter, direction, isCrit,
                    WeaponDamage.KnockbackSpeed(amount));
                if (!_projectiles.Spawn(shooterPosition, direction, speed, maxDistance, damage, weapon.Pierce))
                {
                    // 池被打满：这一枪剩下的弹道就没了，冷却照常起算，不因为丢了弹道而卡住下一枪
                    break;
                }
            }

            weapon.Fire(stats);
        }

        /// <summary>
        /// 射程内最近的活目标。用 QueryCircle 而不是 QueryNearest：
        /// 快照里可能留着已移除/已死的槽位，QueryNearest 选中死人就整轮不开火，
        /// 而多扫一圈挑出"活着且最近"的那一个只多花几次比较。
        /// </summary>
        int FindNearestAlive(Vector2 origin, float range)
        {
            _candidates.Clear();
            _enemies.Grid.QueryCircle(origin, range, _candidates);

            int best = -1;
            float bestSqr = range * range;
            for (int i = 0; i < _candidates.Count; i++)
            {
                int slot = _candidates[i];
                EnemyModel enemy = _enemies.Get(slot);
                if (enemy == null || !enemy.IsAlive || enemy.IsDead) continue;

                // 精确距离用当前模型位置，不用快照位置
                float sqr = (enemy.Position - origin).sqrMagnitude;
                if (sqr > bestSqr) continue;

                bestSqr = sqr;
                best = slot;
            }
            return best;
        }
    }
}
