using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WowAddonManager
{
    /// <summary>진행 상황 보고용 콜백</summary>
    public delegate void Reporter(string message, int percent);

    public class Flavor
    {
        public string Name;
        public string Path;
        public string AddOnsPath;
        public string WtfPath;
        public int AddOnCount;

        public override string ToString()
        {
            return Name + "   (애드온 " + AddOnCount + "개)";
        }
    }

    public class AddonInfo
    {
        public string Folder = "";
        public string Title = "";
        public string Version = "";
        public string Curse = "";
        public string Wago = "";
        public string WowI = "";
    }

    public class ExportOptions
    {
        public string WowPath;
        public List<Flavor> Flavors = new List<Flavor>();
        public string OutputDir;
        public bool IncludeSettings = true;
        public bool IncludeGraphicsConfig = false;
    }

    public class InstallOptions
    {
        public string WowPath;
        public string SourceDir;
        public List<string> FlavorNames = new List<string>();
        public bool IncludeSettings = true;
        public bool CleanInstall = false;
        public bool Backup = true;
        public string BackupRoot;
    }

    public class ManifestInfo
    {
        public string SourcePC = "";
        public string ExportedAt = "";
        public bool HasSettings;
        public Dictionary<string, FlavorEntry> Flavors = new Dictionary<string, FlavorEntry>();
    }

    public class FlavorEntry
    {
        public string AddOnsZip = "";
        public string WtfZip = "";
        public int Folders;
    }

    public static class WowCore
    {
        static readonly string[] ExcludeNames = { "cache.md5", "Thumbs.db", "desktop.ini" };
        static readonly string[] ExcludeSuffixes = { ".old", ".bak" };

        // ── Config.wtf 의 그래픽 설정만 골라내기 ───────────────────
        // 이름이 이 접두어로 시작하면 그래픽
        static readonly string[] GfxPrefixes = { "gx", "graphics", "ffx" };

        // 이 이름들은 그래픽·하드웨어에 묶인 값이라 다른 PC로 옮기면 안 된다
        static readonly string[] GfxNames = {
            "brightness", "contrast", "lowlatencymode", "msaaquality", "outlineenginemode",
            "renderscale", "resamplequality", "resamplealwayssharpen", "ssao", "hwdetect",
            "farclip", "horizonclip", "horizonstart", "groundeffectdensity", "groundeffectdist",
            "doodadlodscale", "lodobjectcullsize", "lodobjectfadescale", "lodobjectminsize",
            "physicslevel", "projectedtextures", "refraction", "reflectionmode", "rippledetail",
            "shadowmode", "shadowrt", "shadowsoft", "shadowtexturesize", "shadowblendcascades",
            "shadownumcascades", "entityshadowfadescale", "sunshafts", "terrainloddist",
            "terrainloddiv", "volumefog", "volumefoglevel", "vsync", "waterdetail",
            "weatherdensity", "wmoloddist", "texturefilteringmode", "videooptionsversion",
            "usetargetfps", "targetfps", "maxfps", "maxfpsbk", "uiscale", "useuiscale",
            "componentmodellevel", "particledensity", "spelldensity",
            "sound_outputdrivername"   // 오디오 장치 이름 — PC마다 다르다
        };

        /// <summary>그래픽·해상도·하드웨어에 묶인 설정인가</summary>
        public static bool IsGraphicsCVar(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();

            // RAID 접두어가 붙은 것은 전부 전투 전용 그래픽 설정이다
            if (n.StartsWith("raid")) n = n.Substring(4);

            foreach (string p in GfxPrefixes) if (n.StartsWith(p)) return true;
            foreach (string g in GfxNames) if (n == g) return true;
            return false;
        }

        /// <summary>Config.wtf 에서 그래픽 줄만 뺀 내용을 돌려준다</summary>
        public static string FilterConfigWtf(string path, out int kept, out int dropped)
        {
            kept = 0; dropped = 0;
            StringBuilder b = new StringBuilder();
            foreach (string line in File.ReadAllLines(path))
            {
                Match m = Regex.Match(line, @"^\s*SET\s+([A-Za-z0-9_\-]+)\s");
                if (m.Success && IsGraphicsCVar(m.Groups[1].Value)) { dropped++; continue; }
                b.AppendLine(line);
                if (m.Success) kept++;
            }
            return b.ToString();
        }

        public static bool IsExcluded(string fileName)
        {
            foreach (string n in ExcludeNames)
                if (string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string s in ExcludeSuffixes)
                if (fileName.EndsWith(s, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ── WoW 경로 탐지 ────────────────────────────────────────────
        public static string FindWowPath()
        {
            List<string> cand = new List<string>();

            foreach (string sub in new string[] {
                @"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft",
                @"SOFTWARE\Blizzard Entertainment\World of Warcraft" })
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(sub))
                    {
                        if (k != null)
                        {
                            object v = k.GetValue("InstallPath");
                            if (v != null) cand.Add(v.ToString());
                        }
                    }
                }
                catch { }
            }

            try
            {
                string pdb = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Battle.net\Agent\product.db");
                if (File.Exists(pdb))
                {
                    string raw = File.ReadAllText(pdb, Encoding.ASCII);
                    foreach (Match m in Regex.Matches(raw, @"[A-Za-z]:\\[^\x00-\x1F""]*World of Warcraft"))
                        cand.Add(m.Value);
                }
            }
            catch { }

            string[] subdirs = {
                @"Program Files (x86)\World of Warcraft",
                @"Program Files\World of Warcraft",
                @"World of Warcraft",
                @"Games\World of Warcraft",
                @"Battle.net\World of Warcraft" };
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (!d.IsReady) continue;
                    foreach (string s in subdirs) cand.Add(Path.Combine(d.RootDirectory.FullName, s));
                }
            }
            catch { }

            foreach (string c in cand)
            {
                string p = TrimToWowRoot(c);
                if (p != null) return p;
            }
            return null;
        }

        /// <summary>flavor 폴더를 가리키면 한 단계 올려서, 유효한 WoW 루트면 반환</summary>
        public static string TrimToWowRoot(string candidate)
        {
            if (string.IsNullOrEmpty(candidate)) return null;
            try
            {
                string p = candidate.Trim().TrimEnd('\\', '/');
                string leaf = Path.GetFileName(p);
                if (leaf != null && leaf.StartsWith("_") && leaf.EndsWith("_"))
                    p = Path.GetDirectoryName(p);
                if (string.IsNullOrEmpty(p) || !Directory.Exists(p)) return null;
                if (Directory.GetDirectories(p, "_*_").Length == 0) return null;
                return Path.GetFullPath(p);
            }
            catch { return null; }
        }

        public static List<Flavor> GetFlavors(string wowPath)
        {
            List<Flavor> list = new List<Flavor>();
            if (string.IsNullOrEmpty(wowPath) || !Directory.Exists(wowPath)) return list;

            foreach (string dir in Directory.GetDirectories(wowPath, "_*_"))
            {
                Flavor f = new Flavor();
                f.Name = Path.GetFileName(dir);
                f.Path = dir;
                f.AddOnsPath = Path.Combine(dir, @"Interface\AddOns");
                f.WtfPath = Path.Combine(dir, "WTF");
                try
                {
                    f.AddOnCount = Directory.Exists(f.AddOnsPath)
                        ? Directory.GetDirectories(f.AddOnsPath).Length : 0;
                }
                catch { f.AddOnCount = 0; }
                list.Add(f);
            }
            list.Sort(delegate (Flavor a, Flavor b) { return b.AddOnCount.CompareTo(a.AddOnCount); });
            return list;
        }

        // ── .toc 읽기 ───────────────────────────────────────────────
        public static List<AddonInfo> GetAddons(string addOnsPath)
        {
            List<AddonInfo> list = new List<AddonInfo>();
            if (string.IsNullOrEmpty(addOnsPath) || !Directory.Exists(addOnsPath)) return list;

            foreach (string dir in Directory.GetDirectories(addOnsPath))
            {
                AddonInfo a = new AddonInfo();
                a.Folder = Path.GetFileName(dir);

                string toc = null;
                try
                {
                    string[] tocs = Directory.GetFiles(dir, "*.toc");
                    foreach (string t in tocs)
                    {
                        if (string.Equals(Path.GetFileNameWithoutExtension(t), a.Folder,
                                          StringComparison.OrdinalIgnoreCase)) { toc = t; break; }
                    }
                    if (toc == null && tocs.Length > 0) toc = tocs[0];
                }
                catch { }

                if (toc != null)
                {
                    try
                    {
                        foreach (string line in File.ReadAllLines(toc))
                        {
                            Match m = Regex.Match(line, @"^\s*##\s*([^:]+):\s*(.*)$");
                            if (!m.Success) continue;
                            string k = m.Groups[1].Value.Trim();
                            string v = m.Groups[2].Value.Trim();
                            if (k.Equals("Title", StringComparison.OrdinalIgnoreCase) && a.Title == "")
                                a.Title = Regex.Replace(v, @"\|c[0-9a-fA-F]{8}|\|r", "");
                            else if (k.Equals("Version", StringComparison.OrdinalIgnoreCase) && a.Version == "")
                                a.Version = v;
                            else if (k.Equals("X-Curse-Project-ID", StringComparison.OrdinalIgnoreCase)) a.Curse = v;
                            else if (k.Equals("X-Wago-ID", StringComparison.OrdinalIgnoreCase)) a.Wago = v;
                            else if (k.Equals("X-WoWI-ID", StringComparison.OrdinalIgnoreCase)) a.WowI = v;
                        }
                    }
                    catch { }
                }
                if (a.Title == "") a.Title = a.Folder;
                list.Add(a);
            }
            list.Sort(delegate (AddonInfo x, AddonInfo y)
            { return string.Compare(x.Folder, y.Folder, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        // ── zip ─────────────────────────────────────────────────────
        static List<string> CollectFiles(string root)
        {
            List<string> result = new List<string>();
            if (!Directory.Exists(root)) return result;

            foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                if (IsExcluded(Path.GetFileName(f))) continue;
                result.Add(f);
            }
            return result;
        }

        /// <param name="filterConfigPath">
        /// 이 경로의 파일은 그래픽 줄을 빼고 담는다 (Config.wtf). null 이면 전부 그대로.
        /// </param>
        public static int CreateZip(string baseDir, string destZip, string filterConfigPath,
                                    string label, Reporter report)
        {
            List<string> files = CollectFiles(baseDir);
            if (files.Count == 0) return 0;

            string baseFull = Path.GetFullPath(baseDir).TrimEnd('\\') + "\\";
            Directory.CreateDirectory(Path.GetDirectoryName(destZip));
            if (File.Exists(destZip)) File.Delete(destZip);

            using (ZipArchive zip = ZipFile.Open(destZip, ZipArchiveMode.Create))
            {
                for (int i = 0; i < files.Count; i++)
                {
                    string full = Path.GetFullPath(files[i]);
                    string rel = full.Substring(baseFull.Length).Replace('\\', '/');
                    try { zip.CreateEntryFromFile(full, rel, CompressionLevel.Optimal); }
                    catch (Exception ex)
                    {
                        if (report != null) report("  건너뜀: " + rel + " (" + ex.Message + ")", -1);
                        continue;
                    }
                    if (report != null && (i % 150 == 0 || i == files.Count - 1))
                        report(null, (int)((i + 1) * 100L / files.Count));
                }
            }
            return files.Count;
        }

        public static int ExtractZip(string zipPath, string destDir, string label, Reporter report)
        {
            Directory.CreateDirectory(destDir);
            // GetFullPath 는 8.3 단축경로를 펼치므로 비교 기준도 같은 방식으로 정규화한다
            string root = Path.GetFullPath(destDir).TrimEnd('\\') + "\\";
            int count = 0;

            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                int total = zip.Entries.Count, i = 0;
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    i++;
                    if (string.IsNullOrEmpty(e.Name)) continue;   // 디렉터리 엔트리

                    string target = Path.GetFullPath(Path.Combine(root, e.FullName.Replace('/', '\\')));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        if (report != null) report("  수상한 경로 무시: " + e.FullName, -1);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    try
                    {
                        if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                        e.ExtractToFile(target, true);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        if (report != null) report("  실패: " + e.FullName + " (" + ex.Message + ")", -1);
                    }
                    if (report != null && (i % 150 == 0 || i == total))
                        report(null, (int)(i * 100L / Math.Max(1, total)));
                }
            }
            return count;
        }

        // ── 내보내기 ────────────────────────────────────────────────
        public static void Export(ExportOptions o, Reporter report)
        {
            Directory.CreateDirectory(o.OutputDir);
            StringBuilder json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"schema\": 1,\n");
            json.Append("  \"exportedAt\": \"" + Esc(DateTime.Now.ToString("o", CultureInfo.InvariantCulture)) + "\",\n");
            json.Append("  \"sourcePC\": \"" + Esc(Environment.MachineName) + "\",\n");
            json.Append("  \"sourceWow\": \"" + Esc(o.WowPath) + "\",\n");
            json.Append("  \"settings\": " + (o.IncludeSettings ? "true" : "false") + ",\n");
            json.Append("  \"flavors\": {\n");

            for (int fi = 0; fi < o.Flavors.Count; fi++)
            {
                Flavor f = o.Flavors[fi];
                report("=== " + f.Name + " — 애드온 " + f.AddOnCount + "개", 0);

                string addonsZip = f.Name + "-AddOns.zip";
                int n = CreateZip(f.AddOnsPath, Path.Combine(o.OutputDir, addonsZip), null, "AddOns", report);
                report("  애드온 " + n.ToString("N0") + " 파일 → " + addonsZip + Size(Path.Combine(o.OutputDir, addonsZip)), 100);

                string wtfZip = null;
                if (o.IncludeSettings && Directory.Exists(f.WtfPath))
                {
                    wtfZip = f.Name + "-WTF.zip";
                    // 그래픽을 빼더라도 Config.wtf 자체는 담는다 (사운드·이름표 같은 개인 설정이 같이 들어 있다)
                    string cfg = Path.Combine(f.WtfPath, "Config.wtf");
                    int w = CreateZip(f.WtfPath, Path.Combine(o.OutputDir, wtfZip),
                        (o.IncludeGraphicsConfig || !File.Exists(cfg)) ? null : cfg, "WTF", report);
                    if (w == 0) wtfZip = null;
                    else report("  설정 " + w.ToString("N0") + " 파일 → " + wtfZip + Size(Path.Combine(o.OutputDir, wtfZip)), 100);
                }

                List<AddonInfo> addons = GetAddons(f.AddOnsPath);
                json.Append("    \"" + Esc(f.Name) + "\": {\n");
                json.Append("      \"addOnsZip\": \"" + Esc(addonsZip) + "\",\n");
                if (wtfZip != null) json.Append("      \"wtfZip\": \"" + Esc(wtfZip) + "\",\n");
                json.Append("      \"graphicsConfigIncluded\": " + (o.IncludeGraphicsConfig ? "true" : "false") + ",\n");
                json.Append("      \"folders\": " + addons.Count + ",\n");
                json.Append("      \"addons\": [\n");
                for (int i = 0; i < addons.Count; i++)
                {
                    AddonInfo a = addons[i];
                    json.Append("        { \"folder\": \"" + Esc(a.Folder) + "\"");
                    json.Append(", \"title\": \"" + Esc(a.Title) + "\"");
                    json.Append(", \"version\": \"" + Esc(a.Version) + "\"");
                    if (a.Curse != "") json.Append(", \"curse\": \"" + Esc(a.Curse) + "\"");
                    if (a.Wago != "") json.Append(", \"wago\": \"" + Esc(a.Wago) + "\"");
                    if (a.WowI != "") json.Append(", \"wowi\": \"" + Esc(a.WowI) + "\"");
                    json.Append(" }" + (i < addons.Count - 1 ? "," : "") + "\n");
                }
                json.Append("      ]\n");
                json.Append("    }" + (fi < o.Flavors.Count - 1 ? "," : "") + "\n");
            }

            json.Append("  }\n}\n");
            File.WriteAllText(Path.Combine(o.OutputDir, "manifest.json"), json.ToString(),
                              new UTF8Encoding(false));
            report("manifest.json 작성 완료", 100);
        }

        static string Size(string path)
        {
            try { return string.Format(" ({0:N1} MB)", new FileInfo(path).Length / 1048576.0); }
            catch { return ""; }
        }

        static string Esc(string s)
        {
            if (s == null) return "";
            StringBuilder b = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '"') b.Append("\\\"");
                else if (c == '\\') b.Append("\\\\");
                else if (c == '\n') b.Append("\\n");
                else if (c == '\r') b.Append("\\r");
                else if (c == '\t') b.Append("\\t");
                else if (c < 0x20) b.Append("\\u" + ((int)c).ToString("x4"));
                else b.Append(c);
            }
            return b.ToString();
        }

        // ── manifest 읽기 (필요한 필드만, 의존성 없는 간단 파서) ────
        public static ManifestInfo ReadManifest(string sourceDir)
        {
            string path = Path.Combine(sourceDir, "manifest.json");
            if (!File.Exists(path))
                throw new FileNotFoundException("manifest.json 이 없습니다.\n내보내기로 만든 폴더를 선택하세요.\n\n" + sourceDir);

            string text = File.ReadAllText(path, Encoding.UTF8);
            ManifestInfo mi = new ManifestInfo();
            mi.SourcePC = Grab(text, "sourcePC");
            mi.ExportedAt = Grab(text, "exportedAt");
            mi.HasSettings = Regex.IsMatch(text, "\"settings\"\\s*:\\s*true");

            // flavor 블록: "_retail_": { ... "addOnsZip": "...", "wtfZip": "...", "folders": n
            foreach (Match m in Regex.Matches(text, "\"(_[A-Za-z0-9]+_)\"\\s*:\\s*\\{"))
            {
                string name = m.Groups[1].Value;
                int start = m.Index;
                int end = text.IndexOf("\"addons\"", start, StringComparison.Ordinal);
                if (end < 0) end = Math.Min(text.Length, start + 2000);
                string block = text.Substring(start, end - start);

                FlavorEntry fe = new FlavorEntry();
                fe.AddOnsZip = Grab(block, "addOnsZip");
                fe.WtfZip = Grab(block, "wtfZip");
                int n;
                Match fm = Regex.Match(block, "\"folders\"\\s*:\\s*(\\d+)");
                if (fm.Success && int.TryParse(fm.Groups[1].Value, out n)) fe.Folders = n;
                if (fe.AddOnsZip != "") mi.Flavors[name] = fe;
            }
            if (mi.Flavors.Count == 0)
                throw new InvalidDataException("manifest.json 에서 애드온 정보를 읽지 못했습니다.");
            return mi;
        }

        static string Grab(string text, string key)
        {
            Match m = Regex.Match(text, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return "";
            return m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
        }

        // ── 설치 ────────────────────────────────────────────────────
        public static void Install(InstallOptions o, Reporter report)
        {
            string[] procs = { "Wow", "WowClassic", "WowB", "WowT", "WowLauncher" };
            List<string> running = new List<string>();
            foreach (string p in procs)
            {
                try
                {
                    if (System.Diagnostics.Process.GetProcessesByName(p).Length > 0) running.Add(p + ".exe");
                }
                catch { }
            }
            if (running.Count > 0)
                throw new InvalidOperationException(
                    "WoW가 실행 중입니다 (" + string.Join(", ", running.ToArray()) + ").\n" +
                    "게임을 완전히 종료한 뒤 다시 시도하세요.");

            ManifestInfo mi = ReadManifest(o.SourceDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupDir = Path.Combine(o.BackupRoot, stamp);

            foreach (string name in o.FlavorNames)
            {
                if (!mi.Flavors.ContainsKey(name)) continue;
                FlavorEntry fe = mi.Flavors[name];
                string flavorPath = Path.Combine(o.WowPath, name);

                report("=== " + name, 0);
                if (!Directory.Exists(flavorPath))
                {
                    report("  이 PC에 " + name + " 이(가) 없어 건너뜁니다.", 100);
                    continue;
                }

                string addOns = Path.Combine(flavorPath, @"Interface\AddOns");
                string wtf = Path.Combine(flavorPath, "WTF");

                if (o.Backup)
                {
                    if (Directory.Exists(addOns))
                    {
                        int n = CreateZip(addOns, Path.Combine(backupDir, name + "-AddOns.zip"), null, "backup", report);
                        if (n > 0) report("  백업 " + n.ToString("N0") + " 파일 → " + name + "-AddOns.zip", 100);
                    }
                    if (o.IncludeSettings && Directory.Exists(wtf))
                    {
                        int n = CreateZip(wtf, Path.Combine(backupDir, name + "-WTF.zip"), null, "backup", report);
                        if (n > 0) report("  백업 " + n.ToString("N0") + " 파일 → " + name + "-WTF.zip", 100);
                    }
                }

                string zipPath = Path.Combine(o.SourceDir, fe.AddOnsZip);
                if (File.Exists(zipPath))
                {
                    if (o.CleanInstall && Directory.Exists(addOns))
                    {
                        report("  기존 애드온 폴더 비우는 중…", 0);
                        foreach (string d in Directory.GetDirectories(addOns))
                        { try { DeleteTree(d); } catch (Exception ex) { report("  삭제 실패: " + Path.GetFileName(d) + " (" + ex.Message + ")", -1); } }
                        foreach (string ff in Directory.GetFiles(addOns))
                        { try { File.Delete(ff); } catch { } }
                    }
                    int n = ExtractZip(zipPath, addOns, "AddOns", report);
                    report("  애드온 " + n.ToString("N0") + " 파일 설치", 100);
                }
                else report("  " + fe.AddOnsZip + " 파일이 없습니다.", -1);

                if (o.IncludeSettings && fe.WtfZip != "")
                {
                    string wzip = Path.Combine(o.SourceDir, fe.WtfZip);
                    if (File.Exists(wzip))
                    {
                        int n = ExtractZip(wzip, wtf, "WTF", report);
                        report("  설정 " + n.ToString("N0") + " 파일 설치", 100);
                    }
                    else report("  " + fe.WtfZip + " 파일이 없습니다.", -1);
                }
            }

            if (o.Backup && Directory.Exists(backupDir))
                report("기존 상태 백업: " + backupDir, 100);
        }

        static void DeleteTree(string dir)
        {
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(dir, true);
        }

        public static string HumanSize(long bytes)
        {
            if (bytes >= 1073741824) return string.Format("{0:N1} GB", bytes / 1073741824.0);
            if (bytes >= 1048576) return string.Format("{0:N1} MB", bytes / 1048576.0);
            if (bytes >= 1024) return string.Format("{0:N0} KB", bytes / 1024.0);
            return bytes + " B";
        }

        /// <summary>폴더 전체 용량 (내보내기 예상치 안내용)</summary>
        public static long DirectorySize(string dir)
        {
            long total = 0;
            if (!Directory.Exists(dir)) return 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return total;
        }
    }
}
