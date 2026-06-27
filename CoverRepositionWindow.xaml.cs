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
    /// The crop frame is fixed in the center; the user drags the IMAGE underneath it
    /// to choose which region appears in the 300px cover banner — just like Windows Photos.
    /// </summary>
    public sealed partial class CoverRepositionWindow : Window
    {
        // ── Public result ────────────────────────────────────────────────────────
        public bool Confirmed { get; private set; }

        /// <summary>Raised on the caller's thread when Done is clicked.</summary>
        public event EventHandler<(double tx, double ty)> RepositionConfirmed;

        // ── Construction parameters ──────────────────────────────────────────────
        private readonly double _imgNativeW, _imgNativeH;
        private readonly double _bannerW, _bannerH;
        private readonly double _initialTX, _initialTY;

        // ── Scale factors ─────────────────────────────────────────────────────────
        // bannerScale   : banner display pixels per native image pixel
        // overlayScale  : popup display pixels per native image pixel
        // cropDispScale : popup display pixels per banner display pixel  (= overlayScale / bannerScale)
        private double _bannerScale;
        private double _overlayScale;
        private double _cropDispScale;

        // ── Layout ────────────────────────────────────────────────────────────────
        private bool   _layoutDone;
        private double _cropX, _cropY, _cropW, _cropH; // crop frame (fixed, canvas coords)
        private double _imgLeft, _imgTop;               // image top-left (canvas coords, changes on drag)

        // ── Drag state ────────────────────────────────────────────────────────────
        private bool   _isDragging;
        private Point  _dragStartPt;
        private double _imgLeftAtStart, _imgTopAtStart;

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
            _initialTX  = currentTX;
            _initialTY  = currentTY;

            // Compute banner scale
            double bannerAspect = bannerW / bannerH;
            double imgAspect    = nativeW / nativeH;
            _bannerScale = (imgAspect > bannerAspect)
                ? bannerH / nativeH   // image wider than banner → fit by height
                : bannerW / nativeW;  // image taller than banner → fit by width

            // Set image source
            RepoImage.Source = imageSource;

            // Size and center the window
            AppWindow.Resize(new SizeInt32(960, 640));
            try
            {
                var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    AppWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                int x = (display.WorkArea.Width  - 960) / 2;
                int y = (display.WorkArea.Height - 640) / 2;
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

            // Crop frame: represent the banner area, sized to fill 70% of the window
            double maxCropW = areaW * 0.82;
            double maxCropH = areaH * 0.70;
            double cs = Math.Min(maxCropW / _bannerW, maxCropH / _bannerH); // crop display scale
            _cropDispScale = cs;
            _cropW = _bannerW * cs;
            _cropH = _bannerH * cs;

            // Center the crop frame in the canvas
            _cropX = (areaW - _cropW) / 2.0;
            _cropY = (areaH - _cropH) / 2.0;

            // Image overlay scale: overlayScale = bannerScale × cropDispScale
            // This guarantees the image is always ≥ crop frame (covers it completely)
            _overlayScale = _bannerScale * cs;

            double dispImgW = _imgNativeW * _overlayScale;
            double dispImgH = _imgNativeH * _overlayScale;

            // Resize the canvas to fill the container
            MainCanvas.Width  = areaW;
            MainCanvas.Height = areaH;

            // Resize the image
            RepoImage.Width  = dispImgW;
            RepoImage.Height = dispImgH;

            // Position the crop frame
            Canvas.SetLeft(CropFrameGrid, _cropX);
            Canvas.SetTop(CropFrameGrid,  _cropY);
            CropFrameGrid.Width  = _cropW;
            CropFrameGrid.Height = _cropH;

            // Compute initial image position from current banner offsets (only once)
            if (!_layoutDone)
            {
                _layoutDone = true;
                // imgLeft = cropX + TX * cropDispScale
                // (derived from: visible-start in crop = -TX in banner → cropX - imgLeft in overlay)
                _imgLeft = _cropX + _initialTX * _cropDispScale;
                _imgTop  = _cropY + _initialTY * _cropDispScale;
            }

            // Constrain and apply
            ConstrainImagePosition(dispImgW, dispImgH);
            Canvas.SetLeft(RepoImage, _imgLeft);
            Canvas.SetTop(RepoImage,  _imgTop);

            UpdateDimRects(areaW, areaH);
        }

        /// <summary>
        /// Ensures the image always fully covers the crop frame
        /// (the crop frame must never show the canvas background).
        /// </summary>
        private void ConstrainImagePosition(double dispImgW, double dispImgH)
        {
            // Image left edge must be ≤ crop left edge
            _imgLeft = Math.Min(_imgLeft, _cropX);
            // Image right edge must be ≥ crop right edge
            _imgLeft = Math.Max(_imgLeft, _cropX + _cropW - dispImgW);
            // Image top edge must be ≤ crop top edge
            _imgTop = Math.Min(_imgTop, _cropY);
            // Image bottom edge must be ≥ crop bottom edge
            _imgTop = Math.Max(_imgTop, _cropY + _cropH - dispImgH);
        }

        /// <summary>
        /// Positions the 4 dark rectangles that dim everything outside the crop frame.
        /// </summary>
        private void UpdateDimRects(double canvasW, double canvasH)
        {
            // Top: above crop frame
            Canvas.SetLeft(DimTop, 0); Canvas.SetTop(DimTop, 0);
            DimTop.Width  = canvasW;
            DimTop.Height = Math.Max(0, _cropY);

            // Bottom: below crop frame
            double by = _cropY + _cropH;
            Canvas.SetLeft(DimBottom, 0); Canvas.SetTop(DimBottom, by);
            DimBottom.Width  = canvasW;
            DimBottom.Height = Math.Max(0, canvasH - by);

            // Left: beside crop frame (between top and bottom crop edges)
            Canvas.SetLeft(DimLeft, 0); Canvas.SetTop(DimLeft, _cropY);
            DimLeft.Width  = Math.Max(0, _cropX);
            DimLeft.Height = _cropH;

            // Right: beside crop frame
            double rx = _cropX + _cropW;
            Canvas.SetLeft(DimRight, rx); Canvas.SetTop(DimRight, _cropY);
            DimRight.Width  = Math.Max(0, canvasW - rx);
            DimRight.Height = _cropH;
        }

        // ── Drag handlers (image moves, crop frame stays fixed) ──────────────────
        private void RepoImage_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            (sender as UIElement)?.CapturePointer(e.Pointer);
            _dragStartPt    = e.GetCurrentPoint(MainCanvas).Position;
            _imgLeftAtStart = _imgLeft;
            _imgTopAtStart  = _imgTop;
            _isDragging     = true;
            e.Handled = true;
        }

        private void RepoImage_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging) return;
            var pt = e.GetCurrentPoint(MainCanvas);
            if (!pt.Properties.IsLeftButtonPressed) { _isDragging = false; return; }

            double dx = pt.Position.X - _dragStartPt.X;
            double dy = pt.Position.Y - _dragStartPt.Y;

            _imgLeft = _imgLeftAtStart + dx;
            _imgTop  = _imgTopAtStart  + dy;

            ConstrainImagePosition(RepoImage.Width, RepoImage.Height);

            Canvas.SetLeft(RepoImage, _imgLeft);
            Canvas.SetTop(RepoImage,  _imgTop);
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
            // Convert image position back to banner TranslateX/Y
            // visible-start in overlay = cropX - imgLeft
            // visible-start in native  = (cropX - imgLeft) / overlayScale
            // visible-start in banner  = (cropX - imgLeft) / overlayScale * bannerScale
            // TranslateX = -visible-start-in-banner = -(cropX - imgLeft) * bannerScale / overlayScale
            double tx = -(_cropX - _imgLeft) * _bannerScale / _overlayScale;
            double ty = -(_cropY - _imgTop)  * _bannerScale / _overlayScale;

            Confirmed = true;
            RepositionConfirmed?.Invoke(this, (tx, ty));
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }
    }
}
