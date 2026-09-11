# ScreenGrab — Cross-Platform (Windows + Fedora/KDE) Port Plan

## Context

ScreenGrab is a ~1,600-line single-project WinForms app (net10.0-windows7.0) that
lives in the system tray, captures a screen region on **Ctrl-Alt-F12**, lets the user
annotate with red rounded rectangles and movable text boxes (with undo/redo), then
copies to clipboard or saves a PNG to `~/Downloads/ScreenGrab`. It is entirely
Windows-bound through WinForms, GDI+/`System.Drawing`, user32/gdi32 P/Invoke, the
tray `NotifyIcon`, `Clipboard`, `Graphics.CopyFromScreen`, and a registry autostart entry.

The user also runs **Fedora 44 with KDE Plasma (Plasma 6, Wayland)** and wants the same
functionality there, including the same hotkey. Decision (confirmed): **one shared
Avalonia UI + SkiaSharp codebase** that runs on both OSes, with a thin per-OS layer for
the five platform-specific concerns. KDE is favorable: Spectacle/KWin handle capture,
and a KDE custom global shortcut can bind Ctrl-Alt-F12 to launch/trigger the app.

Outcome: a single `dotnet` solution producing a Windows build (feature-identical to today)
and a Fedora/KDE build with the same capture → annotate → copy/save workflow.

## Target architecture

Convert the project from WinForms to **Avalonia UI** (cross-platform .NET UI) with
**SkiaSharp** for all image compositing/annotation drawing (replaces GDI+). The annotation
model and undo/redo are pure C# and port unchanged.

```
ScreenGrab.sln
  ScreenGrab.Core        net10.0   — annotation model, undo/redo, capture→annotate flow, save logic (OS-agnostic)
  ScreenGrab.App         net10.0   — Avalonia UI: editor window, selection overlay, toast, tray (runs on both OSes)
  ScreenGrab.Windows     net10.0-windows  — Windows impls of the platform interfaces
  ScreenGrab.Linux       net10.0   — Linux/KDE impls of the platform interfaces
```

(Core + App + one platform assembly could also be merged with multi-targeting + `#if`
guards if the four-project split feels heavy; keep the *interface boundary* regardless.)

### Platform abstraction (defined in Core, resolved at startup by RID/OS)

| Interface | Windows impl | Linux/KDE impl |
|---|---|---|
| `IScreenCapture` (full-screen → `SKBitmap`) | GDI `CopyFromScreen`/BitBlt into SKBitmap | Shell out to **Spectacle** `spectacle -bnm -o <tmp.png>` (current monitor, background, no-notify); optional faster path via `org.kde.KWin.ScreenShot2` DBus |
| `IGlobalHotkey` | `RegisterHotKey`/`WM_HOTKEY` (existing user32 code) | No self-registration; rely on **KDE custom shortcut → `screengrab --capture`** + single-instance IPC (below). Optional: `org.freedesktop.portal.GlobalShortcuts` |
| `ISingleInstanceIpc` | Mutex + trigger running instance | Unix-domain socket lock in `$XDG_RUNTIME_DIR`; `--capture` sends "capture" to the running tray instance |
| `IClipboardService` (image + text) | Avalonia clipboard (works on Win) | text via Avalonia; **image via `wl-copy --type image/png`** (Klipper/Wayland) with `xclip` X11 fallback |
| `IStartupManager` | existing registry `StartupManager.cs` | write/remove `~/.config/autostart/screengrab.desktop` |

## Rewrite map (WinForms/GDI+ → Avalonia/Skia)

All references are to current [ScreenGrab/Form1.cs](ScreenGrab/Form1.cs) unless noted.

- **Annotation model & undo/redo** — `IAnnotation`/`IOperation` records, `ProjectAnnotations`,
  `AddRectOp`/`AddTextOp`/`EditTextOp`/`DeleteTextOp`, history/redo stacks (L16–59, L1168–1227):
  move to Core **verbatim** (pure logic, no UI types). This is the crown jewel — preserve it.
- **Image compositing** — every `Graphics.FromImage`/`Pen`/`SolidBrush`/`GraphicsPath`/
  `SmoothingMode`/`TextRenderer`/`BufferedGraphics` call (`DrawRoundedRectangle` L1243,
  `DrawTextAnnotationOnBitmap` L888, `GetTextAnnotationBounds` L854, `RedrawImageCore` L1194,
  `AddRedBorder` L1152, `OnPaint` L397) → reimplement on **`SKCanvas`/`SKBitmap`/`SKPaint`/
  `SKPath`/`SKFont`**. Skia gives `AddRoundRect`, PNG encode (`SKImage.Encode`), text
  measure/draw, and antialiasing — a direct 1:1 mapping. Keep the "burn annotations into the
  bitmap + keep a clean `originalImage` for redraw" strategy.
