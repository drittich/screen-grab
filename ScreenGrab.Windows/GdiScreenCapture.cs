using System;
using System.Runtime.InteropServices;
using ScreenGrab.Core;
using SkiaSharp;

namespace ScreenGrab.Windows;

/// <summary>
/// Windows <see cref="IScreenCapture"/>: BitBlt the primary screen into a 32bpp DIB and copy the
/// pixels into an <see cref="SKBitmap"/>. This replaces the WinForms <c>Graphics.CopyFromScreen</c>
/// full-screen grab (Form1 L267-274) with self-contained user32/gdi32 P/Invoke — no System.Drawing.
/// </summary>
public sealed class GdiScreenCapture : IScreenCapture
{
	private const int SRCCOPY = 0x00CC0020;
	private const int SM_CXSCREEN = 0;
	private const int SM_CYSCREEN = 1;
	private const uint DIB_RGB_COLORS = 0;
	private const uint BI_RGB = 0;

	public SKBitmap CaptureFullScreen()
	{
		int width = GetSystemMetrics(SM_CXSCREEN);
		int height = GetSystemMetrics(SM_CYSCREEN);
		if (width <= 0 || height <= 0)
			throw new InvalidOperationException("Could not determine the primary screen size.");

		IntPtr screenDc = GetDC(IntPtr.Zero);
		if (screenDc == IntPtr.Zero)
			throw new InvalidOperationException("GetDC failed for the screen.");

		IntPtr memDc = IntPtr.Zero;
		IntPtr hBitmap = IntPtr.Zero;
		IntPtr oldBitmap = IntPtr.Zero;
		try
		{
			memDc = CreateCompatibleDC(screenDc);
			hBitmap = CreateCompatibleBitmap(screenDc, width, height);
			if (memDc == IntPtr.Zero || hBitmap == IntPtr.Zero)
				throw new InvalidOperationException("Failed to allocate a GDI capture bitmap.");

			oldBitmap = SelectObject(memDc, hBitmap);
			if (!BitBlt(memDc, 0, 0, width, height, screenDc, 0, 0, SRCCOPY))
				throw new InvalidOperationException("BitBlt of the screen failed.");

			// Top-down 32bpp BGRA (negative height). BitBlt leaves the alpha byte as 0, so force it
			// to 255 below and treat the bitmap as opaque.
			var header = new BITMAPINFOHEADER
			{
				biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
				biWidth = width,
				biHeight = -height,
				biPlanes = 1,
				biBitCount = 32,
				biCompression = BI_RGB,
			};

			int stride = width * 4;
			byte[] buffer = new byte[stride * height];
			int scanned = GetDIBits(memDc, hBitmap, 0, (uint)height, buffer, ref header, DIB_RGB_COLORS);
			if (scanned == 0)
				throw new InvalidOperationException("GetDIBits returned no scanlines.");

			for (int i = 3; i < buffer.Length; i += 4)
				buffer[i] = 0xFF;

			var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
			var bitmap = new SKBitmap(info);
			Marshal.Copy(buffer, 0, bitmap.GetPixels(), buffer.Length);
			return bitmap;
		}
		finally
		{
			if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
			if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
			if (memDc != IntPtr.Zero) DeleteDC(memDc);
			ReleaseDC(IntPtr.Zero, screenDc);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct BITMAPINFOHEADER
	{
		public uint biSize;
		public int biWidth;
		public int biHeight;
		public ushort biPlanes;
		public ushort biBitCount;
		public uint biCompression;
		public uint biSizeImage;
		public int biXPelsPerMeter;
		public int biYPelsPerMeter;
		public uint biClrUsed;
		public uint biClrImportant;
		// GetDIBits with a 32bpp BI_RGB header does not read a color table, so none is declared.
	}

	[DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
	[DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
	[DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
	[DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDc);
	[DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDc);
	[DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int width, int height);
	[DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
	[DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);
	[DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hDest, int x, int y, int w, int h, IntPtr hSrc, int sx, int sy, int rop);

	[DllImport("gdi32.dll")]
	private static extern int GetDIBits(IntPtr hDc, IntPtr hBitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
}
