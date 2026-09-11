using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ScreenGrab.Core;
using SkiaSharp;

namespace ScreenGrab.App;

/// <summary>
/// The Avalonia annotation editor — the port of the WinForms <c>Form1</c> canvas. It shows a
/// captured image and lets the user draw red rounded-rectangle highlights (click-drag) and
/// movable white-on-blue text boxes (double-click), with Ctrl+Z / Ctrl+Y undo/redo. All drawing
/// geometry comes from <see cref="AnnotationCompositor"/> so the live editor and the burned-in
/// output agree.
///
/// Coordinates: the overlay <see cref="Canvas"/> is laid out 1:1 with image pixels (the image is
/// drawn at its natural size at 0,0), so pointer positions relative to the overlay are image
/// coordinates. There is no <c>headerPanel.Height</c> offset — the toolbar lives in a separate
/// dock region, unlike the WinForms form.
/// </summary>
public partial class EditorWindow : Window
{
	private const int MinRectDimension = 10; // matches Form1's minimum rubber-band size
	private const double TextDragThreshold = 4; // pixels of movement before a text-box drag starts

	private readonly SKBitmap _clean;               // pristine capture; never mutated
	private readonly AnnotationHistory _history = new();
	private readonly AnnotationCompositor _compositor;
	private readonly IClipboardService _clipboard = PlatformServices.CreateClipboardService();
	private readonly float _scaleFactor;

	private WriteableBitmap? _display;              // current Avalonia frame (disposed on replace)

	// Rubber-band rectangle drag state (image coordinates).
	private Point? _rectDragStart;
	private Rectangle? _rubberBand;

	// Active text box state.
	private TextBox? _activeTextBox;
	private int _textOriginX;
	private int _textOriginY;
	private float _activeFontSize;
	private TextAnnotation? _editingOriginal;

	// Text-box move-drag state (overlay coordinates).
	private Point? _tbDragStart;
	private Point _tbGrabOffset;
	private bool _tbDragging;

	/// <summary>Design-time constructor (required by the Avalonia XAML tooling).</summary>
	public EditorWindow() : this(CreateBlank(800, 600)) { }

	/// <summary>Opens the editor on <paramref name="capture"/>. The window takes ownership of the
	/// bitmap and disposes it when closed.</summary>
	public EditorWindow(SKBitmap capture)
	{
		_clean = capture ?? throw new ArgumentNullException(nameof(capture));
		InitializeComponent();

		// RenderScaling is only reliable once the window has a visual root; default to 1 here and
		// refine on open. It drives the compositor's high-DPI stroke thickness and font sizing.
		_scaleFactor = 1f;
		_compositor = new AnnotationCompositor(_scaleFactor);

		// Size the overlay to the image so the ScrollViewer knows the content bounds.
		Overlay.Width = _clean.Width;
		Overlay.Height = _clean.Height;

		// Keep the window from opening larger than the work area for big captures.
		Width = Math.Min(_clean.Width + 40, 1600);
		Height = Math.Min(_clean.Height + 100, 1000);

		Overlay.PointerPressed += OnOverlayPointerPressed;
		Overlay.PointerMoved += OnOverlayPointerMoved;
		Overlay.PointerReleased += OnOverlayPointerReleased;

		RerenderComposite();
	}

	private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

	private static SKBitmap CreateBlank(int w, int h)
	{
		var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
		using var canvas = new SKCanvas(bmp);
		canvas.Clear(SKColors.White);
		return bmp;
	}

	// ---- Rendering ---------------------------------------------------------------------------

	/// <summary>Re-projects the history and repaints the image. Pass <paramref name="exclude"/> to
	/// hide one annotation (used while its text box is being re-edited).</summary>
	private void RerenderComposite(IAnnotation? exclude = null)
	{
		var live = _history.ProjectAnnotations();
		using SKBitmap composite = _compositor.Render(_clean, live, exclude);
		WriteableBitmap next = SkiaInterop.ToAvaloniaBitmap(composite);

		ImageView.Source = next;
		ImageView.Width = composite.Width;
		ImageView.Height = composite.Height;

		_display?.Dispose();
		_display = next;
	}

	// ---- Keyboard ----------------------------------------------------------------------------

	protected override void OnKeyDown(KeyEventArgs e)
	{
		bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

		if (e.Key == Key.Escape)
		{
			if (_activeTextBox != null)
				CancelText();
			else
				Close();
			e.Handled = true;
			return;
		}

		if (_activeTextBox == null && ctrl && e.Key == Key.Z && _history.CanUndo)
		{
			_history.Undo();
			RerenderComposite();
			e.Handled = true;
			return;
		}

		if (_activeTextBox == null && ctrl && e.Key == Key.Y && _history.CanRedo)
		{
			_history.Redo();
			RerenderComposite();
			e.Handled = true;
			return;
		}

		base.OnKeyDown(e);
	}

