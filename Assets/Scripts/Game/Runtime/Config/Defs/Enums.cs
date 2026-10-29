namespace Game.Config
{
    public enum DamageType
    {
        Physical,
        Fire,
        Curse,
        True
    }

    public enum WeaponKind
    {
        Ranged,
        Melee
    }

    public enum CompanionFaction
    {
        None,
        Machine,
        Ghost,
        Beast,
        Doll
    }

    public enum ItemRarity
    {
        Common,
        Rare,
        Epic,
        Legendary
    }

    public enum ItemCategory
    {
        Passive,
        Trigger,
        Economy,
        Special
    }

    public enum EnemyBehaviourType
    {
        Chase,
        Charge,
        Ranged,
        Split,
        Shield,
        EliteSummoner,
        EliteDischarger,
        Boss
    }

    public enum BuffEffectType
    {
        None,
        StatModifier,
        PeriodicDamage,
        OnHitTrigger,
        OnKillTrigger
    }

    public enum StatType
    {
        Unknown,
        MaxHP,
        Regen,
        Armor,
        Dodge,
        MoveSpeed,
        Attack,
        AttackSpeed,
        CritChance,
        CritDamage,
        ProjectileCount,
        Range,
        PickupRadius,
        Luck,
        Treasure,
        ContactDamage
    }
}
