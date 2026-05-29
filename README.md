<p align="center">
  <img src="docs/assets/app-icon.png" alt="Quicker Image Annotator icon" width="96" height="96">
</p>

# Quicker Image Annotator

一个轻量的 Windows 图片标注工具，适合从剪贴板或本地图片快速打开、圈画重点、打马赛克、添加文字，然后保存并复制结果。

## 功能

- 支持矩形、椭圆、箭头、画笔、马赛克和文字标注。
- 支持撤销、清空、适合窗口、窗口置顶和缩放查看。
- 可以直接读取剪贴板图片，也可以通过命令行传入图片路径。
- 保存时会生成带 `_标注` 后缀的新图片，并把结果复制回剪贴板。
- 渲染时缓存静态标注，拖动、缩放和连续绘制更流畅。

## 下载

最新版本可以在 [GitHub Releases](https://github.com/huahai0202/quicker-image-annotator/releases/latest) 下载。

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

也可以点击工具栏上的设置按钮选择固定保存目录，或设置环境变量 `QUICKER_ANNOTATOR_OUTPUT_DIR`。命令行参数和环境变量会优先于工具栏设置；未设置时仍保存到原图片所在目录。

常用快捷键：

- `Ctrl+S`：保存并关闭。
- `Ctrl+Z`：撤销上一步标注。
- `Esc`：取消并关闭。
- `F` 或 `0`：适合窗口。
- `1` / `2` / `3` / `4`：切换矩形、箭头、画笔、马赛克。

## 构建

项目使用 .NET Framework 的 `csc.exe` 编译，不需要 `.csproj` 文件。运行构建脚本会在源码变更后自动重新编译并启动应用：

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1
```

自检：

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -SelfTest
```

x64 构建：

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -Platform x64 -SelfTest
```

## 发布

Release 资产包含可直接运行的 `AnnotatorApp.exe`，以及显式 x64 构建的 `AnnotatorApp-x64.exe`。源码包由 GitHub Releases 自动附带。
