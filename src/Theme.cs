using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WowAddonManager
{
    /// <summary>
    /// 와우 게임 UI 를 본뜬 테마.
    /// 어두운 청회색 패널 + 두꺼운 금색 장식 테두리 + 가운데 제목 명판 + 하단 탭.
    /// </summary>
    public static class Theme
    {
        // 패널 / 바탕
        public static readonly Color FrameTop = Color.FromArgb(44, 44, 54);
        public static readonly Color FrameBottom = Color.FromArgb(20, 20, 27);
        public static readonly Color Inset = Color.FromArgb(13, 13, 18);
        public static readonly Color InsetAlt = Color.FromArgb(24, 24, 32);
        public static readonly Color Hover = Color.FromArgb(44, 46, 62);

        // 금색 테두리 3단
        public static readonly Color GoldLight = Color.FromArgb(201, 170, 113);
        public static readonly Color GoldMid = Color.FromArgb(139, 117, 54);
        public static readonly Color GoldDarkLine = Color.FromArgb(74, 60, 30);
        public static readonly Color EdgeBlack = Color.FromArgb(6, 6, 9);

        // 글자
        public static readonly Color Gold = Color.FromArgb(255, 209, 110);
        public static readonly Color GoldBright = Color.FromArgb(255, 231, 160);
        public static readonly Color GoldDim = Color.FromArgb(178, 150, 98);
        public static readonly Color Text = Color.FromArgb(226, 224, 216);
        public static readonly Color TextDim = Color.FromArgb(154, 152, 146);
        public static readonly Color TextMuted = Color.FromArgb(108, 107, 104);

        // 강조
        public static readonly Color SelBlueTop = Color.FromArgb(38, 74, 126);
        public static readonly Color SelBlueBottom = Color.FromArgb(20, 40, 72);
        public static readonly Color SelEdge = Color.FromArgb(96, 158, 224);
        public static readonly Color Red = Color.FromArgb(166, 44, 44);
        public static readonly Color RedBright = Color.FromArgb(214, 72, 72);
        public static readonly Color Ok = Color.FromArgb(104, 196, 104);
        public static readonly Color Danger = Color.FromArgb(226, 96, 96);

        static FontFamily serif;

        /// <summary>와우 제목 느낌의 세리프 글꼴 (한글 포함)</summary>
        public static Font Serif(float size, FontStyle style)
        {
            if (serif == null)
            {
                foreach (string n in new string[] { "바탕", "Batang", "궁서", "Georgia", "Times New Roman" })
                {
                    try { serif = new FontFamily(n); break; }
                    catch { }
                }
                if (serif == null) serif = FontFamily.GenericSerif;
            }
            try { return new Font(serif, size, style); }
            catch { return new Font(FontFamily.GenericSerif, size, style); }
        }

        public static Font F(float size, FontStyle style)
        {
            try { return new Font("맑은 고딕", size, style); }
            catch { return new Font(FontFamily.GenericSansSerif, size, style); }
        }

        public static Font Body { get { return F(9f, FontStyle.Regular); } }
        public static Font BodyBold { get { return F(9f, FontStyle.Bold); } }
        public static Font Small { get { return F(8.25f, FontStyle.Regular); } }
        public static Font Mono { get { return new Font("Consolas", 8.5f); } }
        public static Font TitleFont { get { return Serif(12f, FontStyle.Bold); } }
        public static Font HeadFont { get { return Serif(13f, FontStyle.Bold); } }
        public static Font TabFont { get { return F(9f, FontStyle.Bold); } }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (radius <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillV(Graphics g, Rectangle r, Color top, Color bottom)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (LinearGradientBrush b = new LinearGradientBrush(
                       new Rectangle(r.X, r.Y, r.Width, r.Height + 1), top, bottom, 90f))
                g.FillRectangle(b, r);
        }

        /// <summary>창 전체를 감싸는 두꺼운 금색 장식 테두리</summary>
        public static void DrawOrnateFrame(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 바탕
            FillV(g, r, FrameTop, FrameBottom);

            // 바깥 검정 1px → 금색 2px → 안쪽 검정 1px (게임 창과 같은 얇은 3단)
            using (Pen p = new Pen(EdgeBlack))
                g.DrawRectangle(p, r);
            using (LinearGradientBrush lb = new LinearGradientBrush(
                       new Rectangle(r.X, r.Y, r.Width, r.Height + 1),
                       Color.FromArgb(186, 156, 98), Color.FromArgb(108, 88, 44), 90f))
            using (Pen p = new Pen(lb, 2f))
                g.DrawRectangle(p, r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
            using (Pen p = new Pen(EdgeBlack))
                g.DrawRectangle(p, r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8);

            // 네 귀퉁이 장식
            int c = 18;
            DrawCorner(g, new Rectangle(r.X + 1, r.Y + 1, c, c), 0);
            DrawCorner(g, new Rectangle(r.Right - c - 1, r.Y + 1, c, c), 90);
            DrawCorner(g, new Rectangle(r.Right - c - 1, r.Bottom - c - 1, c, c), 180);
            DrawCorner(g, new Rectangle(r.X + 1, r.Bottom - c - 1, c, c), 270);

            g.SmoothingMode = SmoothingMode.Default;
        }

        static void DrawCorner(Graphics g, Rectangle r, float rotate)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            g.RotateTransform(rotate);
            g.TranslateTransform(-r.Width / 2f, -r.Height / 2f);

            using (LinearGradientBrush b = new LinearGradientBrush(
                       new Rectangle(0, 0, r.Width, r.Height),
                       Color.FromArgb(214, 186, 128), Color.FromArgb(120, 98, 48), 45f))
            {
                g.FillPolygon(b, new Point[] {
                    new Point(0, 0), new Point(r.Width, 0),
                    new Point(r.Width - 7, 7), new Point(7, 7),
                    new Point(7, r.Height - 7), new Point(0, r.Height) });
            }
            using (Pen p = new Pen(EdgeBlack))
                g.DrawPolygon(p, new Point[] {
                    new Point(0, 0), new Point(r.Width, 0),
                    new Point(r.Width - 7, 7), new Point(7, 7),
                    new Point(7, r.Height - 7), new Point(0, r.Height) });

            g.Restore(st);
        }

        /// <summary>움푹 들어간 영역 (목록·입력란)</summary>
        public static void DrawInset(Graphics g, Rectangle r, bool focus)
        {
            FillV(g, r, Inset, Color.FromArgb(20, 20, 27));
            using (Pen p = new Pen(EdgeBlack)) g.DrawRectangle(p, r);
            using (Pen p = new Pen(focus ? GoldLight : GoldDarkLine))
                g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
        }

        /// <summary>가운데 제목 명판 — 창 폭을 가로지르는 가늘고 긴 판</summary>
        public static void DrawTitlePlate(Graphics g, Rectangle r, string text)
        {
            FillV(g, r, Color.FromArgb(34, 33, 30), Color.FromArgb(18, 17, 15));
            using (Pen p = new Pen(EdgeBlack)) g.DrawRectangle(p, r);
            using (Pen p = new Pen(GoldMid))
                g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            using (Pen p = new Pen(Color.FromArgb(60, 255, 226, 160)))
                g.DrawLine(p, r.X + 2, r.Y + 2, r.Right - 2, r.Y + 2);

            TextRenderer.DrawText(g, text, TitleFont, r, Gold,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        /// <summary>금색 테두리 원형 아이콘 자리</summary>
        public static void DrawPortraitRing(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(16, 16, 22)))
                g.FillEllipse(b, r);
            using (Pen p = new Pen(EdgeBlack, 3f)) g.DrawEllipse(p, r);
            using (LinearGradientBrush lb = new LinearGradientBrush(
                       r, Color.FromArgb(222, 196, 140), Color.FromArgb(118, 96, 46), 90f))
            using (Pen p = new Pen(lb, 3f))
                g.DrawEllipse(p, new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4));
            g.SmoothingMode = SmoothingMode.Default;
        }

        /// <summary>목록 행에 쓰는 원형 배지</summary>
        public static void DrawBadge(Graphics g, Rectangle r, string glyph, bool selected, bool dim)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (LinearGradientBrush b = new LinearGradientBrush(r,
                       selected ? Color.FromArgb(52, 90, 142) : Color.FromArgb(46, 46, 58),
                       selected ? Color.FromArgb(22, 44, 76) : Color.FromArgb(22, 22, 30), 90f))
                g.FillEllipse(b, r);
            using (Pen p = new Pen(EdgeBlack, 2f)) g.DrawEllipse(p, r);
            using (Pen p = new Pen(dim ? GoldDarkLine : selected ? GoldLight : GoldMid, 2f))
                g.DrawEllipse(p, new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2));

            TextRenderer.DrawText(g, glyph, F(11f, FontStyle.Bold), r,
                dim ? TextMuted : selected ? GoldBright : GoldDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            g.SmoothingMode = SmoothingMode.Default;
        }
    }

    // ────────────────────────────────────────────────────────────
    /// <summary>창 오른쪽 위 빨간 닫기 단추</summary>
    public class RedCloseButton : Control
    {
        bool hover;

        public RedCloseButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(26, 26);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);

            using (LinearGradientBrush b = new LinearGradientBrush(r,
                       hover ? Color.FromArgb(226, 86, 86) : Theme.RedBright,
                       hover ? Color.FromArgb(150, 32, 32) : Theme.Red, 90f))
                g.FillEllipse(b, r);
            using (Pen p = new Pen(Theme.EdgeBlack, 2f)) g.DrawEllipse(p, r);
            using (Pen p = new Pen(Color.FromArgb(240, 190, 190), 2f))
            {
                int m = 8;
                g.DrawLine(p, r.X + m, r.Y + m, r.Right - m, r.Bottom - m);
                g.DrawLine(p, r.Right - m, r.Y + m, r.X + m, r.Bottom - m);
            }
        }
    }

    /// <summary>창 최소화 단추 (작은 금색 테두리 원)</summary>
    public class MinButton : Control
    {
        bool hover;

        public MinButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(24, 24);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (LinearGradientBrush b = new LinearGradientBrush(r,
                       Color.FromArgb(56, 56, 68), Color.FromArgb(24, 24, 32), 90f))
                g.FillEllipse(b, r);
            using (Pen p = new Pen(Theme.EdgeBlack, 2f)) g.DrawEllipse(p, r);
            using (Pen p = new Pen(hover ? Theme.GoldLight : Theme.GoldMid, 2f))
                g.DrawEllipse(p, new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2));
            using (Pen p = new Pen(hover ? Theme.GoldBright : Theme.GoldDim, 2f))
                g.DrawLine(p, r.X + 7, r.Y + r.Height / 2, r.Right - 7, r.Y + r.Height / 2);
        }
    }

    public enum BtnKind { Normal, Important }

    /// <summary>와우식 단추 — 어두운 금속판 + 금색(또는 붉은) 테두리</summary>
    public class WowButton : Button
    {
        public BtnKind Kind = BtnKind.Normal;
        bool hover, down;

        public WowButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            Height = 30;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color t, b2, line, text;
            if (!Enabled)
            {
                t = Color.FromArgb(34, 34, 42); b2 = Color.FromArgb(24, 24, 30);
                line = Color.FromArgb(62, 60, 54); text = Theme.TextMuted;
            }
            else if (Kind == BtnKind.Important)
            {
                t = down ? Color.FromArgb(40, 30, 32) : hover ? Color.FromArgb(62, 44, 46) : Color.FromArgb(50, 36, 38);
                b2 = down ? Color.FromArgb(24, 18, 19) : hover ? Color.FromArgb(34, 24, 25) : Color.FromArgb(26, 19, 20);
                line = hover ? Theme.RedBright : Theme.Red;
                text = hover ? Color.FromArgb(255, 220, 220) : Color.FromArgb(240, 196, 196);
            }
            else
            {
                t = down ? Color.FromArgb(32, 32, 42) : hover ? Color.FromArgb(58, 58, 72) : Color.FromArgb(46, 46, 58);
                b2 = down ? Color.FromArgb(20, 20, 27) : hover ? Color.FromArgb(28, 28, 37) : Color.FromArgb(22, 22, 30);
                line = hover ? Theme.GoldLight : Theme.GoldMid;
                text = hover ? Theme.GoldBright : Theme.Gold;
            }

            Theme.FillV(g, r, t, b2);
            using (Pen p = new Pen(Theme.EdgeBlack)) g.DrawRectangle(p, r);
            using (Pen p = new Pen(line)) g.DrawRectangle(p, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);

            TextRenderer.DrawText(g, Text, Font, r, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>창 아래쪽 탭 (와우 창 하단 탭과 같은 모양)</summary>
    public class WowTab : Control
    {
        public bool Selected;
        bool hover;

        public WowTab()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Height = 30;
            Font = Theme.TabFont;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            using (GraphicsPath p = new GraphicsPath())
            {
                int d = 10;
                p.AddLine(r.X, r.Y, r.Right, r.Y);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();

                using (LinearGradientBrush b = new LinearGradientBrush(
                           new Rectangle(r.X, r.Y, r.Width, r.Height + 1),
                           Selected ? Color.FromArgb(60, 60, 74) : hover ? Color.FromArgb(44, 44, 56) : Color.FromArgb(32, 32, 41),
                           Selected ? Color.FromArgb(28, 28, 38) : Color.FromArgb(18, 18, 24), 90f))
                    g.FillPath(b, p);

                using (Pen pen = new Pen(Theme.EdgeBlack, 2f)) g.DrawPath(pen, p);
                using (Pen pen = new Pen(Selected ? Theme.GoldLight : Theme.GoldDarkLine,
                                         Selected ? 2f : 1f))
                    g.DrawPath(pen, p);
            }

            TextRenderer.DrawText(g, Text, Font, r,
                Selected ? Theme.GoldBright : hover ? Theme.Gold : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            g.SmoothingMode = SmoothingMode.Default;
        }
    }

    /// <summary>체크 상자</summary>
    public class WowCheck : CheckBox
    {
        bool hover;

        public WowCheck()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.Body;
            ForeColor = Theme.Text;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            AutoSize = false;
            Height = 22;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (BackColor != Color.Transparent) g.Clear(BackColor);
            Rectangle box = new Rectangle(0, (Height - 16) / 2, 16, 16);
            Theme.FillV(g, box, Color.FromArgb(30, 30, 40), Color.FromArgb(14, 14, 20));
            using (Pen p = new Pen(Theme.EdgeBlack)) g.DrawRectangle(p, box);
            using (Pen p = new Pen(hover && Enabled ? Theme.GoldLight : Theme.GoldMid))
                g.DrawRectangle(p, box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2);

            if (Checked)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Enabled ? Theme.GoldBright : Theme.TextMuted, 2.2f))
                {
                    g.DrawLine(p, box.X + 3, box.Y + 8, box.X + 6, box.Y + 12);
                    g.DrawLine(p, box.X + 6, box.Y + 12, box.X + 13, box.Y + 4);
                }
                g.SmoothingMode = SmoothingMode.Default;
            }

            Color c = !Enabled ? Theme.TextMuted : hover ? Theme.Gold : ForeColor;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(24, 0, Width - 24, Height), c,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>진행 막대 — 움푹 들어간 홈에 금색이 찬다</summary>
    public class WowProgress : Control
    {
        int val;
        public int Value
        {
            get { return val; }
            set { val = Math.Max(0, Math.Min(100, value)); Invalidate(); }
        }

        public WowProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 16;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.DrawInset(g, r, false);

            if (val > 0)
            {
                int w = (int)((Width - 6) * (val / 100.0));
                Rectangle f = new Rectangle(3, 3, Math.Max(1, w), Height - 7);
                Theme.FillV(g, f, Color.FromArgb(236, 186, 78), Color.FromArgb(142, 100, 24));
                using (Pen p = new Pen(Color.FromArgb(255, 230, 160)))
                    g.DrawLine(p, f.X, f.Y, f.Right - 1, f.Y);
            }
        }
    }

    /// <summary>버전 목록 — 원형 배지 + 이름 + 애드온 개수 (CheckedListBox 는 행 높이를 못 바꿔서 직접 그린다)</summary>
    public class FlavorList : ListBox
    {
        readonly System.Collections.Generic.HashSet<int> checkedSet =
            new System.Collections.Generic.HashSet<int>();
        int hoverIndex = -1;

        public event EventHandler CheckChanged;

        public FlavorList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 50;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Inset;
            ForeColor = Theme.Text;
            IntegralHeight = false;
            Font = Theme.Body;
        }

        public void AddFlavor(Flavor f, bool chk)
        {
            int i = Items.Add(f);
            if (chk) checkedSet.Add(i);
        }

        public void ClearItems()
        {
            Items.Clear();
            checkedSet.Clear();
        }

        public bool IsChecked(int i) { return checkedSet.Contains(i); }

        public System.Collections.Generic.List<Flavor> CheckedFlavors()
        {
            System.Collections.Generic.List<Flavor> list =
                new System.Collections.Generic.List<Flavor>();
            for (int i = 0; i < Items.Count; i++)
                if (checkedSet.Contains(i)) list.Add((Flavor)Items[i]);
            return list;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = IndexFromPoint(e.Location);
            if (i >= 0 && i < Items.Count)
            {
                if (checkedSet.Contains(i)) checkedSet.Remove(i);
                else checkedSet.Add(i);
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
            hoverIndex = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            Graphics g = e.Graphics;
            Flavor f = Items[e.Index] as Flavor;
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool usable = f != null && f.AddOnCount > 0;
            bool chk = checkedSet.Contains(e.Index);
            Rectangle b = e.Bounds;

            if (sel)
            {
                Theme.FillV(g, b, Theme.SelBlueTop, Theme.SelBlueBottom);
                using (Pen p = new Pen(Theme.SelEdge))
                {
                    g.DrawLine(p, b.X, b.Y, b.Right, b.Y);
                    g.DrawLine(p, b.X, b.Bottom - 1, b.Right, b.Bottom - 1);
                }
            }
            else if (e.Index == hoverIndex)
            {
                using (SolidBrush br = new SolidBrush(Theme.Hover)) g.FillRectangle(br, b);
            }
            else
            {
                using (SolidBrush br = new SolidBrush(Theme.Inset)) g.FillRectangle(br, b);
            }

            Rectangle badge = new Rectangle(b.X + 10, b.Y + 7, 36, 36);
            Theme.DrawBadge(g, badge, chk ? "✓" : "", sel, !usable);

            TextRenderer.DrawText(g, FriendlyName(f != null ? f.Name : ""), Theme.BodyBold,
                new Rectangle(b.X + 56, b.Y + 8, b.Width - 64, 18),
                usable ? (sel ? Theme.GoldBright : Theme.Text) : Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            string sub = f != null ? (usable ? "애드온 " + f.AddOnCount + "개" : "비어 있음") : "";
            TextRenderer.DrawText(g, sub, Theme.Small,
                new Rectangle(b.X + 56, b.Y + 27, b.Width - 64, 16),
                sel ? Theme.GoldDim : Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        /// <summary>_retail_ 같은 폴더 이름을 사람이 읽기 쉬운 이름으로</summary>
        public static string FriendlyName(string flavor)
        {
            switch (flavor)
            {
                case "_retail_": return "정식 서버";
                case "_classic_": return "클래식";
                case "_classic_era_": return "클래식 오리지널";
                case "_classic_ptr_": return "클래식 테스트";
                case "_ptr_": return "테스트 서버";
                case "_beta_": return "베타";
                case "_anniversary_": return "기념 서버";
                case "_forever_": return "클래식 포에버";
                default: return flavor;
            }
        }
    }

    /// <summary>입력란 — 움푹 들어간 홈</summary>
    public class WowField : Panel
    {
        public TextBox Box = new TextBox();

        public WowField()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 28;
            BackColor = Theme.Inset;

            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.Inset;
            Box.ForeColor = Theme.Text;
            Box.Font = Theme.Body;
            Box.Location = new Point(8, 6);
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Controls.Add(Box);
            Resize += delegate { Box.Width = Math.Max(20, Width - 16); };
        }

        public string Value
        {
            get { return Box.Text; }
            set { Box.Text = value; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.DrawInset(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Box.Focused);
            base.OnPaint(e);
        }
    }

    /// <summary>금색 글자 링크</summary>
    public class GoldLink : Label
    {
        public GoldLink()
        {
            AutoSize = true;
            Font = Theme.Small;
            ForeColor = Theme.GoldDim;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            ForeColor = Theme.GoldBright;
            Font = new Font(Font, FontStyle.Underline);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            ForeColor = Theme.GoldDim;
            Font = new Font(Font, FontStyle.Regular);
            base.OnMouseLeave(e);
        }
    }

    /// <summary>내용 영역 안의 제목 (금색 세리프 + 밑줄)</summary>
    public class WowHeading : Control
    {
        public string Caption = "";

        public WowHeading()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 28;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TextRenderer.DrawText(g, Caption, Theme.HeadFont,
                new Rectangle(0, 0, Width, Height - 6), Theme.Gold,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            using (Pen p = new Pen(Color.FromArgb(70, 64, 46)))
                g.DrawLine(p, 0, Height - 3, Width, Height - 3);
        }
    }
}
