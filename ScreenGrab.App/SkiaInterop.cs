using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace ScreenGrab.App;

/// <summary>
/// Bridges the Core SkiaSharp compositor to Avalonia's display pipeline. The compositor burns
/// annotations into an <see cref="SKBitmap"/>; the editor shows it via an Avalonia
/// <see cref="WriteableBitmap"/>. Conversion only happens when the composite changes (a new
/// annotation, an undo/redo), not every frame, so a straight pixel copy is fine.
/// </summary>
internal static class SkiaInterop
{
	/// <summary>
	/// Copies an <see cref="SKBitmap"/> into a new Avalonia <see cref="WriteableBitmap"/>
	/// (BGRA8888, premultiplied). The source is left untouched; the caller owns both bitmaps.
	/// </summary>
	public static WriteableBitmap ToAvaloniaBitmap(SKBitmap source)
	{
		SKBitmap bmp = source;
		bool disposeTemp = false;

		// Avalonia's WriteableBitmap here is BGRA8888; convert if the source differs.
		if (bmp.ColorType != SKColorType.Bgra8888)
		{
			var converted = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
			source.CopyTo(converted, SKColorType.Bgra8888);
			bmp = converted;
			disposeTemp = true;
		}

		try
		{
			var writeable = new WriteableBitmap(
				new PixelSize(bmp.Width, bmp.Height),
				new Vector(96, 96),
				PixelFormat.Bgra8888,
				AlphaFormat.Premul);

			using (ILockedFramebuffer fb = writeable.Lock())
			{
				IntPtr srcPtr = bmp.GetPixels();
				IntPtr dstPtr = fb.Address;
				int srcStride = bmp.RowBytes;
				int dstStride = fb.RowBytes;

				if (srcStride == dstStride)
				{
					// Contiguous, identical layout: one copy through a managed staging buffer.
					int length = srcStride * bmp.Height;
					byte[] buffer = new byte[length];
					Marshal.Copy(srcPtr, buffer, 0, length);
					Marshal.Copy(buffer, 0, dstPtr, length);
				}
				else
				{
					// Different strides (padding): copy row by row.
					int rowBytes = Math.Min(srcStride, dstStride);
					byte[] row = new byte[rowBytes];
					for (int y = 0; y < bmp.Height; y++)
					{
						Marshal.Copy(IntPtr.Add(srcPtr, y * srcStride), row, 0, rowBytes);
						Marshal.Copy(row, 0, IntPtr.Add(dstPtr, y * dstStride), rowBytes);
					}
				}
			}

			return writeable;
		}
		finally
		{
			if (disposeTemp)
				bmp.Dispose();
		}
	}
}
