using System;
using Avalonia;
using ScreenGrab.Core;

namespace ScreenGrab.App;

internal static class Program
{
	// The single-instance lock, owned by the primary tray process for the app's lifetime and read by
	// App to wire the IPC capture listener. Set before the Avalonia lifetime starts.
	internal static ISingleInstanceIpc? Ipc { get; private set; }

	// True when this launch requested an immediate capture (`--capture`) — the KDE shortcut path.
	internal static bool LaunchWithCapture { get; private set; }

	// Initialization code. Don't use any Avalonia, third-party APIs or any
	// SynchronizationContext-reliant code before AppMain is called: things aren't
	// initialized yet and stuff might break.
	[STAThread]
	public static int Main(string[] args)
	{
		LaunchWithCapture = Array.Exists(args, a =>
			string.Equals(a, "--capture", StringComparison.OrdinalIgnoreCase));

		// Enforce a single tray instance. A second launch forwards its capture request (if any) to the
		// running instance over IPC and exits — this is how the KDE `screengrab --capture` shortcut
		// (and a duplicate Windows launch) reaches the resident tray app.
		var ipc = PlatformServices.CreateSingleInstanceIpc();
		if (!ipc.TryAcquire())
		{
			if (LaunchWithCapture)
			{
				try { ipc.SignalCapture(); }
				catch (Exception ex) { Console.Error.WriteLine($"ScreenGrab: could not signal the running instance: {ex.Message}"); }
			}
			ipc.Dispose();
			return 0;
		}

		Ipc = ipc;
		try
		{
			return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
		}
		finally
		{
			ipc.Dispose();
		}
	}

	// Avalonia configuration, don't remove; also used by the visual designer.
	public static AppBuilder BuildAvaloniaApp()
		=> AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.LogToTrace();
}
