<p align="center">
  <img src="docs/assets/app-icon.png" alt="Quicker Image Annotator icon" width="96" height="96">
</p>

# Quicker Image Annotator

Quicker Image Annotator 是一个轻量的 Windows 图片标注工具。它面向截图、剪贴板图片和本地图片的快速处理：打开图片、圈出重点、打马赛克、添加文字、保存结果，并把标注后的图片复制回剪贴板。

## 主要功能

- 从剪贴板图片或本地图片路径启动。
- 支持识别窗口或框选屏幕区域并进入标注。
- 支持矩形、椭圆、箭头、画笔、马赛克和文字标注。
- 支持选择、移动、缩放、调整大小、撤销、清空、适合窗口和窗口置顶。
- 箭头可以通过首尾控制点继续调整方向和长度。
- 文字标注支持输入法、光标、选区、复制、剪切、粘贴和双击再编辑。
- 选中已有标注后可以继续修改颜色和线宽。
- 保存时生成带 `_标注` 后缀的新图片，并复制到剪贴板。
- 截图后原始截图会先复制到剪贴板，保存后再复制标注结果。
- 支持接入百度 OCR，可在标准版和高精度版之间切换，并在识别后按逐行或智能段落查看结果，且可一键调用 Google 翻译。
- 可设置固定输出目录，适合连续处理多张图片。
- 可开启开机自启后台，并使用可自定义的全局快捷键截图标注。
- 后台运行时显示系统托盘图标，左键点击可打开截图标注，右键可进入设置、检查更新或退出后台。

## 下载

最新版本可以在 [GitHub Releases](https://github.com/huahai0202/quicker-image-annotator/releases/latest) 下载。

发布资产包含：

- `AnnotatorApp.exe`：默认 AnyCPU 构建。
- `AnnotatorApp-x64.exe`：显式 x64 构建。
- `AnnotatorApp-x86.exe`：显式 x86 构建。

源码仓库不提交可执行产物；exe 只通过 Release 资产分发。

## 使用方式

复制一张图片到剪贴板后直接运行：

```powershell
.\AnnotatorApp.exe
```

也可以传入本地图片路径：

```powershell
.\AnnotatorApp.exe "C:\path\to\image.png"
```

识别窗口或框选屏幕区域并进入标注：

```powershell
.\AnnotatorApp.exe --screenshot
```

运行后台热键宿主：

```powershell
.\AnnotatorApp.exe --background
```

后台宿主会显示系统托盘图标；左键点击图标和全局快捷键一样，会打开截图标注入口。右键菜单支持设置、打开标注保存目录、打开截图保存目录、检查更新、关于和退出后台。

标注保存目录、截图保存目录、开机自启和全局快捷键可以通过工具栏设置按钮或托盘设置窗口保存；未设置时，本地图片保存到原图片目录，剪贴板图片或截图保存到系统临时目录。

百度 OCR 可在托盘设置窗口中填写 API Key 和 Secret Key，并选择默认识别版本与结果排版。OCR 调用使用百度通用文字识别标准版 `general_basic` 或高精度版 `accurate_basic`；点击工具栏 OCR 按钮或按 `Ctrl+O` 后，若当前有选中的标注区域则识别该区域，否则识别整张图片。识别完成后会打开结果窗口，可切换逐行和智能段落排版、复制文本，或点击 Google 翻译将当前 OCR 文本翻译为中文。

## 快捷键和操作

- `Ctrl+S`：保存并关闭。
- `Ctrl+Z`：撤销上一步标注或变换。
- `Ctrl+D`：清空当前标注。
- `Ctrl+O`：调用百度 OCR 识别当前选区或整张图。
- `Ctrl+F`：适合窗口。
- `Ctrl+P`：切换窗口置顶。
- `1` 到 `6`：切换矩形、椭圆、箭头、画笔、马赛克和文字工具。
- `Alt+A`：默认全局快捷键；可在托盘设置窗口改为其他组合。
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
- 屏幕截图：Win32/GDI。
- OCR 和翻译网络调用：百度 OCR HTTP API、Google Translate 网络接口。
- 后台快捷键、自启、托盘图标和设置窗口：Win32 HotKey、Shell Notify Icon、Win32 原生控件与当前用户 Run 启动项。

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

x86 自检：

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -Platform x86 -SelfTest
```

## 自检覆盖

自检会覆盖图片 I/O、屏幕截图、截图选择 HUD、GPU 导出、DirectWrite 文本测量和 hit-test、Direct2D 资源重建、窗口 chrome 渲染、主要交互语义、文字编辑、撤销、快捷键映射、OCR 排版语义、Google 翻译解析、自启命令、命令行入口、剪贴板 PNG/DIB 流程，以及基准 smoke test。自检不会真实调用百度 OCR 或 Google 翻译，也不会消耗 OCR 额度。

## 发布

发布或上传 GitHub 时需要同时更新三个可执行文件：

- `AnnotatorApp.exe`
- `AnnotatorApp-x64.exe`
- `AnnotatorApp-x86.exe`

这些文件应作为 GitHub Release 资产上传，不提交到源码树。

项目说明和性能记录放在 [docs/superpowers/specs](docs/superpowers/specs)。
