# KoiotoTJAReader

一個 Koioto 節奏遊戲的直接 TJA 格式讀取器插件，無需轉換為 TCC/TCI 格式。

**語言:** [English](README.md) | [繁體中文](#koiototjareader) | [日本語](README.ja.md)

## 功能

✅ **直接讀取 .tja 檔案** - 無需轉換  
✅ **完整的元數據支持** - 標題、藝術家、BPM、偏移、預覽時間  
✅ **多個難度等級** - Easy、Normal、Hard、Oni、Edit（Ura）  
✅ **分數系統支持**
  - SCOREMODE: 0（AC 1-7）、1（AC 8-14）、2（AC 0）
  - 每個難度的 SCOREINIT 和 SCOREDIFF
  - 完整的分數計算實現

✅ **完整的圖表命令支持**
  - #BPMCHANGE - BPM 中途改變
  - #SCROLL - 滾動速度調整
  - #MEASURE - 時間簽名改變
  - #GOGOSTART / #GOGOEND - Go-Go 時間段
  - #DELAY - 圖表延遲

✅ **氣球/庫蘇達瑪支持** - 可配置的擊中目標  
✅ **空白小節處理** - 保留所有小節類型  

## 安裝

### 快速開始

1. **下載 DLL**
   - 從 [Releases](../../releases) 頁面獲取 `TJAReader.dll`

2. **安裝到 Koioto**
   ```bash
   複製 TJAReader.dll 到 C:\path\to\Koioto\Plugins\
   ```

3. **重啟 Koioto**
   - Koioto 將自動加載插件
   - .tja 檔案現在將出現在歌曲選擇中

### 從源代碼構建

```bash
# 克隆倉庫
git clone https://github.com/GanYiZhong/KoiotoTJAReader.git
cd KoiotoTJAReader/TJAReader

# 構建項目
dotnet build TJAReader.csproj -c Release
```

## 使用方法

### 在 Koioto 中

安裝後，.tja 檔案將直接加載，就像其他圖表格式一樣。

### 編程訪問

```csharp
using ZhongTaiko.TJAReader;

// 獲取分數配置
var fileReader = new FileReader();
var scoring = fileReader.GetScoringConfig("path/to/song.tja", Koioto.Support.FileReader.Courses.Oni);

// 根據 combo 計算分數
int points = scoring.CalculatePoints(50);
int finalScore = ScoringConfig.ApplyDivision(points);
```

## 文檔

- [SCORING_IMPORT_GUIDE.md](../SCORING_IMPORT_GUIDE.md) - 分數系統集成指南
- [SCORING_QUICK_START.md](../SCORING_QUICK_START.md) - 快速開始示例

## TJA 格式支持

| 標頭 | 支持 | 備註 |
|------|------|------|
| TITLE | ✅ | 歌曲標題 |
| ARTIST | ✅ | 藝術家名稱 |
| CREATOR | ✅ | 圖表創建者 |
| BPM | ✅ | 每分鐘節拍數 |
| WAVE | ✅ | 音頻檔案路徑 |
| OFFSET | ✅ | 偏移秒數 |
| SCOREMODE | ✅ | 分數計算模式 |
| SCOREINIT | ✅ | 基礎分數 |
| SCOREDIFF | ✅ | 難度倍增器 |
| BALLOON | ✅ | 氣球擊中次數 |

## 分數模式

### 模式 0: AC 1-7 世代
- Combo < 200: 1000 分
- Combo ≥ 200: 2000 分

### 模式 1: AC 8-14 世代（默認）
```
分數 = INIT + DIFF × ⌊(min(combo, 100) - 1) / 10⌋
```

### 模式 2: AC 0 世代
```
分數 = INIT + DIFF × 倍增器
（倍增器: 100→8, 50→4, 30→2, 10→1, 0→0）
```

## 系統需求

- **.NET Framework 4.7.2+** 或 **.NET 6.0+**
- **Koioto**（支持 IChartReadable 插件）

## 許可證

MIT 許可證 - 詳見 LICENSE 檔案

## 作者

**ZhongTaiko**

---

了解更多信息，請查看其他語言的文檔：
- [English](README.md)
- [日本語](README.ja.md)

