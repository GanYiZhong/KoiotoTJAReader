using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace ZhongTaiko.TJAReader
{
    /// <summary>
    /// Manages TJA metadata caching to avoid re-parsing files on every load.
    /// Uses simple file hashing for change detection.
    /// </summary>
    public class CacheManager
    {
        private const string CACHE_FILENAME = "tja_cache.xml";
        private readonly string _cacheFilePath;
        private XDocument _cacheDoc;

        public CacheManager(string pluginDirectory = null)
        {
            if (pluginDirectory == null)
            {
                pluginDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Koioto");
            }

            _cacheFilePath = Path.Combine(pluginDirectory, CACHE_FILENAME);
            LoadCache();
        }

        /// <summary>
        /// Checks if a TJA file is valid in cache (unchanged since last parse).
        /// </summary>
        public bool IsCacheValid(string filePath)
        {
            if (_cacheDoc == null)
                return false;

            // Normalize path to handle case/separator differences
            var normalizedPath = NormalizePath(filePath);

            var fileElem = _cacheDoc.Root?.Elements("file").FirstOrDefault(e => NormalizePath((string)e.Attribute("path")) == normalizedPath);
            if (fileElem == null)
                return false;

            var cachedHash = (string)fileElem.Attribute("hash");
            var currentHash = ComputeFileHash(filePath);

            return cachedHash == currentHash;
        }

        /// <summary>
        /// Retrieves cached metadata for a TJA file.
        /// </summary>
        public Dictionary<string, TJACourse> GetCachedCourses(string filePath)
        {
            if (_cacheDoc == null)
                return null;

            var fileElem = _cacheDoc.Root?.Elements("file").FirstOrDefault(e => (string)e.Attribute("path") == filePath);
            if (fileElem == null)
                return null;

            var courses = new Dictionary<string, TJACourse>();
            foreach (var courseElem in fileElem.Elements("course"))
            {
                var difficulty = (string)courseElem.Attribute("difficulty");
                var level = int.TryParse((string)courseElem.Attribute("level"), out var lvl) ? lvl : 1;

                courses[difficulty] = new TJACourse { Difficulty = difficulty, Level = level };
            }

            return courses;
        }

        /// <summary>
        /// Retrieves cached full metadata (title, artist, etc.) for a TJA file.
        /// </summary>
        public TJAMetadata GetCachedMetadata(string filePath)
        {
            if (_cacheDoc == null)
                return null;

            var fileElem = _cacheDoc.Root?.Elements("file").FirstOrDefault(e => (string)e.Attribute("path") == filePath);
            if (fileElem == null)
                return null;

            var metaElem = fileElem.Element("metadata");
            if (metaElem == null)
                return null;

            var metadata = new TJAMetadata
            {
                Title = (string)metaElem.Attribute("title") ?? "",
                Subtitle = (string)metaElem.Attribute("subtitle") ?? "",
                Artist = ((string)metaElem.Attribute("artist") ?? "").Split(';'),
                Creator = ((string)metaElem.Attribute("creator") ?? "").Split(';'),
                Audio = (string)metaElem.Attribute("audio"),
                BPM = double.TryParse((string)metaElem.Attribute("bpm"), out var bpm) ? bpm : 120,
                Offset = double.TryParse((string)metaElem.Attribute("offset"), out var off) ? off : 0,
                SongPreview = double.TryParse((string)metaElem.Attribute("preview"), out var prev) ? prev : 0,
                Albumart = (string)metaElem.Attribute("albumart"),
                Background = (string)metaElem.Attribute("background"),
                MovieOffset = double.TryParse((string)metaElem.Attribute("movieoffset"), out var mov) ? mov : (double?)null,
                ScoreMode = int.TryParse((string)metaElem.Attribute("scoremode"), out var sm) ? sm : 1
            };

            return metadata;
        }

        /// <summary>
        /// Stores metadata for a TJA file in cache (courses and full metadata).
        /// </summary>
        public void CacheMetadata(string filePath, TJACourse[] courses, TJAMetadata fullMetadata = null)
        {
            if (_cacheDoc?.Root == null)
                _cacheDoc = new XDocument(new XElement("cache"));

            var normalizedPath = NormalizePath(filePath);

            var existingFile = _cacheDoc.Root.Elements("file").FirstOrDefault(e => NormalizePath((string)e.Attribute("path")) == normalizedPath);

            // Check if content actually changed
            bool contentChanged = true;
            if (existingFile != null)
            {
                var existingCourses = existingFile.Elements("course").Select(c => (string)c.Attribute("difficulty")).ToHashSet();
                var newCourses = courses.Select(c => c.Difficulty).ToHashSet();
                contentChanged = !existingCourses.SetEquals(newCourses);

                if (!contentChanged)
                {
                    System.Diagnostics.Debug.WriteLine($"[CacheManager] Cache content unchanged for {filePath}, skipping write");
                    return;
                }

                existingFile.Remove();
            }

            var fileElem = new XElement("file",
                new XAttribute("path", filePath),
                new XAttribute("hash", ComputeFileHash(filePath)),
                new XAttribute("modified", File.GetLastWriteTimeUtc(filePath).Ticks)
            );

            // Store full metadata for cache hits
            if (fullMetadata != null)
            {
                fileElem.Add(new XElement("metadata",
                    new XAttribute("title", fullMetadata.Title ?? ""),
                    new XAttribute("subtitle", fullMetadata.Subtitle ?? ""),
                    new XAttribute("artist", string.Join(";", fullMetadata.Artist ?? new string[] { })),
                    new XAttribute("creator", string.Join(";", fullMetadata.Creator ?? new string[] { })),
                    new XAttribute("audio", fullMetadata.Audio ?? ""),
                    new XAttribute("bpm", fullMetadata.BPM ?? 120),
                    new XAttribute("offset", fullMetadata.Offset ?? 0),
                    new XAttribute("preview", fullMetadata.SongPreview ?? 0),
                    new XAttribute("albumart", fullMetadata.Albumart ?? ""),
                    new XAttribute("background", fullMetadata.Background ?? ""),
                    new XAttribute("movieoffset", fullMetadata.MovieOffset?.ToString() ?? ""),
                    new XAttribute("scoremode", fullMetadata.ScoreMode ?? 1)
                ));
            }

            foreach (var course in courses)
            {
                fileElem.Add(new XElement("course",
                    new XAttribute("difficulty", course.Difficulty),
                    new XAttribute("level", course.Level ?? 1)
                ));
            }

            _cacheDoc.Root.Add(fileElem);
            System.Diagnostics.Debug.WriteLine($"[CacheManager] Updated cache for {filePath}");
        }

        /// <summary>
        /// Saves cache to disk.
        /// </summary>
        public void Save()
        {
            try
            {
                if (_cacheDoc == null)
                    return;

                var directory = Path.GetDirectoryName(_cacheFilePath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                _cacheDoc.Save(_cacheFilePath);
                System.Diagnostics.Debug.WriteLine($"[CacheManager] Cache saved to {_cacheFilePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheManager] Failed to save cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears all cache and deletes cache file.
        /// </summary>
        public void Clear()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                    File.Delete(_cacheFilePath);

                _cacheDoc = new XDocument(new XElement("cache"));
                System.Diagnostics.Debug.WriteLine("[CacheManager] Cache cleared");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheManager] Failed to clear cache: {ex.Message}");
            }
        }

        private void LoadCache()
        {
            try
            {
                if (!File.Exists(_cacheFilePath))
                {
                    System.Diagnostics.Debug.WriteLine("[CacheManager] No cache file found, starting fresh");
                    _cacheDoc = new XDocument(new XElement("cache"));
                    return;
                }

                _cacheDoc = XDocument.Load(_cacheFilePath);
                var fileCount = _cacheDoc.Root?.Elements("file").Count() ?? 0;
                System.Diagnostics.Debug.WriteLine($"[CacheManager] Loaded cache with {fileCount} entries");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheManager] Failed to load cache: {ex.Message}, starting fresh");
                _cacheDoc = new XDocument(new XElement("cache"));
            }
        }

        private string ComputeFileHash(string filePath)
        {
            try
            {
                using (var sha1 = SHA1.Create())
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        var hash = sha1.ComputeHash(stream);
                        return Convert.ToBase64String(hash);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheManager] Failed to compute hash for {filePath}: {ex.Message}");
                return "";
            }
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";

            // Convert to full path and normalize separators
            var fullPath = Path.GetFullPath(path).ToUpperInvariant();
            return fullPath.Replace('\\', '/');
        }
    }
}
