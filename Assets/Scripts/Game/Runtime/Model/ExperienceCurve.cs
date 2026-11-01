using System;
using System.Collections.Generic;
using Game.Config;

namespace Game.Model
{
    /// <summary>
    /// 经验曲线。配置是"从当前等级升到下一级要多少经验"，不是累计值：
    /// 这样改一级的节奏不会连带把后面所有等级的门槛都推一遍。
    ///
    /// 只读、不掷骰、不碰 UnityEngine，升级门槛与发卡数量都能在 EditMode 里直接验。
    /// </summary>
    public sealed class ExperienceCurve
    {
        readonly LevelDef[] _levels;

        public ExperienceCurve(IReadOnlyList<LevelDef> levels)
        {
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            if (levels.Count == 0) throw new ArgumentException("等级表是空的", nameof(levels));

            _levels = new LevelDef[levels.Count];
            for (int i = 0; i < levels.Count; i++) _levels[i] = levels[i];
        }

        /// <summary>等级上限 = 等级表行数。到顶之后经验不再累积。</summary>
        public int MaxLevel => _levels.Length;

        public int XpToNext(int level)
        {
            int index = ClampIndex(level);
            return Math.Max(1, _levels[index].XpToNext);
        }

        public int OfferCount(int level)
        {
            int index = ClampIndex(level);
            return Math.Max(1, _levels[index].OfferCount);
        }

        /// <summary>稀有度上浮 0..1。与幸运属性相加后由 BuildController 转成权重。</summary>
        public float RarityBonus(int level)
        {
            int index = ClampIndex(level);
            float value = _levels[index].RarityBonus;
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        int ClampIndex(int level)
        {
            if (level < 1) return 0;
            if (level > _levels.Length) return _levels.Length - 1;
            return level - 1;
        }
    }
}
