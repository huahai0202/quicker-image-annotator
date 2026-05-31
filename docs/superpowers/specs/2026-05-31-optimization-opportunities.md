# Optimization Opportunities

## Scope

This note records likely optimization opportunities across the current codebase. The project is a Windows desktop image annotator, so the meaningful surfaces are startup, rendering, clipboard I/O, window interaction, text editing, persistence, and release packaging. There is no database layer, network API layer, or third-party SDK integration in the current repository; those areas are therefore out of scope for actual tuning, but future integration points are noted where relevant.

## Baseline signals

Current measured and structural signals:

- 30-frame x64 render benchmark: 1080p P95 5.685 ms, 4K P95 8.564 ms, 8K P95 11.961 ms, long-shot P95 7.866 ms.
- Earlier 60-frame benchmark in `docs/superpowers/specs/2026-05-30-render-benchmark.md`: 1080p P95 3.278 ms, 4K P95 5.449 ms, 8K P95 15.541 ms, long-shot P95 5.909 ms.
- Source size: about 282.5 KB across 11 `.cs` files.
- Largest files: `GpuAnnotatorWindow.cs` 2796 lines, `GpuRenderer.cs` 1078 lines, `NativeApi.cs` 1016 lines, `AnnotatorApp.cs` 858 lines, `ClipboardBridge.cs` 538 lines.

Already landed first-pass wins include lazy renderer creation on first paint, launch-to-first-frame logging, input-to-frame logging, `ClipboardBridge` extraction, inline and static text layout caching, shared DirectWrite factory reuse, cached hover cursor lookup, reduced per-tile mosaic overlay work, per-process clipboard temp reuse, clipboard startup smoke coverage, and memory-DC benchmark smoke coverage. The remaining items below are the next optimization frontier rather than a snapshot of untouched code.

## Priority 1: Reduce startup work on the UI thread

### Current state

`Program.Main` still loads the image synchronously before the window appears. `GpuAnnotatorWindow.Run()` now defers renderer creation until the first paint, so the old eager first-frame work is gone. `WicImageDocument.Load()` still decodes the full image into memory before the window appears, so image decode and clipboard/file I/O remain the dominant startup cost.

### Proposed improvement

- Move image decode or metadata gathering off the main thread if startup latency becomes a larger concern.
- Keep any new renderer caches lazy so they do not reintroduce up-front stalls.

### Expected benefit

- Lower time-to-first-interaction.
- Less visible startup stall on large clipboard images or large local files.
- Better perceived responsiveness on cold start.

### Complexity

Medium.

### Priority

High.

### Notes

The latest 30-frame benchmark still shows steady render times, so the bigger win here is startup latency rather than steady-state throughput.

## Priority 2: Split the 3k-line window class

### Current state

`GpuAnnotatorWindow.cs` still mixes window creation, event handling, hit-testing, inline text editing, selection editing, settings overlay, and save/close flow in a single file, but clipboard handling has already been extracted into `ClipboardBridge`. The window class is smaller than before, yet it remains the repo's main interaction hotspot.

### Proposed improvement

Split responsibilities into focused units such as:

- `WindowLifecycle` or `WindowHost`
- `InputController`
- `SelectionEditor`
- `TextEditor`
- `OverlayController`

### Expected benefit

- Easier maintenance and safer refactoring.
- Lower risk of regressions when changing interaction behavior.
- Better testability for hit-testing and text-editing logic.

### Complexity

Medium to high.

### Priority

High.

### Notes

This remains the main maintainability hotspot in the repo.

## Priority 3: Cache text-format and text-layout work more aggressively

### Current state

`GpuRenderer.MeasureTextBounds()` and `GpuRenderer.HitTestText()` now reuse a shared DirectWrite factory, and `TextLayoutCache` is used for active editing sessions, stable UI text, and static text annotations. `GpuAnnotatorWindow` also keeps an inline text layout for caret and selection work. The remaining repeated text work is mostly fallback prefix measurement when the inline cache is unavailable.

### Proposed improvement

- Replace any remaining prefix-width fallback with layout hit tests or incremental caret metrics.

### Expected benefit

- Lower CPU usage during inline text editing and overlay rendering.
- Less allocation churn.
- Better typing and caret-move responsiveness on long text strings.

### Complexity

Medium.

### Priority

High.

### Quantitative angle

Text hit-testing and prefix measurement are called from mouse movement and editing paths. Even if each call is cheap, the multiplication across frames makes this a likely hotspot on long captions.

## Priority 4: Cut repeated hit-testing and geometry recomputation on mouse move

### Current state

`MouseMove()` can still invoke tooltip updates, hover cursor checks, selection hit-testing, view conversion, and repeated `GetView()` calls. `GetHoverCursorId()` now memoizes cursor results by pointer position and hover-state version, and it short-circuits to an arrow cursor when the pointer is outside the image view.

