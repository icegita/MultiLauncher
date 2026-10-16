using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MultiLauncher;

// 弹出列表（自绘弹窗、整行命中、宽度钉回、屏幕取图那几张走的就是这一块）。
// 2026-10-15 从 UiSelect.cs 整段搬出，一行未改。
internal sealed partial class DarkSelect : Control
{
    // ---------------- 弹出列表 ----------------

    const int WS_EX_NOACTIVATE = 0x0800_0000;

    // 自检用：确认弹出窗确实不会抢主窗的激活（BUG-009 的根因就是这个）
    internal static bool PopupNeverActivatesOwner()
    {
        using var f = new DropForm(new[] { new DarkItem { Text = "x" } }, -1, 100, 100, 96);
        return f.NonActivating;
    }

    // 自检用：下拉列表的两条口径（用户反馈「点选项整行都应生效」）——
    //   ① 行内任意 Y（含行底/行顶附近）都能选中：整行命中；
    //   ② 尺寸被系统改过后，ReassertSize 能把窗口钉回请求值（否则"行右侧"会落在窗外）。
    internal static (bool hitTop, bool hitBottom, bool resizeBack) DropHitTestForTest()
    {
        using var f = new DropForm(new[] { new DarkItem { Text = "a" }, new DarkItem { Text = "b" } }, 0, 100, 200, 96);
        int picked = -1;
        f.Picked += i => picked = i;
        // dpi=96：pad=6，两项各 28 高 → 第一行 6..34、第二行 34..62
        bool hitTop = f.PickAtClientY(35) && picked == 1;
        picked = -1;
        bool hitBottom = f.PickAtClientY(60) && picked == 1;
        var want = f.ClientSize;              // 构造后的请求尺寸（高度是 min(maxH, content)，别自己算）
        f.ClientSize = new Size(50, 50);
        f.ReassertSize();
        bool resizeBack = f.ClientSize == want;
        return (hitTop, hitBottom, resizeBack);
    }

    sealed class DropForm : Form
    {
        public event Action<int>? Picked;
        public event Action? Cancelled;

