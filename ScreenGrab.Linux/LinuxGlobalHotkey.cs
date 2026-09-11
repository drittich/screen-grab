using System;
using ScreenGrab.Core;

namespace ScreenGrab.Linux;

/// <summary>
/// Linux/KDE <see cref="IGlobalHotkey"/>: intentionally a no-op. Wayland forbids apps from grabbing a
/// system-global hotkey, so ScreenGrab does not self-register. Instead the user binds Ctrl+Alt+F12 to
/// <c>screengrab --capture</c> as a KDE custom shortcut (System Settings → Shortcuts); that launch is
/// forwarded to the running tray instance over <see cref="LinuxSingleInstanceIpc"/>.
/// </summary>
public sealed class LinuxGlobalHotkey : IGlobalHotkey
{
	public void Register(Action onTriggered) { /* external KDE shortcut → --capture → IPC */ }

	public void Dispose() { }
}
