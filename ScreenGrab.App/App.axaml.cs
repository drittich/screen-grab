using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ScreenGrab.Core;
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

	// Guards against re-entrant capture (a second tray click while the picker is up).
	private bool _capturing;

	private void OnTrayClicked(object? sender, EventArgs e) => _ = StartCaptureAsync();

	private void OnCapture(object? sender, EventArgs e) => _ = StartCaptureAsync();

	/// <summary>
	/// The phase-4 capture flow: grab the full screen via the per-OS <see cref="IScreenCapture"/>,
	/// let the user rubber-band a region in the fullscreen selection overlay, crop it, and open the
	/// annotation editor on the crop. Cancelling the overlay (Escape / empty drag) aborts cleanly.
	/// </summary>
	private async Task StartCaptureAsync()
	{
		if (_capturing) return;
		_capturing = true;
		try
		{
			SKBitmap full;
			try
			{
				full = await Task.Run(PlatformServices.CreateScreenCapture().CaptureFullScreen);
			}
			catch (Exception ex)
			{
				// Capture failed (e.g. Spectacle missing on Linux). Nothing to show; log and bail.
				Console.Error.WriteLine($"ScreenGrab: capture failed: {ex.Message}");
				return;
			}

			try
			{
				var picker = new SelectionWindow(full);
				SKRectI? region = await picker.SelectAsync();
				if (region is not { Width: > 0, Height: > 0 })
					return; // cancelled

				SKBitmap cropped = ImageOps.Crop(full, region.Value);
				new EditorWindow(cropped).Show();
			}
			finally
			{
				full.Dispose();
			}
		}
		finally
		{
			_capturing = false;
		}
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
