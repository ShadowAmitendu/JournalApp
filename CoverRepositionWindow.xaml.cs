using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;
using Windows.Graphics;

namespace JournalApp
{
    /// <summary>
    /// Popup window for repositioning the cover image.
    ///
    /// Design:
    ///   - The CROP FRAME is fixed in the centre of the window (represents the 300 px banner).
    ///   - The IMAGE moves underneath it (user drags to choose which region appears).
    ///   - Internal state is kept in banner-display-pixel coordinates (_currentTX/_currentTY)
    ///     so that window resizes never corrupt the saved position.
    /// </summary>
    public sealed partial class CoverRepositionWindow : Window
    {
        // ── Public result ────────────────────────────────────────────────────────
        public bool Confirmed { get; private set; }

        /// <summary>Raised (on caller's dispatcher) when Done is clicked.</summary>
        public event EventHandler<(double tx, double ty)> RepositionConfirmed;

        // ── Construction parameters ──────────────────────────────────────────────
        private readonly double _imgNativeW, _imgNativeH;
        private readonly double _bannerW, _bannerH;
        private readonly double _bannerScale;   // banner display-px per native-px

        // ── Current reposition state (in BANNER display pixels) ──────────────────
        // These are the source of truth and survive window resizes.
        private double _currentTX, _currentTY;

        // ── Per-layout computed values (recalculated on every SizeChanged) ───────
        private double _overlayScale;   // overlay display-px per native-px = bannerScale × cs
        private double _cropDispScale;  // overlay display-px per banner display-px (= cs)
        private double _cropX, _cropY, _cropW, _cropH;  // crop frame (canvas coords)

        // ── Drag state ────────────────────────────────────────────────────────────
        private bool   _isDragging;
        private Point  _dragStartPt;
        private double _imgLeftAtDragStart, _imgTopAtDragStart;

        // ─────────────────────────────────────────────────────────────────────────
        public CoverRepositionWindow(
            ImageSource imageSource,
            double nativeW, double nativeH,
            double bannerW, double bannerH,
            double currentTX, double currentTY)
        {
            InitializeComponent();

            _imgNativeW = nativeW;
            _imgNativeH = nativeH;
            _bannerW    = bannerW;
            _bannerH    = bannerH;

            // Banner scale: how many banner-display-pixels per native pixel
            double bannerAspect = bannerW / bannerH;
            double imgAspect    = nativeW / nativeH;
            _bannerScale = (imgAspect > bannerAspect)
                ? bannerH / nativeH   // wide image → fit by height
                : bannerW / nativeW;  // tall/square image → fit by width

            // Current offset in banner display pixels — start from caller's saved offset
            _currentTX = currentTX;
            _currentTY = currentTY;

            RepoImage.Source = imageSource;

            // Size and centre the window
            AppWindow.Resize(new SizeInt32(960, 640));
            try
            {
                var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    AppWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                int x = Math.Max(0, (display.WorkArea.Width  - 960) / 2);
                int y = Math.Max(0, (display.WorkArea.Height - 640) / 2);
                AppWindow.Move(new PointInt32(x, y));
            }
            catch { /* ignore positioning errors */ }
        }

        // ── Layout ───────────────────────────────────────────────────────────────
        private void CanvasContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            LayoutCanvas(e.NewSize.Width, e.NewSize.Height);
        }

