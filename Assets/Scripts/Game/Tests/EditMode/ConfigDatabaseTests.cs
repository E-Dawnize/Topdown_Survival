using System.IO;
using Game.Config;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 直接加载仓库里的 Assets/StreamingAssets/Config，确认真实配置能过校验、
    /// 构筑四类卡都有、导演最后一段带 BOSS。Unity EditMode 与命令行 harness 的工作目录都是项目根。
    /// </summary>
    public class ConfigDatabaseTests
    {
        [Test]
        public void ShippedConfigLoadsAndCoversEveryBuildCategory()
        {
            string root = FindConfigDirectory();
            if (root == null)
            {
                Assert.Ignore("找不到导出后的配置目录");
            }

            var config = ConfigDatabase.Load(new DiskSource(root));

            Assert.GreaterOrEqual(config.Directors.Count, 2);
            Assert.GreaterOrEqual(config.Levels.Count, 10);
            Assert.GreaterOrEqual(config.Upgrades.Count, 20);
            Assert.GreaterOrEqual(config.Buffs.Count, 5);
            Assert.GreaterOrEqual(config.Enemies.Count, 5);
            Assert.GreaterOrEqual(config.Combos.Count, 2, "至少要有两个组合效果");

            Assert.AreEqual(0f, config.Directors[0].StartTime, 1e-4f, "第一段必须从 0 秒开始");
            DirectorDef last = config.Directors[config.Directors.Count - 1];
            Assert.Greater(last.EndTime, 60f, "一局至少要跑一分钟以上");
            Assert.IsTrue(last.HasBoss, "最后一段必须指定 BOSS");

            bool weapon = false;
            bool companion = false;
            bool stat = false;
            bool buff = false;
            foreach (UpgradeDef upgrade in config.Upgrades.Values)
            {
                switch (upgrade.Category)
                {
                    case UpgradeCategory.Weapon: weapon = true; break;
                    case UpgradeCategory.Companion: companion = true; break;
                    case UpgradeCategory.Stat: stat = true; break;
                    case UpgradeCategory.Buff: buff = true; break;
                }
            }

            Assert.IsTrue(weapon, "武器卡");
            Assert.IsTrue(companion, "旅伴卡");
            Assert.IsTrue(stat, "数值卡");
            Assert.IsTrue(buff, "buff 卡");
            Assert.Greater(config.GetBalance("WeaponMaxLevel", 0f), 1f);

            int tagged = 0;
            foreach (UpgradeDef upgrade in config.Upgrades.Values)
            {
                if (upgrade.Tags != null && upgrade.Tags.Length > 0) tagged++;
            }
            Assert.Greater(tagged, 20, "大多数构筑卡要带标签，组合效果才有判定依据");
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
    }
}
