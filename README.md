# KoiotoTJAReader

A direct TJA format reader plugin for Koioto rhythm game, eliminating the need for conversion to TCC/TCI format.

**Languages:** [English](#koiototjareader) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md)

## Features

✅ **Direct .tja File Reading** - No conversion required  
✅ **Complete Metadata Support** - Title, artist, BPM, offset, preview time  
✅ **Multiple Difficulty Levels** - Easy, Normal, Hard, Oni, Edit (Ura)  
✅ **Scoring System Support**
  - SCOREMODE: 0 (AC 1-7), 1 (AC 8-14), 2 (AC 0)
  - SCOREINIT and SCOREDIFF per difficulty
  - Complete score calculation implementation

✅ **Full Chart Command Support**
  - #BPMCHANGE - BPM changes mid-chart
  - #SCROLL - Scroll speed adjustments
  - #MEASURE - Time signature changes
  - #GOGOSTART / #GOGOEND - Go-Go time sections
  - #DELAY - Chart delays

✅ **Balloon/Kusudama Support** - With configurable hit targets  
✅ **Empty Measure Handling** - Preserves all measure types  

## Installation

### Quick Start

1. **Download the DLL**
   - Get `TJAReader.dll` from the [Releases](../../releases) page

2. **Install to Koioto**
   ```bash
   Copy TJAReader.dll to C:\path\to\Koioto\Plugins\
   ```

3. **Restart Koioto**
   - Koioto will automatically load the plugin
   - .tja files will now appear in song selection

### Build from Source

```bash
# Clone the repository
git clone https://github.com/GanYiZhong/KoiotoTJAReader.git
cd KoiotoTJAReader/TJAReader

# Build the project
dotnet build TJAReader.csproj -c Release
```

## Usage

### In Koioto

Once installed, .tja files will load directly like any other chart format.

### Programmatic Access

```csharp
using ZhongTaiko.TJAReader;

// Get scoring configuration
var fileReader = new FileReader();
var scoring = fileReader.GetScoringConfig("path/to/song.tja", Koioto.Support.FileReader.Courses.Oni);

// Calculate points based on combo
int points = scoring.CalculatePoints(50);
int finalScore = ScoringConfig.ApplyDivision(points);
```

## Documentation

- [SCORING_IMPORT_GUIDE.md](../SCORING_IMPORT_GUIDE.md) - Scoring system integration guide
- [SCORING_QUICK_START.md](../SCORING_QUICK_START.md) - Quick start with examples

## TJA Format Support

| Header | Support | Notes |
|--------|---------|-------|
| TITLE | ✅ | Song title |
| ARTIST | ✅ | Artist name |
| CREATOR | ✅ | Chart creator |
| BPM | ✅ | Beats per minute |
| WAVE | ✅ | Audio file path |
| OFFSET | ✅ | Offset in seconds |
| SCOREMODE | ✅ | Scoring calculation mode |
| SCOREINIT | ✅ | Base points |
| SCOREDIFF | ✅ | Difficulty multiplier |
| BALLOON | ✅ | Balloon hit counts |

## Scoring Modes

### Mode 0: AC 1-7 Generation
- Combo < 200: 1000 points
- Combo ≥ 200: 2000 points

### Mode 1: AC 8-14 Generation (Default)
```
points = INIT + DIFF × ⌊(min(combo, 100) - 1) / 10⌋
```

### Mode 2: AC 0 Generation
```
points = INIT + DIFF × multiplier
(multiplier: 100→8, 50→4, 30→2, 10→1, 0→0)
```

## System Requirements

- **.NET Framework 4.7.2+** or **.NET 6.0+**
- **Koioto** (with IChartReadable plugin support)

## License

MIT License - See LICENSE file for details

## Author

**ZhongTaiko**

---

For more information, see the documentation in other languages:
- [繁體中文](README.zh-TW.md)
- [日本語](README.ja.md)

