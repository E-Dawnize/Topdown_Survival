using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 刷怪环的纯数学。导演系统与手动压测共用这一份：同一批怪按黄金角铺开，
    /// 不会在出生那一刻就叠成一团，也不会全落在同几条射线上。
    /// </summary>
    public static class SpawnRing
    {
        /// <summary>
        /// 黄金角（弧度）。同一批里逐个转 137.5°：等角铺开的一串点不会落在同几条射线上，
        /// 比每个怪各随机一个角度均匀得多。同屏数量大时，这一条决定开局是一群还是一团。
        /// </summary>
        public const float GoldenAngleRad = 2.39996323f;

        /// <summary>刷怪环：玩家周围 9~16 米。太近会一开局就贴身，太远在射程外看不见。</summary>
        public const float RadiusMin = 9f;

        public const float RadiusMax = 16f;

        /// <summary>
        /// 环上的第 index 个点（相对玩家）：角度按黄金角逐个转开，半径按面积均匀分布。
        /// 半径按 sqrt 映射，点落在环带上的面密度才均匀（线性插值会让点朝内圈挤）。
        /// </summary>
        public static Vector2 Offset(int index, int count, float phase, float radiusMin = RadiusMin,
            float radiusMax = RadiusMax)
        {
            if (count <= 0) return Vector2.zero;

            float t = (index + 0.5f) / count;
            float radius = Mathf.Sqrt(Mathf.Lerp(radiusMin * radiusMin, radiusMax * radiusMax, t));

            float angle = phase + index * GoldenAngleRad;
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }
    }
}
