using System;
using ScreenGrab.Core;
using SkiaSharp;
using Xunit;

namespace ScreenGrab.Core.Tests;

/// <summary>Covers the capture → crop step used by the selection overlay before the editor opens.</summary>
public class ImageOpsTests
{
	// A source where each pixel encodes its (x, y) so crops are verifiable: R = x, G = y.
	private static SKBitmap Coded(int w, int h)
	{
		var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				bmp.SetPixel(x, y, new SKColor((byte)x, (byte)y, 0, 255));
		return bmp;
	}

	[Fact]
	public void Crop_returns_region_sized_bitmap_from_the_right_offset()
	{
		using SKBitmap src = Coded(100, 80);

		using SKBitmap cropped = ImageOps.Crop(src, new SKRectI(10, 20, 40, 60));

		Assert.Equal(30, cropped.Width);
		Assert.Equal(40, cropped.Height);
		// Top-left of the crop is source (10, 20).
		SKColor c = cropped.GetPixel(0, 0);
		Assert.Equal(10, c.Red);
		Assert.Equal(20, c.Green);
		// Bottom-right pixel maps to source (39, 59).
		SKColor br = cropped.GetPixel(29, 39);
		Assert.Equal(39, br.Red);
		Assert.Equal(59, br.Green);
	}

	[Fact]
	public void Crop_clamps_a_region_that_overruns_the_source()
	{
		using SKBitmap src = Coded(50, 50);

		using SKBitmap cropped = ImageOps.Crop(src, new SKRectI(40, 40, 200, 200));

		Assert.Equal(10, cropped.Width);
		Assert.Equal(10, cropped.Height);
	}

	[Fact]
	public void Crop_throws_when_region_misses_the_source()
	{
		using SKBitmap src = Coded(20, 20);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => ImageOps.Crop(src, new SKRectI(100, 100, 120, 120)));
	}
}
