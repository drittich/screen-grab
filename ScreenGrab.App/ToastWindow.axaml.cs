using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace ScreenGrab.App;

/// <summary>
/// A borderless, topmost, auto-closing toast — the Avalonia port of <c>ShowSilentNotification</c>
/// (Form1 L988). The WinForms rounded-region P/Invoke and manual per-monitor centering are dropped:
/// a rounded <c>Border</c> handles the corners and <c>CenterScreen</c>/the compositor handles
/// placement (absolute positioning is forbidden on Wayland anyway).
/// </summary>
public partial class ToastWindow : Window
{
	private static ToastWindow? _active; // only the most recent toast stays on screen
	private DispatcherTimer? _timer;

	public ToastWindow() => InitializeComponent();

	private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

	/// <summary>Shows <paramref name="message"/> for ~3 seconds, replacing any visible toast. Must be
	/// called on the UI thread.</summary>
	public static void Show(string message)
	{
		_active?.Close();

		var toast = new ToastWindow();
		toast.MessageText.Text = message;
		_active = toast;

		toast.Closed += (_, _) => { if (_active == toast) _active = null; };

		toast._timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
		toast._timer.Tick += (_, _) =>
		{
			toast._timer!.Stop();
			toast.Close();
		};
		toast._timer.Start();

		toast.Show();
	}
}
