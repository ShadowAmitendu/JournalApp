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
    /// Interaction model:
    ///   • The IMAGE is static, displayed at a comfortable size in the centre of the canvas.
    ///   • The CROP FRAME moves — user drags it to choose which region fills the banner.
    ///   • State is stored in relative percentage offsets (_pctX, _pctY) so that
    ///     window resizes never corrupt the position and it scales perfectly.
    /// </summary>
    public sealed partial class CoverRepositionWindow : Window
    {
        // ── Public result ────────────────────────────────────────────────────────
        public bool Confirmed { get; private set; }

        /// <summary>Raised when Done is clicked. Carries the new (pctX, pctY) as percentage offsets.</summary>
        public event EventHandler<(double pctX, double pctY)> RepositionConfirmed;

        // ── Construction parameters ──────────────────────────────────────────────
        private readonly double _imgNativeW, _imgNativeH;
        private readonly double _bannerW, _bannerH;
        private readonly double _bannerScale; // banner display-px per native-px

        // ── Current reposition state (relative percentages [0.0 - 1.0]) ──────────
        private double _pctX, _pctY;

        // ── Per-layout values (recalculated on every canvas SizeChanged) ─────────
        private double _overlayScale;   // popup display-px per native-px
        private double _cropDispScale;  // popup display-px per banner display-px
        private double _imgLeft, _imgTop;          // fixed image position (canvas coords)
        private double _dispImgW, _dispImgH;       // displayed image dimensions
        private double _cropW, _cropH;             // crop frame dimensions (fixed per layout)

        // ── Drag state ────────────────────────────────────────────────────────────
        private bool   _isDragging;
        private Point  _dragStartPt;
        private double _cropLeftAtStart, _cropTopAtStart;

        // ─────────────────────────────────────────────────────────────────────────
        public CoverRepositionWindow(
            ImageSource imageSource,
            double nativeW, double nativeH,
            double bannerW, double bannerH,
            double pctX, double pctY)
        {
            InitializeComponent();

            _imgNativeW = nativeW;
            _imgNativeH = nativeH;
            _bannerW    = bannerW;
            _bannerH    = bannerH;
            _pctX       = pctX;
            _pctY       = pctY;

            // Banner scale — how many banner display-pixels per native pixel
            double bannerAspect = bannerW / bannerH;
            double imgAspect    = nativeW  / nativeH;
            _bannerScale = (imgAspect > bannerAspect)
                ? bannerH / nativeH   // wide image → fit by height
                : bannerW / nativeW;  // tall/square image → fit by width

            RepoImage.Source = imageSource;

            // Size and centre the window on the primary display
            const int WinW = 1080, WinH = 700;
            AppWindow.Resize(new SizeInt32(WinW, WinH));
            try
            {
                var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    AppWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                int x = Math.Max(0, (display.WorkArea.Width  - WinW) / 2);
                int y = Math.Max(0, (display.WorkArea.Height - WinH) / 2);
                AppWindow.Move(new PointInt32(x, y));
            }
            catch { /* ignore on systems where positioning fails */ }
        }

        // ── Layout ───────────────────────────────────────────────────────────────

        private void CanvasContainer_SizeChanged(object sender, SizeChangedEventArgs e)
            => LayoutCanvas(e.NewSize.Width, e.NewSize.Height);

        private void LayoutCanvas(double areaW, double areaH)
        {
            if (_imgNativeW == 0 || _imgNativeH == 0) return;
            if (areaW < 80 || areaH < 80) return;

            // Scale image to fill ~78 % width / ~76 % height of the canvas
            double scaleX = areaW * 0.78 / _imgNativeW;
            double scaleY = areaH * 0.76 / _imgNativeH;
            _overlayScale   = Math.Min(scaleX, scaleY);
            _cropDispScale  = _overlayScale / _bannerScale;

            _dispImgW = _imgNativeW * _overlayScale;
            _dispImgH = _imgNativeH * _overlayScale;

            // Crop frame represents the banner at the current scale
            _cropW = _bannerW * _cropDispScale;
            _cropH = _bannerH * _cropDispScale;

            // Image fixed and centred in the canvas
            _imgLeft = (areaW - _dispImgW) / 2.0;
            _imgTop  = (areaH - _dispImgH) / 2.0;

            // Derive crop frame position from percentage state
            double maxCropLeft = _dispImgW - _cropW;
            double maxCropTop  = _dispImgH - _cropH;

            double cropLeft = _imgLeft + (maxCropLeft > 0 ? _pctX * maxCropLeft : 0);
            double cropTop  = _imgTop  + (maxCropTop > 0 ? _pctY * maxCropTop : 0);

            // Apply to canvas
            MainCanvas.Width  = areaW;
            MainCanvas.Height = areaH;
            RepoImage.Width   = _dispImgW;
            RepoImage.Height  = _dispImgH;
            Canvas.SetLeft(RepoImage, _imgLeft);
            Canvas.SetTop(RepoImage,  _imgTop);

            CropFrameGrid.Width  = _cropW;
            CropFrameGrid.Height = _cropH;
            Canvas.SetLeft(CropFrameGrid, cropLeft);
            Canvas.SetTop(CropFrameGrid,  cropTop);

            UpdateDimRects(areaW, areaH, cropLeft, cropTop);
        }

        private static double Clamp(double value, double min, double max)
            => max < min ? min : Math.Max(min, Math.Min(max, value));

        private void UpdateDimRects(double canvasW, double canvasH, double cropLeft, double cropTop)
        {
            double cropRight  = cropLeft + _cropW;
            double cropBottom = cropTop  + _cropH;

            // Top strip
            Canvas.SetLeft(DimTop, 0);          Canvas.SetTop(DimTop, 0);
            DimTop.Width  = canvasW;            DimTop.Height  = Math.Max(0, cropTop);

            // Bottom strip
            Canvas.SetLeft(DimBottom, 0);       Canvas.SetTop(DimBottom, cropBottom);
            DimBottom.Width = canvasW;          DimBottom.Height = Math.Max(0, canvasH - cropBottom);

            // Left strip
            Canvas.SetLeft(DimLeft, 0);         Canvas.SetTop(DimLeft, cropTop);
            DimLeft.Width  = Math.Max(0, cropLeft); DimLeft.Height = _cropH;

            // Right strip
            Canvas.SetLeft(DimRight, cropRight); Canvas.SetTop(DimRight, cropTop);
            DimRight.Width  = Math.Max(0, canvasW - cropRight); DimRight.Height = _cropH;
        }

        // ── Crop-frame drag ──────────────────────────────────────────────────────

        private void CropFrame_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!(sender is UIElement el)) return;
            el.CapturePointer(e.Pointer);

            _dragStartPt      = e.GetCurrentPoint(MainCanvas).Position;
            _cropLeftAtStart  = Canvas.GetLeft(CropFrameGrid);
            _cropTopAtStart   = Canvas.GetTop(CropFrameGrid);
            _isDragging       = true;
            e.Handled         = true;
        }

        private void CropFrame_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging) return;
            var pt = e.GetCurrentPoint(MainCanvas);
            if (!pt.Properties.IsLeftButtonPressed) { _isDragging = false; return; }

            double dx = pt.Position.X - _dragStartPt.X;
            double dy = pt.Position.Y - _dragStartPt.Y;

            double maxCropLeft = _dispImgW - _cropW;
            double maxCropTop  = _dispImgH - _cropH;

            double newCropLeft = Clamp(_cropLeftAtStart + dx, _imgLeft, _imgLeft + maxCropLeft);
            double newCropTop  = Clamp(_cropTopAtStart  + dy, _imgTop,  _imgTop  + maxCropTop);

            Canvas.SetLeft(CropFrameGrid, newCropLeft);
            Canvas.SetTop(CropFrameGrid,  newCropTop);

            // Update percentage state
            _pctX = maxCropLeft > 0 ? (newCropLeft - _imgLeft) / maxCropLeft : 0.5;
            _pctY = maxCropTop > 0 ? (newCropTop - _imgTop) / maxCropTop : 0.5;

            UpdateDimRects(MainCanvas.Width, MainCanvas.Height, newCropLeft, newCropTop);
            e.Handled = true;
        }

        private void CropFrame_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = false;
            (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        // ── Action buttons ────────────────────────────────────────────────────────

        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            RepositionConfirmed?.Invoke(this, (_pctX, _pctY));
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }
    }
}