### Proposed improvement

- Cache the current view rectangle and scale once per event/frame.
- Reuse computed image-point and hit-test data for cursor selection, selection handles, and tooltip positioning.
- Short-circuit unchanged pointer states to avoid repaint storms.

### Expected benefit

- Lower CPU use during idle pointer movement.
- Smoother cursor transitions on high-DPI or large canvases.
- Fewer invalidations and redraws.

### Complexity

Low to medium.

### Priority

Medium to high.

### Quantitative angle

This is more about frame consistency than average render time. The 8K P95 of 14.347 ms leaves little room for extra pointer-driven overhead before a 60 Hz budget is at risk.

## Priority 5: Optimize mosaic rendering for large selections

### Current state

`DrawMosaic()` still iterates block by block, but the per-tile overlay fill has already been removed. That trims one draw call per tile, though large blurred areas still scale with annotated area rather than with canvas size alone.

### Proposed improvement

- Use an offscreen cached mosaic texture or lower-resolution cached source for large regions.
- Merge adjacent blocks where visual fidelity permits.
- Consider a shader or image effect path if the rendering backend grows more capable later.

### Expected benefit

- Better large-area mosaic performance.
- Reduced draw-call count on dense blur regions.

### Complexity

Medium to high.

### Priority

Medium.

### Quantitative angle

The current 8K benchmark is already under 16.7 ms P95, but large mosaic edits are a plausible path to exceed that budget because they scale with annotated area rather than just canvas size.

## Priority 6: Remove hidden temp-file churn in clipboard flows

### Current state

Clipboard image reads can materialize PNG or DIB content into a temp PNG path. Save/export also writes the final file and pushes clipboard data. This is correct, and `ClipboardBridge` now reuses a single temp PNG path with hash/size checks, but clipboard round trips still touch disk on PNG/DIB conversion.

### Proposed improvement

- Prefer in-memory clipboard handoff where possible.
- Keep reusing the per-session temp path for repeated clipboard reads.
- Consider a temp cleanup policy for stale clipboard artifacts.

### Expected benefit

- Less disk churn.
- Lower latency when users chain screenshots rapidly.
- Fewer leftover temp artifacts.

### Complexity

Low to medium.

### Priority

Medium.

### Notes

This is a resource-utilization win more than a pure render win.

## Priority 7: Tighten `NativeApi` surface and isolate P/Invoke wrappers

### Current state

`NativeApi.cs` is still the broad dump of Win32, COM, Direct2D, DirectWrite, WIC, IME, clipboard, and shell interop, but the shared DirectWrite factory cache and hit-test helpers have already reduced some call-site duplication. Ownership rules are still spread across a lot of surface area.

### Proposed improvement

- Group P/Invoke by subsystem.
- Wrap common COM lifetime patterns in helper types.
- Consider small typed wrappers for clipboard and window handles.

### Expected benefit

- Lower defect risk around unmanaged cleanup.
- Easier future integration with other Windows APIs.
- Better readability for contributors.

### Complexity

Medium.

### Priority

Medium.

### Third-party / integration note

The repo does not currently depend on external libraries, so this is about system API hygiene rather than vendor integration.

## Priority 8: Add targeted regression baselines for user-visible quality

### Current state

There is now a render benchmark, launch-to-first-frame logging, input-to-frame logging, a benchmark smoke self-test, a clipboard startup smoke test, and a broad self-test suite, but there are no stored golden screenshots or enforced latency thresholds for input interactions.

### Proposed improvement

- Add golden image checks for toolbar, settings overlay, text editing, and arrow/selection states.
- Persist or threshold the launch-to-first-frame metric once enough machine-local samples exist.
- Persist or threshold the input-to-frame metric once enough machine-local samples exist.

### Expected benefit

- Safer future optimization.
- Easier detection of accidental UI regressions.
- Better confidence when refactoring the big window class.

### Complexity

Medium.

### Priority

Medium.

### Quantitative angle

The launch-to-first-frame and input-to-frame metrics now complement the existing render benchmark; persisted thresholds would make the current 7.0 to 16.1 ms steady-state P95 frame numbers more actionable.

## Out of scope today

- Database optimization: none exists in the repository.
- Network/API optimization: none exists in the repository.
- Third-party SDK tuning: none exists in the repository.

## Recommended order of attack

1. Split the remaining `GpuAnnotatorWindow` responsibilities into smaller controllers.
2. Add startup and interaction latency measurements.
3. Replace the remaining text prefix fallback with layout hit tests.
4. Optimize mosaic rendering further if real-world edits show it is hot.
5. Add clipboard temp cleanup or in-memory handoff if screenshot chains still feel slow.

## Verification policy

Any code change that touches startup, rendering, or release artifacts should still be followed by:

```powershell
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -SelfTest
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -Platform x64 -SelfTest
powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 -Platform x86 -SelfTest
```
