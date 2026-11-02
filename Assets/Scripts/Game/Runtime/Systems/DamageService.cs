using Game.Core.Events;
using Game.Model;

namespace Game.Systems
{
    /// <summary>
    /// 统一伤害入口。玩家、敌人、旅伴都通过这里结算，避免三条写回/事件路径各自漏掉
    /// 击杀归属、受击事件或死亡判定。
    ///
    /// 兼容策略：玩家仍发 PlayerDamaged（HUD/BuffSystem 现有订阅不变），敌人和旅伴发 UnitDamaged。
    /// 等表现层准备好后，再把 PlayerDamaged 收敛成 UnitDamaged 的一个兼容别名。
    /// </summary>
    public static class DamageService
    {
        public static DamageResult Apply(IDamageable target, in DamageInfo damage, float dodgeRoll,
            IEventBus events = null, bool fromEffect = false)
        {
            if (target == null || !target.IsAlive || damage.Amount <= 0f)
            {
                return new DamageResult(0f, false, false, false, target != null ? target.Hp : 0f);
            }

            DamageResult result = target.ApplyDamage(damage, dodgeRoll);
            if (result.Applied <= 0f) return result;

            if (target.Kind == UnitKind.Player)
            {
                events?.Publish(new PlayerDamaged(result.Applied, result.RemainingHp, target.MaxHp));
            }
            else
            {
                events?.Publish(new UnitDamaged(target.Id, damage.Source, result.Applied, damage.IsCrit,
                    result.RemainingHp, fromEffect, target.Position.x, target.Position.y));
            }

            return result;
        }
    }
}
