using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using CommunityToolkit.Maui.Views;

#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
#endif

namespace DViewer.Controls
{
    public partial class DicomViewerView : ContentView
    {
        public DicomViewerView()
        {
            InitializeComponent();

            // Toolbar initial peeking
            TopToolbar.SizeChanged += (_, __) => InitToolbarPeek();
        }

        // ======== Tool-Auswahl ========
        public enum ViewerTool { Cursor, Measure }

        public static readonly BindableProperty CurrentToolProperty =
            BindableProperty.Create(
                nameof(CurrentTool),
                typeof(ViewerTool),
                typeof(DicomViewerView),
                ViewerTool.Cursor,
                propertyChanged: (b, o, n) => ((DicomViewerView)b).UpdateToolButtons());

        public ViewerTool CurrentTool
        {
            get => (ViewerTool)GetValue(CurrentToolProperty);
            set => SetValue(CurrentToolProperty, value);
        }

        void OnCursorToolClicked(object? s, EventArgs e) { CurrentTool = ViewerTool.Cursor; }
        void OnMeasureToolClicked(object? s, EventArgs e) { CurrentTool = ViewerTool.Measure; }

        void UpdateToolButtons()
        {
            // einfache visuelle Markierung
            if (CursorToolFrame != null)
                CursorToolFrame.BackgroundColor = CurrentTool == ViewerTool.Cursor ? Color.FromArgb("#22FFFFFF") : Colors.Transparent;
            if (MeasureToolFrame != null)
                MeasureToolFrame.BackgroundColor = CurrentTool == ViewerTool.Measure ? Color.FromArgb("#22FFFFFF") : Colors.Transparent;
        }

        Grid Container => ImageLayer ?? Root;

        // ---------- Item ----------
        public static readonly BindableProperty ItemProperty =
            BindableProperty.Create(
                nameof(Item),
                typeof(DicomFileViewModel),
                typeof(DicomViewerView),
                default(DicomFileViewModel),
                propertyChanged: (b, o, n) => ((DicomViewerView)b).OnItemChanged(o as DicomFileViewModel, n as DicomFileViewModel));

        public DicomFileViewModel? Item
        {
            get => (DicomFileViewModel?)GetValue(ItemProperty);
            set => SetValue(ItemProperty, value);
        }

        private void OnItemChanged(DicomFileViewModel? oldVm, DicomFileViewModel? newVm)
        {
            if (oldVm != null) oldVm.PropertyChanged -= OnItemVmPropertyChanged;
            if (newVm != null) newVm.PropertyChanged += OnItemVmPropertyChanged;

            HasVideo = newVm?.HasVideo ?? false;
            HasMultiFrame = newVm?.HasMultiFrame ?? false;

            if (newVm?.RenderFrameWithWindow != null)
            {
                WindowCenter = newVm.DefaultWindowCenter ?? 50.0;
                WindowWidth = Math.Max(1, newVm.DefaultWindowWidth ?? 350.0);
                if (WlOverlay != null) WlOverlay.IsVisible = true;
            }
            else
            {
                if (WlOverlay != null) WlOverlay.IsVisible = false;
            }

            if (HasVideo && Player != null && !string.IsNullOrWhiteSpace(newVm?.VideoPath))
                Player.Source = MediaSource.FromFile(newVm!.VideoPath!);

            RaiseOverlayComputed();
            RenderCurrent();
        }

