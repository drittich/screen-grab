using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ScreenGrab
{
	public partial class Form1 : Form
	{
		private Panel headerPanel;
		private Button copyButton;
		private Button saveButton;

		public Form1() : base()
		{
			this.Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"));

			this.SetStyle(
				ControlStyles.ResizeRedraw     // repaint on Resize
			  | ControlStyles.AllPaintingInWmPaint // skip WM_ERASEBKGND, paint everything in one go
			  | ControlStyles.UserPaint        // you’re handling all painting
			, true);
			this.DoubleBuffered = true; // Enable double buffering for the form


			headerPanel = new Panel
			{
				Dock = DockStyle.Top,
				Height = 60,
				BackColor = SystemColors.ControlLight
			};

			copyButton = new Button
			{
				Text = "Copy",
				Size = new Size(90, 40),
				Location = new Point(10, 15)
			};
			copyButton.Click += CopyButton_Click;

			saveButton = new Button
			{
				Text = "Save",
				Size = new Size(90, 40),
				Location = new Point(100, 15)
			};
			saveButton.Click += SaveButton_Click;

			headerPanel.Controls.Add(copyButton);
			headerPanel.Controls.Add(saveButton);
			this.Controls.Add(headerPanel);

			// Initialize the save button without setting Location; it will be handled dynamically.

			// keep it visible while over the button, hide it when you leave it

			// Add a similar MouseLeave event to the save button

			this.Opacity = 0; // Make the form fully transparent
			this.ShowInTaskbar = false; // Hide the form from the taskbar
			this.FormBorderStyle = FormBorderStyle.None; // Remove the title bar and buttons
			this.Load += (s, e) =>
			{
				this.Hide(); // Ensure the form is hidden immediately after loading
			};

			// Ensure buttons are visible when mouse enters the header panel
			headerPanel.MouseEnter += (s, e) =>
			{
				copyButton.Visible = true;
				saveButton.Visible = true;
			};

			InitializeComponent();

			this.HandleCreated += Form1_HandleCreated;
			this.HandleDestroyed += Form1_HandleDestroyed;
		}

		private void Form1_HandleCreated(object? sender, EventArgs e)
		{
			// register both combos every time we get a handle
			RegisterHotKey(this.Handle, 100, MOD_CONTROL | MOD_ALT, (uint)Keys.F12);
			RegisterHotKey(this.Handle, 101, MOD_CONTROL | MOD_ALT, (uint)Keys.PrintScreen);
		}

		private void Form1_HandleDestroyed(object? sender, EventArgs e)
		{
			// clean up after ourselves
			UnregisterHotKey(this.Handle, 100);
			UnregisterHotKey(this.Handle, 101);
		}


		/// <summary>
		/// Always clear the old background before drawing the new image.
		/// </summary>
		protected override void OnPaintBackground(PaintEventArgs e)
		{
			e.Graphics.Clear(this.BackColor);
		}

		// Define modifier keys
		private const int WM_HOTKEY = 0x0312; // Define WM_HOTKEY constant
		private const uint MOD_CONTROL = 0x0002; // Control key
		private const uint MOD_ALT = 0x0001;     // Alt key

		[DllImport("user32.dll")]
		private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

		[DllImport("user32.dll")]
		private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		// Re-adding missing fields
		private Bitmap? capturedImage;
		private SelectionForm? selectionForm;
		private NotifyIcon? trayIcon;

		// Method to store the tray icon reference
		public void SetTrayIcon(NotifyIcon icon)
		{
			trayIcon = icon;
		}

		// Public method to allow starting the capture from Program.cs
		public void StartCapture()
		{
			StartSelectionProcess();
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (keyData == Keys.Escape)
			{
				// Hide the form when Esc is pressed
				this.Hide();
				return true; // Indicate that the key press was handled
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}


		protected override void WndProc(ref Message m)
		{
			if (m.Msg == WM_HOTKEY)
			{
				StartSelectionProcess();
			}
			base.WndProc(ref m);
		}

		private void StartSelectionProcess()
		{
			try
			{
				// Ensure form is hidden before starting selection
				this.Visible = false; // Ensure the form remains hidden

				// Take screenshot of entire screen
				Rectangle screenBounds = Screen.PrimaryScreen.Bounds;
				Bitmap fullScreenshot = new Bitmap(screenBounds.Width, screenBounds.Height);
				using (Graphics g = Graphics.FromImage(fullScreenshot))
				{
					g.CopyFromScreen(screenBounds.Location, Point.Empty, screenBounds.Size);
				}

				// Create and show selection form with the screenshot as background
				selectionForm = new SelectionForm(fullScreenshot);
				selectionForm.SelectionComplete += SelectionForm_SelectionComplete;
				selectionForm.ShowDialog();
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error during selection process: {ex.Message}", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void SelectionForm_SelectionComplete(object? sender, Rectangle selectedRegion)
		{
			if (sender is SelectionForm sf)
			{
				// Cleanup selection form
				sf.SelectionComplete -= SelectionForm_SelectionComplete;
				sf.Dispose();
				selectionForm = null;

				// Only proceed if we have a valid selection
				if (selectedRegion.Width > 0 && selectedRegion.Height > 0)
				{
					CaptureSelectedRegion(selectedRegion);
				}
			}
		}

		private void CaptureSelectedRegion(Rectangle region)
		{
			// Capture just the selected region
			Bitmap bitmap = new Bitmap(region.Width, region.Height);
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.CopyFromScreen(region.Location, Point.Empty, region.Size);
			}

			// Dispose the old image if it exists to prevent memory leaks
			capturedImage?.Dispose();
			capturedImage = bitmap;

			// **IMPORTANT**: resize the client area to exactly the new image size
			this.SuspendLayout();
			this.FormBorderStyle = FormBorderStyle.None;
			this.AutoScroll = false;
			this.AutoScrollMinSize = Size.Empty;
			this.ClientSize = new Size(capturedImage.Width, capturedImage.Height);
			this.ResumeLayout();

			// Reset form completely - clear any accumulated changes
			this.Opacity = 1.0;
			this.WindowState = FormWindowState.Normal;
			this.ShowInTaskbar = true;
			this.BackColor = SystemColors.Control;
			this.TransparencyKey = Color.Empty;
			this.AutoScrollPosition = Point.Empty; // Reset scroll position

			// Clear any remaining display state
			dragStartPoint = null;
			dragRectangle = null;

			// Ensure the form's client area exactly matches the bitmap size
			// This is critical to avoid black borders
			this.FormBorderStyle = FormBorderStyle.FixedSingle;
			this.ControlBox = false;
			this.MinimizeBox = false;
			this.MaximizeBox = false;
			this.ClientSize = new Size(bitmap.Width, bitmap.Height);

			// Disable auto scrolling to prevent extra padding that causes the border
			this.AutoScroll = false;
			this.AutoScrollMinSize = new Size(0, 0); // Clear auto-scroll size

			// Set title with dimensions
			this.Text = $"ScreenGrab - {bitmap.Width}x{bitmap.Height}";

			// Position the form on screen
			Screen currentScreen = Screen.FromPoint(Cursor.Position);
			int x = Math.Max(currentScreen.WorkingArea.X,
				Math.Min(Cursor.Position.X - (bitmap.Width / 2),
				currentScreen.WorkingArea.Right - bitmap.Width - 20));
			int y = Math.Max(currentScreen.WorkingArea.Y,
				Math.Min(Cursor.Position.Y - (bitmap.Height / 2),
				currentScreen.WorkingArea.Bottom - bitmap.Height - 20));

			this.Location = new Point(x, y);

			// Show the form and bring it to front
			Show();
			SetForegroundWindow(this.Handle);
			this.Activate();
			this.BringToFront();
			this.Focus();

			// Force a complete UI refresh
			this.Invalidate();  // marks entire client rectangle
			this.Update();      // synchronously repaints
		}



		private Point? dragStartPoint = null;
		private Rectangle? dragRectangle = null;

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			if (capturedImage != null)
			{
				// Use double buffering to reduce flickering
				BufferedGraphicsContext currentContext = BufferedGraphicsManager.Current;
				using BufferedGraphics bufferedGraphics = currentContext.Allocate(e.Graphics, this.DisplayRectangle);

				// Draw the image at its original size
				bufferedGraphics.Graphics.DrawImage(capturedImage, AutoScrollPosition.X, AutoScrollPosition.Y,
				capturedImage.Width, capturedImage.Height);

				// Draw the rectangle if it exists
				if (dragRectangle.HasValue)
				{
					using Pen pen = new Pen(Color.Red, 2);
					DrawRoundedRectangle(bufferedGraphics.Graphics, pen, dragRectangle.Value, 10); // Rounded corners with radius 10
				}

				// Render the buffered graphics
				bufferedGraphics.Render();
			}
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			// Ensure the event is not handled if the click is on the copyButton or saveButton
			if (copyButton.Bounds.Contains(e.Location) || saveButton.Bounds.Contains(e.Location))
			{
				return; // Allow the event to propagate to the button
			}

			if (e.Button == MouseButtons.Left)
			{
				dragStartPoint = e.Location;
				dragRectangle = null;
				Invalidate();
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			if (capturedImage != null && !dragStartPoint.HasValue)
			{
				// Calculate button positions: center both buttons as a group
				int gap = 10;
				int groupWidth = copyButton.Width + saveButton.Width + gap;
				int leftStart = (this.ClientSize.Width - groupWidth) / 2;

				copyButton.Visible = true;
				saveButton.Visible = true;
				copyButton.BringToFront();
				saveButton.BringToFront();

				copyButton.Location = new Point(leftStart, 10);
				saveButton.Location = new Point(leftStart + copyButton.Width + gap, 10);
			}

			if (e.Button == MouseButtons.Left && dragStartPoint.HasValue)
			{
				Point currentPoint = e.Location;
				dragRectangle = new Rectangle(
					Math.Min(dragStartPoint.Value.X, currentPoint.X),
					Math.Min(dragStartPoint.Value.Y, currentPoint.Y),
					Math.Abs(dragStartPoint.Value.X - currentPoint.X),
					Math.Abs(dragStartPoint.Value.Y - currentPoint.Y)
				);
				// Hide both buttons during drag
				copyButton.Visible = false;
				saveButton.Visible = false;
				Invalidate();
			}
		}


		protected override void OnMouseUp(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left && dragRectangle.HasValue)
			{
				AddRedBorder(dragRectangle.Value);
				dragStartPoint = null;
				dragRectangle = null;
				Invalidate();
			}
			else
			{
				// Hide both buttons after mouse up
				copyButton.Visible = false;
				saveButton.Visible = false;
			}
		}


		private void CopyButton_Click(object? sender, EventArgs e)
		{
			if (capturedImage != null)
			{
				Clipboard.SetImage(capturedImage);

				// Reset form state before hiding
				ResetFormState();
				this.Hide();
			}
		}

		private void SaveButton_Click(object? sender, EventArgs e)
		{
			try
			{
				if (capturedImage == null)
				{
					MessageBox.Show("No image captured.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}

				string downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads";
				string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
				string filePath = Path.Combine(downloadsPath, $"Screenshot_{timestamp}.png");

				// Save a clone of the captured image to exclude form controls
				using (Bitmap bitmapToSave = new Bitmap(capturedImage))
				{
					bitmapToSave.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
				}
				MessageBox.Show($"Image saved to {filePath}", "Save Successful",
					MessageBoxButtons.OK, MessageBoxIcon.Information);

				// Reset form state before hiding
				ResetFormState();
				this.Hide();
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to save image: {ex.Message}", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		// Add this helper method to reset form state
		private void ResetFormState()
		{
			// Reset to initial state to prepare for next capture
			this.FormBorderStyle = FormBorderStyle.None;
			this.AutoScrollPosition = Point.Empty;
			dragStartPoint = null;
			dragRectangle = null;
		}


		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);

			// Ensure the client area is large enough to hold the image while respecting minimums
			if (capturedImage != null)
			{
				// Update the auto-scroll size to match the image dimensions
				this.AutoScrollMinSize = new Size(capturedImage.Width, capturedImage.Height);

				// Force a repaint
				this.Invalidate();
			}
		}

		private void AddRedBorder(Rectangle selection)
		{
			if (capturedImage == null) return;

			using Graphics g = Graphics.FromImage(capturedImage);
			using Pen redPen = new Pen(Color.Red, 5);
			DrawRoundedRectangle(g, redPen, selection, 10); // Add rounded corners with a radius of 10
			Invalidate(); // Trigger repaint to show the updated image
		}

		private void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle rect, int cornerRadius)
		{
			using GraphicsPath path = new GraphicsPath();
			path.AddArc(rect.X, rect.Y, cornerRadius, cornerRadius, 180, 90);
			path.AddArc(rect.Right - cornerRadius, rect.Y, cornerRadius, cornerRadius, 270, 90);
			path.AddArc(rect.Right - cornerRadius, rect.Bottom - cornerRadius, cornerRadius, cornerRadius, 0, 90);
			path.AddArc(rect.X, rect.Bottom - cornerRadius, cornerRadius, cornerRadius, 90, 90);
			path.CloseFigure();
			g.DrawPath(pen, path);
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			UnregisterHotKey(this.Handle, 100);
			UnregisterHotKey(this.Handle, 101);
			capturedImage?.Dispose();
			base.OnFormClosing(e);
		}

		private void Form1_Load(object sender, EventArgs e)
		{
			// Add a small delay to ensure the form handle is created
			//this.BeginInvoke(new Action(() =>
			//{
			//	// Try to register the hotkey with a different ID
			//	bool isHotKeyRegistered = RegisterHotKey(this.Handle, 100, (uint)(MOD_CONTROL | MOD_ALT), (uint)Keys.F12);
			//
			//	// If that fails, try another key combination
			//	if (!isHotKeyRegistered)
			//	{
			//		int error = Marshal.GetLastWin32Error();
			//		MessageBox.Show($"Failed to register Ctrl+Alt+F12. Error code: {error}", "Error",
			//			MessageBoxButtons.OK, MessageBoxIcon.Warning);
			//
			//		// Try a different key combination
			//		isHotKeyRegistered = RegisterHotKey(this.Handle, 101, (uint)(MOD_CONTROL | MOD_ALT), (uint)Keys.PrintScreen);
			//
			//		if (isHotKeyRegistered)
			//		{
			//			//MessageBox.Show("Hotkey registered: Ctrl+Alt+PrintScreen", "Info",
			//			//	MessageBoxButtons.OK, MessageBoxIcon.Information);
			//		}
			//		else
			//		{
			//			error = Marshal.GetLastWin32Error();
			//			MessageBox.Show($"Failed to register alternate hotkey. Error code: {error}", "Error",
			//				MessageBoxButtons.OK, MessageBoxIcon.Error);
			//		}
			//	}
			//	else
			//	{
			//		//MessageBox.Show("Hotkey registered: Ctrl+Alt+F12", "Info",
			//		//	MessageBoxButtons.OK, MessageBoxIcon.Information);
			//	}
			//}));

			// Add this line to enable auto-scrolling
			this.AutoScroll = true;
		}
	}

	// Selection form class for region selection
	public class SelectionForm : Form
	{
		private Point startPoint;
		private Point currentPoint;
		private bool isDragging = false;
		private Bitmap screenImage;

		public event EventHandler<Rectangle>? SelectionComplete;

		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		public SelectionForm(Bitmap screenCapture)
		{
			screenImage = screenCapture;

			// Configure the form
			this.FormBorderStyle = FormBorderStyle.None;
			this.WindowState = FormWindowState.Maximized;
			this.TopMost = true;
			this.Cursor = Cursors.Cross;
			this.DoubleBuffered = true;
			this.BackgroundImage = screenCapture;
			this.BackColor = Color.Black;
			this.Opacity = 0.7;
			this.ShowInTaskbar = false;

			// Set up event handlers
			this.MouseDown += SelectionForm_MouseDown;
			this.MouseMove += SelectionForm_MouseMove;
			this.MouseUp += SelectionForm_MouseUp;
			this.KeyDown += SelectionForm_KeyDown;

			// Ensure the form comes up in the foreground
			this.Load += (s, e) =>
			{
				this.Activate();
				SetForegroundWindow(this.Handle);
			};
		}


		private void SelectionForm_MouseDown(object? sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				isDragging = true;
				startPoint = e.Location;
				currentPoint = startPoint;
				Invalidate();
			}
		}

		private void SelectionForm_MouseMove(object? sender, MouseEventArgs e)
		{
			if (isDragging)
			{
				currentPoint = e.Location;
				Invalidate();
			}
		}

		private void SelectionForm_MouseUp(object? sender, MouseEventArgs e)
		{
			if (isDragging)
			{
				isDragging = false;
				Rectangle selectionRect = GetSelectionRectangle();

				// Trigger the selection complete event
				SelectionComplete?.Invoke(this, selectionRect);
				this.Close();
			}
		}

		private void SelectionForm_KeyDown(object? sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape)
			{
				// Cancel selection
				SelectionComplete?.Invoke(this, Rectangle.Empty);
				this.Close();
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);

			if (isDragging)
			{
				// Draw selection rectangle
				using (Pen pen = new Pen(Color.Red, 2))
				{
					Rectangle rect = GetSelectionRectangle();
					e.Graphics.DrawRectangle(pen, rect);

					// Add semi-transparent fill to indicate selected area
					using (Brush brush = new SolidBrush(Color.FromArgb(50, Color.White)))
					{
						e.Graphics.FillRectangle(brush, rect);
					}
				}
			}
		}

		private Rectangle GetSelectionRectangle()
		{
			int x = Math.Min(startPoint.X, currentPoint.X);
			int y = Math.Min(startPoint.Y, currentPoint.Y);
			int width = Math.Abs(currentPoint.X - startPoint.X);
			int height = Math.Abs(currentPoint.Y - startPoint.Y);

			return new Rectangle(x, y, width, height);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				screenImage?.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}
