using Game.Controllers;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 商店选格导航（I2-5）。纯逻辑，不建 GameObject、不进 Play：
    /// 可买性用一条 bool 数组代替真实商店，验的是几何（2 行 3 列）、边界、跳过不可买与空状态。
    /// 网格下标一律按 row * Columns + column 排，所以 "110011" 的第 2 位就是第一行第三列。
    /// </summary>
    public class ShopNavigatorTests
    {
        /// <summary>可买性判据。用可变数组而不是字符串：同一用例里能"买走一格"再观察选择往哪走。</summary>
        sealed class Mask
        {
            public readonly bool[] Cells = new bool[ShopNavigator.SlotCount];

            public Mask(string pattern)
            {
                Assert.AreEqual(ShopNavigator.SlotCount, pattern.Length, "掩码必须正好 6 位");

                for (int i = 0; i < pattern.Length; i++)
                {
                    Cells[i] = pattern[i] == '1';
                }
            }

            public bool IsSelectable(int index)
            {
                return Cells[index];
            }
        }

        static ShopNavigator Build(Mask mask)
        {
            return new ShopNavigator(mask.IsSelectable);
        }

        [Test]
        public void LayoutIsTwoRowsOfThreeAndCoversEveryShopSlot()
        {
            Assert.AreEqual(3, ShopNavigator.Columns);
            Assert.AreEqual(2, ShopNavigator.Rows);
            Assert.AreEqual(ShopController.SlotCount, ShopNavigator.SlotCount, "网格格数必须等于商店格数");

            Assert.AreEqual(0, ShopNavigator.RowOf(0));
            Assert.AreEqual(0, ShopNavigator.ColumnOf(0));
            Assert.AreEqual(0, ShopNavigator.RowOf(2));
            Assert.AreEqual(2, ShopNavigator.ColumnOf(2));
            Assert.AreEqual(1, ShopNavigator.RowOf(3));
            Assert.AreEqual(0, ShopNavigator.ColumnOf(3));
            Assert.AreEqual(1, ShopNavigator.RowOf(5));
            Assert.AreEqual(2, ShopNavigator.ColumnOf(5));
        }

        [Test]
        public void NothingSelectedBeforeRefresh()
        {
            ShopNavigator nav = Build(new Mask("111111"));

            Assert.AreEqual(-1, nav.Selected, "还没问过可买性时没有选中格");
            Assert.IsFalse(nav.HasSelection);
        }

        [Test]
        public void RefreshSelectsFirstPurchasableSlot()
        {
            ShopNavigator nav = Build(new Mask("001000"));

            Assert.IsTrue(nav.Refresh());
            Assert.AreEqual(2, nav.Selected, "前两格不可买，第一格可买的在下标 2");
        }

        [Test]
        public void RefreshFindsNothingWhenEverySlotIsBought()
        {
            ShopNavigator nav = Build(new Mask("000000"));

            Assert.IsFalse(nav.Refresh(), "全买完（或全买不起）时没有可选的格子");
            Assert.AreEqual(-1, nav.Selected);
            Assert.IsFalse(nav.HasSelection);
            Assert.IsFalse(nav.Move(0, 1), "没有选中格时方向键不动，也不会抛异常");
            Assert.IsFalse(nav.Move(1, 0));
            Assert.IsFalse(nav.Select(0), "空格不能被直接选中");

            for (int i = 0; i < ShopNavigator.SlotCount; i++)
            {
                Assert.IsFalse(nav.IsSelectable(i));
            }
        }

        [Test]
        public void MoveRightSkipsUnpurchasableSlotsInTheSameRow()
        {
            ShopNavigator nav = Build(new Mask("101000"));
            nav.Refresh();
            Assert.AreEqual(0, nav.Selected);

            Assert.IsTrue(nav.Move(0, 1));
            Assert.AreEqual(2, nav.Selected, "下标 1 不可买，跳过它落到同一行最后一格");
        }

        [Test]
        public void MoveLeftSkipsUnpurchasableSlotsInTheSameRow()
        {
            ShopNavigator nav = Build(new Mask("101000"));
            nav.Refresh();
            nav.Select(2);

            Assert.IsTrue(nav.Move(0, -1));
            Assert.AreEqual(0, nav.Selected, "从左往右跳过后，反向也跳过同一格不可买的");
        }

        [Test]
        public void MoveUpAndDownStayInTheSameColumn()
        {
            ShopNavigator nav = Build(new Mask("111111"));
            nav.Refresh();
            Assert.AreEqual(0, nav.Selected);

            Assert.IsTrue(nav.Move(1, 0));
            Assert.AreEqual(3, nav.Selected, "第一行第一列往下是第二行第一列");
            Assert.IsTrue(nav.Move(-1, 0));
            Assert.AreEqual(0, nav.Selected);
        }

        [Test]
        public void MoveStopsAtRightEdgeWithoutWrappingToNextRow()
        {
            ShopNavigator nav = Build(new Mask("111111"));
            nav.Refresh();
            nav.Select(2);

            Assert.IsFalse(nav.Move(0, 1), "行末再往右没有格子：不绕到下一行");
            Assert.AreEqual(2, nav.Selected);
        }

        [Test]
        public void MoveStopsAtLeftAndTopAndBottomEdges()
        {
            ShopNavigator nav = Build(new Mask("111111"));
            nav.Refresh();

            Assert.IsFalse(nav.Move(0, -1), "第一列再往左没有格子");
            Assert.AreEqual(0, nav.Selected);

            Assert.IsFalse(nav.Move(-1, 0), "第一行再往上没有格子");
            Assert.AreEqual(0, nav.Selected);

            nav.Select(4);
            Assert.IsFalse(nav.Move(1, 0), "最后一行再往下没有格子");
            Assert.AreEqual(4, nav.Selected);
        }

        [Test]
        public void MoveStopsShortWhenTheWholeDirectionIsUnpurchasable()
        {
            ShopNavigator nav = Build(new Mask("110011"));
            nav.Refresh();
            nav.Select(1);

            Assert.IsFalse(nav.Move(0, 1), "第三列两格都不可买：原地不动，不要跳到别的行上去");
            Assert.AreEqual(1, nav.Selected);
        }

        [Test]
        public void MoveDownDoesNothingWhenTheRowBelowIsUnpurchasable()
        {
            ShopNavigator nav = Build(new Mask("100001"));
            nav.Refresh();
            nav.Select(0);

            Assert.IsFalse(nav.Move(1, 0), "同列只有两格，下一格不可买且再往下出界：原地不动");
            Assert.AreEqual(0, nav.Selected);
        }

        [Test]
        public void RefreshKeepsTheSelectionWhenItIsStillPurchasable()
        {
            var mask = new Mask("111111");
            ShopNavigator nav = Build(mask);
            nav.Refresh();
            nav.Select(4);

            mask.Cells[0] = false;   // 别处的格子变了，选中格没事
            mask.Cells[5] = false;

            Assert.IsTrue(nav.Refresh());
            Assert.AreEqual(4, nav.Selected, "挑东西的时候不该被别处的变化挤走");
        }

        [Test]
        public void RefreshMovesForwardAfterTheSelectedSlotIsBought()
        {
            var mask = new Mask("111111");
            ShopNavigator nav = Build(mask);
            nav.Refresh();
            Assert.AreEqual(0, nav.Selected);

            mask.Cells[0] = false;   // 买走了第一格
            nav.Refresh();

            Assert.AreEqual(1, nav.Selected, "买完顺延到下一格，不用重新找");
        }

        [Test]
        public void RefreshWrapsAroundWhenTheLastSlotIsBought()
        {
            var mask = new Mask("111111");
            ShopNavigator nav = Build(mask);
            nav.Refresh();
            nav.Select(5);

            mask.Cells[5] = false;
            nav.Refresh();

            Assert.AreEqual(0, nav.Selected, "最后一格买走后从头找第一格可买的");
        }

        [Test]
        public void SelectRejectsUnpurchasableSlotsAndKeepsTheOldSelection()
        {
            ShopNavigator nav = Build(new Mask("101010"));
            nav.Refresh();
            Assert.AreEqual(0, nav.Selected);

            Assert.IsFalse(nav.Select(1), "不可买的格子选不中");
            Assert.AreEqual(0, nav.Selected);
            Assert.IsTrue(nav.Select(2));
            Assert.AreEqual(2, nav.Selected);
        }
    }
}
