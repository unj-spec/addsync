using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AddSync
{
    /// <summary>설치하면 무엇이 바뀌는지 먼저 보여준다</summary>
    public class PreviewForm : Form
    {
        DarkListView lv;

        public PreviewForm(InstallPlan plan, InstallOptions o)
        {
            Text = "설치하면 이렇게 바뀝니다";
            Font = Theme.Body;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(600, 520);

            // ── 머리말 ──────────────────────────────────────────
            Panel head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 92;
            head.BackColor = Theme.Panel;
            head.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                TextRenderer.DrawText(g, "대상: " + o.WowPath, Theme.Small,
                    new Rectangle(20, 14, head.Width - 40, 18), Theme.TextMuted, TextFormatFlags.Left);

                int x = 20;
                x = Chip(g, x, "새로 설치", plan.Add.Count, Theme.Ok);
                x = Chip(g, x, "덮어쓰기", plan.Overwrite.Count, Theme.Accent);
                if (o.CleanInstall) x = Chip(g, x, "삭제", plan.Remove.Count, Theme.Danger);
                else x = Chip(g, x, "그대로 둠", plan.Keep.Count, Theme.TextMuted);

                using (Pen p = new Pen(Theme.Line))
                    g.DrawLine(p, 0, head.Height - 1, head.Width, head.Height - 1);
            };
            Controls.Add(head);

            // ── 목록 ────────────────────────────────────────────
            lv = new DarkListView();
            lv.Dock = DockStyle.Fill;
            lv.Columns.Add("상태", 110);
            lv.Columns.Add("애드온", 450);

            lv.HookDarkDraw(
                delegate (ListViewItem it, int i)
                {
                    if (i != 0) return Theme.Text;
                    return it.Tag is Color ? (Color)it.Tag : Theme.Text;
                },
                delegate (int i) { return i == 0 ? Theme.BodyBold : Theme.Body; });
            lv.Resize += delegate { lv.StretchLastColumn(); };
            Controls.Add(lv);
            lv.BringToFront();

            Add(plan.Add, "새로 설치", Theme.Ok);
            Add(plan.Overwrite, "덮어쓰기", Theme.Accent);
            if (o.CleanInstall) Add(plan.Remove, "삭제됨", Theme.Danger);
            else Add(plan.Keep, "그대로 둠", Theme.TextMuted);

            // ── 바닥 ────────────────────────────────────────────
            Panel foot = new Panel();
            foot.Dock = DockStyle.Bottom;
            foot.Height = 104;
            foot.BackColor = Theme.Panel;
            foot.Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Line))
                    e.Graphics.DrawLine(p, 0, 0, foot.Width, 0);

                List<string> notes = new List<string>();
                notes.Add(o.IncludeSettings
                    ? "• 개인 설정(키바인드 · 매크로 · 인터페이스)도 함께 덮어씁니다."
                    : "• 개인 설정은 건드리지 않습니다.");
                if (o.Backup) notes.Add("• 지금 상태는 백업해 둡니다. 언제든 되돌릴 수 있습니다.");
                else notes.Add("• 백업하지 않습니다.");
                if (o.MapAccount && o.SourceAccountId.Length > 0)
                    notes.Add("• 계정 폴더 " + o.SourceAccountId + " → " + o.TargetAccountId + " 로 바꿔 넣습니다.");
                else if (plan.SourceAccount.Length > 0 && plan.TargetAccount.Length > 0
                         && plan.SourceAccount != plan.TargetAccount)
                    notes.Add("• 계정 번호가 다릅니다 (" + plan.SourceAccount + " ≠ " + plan.TargetAccount
                              + "). 설정이 붙지 않을 수 있습니다.");

                int y = 12;
                foreach (string n in notes)
                {
                    TextRenderer.DrawText(e.Graphics, n, Theme.Small,
                        new Rectangle(20, y, foot.Width - 40, 18),
                        n.Contains("≠") ? Theme.Danger : Theme.TextDim, TextFormatFlags.Left);
                    y += 19;
                }
            };
            Controls.Add(foot);

            FlatBtn ok = new FlatBtn();
            ok.Text = "설치";
            ok.Kind = BtnKind.Primary;
            ok.Size = new Size(110, 34);
            ok.Location = new Point(ClientSize.Width - 250, foot.Height - 48);
            ok.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            foot.Controls.Add(ok);

            FlatBtn cancel = new FlatBtn();
            cancel.Text = "취소";
            cancel.Size = new Size(110, 34);
            cancel.Location = new Point(ClientSize.Width - 130, foot.Height - 48);
            cancel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            foot.Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        static int Chip(Graphics g, int x, string label, int count, Color color)
        {
            string text = label + " " + count;
            Size sz = TextRenderer.MeasureText(g, text, Theme.BodyBold);
            Rectangle r = new Rectangle(x, 42, sz.Width + 24, 30);
            Theme.DrawRound(g, r, 6, Color.FromArgb(24, 27, 34), Theme.Line);
            using (SolidBrush b = new SolidBrush(color))
                g.FillEllipse(b, r.X + 10, r.Y + 13, 6, 6);
            TextRenderer.DrawText(g, text, Theme.BodyBold,
                new Rectangle(r.X + 20, r.Y, r.Width - 24, r.Height),
                count > 0 ? Theme.Text : Theme.TextOff,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return r.Right + 8;
        }

        void Add(List<string> items, string state, Color color)
        {
            foreach (string f in items)
            {
                ListViewItem it = new ListViewItem(state);
                it.SubItems.Add(f);
                it.Tag = color;
                lv.Items.Add(it);
            }
        }
    }
}
