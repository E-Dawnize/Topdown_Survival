using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Game.Config
{
    /// <summary>
    /// 运行时配置库。加载 StreamingAssets/Config 下导表脚本产出的 JSON，构建索引并校验。
    /// 只读：任何运行时强化都走 StatBlock 的 modifier，不写回这里。
    /// </summary>
    public sealed class ConfigDatabase
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
        };

        public string Version { get; private set; }
        public DateTime ExportedAt { get; private set; }

        public IReadOnlyDictionary<string, CharacterDef> Characters => _characters;
        public IReadOnlyDictionary<string, WeaponDef> Weapons => _weapons;
        public IReadOnlyDictionary<string, CompanionDef> Companions => _companions;
        public IReadOnlyDictionary<string, ItemDef> Items => _items;
        public IReadOnlyDictionary<string, EnemyDef> Enemies => _enemies;
        public IReadOnlyList<WaveDef> Waves => _waves;
        public IReadOnlyDictionary<string, BuffDef> Buffs => _buffs;
        public IReadOnlyDictionary<string, float> Balance => _balance;

        Dictionary<string, CharacterDef> _characters;
        Dictionary<string, WeaponDef> _weapons;
        Dictionary<string, CompanionDef> _companions;
        Dictionary<string, ItemDef> _items;
        Dictionary<string, EnemyDef> _enemies;
        List<WaveDef> _waves;
        Dictionary<string, BuffDef> _buffs;
        Dictionary<string, float> _balance;

        public static ConfigDatabase Load(IConfigSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!source.TryRead("manifest.json", out string manifestText))
            {
                throw new ConfigException("找不到 manifest.json，先运行 tools/export_config.py 导表");
            }

            ConfigManifest manifest;
            try
            {
                manifest = JsonConvert.DeserializeObject<ConfigManifest>(manifestText, Settings);
            }
            catch (Exception e)
            {
                throw new ConfigException("manifest.json 解析失败: " + e.Message);
            }

            if (manifest == null || string.IsNullOrEmpty(manifest.version))
            {
                throw new ConfigException("manifest.json 缺少 version");
            }

            var db = new ConfigDatabase
            {
                Version = manifest.version,
                ExportedAt = ParseTime(manifest.exportedAt),
                _characters = LoadIndex<CharacterDef>(source, "Character.json"),
                _weapons = LoadIndex<WeaponDef>(source, "Weapon.json"),
                _companions = LoadIndex<CompanionDef>(source, "Companion.json"),
                _items = LoadIndex<ItemDef>(source, "Item.json"),
                _enemies = LoadIndex<EnemyDef>(source, "Enemy.json"),
                _waves = LoadList<WaveDef>(source, "Wave.json"),
                _buffs = LoadIndex<BuffDef>(source, "Buff.json"),
                _balance = LoadBalance(source, "Balance.json"),
            };

            db._waves.Sort((a, b) => a.Index.CompareTo(b.Index));
            db.Validate();
            return db;
        }

        static DateTime ParseTime(string text)
        {
            return DateTime.TryParse(text, out DateTime t) ? t : default;
        }

        static List<T> LoadList<T>(IConfigSource source, string fileName) where T : class
        {
            if (!source.TryRead(fileName, out string text))
            {
                throw new ConfigException($"缺少 {fileName}");
            }

            try
            {
                List<T> list = JsonConvert.DeserializeObject<List<T>>(text, Settings);
                return list ?? new List<T>();
            }
            catch (Exception e)
            {
                throw new ConfigException($"{fileName} 解析失败: {e.Message}");
            }
        }

        static Dictionary<string, T> LoadIndex<T>(IConfigSource source, string fileName) where T : class
        {
            List<T> list = LoadList<T>(source, fileName);
            var map = new Dictionary<string, T>(list.Count);
            foreach (T item in list)
            {
                string id = GetId(item);
                if (string.IsNullOrEmpty(id))
                {
                    throw new ConfigException($"{fileName} 里有记录缺少 Id");
                }

                if (map.ContainsKey(id))
                {
                    throw new ConfigException($"{fileName} 里 Id '{id}' 重复");
                }

                map.Add(id, item);
            }

            return map;
        }

        static string GetId(object item)
        {
            switch (item)
            {
                case CharacterDef c: return c.Id;
                case WeaponDef w: return w.Id;
                case CompanionDef c: return c.Id;
                case ItemDef i: return i.Id;
                case EnemyDef e: return e.Id;
                case BuffDef b: return b.Id;
                case BalanceDef b: return b.Id;
                default: return null;
            }
        }

        static Dictionary<string, float> LoadBalance(IConfigSource source, string fileName)
        {
            List<BalanceDef> list = LoadList<BalanceDef>(source, fileName);
            var map = new Dictionary<string, float>(list.Count);
            foreach (BalanceDef def in list)
            {
                if (string.IsNullOrEmpty(def.Id))
                {
                    throw new ConfigException($"{fileName} 里有记录缺少 Id");
                }

                map[def.Id] = def.Value;
            }

            return map;
        }

        /// <summary>引用完整性与数值区间。开发期失败直接抛，让问题在启动时就暴露。</summary>
        public void Validate()
        {
            var errors = new List<string>();

            foreach (CharacterDef c in _characters.Values)
            {
                RequireRef(_weapons, c.StartingWeaponId, "Character", c.Id, "StartingWeaponId", errors);
                if (c.BaseHP <= 0f) errors.Add($"Character.{c.Id}: BaseHP 必须大于 0");
                if (c.BaseAttackInterval <= 0f) errors.Add($"Character.{c.Id}: BaseAttackInterval 必须大于 0");
            }

            foreach (WeaponDef w in _weapons.Values)
            {
                if (w.Cooldown <= 0f) errors.Add($"Weapon.{w.Id}: Cooldown 必须大于 0");
                if (w.Range <= 0f) errors.Add($"Weapon.{w.Id}: Range 必须大于 0");
                if (w.Projectiles <= 0) errors.Add($"Weapon.{w.Id}: Projectiles 至少为 1");
                if (w.Kind == WeaponKind.Ranged && w.ProjectileSpeed <= 0f)
                {
                    errors.Add($"Weapon.{w.Id}: 远程武器 ProjectileSpeed 必须大于 0");
                }
            }

            foreach (CompanionDef c in _companions.Values)
            {
                RequireRef(_weapons, c.WeaponId, "Companion", c.Id, "WeaponId", errors);
                if (c.FollowRadius <= 0f) errors.Add($"Companion.{c.Id}: FollowRadius 必须大于 0");
            }

            foreach (ItemDef i in _items.Values)
            {
                if (i.BasePrice < 0) errors.Add($"Item.{i.Id}: BasePrice 不能为负");
                if (i.MaxStack <= 0) errors.Add($"Item.{i.Id}: MaxStack 至少为 1");
                if (!i.HasStatA && !i.HasStatB && string.IsNullOrEmpty(i.EffectType))
                {
                    errors.Add($"Item.{i.Id}: 既没有属性也没有 EffectType，这件道具没有效果");
                }
            }

            foreach (EnemyDef e in _enemies.Values)
            {
                if (e.HP <= 0f) errors.Add($"Enemy.{e.Id}: HP 必须大于 0");
                if (e.Speed < 0f) errors.Add($"Enemy.{e.Id}: Speed 不能为负");
                if (e.BodyRadius <= 0f) errors.Add($"Enemy.{e.Id}: BodyRadius 必须大于 0");
            }

            var waveIndices = new HashSet<int>();
            foreach (WaveDef w in _waves)
            {
                if (!waveIndices.Add(w.Index)) errors.Add($"Wave.{w.Id}: Index {w.Index} 重复");
                if (w.Duration <= 0f) errors.Add($"Wave.{w.Id}: Duration 必须大于 0");
                if (w.SpawnInterval <= 0f) errors.Add($"Wave.{w.Id}: SpawnInterval 必须大于 0");
                if (w.SpawnCountMin > w.SpawnCountMax)
                {
                    errors.Add($"Wave.{w.Id}: SpawnCountMin 大于 SpawnCountMax");
                }

                if (w.EnemyPool == null || w.EnemyPool.Length == 0)
                {
                    errors.Add($"Wave.{w.Id}: EnemyPool 为空");
                }
                else
                {
                    foreach (string enemyId in w.EnemyPool)
                    {
                        RequireRef(_enemies, enemyId, "Wave", w.Id, "EnemyPool", errors);
                    }
                }

                if (!string.IsNullOrEmpty(w.BossId))
                {
                    RequireRef(_enemies, w.BossId, "Wave", w.Id, "BossId", errors);
                }
            }

            if (_waves.Count == 0) errors.Add("Wave 表没有任何波次");

            foreach (BuffDef b in _buffs.Values)
            {
                if (b.Duration <= 0f) errors.Add($"Buff.{b.Id}: Duration 必须大于 0");
                if (b.MaxStacks <= 0) errors.Add($"Buff.{b.Id}: MaxStacks 至少为 1");
            }

            if (errors.Count > 0)
            {
                throw new ConfigException("配置校验失败（" + errors.Count + " 项）:\n  " + string.Join("\n  ", errors));
            }
        }

        static void RequireRef<T>(Dictionary<string, T> table, string id, string sheet, string rowId,
            string field, List<string> errors)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!table.ContainsKey(id))
            {
                errors.Add($"{sheet}.{rowId}.{field}: 引用的 '{id}' 不存在");
            }
        }

        public float GetBalance(string id, float fallback)
        {
            return _balance.TryGetValue(id, out float v) ? v : fallback;
        }
    }
}
