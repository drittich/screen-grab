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

## Fedora / KDE

The Linux build runs the same capture → annotate → copy/save workflow. Two notes specific to KDE
Plasma 6 (Wayland):

### Global hotkey (Ctrl-Alt-F12)

Wayland does not let an app grab a system-global hotkey, so ScreenGrab does not self-register one.
Bind it once as a KDE custom shortcut:

1. **System Settings → Keyboard → Shortcuts → Add New → Command or Script**.
2. Set the command to `screengrab --capture`.
3. Assign the trigger **Ctrl+Alt+F12** and apply.

`screengrab --capture` forwards the request to the already-running tray instance over a Unix-domain
socket in `$XDG_RUNTIME_DIR` (and starts one, capturing immediately, if none is running). A single tray
instance is enforced, so a second plain `screengrab` launch just exits.

### Dependencies

Capture uses **Spectacle** and image-to-clipboard uses **wl-clipboard** (`wl-copy`); the Fedora RPM
declares both as requirements.
