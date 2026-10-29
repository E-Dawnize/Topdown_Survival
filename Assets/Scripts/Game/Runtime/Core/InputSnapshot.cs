using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// 一帧的输入快照。RunLoop 每帧生成一份，模拟步进时按引用传递，
    /// 消费过的按钮位会被清掉，保证一帧跑多步时按下只生效一次。
    /// </summary>
    public struct InputSnapshot
    {
        public Vector2 Move;
        public Vector2 Aim;
        public bool DashPressed;
        public bool SkillPressed;
        public bool PausePressed;

        public bool AnyPressed => DashPressed || SkillPressed || PausePressed;
    }
}
