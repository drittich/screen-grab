using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace ScreenGrab.App;

/// <summary>
/// Fullscreen borderless region picker — the Avalonia port of the WinForms <c>SelectionForm</c>
/// (Form1 L1275). It shows the captured screen dimmed, lets the user rubber-band a rectangle, and
/// resolves <see cref="SelectAsync"/> with the chosen region in <em>source-image pixels</em> (or
/// <c>null</c> if Escape / a zero-size drag cancels). The caller crops that region for the editor.
///
/// Coordinates: the backdrop <see cref="Image"/> is stretched to fill the window, so the overlay's
/// DIP coordinates scale linearly onto the source pixels. The scale (source px per overlay DIP) is
/// applied once on release, which keeps the picker correct under Wayland/HiDPI without positioning
/// the window itself (which Wayland forbids anyway).
/// </summary>
public partial class SelectionWindow : Window
{
	private readonly SKBitmap _full;
	private readonly int _srcWidth;
	private readonly int _srcHeight;
	private readonly TaskCompletionSource<SKRectI?> _result = new();

	private Point? _dragStart;   // overlay (DIP) coordinates
	private bool _completed;

	/// <summary>Design-time constructor (required by the Avalonia XAML tooling).</summary>
	public SelectionWindow() : this(CreateBlank()) { }

	private WriteableBitmap? _backdrop;

	/// <summary>Opens the picker over <paramref name="fullScreen"/>. The caller retains ownership of
	/// the bitmap (it is needed to crop the result); the window only reads it to build its display
	/// copy and to size the coordinate mapping.</summary>
	public SelectionWindow(SKBitmap fullScreen)
	{
		_full = fullScreen ?? throw new ArgumentNullException(nameof(fullScreen));
		_srcWidth = _full.Width;
		_srcHeight = _full.Height;

		InitializeComponent();

		_backdrop = SkiaInterop.ToAvaloniaBitmap(_full);
		Backdrop.Source = _backdrop;
		Cursor = new Cursor(StandardCursorType.Cross);

		Overlay.PointerPressed += OnPointerPressed;
		Overlay.PointerMoved += OnPointerMoved;
		Overlay.PointerReleased += OnPointerReleased;
	}

	private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

	private static SKBitmap CreateBlank()
	{
		var bmp = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
		using var canvas = new SKCanvas(bmp);
		canvas.Clear(SKColors.Black);
		return bmp;
	}

	/// <summary>Shows the picker and completes with the selected region (source pixels) or
	/// <c>null</c> if cancelled.</summary>
	public Task<SKRectI?> SelectAsync()
	{
		Show();
		Activate();
		return _result.Task;
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			e.Handled = true;
			Complete(null);
			return;
		}
		base.OnKeyDown(e);
	}

	private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(Overlay).Properties.IsLeftButtonPressed) return;
		_dragStart = e.GetPosition(Overlay);
		Band.IsVisible = false;
	}

	private void OnPointerMoved(object? sender, PointerEventArgs e)
	{
		if (_dragStart == null) return;
		if (!e.GetCurrentPoint(Overlay).Properties.IsLeftButtonPressed) return;

		Point start = _dragStart.Value;
		Point current = e.GetPosition(Overlay);

		double x = Math.Min(start.X, current.X);
		double y = Math.Min(start.Y, current.Y);
		double w = Math.Abs(start.X - current.X);
		double h = Math.Abs(start.Y - current.Y);

		Canvas.SetLeft(Band, x);
		Canvas.SetTop(Band, y);
		Band.Width = w;
		Band.Height = h;
		Band.IsVisible = true;
	}

	private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (_dragStart == null) return;
		Point start = _dragStart.Value;
		Point end = e.GetPosition(Overlay);
		_dragStart = null;

		SKRectI region = ToSourceRect(start, end);
		if (region.Width <= 0 || region.Height <= 0)
		{
			Complete(null); // click without a drag = cancel, matching the WinForms behaviour
			return;
		}
		Complete(region);
	}

	// Map the DIP-space drag onto source pixels via the backdrop's stretch scale, then clamp to the
	// image so a drag past the edge still yields a valid crop.
	private SKRectI ToSourceRect(Point a, Point b)
	{
		double overlayW = Overlay.Bounds.Width;
		double overlayH = Overlay.Bounds.Height;
		if (overlayW <= 0 || overlayH <= 0) return SKRectI.Empty;

		double scaleX = _srcWidth / overlayW;
		double scaleY = _srcHeight / overlayH;

		int left = (int)Math.Round(Math.Min(a.X, b.X) * scaleX);
		int top = (int)Math.Round(Math.Min(a.Y, b.Y) * scaleY);
		int right = (int)Math.Round(Math.Max(a.X, b.X) * scaleX);
		int bottom = (int)Math.Round(Math.Max(a.Y, b.Y) * scaleY);

		left = Math.Clamp(left, 0, _srcWidth);
		top = Math.Clamp(top, 0, _srcHeight);
		right = Math.Clamp(right, 0, _srcWidth);
		bottom = Math.Clamp(bottom, 0, _srcHeight);

		return new SKRectI(left, top, right, bottom);
	}

	private void Complete(SKRectI? region)
	{
		if (_completed) return;
		_completed = true;
		_result.TrySetResult(region);
		Close();
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		// If the window is dismissed some other way, treat it as a cancel. _full is owned by the
		// caller (it crops the result), so only the display copy is released here.
		_result.TrySetResult(null);
		_backdrop?.Dispose();
		_backdrop = null;
	}
}
