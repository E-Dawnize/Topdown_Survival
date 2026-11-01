using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 武器 Id → 视觉 prefab / 动作配置的唯一入口。
    ///
    /// 运行时不按 WeaponDef.Id 拼几何体，也不在代码里维护 Id 的 switch：
    /// 加一把武器 = 在 catalog 里加一条 + 放一个 prefab。战斗层仍然只认 WeaponModel，
    /// 表现层只读 Id、ShotsFired 与 AimDirection，换模型不会碰伤害/冷却/索敌。
    ///
    /// 资产在 Assets/Resources/WeaponVisualCatalog.asset，由
    /// Tools/Weapons/Create Missing Visual Prefabs 生成/补齐；缺条目会在 EditMode 测试里直接失败。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Weapon Visual Catalog", fileName = "WeaponVisualCatalog")]
    public sealed class WeaponVisualCatalog : ScriptableObject
    {
        /// <summary>Resources 下的固定路径；PlayerView 与 CompanionView 都从这里取。</summary>
        public const string ResourcePath = "WeaponVisualCatalog";

        [Serializable]
        public sealed class Entry
        {
            public string WeaponId;
            public GameObject Prefab;
            public WeaponAttackStyle AttackStyle = WeaponAttackStyle.Slash;
            public float AttackDuration = 0.30f;
        }

        [SerializeField] Entry[] _entries = Array.Empty<Entry>();

        public IReadOnlyList<Entry> Entries => _entries;

        public Entry Find(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId) || _entries == null) return null;

            for (int i = 0; i < _entries.Length; i++)
            {
                Entry entry = _entries[i];
                if (entry != null && entry.WeaponId == weaponId) return entry;
            }

            return null;
        }

        /// <summary>没有 catalog 时返回 null，由调用方给出明确错误，而不是静默生成白模。</summary>
        public static WeaponVisualCatalog LoadDefault()
        {
            return Resources.Load<WeaponVisualCatalog>(ResourcePath);
        }
    }
}
