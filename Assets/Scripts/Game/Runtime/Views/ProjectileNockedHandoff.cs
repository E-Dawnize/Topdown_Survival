using System.Collections.Generic;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 弓把“搭箭”交接给投射物池的临时槽。开火时 WeaponVisual 把 NockedArrow 的 GameObject
    /// 摘下来放这里；ProjectileViewPool 为新出现的投射物取走它，于是飞出去的就是弓上那一支。
    /// 只搬运表现对象，不参与命中/伤害。
    /// </summary>
    public static class ProjectileNockedHandoff
    {
        readonly struct Entry
        {
            public readonly GameObject Arrow;
            public readonly float Time;

            public Entry(GameObject arrow, float time)
            {
                Arrow = arrow;
                Time = time;
            }
        }

        static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>(5);

        public static void Set(int ownerKey, GameObject arrow)
        {
            if (arrow == null) return;

            if (Entries.TryGetValue(ownerKey, out Entry old) && old.Arrow != null && old.Arrow != arrow)
                Object.Destroy(old.Arrow);

            Entries[ownerKey] = new Entry(arrow, Time.time);
        }

        /// <summary>取走待交接的搭箭；超过 maxAge 说明这一发没有对应投射物，直接销毁。</summary>
        public static GameObject Take(int ownerKey, float maxAge)
        {
            if (!Entries.TryGetValue(ownerKey, out Entry entry)) return null;
            Entries.Remove(ownerKey);

            if (entry.Arrow == null) return null;
            if (Time.time - entry.Time > maxAge)
            {
                Object.Destroy(entry.Arrow);
                return null;
            }

            return entry.Arrow;
        }

        public static void Clear()
        {
            foreach (KeyValuePair<int, Entry> pair in Entries)
            {
                if (pair.Value.Arrow != null) Object.Destroy(pair.Value.Arrow);
            }

            Entries.Clear();
        }
    }
}
