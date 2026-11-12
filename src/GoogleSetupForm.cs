using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace WowAddonManager
{
    /// <summary>
    /// 구글 연동 설정 — 사용자가 자기 OAuth 클라이언트 ID 를 넣는 창.
    /// (빌드할 때 AppInfo 에 넣어 뒀다면 이 창은 뜨지 않는다)
    /// </summary>
    public class GoogleSetupForm : Form
    {
        Field fldId, fldSecret;

        public string ClientId { get { return fldId.Value.Trim(); } }
        public string ClientSecret { get { return fldSecret.Value.Trim(); } }

        public GoogleSetupForm(string id, string secret)
        {
            Text = "구글 연동 설정";
            Font = Theme.Body;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 420);

            Label title = new Label();
            title.Text = "구글 드라이브를 쓰려면 OAuth 클라이언트가 필요합니다";
            title.Font = Theme.H1;
            title.ForeColor = Theme.Text;
            title.BackColor = Theme.Panel;
            title.AutoSize = false;
            title.Location = new Point(22, 20);
            title.Size = new Size(520, 24);
            Controls.Add(title);

            Label steps = new Label();
            steps.Text =
                "구글이 '어느 프로그램이 요청하는지' 알아야 하기 때문입니다.\n" +
                "본인 구글 계정의 드라이브에 접근하는 것과는 별개이고, 한 번만 만들면 됩니다.\n" +
                "\n" +
                "1.  console.cloud.google.com 접속 → 새 프로젝트 만들기\n" +
                "2.  API 및 서비스 → 라이브러리 → 'Google Drive API' 사용 설정\n" +
                "3.  OAuth 동의 화면 → 외부 → 앱 이름과 지원 이메일 입력\n" +
                "4.  사용자 인증 정보 → OAuth 클라이언트 ID → 유형 '데스크톱 앱'\n" +
                "5.  나온 두 값을 아래에 붙여 넣기";
            steps.Font = Theme.Body;
            steps.ForeColor = Theme.TextDim;
            steps.BackColor = Theme.Panel;
            steps.AutoSize = false;
            steps.Location = new Point(22, 52);
            steps.Size = new Size(520, 150);
            Controls.Add(steps);

            FlatBtn open = new FlatBtn();
            open.Text = "구글 클라우드 콘솔 열기";
            open.Size = new Size(190, 32);
            open.Location = new Point(22, 206);
            open.Click += delegate { Updater.OpenUrl("https://console.cloud.google.com/apis/credentials"); };
            Controls.Add(open);

            FlatBtn load = new FlatBtn();
            load.Text = "받은 JSON 파일에서 불러오기";
            load.Kind = BtnKind.Primary;
            load.Size = new Size(210, 32);
            load.Location = new Point(222, 206);
            load.Click += delegate { LoadFromJson(); };
            Controls.Add(load);

            Label l1 = Caption("클라이언트 ID", new Point(22, 254));
            fldId = new Field();
            fldId.Location = new Point(22, 274);
            fldId.Size = new Size(516, 32);
            fldId.Value = id;
            Controls.Add(fldId);

            Label l2 = Caption("클라이언트 보안 비밀번호", new Point(22, 316));
            fldSecret = new Field();
            fldSecret.Location = new Point(22, 336);
            fldSecret.Size = new Size(516, 32);
            fldSecret.Value = secret;
            Controls.Add(fldSecret);

            FlatBtn ok = new FlatBtn();
            ok.Text = "저장";
            ok.Kind = BtnKind.Primary;
            ok.Size = new Size(100, 34);
            ok.Location = new Point(ClientSize.Width - 226, 374);
            ok.Click += delegate
            {
                if (ClientId.Length == 0)
                {
                    Msg.Info(this, AppInfo.Name, "클라이언트 ID 를 입력하세요.");
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            FlatBtn cancel = new FlatBtn();
            cancel.Text = "취소";
            cancel.Size = new Size(100, 34);
            cancel.Location = new Point(ClientSize.Width - 116, 374);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>
        /// 콘솔에서 받은 client_secret_*.json 을 그대로 읽어 두 칸을 채운다.
        /// 사람이 긴 문자열을 옮겨 적다 틀리는 일을 없앤다.
        /// </summary>
        void LoadFromJson()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "구글 콘솔에서 받은 JSON 파일을 고르세요";
                d.Filter = "클라이언트 정보 (client_secret*.json)|client_secret*.json|JSON 파일 (*.json)|*.json";
                try
                {
                    string dl = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    if (Directory.Exists(dl)) d.InitialDirectory = dl;
                }
                catch { }

                if (d.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    Dictionary<string, object> root =
                        Json.Obj(Json.Parse(File.ReadAllText(d.FileName, Encoding.UTF8)));
                    Dictionary<string, object> node = null;
                    if (root != null)
                    {
                        object v;
                        if (root.TryGetValue("installed", out v)) node = Json.Obj(v);
                        else if (root.TryGetValue("web", out v)) node = Json.Obj(v);
                        else node = root;
                    }

                    string id = Json.Str(node, "client_id", "");
                    string secret = Json.Str(node, "client_secret", "");
                    if (id.Length == 0)
                    {
                        Msg.Show(this, AppInfo.Name,
                            "그 파일에서 클라이언트 ID 를 찾지 못했습니다.\n\n" +
                            "구글 콘솔에서 'JSON 다운로드' 로 받은 파일을 고르세요.",
                            MsgKind.Warning,
                            new string[] { "확인" }, new DialogResult[] { DialogResult.OK });
                        return;
                    }

                    fldId.Value = id;
                    fldSecret.Value = secret;
                }
                catch (Exception ex)
                {
                    Msg.Show(this, AppInfo.Name, "파일을 읽지 못했습니다.\n\n" + ex.Message,
                        MsgKind.Danger,
                        new string[] { "확인" }, new DialogResult[] { DialogResult.OK });
                }
            }
        }

        Label Caption(string text, Point p)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = Theme.Tiny;
            l.Location = p;
            l.ForeColor = Theme.TextMuted;
            l.BackColor = Theme.Panel;
            Controls.Add(l);
            return l;
        }
    }
}
