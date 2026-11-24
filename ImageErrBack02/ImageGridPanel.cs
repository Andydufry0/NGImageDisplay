using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImageErrBack02
{
    /// <summary>
    /// 高性能图片网格面板
    /// 优化策略：
    /// 1. 用 GetThumbnailImage 快速生成缩略图（JPEG会读取EXIF内嵌缩略图，极快）
    /// 2. 预分配数组，减少GC压力
    /// 3. 脏标记批量合并刷新，减少BeginInvoke次数
    /// 4. 文件流加 SequentialScan 提示，优化磁盘读取
    /// </summary>
    public class ImageGridPanel : Panel
    {
        private string[] _imagePaths;
        private Bitmap[] _thumbnails;
        private Rectangle[] _imageRects;
        private int _columns = 1;
        private int _rows = 1;
        private const int CellPadding = 4;
        private long _batchId = 0;
        private int _imageCount = 0;

        public event Action<string> OnRightClickImage;

        public ImageGridPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.DoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            UpdateStyles();

            MouseClick += ImageGridPanel_MouseClick;
        }

        private void ImageGridPanel_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && _imageRects != null)
            {
                for (int i = 0; i < _imageRects.Length; i++)
                {
                    if (_imageRects[i].Contains(e.Location) && i < _imageCount)
                    {
                        OnRightClickImage?.Invoke(_imagePaths[i]);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 设置图片路径列表，预分配数组，立即计算布局并刷新
        /// </summary>
        public void SetImagePaths(List<string> paths)
        {
            // 释放旧缩略图
            if (_thumbnails != null)
            {
                for (int i = 0; i < _thumbnails.Length; i++)
                    _thumbnails[i]?.Dispose();
            }

            if (paths == null || paths.Count == 0)
            {
                _imagePaths = null;
                _thumbnails = null;
                _imageRects = null;
                _imageCount = 0;
                Invalidate();
                return;
            }

            _imageCount = paths.Count;
            _imagePaths = new string[_imageCount];
            paths.CopyTo(_imagePaths);
            _thumbnails = new Bitmap[_imageCount];
            _imageRects = new Rectangle[_imageCount];

            // 强制布局确保 ClientSize 有效
            if (Parent != null)
                Parent.PerformLayout();

            CalculateLayout();
            Invalidate();
        }

        private void CalculateLayout()
        {
            if (_imageCount == 0) return;

            if (_imageCount <= 3)
            {
                _columns = _imageCount;
                _rows = 1;
            }
            else
            {
                _columns = (int)Math.Floor(Math.Sqrt(_imageCount));
                if (_columns < 2) _columns = 2;
                _rows = (int)Math.Ceiling(_imageCount / (double)_columns);
                while (_rows < _columns && _columns > 1)
                {
                    _columns--;
                    _rows = (int)Math.Ceiling(_imageCount / (double)_columns);
                }
            }

            UpdateImageRects();
        }

        private void UpdateImageRects()
        {
            if (_imageCount == 0 || _imageRects == null) return;

            int cellW = (ClientSize.Width - CellPadding) / _columns;
            int cellH = (ClientSize.Height - CellPadding) / _rows;
            if (cellW < 10) cellW = 10;
            if (cellH < 10) cellH = 10;

            for (int i = 0; i < _imageCount; i++)
            {
                int col = i % _columns;
                int row = i / _columns;
                _imageRects[i] = new Rectangle(
                    col * cellW + CellPadding / 2,
                    row * cellH + CellPadding / 2,
                    cellW - CellPadding,
                    cellH - CellPadding);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_imageCount > 0)
                UpdateImageRects();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_imageCount == 0 || _imageRects == null) return;

            var g = e.Graphics;
            g.Clear(Color.FromArgb(45, 45, 45));
            g.InterpolationMode = InterpolationMode.NearestNeighbor; // 最快

            using (var borderPen = new Pen(Color.FromArgb(80, 80, 80), 1))
            {
                for (int i = 0; i < _imageCount; i++)
                {
                    var rect = _imageRects[i];
                    g.DrawRectangle(borderPen, rect);

                    var bmp = _thumbnails[i];
                    if (bmp != null)
                    {
                        float scale = Math.Min((float)rect.Width / bmp.Width, (float)rect.Height / bmp.Height);
                        int drawW = (int)(bmp.Width * scale);
                        int drawH = (int)(bmp.Height * scale);
                        g.DrawImage(bmp,
                            rect.X + (rect.Width - drawW) / 2,
                            rect.Y + (rect.Height - drawH) / 2,
                            drawW, drawH);
                    }
                }
            }
        }

        /// <summary>
        /// 异步加载缩略图，脏标记批量合并刷新
        /// </summary>
        public async Task LoadThumbnailsAsync(CancellationToken cancellationToken = default)
        {
            long currentBatch = Interlocked.Increment(ref _batchId);

            if (_imageCount == 0 || _imageRects == null) return;

            // 目标尺寸 = 网格单元格大小
            int tw = _imageRects[0].Width;
            int th = _imageRects[0].Height;
            if (tw < 50) tw = 200;
            if (th < 50) th = 200;

            // 线程数根据图片数量决定，最大8个
            int maxConcurrent = Math.Min(_imageCount, 8);
            var sem = new SemaphoreSlim(maxConcurrent, maxConcurrent);
            var tasks = new Task[_imageCount];

            // 脏标记
            int dirty = 0;

            for (int i = 0; i < _imageCount; i++)
            {
                int idx = i;
                string path = _imagePaths[idx];

                tasks[idx] = Task.Run(() =>
                {
                    sem.Wait(cancellationToken);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var bmp = LoadThumbnailFast(path, tw, th);

                        if (bmp == null || _batchId != currentBatch || IsDisposed)
                        {
                            bmp?.Dispose();
                            return;
                        }

                        // 写入缩略图数组（每个idx只有一个线程写，无竞争）
                        _thumbnails[idx]?.Dispose();
                        _thumbnails[idx] = bmp;

                        // 脏标记：只在没有排队刷新时发起UI刷新
                        if (Interlocked.CompareExchange(ref dirty, 1, 0) == 0)
                        {
                            if (InvokeRequired)
                            {
                                BeginInvoke(new Action(() =>
                                {
                                    Interlocked.Exchange(ref dirty, 0);
                                    if (!IsDisposed && _batchId == currentBatch)
                                        Invalidate();
                                }));
                            }
                            else
                            {
                                Interlocked.Exchange(ref dirty, 0);
                                Invalidate();
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                    finally
                    {
                        sem.Release();
                    }
                }, cancellationToken);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
            sem.Dispose();

            // 最后一次强制刷新，确保所有图片都显示
            if (!IsDisposed && _batchId == currentBatch)
            {
                if (InvokeRequired)
                    BeginInvoke(new Action(() => { if (!IsDisposed && _batchId == currentBatch) Invalidate(); }));
                else
                    Invalidate();
            }
        }

        /// <summary>
        /// 快速生成缩略图
        /// 优先用 GetThumbnailImage（JPEG会读取EXIF内嵌缩略图，极快）
        /// 失败时回退到手动缩放
        /// </summary>
        private static Bitmap LoadThumbnailFast(string path, int targetW, int targetH)
        {
            try
            {
                // 用 SequentialScan 提示优化磁盘读取
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                {
                    using (var srcImg = Image.FromStream(fs, false, false))
                    {
                        // 优先尝试 GetThumbnailImage（JPEG有EXIF缩略图时极快）
                        try
                        {
                            var thumb = (Bitmap)srcImg.GetThumbnailImage(targetW, targetH, () => false, IntPtr.Zero);
                            if (thumb != null && thumb.Width > 0 && thumb.Height > 0)
                                return thumb;
                        }
                        catch { }

                        // 回退：手动缩放
                        float scaleW = (float)targetW / srcImg.Width;
                        float scaleH = (float)targetH / srcImg.Height;
                        float scale = Math.Min(scaleW, scaleH);

                        if (scale >= 1.0f)
                            return new Bitmap(srcImg);

                        int newW = Math.Max((int)(srcImg.Width * scale), 1);
                        int newH = Math.Max((int)(srcImg.Height * scale), 1);

                        var bmp = new Bitmap(newW, newH, PixelFormat.Format24bppRgb);
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.SmoothingMode = SmoothingMode.HighSpeed;
                            g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                            g.DrawImage(srcImg, 0, 0, newW, newH);
                        }
                        return bmp;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                MouseClick -= ImageGridPanel_MouseClick;
                if (_thumbnails != null)
                {
                    for (int i = 0; i < _thumbnails.Length; i++)
                        _thumbnails[i]?.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
