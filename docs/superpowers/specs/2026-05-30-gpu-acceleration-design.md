# GPU-only 迁移实施记录

## 0. 当前结论

当前主干已经从 WinForms / GDI+ 混合架构迁到 GPU-only 架构：

- 应用自有代码不再引用 `System.Drawing` 或 `System.Windows.Forms`。
- 构建脚本不再引用 `System.Drawing.dll` 或 `System.Windows.Forms.dll`。
- 主窗口使用原生 Win32 `HWND` 和消息循环。
- 主画布、工具栏、提示、设置 overlay、颜色/线宽面板和标注绘制使用 Direct2D / DirectWrite。
- 图片解码、保存编码和离屏导出使用 WIC。
- 已删除 `GdiAnnotationRenderer`、`RenderCanvasGdi`、`RenderFinalImageGdi` 和 `--render-backend` 后端选择。
- GPU 初始化失败不再回退 GDI+；会尝试重建 Direct2D 资源，仍失败则显示原生错误并退出。

这已经不是“默认 Auto + GDI+ 回退”版本，而是 GPU-only 主线版本。

## 1. 架构现状

### 1.1 核心模块

- `GpuPrimitives.cs`
  - 定义 `GpuPoint`、`GpuRect`、`GpuExtent`、`Rgba`、`AnnotationItem`、`ToolMode`、`SelectionHandle` 和 GPU-only 样式常量。
- `NativeApi.cs`
  - 封装 Win32、COM、Direct2D、DirectWrite、WIC、Imm32 和 Clipboard P/Invoke。
- `WicImageDocument.cs`
  - 负责 WIC 图片解码、WIC 像素存储、PNG / JPEG / BMP 编码。
- `GpuRenderer.cs`
  - 唯一渲染器，支持 Win32 HDC 交互渲染和 WIC 离屏导出。
- `GpuAnnotatorWindow.cs`
  - Win32 窗口、工具栏、鼠标、滚轮、键盘、IME composition、保存、设置 overlay 和剪贴板桥接。
- `AnnotatorApp.cs`
  - 程序入口、参数解析、自检、GPU 基准报告。

### 1.2 已迁移功能

- 图片路径输入和剪贴板文件输入。
- WIC 解码 PNG / JPEG / BMP / GIF / TIFF 第一帧。
- Win32 窗口和消息循环。
- Direct2D 绘制底图缩放、矩形、椭圆、箭头、画笔、文字、马赛克、选区框、工具栏、tooltip、颜色/线宽面板和设置 overlay。
- DirectWrite 绘制已提交文字、文字编辑预览、选区、caret、IME composition 下划线。
- WIC 离屏导出并编码为 PNG / JPEG / BMP。
- 保存后写入剪贴板：
  - `CF_HDROP` 文件列表。
  - 注册 `PNG` 格式。
  - `CF_DIB` 可粘贴像素数据。
- 剪贴板读取：
  - `CF_HDROP` 图片文件。
  - 注册 `PNG` 格式。
  - `CF_DIB` / `CF_DIBV5`。
- 文本剪贴板：
  - `CF_UNICODETEXT` 支持文字编辑态 `Ctrl+C/X/V`。
- `--render-profile` 和 `--render-benchmark` 继续保留。

### 1.3 交互行为

- 选择标注后可以移动、删除、撤销。
- 选中已有标注后，颜色/线宽面板会同步当前样式，并可直接修改选中标注。
- 箭头选中后只显示首尾两个控制点，拖动首尾直接改变箭头方向和长度。
- 矩形、椭圆、马赛克保留八方向调整点。
- 已完成文字可双击重新编辑；提交时更新原标注并写入撤销栈。
- 文字编辑支持 caret、选区、方向键、`Home` / `End`、`Shift` 选区、`Backspace` / `Delete`、`Ctrl+A/C/X/V` 和中文输入法候选窗定位。
- 鼠标光标按上下文切换：
  - 空白图片区域使用当前工具对应的十字或 I-beam。
  - 标注本体显示移动光标。
  - 调整点显示对应方向的 resize 光标。
  - 工具栏、画布外和设置 overlay 显示箭头。
- 置顶按钮会显示选中态。
- 右侧临时 `GPU-only` 状态标记已移除。

### 1.4 行为变化

- 已移除 `--render-backend Gdi|Auto|D2D`。
- `actual_backend` 固定为 `GpuRenderer`。
- `hardware_accelerated` 固定为 `yes`，如果 Direct2D 初始化失败则启动失败。
- 设置界面已改为 GPU overlay，文件夹选择使用原生 Shell 对话框。
- 马赛克已脱离 GDI+，当前实现为 Direct2D 逐块 1x1 texel 采样后 nearest-neighbor 放大，并叠加遮罩。

## 2. 验证现状

每次项目修改后必须运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -SelfTest
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -Platform x64 -SelfTest
```

当前自检覆盖：

- 禁止源码和构建脚本引用 `System.Drawing`、`System.Windows.Forms`、`Graphics`、`Bitmap`、`ImageFormat`。
- WIC PNG roundtrip。
- GPU 离屏导出像素尺寸与 alpha 有效性。
- DirectWrite text layout 测量与 hit-test。
- Direct2D 窗口目标释放后重建并再次渲染。
- GPU 窗口 chrome 渲染。
- 选中、移动、删除、撤销、样式修改、文字编辑和箭头首尾控制点。
- 剪贴板 PNG、DIB 和保存输出回读。
- GPU 基准 smoke test。

2026-05-30 x64 交互路径基准见：

```text
docs/superpowers/specs/2026-05-30-render-benchmark.md
```

最新 60 帧结果：

| 场景 | 图片 | 画布 | actual_backend | hardware_accelerated | P95 |
| --- | --- | --- | --- | --- | ---: |
| 1080p | 1920x1080 | 1280x720 | GpuRenderer | yes | 3.278ms |
| 4K | 3840x2160 | 1600x900 | GpuRenderer | yes | 5.449ms |
| 8K | 7680x4320 | 1600x900 | GpuRenderer | yes | 15.541ms |
| 长截图 | 1440x6400 | 900x1400 | GpuRenderer | yes | 5.909ms |

## 3. 发布资产

GitHub 发布或推送构建时需要同时更新：

- `AnnotatorApp.exe`
- `AnnotatorApp-x64.exe`

`BuildAndRun.ps1` 默认生成 `AnnotatorApp.exe`，使用 `-Platform x64` 生成 `AnnotatorApp-x64.exe`。

## 4. 剩余风险

- 当前 Direct2D / WIC 路径覆盖了应用自有渲染和导出，但马赛克尚未使用独立 pixel shader；如果未来大面积马赛克成为瓶颈，再评估 shader 或多级离屏缓存。
- 自检覆盖主要行为链路，真实 GUI 仍建议在发布前手工走一遍：剪贴板图片、文件路径、缩放平移、各类标注、中文 IME、设置 overlay、保存和剪贴板回写。
- GPU 初始化失败不回退 GDI+ 是刻意设计，用户环境若缺少可用 Direct2D/WIC 组件会直接失败。

## 5. 后续方向

1. 增加更多真实截图 golden baseline，覆盖工具栏、设置 overlay、文字编辑态和各种选区状态。
2. 继续观察 8K / 长截图场景下的马赛克和超长画笔路径性能。
3. 若后续接入 GitHub Release 自动化，确保两个 exe 都作为 release assets 上传。