	// ---- Rubber-band rectangle ---------------------------------------------------------------

	private void OnOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		var props = e.GetCurrentPoint(Overlay).Properties;
		if (!props.IsLeftButtonPressed) return;

		// A double-click starts/edits a text box instead of a rectangle.
		if (e.ClickCount == 2)
		{
			_rectDragStart = null;
			RemoveRubberBand();
			HandleDoubleClick(e.GetPosition(Overlay));
			e.Handled = true;
			return;
		}

		if (_activeTextBox != null) return;

		Point p = ClampToImage(e.GetPosition(Overlay));
		_rectDragStart = p;
		RemoveRubberBand();
	}

	private void OnOverlayPointerMoved(object? sender, PointerEventArgs e)
	{
		if (_rectDragStart == null || _activeTextBox != null) return;
		if (!e.GetCurrentPoint(Overlay).Properties.IsLeftButtonPressed) return;

		Point current = ClampToImage(e.GetPosition(Overlay));
		Point start = _rectDragStart.Value;

		double x = Math.Min(start.X, current.X);
		double y = Math.Min(start.Y, current.Y);
		double w = Math.Abs(start.X - current.X);
		double h = Math.Abs(start.Y - current.Y);

		EnsureRubberBand();
		Canvas.SetLeft(_rubberBand!, x);
		Canvas.SetTop(_rubberBand!, y);
		_rubberBand!.Width = w;
		_rubberBand!.Height = h;
		_rubberBand!.IsVisible = true;
	}

	private void OnOverlayPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (_rectDragStart == null) return;

		if (_rubberBand != null && _rubberBand.IsVisible)
		{
			int w = (int)Math.Round(_rubberBand.Width);
			int h = (int)Math.Round(_rubberBand.Height);
			if (w >= MinRectDimension && h >= MinRectDimension)
			{
				int x = (int)Math.Round(Canvas.GetLeft(_rubberBand));
				int y = (int)Math.Round(Canvas.GetTop(_rubberBand));
				_history.Add(new AddRectOp(new RectAnnotation(x, y, w, h)));
				RerenderComposite();
			}
		}

		_rectDragStart = null;
		RemoveRubberBand();
	}

	private void EnsureRubberBand()
	{
		if (_rubberBand != null) return;
		_rubberBand = new Rectangle
		{
			Stroke = Brushes.Red,
			StrokeThickness = 2,
			RadiusX = 10,
			RadiusY = 10,
			Fill = Brushes.Transparent,
			IsHitTestVisible = false,
			IsVisible = false,
		};
		Overlay.Children.Add(_rubberBand);
	}

	private void RemoveRubberBand()
	{
		if (_rubberBand == null) return;
		Overlay.Children.Remove(_rubberBand);
		_rubberBand = null;
	}

	private Point ClampToImage(Point p)
		=> new(Math.Clamp(p.X, 0, _clean.Width), Math.Clamp(p.Y, 0, _clean.Height));

	// ---- Text annotations --------------------------------------------------------------------

	private void HandleDoubleClick(Point overlayPoint)
	{
		if (_activeTextBox != null) return;

		int imgX = (int)overlayPoint.X;
		int imgY = (int)overlayPoint.Y;
		if (imgX < 0 || imgY < 0 || imgX >= _clean.Width || imgY >= _clean.Height) return;

		TextAnnotation? hit = FindTextAnnotationAt(imgX, imgY);
		if (hit != null)
		{
			EditExistingTextAnnotation(hit);
			return;
		}

		StartTextBox(imgX, imgY);
	}

	// Topmost (last-drawn) live text annotation whose rendered box contains the point.
	private TextAnnotation? FindTextAnnotationAt(int imgX, int imgY)
	{
		var live = _history.ProjectAnnotations();
		for (int i = live.Count - 1; i >= 0; i--)
		{
			if (live[i] is TextAnnotation t)
			{
				TextLayout layout = _compositor.MeasureText(t, _clean.Width, _clean.Height);
				if (!layout.IsEmpty && layout.Bounds.Contains(imgX, imgY))
					return t;
			}
		}
		return null;
	}

	// Re-open an existing annotation for editing. History is untouched here; the original is hidden
	// from the canvas while editing and the change is recorded as one EditTextOp/DeleteTextOp on
	// commit (or discarded on cancel), keeping undo/redo clean.
	private void EditExistingTextAnnotation(TextAnnotation annotation)
	{
		_editingOriginal = annotation;
		RerenderComposite(exclude: annotation);
		StartTextBox(annotation.X, annotation.Y, annotation.Text, annotation.FontSize);
	}

	private void StartTextBox(int originX, int originY, string? seedText = null, float? seedFontSize = null)
	{
		_textOriginX = originX;
		_textOriginY = originY;
		_activeFontSize = seedFontSize ?? AnnotationCompositor.DefaultFontSize * _scaleFactor;

		var tb = new TextBox
		{
			Text = seedText ?? string.Empty,
			FontFamily = new FontFamily("Segoe UI"),
			FontSize = _activeFontSize,
			Foreground = Brushes.White,
			Background = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),
			BorderThickness = new Thickness(0),
			Padding = new Thickness(AnnotationCompositor.TextPadding),
			AcceptsReturn = true,
			TextWrapping = TextWrapping.Wrap,
			CaretBrush = Brushes.White,
			MinWidth = 0,
			MinHeight = 0,
		};

		tb.TextChanged += (_, _) => ResizeTextBox();
		tb.KeyDown += OnTextBoxKeyDown;
		tb.LostFocus += OnTextBoxLostFocus;
		tb.PointerPressed += OnTextBoxPointerPressed;
		tb.PointerMoved += OnTextBoxPointerMoved;
		tb.PointerReleased += OnTextBoxPointerReleased;
		tb.PointerWheelChanged += OnTextBoxPointerWheel;

		_activeTextBox = tb;
		Canvas.SetLeft(tb, originX);
		Canvas.SetTop(tb, originY);
		Overlay.Children.Add(tb);
		ResizeTextBox();

		tb.Focus();
		tb.CaretIndex = tb.Text?.Length ?? 0;
	}

	// Auto-size the box to its content, capped at the image's right edge and wrapping only when
	// needed. Reuses the compositor's MeasureText so the live box matches the burned-in result.
	private void ResizeTextBox()
	{
		if (_activeTextBox == null) return;

		string text = _activeTextBox.Text ?? string.Empty;
		string measured = text.Length > 0 ? text : " ";
		var probe = new TextAnnotation(_textOriginX, _textOriginY, measured, _activeFontSize);
		TextLayout layout = _compositor.MeasureText(probe, _clean.Width, _clean.Height);

		if (layout.IsEmpty)
		{
			int min = (int)_activeFontSize + AnnotationCompositor.TextPadding * 2;
			_activeTextBox.Width = min;
			_activeTextBox.Height = min;
			return;
		}

		_activeTextBox.Width = layout.Bounds.Width;
		_activeTextBox.Height = layout.Bounds.Height;
	}

	private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
		{
			e.Handled = true;
			CommitText();
		}
		// Escape bubbles to the window's OnKeyDown, which routes it to CancelText.
	}

	private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
	{
		if (_activeTextBox != null)
			CommitText();
	}

	// Drag anywhere on the box (past a small threshold) to move it. Works in overlay coordinates,
	// with a fixed grab offset so the box tracks the cursor without jitter.
	private void OnTextBoxPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (_activeTextBox == null) return;
		if (!e.GetCurrentPoint(Overlay).Properties.IsLeftButtonPressed) return;

		Point pointer = e.GetPosition(Overlay);
		_tbGrabOffset = new Point(pointer.X - _textOriginX, pointer.Y - _textOriginY);
		_tbDragStart = pointer;
		_tbDragging = false;
	}

	private void OnTextBoxPointerMoved(object? sender, PointerEventArgs e)
	{
		if (_activeTextBox == null || _tbDragStart == null) return;
		if (!e.GetCurrentPoint(Overlay).Properties.IsLeftButtonPressed) return;

		Point pointer = e.GetPosition(Overlay);
		double dx = pointer.X - _tbDragStart.Value.X;
		double dy = pointer.Y - _tbDragStart.Value.Y;

		if (!_tbDragging && (Math.Abs(dx) > TextDragThreshold || Math.Abs(dy) > TextDragThreshold))
			_tbDragging = true;

		if (!_tbDragging) return;

		// Clamp the top-left to the image so the box can sit right at the edge; ResizeTextBox then
		// shrinks the wrap width as it nears the right/bottom.
		int newX = (int)Math.Clamp(pointer.X - _tbGrabOffset.X, 0, _clean.Width - 1);
		int newY = (int)Math.Clamp(pointer.Y - _tbGrabOffset.Y, 0, _clean.Height - 1);

		_textOriginX = newX;
		_textOriginY = newY;
		Canvas.SetLeft(_activeTextBox, newX);
		Canvas.SetTop(_activeTextBox, newY);
		ResizeTextBox();
		e.Handled = true;
	}

	private void OnTextBoxPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		bool wasDragging = _tbDragging;
		_tbDragStart = null;
		_tbDragging = false;
		if (wasDragging)
		{
			ResizeTextBox();
			e.Handled = true;
		}
	}

	private void OnTextBoxPointerWheel(object? sender, PointerWheelEventArgs e)
	{
		if (_activeTextBox == null) return;

		float delta = e.Delta.Y > 0 ? 1f : -1f;
		float newSize = Math.Clamp(
			_activeFontSize + delta,
			AnnotationCompositor.MinFontSize * _scaleFactor,
			AnnotationCompositor.MaxFontSize * _scaleFactor);
		if (Math.Abs(newSize - _activeFontSize) < float.Epsilon) return;

		_activeFontSize = newSize;
		_activeTextBox.FontSize = newSize;
		ResizeTextBox();
		e.Handled = true;
	}

	private void CommitText()
	{
		if (_activeTextBox == null) return;

		TextBox tb = _activeTextBox;
		_activeTextBox = null;               // clear first so LostFocus can't re-enter
		TextAnnotation? original = _editingOriginal;
		_editingOriginal = null;

		string trimmed = (tb.Text ?? string.Empty).Trim();

		if (original != null)
		{
			if (trimmed.Length == 0)
			{
				// Cleared while editing -> delete the original.
				_history.Add(new DeleteTextOp(original));
			}
			else
			{
				var edited = new TextAnnotation(_textOriginX, _textOriginY, tb.Text!, _activeFontSize);
				if (edited != original)
					_history.Add(new EditTextOp(original, edited));
			}
			RerenderComposite(); // canvas was drawn excluding the original; repaint in full
		}
		else if (trimmed.Length > 0)
		{
			var annotation = new TextAnnotation(_textOriginX, _textOriginY, tb.Text!, _activeFontSize);
			_history.Add(new AddTextOp(annotation));
			RerenderComposite();
		}

		RemoveTextBox(tb);
	}

	private void CancelText()
	{
		if (_activeTextBox == null) return;
		TextBox tb = _activeTextBox;
		_activeTextBox = null;
		bool wasEditing = _editingOriginal != null;
		_editingOriginal = null;
		RemoveTextBox(tb);

		// While editing, the canvas had the original excluded; restore it.
		if (wasEditing)
			RerenderComposite();
	}

	private void RemoveTextBox(TextBox tb)
	{
		tb.KeyDown -= OnTextBoxKeyDown;
		tb.LostFocus -= OnTextBoxLostFocus;
		tb.PointerPressed -= OnTextBoxPointerPressed;
		tb.PointerMoved -= OnTextBoxPointerMoved;
		tb.PointerReleased -= OnTextBoxPointerReleased;
		tb.PointerWheelChanged -= OnTextBoxPointerWheel;

		Overlay.Children.Remove(tb);
		_tbDragStart = null;
		_tbDragging = false;
		Focus(); // return focus so keyboard shortcuts keep working
	}

	// ---- Toolbar (Copy / Save / Save & Copy Path) --------------------------------------------

	/// <summary>Composites the current annotations onto the clean capture. Caller disposes.</summary>
	private SKBitmap BuildComposite()
	{
		CommitPendingText();
		return _compositor.Render(_clean, _history.ProjectAnnotations());
	}

	// Commit any live text box so it is burned into the copied/saved image (mirrors the WinForms
	// flow where committing happened before Copy/Save ran).
	private void CommitPendingText()
	{
		if (_activeTextBox != null)
			CommitText();
	}

	// Copy the annotated image to the clipboard, then close — the port of CopyButton_Click
	// (Form1 L907) whose Hide() becomes Close() for this per-capture window.
	private void OnCopyClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			using SKBitmap composite = BuildComposite();
			_clipboard.SetImage(composite);
			Close();
		}
		catch (Exception ex)
		{
			ToastWindow.Show($"Failed to copy image: {ex.Message}");
		}
	}

	private void OnSaveClick(object? sender, RoutedEventArgs e) => SaveScreenshot(copyPath: false);

	private void OnSavePathClick(object? sender, RoutedEventArgs e) => SaveScreenshot(copyPath: true);

	// The port of SaveScreenshot (Form1 L921): encode a PNG to ~/Downloads/ScreenGrab, optionally
	// copy the path to the clipboard, toast the result, then close.
	private void SaveScreenshot(bool copyPath)
	{
		try
		{
			string filePath;
			using (SKBitmap composite = BuildComposite())
				filePath = ScreenshotSaver.Save(composite);

			if (copyPath)
			{
				_clipboard.SetText(filePath);
				ToastWindow.Show($"Image saved to {filePath}\nPath copied to clipboard");
			}
			else
			{
				ToastWindow.Show($"Image saved to {filePath}");
			}

			Close();
		}
		catch (Exception ex)
		{
			ToastWindow.Show($"Failed to save image: {ex.Message}");
		}
	}

	// ---- Lifetime ----------------------------------------------------------------------------

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		_display?.Dispose();
		_clean.Dispose();
	}
}