- **Editor window** — the host `Form1` becomes an Avalonia `Window` showing the `SKBitmap`
  via a custom render control; mouse handlers (`OnMouseDown/Move/Up/DoubleClick` L424–531) and
  `ProcessCmdKey` shortcuts (Esc / Ctrl+Z / Ctrl+Y, L223) map to Avalonia pointer/key events.
- **Text box** — WinForms `TextBox` overlay + drag/resize/mouse-wheel-font logic
  (`StartTextBox`→`CommitText` L565–814) → Avalonia `TextBox` positioned in an overlay
  `Canvas`; port the drag/offset/wrap math as-is (screen→client coordinate calls become
  Avalonia equivalents).
- **Selection overlay** — `SelectionForm` (L1275) → a **fullscreen borderless Avalonia window**
  showing the captured bitmap dimmed, rubber-band rectangle on top, Esc cancels. (KWin honors
  fullscreen top-levels; no layer-shell needed.)
- **Toast** — `ShowSilentNotification` (L988) → borderless topmost Avalonia window, auto-close
  timer; drop the `CreateRoundRectRgn` P/Invoke (use Skia/Avalonia rounded clip).
- **Tray** — `Program.cs` `NotifyIcon` + context menu (Capture / Run at startup / Exit) →
  **Avalonia `TrayIcon`** (StatusNotifierItem on KDE, Shell tray on Windows) with the same menu.
- **Save** — `SaveScreenshot` (L921) → Core; `~/Downloads/ScreenGrab` path via
  `Environment.SpecialFolder.UserProfile` is already cross-platform. Encode via Skia.
- **DPI** — `DeviceDpi/96f` scaling (L88) → Avalonia's `RenderScaling`/layout scaling; keep the
  same "high-DPI thickness/size" branches, driven by the Avalonia value.
- **Drop entirely on Linux**: alt-tab hiding (`GetWindowLong`/`WS_EX_TOOLWINDOW`, L1080) — a tray
  app with no taskbar entry is the Avalonia default; keep the P/Invoke only in the Windows impl.

## Linux/KDE specifics

- **Hotkey (same Ctrl-Alt-F12)**: ship a documented one-time setup — System Settings →
  Shortcuts → add a custom shortcut binding **Ctrl+Alt+F12** to `screengrab --capture`. The
  app supports a `--capture` CLI flag; if an instance is already running it forwards "capture"
  over the single-instance socket and exits, otherwise it starts and captures. (Optionally
  auto-register the KDE shortcut on first run by writing the `khotkeys`/`kglobalshortcutsrc`
  entry, but the manual step is the reliable baseline.)
- **Capture backend**: default to Spectacle CLI (guaranteed on KDE, works on Wayland). Capture
  the current monitor to match today's `Screen.PrimaryScreen` behavior, then run our own
  selection overlay + annotation on the result — identical UX to Windows.
- **Clipboard image**: Wayland clipboards don't reliably take images from generic toolkits;
  use `wl-copy` (present with Plasma/Wayland). Document `wl-clipboard` as a dependency.
- **Autostart**: `IStartupManager` writes `~/.config/autostart/screengrab.desktop` with
  `Exec=screengrab` (no `--capture`), mirroring the registry toggle in the tray menu.
- **Packaging**: publish self-contained (`dotnet publish -r linux-x64`) + an install script that
  drops the binary, a `screengrab.desktop` app entry, and the icon into `~/.local`. Note the
  runtime deps: `spectacle`, `wl-clipboard`.

### Known Wayland caveats to accept

- **Window positioning**: Wayland forbids apps from setting absolute window coordinates, so the
  editor/toast can't be centered on the cursor's monitor the way `CaptureSelectedRegion` (L361)
  and the toast (L1034) do on Windows — KWin places them. Functionally fine; note in UX.
- **Capture permission**: Spectacle/KWin may surface a one-time permission on first capture
  depending on KDE settings; no per-shot prompt expected.

## Files

- **New**: `ScreenGrab.Core/*` (annotation model, `Annotator`/Skia compositor, save, interfaces),
  `ScreenGrab.App/*` (Avalonia `App`, `EditorWindow`, `SelectionWindow`, `ToastWindow`, tray),
  `ScreenGrab.Linux/*` (Spectacle capture, socket IPC, wl-copy clipboard, autostart .desktop),
  `ScreenGrab.Windows/*` (move existing P/Invoke + `StartupManager.cs` here).
- **Reuse as-is**: annotation/operation records and undo/redo replay from
  [Form1.cs](ScreenGrab/Form1.cs) L16–59, L1168–1227; save path logic L921–957;
  [StartupManager.cs](ScreenGrab/StartupManager.cs) (→ Windows project); `icon.ico` (+ add a
  PNG for Linux tray/.desktop).
