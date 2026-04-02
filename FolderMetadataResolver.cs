using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZhongTaiko.TJAReader
{
    /// <summary>
    /// Folder metadata container for genre/name/description/albumart
    /// </summary>
    public class FolderMetadata
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Albumart { get; set; }
        public string GenreName { get; set; }
    }

    /// <summary>
    /// Resolves folder metadata from folder.json, genre.ini, and box.def
    /// Priority: folder.json (name/description/albumart) > box.def/genre.ini (GenreName)
    /// </summary>
    public static class FolderMetadataResolver
    {
        private static readonly string DebugLogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory ?? ".",
            "Logs",
            "TJAReader_debug.txt");

        // Cache detected encodings to avoid redundant detection on repeated reads
        private static readonly Dictionary<string, string> EncodingCache = new Dictionary<string, string>();

        // Cache folder metadata to avoid re-reading folder.json/genre.ini/box.def for every file in same folder
        private static readonly Dictionary<string, FolderMetadata> FolderMetadataCache = new Dictionary<string, FolderMetadata>();
        private static readonly object FolderMetadataCacheLock = new object();

        static FolderMetadataResolver()
        {
            try
            {
                // Register CodePages encoding provider for Shift-JIS, GBK, EUC-JP in .NET Core
                Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            }
            catch
            {
                // CodePages not available - will fall back to UTF-8 only
            }

            try
            {
                var generated = RunInitialScan();
                if (generated > 0)
                {
                    Trace($"RunInitialScan: generated {generated} folder.json files, restarting Koioto...");
                    RestartKoioto();
                }
            }
            catch
            {
            }
        }

        private static void RestartKoioto()
        {
            try
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
                System.Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Trace($"RestartKoioto failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Pre-generates folder.json for all directories containing genre.ini or box.def,
        /// so Koioto can read them on the FIRST launch instead of requiring a second restart.
        /// Called once at class initialization (before Koioto's folder.json scan pass).
        /// Returns the number of newly generated folder.json files.
        /// </summary>
        private static int RunInitialScan()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
            Trace($"RunInitialScan: baseDir={baseDir}");

            // Check common songs directory names relative to app base
            foreach (var candidate in new[] { "Songs", "songs", "Song", "song", "Music", "Charts", "楽曲" })
            {
                var songsPath = Path.Combine(baseDir, candidate);
                if (Directory.Exists(songsPath))
                {
                    Trace($"RunInitialScan: found songs dir={songsPath}");
                    return PreGenerateFolderJsonFiles(songsPath);
                }
            }

            Trace("RunInitialScan: no songs directory found, skipping pre-generation");
            return 0;
        }

        private static int PreGenerateFolderJsonFiles(string rootPath)
        {
            var count = 0;
            try
            {
                var dirs = Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories);
                foreach (var dir in dirs)
                {
                    var folderJsonPath = Path.Combine(dir, "folder.json");
                    if (File.Exists(folderJsonPath))
                        continue;

                    var genreIniPath = Path.Combine(dir, "genre.ini");
                    var boxDefPath = Path.Combine(dir, "box.def");

                    string genreName = null;
                    if (File.Exists(genreIniPath))
                        genreName = ParseGenreIni(genreIniPath);
                    if (string.IsNullOrEmpty(genreName) && File.Exists(boxDefPath))
                        genreName = ParseBoxDef(boxDefPath);

                    if (!string.IsNullOrEmpty(genreName))
                    {
                        try
                        {
                            var jsonContent = "{\n" +
                                $"    \"name\": \"{EscapeJson(genreName)}\",\n" +
                                $"    \"description\": \"\",\n" +
                                $"    \"albumart\": \"\"\n" +
                                "}";
                            File.WriteAllText(folderJsonPath, jsonContent, Encoding.UTF8);
                            Trace($"PreGenerate: created {folderJsonPath} name={genreName}");
                            count++;
                        }
                        catch (Exception ex)
                        {
                            Trace($"PreGenerate: failed to write {folderJsonPath}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace($"PreGenerateFolderJsonFiles error: {ex.Message}");
            }
            return count;
        }

        public static FolderMetadata Resolve(string tjaFilePath)
        {
            // Walk UP to find parent folder containing genre.ini/box.def
            // TJA files may be in subfolders, but metadata files are in parent
            var currentFolder = Path.GetDirectoryName(tjaFilePath);
            var parentFolder = Path.GetDirectoryName(currentFolder);

            // Check cache first - significantly reduces I/O on folder with many files
            lock (FolderMetadataCacheLock)
            {
                if (FolderMetadataCache.ContainsKey(currentFolder))
                {
                    Trace($"Resolve cache hit: currentFolder={currentFolder}");
                    return FolderMetadataCache[currentFolder];
                }
                if (!string.IsNullOrEmpty(parentFolder) && FolderMetadataCache.ContainsKey(parentFolder))
                {
                    Trace($"Resolve cache hit: parentFolder={parentFolder}");
                    return FolderMetadataCache[parentFolder];
                }
            }

            var metadata = new FolderMetadata();

            try
            {
                Trace($"Resolve start: TJA={tjaFilePath}");
                Trace($"Resolve folders: current={currentFolder ?? "<null>"}, parent={parentFolder ?? "<null>"}");
                Trace($"Resolve cache miss - reading folder metadata");

                // Try current folder first, then parent folder
                var searchFolders = new[] { currentFolder, parentFolder };

                foreach (var folderPath in searchFolders)
                {
                    if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                    {
                        Trace($"Skipping folder: path={folderPath ?? "<null>"} exists={Directory.Exists(folderPath ?? string.Empty)}");
                        continue;
                    }

                    Trace($"Inspecting folder: {folderPath}");

                    // Step 1: Load folder.json (name, description, albumart)
                    var folderJsonPath = Path.Combine(folderPath, "folder.json");
                    var folderJsonExists = File.Exists(folderJsonPath);
                    Trace($"  folder.json exists={folderJsonExists} path={folderJsonPath}");
                    if (File.Exists(folderJsonPath) && string.IsNullOrEmpty(metadata.Name))
                    {
                        var json = ParseJsonSimple(File.ReadAllText(folderJsonPath, Encoding.UTF8));
                        metadata.Name = json.ContainsKey("name") ? json["name"] : Path.GetFileName(folderPath);
                        metadata.Description = json.ContainsKey("description") ? json["description"] : "";
                        metadata.Albumart = json.ContainsKey("albumart") ? json["albumart"] : "";
                        Trace($"  Loaded folder.json: name={metadata.Name ?? "<null>"}, description={metadata.Description ?? "<null>"}, albumart={metadata.Albumart ?? "<null>"}");
                    }

                    // Step 2: Load box.def (colon-separated #GENRE)
                    var boxDefPath = Path.Combine(folderPath, "box.def");
                    var boxDefExists = File.Exists(boxDefPath);
                    Trace($"  box.def exists={boxDefExists} path={boxDefPath}");
                    if (boxDefExists && string.IsNullOrEmpty(metadata.GenreName))
                    {
                        var genreName = ParseBoxDef(boxDefPath);
                        if (!string.IsNullOrEmpty(genreName))
                        {
                            metadata.GenreName = genreName;
                            Trace($"  Loaded GenreName from box.def: {genreName}");
                        }
                    }

                    // Step 3: Load genre.ini (GenreName only, highest priority)
                    var genreIniPath = Path.Combine(folderPath, "genre.ini");
                    var genreIniExists = File.Exists(genreIniPath);
                    Trace($"  genre.ini exists={genreIniExists} path={genreIniPath}");
                    if (genreIniExists)
                    {
                        var genreName = ParseGenreIni(genreIniPath);
                        if (!string.IsNullOrEmpty(genreName))
                        {
                            metadata.GenreName = genreName;
                            Trace($"  Loaded GenreName from genre.ini: {genreName}");
                        }
                    }
                }

                // Fallback to directory name if no metadata found
                if (string.IsNullOrEmpty(metadata.Name))
                    metadata.Name = Path.GetFileName(currentFolder);

                // AUTO-GENERATE folder.json if genre.ini/box.def found but no folder.json
                // Koioto reads folder.json natively for UI display - plugin API has no GenreName field
                if (!string.IsNullOrEmpty(metadata.GenreName))
                {
                    foreach (var folderPath in searchFolders)
                    {
                        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                            continue;

                        var folderJsonPath = Path.Combine(folderPath, "folder.json");
                        var hasGenreSource = File.Exists(Path.Combine(folderPath, "genre.ini"))
                                          || File.Exists(Path.Combine(folderPath, "box.def"));

                        if (hasGenreSource && !File.Exists(folderJsonPath))
                        {
                            try
                            {
                                var jsonContent = "{\n" +
                                    $"    \"name\": \"{EscapeJson(metadata.GenreName)}\",\n" +
                                    $"    \"description\": \"\",\n" +
                                    $"    \"albumart\": \"\"\n" +
                                    "}";
                                File.WriteAllText(folderJsonPath, jsonContent, Encoding.UTF8);
                                Trace($"AUTO-GENERATED folder.json at {folderJsonPath} with name={metadata.GenreName}");
                            }
                            catch (Exception ex)
                            {
                                Trace($"Failed to auto-generate folder.json: {ex.Message}");
                            }
                        }
                    }
                }

                Trace(
                    $"Resolve complete: Name={metadata.Name ?? "<null>"}, Description={metadata.Description ?? "<null>"}, Albumart={metadata.Albumart ?? "<null>"}, GenreName={metadata.GenreName ?? "<null>"}");

                // Cache result for this folder
                lock (FolderMetadataCacheLock)
                {
                    if (!FolderMetadataCache.ContainsKey(currentFolder))
                        FolderMetadataCache[currentFolder] = metadata;
                    if (!string.IsNullOrEmpty(parentFolder) && !FolderMetadataCache.ContainsKey(parentFolder) && !metadata.Name.Equals(currentFolder))
                        FolderMetadataCache[parentFolder] = metadata;
                }
            }
            catch (Exception ex)
            {
                Trace($"Error resolving folder metadata: {ex}");
                if (string.IsNullOrEmpty(metadata.Name))
                    metadata.Name = Path.GetFileName(Path.GetDirectoryName(tjaFilePath));
            }

            return metadata;
        }

        /// <summary>
        /// Parse genre.ini file and extract GenreName
        /// Format: GenreName=value (case-insensitive)
        /// Encoding: UTF-8 first, fallback to Shift-JIS
        /// </summary>
        private static string ParseGenreIni(string genreIniPath)
        {
            try
            {
                var content = ReadTextWithDetection(genreIniPath, out var encodingUsed, out var byteCount);
                Trace($"ParseGenreIni: path={genreIniPath}, bytes={byteCount}, encoding={encodingUsed}");

                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    Trace($"  genre.ini line[{i}]={line}");

                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";"))
                        continue;

                    if (line.StartsWith("GenreName", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split('=');
                        Trace($"  genre.ini GenreName candidate: parts={parts.Length}");
                        if (parts.Length >= 2)
                        {
                            var value = parts[1].Trim();
                            if (!string.IsNullOrEmpty(value))
                            {
                                Trace($"  genre.ini parsed GenreName={value}");
                                return value;
                            }
                        }
                    }
                }

                Trace("  genre.ini parse result: no GenreName found");
            }
            catch (Exception ex)
            {
                Trace($"Error parsing genre.ini: {ex}");
            }

            return null;
        }

        /// <summary>
        /// Parse box.def file and extract #GENRE entries
        /// Format: #GENRE:GenreName (colon-separated)
        /// Returns: GenreName string
        /// </summary>
        private static string ParseBoxDef(string boxDefPath)
        {
            try
            {
                var content = ReadTextWithDetection(boxDefPath, out var encodingUsed, out var byteCount);
                Trace($"ParseBoxDef: path={boxDefPath}, bytes={byteCount}, encoding={encodingUsed}");

                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var trimmed = line.Trim();
                    Trace($"  box.def line[{i}]={trimmed}");

                    // Skip comments and empty lines
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";"))
                        continue;

                    // Parse #GENRE entries: #GENRE:GenreName
                    if (trimmed.StartsWith("#GENRE", StringComparison.OrdinalIgnoreCase))
                    {
                        // Format: #GENRE:GenreName
                        var colonIdx = trimmed.IndexOf(':');
                        if (colonIdx > 0)
                        {
                            var genreName = trimmed.Substring(colonIdx + 1).Trim();
                            if (!string.IsNullOrEmpty(genreName))
                            {
                                Trace($"  box.def parsed GenreName={genreName}");
                                return genreName;
                            }
                        }
                    }
                }

                Trace("  box.def parse result: no #GENRE found");
            }
            catch (Exception ex)
            {
                Trace($"Error parsing box.def: {ex}");
            }

            return null;
        }

        /// <summary>
        /// Simple JSON parser for folder.json (only extracts string values)
        /// Returns: Dictionary of key-value pairs
        /// </summary>
        private static Dictionary<string, string> ParseJsonSimple(string json)
        {
            var result = new Dictionary<string, string>();

            try
            {
                // Very basic JSON parsing for folder.json
                // Format: {"name":"value","description":"value","albumart":"value"}
                var lines = json.Split(',');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Contains(":"))
                    {
                        var parts = trimmed.Split(':');
                        if (parts.Length == 2)
                        {
                            var key = parts[0].Trim().Trim('"', '{', '}').ToLower();
                            var value = parts[1].Trim().Trim('"');
                            result[key] = value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace($"Error parsing folder.json: {ex}");
            }

            return result;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        internal static string ReadTextWithDetection(string path)
        {
            return ReadTextWithDetection(path, out _, out _);
        }

        internal static string ReadTextWithDetection(string path, out string encodingUsed, out int byteCount)
        {
            var bytes = File.ReadAllBytes(path);
            byteCount = bytes.Length;
            string text;

            // Check encoding cache first (huge speedup for repeated reads)
            if (EncodingCache.TryGetValue(path, out var cachedEncoding))
            {
                encodingUsed = cachedEncoding;
                try
                {
                    if (cachedEncoding == "utf-8-bom")
                    {
                        text = new UTF8Encoding(true, true).GetString(bytes);
                    }
                    else if (cachedEncoding == "utf-8")
                    {
                        text = Encoding.UTF8.GetString(bytes);
                    }
                    else
                    {
                        text = Encoding.GetEncoding(int.Parse(cachedEncoding.Split('_')[0])).GetString(bytes);
                    }
                    return text.TrimStart('\uFEFF');
                }
                catch
                {
                    // Cache entry invalid, fall through to re-detect
                }
            }

            if (HasUtf8Bom(bytes))
            {
                encodingUsed = "utf-8-bom";
                EncodingCache[path] = encodingUsed;
                text = new UTF8Encoding(true, true).GetString(bytes);
                return text.TrimStart('\uFEFF');
            }

            if (TryDecode(bytes, new UTF8Encoding(false, true), out text))
            {
                encodingUsed = "utf-8";
                EncodingCache[path] = encodingUsed;
                return text.TrimStart('\uFEFF');
            }

            var candidates = new List<DecodedTextCandidate>();
            AddDecodeCandidate(candidates, bytes, 932, "shift_jis", path, 0);
            AddDecodeCandidate(candidates, bytes, 936, "gbk", path, 1);

            DecodedTextCandidate bestCandidate = null;
            foreach (var candidate in candidates)
            {
                if (bestCandidate == null
                    || candidate.Score > bestCandidate.Score
                    || (candidate.Score == bestCandidate.Score && candidate.Priority < bestCandidate.Priority))
                {
                    bestCandidate = candidate;
                }
            }

            if (bestCandidate != null)
            {
                encodingUsed = bestCandidate.Name;
                EncodingCache[path] = encodingUsed;
                Trace($"Encoding selected: {encodingUsed} (score={bestCandidate.Score}) for {path}");
                return bestCandidate.Text.TrimStart('\uFEFF');
            }

            // All encoding candidates unavailable — fall back to UTF-8 (lossy but functional)
            encodingUsed = "utf-8-fallback";
            EncodingCache[path] = encodingUsed;
            Trace($"Encoding fallback: no CodePages available, using UTF-8 for {path}");
            return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        }

        private static void AddDecodeCandidate(List<DecodedTextCandidate> candidates, byte[] bytes, int codePage, string encodingName, string path, int priority)
        {
            try
            {
                // Use lenient decode directly (fast, never throws)
                var text = Encoding.GetEncoding(codePage).GetString(bytes);
                var score = ScoreDecodedText(path, text, encodingName);

                candidates.Add(new DecodedTextCandidate
                {
                    Name = encodingName,
                    Text = text,
                    Score = score,
                    Priority = priority
                });
            }
            catch (NotSupportedException)
            {
                // Encoding not available (e.g. .NET Core without CodePages) — skip this candidate
            }
        }

        private static Encoding GetStrictEncoding(int codePage)
        {
            return Encoding.GetEncoding(
                codePage,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
        }

        private static bool TryDecode(byte[] bytes, Encoding encoding, out string text)
        {
            try
            {
                text = encoding.GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                text = null;
                return false;
            }
        }

        private static int ScoreDecodedText(string path, string text, string encodingName)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var score = 0;
            var upperText = text.ToUpperInvariant();

            if (upperText.Contains("TITLE:")) score += 30;
            if (upperText.Contains("SUBTITLE:")) score += 20;
            if (upperText.Contains("WAVE:")) score += 20;
            if (upperText.Contains("COURSE:")) score += 20;
            if (upperText.Contains("LEVEL:")) score += 15;
            if (upperText.Contains("#START")) score += 30;
            if (upperText.Contains("#END")) score += 30;
            if (upperText.Contains("#GENRE")) score += 30;
            if (upperText.Contains("GENRENAME=")) score += 30;

            var controlCount = 0;
            var suspiciousScriptCount = 0;
            var cjkCount = 0;
            var kanaCount = 0;

            foreach (var ch in text)
            {
                if (ch == '\0')
                    score -= 100;

                if (char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t')
                    controlCount++;

                if (IsCjk(ch))
                    cjkCount++;

                if (IsJapaneseKana(ch))
                    kanaCount++;

                if (IsSuspiciousScript(ch))
                    suspiciousScriptCount++;
            }

            score -= controlCount * 20;
            score -= suspiciousScriptCount * 4;

            if (kanaCount > 0)
                score += 20;

            if (cjkCount > 0)
                score += 10;

            if (encodingName == "shift_jis" || encodingName == "euc-jp")
            {
                if (kanaCount > 0)
                    score += 15;
            }
            else if (encodingName == "gbk")
            {
                // Only penalize GBK when kana detected (strong Japanese indicator)
                if (kanaCount > 0)
                    score -= 10;
            }

            var extension = Path.GetExtension(path) ?? string.Empty;
            if (extension.Equals(".ini", StringComparison.OrdinalIgnoreCase) && upperText.Contains("GENRENAME="))
                score += 20;

            if (extension.Equals(".def", StringComparison.OrdinalIgnoreCase) && upperText.Contains("#GENRE"))
                score += 20;

            if (extension.Equals(".tja", StringComparison.OrdinalIgnoreCase) && upperText.Contains("#START"))
                score += 20;

            return score;
        }

        private static bool IsCjk(char ch)
        {
            return (ch >= '\u3400' && ch <= '\u4DBF')
                || (ch >= '\u4E00' && ch <= '\u9FFF')
                || (ch >= '\uF900' && ch <= '\uFAFF');
        }

        private static bool IsJapaneseKana(char ch)
        {
            return (ch >= '\u3040' && ch <= '\u309F')
                || (ch >= '\u30A0' && ch <= '\u30FF')
                || (ch >= '\uFF66' && ch <= '\uFF9D');
        }

        private static bool IsSuspiciousScript(char ch)
        {
            return (ch >= '\u0080' && ch <= '\u009F')
                || (ch >= '\u0370' && ch <= '\u03FF')
                || (ch >= '\u0400' && ch <= '\u04FF')
                || (ch >= '\uE000' && ch <= '\uF8FF')
                || ch == '\uFFFD';
        }

        private sealed class DecodedTextCandidate
        {
            public string Name { get; set; }
            public string Text { get; set; }
            public int Score { get; set; }
            public int Priority { get; set; }
        }

        internal static void Trace(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [TJAReader] {message}";

            try
            {
                System.Diagnostics.Debug.WriteLine(line);
            }
            catch
            {
            }

            try
            {
                var logDir = Path.GetDirectoryName(DebugLogPath);
                if (!string.IsNullOrEmpty(logDir))
                    Directory.CreateDirectory(logDir);

                File.AppendAllLines(DebugLogPath, new[] { line });
            }
            catch
            {
            }
        }
    }
}
