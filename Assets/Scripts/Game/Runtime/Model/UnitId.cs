using System;

namespace Game.Model
{
    /// <summary>单位种类。归属判断、击杀统计、视图解析都先看它，不靠 ID 数值区间猜。</summary>
    public enum UnitKind
    {
        Unknown,
        Player,
        Enemy,
        Companion
    }

    /// <summary>
    /// 一局内的稳定单位句柄。Value 不复用，Kind 是归属信息，不参与相等判断。
    /// 这是模拟层身份类型，不属于事件系统。
    /// </summary>
    public readonly struct UnitId : IEquatable<UnitId>
    {
        public static readonly UnitId None = new UnitId(0, UnitKind.Unknown);

        /// <summary>玩家固定使用 1 号；敌人与旅伴由本局分配器从 2 开始分配。</summary>
        public static readonly UnitId Player = new UnitId(1, UnitKind.Player);

        public readonly int Value;
        public readonly UnitKind Kind;

        public UnitId(int value, UnitKind kind = UnitKind.Unknown)
        {
            Value = value;
            Kind = kind;
        }

        public bool IsValid => Value != 0;
        public bool Equals(UnitId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is UnitId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Kind + "#" + Value;
        public static bool operator ==(UnitId a, UnitId b) => a.Value == b.Value;
        public static bool operator !=(UnitId a, UnitId b) => a.Value != b.Value;
    }

    /// <summary>一局内单调分配实体 ID；死亡与槽位复用都不回收。</summary>
    public sealed class UnitIdAllocator
    {
        int _next;

        public UnitIdAllocator(int firstValue = 1)
        {
            _next = firstValue < 1 ? 1 : firstValue;
        }

        public int MaxAllocated => _next - 1;

        public UnitId Allocate(UnitKind kind)
        {
            return new UnitId(_next++, kind);
        }
    }
}
