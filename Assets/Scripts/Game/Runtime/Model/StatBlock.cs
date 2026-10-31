using System;
using System.Collections.Generic;
using Game.Config;
using UnityEngine;

namespace Game.Model
{
    /// <summary>三乘区的运算方式。Multiply 的 Value 是倍数（1.2 表示 +20%），不是增量。</summary>
    public enum ModOp
    {
        Flat,
        Percent,
        Multiply
    }

    /// <summary>一条属性修正。只读结构，多个道具可以安全共用同一份数据。</summary>
    public readonly struct StatModifier
    {
        public readonly StatType Stat;
        public readonly float Value;
        public readonly ModOp Op;

        /// <summary>修正的来源（道具实例、buff 实例）。RemoveBySource 按引用相等批量移除。</summary>
        public readonly object Source;

        public StatModifier(StatType stat, float value, ModOp op, object source)
        {
            Stat = stat;
            Op = op;
            Value = value;
            Source = source;
        }
    }

    /// <summary>
    /// 单位属性。所有加成必须走这里，禁止直接改 HP 之类的最终值。
    /// 最终值 = (base + Σflat) × (1 + Σpercent) × Πmultiply。
    /// 读取走缓存：Add/Remove 只标脏，下次读取时整条重算，稳态读不产生 GC。
    /// </summary>
    public sealed class StatBlock
    {
        static readonly int StatCount = Enum.GetValues(typeof(StatType)).Length;

        readonly float[] _baseValue = new float[StatCount];
        readonly float[] _cached = new float[StatCount];

        // 与 _cached 一一对应；构造时全为 false，所以首次读取一定走重算拿到 base
        readonly bool[] _upToDate = new bool[StatCount];

        // 惰性分配：绝大多数单位的大多数属性一条修正都没有，预先开满 List 是纯浪费
        readonly List<StatModifier>[] _modifiers = new List<StatModifier>[StatCount];

        public float Base(StatType stat) => _baseValue[(int)stat];

        public void SetBase(StatType stat, float value)
        {
            int index = (int)stat;
            if (_baseValue[index] == value) return;
            _baseValue[index] = value;
            _upToDate[index] = false;
        }

        public float this[StatType stat]
        {
            get
            {
                int index = (int)stat;
                if (!_upToDate[index])
                {
                    _cached[index] = Evaluate(stat);
                    _upToDate[index] = true;
                }
                return _cached[index];
            }
        }

        public void Add(in StatModifier modifier)
        {
            int index = (int)modifier.Stat;
            List<StatModifier> list = _modifiers[index];
            if (list == null)
            {
                // 初始容量 4 覆盖"一个道具 + 一个 buff + 几条天赋"的量级，够用不扩容
                list = new List<StatModifier>(4);
                _modifiers[index] = list;
            }
            list.Add(modifier);
            _upToDate[index] = false;
        }

        /// <summary>按 Source 的引用相等批量移除，同一来源挂在多个属性上的修正一起走。</summary>
        public void RemoveBySource(object source)
        {
            for (int i = 0; i < StatCount; i++)
            {
                List<StatModifier> list = _modifiers[i];
                if (list == null || list.Count == 0) continue;

                bool removed = false;
                for (int j = list.Count - 1; j >= 0; j--)   // 倒序删，避免 RemoveAt 后下标错位
                {
                    if (!ReferenceEquals(list[j].Source, source)) continue;
                    list.RemoveAt(j);
                    removed = true;
                }
                if (removed) _upToDate[i] = false;
            }
        }

        float Evaluate(StatType stat)
        {
            int index = (int)stat;
            List<StatModifier> list = _modifiers[index];

            // 没有修正时直接返回 base，保证结果与 base 逐位相等（乘 1 虽然也精确，但没必要绕一圈）
            if (list == null || list.Count == 0) return ClampIfProbability(stat, _baseValue[index]);

            float flat = 0f;
            float percent = 0f;
            float multiply = 1f;
            for (int i = 0; i < list.Count; i++)
            {
                StatModifier m = list[i];
                switch (m.Op)
                {
                    case ModOp.Flat:
                        flat += m.Value;
                        break;
                    case ModOp.Percent:
                        percent += m.Value;
                        break;
                    case ModOp.Multiply:
                        multiply *= m.Value;
                        break;
                }
            }

            float value = (_baseValue[index] + flat) * (1f + percent) * multiply;
            return ClampIfProbability(stat, value);
        }

        /// <summary>概率类属性封顶 1，否则 120% 闪避和 90% 闪避在实现上等价、在数值上不一致。</summary>
        static float ClampIfProbability(StatType stat, float value)
        {
            return stat == StatType.Dodge || stat == StatType.CritChance ? Mathf.Clamp01(value) : value;
        }
    }
}
