using System.Collections.Generic;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 武器表现层 → 投射物表现层的“枪口”桥。
    ///
    /// 模拟层不知道武器架槽位，箭头永远从射手中心生成；但表现层可以直接用弓把/闪光挂点的
    /// 世界坐标，让第一小段飞行从武器中间接出来。这里只存表现坐标，不参与命中/伤害。
    /// key 与 ProjectileViewSources.OwnerKey 一致：0 玩家，槽位 + 1 旅伴。
    /// </summary>
    public static class ProjectileMuzzleRegistry
    {
        readonly struct Entry
        {
            public readonly Vector3 Position;
            public readonly float Time;

            public Entry(Vector3 position, float time)
            {
                Position = position;
                Time = time;
            }
        }

        static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>(5);

        public static void Set(int ownerKey, Vector3 worldPosition)
        {
            Entries[ownerKey] = new Entry(worldPosition, Time.time);
        }

        /// <summary>读取最近一次开火的枪口；超过 maxAge 视为过期，避免旧枪口粘在新箭上。</summary>
        public static bool TryGet(int ownerKey, float maxAge, out Vector3 worldPosition)
        {
            if (Entries.TryGetValue(ownerKey, out Entry entry) && Time.time - entry.Time <= maxAge)
            {
                worldPosition = entry.Position;
                return true;
            }

            worldPosition = default;
            return false;
        }

        public static void Clear() => Entries.Clear();
    }
}
