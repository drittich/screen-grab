using SkiaSharp;

namespace ScreenGrab.Core;

/// <summary>
/// Grabs the whole screen as a fresh <see cref="SKBitmap"/>. The result feeds the fullscreen
/// selection overlay, which crops the user's chosen region for the editor. Implemented per-OS:
/// GDI <c>BitBlt</c> on Windows, Spectacle CLI on Linux/KDE.
/// </summary>
public interface IScreenCapture
{
	/// <summary>Captures the current/primary monitor. The caller owns and disposes the bitmap.</summary>
	SKBitmap CaptureFullScreen();
}
