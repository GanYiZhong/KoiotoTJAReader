using Koioto.Support;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZhongTaiko.TJAReader
{
    /// <summary>
    /// TJA format reader plugin for Koioto.
    /// Directly reads .tja files without conversion to TCC format.
    /// Uses JSON cache to speed up repeated loads.
    /// </summary>
    public class FileReader : Koioto.Plugin.IChartReadable
    {
        private static CacheManager _cache = new CacheManager();

        public string Name => "TJA Reader";

        public string[] Creator => new string[] { "ZhongTaiko" };

        public string Description => "Koioto file Reader plugin for Taiko Jiro TJA format.\n" +
            "Reads .tja files directly without conversion.";

        public string Version => "1.0";

        public string[] GetExtensions()
        {
            return new string[] { ".tja" };
        }

        public SongSelectMetadata GetSelectable(string filePath)
        {
            try
            {
                FolderMetadataResolver.Trace($"GetSelectable start: filePath={filePath}");

                // Check if file is already cached (unchanged)
                TJAMetadata metadata = null;
                TJACourse[] courses = null;

                if (_cache.IsCacheValid(filePath))
                {
                    FolderMetadataResolver.Trace($"[Cache HIT] Using cached data for {filePath}");
                    var cachedCourses = _cache.GetCachedCourses(filePath);
                    if (cachedCourses != null && cachedCourses.Count > 0)
                    {
                        // Reconstruct metadata with defaults (sufficient for song selection)
                        metadata = new TJAMetadata();
                        courses = cachedCourses.Values.ToArray();
                    }
                }

                // If not in cache, parse the TJA file
                if (metadata == null || courses == null || courses.Length == 0)
                {
                    FolderMetadataResolver.Trace($"[Cache MISS] Parsing {filePath}");
                    var tjaText = ReadTjaText(filePath);
                    var parser = new TJAParser(tjaText);

                    metadata = parser.GetMetadata();
                    courses = parser.GetCourses();

                    // Cache the course info for next time
                    if (courses.Length > 0)
                    {
                        _cache.CacheMetadata(filePath, courses);
                        _cache.Save();
                    }
                }

                if (courses.Length == 0)
                {
                    return null;
                }

                // Resolve folder metadata (genre.ini, box.def, folder.json)
                var folderMeta = FolderMetadataResolver.Resolve(filePath);
                FolderMetadataResolver.Trace(
                    $"GetSelectable folderMeta after Resolve: Name={folderMeta?.Name ?? "<null>"}, Description={folderMeta?.Description ?? "<null>"}, Albumart={folderMeta?.Albumart ?? "<null>"}, GenreName={folderMeta?.GenreName ?? "<null>"}");

                var result = new SongSelectMetadata
                {
                    FilePath = filePath,
                    Title = metadata.Title,
                    SubTitle = metadata.Subtitle,
                    BPM = metadata.BPM,
                    Artist = metadata.Artist?.Length > 0 ? metadata.Artist : new string[] { "" },
                    Creator = metadata.Creator?.Length > 0 ? metadata.Creator : new string[] { "" },
                    PreviewSong = ResolveAudioPath(filePath, metadata.Audio),
                    SongPreviewTime = metadata.SongPreview,
                    AlbumartPath = (folderMeta.Albumart != null && folderMeta.Albumart.Length > 0)
                        ? GetPath(Path.GetDirectoryName(filePath), folderMeta.Albumart)
                        : (metadata.Albumart != null ? GetPath(filePath, metadata.Albumart) : null)
                };

                // NOTE: folderMeta.Name, folderMeta.Description, folderMeta.GenreName are available
                // for display in UI if Koioto's SongSelectMetadata is extended in the future

                foreach (var course in courses)
                {
                    var diff = new Difficulty();
                    diff.Level = course.Level ?? 1;
                    result[GetCoursesFromString(course.Difficulty)] = diff;
                }

                FolderMetadataResolver.Trace(
                    $"GetSelectable complete: Title={result.Title ?? "<null>"}, AlbumartPath={result.AlbumartPath ?? "<null>"}, GenreName(unmapped)={folderMeta?.GenreName ?? "<null>"}");

                return result;
            }
            catch (System.Exception ex)
            {
                FolderMetadataResolver.Trace($"GetSelectable error: {ex}");
                return null;
            }
        }

        public Player<Playable> GetPlayable(string filePath, Koioto.Support.FileReader.Courses courses)
        {
            try
            {
                var tjaText = ReadTjaText(filePath);
                var parser = new TJAParser(tjaText);

                var metadata = parser.GetMetadata();
                var tjaCourseName = GetTJACourseName(courses);
                var courseData = parser.GetCourse(tjaCourseName);

                if (courseData == null)
                {
                    return null;
                }

                var otcc = courseData;
                var result = new Player<Playable>();

                result.Single = TJAParser.CourseParser.Parse(metadata, otcc);
                result.Multiple = new Playable[0];

                return result;
            }
            catch
            {
                return null;
            }
        }

        public ChartMetadata GetChartInfo(string filePath)
        {
            try
            {
                var tjaText = ReadTjaText(filePath);
                var parser = new TJAParser(tjaText);
                var metadata = parser.GetMetadata();

                var chartMetadata = new ChartMetadata();

                chartMetadata.Title = new string[1] { metadata.Title };
                chartMetadata.Subtitle = new string[1] { metadata.Subtitle };

                chartMetadata.Artist = new string[1][];
                chartMetadata.Artist[0] = metadata.Artist ?? new string[] { "" };

                chartMetadata.Creator = new string[1][];
                chartMetadata.Creator[0] = metadata.Creator ?? new string[] { "" };

                chartMetadata.Audio = new string[1] { ResolveAudioPath(filePath, metadata.Audio) };

                chartMetadata.Background = new string[1];
                chartMetadata.Background[0] = null;

                chartMetadata.Movieoffset = new double?[1];
                chartMetadata.Movieoffset[0] = null;

                chartMetadata.BPM = new double?[1];
                chartMetadata.BPM[0] = metadata.BPM;

                chartMetadata.Offset = new double?[1];
                chartMetadata.Offset[0] = metadata.Offset;

                return chartMetadata;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 獲取課程的分數配置
        /// </summary>
        public ScoringConfig GetScoringConfig(string filePath, Koioto.Support.FileReader.Courses course)
        {
            try
            {
                var tjaText = ReadTjaText(filePath);
                var parser = new TJAParser(tjaText);

                var metadata = parser.GetMetadata();
                var tjaCourseName = GetTJACourseName(course);
                var courseData = parser.GetCourse(tjaCourseName);

                if (courseData == null)
                    return null;

                return new ScoringConfig(
                    metadata.ScoreMode ?? 1,
                    courseData.Scoreinit ?? 1000,
                    courseData.Scorediff ?? 100
                );
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 獲取課程的分數配置和遊玩數據
        /// </summary>
        public PlayableWithScoring GetPlayableWithScoring(string filePath, Koioto.Support.FileReader.Courses course)
        {
            try
            {
                var player = GetPlayable(filePath, course);
                if (player == null)
                    return null;

                var scoring = GetScoringConfig(filePath, course);
                return new PlayableWithScoring(player.Single, scoring);
            }
            catch
            {
                return null;
            }
        }

        private Koioto.Support.FileReader.Courses GetCoursesFromString(string str)
        {
            switch (str.ToLower())
            {
                case "easy":
                case "0":
                    return Koioto.Support.FileReader.Courses.Easy;
                case "normal":
                case "1":
                    return Koioto.Support.FileReader.Courses.Normal;
                case "hard":
                case "2":
                    return Koioto.Support.FileReader.Courses.Hard;
                case "edit":
                case "ura":
                case "4":
                    return Koioto.Support.FileReader.Courses.Edit;
                case "oni":
                case "3":
                default:
                    return Koioto.Support.FileReader.Courses.Oni;
            }
        }

        private string GetTJACourseName(Koioto.Support.FileReader.Courses course)
        {
            switch (course)
            {
                case Koioto.Support.FileReader.Courses.Easy: return "Easy";
                case Koioto.Support.FileReader.Courses.Normal: return "Normal";
                case Koioto.Support.FileReader.Courses.Hard: return "Hard";
                case Koioto.Support.FileReader.Courses.Edit: return "Edit";
                case Koioto.Support.FileReader.Courses.Oni:
                default: return "Oni";
            }
        }

        private string GetPath(string origin, string target)
        {
            return Path.Combine(Path.GetDirectoryName(origin), target);
        }

        /// <summary>
        /// Resolves audio path with fallback: if WAVE path doesn't exist,
        /// search for audio file matching the TJA filename in same directory.
        /// Handles cases where TITLE/WAVE fields have different characters than the actual filename.
        /// </summary>
        private string ResolveAudioPath(string tjaFilePath, string waveValue)
        {
            if (string.IsNullOrEmpty(waveValue))
                return null;

            var dir = Path.GetDirectoryName(tjaFilePath);
            var wavePath = Path.Combine(dir, waveValue);
            if (File.Exists(wavePath))
                return wavePath;

            // Fallback: try audio file with same base name as TJA
            var tjaBase = Path.GetFileNameWithoutExtension(tjaFilePath);
            foreach (var ext in new[] { ".ogg", ".wav", ".mp3", ".flac" })
            {
                var fallback = Path.Combine(dir, tjaBase + ext);
                if (File.Exists(fallback))
                    return fallback;
            }

            return wavePath;
        }

        private static string ReadTjaText(string filePath)
        {
            var text = FolderMetadataResolver.ReadTextWithDetection(filePath, out var encodingUsed, out var byteCount);
            FolderMetadataResolver.Trace($"ReadTjaText: path={filePath}, bytes={byteCount}, encoding={encodingUsed}");
            return text;
        }

        /// <summary>
        /// Reconstructs TJACourse array from cache.
        /// </summary>
        private TJACourse[] ReconstructCoursesFromCache(Dictionary<string, TJACourse> cachedCourses)
        {
            if (cachedCourses == null || cachedCourses.Count == 0)
                return new TJACourse[0];

            return cachedCourses.Values.ToArray();
        }

        /// <summary>
        /// Reconstructs TJAMetadata. Since cache only stores courses, use defaults for metadata.
        /// </summary>
        private TJAMetadata ReconstructMetadataFromCache()
        {
            return new TJAMetadata(); // Return with defaults; metadata is loaded fresh anyway
        }
    }

    public class TJAMetadata
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string[] Artist { get; set; } = new string[] { "" };
        public string[] Creator { get; set; } = new string[] { "" };
        public string Audio { get; set; }
        public double? BPM { get; set; } = 120;
        public double? Offset { get; set; } = 0;
        public double? SongPreview { get; set; } = 0;
        public string Albumart { get; set; }
        public string Background { get; set; }
        public double? MovieOffset { get; set; }
        public int? ScoreMode { get; set; } = 1; // Default to AC 8-14 generation (1)
    }

    public class TJACourse
    {
        public string Difficulty { get; set; }
        public int? Level { get; set; }
    }

    public class TJACourseData
    {
        public int? Scoreinit { get; set; }
        public int? Scorediff { get; set; }
        public int?[] Balloon { get; set; }
        public System.Collections.Generic.List<string[]> Measures { get; set; }
    }

    /// <summary>
    /// 分數配置包裝類，用於關聯分數計算參數到 Playable 對象
    /// </summary>
    public class ScoringConfig
    {
        /// <summary>分數模式 (0=AC1-7, 1=AC8-14, 2=AC0)</summary>
        public int ScoreMode { get; set; }

        /// <summary>基礎分數 (INIT 值)</summary>
        public int ScoreInit { get; set; }

        /// <summary>難度倍增器 (DIFF 值)</summary>
        public int ScoreDiff { get; set; }

        public ScoringConfig()
        {
            ScoreMode = 1;
            ScoreInit = 1000;
            ScoreDiff = 100;
        }

        public ScoringConfig(int mode, int init, int diff)
        {
            ScoreMode = mode;
            ScoreInit = init;
            ScoreDiff = diff;
        }

        /// <summary>計算指定 combo 的分數點數</summary>
        public int CalculatePoints(int combo)
        {
            return TJAParser.ScoringCalculator.CalculatePoints(combo, ScoreMode, ScoreInit, ScoreDiff);
        }

        /// <summary>應用 TJA 分數除法規則</summary>
        public static int ApplyDivision(int score)
        {
            return TJAParser.ScoringCalculator.ApplyScoreDivision(score);
        }
    }

    public class PlayableWithScoring
    {
        public Playable Playable { get; set; }
        public ScoringConfig ScoringConfig { get; set; }

        public PlayableWithScoring(Playable playable, ScoringConfig scoring)
        {
            Playable = playable;
            ScoringConfig = scoring;
        }
    }
}
