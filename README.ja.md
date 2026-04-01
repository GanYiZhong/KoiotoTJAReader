# KoiotoTJAReader

Koioto リズムゲーム用の直接 TJA 形式リーダープラグイン。TCC/TCI 形式への変換が不要です。

**言語:** [English](README.md) | [繁體中文](README.zh-TW.md) | [日本語](#koiototjareader)

## 機能

✅ **直接的な .tja ファイル読み込み** - 変換不要  
✅ **完全なメタデータサポート** - タイトル、アーティスト、BPM、オフセット、プレビュー時間  
✅ **複数の難易度レベル** - Easy、Normal、Hard、Oni、Edit（Ura）  
✅ **スコアシステムサポート**
  - SCOREMODE: 0（AC 1-7）、1（AC 8-14）、2（AC 0）
  - 難易度ごとの SCOREINIT と SCOREDIFF
  - 完全なスコア計算実装

✅ **完全なチャートコマンドサポート**
  - #BPMCHANGE - BPM の途中変更
  - #SCROLL - スクロール速度調整
  - #MEASURE - 拍子記号の変更
  - #GOGOSTART / #GOGOEND - ゴーゴータイムセクション
  - #DELAY - チャート遅延

✅ **風船/クスダマサポート** - 設定可能なヒット目標  
✅ **空白小節処理** - すべての小節タイプを保持  

## インストール

### クイックスタート

1. **DLL をダウンロード**
   - [Releases](../../releases) ページから `TJAReader.dll` を取得

2. **Koioto にインストール**
   ```bash
   TJAReader.dll を C:\path\to\Koioto\Plugins\ にコピー
   ```

3. **Koioto を再起動**
   - Koioto が自動的にプラグインをロード
   - .tja ファイルが曲選択に表示される

### ソースからビルド

```bash
# リポジトリをクローン
git clone https://github.com/GanYiZhong/KoiotoTJAReader.git
cd KoiotoTJAReader/TJAReader

# プロジェクトをビルド
dotnet build TJAReader.csproj -c Release
```

## 使用方法

### Koioto 内での使用

インストール後、.tja ファイルは他のチャート形式と同じように直接ロードされます。

### プログラム的アクセス

```csharp
using ZhongTaiko.TJAReader;

// スコア設定を取得
var fileReader = new FileReader();
var scoring = fileReader.GetScoringConfig("path/to/song.tja", Koioto.Support.FileReader.Courses.Oni);

// コンボに基づいてポイントを計算
int points = scoring.CalculatePoints(50);
int finalScore = ScoringConfig.ApplyDivision(points);
```

## ドキュメント

- [SCORING_IMPORT_GUIDE.md](../SCORING_IMPORT_GUIDE.md) - スコアシステム統合ガイド
- [SCORING_QUICK_START.md](../SCORING_QUICK_START.md) - クイックスタート例

## TJA フォーマットサポート

| ヘッダー | サポート | 備考 |
|---------|---------|------|
| TITLE | ✅ | 曲のタイトル |
| ARTIST | ✅ | アーティスト名 |
| CREATOR | ✅ | チャート作成者 |
| BPM | ✅ | ビート毎分 |
| WAVE | ✅ | オーディオファイルパス |
| OFFSET | ✅ | オフセット（秒） |
| SCOREMODE | ✅ | スコア計算モード |
| SCOREINIT | ✅ | 基礎ポイント |
| SCOREDIFF | ✅ | 難度倍数 |
| BALLOON | ✅ | 風船ヒット数 |

## スコアモード

### モード 0: AC 1-7 世代
- コンボ < 200: 1000 ポイント
- コンボ ≥ 200: 2000 ポイント

### モード 1: AC 8-14 世代（デフォルト）
```
スコア = INIT + DIFF × ⌊(min(コンボ, 100) - 1) / 10⌋
```

### モード 2: AC 0 世代
```
スコア = INIT + DIFF × 倍数
（倍数: 100→8, 50→4, 30→2, 10→1, 0→0）
```

## システム要件

- **.NET Framework 4.7.2+** または **.NET 6.0+**
- **Koioto**（IChartReadable プラグインサポート対応）

## ライセンス

MIT ライセンス - LICENSE ファイルを参照

## 作者

**ZhongTaiko**

---

その他の言語のドキュメントについては、以下を参照してください：
- [English](README.md)
- [繁體中文](README.zh-TW.md)

