using System.Collections.Generic;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public class SpatialGridTests
    {
        /// <summary>格子只认接口给的位置，测试里用一张表假装是某个系统。</summary>
        sealed class FakeSource : ISpatialGridSource
        {
            readonly List<Vector2> _positions = new List<Vector2>();

            public void Set(int slot, Vector2 position)
            {
                while (_positions.Count <= slot) _positions.Add(Vector2.zero);
                _positions[slot] = position;
            }

            public Vector2 PositionOf(int unitIndex) => _positions[unitIndex];
        }

        static int Place(SpatialGrid grid, FakeSource source, Vector2 position)
        {
            int slot = grid.Allocate();
            source.Set(slot, position);
            return slot;
        }

        // 格子边界由 Origin + CellSize 决定；测试里自己算一遍，避免和实现的内部约定猜来猜去
        static int CellXOf(SpatialGrid grid, float x)
        {
            return Mathf.FloorToInt((x - grid.Origin.x) / SpatialGrid.CellSize);
        }

        [Test]
        public void QueryCircleFindsUnitInsideRadius()
        {
            var grid = new SpatialGrid();
            var source = new FakeSource();
            int slot = Place(grid, source, new Vector2(1f, 1f));
            grid.Rebuild(Vector2.zero, source);

            var results = new List<int>();
            int found = grid.QueryCircle(Vector2.zero, 2f, results);

            Assert.AreEqual(1, found);
            CollectionAssert.AreEqual(new[] { slot }, results);
        }

        [Test]
        public void QueryCircleFindsUnitInNeighbourCell()
        {
            var grid = new SpatialGrid();
            var source = new FakeSource();
            Vector2 center = new Vector2(0.5f, 0.5f);

            // 距离 2.05 米、横向跨过一个格子边界：命中要么靠邻格、要么根本不命中，正好考跨格
            Vector2 target = new Vector2(2.55f, 0.5f);
            int slot = Place(grid, source, target);
            grid.Rebuild(center, source);

            Assert.AreNotEqual(CellXOf(grid, center.x), CellXOf(grid, target.x), "两个点必须落在不同的格子里，否则这条测试没意义");

            var results = new List<int>();
            int found = grid.QueryCircle(center, 2.1f, results);

            Assert.AreEqual(1, found);
            CollectionAssert.AreEqual(new[] { slot }, results);
        }

        [Test]
        public void QueryCircleRejectsUnitOutsideRadius()
        {
            var grid = new SpatialGrid();
            var source = new FakeSource();
            Place(grid, source, new Vector2(0.5f, 0.5f));
            Place(grid, source, new Vector2(1.9f, 0.5f));
            grid.Rebuild(Vector2.zero, source);

            var results = new List<int>();
            int found = grid.QueryCircle(new Vector2(0.5f, 0.5f), 0.2f, results);

            // 两个单位在同一个格子里，格内列表命中后还要靠精确距离剔掉 1.4 米外那个
            Assert.AreEqual(1, found);
            Assert.AreEqual(0.5f, source.PositionOf(results[0]).x, 1e-4f);
        }

        [Test]
        public void RemovedUnitIsNoLongerFound()
        {
            var grid = new SpatialGrid();
            var source = new FakeSource();
            int removed = Place(grid, source, new Vector2(0.5f, 0f));
            int kept = Place(grid, source, new Vector2(1f, 0f));
            grid.Rebuild(Vector2.zero, source);

            grid.Release(removed);

            var results = new List<int>();
            int found = grid.QueryCircle(Vector2.zero, 3f, results);

            Assert.AreEqual(1, found);
            CollectionAssert.AreEqual(new[] { kept }, results);
            Assert.AreEqual(1, grid.Count);
            Assert.IsFalse(grid.IsAllocated(removed));
        }

        [Test]
        public void RemovalKeepsOtherSlotIndices()
        {
            var grid = new SpatialGrid();
            var source = new FakeSource();
            int first = Place(grid, source, new Vector2(-1f, 0f));
            int second = Place(grid, source, new Vector2(0f, 0f));
            int third = Place(grid, source, new Vector2(1f, 0f));
            grid.Rebuild(Vector2.zero, source);

            // 移除中间的槽位会触发格内列表末尾交换，其余单位的槽位号必须原样保留
            grid.Release(second);

            Assert.AreEqual(first, grid.QueryNearest(new Vector2(-1f, 0f), 0.1f));
            Assert.AreEqual(third, grid.QueryNearest(new Vector2(1f, 0f), 0.1f));
            Assert.AreEqual(-1, grid.QueryNearest(new Vector2(0f, 0f), 0.1f), "被移除的槽位不能再被查到");

            var results = new List<int>();
            grid.QueryCircle(Vector2.zero, 5f, results);
            CollectionAssert.AreEquivalent(new[] { first, third }, results);
        }

        [Test]
        public void RollingRebuildFindsUnitsAtNewCenter()
        {
            var grid = new SpatialGrid();
            var source = new FakeSource();
            int slot = Place(grid, source, Vector2.zero);

            grid.Rebuild(Vector2.zero, source);
            var results = new List<int>();
            Assert.AreEqual(1, grid.QueryCircle(Vector2.zero, 3f, results), "滚动之前应当在原点查到");

            // 玩家跑到 40 米外：格子整体跟着滚，单位也要按新位置重新落格
            Vector2 newCenter = new Vector2(40f, 40f);
            source.Set(slot, newCenter + new Vector2(1f, 0f));
            grid.Rebuild(newCenter, source);

            results.Clear();
            Assert.AreEqual(1, grid.QueryCircle(newCenter, 3f, results), "滚动之后应当在新中心查到");
            Assert.AreEqual(slot, results[0], "滚动不改变槽位号");

            results.Clear();
            Assert.AreEqual(0, grid.QueryCircle(Vector2.zero, 3f, results), "旧位置已经不在表里");
        }

        [Test]
        public void QueryNearestReturnsMinusOneOnEmptyGrid()
        {
            var grid = new SpatialGrid();

            Assert.AreEqual(-1, grid.QueryNearest(Vector2.zero, 10f));
        }

        [Test]
        public void QueryNearestReturnsClosestOfSeveral()
        {
            var grid = new SpatialGrid();
            var source = new FakeSource();
            Place(grid, source, new Vector2(1.2f, 0f));
            int closest = Place(grid, source, new Vector2(0.4f, 0.3f));   // 0.5 米
            Place(grid, source, new Vector2(-2.5f, 0f));
            grid.Rebuild(Vector2.zero, source);

            Assert.AreEqual(closest, grid.QueryNearest(Vector2.zero, 4f));
        }

        [Test]
        public void QueryNearestRespectsMaxRadius()
        {
            var grid = new SpatialGrid();
            var source = new FakeSource();
            Place(grid, source, new Vector2(2f, 0f));
            grid.Rebuild(Vector2.zero, source);

            Assert.AreEqual(-1, grid.QueryNearest(Vector2.zero, 1.5f), "最近的也在 1.5 米外，应当是 -1");
            Assert.AreEqual(0, grid.QueryNearest(Vector2.zero, 2.5f));
        }
    }
}
