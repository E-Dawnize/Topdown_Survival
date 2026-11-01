using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 敌人对玩家的伤害结算。近战挥击、冲锋撞击、远程弹丸都走这一条路径，
    /// 护甲/闪避/冲刺无敌帧与 PlayerDamaged 事件只在这里实现一次。
    ///
    /// 随机骰子由调用方传进来：DamageResolver 是纯函数，随机流只归模拟层管。
    /// rng 为空时把 Dodge 按 0 处理（与 EnemySystem 的旧行为一致），而不是拿玩家 Dodge
    /// 去和始终为 0 的骰子比较——那样有闪避属性就会变成必定闪避。
    /// </summary>
    internal static class PlayerDamage
    {
        public static void Strike(PlayerModel player, float amount, UnitId source, Vector2 direction,
            IRandom01 dodgeRng, IEventBus events)
        {
            if (player == null || amount <= 0f || player.IsDead) return;

            float dodge = 0f;
            float dodgeRoll = 0f;
            if (dodgeRng != null)
            {
                dodge = player.Stats[StatType.Dodge];
                dodgeRoll = dodgeRng.Next01();
            }

            var damage = new DamageInfo(amount, DamageType.Physical, source, direction,
                0f, false, DamageFlags.Dodgeable);
            var target = new DamageTarget(player.HP, player.Stats[StatType.Armor], dodge,
                player.IsInvulnerable);

            DamageResult result = DamageResolver.Resolve(damage, target, dodgeRoll);
            player.HP = result.RemainingHp;

            // 触发型 buff（受击叠甲、残血加攻）与 HUD 红闪都靠这条事件。
            // 只在实际扣到血时发：闪避与冲刺无敌帧不该触发“受击”。
            if (result.Applied > 0f)
            {
                events?.Publish(new PlayerDamaged(result.Applied, result.RemainingHp, player.MaxHP));
            }
        }
    }
}
