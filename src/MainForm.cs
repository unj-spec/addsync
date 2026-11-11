using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WowAddonManager
{
    public class MainForm : Form
    {
        // 창
        Panel pnlTitle, pnlSide, pnlMain, pnlStatus, pnlPath, pnlFooter, pnlContent, pnlUpdate;
        SysBtn btnMin, btnClose;
        LinkText lnkUpdate, lnkDetail;

        // 업데이트 알림
        Label lblUpdateText;
        FlatBtn btnUpdateNow, btnUpdateLater;

        // 경로
        Field fldWow;
        Label lblWowState;
        FlatBtn btnBrowseWow;

        // 버전 목록
        VersionList lstFlavors;

        // 내용
        TabStrip tabs;
        Panel pnlExport, pnlInstall, pnlAddons;
        Label lblExpDesc, lblInsDesc, lblInsInfo, lblHint;
        Field fldExportDir, fldSourceDir;
        FlatBtn btnExportDir, btnSourceDir, btnPrimary, btnOpenFolder;
        Check chkExpSettings, chkExpGraphics, chkInsSettings, chkInsClean, chkInsBackup;
        ListView lvAddons;

        // 상태
        Label lblStatus;
        Bar bar;
        TextBox txtLog;

        Thread worker;
        Settings settings = new Settings();
        ReleaseInfo pendingUpdate;
        bool logOpen;
        int tab;
        string lastOutputDir;

        const int LogH = 150;
        const int SideW = 232;

        public MainForm()
        {
            settings.Load();

            Text = AppInfo.Name;
            Font = Theme.Body;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(980, 650);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            try { Icon = LoadAppIcon(); }
            catch { }

            Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Color.FromArgb(38, 43, 52)))
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            };

            BuildStatus();
            BuildTitle();
            BuildSide();
            BuildMain();
            OrderDocks();

            SetTab(int.Parse(settings.Get("tab", "0")));
            Shown += delegate { Detect(false); StartSilentUpdateCheck(); };
            FormClosing += delegate { SaveSettings(); };
        }

        /// <summary>
        /// 도킹 순서를 못 박는다. WinForms 는 자식 색인이 클수록 먼저(바깥쪽에) 자리를 잡으므로,
        /// 제목 줄과 상태 줄이 사이드바 위아래를 가로지르게 하려면 색인이 더 커야 한다.
        /// </summary>
        void OrderDocks()
        {
            Controls.SetChildIndex(pnlMain, 0);
            Controls.SetChildIndex(pnlSide, 1);
            Controls.SetChildIndex(txtLog, 2);
            Controls.SetChildIndex(pnlStatus, 3);
            Controls.SetChildIndex(pnlTitle, 4);

            pnlMain.Controls.SetChildIndex(pnlContent, 0);
            pnlMain.Controls.SetChildIndex(pnlFooter, 1);
            pnlMain.Controls.SetChildIndex(tabs, 2);
            pnlMain.Controls.SetChildIndex(pnlPath, 3);
            pnlMain.Controls.SetChildIndex(pnlUpdate, 4);
        }

        static Icon LoadAppIcon()
        {
            System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream("app.ico"))
                if (s != null) return new Icon(s);
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }

        // ── 창 끌기 / 크기 조절 ──────────────────────────────────
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        const int WM_NCLBUTTONDOWN = 0xA1, WM_NCHITTEST = 0x84;
        const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11,
                  HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                  HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        void DragWindow()
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

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
                    const int gr = 5;
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
                }
                return;
            }
            base.WndProc(ref m);
        }

        // ── 제목 줄 ─────────────────────────────────────────────
        void BuildTitle()
        {
            pnlTitle = new Panel();
            pnlTitle.Dock = DockStyle.Top;
            pnlTitle.Height = 44;
            pnlTitle.BackColor = Theme.TitleBar;
            pnlTitle.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                if (Icon != null)
                {
                    Rectangle logo = new Rectangle(14, 11, 22, 22);
                    Theme.FillRound(g, logo, 5, Theme.Accent);
                    using (Icon ic = new Icon(Icon, 16, 16))
                        g.DrawIcon(ic, new Rectangle(logo.X + 3, logo.Y + 3, 16, 16));
                }
                TextRenderer.DrawText(g, AppInfo.Name, Theme.BodyBold,
                    new Rectangle(46, 0, 300, pnlTitle.Height), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                using (Pen p = new Pen(Theme.Line))
                    g.DrawLine(p, 0, pnlTitle.Height - 1, pnlTitle.Width, pnlTitle.Height - 1);
            };
            pnlTitle.MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) DragWindow();
            };
            Controls.Add(pnlTitle);

            btnClose = new SysBtn();
            btnClose.IsClose = true;
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Click += delegate { Close(); };
            pnlTitle.Controls.Add(btnClose);

            btnMin = new SysBtn();
            btnMin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            pnlTitle.Controls.Add(btnMin);

            pnlTitle.Resize += delegate { LayoutTitle(); };
            LayoutTitle();
        }

        void LayoutTitle()
        {
            btnClose.Location = new Point(pnlTitle.Width - btnClose.Width - 8, 8);
            btnMin.Location = new Point(btnClose.Left - btnMin.Width - 2, 8);
        }

        // ── 좌측 ────────────────────────────────────────────────
        void BuildSide()
        {
            pnlSide = new Panel();
            pnlSide.Dock = DockStyle.Left;
            pnlSide.Width = SideW;
            pnlSide.BackColor = Theme.Side;
            pnlSide.Paint += delegate (object s, PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, "게임 버전", Theme.Tiny,
                    new Rectangle(18, 16, 200, 16), Theme.TextMuted, TextFormatFlags.Left);
                using (Pen p = new Pen(Theme.Line))
                {
                    e.Graphics.DrawLine(p, pnlSide.Width - 1, 0, pnlSide.Width - 1, pnlSide.Height);
                    e.Graphics.DrawLine(p, 0, pnlSide.Height - 41, pnlSide.Width - 1, pnlSide.Height - 41);
                }
            };
            Controls.Add(pnlSide);

            lnkUpdate = new LinkText();
            lnkUpdate.Text = "v" + AppInfo.Version + " · 업데이트 확인";
            lnkUpdate.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lnkUpdate.BackColor = Theme.Side;
            lnkUpdate.Click += delegate { ManualUpdateCheck(); };
            pnlSide.Controls.Add(lnkUpdate);

            lstFlavors = new VersionList();
            lstFlavors.SelectedIndexChanged += delegate { UpdateHints(); RefreshAddons(); };
            lstFlavors.CheckChanged += delegate { UpdateHints(); };
            pnlSide.Controls.Add(lstFlavors);

            pnlSide.Resize += delegate { LayoutSide(); };
            LayoutSide();
        }

        void LayoutSide()
        {
            lstFlavors.Location = new Point(0, 38);
            lstFlavors.Size = new Size(pnlSide.Width - 1, Math.Max(60, pnlSide.Height - 38 - 42));
            lnkUpdate.Location = new Point(18, pnlSide.Height - 28);
        }

        // ── 우측 ────────────────────────────────────────────────
        void BuildMain()
        {
            pnlMain = new Panel();
            pnlMain.Dock = DockStyle.Fill;
            pnlMain.BackColor = Theme.Panel;
            Controls.Add(pnlMain);

            BuildFooter();
            BuildContent();
            BuildTabs();
            BuildPathRow();
            BuildUpdateBar();
        }

        void BuildUpdateBar()
        {
            pnlUpdate = new Panel();
            pnlUpdate.Dock = DockStyle.Top;
            pnlUpdate.Height = 44;
            pnlUpdate.Visible = false;
            pnlUpdate.BackColor = Color.FromArgb(17, 35, 54);
            pnlUpdate.Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.AccentLine))
                    e.Graphics.DrawLine(p, 0, pnlUpdate.Height - 1, pnlUpdate.Width, pnlUpdate.Height - 1);
            };
            pnlMain.Controls.Add(pnlUpdate);

            lblUpdateText = new Label();
            lblUpdateText.AutoSize = true;
            lblUpdateText.Location = new Point(18, 14);
            lblUpdateText.ForeColor = Theme.AccentText;
            lblUpdateText.BackColor = pnlUpdate.BackColor;
            pnlUpdate.Controls.Add(lblUpdateText);

            btnUpdateNow = new FlatBtn();
            btnUpdateNow.Text = "지금 업데이트";
            btnUpdateNow.Kind = BtnKind.Primary;
            btnUpdateNow.Size = new Size(112, 28);
            btnUpdateNow.BackColor = pnlUpdate.BackColor;
            btnUpdateNow.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnUpdateNow.Click += delegate { DoUpdate(); };
            pnlUpdate.Controls.Add(btnUpdateNow);

            btnUpdateLater = new FlatBtn();
            btnUpdateLater.Text = "나중에";
            btnUpdateLater.Size = new Size(72, 28);
            btnUpdateLater.BackColor = pnlUpdate.BackColor;
            btnUpdateLater.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnUpdateLater.Click += delegate { pnlUpdate.Visible = false; };
            pnlUpdate.Controls.Add(btnUpdateLater);

            pnlUpdate.Resize += delegate
            {
                btnUpdateLater.Location = new Point(pnlUpdate.Width - btnUpdateLater.Width - 16, 8);
                btnUpdateNow.Location = new Point(btnUpdateLater.Left - btnUpdateNow.Width - 8, 8);
            };
        }

        void BuildPathRow()
        {
            pnlPath = new Panel();
            pnlPath.Dock = DockStyle.Top;
            pnlPath.Height = 54;
            pnlPath.BackColor = Theme.Panel;
            pnlPath.Paint += delegate (object s, PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, "와우 위치", Theme.Small,
                    new Rectangle(18, 0, 70, pnlPath.Height), Theme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                using (Pen p = new Pen(Theme.Line))
                    e.Graphics.DrawLine(p, 0, pnlPath.Height - 1, pnlPath.Width, pnlPath.Height - 1);
            };
            pnlMain.Controls.Add(pnlPath);

            fldWow = new Field();
            fldWow.Box.ReadOnly = true;
            fldWow.Location = new Point(84, 11);
            pnlPath.Controls.Add(fldWow);

            lblWowState = new Label();
            lblWowState.AutoSize = true;
            lblWowState.Font = Theme.Small;
            lblWowState.ForeColor = Theme.Ok;
            lblWowState.BackColor = Theme.Panel;
            pnlPath.Controls.Add(lblWowState);

            btnBrowseWow = new FlatBtn();
            btnBrowseWow.Text = "변경";
            btnBrowseWow.Size = new Size(68, 32);
            btnBrowseWow.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseWow.Click += delegate { BrowseWow(); };
            pnlPath.Controls.Add(btnBrowseWow);

            pnlPath.Resize += delegate { LayoutPath(); };
        }

        void LayoutPath()
        {
            btnBrowseWow.Location = new Point(pnlPath.Width - btnBrowseWow.Width - 18, 11);
            lblWowState.Location = new Point(btnBrowseWow.Left - lblWowState.Width - 12, 20);
            fldWow.Size = new Size(Math.Max(120, lblWowState.Left - 12 - fldWow.Left), 32);
        }

        void BuildTabs()
        {
            tabs = new TabStrip();
            tabs.Dock = DockStyle.Top;
            tabs.Items = new string[] { "내보내기", "설치", "애드온 목록" };
            tabs.SelectedChanged += delegate { SetTab(tabs.Selected); };
            pnlMain.Controls.Add(tabs);
        }

        void BuildFooter()
        {
            pnlFooter = new Panel();
            pnlFooter.Dock = DockStyle.Bottom;
            pnlFooter.Height = 66;
            pnlFooter.BackColor = Theme.Panel;
            pnlFooter.Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Line))
                    e.Graphics.DrawLine(p, 0, 0, pnlFooter.Width, 0);
            };
            pnlMain.Controls.Add(pnlFooter);

            btnPrimary = new FlatBtn();
            btnPrimary.Kind = BtnKind.Primary;
            btnPrimary.Font = Theme.H2;
            btnPrimary.Size = new Size(168, 38);
            btnPrimary.Location = new Point(22, 14);
            btnPrimary.Click += delegate { if (tab == 0) StartExport(); else StartInstall(); };
            pnlFooter.Controls.Add(btnPrimary);

            btnOpenFolder = new FlatBtn();
            btnOpenFolder.Text = "폴더 열기";
            btnOpenFolder.Size = new Size(96, 38);
            btnOpenFolder.Location = new Point(200, 14);
            btnOpenFolder.Visible = false;
            btnOpenFolder.Click += delegate
            {
                if (lastOutputDir != null && Directory.Exists(lastOutputDir))
                    Updater.OpenUrl(lastOutputDir);
            };
            pnlFooter.Controls.Add(btnOpenFolder);

            lblHint = new Label();
            lblHint.AutoSize = false;
            lblHint.Font = Theme.Small;
            lblHint.ForeColor = Theme.TextMuted;
            lblHint.BackColor = Theme.Panel;
            lblHint.Location = new Point(206, 26);
            lblHint.Size = new Size(460, 18);
            pnlFooter.Controls.Add(lblHint);

            pnlFooter.Resize += delegate { LayoutFooter(); };
        }

        void LayoutFooter()
        {
            int x = btnPrimary.Right + 10;
            if (btnOpenFolder.Visible) { btnOpenFolder.Location = new Point(x, 14); x = btnOpenFolder.Right + 12; }
            lblHint.Location = new Point(x + 2, 26);
            lblHint.Width = Math.Max(80, pnlFooter.Width - x - 24);
        }

        void BuildContent()
        {
            pnlContent = new Panel();
            pnlContent.Dock = DockStyle.Fill;
            pnlContent.BackColor = Theme.Panel;
            pnlContent.Padding = new Padding(22, 20, 22, 10);
            pnlMain.Controls.Add(pnlContent);

            BuildExportPanel();
            BuildInstallPanel();
            BuildAddonsPanel();
        }

        Label Title(string text, Panel parent)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Font = Theme.H1;
            l.ForeColor = Theme.Text;
            l.BackColor = Theme.Panel;
            l.Location = new Point(0, 0);
            l.Size = new Size(400, 24);
            parent.Controls.Add(l);
            return l;
        }

        void Caption(string text, Point p, Panel parent)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = Theme.Tiny;
            l.Location = p;
            l.ForeColor = Theme.TextMuted;
            l.BackColor = Theme.Panel;
            parent.Controls.Add(l);
        }

        void BuildExportPanel()
        {
            pnlExport = new Panel();
            pnlExport.Dock = DockStyle.Fill;
            pnlExport.BackColor = Theme.Panel;
            pnlContent.Controls.Add(pnlExport);

            Title("내보내기", pnlExport);

            lblExpDesc = new Label();
            lblExpDesc.Text = "이 PC의 애드온과 설정을 한 폴더에 담습니다.\n" +
                              "그 폴더를 USB나 클라우드에 올려 두고, 다른 PC에서 '설치'로 가져오면 됩니다.";
            lblExpDesc.AutoSize = false;
            lblExpDesc.Location = new Point(0, 30);
            lblExpDesc.Size = new Size(620, 42);
            lblExpDesc.ForeColor = Theme.TextDim;
            lblExpDesc.BackColor = Theme.Panel;
            pnlExport.Controls.Add(lblExpDesc);

            Caption("저장 위치", new Point(0, 84), pnlExport);

            fldExportDir = new Field();
            fldExportDir.Location = new Point(0, 104);
            fldExportDir.Value = settings.Get("exportDir", AppInfo.DefaultExportRoot);
            EnableFolderDrop(fldExportDir);
            pnlExport.Controls.Add(fldExportDir);

            btnExportDir = new FlatBtn();
            btnExportDir.Text = "찾아보기";
            btnExportDir.Size = new Size(84, 32);
            btnExportDir.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnExportDir.Click += delegate
            {
                string d = PickFolder("애드온과 설정을 저장할 폴더", fldExportDir.Value);
                if (d != null) fldExportDir.Value = d;
            };
            pnlExport.Controls.Add(btnExportDir);

            chkExpSettings = MakeCheck("설정(WTF)도 함께 — 애드온 프로필 · 키바인드 · 매크로",
                new Point(0, 150), settings.GetBool("expSettings", true));
            chkExpSettings.CheckedChanged += delegate { chkExpGraphics.Enabled = chkExpSettings.Checked; };
            pnlExport.Controls.Add(chkExpSettings);

            chkExpGraphics = MakeCheck("해상도 · 그래픽 설정도 포함 (모니터가 다르면 권장하지 않음)",
                new Point(25, 178), settings.GetBool("expGraphics", false));
            chkExpGraphics.ForeColor = Theme.TextDim;
            pnlExport.Controls.Add(chkExpGraphics);

            pnlExport.Resize += delegate
            {
                int w = pnlExport.Width;
                lblExpDesc.Width = w;
                btnExportDir.Location = new Point(w - 84, 104);
                fldExportDir.Size = new Size(Math.Max(120, w - 84 - 8), 32);
            };
        }

        void BuildInstallPanel()
        {
            pnlInstall = new Panel();
            pnlInstall.Dock = DockStyle.Fill;
            pnlInstall.BackColor = Theme.Panel;
            pnlInstall.Visible = false;
            pnlContent.Controls.Add(pnlInstall);

            Title("설치", pnlInstall);

            lblInsDesc = new Label();
            lblInsDesc.Text = "내보내기로 만든 폴더를 골라 이 PC에 그대로 설치합니다.\n" +
                              "와우는 완전히 종료해 두세요.";
            lblInsDesc.AutoSize = false;
            lblInsDesc.Location = new Point(0, 30);
            lblInsDesc.Size = new Size(620, 42);
            lblInsDesc.ForeColor = Theme.TextDim;
            lblInsDesc.BackColor = Theme.Panel;
            pnlInstall.Controls.Add(lblInsDesc);

            Caption("가져올 폴더", new Point(0, 84), pnlInstall);

            fldSourceDir = new Field();
            fldSourceDir.Location = new Point(0, 104);
            fldSourceDir.Value = settings.Get("sourceDir", AppInfo.DefaultExportRoot);
            fldSourceDir.Box.TextChanged += delegate { ShowManifest(); };
            EnableFolderDrop(fldSourceDir);
            pnlInstall.Controls.Add(fldSourceDir);

            btnSourceDir = new FlatBtn();
            btnSourceDir.Text = "찾아보기";
            btnSourceDir.Size = new Size(84, 32);
            btnSourceDir.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSourceDir.Click += delegate
            {
                string d = PickFolder("내보내기로 만든 폴더 (manifest.json 이 있는 곳)", fldSourceDir.Value);
                if (d != null) fldSourceDir.Value = d;
            };
            pnlInstall.Controls.Add(btnSourceDir);

            lblInsInfo = new Label();
            lblInsInfo.AutoSize = false;
            lblInsInfo.Location = new Point(0, 142);
            lblInsInfo.Size = new Size(620, 18);
            lblInsInfo.Font = Theme.Small;
            lblInsInfo.ForeColor = Theme.TextMuted;
            lblInsInfo.BackColor = Theme.Panel;
            pnlInstall.Controls.Add(lblInsInfo);

            chkInsSettings = MakeCheck("설정(WTF)도 함께 설치", new Point(0, 170),
                settings.GetBool("insSettings", true));
            pnlInstall.Controls.Add(chkInsSettings);

            chkInsBackup = MakeCheck("설치 전에 지금 상태를 백업", new Point(0, 198),
                settings.GetBool("insBackup", true));
            pnlInstall.Controls.Add(chkInsBackup);

            chkInsClean = MakeCheck("기존 애드온을 모두 지우고 설치 (완전히 동일하게)",
                new Point(0, 226), settings.GetBool("insClean", false));
            chkInsClean.ForeColor = Theme.Danger;
            pnlInstall.Controls.Add(chkInsClean);

            pnlInstall.Resize += delegate
            {
                int w = pnlInstall.Width;
                lblInsDesc.Width = w;
                lblInsInfo.Width = w;
                btnSourceDir.Location = new Point(w - 84, 104);
                fldSourceDir.Size = new Size(Math.Max(120, w - 84 - 8), 32);
            };
        }

        void BuildAddonsPanel()
        {
            pnlAddons = new Panel();
            pnlAddons.Dock = DockStyle.Fill;
            pnlAddons.BackColor = Theme.Panel;
            pnlAddons.Visible = false;
            pnlContent.Controls.Add(pnlAddons);

            Title("애드온 목록", pnlAddons);

            lvAddons = new ListView();
            lvAddons.View = View.Details;
            lvAddons.FullRowSelect = true;
            lvAddons.BorderStyle = BorderStyle.None;
            lvAddons.BackColor = Theme.Field;
            lvAddons.ForeColor = Theme.Text;
            lvAddons.OwnerDraw = true;
            lvAddons.Location = new Point(0, 34);
            lvAddons.Columns.Add("애드온", 240);
            lvAddons.Columns.Add("폴더", 200);
            lvAddons.Columns.Add("버전", 100);
            lvAddons.Columns.Add("출처", 110);

            lvAddons.DrawColumnHeader += delegate (object s, DrawListViewColumnHeaderEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(22, 25, 31)))
                    e.Graphics.FillRectangle(b, e.Bounds);
                using (Pen p = new Pen(Theme.Line))
                    e.Graphics.DrawLine(p, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.Tiny,
                    new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                    Theme.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };
            lvAddons.DrawItem += delegate (object s, DrawListViewItemEventArgs e)
            {
                bool sel = (e.State & ListViewItemStates.Selected) != 0;
                using (SolidBrush b = new SolidBrush(sel ? Theme.RowSel
                        : e.ItemIndex % 2 == 0 ? Theme.Field : Color.FromArgb(15, 17, 22)))
                    e.Graphics.FillRectangle(b, e.Bounds);
            };
            lvAddons.DrawSubItem += delegate (object s, DrawListViewSubItemEventArgs e)
            {
                Color c = e.ColumnIndex == 0 ? Theme.Text : Theme.TextDim;
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Theme.Body,
                    new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                    c, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            pnlAddons.Controls.Add(lvAddons);

            pnlAddons.Resize += delegate
            {
                lvAddons.Size = new Size(pnlAddons.Width, Math.Max(60, pnlAddons.Height - 34));
                // 마지막 열을 남는 폭까지 늘려 머리글에 빈 칸이 생기지 않게 한다
                int used = 0;
                for (int i = 0; i < lvAddons.Columns.Count - 1; i++) used += lvAddons.Columns[i].Width;
                lvAddons.Columns[lvAddons.Columns.Count - 1].Width =
                    Math.Max(110, lvAddons.Width - used - 20);
            };
        }

        Check MakeCheck(string text, Point p, bool chk)
        {
            Check c = new Check();
            c.Text = text;
            c.Location = p;
            c.Size = new Size(460, 24);
            c.Checked = chk;
            return c;
        }

        void EnableFolderDrop(Field f)
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

        // ── 상태 줄 ─────────────────────────────────────────────
        void BuildStatus()
        {
            pnlStatus = new Panel();
            pnlStatus.Dock = DockStyle.Bottom;
            pnlStatus.Height = 32;
            pnlStatus.BackColor = Theme.TitleBar;
            pnlStatus.Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Line))
                    e.Graphics.DrawLine(p, 0, 0, pnlStatus.Width, 0);
            };
            Controls.Add(pnlStatus);

            lblStatus = new Label();
            lblStatus.Text = "준비됨";
            lblStatus.AutoSize = false;
            lblStatus.Font = Theme.Small;
            lblStatus.ForeColor = Theme.TextMuted;
            lblStatus.BackColor = Theme.TitleBar;
            lblStatus.Location = new Point(18, 9);
            lblStatus.Size = new Size(320, 16);
            pnlStatus.Controls.Add(lblStatus);

            bar = new Bar();
            bar.Location = new Point(350, 14);
            pnlStatus.Controls.Add(bar);

            lnkDetail = new LinkText();
            lnkDetail.Text = "자세히 ▾";
            lnkDetail.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lnkDetail.BackColor = Theme.TitleBar;
            lnkDetail.Click += delegate { ToggleLog(); };
            pnlStatus.Controls.Add(lnkDetail);

            txtLog = new TextBox();
            txtLog.Dock = DockStyle.Bottom;
            txtLog.Height = LogH;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Color.FromArgb(10, 12, 15);
            txtLog.ForeColor = Theme.TextDim;
            txtLog.Font = Theme.Mono;
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.Visible = false;
            Controls.Add(txtLog);

            pnlStatus.Resize += delegate
            {
                lnkDetail.Location = new Point(pnlStatus.Width - lnkDetail.Width - 18, 9);
                bar.Width = Math.Max(60, lnkDetail.Left - 350 - 20);
            };
        }

        void ToggleLog()
        {
            logOpen = !logOpen;
            lnkDetail.Text = logOpen ? "자세히 ▴" : "자세히 ▾";
            txtLog.Visible = logOpen;
            Height += logOpen ? LogH : -LogH;
        }

        void SetTab(int t)
        {
            if (t < 0 || t > 2) t = 0;
            tab = t;
            tabs.Selected = t;
            tabs.Invalidate();

            pnlExport.Visible = t == 0;
            pnlInstall.Visible = t == 1;
            pnlAddons.Visible = t == 2;
            if (t == 0) pnlExport.BringToFront();
            else if (t == 1) pnlInstall.BringToFront();
            else pnlAddons.BringToFront();

            btnPrimary.Visible = t != 2;
            btnPrimary.Text = t == 0 ? "내보내기 시작" : "설치 시작";
            btnOpenFolder.Visible = false;

            if (t == 1) ShowManifest();
            if (t == 2) RefreshAddons();
            UpdateHints();
            LayoutFooter();
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
                lblWowState.Text = "● 찾지 못함";
                lblWowState.ForeColor = Theme.Danger;
                Status("와우 설치 위치를 찾지 못했습니다. '변경'으로 지정하세요.");
                Log("자동 탐지 실패.");
                if (verbose) Info("와우 설치 위치를 찾지 못했습니다.\n'변경'을 눌러 폴더를 지정하세요.");
                LayoutPath();
                return;
            }

            fldWow.Value = p;
            lblWowState.Text = "● 찾음";
            lblWowState.ForeColor = Theme.Ok;
            Log("와우 위치: " + p);
            LoadFlavors();
            LayoutPath();
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
            lblWowState.Text = "● 찾음";
            lblWowState.ForeColor = Theme.Ok;
            LoadFlavors();
            LayoutPath();
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

            if (tab == 2) lblHint.Text = "";
            else if (sel.Count == 0) lblHint.Text = "왼쪽에서 게임 버전을 하나 이상 선택하세요.";
            else if (tab == 0) lblHint.Text = "선택한 " + sel.Count + "개 버전, 애드온 " + addons + "개를 묶습니다.";
            else lblHint.Text = "선택한 " + sel.Count + "개 버전에 설치합니다.";

            btnPrimary.Enabled = sel.Count > 0;
        }

        void ShowManifest()
        {
            if (lblInsInfo == null) return;
            try
            {
                ManifestInfo mi = WowCore.ReadManifest(fldSourceDir.Value);
                List<string> names = new List<string>();
                foreach (string k in mi.Flavors.Keys) names.Add(VersionList.FlavorName(k));
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
                foreach (string k in mi.Flavors.Keys) have.Add(VersionList.FlavorName(k));
                Info("설치할 버전이 없습니다.\n\n가져올 폴더에 들어있는 버전: "
                     + string.Join(", ", have.ToArray())
                     + "\n왼쪽 목록에서 같은 버전을 선택하세요.");
                return;
            }

            List<string> friendly = new List<string>();
            foreach (string n in names) friendly.Add(VersionList.FlavorName(n));

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
                            LayoutFooter();
                            LoadFlavors();
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
            tabs.Enabled = !busy;
            pnlExport.Enabled = !busy;
            pnlInstall.Enabled = !busy;
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
