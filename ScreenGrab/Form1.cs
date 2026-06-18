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

		// Annotations are the drawable items; operations are the undoable steps that produce them.
		private interface IAnnotation { }
		private record RectAnnotation(Rectangle Rect) : IAnnotation;
		private record TextAnnotation(Point Location, string Text, float FontSize) : IAnnotation;

		// Operation history. Replaying all operations oldest->newest yields the live annotation
		// set. This keeps the stack append-only so undo/redo of edits is predictable.
		private interface IOperation { }
		private record AddRectOp(RectAnnotation Annotation) : IOperation;
		private record AddTextOp(TextAnnotation Annotation) : IOperation;
		private record EditTextOp(TextAnnotation Old, TextAnnotation New) : IOperation;
		private record DeleteTextOp(TextAnnotation Target) : IOperation;

		private Stack<IOperation> _history = new Stack<IOperation>();
		private Stack<IOperation> _redoStack = new Stack<IOperation>();

		// Projects the operation history into the ordered set of currently-live annotations.
		private List<IAnnotation> ProjectAnnotations()
		{
			var live = new List<IAnnotation>();
			// _history enumerates newest-first, so reverse to replay oldest-first.
			foreach (IOperation op in _history.Reverse())
			{
				switch (op)
				{
					case AddRectOp ar:
						live.Add(ar.Annotation);
						break;
					case AddTextOp at:
						live.Add(at.Annotation);
						break;
					case EditTextOp et:
						int idx = live.IndexOf(et.Old);
						if (idx >= 0)
							live[idx] = et.New;       // replace in place, preserving draw order
						else
							live.Add(et.New);          // defensive: original missing, just add
						break;
					case DeleteTextOp dt:
						live.Remove(dt.Target);
						break;
				}
			}
			return live;
		}

		private readonly int highlightThickness;
		const int highlightCornerRadius = 20;

		// Text annotation constants
		private const int TextPadding = 4;
		private static readonly Color TextBgColor = Color.FromArgb(0x15, 0x65, 0xC0);
		private const float DefaultFontSize = 16f;
		private const float MinFontSize = 8f;
		private const float MaxFontSize = 72f;

		// Active text box state
		private TextBox? _activeTextBox = null;
		private Point _textBoxImageOrigin;   // image-relative top-left when committed
		private float _activeTextFontSize;
		// When re-editing an existing annotation, the original is held here so commit records
		// an EditTextOp (old -> new) rather than an AddTextOp. Null for a brand-new annotation.
		private TextAnnotation? _editingOriginal = null;

		// Text box drag state
		private Point? _tbDragMouseDown = null;   // screen point where the drag began
		private Point _tbDragGrabOffset;          // offset from box top-left to cursor at grab time (screen space)
		private bool _tbDragging = false;

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
				if (_activeTextBox != null)
				{
					CancelText();
					return true;
				}
				Hide();
				HideFromAltTab();
				return true;
			}
			else if (keyData == (Keys.Control | Keys.Z) && _history.Count > 0 && _activeTextBox == null)
			{
				UndoLastAnnotation();
				return true;
			}
			else if (keyData == (Keys.Control | Keys.Y) && _redoStack.Count > 0 && _activeTextBox == null)
			{
				RedoAnnotation();
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
			_history.Clear();
			_redoStack.Clear();
			_editingOriginal = null;


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

			if (e.Button == MouseButtons.Left && _activeTextBox == null)
			{
				dragStartPoint = new Point(e.X, e.Y - headerPanel.Height);
				dragRectangle = null;
				Invalidate();
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			if (capturedImage != null && !dragStartPoint.HasValue && _activeTextBox == null)
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

			if (e.Button == MouseButtons.Left && dragStartPoint.HasValue && _activeTextBox == null)
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
			if (e.Button == MouseButtons.Left && dragRectangle.HasValue && _activeTextBox == null)
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
			else if (_activeTextBox == null)
			{
				copyButton.Visible = false;
				saveButton.Visible = false;
				saveAndCopyPathButton.Visible = false;
			}
		}

		protected override void OnMouseDoubleClick(MouseEventArgs e)
		{
			base.OnMouseDoubleClick(e);

			if (capturedImage == null) return;
			if (e.Button != MouseButtons.Left) return;
			if (_activeTextBox != null) return;
			if (copyButton.Bounds.Contains(e.Location) || saveButton.Bounds.Contains(e.Location)
				|| saveAndCopyPathButton.Bounds.Contains(e.Location)) return;

			// Image-relative position
			int imgX = e.X;
			int imgY = e.Y - headerPanel.Height;
			if (imgX < 0 || imgY < 0 || imgX >= capturedImage.Width || imgY >= capturedImage.Height) return;

			// Cancel any in-progress single-click drag that the double-click may have started
			dragStartPoint = null;
			dragRectangle = null;

			Point imagePoint = new Point(imgX, imgY);

			// If the click lands on an existing text annotation, re-open it for editing.
			TextAnnotation? hit = FindTextAnnotationAt(imagePoint);
			if (hit != null)
			{
				EditExistingTextAnnotation(hit);
				return;
			}

			StartTextBox(imagePoint);
		}

		// Returns the topmost (last-drawn) live text annotation whose box contains the point.
		private TextAnnotation? FindTextAnnotationAt(Point imagePoint)
		{
			List<IAnnotation> live = ProjectAnnotations();
			// Iterate in reverse so the topmost (most recently drawn) wins on overlap.
			for (int i = live.Count - 1; i >= 0; i--)
			{
				if (live[i] is TextAnnotation t)
				{
					Rectangle bounds = GetTextAnnotationBounds(t);
					if (!bounds.IsEmpty && bounds.Contains(imagePoint))
						return t;
				}
			}
			return null;
		}

		// Opens an editor seeded from an existing annotation. History is NOT mutated here; the
		// original is hidden from the canvas while editing and the change is recorded as a single
		// EditTextOp on commit (or discarded on cancel), keeping undo/redo clean.
		private void EditExistingTextAnnotation(TextAnnotation annotation)
		{
			_editingOriginal = annotation;

			// Temporarily repaint the canvas without the annotation being edited so the live
			// text box isn't drawn over a stale burned-in copy.
			RedrawImageExcluding(annotation);

			// Re-open an editor pre-populated from the annotation.
			StartTextBox(annotation.Location, annotation.Text, annotation.FontSize);
		}

		private void StartTextBox(Point imageOrigin, string? seedText = null, float? seedFontSize = null)
		{
			if (capturedImage == null) return;

			_textBoxImageOrigin = imageOrigin;
			_activeTextFontSize = seedFontSize ?? DefaultFontSize * scaleFactor;

			// TextBox left edge is imageOrigin.X, top edge is imageOrigin.Y + headerPanel.Height (form coords)
			int formX = imageOrigin.X;
			int formY = imageOrigin.Y + headerPanel.Height;

			// Start small; the box auto-sizes to content via ResizeTextBox().
			int initialWidth = (int)(_activeTextFontSize) + TextPadding * 2;

			var tb = new TextBox
			{
				BorderStyle = BorderStyle.None,
				BackColor = TextBgColor,
				ForeColor = Color.White,
				Font = new Font("Segoe UI", _activeTextFontSize, GraphicsUnit.Pixel),
				Multiline = true,
				WordWrap = true,
				ScrollBars = ScrollBars.None,
				Location = new Point(formX, formY),
				Width = initialWidth,
				Height = (int)(_activeTextFontSize + TextPadding * 2),
				Padding = new Padding(TextPadding),
				Text = seedText ?? string.Empty,
			};

			tb.TextChanged += TextBox_TextChanged;
			tb.KeyDown += TextBox_KeyDown;
			tb.Leave += TextBox_Leave;
			tb.MouseDown += TextBox_MouseDown;
			tb.MouseMove += TextBox_MouseMove;
			tb.MouseUp += TextBox_MouseUp;
			tb.MouseWheel += TextBox_MouseWheel;

			_activeTextBox = tb;
			Controls.Add(tb);
			tb.BringToFront();
			tb.Focus();
			// Place caret at end of any seeded text.
			tb.SelectionStart = tb.TextLength;
			ResizeTextBox();

			// Hide buttons while text box is active
			copyButton.Visible = false;
			saveButton.Visible = false;
			saveAndCopyPathButton.Visible = false;
		}

		private void ResizeTextBox()
		{
			if (_activeTextBox == null || capturedImage == null) return;

			// Maximum width the box may occupy before wrapping (click point to image right edge)
			int maxWidth = capturedImage.Width - _textBoxImageOrigin.X;
			int minWidth = (int)(_activeTextFontSize) + TextPadding * 2;
			if (maxWidth < minWidth) maxWidth = minWidth;

			string textForMeasure = _activeTextBox.Text.Length > 0 ? _activeTextBox.Text : " ";

			// Natural content width. Measure with the SAME flags used for wrapping/drawing so
			// the widths agree -- otherwise a single line can wrap by a pixel after committing.
			Size natural = TextRenderer.MeasureText(
				textForMeasure,
				_activeTextBox.Font,
				new Size(int.MaxValue, int.MaxValue),
				TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

			// +1px guard so the resolved width is never a hair narrower than the natural
			// width (which would force an unwanted wrap).
			int contentWidth = Math.Min(natural.Width + TextPadding * 2 + 1, maxWidth);
			if (contentWidth < minWidth) contentWidth = minWidth;
			_activeTextBox.Width = contentWidth;

			// Measure the required height for all text at the resolved width (wraps if needed)
			Size measured = TextRenderer.MeasureText(
				textForMeasure,
				_activeTextBox.Font,
				new Size(contentWidth - TextPadding * 2, int.MaxValue),
				TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

			int desiredHeight = measured.Height + TextPadding * 2;

			// Clamp so the bottom doesn't exceed the image bottom edge
			int maxHeight = capturedImage.Height - _textBoxImageOrigin.Y;
			if (maxHeight < (int)(_activeTextFontSize + TextPadding * 2))
				maxHeight = (int)(_activeTextFontSize + TextPadding * 2);

			_activeTextBox.Height = Math.Min(desiredHeight, maxHeight);
		}

		private void TextBox_TextChanged(object? sender, EventArgs e)
		{
			ResizeTextBox();
		}

		private void TextBox_KeyDown(object? sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Enter && !e.Shift)
			{
				e.SuppressKeyPress = true;
				CommitText();
			}
			// Escape is handled by ProcessCmdKey
		}

		private void TextBox_Leave(object? sender, EventArgs e)
		{
			// Only commit on leave if the text box is still active (not already committed/cancelled)
			if (_activeTextBox != null)
				CommitText();
		}

		private void TextBox_MouseDown(object? sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left && _activeTextBox != null)
			{
				// Work in screen coordinates so the grab anchor stays fixed as the box moves.
				Point screenMouse = _activeTextBox.PointToScreen(e.Location);
				// Box top-left in screen coords:
				Point boxScreen = PointToScreen(_activeTextBox.Location);
				_tbDragMouseDown = screenMouse;
				// Offset from the box's top-left to the cursor, in screen space (stays constant).
				_tbDragGrabOffset = new Point(
					screenMouse.X - boxScreen.X,
					screenMouse.Y - boxScreen.Y);
				_tbDragging = false;
			}
		}

		private void TextBox_MouseMove(object? sender, MouseEventArgs e)
		{
			if (_activeTextBox == null || !_tbDragMouseDown.HasValue) return;
			if (e.Button != MouseButtons.Left) return;

			Point screenMouse = _activeTextBox.PointToScreen(e.Location);
			int dx = screenMouse.X - _tbDragMouseDown.Value.X;
			int dy = screenMouse.Y - _tbDragMouseDown.Value.Y;

			if (!_tbDragging && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4))
				_tbDragging = true;

			if (_tbDragging && capturedImage != null)
			{
				// Desired box top-left in screen coords = cursor - fixed grab offset,
				// then convert back to this form's client coords.
				Point desiredScreen = new Point(
					screenMouse.X - _tbDragGrabOffset.X,
					screenMouse.Y - _tbDragGrabOffset.Y);
				Point desiredClient = PointToClient(desiredScreen);
				int desiredX = desiredClient.X;
				int desiredY = desiredClient.Y;

				// Clamp the box's TOP-LEFT to the image area. We clamp the origin (not the
				// far edge) so the box can be moved right up to the edge; ResizeTextBox then
				// shrinks the wrap width and re-wraps the text as the box nears the edge.
				int newFormX = Math.Max(0, Math.Min(desiredX, capturedImage.Width - 1));
				int newFormY = Math.Max(headerPanel.Height,
					Math.Min(desiredY, headerPanel.Height + capturedImage.Height - 1));

				// Apply position + re-fit in one layout pass to avoid intermediate repaints.
				_activeTextBox.SuspendLayout();
				_activeTextBox.Location = new Point(newFormX, newFormY);
				_textBoxImageOrigin = new Point(newFormX, newFormY - headerPanel.Height);

				// Re-fit width/height to the new available space (handles wrapping near the
				// right edge). The coordinate math above is stable, so this no longer jitters.
				ResizeTextBox();
				_activeTextBox.ResumeLayout();
			}
		}

		private void TextBox_MouseUp(object? sender, MouseEventArgs e)
		{
			bool wasDragging = _tbDragging;
			_tbDragMouseDown = null;
			_tbDragging = false;

			// The new position changes the available wrap width; re-fit once now that the drag ended.
			if (wasDragging)
				ResizeTextBox();
		}

		private void TextBox_MouseWheel(object? sender, MouseEventArgs e)
		{
			if (_activeTextBox == null) return;

			float delta = e.Delta > 0 ? 1f : -1f;
			float newSize = Math.Clamp(_activeTextFontSize + delta, MinFontSize * scaleFactor, MaxFontSize * scaleFactor);
			if (newSize == _activeTextFontSize) return;

			_activeTextFontSize = newSize;
			Font oldFont = _activeTextBox.Font;
			_activeTextBox.Font = new Font("Segoe UI", _activeTextFontSize, GraphicsUnit.Pixel);
			oldFont.Dispose();
			ResizeTextBox();
		}

		private void CommitText()
		{
			if (_activeTextBox == null) return;

			var tb = _activeTextBox;
			_activeTextBox = null; // clear first to prevent Leave re-entry
			TextAnnotation? original = _editingOriginal;
			_editingOriginal = null;

			string text = tb.Text.Trim();

			if (capturedImage != null)
			{
				if (original != null)
				{
					// Editing an existing annotation.
					if (text.Length == 0)
					{
						// Cleared -> delete the original.
						_history.Push(new DeleteTextOp(original));
						_redoStack.Clear();
						RedrawImage();
					}
					else
					{
						var edited = new TextAnnotation(_textBoxImageOrigin, tb.Text, _activeTextFontSize);
						// Only record a change if something actually differs.
						if (edited != original)
						{
							_history.Push(new EditTextOp(original, edited));
							_redoStack.Clear();
						}
						// Repaint either way: the canvas was drawn excluding the original.
						RedrawImage();
					}
				}
				else if (text.Length > 0)
				{
					// Brand-new annotation.
					var annotation = new TextAnnotation(_textBoxImageOrigin, tb.Text, _activeTextFontSize);
					_history.Push(new AddTextOp(annotation));
					_redoStack.Clear();
					DrawTextAnnotationOnBitmap(annotation);
					Invalidate();
				}
			}

			RemoveTextBox(tb);
		}

		private void CancelText()
		{
			if (_activeTextBox == null) return;
			var tb = _activeTextBox;
			_activeTextBox = null;
			bool wasEditing = _editingOriginal != null;
			_editingOriginal = null;
			RemoveTextBox(tb);

			// If we were editing, the canvas had the original excluded; restore it.
			if (wasEditing)
				RedrawImage();
		}

		private void RemoveTextBox(TextBox tb)
		{
			tb.TextChanged -= TextBox_TextChanged;
			tb.KeyDown -= TextBox_KeyDown;
			tb.Leave -= TextBox_Leave;
			tb.MouseDown -= TextBox_MouseDown;
			tb.MouseMove -= TextBox_MouseMove;
			tb.MouseUp -= TextBox_MouseUp;
			tb.MouseWheel -= TextBox_MouseWheel;

			Font tbFont = tb.Font;
			Controls.Remove(tb);
			tb.Dispose();
			tbFont.Dispose();

			_tbDragMouseDown = null;
			_tbDragging = false;

			// Return focus to form so keyboard shortcuts work
			Focus();
		}

		// Computes the rendered background rectangle (image coordinates) for a text annotation.
		// Used for both drawing and hit-testing so they stay in sync.
		private Rectangle GetTextAnnotationBounds(TextAnnotation annotation)
		{
			if (capturedImage == null) return Rectangle.Empty;

			using Font font = new Font("Segoe UI", annotation.FontSize, GraphicsUnit.Pixel);
			int maxWidth = capturedImage.Width - annotation.Location.X;
			if (maxWidth < 1) return Rectangle.Empty;

			// Auto-size to content width, capped at the available width (wraps only if needed).
			// Mirrors ResizeTextBox -- same flags as the wrap/draw step so widths agree and a
			// single line doesn't wrap by a pixel after committing.
			Size natural = TextRenderer.MeasureText(
				annotation.Text,
				font,
				new Size(int.MaxValue, int.MaxValue),
				TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

			// +1px guard (matches ResizeTextBox) so a single line doesn't wrap by a hair.
			int boxWidth = Math.Min(natural.Width + TextPadding * 2 + 1, maxWidth);

			Size measured = TextRenderer.MeasureText(
				annotation.Text,
				font,
				new Size(boxWidth - TextPadding * 2, int.MaxValue),
				TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

			int boxHeight = measured.Height + TextPadding * 2;

			// Clamp to image bounds
			boxHeight = Math.Min(boxHeight, capturedImage.Height - annotation.Location.Y);

			return new Rectangle(annotation.Location.X, annotation.Location.Y, boxWidth, boxHeight);
		}

		private void DrawTextAnnotationOnBitmap(TextAnnotation annotation)
		{
			if (capturedImage == null) return;

			Rectangle bgRect = GetTextAnnotationBounds(annotation);
			if (bgRect.IsEmpty) return;

			Rectangle textRect = new Rectangle(
				bgRect.X + TextPadding, bgRect.Y + TextPadding,
				bgRect.Width - TextPadding * 2, bgRect.Height - TextPadding * 2);

			using Font font = new Font("Segoe UI", annotation.FontSize, GraphicsUnit.Pixel);
			using Graphics g = Graphics.FromImage(capturedImage);
			using SolidBrush bgBrush = new SolidBrush(TextBgColor);
			g.FillRectangle(bgBrush, bgRect);
			TextRenderer.DrawText(g, annotation.Text, font, textRect, Color.White,
				TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
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

			string targetDir = @"Downloads\ScreenGrab";
			string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), targetDir);
			Directory.CreateDirectory(downloadsPath);
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
			// Dismiss any active text box without committing
			if (_activeTextBox != null)
			{
				var tb = _activeTextBox;
				_activeTextBox = null;
				RemoveTextBox(tb);
			}
			_editingOriginal = null;

			// Reset to initial state to prepare for next capture
			FormBorderStyle = FormBorderStyle.None;
			AutoScrollPosition = Point.Empty;
			dragStartPoint = null;
			dragRectangle = null;

			// Clear undo/redo stacks
			_history.Clear();
			_redoStack.Clear();
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

			// Record an add-rectangle operation
			_history.Push(new AddRectOp(new RectAnnotation(selection)));
			// Adding a new operation invalidates the redo history
			_redoStack.Clear();

			// Draw the rectangle on the image
			using Graphics g = Graphics.FromImage(capturedImage);
			using Pen redPen = new Pen(Color.Red, highlightThickness);
			DrawRoundedRectangle(g, redPen, selection, highlightCornerRadius);
			Invalidate(); // Trigger repaint to show the updated image
		}

		private void UndoLastAnnotation()
		{
			if (capturedImage == null || _history.Count == 0) return;

			IOperation last = _history.Pop();
			_redoStack.Push(last);
			// Edits replace in place, so re-project and redraw the whole image.
			RedrawImage();
		}

		private void RedoAnnotation()
		{
			if (capturedImage == null || _redoStack.Count == 0) return;

			IOperation op = _redoStack.Pop();
			_history.Push(op);
			// Re-project and redraw so edit operations are applied correctly.
			RedrawImage();
		}

		private void RedrawImage() => RedrawImageCore(null);

		// Repaints the canvas with the live annotation set, optionally skipping one annotation
		// (used while re-editing so the editor isn't drawn over a stale burned-in copy).
		private void RedrawImageExcluding(IAnnotation exclude) => RedrawImageCore(exclude);

		private void RedrawImageCore(IAnnotation? exclude)
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
				g.DrawImage(originalImage!, 0, 0);
			}

			// Redraw the projected (live) annotation set in draw order
			using (Graphics g = Graphics.FromImage(capturedImage))
			{
				using Pen redPen = new Pen(Color.Red, highlightThickness);
				foreach (IAnnotation annotation in ProjectAnnotations())
				{
					if (exclude != null && ReferenceEquals(annotation, exclude))
						continue;
					if (annotation is RectAnnotation r)
						DrawRoundedRectangle(g, redPen, r.Rect, highlightCornerRadius);
					else if (annotation is TextAnnotation t)
						DrawTextAnnotationOnBitmap(t);
				}
			}

			Invalidate();
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