        private void OnItemVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(DicomFileViewModel.Image)
                or nameof(DicomFileViewModel.VideoPath)
                or nameof(DicomFileViewModel.FrameCount))
            {
                HasVideo = Item?.HasVideo ?? HasVideo;
                HasMultiFrame = Item?.HasMultiFrame ?? HasMultiFrame;

                if (HasVideo && Player != null && !string.IsNullOrWhiteSpace(Item?.VideoPath))
                    Player.Source = MediaSource.FromFile(Item!.VideoPath!);

                RaiseOverlayComputed();
                RenderCurrent();
            }
        }

        // ---------- Flags ----------
        public static readonly BindableProperty HasVideoProperty =
            BindableProperty.Create(nameof(HasVideo), typeof(bool), typeof(DicomViewerView), false,
                propertyChanged: (b, o, n) => ((DicomViewerView)b).UpdateVisualState());
        public bool HasVideo { get => (bool)GetValue(HasVideoProperty); set => SetValue(HasVideoProperty, value); }

        public static readonly BindableProperty HasMultiFrameProperty =
            BindableProperty.Create(nameof(HasMultiFrame), typeof(bool), typeof(DicomViewerView), false,
                propertyChanged: (b, o, n) => ((DicomViewerView)b).UpdateVisualState());
        public bool HasMultiFrame { get => (bool)GetValue(HasMultiFrameProperty); set => SetValue(HasMultiFrameProperty, value); }

        // ---------- VideoSource ----------
        public static readonly BindableProperty VideoSourceProperty =
            BindableProperty.Create(nameof(VideoSource), typeof(MediaSource), typeof(DicomViewerView), default(MediaSource),
                propertyChanged: (b, o, n) => ((DicomViewerView)b).OnVideoSourceChanged());
        public MediaSource? VideoSource { get => (MediaSource?)GetValue(VideoSourceProperty); set => SetValue(VideoSourceProperty, value); }
        private void OnVideoSourceChanged()
        {
            if (Player != null) Player.Source = VideoSource;
            UpdateVisualState();
        }

        // ---------- FrameIndex ----------
        public static readonly BindableProperty FrameIndexProperty =
            BindableProperty.Create(nameof(FrameIndex), typeof(int), typeof(DicomViewerView), 0,
                propertyChanged: (b, o, n) => ((DicomViewerView)b).OnFrameIndexChanged());
        public int FrameIndex { get => (int)GetValue(FrameIndexProperty); set => SetValue(FrameIndexProperty, value); }
        private void OnFrameIndexChanged() => RenderCurrent();

        // ---------- Commands ----------
        public static readonly BindableProperty PrevFrameCommandProperty =
            BindableProperty.Create(nameof(PrevFrameCommand), typeof(ICommand), typeof(DicomViewerView));
        public ICommand? PrevFrameCommand { get => (ICommand?)GetValue(PrevFrameCommandProperty); set => SetValue(PrevFrameCommandProperty, value); }

        public static readonly BindableProperty NextFrameCommandProperty =
            BindableProperty.Create(nameof(NextFrameCommand), typeof(ICommand), typeof(DicomViewerView));
        public ICommand? NextFrameCommand { get => (ICommand?)GetValue(NextFrameCommandProperty); set => SetValue(NextFrameCommandProperty, value); }

        public static readonly BindableProperty PlayPauseCommandProperty =
            BindableProperty.Create(nameof(PlayPauseCommand), typeof(ICommand), typeof(DicomViewerView));
        public ICommand? PlayPauseCommand { get => (ICommand?)GetValue(PlayPauseCommandProperty); set => SetValue(PlayPauseCommandProperty, value); }

        public static readonly BindableProperty IsPlayingProperty =
            BindableProperty.Create(nameof(IsPlaying), typeof(bool), typeof(DicomViewerView), false);
        public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }

        // ---------- Patienten-Overlay (öffentliche DP) ----------
        public static readonly BindableProperty PatientNameWithSexProperty =
            BindableProperty.Create(nameof(PatientNameWithSex), typeof(string), typeof(DicomViewerView), string.Empty,
                propertyChanged: (b, o, n) => ((DicomViewerView)b).RaiseOverlayComputed());
        public string PatientNameWithSex { get => (string)GetValue(PatientNameWithSexProperty); set => SetValue(PatientNameWithSexProperty, value); }

        public static readonly BindableProperty SpeciesProperty =
            BindableProperty.Create(nameof(Species), typeof(string), typeof(DicomViewerView), string.Empty,
                propertyChanged: (b, o, n) => ((DicomViewerView)b).RaiseOverlayComputed());
        public string Species { get => (string)GetValue(SpeciesProperty); set => SetValue(SpeciesProperty, value); }

        public static readonly BindableProperty PatientIDProperty =
            BindableProperty.Create(nameof(PatientID), typeof(string), typeof(DicomViewerView), string.Empty,
                propertyChanged: (b, o, n) => ((DicomViewerView)b).RaiseOverlayComputed());
        public string PatientID { get => (string)GetValue(PatientIDProperty); set => SetValue(PatientIDProperty, value); }

        public static readonly BindableProperty BirthDateDisplayProperty =
            BindableProperty.Create(nameof(BirthDateDisplay), typeof(string), typeof(DicomViewerView), string.Empty,
                propertyChanged: (b, o, n) => ((DicomViewerView)b).RaiseOverlayComputed());
        public string BirthDateDisplay { get => (string)GetValue(BirthDateDisplayProperty); set => SetValue(BirthDateDisplayProperty, value); }

        public static readonly BindableProperty OtherPidProperty =
            BindableProperty.Create(nameof(OtherPid), typeof(string), typeof(DicomViewerView), string.Empty,
                propertyChanged: (b, o, n) => ((DicomViewerView)b).RaiseOverlayComputed());
        public string OtherPid { get => (string)GetValue(OtherPidProperty); set => SetValue(OtherPidProperty, value); }

        // ---------- Window/Level ----------
        public static readonly BindableProperty WindowCenterProperty =
            BindableProperty.Create(nameof(WindowCenter), typeof(double), typeof(DicomViewerView), 0d,
                propertyChanged: (b, o, n) => ((DicomViewerView)b).OnWindowParamChanged());
        public double WindowCenter { get => (double)GetValue(WindowCenterProperty); set => SetValue(WindowCenterProperty, value); }

        public static readonly BindableProperty WindowWidthProperty =
            BindableProperty.Create(nameof(WindowWidth), typeof(double), typeof(DicomViewerView), 0d,
                propertyChanged: (b, o, n) => ((DicomViewerView)b).OnWindowParamChanged());
        public double WindowWidth { get => (double)GetValue(WindowWidthProperty); set => SetValue(WindowWidthProperty, Math.Max(1, value)); }

        public string WindowCenterText => double.IsFinite(WindowCenter) ? $"{WindowCenter:0}" : "";
        public string WindowWidthText => double.IsFinite(WindowWidth) ? $"{WindowWidth:0}" : "";
        public string OverlayWindowLevelText => $"WL/WW: {WindowCenterText}/{WindowWidthText}";
        public string OverlayZoomText => $"Zoom: {(_currentScale * 100):0}%";

        DateTime _lastRender = DateTime.MinValue;
        bool _wlScheduled;

        private void OnWindowParamChanged()
        {
            OnPropertyChanged(nameof(WindowCenterText));
            OnPropertyChanged(nameof(WindowWidthText));
            OnPropertyChanged(nameof(OverlayWindowLevelText));

            var now = DateTime.UtcNow;
            if ((now - _lastRender).TotalMilliseconds >= 18)
            {
                _lastRender = now;
                RenderCurrent();
                return;
            }
            if (_wlScheduled) return;

            _wlScheduled = true;
            var delay = TimeSpan.FromMilliseconds(Math.Max(1, 18 - (int)(now - _lastRender).TotalMilliseconds));
            (Dispatcher ?? Microsoft.Maui.Controls.Application.Current?.Dispatcher)?.StartTimer(delay, () =>
            {
                _wlScheduled = false;
                _lastRender = DateTime.UtcNow;
                RenderCurrent();
                return false;
            });
        }

        // ---------- Render ----------
        private void RenderCurrent()
        {
            if (HasVideo) return;

            try
            {
                if (Item?.RenderFrameWithWindow != null && WindowWidth > 0)
                {
                    var frame = Math.Max(0, FrameIndex);
                    Img.Source = Item.RenderFrameWithWindow(WindowCenter, WindowWidth, frame);
                    Item.PrefetchFrames?.Invoke(frame);
                    UpdateVisualState();
                    return;
                }

                if (Item?.GetFrameImageSource != null && Item.FrameCount > 0)
                {
                    var idx = Math.Max(0, Math.Min(FrameIndex, Item.FrameCount - 1));
                    Img.Source = Item.GetFrameImageSource(idx);
                    Item.PrefetchFrames?.Invoke(idx);
                    UpdateVisualState();
                    return;
                }

                Img.Source = Item?.Image;
            }
            catch { }

            UpdateVisualState();
        }

        // ---------- Fullscreen ----------
        public event EventHandler? FullscreenRequested;
        private void OnFullscreenTapped(object? s, TappedEventArgs e)
            => FullscreenRequested?.Invoke(this, EventArgs.Empty);

        // ---------- Sichtbarkeit ----------
        private void UpdateVisualState()
        {
            if (Img != null) Img.IsVisible = !HasVideo;
            if (Player != null) Player.IsVisible = HasVideo;

            if (FrameOverlay != null) FrameOverlay.IsVisible = HasMultiFrame;
            if (WlOverlay != null) WlOverlay.IsVisible = Item?.RenderFrameWithWindow != null;

            InvalidateMeasure();
            ForceLayout();
        }

        // ===================== Gesten =====================
        const double MIN_SCALE = 1.0;
        const double MAX_SCALE = 12.0;
        double _currentScale = 1;
        double _startX, _startY;
        bool _pinching;

        const double WLWidthSensitivity = 2.0;
        const double WLCenterSensitivity = 2.0;
        const double WL_MIN_STEP_W = 1;
        const double WL_MIN_STEP_C = 1;

        double _wlStartCenter, _wlStartWidth;

        // Maus: Links = WL (nur Cursor-Tool), Rechts = Zoom (um Startpunkt), Mitte = Move
        bool _mouseLeftDown, _mouseRightDown, _mouseMiddleDown;
        Point _mouseStart;
        double _zoomStartScale;
        Point _zoomAnchorScreen;

        // Doppel-Rechtsklick Reset
        DateTime _lastRightDownTime = DateTime.MinValue;
        Point _lastRightDownPos;
        const int RIGHT_DBLCLICK_MS = 350;
        const double RIGHT_DBLCLICK_MAXPX = 10;

        // ===== Touch (non-Windows) =====
        private void OnPinchUpdated(object? s, PinchGestureUpdatedEventArgs e)
        {
#if WINDOWS
            return;
#else
            if (CurrentTool != ViewerTool.Cursor) return;
            if (HasVideo || Img == null) return;

            switch (e.Status)
            {
                case GestureStatus.Started:
                    _pinching = true;
                    Img.AnchorX = e.ScaleOrigin.X;
                    Img.AnchorY = e.ScaleOrigin.Y;
                    break;
                case GestureStatus.Running:
                    var newScale = Math.Clamp(_currentScale * e.Scale, MIN_SCALE, MAX_SCALE);
                    Img.Scale = newScale;
                    break;
                case GestureStatus.Canceled:
                case GestureStatus.Completed:
                    _pinching = false;
                    _currentScale = Img.Scale;
                    ClampTranslation();
                    OnPropertyChanged(nameof(OverlayZoomText));
                    break;
            }
#endif
        }

        private void OnPanUpdated(object? s, PanUpdatedEventArgs e)
        {
#if WINDOWS
            return;
#else
            if (CurrentTool != ViewerTool.Cursor) return;
            if (HasVideo || Img == null) return;
            if (_currentScale <= 1) return;

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _startX = Img.TranslationX;
                    _startY = Img.TranslationY;
                    break;
                case GestureStatus.Running:
                    Img.TranslationX = _startX + e.TotalX;
                    Img.TranslationY = _startY + e.TotalY;
                    ClampTranslation();
                    break;
            }