- **Retire**: WinForms designer files, `NotifyIcon`, all GDI+ and window-style/region P/Invoke
  once the Avalonia/Skia equivalents land.

## Phased implementation

**Status legend:** ✅ done · 🚧 in progress · ⬜ not started

1. ✅ **Scaffold** the Avalonia solution + project split; add `Avalonia`, `Avalonia.Desktop`,
   `Avalonia.Skia`, `SkiaSharp` packages. Verify empty tray app runs on Windows and Fedora.
   - Done 2026-09-11. Four new projects created and added to `screengrab.sln`:
     `ScreenGrab.Core` (net10.0, SkiaSharp 3.119.0), `ScreenGrab.App`
     (multi-targeted `net10.0;net10.0-windows`, Avalonia 12.1.2 + Fluent theme, tray-only,
     `AssemblyName=screengrab`), `ScreenGrab.Windows` (net10.0-windows), `ScreenGrab.Linux` (net10.0).
   - App references its per-OS platform project via TFM condition (`net10.0-windows`→Windows,
     `net10.0`→Linux), so each OS build links only its native code. All projects set
     `EnableWindowsTargeting` so both TFMs build from Windows.
   - `App.axaml` declares an Avalonia `TrayIcon` with Capture / Run at startup (checkbox) / Exit
     menu; handlers in `App.axaml.cs` are stubs for later phases; `ShutdownMode.OnExplicitShutdown`
     (no main window). Icon copied to `ScreenGrab.App/Assets/icon.ico` (+ `icon.png` for the Linux SNI tray).
   - Both TFMs build clean (0 warnings/errors); Windows exe launches and stays resident in the tray.
     Fedora/KDE run **not yet verified** in this environment — needs a manual smoke test on the target box.
   - Deviations from the plan's package list: dropped `Avalonia.Diagnostics` (no 12.1.2 release) and
     `.WithInterFont()` (needs `Avalonia.Fonts.Inter`); neither is needed for the scaffold. The old
     WinForms `ScreenGrab` project is left untouched and still in the solution until later phases retire it.
2. ✅ **Core**: port annotation model + undo/redo (verbatim) and build the SkiaSharp compositor
   (rounded rect, text, redraw-from-clean). Unit-test `ProjectAnnotations` replay.
   - Done 2026-09-11. `Class1.cs` placeholder removed; three files added to `ScreenGrab.Core`:
     - `Annotations.cs` — the model ported verbatim as public records: `IAnnotation`,
       `RectAnnotation`/`TextAnnotation`, `IOperation`, `AddRectOp`/`AddTextOp`/`EditTextOp`/`DeleteTextOp`.
       Geometry uses plain ints (was `System.Drawing` `Point`/`Rectangle`) so the model stays
       toolkit-agnostic; record equality drives the edit/replace logic unchanged.
     - `AnnotationHistory.cs` — the crown-jewel history/redo stacks + `ProjectAnnotations()` replay,
       lifted verbatim from `Form1`, wrapped as a reusable class (`Add`/`Undo`/`Redo`/`Clear`,
       `CanUndo`/`CanRedo`). Adding an op clears redo, exactly as before.
     - `AnnotationCompositor.cs` — SkiaSharp reimplementation of the GDI+ path. `DrawRoundedRectangle`
       → `SKCanvas.DrawRoundRect` (radius = the old GDI ellipse diameter / 2); text → `SKFont`/`DrawText`
       with a greedy word-wrap that mirrors `TextRenderer` `WordBreak` (no mid-word splits) and the same
       auto-size-to-content, cap-at-right-edge, +1px guard, clamp-height rules; `Render(clean, annotations,
       exclude?)` returns a fresh composite from a clean copy — the "keep a clean original, re-project +
       redraw" strategy, now allocation-per-render instead of mutating in place. `MeasureText` returns a
       `TextLayout` (bounds + wrapped lines) reused by both draw and hit-testing.
   - New xUnit project `ScreenGrab.Core.Tests` (net10.0), added to `screengrab.sln`. 15 tests, all green:
     10 cover `ProjectAnnotations` replay + undo/redo (insertion order, edit-in-place, defensive append
     for a missing original, delete, redo-clear-on-add, undo-reveals-original, clear), 5 smoke-test the
     compositor (bounds anchoring, right-edge wrapping, empty text, copy-not-mutate, undo-by-re-render).
   - Text metric parity with WinForms `TextRenderer` is not yet visually validated on a real capture
     (the known risk); the wrap math is faithful but the +1px guard may need re-tuning in phase 3/4.
     Save/PNG-encode via Skia stays in phase 5. Full solution builds clean (old WinForms project untouched).
