using System;
using System.IO;
using SkiaSharp;

namespace ScreenGrab.Core;

/// <summary>
/// Encodes a captured/annotated bitmap to a timestamped PNG under <c>~/Downloads/ScreenGrab</c>.
/// The OS-agnostic port of <c>SaveScreenshot</c> (Form1 L921-957): the <c>UserProfile</c> +
/// <c>Downloads\ScreenGrab</c> path is already cross-platform, and Skia's PNG encoder replaces
/// <c>Bitmap.Save(..., ImageFormat.Png)</c>. Clipboard copy and the toast stay with the caller.
/// </summary>
public static class ScreenshotSaver
{
	/// <summary>
	/// Writes <paramref name="image"/> as a PNG and returns its full path. Creates the target
	/// directory if needed. Throws on encode/IO failure.
	/// </summary>
	public static string Save(SKBitmap image)
	{
		ArgumentNullException.ThrowIfNull(image);

		string dir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			"Downloads", "ScreenGrab");
		Directory.CreateDirectory(dir);

		string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
		string filePath = Path.Combine(dir, $"Screenshot_{timestamp}.png");

		using SKData data = image.Encode(SKEncodedImageFormat.Png, 100)
			?? throw new InvalidOperationException("Failed to PNG-encode the screenshot.");
		using FileStream stream = File.Create(filePath);
		data.SaveTo(stream);

		return filePath;
	}
}
