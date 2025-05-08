using System.Runtime.InteropServices;

namespace ScreenGrab
{
	public partial class Form1 : Form
	{
		private Button copyButton;

		public Form1() : base()
		{
			this.DoubleBuffered = true; // Enable double buffering for the form

			// Initialize the copy button
			copyButton = new Button
			{
				Text = "Copy",
				Visible = false,
				Size = new Size(120, 40)
			};
			copyButton.Click += CopyButton_Click;
			this.Controls.Add(copyButton);

			// keep it visible while over the button, hide it when you leave it
			copyButton.MouseLeave += (s, e) =>
			{
				// only hide if the mouse is really off the form too
				var clientPos = this.PointToClient(Cursor.Position);
				if (!this.ClientRectangle.Contains(clientPos))
					copyButton.Visible = false;
			};


			this.Opacity = 0; // Make the form fully transparent
			this.ShowInTaskbar = false; // Hide the form from the taskbar
			this.FormBorderStyle = FormBorderStyle.None; // Remove the title bar and buttons
			this.Load += (s, e) =>
			{
				this.Hide(); // Ensure the form is hidden immediately after loading
			};
			InitializeComponent();
			// Closing the constructor properly
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

			// Resize form to fit the captured image
			this.ClientSize = new Size(bitmap.Width, bitmap.Height);

			// Remove title bar and control buttons
			this.FormBorderStyle = FormBorderStyle.FixedSingle;
			this.ControlBox = false;
			this.MinimizeBox = false;
			this.MaximizeBox = false;

			// Make the form visible with a title bar
			this.Text = $"ScreenGrab - {bitmap.Width}x{bitmap.Height}";
			this.Opacity = 1.0;  // Ensure full opacity
			this.WindowState = FormWindowState.Normal;
			this.ShowInTaskbar = true;

			// Ensure we're not transparent or hidden in any way
			this.BackColor = SystemColors.Control;
			this.TransparencyKey = Color.Empty;

			// Add this to your CaptureSelectedRegion method before the Show() call
			// Ensure the form appears in a visible area of the screen
			Screen currentScreen = Screen.FromPoint(Cursor.Position);
			int x = Math.Max(currentScreen.WorkingArea.X,
				Math.Min(Cursor.Position.X - (bitmap.Width / 2),
				currentScreen.WorkingArea.Right - bitmap.Width - 20));
			int y = Math.Max(currentScreen.WorkingArea.Y,
				Math.Min(Cursor.Position.Y - (bitmap.Height / 2),
				currentScreen.WorkingArea.Bottom - bitmap.Height - 20));

			this.Location = new Point(x, y);

			// Show the form and bring it to the front
			Show();
			SetForegroundWindow(this.Handle);
			this.Activate();
			this.BringToFront();
			this.Focus();

			// Ensure UI is updated
			Application.DoEvents();

			this.Refresh();
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
					bufferedGraphics.Graphics.DrawRectangle(pen, dragRectangle.Value);
				}

				// Render the buffered graphics
				bufferedGraphics.Render();
			}
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			// Ensure the event is not handled if the click is on the copyButton
			if (copyButton.Bounds.Contains(e.Location))
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
				// Show the copy button when hovering over the image
				copyButton.Visible = true;
				copyButton.BringToFront();

				copyButton.Location = new Point((this.ClientSize.Width - copyButton.Width) / 2, 10); // Horizontally centered at the top
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
				copyButton.Visible = false; // Hide the button during drag
				Invalidate();
			}
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			base.OnMouseLeave(e);
			// convert screen to client so we can test against copyButton.Bounds
			var clientPos = this.PointToClient(Cursor.Position);
			if (!copyButton.Bounds.Contains(clientPos))
			{
				copyButton.Visible = false;
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
				copyButton.Visible = false; // Hide the button after mouse up
			}
		}
		private void CopyButton_Click(object? sender, EventArgs e)
		{
			if (capturedImage != null)
			{
				Clipboard.SetImage(capturedImage);
				MessageBox.Show("Image copied to clipboard!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
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
			g.DrawRectangle(redPen, selection);
			Invalidate(); // Trigger repaint to show the updated image
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
			this.BeginInvoke(new Action(() =>
			{
				// Try to register the hotkey with a different ID
				bool isHotKeyRegistered = RegisterHotKey(this.Handle, 100, (uint)(MOD_CONTROL | MOD_ALT), (uint)Keys.F12);

				// If that fails, try another key combination
				if (!isHotKeyRegistered)
				{
					int error = Marshal.GetLastWin32Error();
					MessageBox.Show($"Failed to register Ctrl+Alt+F12. Error code: {error}", "Error",
						MessageBoxButtons.OK, MessageBoxIcon.Warning);

					// Try a different key combination
					isHotKeyRegistered = RegisterHotKey(this.Handle, 101, (uint)(MOD_CONTROL | MOD_ALT), (uint)Keys.PrintScreen);

					if (isHotKeyRegistered)
					{
						//MessageBox.Show("Hotkey registered: Ctrl+Alt+PrintScreen", "Info",
						//	MessageBoxButtons.OK, MessageBoxIcon.Information);
					}
					else
					{
						error = Marshal.GetLastWin32Error();
						MessageBox.Show($"Failed to register alternate hotkey. Error code: {error}", "Error",
							MessageBoxButtons.OK, MessageBoxIcon.Error);
					}
				}
				else
				{
					//MessageBox.Show("Hotkey registered: Ctrl+Alt+F12", "Info",
					//	MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
			}));

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
