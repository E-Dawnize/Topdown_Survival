using System;

namespace Game.Core.Events
{
    /// <summary>单位句柄。0 表示无效。</summary>
    public readonly struct UnitId : IEquatable<UnitId>
    {
        public static readonly UnitId None = new UnitId(0);

        public readonly int Value;

        public UnitId(int value)
        {
            Value = value;
        }

        public bool IsValid => Value != 0;
        public bool Equals(UnitId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is UnitId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => "#" + Value;
        public static bool operator ==(UnitId a, UnitId b) => a.Value == b.Value;
        public static bool operator !=(UnitId a, UnitId b) => a.Value != b.Value;
    }

    // ---- 战斗 ----

    public readonly struct UnitDamaged
    {
        public readonly UnitId Target;
        public readonly UnitId Source;
        public readonly float Amount;
        public readonly bool IsCrit;
        public readonly float RemainingHp;

        public UnitDamaged(UnitId target, UnitId source, float amount, bool isCrit, float remainingHp)
        {
            Target = target;
            Source = source;
            Amount = amount;
            IsCrit = isCrit;
            RemainingHp = remainingHp;
        }
    }

    public readonly struct UnitDied
    {
        public readonly UnitId Unit;
        public readonly UnitId Killer;
        public readonly float X;
        public readonly float Z;

        public UnitDied(UnitId unit, UnitId killer, float x, float z)
        {
            Unit = unit;
            Killer = killer;
            X = x;
            Z = z;
        }
    }

    public readonly struct WeaponFired
    {
        public readonly UnitId Owner;
        public readonly string WeaponId;
        public readonly int ProjectileCount;

        public WeaponFired(UnitId owner, string weaponId, int projectileCount)
        {
            Owner = owner;
            WeaponId = weaponId;
            ProjectileCount = projectileCount;
        }
    }

    // ---- 经济 ----

    public readonly struct CoinChanged
    {
        public readonly int Current;
        public readonly int Delta;

        public CoinChanged(int current, int delta)
        {
            Current = current;
            Delta = delta;
        }
    }

    public readonly struct CoinPicked
    {
        public readonly int Value;
        public readonly float X;
        public readonly float Z;

        public CoinPicked(int value, float x, float z)
        {
            Value = value;
            X = x;
            Z = z;
        }
    }

    // ---- 流程 ----

    public readonly struct RunStarted
    {
        public readonly ulong Seed;
        public readonly string CharacterId;

        public RunStarted(ulong seed, string characterId)
        {
            Seed = seed;
            CharacterId = characterId;
        }
    }

    public readonly struct WaveStarted
    {
        public readonly int Index;
        public readonly float Duration;

        public WaveStarted(int index, float duration)
        {
            Index = index;
            Duration = duration;
        }
    }

    public readonly struct WaveCleared
    {
        public readonly int Index;
        public readonly int CoinReward;

        public WaveCleared(int index, int coinReward)
        {
            Index = index;
            CoinReward = coinReward;
        }
    }

    public readonly struct ShopOpened
    {
        public readonly int WaveIndex;

        public ShopOpened(int waveIndex)
        {
            WaveIndex = waveIndex;
        }
    }

    public readonly struct ShopClosed
    {
        public readonly int CoinsSpent;

        public ShopClosed(int coinsSpent)
        {
            CoinsSpent = coinsSpent;
        }
    }

    public readonly struct PlayerDashed
    {
        public readonly float X;
        public readonly float Z;
        public readonly float Angle;

        public PlayerDashed(float x, float z, float angle)
        {
            X = x;
            Z = z;
            Angle = angle;
        }
    }

    public readonly struct PlayerDied
    {
        public readonly float X;
        public readonly float Z;

        public PlayerDied(float x, float z)
        {
            X = x;
            Z = z;
        }
    }

    public readonly struct RunSummary
    {
        public readonly int WavesCleared;
        public readonly int Kills;
        public readonly int Coins;
        public readonly float Duration;
        public readonly ulong Seed;

        public RunSummary(int wavesCleared, int kills, int coins, float duration, ulong seed)
        {
            WavesCleared = wavesCleared;
            Kills = kills;
            Coins = coins;
            Duration = duration;
            Seed = seed;
        }
    }

    public readonly struct RunEnded
    {
        public readonly RunSummary Summary;
        public readonly bool Victory;

        public RunEnded(in RunSummary summary, bool victory)
        {
            Summary = summary;
            Victory = victory;
        }
    }
}
