using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 渲染快照。模拟跑固定步长，渲染按实际帧率取两个模拟步之间的插值。
    /// 视图只吃这份数据，不直接读 Model：模拟的阶梯落到 Transform 上就是抖动，
    /// 而模拟状态本身该归物理与判定用。
    /// </summary>
    public readonly struct ViewSnapshot
    {
        public readonly Vector2 Position;

        /// <summary>世界 XZ 平面上的朝向单位向量。</summary>
        public readonly Vector2 Forward;

        public ViewSnapshot(Vector2 position, Vector2 forward)
        {
            Position = position;
            Forward = forward;
        }

        public static Vector2 DirectionOf(float facingRadians)
        {
            return new Vector2(Mathf.Cos(facingRadians), Mathf.Sin(facingRadians));
        }
    }
}
