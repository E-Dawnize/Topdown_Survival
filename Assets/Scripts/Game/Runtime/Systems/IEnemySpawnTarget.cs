using Game.Config;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 波次系统刷怪的出口。EnemySystem 直接实现它，生产路径上没有额外的适配对象。
    ///
    /// 单独抽出这一层是为了把"同屏上限"和"刷怪节奏"从敌人的数据模型里分离开：
    /// 验 ConcurrentCap、刷怪间隔与'整局零分配'时，不必真的建 EnemyModel，
    /// 测出来的就是波次循环自己的开销，而不是模型层的既成分配。
    /// </summary>
    public interface IEnemySpawnTarget
    {
        /// <summary>同屏存活数。波次系统靠它守 ConcurrentCap。</summary>
        int AliveCount { get; }

        /// <summary>生成一个敌人，返回它的槽位号。位置由调用方给，这里不做钳制与修正。</summary>
        int Spawn(EnemyDef def, Vector2 position);
    }
}
