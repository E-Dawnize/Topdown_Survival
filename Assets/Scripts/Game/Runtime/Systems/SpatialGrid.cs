using System.Collections.Generic;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// 重建时读单位位置的唯一入口。位置只存在 Model 里，格子里再存一份会在移动后失同步，
    /// 所以每次 Rebuild 向这里读一次、连同快照一起留给本轮的查询用。
    /// </summary>
    public interface ISpatialGridSource
    {
        Vector2 PositionOf(int unitIndex);
    }

    /// <summary>
    /// 均匀网格空间索引。格子边长 2 米，32×32 覆盖 64 米，以传入的中心滚动。
    /// 三条语义（调用方必须按这个用）：
    /// 1. 槽位（= 单位索引）由 Allocate 分配。一经分配就不再变化：别的单位离开不会让任何槽位号前移，
    ///    只做格内列表的末尾交换。Remove 之后槽位进空闲栈，可能被下一个单位复用。
    /// 2. Rebuild 只重排"槽位 → 格子"的对应关系，不改变任何槽位号。滚动（换中心）就是整表重建，
    ///    没有增量更新——200 单位重建在 0.1ms 量级，比维护边界迁移省事也省错。
    /// 3. 查询返回的是上一次 Rebuild 时的位置快照。主循环里重建在移动之后、查询发生在下一 tick，
    ///    这样同一 tick 内所有消费者看到同一份位置，不会因为谁先查询而结果不同。
    /// 超出 ±32 米的单位不进表：查询半径本来不会超过这个范围，进表只会白白撑大格子。
    /// </summary>
    public sealed class SpatialGrid
    {
        public const float CellSize = 2f;
        public const int CellsPerSide = 32;

        /// <summary>半边长：32 格 × 2 米 = 覆盖 64 米，中心到边缘 32 米。</summary>
        public const float HalfCoverage = CellSize * CellsPerSide * 0.5f;

        const int CellCount = CellsPerSide * CellsPerSide;

        // 每格预分配 8：200 单位 / 1024 格的密度下绝大多数格子是空的或只放一两个，
        // 8 足够覆盖局部聚集，扩容只发生在极端堆叠时。
        const int InitialCellCapacity = 8;

        readonly List<int>[] _cells = new List<int>[CellCount];
        readonly List<int> _live = new List<int>(256);     // 稠密存活槽位：Rebuild 只遍历它，不扫空槽
        readonly List<int> _free = new List<int>(64);      // 空闲槽位栈

        // 每个槽位的簿记。用 List 而不是数组，省掉四处手写扩容；下标就是槽位号。
        readonly List<Vector2> _snapshot = new List<Vector2>(256);
        readonly List<int> _denseIndexOf = new List<int>(256);
        readonly List<int> _cellOf = new List<int>(256);
        readonly List<int> _cellSlotPos = new List<int>(256);
        readonly List<bool> _allocated = new List<bool>(256);

        Vector2 _origin;

        public SpatialGrid()
        {
            for (int i = 0; i < CellCount; i++)
            {
                _cells[i] = new List<int>(InitialCellCapacity);
            }
        }

        /// <summary>存活的单位数（已 Allocate 且未 Remove）。</summary>
        public int Count => _live.Count;

        /// <summary>网格左下角。测试与调试画格线用；滚动后跟着中心一起变。</summary>
        public Vector2 Origin => _origin;

        /// <summary>分配一个槽位（= 单位索引）。优先复用空闲槽，保证长时间跑不会无限长大。</summary>
        public int Allocate()
        {
            int slot;
            if (_free.Count > 0)
            {
                slot = _free[_free.Count - 1];
                _free.RemoveAt(_free.Count - 1);
            }
            else
            {
                slot = _snapshot.Count;
                _snapshot.Add(Vector2.zero);
                _denseIndexOf.Add(-1);
                _cellOf.Add(-1);
                _cellSlotPos.Add(-1);
                _allocated.Add(false);
            }

            _allocated[slot] = true;
            _denseIndexOf[slot] = _live.Count;
            _live.Add(slot);
            return slot;
        }

        /// <summary>
        /// 释放槽位。O(1)：格内列表末尾交换 + 稠密表末尾交换，其他单位的槽位号一个都不动。
        /// 重复释放或释放未分配的槽位是空操作，不抛异常——清理阶段连着调不会炸。
        /// </summary>
        public void Release(int slot)
        {
            if (slot < 0 || slot >= _allocated.Count || !_allocated[slot]) return;

            int cell = _cellOf[slot];
            if (cell >= 0) RemoveFromCell(cell, slot);

            int denseIndex = _denseIndexOf[slot];
            int lastSlot = _live[_live.Count - 1];
            _live[denseIndex] = lastSlot;
            _denseIndexOf[lastSlot] = denseIndex;
            _live.RemoveAt(_live.Count - 1);

            _allocated[slot] = false;
            _denseIndexOf[slot] = -1;
            _free.Add(slot);
        }

        /// <summary>槽位是否还活着。查询结果里的槽位可能在两次 Rebuild 之间已经被移除，用前要问一下。</summary>
        public bool IsAllocated(int slot)
        {
            return slot >= 0 && slot < _allocated.Count && _allocated[slot];
        }

        /// <summary>上一次 Rebuild 时记录的位置。只对已分配且上过表的槽位有意义。</summary>
        public Vector2 SnapshotPosition(int slot) => _snapshot[slot];

        /// <summary>
        /// 以 center 为网格中心整表重建。滚动就是调用它换一个 center：旧格子内容全部清掉，
        /// 存活槽位按新位置重新落格。槽位号不变，所以外部按槽位存的数组不用跟着搬。
        /// </summary>
        public void Rebuild(Vector2 center, ISpatialGridSource source)
        {
            _origin = center - new Vector2(HalfCoverage, HalfCoverage);

            // 1024 个格子逐个判空再 Clear：比维护"非空格子表"便宜，也不会漏清
            for (int i = 0; i < CellCount; i++)
            {
                List<int> cell = _cells[i];
                if (cell.Count > 0) cell.Clear();
            }

            for (int i = 0; i < _live.Count; i++)
            {
                int slot = _live[i];
                Vector2 position = source.PositionOf(slot);
                _snapshot[slot] = position;

                int cell = CellIndexOf(position);
                _cellOf[slot] = cell;
                if (cell < 0)
                {
                    _cellSlotPos[slot] = -1;
                    continue;
                }

                List<int> list = _cells[cell];
                _cellSlotPos[slot] = list.Count;
                list.Add(slot);
            }
        }

        /// <summary>
        /// 查询圆内的单位，结果追加进 results（不清空，返回本次追加的数量）。
        /// 容器由调用方持有并复用：这条路径每 tick 会被调几百次，自己 new 就是稳定 GC。
        /// 距离用 Rebuild 时的快照算，所以格内列表命中后还要再做一次精确距离过滤——
        /// 格子是方的，圆查询必然会多带上角落里的单位。
        /// </summary>
        public int QueryCircle(Vector2 center, float radius, List<int> results)
        {
            if (results == null || radius < 0f) return 0;

            int minX = CellX(center.x - radius);
            int maxX = CellX(center.x + radius);
            int minY = CellY(center.y - radius);
            int maxY = CellY(center.y + radius);

            if (maxX < 0 || maxY < 0 || minX >= CellsPerSide || minY >= CellsPerSide) return 0;
            if (minX < 0) minX = 0;
            if (minY < 0) minY = 0;
            if (maxX >= CellsPerSide) maxX = CellsPerSide - 1;
            if (maxY >= CellsPerSide) maxY = CellsPerSide - 1;

            float sqrRadius = radius * radius;
            int found = 0;
            for (int y = minY; y <= maxY; y++)
            {
                int row = y * CellsPerSide;
                for (int x = minX; x <= maxX; x++)
                {
                    List<int> list = _cells[row + x];
                    for (int i = 0; i < list.Count; i++)
                    {
                        int slot = list[i];
                        if ((_snapshot[slot] - center).sqrMagnitude > sqrRadius) continue;
                        results.Add(slot);
                        found++;
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// 查询 maxRadius 内离 center 最近的一个单位，没有返回 -1。
        /// 按环向外扫并在"更外圈不可能更近"时提前停：索敌每 tick 都调，不需要把外圈扫满。
        /// </summary>
        public int QueryNearest(Vector2 center, float maxRadius)
        {
            if (_live.Count == 0 || maxRadius < 0f) return -1;

            int centerX = CellX(center.x);
            int centerY = CellY(center.y);

            int best = -1;
            float bestSqr = maxRadius * maxRadius;

            int maxRing = Mathf.CeilToInt(maxRadius / CellSize) + 1;
            if (maxRing > CellsPerSide) maxRing = CellsPerSide;

            for (int ring = 0; ring <= maxRing; ring++)
            {
                // 第 ring 环的格子离中心最近也有 (ring-1)×CellSize，比这个还远就不用再往外看
                if (best >= 0)
                {
                    float ringMin = (ring - 1) * CellSize;
                    if (ringMin * ringMin > bestSqr) break;
                }
                ScanRing(centerX, centerY, ring, center, ref best, ref bestSqr);
            }
            return best;
        }

        void ScanRing(int centerX, int centerY, int ring, Vector2 center, ref int best, ref float bestSqr)
        {
            if (ring == 0)
            {
                ScanCell(centerX, centerY, center, ref best, ref bestSqr);
                return;
            }

            int minX = centerX - ring;
            int maxX = centerX + ring;
            int minY = centerY - ring;
            int maxY = centerY + ring;

            for (int x = minX; x <= maxX; x++)
            {
                ScanCell(x, minY, center, ref best, ref bestSqr);
                ScanCell(x, maxY, center, ref best, ref bestSqr);
            }
            for (int y = minY + 1; y < maxY; y++)
            {
                ScanCell(minX, y, center, ref best, ref bestSqr);
                ScanCell(maxX, y, center, ref best, ref bestSqr);
            }
        }

        void ScanCell(int x, int y, Vector2 center, ref int best, ref float bestSqr)
        {
            if (x < 0 || x >= CellsPerSide || y < 0 || y >= CellsPerSide) return;

            List<int> list = _cells[y * CellsPerSide + x];
            for (int i = 0; i < list.Count; i++)
            {
                int slot = list[i];
                float sqr = (_snapshot[slot] - center).sqrMagnitude;
                if (best < 0)
                {
                    if (sqr <= bestSqr)
                    {
                        best = slot;
                        bestSqr = sqr;
                    }
                }
                else if (sqr < bestSqr)   // 等距时保留先扫到的那个，结果不随遍历顺序变来变去
                {
                    best = slot;
                    bestSqr = sqr;
                }
            }
        }

        void RemoveFromCell(int cell, int slot)
        {
            List<int> list = _cells[cell];
            int position = _cellSlotPos[slot];
            int lastSlot = list[list.Count - 1];

            // 末尾交换：把列表最后一个搬到槽位原来的位置上，只改两个单位的簿记，O(1)
            list[position] = lastSlot;
            _cellSlotPos[lastSlot] = position;
            list.RemoveAt(list.Count - 1);

            _cellOf[slot] = -1;
            _cellSlotPos[slot] = -1;
        }

        int CellIndexOf(Vector2 position)
        {
            int x = CellX(position.x);
            int y = CellY(position.y);
            if (x < 0 || x >= CellsPerSide || y < 0 || y >= CellsPerSide) return -1;
            return y * CellsPerSide + x;
        }

        int CellX(float worldX) => Mathf.FloorToInt((worldX - _origin.x) / CellSize);

        int CellY(float worldY) => Mathf.FloorToInt((worldY - _origin.y) / CellSize);
    }
}
