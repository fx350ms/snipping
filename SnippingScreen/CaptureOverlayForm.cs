namespace SnippingScreen;

internal sealed class CaptureOverlayForm : Form
{
    private readonly Bitmap _screenImage;
    private readonly Rectangle _virtualBounds;
    private readonly CaptureMode _mode;
    private Point _startPoint;
    private Rectangle _selection;
    private bool _dragging;

    public Bitmap? CapturedImage { get; private set; }

    public CaptureOverlayForm(CaptureMode mode)
    {
        _mode = mode;
        _virtualBounds = SystemInformation.VirtualScreen;
        _screenImage = new Bitmap(_virtualBounds.Width, _virtualBounds.Height);

        using (var g = Graphics.FromImage(_screenImage))
        {
            g.CopyFromScreen(_virtualBounds.Location, Point.Empty, _virtualBounds.Size);
        }

        StartPosition = FormStartPosition.Manual;
        Bounds = _virtualBounds;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Cursor = mode == CaptureMode.Region ? Cursors.Cross : Cursors.Hand;
        KeyPreview = true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_mode == CaptureMode.FullScreen)
        {
            CapturedImage = (Bitmap)_screenImage.Clone();
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(_screenImage, 0, 0);

        using var shade = new SolidBrush(Color.FromArgb(120, Color.Black));
        e.Graphics.FillRectangle(shade, ClientRectangle);

        if (_selection.Width > 0 && _selection.Height > 0)
        {
            e.Graphics.SetClip(_selection);
            e.Graphics.DrawImageUnscaled(_screenImage, 0, 0);
            e.Graphics.ResetClip();
            using var pen = new Pen(Color.DeepSkyBlue, 2);
            e.Graphics.DrawRectangle(pen, _selection);
        }

        var text = _mode == CaptureMode.Window
            ? "Click cửa sổ/vùng muốn chụp. Esc để hủy."
            : "Kéo chuột để chọn vùng. Enter để chụp, Esc để hủy.";
        TextRenderer.DrawText(e.Graphics, text, Font, new Point(16, 16), Color.White);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_mode == CaptureMode.Window)
        {
            _selection = GetWindowLikeArea(e.Location);
            FinishCapture();
            return;
        }

        _dragging = true;
        _startPoint = e.Location;
        _selection = new Rectangle(e.Location, Size.Empty);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _selection = NormalizeRectangle(_startPoint, e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_dragging)
        {
            return;
        }

        _dragging = false;
        _selection = NormalizeRectangle(_startPoint, e.Location);
        if (_selection.Width >= 4 && _selection.Height >= 4)
        {
            FinishCapture();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (e.KeyCode == Keys.Enter && _selection.Width > 0 && _selection.Height > 0)
        {
            FinishCapture();
        }
    }

    private void FinishCapture()
    {
        if (_selection.Width <= 0 || _selection.Height <= 0)
        {
            return;
        }

        CapturedImage = _screenImage.Clone(_selection, _screenImage.PixelFormat);
        DialogResult = DialogResult.OK;
        Close();
    }

    private Rectangle GetWindowLikeArea(Point point)
    {
        var screenPoint = new Point(point.X + _virtualBounds.Left, point.Y + _virtualBounds.Top);
        var bounds = NativeMethods.FindSmallestVisibleWindowAt(screenPoint, Handle) ?? Screen.FromPoint(screenPoint).Bounds;
        return new Rectangle(
            bounds.Left - _virtualBounds.Left,
            bounds.Top - _virtualBounds.Top,
            bounds.Width,
            bounds.Height);
    }

    private static Rectangle NormalizeRectangle(Point a, Point b)
    {
        return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _screenImage.Dispose();
        }

        base.Dispose(disposing);
    }
}
