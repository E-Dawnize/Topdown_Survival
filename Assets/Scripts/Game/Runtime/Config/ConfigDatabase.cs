using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Game.Config
{
    /// <summary>
    /// 运行时配置库。加载 StreamingAssets/Config 下导表脚本产出的 JSON，构建索引并校验。
    /// 只读：任何运行时强化都走 StatBlock 的 modifier，不写回这里。
    ///
    /// 这一版把"波次 + 商店"换成了"导演时间轴 + 升级构筑"：
    /// Director 决定什么时候刷多少、刷什么品质的怪，Level 决定升级节奏，
    /// Upgrade 是升级时抽的三张卡，Buff 是被抽到后真正生效的触发效果。
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
        public IReadOnlyDictionary<string, EnemyDef> Enemies => _enemies;
        public IReadOnlyDictionary<string, BuffDef> Buffs => _buffs;
        public IReadOnlyDictionary<string, UpgradeDef> Upgrades => _upgrades;
        public IReadOnlyDictionary<string, ComboDef> Combos => _combos;
        public IReadOnlyList<DirectorDef> Directors => _directors;
        public IReadOnlyList<LevelDef> Levels => _levels;
        public IReadOnlyDictionary<string, float> Balance => _balance;

        Dictionary<string, CharacterDef> _characters;
        Dictionary<string, WeaponDef> _weapons;
        Dictionary<string, CompanionDef> _companions;
        Dictionary<string, EnemyDef> _enemies;
        Dictionary<string, BuffDef> _buffs;
        Dictionary<string, UpgradeDef> _upgrades;
        Dictionary<string, ComboDef> _combos;
        List<DirectorDef> _directors;
        List<LevelDef> _levels;
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
                _enemies = LoadIndex<EnemyDef>(source, "Enemy.json"),
                _buffs = LoadIndex<BuffDef>(source, "Buff.json"),
                _upgrades = LoadIndex<UpgradeDef>(source, "Upgrade.json"),
                _combos = LoadIndex<ComboDef>(source, "Combo.json"),
                _directors = LoadList<DirectorDef>(source, "Director.json"),
                _levels = LoadList<LevelDef>(source, "Level.json"),
                _balance = LoadBalance(source, "Balance.json"),
            };

            db._directors.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            db._levels.Sort((a, b) => a.Level.CompareTo(b.Level));
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
                case EnemyDef e: return e.Id;
                case BuffDef b: return b.Id;
                case UpgradeDef u: return u.Id;
                case ComboDef c: return c.Id;
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

                if (map.ContainsKey(def.Id))
                {
                    throw new ConfigException($"{fileName} 里 Id '{def.Id}' 重复");
                }

                map.Add(def.Id, def.Value);
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
                if (c.BaseMoveSpeed < 0f) errors.Add($"Character.{c.Id}: BaseMoveSpeed 不能为负");
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

                if (!string.IsNullOrEmpty(w.BehaviourType))
                {
                    bool ranged = w.BehaviourType == "Ranged";
                    bool melee = w.BehaviourType == "Melee";
                    if (!ranged && !melee)
                    {
                        errors.Add($"Weapon.{w.Id}: BehaviourType 只能是 Ranged/Melee（当前 '{w.BehaviourType}'）");
                    }
                    else if (ranged != (w.Kind == WeaponKind.Ranged))
                    {
                        errors.Add($"Weapon.{w.Id}: BehaviourType '{w.BehaviourType}' 与 Kind '{w.Kind}' 不一致");
                    }
                }
            }

            foreach (CompanionDef c in _companions.Values)
            {
                RequireRef(_weapons, c.WeaponId, "Companion", c.Id, "WeaponId", errors);
                if (c.FollowRadius <= 0f) errors.Add($"Companion.{c.Id}: FollowRadius 必须大于 0");
                if (c.MaxHP <= 0f) errors.Add($"Companion.{c.Id}: MaxHP 必须大于 0");
            }

            foreach (EnemyDef e in _enemies.Values)
            {
                if (string.IsNullOrEmpty(e.PrefabId)) errors.Add($"Enemy.{e.Id}: PrefabId 不能为空");
                if (e.Tier < 1) errors.Add($"Enemy.{e.Id}: Tier 至少为 1");
                if (e.HP <= 0f) errors.Add($"Enemy.{e.Id}: HP 必须大于 0");
                if (e.Speed < 0f) errors.Add($"Enemy.{e.Id}: Speed 不能为负");
                if (e.XpValue < 0) errors.Add($"Enemy.{e.Id}: XpValue 不能为负");
                if (e.BodyRadius <= 0f) errors.Add($"Enemy.{e.Id}: BodyRadius 必须大于 0");
                if (e.Scale <= 0f) errors.Add($"Enemy.{e.Id}: Scale 必须大于 0");
            }

            ValidateDirectors(errors);
            ValidateLevels(errors);
            ValidateUpgrades(errors);
            ValidateBuffs(errors);
            ValidateCombos(errors);
            ValidateBalance(errors);

            if (errors.Count > 0)
            {
                throw new ConfigException("配置校验失败（" + errors.Count + " 项）:\n  " +
                                          string.Join("\n  ", errors));
            }
        }

        void ValidateDirectors(List<string> errors)
        {
            if (_directors.Count == 0)
            {
                errors.Add("Director 表没有任何时间轴");
                return;
            }

            float previousEnd = -1f;
            for (int i = 0; i < _directors.Count; i++)
            {
                DirectorDef d = _directors[i];
                if (d.StartTime < 0f) errors.Add($"Director.{d.Id}: StartTime 不能为负");
                if (d.EndTime <= d.StartTime) errors.Add($"Director.{d.Id}: EndTime 必须大于 StartTime");
                if (i == 0 && Math.Abs(d.StartTime) > 0.001f)
                {
                    errors.Add($"Director.{d.Id}: 第一段必须从 0 秒开始");
                }

                if (i > 0 && Math.Abs(d.StartTime - previousEnd) > 0.001f)
                {
                    errors.Add($"Director.{d.Id}: 与上一段时间不连续（上一段结束 {previousEnd}）");
                }

                previousEnd = d.EndTime;
                if (d.Threat < 1) errors.Add($"Director.{d.Id}: Threat 至少为 1");
                if (d.SpawnInterval <= 0f) errors.Add($"Director.{d.Id}: SpawnInterval 必须大于 0");
                if (d.SpawnCountMin <= 0 || d.SpawnCountMin > d.SpawnCountMax)
                {
                    errors.Add($"Director.{d.Id}: SpawnCountMin/Max 不合法");
                }

                if (d.ConcurrentCap <= 0) errors.Add($"Director.{d.Id}: ConcurrentCap 必须大于 0");
                if (d.EliteChance < 0f || d.EliteChance > 1f)
                {
                    errors.Add($"Director.{d.Id}: EliteChance 必须在 0..1");
                }

                if (d.HpScale <= 0f || d.DamageScale <= 0f || d.SpeedScale <= 0f)
                {
                    errors.Add($"Director.{d.Id}: HpScale/DamageScale/SpeedScale 必须大于 0");
                }

                if (d.EnemyPool == null || d.EnemyPool.Length == 0)
                {
                    errors.Add($"Director.{d.Id}: EnemyPool 为空");
                }
                else
                {
                    foreach (string enemyId in d.EnemyPool)
                    {
                        RequireRef(_enemies, enemyId, "Director", d.Id, "EnemyPool", errors);
                    }
                }

                if (!string.IsNullOrEmpty(d.BossId))
                {
                    RequireRef(_enemies, d.BossId, "Director", d.Id, "BossId", errors);
                    if (i != _directors.Count - 1)
                    {
                        errors.Add($"Director.{d.Id}: BossId 只允许写在最后一段");
                    }
                }
            }

            DirectorDef last = _directors[_directors.Count - 1];
            if (string.IsNullOrEmpty(last.BossId))
            {
                errors.Add($"Director.{last.Id}: 最后一段必须指定 BossId，否则时间到了没有 BOSS");
            }

            if (last.BossAddInterval <= 0f)
            {
                errors.Add($"Director.{last.Id}: BossAddInterval 必须大于 0");
            }
        }

        void ValidateLevels(List<string> errors)
        {
            if (_levels.Count == 0)
            {
                errors.Add("Level 表没有任何等级");
                return;
            }

            for (int i = 0; i < _levels.Count; i++)
            {
                LevelDef level = _levels[i];
                if (level.Level != i + 1)
                {
                    errors.Add($"Level.{level.Level}: 等级必须从 1 连续排到 {_levels.Count}");
                }

                if (level.XpToNext <= 0) errors.Add($"Level.{level.Level}: XpToNext 必须大于 0");
                if (level.OfferCount < 1) errors.Add($"Level.{level.Level}: OfferCount 至少为 1");
                if (level.RarityBonus < 0f) errors.Add($"Level.{level.Level}: RarityBonus 不能为负");
            }
        }

        void ValidateUpgrades(List<string> errors)
        {
            if (_upgrades.Count == 0) errors.Add("Upgrade 表没有任何构筑卡");

            foreach (UpgradeDef u in _upgrades.Values)
            {
                if (u.Weight <= 0) errors.Add($"Upgrade.{u.Id}: Weight 必须大于 0");
                if (u.MaxStacks <= 0) errors.Add($"Upgrade.{u.Id}: MaxStacks 至少为 1");

                switch (u.Category)
                {
                    case UpgradeCategory.Weapon:
                        if (string.IsNullOrEmpty(u.WeaponId)) errors.Add($"Upgrade.{u.Id}: Weapon 卡缺少 WeaponId");
                        RequireRef(_weapons, u.WeaponId, "Upgrade", u.Id, "WeaponId", errors);
                        break;
                    case UpgradeCategory.Companion:
                        if (string.IsNullOrEmpty(u.CompanionId))
                        {
                            errors.Add($"Upgrade.{u.Id}: Companion 卡缺少 CompanionId");
                        }
                        RequireRef(_companions, u.CompanionId, "Upgrade", u.Id, "CompanionId", errors);
                        break;
                    case UpgradeCategory.Buff:
                        if (string.IsNullOrEmpty(u.BuffId)) errors.Add($"Upgrade.{u.Id}: Buff 卡缺少 BuffId");
                        RequireRef(_buffs, u.BuffId, "Upgrade", u.Id, "BuffId", errors);
                        break;
                    case UpgradeCategory.Stat:
                        if (!u.HasStatA && !u.HasStatB)
                        {
                            errors.Add($"Upgrade.{u.Id}: Stat 卡必须至少有一条属性");
                        }
                        break;
                }
            }
        }

        void ValidateCombos(List<string> errors)
        {
            var knownTags = new HashSet<string>();
            foreach (UpgradeDef upgrade in _upgrades.Values)
            {
                if (upgrade.Tags == null) continue;
                foreach (string tag in upgrade.Tags)
                {
                    if (!string.IsNullOrEmpty(tag)) knownTags.Add(tag);
                }
            }

            foreach (ComboDef combo in _combos.Values)
            {
                if (string.IsNullOrEmpty(combo.TagA)) errors.Add($"Combo.{combo.Id}: TagA 不能为空");
                if (combo.CountA <= 0) errors.Add($"Combo.{combo.Id}: CountA 必须大于 0");
                if (!string.IsNullOrEmpty(combo.TagB) && combo.CountB <= 0)
                {
                    errors.Add($"Combo.{combo.Id}: 有 TagB 时 CountB 必须大于 0");
                }
                if (!knownTags.Contains(combo.TagA))
                {
                    errors.Add($"Combo.{combo.Id}: TagA '{combo.TagA}' 没有任何 Upgrade 卡在用");
                }
                if (!string.IsNullOrEmpty(combo.TagB) && !knownTags.Contains(combo.TagB))
                {
                    errors.Add($"Combo.{combo.Id}: TagB '{combo.TagB}' 没有任何 Upgrade 卡在用");
                }
                RequireRef(_buffs, combo.BuffId, "Combo", combo.Id, "BuffId", errors);
            }
        }

        void ValidateBuffs(List<string> errors)
        {
            foreach (BuffDef b in _buffs.Values)
            {
                if (b.Duration < 0f) errors.Add($"Buff.{b.Id}: Duration 不能为负");
                if (b.MaxStacks <= 0) errors.Add($"Buff.{b.Id}: MaxStacks 至少为 1");

                if (b.Effect == BuffEffectType.None && !b.HasStatA && !b.HasStatB)
                {
                    errors.Add($"Buff.{b.Id}: 既没有 Effect 也没有属性，等于没有效果");
                }

                bool timed = b.Effect == BuffEffectType.PeriodicHeal ||
                             b.Effect == BuffEffectType.OnHitChain ||
                             b.Effect == BuffEffectType.OnHitExplode ||
                             b.Effect == BuffEffectType.OrbitDamage;
                if (timed && b.Interval <= 0f)
                {
                    errors.Add($"Buff.{b.Id}: {b.Effect} 必须填大于 0 的 Interval");
                }
            }
        }

        static readonly string[] RequiredBalanceKeys =
        {
            "ArenaHalfExtent", "DashSpeedMultiplier", "DashDuration", "DashCooldown",
            "WeaponSlots", "WeaponMaxLevel", "WeaponDamagePerLevel", "WeaponCooldownPerLevel",
            "WeaponProjectileMilestone", "WeaponPierceMilestone", "HeartDropChance", "HeartHeal",
            "ExperienceCollectCooldown", "EliteHpScale", "EliteDamageScale", "EliteSpeedScale",
            "EliteXpScale", "EliteScale", "BuildWeightWeapon", "BuildWeightCompanion",
            "BuildWeightStat", "BuildWeightBuff", "RerollsPerLevel", "LuckRarityWeight",
        };

        /// <summary>
        /// Balance 的 key 是运行时契约，不再靠 GetBalance 的 fallback 静默补默认值。
        /// 缺 key 或关键数值越界都应在启动/导表阶段失败，而不是变成另一套隐藏数值。
        /// </summary>
        void ValidateBalance(List<string> errors)
        {
            foreach (string key in RequiredBalanceKeys)
            {
                if (!_balance.ContainsKey(key)) errors.Add($"Balance: 缺少必需项 '{key}'");
            }

            CheckRange(errors, "ArenaHalfExtent", 0.01f, float.PositiveInfinity);
            CheckRange(errors, "DashSpeedMultiplier", 0.01f, float.PositiveInfinity);
            CheckRange(errors, "DashDuration", 0.01f, float.PositiveInfinity);
            CheckRange(errors, "DashCooldown", 0.01f, float.PositiveInfinity);
            CheckRange(errors, "WeaponSlots", 1f, float.PositiveInfinity);
            CheckRange(errors, "WeaponMaxLevel", 1f, float.PositiveInfinity);
            CheckRange(errors, "WeaponDamagePerLevel", 0f, float.PositiveInfinity);
            CheckRange(errors, "WeaponCooldownPerLevel", 0.01f, float.PositiveInfinity);
            CheckRange(errors, "WeaponProjectileMilestone", 1f, float.PositiveInfinity);
            CheckRange(errors, "WeaponPierceMilestone", 1f, float.PositiveInfinity);
            CheckRange(errors, "HeartDropChance", 0f, 1f);
            CheckRange(errors, "HeartHeal", 0.01f, float.PositiveInfinity);
            CheckRange(errors, "ExperienceCollectCooldown", 0.01f, float.PositiveInfinity);
            CheckRange(errors, "EliteHpScale", 0.01f, float.PositiveInfinity);
            CheckRange(errors, "EliteDamageScale", 0.01f, float.PositiveInfinity);
            CheckRange(errors, "EliteSpeedScale", 0.01f, float.PositiveInfinity);
            CheckRange(errors, "EliteXpScale", 0.01f, float.PositiveInfinity);
            CheckRange(errors, "EliteScale", 0.01f, float.PositiveInfinity);
            CheckRange(errors, "BuildWeightWeapon", 0f, float.PositiveInfinity);
            CheckRange(errors, "BuildWeightCompanion", 0f, float.PositiveInfinity);
            CheckRange(errors, "BuildWeightStat", 0f, float.PositiveInfinity);
            CheckRange(errors, "BuildWeightBuff", 0f, float.PositiveInfinity);
            CheckRange(errors, "RerollsPerLevel", 0f, float.PositiveInfinity);
            CheckRange(errors, "LuckRarityWeight", 0f, float.PositiveInfinity);
        }

        void CheckRange(List<string> errors, string key, float min, float max)
        {
            if (!_balance.TryGetValue(key, out float value)) return;   // 缺失项上面已经报过
            if (value < min || value > max)
            {
                errors.Add($"Balance.{key}: 值 {value} 超出范围 [{min}, {max}]");
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

        public int GetBalanceInt(string id, int fallback)
        {
            return _balance.TryGetValue(id, out float v) ? (int)(v + 0.5f) : fallback;
        }
    }
}
