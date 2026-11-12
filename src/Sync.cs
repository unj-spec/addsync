using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace AddSync
{
    // ── 내보낸 꾸러미의 구조 ────────────────────────────────────
    public class AddonEntry
    {
        public string Folder = "", Zip = "", Title = "", Version = "";
        public string Curse = "", Wago = "", WowI = "";
        public int Files;
        public long Bytes;
        public long Stamp;        // 폴더 안 파일의 가장 늦은 수정 시각 (ticks)
        public string Hash = "";  // 정밀 검사를 했을 때만 채운다

        /// <summary>빠른 비교용 지문 — 파일 수 · 총 바이트 · 최종 수정 시각</summary>
        public bool QuickEquals(AddonEntry other)
        {
            return other != null && other.Files == Files
                && other.Bytes == Bytes && other.Stamp == Stamp;
        }
    }

    public class FlavorPack
    {
        public string Name = "";
        public string WtfZip = "";
        public int WtfFiles;
        public long WtfBytes, WtfStamp;
        public bool GraphicsIncluded;
        public List<AddonEntry> Addons = new List<AddonEntry>();

        /// <summary>schema 1 로 만든 꾸러미는 애드온이 zip 하나에 들어 있다</summary>
        public string LegacyAddOnsZip = "";

        /// <summary>schema 1 꾸러미의 애드온 폴더 수</summary>
        public int LegacyFolders;

        public int FolderCount { get { return Addons.Count > 0 ? Addons.Count : LegacyFolders; } }

        public bool IsLegacy { get { return LegacyAddOnsZip.Length > 0; } }
    }

    public class Manifest
    {
        public int Schema = 2;
        public string ExportedAt = "", SourcePC = "", SourceWow = "";
        public bool Settings = true;
        public Dictionary<string, FlavorPack> Flavors =
            new Dictionary<string, FlavorPack>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>설치하면 무엇이 바뀌는지</summary>
    public class InstallPlan
    {
        public List<string> Add = new List<string>();        // 새로 생기는 애드온
        public List<string> Overwrite = new List<string>();  // 덮어쓰는 애드온
        public List<string> Keep = new List<string>();       // 대상에만 있어 그대로 두는 것
        public List<string> Remove = new List<string>();     // 완전 교체일 때 사라지는 것
        public bool Settings;
        public string SourceAccount = "", TargetAccount = "";
    }

    public class BackupInfo
    {
        public string Dir = "";
        public DateTime When;
        public long Bytes;
        public List<string> Flavors = new List<string>();

        public override string ToString()
        {
            return When.ToString("yyyy-MM-dd HH:mm");
        }
    }

    /// <summary>내보내기 · 설치 · 미리보기 · 백업</summary>
    public static class Sync
    {
        public const string ManifestName = "manifest.json";

        // ── 폴더 지문 ───────────────────────────────────────────
        public static void Fingerprint(string dir, out int files, out long bytes, out long stamp)
        {
            files = 0; bytes = 0; stamp = 0;
            if (!Directory.Exists(dir)) return;

            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (WowCore.IsExcluded(Path.GetFileName(f))) continue;
                try
                {
                    FileInfo fi = new FileInfo(f);
                    files++;
                    bytes += fi.Length;
                    long t = fi.LastWriteTimeUtc.Ticks;
                    if (t > stamp) stamp = t;
                }
                catch { }
            }
        }

        /// <summary>
        /// 이 PC 와우의 현재 상태를 한 줄로 나타낸다.
        /// 지난번 동기화 때와 달라졌는지 비교하는 데 쓴다.
        /// </summary>
        public static string LocalSignature(List<Flavor> flavors, bool includeSettings)
        {
            StringBuilder b = new StringBuilder();
            foreach (Flavor f in flavors)
            {
                int n; long by, st;
                Fingerprint(f.AddOnsPath, out n, out by, out st);
                b.Append(f.Name).Append(':').Append(n).Append('/').Append(by).Append('/').Append(st);
                if (includeSettings)
                {
                    Fingerprint(f.WtfPath, out n, out by, out st);
                    b.Append("|w").Append(n).Append('/').Append(by).Append('/').Append(st);
                }
                b.Append(';');
            }
            return b.ToString();
        }

        /// <summary>내용까지 읽어 만든 해시 (정밀 검사용)</summary>
        public static string ContentHash(string dir)
        {
            if (!Directory.Exists(dir)) return "";
            List<string> list = new List<string>();
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                if (!WowCore.IsExcluded(Path.GetFileName(f))) list.Add(f);
            list.Sort(StringComparer.OrdinalIgnoreCase);

            string baseDir = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            using (MD5 md5 = MD5.Create())
            {
                foreach (string f in list)
                {
                    string rel = Path.GetFullPath(f).Substring(baseDir.Length).ToLowerInvariant();
                    byte[] head = Encoding.UTF8.GetBytes(rel + "\n");
                    md5.TransformBlock(head, 0, head.Length, head, 0);
                    try
                    {
                        using (FileStream fs = File.OpenRead(f))
                        {
                            byte[] buf = new byte[65536];
                            int n;
                            while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                                md5.TransformBlock(buf, 0, n, buf, 0);
                        }
                    }
                    catch { }
                }
                md5.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(md5.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        // ── 내보내기 ────────────────────────────────────────────
        public static void Export(ExportOptions o, Reporter report)
        {
            Directory.CreateDirectory(o.OutputDir);
            Manifest prev = TryReadManifest(o.OutputDir);
            Manifest now = new Manifest();
            now.ExportedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            now.SourcePC = Environment.MachineName;
            now.SourceWow = o.WowPath;
            now.Settings = o.IncludeSettings;

            foreach (Flavor f in o.Flavors)
            {
                report("=== " + VersionNames.Friendly(f.Name), 0);

                FlavorPack pack = new FlavorPack();
                pack.Name = f.Name;
                pack.GraphicsIncluded = o.IncludeGraphicsConfig;

                FlavorPack old = null;
                if (prev != null) prev.Flavors.TryGetValue(f.Name, out old);

                string addonsDir = Path.Combine(o.OutputDir, f.Name + "\\addons");
                Directory.CreateDirectory(addonsDir);

                string[] dirs = Directory.Exists(f.AddOnsPath)
                    ? Directory.GetDirectories(f.AddOnsPath) : new string[0];

                List<AddonInfo> infos = WowCore.GetAddons(f.AddOnsPath);
                Dictionary<string, AddonInfo> infoMap =
                    new Dictionary<string, AddonInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (AddonInfo a in infos) infoMap[a.Folder] = a;

                int reused = 0, rebuilt = 0;
                for (int i = 0; i < dirs.Length; i++)
                {
                    string folder = Path.GetFileName(dirs[i]);
                    AddonEntry e = new AddonEntry();
                    e.Folder = folder;
                    e.Zip = f.Name + "/addons/" + folder + ".zip";

                    AddonInfo info;
                    if (infoMap.TryGetValue(folder, out info))
                    {
                        e.Title = info.Title; e.Version = info.Version;
                        e.Curse = info.Curse; e.Wago = info.Wago; e.WowI = info.WowI;
                    }
                    else e.Title = folder;

                    int files; long bytes, stamp;
                    Fingerprint(dirs[i], out files, out bytes, out stamp);
                    e.Files = files; e.Bytes = bytes; e.Stamp = stamp;

                    AddonEntry before = FindAddon(old, folder);
                    string zipPath = Path.Combine(o.OutputDir, e.Zip.Replace('/', '\\'));
                    bool same = before != null && e.QuickEquals(before) && File.Exists(zipPath);

                    if (same && o.DeepCheck)
                    {
                        // 정밀 검사: 내용 해시까지 맞아야 그대로 둔다
                        e.Hash = ContentHash(dirs[i]);
                        same = before.Hash.Length > 0 && before.Hash == e.Hash;
                    }
                    else if (same) e.Hash = before.Hash;

                    if (same) { reused++; }
                    else
                    {
                        if (o.DeepCheck && e.Hash.Length == 0) e.Hash = ContentHash(dirs[i]);
                        WowCore.CreateZip(dirs[i], zipPath, null, folder, null);
                        rebuilt++;
                    }

                    pack.Addons.Add(e);
                    if (report != null)
                        report(null, (int)((i + 1) * 100L / Math.Max(1, dirs.Length)));
                }

                // 목록에서 사라진 애드온의 zip 은 지운다
                int removed = 0;
                HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (AddonEntry e in pack.Addons) wanted.Add(e.Folder + ".zip");
                foreach (string z in Directory.GetFiles(addonsDir, "*.zip"))
                    if (!wanted.Contains(Path.GetFileName(z)))
                    { try { File.Delete(z); removed++; } catch { } }

                report("  애드온 " + pack.Addons.Count + "개 — 새로 " + rebuilt
                       + "개, 그대로 " + reused + "개"
                       + (removed > 0 ? ", 삭제 " + removed + "개" : ""), 100);

                // ── 설정 ────────────────────────────────────────
                if (o.IncludeSettings && Directory.Exists(f.WtfPath))
                {
                    int wf; long wb, ws;
                    Fingerprint(f.WtfPath, out wf, out wb, out ws);
                    pack.WtfZip = f.Name + "/wtf.zip";
                    pack.WtfFiles = wf; pack.WtfBytes = wb; pack.WtfStamp = ws;

                    string wtfPath = Path.Combine(o.OutputDir, pack.WtfZip.Replace('/', '\\'));
                    bool sameWtf = old != null && File.Exists(wtfPath)
                        && old.WtfFiles == wf && old.WtfBytes == wb && old.WtfStamp == ws
                        && old.GraphicsIncluded == o.IncludeGraphicsConfig;

                    if (sameWtf) report("  설정 — 바뀐 것 없음", 100);
                    else
                    {
                        string cfg = Path.Combine(f.WtfPath, "Config.wtf");
                        int n = WowCore.CreateZip(f.WtfPath, wtfPath,
                            (o.IncludeGraphicsConfig || !File.Exists(cfg)) ? null : cfg, "WTF", report);
                        report("  설정 " + n.ToString("N0") + " 파일", 100);
                    }
                }

                now.Flavors[f.Name] = pack;
            }

            WriteManifest(now, o.OutputDir);
            report("완료 — " + Path.Combine(o.OutputDir, ManifestName), 100);
        }

        static AddonEntry FindAddon(FlavorPack pack, string folder)
        {
            if (pack == null) return null;
            foreach (AddonEntry e in pack.Addons)
                if (string.Equals(e.Folder, folder, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        // ── manifest 읽기 / 쓰기 ────────────────────────────────
        public static void WriteManifest(Manifest m, string dir)
        {
            Json.Writer w = new Json.Writer();
            w.StartObject(null);
            w.Put("schema", m.Schema);
            w.Put("exportedAt", m.ExportedAt);
            w.Put("sourcePC", m.SourcePC);
            w.Put("sourceWow", m.SourceWow);
            w.Put("settings", m.Settings);

            w.StartObject("flavors");
            foreach (KeyValuePair<string, FlavorPack> kv in m.Flavors)
            {
                FlavorPack p = kv.Value;
                w.StartObject(kv.Key);
                w.Put("wtfZip", p.WtfZip);
                w.Put("wtfFiles", p.WtfFiles);
                w.Put("wtfBytes", p.WtfBytes);
                w.Put("wtfStamp", p.WtfStamp);
                w.Put("graphicsIncluded", p.GraphicsIncluded);
                w.Put("folders", p.Addons.Count);

                w.StartArray("addons");
                foreach (AddonEntry e in p.Addons)
                {
                    w.StartObject(null);
                    w.Put("folder", e.Folder);
                    w.Put("zip", e.Zip);
                    w.Put("title", e.Title);
                    w.Put("version", e.Version);
                    if (e.Curse.Length > 0) w.Put("curse", e.Curse);
                    if (e.Wago.Length > 0) w.Put("wago", e.Wago);
                    if (e.WowI.Length > 0) w.Put("wowi", e.WowI);
                    w.Put("files", e.Files);
                    w.Put("bytes", e.Bytes);
                    w.Put("stamp", e.Stamp);
                    if (e.Hash.Length > 0) w.Put("hash", e.Hash);
                    w.EndObject();
                }
                w.EndArray();
                w.EndObject();
            }
            w.EndObject();
            w.EndObject();

            File.WriteAllText(Path.Combine(dir, ManifestName), w.ToString(), new UTF8Encoding(false));
        }

        public static Manifest TryReadManifest(string dir)
        {
            try { return ReadManifest(dir); }
            catch { return null; }
        }

        public static Manifest ReadManifest(string dir)
        {
            string path = Path.Combine(dir ?? "", ManifestName);
            if (!File.Exists(path))
                throw new FileNotFoundException("manifest.json 이 없습니다.\n내보내기로 만든 폴더를 고르세요.\n\n" + dir);

            Dictionary<string, object> root = Json.Obj(Json.Parse(File.ReadAllText(path, Encoding.UTF8)));
            if (root == null) throw new InvalidDataException("manifest.json 을 읽지 못했습니다.");

            Manifest m = new Manifest();
            m.Schema = (int)Json.Num(root, "schema", 1);
            m.ExportedAt = Json.Str(root, "exportedAt", "");
            m.SourcePC = Json.Str(root, "sourcePC", "");
            m.SourceWow = Json.Str(root, "sourceWow", "");
            m.Settings = Json.Bool(root, "settings", true);

            object fo;
            root.TryGetValue("flavors", out fo);
            Dictionary<string, object> flavors = Json.Obj(fo);
            if (flavors == null) throw new InvalidDataException("manifest.json 에 버전 정보가 없습니다.");

            foreach (KeyValuePair<string, object> kv in flavors)
            {
                Dictionary<string, object> fd = Json.Obj(kv.Value);
                if (fd == null) continue;

                FlavorPack p = new FlavorPack();
                p.Name = kv.Key;
                p.WtfZip = Json.Str(fd, "wtfZip", "");
                p.WtfFiles = (int)Json.Num(fd, "wtfFiles", 0);
                p.WtfBytes = Json.Num(fd, "wtfBytes", 0);
                p.WtfStamp = Json.Num(fd, "wtfStamp", 0);
                p.GraphicsIncluded = Json.Bool(fd, "graphicsIncluded", false);
                p.LegacyAddOnsZip = Json.Str(fd, "addOnsZip", "");   // schema 1
                p.LegacyFolders = (int)Json.Num(fd, "folders", 0);

                List<object> arr = Json.Arr(fd.ContainsKey("addons") ? fd["addons"] : null);
                if (arr != null)
                {
                    foreach (object ao in arr)
                    {
                        Dictionary<string, object> ad = Json.Obj(ao);
                        if (ad == null) continue;
                        AddonEntry e = new AddonEntry();
                        e.Folder = Json.Str(ad, "folder", "");
                        e.Zip = Json.Str(ad, "zip", "");
                        e.Title = Json.Str(ad, "title", e.Folder);
                        e.Version = Json.Str(ad, "version", "");
                        e.Curse = Json.Str(ad, "curse", "");
                        e.Wago = Json.Str(ad, "wago", "");
                        e.WowI = Json.Str(ad, "wowi", "");
                        e.Files = (int)Json.Num(ad, "files", 0);
                        e.Bytes = Json.Num(ad, "bytes", 0);
                        e.Stamp = Json.Num(ad, "stamp", 0);
                        e.Hash = Json.Str(ad, "hash", "");
                        if (e.Folder.Length > 0) p.Addons.Add(e);
                    }
                }

                m.Flavors[kv.Key] = p;
            }

            if (m.Flavors.Count == 0)
                throw new InvalidDataException("manifest.json 에서 애드온 정보를 읽지 못했습니다.");
            return m;
        }

        // ── 파일 하나로 묶기 / 풀기 ─────────────────────────────
        public const string BundleExt = ".wam";

        // ── 스스로 푸는 실행 파일 ───────────────────────────────
        // 프로그램 뒤에 꾸러미를 붙이고, 맨 끝에 표식을 남긴다.
        //   [ AddSync.exe ][ .wam 내용 ][ 길이 8바이트 ][ 매직 8바이트 ]
        // .NET exe 는 뒤에 무엇이 붙어도 그대로 실행된다.
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("ADDSYNC1");
        const int TrailerLen = 16;

        /// <summary>받는 쪽에 설치가 필요 없는 실행 파일을 만든다</summary>
        public static void PackSelfExtract(string bundleFile, string destExe,
                                           string programExe, Reporter report)
        {
            if (report != null) report("실행 파일로 묶는 중…", 0);

            using (FileStream outFs = new FileStream(destExe, FileMode.Create, FileAccess.Write))
            {
                using (FileStream prog = File.OpenRead(programExe)) prog.CopyTo(outFs);

                long payloadAt = outFs.Position;
                using (FileStream wam = File.OpenRead(bundleFile)) wam.CopyTo(outFs);
                long payloadLen = outFs.Position - payloadAt;

                outFs.Write(BitConverter.GetBytes(payloadLen), 0, 8);
                outFs.Write(Magic, 0, Magic.Length);
            }

            if (report != null)
                report("실행 파일 " + WowCore.HumanSize(new FileInfo(destExe).Length), 100);
        }

        /// <summary>이 실행 파일이 꾸러미를 품고 있는지 본다</summary>
        public static bool HasPayload(string exePath)
        {
            long at, len;
            return FindPayload(exePath, out at, out len);
        }

        static bool FindPayload(string exePath, out long at, out long len)
        {
            at = 0; len = 0;
            try
            {
                using (FileStream fs = File.OpenRead(exePath))
                {
                    if (fs.Length < TrailerLen + 1024) return false;
                    fs.Seek(-TrailerLen, SeekOrigin.End);

                    byte[] tail = new byte[TrailerLen];
                    if (fs.Read(tail, 0, TrailerLen) != TrailerLen) return false;

                    for (int i = 0; i < Magic.Length; i++)
                        if (tail[8 + i] != Magic[i]) return false;

                    len = BitConverter.ToInt64(tail, 0);
                    at = fs.Length - TrailerLen - len;
                    return len > 0 && at > 0;
                }
            }
            catch { return false; }
        }

        /// <summary>품고 있는 꾸러미를 꺼내 .wam 파일로 만든다</summary>
        public static string ExtractPayload(string exePath, string destFile, Reporter report)
        {
            long at, len;
            if (!FindPayload(exePath, out at, out len))
                throw new InvalidDataException("이 파일에는 애드온 꾸러미가 들어 있지 않습니다.");

            Directory.CreateDirectory(Path.GetDirectoryName(destFile));
            if (report != null) report("담긴 애드온을 꺼내는 중…", 0);

            using (FileStream fs = File.OpenRead(exePath))
            using (FileStream outFs = new FileStream(destFile, FileMode.Create, FileAccess.Write))
            {
                fs.Seek(at, SeekOrigin.Begin);
                byte[] buf = new byte[1 << 20];
                long left = len;
                long done = 0;
                while (left > 0)
                {
                    int want = (int)Math.Min(buf.Length, left);
                    int got = fs.Read(buf, 0, want);
                    if (got <= 0) break;
                    outFs.Write(buf, 0, got);
                    left -= got;
                    done += got;
                    if (report != null) report(null, (int)(done * 100L / len));
                }
            }
            return destFile;
        }

        /// <summary>
        /// 내보낸 폴더를 파일 하나로 묶는다. 안에 든 것이 이미 zip 이라
        /// 다시 압축하지 않고 담기만 한다 (시간만 걸리고 크기는 거의 그대로다).
        /// </summary>
        public static int PackBundle(string exportDir, string destFile, Reporter report)
        {
            if (!File.Exists(Path.Combine(exportDir, ManifestName)))
                throw new IOException("묶을 내용이 없습니다.");

            // 작업 폴더에는 지난번에 내보낸 다른 버전이 남아 있을 수 있다.
            // manifest 가 가리키는 것만 담아야 꾸러미가 쓸데없이 커지지 않는다.
            Manifest m = ReadManifest(exportDir);
            List<string> rels = new List<string>();
            rels.Add(ManifestName);
            foreach (KeyValuePair<string, FlavorPack> kv in m.Flavors)
            {
                FlavorPack p = kv.Value;
                if (p.WtfZip.Length > 0) rels.Add(p.WtfZip);
                if (p.IsLegacy && p.LegacyAddOnsZip.Length > 0) rels.Add(p.LegacyAddOnsZip);
                foreach (AddonEntry e in p.Addons) rels.Add(e.Zip);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destFile));
            if (File.Exists(destFile)) File.Delete(destFile);

            int n = 0;
            using (ZipArchive zip = ZipFile.Open(destFile, ZipArchiveMode.Create))
            {
                for (int i = 0; i < rels.Count; i++)
                {
                    string rel = rels[i].Replace('\\', '/');
                    string full = Path.Combine(exportDir, rel.Replace('/', '\\'));
                    if (!File.Exists(full)) continue;
                    zip.CreateEntryFromFile(full, rel, CompressionLevel.NoCompression);
                    n++;
                    if (report != null && (i % 20 == 0 || i == rels.Count - 1))
                        report(null, (int)((i + 1) * 100L / rels.Count));
                }
            }
            return n;
        }

        /// <summary>묶음 파일을 폴더로 푼다</summary>
        public static int UnpackBundle(string bundleFile, string destDir, Reporter report)
        {
            if (!File.Exists(bundleFile))
                throw new FileNotFoundException("파일을 찾을 수 없습니다.\n\n" + bundleFile);

            if (Directory.Exists(destDir))
            {
                try { WowCore.DeleteTree(destDir); }
                catch { }
            }
            Directory.CreateDirectory(destDir);
            int n = WowCore.ExtractZip(bundleFile, destDir, "bundle", report);

            if (!File.Exists(Path.Combine(destDir, ManifestName)))
                throw new InvalidDataException(
                    "이 파일은 애드온 꾸러미가 아닙니다.\n" +
                    "'파일로 내보내기' 로 만든 " + BundleExt + " 파일을 고르세요.");
            return n;
        }

        /// <summary>묶음 파일 안의 manifest 만 들여다본다 (풀지 않고)</summary>
        public static Manifest PeekBundle(string bundleFile)
        {
            string temp = Path.Combine(Path.GetTempPath(), "wam-peek");
            try
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
                Directory.CreateDirectory(temp);

                using (ZipArchive z = ZipFile.OpenRead(bundleFile))
                    foreach (ZipArchiveEntry e in z.Entries)
                        if (string.Equals(e.FullName, ManifestName, StringComparison.OrdinalIgnoreCase))
                        {
                            e.ExtractToFile(Path.Combine(temp, ManifestName), true);
                            return ReadManifest(temp);
                        }
                throw new InvalidDataException("꾸러미 정보가 없는 파일입니다.");
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
                catch { }
            }
        }

        // ── 계정 폴더 번호 ──────────────────────────────────────
        public static List<string> AccountsInZip(string wtfZipPath)
        {
            List<string> ids = new List<string>();
            if (!File.Exists(wtfZipPath)) return ids;
            try
            {
                using (ZipArchive z = ZipFile.OpenRead(wtfZipPath)) ScanAccounts(z, ids);
            }
            catch { }
            return ids;
        }

        /// <summary>
        /// 꾸러미(.wam) 안에 든 wtf.zip 에서 바로 계정 번호를 읽는다.
        /// 풀기 전에도 계정 폴더 번호가 다른지 알 수 있어야 한다.
        /// </summary>
        public static List<string> AccountsInBundle(string bundleFile, string wtfEntry)
        {
            List<string> ids = new List<string>();
            if (!File.Exists(bundleFile) || string.IsNullOrEmpty(wtfEntry)) return ids;
            try
            {
                using (ZipArchive outer = ZipFile.OpenRead(bundleFile))
                {
                    ZipArchiveEntry we = outer.GetEntry(wtfEntry.Replace('\\', '/'));
                    if (we == null) return ids;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (Stream src = we.Open()) src.CopyTo(ms);
                        ms.Position = 0;
                        using (ZipArchive inner = new ZipArchive(ms, ZipArchiveMode.Read))
                            ScanAccounts(inner, ids);
                    }
                }
            }
            catch { }
            return ids;
        }

        static void ScanAccounts(ZipArchive z, List<string> ids)
        {
            foreach (ZipArchiveEntry e in z.Entries)
            {
                string[] parts = e.FullName.Split('/');
                if (parts.Length >= 2 &&
                    string.Equals(parts[0], "Account", StringComparison.OrdinalIgnoreCase) &&
                    parts[1].Length > 0 && parts[1].IndexOf('#') > 0 &&
                    !ids.Contains(parts[1]))
                    ids.Add(parts[1]);
            }
        }

        public static List<string> AccountsOnDisk(string wtfPath)
        {
            List<string> ids = new List<string>();
            string acc = Path.Combine(wtfPath, "Account");
            if (!Directory.Exists(acc)) return ids;
            foreach (string d in Directory.GetDirectories(acc))
            {
                string n = Path.GetFileName(d);
                if (n.IndexOf('#') > 0) ids.Add(n);
            }
            return ids;
        }

        // ── 미리보기 ────────────────────────────────────────────
        public static InstallPlan Preview(InstallOptions o, Manifest m)
        {
            InstallPlan plan = new InstallPlan();
            plan.Settings = o.IncludeSettings;

            foreach (string name in o.FlavorNames)
            {
                FlavorPack p;
                if (!m.Flavors.TryGetValue(name, out p)) continue;

                string addOns = Path.Combine(Path.Combine(o.WowPath, name), @"Interface\AddOns");
                HashSet<string> onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(addOns))
                    foreach (string d in Directory.GetDirectories(addOns))
                        onDisk.Add(Path.GetFileName(d));

                HashSet<string> incoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (p.IsLegacy)
                {
                    // schema 1 은 zip 하나라 목록을 들여다봐야 한다
                    try
                    {
                        using (ZipArchive z = ZipFile.OpenRead(Path.Combine(o.SourceDir, p.LegacyAddOnsZip)))
                            foreach (ZipArchiveEntry e in z.Entries)
                            {
                                int i = e.FullName.IndexOf('/');
                                if (i > 0) incoming.Add(e.FullName.Substring(0, i));
                            }
                    }
                    catch { }
                }
                else foreach (AddonEntry e in p.Addons) incoming.Add(e.Folder);

                foreach (string f in incoming)
                {
                    if (onDisk.Contains(f)) plan.Overwrite.Add(f);
                    else plan.Add.Add(f);
                }
                foreach (string f in onDisk)
                    if (!incoming.Contains(f))
                    {
                        if (o.CleanInstall) plan.Remove.Add(f);
                        else plan.Keep.Add(f);
                    }

                // 계정 번호 비교 (설정을 설치할 때만 의미가 있다)
                if (o.IncludeSettings && p.WtfZip.Length > 0)
                {
                    List<string> src = AccountsInZip(Path.Combine(o.SourceDir, p.WtfZip.Replace('/', '\\')));
                    List<string> dst = AccountsOnDisk(Path.Combine(Path.Combine(o.WowPath, name), "WTF"));
                    if (src.Count > 0) plan.SourceAccount = string.Join(", ", src.ToArray());
                    if (dst.Count > 0) plan.TargetAccount = string.Join(", ", dst.ToArray());
                }
            }

            plan.Add.Sort(StringComparer.OrdinalIgnoreCase);
            plan.Overwrite.Sort(StringComparer.OrdinalIgnoreCase);
            plan.Keep.Sort(StringComparer.OrdinalIgnoreCase);
            plan.Remove.Sort(StringComparer.OrdinalIgnoreCase);
            return plan;
        }

        // ── 설치 ────────────────────────────────────────────────
        public static void Install(InstallOptions o, Reporter report)
        {
            WowCore.EnsureWowClosed();
            Manifest m = ReadManifest(o.SourceDir);

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupDir = Path.Combine(o.BackupRoot, stamp);

            foreach (string name in o.FlavorNames)
            {
                FlavorPack p;
                if (!m.Flavors.TryGetValue(name, out p)) continue;

                string flavorPath = Path.Combine(o.WowPath, name);
                report("=== " + VersionNames.Friendly(name), 0);
                if (!Directory.Exists(flavorPath))
                {
                    report("  이 PC에 없어 건너뜁니다.", 100);
                    continue;
                }

                string addOns = Path.Combine(flavorPath, @"Interface\AddOns");
                string wtf = Path.Combine(flavorPath, "WTF");

                if (o.Backup)
                {
                    if (Directory.Exists(addOns))
                    {
                        int n = WowCore.CreateZip(addOns, Path.Combine(backupDir, name + "-AddOns.zip"),
                            null, "backup", report);
                        if (n > 0) report("  백업 " + n.ToString("N0") + " 파일", 100);
                    }
                    if (o.IncludeSettings && Directory.Exists(wtf))
                        WowCore.CreateZip(wtf, Path.Combine(backupDir, name + "-WTF.zip"), null, "backup", report);
                }

                if (o.CleanInstall && Directory.Exists(addOns))
                {
                    report("  기존 애드온 비우는 중…", 0);
                    foreach (string d in Directory.GetDirectories(addOns))
                    { try { WowCore.DeleteTree(d); } catch { } }
                }

                // 애드온
                if (p.IsLegacy)
                {
                    string zip = Path.Combine(o.SourceDir, p.LegacyAddOnsZip);
                    if (File.Exists(zip))
                        report("  애드온 " + WowCore.ExtractZip(zip, addOns, "AddOns", report).ToString("N0")
                               + " 파일 설치", 100);
                }
                else
                {
                    int done = 0, files = 0;
                    foreach (AddonEntry e in p.Addons)
                    {
                        string zip = Path.Combine(o.SourceDir, e.Zip.Replace('/', '\\'));
                        if (File.Exists(zip))
                            files += WowCore.ExtractZip(zip, Path.Combine(addOns, e.Folder), e.Folder, null);
                        done++;
                        if (report != null)
                            report(null, (int)(done * 100L / Math.Max(1, p.Addons.Count)));
                    }
                    report("  애드온 " + p.Addons.Count + "개, " + files.ToString("N0") + " 파일 설치", 100);
                }

                // 설정
                if (o.IncludeSettings && p.WtfZip.Length > 0)
                {
                    string zip = Path.Combine(o.SourceDir, p.WtfZip.Replace('/', '\\'));
                    if (File.Exists(zip))
                    {
                        Func<string, string> map = null;
                        if (o.MapAccount && o.SourceAccountId.Length > 0 && o.TargetAccountId.Length > 0
                            && !string.Equals(o.SourceAccountId, o.TargetAccountId, StringComparison.OrdinalIgnoreCase))
                        {
                            string from = "Account/" + o.SourceAccountId + "/";
                            string to = "Account/" + o.TargetAccountId + "/";
                            map = delegate (string entry)
                            {
                                if (entry.StartsWith(from, StringComparison.OrdinalIgnoreCase))
                                    return to + entry.Substring(from.Length);
                                return entry;
                            };
                            report("  계정 폴더 " + o.SourceAccountId + " → " + o.TargetAccountId, -1);
                        }

                        int n = WowCore.ExtractZip(zip, wtf, "WTF", report, map);
                        report("  설정 " + n.ToString("N0") + " 파일 설치", 100);
                    }
                }
            }

            if (o.Backup && Directory.Exists(backupDir))
            {
                report("백업: " + backupDir, 100);
                int gone = PruneBackups(o.BackupRoot, o.KeepBackups);
                if (gone > 0) report("오래된 백업 " + gone + "개 정리", 100);
            }
        }

        // ── 백업 ────────────────────────────────────────────────
        public static List<BackupInfo> ListBackups(string root)
        {
            List<BackupInfo> list = new List<BackupInfo>();
            if (!Directory.Exists(root)) return list;

            foreach (string d in Directory.GetDirectories(root))
            {
                BackupInfo b = new BackupInfo();
                b.Dir = d;
                DateTime when;
                if (!DateTime.TryParseExact(Path.GetFileName(d), "yyyyMMdd-HHmmss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                {
                    try { when = Directory.GetCreationTime(d); } catch { when = DateTime.MinValue; }
                }
                b.When = when;

                foreach (string z in Directory.GetFiles(d, "*.zip"))
                {
                    try { b.Bytes += new FileInfo(z).Length; } catch { }
                    string n = Path.GetFileNameWithoutExtension(z);
                    int i = n.LastIndexOf('-');
                    string flavor = i > 0 ? n.Substring(0, i) : n;
                    if (!b.Flavors.Contains(flavor)) b.Flavors.Add(flavor);
                }
                if (b.Flavors.Count > 0) list.Add(b);
            }

            list.Sort(delegate (BackupInfo a, BackupInfo c) { return c.When.CompareTo(a.When); });
            return list;
        }

        /// <summary>최근 keep 개만 남기고 지운다. 지운 개수를 돌려준다.</summary>
        public static int PruneBackups(string root, int keep)
        {
            if (keep <= 0) keep = 5;
            List<BackupInfo> all = ListBackups(root);
            int gone = 0;
            for (int i = keep; i < all.Count; i++)
            {
                try { WowCore.DeleteTree(all[i].Dir); gone++; }
                catch { }
            }
            return gone;
        }

        public static void RestoreBackup(BackupInfo b, string wowPath, bool settings, Reporter report)
        {
            WowCore.EnsureWowClosed();

            foreach (string zip in Directory.GetFiles(b.Dir, "*.zip"))
            {
                string n = Path.GetFileNameWithoutExtension(zip);
                int i = n.LastIndexOf('-');
                if (i <= 0) continue;
                string flavor = n.Substring(0, i);
                string kind = n.Substring(i + 1);

                string flavorPath = Path.Combine(wowPath, flavor);
                if (!Directory.Exists(flavorPath)) continue;

                if (kind.Equals("AddOns", StringComparison.OrdinalIgnoreCase))
                {
                    string addOns = Path.Combine(flavorPath, @"Interface\AddOns");
                    report("=== " + VersionNames.Friendly(flavor) + " 애드온 되돌리는 중…", 0);
                    if (Directory.Exists(addOns))
                        foreach (string d in Directory.GetDirectories(addOns))
                        { try { WowCore.DeleteTree(d); } catch { } }
                    int c = WowCore.ExtractZip(zip, addOns, "AddOns", report);
                    report("  " + c.ToString("N0") + " 파일 복원", 100);
                }
                else if (settings && kind.Equals("WTF", StringComparison.OrdinalIgnoreCase))
                {
                    string wtf = Path.Combine(flavorPath, "WTF");
                    report("=== " + VersionNames.Friendly(flavor) + " 설정 되돌리는 중…", 0);
                    int c = WowCore.ExtractZip(zip, wtf, "WTF", report);
                    report("  " + c.ToString("N0") + " 파일 복원", 100);
                }
            }
        }
    }

    /// <summary>폴더 이름 → 사람이 읽는 이름</summary>
    public static class VersionNames
    {
        public static string Friendly(string flavor)
        {
            switch (flavor)
            {
                case "_retail_": return "정식 서버";
                case "_classic_": return "클래식";
                case "_classic_era_": return "클래식 오리지널";
                case "_classic_ptr_": return "클래식 테스트";
                case "_ptr_": return "테스트 서버";
                case "_beta_": return "베타";
                case "_anniversary_": return "기념 서버";
                case "_forever_": return "클래식 포에버";
                default: return flavor;
            }
        }
    }
}
