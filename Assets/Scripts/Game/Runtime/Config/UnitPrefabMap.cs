using System;
using UnityEngine;

namespace Game.Config
{
    /// <summary>
    /// Id 到预制体的显式映射。不用 Resources.Load 按名字查：改名不会报错，
    /// 静态分析也查不出谁在用哪个资源。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Unit Prefab Map", fileName = "UnitPrefabMap")]
    public sealed class UnitPrefabMap : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Id;
            public GameObject Prefab;
        }

        [SerializeField] Entry[] _entries = Array.Empty<Entry>();

        public GameObject Find(string id)
        {
            if (string.IsNullOrEmpty(id) || _entries == null) return null;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Id == id) return _entries[i].Prefab;
            }

            return null;
        }

        public bool Has(string id) => Find(id) != null;

        public int Count => _entries?.Length ?? 0;
        public Entry[] Entries => _entries;
    }
}
