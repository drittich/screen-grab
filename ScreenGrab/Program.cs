namespace ScreenGrab;

static class Program
{
	/// <summary>
	///  The main entry point for the application.
	/// </summary>
	[STAThread]
	static void Main()
	{
		ApplicationConfiguration.Initialize();

		// Create the main form but don't show it initially
		Form1 mainForm = new Form1();
		mainForm.Visible = false;

		// Create a NotifyIcon for the system tray
		NotifyIcon trayIcon = new NotifyIcon
		{
			Icon = SystemIcons.Application,
			Visible = true,
			Text = "ScreenGrab",
			ContextMenuStrip = new ContextMenuStrip()
		};

		// Add options to the tray menu
		trayIcon.ContextMenuStrip.Items.Add("Capture", null, (sender, e) => mainForm.StartCapture());
		trayIcon.ContextMenuStrip.Items.Add("Exit", null, (sender, e) => Application.Exit());

		// Enable tray icon double-click to start capture
		trayIcon.DoubleClick += (sender, e) => mainForm.StartCapture();

		// Store the tray icon in the form so it doesn't get garbage collected
		mainForm.SetTrayIcon(trayIcon);

		// Run the application with the main form as the message loop owner
		// but keep it invisible
		Application.Run(mainForm);
	}
}
