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

        static FolderMetadataResolver()
        {
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
            var metadata = new FolderMetadata();

            try
            {
                // Walk UP to find parent folder containing genre.ini/box.def
                // TJA files may be in subfolders, but metadata files are in parent
                var currentFolder = Path.GetDirectoryName(tjaFilePath);
                var parentFolder = Path.GetDirectoryName(currentFolder);

                Trace($"Resolve start: TJA={tjaFilePath}");
                Trace($"Resolve folders: current={currentFolder ?? "<null>"}, parent={parentFolder ?? "<null>"}");

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

        private static string ReadTextWithDetection(string path, out string encodingUsed, out int byteCount)
        {
            var bytes = File.ReadAllBytes(path);
            byteCount = bytes.Length;
            string text;

            if (HasUtf8Bom(bytes))
            {
                encodingUsed = "utf-8-bom";
                text = new UTF8Encoding(true).GetString(bytes);
                return text.TrimStart('\uFEFF');
            }

            if (LooksLikeUtf8(bytes))
            {
                encodingUsed = "utf-8";
                text = Encoding.UTF8.GetString(bytes);
                return text.TrimStart('\uFEFF');
            }

            encodingUsed = "shift_jis";
            text = Encoding.GetEncoding("shift_jis").GetString(bytes);
            return text.TrimStart('\uFEFF');
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        }

        private static bool LooksLikeUtf8(byte[] bytes)
        {
            var i = 0;
            while (i < bytes.Length)
            {
                var b = bytes[i];

                if (b <= 0x7F)
                {
                    i++;
                    continue;
                }

                int expectedContinuationBytes;
                if ((b & 0xE0) == 0xC0)
                    expectedContinuationBytes = 1;
                else if ((b & 0xF0) == 0xE0)
                    expectedContinuationBytes = 2;
                else if ((b & 0xF8) == 0xF0)
                    expectedContinuationBytes = 3;
                else
                    return false;

                if (i + expectedContinuationBytes >= bytes.Length)
                    return false;

                for (var j = 1; j <= expectedContinuationBytes; j++)
                {
                    if ((bytes[i + j] & 0xC0) != 0x80)
                        return false;
                }

                i += expectedContinuationBytes + 1;
            }

            return true;
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
