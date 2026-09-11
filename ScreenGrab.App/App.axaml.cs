using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SkiaSharp;

namespace ScreenGrab.App;

public partial class App : Application
{
	public override void Initialize() => AvaloniaXamlLoader.Load(this);

	public override void OnFrameworkInitializationCompleted()
	{
		// Tray-only app: no main window. The editor/selection windows are
		// created on demand in later phases.
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

		base.OnFrameworkInitializationCompleted();
	}

	private void OnTrayClicked(object? sender, EventArgs e) => OpenEditor();

	private void OnCapture(object? sender, EventArgs e) => OpenEditor();

	// Phase 3 harness: opens the editor on a blank canvas so the annotation flow can be exercised
	// on both OSes before real capture lands. Phase 4 replaces the blank bitmap with an
	// IScreenCapture result fed through the selection overlay.
	private void OpenEditor()
	{
		var editor = new EditorWindow(CreatePlaceholderCapture(1280, 800));
		editor.Show();
	}

	private static SKBitmap CreatePlaceholderCapture(int width, int height)
	{
		var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
		using var canvas = new SKCanvas(bmp);
		canvas.Clear(SKColors.White);
		return bmp;
	}

	private void OnToggleStartup(object? sender, EventArgs e)
	{
		// Phase 5: IStartupManager (registry on Windows, .desktop on Linux).
	}

	private void OnExit(object? sender, EventArgs e)
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			desktop.Shutdown();
	}
}
