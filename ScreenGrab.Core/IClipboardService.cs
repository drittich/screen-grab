using SkiaSharp;

namespace ScreenGrab.Core;

/// <summary>
/// Puts an image or text onto the system clipboard. Implemented per-OS: Win32 clipboard
/// (CF_DIB / CF_UNICODETEXT) on Windows, <c>wl-copy</c> on Linux/KDE (Wayland). Replaces the WinForms
/// <c>Clipboard.SetImage</c>/<c>Clipboard.SetText</c> calls (Form1 L911, L943).
/// </summary>
public interface IClipboardService
{
	/// <summary>Copies <paramref name="image"/> to the clipboard as a bitmap.</summary>
	void SetImage(SKBitmap image);

	/// <summary>Copies <paramref name="text"/> to the clipboard as plain text.</summary>
	void SetText(string text);
}
