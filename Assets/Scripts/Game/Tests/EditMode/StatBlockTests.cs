using Game.Config;
using Game.Model;
using NUnit.Framework;

namespace Game.Tests
{
    public class StatBlockTests
    {
        // 来源用普通 object：真实代码里是道具实例 / buff 实例，测试里只要引用不同就是不同来源
        static object NewSource() => new object();

        static StatBlock MakeBlock()
        {
            var stats = new StatBlock();
            stats.SetBase(StatType.MaxHP, 100f);
            stats.SetBase(StatType.MoveSpeed, 5f);
            return stats;
        }

        [Test]
        public void WithoutModifiersValueEqualsBase()
        {
            StatBlock stats = MakeBlock();

            Assert.AreEqual(100f, stats[StatType.MaxHP], 0f);
            Assert.AreEqual(5f, stats[StatType.MoveSpeed], 0f);
            Assert.AreEqual(0f, stats[StatType.Dodge], 0f, "没设过 base 的属性应当就是 0");
        }

        [Test]
        public void ThreeZonesCombineAsHandCalculated()
        {
            StatBlock stats = MakeBlock();
            object sword = NewSource();
            object ring = NewSource();

            stats.Add(new StatModifier(StatType.MaxHP, 10f, ModOp.Flat, sword));
            stats.Add(new StatModifier(StatType.MaxHP, 10f, ModOp.Flat, ring));
            stats.Add(new StatModifier(StatType.MaxHP, 0.15f, ModOp.Percent, sword));
            stats.Add(new StatModifier(StatType.MaxHP, 0.10f, ModOp.Percent, ring));
            stats.Add(new StatModifier(StatType.MaxHP, 1.2f, ModOp.Multiply, sword));
            stats.Add(new StatModifier(StatType.MaxHP, 0.5f, ModOp.Multiply, ring));

            // (100 + 10 + 10) × (1 + 0.15 + 0.10) × 1.2 × 0.5
            // = 120 × 1.25 × 1.2 × 0.5 = 90
            Assert.AreEqual(90f, stats[StatType.MaxHP], 1e-4f);
        }

        [Test]
        public void MultiplyIsMultiplierNotIncrement()
        {
            StatBlock stats = MakeBlock();
            stats.Add(new StatModifier(StatType.MaxHP, 1.2f, ModOp.Multiply, NewSource()));

            // 100 × 1.2 = 120；若误按增量处理会得到 100 + 1.2 = 101.2
            Assert.AreEqual(120f, stats[StatType.MaxHP], 1e-4f);
        }

        [Test]
        public void RemoveBySourceDropsAllModifiersOfThatSource()
        {
            StatBlock stats = MakeBlock();
            object potion = NewSource();

            // 同一来源在三个乘区各挂一条，移除时必须三条一起走，不能只撤掉最后一条
            stats.Add(new StatModifier(StatType.MaxHP, 20f, ModOp.Flat, potion));
            stats.Add(new StatModifier(StatType.MaxHP, 0.5f, ModOp.Percent, potion));
            stats.Add(new StatModifier(StatType.MaxHP, 2f, ModOp.Multiply, potion));

            // (100 + 20) × 1.5 × 2 = 360
            Assert.AreEqual(360f, stats[StatType.MaxHP], 1e-4f);

            stats.RemoveBySource(potion);
            Assert.AreEqual(100f, stats[StatType.MaxHP], 0f, "移除来源后必须回到基线");
        }

        [Test]
        public void RemoveBySourceKeepsOtherSources()
        {
            StatBlock stats = MakeBlock();
            object sword = NewSource();
            object ring = NewSource();

            stats.Add(new StatModifier(StatType.MaxHP, 10f, ModOp.Flat, sword));
            stats.Add(new StatModifier(StatType.MaxHP, 0.10f, ModOp.Percent, sword));
            stats.Add(new StatModifier(StatType.MaxHP, 10f, ModOp.Flat, ring));
            stats.Add(new StatModifier(StatType.MaxHP, 0.10f, ModOp.Percent, ring));

            stats.RemoveBySource(sword);

            // 剩下 ring：(100 + 10) × 1.1 = 121
            Assert.AreEqual(121f, stats[StatType.MaxHP], 1e-4f);

            stats.RemoveBySource(ring);
            Assert.AreEqual(100f, stats[StatType.MaxHP], 0f);
        }

        [Test]
        public void RemoveBySourceIgnoresEqualButDifferentObjects()
        {
            StatBlock stats = MakeBlock();
            object first = NewSource();
            object second = NewSource();

            stats.Add(new StatModifier(StatType.MaxHP, 10f, ModOp.Flat, first));
            stats.Add(new StatModifier(StatType.MaxHP, 10f, ModOp.Flat, second));

            stats.RemoveBySource(first);

            // 按引用相等而不是相等语义：另一个来源的 +10 得留着
            Assert.AreEqual(110f, stats[StatType.MaxHP], 0f);
        }

        [Test]
        public void ProbabilityStatsClampToOne()
        {
            StatBlock stats = MakeBlock();
            stats.SetBase(StatType.Dodge, 0.4f);
            stats.SetBase(StatType.CritChance, 0.05f);

            object a = NewSource();
            object b = NewSource();
            stats.Add(new StatModifier(StatType.Dodge, 0.8f, ModOp.Flat, a));
            stats.Add(new StatModifier(StatType.CritChance, 1.5f, ModOp.Flat, b));

            Assert.AreEqual(1f, stats[StatType.Dodge], 0f, "0.4 + 0.8 = 1.2，要封顶到 1");
            Assert.AreEqual(1f, stats[StatType.CritChance], 0f);
        }

        [Test]
        public void ProbabilityStatsClampToZero()
        {
            StatBlock stats = MakeBlock();
            stats.SetBase(StatType.Dodge, 0.1f);
            stats.Add(new StatModifier(StatType.Dodge, -0.5f, ModOp.Flat, NewSource()));

            Assert.AreEqual(0f, stats[StatType.Dodge], 0f, "0.1 - 0.5 = -0.4，要夹到 0");
        }

        [Test]
        public void NonProbabilityStatsAreNotClamped()
        {
            StatBlock stats = MakeBlock();
            stats.Add(new StatModifier(StatType.MaxHP, 2f, ModOp.Percent, NewSource()));

            // 300 不该被 clamp；只有 Dodge / CritChance 这类概率属性才夹 [0,1]
            Assert.AreEqual(300f, stats[StatType.MaxHP], 1e-4f);
        }

        [Test]
        public void CachedValueTracksLaterAddAndRemove()
        {
            StatBlock stats = MakeBlock();
            object potion = NewSource();

            Assert.AreEqual(100f, stats[StatType.MaxHP], 0f);   // 先读一次，把缓存填上

            stats.Add(new StatModifier(StatType.MaxHP, 25f, ModOp.Flat, potion));
            Assert.AreEqual(125f, stats[StatType.MaxHP], 0f, "Add 之后缓存必须失效");

            stats.RemoveBySource(potion);
            Assert.AreEqual(100f, stats[StatType.MaxHP], 0f, "Remove 之后缓存必须失效");
        }

        [Test]
        public void BaseChangeAffectsValue()
        {
            StatBlock stats = MakeBlock();
            stats.Add(new StatModifier(StatType.MaxHP, 0.5f, ModOp.Percent, NewSource()));
            Assert.AreEqual(150f, stats[StatType.MaxHP], 1e-4f);

            stats.SetBase(StatType.MaxHP, 200f);
            Assert.AreEqual(300f, stats[StatType.MaxHP], 1e-4f, "改 base 之后同一份修正要跟着重算");
        }
    }
}
