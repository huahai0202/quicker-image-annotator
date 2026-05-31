<p align="center">
  <img src="docs/assets/app-icon.png" alt="Quicker Image Annotator icon" width="96" height="96">
</p>

# Quicker Image Annotator

一个轻量的 Windows 图片标注工具，适合从剪贴板或本地图片快速打开、圈画重点、打马赛克、添加文字，然后保存并复制结果。

当前主干是 GPU-only 版本：应用自有绘制、文字测量、图片解码、导出和剪贴板图片处理已经迁到 Win32 + Direct2D + DirectWrite + WIC，不再依赖 WinForms / GDI+ / `System.Drawing`。

## 功能

- 支持矩形、椭圆、箭头、画笔、马赛克和文字标注。
- 支持选择、移动、调整大小、撤销、清空、适合窗口、窗口置顶和缩放查看。
- 箭头选中后使用首尾两个控制点调整方向和长度。
- 文字标注支持输入法 composition、光标、选区、复制/剪切/粘贴；双击已完成文字可以继续编辑。
- 选中已完成标注后可以继续修改颜色和线宽。
- 可以直接读取剪贴板图片，也可以通过命令行传入图片路径。
- 保存时会生成带 `_标注` 后缀的新图片，并把结果复制回剪贴板。

## GPU-only 架构

- 窗口与消息循环：原生 Win32 `HWND`。
- 交互渲染：Direct2D 窗口目标。
- 文字绘制与测量：DirectWrite。
- 图片解码与编码：WIC，支持 PNG / JPEG / BMP / GIF / TIFF 第一帧输入，导出 PNG / JPEG / BMP。
- 离屏导出：GPU 渲染到 WIC bitmap target 后编码。
- 剪贴板：Win32 Clipboard，支持文件列表、PNG 注册格式、DIB / DIBV5。
- GPU 初始化失败不会回退 GDI+；会尝试重建资源，仍失败则显示原生错误并退出。

## 下载

最新版本可以在 [GitHub Releases](https://github.com/huahai0202/quicker-image-annotator/releases/latest) 下载。

Release 资产：

- `AnnotatorApp.exe`：默认 AnyCPU 构建。
- `AnnotatorApp-x64.exe`：显式 x64 构建。

## 使用

复制一张图片到剪贴板后，直接运行：

```powershell
.\AnnotatorApp.exe
```

也可以传入本地图片路径：

```powershell
.\AnnotatorApp.exe "C:\path\to\image.png"
```

如果希望所有标注结果都保存到固定目录，可以传入输出目录：

```powershell
.\AnnotatorApp.exe --output-dir "D:\AnnotatedImages"
.\AnnotatorApp.exe "C:\path\to\image.png" --output-dir "D:\AnnotatedImages"
```

也可以点击工具栏上的设置按钮选择固定保存目录，或设置环境变量 `QUICKER_ANNOTATOR_OUTPUT_DIR`。命令行参数和环境变量会优先于工具栏设置；未设置时，本地图片保存到原图片目录，剪贴板图片保存到系统临时目录。

常用快捷键：

- `Ctrl+S`：保存并关闭。
- `Ctrl+Z`：撤销上一步标注或变换。
- `Esc`：取消文字编辑；非文字编辑时关闭窗口。
- `Delete` / `Backspace`：删除选中的标注。
- 鼠标滚轮：缩放查看。
- 右键拖动：平移画布。
- 文字编辑时 `Ctrl+A/C/X/V`、方向键、`Home`、`End`、`Shift` 选区可用。

## 构建

项目使用 .NET Framework 的 `csc.exe` 编译，不需要 `.csproj` 文件，也不需要 NuGet 或第三方 DLL。

默认构建并运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1
```

自检：

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

## 自检覆盖

- 禁止源码和构建脚本引用 `System.Drawing`、`System.Windows.Forms`、`Graphics`、`Bitmap`、`ImageFormat`。
- WIC 解码/编码 roundtrip。
- GPU 离屏导出。
- DirectWrite 文本测量和 hit-test。
- Direct2D 资源重建。
- 文字编辑、已有文字双击编辑、选中标注样式修改、箭头首尾控制点。
- 剪贴板 PNG、DIB 和保存输出回读。
- 基准 smoke test。

## 发布

发布或上传 GitHub 时需要同时更新两个可执行文件：

- `AnnotatorApp.exe`
- `AnnotatorApp-x64.exe`

源码包由 GitHub Releases 自动附带。完整 GPU-only 迁移记录见 [docs/superpowers/specs/2026-05-30-gpu-acceleration-design.md](docs/superpowers/specs/2026-05-30-gpu-acceleration-design.md)。
