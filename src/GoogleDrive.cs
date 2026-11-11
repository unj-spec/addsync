using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace WowAddonManager
{
    public class DriveFile
    {
        public string Id = "", Name = "", MimeType = "";
        public long Size;
        public string Modified = "";
        public bool IsFolder { get { return MimeType == "application/vnd.google-apps.folder"; } }
    }

    /// <summary>
    /// 구글 드라이브 연동. 외부 라이브러리 없이 OAuth 2.0 데스크톱 흐름(PKCE)과
    /// Drive v3 REST 를 직접 쓴다.
    ///
    /// 권한 범위는 drive.file — **이 프로그램이 만든 파일만** 볼 수 있다.
    /// 사용자의 다른 드라이브 파일에는 접근하지 않는다.
    /// </summary>
    public static class GoogleDrive
    {
        public const string Scope = "https://www.googleapis.com/auth/drive.file";
        const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
        const string TokenUrl = "https://oauth2.googleapis.com/token";
        const string ApiFiles = "https://www.googleapis.com/drive/v3/files";
        const string UploadFiles = "https://www.googleapis.com/upload/drive/v3/files";
        public const string FolderName = "WoW 애드온 매니저";

        static string accessToken = "";
        static DateTime accessExpires = DateTime.MinValue;

        public static bool IsConfigured
        {
            get { return AppInfo.GoogleClientId.Length > 0; }
        }

        public static bool IsSignedIn
        {
            get { return LoadRefreshToken().Length > 0; }
        }

        public static string AccountEmail = "";

        // ── 토큰 보관 (DPAPI 로 현재 사용자만 풀 수 있게) ────────
        static string TokenPath
        {
            get { return Path.Combine(AppInfo.DataDir, "google.dat"); }
        }

        static void SaveRefreshToken(string token)
        {
            try
            {
                if (token.Length == 0) { if (File.Exists(TokenPath)) File.Delete(TokenPath); return; }
                byte[] data = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(TokenPath, data);
            }
            catch { }
        }

        static string LoadRefreshToken()
        {
            try
            {
                if (!File.Exists(TokenPath)) return "";
                byte[] data = ProtectedData.Unprotect(
                    File.ReadAllBytes(TokenPath), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch { return ""; }
        }

        public static void SignOut()
        {
            SaveRefreshToken("");
            accessToken = "";
            accessExpires = DateTime.MinValue;
            AccountEmail = "";
        }

        // ── 로그인 ──────────────────────────────────────────────
        /// <summary>브라우저를 띄워 로그인시킨다. 성공하면 true.</summary>
        public static bool SignIn(Action<string> status)
        {
            if (!IsConfigured)
                throw new InvalidOperationException(
                    "구글 연동이 설정되지 않았습니다.\n" +
                    "구글 클라우드 콘솔에서 만든 OAuth 클라이언트 ID 가 필요합니다.");

            Tls.Prepare();

            string verifier = RandomUrlSafe(64);
            string challenge = Base64Url(SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            string state = RandomUrlSafe(16);

            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string redirect = "http://127.0.0.1:" + port;

            string url = AuthUrl
                + "?client_id=" + Uri.EscapeDataString(AppInfo.GoogleClientId)
                + "&redirect_uri=" + Uri.EscapeDataString(redirect)
                + "&response_type=code"
                + "&scope=" + Uri.EscapeDataString(Scope)
                + "&code_challenge=" + challenge
                + "&code_challenge_method=S256"
                + "&access_type=offline&prompt=consent"
                + "&state=" + state;

            status("브라우저에서 구글 로그인을 마쳐 주세요…");
            Updater.OpenUrl(url);

            string code = WaitForCode(listener, state, 180);
            listener.Stop();
            if (code.Length == 0) return false;

            status("토큰을 받는 중…");
            string body = "code=" + Uri.EscapeDataString(code)
                + "&client_id=" + Uri.EscapeDataString(AppInfo.GoogleClientId)
                + "&client_secret=" + Uri.EscapeDataString(AppInfo.GoogleClientSecret)
                + "&redirect_uri=" + Uri.EscapeDataString(redirect)
                + "&grant_type=authorization_code"
                + "&code_verifier=" + verifier;

            Dictionary<string, object> res = Json.Obj(Json.Parse(PostForm(TokenUrl, body)));
            string refresh = Json.Str(res, "refresh_token", "");
            accessToken = Json.Str(res, "access_token", "");
            accessExpires = DateTime.UtcNow.AddSeconds(Json.Num(res, "expires_in", 3000) - 60);

            if (refresh.Length == 0 || accessToken.Length == 0) return false;
            SaveRefreshToken(refresh);
            return true;
        }

        /// <summary>로컬 루프백으로 돌아오는 인증 코드를 기다린다</summary>
        static string WaitForCode(TcpListener listener, string state, int timeoutSec)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(timeoutSec);
            while (DateTime.UtcNow < until)
            {
                if (!listener.Pending()) { System.Threading.Thread.Sleep(150); continue; }

                using (TcpClient client = listener.AcceptTcpClient())
                using (NetworkStream ns = client.GetStream())
                {
                    byte[] buf = new byte[8192];
                    int n = ns.Read(buf, 0, buf.Length);
                    string req = Encoding.UTF8.GetString(buf, 0, n);

                    string code = "", gotState = "";
                    int q = req.IndexOf('?');
                    int sp = req.IndexOf(' ', q < 0 ? 0 : q);
                    if (q > 0 && sp > q)
                    {
                        foreach (string pair in req.Substring(q + 1, sp - q - 1).Split('&'))
                        {
                            int eq = pair.IndexOf('=');
                            if (eq <= 0) continue;
                            string k = pair.Substring(0, eq), v = Uri.UnescapeDataString(pair.Substring(eq + 1));
                            if (k == "code") code = v;
                            else if (k == "state") gotState = v;
                        }
                    }

                    bool ok = code.Length > 0 && gotState == state;
                    string html = "<!doctype html><meta charset=utf-8><body style=\"font-family:sans-serif;"
                        + "background:#131519;color:#e6e8ec;display:flex;align-items:center;"
                        + "justify-content:center;height:100vh;margin:0\"><div style=\"text-align:center\">"
                        + "<h2 style=\"font-weight:600\">" + (ok ? "연결되었습니다" : "연결하지 못했습니다")
                        + "</h2><p style=\"color:#828b99\">"
                        + (ok ? "이 창을 닫고 프로그램으로 돌아가세요." : "프로그램에서 다시 시도해 주세요.")
                        + "</p></div></body>";
                    byte[] outBuf = Encoding.UTF8.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n"
                        + "Content-Length: " + Encoding.UTF8.GetByteCount(html) + "\r\nConnection: close\r\n\r\n" + html);
                    ns.Write(outBuf, 0, outBuf.Length);
                    ns.Flush();

                    if (ok) return code;
                }
            }
            return "";
        }

        // ── 액세스 토큰 ─────────────────────────────────────────
        static string Token()
        {
            if (accessToken.Length > 0 && DateTime.UtcNow < accessExpires) return accessToken;

            string refresh = LoadRefreshToken();
            if (refresh.Length == 0) throw new InvalidOperationException("구글에 로그인되어 있지 않습니다.");

            Tls.Prepare();
            string body = "client_id=" + Uri.EscapeDataString(AppInfo.GoogleClientId)
                + "&client_secret=" + Uri.EscapeDataString(AppInfo.GoogleClientSecret)
                + "&refresh_token=" + Uri.EscapeDataString(refresh)
                + "&grant_type=refresh_token";

            Dictionary<string, object> res = Json.Obj(Json.Parse(PostForm(TokenUrl, body)));
            accessToken = Json.Str(res, "access_token", "");
            accessExpires = DateTime.UtcNow.AddSeconds(Json.Num(res, "expires_in", 3000) - 60);
            if (accessToken.Length == 0)
            {
                SignOut();
                throw new InvalidOperationException("구글 로그인이 만료되었습니다. 다시 로그인해 주세요.");
            }
            return accessToken;
        }

        // ── 폴더 / 파일 ─────────────────────────────────────────
        public static string EnsureFolder()
        {
            string q = "mimeType='application/vnd.google-apps.folder' and trashed=false and name="
                       + Quote(FolderName);
            List<DriveFile> found = Query(q);
            if (found.Count > 0) return found[0].Id;

            string json = "{\"name\":" + Json.Quote(FolderName)
                + ",\"mimeType\":\"application/vnd.google-apps.folder\"}";
            Dictionary<string, object> res = Json.Obj(Json.Parse(
                Send("POST", ApiFiles + "?fields=id", json, "application/json")));
            return Json.Str(res, "id", "");
        }

        public static List<DriveFile> ListFolder(string folderId)
        {
            return Query("'" + folderId + "' in parents and trashed=false");
        }

        static List<DriveFile> Query(string q)
        {
            List<DriveFile> list = new List<DriveFile>();
            string pageToken = "";
            do
            {
                string url = ApiFiles + "?q=" + Uri.EscapeDataString(q)
                    + "&fields=nextPageToken,files(id,name,mimeType,size,modifiedTime)"
                    + "&pageSize=1000"
                    + (pageToken.Length > 0 ? "&pageToken=" + Uri.EscapeDataString(pageToken) : "");

                Dictionary<string, object> res = Json.Obj(Json.Parse(Send("GET", url, null, null)));
                List<object> files = Json.Arr(res != null && res.ContainsKey("files") ? res["files"] : null);
                if (files != null)
                    foreach (object o in files)
                    {
                        Dictionary<string, object> d = Json.Obj(o);
                        if (d == null) continue;
                        DriveFile f = new DriveFile();
                        f.Id = Json.Str(d, "id", "");
                        f.Name = Json.Str(d, "name", "");
                        f.MimeType = Json.Str(d, "mimeType", "");
                        f.Size = Json.Num(d, "size", 0);
                        f.Modified = Json.Str(d, "modifiedTime", "");
                        list.Add(f);
                    }
                pageToken = Json.Str(res, "nextPageToken", "");
            } while (pageToken.Length > 0);
            return list;
        }

        /// <summary>파일 하나를 올린다 (같은 이름이 있으면 덮어쓴다)</summary>
        public static void UploadFile(string localPath, string folderId, string name,
                                      string existingId, Action<int> progress)
        {
            string url = existingId.Length > 0
                ? UploadFiles + "/" + existingId + "?uploadType=resumable"
                : UploadFiles + "?uploadType=resumable";

            string meta = existingId.Length > 0
                ? "{\"name\":" + Json.Quote(name) + "}"
                : "{\"name\":" + Json.Quote(name) + ",\"parents\":[" + Json.Quote(folderId) + "]}";

            // 1) 업로드 세션 열기
            HttpWebRequest req = NewRequest(existingId.Length > 0 ? "PATCH" : "POST", url);
            req.ContentType = "application/json; charset=UTF-8";
            byte[] metaBytes = Encoding.UTF8.GetBytes(meta);
            req.ContentLength = metaBytes.Length;
            using (Stream rs = req.GetRequestStream()) rs.Write(metaBytes, 0, metaBytes.Length);

            string session;
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                session = res.Headers["Location"];
            if (string.IsNullOrEmpty(session))
                throw new IOException("업로드 세션을 열지 못했습니다: " + name);

            // 2) 내용 올리기
            FileInfo fi = new FileInfo(localPath);
            HttpWebRequest put = (HttpWebRequest)WebRequest.Create(session);
            put.Method = "PUT";
            put.Timeout = 600000;
            put.ReadWriteTimeout = 600000;
            put.AllowWriteStreamBuffering = false;
            put.ContentLength = fi.Length;

            using (FileStream fs = File.OpenRead(localPath))
            using (Stream rs = put.GetRequestStream())
            {
                byte[] buf = new byte[262144];
                long done = 0;
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    rs.Write(buf, 0, n);
                    done += n;
                    if (progress != null && fi.Length > 0)
                        progress((int)(done * 100 / fi.Length));
                }
            }
            using (put.GetResponse()) { }
        }

        public static void DownloadFile(string fileId, string localPath, Action<int> progress)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localPath));
            HttpWebRequest req = NewRequest("GET", ApiFiles + "/" + fileId + "?alt=media");
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (Stream input = res.GetResponseStream())
            using (FileStream fs = File.Create(localPath))
            {
                long total = res.ContentLength, done = 0;
                byte[] buf = new byte[262144];
                int n;
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    fs.Write(buf, 0, n);
                    done += n;
                    if (progress != null && total > 0) progress((int)(done * 100 / total));
                }
            }
        }

        public static void DeleteFile(string fileId)
        {
            Send("DELETE", ApiFiles + "/" + fileId, null, null);
        }

        // ── 폴더 통째로 올리기 / 받기 ───────────────────────────
        static readonly Dictionary<string, string> folderCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>드라이브 안에 relative 경로대로 폴더를 만들고 id 를 돌려준다</summary>
        static string EnsureSubFolder(string rootId, string relative)
        {
            if (relative.Length == 0) return rootId;
            string key = rootId + "|" + relative;
            string id;
            if (folderCache.TryGetValue(key, out id)) return id;

            string parent = rootId;
            foreach (string part in relative.Split('/'))
            {
                if (part.Length == 0) continue;
                List<DriveFile> found = Query(
                    "mimeType='application/vnd.google-apps.folder' and trashed=false and name="
                    + Quote(part) + " and '" + parent + "' in parents");

                if (found.Count > 0) parent = found[0].Id;
                else
                {
                    string json = "{\"name\":" + Json.Quote(part)
                        + ",\"mimeType\":\"application/vnd.google-apps.folder\""
                        + ",\"parents\":[" + Json.Quote(parent) + "]}";
                    Dictionary<string, object> res = Json.Obj(Json.Parse(
                        Send("POST", ApiFiles + "?fields=id", json, "application/json")));
                    parent = Json.Str(res, "id", "");
                }
            }
            folderCache[key] = parent;
            return parent;
        }

        /// <summary>내보낸 폴더를 드라이브에 올린다. 크기가 같은 파일은 건너뛴다.</summary>
        public static void UploadFolder(string localDir, Reporter report)
        {
            folderCache.Clear();
            string rootId = EnsureFolder();
            if (rootId.Length == 0) throw new IOException("드라이브 폴더를 만들지 못했습니다.");

            string[] files = Directory.GetFiles(localDir, "*", SearchOption.AllDirectories);
            string baseDir = Path.GetFullPath(localDir).TrimEnd('\\') + "\\";

            // 디렉터리별로 묶어 목록을 한 번만 읽는다
            Dictionary<string, List<DriveFile>> remote =
                new Dictionary<string, List<DriveFile>>(StringComparer.OrdinalIgnoreCase);

            int sent = 0, skipped = 0, i = 0;
            foreach (string f in files)
            {
                i++;
                string rel = Path.GetFullPath(f).Substring(baseDir.Length).Replace('\\', '/');
                string dir = rel.IndexOf('/') >= 0 ? rel.Substring(0, rel.LastIndexOf('/')) : "";
                string name = Path.GetFileName(f);

                string folderId = EnsureSubFolder(rootId, dir);
                List<DriveFile> list;
                if (!remote.TryGetValue(folderId, out list))
                {
                    list = ListFolder(folderId);
                    remote[folderId] = list;
                }

                DriveFile existing = null;
                foreach (DriveFile d in list)
                    if (!d.IsFolder && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
                    { existing = d; break; }

                long size = new FileInfo(f).Length;
                // manifest 는 항상 올린다 (크기가 같아도 내용이 바뀔 수 있다)
                bool same = existing != null && existing.Size == size
                            && !name.Equals(Sync.ManifestName, StringComparison.OrdinalIgnoreCase);
                if (same) { skipped++; }
                else
                {
                    report("  올리는 중 " + rel, -1);
                    UploadFile(f, folderId, name, existing != null ? existing.Id : "", null);
                    sent++;
                }
                if (report != null) report(null, (int)(i * 100L / Math.Max(1, files.Length)));
            }

            report("드라이브에 올림 — 새로 " + sent + "개, 그대로 " + skipped + "개", 100);
        }

        /// <summary>드라이브의 꾸러미를 로컬 폴더로 받는다. 크기가 같은 파일은 건너뛴다.</summary>
        public static void DownloadFolder(string localDir, Reporter report)
        {
            string rootId = EnsureFolder();
            Directory.CreateDirectory(localDir);

            int got = 0, skipped = 0;
            Walk(rootId, localDir, report, ref got, ref skipped);
            if (got == 0 && skipped == 0)
                throw new IOException("드라이브에 올려 둔 꾸러미가 없습니다.\n먼저 다른 PC에서 올려 주세요.");

            report("드라이브에서 받음 — 새로 " + got + "개, 그대로 " + skipped + "개", 100);
        }

        static void Walk(string folderId, string localDir, Reporter report, ref int got, ref int skipped)
        {
            List<DriveFile> items = ListFolder(folderId);
            foreach (DriveFile d in items)
            {
                string target = Path.Combine(localDir, d.Name);
                if (d.IsFolder)
                {
                    Directory.CreateDirectory(target);
                    Walk(d.Id, target, report, ref got, ref skipped);
                    continue;
                }

                bool same = File.Exists(target) && new FileInfo(target).Length == d.Size
                            && !d.Name.Equals(Sync.ManifestName, StringComparison.OrdinalIgnoreCase);
                if (same) { skipped++; continue; }

                report("  받는 중 " + d.Name, -1);
                DownloadFile(d.Id, target, delegate (int pct)
                {
                    if (report != null) report(null, pct);
                });
                got++;
            }
        }

        // ── HTTP 도우미 ─────────────────────────────────────────
        static HttpWebRequest NewRequest(string method, string url)
        {
            Tls.Prepare();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = "WowAddonManager/" + AppInfo.Version;
            req.Headers["Authorization"] = "Bearer " + Token();
            req.Timeout = 60000;
            req.ReadWriteTimeout = 120000;
            return req;
        }

        static string Send(string method, string url, string body, string contentType)
        {
            HttpWebRequest req = NewRequest(method, url);
            if (body != null)
            {
                req.ContentType = contentType ?? "application/json; charset=UTF-8";
                byte[] data = Encoding.UTF8.GetBytes(body);
                req.ContentLength = data.Length;
                using (Stream rs = req.GetRequestStream()) rs.Write(data, 0, data.Length);
            }
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        static string PostForm(string url, string body)
        {
            Tls.Prepare();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";
            req.UserAgent = "WowAddonManager/" + AppInfo.Version;
            req.Timeout = 30000;

            byte[] data = Encoding.UTF8.GetBytes(body);
            req.ContentLength = data.Length;
            using (Stream rs = req.GetRequestStream()) rs.Write(data, 0, data.Length);

            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        static string Quote(string s) { return "'" + s.Replace("'", "\\'") + "'"; }

        static string RandomUrlSafe(int bytes)
        {
            byte[] b = new byte[bytes];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider()) rng.GetBytes(b);
            return Base64Url(b);
        }

        static string Base64Url(byte[] b)
        {
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }

    /// <summary>TLS 1.2 를 켜 둔다 (.NET 4.0 기본값은 구글이 거부한다)</summary>
    public static class Tls
    {
        public static void Prepare()
        {
            try
            {
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
            }
            catch { }
        }
    }
}
