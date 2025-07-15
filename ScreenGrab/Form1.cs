using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ScreenGrab
{
	public partial class Form1 : Form
	{
		private Panel headerPanel;
		private Button copyButton;
		private Button saveButton;
		private Button saveAndCopyPathButton;
		private readonly bool isHighDpi;
		private readonly float scaleFactor;

		private Stack<Rectangle> rectangleHistory = new Stack<Rectangle>();
		private Stack<Rectangle> redoStack = new Stack<Rectangle>();

		private readonly int highlightThickness;
		const int highlightCornerRadius = 20;

		public Form1() : base()
		{
			Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"));

			scaleFactor = DeviceDpi / 96f;
			isHighDpi = scaleFactor > 1f;
			highlightThickness = isHighDpi ? 5 : 2;

			SetStyle(
					ControlStyles.ResizeRedraw
			  | ControlStyles.AllPaintingInWmPaint
			  | ControlStyles.UserPaint
			, true);
			DoubleBuffered = true;

			int headerHeight = isHighDpi ? 60 : 70;
			int buttonWidth = isHighDpi ? 90 : 110;
			int buttonHeight = isHighDpi ? 40 : 50;

			headerPanel = new Panel
			{
				Dock = DockStyle.Top,
				Height = headerHeight,
				BackColor = SystemColors.ControlLight
			};

			copyButton = new Button
			{
				Text = "Copy",
				Size = new Size(buttonWidth, buttonHeight),
				Location = new Point(10, (headerHeight - buttonHeight) / 2)
			};
			copyButton.Click += CopyButton_Click;

			saveButton = new Button
			{
				Text = "Save",
				Size = new Size(buttonWidth, buttonHeight),
				Location = new Point(100, (headerHeight - buttonHeight) / 2)
			};
			saveButton.Click += SaveButton_Click;

			saveAndCopyPathButton = new Button
			{
				Text = "Save (copy path)",
				Size = new Size(isHighDpi ? 240 : 260, buttonHeight),
				Location = new Point(200, (headerHeight - buttonHeight) / 2)
			};
			saveAndCopyPathButton.Click += SaveAndCopyPathButton_Click;

			headerPanel.Controls.Add(copyButton);
			headerPanel.Controls.Add(saveButton);
			headerPanel.Controls.Add(saveAndCopyPathButton);
			Controls.Add(headerPanel);

			Opacity = 0;
			ShowInTaskbar = false;
			FormBorderStyle = FormBorderStyle.None;
			Load += (s, e) =>
			{
				Hide();
				HideFromAltTab();
			};

			// Ensure buttons are visible when mouse enters the header panel
			headerPanel.MouseEnter += (s, e) =>
			{
				copyButton.Visible = true;
				saveButton.Visible = true;
			};

			InitializeComponent();

			HandleCreated += Form1_HandleCreated;
			HandleDestroyed += Form1_HandleDestroyed;
		}

		private void Form1_HandleCreated(object? sender, EventArgs e)
		{
			// register both combos every time we get a handle
			RegisterHotKey(Handle, 100, MOD_CONTROL | MOD_ALT, (uint)Keys.F12);
			RegisterHotKey(Handle, 101, MOD_CONTROL | MOD_ALT, (uint)Keys.PrintScreen);
			HideFromAltTab();
		}

		private void Form1_HandleDestroyed(object? sender, EventArgs e)
		{
			UnregisterHotKey(Handle, 100);
			UnregisterHotKey(Handle, 101);
		}


		/// <summary>
		/// Always clear the old background before drawing the new image.
		/// </summary>
		protected override void OnPaintBackground(PaintEventArgs e)
		{
			e.Graphics.Clear(BackColor);
		}

		private const int WM_HOTKEY = 0x0312; // Define WM_HOTKEY constant
		private const uint MOD_CONTROL = 0x0002; // Control key
		private const uint MOD_ALT = 0x0001;     // Alt key

		[DllImport("user32.dll")]
		private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

		[DllImport("user32.dll")]
		private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll")]
		private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

		private const int GWL_EXSTYLE = -20;
		private const int WS_EX_TOOLWINDOW = 0x00000080;
		private const int WS_EX_APPWINDOW = 0x00040000;

		// Re-adding missing fields
		private Bitmap? capturedImage;
		private Bitmap? fullScreenImage; // store initial full screen capture
		private SelectionForm? selectionForm;
		private NotifyIcon? trayIcon;

		public void SetTrayIcon(NotifyIcon icon)
		{
			trayIcon = icon;
		}

		public void StartCapture()
		{
			StartSelectionProcess();
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (keyData == Keys.Escape)
			{
				Hide();
				HideFromAltTab();
				return true;
			}
			else if (keyData == (Keys.Control | Keys.Z) && rectangleHistory.Count > 0)
			{
				UndoLastRectangle();
				return true;
			}
			else if (keyData == (Keys.Control | Keys.Y) && redoStack.Count > 0)
			{
				RedoRectangle();
				return true;
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}


		protected override void WndProc(ref Message m)
		{
			if (m.Msg == WM_HOTKEY)
			{
				CloseActiveNotification();
				StartSelectionProcess();
			}
			base.WndProc(ref m);
		}

		private void StartSelectionProcess()
		{
			try
			{
				Visible = false;
				HideFromAltTab();

				Rectangle screenBounds = Screen.PrimaryScreen?.Bounds
						?? throw new InvalidOperationException("No primary screen found.");
				fullScreenImage?.Dispose();
				fullScreenImage = new Bitmap(screenBounds.Width, screenBounds.Height);
				using (Graphics g = Graphics.FromImage(fullScreenImage))
				{
					g.CopyFromScreen(screenBounds.Location, Point.Empty, screenBounds.Size);
				}

				selectionForm = new SelectionForm(fullScreenImage);
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
				sf.SelectionComplete -= SelectionForm_SelectionComplete;
				sf.Dispose();
				selectionForm = null;

				if (selectedRegion.Width > 0 && selectedRegion.Height > 0)
				{
					CaptureSelectedRegion(selectedRegion);
				}
			}
		}

		private void CaptureSelectedRegion(Rectangle region)
		{
			Bitmap bitmap = new Bitmap(region.Width, region.Height);
			if (fullScreenImage != null)
			{
				// Crop the selected region from the previously captured full screen image
				using Graphics g = Graphics.FromImage(bitmap);
				g.DrawImage(fullScreenImage, new Rectangle(Point.Empty, region.Size), region, GraphicsUnit.Pixel);
			}
			else
			{
				// Fallback to capturing from screen if full image is not available
				using Graphics g = Graphics.FromImage(bitmap);
				g.CopyFromScreen(region.Location, Point.Empty, region.Size);
			}

			capturedImage?.Dispose();
			capturedImage = bitmap;
			fullScreenImage?.Dispose();
			fullScreenImage = null;

			originalImage?.Dispose();
			originalImage = new Bitmap(capturedImage);
			hasOriginalImage = true;

			SuspendLayout();

			FormBorderStyle = FormBorderStyle.FixedSingle;
			ControlBox = false;
			MinimizeBox = false;
			MaximizeBox = false;

			AutoScroll = false;
			AutoScrollMinSize = Size.Empty;

			ClientSize = new Size(bitmap.Width, bitmap.Height + headerPanel.Height);

			// Reset form visual state
			Opacity = 1.0;
			WindowState = FormWindowState.Normal;
			ShowInTaskbar = true;
			BackColor = SystemColors.Control;
			TransparencyKey = Color.Empty;
			AutoScrollPosition = Point.Empty;

			// Clear any remaining display state
			dragStartPoint = null;
			dragRectangle = null;

			// Clear undo/redo stacks
			rectangleHistory.Clear();
			redoStack.Clear();


			// Set title with dimensions
			Text = $"ScreenGrab - {bitmap.Width}x{bitmap.Height}";

			// Position the form on screen
			Screen currentScreen = Screen.FromPoint(Cursor.Position);
			int x = Math.Max(currentScreen.WorkingArea.X,
				Math.Min(Cursor.Position.X - (bitmap.Width / 2),
				currentScreen.WorkingArea.Right - bitmap.Width - 20));
			int y = Math.Max(currentScreen.WorkingArea.Y,
				Math.Min(Cursor.Position.Y - (bitmap.Height / 2),
				currentScreen.WorkingArea.Bottom - bitmap.Height - 20));

			Location = new Point(x, y);

			ResumeLayout();

			// Center the form on the primary screen
			StartPosition = FormStartPosition.Manual;
			Rectangle screenBounds = Screen.PrimaryScreen.Bounds;
			Location = new Point(
				screenBounds.Left + (screenBounds.Width - Width) / 2,
				screenBounds.Top + (screenBounds.Height - Height) / 2
			);

			// Show the form and bring it to front
			ShowInAltTab();
			Show();
			SetForegroundWindow(Handle);
			Activate();
			BringToFront();
			Focus();

			// Force a complete UI refresh
			Invalidate();
			Update();
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
				using BufferedGraphics bufferedGraphics = currentContext.Allocate(e.Graphics, DisplayRectangle);

				// Draw the image at its original size
				bufferedGraphics.Graphics.DrawImage(capturedImage, AutoScrollPosition.X, AutoScrollPosition.Y + headerPanel.Height,
				capturedImage.Width, capturedImage.Height);

				// Draw the rectangle if it exists
				if (dragRectangle.HasValue)
				{
					Rectangle rect = dragRectangle.Value;
					rect.Offset(0, headerPanel.Height);
					using Pen pen = new Pen(Color.Red, 2);
					DrawRoundedRectangle(bufferedGraphics.Graphics, pen, rect, 10);
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
				dragStartPoint = new Point(e.X, e.Y - headerPanel.Height);
				dragRectangle = null;
				Invalidate();
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			if (capturedImage != null && !dragStartPoint.HasValue)
			{
				// Calculate button positions: center all buttons as a group
				int gap = 10;
				int groupWidth = copyButton.Width + saveButton.Width + saveAndCopyPathButton.Width + (gap * 2);
				int leftStart = (ClientSize.Width - groupWidth) / 2;
				int top = (headerPanel.Height - copyButton.Height) / 2;

				copyButton.Visible = true;
				saveButton.Visible = true;
				saveAndCopyPathButton.Visible = true;
				copyButton.BringToFront();
				saveButton.BringToFront();
				saveAndCopyPathButton.BringToFront();

				copyButton.Location = new Point(leftStart, top);
				saveButton.Location = new Point(leftStart + copyButton.Width + gap, top);
				saveAndCopyPathButton.Location = new Point(leftStart + copyButton.Width + saveButton.Width + (gap * 2), top);
			}

			if (e.Button == MouseButtons.Left && dragStartPoint.HasValue)
			{
				Point currentPoint = new Point(e.X, e.Y - headerPanel.Height);
				dragRectangle = new Rectangle(
					Math.Min(dragStartPoint.Value.X, currentPoint.X),
					Math.Min(dragStartPoint.Value.Y, currentPoint.Y),
					Math.Abs(dragStartPoint.Value.X - currentPoint.X),
					Math.Abs(dragStartPoint.Value.Y - currentPoint.Y)
				);
				// Hide all buttons during drag
				copyButton.Visible = false;
				saveButton.Visible = false;
				saveAndCopyPathButton.Visible = false;
				Invalidate();
			}
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left && dragRectangle.HasValue)
			{
				// Ensure the rectangle has a minimum valid size (mouse was dragged sufficiently)
				const int MinDimension = 10; // Minimum width and height for a valid rectangle
				if (dragRectangle.Value.Width >= MinDimension && dragRectangle.Value.Height >= MinDimension)
				{
					AddRedBorder(dragRectangle.Value);
				}
				dragStartPoint = null;
				dragRectangle = null;
				Invalidate();
			}
			else
			{
				copyButton.Visible = false;
				saveButton.Visible = false;
				saveAndCopyPathButton.Visible = false;
			}
		}

		private void CopyButton_Click(object? sender, EventArgs e)
		{
			if (capturedImage != null)
			{
				Clipboard.SetImage(capturedImage);

				// Reset form state before hiding
				ResetFormState();

				Hide();
				HideFromAltTab();
			}
		}

		private string SaveScreenshot(bool copyPathToClipboard)
		{
			if (capturedImage == null)
			{
				MessageBox.Show("No image captured.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return string.Empty;
			}

			string downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads";
			string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string filePath = Path.Combine(downloadsPath, $"Screenshot_{timestamp}.png");

			// Save a clone of the captured image to exclude form controls
			using (Bitmap bitmapToSave = new Bitmap(capturedImage))
			{
				bitmapToSave.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
			}

			if (copyPathToClipboard)
			{
				Clipboard.SetText(filePath);
				ShowSilentNotification($"Image saved to {filePath}\nPath copied to clipboard");
			}
			else
			{
				ShowSilentNotification($"Image saved to {filePath}");
			}

			// Reset form state before hiding
			ResetFormState();
			Hide();
			HideFromAltTab();

			return filePath;
		}

		private void SaveButton_Click(object? sender, EventArgs e)
		{
			try
			{
				SaveScreenshot(false);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to save image: {ex.Message}", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void SaveAndCopyPathButton_Click(object? sender, EventArgs e)
		{
			try
			{
				SaveScreenshot(true);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to save image: {ex.Message}", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		// field to track the current notification form
		private Form? activeToastNotification = null;

		private void ShowSilentNotification(string message)
		{
			CloseActiveNotification();

			Form toast = new Form
			{
				FormBorderStyle = FormBorderStyle.None,
				StartPosition = FormStartPosition.Manual,
				BackColor = Color.DarkSlateGray,
				Opacity = 0.9,
				ShowInTaskbar = false,
				TopMost = true,
				Padding = new Padding(20)
			};

			// Store reference to the active notification
			activeToastNotification = toast;

			// Add message label
			Label label = new Label
			{
				Text = message,
				ForeColor = Color.White,
				AutoSize = false,
				TextAlign = ContentAlignment.MiddleCenter,
				Font = new Font("Segoe UI", 10),
				Dock = DockStyle.Fill
			};

			// Calculate the required size
			using (Graphics g = toast.CreateGraphics())
			{
				// Measure text with some extra space for padding
				SizeF textSize = g.MeasureString(message, label.Font, new SizeF(400, 1000));

				// Set minimum width and height values
				int width = Math.Max(850, (int)textSize.Width + 40);
				int height = Math.Max(80, (int)textSize.Height + 40);

				// Set the form size based on the text measurements
				toast.Size = new Size(width, height);
			}

			toast.Controls.Add(label);

			// Position in center of active screen
			Screen currentScreen = Screen.FromPoint(Cursor.Position);
			toast.Location = new Point(
				currentScreen.WorkingArea.Left + (currentScreen.WorkingArea.Width - toast.Width) / 2,
				currentScreen.WorkingArea.Top + (currentScreen.WorkingArea.Height - toast.Height) / 2
			);

			// Round the corners of the form
			toast.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, toast.Width, toast.Height, 15, 15));

			// Handle form closing to clear the reference
			toast.FormClosed += (s, e) => activeToastNotification = null;

			// Show toast and automatically close after 3 seconds
			toast.Show();

			Task.Delay(3000).ContinueWith(t =>
			{
				if (activeToastNotification == toast) // Only close if it's still the active notification
				{
					if (toast.InvokeRequired && !toast.IsDisposed)
						toast.Invoke(new Action(() => toast.Close()));
					else if (!toast.IsDisposed)
						toast.Close();
				}
			});
		}

		private void CloseActiveNotification()
		{
			if (activeToastNotification != null && !activeToastNotification.IsDisposed)
			{
				try
				{
					if (activeToastNotification.InvokeRequired)
						activeToastNotification.Invoke(new Action(() => activeToastNotification.Close()));
					else
						activeToastNotification.Close();
				}
				catch (ObjectDisposedException)
				{
					// Form may have been disposed in another thread
				}
				activeToastNotification = null;
			}
		}

		private void HideFromAltTab()
		{
			int style = GetWindowLong(Handle, GWL_EXSTYLE);
			style |= WS_EX_TOOLWINDOW;
			style &= ~WS_EX_APPWINDOW;
			SetWindowLong(Handle, GWL_EXSTYLE, style);
		}

		private void ShowInAltTab()
		{
			int style = GetWindowLong(Handle, GWL_EXSTYLE);
			style &= ~WS_EX_TOOLWINDOW;
			style |= WS_EX_APPWINDOW;
			SetWindowLong(Handle, GWL_EXSTYLE, style);
		}


		// Add this P/Invoke for rounded corners
		[DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
		private static extern IntPtr CreateRoundRectRgn(
			int nLeftRect,
			int nTopRect,
			int nRightRect,
			int nBottomRect,
			int nWidthEllipse,
			int nHeightEllipse
		);

		private void ResetFormState()
		{
			// Reset to initial state to prepare for next capture
			FormBorderStyle = FormBorderStyle.None;
			AutoScrollPosition = Point.Empty;
			dragStartPoint = null;
			dragRectangle = null;

			// Clear undo/redo stacks
			rectangleHistory.Clear();
			redoStack.Clear();
			if (originalImage != null)
			{
				originalImage.Dispose();
				originalImage = null;
			}
			hasOriginalImage = false;
		}


		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);

			// Ensure the client area is large enough to hold the image while respecting minimums
			if (capturedImage != null)
			{
				// Update the auto-scroll size to match the image dimensions
				AutoScrollMinSize = new Size(capturedImage.Width, capturedImage.Height);

				// Force a repaint
				Invalidate();
			}
		}

		private void AddRedBorder(Rectangle selection)
		{
			if (capturedImage == null) return;

			// Add to history
			rectangleHistory.Push(selection);
			// Clear redo stack when a new rectangle is added
			redoStack.Clear();

			// Draw the rectangle on the image
			using Graphics g = Graphics.FromImage(capturedImage);
			using Pen redPen = new Pen(Color.Red, highlightThickness);
			DrawRoundedRectangle(g, redPen, selection, highlightCornerRadius); // Add rounded corners with a radius of 10
			Invalidate(); // Trigger repaint to show the updated image
		}

		// Add these new methods for undo/redo functionality
		private void UndoLastRectangle()
		{
			if (capturedImage == null || rectangleHistory.Count == 0) return;

			// Pop the last rectangle from history and add to redo stack
			Rectangle lastRect = rectangleHistory.Pop();
			redoStack.Push(lastRect);

			// Recreate the image from scratch with all rectangles except the last one
			RedrawImage();
		}

		private void RedoRectangle()
		{
			if (capturedImage == null || redoStack.Count == 0) return;

			// Pop the last undone rectangle and add back to history
			Rectangle redoRect = redoStack.Pop();
			rectangleHistory.Push(redoRect);

			// Draw this rectangle on the image
			using Graphics g = Graphics.FromImage(capturedImage);
			using Pen redPen = new Pen(Color.Red, highlightThickness);
			DrawRoundedRectangle(g, redPen, redoRect, highlightCornerRadius);

			Invalidate(); // Refresh display
		}

		private void RedrawImage()
		{
			if (capturedImage == null) return;

			// Store the original captured image if this is our first undo operation
			if (!hasOriginalImage)
			{
				StoreOriginalImage();
			}

			// Restore from original clean image
			using (Graphics g = Graphics.FromImage(capturedImage))
			{
				g.Clear(Color.Transparent);
				g.DrawImage(originalImage, 0, 0);
			}

			// Redraw all rectangles in history
			using (Graphics g = Graphics.FromImage(capturedImage))
			{
				using Pen redPen = new Pen(Color.Red, highlightThickness);
				// Create a temporary copy of the history to preserve order
				Rectangle[] tempHistory = rectangleHistory.Reverse().ToArray();
				foreach (Rectangle rect in tempHistory)
				{
					DrawRoundedRectangle(g, redPen, rect, highlightCornerRadius);
				}
			}

			Invalidate(); // Refresh the display
		}

		// Add fields to store the original image
		private Bitmap? originalImage = null;
		private bool hasOriginalImage = false;

		// Method to store the original image for the first undo operation
		private void StoreOriginalImage()
		{
			if (capturedImage != null && !hasOriginalImage)
			{
				originalImage = new Bitmap(capturedImage);
				hasOriginalImage = true;
			}
		}

		private void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle rect, int cornerRadius)
		{
			SmoothingMode original = g.SmoothingMode;
			g.SmoothingMode = SmoothingMode.AntiAlias;

			using GraphicsPath path = new GraphicsPath();
			path.AddArc(rect.X, rect.Y, cornerRadius, cornerRadius, 180, 90);
			path.AddArc(rect.Right - cornerRadius, rect.Y, cornerRadius, cornerRadius, 270, 90);
			path.AddArc(rect.Right - cornerRadius, rect.Bottom - cornerRadius, cornerRadius, cornerRadius, 0, 90);
			path.AddArc(rect.X, rect.Bottom - cornerRadius, cornerRadius, cornerRadius, 90, 90);
			path.CloseFigure();
			g.DrawPath(pen, path);

			g.SmoothingMode = original;
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			UnregisterHotKey(Handle, 100);
			UnregisterHotKey(Handle, 101);
			capturedImage?.Dispose();
			originalImage?.Dispose();
			base.OnFormClosing(e);
		}

		private void Form1_Load(object sender, EventArgs e)
		{
			AutoScroll = true;
		}
	}

	// Selection form class for region selection
	public class SelectionForm : Form
	{
		private Point startPoint;
		private Point currentPoint;
		private bool isDragging = false;
		private readonly Bitmap screenImage;

		public event EventHandler<Rectangle>? SelectionComplete;

		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		public SelectionForm(Bitmap screenCapture)
		{
			screenImage = (Bitmap)screenCapture.Clone();

			// Configure the form
			FormBorderStyle = FormBorderStyle.None;
			WindowState = FormWindowState.Maximized;
			TopMost = true;
			Cursor = Cursors.Cross;
			DoubleBuffered = true;
			BackgroundImage = screenImage;
			BackColor = Color.Black;
			Opacity = 0.7;
			ShowInTaskbar = false;

			// Set up event handlers
			MouseDown += SelectionForm_MouseDown;
			MouseMove += SelectionForm_MouseMove;
			MouseUp += SelectionForm_MouseUp;
			KeyDown += SelectionForm_KeyDown;

			// Ensure the form comes up in the foreground
			Load += (s, e) =>
			{
				Activate();
				SetForegroundWindow(Handle);
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
				Close();
			}
		}

		private void SelectionForm_KeyDown(object? sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape)
			{
				// Cancel selection
				SelectionComplete?.Invoke(this, Rectangle.Empty);
				Close();
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
				BackgroundImage = null; // image disposed by caller
				screenImage.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}
