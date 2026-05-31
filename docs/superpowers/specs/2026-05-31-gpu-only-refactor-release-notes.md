# 2026-05-31 GPU-only 重构发布说明

## 摘要

本次是一次重大架构重构：Quicker Image Annotator 从 WinForms / GDI+ 混合实现迁移为 GPU-only 实现。应用自有渲染、图片 I/O、导出、文字测量、工具栏、设置 overlay 和剪贴板图片处理均改为原生 Windows API P/Invoke 路线，不引入 NuGet 或第三方 DLL。

## 主要变化

- 渲染后端固定为 `GpuRenderer`。
- Direct2D 负责窗口绘制和 WIC 离屏导出。
- DirectWrite 负责文字绘制、测量、hit-test 和 IME 编辑态显示。
- WIC 负责 PNG / JPEG / BMP / GIF / TIFF 第一帧解码，以及 PNG / JPEG / BMP 编码。
- Win32 `HWND` + 消息循环替代 WinForms 窗口和控件树。
- Win32 Clipboard 支持图片文件、PNG 注册格式、DIB / DIBV5 和文字编辑态 Unicode 文本。
- 删除 GDI+ 后端、后端选择参数和 `System.Drawing` / `System.Windows.Forms` 依赖。

## 交互修复

- 选中标注后支持移动、删除、撤销、继续调整样式。
- 颜色/线宽浮层改为 GPU 绘制并带短动画。
- 设置 overlay 改为 GPU 绘制，保留保存目录、浏览、清空、保存、取消。
- 清空标注增加二次确认。
- 已完成文字双击可继续编辑。
- 文字编辑支持 caret、选区、IME composition、`Ctrl+A/C/X/V`、方向键、`Home` / `End`。
- 箭头选中后只显示首尾两个控制点。
- 鼠标光标按工具、标注本体、调整点和文字编辑态切换。
- 置顶按钮显示选中态。
- 移除临时 `GPU-only` UI 标记。

## 构建与发布

需要同时生成并上传两个可执行文件：

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -SelfTest
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -Platform x64 -SelfTest
```

生成物：

- `AnnotatorApp.exe`
- `AnnotatorApp-x64.exe`

## 验证

本次发布前要求两套自检均通过。自检覆盖：

- 禁用依赖扫描。
- WIC roundtrip。
- GPU 导出。
- DirectWrite 测量与 hit-test。
- Direct2D resource rebuild。
- 窗口 chrome 渲染。
- 标注选择、变换、删除、撤销。
- 文字编辑和已有文字重新编辑。
- 箭头首尾控制点。
- 剪贴板读写。
- 基准 smoke test。
