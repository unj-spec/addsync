using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WowAddonManager
{
    public enum MsgKind { Info, Question, Warning, Danger }

    /// <summary>
    /// 윈도우 기본 알림창 대신 쓰는, 프로그램과 같은 톤의 창.
    /// 본문 아래에 단추를 오른쪽으로 모아 두고, 첫 단추가 기본값이다.
    /// </summary>
    public class MsgForm : Form
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int wp, int lp);

        const int WM_NCLBUTTONDOWN = 0xA1;
        const int HTCAPTION = 2;

        const int Pad = 24;
        const int TitleH = 44;
        const int FootH = 62;
        const int IconW = 46;

        readonly string body;
        readonly MsgKind kind;
        readonly string caption;
        int bodyTop;

        public MsgForm(string caption, string body, MsgKind kind,
                       string[] labels, DialogResult[] results)
        {
            this.caption = caption ?? AppInfo.Name;
            this.body = body ?? "";
            this.kind = kind;

            Font = Theme.Body;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;

            int w = 460;
            int textW = w - Pad * 2 - IconW;
            Size m = TextRenderer.MeasureText(this.body, Theme.Body,
                        new Size(textW, 4000), TextFormatFlags.WordBreak);
            bodyTop = TitleH + 22;
            int h = bodyTop + Math.Max(40, m.Height) + 20 + FootH;
            ClientSize = new Size(w, Math.Min(h, 620));

            // ── 제목 줄 ─────────────────────────────────────────
            Panel title = new Panel();
            title.Dock = DockStyle.Top;
            title.Height = TitleH;
            title.BackColor = Theme.TitleBar;
            title.Paint += delegate (object s, PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, this.caption, Theme.BodyBold,
                    new Rectangle(18, 0, title.Width - 70, title.Height), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                using (Pen p = new Pen(Theme.Line))
                    e.Graphics.DrawLine(p, 0, title.Height - 1, title.Width, title.Height - 1);
            };
            title.MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            };
            Controls.Add(title);

            SysBtn close = new SysBtn();
            close.Kind = SysKind.Close;
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Click += delegate
            {
                DialogResult = results.Length > 0 ? results[results.Length - 1] : DialogResult.Cancel;
                Close();
            };
            title.Controls.Add(close);
            title.Resize += delegate
            {
                close.Location = new Point(title.Width - close.Width - 8, 8);
            };
            close.Location = new Point(title.Width - close.Width - 8, 8);

            // ── 바닥 (단추) ─────────────────────────────────────
            Panel foot = new Panel();
            foot.Dock = DockStyle.Bottom;
            foot.Height = FootH;
            foot.BackColor = Theme.Panel;
            foot.Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Line))
                    e.Graphics.DrawLine(p, 0, 0, foot.Width, 0);
            };
            Controls.Add(foot);

            List<FlatBtn> btns = new List<FlatBtn>();
            for (int i = 0; i < labels.Length; i++)
            {
                DialogResult r = results[i];
                FlatBtn b = new FlatBtn();
                b.Text = labels[i];
                b.Kind = i == 0 ? BtnKind.Primary : BtnKind.Ghost;
                b.Size = new Size(Math.Max(96, TextRenderer.MeasureText(
                                      labels[i], Theme.Body).Width + 36), 34);
                b.Click += delegate { DialogResult = r; Close(); };
                foot.Controls.Add(b);
                btns.Add(b);
            }

            EventHandler lay = delegate
            {
                int x = foot.Width - Pad;
                for (int i = btns.Count - 1; i >= 0; i--)
                {
                    x -= btns[i].Width;
                    btns[i].Location = new Point(x, (FootH - btns[i].Height) / 2);
                    x -= 8;
                }
            };
            foot.Resize += lay;
            lay(null, EventArgs.Empty);

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    DialogResult = results[results.Length - 1];
                    Close();
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    DialogResult = results[0];
                    Close();
                }
            };

            if (btns.Count > 0) btns[0].Select();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            Color c = kind == MsgKind.Danger ? Theme.Danger
                    : kind == MsgKind.Warning ? Color.FromArgb(226, 170, 62)
                    : Theme.Accent;
            string glyph = kind == MsgKind.Question ? "?"
                         : kind == MsgKind.Info ? "i" : "!";

            Rectangle dot = new Rectangle(Pad, bodyTop - 2, 28, 28);
            Theme.FillRound(g, dot, 14, Color.FromArgb(38, c.R, c.G, c.B));
            TextRenderer.DrawText(g, glyph, Theme.H2, dot, c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            Rectangle txt = new Rectangle(Pad + IconW, bodyTop,
                                          ClientSize.Width - Pad * 2 - IconW,
                                          ClientSize.Height - bodyTop - FootH - 14);
            TextRenderer.DrawText(g, body, Theme.Body, txt, Theme.TextSoft,
                TextFormatFlags.Left | TextFormatFlags.WordBreak);

            using (Pen p = new Pen(Theme.Line))
                g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    /// <summary>알림창을 띄우는 짧은 길</summary>
    public static class Msg
    {
        public static void Info(IWin32Window owner, string caption, string body)
        {
            Show(owner, caption, body, MsgKind.Info,
                 new string[] { "확인" }, new DialogResult[] { DialogResult.OK });
        }

        /// <summary>예 / 아니오 (필요하면 취소까지)</summary>
        public static DialogResult Ask(IWin32Window owner, string caption, string body,
                                       string yes, string no, string cancel, MsgKind kind)
        {
            List<string> labels = new List<string>();
            List<DialogResult> results = new List<DialogResult>();
            labels.Add(yes); results.Add(DialogResult.Yes);
            labels.Add(no); results.Add(DialogResult.No);
            if (cancel != null) { labels.Add(cancel); results.Add(DialogResult.Cancel); }
            return Show(owner, caption, body, kind, labels.ToArray(), results.ToArray());
        }

        public static DialogResult Show(IWin32Window owner, string caption, string body,
                                        MsgKind kind, string[] labels, DialogResult[] results)
        {
            using (MsgForm f = new MsgForm(caption, body, kind, labels, results))
            {
                Form parent = owner as Form;
                if (parent != null)
                {
                    try { f.Icon = parent.Icon; }
                    catch { }
                }
                return f.ShowDialog(owner);
            }
        }
    }
}
