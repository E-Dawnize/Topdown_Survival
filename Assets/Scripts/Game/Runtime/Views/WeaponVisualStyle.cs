using UnityEngine;

namespace Game.Views
{
    /// <summary>武器挥动/后坐力的表现类型。只影响动画，不影响判定。</summary>
    public enum WeaponAttackStyle
    {
        Slash,
        DoubleSlash,
        Slam,
        BowDraw,
        CrossbowRecoil,
        OrbPulse
    }

    /// <summary>
    /// 武器表现层里与具体资源无关的纯数据：槽位、缩放、跟随参数。
    /// 模型/动作/时长在 WeaponVisualCatalog 资产里，这里不再维护 Id → 外形的映射，
    /// 避免"配置表加了武器、代码里忘了加 case"时静默退化成一把剑。
    /// </summary>
    public static class WeaponVisualStyle
    {
        // prefab 里的模型已经按米缩放到目标尺寸（剑约 1.2m、弓约 1.3m），
        // 这里的 Scale 只负责"玩家/旅伴再乘一档"，不再是几何体时代的 1.8 放大。
        public const float PlayerScale = 1.0f;
        public const float CompanionScale = 0.5f;

        /// <summary>武器追玩家时的 SmoothDamp 时间；比旅伴稍微硬一点，不能甩到身后太多。</summary>
        public const float FollowSmoothTime = 0.18f;

        /// <summary>玩家匀速移动时武器目标点落到身后的秒数，形成拖尾。</summary>
        public const float FollowTrailTime = 0.10f;

        /// <summary>冲刺时拖尾最多落后这么多米，避免武器被甩出屏幕。</summary>
        public const float FollowMaxTrail = 1.1f;

        /// <summary>
        /// 玩家转向时武器架追角度的 SmoothDamp 时间。玩家根节点是瞬时转的；
        /// 武器架如果不做这一层，整个武器排会跟着一起瞬移一圈。
        /// </summary>
        public const float FollowTurnSmoothTime = 0.16f;

        /// <summary>
        /// 玩家最多四把武器。按当前持有数量给固定槽位：一把在背后正中，两把在左右，
        /// 三/四把再加前方。所有槽位都落在身体半径（0.5 米）之外，避免模型插进胶囊。
        /// </summary>
        public static Vector3 PlayerSlot(int index, int count)
        {
            if (count <= 1) return new Vector3(0f, 1.20f, -0.82f);
            if (count == 2)
            {
                return index == 0
                    ? new Vector3(-0.82f, 1.08f, -0.30f)
                    : new Vector3(0.82f, 1.08f, -0.30f);
            }

            if (count == 3)
            {
                if (index == 0) return new Vector3(-0.82f, 1.08f, -0.30f);
                if (index == 1) return new Vector3(0.82f, 1.08f, -0.30f);
                return new Vector3(0.82f, 1.62f, 0.28f);
            }

            switch (index)
            {
                case 0: return new Vector3(-0.82f, 1.08f, -0.30f);
                case 1: return new Vector3(0.82f, 1.08f, -0.30f);
                case 2: return new Vector3(-0.82f, 1.62f, 0.28f);
                case 3: return new Vector3(0.82f, 1.62f, 0.28f);
            }

            // 配置上限是 4，但表里万一被改成 >4，不能让后面的武器全叠在同一个槽位上。
            int extra = index - 3;
            float ring = 0.82f + (extra / 4) * 0.35f;
            float angle = (extra % 4) * Mathf.PI * 0.5f;
            return new Vector3(Mathf.Cos(angle) * ring, 1.62f + (extra / 4) * 0.18f,
                Mathf.Sin(angle) * ring);
        }

        /// <summary>旅伴只有一把武器，放在身体右侧；旅伴根节点会再乘 0.7 的身体缩放。</summary>
        public static Vector3 CompanionSlot => new Vector3(0.45f, 0.25f, 0.02f);

        /// <summary>
        /// 武器在玩家本地空间里的跟随目标：固定槽位 + 反方向的移动拖尾。
        /// 武器跟着玩家根节点转，所以转向时不会出现世界锚点滞后导致的甩出/穿身；
        /// 只把速度拖尾从世界空间换回玩家本地空间。静止时就是槽位本身，冲刺时按 FollowMaxTrail 夹住。
        /// </summary>
        public static Vector3 FollowLocalTarget(Vector3 slot, Vector2 velocity, Quaternion playerRotation)
        {
            Vector3 trail = new Vector3(-velocity.x, 0f, -velocity.y) * FollowTrailTime;
            if (trail.sqrMagnitude > FollowMaxTrail * FollowMaxTrail)
            {
                trail = trail.normalized * FollowMaxTrail;
            }

            return slot + Quaternion.Inverse(playerRotation) * trail;
        }
    }
}
