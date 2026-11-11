using System;
using System.IO;
using System.Reflection;
using System.Text;

[assembly: AssemblyTitle("WoW 애드온 매니저")]
[assembly: AssemblyDescription("와우 애드온과 설정을 다른 PC로 그대로 옮깁니다")]
[assembly: AssemblyProduct("WoW Addon Manager")]
[assembly: AssemblyCompany("unj-spec")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion(WowAddonManager.AppInfo.Version)]
[assembly: AssemblyFileVersion(WowAddonManager.AppInfo.Version)]

namespace WowAddonManager
{
    public static class AppInfo
    {
        /// <summary>이 값 하나만 올리면 설치 파일과 자동 업데이트 버전이 같이 올라간다</summary>
        public const string Version = "1.0.0";

        public const string Name = "WoW 애드온 매니저";
        public const string RepoOwner = "unj-spec";

        // ── 구글 드라이브 연동 ──────────────────────────────────
        // 구글 클라우드 콘솔 → 사용자 인증 정보 → OAuth 클라이언트 ID(데스크톱 앱)
        // 에서 받은 값을 여기에 넣으면 클라우드 탭이 켜진다.
        // 데스크톱 앱의 보안 비밀번호는 구글 설계상 공개되는 값이다(PKCE 로 보호한다).
        public const string GoogleClientId = "";
        public const string GoogleClientSecret = "";

        public const string RepoName = "wow-addon-manager";

        public static string RepoUrl
        {
            get { return "https://github.com/" + RepoOwner + "/" + RepoName; }
        }

        public static string ReleasesUrl
        {
            get { return RepoUrl + "/releases"; }
        }

        public static string DataDir
        {
            get
            {
                string d = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "WowAddonManager");
                try { Directory.CreateDirectory(d); }
                catch { }
                return d;
            }
        }

        /// <summary>설치 폴더가 쓰기 불가(Program Files)일 수 있으므로 백업은 내 문서 아래에 둔다</summary>
        public static string DefaultBackupRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    @"WowAddonManager\backup");
            }
        }

        public static string DefaultExportRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    @"WowAddonManager\export");
            }
        }
    }

    /// <summary>key=value 한 줄짜리 설정 파일</summary>
    public class Settings
    {
        readonly System.Collections.Generic.Dictionary<string, string> map =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string Path_ { get { return Path.Combine(AppInfo.DataDir, "settings.ini"); } }

        public void Load()
        {
            try
            {
                if (!File.Exists(Path_)) return;
                foreach (string line in File.ReadAllLines(Path_, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    map[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                StringBuilder b = new StringBuilder();
                foreach (System.Collections.Generic.KeyValuePair<string, string> kv in map)
                    b.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(Path_, b.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        public string Get(string key, string fallback)
        {
            string v;
            if (map.TryGetValue(key, out v) && v.Length > 0) return v;
            return fallback;
        }

        public bool GetBool(string key, bool fallback)
        {
            string v = Get(key, null);
            if (v == null) return fallback;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public void Set(string key, string value) { map[key] = value ?? ""; }
        public void Set(string key, bool value) { map[key] = value ? "1" : "0"; }
    }
}