3. ✅ **Editor window** in Avalonia: render the SKBitmap, port mouse/keyboard annotation flow and
   the text-box overlay (create → drag → resize → wheel-font → commit/cancel/edit/delete).
   - Done 2026-09-11. Three files added to `ScreenGrab.App`:
     - `EditorWindow.axaml` / `.axaml.cs` — an Avalonia `Window` (toolbar + `ScrollViewer` over a
       `Canvas` overlay hosting an `Image`). Ctor takes an `SKBitmap` (owns/disposes it). Uses
       `AnnotationHistory` + `AnnotationCompositor` from Core; re-projects and repaints on every
       change. Ports `Form1` verbatim in behaviour: left-drag draws a red rounded-rect rubber-band
       (Avalonia `Rectangle` preview, committed as `AddRectOp` at ≥10px); double-click hit-tests
       live text (via `MeasureText` bounds) to edit-in-place or starts a new text box; text box
       auto-sizes through the compositor's `MeasureText` so the live box matches the burned-in
       result; drag-to-move (4px threshold, fixed grab offset), mouse-wheel font size
       (`Min/MaxFontSize*scale`), Enter/LostFocus commit, Esc cancels; edit commits as one
       `EditTextOp`/`DeleteTextOp`, hiding the original via the compositor's `exclude` while editing.
       Ctrl+Z/Ctrl+Y drive `AnnotationHistory.Undo/Redo`; Esc closes when no text box is active.
     - `SkiaInterop.cs` — copies a composited `SKBitmap` into an Avalonia BGRA8888 `WriteableBitmap`
       (managed `Marshal.Copy`, stride-aware; no `unsafe`). Conversion runs only when the composite
       changes, not per frame.
   - Coordinates: the overlay `Canvas` is laid out 1:1 with image pixels (no `headerPanel.Height`
     offset — the toolbar is a separate dock region), so pointer positions are image coordinates.
   - Tray `Capture`/click now open the editor on a blank 1280×800 canvas — a **temporary phase-3
     harness** so the flow is exercisable on both OSes before capture exists; phase 4 swaps the blank
     bitmap for an `IScreenCapture` result via the selection overlay. Toolbar Copy/Save/Save&CopyPath
     buttons are present but stubbed (phase 5).
   - Both TFMs build clean (0/0); 15 Core tests still green; Windows exe launches and stays resident.
     Deviations/known gaps: `_scaleFactor` is fixed at 1 for now (real `RenderScaling` threading is
     deferred — HiDPI captures display 1:1 in DIPs, may look soft until phase 4); SkiaSharp vs
     Avalonia `TextBox` text metrics may differ by a hair (the known wrap risk) — not yet validated
     on a real capture; interactive editor UX **not yet smoke-tested** on Fedora/KDE.
4. **Capture + selection overlay**: `IScreenCapture` (Windows GDI first, then Spectacle on
   Linux) feeding the fullscreen selection window → crop → editor.
5. **Tray, clipboard, save, toast, autostart** behind the interfaces for both OSes.
6. **Hotkey/IPC**: Windows `RegisterHotKey`; Linux `--capture` flag + socket + documented KDE
   custom shortcut.
7. **Package**: Windows publish (unchanged UX) + Linux self-contained publish and install script.

## Verification

- **Windows regression**: build and run; confirm tray menu, Ctrl-Alt-F12 capture, region drag,
  rounded-rect + text annotations, drag/resize/font-wheel on text, Ctrl+Z/Y undo/redo, Copy,
  Save, Save(copy path), toast, and Run-at-startup all behave exactly as before.
- **Fedora/KDE**: on Plasma 6 Wayland — install, bind Ctrl+Alt+F12 to `screengrab --capture`,
  confirm capture (Spectacle) → overlay selection → annotate → Copy (paste into an app via
  Klipper) → Save PNG to `~/Downloads/ScreenGrab` → path-copy → toast → autostart toggle writes
  `~/.config/autostart/screengrab.desktop`. Also smoke-test the tray menu Capture/Exit.
- **Cross-check** annotation rendering (rounded-rect radius/thickness, text bg color
  `#1565C0`, wrapping, DPI scaling) matches between OSes on the same captured image.

## Open risks / to validate during build

- SkiaSharp text metrics vs WinForms `TextRenderer` wrapping — re-tune the +1px guard math
  (`ResizeTextBox` L617 / `GetTextAnnotationBounds` L854) against Skia's measurement.
- Spectacle "current monitor, background, no-notify" flags on the installed version; fall back
  to KWin ScreenShot2 DBus or portal Screenshot if flags differ on Plasma 6.
- Avalonia image clipboard on Windows (verify `SetImage` parity with WinForms) and `wl-copy`
  image behavior with Klipper.
- Whether to auto-write the KDE global shortcut vs. document the manual bind (default: document).
