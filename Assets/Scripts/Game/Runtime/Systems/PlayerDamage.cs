using Game.Config;
using Game.Core.Events;
using Game.Model;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 敌人对玩家的伤害入口。近战挥击、冲锋撞击、远程弹丸都走这一条路径，
    /// 但 HP 写回、护甲/闪避/无敌帧和 PlayerDamaged 事件由统一的 DamageService 完成。
    ///
    /// 随机骰子由调用方传进来：DamageResolver 是纯函数，随机流只归模拟层管。
    /// rng 为空时传 roll=1 关闭闪避，而不是拿玩家 Dodge 和 0 比较变成必闪。
    /// </summary>
    internal static class PlayerDamage
    {
        public static void Strike(PlayerModel player, float amount, UnitId source, Vector2 direction,
            IRandom01 dodgeRng, IEventBus events)
        {
            if (player == null || amount <= 0f || player.IsDead) return;

            // 没有随机流时用 1.0 当骰子：StatBlock 把 Dodge 夹在 [0,1]，
            // 因此 roll=1 等价于关闭闪避，而不是拿玩家 Dodge 和 0 比较变成必闪。
            float dodgeRoll = dodgeRng != null ? dodgeRng.Next01() : 1f;
            var damage = new DamageInfo(amount, DamageType.Physical, source, direction,
                0f, false, DamageFlags.Dodgeable);

            // HP 写回与 PlayerDamaged 事件都由统一入口完成；这里只负责构造敌人伤害。
            DamageService.Apply(player, damage, dodgeRoll, events);
        }
    }
}
