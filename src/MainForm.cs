using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WowAddonManager
{
    public class MainForm : Form
    {
        // 창 장식
        RedCloseButton btnClose;
        MinButton btnMin;
        GoldLink lnkUpdate;

        // 업데이트 알림
        Panel pnlUpdate;
        Label lblUpdateText;
        WowButton btnUpdateNow, btnUpdateLater;

        // 와우 경로
        WowField fldWow;
        Label lblWowState;
        WowButton btnBrowseWow;

        // 좌측 버전 목록
        FlavorList lstFlavors;

        // 우측 내용
        Panel pnlRight;
        WowHeading headRight;
        Panel pnlExport, pnlInstall, pnlAddons;
        WowField fldExportDir, fldSourceDir;
        WowButton btnExportDir, btnSourceDir, btnPrimary, btnOpenFolder;
        WowCheck chkExpSettings, chkExpGraphics, chkInsSettings, chkInsClean, chkInsBackup;
        Label lblInsInfo, lblHint, lblExpDesc, lblInsDesc;
        ListView lvAddons;

        // 하단
        Label lblStatus;
        WowProgress bar;
        GoldLink lnkDetail;
        TextBox txtLog;
        WowTab tabExport, tabInstall, tabAddons;

        Thread worker;
        Settings settings = new Settings();
        ReleaseInfo pendingUpdate;
        bool logOpen;
        int tab;                 // 0 내보내기, 1 설치, 2 애드온 목록
        string lastOutputDir;

        const int LogH = 140;
        const int TabH = 30;

        public MainForm()
        {
            settings.Load();

            Text = AppInfo.Name;
            Font = Theme.Body;
            BackColor = Theme.FrameBottom;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(940, 640);
            MinimumSize = new Size(880, 600);
            StartPosition = FormStartPosition.CenterScreen;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            try { Icon = LoadAppIcon(); }
            catch { }

            BuildChrome();
            BuildUpdateBar();
            BuildPathRow();
            BuildLeft();
            BuildRight();
            BuildBottom();

            SetTab(int.Parse(settings.Get("tab", "0")));
            Shown += delegate
            {
                Relayout();
                Detect(false);
                StartSilentUpdateCheck();
            };
            Resize += delegate { Relayout(); };
            FormClosing += delegate { SaveSettings(); };
        }

        static Icon LoadAppIcon()
        {
            System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream("app.ico"))
                if (s != null) return new Icon(s);
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }

        // ── 창 그리기 ───────────────────────────────────────────
        /// <summary>오른쪽 내용 구역 바탕색 — 자식 컨트롤이 같은 색으로 지운다 (투명 중첩은 잔상이 남는다)</summary>
        static readonly Color ContentBg = Color.FromArgb(32, 32, 40);

        int FrameBottomY { get { return ClientSize.Height - TabH - 8; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle frame = new Rectangle(0, 0, ClientSize.Width - 1, FrameBottomY);
            Theme.DrawOrnateFrame(g, frame);

            // 제목 명판
            Rectangle plate = new Rectangle(66, 8, ClientSize.Width - 132, 24);
            Theme.DrawTitlePlate(g, plate, AppInfo.Name);

            // 좌상단 원형 초상 (프레임에 걸치게)
            Rectangle portrait = new Rectangle(8, 4, 50, 50);
            Theme.DrawPortraitRing(g, portrait);
            if (Icon != null)
                using (Icon ic = new Icon(Icon, 32, 32))
                    g.DrawIcon(ic, new Rectangle(portrait.X + 9, portrait.Y + 9, 32, 32));

            // 내용 구역 배경
            int top = ContentTop();
            Rectangle leftRect = new Rectangle(12, top, 288, FrameBottomY - top - 74);
            Theme.DrawInset(g, leftRect, false);

            Rectangle rightRect = new Rectangle(leftRect.Right + 10, top,
                ClientSize.Width - leftRect.Right - 24, leftRect.Height);
            using (SolidBrush b = new SolidBrush(ContentBg)) g.FillRectangle(b, rightRect);
            using (Pen p = new Pen(Theme.EdgeBlack)) g.DrawRectangle(p, rightRect);
            using (Pen p = new Pen(Theme.GoldDarkLine))
                g.DrawRectangle(p, rightRect.X + 1, rightRect.Y + 1, rightRect.Width - 2, rightRect.Height - 2);

            // 목록 제목 줄
            TextRenderer.DrawText(g, "옮길 버전", Theme.HeadFont,
                new Rectangle(leftRect.X + 12, leftRect.Y + 6, leftRect.Width - 24, 24),
                Theme.Gold, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            using (Pen p = new Pen(Color.FromArgb(70, 64, 46)))
                g.DrawLine(p, leftRect.X + 10, leftRect.Y + 30, leftRect.Right - 10, leftRect.Y + 30);

            base.OnPaint(e);
        }

        int ContentTop()
        {
            return 84 + (pnlUpdate.Visible ? pnlUpdate.Height + 6 : 0);
        }

        // ── 창 끌기 / 크기 조절 ──────────────────────────────────
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        const int WM_NCLBUTTONDOWN = 0xA1, WM_NCHITTEST = 0x84;
        const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11,
                  HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                  HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    int x = (short)((long)m.LParam & 0xFFFF);
                    int y = (short)(((long)m.LParam >> 16) & 0xFFFF);
                    Point p = PointToClient(new Point(x, y));
                    const int gr = 6;
                    bool l = p.X <= gr, r = p.X >= ClientSize.Width - gr;
                    bool t = p.Y <= gr, b = p.Y >= ClientSize.Height - gr;

                    if (l && t) m.Result = (IntPtr)HTTOPLEFT;
                    else if (r && t) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (l && b) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (r && b) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else if (l) m.Result = (IntPtr)HTLEFT;
                    else if (r) m.Result = (IntPtr)HTRIGHT;
                    else if (t) m.Result = (IntPtr)HTTOP;
                    else if (b) m.Result = (IntPtr)HTBOTTOM;
                    else if (p.Y < 36 && p.X > 60 && p.X < ClientSize.Width - 70)
                        m.Result = (IntPtr)HTCAPTION;   // 제목 명판을 끌면 창이 움직인다
                }
                return;
            }
            base.WndProc(ref m);
        }

        void BuildChrome()
        {
            btnClose = new RedCloseButton();
            btnClose.Click += delegate { Close(); };
            Controls.Add(btnClose);

            btnMin = new MinButton();
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            Controls.Add(btnMin);

            lnkUpdate = new GoldLink();
            lnkUpdate.Text = "v" + AppInfo.Version + " · 업데이트 확인";
            lnkUpdate.Click += delegate { ManualUpdateCheck(); };
            Controls.Add(lnkUpdate);
        }

        void BuildUpdateBar()
        {
            pnlUpdate = new Panel();
            pnlUpdate.Height = 38;
            pnlUpdate.Visible = false;
            pnlUpdate.BackColor = Color.FromArgb(40, 34, 20);
            pnlUpdate.Paint += delegate (object s, PaintEventArgs e)
            {
                Rectangle r = new Rectangle(0, 0, pnlUpdate.Width - 1, pnlUpdate.Height - 1);
                Theme.FillV(e.Graphics, r, Color.FromArgb(62, 52, 28), Color.FromArgb(36, 30, 16));
                using (Pen p = new Pen(Theme.EdgeBlack)) e.Graphics.DrawRectangle(p, r);
                using (Pen p = new Pen(Theme.GoldMid))
                    e.Graphics.DrawRectangle(p, 1, 1, r.Width - 2, r.Height - 2);
            };
            Controls.Add(pnlUpdate);

            lblUpdateText = new Label();
            lblUpdateText.AutoSize = true;
            lblUpdateText.Location = new Point(14, 11);
            lblUpdateText.ForeColor = Theme.Gold;
            lblUpdateText.BackColor = Color.Transparent;
            pnlUpdate.Controls.Add(lblUpdateText);

            btnUpdateNow = new WowButton();
            btnUpdateNow.Text = "지금 업데이트";
            btnUpdateNow.Size = new Size(114, 26);
            btnUpdateNow.Click += delegate { DoUpdate(); };
            pnlUpdate.Controls.Add(btnUpdateNow);

            btnUpdateLater = new WowButton();
            btnUpdateLater.Text = "나중에";
            btnUpdateLater.Size = new Size(72, 26);
            btnUpdateLater.Click += delegate { pnlUpdate.Visible = false; Relayout(); };
            pnlUpdate.Controls.Add(btnUpdateLater);
        }

        void BuildPathRow()
        {
            fldWow = new WowField();
            fldWow.Box.ReadOnly = true;
            Controls.Add(fldWow);

            lblWowState = new Label();
            lblWowState.AutoSize = true;
            lblWowState.Font = Theme.Small;
            lblWowState.ForeColor = Theme.Ok;
            lblWowState.BackColor = Color.Transparent;
            Controls.Add(lblWowState);

            btnBrowseWow = new WowButton();
            btnBrowseWow.Text = "변경";
            btnBrowseWow.Size = new Size(72, 28);
            btnBrowseWow.Click += delegate { BrowseWow(); };
            Controls.Add(btnBrowseWow);
        }

        void BuildLeft()
        {
            lstFlavors = new FlavorList();
            lstFlavors.SelectedIndexChanged += delegate { UpdateHints(); RefreshAddons(); };
            lstFlavors.CheckChanged += delegate { UpdateHints(); };
            Controls.Add(lstFlavors);
        }

        // ── 우측 내용 ───────────────────────────────────────────
        void BuildRight()
        {
            pnlRight = new Panel();
            pnlRight.BackColor = ContentBg;
            Controls.Add(pnlRight);

            headRight = new WowHeading();
            headRight.Location = new Point(0, 0);
            pnlRight.Controls.Add(headRight);

            BuildExportPanel();
            BuildInstallPanel();
            BuildAddonsPanel();

            lblHint = new Label();
            lblHint.AutoSize = false;
            lblHint.Font = Theme.Small;
            lblHint.ForeColor = Theme.TextMuted;
            lblHint.BackColor = ContentBg;
            pnlRight.Controls.Add(lblHint);

            btnPrimary = new WowButton();
            btnPrimary.Font = Theme.F(10f, FontStyle.Bold);
            btnPrimary.Height = 38;
            btnPrimary.Click += delegate { if (tab == 0) StartExport(); else StartInstall(); };
            pnlRight.Controls.Add(btnPrimary);

            btnOpenFolder = new WowButton();
            btnOpenFolder.Text = "폴더 열기";
            btnOpenFolder.Height = 38;
            btnOpenFolder.Width = 96;
            btnOpenFolder.Visible = false;
            btnOpenFolder.Click += delegate
            {
                if (lastOutputDir != null && Directory.Exists(lastOutputDir))
                    Updater.OpenUrl(lastOutputDir);
            };
            pnlRight.Controls.Add(btnOpenFolder);
        }

        Label Caption(string text, Point p, Panel parent)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = p;
            l.ForeColor = Theme.TextDim;
            l.BackColor = ContentBg;
            parent.Controls.Add(l);
            return l;
        }

        void BuildExportPanel()
        {
            pnlExport = new Panel();
            pnlExport.BackColor = ContentBg;
            pnlRight.Controls.Add(pnlExport);

            lblExpDesc = new Label();
            lblExpDesc.Text = "이 PC의 애드온과 설정을 한 폴더에 담습니다.\n" +
                              "그 폴더를 USB나 클라우드에 올려 두고, 다른 PC에서 '설치'로 가져오면 됩니다.";
            lblExpDesc.AutoSize = false;
            lblExpDesc.Height = 40;
            lblExpDesc.Location = new Point(0, 2);
            lblExpDesc.ForeColor = Theme.TextDim;
            lblExpDesc.BackColor = ContentBg;
            pnlExport.Controls.Add(lblExpDesc);

            Caption("저장 위치", new Point(0, 52), pnlExport);

            fldExportDir = new WowField();
            fldExportDir.Location = new Point(0, 72);
            fldExportDir.Value = settings.Get("exportDir", AppInfo.DefaultExportRoot);
            EnableFolderDrop(fldExportDir);
            pnlExport.Controls.Add(fldExportDir);

            btnExportDir = new WowButton();
            btnExportDir.Text = "찾아보기";
            btnExportDir.Size = new Size(86, 28);
            btnExportDir.Click += delegate
            {
                string d = PickFolder("애드온과 설정을 저장할 폴더", fldExportDir.Value);
                if (d != null) fldExportDir.Value = d;
            };
            pnlExport.Controls.Add(btnExportDir);

            chkExpSettings = MakeCheck("설정(WTF)도 함께 — 애드온 프로필·키바인드·매크로",
                new Point(0, 112), settings.GetBool("expSettings", true));
            chkExpSettings.CheckedChanged += delegate { chkExpGraphics.Enabled = chkExpSettings.Checked; };
            pnlExport.Controls.Add(chkExpSettings);

            chkExpGraphics = MakeCheck("해상도·그래픽 설정도 포함 (모니터가 다르면 권장하지 않음)",
                new Point(22, 138), settings.GetBool("expGraphics", false));
            pnlExport.Controls.Add(chkExpGraphics);
        }

        void BuildInstallPanel()
        {
            pnlInstall = new Panel();
            pnlInstall.BackColor = ContentBg;
            pnlInstall.Visible = false;
            pnlRight.Controls.Add(pnlInstall);

            lblInsDesc = new Label();
            lblInsDesc.Text = "내보내기로 만든 폴더를 골라 이 PC에 그대로 설치합니다.\n" +
                              "와우는 완전히 종료해 두세요.";
            lblInsDesc.AutoSize = false;
            lblInsDesc.Height = 40;
            lblInsDesc.Location = new Point(0, 2);
            lblInsDesc.ForeColor = Theme.TextDim;
            lblInsDesc.BackColor = ContentBg;
            pnlInstall.Controls.Add(lblInsDesc);

            Caption("가져올 폴더", new Point(0, 52), pnlInstall);

            fldSourceDir = new WowField();
            fldSourceDir.Location = new Point(0, 72);
            fldSourceDir.Value = settings.Get("sourceDir", AppInfo.DefaultExportRoot);
            fldSourceDir.Box.TextChanged += delegate { ShowManifest(); };
            EnableFolderDrop(fldSourceDir);
            pnlInstall.Controls.Add(fldSourceDir);

            btnSourceDir = new WowButton();
            btnSourceDir.Text = "찾아보기";
            btnSourceDir.Size = new Size(86, 28);
            btnSourceDir.Click += delegate
            {
                string d = PickFolder("내보내기로 만든 폴더 (manifest.json 이 있는 곳)", fldSourceDir.Value);
                if (d != null) fldSourceDir.Value = d;
            };
            pnlInstall.Controls.Add(btnSourceDir);

            lblInsInfo = new Label();
            lblInsInfo.AutoSize = false;
            lblInsInfo.Location = new Point(0, 104);
            lblInsInfo.Height = 18;
            lblInsInfo.Font = Theme.Small;
            lblInsInfo.ForeColor = Theme.TextMuted;
            lblInsInfo.BackColor = ContentBg;
            pnlInstall.Controls.Add(lblInsInfo);

            chkInsSettings = MakeCheck("설정(WTF)도 함께 설치", new Point(0, 128),
                settings.GetBool("insSettings", true));
            pnlInstall.Controls.Add(chkInsSettings);

            chkInsBackup = MakeCheck("설치 전에 지금 상태를 백업", new Point(0, 152),
                settings.GetBool("insBackup", true));
            pnlInstall.Controls.Add(chkInsBackup);

            chkInsClean = MakeCheck("기존 애드온을 모두 지우고 설치 (완전히 동일하게)",
                new Point(0, 176), settings.GetBool("insClean", false));
            chkInsClean.ForeColor = Theme.Danger;
            pnlInstall.Controls.Add(chkInsClean);
        }

        void BuildAddonsPanel()
        {
            pnlAddons = new Panel();
            pnlAddons.BackColor = ContentBg;
            pnlAddons.Visible = false;
            pnlRight.Controls.Add(pnlAddons);

            lvAddons = new ListView();
            lvAddons.View = View.Details;
            lvAddons.FullRowSelect = true;
            lvAddons.BorderStyle = BorderStyle.None;
            lvAddons.BackColor = Theme.Inset;
            lvAddons.ForeColor = Theme.Text;
            lvAddons.OwnerDraw = true;
            lvAddons.Location = new Point(0, 0);
            lvAddons.Columns.Add("애드온", 220);
            lvAddons.Columns.Add("폴더", 180);
            lvAddons.Columns.Add("버전", 90);
            lvAddons.Columns.Add("출처", 100);

            lvAddons.DrawColumnHeader += delegate (object s, DrawListViewColumnHeaderEventArgs e)
            {
                Theme.FillV(e.Graphics, e.Bounds, Color.FromArgb(52, 52, 64), Color.FromArgb(28, 28, 36));
                using (Pen p = new Pen(Theme.EdgeBlack))
                    e.Graphics.DrawRectangle(p, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.BodyBold,
                    new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
                    Theme.Gold, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };
            lvAddons.DrawItem += delegate (object s, DrawListViewItemEventArgs e)
            {
                bool sel = (e.State & ListViewItemStates.Selected) != 0;
                if (sel) Theme.FillV(e.Graphics, e.Bounds, Theme.SelBlueTop, Theme.SelBlueBottom);
                else using (SolidBrush b = new SolidBrush(e.ItemIndex % 2 == 0 ? Theme.Inset : Theme.InsetAlt))
                        e.Graphics.FillRectangle(b, e.Bounds);
            };
            lvAddons.DrawSubItem += delegate (object s, DrawListViewSubItemEventArgs e)
            {
                Color c = e.ColumnIndex == 0 ? Theme.Text : Theme.TextDim;
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Theme.Body,
                    new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
                    c, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            pnlAddons.Controls.Add(lvAddons);
        }

        WowCheck MakeCheck(string text, Point p, bool chk)
        {
            WowCheck c = new WowCheck();
            c.Text = text;
            c.Location = p;
            c.Size = new Size(440, 22);
            c.BackColor = ContentBg;
            c.Checked = chk;
            return c;
        }

        void EnableFolderDrop(WowField f)
        {
            Control[] targets = { f, f.Box };
            foreach (Control t in targets)
            {
                t.AllowDrop = true;
                t.DragEnter += delegate (object s, DragEventArgs e)
                {
                    e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop)
                        ? DragDropEffects.Copy : DragDropEffects.None;
                };
                t.DragDrop += delegate (object s, DragEventArgs e)
                {
                    string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (paths == null || paths.Length == 0) return;
                    string p = paths[0];
                    if (File.Exists(p)) p = Path.GetDirectoryName(p);
                    if (Directory.Exists(p)) f.Value = p;
                };
            }
        }

        // ── 하단 ────────────────────────────────────────────────
        void BuildBottom()
        {
            lblStatus = new Label();
            lblStatus.Text = "준비됨";
            lblStatus.AutoSize = false;
            lblStatus.Height = 18;
            lblStatus.ForeColor = Theme.TextDim;
            lblStatus.BackColor = Color.Transparent;
            Controls.Add(lblStatus);

            lnkDetail = new GoldLink();
            lnkDetail.Text = "자세히 ▾";
            lnkDetail.Click += delegate { ToggleLog(); };
            Controls.Add(lnkDetail);

            bar = new WowProgress();
            Controls.Add(bar);

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Theme.Inset;
            txtLog.ForeColor = Theme.TextDim;
            txtLog.Font = Theme.Mono;
            txtLog.BorderStyle = BorderStyle.FixedSingle;
            txtLog.Visible = false;
            Controls.Add(txtLog);

            tabExport = new WowTab(); tabExport.Text = "내보내기";
            tabExport.Click += delegate { SetTab(0); };
            Controls.Add(tabExport);

            tabInstall = new WowTab(); tabInstall.Text = "설치";
            tabInstall.Click += delegate { SetTab(1); };
            Controls.Add(tabInstall);

            tabAddons = new WowTab(); tabAddons.Text = "애드온 목록";
            tabAddons.Click += delegate { SetTab(2); };
            Controls.Add(tabAddons);
        }

        void ToggleLog()
        {
            logOpen = !logOpen;
            lnkDetail.Text = logOpen ? "자세히 ▴" : "자세히 ▾";
            txtLog.Visible = logOpen;
            Height += logOpen ? LogH : -LogH;
            Relayout();
        }

        void SetTab(int t)
        {
            if (t < 0 || t > 2) t = 0;
            tab = t;
            tabExport.Selected = t == 0;
            tabInstall.Selected = t == 1;
            tabAddons.Selected = t == 2;
            tabExport.Invalidate(); tabInstall.Invalidate(); tabAddons.Invalidate();

            pnlExport.Visible = t == 0;
            pnlInstall.Visible = t == 1;
            pnlAddons.Visible = t == 2;

            headRight.Caption = t == 0 ? "내보내기" : t == 1 ? "설치" : "애드온 목록";
            headRight.Invalidate();

            btnPrimary.Visible = t != 2;
            btnPrimary.Kind = t == 1 ? BtnKind.Important : BtnKind.Normal;
            btnPrimary.Text = t == 0 ? "내보내기 시작" : "설치 시작";
            btnOpenFolder.Visible = false;

            if (t == 1) ShowManifest();
            if (t == 2) RefreshAddons();
            UpdateHints();
            Relayout();
        }

        // ── 배치 ────────────────────────────────────────────────
        void Relayout()
        {
            if (fldWow == null || tabAddons == null) return;

            int W = ClientSize.Width;
            btnClose.Location = new Point(W - 36, 7);
            btnMin.Location = new Point(W - 64, 8);

            pnlUpdate.Location = new Point(12, 40);
            pnlUpdate.Width = W - 24;
            btnUpdateLater.Location = new Point(pnlUpdate.Width - btnUpdateLater.Width - 12, 6);
            btnUpdateNow.Location = new Point(btnUpdateLater.Left - btnUpdateNow.Width - 8, 6);

            int pathY = pnlUpdate.Visible ? pnlUpdate.Bottom + 8 : 42;
            btnBrowseWow.Location = new Point(W - 72 - 12, pathY);
            lblWowState.Location = new Point(btnBrowseWow.Left - lblWowState.Width - 10, pathY + 6);
            fldWow.Location = new Point(12, pathY);
            fldWow.Size = new Size(Math.Max(150, lblWowState.Left - 10 - 12), 28);

            int top = ContentTop();
            int contentH = FrameBottomY - top - 74;

            Rectangle leftRect = new Rectangle(12, top, 288, contentH);
            lstFlavors.Location = new Point(leftRect.X + 4, leftRect.Y + 36);
            lstFlavors.Size = new Size(leftRect.Width - 8, leftRect.Height - 40);

            Rectangle rightRect = new Rectangle(leftRect.Right + 10, top, W - leftRect.Right - 24, contentH);
            pnlRight.Location = new Point(rightRect.X + 14, rightRect.Y + 10);
            pnlRight.Size = new Size(rightRect.Width - 28, rightRect.Height - 20);

            int rw = pnlRight.Width;
            headRight.Size = new Size(rw, 28);

            int innerTop = 36;
            pnlExport.Location = new Point(0, innerTop);
            pnlExport.Size = new Size(rw, 170);
            pnlInstall.Location = new Point(0, innerTop);
            pnlInstall.Size = new Size(rw, 205);

            lblExpDesc.Width = rw;
            lblInsDesc.Width = rw;
            fldExportDir.Size = new Size(rw - 86 - 8, 28);
            btnExportDir.Location = new Point(rw - 86, 72);
            fldSourceDir.Size = new Size(rw - 86 - 8, 28);
            btnSourceDir.Location = new Point(rw - 86, 72);
            lblInsInfo.Width = rw;

            int btnTop = pnlRight.Height - 38;
            btnOpenFolder.Location = new Point(rw - btnOpenFolder.Width, btnTop);
            btnPrimary.Width = 220;
            btnPrimary.Location = new Point((rw - btnPrimary.Width) / 2
                - (btnOpenFolder.Visible ? btnOpenFolder.Width / 2 + 6 : 0), btnTop);
            lblHint.Location = new Point(0, btnTop - 24);
            lblHint.Size = new Size(rw, 20);

            pnlAddons.Location = new Point(0, innerTop);
            pnlAddons.Size = new Size(rw, btnTop - innerTop + 30);
            lvAddons.Size = pnlAddons.Size;

            // 상태줄 + 진행 막대 (프레임 안쪽 아래)
            int sy = FrameBottomY - 64;
            lblStatus.Location = new Point(16, sy);
            lblStatus.Width = W - 150;
            lnkDetail.Location = new Point(W - lnkDetail.Width - 16, sy);
            lnkUpdate.Location = new Point(lnkDetail.Left - lnkUpdate.Width - 18, sy);
            bar.Location = new Point(16, sy + 22);
            bar.Width = W - 32;

            if (logOpen)
            {
                txtLog.Location = new Point(16, bar.Bottom + 6);
                txtLog.Size = new Size(W - 32, FrameBottomY - bar.Bottom - 12);
            }

            // 하단 탭 (프레임 밖으로 걸침)
            int tx = 24, ty = FrameBottomY - 2;
            tabExport.Location = new Point(tx, ty); tabExport.Size = new Size(96, TabH);
            tabInstall.Location = new Point(tx + 100, ty); tabInstall.Size = new Size(78, TabH);
            tabAddons.Location = new Point(tx + 182, ty); tabAddons.Size = new Size(110, TabH);

            Invalidate();
        }

        // ── 경로 / 목록 ─────────────────────────────────────────
        void Detect(bool verbose)
        {
            string p = WowCore.TrimToWowRoot(settings.Get("wowPath", null));
            if (p == null)
            {
                Log("와우 설치 위치를 찾는 중…");
                p = WowCore.FindWowPath();
            }

            if (p == null)
            {
                fldWow.Value = "";
                lblWowState.Text = "찾지 못함";
                lblWowState.ForeColor = Theme.Danger;
                Status("와우 설치 위치를 찾지 못했습니다. '변경'으로 지정하세요.");
                Log("자동 탐지 실패.");
                if (verbose) Info("와우 설치 위치를 찾지 못했습니다.\n'변경'을 눌러 폴더를 지정하세요.");
                Relayout();
                return;
            }

            fldWow.Value = p;
            lblWowState.Text = "찾음";
            lblWowState.ForeColor = Theme.Ok;
            Log("와우 위치: " + p);
            LoadFlavors();
            Relayout();
        }

        void BrowseWow()
        {
            string d = PickFolder("World of Warcraft 폴더 (_retail_ 등이 들어있는 폴더)", fldWow.Value);
            if (d == null) return;
            string root = WowCore.TrimToWowRoot(d);
            if (root == null)
            {
                Info("그 폴더 안에서 _retail_ 같은 버전 폴더를 찾지 못했습니다.\n\n" +
                     "World of Warcraft 폴더를 선택하세요.");
                return;
            }
            fldWow.Value = root;
            lblWowState.Text = "찾음";
            lblWowState.ForeColor = Theme.Ok;
            LoadFlavors();
            Relayout();
        }

        void LoadFlavors()
        {
            lstFlavors.ClearItems();
            foreach (Flavor f in WowCore.GetFlavors(fldWow.Value))
                lstFlavors.AddFlavor(f, f.AddOnCount > 0);
            if (lstFlavors.Items.Count > 0) lstFlavors.SelectedIndex = 0;
            UpdateHints();
            RefreshAddons();
            if (tab == 1) ShowManifest();
        }

        List<Flavor> CheckedFlavors() { return lstFlavors.CheckedFlavors(); }

        void RefreshAddons()
        {
            if (lvAddons == null || !pnlAddons.Visible) return;
            Flavor f = lstFlavors.SelectedItem as Flavor;
            lvAddons.BeginUpdate();
            lvAddons.Items.Clear();
            if (f != null)
            {
                foreach (AddonInfo a in WowCore.GetAddons(f.AddOnsPath))
                {
                    string src = a.Curse != "" ? "CurseForge"
                               : a.Wago != "" ? "Wago"
                               : a.WowI != "" ? "WoWInterface" : "";
                    ListViewItem it = new ListViewItem(a.Title);
                    it.SubItems.Add(a.Folder);
                    it.SubItems.Add(a.Version);
                    it.SubItems.Add(src);
                    lvAddons.Items.Add(it);
                }
            }
            lvAddons.EndUpdate();
        }

        void UpdateHints()
        {
            if (lblHint == null) return;
            List<Flavor> sel = CheckedFlavors();
            int addons = 0;
            foreach (Flavor f in sel) addons += f.AddOnCount;

            if (fldWow.Value.Length == 0) { lblHint.Text = ""; btnPrimary.Enabled = false; return; }

            if (tab == 2)
                lblHint.Text = "";
            else if (sel.Count == 0)
                lblHint.Text = "왼쪽에서 버전을 하나 이상 체크하세요.";
            else if (tab == 0)
                lblHint.Text = "선택한 " + sel.Count + "개 버전, 애드온 " + addons + "개를 묶습니다.";
            else
                lblHint.Text = "선택한 " + sel.Count + "개 버전에 설치합니다.";

            btnPrimary.Enabled = sel.Count > 0;
        }

        void ShowManifest()
        {
            if (lblInsInfo == null) return;
            try
            {
                ManifestInfo mi = WowCore.ReadManifest(fldSourceDir.Value);
                List<string> names = new List<string>();
                foreach (string k in mi.Flavors.Keys) names.Add(FlavorList.FriendlyName(k));
                lblInsInfo.ForeColor = Theme.Ok;
                lblInsInfo.Text = mi.SourcePC + " 에서 내보냄 · " + string.Join(", ", names.ToArray())
                    + (mi.HasSettings ? " · 설정 포함" : " · 설정 없음");
            }
            catch (FileNotFoundException)
            {
                lblInsInfo.ForeColor = Theme.TextMuted;
                lblInsInfo.Text = "내보내기로 만든 폴더를 고르세요. (manifest.json 이 있는 폴더)";
            }
            catch (Exception ex)
            {
                lblInsInfo.ForeColor = Theme.Danger;
                lblInsInfo.Text = ex.Message;
            }
        }

        // ── 작업 ────────────────────────────────────────────────
        void StartExport()
        {
            if (!CheckWow()) return;
            List<Flavor> sel = CheckedFlavors();
            if (sel.Count == 0) { Info("내보낼 버전을 왼쪽에서 선택하세요."); return; }

            string outDir = fldExportDir.Value.Trim();
            if (outDir.Length == 0) { Info("저장 위치를 지정하세요."); return; }
            try { Directory.CreateDirectory(outDir); }
            catch (Exception ex) { Info("저장 위치를 만들 수 없습니다.\n\n" + ex.Message); return; }

            ExportOptions o = new ExportOptions();
            o.WowPath = fldWow.Value;
            o.Flavors = sel;
            o.OutputDir = outDir;
            o.IncludeSettings = chkExpSettings.Checked;
            o.IncludeGraphicsConfig = chkExpSettings.Checked && chkExpGraphics.Checked;

            long raw = 0;
            foreach (Flavor f in sel)
            {
                raw += WowCore.DirectorySize(f.AddOnsPath);
                if (o.IncludeSettings) raw += WowCore.DirectorySize(f.WtfPath);
            }

            lastOutputDir = outDir;
            Log("");
            Log("내보내기 — 원본 " + WowCore.HumanSize(raw) + " → " + outDir);
            Status("내보내는 중…");
            Run(delegate (Reporter rep) { WowCore.Export(o, rep); },
                "내보내기가 끝났습니다.\n\n이 폴더를 USB·구글드라이브·OneDrive에 두고,\n다른 PC에서 '설치'로 가져오면 됩니다.\n\n" + outDir,
                true);
        }

        void StartInstall()
        {
            if (!CheckWow()) return;
            ManifestInfo mi;
            try { mi = WowCore.ReadManifest(fldSourceDir.Value); }
            catch (Exception ex) { Info(ex.Message); return; }

            List<string> names = new List<string>();
            foreach (Flavor f in CheckedFlavors())
                if (mi.Flavors.ContainsKey(f.Name)) names.Add(f.Name);

            if (names.Count == 0)
            {
                List<string> have = new List<string>();
                foreach (string k in mi.Flavors.Keys) have.Add(FlavorList.FriendlyName(k));
                Info("설치할 버전이 없습니다.\n\n가져올 폴더에 들어있는 버전: "
                     + string.Join(", ", have.ToArray())
                     + "\n왼쪽 목록에서 같은 버전을 체크하세요.");
                return;
            }

            List<string> friendly = new List<string>();
            foreach (string n in names) friendly.Add(FlavorList.FriendlyName(n));

            string msg = string.Join(", ", friendly.ToArray()) + " 에 설치합니다.\n\n"
                + "대상: " + fldWow.Value + "\n\n"
                + (chkInsClean.Checked
                    ? "• 기존 애드온을 모두 지우고 설치합니다.\n"
                    : "• 같은 이름은 덮어쓰고, 나머지는 그대로 둡니다.\n")
                + (chkInsBackup.Checked ? "• 지금 상태는 백업해 둡니다.\n" : "• 백업하지 않습니다.\n")
                + "\n계속할까요?";

            if (MessageBox.Show(this, msg, "설치 확인", MessageBoxButtons.OKCancel,
                                MessageBoxIcon.Question) != DialogResult.OK) return;

            InstallOptions o = new InstallOptions();
            o.WowPath = fldWow.Value;
            o.SourceDir = fldSourceDir.Value;
            o.FlavorNames = names;
            o.IncludeSettings = chkInsSettings.Checked && mi.HasSettings;
            o.CleanInstall = chkInsClean.Checked;
            o.Backup = chkInsBackup.Checked;
            o.BackupRoot = AppInfo.DefaultBackupRoot;

            lastOutputDir = o.BackupRoot;
            Log("");
            Log("설치 — " + o.SourceDir + " → " + o.WowPath);
            Status("설치하는 중…");
            Run(delegate (Reporter rep) { WowCore.Install(o, rep); },
                "설치가 끝났습니다.\n\n와우를 실행해 캐릭터 선택 화면의 'AddOns' 에서 목록을 확인하세요.",
                false);
        }

        bool CheckWow()
        {
            if (WowCore.TrimToWowRoot(fldWow.Value) == null)
            {
                Info("와우 설치 위치가 올바르지 않습니다.\n'변경'을 눌러 지정하세요.");
                return false;
            }
            return true;
        }

        delegate void Job(Reporter report);

        void Run(Job job, string doneMessage, bool showOpenFolder)
        {
            if (worker != null && worker.IsAlive) { Info("이미 작업이 진행 중입니다."); return; }
            SaveSettings();
            SetBusy(true);

            Reporter rep = delegate (string message, int percent)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (message != null) { Log(message); Status(message.TrimStart(' ', '=')); }
                        if (percent >= 0) bar.Value = percent;
                    });
                }
                catch { }
            };

            worker = new Thread(delegate ()
            {
                string error = null;
                try { job(rep); }
                catch (Exception ex) { error = ex.Message; }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        SetBusy(false);
                        if (error != null)
                        {
                            bar.Value = 0;
                            Log("실패: " + error);
                            Status("실패했습니다.");
                            MessageBox.Show(this, error, "작업 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        else
                        {
                            bar.Value = 100;
                            Log("완료.");
                            Status("완료되었습니다.");
                            btnOpenFolder.Visible = showOpenFolder;
                            LoadFlavors();
                            Relayout();
                            MessageBox.Show(this, doneMessage, "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    });
                }
                catch { }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void SetBusy(bool busy)
        {
            btnPrimary.Enabled = !busy;
            btnBrowseWow.Enabled = !busy;
            lstFlavors.Enabled = !busy;
            pnlExport.Enabled = !busy;
            pnlInstall.Enabled = !busy;
            tabExport.Enabled = !busy;
            tabInstall.Enabled = !busy;
            tabAddons.Enabled = !busy;
            Cursor = busy ? Cursors.AppStarting : Cursors.Default;
            if (busy) { bar.Value = 0; btnOpenFolder.Visible = false; }
        }

        void SaveSettings()
        {
            settings.Set("wowPath", fldWow.Value);
            settings.Set("exportDir", fldExportDir.Value);
            settings.Set("sourceDir", fldSourceDir.Value);
            settings.Set("expSettings", chkExpSettings.Checked);
            settings.Set("expGraphics", chkExpGraphics.Checked);
            settings.Set("insSettings", chkInsSettings.Checked);
            settings.Set("insBackup", chkInsBackup.Checked);
            settings.Set("insClean", chkInsClean.Checked);
            settings.Set("tab", tab.ToString());
            settings.Save();
        }

        // ── 업데이트 ────────────────────────────────────────────
        void StartSilentUpdateCheck()
        {
            Thread t = new Thread(delegate ()
            {
                ReleaseInfo r = Updater.CheckLatest();
                if (!Updater.IsNewer(r)) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        pendingUpdate = r;
                        lblUpdateText.Text = "새 버전 v" + r.Version + " 이(가) 나왔습니다. (지금 v" + AppInfo.Version + ")";
                        pnlUpdate.Visible = true;
                        Relayout();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void ManualUpdateCheck()
        {
            Status("업데이트 확인 중…");
            Thread t = new Thread(delegate ()
            {
                ReleaseInfo r = Updater.CheckLatest();
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (r == null)
                        {
                            Status("업데이트 정보를 가져오지 못했습니다.");
                            Info("업데이트 정보를 가져오지 못했습니다.\n인터넷 연결을 확인하거나, 아래 주소에서 직접 확인하세요.\n\n"
                                 + AppInfo.ReleasesUrl);
                            return;
                        }
                        if (Updater.IsNewer(r))
                        {
                            pendingUpdate = r;
                            lblUpdateText.Text = "새 버전 v" + r.Version + " 이(가) 나왔습니다. (지금 v" + AppInfo.Version + ")";
                            pnlUpdate.Visible = true;
                            Relayout();
                            Status("새 버전 v" + r.Version + " 이(가) 있습니다.");
                        }
                        else
                        {
                            Status("최신 버전입니다. (v" + AppInfo.Version + ")");
                            Info("최신 버전을 쓰고 있습니다. (v" + AppInfo.Version + ")");
                        }
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void DoUpdate()
        {
            if (pendingUpdate == null) return;

            if (pendingUpdate.InstallerUrl.Length == 0)
            {
                Updater.OpenUrl(pendingUpdate.PageUrl.Length > 0 ? pendingUpdate.PageUrl : AppInfo.ReleasesUrl);
                return;
            }

            string notes = pendingUpdate.Notes.Length > 0
                ? "\n\n바뀐 점\n" + Truncate(pendingUpdate.Notes, 500) : "";
            if (MessageBox.Show(this,
                    "v" + pendingUpdate.Version + " 로 업데이트합니다." + notes +
                    "\n\n설치 파일을 받아 실행합니다. 프로그램은 자동으로 닫힙니다.\n계속할까요?",
                    "업데이트", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            btnUpdateNow.Enabled = false;
            Status("업데이트 내려받는 중…");

            Thread t = new Thread(delegate ()
            {
                string file = null, error = null;
                try
                {
                    file = Updater.Download(pendingUpdate, delegate (int pct)
                    {
                        try { BeginInvoke((MethodInvoker)delegate { bar.Value = pct; }); }
                        catch { }
                    });
                }
                catch (Exception ex) { error = ex.Message; }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        btnUpdateNow.Enabled = true;
                        if (error != null || file == null)
                        {
                            Status("업데이트를 받지 못했습니다.");
                            Info("업데이트를 받지 못했습니다.\n\n" + error +
                                 "\n\n아래 주소에서 직접 받을 수 있습니다.\n" + AppInfo.ReleasesUrl);
                            return;
                        }
                        if (Updater.RunInstaller(file)) Application.Exit();
                        else
                        {
                            Status("설치 파일을 실행하지 못했습니다.");
                            Info("설치 파일을 실행하지 못했습니다. 관리자 권한이 필요합니다.\n\n직접 실행하세요:\n" + file);
                        }
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        static string Truncate(string s, int n)
        {
            if (s == null) return "";
            s = s.Trim();
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        // ── 유틸 ────────────────────────────────────────────────
        string PickFolder(string description, string initial)
        {
            FolderBrowserDialog d = new FolderBrowserDialog();
            d.Description = description;
            d.ShowNewFolderButton = true;
            try { if (Directory.Exists(initial)) d.SelectedPath = initial; }
            catch { }
            if (d.ShowDialog(this) == DialogResult.OK) return d.SelectedPath;
            return null;
        }

        void Info(string msg)
        {
            MessageBox.Show(this, msg, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void Status(string msg) { lblStatus.Text = msg; }

        void Log(string message)
        {
            if (txtLog.TextLength > 200000) txtLog.Clear();
            txtLog.AppendText(message + Environment.NewLine);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (worker != null && worker.IsAlive)
            {
                if (MessageBox.Show(this, "작업이 진행 중입니다. 정말 닫을까요?", "확인",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                { e.Cancel = true; return; }
            }
            base.OnFormClosing(e);
        }

        [STAThread]
        public static void Main()
        {
            try { if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); }
            catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
