using Game.Config;
using Game.Controllers;
using Game.Core;
using Game.Core.Events;
using Game.Model;
using Game.Systems;
using Game.Systems.Weapons;

namespace Game.Tests
{
    /// <summary>
    /// 流程测试夹具。对象图与 AppRoot 共用 RunComposition，这里只负责推进固定步与自动选卡。
    /// </summary>
    public sealed class TestRun
    {
        public const float Dt = 1f / 60f;
        public const float ArenaHalf = 67f;
        public const ulong Seed = 0xA1B2C3D4E5F60718ul;

        public readonly RunComposition Composition;
        public readonly EventBus Bus;
        public readonly PlayerModel Player;
        public readonly RunState State;
        public readonly EnemySystem Enemies;
        public readonly WeaponSystem Weapons;
        public readonly CompanionSystem Companions;
        public readonly BuffSystem Buffs;
        public readonly DirectorSystem Director;
        public readonly BuildController Build;
        public readonly PickupSystem Pickups;
        public readonly RunController Controller;
        public readonly RunSimulation Simulation;
        public readonly ConfigDatabase Config;

        TestRun(float hp)
        {
            Config = TestContent.Config();
            Composition = RunComposition.Create(Config, "Scavenger", Seed, playerMaxHp: hp);

            Bus = Composition.Events;
            Player = Composition.Player;
            State = Composition.Run;
            Enemies = Composition.Enemies;
            Weapons = Composition.Weapons;
            Companions = Composition.Companions;
            Buffs = Composition.Buffs;
            Director = Composition.Director;
            Build = Composition.Build;
            Pickups = Composition.Pickups;
            Controller = Composition.RunController;
            Simulation = Composition.Simulation;
        }

        public static TestRun Create(float hp = 1000000f)
        {
            var run = new TestRun(hp);
            run.Controller.StartRun();
            run.Bus.Flush();
            return run;
        }

        public void Step(int steps = 1)
        {
            var input = new InputSnapshot();
            for (int i = 0; i < steps; i++)
            {
                Simulation.Tick(Dt, ref input);
                if (Controller.Phase == RunPhase.LevelUp)
                {
                    Controller.ChooseUpgrade(0);
                    Bus.Flush();
                }
            }
        }

        public void AdvanceSeconds(float seconds)
        {
            Step((int)(seconds / Dt + 0.5f));
        }
    }
}
