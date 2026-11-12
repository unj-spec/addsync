using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AddSync
{
    public class ReleaseInfo
    {
        public string Version = "";      // 1.2.0
        public string Tag = "";          // v1.2.0
        public string InstallerUrl = ""; // .exe 자산 주소
        public string Notes = "";
        public string PageUrl = "";
    }

    /// <summary>
    /// 프로그램 자체의 업데이트. GitHub Releases 를 확인하고,
    /// 새 버전이 있으면 설치 파일을 받아 실행한다. (외부 라이브러리 없음)
    /// </summary>
    public static class Updater
    {
        const string ApiUrl =
            "https://api.github.com/repos/" + AppInfo.RepoOwner + "/" + AppInfo.RepoName + "/releases/latest";

        static void PrepareTls()
        {
            try
            {
                // .NET 4.0 로 빌드해도 런타임이 4.5+ 면 TLS 1.2 를 쓸 수 있다
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
            }
            catch { }
        }

        /// <summary>최신 릴리스 조회. 실패하면 null (조용히 넘어간다)</summary>
        public static ReleaseInfo CheckLatest()
        {
            PrepareTls();
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ApiUrl);
                // 사용자 정보는 보내지 않는다. 일반적인 클라이언트 이름만.
                req.UserAgent = "AddSync/" + AppInfo.Version;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 10000;
                req.ReadWriteTimeout = 10000;

                string json;
                using (WebResponse res = req.GetResponse())
                using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                    json = sr.ReadToEnd();

                ReleaseInfo r = new ReleaseInfo();
                r.Tag = Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                r.PageUrl = Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
                r.Notes = Unescape(Match(json, "\"body\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""));
                if (r.Tag.Length == 0) return null;
                r.Version = r.Tag.TrimStart('v', 'V');

                foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
                {
                    string url = m.Groups[1].Value;
                    if (!url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    // 설치 파일을 먼저 고른다 (포터블 exe 도 같이 올라가므로)
                    if (url.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0)
                    { r.InstallerUrl = url; break; }
                    if (r.InstallerUrl.Length == 0) r.InstallerUrl = url;
                }
                return r;
            }
            catch { return null; }
        }

        /// <summary>a 가 b 보다 높으면 1, 같으면 0, 낮으면 -1</summary>
        public static int CompareVersions(string a, string b)
        {
            string[] pa = (a ?? "").Split('.'), pb = (b ?? "").Split('.');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int va = Part(pa, i), vb = Part(pb, i);
                if (va != vb) return va > vb ? 1 : -1;
            }
            return 0;
        }

        static int Part(string[] parts, int i)
        {
            if (i >= parts.Length) return 0;
            Match m = Regex.Match(parts[i], "^\\d+");
            int v;
            if (m.Success && int.TryParse(m.Value, out v)) return v;
            return 0;
        }

        public static bool IsNewer(ReleaseInfo r)
        {
            return r != null && r.Version.Length > 0 && CompareVersions(r.Version, AppInfo.Version) > 0;
        }

        /// <summary>설치 파일을 받아 임시 폴더에 저장하고 경로를 돌려준다</summary>
        public static string Download(ReleaseInfo r, Action<int> progress)
        {
            PrepareTls();
            string dir = Path.Combine(Path.GetTempPath(), "AddSyncUpdate");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "AddSync-Setup-" + r.Version + ".exe");
            if (File.Exists(file)) { try { File.Delete(file); } catch { } }

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(r.InstallerUrl);
            req.UserAgent = "AddSync/" + AppInfo.Version;
            req.Timeout = 20000;

            using (WebResponse res = req.GetResponse())
            using (Stream input = res.GetResponseStream())
            using (FileStream output = File.Create(file))
            {
                long total = res.ContentLength, done = 0;
                byte[] buf = new byte[65536];
                int n;
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    output.Write(buf, 0, n);
                    done += n;
                    if (progress != null && total > 0)
                        progress((int)(done * 100 / total));
                }
            }
            return file;
        }

        /// <summary>설치 파일 실행 (관리자 권한 요청). 성공하면 이 프로그램은 종료해야 한다</summary>
        public static bool RunInstaller(string path)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(path);
                psi.UseShellExecute = true;
                psi.Verb = "runas";          // Program Files 에 쓰려면 관리자 권한이 필요하다
                Process.Start(psi);
                return true;
            }
            catch { return false; }         // 사용자가 UAC 를 취소한 경우 포함
        }

        public static void OpenUrl(string url)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(url);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { }
        }

        static string Match(string text, string pattern)
        {
            Match m = Regex.Match(text, pattern);
            return m.Success ? m.Groups[1].Value : "";
        }

        static string Unescape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\r\\n", "\n").Replace("\\n", "\n")
                    .Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}
