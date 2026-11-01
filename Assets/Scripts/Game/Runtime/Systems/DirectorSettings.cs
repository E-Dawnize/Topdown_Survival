using System;
using System.Collections.Generic;
using Game.Config;

namespace Game.Systems
{
    /// <summary>
    /// 精英怪的 5 个倍率。DirectorSystem 的刷怪算法只依赖这份窄配置，
    /// 不再为了拿 5 个数而握住整个 ConfigDatabase。
    /// </summary>
    public readonly struct EliteScaling
    {
        public readonly float Hp;
        public readonly float Damage;
        public readonly float Speed;
        public readonly float Xp;
        public readonly float VisualScale;

        public EliteScaling(float hp, float damage, float speed, float xp, float visualScale)
        {
            Hp = hp;
            Damage = damage;
            Speed = speed;
            Xp = xp;
            VisualScale = visualScale;
        }

        /// <summary>与 Balance 表默认值一致；配置缺失或测试直接构造时使用。</summary>
        public static readonly EliteScaling Default = new EliteScaling(3.2f, 1.5f, 1.05f, 4f, 1.35f);

        public static EliteScaling FromConfig(ConfigDatabase config)
        {
            if (config == null) return Default;

            return new EliteScaling(
                config.GetBalance("EliteHpScale", Default.Hp),
                config.GetBalance("EliteDamageScale", Default.Damage),
                config.GetBalance("EliteSpeedScale", Default.Speed),
                config.GetBalance("EliteXpScale", Default.Xp),
                config.GetBalance("EliteScale", Default.VisualScale));
        }
    }

    /// <summary>
    /// 导演的窄配置：时间轴 + 精英倍率。真正的时间轴算法只读这个值对象；
    /// 需要从整库装配时走 FromConfig / DirectorSystem 的 ConfigDatabase 便利构造。
    /// </summary>
    public sealed class DirectorSettings
    {
        public readonly IReadOnlyList<DirectorDef> Directors;
        public readonly EliteScaling Elite;

        public DirectorSettings(IReadOnlyList<DirectorDef> directors, EliteScaling elite)
        {
            Directors = directors ?? throw new ArgumentNullException(nameof(directors));
            if (directors.Count == 0) throw new ArgumentException("导演时间轴是空的", nameof(directors));

            Elite = elite;
        }

        public static DirectorSettings FromConfig(IReadOnlyList<DirectorDef> directors, ConfigDatabase config)
        {
            return new DirectorSettings(directors, EliteScaling.FromConfig(config));
        }
    }
}
