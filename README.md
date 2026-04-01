# TJA Reader Plugin for Koioto

这是一个Koioto插件，允许直接读取 `.tja` 文件，而无需将其转换为 `.tcc/.tci` 格式。

## 功能

- ✅ 直接读取 `.tja` 文件
- ✅ 解析所有TJA元数据（标题、字幕、艺术家、BPM等）
- ✅ 支持多个难易度（Easy/Normal/Hard/Oni/Edit）
- ✅ 支持气球音符（Balloon）
- ✅ 支持所有主要命令（#bpm, #scroll, #gogobegin/end, #measure, #delay）
- ✅ 无需预处理或转换

## 构建

```bash
# 在Visual Studio中打开TJAReader.csproj
# 或使用命令行
cd D:\koioto\TJAReader
msbuild TJAReader.csproj /p:Configuration=Release
```

输出DLL将放在 `Plugins/` 目录中。

## 使用

1. 将编译后的 `TJAReader.dll` 放入 `Plugins/` 目录
2. 重启Koioto
3. 现在可以直接加载 `.tja` 文件

## 架构

### FileReader.cs
- 主插件入口点
- 实现 `IChartReadable` 接口
- 处理元数据读取和播放请求

### TJAParser.cs
- TJA格式解析器
- 将TJA语法转换为Koioto内部格式
- CourseParser：将TJA课程数据转换为Playable对象

## 支持的功能

### 元数据
- TITLE - 歌曲标题
- SUBTITLE - 副标题
- ARTIST - 艺术家
- CREATOR - 谱师
- BPM - 节拍每分钟
- WAVE - 音频文件路径
- OFFSET - 音频偏移（秒）
- DEMOSTART - 预览开始时间
- ALBUMART - 专辑艺术

### 命令
- `#BPMCHANGE` - 改变BPM
- `#MEASURE` / `#TSIGN` - 改变时间签名
- `#GOGOSTART` / `#GOGOEND` - Go-Go时间段
- `#SCROLL` - 改变滚动速度
- `#DELAY` - 延迟
- `#MEASURE` - 时间签名变更

### 音符
- 0 = 休止符
- 1 = 左鼓
- 2 = 右鼓
- 3 = 大左鼓
- 4 = 大右鼓
- 5 = 滚筒开始
- 6 = 大滚筒开始
- 7 = 气球开始
- 8 = 滚筒/气球结束

## 与OpenTaikoChart插件的区别

| 功能 | TJA Reader | OpenTaikoChart |
|------|-----------|-----------------|
| 输入格式 | `.tja` | `.tci` / `.tcc` |
| 转换需求 | 无 | 需要 |
| 解析复杂度 | 直接 | 通过JSON |
| 灵活性 | 高 | 标准化 |

## 技术细节

### 解析流程
1. 读取TJA文本文件（UTF-8）
2. 逐行解析，分离元数据和图表数据
3. 在 `#START` 处将元数据模式切换为图表模式
4. 收集课程数据（措施和命令）
5. 根据元数据和课程数据生成可播放对象

### 时间计算
- 基于BPM和时间签名计算措施持续时间
- 每个音符在措施中均匀分布
- 支持mid-measure BPM和measure changes

## 局限性

- 不支持多人/couple模式
- 不支持branch（分支）功能
- 简单的气球处理（固定目标数）

## 许可证

与Koioto相同
