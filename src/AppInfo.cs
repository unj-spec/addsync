using System;
using System.IO;
using System.Reflection;
using System.Text;

[assembly: AssemblyTitle("AddSync")]
[assembly: AssemblyDescription("와우 애드온과 설정을 다른 PC로 그대로 옮깁니다")]
[assembly: AssemblyProduct("AddSync")]
[assembly: AssemblyCompany("unj-spec")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion(AddSync.AppInfo.Version)]
[assembly: AssemblyFileVersion(AddSync.AppInfo.Version)]

namespace AddSync
{
    public static class AppInfo
    {
        /// <summary>이 값 하나만 올리면 설치 파일과 자동 업데이트 버전이 같이 올라간다</summary>
        public const string Version = "1.2.0";

        public const string Name = "AddSync";
        public const string Author = "made by Bowluvr";
        public const string RepoOwner = "unj-spec";

        // ── 구글 드라이브 연동 ──────────────────────────────────
        // 구글 클라우드 콘솔 → 사용자 인증 정보 → OAuth 클라이언트 ID(데스크톱 앱)
        // 에서 받은 값을 여기에 넣으면 클라우드 탭이 켜진다.
        // 데스크톱 앱의 보안 비밀번호는 구글 설계상 공개되는 값이다(PKCE 로 보호한다).
        // 빌드할 때 넣어 두면 사용자가 따로 설정할 필요가 없다. 비워 두면
        // 프로그램 안 '구글 연동 설정' 창에서 각자 입력할 수 있다.
        public const string BuiltInGoogleClientId = "";
        public const string BuiltInGoogleClientSecret = "";

        // 프로그램이 뜰 때 설정 파일에서 채워 넣는다
        public static string RuntimeGoogleClientId = "";
        public static string RuntimeGoogleClientSecret = "";

        public static string GoogleClientId
        {
            get
            {
                return RuntimeGoogleClientId.Length > 0
                    ? RuntimeGoogleClientId : BuiltInGoogleClientId;
            }
        }

        public static string GoogleClientSecret
        {
            get
            {
                return RuntimeGoogleClientSecret.Length > 0
                    ? RuntimeGoogleClientSecret : BuiltInGoogleClientSecret;
            }
        }

        public const string RepoName = "addsync";

        public static string RepoUrl
        {
            get { return "https://github.com/" + RepoOwner + "/" + RepoName; }
        }

        public static string ReleasesUrl
        {
            get { return RepoUrl + "/releases"; }
        }

        /// <summary>자료를 두는 폴더 이름 (예전 이름은 WowAddonManager 였다)</summary>
        public const string FolderName = "AddSync";
        const string OldFolderName = "WowAddonManager";

        /// <summary>
        /// 이름을 바꾸기 전에 쓰던 폴더가 있으면 한 번만 옮겨 온다.
        /// 설정·백업·받아 둔 꾸러미를 잃지 않게.
        /// </summary>
        static string Moved(Environment.SpecialFolder where, string tail)
        {
            string root = Environment.GetFolderPath(where);
            string now = Path.Combine(root, FolderName);
            string before = Path.Combine(root, OldFolderName);

            try
            {
                if (Directory.Exists(before))
                {
                    if (!Directory.Exists(now)) Directory.Move(before, now);
                    // 통째로 옮기지 못했으면(파일이 열려 있는 등) 하나씩 옮긴다
                    else CopyMissing(before, now);
                }
            }
            catch { }

            return tail.Length == 0 ? now : Path.Combine(now, tail);
        }

        /// <summary>새 폴더에 아직 없는 것만 옮겨 담는다</summary>
        static void CopyMissing(string from, string to)
        {
            string[] files;
            try { files = Directory.GetFiles(from, "*", SearchOption.AllDirectories); }
            catch { return; }

            foreach (string f in files)
            {
                try
                {
                    string rel = f.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar);
                    string g = Path.Combine(to, rel);
                    if (File.Exists(g)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(g));
                    File.Copy(f, g);
                }
                catch { }
            }
        }

        /// <summary>
        /// 설정에 저장된 예전 폴더 경로를 새 이름으로 바꿔 준다.
        /// (…\WowAddonManager\… → …\AddSync\…)
        /// </summary>
        public static string FixOldPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            string oldSeg = Path.DirectorySeparatorChar + OldFolderName + Path.DirectorySeparatorChar;
            string newSeg = Path.DirectorySeparatorChar + FolderName + Path.DirectorySeparatorChar;
            int i = p.IndexOf(oldSeg, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return p;
            return p.Substring(0, i) + newSeg + p.Substring(i + oldSeg.Length);
        }

        public static string DataDir
        {
            get
            {
                string d = Moved(Environment.SpecialFolder.ApplicationData, "");
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
                return Moved(Environment.SpecialFolder.MyDocuments, "backup");
            }
        }

        /// <summary>드라이브에서 받은 꾸러미를 두는 곳 (사용자 눈에 띌 필요 없다)</summary>
        public static string CloudCacheDir
        {
            get
            {
                string d = Moved(Environment.SpecialFolder.LocalApplicationData, "cloud");
                try { Directory.CreateDirectory(d); }
                catch { }
                return d;
            }
        }

        /// <summary>증분을 유지하려고 두는 작업 폴더 (사용자가 직접 열 일은 없다)</summary>
        public static string WorkExportDir
        {
            get
            {
                string d = Moved(Environment.SpecialFolder.LocalApplicationData, "work");
                try { Directory.CreateDirectory(d); }
                catch { }
                return d;
            }
        }

        /// <summary>'파일로 내보내기' 의 기본 파일 이름</summary>
        public static string DefaultBundleFile
        {
            get
            {
                return Moved(Environment.SpecialFolder.MyDocuments, "AddSync.wam");
            }
        }

        public static string DefaultExportRoot
        {
            get
            {
                return Moved(Environment.SpecialFolder.MyDocuments, "export");
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
