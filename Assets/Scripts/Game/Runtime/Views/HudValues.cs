using System;
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
        Coins = 1 << 1,
        Wave = 1 << 2,
        WaveTime = 1 << 3,
        Dash = 1 << 4,
        AliveEnemies = 1 << 5,
        Kills = 1 << 6,
        All = Hp | Coins | Wave | WaveTime | Dash | AliveEnemies | Kills
    }

    /// <summary>
    /// HUD 每帧读到的数值快照。抽成纯数据（不碰 UnityEngine）有两个用处：
    ///   1 只有值真的变了才写 uGUI：每帧无条件改 text / fillAmount 会一直给布局制造脏标记，
    ///     一屏静态数字也会让 Canvas 每帧重建；
    ///   2 取值与比对的规则都能在仓库外的 dotnet harness 里直接测，不必进 Unity 看画面。
    ///
    /// 显示用的取整也定在这里（血与倒计时向上取整、冲刺计时按 0.1 秒分档），
    /// 免得文本和判定各按一套取整，出现"写着还剩 0s 其实还在打"这类错位。
    /// 冲刺用向上取整而不是四舍五入：0.04 秒的残余如果舍成 0，就再也不会因为"变成 0"而刷新成"就绪"。
    /// </summary>
    public readonly struct HudValues : IEquatable<HudValues>
    {
        public readonly int Hp;
        public readonly int MaxHp;
        public readonly float HpRatio;
        public readonly int Coins;
        public readonly int WaveIndex;
        public readonly int WavesTotal;
        public readonly int WaveSecondsLeft;
        public readonly int DashTenths;
        public readonly int AliveEnemies;
        public readonly int Kills;

        public HudValues(int hp, int maxHp, float hpRatio, int coins, int waveIndex, int wavesTotal,
            int waveSecondsLeft, int dashTenths, int aliveEnemies, int kills)
        {
            Hp = hp;
            MaxHp = maxHp;
            HpRatio = hpRatio;
            Coins = coins;
            WaveIndex = waveIndex;
            WavesTotal = wavesTotal;
            WaveSecondsLeft = waveSecondsLeft;
            DashTenths = dashTenths;
            AliveEnemies = aliveEnemies;
            Kills = kills;
        }

        /// <summary>
        /// 从 Model / Controller 的只读接口取一份快照。规则（血量比例、波次剩余时间、冲刺冷却）
        /// 都向各自的所有者要，HUD 不在这里重算一遍——两处各算一次，改了规则就会只改对一半。
        /// 任一引用为 null 时该项给 0：HUD 在没有战斗的场景里照常显示占位值。
        /// </summary>
        public static HudValues From(PlayerModel player, RunState run, RunController runController,
            EnemySystem enemies)
        {
            float hp = player != null ? player.HP : 0f;
            float maxHp = player != null ? player.MaxHP : 0f;
            float dashCooldownTimer = player != null ? player.DashCooldownTimer : 0f;

            return new HudValues(
                hp: (int)Math.Ceiling(hp),
                maxHp: (int)Math.Ceiling(maxHp),
                hpRatio: player != null ? player.HpRatio : 0f,
                coins: run != null ? run.Coins : 0,
                waveIndex: run != null ? run.WaveIndex : 0,
                wavesTotal: run != null ? run.WavesTotal : 0,
                waveSecondsLeft: runController != null ? SecondsLeftCeil(runController.WaveSecondsLeft) : 0,
                dashTenths: dashCooldownTimer > 0f ? (int)Math.Ceiling(dashCooldownTimer * 10f) : 0,
                aliveEnemies: enemies != null ? enemies.AliveCount : 0,
                kills: runController != null ? runController.Kills : 0);
        }

        /// <summary>
        /// 倒计时向上取整，先减去一个亚毫秒的容差：固定步长累加出来的 29.99994 秒
        /// 会让剩余时间变成 30.00006，直接向上取整就多跳一秒（倒计时看着卡一下）。
        /// 容差只吃掉累加误差，不影响“还剩 0.02 秒也算 1 秒”这条显示规则。
        /// </summary>
        static int SecondsLeftCeil(float secondsLeft)
        {
            if (secondsLeft <= 0f) return 0;
            return (int)Math.Ceiling(secondsLeft - 0.001f);
        }

        /// <summary>与另一份快照比，返回哪几项不一样。全一样时返回 <see cref="HudField.None"/>。</summary>
        public HudField Diff(in HudValues previous)
        {
            HudField changed = HudField.None;

            if (Hp != previous.Hp || MaxHp != previous.MaxHp || !HpRatio.Equals(previous.HpRatio))
            {
                changed |= HudField.Hp;
            }

            if (Coins != previous.Coins) changed |= HudField.Coins;
            if (WaveIndex != previous.WaveIndex || WavesTotal != previous.WavesTotal) changed |= HudField.Wave;
            if (WaveSecondsLeft != previous.WaveSecondsLeft) changed |= HudField.WaveTime;
            if (DashTenths != previous.DashTenths) changed |= HudField.Dash;
            if (AliveEnemies != previous.AliveEnemies) changed |= HudField.AliveEnemies;
            if (Kills != previous.Kills) changed |= HudField.Kills;

            return changed;
        }

        /// <summary>
        /// 用 <paramref name="written"/> 指定的那几项从 <paramref name="newer"/> 覆盖本快照，其余保持原值。
        /// HUD 存的是"上一次真正写进 uGUI 的数值"，不是"上一帧读到什么"：
        /// 商店与结算里战斗项被冻结、没写进去，它们就必须继续留在旧值里，
        /// 否则解冻后差值算不出来，血条与倒计时会一直停在冻结前的数字上。
        /// </summary>
        public HudValues Merge(in HudValues newer, HudField written)
        {
            return new HudValues(
                hp: (written & HudField.Hp) != 0 ? newer.Hp : Hp,
                maxHp: (written & HudField.Hp) != 0 ? newer.MaxHp : MaxHp,
                hpRatio: (written & HudField.Hp) != 0 ? newer.HpRatio : HpRatio,
                coins: (written & HudField.Coins) != 0 ? newer.Coins : Coins,
                waveIndex: (written & HudField.Wave) != 0 ? newer.WaveIndex : WaveIndex,
                wavesTotal: (written & HudField.Wave) != 0 ? newer.WavesTotal : WavesTotal,
                waveSecondsLeft: (written & HudField.WaveTime) != 0 ? newer.WaveSecondsLeft : WaveSecondsLeft,
                dashTenths: (written & HudField.Dash) != 0 ? newer.DashTenths : DashTenths,
                aliveEnemies: (written & HudField.AliveEnemies) != 0 ? newer.AliveEnemies : AliveEnemies,
                kills: (written & HudField.Kills) != 0 ? newer.Kills : Kills);
        }

        public bool Equals(HudValues other) => Diff(other) == HudField.None;

        public override bool Equals(object obj) => obj is HudValues other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Hp;
                hash = (hash * 397) ^ MaxHp;
                hash = (hash * 397) ^ HpRatio.GetHashCode();
                hash = (hash * 397) ^ Coins;
                hash = (hash * 397) ^ WaveIndex;
                hash = (hash * 397) ^ WavesTotal;
                hash = (hash * 397) ^ WaveSecondsLeft;
                hash = (hash * 397) ^ DashTenths;
                hash = (hash * 397) ^ AliveEnemies;
                hash = (hash * 397) ^ Kills;
                return hash;
            }
        }
    }
}
