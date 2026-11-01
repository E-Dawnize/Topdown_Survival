using System;
using Game.Core.Events;

namespace Game.Views
{
    /// <summary>
    /// 结算面板上的几行字。纯数据（不碰 UnityEngine），面板只管把这几行写进 Text。
    /// 数值一律从 RunSummary 取：它由 RunController 在收尾那一刻按当时的 Model 拼好。
    /// </summary>
    public readonly struct ResultLines
    {
        public readonly string Title;
        public readonly string Duration;
        public readonly string Level;
        public readonly string Kills;
        public readonly string Build;

        ResultLines(string title, string duration, string level, string kills, string build)
        {
            Title = title;
            Duration = duration;
            Level = level;
            Kills = kills;
            Build = build;
        }

        public static ResultLines From(in RunSummary summary, bool victory)
        {
            return new ResultLines(
                title: victory ? "通关" : "阵亡",
                duration: "存活时间  " + Clock(summary.Duration),
                level: "等级  " + summary.Level,
                kills: "击杀  " + summary.Kills + "（精英 " + summary.EliteKills + "）",
                build: "武器 " + summary.Weapons + "　旅伴 " + summary.Companions);
        }

        public static string Clock(float seconds) => TimeFormat.Clock(seconds);
    }
}
