using System.Collections.Generic;
using System.IO;
using Game.Config;
using Game.Views;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// catalog 是武器 Id → prefab 的唯一入口。这里直接加载真实 catalog 与真实 Weapon.json：
    /// 任何一把配置里的武器缺 prefab、动作重复、时长盖不住判定窗口，都在 EditMode 里失败，
    /// 而不是等到 Play 里发现某把武器不显示。
    /// </summary>
    public class WeaponVisualCatalogTests
    {
        static WeaponVisualCatalog Catalog()
        {
            WeaponVisualCatalog catalog = WeaponVisualCatalog.LoadDefault();
            Assert.IsNotNull(catalog,
                "缺少 Assets/Resources/WeaponVisualCatalog.asset；运行 Tools/Weapons/Create Missing Visual Prefabs");
            return catalog;
        }

        [Test]
        public void EveryConfiguredWeaponHasACatalogEntryWithAPrefab()
        {
            string root = FindConfigDirectory();
            if (root == null) Assert.Ignore("找不到导出后的配置目录");

            ConfigDatabase config = ConfigDatabase.Load(new DiskSource(root));
            WeaponVisualCatalog catalog = Catalog();

            Assert.AreEqual(config.Weapons.Count, catalog.Entries.Count,
                "catalog 条目数与 Weapon.json 不一致：加武器时忘了跑 prefab 生成");

            foreach (WeaponDef def in config.Weapons.Values)
            {
                WeaponVisualCatalog.Entry entry = catalog.Find(def.Id);
                Assert.IsNotNull(entry, def.Id + " 在 catalog 里没有条目");
                Assert.IsNotNull(entry.Prefab, def.Id + " 的 catalog 条目没有 prefab");
                Assert.Greater(entry.AttackDuration, 0f, def.Id + " 的动作时长必须为正");
            }
        }

        [Test]
        public void AttackStylesAreDistinctAndCoverEveryShape()
        {
            WeaponVisualCatalog catalog = Catalog();
            var styles = new HashSet<WeaponAttackStyle>();

            foreach (WeaponVisualCatalog.Entry entry in catalog.Entries)
            {
                Assert.IsNotNull(entry, "catalog 里有空条目");
                Assert.IsTrue(styles.Add(entry.AttackStyle),
                    entry.WeaponId + " 的动作类型和另一把武器重复：" + entry.AttackStyle);
            }

            Assert.AreEqual(6, styles.Count, "六把武器应有六种不同动作");
        }

        [Test]
        public void EveryPrefabCarriesWeaponVisualAndFlashChild()
        {
            WeaponVisualCatalog catalog = Catalog();

            foreach (WeaponVisualCatalog.Entry entry in catalog.Entries)
            {
                GameObject instance = Object.Instantiate(entry.Prefab);
                try
                {
                    WeaponVisual visual = instance.GetComponent<WeaponVisual>();
                    Assert.IsNotNull(visual, entry.WeaponId + " 的 prefab 上没有 WeaponVisual");
                    Assert.IsNotNull(instance.transform.Find("Flash"),
                        entry.WeaponId + " 的 prefab 上没有 Flash 子节点");
                    Assert.IsTrue(visual.HasMuzzle, entry.WeaponId + " 的 prefab 上没有 Muzzle 枪口");
                    Assert.Greater(instance.transform.childCount, 0,
                        entry.WeaponId + " 的 prefab 没有模型子节点");

                    if (entry.WeaponId == "Bow")
                    {
                        Assert.IsTrue(visual.HasNockedArrow, "弓箭 prefab 上必须挂搭箭");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void MeleeAnimationCoversTheConfiguredDamageWindow()
        {
            WeaponVisualCatalog catalog = Catalog();
            string root = FindConfigDirectory();
            if (root == null) Assert.Ignore("找不到导出后的配置目录");
            ConfigDatabase config = ConfigDatabase.Load(new DiskSource(root));

            foreach (WeaponDef def in config.Weapons.Values)
            {
                if (def.Kind != WeaponKind.Melee) continue;

                WeaponVisualCatalog.Entry entry = catalog.Find(def.Id);
                float windowEnd = def.GetParam(1, 0.22f);
                Assert.Greater(entry.AttackDuration, windowEnd,
                    def.Id + " 的挥砍动画必须在伤害窗口结束前不回到待机");
            }
        }

        [Test]
        public void RangedAnimationIsShorterThanItsCooldown()
        {
            WeaponVisualCatalog catalog = Catalog();
            string root = FindConfigDirectory();
            if (root == null) Assert.Ignore("找不到导出后的配置目录");
            ConfigDatabase config = ConfigDatabase.Load(new DiskSource(root));

            foreach (WeaponDef def in config.Weapons.Values)
            {
                if (def.Kind != WeaponKind.Ranged) continue;

                WeaponVisualCatalog.Entry entry = catalog.Find(def.Id);
                Assert.Less(entry.AttackDuration, def.Cooldown,
                    def.Id + " 的动作比冷却还长，连续开火会互相打断");
            }
        }

        static string FindConfigDirectory()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "Assets", "StreamingAssets", "Config");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        sealed class DiskSource : IConfigSource
        {
            readonly string _root;

            public DiskSource(string root)
            {
                _root = root;
            }

            public bool TryRead(string fileName, out string text)
            {
                string path = Path.Combine(_root, fileName);
                if (!File.Exists(path))
                {
                    text = null;
                    return false;
                }

                text = File.ReadAllText(path);
                return true;
            }
        }
    }
}
