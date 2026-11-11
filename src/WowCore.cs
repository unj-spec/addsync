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

        /// <summary>내용 해시까지 비교해 바뀐 애드온을 정확히 가려낸다 (느림)</summary>
        public bool DeepCheck = false;
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
        public int KeepBackups = 5;

        /// <summary>다른 배틀넷 계정으로 옮길 때 WTF\Account 폴더 번호를 바꿔준다</summary>
        public bool MapAccount = false;
        public string SourceAccountId = "";
        public string TargetAccountId = "";
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
                    try
                    {
                        if (filterConfigPath != null && string.Equals(full,
                                Path.GetFullPath(filterConfigPath), StringComparison.OrdinalIgnoreCase))
                        {
                            // Config.wtf 는 그래픽 줄만 빼고 담는다
                            int kept, dropped;
                            string body = FilterConfigWtf(full, out kept, out dropped);
                            ZipArchiveEntry entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
                            using (StreamWriter sw = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                                sw.Write(body);
                            if (report != null)
                                report("  Config.wtf — 개인 설정 " + kept + "개 포함, 그래픽 "
                                       + dropped + "개 제외", -1);
                        }
                        else zip.CreateEntryFromFile(full, rel, CompressionLevel.Optimal);
                    }
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
            return ExtractZip(zipPath, destDir, label, report, null);
        }

        /// <param name="mapEntry">zip 안 경로를 바꿔 담고 싶을 때 (계정 폴더 번호 교체)</param>
        public static int ExtractZip(string zipPath, string destDir, string label, Reporter report,
                                     Func<string, string> mapEntry)
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

                    string name = mapEntry != null ? mapEntry(e.FullName) : e.FullName;
                    string target = Path.GetFullPath(Path.Combine(root, name.Replace('/', '\\')));
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

        /// <summary>와우가 실행 중이면 막는다 (파일이 잠겨 있다)</summary>
        public static void EnsureWowClosed()
        {
            string[] procs = { "Wow", "WowClassic", "WowB", "WowT" };
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
                    "와우가 실행 중입니다 (" + string.Join(", ", running.ToArray()) + ").\n" +
                    "게임을 완전히 종료한 뒤 다시 시도하세요.");
        }

        public static void DeleteTree(string dir)
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
