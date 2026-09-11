using System;

namespace ScreenGrab.Core;

/// <summary>
/// Enforces a single running tray instance and forwards <c>--capture</c> requests to it. Windows uses a
/// named <c>Mutex</c> + a named pipe (the port of the <c>Program.cs</c> mutex); Linux/KDE uses a
/// Unix-domain socket in <c>$XDG_RUNTIME_DIR</c>, which is how a KDE custom shortcut's
/// <c>screengrab --capture</c> reaches the already-running tray instance.
/// </summary>
public interface ISingleInstanceIpc : IDisposable
{
	/// <summary>Attempt to become the primary (first) instance. Returns <c>true</c> if acquired — the
	/// caller then starts the tray and calls <see cref="StartListening"/>. Returns <c>false</c> if
	/// another instance already owns the lock — the caller should <see cref="SignalCapture"/> (when
	/// launched with <c>--capture</c>) and exit.</summary>
	bool TryAcquire();

	/// <summary>Primary instance only: begin accepting capture requests from later
	/// <c>--capture</c> launches. Each request raises <see cref="CaptureRequested"/>.</summary>
	void StartListening();

	/// <summary>Raised (possibly on a background thread) when a secondary process forwards a capture
	/// request to this primary instance. Marshal to the UI thread inside the handler.</summary>
	event Action? CaptureRequested;

	/// <summary>Secondary instance only: tell the running primary instance to start a capture.</summary>
	void SignalCapture();
}
