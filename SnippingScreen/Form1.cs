using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SnippingScreen
{
    public partial class Form1 : Form
    {
        private readonly AppSettings _settings;
        private readonly ImageUploadService _uploadService;
        private readonly PictureBox _canvas = new();
        private readonly ComboBox _captureMode = new();
        private readonly ComboBox _format = new();
        private readonly TextBox _urlText = new();
        private readonly Button _uploadButton = new();
        private readonly Button _copyLinkButton = new();
        private readonly ProgressBar _uploadProgress = new();
        private readonly Label _uploadState = new();
        private readonly ToolStripStatusLabel _status = new();

        private Bitmap? _currentImage;
        private Bitmap? _shapePreview;
        private EditorTool _tool = EditorTool.Pen;
        private Color _drawColor = Color.Red;
        private int _strokeWidth = 4;
        private bool _drawing;
        private Point _startPoint;
        private Point _lastPoint;

        public Form1()
        {
            _settings = AppSettings.Load();
            _uploadService = new ImageUploadService(_settings);
            InitializeComponent();
            BuildInterface();
            RegisterGlobalHotKey();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WmHotKey && m.WParam.ToInt32() == NativeMethods.HotKeyId)
            {
                BeginCapture();
                return;
            }

            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.N:
                    BeginCapture();
                    return true;
                case Keys.Control | Keys.C:
                    CopyToClipboard();
                    return true;
                case Keys.Control | Keys.S:
                    SaveImage();
                    return true;
                case Keys.Control | Keys.U:
                    _ = UploadImageAsync();
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BuildInterface()
        {
            Text = "PBT - Snipping Screen";
            MinimumSize = new Size(980, 650);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            var toolbar = BuildToolbar();

            _canvas.Dock = DockStyle.Fill;
            _canvas.BackColor = Color.FromArgb(34, 34, 34);
            _canvas.SizeMode = PictureBoxSizeMode.Zoom;
            _canvas.MouseDown += CanvasMouseDown;
            _canvas.MouseMove += CanvasMouseMove;
            _canvas.MouseUp += CanvasMouseUp;

            var uploadPanel = BuildUploadPanel();

            var bottom = new StatusStrip();
            bottom.Items.Add(_status);
            _status.Text = "Ctrl+Win+Print: capture | Ctrl+C: copy image | Ctrl+S: save | Ctrl+U: upload | Ctrl+N: new";

            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(_canvas, 0, 1);
            root.Controls.Add(uploadPanel, 0, 2);
            root.Controls.Add(bottom, 0, 3);
            Controls.Add(root);
        }

        private Control BuildToolbar()
        {
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 8, 10, 6),
                WrapContents = true,
                AutoScroll = true
            };

            _captureMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _captureMode.Width = 128;
            _captureMode.Items.AddRange(["Region", "Window", "Full screen"]);
            _captureMode.SelectedIndex = 0;

            _format.DropDownStyle = ComboBoxStyle.DropDownList;
            _format.Width = 78;
            _format.Items.AddRange(["PNG", "JPG", "GIF"]);
            _format.SelectedIndex = 0;

            var width = new NumericUpDown { Minimum = 1, Maximum = 24, Value = _strokeWidth, Width = 54 };
            width.ValueChanged += (_, _) => _strokeWidth = (int)width.Value;

            toolbar.Controls.Add(MakeButton("New", (_, _) => BeginCapture()));
            toolbar.Controls.Add(MakeButton("Copy", (_, _) => CopyToClipboard()));
            toolbar.Controls.Add(MakeButton("Save", (_, _) => SaveImage()));
            _uploadButton.Text = "Upload";
            _uploadButton.AutoSize = true;
            _uploadButton.Height = 32;
            _uploadButton.Margin = new Padding(3, 0, 3, 0);
            _uploadButton.Click += async (_, _) => await UploadImageAsync();
            toolbar.Controls.Add(_uploadButton);
            toolbar.Controls.Add(MakeLabel("Capture"));
            toolbar.Controls.Add(_captureMode);
            toolbar.Controls.Add(MakeLabel("Format"));
            toolbar.Controls.Add(_format);
            toolbar.Controls.Add(MakeSeparator());
            toolbar.Controls.Add(ToolButton("Pen", EditorTool.Pen));
            toolbar.Controls.Add(ToolButton("Box", EditorTool.Rectangle));
            toolbar.Controls.Add(ToolButton("Line", EditorTool.Line));
            toolbar.Controls.Add(ToolButton("Highlight", EditorTool.Highlight));
            toolbar.Controls.Add(ColorButton("Red", Color.Red));
            toolbar.Controls.Add(ColorButton("Yellow", Color.Gold));
            toolbar.Controls.Add(ColorButton("Blue", Color.DeepSkyBlue));
            toolbar.Controls.Add(MakeLabel("Stroke"));
            toolbar.Controls.Add(width);

            return toolbar;
        }

        private Control BuildUploadPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                Padding = new Padding(10, 5, 10, 5)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

            _uploadState.Text = "Ready";
            _uploadState.AutoSize = false;
            _uploadState.Dock = DockStyle.Fill;
            _uploadState.TextAlign = ContentAlignment.MiddleLeft;

            _urlText.Dock = DockStyle.Fill;
            _urlText.ReadOnly = true;
            _urlText.PlaceholderText = "Image link from API";

            _copyLinkButton.Text = "Copy link";
            _copyLinkButton.Dock = DockStyle.Fill;
            _copyLinkButton.Enabled = false;
            _copyLinkButton.Click += (_, _) => CopyUploadLink();

            _uploadProgress.Dock = DockStyle.Fill;
            _uploadProgress.Style = ProgressBarStyle.Blocks;
            _uploadProgress.Visible = false;

            panel.Controls.Add(_uploadState, 0, 0);
            panel.Controls.Add(_urlText, 1, 0);
            panel.Controls.Add(_copyLinkButton, 2, 0);
            panel.Controls.Add(_uploadProgress, 3, 0);

            return panel;
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 32, Margin = new Padding(3, 0, 3, 0) };
            button.Click += onClick;
            return button;
        }

        private static Label MakeLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Padding = new Padding(10, 7, 0, 0) };
        }

        private static Label MakeSeparator()
        {
            return new Label { Text = "|", AutoSize = true, Padding = new Padding(10, 7, 4, 0), ForeColor = Color.Gray };
        }

        private Button ToolButton(string text, EditorTool tool)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 32, Margin = new Padding(3, 0, 3, 0) };
            button.Click += (_, _) =>
            {
                _tool = tool;
                _status.Text = $"Tool: {text}";
            };
            return button;
        }

        private Button ColorButton(string text, Color color)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 32,
                Margin = new Padding(3, 0, 3, 0),
                BackColor = color,
                ForeColor = color.GetBrightness() < 0.45 ? Color.White : Color.Black
            };
            button.Click += (_, _) =>
            {
                _drawColor = color;
                _status.Text = $"Color: {text}";
            };
            return button;
        }

        private async void BeginCapture()
        {
            Hide();
            await Task.Delay(160);
            try
            {
                CaptureNow();
            }
            finally
            {
                Show();
                Activate();
            }
        }

        private void CaptureNow()
        {
            var mode = _captureMode.SelectedIndex switch
            {
                1 => CaptureMode.Window,
                2 => CaptureMode.FullScreen,
                _ => CaptureMode.Region
            };

            using var overlay = new CaptureOverlayForm(mode);
            if (overlay.ShowDialog() == DialogResult.OK && overlay.CapturedImage != null)
            {
                SetCurrentImage(overlay.CapturedImage);
                overlay.CapturedImage.Dispose();
                if (_currentImage != null)
                {
                    Clipboard.SetImage(_currentImage);
                }

                _status.Text = "Captured, copied to clipboard, ready to annotate.";
            }
            else
            {
                _status.Text = "Capture cancelled.";
            }
        }

        private void CanvasMouseDown(object? sender, MouseEventArgs e)
        {
            if (_currentImage == null || e.Button != MouseButtons.Left || !TryGetImagePoint(e.Location, out var point))
            {
                return;
            }

            _drawing = true;
            _startPoint = point;
            _lastPoint = point;
            _shapePreview?.Dispose();
            _shapePreview = (Bitmap)_currentImage.Clone();
        }

        private void CanvasMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_drawing || _currentImage == null || !TryGetImagePoint(e.Location, out var point))
            {
                return;
            }

            if (_tool == EditorTool.Pen || _tool == EditorTool.Highlight)
            {
                using var g = Graphics.FromImage(_currentImage);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = CreatePen(_tool == EditorTool.Highlight);
                g.DrawLine(pen, _lastPoint, point);
                _lastPoint = point;
                _canvas.Image = _currentImage;
                _canvas.Invalidate();
                return;
            }

            var temp = (Bitmap)_shapePreview!.Clone();
            using (var g = Graphics.FromImage(temp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = CreatePen(false);
                if (_tool == EditorTool.Rectangle)
                {
                    g.DrawRectangle(pen, NormalizeRectangle(_startPoint, point));
                }
                else
                {
                    g.DrawLine(pen, _startPoint, point);
                }
            }

            var old = _canvas.Image;
            _canvas.Image = temp;
            if (!ReferenceEquals(old, _currentImage))
            {
                old?.Dispose();
            }
        }

        private void CanvasMouseUp(object? sender, MouseEventArgs e)
        {
            if (!_drawing || _currentImage == null)
            {
                return;
            }

            _drawing = false;
            if (TryGetImagePoint(e.Location, out var point) && _tool is EditorTool.Rectangle or EditorTool.Line)
            {
                using var g = Graphics.FromImage(_currentImage);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = CreatePen(false);
                if (_tool == EditorTool.Rectangle)
                {
                    g.DrawRectangle(pen, NormalizeRectangle(_startPoint, point));
                }
                else
                {
                    g.DrawLine(pen, _startPoint, point);
                }
            }

            if (!ReferenceEquals(_canvas.Image, _currentImage))
            {
                _canvas.Image?.Dispose();
            }

            _canvas.Image = _currentImage;
            _shapePreview?.Dispose();
            _shapePreview = null;
            _status.Text = "Annotation updated.";
        }

        private Pen CreatePen(bool highlight)
        {
            var penColor = highlight ? Color.FromArgb(110, _drawColor) : _drawColor;
            return new Pen(penColor, highlight ? Math.Max(10, _strokeWidth * 3) : _strokeWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
        }

        private bool TryGetImagePoint(Point controlPoint, out Point imagePoint)
        {
            imagePoint = Point.Empty;
            if (_currentImage == null || _canvas.ClientSize.Width <= 0 || _canvas.ClientSize.Height <= 0)
            {
                return false;
            }

            var imageRect = GetImageDisplayRectangle();
            if (!imageRect.Contains(controlPoint) || imageRect.Width <= 0 || imageRect.Height <= 0)
            {
                return false;
            }

            var x = (controlPoint.X - imageRect.Left) * _currentImage.Width / (double)imageRect.Width;
            var y = (controlPoint.Y - imageRect.Top) * _currentImage.Height / (double)imageRect.Height;
            imagePoint = new Point((int)Math.Clamp(x, 0, _currentImage.Width - 1), (int)Math.Clamp(y, 0, _currentImage.Height - 1));
            return true;
        }

        private Rectangle GetImageDisplayRectangle()
        {
            if (_currentImage == null)
            {
                return Rectangle.Empty;
            }

            var imageRatio = _currentImage.Width / (double)_currentImage.Height;
            var boxRatio = _canvas.ClientSize.Width / (double)_canvas.ClientSize.Height;
            if (imageRatio > boxRatio)
            {
                var width = _canvas.ClientSize.Width;
                var height = (int)(width / imageRatio);
                return new Rectangle(0, (_canvas.ClientSize.Height - height) / 2, width, height);
            }

            var h = _canvas.ClientSize.Height;
            var w = (int)(h * imageRatio);
            return new Rectangle((_canvas.ClientSize.Width - w) / 2, 0, w, h);
        }

        private static Rectangle NormalizeRectangle(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        private void CopyToClipboard()
        {
            if (_currentImage == null)
            {
                _status.Text = "No image to copy.";
                return;
            }

            Clipboard.SetImage(_currentImage);
            _status.Text = "Image copied to clipboard.";
        }

        private void CopyUploadLink()
        {
            if (string.IsNullOrWhiteSpace(_urlText.Text))
            {
                _status.Text = "No upload link to copy.";
                return;
            }

            Clipboard.SetText(_urlText.Text);
            _status.Text = "Upload link copied.";
        }

        private void SaveImage()
        {
            if (_currentImage == null)
            {
                _status.Text = "No image to save.";
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "Save image",
                FileName = $"snip-{DateTime.Now:yyyyMMdd-HHmmss}.{SelectedExtension()}",
                Filter = "PNG Image|*.png|JPEG Image|*.jpg;*.jpeg|GIF Image|*.gif",
                FilterIndex = _format.SelectedIndex + 1
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            SaveCurrentImage(dialog.FileName, SelectedImageFormat());
            _status.Text = $"Saved: {dialog.FileName}";
        }

        private void SaveCurrentImage(string fileName, ImageFormat format)
        {
            if (_currentImage == null)
            {
                return;
            }

            if (format.Guid != ImageFormat.Jpeg.Guid)
            {
                _currentImage.Save(fileName, format);
                return;
            }

            using var jpeg = new Bitmap(_currentImage.Width, _currentImage.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(jpeg))
            {
                g.Clear(Color.White);
                g.DrawImageUnscaled(_currentImage, 0, 0);
            }

            jpeg.Save(fileName, ImageFormat.Jpeg);
        }

        private async Task UploadImageAsync()
        {
            if (_currentImage == null)
            {
                _status.Text = "No image to upload.";
                return;
            }

            try
            {
                SetUploadUi(true, "Uploading...");
                var url = await _uploadService.UploadAsync(_currentImage);
                _urlText.Text = url;
                _copyLinkButton.Enabled = !string.IsNullOrWhiteSpace(url);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    Clipboard.SetText(url);
                }

                SetUploadUi(false, "Upload done");
                _status.Text = "Upload completed. Link copied to clipboard.";
            }
            catch (Exception ex)
            {
                SetUploadUi(false, "Upload failed");
                _status.Text = $"Upload failed: {ex.Message}";
                MessageBox.Show(this, ex.Message, "Upload failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetUploadUi(bool uploading, string state)
        {
            UseWaitCursor = uploading;
            _uploadButton.Enabled = !uploading;
            _uploadState.Text = uploading ? $"{state} Please wait" : state;
            _uploadProgress.Visible = uploading;
            _uploadProgress.Style = uploading ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            _uploadProgress.MarqueeAnimationSpeed = uploading ? 30 : 0;
        }

        private void SetCurrentImage(Image image)
        {
            var next = new Bitmap(image);
            if (!ReferenceEquals(_canvas.Image, _currentImage))
            {
                _canvas.Image?.Dispose();
            }

            _currentImage?.Dispose();
            _shapePreview?.Dispose();
            _shapePreview = null;
            _currentImage = next;
            _canvas.Image = _currentImage;
            _urlText.Clear();
            _copyLinkButton.Enabled = false;
            SetUploadUi(false, "Ready");
        }

        private ImageFormat SelectedImageFormat()
        {
            return _format.SelectedIndex switch
            {
                1 => ImageFormat.Jpeg,
                2 => ImageFormat.Gif,
                _ => ImageFormat.Png
            };
        }

        private string SelectedExtension()
        {
            return _format.SelectedIndex switch
            {
                1 => "jpg",
                2 => "gif",
                _ => "png"
            };
        }

        private void RegisterGlobalHotKey()
        {
            NativeMethods.RegisterHotKey(Handle, NativeMethods.HotKeyId, NativeMethods.ModControl | NativeMethods.ModWin, NativeMethods.VkSnapshot);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            NativeMethods.UnregisterHotKey(Handle, NativeMethods.HotKeyId);
            _shapePreview?.Dispose();
            _currentImage?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