#endif
        }

        private void OnWlPanUpdated(object? s, PanUpdatedEventArgs e)
        {
#if WINDOWS
            return;
#else
            if (CurrentTool != ViewerTool.Cursor) return;
            if (HasVideo || Item?.RenderFrameWithWindow == null) return;
            if (_pinching) return;

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _wlStartCenter = WindowCenter;
                    _wlStartWidth = WindowWidth;
                    break;
                case GestureStatus.Running:
                    var newWidth = Math.Max(1, _wlStartWidth + (e.TotalX * WLWidthSensitivity));
                    var newCenter = _wlStartCenter - (e.TotalY * WLCenterSensitivity);
                    if (Math.Abs(newWidth - WindowWidth) >= WL_MIN_STEP_W) WindowWidth = newWidth;
                    if (Math.Abs(newCenter - WindowCenter) >= WL_MIN_STEP_C) WindowCenter = newCenter;
                    break;
            }
#endif
        }

        // ===== MAUI-Pointer (non-Windows) =====
        private void OnPointerPressed(object? s, PointerEventArgs e)
        {
#if WINDOWS
            MaybeToggleToolbar(e.GetPosition(Container) ?? default);
            return;
#else
            MaybeToggleToolbar(e.GetPosition(Container) ?? default);
            if (CurrentTool != ViewerTool.Cursor) return;
            if (HasVideo) return;

            _mouseStart = e.GetPosition(Container) ?? default;
            _mouseLeftDown = true; _mouseRightDown = false; _mouseMiddleDown = false;

            _wlStartCenter = WindowCenter;
            _wlStartWidth = WindowWidth;
            _zoomStartScale = _currentScale;
            _zoomAnchorScreen = _mouseStart;
            _startX = Img?.TranslationX ?? 0;
            _startY = Img?.TranslationY ?? 0;
#endif
        }

        private void OnPointerMoved(object? s, PointerEventArgs e)
        {
#if WINDOWS
            MaybeToggleToolbar(e.GetPosition(Container) ?? default);
            return;
#else
            MaybeToggleToolbar(e.GetPosition(Container) ?? default);
            if (CurrentTool != ViewerTool.Cursor) return;
            if (HasVideo || (!_mouseLeftDown && !_mouseRightDown && !_mouseMiddleDown)) return;

            var pos = e.GetPosition(Container) ?? default;
            var dx = pos.X - _mouseStart.X;
            var dy = pos.Y - _mouseStart.Y;

            if (_mouseLeftDown && Item?.RenderFrameWithWindow != null)
            {
                var newWidth = Math.Max(1, _wlStartWidth + (dx * WLWidthSensitivity));
                var newCenter = _wlStartCenter - (dy * WLCenterSensitivity);
                if (Math.Abs(newWidth - WindowWidth) >= WL_MIN_STEP_W) WindowWidth = newWidth;
                if (Math.Abs(newCenter - WindowCenter) >= WL_MIN_STEP_C) WindowCenter = newCenter;
            }
            else if (_mouseRightDown && Img != null)
            {
                var factor = Math.Pow(1.01, -dy);
                var newScale = Math.Clamp(_zoomStartScale * factor, MIN_SCALE, MAX_SCALE);
                ZoomAt(_zoomAnchorScreen, newScale);
            }
            else if (_mouseMiddleDown && Img != null)
            {
                Img.TranslationX = _startX + dx;
                Img.TranslationY = _startY + dy;
                ClampTranslation();
            }
#endif
        }

        private void OnPointerReleased(object? s, PointerEventArgs e)
        {
#if WINDOWS
            return;
#else
            _mouseLeftDown = _mouseRightDown = _mouseMiddleDown = false;
#endif
        }

