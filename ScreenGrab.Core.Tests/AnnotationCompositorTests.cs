using ScreenGrab.Core;
using SkiaSharp;
using Xunit;

namespace ScreenGrab.Core.Tests;

/// <summary>Smoke-tests the SkiaSharp compositor: text measurement/wrapping and rendering.</summary>
public class AnnotationCompositorTests
{
	private static SKBitmap Blank(int w, int h)
	{
		var bmp = new SKBitmap(w, h);
		using var canvas = new SKCanvas(bmp);
		canvas.Clear(SKColors.White);
		return bmp;
	}

	[Fact]
	public void MeasureText_produces_bounds_anchored_at_origin()
	{
		var c = new AnnotationCompositor();
		var t = new TextAnnotation(30, 40, "hello", 16f);

		TextLayout layout = c.MeasureText(t, 400, 300);

		Assert.False(layout.IsEmpty);
		Assert.Equal(30, layout.Bounds.Left);
		Assert.Equal(40, layout.Bounds.Top);
		Assert.True(layout.Bounds.Width > 0 && layout.Bounds.Height > 0);
		Assert.Single(layout.Lines);
	}

	[Fact]
	public void MeasureText_wraps_long_text_at_the_right_edge()
	{
		var c = new AnnotationCompositor();
		// Anchored near the right edge so the available width forces wrapping.
		var t = new TextAnnotation(350, 10, "the quick brown fox jumps over the lazy dog", 16f);

		TextLayout layout = c.MeasureText(t, 400, 300);

		Assert.True(layout.Lines.Count > 1, "expected multiple wrapped lines near the right edge");
		Assert.True(layout.Bounds.Right <= 400);
	}

	[Fact]
	public void MeasureText_returns_empty_for_empty_text()
	{
		var c = new AnnotationCompositor();
		Assert.True(c.MeasureText(new TextAnnotation(0, 0, "", 16f), 100, 100).IsEmpty);
	}

	[Fact]
	public void Render_returns_a_copy_and_leaves_the_clean_bitmap_untouched()
	{
		var c = new AnnotationCompositor();
		using var clean = Blank(100, 100);
		var annotations = new IAnnotation[] { new RectAnnotation(10, 10, 50, 50) };

		using SKBitmap result = c.Render(clean, annotations);

		Assert.NotSame(clean, result);
		Assert.Equal(clean.Width, result.Width);
		Assert.Equal(clean.Height, result.Height);
		// The clean bitmap stays all-white; the highlight stroke lands somewhere on the result.
		Assert.Equal(SKColors.White, clean.GetPixel(35, 10));
	}

	[Fact]
	public void Render_can_be_reused_for_undo_by_dropping_the_last_annotation()
	{
		var c = new AnnotationCompositor();
		using var clean = Blank(100, 100);
		var r1 = new RectAnnotation(10, 10, 30, 30);
		var r2 = new RectAnnotation(50, 50, 30, 30);

		using SKBitmap withBoth = c.Render(clean, new IAnnotation[] { r1, r2 });
		using SKBitmap withoutLast = c.Render(clean, new IAnnotation[] { r1 });

		Assert.Equal(withBoth.Width, withoutLast.Width);
	}
}
