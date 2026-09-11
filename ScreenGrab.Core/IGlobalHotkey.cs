using System;

namespace ScreenGrab.Core;

/// <summary>
/// Registers the global capture hotkey (Ctrl-Alt-F12, plus Ctrl-Alt-PrintScreen on Windows) and
/// raises <paramref name="onTriggered"/> when it fires. Implemented per-OS: Windows self-registers
/// via <c>RegisterHotKey</c> on a message-only window (the port of the <c>Form1</c> user32 code);
/// Linux/KDE does <b>not</b> self-register — the user binds Ctrl+Alt+F12 to <c>screengrab --capture</c>
/// as a KDE custom shortcut, so the Linux implementation is a no-op and capture arrives via
/// <see cref="ISingleInstanceIpc"/> instead.
/// </summary>
public interface IGlobalHotkey : IDisposable
{
	/// <summary>Begin listening for the hotkey. <paramref name="onTriggered"/> may be raised on a
	/// background thread, so marshal to the UI thread inside the callback. A no-op on platforms that
	/// rely on an external shortcut.</summary>
	void Register(Action onTriggered);
}