#if WINDOWS
        // ===== Native Windows Pointer =====
        FrameworkElement? _native;

        protected override void OnHandlerChanged()
        {
            base.OnHandlerChanged();

            if (_native != null)
            {
                _native.PointerPressed  -= Native_PointerPressed;
                _native.PointerMoved    -= Native_PointerMoved;
                _native.PointerReleased -= Native_PointerReleased;
                _native.PointerCanceled -= Native_PointerReleased;
                _native.PointerExited   -= Native_PointerReleased;
                _native = null;
            }

            _native = (Container?.Handler?.PlatformView as FrameworkElement)
                      ?? (Handler?.PlatformView as FrameworkElement);

            if (_native != null)
            {
                _native.PointerPressed  += Native_PointerPressed;
                _native.PointerMoved    += Native_PointerMoved;
                _native.PointerReleased += Native_PointerReleased;
                _native.PointerCanceled += Native_PointerReleased;
                _native.PointerExited   += Native_PointerReleased;
            }
        }

        private void Native_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_native == null) return;

            var pt    = e.GetCurrentPoint(_native);
            var props = pt.Properties;

            MaybeToggleToolbar(new Point(pt.Position.X, pt.Position.Y));

            // Doppel-Rechtsklick Reset
            if (props.IsRightButtonPressed)
            {
                var now = DateTime.UtcNow;
                var pos = new Point(pt.Position.X, pt.Position.Y);
                bool isDouble =
                    (now - _lastRightDownTime).TotalMilliseconds <= RIGHT_DBLCLICK_MS &&
                    Math.Abs(pos.X - _lastRightDownPos.X) <= RIGHT_DBLCLICK_MAXPX &&
                    Math.Abs(pos.Y - _lastRightDownPos.Y) <= RIGHT_DBLCLICK_MAXPX;
                _lastRightDownTime = now;
                _lastRightDownPos = pos;

                if (isDouble)
                {
                    ResetViewToDefaults();
                    _mouseLeftDown = _mouseRightDown = _mouseMiddleDown = false;
                    e.Handled = true;
                    return;
                }
            }

            if (CurrentTool != ViewerTool.Cursor) return;
            if (HasVideo) return;

            _mouseLeftDown   = props.IsLeftButtonPressed;
            _mouseRightDown  = props.IsRightButtonPressed;
            _mouseMiddleDown = props.IsMiddleButtonPressed;

            _mouseStart        = new Point(pt.Position.X, pt.Position.Y);
            _zoomAnchorScreen  = _mouseStart;
            _zoomStartScale    = _currentScale;

            _wlStartCenter = WindowCenter;
            _wlStartWidth  = WindowWidth;

            _startX = Img?.TranslationX ?? 0;
            _startY = Img?.TranslationY ?? 0;

            try { _native.CapturePointer(e.Pointer); } catch { }
            e.Handled = true;
        }

        private void Native_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_native == null) return;

            var pt  = e.GetCurrentPoint(_native);
            MaybeToggleToolbar(new Point(pt.Position.X, pt.Position.Y));

            if (CurrentTool != ViewerTool.Cursor) { e.Handled = true; return; }
            if (HasVideo || (!_mouseLeftDown && !_mouseRightDown && !_mouseMiddleDown)) { e.Handled = true; return; }

            var pos = new Point(pt.Position.X, pt.Position.Y);
            var dx = pos.X - _mouseStart.X;
            var dy = pos.Y - _mouseStart.Y;

            if (_mouseLeftDown && Item?.RenderFrameWithWindow != null)
            {
                var newWidth  = Math.Max(1, _wlStartWidth  + (dx * WLWidthSensitivity));
                var newCenter =           _wlStartCenter - (dy * WLCenterSensitivity);
                if (Math.Abs(newWidth  - WindowWidth)  >= WL_MIN_STEP_W) WindowWidth  = newWidth;
                if (Math.Abs(newCenter - WindowCenter) >= WL_MIN_STEP_C) WindowCenter = newCenter;
            }
            else if (_mouseRightDown)
            {
                var factor   = Math.Pow(1.01, -dy);
                var newScale = Math.Clamp(_zoomStartScale * factor, MIN_SCALE, MAX_SCALE);
                ZoomAt(_zoomAnchorScreen, newScale);
            }
            else if (_mouseMiddleDown && Img != null)
            {
                Img.TranslationX = _startX + dx;
                Img.TranslationY = _startY + dy;
                ClampTranslation();
            }

            e.Handled = true;
        }

        private void Native_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _mouseLeftDown = _mouseRightDown = _mouseMiddleDown = false;
            try { _native?.ReleasePointerCapture(e.Pointer); } catch { }
            e.Handled = true;
        }
