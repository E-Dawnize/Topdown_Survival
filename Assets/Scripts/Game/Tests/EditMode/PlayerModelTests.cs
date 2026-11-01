using Game.Config;
using Game.Model;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 玩家属性走 StatBlock（I1-3 接入）。构造参数只是写进 StatBlock 的基线，
    /// MaxHP / MoveSpeed 必须从 StatBlock 读，否则道具与 buff 加了修正读不到。
    /// </summary>
    public class PlayerModelTests
    {
        static PlayerModel MakePlayer()
        {
            return new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);
        }

        [Test]
        public void ConstructorWritesBaseHpAndMoveSpeedIntoStatBlock()
        {
            PlayerModel player = MakePlayer();

            Assert.AreEqual(100f, player.Stats.Base(StatType.MaxHP), 0f);
            Assert.AreEqual(5f, player.Stats.Base(StatType.MoveSpeed), 0f);
            Assert.AreEqual(100f, player.MaxHP, 0f);
            Assert.AreEqual(5f, player.MoveSpeed, 0f);
        }

        [Test]
        public void ModifierOnStatBlockChangesMaxHpAndMoveSpeed()
        {
            PlayerModel player = MakePlayer();
            var item = new object();

            player.Stats.Add(new StatModifier(StatType.MaxHP, 0.2f, ModOp.Percent, item));
            player.Stats.Add(new StatModifier(StatType.MoveSpeed, 1f, ModOp.Flat, item));

            Assert.AreEqual(120f, player.MaxHP, 1e-4f, "MaxHP 必须从 StatBlock 读，不能被构造时的数字钉死");
            Assert.AreEqual(6f, player.MoveSpeed, 1e-4f);

            player.Stats.RemoveBySource(item);
            Assert.AreEqual(100f, player.MaxHP, 1e-4f);
            Assert.AreEqual(5f, player.MoveSpeed, 1e-4f);
        }

        [Test]
        public void CombatDefaultsMatchGdd()
        {
            PlayerModel player = MakePlayer();

            Assert.AreEqual(0f, player.Stats[StatType.Attack], 0f, "攻击加成默认 0");
            Assert.AreEqual(0.05f, player.Stats[StatType.CritChance], 1e-6f, "暴击率默认 5%");
            Assert.AreEqual(1.5f, player.Stats[StatType.CritDamage], 1e-6f, "暴击伤害默认 150%");
            Assert.AreEqual(0f, player.Stats[StatType.AttackSpeed], 0f, "攻速加成默认 0");
            Assert.AreEqual(2.5f, player.Stats[StatType.PickupRadius], 1e-6f, "拾取范围默认 2.5 米（GDD 5.1），磁吸读它");
        }

        [Test]
        public void DashFieldsStayOutsideStatBlock()
        {
            // 冲刺的数字还是构造时定死的普通字段：I1-2 的机制不经过属性系统
            PlayerModel player = MakePlayer();

            Assert.AreEqual(15f, player.DashSpeed, 0f);
            Assert.AreEqual(0.15f, player.DashDuration, 0f);
            Assert.AreEqual(2f, player.DashCooldown, 0f);
            Assert.AreEqual(67f, player.ArenaHalfExtent, 0f);
        }
    }
}