        private void LayoutCanvas(double areaW, double areaH)
        {
            if (_imgNativeW == 0 || _imgNativeH == 0) return;
            if (areaW < 50 || areaH < 50) return;

            // ── Crop frame dimensions ───────────────────────────────────────────
            // Scale the crop frame to fill ~80 % of window width / ~70 % of canvas height
            double maxCropW = areaW * 0.82;
            double maxCropH = areaH * 0.70;
            double cs = Math.Min(maxCropW / _bannerW, maxCropH / _bannerH);
            _cropDispScale = cs;

            _cropW = _bannerW * cs;
            _cropH = _bannerH * cs;

            // Centre the crop frame
            _cropX = (areaW - _cropW) / 2.0;
            _cropY = (areaH - _cropH) / 2.0;

            // ── Image dimensions in the overlay ─────────────────────────────────
            // overlayScale = bannerScale × cs  → guarantees image ≥ crop frame
            _overlayScale = _bannerScale * cs;

            double dispImgW = _imgNativeW * _overlayScale;
            double dispImgH = _imgNativeH * _overlayScale;

            // ── Resize canvas / image ────────────────────────────────────────────
            MainCanvas.Width  = areaW;
            MainCanvas.Height = areaH;
            RepoImage.Width   = dispImgW;
            RepoImage.Height  = dispImgH;

            // ── Position crop frame ──────────────────────────────────────────────
            Canvas.SetLeft(CropFrameGrid, _cropX);
            Canvas.SetTop(CropFrameGrid,  _cropY);
            CropFrameGrid.Width  = _cropW;
            CropFrameGrid.Height = _cropH;

            // ── Derive image position from _currentTX/_currentTY ─────────────────
            // Relationship (derived from coordinate mapping):
            //   cropX - imgLeft  = -_currentTX * _cropDispScale
            //   ⟹ imgLeft       = cropX + _currentTX * _cropDispScale
            double imgLeft = _cropX + _currentTX * _cropDispScale;
            double imgTop  = _cropY + _currentTY * _cropDispScale;

            // Constrain so the image always covers the crop frame
            imgLeft = ConstrainToRange(imgLeft, _cropX + _cropW - dispImgW, _cropX);
            imgTop  = ConstrainToRange(imgTop,  _cropY + _cropH - dispImgH, _cropY);

            Canvas.SetLeft(RepoImage, imgLeft);
            Canvas.SetTop(RepoImage,  imgTop);

            // Sync _currentTX/_currentTY back from constrained position
            _currentTX = -((_cropX - imgLeft) * _bannerScale / _overlayScale);
            _currentTY = -((_cropY - imgTop)  * _bannerScale / _overlayScale);

            UpdateDimRects(areaW, areaH);
        }

        private static double ConstrainToRange(double value, double min, double max)
            => Math.Max(min, Math.Min(max, value));

        private void UpdateDimRects(double canvasW, double canvasH)
        {
            // Top
            Canvas.SetLeft(DimTop, 0); Canvas.SetTop(DimTop, 0);
            DimTop.Width  = canvasW;
            DimTop.Height = Math.Max(0, _cropY);

            // Bottom
            double by = _cropY + _cropH;
            Canvas.SetLeft(DimBottom, 0); Canvas.SetTop(DimBottom, by);
            DimBottom.Width  = canvasW;
            DimBottom.Height = Math.Max(0, canvasH - by);

            // Left
            Canvas.SetLeft(DimLeft, 0); Canvas.SetTop(DimLeft, _cropY);
            DimLeft.Width  = Math.Max(0, _cropX);
            DimLeft.Height = _cropH;

            // Right
            double rx = _cropX + _cropW;
            Canvas.SetLeft(DimRight, rx); Canvas.SetTop(DimRight, _cropY);
            DimRight.Width  = Math.Max(0, canvasW - rx);
            DimRight.Height = _cropH;
        }

        // ── Drag: image moves, crop frame stays fixed ─────────────────────────────
        private void RepoImage_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            (sender as UIElement)?.CapturePointer(e.Pointer);
            _dragStartPt       = e.GetCurrentPoint(MainCanvas).Position;
            _imgLeftAtDragStart  = Canvas.GetLeft(RepoImage);
            _imgTopAtDragStart   = Canvas.GetTop(RepoImage);
            _isDragging = true;
            e.Handled = true;
        }

        private void RepoImage_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging) return;
            var pt = e.GetCurrentPoint(MainCanvas);
            if (!pt.Properties.IsLeftButtonPressed) { _isDragging = false; return; }

            double dx = pt.Position.X - _dragStartPt.X;
            double dy = pt.Position.Y - _dragStartPt.Y;

            double dispImgW = RepoImage.Width;
            double dispImgH = RepoImage.Height;

            double newLeft = ConstrainToRange(
                _imgLeftAtDragStart + dx,
                _cropX + _cropW - dispImgW, _cropX);

            double newTop = ConstrainToRange(
                _imgTopAtDragStart + dy,
                _cropY + _cropH - dispImgH, _cropY);

            Canvas.SetLeft(RepoImage, newLeft);
            Canvas.SetTop(RepoImage,  newTop);

            // Update authoritative state in banner-display-pixel space
            _currentTX = -((_cropX - newLeft) * _bannerScale / _overlayScale);
            _currentTY = -((_cropY - newTop)  * _bannerScale / _overlayScale);

            e.Handled = true;
        }

        private void RepoImage_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = false;
            (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        // ── Action buttons ────────────────────────────────────────────────────────
        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            // _currentTX/_currentTY are already in banner display-pixel space — pass directly
            Confirmed = true;
            RepositionConfirmed?.Invoke(this, (_currentTX, _currentTY));
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }
    }
}
