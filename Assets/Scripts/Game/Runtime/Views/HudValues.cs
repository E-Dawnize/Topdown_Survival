using System;
using Game.Controllers;
using Game.Core;
using Game.Model;
using Game.Systems;

namespace Game.Views
{
    /// <summary>HUD 上会变的一项数值。用位标记表达"哪几项变了"，刷新时只写这几项。</summary>
    [Flags]
    public enum HudField
    {
        None = 0,
        Hp = 1 << 0,
        Progress = 1 << 1,
        RunTime = 1 << 2,
        Dash = 1 << 3,
        AliveEnemies = 1 << 4,
        Kills = 1 << 5,
        Build = 1 << 6,
        Boss = 1 << 7,
        All = Hp | Progress | RunTime | Dash | AliveEnemies | Kills | Build | Boss
    }

    /// <summary>
    /// HUD 每帧读到的数值快照。抽成纯数据（不碰 UnityEngine）：
    ///   1 只有值真的变了才写 uGUI，避免每帧重建 Canvas；
    ///   2 取值与比对规则能在 EditMode 里直接测。
    ///
    /// 快照必须自带展示所需的全部信息：HudView 只拿这一份值写控件，
    /// 不再回头去读 PlayerModel / DirectorSystem / EnemySystem——否则“哪些字段参与刷新”
    /// 会散落在 Diff 与 ApplyValues 两处，改一个显示项要同时动两个地方。
    /// </summary>
    public readonly struct HudValues
    {
        public readonly int Hp;
        public readonly int MaxHp;
        public readonly float HpRatio;

        /// <summary>玩家不存在（装配半途失败）时血条文字显示 "-- / --"。</summary>
        public readonly bool HasPlayer;

        public readonly int Level;
        public readonly int Xp;
        public readonly int XpToNext;
        public readonly float XpRatio;

        public readonly int RunSecondsLeft;
        public readonly int Threat;
        public readonly int Kills;
        public readonly int AliveEnemies;

        /// <summary>冲刺冷却剩余，向上取整到 0.1 秒。0 表示就绪；它是 Diff 的判据（整数不受浮点噪声影响）。</summary>
        public readonly int DashTenths;

        /// <summary>冲刺冷却进度：1 = 就绪，0 = 刚按下。没有玩家时为 0（条是空的）。</summary>
        public readonly float DashRatio;

        public readonly int Weapons;
        public readonly int WeaponSlots;
        public readonly int Companions;
        public readonly int CompanionSlots;
        public readonly int Combos;

        /// <summary>
        /// 构筑内容的版本号。数量相同但武器/旅伴升级、组合触发时它会变；
        /// HUD 只在它变化时重拼装备栏文字，不需要每帧拼字符串。
        /// </summary>
        public readonly int BuildRevision;

        public readonly bool BossActive;
        public readonly float BossHpRatio;
        public readonly string BossName;
        public readonly bool BossEnraged;

        /// <summary>
        /// 升级选卡 / 结算期间为 true：战斗数据冻结，HudView 不写那些控件。
        /// 放在快照里，HudView 就不必为了一个 `Phase` 判断而持有 RunController。
        /// </summary>
        public readonly bool Frozen;

        public HudValues(
            int hp, int maxHp, float hpRatio, bool hasPlayer,
            int level, int xp, int xpToNext, float xpRatio,
            int runSecondsLeft, int threat,
            int kills, int aliveEnemies,
            int dashTenths, float dashRatio,
            int weapons, int weaponSlots, int companions, int companionSlots, int combos,
            bool bossActive, float bossHpRatio, string bossName, bool bossEnraged,
            bool frozen, int buildRevision = 0)
        {
            Hp = hp;
            MaxHp = maxHp;
            HpRatio = hpRatio;
            HasPlayer = hasPlayer;

            Level = level;
            Xp = xp;
            XpToNext = xpToNext;
            XpRatio = xpRatio;

            RunSecondsLeft = runSecondsLeft;
            Threat = threat;
            Kills = kills;
            AliveEnemies = aliveEnemies;

            DashTenths = dashTenths;
            DashRatio = dashRatio;

            Weapons = weapons;
            WeaponSlots = weaponSlots;
            Companions = companions;
            CompanionSlots = companionSlots;
            Combos = combos;
            BuildRevision = buildRevision;

            BossActive = bossActive;
            BossHpRatio = bossHpRatio;
            BossName = bossName;
            BossEnraged = bossEnraged;

            Frozen = frozen;
        }

        public static HudValues From(PlayerModel player, RunState run, RunController runController,
            EnemySystem enemies, BuildController build, DirectorSystem director, bool bossEnraged)
        {
            bool hasPlayer = player != null;
            float hp = hasPlayer ? player.HP : 0f;
            float maxHp = hasPlayer ? player.MaxHP : 0f;

            float dashCooldown = hasPlayer ? player.DashCooldown : 0f;
            float dashTimer = hasPlayer ? player.DashCooldownTimer : 0f;
            float dashRatio = hasPlayer
                ? (dashCooldown > 0f ? Clamp01(1f - dashTimer / dashCooldown) : 1f)
                : 0f;

            return new HudValues(
                hp: (int)Math.Ceiling(hp),
                maxHp: (int)Math.Ceiling(maxHp),
                hpRatio: hasPlayer ? player.HpRatio : 0f,
                hasPlayer: hasPlayer,
                level: run != null ? run.Level : 1,
                xp: run != null ? run.Xp : 0,
                xpToNext: run != null ? run.XpToNext : 0,
                xpRatio: run != null ? run.XpRatio : 0f,
                runSecondsLeft: runController != null ? SecondsLeftCeil(runController.RunSecondsLeft) : 0,
                threat: run != null ? Math.Max(1, run.HighestThreat) : 1,
                kills: run != null ? run.Kills : 0,
                aliveEnemies: enemies != null ? enemies.AliveCount : 0,
                dashTenths: dashTimer > 0f ? (int)Math.Ceiling(dashTimer * 10f) : 0,
                dashRatio: dashRatio,
                weapons: build != null ? build.WeaponCount : 0,
                weaponSlots: build != null ? build.WeaponSlots : 4,
                companions: build != null ? build.CompanionCount : 0,
                companionSlots: CompanionSystem.MaxCompanions,
                combos: build != null ? build.ActiveComboCount : 0,
                bossActive: director != null && director.BossActive,
                bossHpRatio: director != null ? director.BossHpRatio : 0f,
                bossName: director != null ? director.BossName : string.Empty,
                bossEnraged: bossEnraged,
                frozen: runController != null &&
                        (runController.Phase == RunPhase.LevelUp || runController.Phase == RunPhase.GameOver),
                buildRevision: build != null ? build.LoadoutRevision : 0);
        }

        static int SecondsLeftCeil(float secondsLeft)
        {
            if (secondsLeft <= 0f) return 0;
            return (int)Math.Ceiling(secondsLeft - 0.001f);
        }

        static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        /// <summary>哪些项和上一份快照不同。差异按 HUD 的显示块分组，刷新时按块整块重写。</summary>
        public HudField Diff(in HudValues previous)
        {
            HudField changed = HudField.None;

            if (Hp != previous.Hp || MaxHp != previous.MaxHp || HasPlayer != previous.HasPlayer ||
                !HpRatio.Equals(previous.HpRatio))
            {
                changed |= HudField.Hp;
            }

            if (Level != previous.Level || Xp != previous.Xp || XpToNext != previous.XpToNext ||
                !XpRatio.Equals(previous.XpRatio))
            {
                changed |= HudField.Progress;
            }

            if (RunSecondsLeft != previous.RunSecondsLeft || Threat != previous.Threat)
            {
                changed |= HudField.RunTime;
            }

            if (DashTenths != previous.DashTenths || !DashRatio.Equals(previous.DashRatio))
            {
                changed |= HudField.Dash;
            }

            if (AliveEnemies != previous.AliveEnemies) changed |= HudField.AliveEnemies;
            if (Kills != previous.Kills) changed |= HudField.Kills;

            if (Weapons != previous.Weapons || WeaponSlots != previous.WeaponSlots ||
                Companions != previous.Companions || CompanionSlots != previous.CompanionSlots ||
                Combos != previous.Combos || BuildRevision != previous.BuildRevision)
            {
                changed |= HudField.Build;
            }

            if (BossActive != previous.BossActive || BossEnraged != previous.BossEnraged ||
                !BossHpRatio.Equals(previous.BossHpRatio) ||
                !string.Equals(BossName, previous.BossName, StringComparison.Ordinal))
            {
                changed |= HudField.Boss;
            }

            return changed;
        }
    }
}
