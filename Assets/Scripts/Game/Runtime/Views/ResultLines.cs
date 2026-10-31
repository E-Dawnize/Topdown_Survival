using System;
using Game.Core.Events;

namespace Game.Views
{
    /// <summary>
    /// 结算面板上的五行字。纯数据（不碰 UnityEngine），面板只管把这几行写进 Text。
    ///
    /// 数值一律从 <see cref="RunSummary"/> 取：它由 RunController 在收尾那一刻按当时的 Model 拼好，
    /// 面板再自己去读一遍 RunState / 敌人数就等于算第二次，两处迟早会差一帧或者差一次清零。
    /// 只有"第几波"要额外的波次索引——死在半路时那一波没打完，RunSummary.WavesCleared 数不到它。
    /// </summary>
    public readonly struct ResultLines
    {
        public readonly string Title;
        public readonly string Duration;
        public readonly string Wave;
        public readonly string Coins;
        public readonly string Kills;

        ResultLines(string title, string duration, string wave, string coins, string kills)
        {
            Title = title;
            Duration = duration;
            Wave = wave;
            Coins = coins;
            Kills = kills;
        }

        public static ResultLines From(in RunSummary summary, bool victory, int waveIndex, int wavesTotal)
        {
            return new ResultLines(
                title: victory ? "通关" : "阵亡",
                duration: "存活时间  " + Clock(summary.Duration),
                wave: "波次  " + waveIndex + " / " + wavesTotal,
                coins: "金币  " + summary.Coins,
                kills: "击杀  " + summary.Kills);
        }

        /// <summary>
        /// 秒数 → m:ss。负数按 0 算，秒数向下取整：
        /// 取整方向与"存活了多久"的直觉一致（59.9 秒显示 0:59 而不是 1:00），
        /// 分钟不封顶，一局跑成什么样都不会显示成 0:00。
        /// </summary>
        public static string Clock(float seconds)
        {
            if (seconds < 0f) seconds = 0f;

            int total = (int)Math.Floor(seconds);
            int minutes = total / 60;
            int rest = total % 60;
            return minutes + ":" + (rest < 10 ? "0" + rest : rest.ToString());
        }
    }
}
