using System;

namespace Game.Model
{
    /// <summary>
    /// 一局内的进度：等级、经验、击杀、威胁阶段。
    /// 这一版没有金币与波次——掉落物是经验，怪物强度由 DirectorSystem 按时间推进，
    /// 所以这里只保留 HUD、升级抽卡和结算真正会读的字段。
    ///
    /// AddExperience 会处理一次拾取跨过多个等级的情况：经验按"每级门槛"逐级扣，
    /// 溢出的部分留给下一级，产生的待选升级次数累积在 PendingLevelUps 里。
    /// 战斗不会因为有两次升级就一次弹出两张卡——RunController 一次只开一轮，选完再开下一轮。
    /// </summary>
    public sealed class RunState
    {
        public RunState(ExperienceCurve curve)
        {
            Curve = curve ?? throw new ArgumentNullException(nameof(curve));

            Level = 1;
            Xp = 0;
            TotalXp = 0;
            PendingLevelUps = 0;
            Kills = 0;
            EliteKills = 0;
            BossKills = 0;
            HighestThreat = 1;
        }

        public readonly ExperienceCurve Curve;

        /// <summary>当前等级，从 1 开始。</summary>
        public int Level { get; private set; }

        /// <summary>当前等级里已经攒的经验（不含已升级扣掉的部分）。</summary>
        public int Xp { get; private set; }

        /// <summary>整局累计获得的经验，结算与调参用。</summary>
        public int TotalXp { get; private set; }

        /// <summary>还欠玩家几次升级选择。一次拾取可能跨两级，这里就会是 2。</summary>
        public int PendingLevelUps { get; private set; }

        public int Kills { get; private set; }
        public int EliteKills { get; private set; }
        public int BossKills { get; private set; }

        /// <summary>本局见过的最高威胁等级，用于 HUD 与结算。</summary>
        public int HighestThreat { get; private set; }

        public int MaxLevel => Curve.MaxLevel;
        public int XpToNext => Curve.XpToNext(Level);
        public bool IsMaxLevel => Level >= Curve.MaxLevel;
        public float XpRatio => XpToNext <= 0 ? 0f : Math.Clamp((float)Xp / XpToNext, 0f, 1f);

        /// <summary>换局归零。等级曲线是配置事实，不重置。</summary>
        public void ResetForNewRun()
        {
            Level = 1;
            Xp = 0;
            TotalXp = 0;
            PendingLevelUps = 0;
            Kills = 0;
            EliteKills = 0;
            BossKills = 0;
            HighestThreat = 1;
        }

        /// <summary>
        /// 加经验并结算升级。返回这次加经验实际升了几级。
        /// 满级后经验不再累积：HUD 显示满级，升级抽卡也不会再排。
        /// </summary>
        public int AddExperience(int amount)
        {
            if (amount <= 0) return 0;

            TotalXp += amount;
            if (IsMaxLevel) return 0;

            Xp += amount;
            int gained = 0;

            while (!IsMaxLevel && Xp >= XpToNext)
            {
                Xp -= XpToNext;
                Level++;
                gained++;
                PendingLevelUps++;
            }

            // 到满级时把残余经验清掉，避免 HUD 显示一个永远不满的条
            if (IsMaxLevel) Xp = 0;
            return gained;
        }

        /// <summary>打开一轮升级时消费一次。RunController 每次只消费一次，选完再看还有没有。</summary>
        public bool ConsumePendingLevelUp()
        {
            if (PendingLevelUps <= 0) return false;
            PendingLevelUps--;
            return true;
        }

        public void AddKill(bool elite, bool boss)
        {
            Kills++;
            if (elite) EliteKills++;
            if (boss) BossKills++;
        }

        public void ReportThreat(int threat)
        {
            if (threat > HighestThreat) HighestThreat = threat;
        }
    }
}
