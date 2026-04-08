using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PdfOverlayCompare
{
    /// <summary>
    /// A panel that displays a rendered PDF page with pan, zoom,
    /// point-picking, snap-to-corner, and arrow-key nudging.
    /// </summary>
    public class PdfViewPanel : Panel
    {
        private Bitmap _pageImage;
        private float _zoom = 1.0f;
        private PointF _panOffset = PointF.Empty;
        private bool _isPanning;
        private Point _panStart;
        private PointF _panOffsetStart;

        private List<PointF> _pickedPoints = new List<PointF>();
        private int _maxPoints = 3;
        private int _selectedPointIndex = -1; // which point is selected for arrow nudge
        private bool _pickingEnabled;
        private string _label = "";

        // Snap settings
        private int _snapRadius = 15; // search radius in image pixels for snap
        private bool _snapEnabled = true;

        // Colors for the 3 calibration points
        private static readonly Color[] PointColors = new Color[]
        {
            Color.Red, Color.LimeGreen, Color.DodgerBlue
        };
        private static readonly string[] PointLabels = new string[] { "1", "2", "3" };

        public event EventHandler PointsChanged;
        public event EventHandler<PointF> PointPicked;

        public PdfViewPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(240, 240, 240);
            TabStop = true; // allow focus for key events
        }

        #region Properties

        public string Label
        {
            get { return _label; }
            set { _label = value; Invalidate(); }
        }

        public Bitmap PageImage
        {
            get { return _pageImage; }
            set
            {
                _pageImage = value;
                _pickedPoints.Clear();
                _selectedPointIndex = -1;
                ZoomToFit();
                Invalidate();
            }
        }

        public bool PickingEnabled
        {
            get { return _pickingEnabled; }
            set
            {
                _pickingEnabled = value;
                Cursor = value ? Cursors.Cross : Cursors.Default;
                if (!value) _selectedPointIndex = -1;
            }
        }

        public bool SnapEnabled
        {
            get { return _snapEnabled; }
            set { _snapEnabled = value; }
        }

        public int SnapRadius
        {
            get { return _snapRadius; }
            set { _snapRadius = Math.Max(3, Math.Min(50, value)); }
        }

        public List<PointF> PickedPoints => _pickedPoints;

        public float Zoom
        {
            get { return _zoom; }
            set
            {
                _zoom = Math.Max(0.05f, Math.Min(20f, value));
                Invalidate();
            }
        }

        #endregion

        #region Coordinate Conversion

        public PointF ScreenToImage(Point screen)
        {
            float ix = (screen.X - _panOffset.X) / _zoom;
            float iy = (screen.Y - _panOffset.Y) / _zoom;
            return new PointF(ix, iy);
        }

        public PointF ImageToScreen(PointF image)
        {
            float sx = image.X * _zoom + _panOffset.X;
            float sy = image.Y * _zoom + _panOffset.Y;
            return new PointF(sx, sy);
        }

        #endregion

        #region Zoom / Pan

        public void ZoomToFit()
        {
            if (_pageImage == null || Width == 0 || Height == 0)
                return;

            // Reserve space for the label bar at top (~28px)
            int reservedTop = 28;
            int availW = Width;
            int availH = Height - reservedTop;
            if (availH < 50) availH = Height;

            float zx = (float)availW / _pageImage.Width;
            float zy = (float)availH / _pageImage.Height;
            _zoom = Math.Min(zx, zy) * 0.95f;

            // Center within available area
            float imgDispW = _pageImage.Width * _zoom;
            float imgDispH = _pageImage.Height * _zoom;
            _panOffset = new PointF(
                (availW - imgDispW) / 2f,
                reservedTop + (availH - imgDispH) / 2f);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_pageImage == null) return;

            PointF imgBefore = ScreenToImage(e.Location);
            float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
            _zoom = Math.Max(0.05f, Math.Min(20f, _zoom * factor));
            PointF imgAfter = ScreenToImage(e.Location);

            _panOffset = new PointF(
                _panOffset.X + (imgAfter.X - imgBefore.X) * _zoom,
                _panOffset.Y + (imgAfter.Y - imgBefore.Y) * _zoom);

            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus(); // grab focus for arrow keys

            if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right)
            {
                _isPanning = true;
                _panStart = e.Location;
                _panOffsetStart = _panOffset;
                Cursor = Cursors.SizeAll;
            }
            else if (e.Button == MouseButtons.Left && _pickingEnabled && _pageImage != null)
            {
                PointF imgPt = ScreenToImage(e.Location);

                // Check bounds
                if (imgPt.X >= 0 && imgPt.Y >= 0 &&
                    imgPt.X < _pageImage.Width && imgPt.Y < _pageImage.Height)
                {
                    // Try snap to nearest corner
                    if (_snapEnabled)
                    {
                        imgPt = SnapToCorner(imgPt);
                    }

                    if (_pickedPoints.Count < _maxPoints)
                    {
                        _pickedPoints.Add(imgPt);
                        _selectedPointIndex = _pickedPoints.Count - 1;
                    }
                    else
                    {
                        _pickedPoints.RemoveAt(0);
                        _pickedPoints.Add(imgPt);
                        _selectedPointIndex = _pickedPoints.Count - 1;
                    }
                    PointsChanged?.Invoke(this, EventArgs.Empty);
                    PointPicked?.Invoke(this, imgPt);
                    Invalidate();
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isPanning)
            {
                _panOffset = new PointF(
                    _panOffsetStart.X + (e.X - _panStart.X),
                    _panOffsetStart.Y + (e.Y - _panStart.Y));
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isPanning)
            {
                _isPanning = false;
                Cursor = _pickingEnabled ? Cursors.Cross : Cursors.Default;
            }
        }

        #endregion

        #region Arrow Key Nudging

        protected override bool IsInputKey(Keys keyData)
        {
            // Allow arrow keys to reach OnKeyDown instead of being
            // consumed by the container for focus navigation
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.Tab:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (_selectedPointIndex < 0 || _selectedPointIndex >= _pickedPoints.Count)
                return;

            // Arrow keys: nudge selected point
            // Normal = 1 image pixel, Shift = 0.1 pixel (sub-pixel)
            float step = e.Shift ? 0.1f : 1.0f;
            PointF pt = _pickedPoints[_selectedPointIndex];
            bool moved = false;

            switch (e.KeyCode)
            {
                case Keys.Left:
                    pt = new PointF(pt.X - step, pt.Y);
                    moved = true;
                    break;
                case Keys.Right:
                    pt = new PointF(pt.X + step, pt.Y);
                    moved = true;
                    break;
                case Keys.Up:
                    pt = new PointF(pt.X, pt.Y - step);
                    moved = true;
                    break;
                case Keys.Down:
                    pt = new PointF(pt.X, pt.Y + step);
                    moved = true;
                    break;
                case Keys.Tab:
                    // Cycle selected point
                    if (_pickedPoints.Count > 0)
                    {
                        _selectedPointIndex = (_selectedPointIndex + 1) % _pickedPoints.Count;
                        Invalidate();
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                    }
                    return;
            }

            if (moved)
            {
                // Clamp to image bounds
                if (_pageImage != null)
                {
                    pt = new PointF(
                        Math.Max(0, Math.Min(_pageImage.Width - 1, pt.X)),
                        Math.Max(0, Math.Min(_pageImage.Height - 1, pt.Y)));
                }
                _pickedPoints[_selectedPointIndex] = pt;
                PointsChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        #endregion

        #region Snap to Corner (Harris Corner Detection)

        /// <summary>
        /// Finds the nearest corner point within the snap radius using
        /// a simplified Harris corner detector. Falls back to the
        /// original click position if no strong corner is found.
        /// </summary>
        private PointF SnapToCorner(PointF clickPt)
        {
            if (_pageImage == null) return clickPt;

            int cx = (int)Math.Round(clickPt.X);
            int cy = (int)Math.Round(clickPt.Y);
            int r = _snapRadius;
            int w = _pageImage.Width;
            int h = _pageImage.Height;

            int x0 = Math.Max(0, cx - r);
            int y0 = Math.Max(0, cy - r);
            int x1 = Math.Min(w - 1, cx + r);
            int y1 = Math.Min(h - 1, cy + r);
            int rw = x1 - x0 + 1;
            int rh = y1 - y0 + 1;

            if (rw < 5 || rh < 5) return clickPt;

            byte[] gray = GetGrayscaleRegion(x0, y0, rw, rh);

            // Harris corner response
            float bestResponse = 0;
            int bestX = cx, bestY = cy;

            for (int ly = 2; ly < rh - 2; ly++)
            {
                for (int lx = 2; lx < rw - 2; lx++)
                {
                    float sumIx2 = 0, sumIy2 = 0, sumIxIy = 0;

                    for (int wy = -1; wy <= 1; wy++)
                    {
                        for (int wx = -1; wx <= 1; wx++)
                        {
                            int py = ly + wy;
                            int px = lx + wx;
                            float ix = (gray[py * rw + px + 1] - gray[py * rw + px - 1]) / 2f;
                            float iy = (gray[(py + 1) * rw + px] - gray[(py - 1) * rw + px]) / 2f;
                            sumIx2 += ix * ix;
                            sumIy2 += iy * iy;
                            sumIxIy += ix * iy;
                        }
                    }

                    float det = sumIx2 * sumIy2 - sumIxIy * sumIxIy;
                    float trace = sumIx2 + sumIy2;
                    float response = det - 0.04f * trace * trace;

                    if (response > bestResponse)
                    {
                        bestResponse = response;
                        bestX = x0 + lx;
                        bestY = y0 + ly;
                    }
                }
            }

            // Only snap if corner response is strong enough
            if (bestResponse > 500)
            {
                return new PointF(bestX, bestY);
            }

            return clickPt;
        }

        private byte[] GetGrayscaleRegion(int rx, int ry, int rw, int rh)
        {
            byte[] gray = new byte[rw * rh];

            BitmapData data = _pageImage.LockBits(
                new Rectangle(rx, ry, rw, rh),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            byte[] pixels = new byte[rw * rh * 4];
            for (int row = 0; row < rh; row++)
            {
                Marshal.Copy(
                    IntPtr.Add(data.Scan0, row * data.Stride),
                    pixels, row * rw * 4, rw * 4);
            }
            _pageImage.UnlockBits(data);

            for (int i = 0; i < rw * rh; i++)
            {
                int pi = i * 4;
                gray[i] = (byte)(0.299 * pixels[pi + 2] + 0.587 * pixels[pi + 1] + 0.114 * pixels[pi + 0]);
            }

            return gray;
        }

        #endregion

        #region Paint

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (_pageImage != null)
            {
                g.TranslateTransform(_panOffset.X, _panOffset.Y);
                g.ScaleTransform(_zoom, _zoom);
                g.DrawImage(_pageImage, 0, 0, _pageImage.Width, _pageImage.Height);
                g.ResetTransform();

                // Draw picked points
                for (int i = 0; i < _pickedPoints.Count; i++)
                {
                    PointF sp = ImageToScreen(_pickedPoints[i]);
                    int radius = 8;
                    Color c = PointColors[i % PointColors.Length];
                    bool selected = (i == _selectedPointIndex);

                    // Cross-hair
                    float penW = selected ? 3f : 2f;
                    using (Pen pen = new Pen(c, penW))
                    {
                        g.DrawLine(pen, sp.X - radius * 2, sp.Y, sp.X + radius * 2, sp.Y);
                        g.DrawLine(pen, sp.X, sp.Y - radius * 2, sp.X, sp.Y + radius * 2);
                        g.DrawEllipse(pen, sp.X - radius, sp.Y - radius, radius * 2, radius * 2);
                    }

                    // Selection indicator (dashed outer ring)
                    if (selected)
                    {
                        using (Pen pen = new Pen(Color.Yellow, 1.5f))
                        {
                            pen.DashStyle = DashStyle.Dash;
                            g.DrawEllipse(pen, sp.X - radius - 4, sp.Y - radius - 4,
                                (radius + 4) * 2, (radius + 4) * 2);
                        }
                    }

                    // Label with pixel coordinates
                    string lbl = PointLabels[i % PointLabels.Length];
                    PointF pt = _pickedPoints[i];
                    string coordStr = string.Format("{0} ({1:F1}, {2:F1})", lbl, pt.X, pt.Y);
                    using (Font f = new Font("Arial", 8, FontStyle.Bold))
                    using (Brush bg = new SolidBrush(Color.FromArgb(180, 255, 255, 255)))
                    using (Brush br = new SolidBrush(c))
                    {
                        var sz = g.MeasureString(coordStr, f);
                        g.FillRectangle(bg, sp.X + radius + 2, sp.Y - radius - 2, sz.Width + 2, sz.Height);
                        g.DrawString(coordStr, f, br, sp.X + radius + 3, sp.Y - radius - 2);
                    }
                }
            }
            else
            {
                string msg = "No PDF loaded.\nClick 'Open' to load a file.";
                using (Font f = new Font("Segoe UI", 11, FontStyle.Italic))
                using (Brush br = new SolidBrush(Color.Gray))
                {
                    var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString(msg, f, br, ClientRectangle, sf);
                }
            }

            // Draw label in top-left corner
            if (!string.IsNullOrEmpty(_label))
            {
                using (Font f = new Font("Segoe UI", 10, FontStyle.Bold))
                {
                    var sz = g.MeasureString(_label, f);
                    var rect = new RectangleF(4, 4, sz.Width + 8, sz.Height + 4);
                    using (Brush bg = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                        g.FillRectangle(bg, rect);
                    using (Brush br = new SolidBrush(Color.FromArgb(50, 50, 50)))
                        g.DrawString(_label, f, br, 8, 6);
                }
            }

            // Picking status / instructions
            if (_pickingEnabled)
            {
                string status;
                if (_pickedPoints.Count < _maxPoints)
                {
                    status = string.Format("Click to pick point {0} of {1}  |  Snap: {2}",
                        _pickedPoints.Count + 1, _maxPoints,
                        _snapEnabled ? "ON" : "OFF");
                }
                else
                {
                    status = string.Format("All {0} points set  |  Arrow: nudge  Shift+Arrow: fine  Tab: cycle",
                        _maxPoints);
                }
                using (Font f = new Font("Segoe UI", 8))
                {
                    var sz = g.MeasureString(status, f);
                    float x = Width - sz.Width - 8;
                    var rect = new RectangleF(x - 4, 4, sz.Width + 8, sz.Height + 4);
                    using (Brush bg = new SolidBrush(Color.FromArgb(200, 255, 255, 200)))
                        g.FillRectangle(bg, rect);
                    g.DrawString(status, f, Brushes.DarkGreen, x, 6);
                }
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Invalidate();
        }

        #endregion

        public void ClearPoints()
        {
            _pickedPoints.Clear();
            _selectedPointIndex = -1;
            PointsChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }
}
