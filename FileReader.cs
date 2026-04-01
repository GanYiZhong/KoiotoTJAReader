using Koioto.Support;
using System.IO;
using System.Linq;
using System.Text;

namespace ZhongTaiko.TJAReader
{
    /// <summary>
    /// TJA format reader plugin for Koioto.
    /// Directly reads .tja files without conversion to TCC format.
    /// </summary>
    public class FileReader : Koioto.Plugin.IChartReadable
    {
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
                var tjaText = File.ReadAllText(filePath, Encoding.UTF8);
                var parser = new TJAParser(tjaText);

                var metadata = parser.GetMetadata();
                var courses = parser.GetCourses();

                if (courses.Length == 0)
                {
                    return null;
                }

                var result = new SongSelectMetadata
                {
                    FilePath = filePath,
                    Title = metadata.Title,
                    SubTitle = metadata.Subtitle,
                    BPM = metadata.BPM,
                    Artist = metadata.Artist?.Length > 0 ? metadata.Artist : new string[] { "" },
                    Creator = metadata.Creator?.Length > 0 ? metadata.Creator : new string[] { "" },
                    PreviewSong = metadata.Audio != null ? GetPath(filePath, metadata.Audio) : null,
                    SongPreviewTime = metadata.SongPreview,
                    AlbumartPath = metadata.Albumart != null ? GetPath(filePath, metadata.Albumart) : null
                };

                foreach (var course in courses)
                {
                    var diff = new Difficulty();
                    diff.Level = course.Level ?? 1;
                    result[GetCoursesFromString(course.Difficulty)] = diff;
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        public Player<Playable> GetPlayable(string filePath, Koioto.Support.FileReader.Courses courses)
        {
            try
            {
                var tjaText = File.ReadAllText(filePath, Encoding.UTF8);
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
                var tjaText = File.ReadAllText(filePath, Encoding.UTF8);
                var parser = new TJAParser(tjaText);
                var metadata = parser.GetMetadata();

                var chartMetadata = new ChartMetadata();

                chartMetadata.Title = new string[1] { metadata.Title };
                chartMetadata.Subtitle = new string[1] { metadata.Subtitle };

                chartMetadata.Artist = new string[1][];
                chartMetadata.Artist[0] = metadata.Artist ?? new string[] { "" };

                chartMetadata.Creator = new string[1][];
                chartMetadata.Creator[0] = metadata.Creator ?? new string[] { "" };

                chartMetadata.Audio = new string[1] { metadata.Audio != null ? GetPath(filePath, metadata.Audio) : null };

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
                var tjaText = File.ReadAllText(filePath, Encoding.UTF8);
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

    /// <summary>
    /// 包含 Playable 和分數配置的容器
    /// </summary>
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
