using System;

namespace Game.Config
{
    // 所有 *Def 的字段名必须和 Excel 表头完全一致，导表脚本不会做名字映射。

    public sealed class ConfigManifest
    {
        public string version;
        public string exportedAt;
        public string[] tables;
    }

    public sealed class CharacterDef
    {
        public string Id;
        public string Name;
        public string Description;
        public float BaseHP;
        public float BaseMoveSpeed;
        public float BaseAttackInterval;
        public float BaseDamage;
        public float BaseRange;
        public string StartingWeaponId;
        public string TraitType;
    }

    public sealed class WeaponDef
    {
        public string Id;
        public string Name;
        public WeaponKind Kind;
        public float BaseDamage;
        public float Cooldown;
        public float Range;
        public int Projectiles;
        public float SpreadDeg;
        public string BehaviourType;
        public float ProjectileSpeed;
        public int Pierce;
        public float[] Params;

        public float GetParam(int index, float fallback)
        {
            return Params != null && index >= 0 && index < Params.Length ? Params[index] : fallback;
        }
    }

    public sealed class CompanionDef
    {
        public string Id;
        public string Name;
        public CompanionFaction Faction;
        public string WeaponId;
        public float FollowRadius;
        public float MaxHP;
    }

    public sealed class ItemDef
    {
        public string Id;
        public string Name;
        public string Description;
        public ItemRarity Rarity;
        public int BasePrice;
        public ItemCategory Category;
        public StatType StatA;
        public float ValueA;
        public StatType StatB;
        public float ValueB;
        public int MaxStack;
        public string EffectType;
        public float[] EffectParams;

        public bool HasStatA => StatA != StatType.Unknown;
        public bool HasStatB => StatB != StatType.Unknown;

        public float GetEffectParam(int index, float fallback)
        {
            return EffectParams != null && index >= 0 && index < EffectParams.Length
                ? EffectParams[index]
                : fallback;
        }
    }

    public sealed class EnemyDef
    {
        public string Id;
        public string Name;
        public EnemyBehaviourType Behaviour;
        public float HP;
        public float Damage;
        public float Speed;
        public float AttackRange;
        public float AttackInterval;
        public int CoinDrop;
        public float BodyRadius;
    }

    public sealed class WaveDef
    {
        public string Id;
        public int Index;
        public float Duration;
        public float SpawnInterval;
        public int SpawnCountMin;
        public int SpawnCountMax;
        public string[] EnemyPool;
        public int ConcurrentCap;
        public int CoinReward;
        public string BossId;

        public bool HasBoss => !string.IsNullOrEmpty(BossId);
    }

    public sealed class BuffDef
    {
        public string Id;
        public string Name;
        public BuffEffectType Effect;
        public float Duration;
        public int MaxStacks;
        public float Interval;
        public string Tags;
        public float[] Params;

        public float GetParam(int index, float fallback)
        {
            return Params != null && index >= 0 && index < Params.Length ? Params[index] : fallback;
        }
    }

    public sealed class BalanceDef
    {
        public string Id;
        public float Value;
        public string Note;
    }
}
