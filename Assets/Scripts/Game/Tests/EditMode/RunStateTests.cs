using Game.Model;
using NUnit.Framework;

namespace Game.Tests
{
    public class RunStateTests
    {
        [Test]
        public void StartsEmpty()
        {
            var run = new RunState();

            Assert.AreEqual(0, run.Coins);
            Assert.AreEqual(1, run.WaveIndex);
            Assert.AreEqual(5, run.WavesTotal);
        }

        [Test]
        public void AddAndSpendCoins()
        {
            var run = new RunState();
            run.AddCoins(30);

            Assert.IsTrue(run.TrySpend(20));
            Assert.AreEqual(10, run.Coins);
        }

        [Test]
        public void SpendMoreThanOwnedKeepsBalance()
        {
            var run = new RunState();
            run.AddCoins(10);

            Assert.IsFalse(run.TrySpend(999));
            Assert.AreEqual(10, run.Coins);
        }

        [Test]
        public void HpRatioAndDeath()
        {
            var player = new PlayerModel(100f, 5f, 15f, 0.15f, 2f, 67f);

            player.HP = 25f;
            Assert.AreEqual(0.25f, player.HpRatio, 0.001f);
            Assert.IsFalse(player.IsDead);

            player.HP = 0f;
            Assert.IsTrue(player.IsDead);
        }
    }
}
