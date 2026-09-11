using System.Text;
using SkiaSharp;

namespace ScreenGrab.Core;

/// <summary>
/// Burns annotations into a captured image using SkiaSharp, replacing the WinForms GDI+/TextRenderer
/// path. Mirrors the original drawing rules: red rounded-rectangle highlights and white-on-blue text
/// boxes that auto-size to their content and wrap at the image's right edge. Rendering always starts
/// from a clean copy of the capture, so undo/redo just re-projects and re-renders.
/// </summary>
public sealed class AnnotationCompositor
{
	// Matches the WinForms constants.
	public const int TextPadding = 4;
	public const float DefaultFontSize = 16f;
	public const float MinFontSize = 8f;
	public const float MaxFontSize = 72f;
	private const int HighlightCornerRadius = 20; // GDI ellipse diameter; Skia radius is half this.

	private static readonly SKColor TextBgColor = new(0x15, 0x65, 0xC0);
	private static readonly SKColor HighlightColor = SKColors.Red;

	private readonly float _highlightThickness;
	private readonly SKTypeface _typeface;

	/// <param name="scaleFactor">DPI scale (DeviceDpi / 96). Drives high-DPI stroke thickness.</param>
	public AnnotationCompositor(float scaleFactor = 1f)
	{
		bool isHighDpi = scaleFactor > 1f;
		_highlightThickness = isHighDpi ? 5f : 2f;
		_typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
	}

	/// <summary>
	/// Returns a new composited bitmap: a copy of <paramref name="clean"/> with every live annotation
	/// drawn in order. Pass <paramref name="exclude"/> to skip one annotation (used while re-editing a
	/// text box so the editor isn't drawn over a stale burned-in copy). The caller owns the result.
	/// </summary>
	public SKBitmap Render(SKBitmap clean, IEnumerable<IAnnotation> annotations, IAnnotation? exclude = null)
	{
		var result = clean.Copy();
		using var canvas = new SKCanvas(result);
		foreach (IAnnotation annotation in annotations)
		{
			if (exclude != null && ReferenceEquals(annotation, exclude))
				continue;
			DrawAnnotation(canvas, annotation, result.Width, result.Height);
		}
		return result;
	}

	/// <summary>Draws a single annotation onto an existing canvas.</summary>
	public void DrawAnnotation(SKCanvas canvas, IAnnotation annotation, int imageWidth, int imageHeight)
	{
		switch (annotation)
		{
			case RectAnnotation r:
				DrawRoundedRectangle(canvas, new SKRect(r.X, r.Y, r.X + r.Width, r.Y + r.Height));
				break;
			case TextAnnotation t:
				DrawTextAnnotation(canvas, t, imageWidth, imageHeight);
				break;
		}
	}

	private void DrawRoundedRectangle(SKCanvas canvas, SKRect rect)
	{
		using var paint = new SKPaint
		{
			Style = SKPaintStyle.Stroke,
			Color = HighlightColor,
			StrokeWidth = _highlightThickness,
			IsAntialias = true,
		};
		float radius = HighlightCornerRadius / 2f; // GDI used the diameter; Skia wants the radius.
		canvas.DrawRoundRect(rect, radius, radius, paint);
	}

	private void DrawTextAnnotation(SKCanvas canvas, TextAnnotation annotation, int imageWidth, int imageHeight)
	{
		TextLayout layout = MeasureText(annotation, imageWidth, imageHeight);
		if (layout.IsEmpty) return;

		SKRect bg = layout.Bounds;
		using var font = new SKFont(_typeface, annotation.FontSize);
		using var bgPaint = new SKPaint { Color = TextBgColor, Style = SKPaintStyle.Fill, IsAntialias = false };
		using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };

		canvas.Save();
		canvas.ClipRect(bg);
		canvas.DrawRect(bg, bgPaint);

		// Baseline of the first line: top padding minus the (negative) ascent.
		float textLeft = bg.Left + TextPadding;
		float baseline = bg.Top + TextPadding - font.Metrics.Ascent;
		float lineHeight = font.Spacing;
		foreach (string line in layout.Lines)
		{
			canvas.DrawText(line, textLeft, baseline, SKTextAlign.Left, font, textPaint);
			baseline += lineHeight;
		}
		canvas.Restore();
	}

	/// <summary>
	/// Computes the rendered background rectangle (image coordinates) and wrapped lines for a text
	/// annotation. Used for drawing and hit-testing so they stay in sync. Mirrors the WinForms
	/// GetTextAnnotationBounds/ResizeTextBox logic: auto-size to content width, cap at the available
	/// width to the image's right edge, wrap only when needed, and clamp height to the image bottom.
	/// </summary>
	public TextLayout MeasureText(TextAnnotation annotation, int imageWidth, int imageHeight)
	{
		int maxWidth = imageWidth - annotation.X;
		if (maxWidth < 1 || string.IsNullOrEmpty(annotation.Text))
			return TextLayout.Empty;

		using var font = new SKFont(_typeface, annotation.FontSize);
		int minWidth = (int)annotation.FontSize + TextPadding * 2;

		// Natural (unwrapped) content width = the widest hard line.
		string[] hardLines = annotation.Text.Split('\n');
		float naturalWidth = 0f;
		foreach (string hard in hardLines)
			naturalWidth = Math.Max(naturalWidth, font.MeasureText(hard));

		// +1px guard so the resolved width is never a hair narrower than the natural width
		// (which would force an unwanted wrap).
		int boxWidth = Math.Min((int)Math.Ceiling(naturalWidth) + TextPadding * 2 + 1, maxWidth);
		if (boxWidth < minWidth) boxWidth = minWidth;

		float wrapWidth = boxWidth - TextPadding * 2;
		var lines = new List<string>();
		foreach (string hard in hardLines)
			WrapLine(hard, font, wrapWidth, lines);

		int textHeight = (int)Math.Ceiling(lines.Count * font.Spacing);
		int boxHeight = textHeight + TextPadding * 2;

		// Clamp to image bounds.
		int maxHeight = imageHeight - annotation.Y;
		if (boxHeight > maxHeight) boxHeight = maxHeight;

		var bounds = new SKRectI(annotation.X, annotation.Y, annotation.X + boxWidth, annotation.Y + boxHeight);
		return new TextLayout(bounds, lines);
	}

	// Greedy word-wrap at wrapWidth; a single word wider than the box is left un-broken (matches
	// TextRenderer's WordBreak behaviour of not splitting mid-word).
	private static void WrapLine(string line, SKFont font, float wrapWidth, List<string> output)
	{
		if (line.Length == 0)
		{
			output.Add(string.Empty);
			return;
		}

		string[] words = line.Split(' ');
		var current = new StringBuilder();
		foreach (string word in words)
		{
			if (current.Length == 0)
			{
				current.Append(word);
				continue;
			}
			string candidate = current + " " + word;
			if (font.MeasureText(candidate) <= wrapWidth)
			{
				current.Clear();
				current.Append(candidate);
			}
			else
			{
				output.Add(current.ToString());
				current.Clear();
				current.Append(word);
			}
		}
		output.Add(current.ToString());
	}
}

/// <summary>The measured background rectangle and wrapped visual lines for a text annotation.</summary>
public readonly record struct TextLayout(SKRectI Bounds, IReadOnlyList<string> Lines)
{
	public static TextLayout Empty => new(SKRectI.Empty, Array.Empty<string>());
	public bool IsEmpty => Bounds.IsEmpty;
}
