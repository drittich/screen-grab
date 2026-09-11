using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

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

	private void OnTrayClicked(object? sender, EventArgs e)
	{
		// Phase 4 wires this to a capture. Left as a no-op for the scaffold.
	}

	private void OnCapture(object? sender, EventArgs e)
	{
		// Phase 4: trigger capture -> selection -> editor.
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
