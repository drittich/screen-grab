using System;
using System.Runtime.InteropServices;
using System.Text;
using ScreenGrab.Core;
using SkiaSharp;

namespace ScreenGrab.Windows;

/// <summary>
/// Windows <see cref="IClipboardService"/> via the Win32 clipboard API — the self-contained,
/// no-System.Drawing port of the WinForms <c>Clipboard.SetImage</c>/<c>Clipboard.SetText</c> calls.
/// Images go on as CF_DIB (a bottom-up 32bpp BGRA device-independent bitmap); text as CF_UNICODETEXT.
/// </summary>
public sealed class WindowsClipboardService : IClipboardService
{
	private const uint CF_UNICODETEXT = 13;
	private const uint CF_DIB = 8;
	private const uint GMEM_MOVEABLE = 0x0002;
	private const uint BI_RGB = 0;

	public void SetImage(SKBitmap image)
	{
		ArgumentNullException.ThrowIfNull(image);
		byte[] dib = BuildDib(image);
		SetClipboard(CF_DIB, dib);
	}

	public void SetText(string text)
	{
		text ??= string.Empty;
		// CF_UNICODETEXT: null-terminated UTF-16LE.
		byte[] bytes = Encoding.Unicode.GetBytes(text + '\0');
		SetClipboard(CF_UNICODETEXT, bytes);
	}

	/// <summary>Builds a packed DIB (BITMAPINFOHEADER + bottom-up BGRA pixels) from the bitmap.</summary>
	private static byte[] BuildDib(SKBitmap image)
	{
		int w = image.Width;
		int h = image.Height;
		int stride = w * 4;
		const int headerSize = 40; // sizeof(BITMAPINFOHEADER)

		byte[] dib = new byte[headerSize + stride * h];

		// BITMAPINFOHEADER — 32bpp BI_RGB, positive height = bottom-up.
		BitConverter.GetBytes((uint)headerSize).CopyTo(dib, 0);
		BitConverter.GetBytes(w).CopyTo(dib, 4);
		BitConverter.GetBytes(h).CopyTo(dib, 8);
		BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);        // biPlanes
		BitConverter.GetBytes((ushort)32).CopyTo(dib, 14);       // biBitCount
		BitConverter.GetBytes(BI_RGB).CopyTo(dib, 16);           // biCompression
		BitConverter.GetBytes((uint)(stride * h)).CopyTo(dib, 20); // biSizeImage

		// Source pixels: read as BGRA8888 top-down, flip vertically into the DIB, force alpha opaque.
		using SKBitmap bgra = EnsureBgra(image);
		ReadOnlySpan<byte> src = bgra.GetPixelSpan();
		int dstBase = headerSize;
		for (int row = 0; row < h; row++)
		{
			int srcRow = row * stride;
			int dstRow = dstBase + (h - 1 - row) * stride; // bottom-up
			for (int x = 0; x < stride; x += 4)
			{
				dib[dstRow + x + 0] = src[srcRow + x + 0]; // B
				dib[dstRow + x + 1] = src[srcRow + x + 1]; // G
				dib[dstRow + x + 2] = src[srcRow + x + 2]; // R
				dib[dstRow + x + 3] = 0xFF;                // A (opaque; CF_DIB alpha is unreliable)
			}
		}

		return dib;
	}

	private static SKBitmap EnsureBgra(SKBitmap image)
	{
		if (image.ColorType == SKColorType.Bgra8888)
			return image.Copy() ?? throw new InvalidOperationException("Failed to copy bitmap for clipboard.");

		var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
		var converted = new SKBitmap(info);
		if (!image.CopyTo(converted, SKColorType.Bgra8888))
		{
			converted.Dispose();
			throw new InvalidOperationException("Failed to convert bitmap to BGRA for clipboard.");
		}
		return converted;
	}

	private static void SetClipboard(uint format, byte[] data)
	{
		if (!OpenClipboard(IntPtr.Zero))
			throw new InvalidOperationException("Could not open the clipboard.");
		try
		{
			EmptyClipboard();

			IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
			if (hGlobal == IntPtr.Zero)
				throw new InvalidOperationException("Clipboard allocation failed.");

			IntPtr ptr = GlobalLock(hGlobal);
			if (ptr == IntPtr.Zero)
			{
				GlobalFree(hGlobal);
				throw new InvalidOperationException("Clipboard lock failed.");
			}
			try
			{
				Marshal.Copy(data, 0, ptr, data.Length);
			}
			finally
			{
				GlobalUnlock(hGlobal);
			}

			// On success the system owns hGlobal; do not free it.
			if (SetClipboardData(format, hGlobal) == IntPtr.Zero)
			{
				GlobalFree(hGlobal);
				throw new InvalidOperationException("SetClipboardData failed.");
			}
		}
		finally
		{
			CloseClipboard();
		}
	}

	[DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
	[DllImport("user32.dll")] private static extern bool CloseClipboard();
	[DllImport("user32.dll")] private static extern bool EmptyClipboard();
	[DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
	[DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
	[DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);
	[DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
	[DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
}
