using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WowAddonManager
{
    /// <summary>배틀넷 런처 톤 — 어두운 평면 배경, 파란 강조색, 둥근 모서리</summary>
    public static class Theme
    {
        public static readonly Color Window = Color.FromArgb(19, 21, 25);
        public static readonly Color TitleBar = Color.FromArgb(13, 15, 18);
        public static readonly Color Side = Color.FromArgb(15, 17, 21);
        public static readonly Color Panel = Color.FromArgb(18, 20, 25);
        public static readonly Color Line = Color.FromArgb(31, 35, 41);
        public static readonly Color Field = Color.FromArgb(12, 14, 18);
        public static readonly Color FieldLine = Color.FromArgb(34, 39, 49);
        public static readonly Color Ghost = Color.FromArgb(27, 31, 38);
        public static readonly Color GhostLine = Color.FromArgb(42, 48, 58);
        public static readonly Color GhostHover = Color.FromArgb(35, 40, 49);
        public static readonly Color RowSel = Color.FromArgb(27, 36, 48);
        public static readonly Color RowHover = Color.FromArgb(23, 26, 33);

        public static readonly Color Accent = Color.FromArgb(27, 133, 232);
        public static readonly Color AccentHover = Color.FromArgb(56, 152, 240);
        public static readonly Color AccentDown = Color.FromArgb(20, 108, 192);
        public static readonly Color AccentSoft = Color.FromArgb(31, 79, 134);
        public static readonly Color AccentLine = Color.FromArgb(45, 108, 168);
        public static readonly Color AccentText = Color.FromArgb(158, 208, 255);

        public static readonly Color Text = Color.FromArgb(230, 232, 236);
        public static readonly Color TextSoft = Color.FromArgb(185, 192, 203);
        public static readonly Color TextDim = Color.FromArgb(130, 139, 153);
        public static readonly Color TextMuted = Color.FromArgb(108, 116, 131);
        public static readonly Color TextOff = Color.FromArgb(78, 86, 99);

        public static readonly Color Ok = Color.FromArgb(63, 191, 106);
        public static readonly Color Danger = Color.FromArgb(222, 92, 92);

        public static Font F(float size, FontStyle style)
        {
            try { return new Font("맑은 고딕", size, style); }
            catch { return new Font(FontFamily.GenericSansSerif, size, style); }
        }

        public static Font Body { get { return F(9f, FontStyle.Regular); } }
        public static Font BodyBold { get { return F(9f, FontStyle.Bold); } }
        public static Font Small { get { return F(8.25f, FontStyle.Regular); } }
        public static Font Tiny { get { return F(7.5f, FontStyle.Bold); } }
        public static Font H1 { get { return F(13f, FontStyle.Bold); } }
        public static Font H2 { get { return F(10.5f, FontStyle.Bold); } }
        public static Font Mono { get { return new Font("Consolas", 8.5f); } }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (radius <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);
            g.SmoothingMode = old;
        }

        public static void DrawRound(Graphics g, Rectangle r, int radius, Color fill, Color line)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rr = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (GraphicsPath p = Round(rr, radius))
            {
                if (fill.A > 0) using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                if (line.A > 0) using (Pen pen = new Pen(line)) g.DrawPath(pen, p);
            }
            g.SmoothingMode = old;
        }
    }

    // ────────────────────────────────────────────────────────────
    public enum BtnKind { Ghost, Primary, Danger }

    public class FlatBtn : Button
    {
        public BtnKind Kind = BtnKind.Ghost;
        public int Radius = 6;
        bool hover, down;

        public FlatBtn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Panel;
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            Height = 32;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Rectangle r = new Rectangle(0, 0, Width, Height);

            Color fill, line, fg;
            if (!Enabled)
            {
                fill = Color.FromArgb(24, 27, 33); line = Color.FromArgb(34, 38, 46); fg = Theme.TextOff;
            }
            else if (Kind == BtnKind.Primary)
            {
                fill = down ? Theme.AccentDown : hover ? Theme.AccentHover : Theme.Accent;
                line = fill; fg = Color.White;
            }
            else if (Kind == BtnKind.Danger)
            {
                fill = down ? Color.FromArgb(132, 44, 44) : hover ? Color.FromArgb(186, 62, 62) : Color.FromArgb(162, 52, 52);
                line = fill; fg = Color.White;
            }
            else
            {
                fill = down ? Color.FromArgb(22, 25, 31) : hover ? Theme.GhostHover : Theme.Ghost;
                line = hover ? Color.FromArgb(58, 66, 78) : Theme.GhostLine;
                fg = hover ? Theme.Text : Theme.TextSoft;
            }

            Theme.DrawRound(g, r, Radius, fill, line);
            TextRenderer.DrawText(g, Text, Font, r, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    public enum SysKind { Min, Max, Restore, Close }

    /// <summary>창 단추 (최소화 / 최대화 / 복원 / 닫기)</summary>
    public class SysBtn : Control
    {
        public SysKind Kind = SysKind.Min;
        public bool IsClose { get { return Kind == SysKind.Close; } }
        bool hover;

        public SysBtn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(34, 28);
            BackColor = Theme.TitleBar;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (hover)
                Theme.FillRound(g, new Rectangle(0, 0, Width, Height), 5,
                    IsClose ? Color.FromArgb(196, 48, 58) : Color.FromArgb(28, 32, 39));

            Color fg = hover ? Color.White : Theme.TextDim;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = Width / 2, cy = Height / 2;
            using (Pen p = new Pen(fg, 1.4f))
            {
                if (Kind == SysKind.Close)
                {
                    g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5);
                    g.DrawLine(p, cx + 5, cy - 5, cx - 5, cy + 5);
                }
                else if (Kind == SysKind.Min)
                    g.DrawLine(p, cx - 5, cy, cx + 5, cy);
                else if (Kind == SysKind.Max)
                    g.DrawRectangle(p, cx - 5, cy - 5, 10, 10);
                else
                {
                    g.DrawRectangle(p, cx - 6, cy - 2, 8, 8);
                    g.DrawLines(p, new Point[] {
                        new Point(cx - 3, cy - 4), new Point(cx - 3, cy - 6),
                        new Point(cx + 6, cy - 6), new Point(cx + 6, cy + 3),
                        new Point(cx + 4, cy + 3) });
                }
            }
            g.SmoothingMode = SmoothingMode.Default;
        }
    }

    /// <summary>밑줄 탭 띠</summary>
    public class TabStrip : Control
    {
        public string[] Items = new string[0];
        public int Selected;
        int hoverIndex = -1;
        readonly List<Rectangle> hit = new List<Rectangle>();

        public event EventHandler SelectedChanged;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            Height = 40;
            Font = Theme.Body;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = -1;
            for (int i = 0; i < hit.Count; i++) if (hit[i].Contains(e.Location)) { idx = i; break; }
            if (idx != hoverIndex) { hoverIndex = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverIndex = -1; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            for (int i = 0; i < hit.Count; i++)
                if (hit[i].Contains(e.Location))
                {
                    if (Selected != i)
                    {
                        Selected = i;
                        Invalidate();
                        if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
                    }
                    break;
                }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            hit.Clear();

            int x = 18;
            for (int i = 0; i < Items.Length; i++)
            {
                bool sel = i == Selected;
                Font f = sel ? Theme.BodyBold : Font;
                Size sz = TextRenderer.MeasureText(g, Items[i], f);
                Rectangle area = new Rectangle(x, 0, sz.Width + 6, Height);
                hit.Add(area);

                Color fg = sel ? Color.White : i == hoverIndex ? Theme.TextSoft : Theme.TextDim;
                TextRenderer.DrawText(g, Items[i], f, area, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                if (sel)
                    using (SolidBrush b = new SolidBrush(Theme.Accent))
                        g.FillRectangle(b, area.X, Height - 2, area.Width, 2);

                x = area.Right + 20;
            }

            using (Pen p = new Pen(Theme.Line))
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    /// <summary>좌측 게임 버전 목록</summary>
    public class VersionList : ListBox
    {
        readonly HashSet<int> checkedSet = new HashSet<int>();
        int hoverIndex = -1;

        public event EventHandler CheckChanged;

        public VersionList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 52;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Side;
            ForeColor = Theme.Text;
            IntegralHeight = false;
            Font = Theme.Body;
        }

        public void AddFlavor(Flavor f, bool chk)
        {
            int i = Items.Add(f);
            if (chk) checkedSet.Add(i);
        }

        public void ClearItems() { Items.Clear(); checkedSet.Clear(); }

        public List<Flavor> CheckedFlavors()
        {
            List<Flavor> list = new List<Flavor>();
            for (int i = 0; i < Items.Count; i++)
                if (checkedSet.Contains(i)) list.Add((Flavor)Items[i]);
            return list;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = IndexFromPoint(e.Location);
            if (i >= 0 && i < Items.Count)
            {
                if (checkedSet.Contains(i)) checkedSet.Remove(i); else checkedSet.Add(i);
                Invalidate();
                if (CheckChanged != null) CheckChanged(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexFromPoint(e.Location);
            if (i != hoverIndex) { hoverIndex = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverIndex = -1; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Flavor f = Items[e.Index] as Flavor;
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool usable = f != null && f.AddOnCount > 0;
            bool chk = checkedSet.Contains(e.Index);
            Rectangle b = e.Bounds;

            using (SolidBrush br = new SolidBrush(Theme.Side)) g.FillRectangle(br, b);

            Rectangle card = new Rectangle(b.X + 6, b.Y + 2, b.Width - 12, b.Height - 4);
            if (sel)
            {
                Theme.FillRound(g, card, 7, Theme.RowSel);
                using (SolidBrush br = new SolidBrush(Theme.Accent))
                    g.FillRectangle(br, card.X, card.Y + 3, 2, card.Height - 6);
            }
            else if (e.Index == hoverIndex)
                Theme.FillRound(g, card, 7, Theme.RowHover);

            // 네모난 게임 아이콘
            Rectangle ico = new Rectangle(card.X + 10, card.Y + (card.Height - 32) / 2, 32, 32);
            Theme.DrawRound(g, ico, 7,
                sel ? Theme.AccentSoft : usable ? Color.FromArgb(39, 45, 56) : Color.FromArgb(33, 38, 46),
                sel ? Theme.AccentLine : usable ? Color.FromArgb(44, 51, 63) : Color.FromArgb(36, 42, 51));
            string initial = FriendlyInitial(f != null ? f.Name : "");
            TextRenderer.DrawText(g, initial, Theme.BodyBold, ico,
                sel ? Theme.AccentText : usable ? Theme.TextDim : Theme.TextOff,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // 체크 상자
            Rectangle box = new Rectangle(card.Right - 26, card.Y + (card.Height - 16) / 2, 16, 16);
            if (chk)
            {
                Theme.DrawRound(g, box, 4, Theme.Accent, Theme.Accent);
                using (Pen p = new Pen(Color.White, 1.8f))
                {
                    g.DrawLine(p, box.X + 4, box.Y + 8, box.X + 7, box.Y + 11);
                    g.DrawLine(p, box.X + 7, box.Y + 11, box.X + 12, box.Y + 5);
                }
            }
            else Theme.DrawRound(g, box, 4, Color.Transparent, Color.FromArgb(57, 65, 79));

            int tx = ico.Right + 10, tw = box.Left - tx - 8;
            TextRenderer.DrawText(g, FlavorName(f != null ? f.Name : ""), Theme.BodyBold,
                new Rectangle(tx, card.Y + 9, tw, 18),
                usable ? (sel ? Color.White : Theme.Text) : Theme.TextOff,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            string sub = f != null ? (usable ? "애드온 " + f.AddOnCount + "개" : "비어 있음") : "";
            TextRenderer.DrawText(g, sub, Theme.Small,
                new Rectangle(tx, card.Y + 26, tw, 16),
                usable ? Theme.TextMuted : Color.FromArgb(69, 76, 88),
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            g.SmoothingMode = SmoothingMode.Default;
        }

        public static string FlavorName(string flavor) { return VersionNames.Friendly(flavor); }

        static string FriendlyInitial(string flavor)
        {
            switch (flavor)
            {
                case "_retail_": return "W";
                case "_forever_": return "F";
                case "_anniversary_": return "A";
                case "_ptr_":
                case "_classic_ptr_": return "T";
                case "_beta_": return "B";
                default: return "C";
            }
        }
    }

    /// <summary>둥근 입력란</summary>
    public class Field : Panel
    {
        public TextBox Box = new TextBox();

        public Field()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 32;
            BackColor = Theme.Panel;

            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.Field;
            Box.ForeColor = Theme.TextSoft;
            Box.Font = Theme.Body;
            Box.Location = new Point(11, 8);
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Controls.Add(Box);
            Resize += delegate { Box.Width = Math.Max(20, Width - 22); };
        }

        public string Value { get { return Box.Text; } set { Box.Text = value; } }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, string lp);

        /// <summary>비어 있을 때 흐리게 보이는 안내 글 (윈도우 기본 기능을 쓴다)</summary>
        public void SetPlaceholder(string text)
        {
            const int EM_SETCUEBANNER = 0x1501;
            try { SendMessage(Box.Handle, EM_SETCUEBANNER, (IntPtr)1, text); }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Theme.DrawRound(e.Graphics, new Rectangle(0, 0, Width, Height), 6,
                Theme.Field, Box.Focused ? Theme.AccentLine : Theme.FieldLine);
            base.OnPaint(e);
        }
    }

    public class Check : CheckBox
    {
        bool hover;

        public Check()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.Body;
            ForeColor = Theme.Text;
            BackColor = Theme.Panel;
            Cursor = Cursors.Hand;
            AutoSize = false;
            Height = 24;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle box = new Rectangle(0, (Height - 16) / 2, 16, 16);

            if (Checked)
            {
                Theme.DrawRound(g, box, 4, Enabled ? Theme.Accent : Color.FromArgb(44, 52, 63),
                                Enabled ? Theme.Accent : Color.FromArgb(44, 52, 63));
                using (Pen p = new Pen(Enabled ? Color.White : Theme.TextOff, 1.8f))
                {
                    g.DrawLine(p, box.X + 4, box.Y + 8, box.X + 7, box.Y + 11);
                    g.DrawLine(p, box.X + 7, box.Y + 11, box.X + 12, box.Y + 5);
                }
            }
            else
                Theme.DrawRound(g, box, 4, Color.Transparent,
                    hover && Enabled ? Color.FromArgb(78, 88, 104) : Color.FromArgb(57, 65, 79));

            g.SmoothingMode = SmoothingMode.Default;
            Color c = !Enabled ? Theme.TextOff : ForeColor;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(25, 0, Width - 25, Height), c,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>가는 진행 막대</summary>
    public class Bar : Control
    {
        int val;
        public int Value
        {
            get { return val; }
            set { val = Math.Max(0, Math.Min(100, value)); Invalidate(); }
        }

        public Bar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 4;
            BackColor = Theme.TitleBar;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Theme.FillRound(g, new Rectangle(0, 0, Width, Height), 2, Color.FromArgb(27, 31, 38));
            if (val > 0)
            {
                int w = Math.Max(3, (int)(Width * (val / 100.0)));
                Theme.FillRound(g, new Rectangle(0, 0, w, Height), 2, Theme.Accent);
            }
        }
    }

    /// <summary>요약 카드 — 제목 한 줄 + 표 형태의 줄들</summary>
    public class SummaryCard : Control
    {
        public string Caption = "";
        public string Empty = "";
        public List<string[]> Rows = new List<string[]>();   // {왼쪽, 가운데, 오른쪽}
        public string FootLeft = "", FootRight = "";

        public SummaryCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
        }

        public int NeededHeight
        {
            get
            {
                int rows = Rows.Count > 0 ? Rows.Count : 1;
                return 38 + rows * 24 + (FootLeft.Length > 0 ? 30 : 8);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            Theme.DrawRound(g, r, 8, Color.FromArgb(21, 24, 30), Theme.Line);

            TextRenderer.DrawText(g, Caption, Theme.Tiny,
                new Rectangle(14, 12, Width - 28, 16), Theme.TextMuted, TextFormatFlags.Left);

            int y = 34;
            if (Rows.Count == 0)
            {
                TextRenderer.DrawText(g, Empty, Theme.Body,
                    new Rectangle(14, y, Width - 28, 22), Theme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return;
            }

            foreach (string[] row in Rows)
            {
                TextRenderer.DrawText(g, row[0], Theme.Body,
                    new Rectangle(14, y, Width - 240, 22), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, row[1], Theme.Body,
                    new Rectangle(Width - 230, y, 110, 22), Theme.TextDim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, row[2], Theme.Body,
                    new Rectangle(Width - 114, y, 100, 22), Theme.TextDim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                y += 24;
            }

            if (FootLeft.Length > 0)
            {
                using (Pen p = new Pen(Theme.Line))
                    g.DrawLine(p, 14, y + 3, Width - 14, y + 3);
                TextRenderer.DrawText(g, FootLeft, Theme.BodyBold,
                    new Rectangle(14, y + 6, Width - 150, 22), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, FootRight, Theme.BodyBold,
                    new Rectangle(Width - 134, y + 6, 120, 22), Theme.AccentText,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }
    }

    /// <summary>글자 링크</summary>
    public class LinkText : Label
    {
        public LinkText()
        {
            AutoSize = true;
            Font = Theme.Small;
            ForeColor = Theme.TextMuted;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { ForeColor = Theme.TextSoft; base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { ForeColor = Theme.TextMuted; base.OnMouseLeave(e); }
    }
}
