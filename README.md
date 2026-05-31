<p align="center">
  <img src="docs/assets/app-icon.png" alt="Quicker Image Annotator icon" width="96" height="96">
</p>

# Quicker Image Annotator

Quicker Image Annotator 是一个轻量的 Windows 图片标注工具。它面向截图、剪贴板图片和本地图片的快速处理：打开图片、圈出重点、打马赛克、添加文字、保存结果，并把标注后的图片复制回剪贴板。

## 主要功能

- 从剪贴板图片或本地图片路径启动。
- 支持矩形、椭圆、箭头、画笔、马赛克和文字标注。
- 支持选择、移动、缩放、调整大小、撤销、清空、适合窗口和窗口置顶。
- 箭头可以通过首尾控制点继续调整方向和长度。
- 文字标注支持输入法、光标、选区、复制、剪切、粘贴和双击再编辑。
- 选中已有标注后可以继续修改颜色和线宽。
- 保存时生成带 `_标注` 后缀的新图片，并复制到剪贴板。
- 可设置固定输出目录，适合连续处理多张图片。

## 下载

最新版本可以在 [GitHub Releases](https://github.com/huahai0202/quicker-image-annotator/releases/latest) 下载。

发布资产包含：

- `AnnotatorApp.exe`：默认 AnyCPU 构建。
- `AnnotatorApp-x64.exe`：显式 x64 构建。

## 使用方式

复制一张图片到剪贴板后直接运行：

```powershell
.\AnnotatorApp.exe
```

也可以传入本地图片路径：

```powershell
.\AnnotatorApp.exe "C:\path\to\image.png"
```

指定输出目录：

```powershell
.\AnnotatorApp.exe --output-dir "D:\AnnotatedImages"
.\AnnotatorApp.exe "C:\path\to\image.png" --output-dir "D:\AnnotatedImages"
```

输出目录也可以通过工具栏设置按钮保存，或通过环境变量 `QUICKER_ANNOTATOR_OUTPUT_DIR` 指定。命令行参数优先级最高；未设置时，本地图片保存到原图片目录，剪贴板图片保存到系统临时目录。

## 快捷键和操作

- `Ctrl+S`：保存并关闭。
- `Ctrl+Z`：撤销上一步标注或变换。
- `Esc`：取消文字编辑；非文字编辑时关闭窗口。
- `Delete` / `Backspace`：删除选中的标注。
- 鼠标滚轮：缩放查看。
- 右键拖动：平移画布。
- 文字编辑时支持 `Ctrl+A/C/X/V`、方向键、`Home`、`End` 和 `Shift` 选区。

## 技术说明

项目不依赖 NuGet 或第三方 DLL，使用 Windows 原生能力实现：

- 窗口和消息循环：Win32 `HWND`。
- 交互渲染和离屏导出：Direct2D。
- 文字绘制、测量和 hit-test：DirectWrite。
- 图片解码和编码：WIC。
- 剪贴板读写：Win32 Clipboard。

支持 PNG、JPEG、BMP、GIF、TIFF 等常见输入格式；导出根据输出扩展名写入 PNG、JPEG 或 BMP，不适合原格式导出的输入会保存为 PNG。

## 构建和自检

项目使用 .NET Framework 的 `csc.exe` 编译，不需要 `.csproj` 文件。

构建并运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1
```

AnyCPU 自检：

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -SelfTest
```

x64 自检：

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -Platform x64 -SelfTest
```

性能基准：

```powershell
.\AnnotatorApp-x64.exe --render-benchmark --render-benchmark-log ".\docs\superpowers\specs\2026-05-30-render-benchmark.md"
```

渲染性能日志：

```powershell
.\AnnotatorApp-x64.exe --render-profile --render-profile-log ".\render-profile.log"
```

## 自检覆盖

自检会覆盖图片 I/O、GPU 导出、DirectWrite 文本测量和 hit-test、Direct2D 资源重建、窗口 chrome 渲染、主要交互语义、文字编辑、撤销、剪贴板 PNG/DIB 流程，以及基准 smoke test。

## 发布

发布或上传 GitHub 时需要同时更新两个可执行文件：

- `AnnotatorApp.exe`
- `AnnotatorApp-x64.exe`

更多设计和历史记录放在 [docs/superpowers/specs](docs/superpowers/specs)。
