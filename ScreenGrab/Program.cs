using System.Threading;

namespace ScreenGrab;

static class Program
{
	private static Mutex? _mutex;
	private const string MutexName = "ScreenGrabApplicationMutex";

	/// <summary>
	///  The main entry point for the application.
	/// </summary>
	[STAThread]
	static void Main()
	{
		ApplicationConfiguration.Initialize();

		// Try to create a new mutex
		bool createdNew;
		_mutex = new Mutex(true, MutexName, out createdNew);

		if (!createdNew)
		{
			MessageBox.Show("ScreenGrab is already running.", "ScreenGrab", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		// Create the main form but don't show it initially
		Form1 mainForm = new Form1();
		mainForm.Visible = false;

		// Create a NotifyIcon for the system tray
		NotifyIcon trayIcon = new NotifyIcon
		{
			Icon = new Icon("icon.ico"),
			Visible = true,
			Text = "ScreenGrab",
			ContextMenuStrip = new ContextMenuStrip()
		};

		// Add options to the tray menu
		trayIcon.ContextMenuStrip.Items.Add("Capture (Ctrl-Alt-F12)", null, (sender, e) => mainForm.StartCapture());
		trayIcon.ContextMenuStrip.Items.Add("Exit", null, (sender, e) => Application.Exit());

		// Enable tray icon double-click to start capture
		trayIcon.DoubleClick += (sender, e) => mainForm.StartCapture();

		// Store the tray icon in the form so it doesn't get garbage collected
		mainForm.SetTrayIcon(trayIcon);

		// Run the application with the main form as the message loop owner
		// but keep it invisible
		try
		{
			Application.Run(mainForm);
		}
		finally
		{
			// Clean up the mutex
			_mutex?.ReleaseMutex();
			_mutex?.Dispose();
		}
	}
}
