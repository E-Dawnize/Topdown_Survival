using Game.Config;
using Game.Systems;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 旅伴在画面上的固定外观：几何尺寸与阵营颜色。
    ///
    /// 单独一个文件，是因为"旅伴长什么样"里有一部分是纯数据：不建 GameObject 也能问
    /// （见 CompanionViewStyleTests）。CompanionView / CompanionViewPool 是 MonoBehaviour 与
    /// CreatePrimitive，只能在 Unity 里看；颜色与尺寸放这里就不会跟着一起变成"没法验"。
    ///
    /// 与玩家一眼分开靠三条，选的是"尺寸 + 阵营色"这两条为主：
    ///   1. 尺寸：旅伴是 0.7 米宽、0.6 米高、站在地面上的矮胶囊；
    ///      玩家是 1 米宽、2 米高的胶囊（Player 预制体 scale 1 的胶囊体，原点在几何中心）。
    ///      高度只有玩家的三成，敌群围上来时也不会和玩家混在一起。
    ///   2. 颜色：旅伴按阵营上色（四阵营四色，见 TintOf）；玩家身体是接近白的浅灰
    ///      （MAT_Player 0.85/0.87/0.92）外加一圈橙色描边壳（PlayerOutline）。同一个阵营的两个旅伴同色，
    ///      这是有意的：队伍里几个阵营一眼就能读出来，而羁绊正是按阵营数触发。
    ///   3. 形状：三个敌人预制体都是缩放 0.7 的方块，旅伴是胶囊，所以也不会被当成怪。
    /// </summary>
    public static class CompanionViewStyle
    {
        /// <summary>
        /// 身体尺寸（本地缩放）。Unity 的胶囊基准是 1 米直径、2 米高，所以 x/z 缩放就是直径、y 缩放是"半高"。
        /// 直径取 CompanionSystem.BodyRadius × 2：两两松解用的身体与画出来的身体必须是同一个数，
        /// 否则会出现"看着还隔着一段却已经互相让位"或者反过来的别扭感。
        /// </summary>
        public static readonly Vector3 BodyScale =
            new Vector3(CompanionSystem.BodyRadius * 2f, 0.3f, CompanionSystem.BodyRadius * 2f);

        /// <summary>
        /// 身体中心离地高度。胶囊网格的原点在几何中心，抬到半高才脚贴地
        /// （地面在 y = 0，敌人预制体也是这么摆的：0.7 的方块放在 y = 0.35）。
        /// </summary>
        public const float BodyCenterHeight = 0.3f;

        /// <summary>
        /// 阵营色。四色都取高饱和：玩家的身体是接近白的浅灰，敌人的颜色是 0.55 饱和度的杂色，
        /// 旅伴用饱和的纯色才在两边都跳得出来。0 是 None（不成羁绊），给一个中性灰。
        /// </summary>
        public static Color TintOf(CompanionFaction faction)
        {
            switch (faction)
            {
                case CompanionFaction.Machine: return new Color(0.30f, 0.55f, 0.95f);   // 机械：冷蓝
                case CompanionFaction.Ghost: return new Color(0.62f, 0.40f, 0.95f);     // 幽灵：青紫
                case CompanionFaction.Beast: return new Color(0.55f, 0.85f, 0.30f);     // 野兽：黄绿
                case CompanionFaction.Doll: return new Color(1f, 0.45f, 0.62f);         // 玩偶：粉
                default: return new Color(0.70f, 0.72f, 0.76f);                        // 无阵营：中性灰
            }
        }
    }
}