#endif

        // ===== Zoom / Pan Utils =====
        private void ClampTranslation()
        {
            if (Img == null) return;

            if (_currentScale <= 1)
            {
                Img.TranslationX = 0;
                Img.TranslationY = 0;
                return;
            }

            double contentW = Container?.Width ?? 0;
            double contentH = Container?.Height ?? 0;
            if (Img.Width <= 0 || Img.Height <= 0 || contentW <= 0 || contentH <= 0) return;

            double scaledW = Img.Width * _currentScale;
            double scaledH = Img.Height * _currentScale;

            double maxX = Math.Max(0, (scaledW - contentW) / 2);
            double maxY = Math.Max(0, (scaledH - contentH) / 2);

            Img.TranslationX = Math.Clamp(Img.TranslationX, -maxX, maxX);
            Img.TranslationY = Math.Clamp(Img.TranslationY, -maxY, maxY);
        }

        private void ZoomAt(Point screenPt, double newScale)
        {
            if (Img == null) return;

            newScale = Math.Clamp(newScale, MIN_SCALE, MAX_SCALE);

            double cx = (Container?.Width ?? 0) * 0.5;
            double cy = (Container?.Height ?? 0) * 0.5;

            double s = _currentScale;
            double k = (s <= 0) ? 1.0 : newScale / s;

            double newTx = (1 - k) * (screenPt.X - cx) + k * Img.TranslationX;
            double newTy = (1 - k) * (screenPt.Y - cy) + k * Img.TranslationY;

            Img.Scale = newScale;
            Img.TranslationX = newTx;
            Img.TranslationY = newTy;

            _currentScale = newScale;
            ClampTranslation();
            OnPropertyChanged(nameof(OverlayZoomText));
        }

        // ===== Reset (Doppel-Rechtsklick) =====
        private void ResetViewToDefaults()
        {
            if (Img != null)
            {
                Img.Scale = 1;
                Img.TranslationX = 0;
                Img.TranslationY = 0;
            }
            _currentScale = 1;
            OnPropertyChanged(nameof(OverlayZoomText));
            ResetWindowLevelToMetadata();
        }

        private void ResetWindowLevelToMetadata()
        {
            var (c, w) = GetWindowDefaultsFromMetadata();
            if (double.IsFinite(c)) WindowCenter = c;
            if (double.IsFinite(w)) WindowWidth = Math.Max(1, w);
        }

        private (double center, double width) GetWindowDefaultsFromMetadata()
        {
            if (Item?.DefaultWindowCenter is double dc && Item?.DefaultWindowWidth is double dw)
                return (dc, dw);

            double c = ParseDicomDouble("0028,1050", "(0028,1050)");
            double w = ParseDicomDouble("0028,1051", "(0028,1051)");

            if (!double.IsFinite(c)) c = 50.0;
            if (!double.IsFinite(w)) w = 350.0;
            return (c, w);
        }

        private double ParseDicomDouble(params string[] keys)
        {
            var s = GetTagValue(keys) ?? "";
            if (string.IsNullOrWhiteSpace(s)) return double.NaN;

            var token = s.Split('\\', '/', ';', '|')[0].Trim();
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;
            if (double.TryParse(token, out v))
                return v;
            return double.NaN;
        }

        // ===================== Overlay-Fallbacks / DICOM-Tags =====================
        public string OverlayPatientNameWithSex
            => !string.IsNullOrWhiteSpace(PatientNameWithSex) ? PatientNameWithSex : ComposeNameWithSex();
        public string OverlaySpeciesBreed
        {
            get
            {
                var sp = !string.IsNullOrWhiteSpace(Species) ? Species : (GetTagValue("0010,2201", "(0010,2201)") ?? "");
                var br = GetTagValue("0010,2292", "(0010,2292)") ?? "";
                if (string.IsNullOrWhiteSpace(br)) return sp;
                if (string.IsNullOrWhiteSpace(sp)) return br;
                return $"{sp} / {br}";
            }
        }
        public string OverlayPatientID
            => !string.IsNullOrWhiteSpace(PatientID) ? PatientID : GetTagValue("0010,0020", "(0010,0020)") ?? "";
        public string OverlayBirthDateDisplay
            => !string.IsNullOrWhiteSpace(BirthDateDisplay) ? BirthDateDisplay : FormatDicomDate(GetTagValue("0010,0030", "(0010,0030)"));
        public string OverlayOtherPid
            => !string.IsNullOrWhiteSpace(OtherPid) ? OtherPid : GetTagValue("0010,1000", "(0010,1000)") ?? "";
        public string OverlayInstanceNumber => GetTagValue("0020,0013", "(0020,0013)") ?? "";
        public string OverlayStudyDescription => GetTagValue("0008,1030", "(0008,1030)") ?? "";
        public string OverlayStudyDate => FormatDicomDate(GetTagValue("0008,0020", "(0008,0020)"));
        public string OverlayStudyTime => FormatDicomTime(GetTagValue("0008,0030", "(0008,0030)"));
        public string OverlaySeriesNumber => GetTagValue("0020,0011", "(0020,0011)") ?? "";
        public string OverlaySliceLocation => GetTagValue("0020,1041", "(0020,1041)") ?? "";
        public string OverlaySliceThickness => GetTagValue("0018,0050", "(0018,0050)") ?? "";

        private void RaiseOverlayComputed()
        {
            OnPropertyChanged(nameof(OverlayPatientNameWithSex));
            OnPropertyChanged(nameof(OverlaySpeciesBreed));
            OnPropertyChanged(nameof(OverlayPatientID));
            OnPropertyChanged(nameof(OverlayBirthDateDisplay));
            OnPropertyChanged(nameof(OverlayOtherPid));
            OnPropertyChanged(nameof(OverlayInstanceNumber));
            OnPropertyChanged(nameof(OverlayStudyDescription));
            OnPropertyChanged(nameof(OverlayStudyDate));
            OnPropertyChanged(nameof(OverlayStudyTime));
            OnPropertyChanged(nameof(OverlaySeriesNumber));
            OnPropertyChanged(nameof(OverlaySliceLocation));
            OnPropertyChanged(nameof(OverlaySliceThickness));
        }

        private string ComposeNameWithSex()
        {
            var name = GetTagValue("0010,0010", "(0010,0010)") ?? "";
            var sex = GetTagValue("0010,0040", "(0010,0040)") ?? "";
            sex = sex switch { "M" => " ♂", "F" => " ♀", _ => string.IsNullOrWhiteSpace(sex) ? "" : $" ({sex})" };
            return string.IsNullOrWhiteSpace(name) ? "" : name + sex;
        }

        private string? GetTagValue(params string[] keys)
        {
            if (Item?.RowMap == null) return null;

            foreach (var key in keys)
            {
                if (Item.RowMap.TryGetValue(key, out var row) && !string.IsNullOrWhiteSpace(row?.Value))
                    return row.Value;

                var k2 = key.Replace("(", "").Replace(")", "").ToUpperInvariant();
                if (Item.RowMap.TryGetValue(k2, out row) && !string.IsNullOrWhiteSpace(row?.Value))
                    return row.Value;
            }
            return null;
        }

        private static string FormatDicomDate(string? dicom)
        {
            if (string.IsNullOrWhiteSpace(dicom)) return "";
            if (dicom!.Length >= 8 &&
                int.TryParse(dicom.Substring(0, 4), out var y) &&
                int.TryParse(dicom.Substring(4, 2), out var m) &&
                int.TryParse(dicom.Substring(6, 2), out var d))
            {
                try { return new DateTime(y, m, d).ToString("yyyy-MM-dd"); }
                catch { }
            }
            return dicom;
        }

        private static string FormatDicomTime(string? dicom)
        {
            if (string.IsNullOrWhiteSpace(dicom)) return "";
            var t = dicom!;
            if (t.Length >= 6 &&
                int.TryParse(t.Substring(0, 2), out var hh) &&
                int.TryParse(t.Substring(2, 2), out var mm) &&
                int.TryParse(t.Substring(4, 2), out var ss))
            {
                try { return new TimeSpan(hh, mm, ss).ToString(@"hh\:mm\:ss"); }
                catch { }
            }
            return dicom;
        }

        // ======= Toolbar Peek/Auto-Hide =======
        const double TOOL_REVEAL_MARGIN = 12;   // Mausnähe in px zum Einblenden
        const double TOOL_PEEK_PIXELS = 6;    // sichtbarer Rand im eingeklappten Zustand
        readonly TimeSpan TOOLBAR_HIDE_DELAY = TimeSpan.FromMilliseconds(1200);

        double _toolbarHiddenY = -40;
        bool _toolbarVisible;
        DateTime _toolbarKeepAliveStamp = DateTime.MinValue;

        void InitToolbarPeek()
        {
            if (TopToolbar?.Height > 0)
            {
                _toolbarHiddenY = -(TopToolbar.Height - TOOL_PEEK_PIXELS);
                TopToolbar.TranslationY = _toolbarHiddenY;
                _toolbarVisible = false;
            }
        }

        void MaybeToggleToolbar(Point screenPos)
        {
            if (TopToolbar == null) return;

            // nahe am oberen Rand?
            if (screenPos.Y <= TOOL_REVEAL_MARGIN)
                ShowToolbar();
            else
                ScheduleToolbarHide();
        }

        void ShowToolbar()
        {
            if (TopToolbar == null) return;
            _toolbarKeepAliveStamp = DateTime.UtcNow;
            if (_toolbarVisible) { ScheduleToolbarHide(); return; }

            _toolbarVisible = true;
            TopToolbar.AbortAnimation("tb");
            TopToolbar.TranslateTo(0, 0, 150, Easing.CubicOut);
            ScheduleToolbarHide();
        }

        void ScheduleToolbarHide()
        {
            var stamp = _toolbarKeepAliveStamp = DateTime.UtcNow;
            (Dispatcher ?? Microsoft.Maui.Controls.Application.Current?.Dispatcher)?.StartTimer(TOOLBAR_HIDE_DELAY, () =>
            {
                // nur hide, wenn kein neuer KeepAlive kam
                if (_toolbarKeepAliveStamp != stamp) return false;
                HideToolbar();
                return false;
            });
        }

        void HideToolbar()
        {
            if (TopToolbar == null || !_toolbarVisible) return;
            _toolbarVisible = false;
            TopToolbar.AbortAnimation("tb");
            var y = _toolbarHiddenY;
            TopToolbar.TranslateTo(0, y, 180, Easing.CubicIn);
        }
    }
}
