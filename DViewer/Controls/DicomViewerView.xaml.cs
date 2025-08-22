// === DicomViewerView.cs (zusammengeführt) ===
using System;
using System.ComponentModel;
using System.Globalization;
using System.Collections.Generic;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using CommunityToolkit.Maui.Views;
using DViewer.Controls.Tools;
using DViewer.Controls.Overlays;
using System.Reflection;
using System.Runtime.ConstrainedExecution;

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

            // ToolOverlay: Drawable, das die aktiven Tools zeichnen lässt
            if (ToolOverlay != null)
                ToolOverlay.Drawable = new ToolOverlayDrawable(this);

            // Toolbar initial peeking
            TopToolbar.SizeChanged += (_, __) => InitToolbarPeek();

            // Aktuelles Tool initialisieren
            OnToolChanged();

            UpdateToolButtons();

            DViewer.Controls.Overlays.MeasureStore.Shared.Changed += OnMeasureStoreChanged;

        }

        // ======== Tool-Auswahl (Cursor / Measure) ========
        public enum ViewerTool { Cursor, Measure }

        public static readonly BindableProperty CurrentToolProperty =
            BindableProperty.Create(
                nameof(CurrentTool),
                typeof(ViewerTool),
                typeof(DicomViewerView),
                ViewerTool.Cursor,
                propertyChanged: (b, o, n) => ((DicomViewerView)b).OnToolChanged());

        public ViewerTool CurrentTool
        {
            get => (ViewerTool)GetValue(CurrentToolProperty);
            set => SetValue(CurrentToolProperty, value);
        }

        IViewerTool? _currentToolRef;
        readonly CursorTool _cursorTool = new();
        readonly MeasureTool _measureTool = new();

        void OnToolChanged()
        {
            _currentToolRef?.OnDeactivated(this);
            _currentToolRef = CurrentTool switch
            {
                ViewerTool.Measure => _measureTool,
                _ => _cursorTool
            };
            _currentToolRef.OnActivated(this);
            UpdateToolButtons();
            InvalidateToolOverlay();
        }

        // --- Cursor visuals (best effort) ---
        public enum HoverCursor { Arrow, Crosshair, SizeAll }
        public void SetHoverCursor(HoverCursor c)
        {
#if WINDOWS
    // WinUI3: ProtectedCursor ist nicht öffentlich setzbar.
    // Außerdem sind InputCursor/InputSystemCursor-APIs zwischen Versionen inkonsistent.
    // -> sichere No-Op, damit alle TargetFrameworks bauen.
    return;
#else
            // Plattformen ohne Cursor-API: No-Op
            return;
#endif
        }

        void OnCursorToolClicked(object? s, EventArgs e) { CurrentTool = ViewerTool.Cursor; }
        void OnMeasureToolClicked(object? s, EventArgs e) { CurrentTool = ViewerTool.Measure; }

        void UpdateToolButtons()
        {
            if (CursorToolFrame != null)
                CursorToolFrame.BackgroundColor = CurrentTool == ViewerTool.Cursor ? Color.FromArgb("#22FFFFFF") : Colors.Transparent;
            if (MeasureToolFrame != null)
                MeasureToolFrame.BackgroundColor = CurrentTool == ViewerTool.Measure ? Color.FromArgb("#22FFFFFF") : Colors.Transparent;

            InvalidateToolOverlay();
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
        private void OnFrameIndexChanged()
        {
            RenderCurrent();
            InvalidateToolOverlay(); // Overlays pro Frame neu
        }

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
        public double MinScale => 1.0;
        public double MaxScale => 12.0;

        double _currentScale = 1;
        public double CurrentScale => _currentScale;

        public void SetScale(double s)
        {
            if (Img == null) return;
            Img.Scale = Math.Clamp(s, MinScale, MaxScale);
        }

        public void CommitScale()
        {
            if (Img == null) return;
            _currentScale = Img.Scale;
            ClampTranslation();
            OnPropertyChanged(nameof(OverlayZoomText));
        }

        public void SetImageAnchor(double ax, double ay)
        {
            if (Img == null) return;
            Img.AnchorX = ax;
            Img.AnchorY = ay;
        }

        public void GetTranslation(out double tx, out double ty)
        {
            tx = Img?.TranslationX ?? 0;
            ty = Img?.TranslationY ?? 0;
        }

        public void SetTranslation(double tx, double ty)
        {
            if (Img == null) return;
            Img.TranslationX = tx;
            Img.TranslationY = ty;
            ClampTranslation();
            InvalidateToolOverlay();
        }

        // --- Public API für Tools (WL/WW) ---
        public void SetWindowCenter(double c) => WindowCenter = c;
        public void SetWindowWidth(double w) => WindowWidth = Math.Max(1, w);

        // --- Public Zugriff auf <Image> (statt private Feld "Img") ---
        public Image? ImageElement => Img;

        public bool CanWindowLevel => Item?.RenderFrameWithWindow != null && !HasVideo;

        double _startX, _startY;
        bool _pinching;

        const double WLWidthSensitivity = 2.0;
        const double WLCenterSensitivity = 2.0;
        const double WL_MIN_STEP_W = 1;
        const double WL_MIN_STEP_C = 1;

        double _wlStartCenter, _wlStartWidth;

        // Maus: Links = WL (nur Cursor-Tool), Rechts = Zoom, Mitte = Move
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
            // An aktives Tool weiterleiten
            _currentToolRef?.OnPinch(this, e);
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
                    var newScale = Math.Clamp(_currentScale * e.Scale, MinScale, MaxScale);
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
            // An aktives Tool weiterleiten
            _currentToolRef?.OnPan(this, e);
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
            // Als "Two-Finger-Pan" an Tool weiterreichen (für Tools, die das verwenden)
            _currentToolRef?.OnTwoFingerPan(this, e);
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
            var posForTool = e.GetPosition(Container) ?? default;
            // an Tool weiterleiten
            _currentToolRef?.OnPointerPressed(this, posForTool, left: true, right: false, middle: false);

#if WINDOWS
            MaybeToggleToolbar(posForTool);
            return;
#else
            MaybeToggleToolbar(posForTool);
            if (CurrentTool != ViewerTool.Cursor) return;
            if (HasVideo) return;

            _mouseStart = posForTool;
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
            var posForTool = e.GetPosition(Container) ?? default;
            // an Tool weiterleiten
            _currentToolRef?.OnPointerMoved(this, posForTool);
#if WINDOWS
            MaybeToggleToolbar(posForTool);
            return;
#else
            MaybeToggleToolbar(posForTool);
            if (CurrentTool != ViewerTool.Cursor) return;
            if (HasVideo || (!_mouseLeftDown && !_mouseRightDown && !_mouseMiddleDown)) return;

            var pos = posForTool;
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
                var newScale = Math.Clamp(_zoomStartScale * factor, MinScale, MaxScale);
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
            var posForTool = e.GetPosition(Container) ?? default;
            // an Tool weiterleiten
            _currentToolRef?.OnPointerReleased(this, posForTool);
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
            var pos   = new Point(pt.Position.X, pt.Position.Y);

            // an Tool weiterleiten (mit echten Buttons)
            _currentToolRef?.OnPointerPressed(this, pos, props.IsLeftButtonPressed, props.IsRightButtonPressed, props.IsMiddleButtonPressed);

            MaybeToggleToolbar(pos);

            // Doppel-Rechtsklick Reset
            if (props.IsRightButtonPressed)
            {
                var now = DateTime.UtcNow;
                bool isDouble =
                    (now - _lastRightDownTime).TotalMilliseconds <= RIGHT_DBLCLICK_MS &&
                    Math.Abs(pos.X - _lastRightDownPos.X) <= RIGHT_DBLCLICK_MAXPX &&
                    Math.Abs(pos.Y - _lastRightDownPos.Y) <= RIGHT_DBLCLICK_MAXPX;
                _lastRightDownTime = now;
                _lastRightDownPos  = pos;

                if (isDouble)
                {
                    ResetViewToDefaults();
                    _mouseLeftDown = _mouseRightDown = _mouseMiddleDown = false;
                    e.Handled = true;
                    return;
                }
            }

            if (CurrentTool != ViewerTool.Cursor || HasVideo)
            {
                e.Handled = true;
                try { _native.CapturePointer(e.Pointer); } catch { }
                return;
            }

            _mouseLeftDown   = props.IsLeftButtonPressed;
            _mouseRightDown  = props.IsRightButtonPressed;
            _mouseMiddleDown = props.IsMiddleButtonPressed;

            _mouseStart        = pos;
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
            var pos = new Point(pt.Position.X, pt.Position.Y);

            // an Tool weiterleiten
            _currentToolRef?.OnPointerMoved(this, pos);

            MaybeToggleToolbar(pos);

            if (CurrentTool != ViewerTool.Cursor || HasVideo || (!_mouseLeftDown && !_mouseRightDown && !_mouseMiddleDown))
            {
                e.Handled = true;
                return;
            }

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
                var newScale = Math.Clamp(_zoomStartScale * factor, MinScale, MaxScale);
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
            // an Tool weiterleiten (Position bei Release)
            if (_native != null)
            {
                var pt  = e.GetCurrentPoint(_native);
                var pos = new Point(pt.Position.X, pt.Position.Y);
                _currentToolRef?.OnPointerReleased(this, pos);
            }

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

        public void ZoomAt(Point screenPt, double newScale)
        {
            if (Img == null) return;

            newScale = Math.Clamp(newScale, MinScale, MaxScale);

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
            InvalidateToolOverlay();
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
            InvalidateToolOverlay();
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

        // ======= DICOM/Overlay Helpers =======
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
        const double TOOL_REVEAL_MARGIN = 12;
        const double TOOL_PEEK_PIXELS = 6;
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

        // ======= Tool Overlay: Drawable, das das aktive Tool zeichnet =======
        private sealed class ToolOverlayDrawable : IDrawable
        {
            private readonly DicomViewerView _owner;
            public ToolOverlayDrawable(DicomViewerView owner) { _owner = owner; }
            public void Draw(ICanvas canvas, RectF dirtyRect)
            {
                _owner._currentToolRef?.Draw(canvas, dirtyRect, _owner);
            }
        }

        public void InvalidateToolOverlay() => ToolOverlay?.Invalidate();

        // ======= DICOM-Geometrie & Koordinaten-Konvertierung =======
        public bool TryGetImagePixelSize(out int cols, out int rows)
        {
            cols = rows = 0;
            var sCols = GetTagValue("0028,0011", "(0028,0011)"); // Columns
            var sRows = GetTagValue("0028,0010", "(0028,0010)"); // Rows
            if (int.TryParse(sCols, out cols) && int.TryParse(sRows, out rows) && cols > 0 && rows > 0)
                return true;
            return false;
        }

        public bool TryGetPixelSpacing(out double rowMm, out double colMm)
        {
            rowMm = colMm = 0;
            var ps = GetTagValue("0028,0030", "(0028,0030)"); // Pixel Spacing: Row\Col
            if (string.IsNullOrWhiteSpace(ps)) return false;

            var parts = ps.Split('\\', '/', ';', '|');
            if (parts.Length >= 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out rowMm) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out colMm) &&
                rowMm > 0 && colMm > 0)
                return true;

            return false;
        }

        // Screen → Image (col,row)
        public bool TryScreenToImage(Point screen, out PointF img)
        {
            img = default;
            if (Img == null) return false;
            if (!TryGetImagePixelSize(out var cols, out var rows)) return false;

            var cx = (Container?.Width ?? 0) * 0.5;
            var cy = (Container?.Height ?? 0) * 0.5;

            var sBase = Math.Min(Img.Width / cols, Img.Height / rows); // AspectFit
            var s = sBase * _currentScale;
            if (s <= 0) return false;

            var left = cx - (cols * s) / 2 + Img.TranslationX;
            var top = cy - (rows * s) / 2 + Img.TranslationY;

            var col = (screen.X - left) / s;
            var row = (screen.Y - top) / s;

            img = new PointF((float)col, (float)row);
            return true;
        }

        // Image (col,row) → Screen
        public bool TryImageToScreen(PointF img, out Point screen)
        {
            screen = default;
            if (Img == null) return false;
            if (!TryGetImagePixelSize(out var cols, out var rows)) return false;

            var cx = (Container?.Width ?? 0) * 0.5;
            var cy = (Container?.Height ?? 0) * 0.5;

            var sBase = Math.Min(Img.Width / cols, Img.Height / rows);
            var s = sBase * _currentScale;
            if (s <= 0) return false;

            var left = cx - (cols * s) / 2 + Img.TranslationX;
            var top = cy - (rows * s) / 2 + Img.TranslationY;

            var x = left + img.X * s;
            var y = top + img.Y * s;

            screen = new Point(x, y);
            return true;
        }

        // Länge zwischen zwei Bildpunkten
        public (bool mmOk, double mm, double px) MeasureLength(PointF a, PointF b)
        {
            var dxPx = b.X - a.X;
            var dyPx = b.Y - a.Y;
            var pxLen = Math.Sqrt(dxPx * dxPx + dyPx * dyPx);

            if (TryGetPixelSpacing(out var rowMm, out var colMm))
            {
                var mmX = dxPx * colMm;
                var mmY = dyPx * rowMm;
                var mm = Math.Sqrt(mmX * mmX + mmY * mmY);
                return (true, mm, pxLen);
            }
            return (false, 0, pxLen);
        }

        // ======= Overlay-Storage (pro Bild/Frame) =======
        //readonly Dictionary<string, List<MeasureShape>> _measuresByFrame = new();

        // Eindeutiger Schlüssel (SOP|Frame)
        string CurrentImageKey
        {
            get
            {
                var sop = GetTagValue("0008,0018", "(0008,0018)") ?? ""; // SOP Instance UID
                return $"{sop}|{FrameIndex}";
            }
        }

        public IReadOnlyList<MeasureShape> GetMeasuresForCurrent()
            => DViewer.Controls.Overlays.MeasureStore.Shared.Snapshot(CurrentImageKey);

        public void AddMeasureForCurrent(MeasureShape shape)
            => DViewer.Controls.Overlays.MeasureStore.Shared.Add(CurrentImageKey, shape);

        private void OnMeasureStoreChanged(string key)
        {
            // Nur neu zeichnen, wenn unsere aktuelle Ansicht betroffen ist
            if (key == CurrentImageKey)
                InvalidateToolOverlay();
        }

        //    protected override void OnHandlerChanged()
        //{
        //    base.OnHandlerChanged();
        //    if (Handler == null)
        //        DViewer.Controls.Overlays.MeasureStore.Shared.Changed -= OnMeasureStoreChanged;
        //}

    }
}
