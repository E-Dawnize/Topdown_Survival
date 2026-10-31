using System;

namespace Game.Controllers
{
    /// <summary>
    /// 商店选格的导航：2 行 × 3 列，正好对上 <see cref="ShopController.SlotCount"/> 的 6 格。
    ///
    /// 选 2×3 而不是 1×6：面板是 16:9 的横向卡片区，6 张 360 宽的卡片一字排开要把每张压到 300 以内，
    /// 描述文字放不下；2 行 3 列每张卡片还能保持可读的宽高比，而且上下键才有意义（1×6 时上下键只能空操作）。
    ///
    /// 纯 C#：不碰 MonoBehaviour、不读输入、不画东西。所以"能不能买"是从构造时给的判据问出来的，
    /// 视图决定哪些格子可买（空格 / 金币不够 / 叠满），这里只管几何与选择。
    /// 因为 6 格的名字、价格、描述始终全画在卡片上，跳过买不起的格子不会藏起任何信息，
    /// 只是让 Enter 按下去永远有反应。
    ///
    /// 用法：先 <see cref="Refresh"/> 问一遍判据并定下初始选择，之后 Move / Select 只读缓存。
    /// 模型一变（买完、刷新、金币变化）就再 Refresh 一次。
    /// </summary>
    public sealed class ShopNavigator
    {
        public const int Columns = 3;
        public const int Rows = 2;
        public const int SlotCount = Rows * Columns;

        readonly Func<int, bool> _isSelectable;

        /// <summary>可买性的缓存。Refresh 时问一遍判据，Move 只读缓存：走一格不该去碰模型两次。</summary>
        readonly bool[] _selectable = new bool[SlotCount];

        /// <summary>当前选中格。−1 表示一格都没得选（全买完 / 全都买不起）。</summary>
        int _selected = -1;

        public ShopNavigator(Func<int, bool> isSelectable)
        {
            _isSelectable = isSelectable ?? throw new ArgumentNullException(nameof(isSelectable));
        }

        public int Selected => _selected;
        public bool HasSelection => _selected >= 0;

        public static int RowOf(int index) => index / Columns;
        public static int ColumnOf(int index) => index % Columns;

        public bool IsSelectable(int index)
        {
            return index >= 0 && index < SlotCount && _selectable[index];
        }

        /// <summary>
        /// 重新问一遍每格能不能买，然后修选择：
        ///   - 选中的格子还能买：不动（挑东西的时候不该被别处的变化挤走）
        ///   - 不能买了（刚买走、刷新后买不起、叠满）：从原下标往后循环找第一格可买的。
        ///     买完手上这一格自然顺延到下一格，比"每次都跳回第一格"少按几次方向键。
        /// 返回是否有选中的格子。
        /// </summary>
        public bool Refresh()
        {
            for (int i = 0; i < SlotCount; i++) _selectable[i] = _isSelectable(i);

            if (IsSelectable(_selected)) return true;

            _selected = NextSelectable(_selected < 0 ? 0 : _selected);
            return _selected >= 0;
        }

        /// <summary>
        /// 往 (rowDelta, columnDelta) 方向走一格。方向上最近的一格不可买就继续沿同一方向走，
        /// 走到边界为止；出了边界、或整条线上都不可买，就原地不动并返回 false。
        /// 只支持单轴（一次只上行/下行或只左/右）：斜着推摇杆时由调用方挑主轴。
        /// </summary>
        public bool Move(int rowDelta, int columnDelta)
        {
            if (_selected < 0) return false;

            int row = RowOf(_selected) + rowDelta;
            int column = ColumnOf(_selected) + columnDelta;

            while (row >= 0 && row < Rows && column >= 0 && column < Columns)
            {
                int index = row * Columns + column;
                if (IsSelectable(index))
                {
                    _selected = index;
                    return true;
                }

                row += rowDelta;
                column += columnDelta;
            }

            return false;
        }

        /// <summary>
        /// 直接选某一格。不可买时返回 false 且不改选择。
        /// 单测用它把选择摆到指定的格子（边界用例不必先走一串 Move 才能到边上）；
        /// 商店界面目前只走 Move，因为全程键盘。
        /// </summary>
        public bool Select(int index)
        {
            if (!IsSelectable(index)) return false;

            _selected = index;
            return true;
        }

        int NextSelectable(int from)
        {
            for (int offset = 0; offset < SlotCount; offset++)
            {
                int index = (from + offset) % SlotCount;
                if (_selectable[index]) return index;
            }

            return -1;
        }
    }
}
