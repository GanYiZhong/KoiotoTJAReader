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
        public static FolderMetadata Resolve(string tjaFilePath)
        {
            var metadata = new FolderMetadata();

            try
            {
                // Walk UP to find parent folder containing genre.ini/box.def
                // TJA files may be in subfolders, but metadata files are in parent
                var currentFolder = Path.GetDirectoryName(tjaFilePath);
                var parentFolder = Path.GetDirectoryName(currentFolder);

                System.Diagnostics.Debug.WriteLine($"[TJAReader] Resolving metadata: TJA={Path.GetFileName(tjaFilePath)}, CurrentFolder={Path.GetFileName(currentFolder)}, ParentFolder={Path.GetFileName(parentFolder)}");

                // Try current folder first, then parent folder
                var searchFolders = new[] { currentFolder, parentFolder };

                foreach (var folderPath in searchFolders)
                {
                    if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                        continue;

                    // Step 1: Load folder.json (name, description, albumart)
                    var folderJsonPath = Path.Combine(folderPath, "folder.json");
                    if (File.Exists(folderJsonPath) && string.IsNullOrEmpty(metadata.Name))
                    {
                        var json = ParseJsonSimple(File.ReadAllText(folderJsonPath, Encoding.UTF8));
                        metadata.Name = json.ContainsKey("name") ? json["name"] : Path.GetFileName(folderPath);
                        metadata.Description = json.ContainsKey("description") ? json["description"] : "";
                        metadata.Albumart = json.ContainsKey("albumart") ? json["albumart"] : "";
                        System.Diagnostics.Debug.WriteLine($"[TJAReader] Loaded folder.json from {Path.GetFileName(folderPath)}: name={metadata.Name}");
                    }

                    // Step 2: Load box.def (colon-separated #GENRE)
                    var boxDefPath = Path.Combine(folderPath, "box.def");
                    if (File.Exists(boxDefPath) && string.IsNullOrEmpty(metadata.GenreName))
                    {
                        var genreName = ParseBoxDef(boxDefPath);
                        if (!string.IsNullOrEmpty(genreName))
                        {
                            metadata.GenreName = genreName;
                            System.Diagnostics.Debug.WriteLine($"[TJAReader] Loaded GenreName from box.def: {genreName}");
                            // Don't return yet - genre.ini may override
                        }
                    }

                    // Step 3: Load genre.ini (GenreName only, highest priority)
                    var genreIniPath = Path.Combine(folderPath, "genre.ini");
                    if (File.Exists(genreIniPath) && string.IsNullOrEmpty(metadata.GenreName))
                    {
                        var genreName = ParseGenreIni(genreIniPath);
                        if (!string.IsNullOrEmpty(genreName))
                        {
                            metadata.GenreName = genreName;
                            System.Diagnostics.Debug.WriteLine($"[TJAReader] Loaded GenreName from genre.ini: {genreName}");
                        }
                    }

                    // If we found metadata, stop searching parent folders
                    if (!string.IsNullOrEmpty(metadata.Name) || !string.IsNullOrEmpty(metadata.GenreName))
                        break;
                }

                // Fallback to directory name if no metadata found
                if (string.IsNullOrEmpty(metadata.Name))
                    metadata.Name = Path.GetFileName(currentFolder);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TJAReader] Error resolving folder metadata: {ex.Message}");
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
                string content = null;

                // Try UTF-8 first
                try
                {
                    content = File.ReadAllText(genreIniPath, Encoding.UTF8);
                }
                catch
                {
                    // Fallback to Shift-JIS (CP932)
                    var encoding = Encoding.GetEncoding("shift_jis");
                    content = File.ReadAllText(genreIniPath, encoding);
                }

                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";"))
                        continue;

                    if (line.StartsWith("GenreName", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split('=');
                        if (parts.Length >= 2)
                        {
                            var value = parts[1].Trim();
                            if (!string.IsNullOrEmpty(value))
                                return value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TJAReader] Error parsing genre.ini: {ex.Message}");
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
                string content = null;

                // Try UTF-8 first
                try
                {
                    content = File.ReadAllText(boxDefPath, Encoding.UTF8);
                }
                catch
                {
                    // Fallback to Shift-JIS (CP932)
                    var encoding = Encoding.GetEncoding("shift_jis");
                    content = File.ReadAllText(boxDefPath, encoding);
                }

                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();

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
                                return genreName;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TJAReader] Error parsing box.def: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"[TJAReader] Error parsing folder.json: {ex.Message}");
            }

            return result;
        }
    }
}
