using Koioto.Support;
using Koioto.Support.FileReader;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZhongTaiko.TJAReader
{
    public class TJAParser
    {
        private string _content;
        private TJAMetadata _metadata;
        private Dictionary<string, TJACourseData> _courses;
        private Dictionary<string, TJACourse> _courseInfo;

        public TJAParser(string tjaContent)
        {
            _content = tjaContent;
            _metadata = new TJAMetadata();
            _courses = new Dictionary<string, TJACourseData>();
            _courseInfo = new Dictionary<string, TJACourse>();
            Parse();
        }

        public TJAMetadata GetMetadata()
        {
            return _metadata;
        }

        public TJACourse[] GetCourses()
        {
            return _courseInfo.Values.ToArray();
        }

        public TJACourseData GetCourse(string difficulty)
        {
            var key = difficulty.ToLower();
            if (_courses.ContainsKey(key))
            {
                var course = _courses[key];
                System.Diagnostics.Debug.WriteLine($"[TJAParser.GetCourse] Loading '{difficulty}' -> key='{key}', Balloon array length={course?.Balloon?.Length ?? 0}");
                if (course?.Balloon != null && course.Balloon.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"  Balloon values: {string.Join(",", course.Balloon.Select(b => b?.ToString() ?? "null"))}");
                }
                return course;
            }
            System.Diagnostics.Debug.WriteLine($"[TJAParser.GetCourse] '{difficulty}' not found. Available courses: {string.Join(", ", _courses.Keys)}");
            return null;
        }

        private void Parse()
        {
            var lines = _content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var inChart = false;
            var currentCourse = "";
            var currentMeasure = new List<string>();
            var currentCourseData = new TJACourseData { Measures = new List<string[]>() as List<string[]> };

            foreach (var rawLine in lines)
            {
                // Strip comments
                var line = rawLine.Contains("//") ? rawLine.Substring(0, rawLine.IndexOf("//")) : rawLine;
                line = line.Trim();

                if (string.IsNullOrEmpty(line))
                    continue;

                // Pre-chart metadata
                if (!inChart)
                {
                    if (line.Equals("#START", StringComparison.InvariantCultureIgnoreCase))
                    {
                        inChart = true;
                        if (string.IsNullOrEmpty(currentCourse))
                        {
                            currentCourse = "oni";
                            _courseInfo[currentCourse] = new TJACourse { Difficulty = currentCourse, Level = 1 };
                        }

                        // Preserve BALLOON, SCOREINIT, SCOREDIFF from pre-#START metadata
                        var preservedBalloon = currentCourseData?.Balloon;
                        var preservedScoreinit = currentCourseData?.Scoreinit;
                        var preservedScorediff = currentCourseData?.Scorediff;

                        currentCourseData = new TJACourseData { Measures = new List<string[]>() };

                        // Restore preserved metadata
                        if (preservedBalloon != null)
                            currentCourseData.Balloon = preservedBalloon;
                        if (preservedScoreinit > 0)
                            currentCourseData.Scoreinit = preservedScoreinit;
                        if (preservedScorediff > 0)
                            currentCourseData.Scorediff = preservedScorediff;

                        currentMeasure = new List<string>();
                        continue;
                    }

                    // Parse metadata
                    if (line.Contains(":"))
                    {
                        var parts = line.Split(new[] { ':' }, 2);
                        var key = parts[0].Trim().ToUpper();
                        var value = parts.Length > 1 ? parts[1].Trim() : "";

                        switch (key)
                        {
                            case "TITLE":
                                _metadata.Title = value;
                                break;
                            case "SUBTITLE":
                                _metadata.Subtitle = value;
                                break;
                            case "ARTIST":
                                _metadata.Artist = new[] { value };
                                break;
                            case "CREATOR":
                                _metadata.Creator = new[] { value };
                                break;
                            case "BPM":
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm))
                                    _metadata.BPM = bpm;
                                break;
                            case "WAVE":
                                _metadata.Audio = value;
                                break;
                            case "OFFSET":
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset))
                                    _metadata.Offset = offset;
                                break;
                            case "DEMOSTART":
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var demo))
                                    _metadata.SongPreview = demo;
                                break;
                            case "ALBUMART":
                                _metadata.Albumart = value;
                                break;
                            case "BGIMAGE":
                                _metadata.Albumart = value;
                                break;
                            case "BGMOVIE":
                                _metadata.Background = value;
                                break;
                            case "MOVIEOFFSET":
                                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var movieOffset))
                                    _metadata.MovieOffset = movieOffset;
                                break;
                            case "SCOREMODE":
                                if (int.TryParse(value, out var scoreMode))
                                    _metadata.ScoreMode = scoreMode;
                                break;
                            case "SCOREINIT":
                                if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value, out var scoreInit) && currentCourseData != null)
                                    currentCourseData.Scoreinit = scoreInit;
                                break;
                            case "SCOREDIFF":
                                if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value, out var scoreDiff) && currentCourseData != null)
                                    currentCourseData.Scorediff = scoreDiff;
                                break;
                            case "COURSE":
                                currentCourse = NormalizeCourse(value);
                                if (!_courseInfo.ContainsKey(currentCourse))
                                {
                                    _courseInfo[currentCourse] = new TJACourse { Difficulty = currentCourse };
                                }
                                break;
                            case "LEVEL":
                                if (int.TryParse(value, out var level) && _courseInfo.ContainsKey(currentCourse))
                                {
                                    _courseInfo[currentCourse].Level = level;
                                }
                                break;
                            case "BALLOON":
                                try
                                {
                                    var balloonStrs = value.Split(',');
                                    var balloons = balloonStrs
                                        .Select(x => x.Trim())
                                        .Where(x => !string.IsNullOrEmpty(x))
                                        .Select(x => int.TryParse(x, out var b) ? (int?)b : null)
                                        .ToArray();
                                    if (currentCourseData != null)
                                    {
                                        currentCourseData.Balloon = balloons;
                                    }
                                }
                                catch { }
                                break;
                        }
                    }
                }
                else
                {
                    // In chart
                    if (line.Equals("#END", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (currentMeasure.Count > 0)
                        {
                            currentCourseData.Measures.Add(currentMeasure.ToArray());
                            currentMeasure.Clear();
                        }

                        if (!string.IsNullOrEmpty(currentCourse))
                        {
                            _courses[currentCourse] = currentCourseData;
                        }

                        inChart = false;
                        currentCourseData = new TJACourseData { Measures = new List<string[]>() };
                        currentCourse = "";
                        continue;
                    }

                    // Mid-chart # commands: keep as single line in measure (don't split by comma)
                    if (line.StartsWith("#") && !line.Equals("#END", StringComparison.InvariantCultureIgnoreCase))
                    {
                        currentMeasure.Add(line);
                        continue;
                    }

                    // Handle measures (comma-separated)
                    if (line.Contains(","))
                    {
                        var parts = line.Split(',');
                        for (int i = 0; i < parts.Length; i++)
                        {
                            var part = parts[i].Trim();
                            // Add the part (even if empty)
                            currentMeasure.Add(part);

                            // Comma signals end of measure
                            if (i < parts.Length - 1)
                            {
                                currentCourseData.Measures.Add(currentMeasure.ToArray());
                                currentMeasure.Clear();
                            }
                        }
                    }
                    else
                    {
                        currentMeasure.Add(line);
                    }
                }
            }
        }

        private string NormalizeCourse(string value)
        {
            switch (value.ToLower())
            {
                case "easy":
                case "0": return "easy";
                case "normal":
                case "1": return "normal";
                case "hard":
                case "2": return "hard";
                case "oni":
                case "3": return "oni";
                case "edit":
                case "ura":
                case "4": return "edit";
                default: return value.ToLower();
            }
        }

        /// <summary>
        /// Calculates points for a note based on TJA scoring mode.
        /// </summary>
        public static class ScoringCalculator
        {
            /// <summary>
            /// Calculates score for a single note based on combo, scoring mode, and difficulty.
            /// </summary>
            /// <param name="combo">Current combo count (before this note)</param>
            /// <param name="scoreMode">Scoring mode (0=AC1-7, 1=AC8-14, 2=AC0)</param>
            /// <param name="scoreInit">Base points (INIT)</param>
            /// <param name="scoreDiff">Difficulty multiplier (DIFF)</param>
            /// <returns>Points for this note, before division by 10</returns>
            public static int CalculatePoints(int combo, int scoreMode, int scoreInit, int scoreDiff)
            {
                switch (scoreMode)
                {
                    case 0:
                        // AC 1-7 generation: Less than 200 combo = 1000 pts/note, 200+ = 2000 pts/note
                        return combo < 200 ? 1000 : 2000;

                    case 1:
                        // AC 8-14 generation: INIT + max(0, DIFF * floor((min(COMBO, 100) - 1) / 10))
                        var multiplier = Math.Max(0, scoreDiff * (Math.Min(combo, 100) - 1) / 10);
                        return scoreInit + multiplier;

                    case 2:
                        // AC 0 generation: INIT + DIFF * {100<=COMBO: 8, 50<=COMBO: 4, 30<=COMBO: 2, 10<=COMBO: 1, 0}
                        int diffMult = 0;
                        if (combo >= 100) diffMult = 8;
                        else if (combo >= 50) diffMult = 4;
                        else if (combo >= 30) diffMult = 2;
                        else if (combo >= 10) diffMult = 1;
                        return scoreInit + scoreDiff * diffMult;

                    default:
                        // Default to mode 1
                        return scoreInit + Math.Max(0, scoreDiff * (Math.Min(combo, 100) - 1) / 10);
                }
            }

            /// <summary>
            /// Applies TJA score division rule: divide by 10, round towards negative infinity, multiply by 10.
            /// </summary>
            public static int ApplyScoreDivision(int score)
            {
                return (int)Math.Floor(score / 10.0) * 10;
            }
        }

        public static class CourseParser
        {
            public static Playable Parse(TJAMetadata metadata, TJACourseData courseData)
            {
                var playable = new Playable
                {
                    Sections = new List<Chip>[1]
                };
                var sections = playable.Sections;
                sections[0] = new List<Chip>();
                var balloonIndex = 0;

                // Debug: log balloon array
                if (courseData?.Balloon != null && courseData.Balloon.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[TJAParser] Loaded Balloon array ({courseData.Balloon.Length} values): {string.Join(",", courseData.Balloon.Select(b => b?.ToString() ?? "null"))}");
                }

                var list = sections[0];

                // Scoring configuration
                var scoreMode = metadata.ScoreMode ?? 1;
                var scoreInit = courseData.Scoreinit ?? 1000;
                var scoreDiff = courseData.Scorediff ?? 100;

                var nowTime = 0L;
                var nowBPM = metadata.BPM ?? 120.0;
                var nowMeasure = new Measure(4, 4);
                var nowScroll = 1.0;
                var isGoGoTime = false;
                var measureCount = 0;
                var isFirstNoteInMeasure = true;
                var barVisible = true;
                Chip rollstartChip = null;

                // Branch state machine (譜面分岐)
                var inBranch = false;
                string currentBranchPath = null; // "n", "e", or "m"
                const string selectedBranch = "m"; // Master branch (hardest path, as engine does not support dynamic branching)

                var offset = metadata.Offset ?? 0;
                if (offset < 0)
                {
                    var bgmStartChip = new Chip
                    {
                        ChipType = Chips.BGMStart,
                        Time = nowTime - (long)(Math.Abs(offset) * 1000.0 * 1000.0),
                        BPM = nowBPM
                    };
                    list.Add(bgmStartChip);
                }
                else
                {
                    var bgmStartChip = new Chip
                    {
                        ChipType = Chips.BGMStart,
                        Time = nowTime,
                        BPM = nowBPM
                    };
                    list.Add(bgmStartChip);
                    nowTime += (long)(offset * 1000.0 * 1000.0);
                }

                foreach (var measure in courseData.Measures)
                {
                    var notesCount = 0;
                    var notesElementCount = 0;

                    // Pre-pass: count notes only from active branch, detect fully inactive measures
                    // Also capture effective #MEASURE and #BPMCHANGE that precede notes in this measure
                    var prepassInBranch = inBranch;
                    var prepassCurrentPath = currentBranchPath;
                    var measureFullyInactive = false; // true if this measure is entirely within an inactive branch
                    var hasAnyActiveContent = false;
                    var effectiveMeasure = nowMeasure;
                    var effectiveBPM = nowBPM;
                    var prepassSeenNote = false;
                    foreach (var line in measure)
                    {
                        if (line.StartsWith("#"))
                        {
                            var cmd = line.ToLower().Trim();
                            if (cmd.StartsWith("#branchstart"))
                            {
                                prepassInBranch = true;
                                prepassCurrentPath = null;
                            }
                            else if (cmd == "#branchend")
                            {
                                prepassInBranch = false;
                                prepassCurrentPath = null;
                                // Don't set hasAnyActiveContent - branch exit is metadata, not renderable content
                            }
                            else if (cmd == "#n") { prepassCurrentPath = "n"; if (prepassCurrentPath == selectedBranch) hasAnyActiveContent = true; }
                            else if (cmd == "#e") { prepassCurrentPath = "e"; if (prepassCurrentPath == selectedBranch) hasAnyActiveContent = true; }
                            else if (cmd == "#m") { prepassCurrentPath = "m"; if (prepassCurrentPath == selectedBranch) hasAnyActiveContent = true; }
                            else if (cmd == "#section")
                            {
                                // #SECTION marks end of current branch section (implicit #BRANCHEND)
                                prepassInBranch = false;
                                prepassCurrentPath = null;
                            }
                            else if (cmd.StartsWith("#levelhold"))
                            {
                                // Branch metadata - don't count as active content
                            }
                            else if (!prepassInBranch || prepassCurrentPath == selectedBranch)
                            {
                                hasAnyActiveContent = true;
                                // Capture MEASURE/BPM changes that precede the first note so measureDuration is correct
                                if (!prepassSeenNote)
                                {
                                    var p2 = cmd.Contains(' ') ? cmd.Substring(cmd.IndexOf(' ')).Trim() : "";
                                    if (cmd.StartsWith("#measure") || cmd.StartsWith("#tsign"))
                                    {
                                        var parts2 = p2.Split('/');
                                        if (parts2.Length == 2 &&
                                            double.TryParse(parts2[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num2) &&
                                            double.TryParse(parts2[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var den2))
                                            effectiveMeasure = new Measure(num2, den2);
                                    }
                                    else if (cmd.StartsWith("#bpm"))
                                    {
                                        if (double.TryParse(p2, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bpm2))
                                            effectiveBPM = bpm2;
                                    }
                                }
                            }
                            continue;
                        }

                        // Skip notes from inactive branches
                        if (prepassInBranch && prepassCurrentPath != selectedBranch)
                            continue;

                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            prepassSeenNote = true;
                            hasAnyActiveContent = true;
                            notesElementCount++;
                            foreach (var digit in line)
                            {
                                if (char.IsDigit(digit))
                                {
                                    notesCount++;
                                }
                            }
                        }
                    }

                    // If this measure has no active content AND involves branching structure, skip it.
                    // Covers both inactive-branch measures (inBranch=true) and branch-start transition
                    // measures where inBranch is still false but prepassInBranch became true.
                    if (!hasAnyActiveContent && (inBranch || prepassInBranch))
                    {
                        measureFullyInactive = true;
                    }

                    // If measure is fully in an inactive branch, only update branch state and balloon index, then skip
                    if (measureFullyInactive)
                    {
                        foreach (var line in measure)
                        {
                            if (line.StartsWith("#"))
                            {
                                var cmd = line.ToLower().Trim();
                                if (cmd.StartsWith("#branchstart")) { inBranch = true; currentBranchPath = null; }
                                else if (cmd == "#branchend" || cmd == "#section") { inBranch = false; currentBranchPath = null; }
                                else if (cmd == "#n") currentBranchPath = "n";
                                else if (cmd == "#e") currentBranchPath = "e";
                                else if (cmd == "#m") currentBranchPath = "m";
                            }
                            else
                            {
                                // Advance balloon index for balloon notes in skipped branches
                                foreach (var digit in line)
                                {
                                    if (ConsumesBalloonEntry(digit))
                                    {
                                        balloonIndex++;
                                    }
                                }
                            }
                        }
                        // Don't create measure chip, don't advance time, don't increment measureCount
                        continue;
                    }

                    // Capture original values before normalization
                    // Use effectiveMeasure/effectiveBPM which reflect any #MEASURE/#BPMCHANGE
                    // commands that appear before the first note in this measure
                    var originalNotesCount = notesCount;
                    var originalMeasureDuration = GetMeasureDuration(effectiveMeasure, effectiveBPM);
                    var originalBPM = effectiveBPM;
                    var originalMeasureRate = effectiveMeasure.GetRate();

                    if (notesCount == 0)
                        notesCount = 1;

                    var measureDuration = originalMeasureDuration;
                    // Prevent division by zero: ensure measure duration is at least 1 microsecond
                    if (measureDuration <= 0)
                        measureDuration = 1;
                    var timePerNotes = (long)(measureDuration / notesCount);

                    foreach (var line in measure)
                    {
                        if (!line.StartsWith("#"))
                        {
                            // Note line: check if we're in an inactive branch
                            if (inBranch && currentBranchPath != selectedBranch)
                            {
                                // Inactive branch: only advance balloonIndex for balloon notes
                                foreach (var digit in line)
                                {
                                    if (ConsumesBalloonEntry(digit))
                                    {
                                        balloonIndex++;
                                    }
                                }
                                continue;
                            }

                            // Active path (or not in a branch): process normally
                            if (isFirstNoteInMeasure)
                            {
                                var measureChip = new Chip
                                {
                                    ChipType = Chips.Measure,
                                    CanShow = barVisible,
                                    Scroll = nowScroll,
                                    BPM = nowBPM,
                                    IsGoGoTime = isGoGoTime,
                                    Measure = nowMeasure,
                                    MeasureCount = measureCount,
                                    Time = nowTime
                                };
                                list.Add(measureChip);
                                isFirstNoteInMeasure = false;
                            }

                            foreach (var digit in line)
                            {
                                if (char.IsDigit(digit))
                                {
                                    var note = GetNotesFromChar(digit);

                                    var noteChip = new Chip
                                    {
                                        ChipType = Chips.Note,
                                        NoteType = note,
                                        Scroll = nowScroll,
                                        BPM = nowBPM,
                                        CanShow = true,
                                        IsGoGoTime = isGoGoTime,
                                        Measure = nowMeasure,
                                        MeasureCount = measureCount,
                                        Time = nowTime
                                    };

                                    if (note == Notes.RollStart || note == Notes.ROLLStart || note == Notes.Balloon)
                                    {
                                        rollstartChip = noteChip;

                                        if (note == Notes.Balloon && courseData.Balloon != null)
                                        {
                                            if (courseData.Balloon.Length > balloonIndex)
                                            {
                                                var balloonHits = courseData.Balloon[balloonIndex] ?? 5;
                                                // Ensure RollObjective is at least 1 to prevent DivideByZeroException in Koioto
                                                noteChip.RollObjective = balloonHits > 0 ? balloonHits : 1;
                                                System.Diagnostics.Debug.WriteLine($"[Balloon] M{measureCount} idx={balloonIndex} value={balloonHits}");
                                                balloonIndex++;
                                            }
                                            else
                                            {
                                                noteChip.RollObjective = 5;
                                                System.Diagnostics.Debug.WriteLine($"[Balloon] M{measureCount} OOB idx={balloonIndex}/{courseData.Balloon.Length}");
                                            }
                                        }
                                    }
                                    else if (note == Notes.RollEnd && rollstartChip != null)
                                    {
                                        rollstartChip.RollEnd = noteChip;
                                        rollstartChip = null;
                                    }

                                    list.Add(noteChip);
                                    nowTime += timePerNotes;
                                }
                            }
                        }
                        else
                        {
                            // Command line
                            var command = line.ToLower();
                            var param = command.IndexOf(' ') >= 0 ?
                                command.Substring(command.IndexOf(' ')).Trim() : "";

                            // --- Branch commands (譜面分岐) ---
                            if (command.StartsWith("#branchstart"))
                            {
                                inBranch = true;
                                currentBranchPath = null;
                                continue;
                            }
                            else if (command == "#branchend")
                            {
                                inBranch = false;
                                currentBranchPath = null;
                                continue;
                            }
                            else if (command == "#n")
                            {
                                currentBranchPath = "n";
                                continue;
                            }
                            else if (command == "#e")
                            {
                                currentBranchPath = "e";
                                continue;
                            }
                            else if (command == "#m")
                            {
                                currentBranchPath = "m";
                                continue;
                            }
                            else if (command == "#section")
                            {
                                // #SECTION marks end of current branch section (implicit #BRANCHEND)
                                inBranch = false;
                                currentBranchPath = null;
                                continue;
                            }
                            else if (command.StartsWith("#levelhold"))
                            {
                                continue;
                            }

                            // Skip commands from inactive branches (don't mutate state)
                            if (inBranch && currentBranchPath != selectedBranch)
                                continue;

                            // --- Normal commands (active path or non-branching) ---
                            var eventChip = new Chip();

                            if (command.StartsWith("#bpm"))
                            {
                                // Matches #BPMCHANGE and #BPM
                                if (double.TryParse(param, NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm))
                                {
                                    eventChip.ChipType = Chips.BPMChange;
                                    nowBPM = bpm;
                                }
                            }
                            else if (command.StartsWith("#scroll"))
                            {
                                if (double.TryParse(param, NumberStyles.Float, CultureInfo.InvariantCulture, out var scroll))
                                {
                                    if (scroll != 0)
                                    {
                                        eventChip.ChipType = Chips.ScrollChange;
                                        nowScroll = scroll;
                                    }
                                }
                            }
                            else if (command.StartsWith("#gogostart") || command.StartsWith("#gogobegin"))
                            {
                                eventChip.ChipType = Chips.GoGoStart;
                                isGoGoTime = true;
                            }
                            else if (command.StartsWith("#gogoend"))
                            {
                                eventChip.ChipType = Chips.GoGoEnd;
                                isGoGoTime = false;
                            }
                            else if (command.StartsWith("#delay"))
                            {
                                if (double.TryParse(param, NumberStyles.Float, CultureInfo.InvariantCulture, out var delay))
                                {
                                    nowTime += (long)(delay * 1000.0 * 1000.0);
                                }
                            }
                            else if (command.StartsWith("#measure") || command.StartsWith("#tsign"))
                            {
                                var parts = param.Split('/');
                                if (parts.Length == 2 &&
                                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var part) &&
                                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var beat))
                                {
                                    eventChip.ChipType = Chips.MeasureChange;
                                    nowMeasure = new Measure(part, beat);
                                }
                            }
                            else if (command.StartsWith("#barlineoff") || command == "#bar hide")
                            {
                                barVisible = false;
                                continue; // No chip needed, affects next Measure chip
                            }
                            else if (command.StartsWith("#barlineon") || command == "#bar show")
                            {
                                barVisible = true;
                                continue;
                            }
                            else
                            {
                                // Other unhandled commands
                                continue;
                            }

                            eventChip.Scroll = nowScroll;
                            eventChip.BPM = nowBPM;
                            eventChip.IsGoGoTime = isGoGoTime;
                            eventChip.Measure = nowMeasure;
                            eventChip.MeasureCount = measureCount;
                            eventChip.Time = nowTime;
                            list.Add(eventChip);
                        }
                    }

                    if (notesElementCount <= 0)
                    {
                        var measureChip = new Chip
                        {
                            ChipType = Chips.Measure,
                            CanShow = barVisible,
                            Scroll = nowScroll,
                            BPM = nowBPM,
                            IsGoGoTime = isGoGoTime,
                            Measure = nowMeasure,
                            MeasureCount = measureCount,
                            Time = nowTime
                        };
                        list.Add(measureChip);
                        nowTime += (long)GetMeasureDuration(effectiveMeasure, effectiveBPM);
                    }

                    measureCount++;
                    isFirstNoteInMeasure = true;
                }

                // Add offset
                {
                    var offsetTime = 3L * 1000 * 1000;
                    list.ForEach(c => c.Time += offsetTime);

                    var last = list.Last();
                    var lastChip = new Chip
                    {
                        BPM = last.BPM,
                        Scroll = last.Scroll,
                        CanShow = false,
                        ChipType = Chips.Measure,
                        Measure = nowMeasure,
                        MeasureCount = measureCount,
                        Time = last.Time + offsetTime
                    };
                    list.Add(lastChip);
                }

                list.Sort();

                return playable;
            }

            private static double GetMeasureDuration(Measure measure, double bpm)
            {
                // Prevent division by zero
                if (bpm <= 0) bpm = 120.0;
                var measureRate = measure.GetRate();
                if (measureRate <= 0) measureRate = 1;
                return measureRate / bpm * 1000 * 1000.0;
            }

            private static Notes GetNotesFromChar(char ch)
            {
                switch (ch)
                {
                    case '0': return Notes.Space;
                    case '1': return Notes.Don;
                    case '2': return Notes.Ka;
                    case '3': return Notes.DON;
                    case '4': return Notes.KA;
                    case '5': return Notes.RollStart;
                    case '6': return Notes.ROLLStart;
                    case '7': return Notes.Balloon;
                    case '9': return Notes.Balloon;
                    case '8': return Notes.RollEnd;
                    default: return Notes.Space;
                }
            }

            private static bool ConsumesBalloonEntry(char ch)
            {
                return ch == '7' || ch == '9';
            }
        }
    }
}
