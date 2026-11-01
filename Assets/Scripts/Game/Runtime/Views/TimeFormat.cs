using System;

namespace Game.Views
{
    /// <summary>界面上的时间格式：秒 → m:ss，负数按 0 算，秒数向下取整。</summary>
    public static class TimeFormat
    {
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
