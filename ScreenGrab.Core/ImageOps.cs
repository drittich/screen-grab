using System;
using SkiaSharp;

namespace ScreenGrab.Core;

/// <summary>Toolkit-agnostic bitmap operations shared by the capture → crop → editor flow.</summary>
public static class ImageOps
{
	/// <summary>
	/// Crops <paramref name="region"/> out of <paramref name="source"/> into a new bitmap. The
	/// region is clamped to the source bounds; the port of <c>CaptureSelectedRegion</c>'s
	/// "draw the selected sub-rectangle into a fresh bitmap" step (was GDI+ <c>DrawImage</c>).
	/// </summary>
	public static SKBitmap Crop(SKBitmap source, SKRectI region)
	{
		if (source is null) throw new ArgumentNullException(nameof(source));

		SKRectI bounds = new(0, 0, source.Width, source.Height);
		SKRectI clamped = SKRectI.Intersect(region, bounds);
		if (clamped.Width <= 0 || clamped.Height <= 0)
			throw new ArgumentOutOfRangeException(nameof(region), "Selection does not overlap the image.");

		var dst = new SKBitmap(new SKImageInfo(clamped.Width, clamped.Height, source.ColorType, source.AlphaType));
		using var canvas = new SKCanvas(dst);
		var srcRect = new SKRect(clamped.Left, clamped.Top, clamped.Right, clamped.Bottom);
		var dstRect = new SKRect(0, 0, clamped.Width, clamped.Height);
		canvas.DrawBitmap(source, srcRect, dstRect);
		return dst;
	}
}
