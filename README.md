## screen-grab

A Windows screen-capture utility.

## Usage

- Ctrl-Alt-F12 to invoke
- Drag to selected image area
- Drag on captured image to add one or more highlight rectangles
- <kbd>Ctrl</kbd>-<kbd>Z</kbd>/<kbd>Ctrl</kbd>-<kbd>Y</kbd> to undo/redo highlights
- Then click button to:
    - Save image to clipboard,
    - Save image to disk, or
    - Save image to disk and copy file path to clipboard
- <kbd>Esc</kbd> to close

## Run at startup

Right-click the ScreenGrab tray icon and check **Run at startup**. This adds a per-user entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (no admin rights needed); it also appears in
Task Manager → Startup apps and Settings → Apps → Startup. Uncheck it to remove the entry.

If you move the executable, re-toggle the option so the registry entry points at the new path.
