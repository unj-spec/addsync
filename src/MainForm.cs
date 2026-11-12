using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AddSync
{
    public class MainForm : Form
    {
        // 창
        Panel pnlTitle, pnlSide, pnlMain, pnlStatus, pnlPath, pnlFooter, pnlContent, pnlUpdate;
        SysBtn btnMin, btnMax, btnClose;
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
        Panel pnlExport, pnlInstall, pnlAddons, pnlSeg;
        Label lblExpDesc, lblInsDesc, lblInsInfo, lblHint;
        Field fldExportDir, fldSourceDir;
        FlatBtn btnExportDir, btnSourceDir, btnPrimary, btnOpenFolder;
        Check chkExpSettings, chkExpGraphics, chkDeep;
        Check chkInsSettings, chkInsClean, chkInsBackup, chkMapAccount, chkCloudClean;
        string srcAccountId = "", dstAccountId = "";
        DarkListView lvAddons;
        Panel pnlBackup;
        DarkListView lvBackup;
        Label lblBackupNote;
        Check chkRestoreSettings;
        FlatBtn btnRestore, btnDeleteBackup, btnBackupFolder;
        List<BackupInfo> backups = new List<BackupInfo>();
        Panel pnlCloud;
        Label lblCloudState, lblCloudDesc, lblCloudNote;
        FlatBtn btnSignIn, btnSignOut, btnUpload, btnDownload, btnCloudSetup;
        Check chkKeepSignIn;
        FlatBtn btnSync;
        Label lblSyncState;
        bool recordAfterJob;
        string recordRemoteAt;
        Check chkAutoUpload;
        bool pendingAutoUpload;
        SummaryCard cardExport, cardInstall;
        Field fldSearch;
        ToolTip tips = new ToolTip();
        readonly List<AddonInfo> allAddons = new List<AddonInfo>();
        readonly Dictionary<string, long> sizeCache = new Dictionary<string, long>();
        Thread sizeWorker;
        bool busy;

        // 상태
        Label lblStatus, lblMade;
        Bar bar;
        DarkTextBox txtLog;

        Thread worker;
        Settings settings = new Settings();
        ReleaseInfo pendingUpdate;
        bool logOpen;
        int tab;
        int fileMode;                 // 0 내보내기, 1 설치
        bool delivery;                // 애드온을 품은 '배달용' 실행 파일로 켜졌나
        FlatBtn btnSegExport, btnSegInstall;
        string lastOutputDir;

        /// <summary>
        /// 꾸러미를 만드는 규칙의 판. 규칙이 바뀌면(예: 설정에서 걸러내는 항목이 달라지면)
        /// 드라이브에 올라가 있는 것이 낡은 것이 되므로 다시 올리게 한다.
        /// </summary>
        const int BundleRule = 2;

        const int LogH = 150;
        const int SideW = 232;

        public MainForm()
        {
            settings.Load();
            AppInfo.RuntimeGoogleClientId = settings.Get("googleClientId", "");
            AppInfo.RuntimeGoogleClientSecret = settings.Get("googleClientSecret", "");
            GoogleDrive.Persist = settings.GetBool("keepSignIn", false);

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

            KeyPreview = true;
            KeyDown += delegate (object s2, KeyEventArgs e2)
            {
                if (e2.KeyCode == Keys.Escape && !busy) Close();
                else if (e2.KeyCode == Keys.F5) Detect(true);
            };

            tips.SetToolTip(btnBrowseWow, "와우 설치 폴더를 직접 고릅니다 (F5: 다시 찾기)");
            tips.SetToolTip(btnExportDir, "애드온과 설정을 저장할 폴더");
            tips.SetToolTip(btnSourceDir, "내보내기로 만든 폴더 (manifest.json 이 있는 곳)");
            tips.SetToolTip(chkExpGraphics, "켜면 해상도·화질까지 그대로 갑니다. 모니터가 다르면 끄세요.");
            tips.SetToolTip(chkInsClean, "대상 PC에만 있던 애드온까지 지웁니다.");
            tips.SetToolTip(fldSearch.Box, "애드온 이름이나 폴더명으로 거릅니다");

            int.TryParse(settings.Get("fileMode", "0"), out fileMode);
            SetTab(int.Parse(settings.Get("tab", "0")));
            delivery = Sync.HasPayload(Application.ExecutablePath);
            Shown += delegate
            {
                Detect(false);
                if (delivery) { BeginDelivery(); return; }
                StartSilentUpdateCheck();
            };
            FormClosing += delegate { SaveSettings(); CleanupOnExit(); };
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
                    // 아이콘 자체가 둥근 타일이라 따로 바탕을 깔지 않는다
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    using (Icon ic = new Icon(Icon, 32, 32))
                    using (Bitmap bm = ic.ToBitmap())
                        g.DrawImage(bm, new Rectangle(14, 10, 24, 24));
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
            btnClose.Kind = SysKind.Close;
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Click += delegate { Close(); };
            pnlTitle.Controls.Add(btnClose);

            btnMax = new SysBtn();
            btnMax.Kind = SysKind.Max;
            btnMax.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnMax.Click += delegate { ToggleMax(); };
            pnlTitle.Controls.Add(btnMax);

            btnMin = new SysBtn();
            btnMin.Kind = SysKind.Min;
            btnMin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            pnlTitle.Controls.Add(btnMin);

            pnlTitle.DoubleClick += delegate { ToggleMax(); };

            pnlTitle.Resize += delegate { LayoutTitle(); };
            LayoutTitle();
        }

        void LayoutTitle()
        {
            btnClose.Location = new Point(pnlTitle.Width - btnClose.Width - 8, 8);
            btnMax.Location = new Point(btnClose.Left - btnMax.Width - 2, 8);
            btnMin.Location = new Point(btnMax.Left - btnMin.Width - 2, 8);
        }

        /// <summary>최대화 / 복원. 테두리 없는 창이라 작업 표시줄을 덮지 않게 직접 범위를 잡는다</summary>
        void ToggleMax()
        {
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
                btnMax.Kind = SysKind.Max;
            }
            else
            {
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
                WindowState = FormWindowState.Maximized;
                btnMax.Kind = SysKind.Restore;
            }
            btnMax.Invalidate();
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
            tabs.Items = new string[] { "파일로 주고받기", "애드온 목록", "백업", "클라우드" };
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
            btnPrimary.Click += delegate { if (fileMode == 0) StartExport(); else StartInstall(); };
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
            pnlContent.Padding = new Padding(22, 8, 22, 8);
            pnlMain.Controls.Add(pnlContent);

            pnlSeg = new Panel();
            pnlSeg.Dock = DockStyle.Top;
            pnlSeg.Height = 38;
            pnlSeg.BackColor = Theme.Panel;
            pnlContent.Controls.Add(pnlSeg);

            btnSegExport = new FlatBtn();
            btnSegExport.Text = "내보내기";
            btnSegExport.Size = new Size(108, 28);
            btnSegExport.Location = new Point(0, 2);
            btnSegExport.Click += delegate { SetFileMode(0); };
            pnlSeg.Controls.Add(btnSegExport);

            btnSegInstall = new FlatBtn();
            btnSegInstall.Text = "설치";
            btnSegInstall.Size = new Size(108, 28);
            btnSegInstall.Location = new Point(114, 2);
            btnSegInstall.Click += delegate { SetFileMode(1); };
            pnlSeg.Controls.Add(btnSegInstall);

            BuildExportPanel();
            BuildInstallPanel();
            BuildAddonsPanel();
            BuildBackupPanel();
            BuildCloudPanel();

            // Dock 은 자식 색인이 클수록 바깥쪽에 자리를 잡는다 → 전환 띠가 가장 커야 위에 붙는다
            pnlContent.Controls.SetChildIndex(pnlSeg, pnlContent.Controls.Count - 1);
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

            Title("파일로 내보내기", pnlExport);

            lblExpDesc = new Label();
            lblExpDesc.Text = "애드온과 개인 설정을 파일 하나로 묶습니다. 그래픽과 해상도만 빠집니다.\n" +
                              "파일 이름을 .exe 로 하면, 받는 쪽은 설치 없이 더블클릭만 하면 됩니다.";
            lblExpDesc.AutoSize = false;
            lblExpDesc.Location = new Point(0, 30);
            lblExpDesc.Size = new Size(620, 42);
            lblExpDesc.ForeColor = Theme.TextDim;
            lblExpDesc.BackColor = Theme.Panel;
            pnlExport.Controls.Add(lblExpDesc);

            Caption("저장할 파일", new Point(0, 84), pnlExport);

            fldExportDir = new Field();
            fldExportDir.Location = new Point(0, 104);
            fldExportDir.Value = AppInfo.FixOldPath(settings.Get("bundleFile", AppInfo.DefaultBundleFile));
            EnableFolderDrop(fldExportDir);
            pnlExport.Controls.Add(fldExportDir);

            btnExportDir = new FlatBtn();
            btnExportDir.Text = "찾아보기";
            btnExportDir.Size = new Size(84, 32);
            btnExportDir.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnExportDir.Click += delegate
            {
                using (SaveFileDialog d = new SaveFileDialog())
                {
                    d.Title = "어디에 저장할까요?";
                    d.Filter = "애드온 꾸러미 (*" + Sync.BundleExt + ")|*" + Sync.BundleExt
                             + "|실행 파일 — 받는 쪽에 설치 불필요 (*.exe)|*.exe";
                    d.FilterIndex = fldExportDir.Value.EndsWith(".exe",
                        StringComparison.OrdinalIgnoreCase) ? 2 : 1;
                    d.FileName = Path.GetFileName(fldExportDir.Value);
                    try
                    {
                        string dir = Path.GetDirectoryName(fldExportDir.Value);
                        if (Directory.Exists(dir)) d.InitialDirectory = dir;
                    }
                    catch { }
                    if (d.ShowDialog(this) == DialogResult.OK) fldExportDir.Value = d.FileName;
                }
            };
            pnlExport.Controls.Add(btnExportDir);

            chkExpSettings = MakeCheck("개인 설정도 함께 — 애드온 프로필 · 키바인드 · 매크로 · 인터페이스 · 사운드",
                new Point(0, 150), settings.GetBool("expSettings", true));
            chkExpSettings.CheckedChanged += delegate { chkExpGraphics.Enabled = chkExpSettings.Checked; };
            pnlExport.Controls.Add(chkExpSettings);

            chkExpGraphics = MakeCheck("그래픽 · 해상도 설정까지 그대로 (모니터가 다르면 권장하지 않음)",
                new Point(25, 178), settings.GetBool("expGraphics", false));
            chkExpGraphics.ForeColor = Theme.TextDim;
            pnlExport.Controls.Add(chkExpGraphics);

            chkDeep = MakeCheck("정밀 검사 — 파일 내용까지 비교해 바뀐 애드온을 가려냅니다 (느림)",
                new Point(0, 206), settings.GetBool("deepCheck", false));
            chkDeep.ForeColor = Theme.TextDim;
            pnlExport.Controls.Add(chkDeep);

            cardExport = new SummaryCard();
            cardExport.Caption = "내보낼 내용";
            cardExport.Empty = "왼쪽에서 게임 버전을 선택하세요.";
            cardExport.Location = new Point(0, 242);
            pnlExport.Controls.Add(cardExport);

            pnlExport.Resize += delegate
            {
                int w = pnlExport.Width;
                lblExpDesc.Width = w;
                btnExportDir.Location = new Point(w - 84, 104);
                fldExportDir.Size = new Size(Math.Max(120, w - 84 - 8), 32);
                cardExport.Size = new Size(w, cardExport.NeededHeight);
            };
        }

        void BuildInstallPanel()
        {
            pnlInstall = new Panel();
            pnlInstall.Dock = DockStyle.Fill;
            pnlInstall.BackColor = Theme.Panel;
            pnlInstall.Visible = false;
            pnlContent.Controls.Add(pnlInstall);

            Title("파일에서 설치", pnlInstall);

            lblInsDesc = new Label();
            lblInsDesc.Text = "내보내기로 만든 파일을 골라 이 PC에 그대로 설치합니다.\n" +
                              "와우는 완전히 종료해 두세요.";
            lblInsDesc.AutoSize = false;
            lblInsDesc.Location = new Point(0, 30);
            lblInsDesc.Size = new Size(620, 42);
            lblInsDesc.ForeColor = Theme.TextDim;
            lblInsDesc.BackColor = Theme.Panel;
            pnlInstall.Controls.Add(lblInsDesc);

            Caption("가져올 파일", new Point(0, 84), pnlInstall);

            fldSourceDir = new Field();
            fldSourceDir.Location = new Point(0, 104);
            fldSourceDir.Value = AppInfo.FixOldPath(settings.Get("sourceFile", AppInfo.DefaultBundleFile));
            fldSourceDir.Box.TextChanged += delegate { ShowManifest(); };
            EnableFolderDrop(fldSourceDir);
            pnlInstall.Controls.Add(fldSourceDir);

            btnSourceDir = new FlatBtn();
            btnSourceDir.Text = "찾아보기";
            btnSourceDir.Size = new Size(84, 32);
            btnSourceDir.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSourceDir.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Title = "가져올 꾸러미 파일을 고르세요";
                    d.Filter = "애드온 꾸러미 (*" + Sync.BundleExt + ")|*" + Sync.BundleExt
                             + "|모든 파일 (*.*)|*.*";
                    try
                    {
                        string dir = Path.GetDirectoryName(fldSourceDir.Value);
                        if (Directory.Exists(dir)) d.InitialDirectory = dir;
                    }
                    catch { }
                    if (d.ShowDialog(this) == DialogResult.OK) fldSourceDir.Value = d.FileName;
                }
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

            chkInsSettings = MakeCheck("개인 설정도 함께 설치", new Point(0, 170),
                settings.GetBool("insSettings", true));
            pnlInstall.Controls.Add(chkInsSettings);

            chkInsBackup = MakeCheck("설치 전에 지금 상태를 백업", new Point(0, 198),
                settings.GetBool("insBackup", true));
            pnlInstall.Controls.Add(chkInsBackup);

            chkInsClean = MakeCheck("기존 애드온을 모두 지우고 설치 (완전히 동일하게)",
                new Point(0, 226), settings.GetBool("insClean", false));
            chkInsClean.ForeColor = Theme.Danger;
            chkInsClean.CheckedChanged += delegate
            {
                if (chkCloudClean != null && chkCloudClean.Checked != chkInsClean.Checked)
                    chkCloudClean.Checked = chkInsClean.Checked;
            };
            pnlInstall.Controls.Add(chkInsClean);

            chkMapAccount = MakeCheck("계정 폴더 번호 맞추기", new Point(0, 254), false);
            chkMapAccount.Visible = false;
            chkMapAccount.ForeColor = Theme.TextDim;
            pnlInstall.Controls.Add(chkMapAccount);

            cardInstall = new SummaryCard();
            cardInstall.Caption = "가져올 내용";
            cardInstall.Empty = "내보내기로 만든 .wam 파일을 고르세요.";
            cardInstall.Location = new Point(0, 286);
            pnlInstall.Controls.Add(cardInstall);

            pnlInstall.Resize += delegate
            {
                int w = pnlInstall.Width;
                lblInsDesc.Width = w;
                lblInsInfo.Width = w;
                btnSourceDir.Location = new Point(w - 84, 104);
                fldSourceDir.Size = new Size(Math.Max(120, w - 84 - 8), 32);
                cardInstall.Size = new Size(w, cardInstall.NeededHeight);
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

            lvAddons = new DarkListView();
            fldSearch = new Field();
            fldSearch.Size = new Size(220, 30);
            fldSearch.Box.Text = "";
            fldSearch.Box.TextChanged += delegate { ApplyAddonFilter(); };
            fldSearch.HandleCreated += delegate { fldSearch.SetPlaceholder("애드온 검색"); };
            pnlAddons.Controls.Add(fldSearch);

            lvAddons.Location = new Point(0, 44);
            lvAddons.Columns.Add("애드온", 240);
            lvAddons.Columns.Add("폴더", 200);
            lvAddons.Columns.Add("버전", 100);
            lvAddons.Columns.Add("출처", 110);

            lvAddons.HookDarkDraw(delegate (int i) { return i == 0 ? Theme.Text : Theme.TextDim; });
            pnlAddons.Controls.Add(lvAddons);

            pnlAddons.Resize += delegate
            {
                fldSearch.Location = new Point(pnlAddons.Width - fldSearch.Width, 0);
                lvAddons.Size = new Size(pnlAddons.Width, Math.Max(60, pnlAddons.Height - 44));
                lvAddons.StretchLastColumn();
            };
        }

        void BuildBackupPanel()
        {
            pnlBackup = new Panel();
            pnlBackup.Dock = DockStyle.Fill;
            pnlBackup.BackColor = Theme.Panel;
            pnlBackup.Visible = false;
            pnlContent.Controls.Add(pnlBackup);

            Title("백업", pnlBackup);

            lblBackupNote = new Label();
            lblBackupNote.AutoSize = false;
            lblBackupNote.Location = new Point(0, 30);
            lblBackupNote.Size = new Size(620, 20);
            lblBackupNote.Font = Theme.Small;
            lblBackupNote.ForeColor = Theme.TextDim;
            lblBackupNote.BackColor = Theme.Panel;
            pnlBackup.Controls.Add(lblBackupNote);

            lvBackup = new DarkListView();
            lvBackup.MultiSelect = false;
            lvBackup.Location = new Point(0, 60);
            lvBackup.Columns.Add("시각", 170);
            lvBackup.Columns.Add("포함된 버전", 300);
            lvBackup.Columns.Add("크기", 110);
            lvBackup.SelectedIndexChanged += delegate
            {
                btnRestore.Enabled = lvBackup.SelectedIndices.Count > 0;
                btnDeleteBackup.Enabled = btnRestore.Enabled;
            };
            lvBackup.HookDarkDraw(delegate (int i) { return i == 0 ? Theme.Text : Theme.TextDim; });
            pnlBackup.Controls.Add(lvBackup);

            chkRestoreSettings = MakeCheck("개인 설정도 함께 되돌리기", new Point(0, 0), true);
            chkRestoreSettings.Width = 220;
            pnlBackup.Controls.Add(chkRestoreSettings);

            btnRestore = new FlatBtn();
            btnRestore.Text = "되돌리기";
            btnRestore.Kind = BtnKind.Danger;
            btnRestore.Size = new Size(104, 32);
            btnRestore.Enabled = false;
            btnRestore.Click += delegate { RestoreSelected(); };
            pnlBackup.Controls.Add(btnRestore);

            btnDeleteBackup = new FlatBtn();
            btnDeleteBackup.Text = "삭제";
            btnDeleteBackup.Size = new Size(84, 32);
            btnDeleteBackup.Enabled = false;
            btnDeleteBackup.Click += delegate { DeleteSelectedBackup(); };
            pnlBackup.Controls.Add(btnDeleteBackup);

            btnBackupFolder = new FlatBtn();
            btnBackupFolder.Text = "폴더 열기";
            btnBackupFolder.Size = new Size(96, 32);
            btnBackupFolder.Click += delegate
            {
                try { Directory.CreateDirectory(AppInfo.DefaultBackupRoot); }
                catch { }
                Updater.OpenUrl(AppInfo.DefaultBackupRoot);
            };
            pnlBackup.Controls.Add(btnBackupFolder);

            pnlBackup.Resize += delegate
            {
                int w = pnlBackup.Width, h = pnlBackup.Height;
                lblBackupNote.Width = w;
                lvBackup.Size = new Size(w, Math.Max(80, h - 60 - 46));
                lvBackup.StretchLastColumn();
                int y = h - 36;
                chkRestoreSettings.Location = new Point(0, y + 4);
                btnRestore.Location = new Point(w - 104, y);
                btnDeleteBackup.Location = new Point(w - 104 - 92, y);
                btnBackupFolder.Location = new Point(w - 104 - 92 - 104, y);
            };
        }

        void BuildCloudPanel()
        {
            pnlCloud = new Panel();
            pnlCloud.Dock = DockStyle.Fill;
            pnlCloud.BackColor = Theme.Panel;
            pnlCloud.Visible = false;
            pnlContent.Controls.Add(pnlCloud);

            Title("구글 드라이브", pnlCloud);

            lblCloudDesc = new Label();
            lblCloudDesc.Text = "집에서 한 번 올려 두면, 어디서든 이 프로그램을 받아 구글 로그인만 하면 됩니다.\n"
                              + "그 PC의 와우를 찾아 애드온과 개인 설정을 그대로 넣어 줍니다.";
            lblCloudDesc.AutoSize = false;
            lblCloudDesc.Location = new Point(0, 30);
            lblCloudDesc.Size = new Size(620, 42);
            lblCloudDesc.ForeColor = Theme.TextDim;
            lblCloudDesc.BackColor = Theme.Panel;
            pnlCloud.Controls.Add(lblCloudDesc);

            lblCloudState = new Label();
            lblCloudState.AutoSize = false;
            lblCloudState.Location = new Point(0, 80);
            lblCloudState.Size = new Size(400, 22);
            lblCloudState.Font = Theme.BodyBold;
            lblCloudState.BackColor = Theme.Panel;
            pnlCloud.Controls.Add(lblCloudState);

            btnSignIn = new FlatBtn();
            btnSignIn.Text = "구글 로그인";
            btnSignIn.Kind = BtnKind.Primary;
            btnSignIn.Size = new Size(126, 34);
            btnSignIn.Location = new Point(0, 108);
            btnSignIn.Click += delegate { CloudSignIn(); };
            pnlCloud.Controls.Add(btnSignIn);

            btnSignOut = new FlatBtn();
            btnSignOut.Text = "로그아웃";
            btnSignOut.Size = new Size(96, 34);
            btnSignOut.Location = new Point(134, 108);
            btnSignOut.Click += delegate
            {
                GoogleDrive.SignOut();
                Status("구글 연결을 끊었습니다.");
                RefreshCloud();
            };
            pnlCloud.Controls.Add(btnSignOut);

            btnSync = new FlatBtn();
            btnSync.Text = "동기화";
            btnSync.Kind = BtnKind.Primary;
            btnSync.Font = Theme.H2;
            btnSync.Size = new Size(176, 44);
            btnSync.Location = new Point(0, 154);
            btnSync.Click += delegate { CloudSync(); };
            pnlCloud.Controls.Add(btnSync);

            lblSyncState = new Label();
            lblSyncState.AutoSize = false;
            lblSyncState.Location = new Point(188, 154);
            lblSyncState.Size = new Size(420, 44);
            lblSyncState.Font = Theme.Small;
            lblSyncState.ForeColor = Theme.TextMuted;
            lblSyncState.BackColor = Theme.Panel;
            pnlCloud.Controls.Add(lblSyncState);

            // 방향을 직접 정하고 싶을 때
            btnDownload = new FlatBtn();
            btnDownload.Text = "받아서 설치";
            btnDownload.Size = new Size(126, 32);
            btnDownload.Location = new Point(0, 208);
            btnDownload.Click += delegate { CloudPullAndInstall(); };
            pnlCloud.Controls.Add(btnDownload);

            btnUpload = new FlatBtn();
            btnUpload.Text = "올리기";
            btnUpload.Size = new Size(104, 32);
            btnUpload.Location = new Point(134, 208);
            btnUpload.Click += delegate { CloudExportAndUpload(); };
            pnlCloud.Controls.Add(btnUpload);

            btnCloudSetup = new FlatBtn();
            btnCloudSetup.Text = "구글 연동 설정";
            btnCloudSetup.Size = new Size(134, 34);
            btnCloudSetup.Location = new Point(0, 108);
            btnCloudSetup.Click += delegate { CloudSetup(); };
            pnlCloud.Controls.Add(btnCloudSetup);

            chkKeepSignIn = MakeCheck("이 PC에 로그인 유지 — 다음에 켤 때 바로 연결됩니다",
                new Point(0, 250), settings.GetBool("keepSignIn", false));
            chkKeepSignIn.CheckedChanged += delegate
            {
                GoogleDrive.Persist = chkKeepSignIn.Checked;
                settings.Set("keepSignIn", chkKeepSignIn.Checked);
                settings.Save();
                RefreshCloud();
            };
            pnlCloud.Controls.Add(chkKeepSignIn);

            chkAutoUpload = MakeCheck("내보내기가 끝나면 드라이브에 자동으로 올리기",
                new Point(0, 276), settings.GetBool("autoUpload", false));
            pnlCloud.Controls.Add(chkAutoUpload);

            // 받아서 설치할 때 쓸 설정. '설치' 쪽 같은 항목과 늘 같은 값을 본다.
            chkCloudClean = MakeCheck("받을 때 기존 애드온을 모두 지우고 설치 (완전히 동일하게)",
                new Point(0, 302), settings.GetBool("insClean", false));
            chkCloudClean.ForeColor = Theme.Danger;
            chkCloudClean.CheckedChanged += delegate
            {
                if (chkInsClean != null && chkInsClean.Checked != chkCloudClean.Checked)
                    chkInsClean.Checked = chkCloudClean.Checked;
                RefreshCloud();
            };
            pnlCloud.Controls.Add(chkCloudClean);

            lblCloudNote = new Label();
            lblCloudNote.AutoSize = false;
            lblCloudNote.Location = new Point(0, 334);
            lblCloudNote.Size = new Size(620, 76);
            lblCloudNote.Font = Theme.Small;
            lblCloudNote.ForeColor = Theme.TextMuted;
            lblCloudNote.BackColor = Theme.Panel;
            pnlCloud.Controls.Add(lblCloudNote);

            pnlCloud.Resize += delegate
            {
                int w = pnlCloud.Width;
                lblCloudDesc.Width = w;
                lblCloudNote.Width = w;
                // 안내 글은 늘 바닥에 붙여 둔다 (잘리지 않게)
                lblCloudNote.Location = new Point(0,
                    Math.Max(334, pnlCloud.Height - lblCloudNote.Height));
            };
        }

        void RefreshCloud()
        {
            if (lblCloudState == null) return;

            if (!GoogleDrive.IsConfigured)
            {
                lblCloudState.Text = "한 번만 설정하면 됩니다";
                lblCloudState.ForeColor = Theme.TextMuted;
                btnSignIn.Visible = btnSignOut.Visible = false;
                btnCloudSetup.Visible = true;
                btnSync.Enabled = false;
                lblSyncState.Text = "";
                btnUpload.Enabled = btnDownload.Enabled = chkAutoUpload.Enabled = false;
                chkCloudClean.Enabled = false;
                lblCloudNote.Text =
                    "구글이 '어느 프로그램이 요청하는지' 알아야 하기 때문에 OAuth 클라이언트가 필요합니다.\n"
                  + "'구글 연동 설정' 을 누르면 만드는 방법을 단계별로 알려 드립니다. 5분이면 끝납니다.\n"
                  + "로그인은 각자 본인 구글 계정으로 하고, 파일도 각자 본인 드라이브에 저장됩니다.";
                return;
            }

            bool on = GoogleDrive.IsSignedIn;
            lblCloudState.Text = on ? "내 구글 드라이브에 연결됨" : "연결되지 않음";
            lblCloudState.ForeColor = on ? Theme.Ok : Theme.TextMuted;
            btnCloudSetup.Visible = AppInfo.BuiltInGoogleClientId.Length == 0;
            btnCloudSetup.Location = new Point(236, 108);
            btnSignIn.Visible = true;
            btnSignOut.Visible = true;
            btnSignIn.Enabled = !on;
            btnSignOut.Enabled = on;
            btnUpload.Enabled = on;
            btnDownload.Enabled = on;
            btnSync.Enabled = on;
            chkAutoUpload.Enabled = on;
            chkCloudClean.Enabled = on;
            chkKeepSignIn.Enabled = true;

            string lastAt = settings.Get("syncRemoteAt", "");
            lblSyncState.Text = !on
                ? "로그인하면 동기화할 수 있습니다."
                : (lastAt.Length == 0
                    ? "아직 동기화한 적이 없습니다.\n누르면 어느 쪽이 최신인지 보고 알아서 올리거나 받습니다."
                    : "마지막 동기화: " + Pretty(lastAt)
                      + "\n누르면 어느 쪽이 최신인지 보고 알아서 올리거나 받습니다.");
            lblCloudNote.Text =
                "• 각자 본인 구글 계정으로 로그인하고, 파일은 본인 드라이브에 저장됩니다.\n"
              + "• 이 프로그램이 만든 파일만 접근합니다 (drive.file 권한). 다른 파일은 보지 않습니다.\n"
              + (chkCloudClean.Checked
                 ? "• 받을 때 이 PC에만 있던 애드온도 지웁니다. 설치 전 백업은 그대로 만들어집니다.\n"
                 : "• 받아도 이 PC에만 있던 애드온은 그대로 둡니다.\n")
              + (chkKeepSignIn.Checked
                 ? "• 로그인 상태가 이 PC에 저장됩니다. 공용 PC라면 꺼 두세요."
                 : "• 이 PC에 아무것도 남기지 않습니다. 프로그램을 끄면 로그인과 받은 파일이 지워집니다.");
        }

        void CloudSetup()
        {
            using (GoogleSetupForm dlg = new GoogleSetupForm(
                       AppInfo.RuntimeGoogleClientId, AppInfo.RuntimeGoogleClientSecret))
            {
                try { dlg.Icon = Icon; }
                catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                AppInfo.RuntimeGoogleClientId = dlg.ClientId;
                AppInfo.RuntimeGoogleClientSecret = dlg.ClientSecret;
                settings.Set("googleClientId", dlg.ClientId);
                settings.Set("googleClientSecret", dlg.ClientSecret);
                settings.Save();
                GoogleDrive.SignOut();
                Status("구글 연동 설정을 저장했습니다. 이제 로그인하세요.");
                RefreshCloud();
            }
        }

        void CloudSignIn() { CloudSignIn(null); }

        void CloudSignIn(Action next)
        {
            btnSignIn.Enabled = false;
            Status("구글 로그인 중…");
            Thread t = new Thread(delegate ()
            {
                string error = null;
                bool ok = false;
                try
                {
                    ok = GoogleDrive.SignIn(delegate (string msg)
                    {
                        try { BeginInvoke((MethodInvoker)delegate { Status(msg); }); }
                        catch { }
                    });
                }
                catch (Exception ex) { error = ex.Message; }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        RefreshCloud();
                        if (error != null) { Status("로그인 실패"); Info("로그인하지 못했습니다.\n\n" + error); }
                        else if (!ok) { Status("로그인 취소됨"); }
                        else
                        {
                            Status("구글 드라이브에 연결되었습니다.");
                            if (next != null) next();
                        }
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        static string Pretty(string iso)
        {
            DateTime t;
            if (DateTime.TryParse(iso, out t)) return t.ToString("yyyy-MM-dd HH:mm");
            return iso;
        }

        /// <summary>어느 쪽이 최신인지 보고 알아서 올리거나 받는다</summary>
        void CloudSync()
        {
            if (!GoogleDrive.IsConfigured) { CloudSetup(); return; }
            if (!CheckWow()) return;
            if (!GoogleDrive.IsSignedIn) { CloudSignIn(delegate { CloudSync(); }); return; }
            if (CheckedFlavors().Count == 0)
            {
                Info("왼쪽에서 게임 버전을 하나 이상 선택하세요.");
                return;
            }

            Status("드라이브 상태를 확인하는 중…");
            GoogleDrive.RemoteState[] box = new GoogleDrive.RemoteState[1];
            Run(delegate (Reporter rep) { box[0] = GoogleDrive.GetRemoteState(); },
                null, false, delegate { DecideSync(box[0]); });
        }

        void DecideSync(GoogleDrive.RemoteState remote)
        {
            List<Flavor> sel = CheckedFlavors();
            string localSig = Sync.LocalSignature(sel, chkExpSettings.Checked);
            string lastSig = settings.Get("syncLocalSig", "");
            string lastAt = settings.Get("syncRemoteAt", "");

            bool first = lastSig.Length == 0;
            bool localChanged = !first && localSig != lastSig;

            // 프로그램이 꾸러미 만드는 방식을 바꿨다면 올라가 있는 것은 낡은 것이다
            int lastRule;
            if (!int.TryParse(settings.Get("bundleRule", "1"), out lastRule)) lastRule = 1;
            bool ruleChanged = !first && lastRule != BundleRule;
            if (ruleChanged)
            {
                Log("꾸러미 규칙이 바뀌어 다시 올립니다 (" + lastRule + " → " + BundleRule + ")");
                localChanged = true;
            }
            bool remoteChanged = remote != null && remote.Exists && remote.ExportedAt != lastAt;

            // 드라이브가 비었으면 올릴 수밖에 없다
            if (remote == null || !remote.Exists)
            {
                Status("드라이브가 비어 있습니다. 이 PC 상태를 올립니다.");
                SyncUp();
                return;
            }

            if (first)
            {
                // 처음 동기화: 이 PC 와우가 비어 있으면 받는 게 맞다
                int addons = 0;
                foreach (Flavor f in sel) addons += f.AddOnCount;
                if (addons == 0) { SyncDown(remote); return; }

                DialogResult r = Msg.Ask(this, "어느 쪽을 기준으로 할까요?",
                    "이 PC와 드라이브 양쪽에 애드온이 있습니다.\n\n"
                    + "드라이브 — " + remote.SourcePC + " 에서 " + Pretty(remote.ExportedAt) + "\n"
                    + "이 PC — 애드온 " + addons + "개",
                    "드라이브 것을 받기", "이 PC 것을 올리기", "취소", MsgKind.Question);
                if (r == DialogResult.Yes) SyncDown(remote);
                else if (r == DialogResult.No) SyncUp();
                return;
            }

            if (localChanged && remoteChanged)
            {
                DialogResult r = Msg.Ask(this, "어느 쪽을 살릴까요?",
                    "양쪽 다 바뀌었습니다. 한쪽은 덮어쓰게 됩니다.\n\n"
                    + "드라이브 — " + remote.SourcePC + " 에서 " + Pretty(remote.ExportedAt) + "\n"
                    + "이 PC — 마지막 동기화 뒤 애드온이나 설정이 바뀜",
                    "드라이브 것으로 맞추기", "이 PC 것으로 맞추기", "취소", MsgKind.Warning);
                if (r == DialogResult.Yes) SyncDown(remote);
                else if (r == DialogResult.No) SyncUp();
                return;
            }

            if (remoteChanged) { SyncDown(remote); return; }
            if (localChanged) { SyncUp(); return; }

            Status("이미 최신입니다.");
            Info("이미 최신입니다.\n\n드라이브와 이 PC 가 같은 상태입니다.");
        }

        void SyncUp()
        {
            recordAfterJob = true;
            recordRemoteAt = null;          // 내보낸 manifest 에서 읽는다
            pendingAutoUpload = true;
            StartExport();
        }

        void SyncDown(GoogleDrive.RemoteState remote)
        {
            recordAfterJob = true;
            recordRemoteAt = remote.ExportedAt;
            CloudPullAndInstall();
        }

        /// <summary>동기화가 끝난 시점을 기록해 둔다 (다음에 방향을 판단하려고)</summary>
        void RecordSync()
        {
            string remoteAt = recordRemoteAt;
            if (remoteAt == null)
            {
                try { remoteAt = Sync.ReadManifest(AppInfo.WorkExportDir).ExportedAt; }
                catch { remoteAt = ""; }
            }
            settings.Set("syncRemoteAt", remoteAt);
            settings.Set("syncLocalSig",
                Sync.LocalSignature(CheckedFlavors(), chkExpSettings.Checked));
            settings.Set("bundleRule", BundleRule.ToString());
            settings.Save();
            RefreshCloud();
        }

        /// <summary>로그인 → 드라이브에서 받기 → 이 PC 와우에 설치, 한 번에</summary>
        void CloudPullAndInstall()
        {
            if (!GoogleDrive.IsConfigured) { CloudSetup(); return; }
            if (!CheckWow()) return;

            if (!GoogleDrive.IsSignedIn)
            {
                CloudSignIn(delegate { CloudPullAndInstall(); });
                return;
            }

            string cache = AppInfo.CloudCacheDir;
            Log("");
            Log("드라이브에서 받기 → " + cache);
            Status("드라이브에서 받는 중…");

            Run(delegate (Reporter rep) { GoogleDrive.DownloadFolder(cache, rep); },
                null, false, delegate
                {
                    fldSourceDir.Value = cache;
                    ShowManifest();

                    Manifest mi;
                    try { mi = Sync.ReadManifest(cache); }
                    catch (Exception ex) { Info(ex.Message); return; }

                    // 이 PC 에도 있는 버전만 고른다
                    List<string> names = new List<string>();
                    foreach (Flavor f in WowCore.GetFlavors(fldWow.Value))
                        if (mi.Flavors.ContainsKey(f.Name)) names.Add(f.Name);

                    if (names.Count == 0)
                    {
                        List<string> have = new List<string>();
                        foreach (string k in mi.Flavors.Keys) have.Add(VersionNames.Friendly(k));
                        Info("드라이브의 꾸러미에 든 버전(" + string.Join(", ", have.ToArray())
                             + ")이 이 PC에는 없습니다.");
                        return;
                    }

                    lstFlavors.CheckOnly(names);
                    UpdateHints();
                    SetFileMode(1);      // 파일 탭의 '설치' 쪽
                    StartInstall();
                });
        }

        /// <summary>이 PC 상태를 내보낸 뒤 드라이브에 올리기, 한 번에</summary>
        void CloudExportAndUpload()
        {
            if (!GoogleDrive.IsConfigured) { CloudSetup(); return; }
            if (!GoogleDrive.IsSignedIn)
            {
                CloudSignIn(delegate { CloudExportAndUpload(); });
                return;
            }
            if (CheckedFlavors().Count == 0)
            {
                Info("왼쪽에서 올릴 게임 버전을 하나 이상 선택하세요.");
                return;
            }
            recordAfterJob = true;
            recordRemoteAt = null;
            pendingAutoUpload = true;
            SetFileMode(0);      // 파일 탭의 '내보내기' 쪽
            StartExport();
        }

        void CloudUpload()
        {
            string dir = AppInfo.WorkExportDir;
            if (!File.Exists(Path.Combine(dir, Sync.ManifestName)))
            {
                Info("올릴 꾸러미가 없습니다.\n\n먼저 '내보내기'를 한 번 실행하세요.");
                return;
            }
            Log("");
            Log("드라이브에 올리기 — " + dir);
            Status("드라이브에 올리는 중…");
            Run(delegate (Reporter rep) { GoogleDrive.UploadFolder(dir, rep); },
                "드라이브에 올렸습니다.\n\n다른 PC에서 '클라우드 → 드라이브에서 받기' 를 누르면 됩니다.", false);
        }

        void CloudDownload()
        {
            string dir = AppInfo.CloudCacheDir;
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) { Info("폴더를 만들 수 없습니다.\n\n" + ex.Message); return; }

            Log("");
            Log("드라이브에서 받기 → " + dir);
            Status("드라이브에서 받는 중…");
            Run(delegate (Reporter rep) { GoogleDrive.DownloadFolder(dir, rep); },
                null, false,
                delegate
                {
                    fldSourceDir.Value = dir;    // 받아 둔 폴더를 설치 원본으로 삼는다
                    SetFileMode(1);
                    Info("드라이브에서 받았습니다.\n\n'파일로 주고받기 → 설치' 에서 설치를 진행하세요.");
                });
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
            bar.Visible = false;
            bar.Location = new Point(350, 14);
            pnlStatus.Controls.Add(bar);

            lblMade = new Label();
            lblMade.Text = AppInfo.Author;
            lblMade.AutoSize = true;
            lblMade.Font = Theme.Small;
            lblMade.ForeColor = Theme.TextOff;
            lblMade.BackColor = Theme.TitleBar;
            pnlStatus.Controls.Add(lblMade);

            lnkDetail = new LinkText();
            lnkDetail.Text = "자세히 ▾";
            lnkDetail.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lnkDetail.BackColor = Theme.TitleBar;
            lnkDetail.Click += delegate { ToggleLog(); };
            pnlStatus.Controls.Add(lnkDetail);

            txtLog = new DarkTextBox();
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
                lblMade.Location = new Point(lnkDetail.Left - lblMade.Width - 18, 9);
                bar.Width = Math.Max(60, lblMade.Left - 350 - 20);
            };
        }

        void ToggleLog()
        {
            logOpen = !logOpen;
            lnkDetail.Text = logOpen ? "자세히 ▴" : "자세히 ▾";
            txtLog.Visible = logOpen;
            Height += logOpen ? LogH : -LogH;
        }

        void SetFileMode(int m)
        {
            fileMode = m;
            settings.Set("fileMode", m.ToString());
            SetTab(0);
        }

        void SetTab(int t)
        {
            if (t < 0 || t > 3) t = 0;
            tab = t;
            tabs.Selected = t;
            tabs.Invalidate();

            bool file = t == 0;
            pnlExport.Visible = file && fileMode == 0;
            pnlInstall.Visible = file && fileMode == 1;
            pnlAddons.Visible = t == 1;
            pnlBackup.Visible = t == 2;
            pnlCloud.Visible = t == 3;

            if (file) { if (fileMode == 0) pnlExport.BringToFront(); else pnlInstall.BringToFront(); }
            else if (t == 1) pnlAddons.BringToFront();
            else if (t == 2) pnlBackup.BringToFront();
            else pnlCloud.BringToFront();

            // 배달용 실행 파일에서는 '내보내기' 를 보여 줄 이유가 없다
            if (pnlSeg != null) pnlSeg.Visible = file && !delivery;
            btnSegExport.Kind = fileMode == 0 ? BtnKind.Primary : BtnKind.Ghost;
            btnSegInstall.Kind = fileMode == 1 ? BtnKind.Primary : BtnKind.Ghost;
            btnSegExport.Invalidate();
            btnSegInstall.Invalidate();

            btnPrimary.Visible = file;
            btnPrimary.Kind = BtnKind.Primary;
            btnPrimary.Text = fileMode == 0 ? "파일로 내보내기" : "파일에서 설치";
            btnOpenFolder.Visible = false;

            if (file && fileMode == 1) ShowManifest();
            if (t == 1) RefreshAddons();
            if (t == 2) RefreshBackups();
            if (t == 3) RefreshCloud();
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
            allAddons.Clear();
            if (f != null) allAddons.AddRange(WowCore.GetAddons(f.AddOnsPath));
            ApplyAddonFilter();
        }

        void UpdateHints()
        {
            if (lblHint == null) return;
            List<Flavor> sel = CheckedFlavors();
            int addons = 0;
            foreach (Flavor f in sel) addons += f.AddOnCount;

            if (fldWow.Value.Length == 0) { lblHint.Text = ""; btnPrimary.Enabled = false; return; }

            if (tab != 0) lblHint.Text = "";
            else if (sel.Count == 0) lblHint.Text = "왼쪽에서 게임 버전을 하나 이상 선택하세요.";
            else if (fileMode == 0) lblHint.Text = "선택한 " + sel.Count + "개 버전, 애드온 " + addons + "개를 파일 하나로 묶습니다.";
            else lblHint.Text = "선택한 " + sel.Count + "개 버전에 설치합니다.";

            btnPrimary.Enabled = sel.Count > 0;
            UpdateExportSummary();
        }

        // ── 요약 카드 ───────────────────────────────────────────
        void UpdateExportSummary()
        {
            if (cardExport == null) return;
            List<Flavor> sel = CheckedFlavors();
            cardExport.Rows.Clear();

            long total = 0;
            bool pending = false;
            foreach (Flavor f in sel)
            {
                string size;
                long bytes;
                if (sizeCache.TryGetValue(f.Path, out bytes))
                {
                    size = WowCore.HumanSize(bytes);
                    total += bytes;
                }
                else { size = "계산 중…"; pending = true; }

                cardExport.Rows.Add(new string[] {
                    VersionList.FlavorName(f.Name), "애드온 " + f.AddOnCount + "개", size });
            }

            cardExport.FootLeft = sel.Count > 0 ? "원본 합계" : "";
            cardExport.FootRight = pending ? "계산 중…" : WowCore.HumanSize(total);
            cardExport.Size = new Size(pnlExport.Width, cardExport.NeededHeight);
            cardExport.Invalidate();

            if (pending) StartSizeScan(sel);
        }

        /// <summary>폴더 용량은 느리므로 뒤에서 재고 캐시해 둔다</summary>
        void StartSizeScan(List<Flavor> sel)
        {
            if (sizeWorker != null && sizeWorker.IsAlive) return;
            List<Flavor> todo = new List<Flavor>();
            foreach (Flavor f in sel) if (!sizeCache.ContainsKey(f.Path)) todo.Add(f);
            if (todo.Count == 0) return;

            sizeWorker = new Thread(delegate ()
            {
                foreach (Flavor f in todo)
                {
                    long n = WowCore.DirectorySize(f.AddOnsPath) + WowCore.DirectorySize(f.WtfPath);
                    Flavor captured = f;
                    long value = n;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            sizeCache[captured.Path] = value;
                            UpdateExportSummary();
                        });
                    }
                    catch { return; }
                }
            });
            sizeWorker.IsBackground = true;
            sizeWorker.Start();
        }

        void UpdateInstallSummary(Manifest mi)
        {
            if (cardInstall == null) return;
            cardInstall.Rows.Clear();
            long total = 0;

            if (mi != null)
            {
                foreach (KeyValuePair<string, FlavorPack> kv in mi.Flavors)
                {
                    FlavorPack p = kv.Value;
                    long bytes = p.WtfBytes;                 // 원본 기준 크기
                    foreach (AddonEntry e in p.Addons) bytes += e.Bytes;
                    total += bytes;
                    cardInstall.Rows.Add(new string[] {
                        VersionNames.Friendly(kv.Key),
                        "애드온 " + p.FolderCount + "개",
                        bytes > 0 ? WowCore.HumanSize(bytes) : "—" });
                }
            }

            long fileSize = 0;
            if (!SourceIsFolder)
                try { fileSize = new FileInfo(fldSourceDir.Value).Length; }
                catch { }
            cardInstall.FootLeft = cardInstall.Rows.Count > 0
                ? (fileSize > 0 ? "파일 크기 " + WowCore.HumanSize(fileSize) + " · 원본 합계"
                                : "원본 합계")
                : "";
            cardInstall.FootRight = WowCore.HumanSize(total);
            cardInstall.Size = new Size(pnlInstall.Width, cardInstall.NeededHeight);
            cardInstall.Invalidate();
        }

        /// <summary>꾸러미와 이 PC 의 배틀넷 계정 폴더 번호가 다른지 본다</summary>
        void CheckAccounts(Manifest mi)
        {
            srcAccountId = ""; dstAccountId = "";
            chkMapAccount.Visible = false;
            if (mi == null) return;

            foreach (Flavor f in CheckedFlavors())
            {
                FlavorPack p;
                if (!mi.Flavors.TryGetValue(f.Name, out p) || p.WtfZip.Length == 0) continue;

                List<string> src = SourceIsFolder
                    ? Sync.AccountsInZip(Path.Combine(fldSourceDir.Value,
                                                      p.WtfZip.Replace('/', '\\')))
                    : Sync.AccountsInBundle(fldSourceDir.Value, p.WtfZip);
                List<string> dst = Sync.AccountsOnDisk(Path.Combine(f.Path, "WTF"));
                if (src.Count != 1 || dst.Count != 1) continue;
                if (string.Equals(src[0], dst[0], StringComparison.OrdinalIgnoreCase)) continue;

                srcAccountId = src[0];
                dstAccountId = dst[0];
                chkMapAccount.Text = "계정 폴더 번호 맞추기 (" + srcAccountId + " → " + dstAccountId + ")";
                chkMapAccount.Visible = true;
                chkMapAccount.Checked = true;
                break;
            }
        }

        // ── 백업 ────────────────────────────────────────────────
        void RefreshBackups()
        {
            if (lvBackup == null) return;
            backups = Sync.ListBackups(AppInfo.DefaultBackupRoot);

            lvBackup.BeginUpdate();
            lvBackup.Items.Clear();
            foreach (BackupInfo b in backups)
            {
                List<string> names = new List<string>();
                foreach (string f in b.Flavors) names.Add(VersionNames.Friendly(f));
                ListViewItem it = new ListViewItem(b.When.ToString("yyyy-MM-dd HH:mm"));
                it.SubItems.Add(string.Join(", ", names.ToArray()));
                it.SubItems.Add(WowCore.HumanSize(b.Bytes));
                lvBackup.Items.Add(it);
            }
            lvBackup.EndUpdate();
            lvBackup.StretchLastColumn();

            long total = 0;
            foreach (BackupInfo b in backups) total += b.Bytes;
            lblBackupNote.Text = backups.Count == 0
                ? "아직 백업이 없습니다. 설치할 때마다 자동으로 만들어집니다."
                : backups.Count + "개 · " + WowCore.HumanSize(total)
                  + " · 최근 5개만 남기고 자동으로 정리합니다.";

            btnRestore.Enabled = lvBackup.SelectedIndices.Count > 0;
            btnDeleteBackup.Enabled = btnRestore.Enabled;
        }

        void RestoreSelected()
        {
            if (lvBackup.SelectedIndices.Count == 0) return;
            BackupInfo b = backups[lvBackup.SelectedIndices[0]];

            if (Msg.Ask(this, "되돌릴까요?",
                    b.When.ToString("yyyy-MM-dd HH:mm") + " 상태로 되돌립니다.\n\n"
                    + "지금 애드온은 이 백업의 내용으로 완전히 교체됩니다.\n"
                    + "와우는 완전히 종료해 두세요.",
                    "되돌리기", "취소", null, MsgKind.Warning) != DialogResult.Yes)
                return;

            BackupInfo target = b;
            bool settings = chkRestoreSettings.Checked;
            string wow = fldWow.Value;
            Log("");
            Log("되돌리기 — " + target.Dir);
            Status("되돌리는 중…");
            Run(delegate (Reporter rep) { Sync.RestoreBackup(target, wow, settings, rep); },
                "되돌리기가 끝났습니다.", false);
        }

        void DeleteSelectedBackup()
        {
            if (lvBackup.SelectedIndices.Count == 0) return;
            BackupInfo b = backups[lvBackup.SelectedIndices[0]];
            if (Msg.Ask(this, "백업을 지울까요?",
                    b.When.ToString("yyyy-MM-dd HH:mm") + " 백업을 지웁니다.\n되돌릴 수 없습니다.",
                    "지우기", "취소", null, MsgKind.Danger) != DialogResult.Yes)
                return;
            try { WowCore.DeleteTree(b.Dir); }
            catch (Exception ex) { Info("지우지 못했습니다.\n\n" + ex.Message); }
            RefreshBackups();
        }

        // ── 애드온 검색 ─────────────────────────────────────────
        void ApplyAddonFilter()
        {
            if (lvAddons == null) return;
            string q = fldSearch.Value.Trim().ToLowerInvariant();

            lvAddons.BeginUpdate();
            lvAddons.Items.Clear();
            foreach (AddonInfo a in allAddons)
            {
                if (q.Length > 0 &&
                    a.Title.ToLowerInvariant().IndexOf(q) < 0 &&
                    a.Folder.ToLowerInvariant().IndexOf(q) < 0) continue;

                string src = a.Curse != "" ? "CurseForge"
                           : a.Wago != "" ? "Wago"
                           : a.WowI != "" ? "WoWInterface" : "";
                ListViewItem it = new ListViewItem(a.Title);
                it.SubItems.Add(a.Folder);
                it.SubItems.Add(a.Version);
                it.SubItems.Add(src);
                lvAddons.Items.Add(it);
            }
            lvAddons.EndUpdate();
            lvAddons.StretchLastColumn();
        }

        /// <summary>
        /// 설치 원본은 '파일에서 설치' 의 .wam 일 수도 있고,
        /// 드라이브에서 받아 둔 폴더일 수도 있다.
        /// </summary>
        bool SourceIsFolder
        {
            get
            {
                try { return Directory.Exists(fldSourceDir.Value); }
                catch { return false; }
            }
        }

        Manifest ReadSourceManifest()
        {
            return SourceIsFolder
                ? Sync.ReadManifest(fldSourceDir.Value)
                : Sync.PeekBundle(fldSourceDir.Value);
        }

        void ShowManifest()
        {
            if (lblInsInfo == null) return;
            try
            {
                Manifest mi = ReadSourceManifest();
                List<string> names = new List<string>();
                foreach (string k in mi.Flavors.Keys) names.Add(VersionList.FlavorName(k));
                lblInsInfo.ForeColor = Theme.Ok;
                lblInsInfo.Text = mi.SourcePC + " 에서 내보냄 · " + string.Join(", ", names.ToArray())
                    + (mi.Settings ? " · 개인 설정 포함" : " · 설정 없음");
                UpdateInstallSummary(mi);
                CheckAccounts(mi);
            }
            catch (FileNotFoundException)
            {
                lblInsInfo.ForeColor = Theme.TextMuted;
                lblInsInfo.Text = "내보내기로 만든 " + Sync.BundleExt + " 파일을 고르세요.";
                UpdateInstallSummary(null);
                chkMapAccount.Visible = false;
            }
            catch (Exception ex)
            {
                lblInsInfo.ForeColor = Theme.Danger;
                lblInsInfo.Text = ex.Message;
                UpdateInstallSummary(null);
            }
        }

        // ── 작업 ────────────────────────────────────────────────
        void StartExport()
        {
            if (!CheckWow()) return;
            List<Flavor> sel = CheckedFlavors();
            if (sel.Count == 0) { Info("내보낼 버전을 왼쪽에서 선택하세요."); return; }

            string bundle = fldExportDir.Value.Trim();
            if (bundle.Length == 0) { Info("저장할 파일을 지정하세요."); return; }

            // 이름이 .exe 면 받는 쪽에 설치가 필요 없는 실행 파일을 만든다
            bool asExe = bundle.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            if (!asExe && !bundle.EndsWith(Sync.BundleExt, StringComparison.OrdinalIgnoreCase))
                bundle += Sync.BundleExt;
            fldExportDir.Value = bundle;

            if (asExe && Sync.HasPayload(Application.ExecutablePath))
            {
                Info("이 창은 애드온이 담긴 배달용 실행 파일입니다.\n"
                     + "실행 파일을 새로 만들려면 설치된 AddSync 에서 하세요.");
                return;
            }

            string outDir = AppInfo.WorkExportDir;    // 증분을 위해 작업 폴더는 그대로 둔다
            try { Directory.CreateDirectory(Path.GetDirectoryName(bundle)); }
            catch (Exception ex) { Info("저장 위치를 만들 수 없습니다.\n\n" + ex.Message); return; }

            ExportOptions o = new ExportOptions();
            o.WowPath = fldWow.Value;
            o.Flavors = sel;
            o.OutputDir = outDir;
            o.IncludeSettings = chkExpSettings.Checked;
            o.IncludeGraphicsConfig = chkExpSettings.Checked && chkExpGraphics.Checked;
            o.DeepCheck = chkDeep.Checked;

            long raw = 0;
            foreach (Flavor f in sel)
            {
                raw += WowCore.DirectorySize(f.AddOnsPath);
                if (o.IncludeSettings) raw += WowCore.DirectorySize(f.WtfPath);
            }

            // 동기화가 이미 올리기로 정해 둔 경우를 덮어쓰면 안 된다
            pendingAutoUpload = pendingAutoUpload
                || (chkAutoUpload != null && chkAutoUpload.Checked
                    && GoogleDrive.IsConfigured && GoogleDrive.IsSignedIn);
            lastOutputDir = Path.GetDirectoryName(bundle);
            string bundlePath = bundle;
            Log("");
            Log("내보내기 — 원본 " + WowCore.HumanSize(raw) + " → " + bundlePath);
            Status("내보내는 중…");

            string selfExe = Application.ExecutablePath;
            Run(delegate (Reporter rep)
                {
                    Sync.Export(o, rep);
                    if (pendingAutoUpload) return;   // 클라우드로 갈 때는 묶지 않는다

                    rep("파일 하나로 묶는 중…", 0);
                    string wam = asExe
                        ? Path.Combine(Path.GetTempPath(), "addsync-deliver" + Sync.BundleExt)
                        : bundlePath;
                    int n = Sync.PackBundle(outDir, wam, rep);

                    if (asExe)
                    {
                        Sync.PackSelfExtract(wam, bundlePath, selfExe, rep);
                        try { File.Delete(wam); }
                        catch { }
                    }
                    rep("묶음 " + n + " 파일 → " + Path.GetFileName(bundlePath)
                        + " (" + WowCore.HumanSize(new FileInfo(bundlePath).Length) + ")", 100);
                },
                asExe
                ? "실행 파일을 만들었습니다.\n\n이 파일 하나만 다른 PC로 옮겨 더블클릭하면,\n"
                  + "그 PC의 와우를 찾아 알아서 설치합니다.\n\n" + bundlePath
                : "내보내기가 끝났습니다.\n\n이 파일을 USB·구글드라이브·OneDrive에 두고,\n"
                  + "다른 PC에서 '설치'로 가져오면 됩니다.\n\n" + bundlePath,
                true);
        }

        void StartInstall()
        {
            if (!CheckWow()) return;
            Manifest mi;
            try { mi = ReadSourceManifest(); }
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

            // 폴더를 그대로 받았다면 풀 것이 없다
            bool fromFolder = SourceIsFolder;
            string unpacked = fromFolder
                ? fldSourceDir.Value
                : Path.Combine(AppInfo.CloudCacheDir, "manual");
            InstallOptions o = new InstallOptions();
            o.WowPath = fldWow.Value;
            o.SourceDir = unpacked;
            o.FlavorNames = names;
            o.IncludeSettings = chkInsSettings.Checked && mi.Settings;
            o.CleanInstall = chkInsClean.Checked;
            o.Backup = chkInsBackup.Checked;
            o.BackupRoot = AppInfo.DefaultBackupRoot;
            o.MapAccount = chkMapAccount.Visible && chkMapAccount.Checked;
            o.SourceAccountId = srcAccountId;
            o.TargetAccountId = dstAccountId;

            string bundleIn = fldSourceDir.Value;
            Log("");
            Log("설치 — " + bundleIn + " → " + o.WowPath);

            if (fromFolder) { ContinueInstall(o, mi); return; }

            // 미리보기는 꾸러미 안을 들여다봐야 하므로 먼저 푼다
            Status("파일 푸는 중…");
            Run(delegate (Reporter rep)
                {
                    rep("파일 푸는 중…", 0);
                    Sync.UnpackBundle(bundleIn, unpacked, rep);
                },
                null, false, delegate { ContinueInstall(o, mi); });
        }

        /// <summary>
        /// 애드온을 품은 실행 파일로 켜졌을 때. 받는 쪽은 설치해 둔 것이 없으므로
        /// 다른 화면은 감추고 '설치' 하나만 남긴다.
        /// </summary>
        void BeginDelivery()
        {
            tabs.Visible = false;
            SetFileMode(1);
            lblInsDesc.Text = "이 파일에 담긴 애드온과 개인 설정을 이 PC에 설치합니다.\n"
                            + "와우는 완전히 종료해 두세요.";
            fldSourceDir.Enabled = false;
            btnSourceDir.Visible = false;

            string selfExe = Application.ExecutablePath;
            string wam = Path.Combine(AppInfo.CloudCacheDir, "deliver" + Sync.BundleExt);
            Status("담긴 애드온을 꺼내는 중…");

            Run(delegate (Reporter rep) { Sync.ExtractPayload(selfExe, wam, rep); },
                null, false,
                delegate
                {
                    fldSourceDir.Value = wam;
                    ShowManifest();
                    try
                    {
                        Manifest mi = Sync.PeekBundle(wam);
                        List<string> names = new List<string>();
                        foreach (Flavor f in WowCore.GetFlavors(fldWow.Value))
                            if (mi.Flavors.ContainsKey(f.Name)) names.Add(f.Name);
                        if (names.Count > 0) { lstFlavors.CheckOnly(names); UpdateHints(); }
                    }
                    catch { }
                    Status("'파일에서 설치' 를 누르면 이 PC에 깔립니다.");
                });
        }

        /// <summary>꾸러미를 푼 뒤 — 무엇이 바뀌는지 보여주고 설치한다</summary>
        void ContinueInstall(InstallOptions o, Manifest mi)
        {
            InstallPlan plan;
            try { plan = Sync.Preview(o, mi); }
            catch (Exception ex) { Info("미리보기를 만들지 못했습니다.\n\n" + ex.Message); return; }

            using (PreviewForm dlg = new PreviewForm(plan, o))
            {
                try { dlg.Icon = Icon; }
                catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) { Status("취소했습니다."); return; }
            }

            lastOutputDir = o.BackupRoot;
            Status("설치하는 중…");
            Run(delegate (Reporter rep) { Sync.Install(o, rep); },
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
            Run(job, doneMessage, showOpenFolder, null);
        }

        /// <param name="next">성공했을 때 이어서 할 일 (완료 창 대신)</param>
        void Run(Job job, string doneMessage, bool showOpenFolder, Action next)
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
                        worker = null;   // 이어서 할 일이 '이미 진행 중'에 막히지 않게
                        SetBusy(false);
                        if (error != null)
                        {
                            bar.Value = 0;
                            recordAfterJob = false;
                            Log("실패: " + error);
                            Status("실패했습니다.");
                            Msg.Show(this, "작업 실패", error, MsgKind.Danger,
                                new string[] { "확인" }, new DialogResult[] { DialogResult.OK });
                        }
                        else
                        {
                            bar.Value = 100;
                            Log("완료.");
                            Status("완료되었습니다.");
                            btnOpenFolder.Visible = showOpenFolder;
                            LayoutFooter();
                            LoadFlavors();

                            if (pendingAutoUpload)
                            {
                                pendingAutoUpload = false;
                                CloudUpload();
                                return;
                            }
                            if (next != null) { next(); return; }
                            if (recordAfterJob) { recordAfterJob = false; RecordSync(); }
                            if (doneMessage != null) Msg.Info(this, "완료", doneMessage);
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
            if (pnlSeg != null) pnlSeg.Enabled = !busy;
            this.busy = busy;
            bar.Visible = busy;
            Cursor = busy ? Cursors.AppStarting : Cursors.Default;
            if (busy) { bar.Value = 0; btnOpenFolder.Visible = false; }
        }

        /// <summary>로그인을 유지하지 않기로 했다면 이 PC에 남은 것을 지운다</summary>
        void CleanupOnExit()
        {
            // 배달용 실행 파일에서 꺼낸 꾸러미는 253MB 쯤 되므로 꼭 치운다
            if (delivery)
            {
                try { WowCore.DeleteTree(AppInfo.CloudCacheDir); }
                catch { }
            }

            if (chkKeepSignIn != null && chkKeepSignIn.Checked) return;
            try { GoogleDrive.SignOut(); }
            catch { }
            try
            {
                if (Directory.Exists(AppInfo.CloudCacheDir))
                    WowCore.DeleteTree(AppInfo.CloudCacheDir);
            }
            catch { }
        }

        void SaveSettings()
        {
            settings.Set("wowPath", fldWow.Value);
            settings.Set("bundleFile", fldExportDir.Value);
            settings.Set("sourceFile", fldSourceDir.Value);
            settings.Set("fileMode", fileMode.ToString());
            settings.Set("expSettings", chkExpSettings.Checked);
            settings.Set("expGraphics", chkExpGraphics.Checked);
            settings.Set("deepCheck", chkDeep.Checked);
            settings.Set("autoUpload", chkAutoUpload != null && chkAutoUpload.Checked);
            settings.Set("keepSignIn", chkKeepSignIn != null && chkKeepSignIn.Checked);
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
            if (Msg.Ask(this, "업데이트할까요?",
                    "v" + pendingUpdate.Version + " 로 업데이트합니다." + notes +
                    "\n\n설치 파일을 받아 실행합니다. 프로그램은 자동으로 닫힙니다.",
                    "업데이트", "나중에", null, MsgKind.Info) != DialogResult.Yes)
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
            Msg.Info(this, AppInfo.Name, msg);
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
                if (Msg.Ask(this, "정말 닫을까요?",
                        "작업이 진행 중입니다. 지금 닫으면 하던 일이 중간에 끊깁니다.",
                        "닫기", "계속하기", null, MsgKind.Warning) != DialogResult.Yes)
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