        // 不抢主窗激活：这样展开/收起全程不动主窗的 z 序（老做法收起后主窗被露到后面，见 BUG-009）
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= WS_EX_NOACTIVATE; return cp; }
        }
        protected override bool ShowWithoutActivation => true;
        internal bool NonActivating => (CreateParams.ExStyle & WS_EX_NOACTIVATE) != 0 && ShowWithoutActivation;

        // 关键（用户实跑第五轮，真机复现后定位）：光有 WS_EX_NOACTIVATE + ShowWithoutActivation 拦不住
        // "点一下列表先把列表激活"——**WinForms 的 Form 自己在 WM_MOUSEACTIVATE 里会激活窗口**，压根不问
        // 扩展样式。这带来两级灾难：①激活让宿主窗 Deactivate，而 DarkSelect 的失活处理器当场收掉列表，
        // 这一击（LDOWN 还没派发到窗口）整个消失；②成功那次收起时 Focus() 又把激活还给宿主，于是下一次
        // 点列表必然触发激活转换 → 必败，失败那次激活留在别处 → 下一次反成。表现为**失败/成功严格交替**，
        // 正是用户说的"第一次选不中、只能第二次展开才选中"（沙箱真鼠标输入探针逐帧实证）。
        // 这里显式回 MA_NOACTIVATE(3)：窗口不激活，但点击照常送到列表上。
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0021 /* WM_MOUSEACTIVATE */) { m.Result = (IntPtr)3 /* MA_NOACTIVATE */; return; }
            base.WndProc(ref m);
        }

        readonly DarkItem[] _items;
        readonly int _itemH, _headH, _sepH, _pad;
        readonly Font _itemFont, _rightFont, _headFont;
        readonly Size _wantSize;
        int _selected, _hover = -1, _top;

        public DropForm(IReadOnlyList<DarkItem> items, int selected, int width, int maxHeight, int dpi)
        {
            _items = items.ToArray();
            _selected = selected;
            int S(int px) => (int)Math.Round(px * dpi / 96.0);
            _itemH = S(28); _headH = S(20); _sepH = S(9); _pad = S(6);
            _itemFont = new Font("Microsoft YaHei UI", 9f);
            _rightFont = new Font("Microsoft YaHei UI", 8.5f);
            _headFont = new Font("Microsoft YaHei UI", 8.5f);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = UiTheme.Card;
            Font = new Font("Microsoft YaHei UI", 9f);
            // 必须 None：默认的 Font 自动缩放会在显示时把窗体再缩一次
            //（实测 829px 的弹出窗显示后变 553px，右侧那列尺寸文字被画到窗口外、看起来"没画"）。
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            int content = _pad * 2;
            foreach (var it in _items) content += HeightOf(it);
            _wantSize = new Size(width, Math.Min(maxHeight, content));
            ClientSize = _wantSize;

            // 让当前选中项可见
            if (_selected >= 0)
            {
                int y = _pad;
                for (int i = 0; i < _items.Length && i != _selected; i++) y += HeightOf(_items[i]);
                int listH = ClientSize.Height;
                if (y + HeightOf(_items[_selected]) > listH) _top = y + HeightOf(_items[_selected]) - listH + _pad;
            }
        }

        int HeightOf(DarkItem it) => it.Kind switch
        {
            DarkItemKind.Header => _headH,
            DarkItemKind.Separator => _sepH,
            _ => _itemH,
        };

        int TotalHeight()
        {
            int h = _pad * 2;
            foreach (var it in _items) h += HeightOf(it);
            return h;
        }

        // 按键由 DarkSelect.ProcessCmdKey 转进来（焦点一直在收起框上）：接下了返回 true
        public bool HandleKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Cancelled?.Invoke(); return true;
                case Keys.Enter:
                    if (_hover >= 0 && _items[_hover].Kind == DarkItemKind.Normal) Picked?.Invoke(_hover);
                    else Cancelled?.Invoke();
                    return true;
                case Keys.Down: MoveSel(1); return true;
                case Keys.Up: MoveSel(-1); return true;
                case Keys.PageDown: MoveSel(6); return true;
                case Keys.PageUp: MoveSel(-6); return true;
            }
            return false;
        }

        void MoveSel(int dir)
        {
            int i = _hover >= 0 ? _hover : _selected;
            for (int n = 0; n < _items.Length; n++)
            {
                i += dir;
                if (i < 0) i = _items.Length - 1;
                if (i >= _items.Length) i = 0;
                if (_items[i].Kind == DarkItemKind.Normal && _items[i].Enabled) { _hover = i; EnsureVisible(i); Invalidate(); return; }
            }
        }

        void EnsureVisible(int index)
        {
            int y = _pad;
            for (int i = 0; i < index; i++) y += HeightOf(_items[i]);
            int h = HeightOf(_items[index]);
            if (y < _top) _top = y;
            else if (y + h > _top + ClientSize.Height) _top = y + h - ClientSize.Height;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = HitTest(e.Y);
            if (idx != _hover) { _hover = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);            // 先走基类（事件订阅者），再做选中——关窗是延迟的，顺序不再互相影响
            if (!PickAtClientY(e.Y)) Cancelled?.Invoke();
        }

        // 按客户区 Y 选中行（OnMouseDown 与"贴边容差"共用）。返回是否真的选中了一行（整行命中）。
        internal bool PickAtClientY(int y)
        {
            int idx = HitTest(y);
            if (idx >= 0 && _items[idx].Kind == DarkItemKind.Normal && _items[idx].Enabled)
            {
                Picked?.Invoke(idx);
                return true;
            }
            return false;
        }

        // 显示后系统/DPI 有可能把尺寸再改一道（BUG-007 的老毛病）：钉回请求值。
        // 不钉的话"行的右侧一段"会落在窗口之外——OutsideClickGuard 会把那一击当成"点外面"，
        // 表现就是「只能点左侧模型名、点整行不生效」（用户实跑反馈 2026-10-01）。
        internal void ReassertSize()
        {
            if (ClientSize == _wantSize) return;
            ClientSize = _wantSize;
        }

        protected override void OnMouseWheel(MouseEventArgs e) => ScrollBy(e.Delta);

        // 滚轮滚列表：系统「悬停滚动」开着时消息直接进本窗，否则由 DarkSelect.OnMouseWheel 转进来
        public void ScrollBy(int delta)
        {
            if (TotalHeight() <= ClientSize.Height) return;
            _top = Math.Max(0, Math.Min(TotalHeight() - ClientSize.Height, _top - Math.Sign(delta) * _itemH * 3));
            Invalidate();
        }

        int HitTest(int y)
        {
            int cur = _pad - _top;
            for (int i = 0; i < _items.Length; i++)
            {
                int h = HeightOf(_items[i]);
                if (y >= cur && y < cur + h) return y < _pad ? -1 : i;
                cur += h;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(UiTheme.Card);

            int cur = _pad - _top;
            int rowW = ClientSize.Width;      // 用 ClientSize 而不是 Width：显示后窗口可能被系统/DPI 再改一次宽度
            for (int i = 0; i < _items.Length; i++)
            {
                var it = _items[i];
                int h = HeightOf(it);
                var row = new Rectangle(0, cur, rowW, h);
                cur += h;
                if (row.Bottom < 0 || row.Top > ClientSize.Height) continue;

                if (it.Kind == DarkItemKind.Separator)
                {
                    int y = row.Top + row.Height / 2;
                    using var pen = new Pen(UiTheme.Border, 1f);
                    g.DrawLine(pen, _pad + 2, y, ClientSize.Width - _pad - 2, y);
                    continue;
                }
                if (it.Kind == DarkItemKind.Header)
                {
                    TextRenderer.DrawText(g, it.Text, _headFont, new Rectangle(_pad + 2, row.Top, row.Width - _pad * 2, row.Height),
                        UiTheme.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    continue;
                }

                bool isSel = i == _selected, isHover = i == _hover;
                if (isSel || isHover)
                {
                    var rr = new Rectangle(_pad - 2, row.Top + 1, rowW - (_pad - 2) * 2, row.Height - 2);
                    using var path = UiTheme.RoundRect(rr, (int)Math.Round(6 * DeviceDpi / 96.0));
                    using var brush = new SolidBrush(isHover ? UiTheme.Input : Color.FromArgb(0x1B, 0x3A, 0x2A));
                    g.FillPath(brush, path);
                    if (isSel)
                    {
                        using var pen = new Pen(UiTheme.Brand, 1f);
                        g.DrawPath(pen, path);
                    }
                }

                int padX = _pad + 6;
                var textRect = new Rectangle(padX, row.Top, rowW - padX * 2, row.Height);
                int usedR = DrawRightRuns(g, it, rowW - padX, row.Top, row.Height, _rightFont, _rightFont, UiTheme.S(this, 8));
                if (usedR > 0) textRect.Width = Math.Max(1, textRect.Width - usedR - UiTheme.S(this, 10));
                TextRenderer.DrawText(g, it.Text, _itemFont, textRect,
                    it.Enabled ? UiTheme.Text : UiTheme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
