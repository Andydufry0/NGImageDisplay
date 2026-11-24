using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImageErrBack02
{
    public class PicBox : PictureBox
    {
        private float _zoom = 1.0f;
        private PointF _offset = PointF.Empty;
        private bool _isDragging;
        private Point _lastMousePos;
        private bool _mouseCaptured;
        private const float ZoomEpsilon = 0.0001f;

        public string ImageFilePath { get; set; }
        public bool EnableRightClickPreview { get; set; } = true;
        public bool OwnImage { get; set; } = false;
        public bool EnableDragClamp { get; set; } = false;
        public float MinZoom { get; set; } = 0.1f;
        public float MaxZoom { get; set; } = 10.0f;
        public bool AutoFitOnSetImage { get; set; } = true;
        public bool AutoFitOnResize { get; set; } = true;

        public PicBox()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.Black;
            SizeMode = PictureBoxSizeMode.Normal;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.DoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();

            MouseWheel += OnMouseWheel;
            DoubleClick += OnDoubleClick;
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            MouseLeave += OnMouseLeave;
            LostFocus += OnLostFocus;
        }

        public new Image Image
        {
            get => base.Image;
            set
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(() => { Image = value; }));
                    return;
                }
                if (OwnImage && base.Image != null && !ReferenceEquals(base.Image, value))
                    base.Image.Dispose();
                base.Image = value;
                if (AutoFitOnSetImage) Fit();
                else Invalidate();
            }
        }

        public void Fit()
        {
            if (Image == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;
            float fitW = (float)ClientSize.Width / Image.Width;
            float fitH = (float)ClientSize.Height / Image.Height;
            _zoom = Math.Min(fitW, fitH);
            _offset = PointF.Empty;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            try
            {
                pe.Graphics.Clear(BackColor);
                if (Image == null) return;
                float w = Image.Width * _zoom;
                float h = Image.Height * _zoom;
                float x = (ClientSize.Width - w) / 2f + _offset.X;
                float y = (ClientSize.Height - h) / 2f + _offset.Y;
                pe.Graphics.InterpolationMode = _isDragging
                    ? InterpolationMode.NearestNeighbor
                    : InterpolationMode.HighQualityBicubic;
                pe.Graphics.DrawImage(Image, new RectangleF(x, y, w, h));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PicBox Paint] {ex.Message}");
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (AutoFitOnResize) Fit();
            else if (EnableDragClamp) ClampOffset();
            Invalidate();
        }

        private void OnMouseWheel(object sender, MouseEventArgs e)
        {
            try
            {
                if (Image == null) return;
                float oldZoom = _zoom;
                _zoom *= e.Delta > 0 ? 2.0f : 0.5f;
                _zoom = Math.Max(MinZoom, Math.Min(MaxZoom, _zoom));
                if (Math.Abs(_zoom - oldZoom) < ZoomEpsilon) return;
                float ratio = _zoom / oldZoom;
                float cxOld = (ClientSize.Width - Image.Width * oldZoom) / 2f;
                float cyOld = (ClientSize.Height - Image.Height * oldZoom) / 2f;
                float cxNew = (ClientSize.Width - Image.Width * _zoom) / 2f;
                float cyNew = (ClientSize.Height - Image.Height * _zoom) / 2f;
                Point mp = e.Location;
                _offset.X = mp.X - (mp.X - cxOld - _offset.X) * ratio - cxNew;
                _offset.Y = mp.Y - (mp.Y - cyOld - _offset.Y) * ratio - cyNew;
                if (EnableDragClamp) ClampOffset();
                Invalidate();
            }
            catch (Exception ex) { Debug.WriteLine($"[PicBox Wheel] {ex.Message}"); }
        }

        private void OnDoubleClick(object sender, EventArgs e) => Fit();

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) return;
            if (e.Button == MouseButtons.Left && Image != null)
            {
                _isDragging = true;
                _lastMousePos = e.Location;
                Cursor = Cursors.SizeAll;
                Capture = true;
                _mouseCaptured = true;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            _offset.X += e.X - _lastMousePos.X;
            _offset.Y += e.Y - _lastMousePos.Y;
            _lastMousePos = e.Location;
            if (EnableDragClamp) ClampOffset();
            Invalidate();
        }

        private void OnMouseUp(object sender, MouseEventArgs e) => EndDrag();
        private void OnMouseLeave(object sender, EventArgs e) => EndDrag();
        private void OnLostFocus(object sender, EventArgs e) => EndDrag();

        private void EndDrag()
        {
            if (!_isDragging) return;
            _isDragging = false;
            Cursor = Cursors.Default;
            if (_mouseCaptured) { Capture = false; _mouseCaptured = false; }
            Invalidate();
        }

        private void ClampOffset()
        {
            if (Image == null) return;
            float imgW = Image.Width * _zoom;
            float imgH = Image.Height * _zoom;
            float maxX = Math.Max(0, (imgW - ClientSize.Width) / 2f);
            float maxY = Math.Max(0, (imgH - ClientSize.Height) / 2f);
            _offset.X = Math.Max(-maxX, Math.Min(maxX, _offset.X));
            _offset.Y = Math.Max(-maxY, Math.Min(maxY, _offset.Y));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                MouseWheel -= OnMouseWheel;
                DoubleClick -= OnDoubleClick;
                MouseDown -= OnMouseDown;
                MouseMove -= OnMouseMove;
                MouseUp -= OnMouseUp;
                MouseLeave -= OnMouseLeave;
                LostFocus -= OnLostFocus;

                if (OwnImage)
                    base.Image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
