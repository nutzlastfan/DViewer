using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace DViewer
{
    public partial class FullscreenImagePage : ContentPage, INotifyPropertyChanged
    {
        // -------- Zoom/Pan ----------
        const double MIN_SCALE = 1.0;
        const double MAX_SCALE = 12.0;

        double _currentScale = 1;
        double _startX, _startY;
        bool _pinching;

        // -------- Window/Level ----------
        const double WLWidthSensitivity = 2.0; // ΔX -> ΔWidth
        const double WLCenterSensitivity = 2.0; // ΔY -> ΔCenter

        double _windowCenter;
        double _windowWidth;

        public double WindowCenter
        {
            get => _windowCenter;
            private set { if (Math.Abs(_windowCenter - value) < double.Epsilon) return; _windowCenter = value; OnPropertyChanged(); }
        }
        public double WindowWidth
        {
            get => _windowWidth;
            private set { if (Math.Abs(_windowWidth - value) < double.Epsilon) return; _windowWidth = Math.Max(1, value); OnPropertyChanged(); }
        }

        double _wlStartCenter, _wlStartWidth;
        System.Threading.CancellationTokenSource? _wlCts;

        // -------- Renderer ----------
        readonly Func<double, double, int, ImageSource>? _render3;
        readonly Func<double, double, ImageSource>? _render2;
        int _frameIndex = 0;
        readonly Func<int>? _frameIndexProvider;   // optional: VM-Frame liefert Play-Updates

        // -------- Patient-Overlay ----------
        public string? PatientNameWithSex { get; }
        public string? Species { get; }
        public string? PatientID { get; }
        public string? BirthDateDisplay { get; }
        public string? OtherPid { get; }

        // -------- Frame-Overlay ----------
        int _frameCount;
        public int FrameCount
        {
            get => _frameCount;
            private set { if (_frameCount == value) return; _frameCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(FramePositionText)); }
        }
        public int FrameIndex
        {
            get => _frameIndex;
            private set { if (_frameIndex == value) return; _frameIndex = value; OnPropertyChanged(nameof(FrameIndex)); OnPropertyChanged(nameof(FramePositionText)); }
        }
        public string FramePositionText => FrameCount > 0 ? $"Frame {Math.Min(FrameIndex + 1, FrameCount)} / {FrameCount}" : string.Empty;

        IDispatcherTimer? _pollTimer; // folgt optional externem FrameIndex

        // ================== CONSTRUCTORS ==================

        // A) center,width,frame (+ optionaler frameIndexProvider + frameCount)
        public FullscreenImagePage(
            Func<double, double, int, ImageSource> renderWithWindow,
            double initialCenter,
            double initialWidth,
            int frameIndex,
            string? title = null,
            string? patientNameWithSex = null,
            string? species = null,
            string? patientId = null,
            string? birthDateDisplay = null,
            string? otherPid = null,
            Func<int>? frameIndexProvider = null,
            int frameCount = 0)
        {
            InitializeComponent();
            NavigationPage.SetHasNavigationBar(this, false);
            Title = title ?? "Vollbild";

            PatientNameWithSex = patientNameWithSex;
            Species = species;
            PatientID = patientId;
            BirthDateDisplay = birthDateDisplay;
            OtherPid = otherPid;

            _render3 = renderWithWindow;
            _frameIndex = Math.Max(0, frameIndex);
            _frameIndexProvider = frameIndexProvider;
            FrameCount = Math.Max(0, frameCount);

            WindowCenter = initialCenter;
            WindowWidth = Math.Max(1, initialWidth);

            Img.Source = _render3(WindowCenter, WindowWidth, _frameIndex);
            BindingContext = this;

            // Wenn ein externer Frame-Provider kommt, poll’e ihn leichtgewichtig
            if (_frameIndexProvider != null)
            {
                _pollTimer = Dispatcher.CreateTimer();
                _pollTimer.Interval = TimeSpan.FromMilliseconds(33); // ~30 fps
                _pollTimer.Tick += (_, __) =>
                {
                    try
                    {
                        var idx = _frameIndexProvider();
                        if (idx != FrameIndex)
                        {
                            FrameIndex = idx;
                            if (_render3 != null)
                                Img.Source = _render3(WindowCenter, WindowWidth, FrameIndex);
                        }
                    }
                    catch { /* still */ }
                };
                _pollTimer.Start();
            }
        }

        // B) center,width
        public FullscreenImagePage(
            Func<double, double, ImageSource> renderWithWindow,
            double initialCenter,
            double initialWidth,
            string? title = null,
            string? patientNameWithSex = null,
            string? species = null,
            string? patientId = null,
            string? birthDateDisplay = null,
            string? otherPid = null)
        {
            InitializeComponent();
            NavigationPage.SetHasNavigationBar(this, false);
            Title = title ?? "Vollbild";

            PatientNameWithSex = patientNameWithSex;
            Species = species;
            PatientID = patientId;
            BirthDateDisplay = birthDateDisplay;
            OtherPid = otherPid;

            _render2 = renderWithWindow;

            WindowCenter = initialCenter;
            WindowWidth = Math.Max(1, initialWidth);
            Img.Source = _render2(WindowCenter, WindowWidth);

            BindingContext = this;
        }

        // C) statisches Bild
        public FullscreenImagePage(
            ImageSource source,
            string? title = null,
            string? patientNameWithSex = null,
            string? species = null,
            string? patientId = null,
            string? birthDateDisplay = null,
            string? otherPid = null)
        {
            InitializeComponent();
            NavigationPage.SetHasNavigationBar(this, false);
            Title = title ?? "Vollbild";

            PatientNameWithSex = patientNameWithSex;
            Species = species;
            PatientID = patientId;
            BirthDateDisplay = birthDateDisplay;
            OtherPid = otherPid;

            Img.Source = source;
            WindowCenter = 0;
            WindowWidth = 0;
            BindingContext = this;
        }

        // ================== TOUCH / TRACKPAD ==================
        private async void OnCloseTapped(object? s, TappedEventArgs e) => await Navigation.PopModalAsync();

        private void OnDoubleTapped(object? s, TappedEventArgs e)
        {
            _currentScale = 1;
            Img.Scale = 1;
            Img.TranslationX = 0;
            Img.TranslationY = 0;
        }

        private void OnPinchUpdated(object? s, PinchGestureUpdatedEventArgs e)
        {
            switch (e.Status)
            {
                case GestureStatus.Started:
                    _pinching = true;
                    Img.AnchorX = e.ScaleOrigin.X;
                    Img.AnchorY = e.ScaleOrigin.Y;
                    break;
                case GestureStatus.Running:
                    Img.Scale = Math.Clamp(_currentScale * e.Scale, MIN_SCALE, MAX_SCALE);
                    break;
                case GestureStatus.Canceled:
                case GestureStatus.Completed:
                    _pinching = false;
                    _currentScale = Img.Scale;
                    ClampTranslation();
                    break;
            }
        }

        private void OnPanUpdated(object? s, PanUpdatedEventArgs e)
        {
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
        }

        // 2-Finger-Pan = Window/Level (debounced)
        private void OnWlPanUpdated(object? s, PanUpdatedEventArgs e)
        {
            if (_render3 == null && _render2 == null) return;
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
                    ApplyWindow(newCenter, newWidth);
                    break;
            }
        }

        void ApplyWindow(double center, double width)
        {
            WindowCenter = center;
            WindowWidth = width;
            DebouncedRender();
        }

        void DebouncedRender(int delayMs = 15)
        {
            if (_render3 == null && _render2 == null) return;

            _wlCts?.Cancel();
            var cts = new System.Threading.CancellationTokenSource();
            _wlCts = cts;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(delayMs, cts.Token);
                    if (cts.IsCancellationRequested) return;

                    if (_render3 != null) Img.Source = _render3(WindowCenter, WindowWidth, FrameIndex);
                    else Img.Source = _render2!(WindowCenter, WindowWidth);
                }
                catch { /* still */ }
                finally
                {
                    if (ReferenceEquals(_wlCts, cts)) _wlCts = null;
                }
            });
        }

        void ClampTranslation()
        {
            if (_currentScale <= 1)
            {
                Img.TranslationX = Img.TranslationY = 0;
                return;
            }

            double contentW = Root.Width;
            double contentH = Root.Height;
            if (Img.Width <= 0 || Img.Height <= 0 || contentW <= 0 || contentH <= 0) return;

            double scaledW = Img.Width * _currentScale;
            double scaledH = Img.Height * _currentScale;

            double maxX = Math.Max(0, (scaledW - contentW) / 2);
            double maxY = Math.Max(0, (scaledH - contentH) / 2);

            Img.TranslationX = Math.Clamp(Img.TranslationX, -maxX, maxX);
            Img.TranslationY = Math.Clamp(Img.TranslationY, -maxY, maxY);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            try { _pollTimer?.Stop(); } catch { }
            _pollTimer = null;
            _wlCts?.Cancel();
            _wlCts = null;
        }

        // INotifyPropertyChanged
        public new event PropertyChangedEventHandler? PropertyChanged;
        private new void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

// ================== WINDOWS: Maus (Links=WL, Rechts=Zoom-to-cursor) ==================
#if WINDOWS
namespace DViewer
{
    public partial class FullscreenImagePage
    {
        Microsoft.UI.Xaml.FrameworkElement? _winRoot;
        bool _winLeftDown, _winRightDown;
        Microsoft.Maui.Graphics.Point _winStart;
        double _winZoomStart;

        protected override void OnHandlerChanged()
        {
            base.OnHandlerChanged();

            if (_winRoot is not null)
            {
                _winRoot.PointerPressed  -= OnWinPointerPressed;
                _winRoot.PointerMoved    -= OnWinPointerMoved;
                _winRoot.PointerReleased -= OnWinPointerReleased;
                _winRoot.PointerCanceled -= OnWinPointerReleased;
                _winRoot.PointerExited   -= OnWinPointerReleased;
            }

            _winRoot = this.Root?.Handler?.PlatformView as Microsoft.UI.Xaml.FrameworkElement;
            if (_winRoot is null) return;

            _winRoot.PointerPressed  += OnWinPointerPressed;
            _winRoot.PointerMoved    += OnWinPointerMoved;
            _winRoot.PointerReleased += OnWinPointerReleased;
            _winRoot.PointerCanceled += OnWinPointerReleased;
            _winRoot.PointerExited   += OnWinPointerReleased;
        }

        void OnWinPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_winRoot is null) return;

            var pt    = e.GetCurrentPoint(_winRoot);
            var props = pt.Properties;

            _winLeftDown  = props.IsLeftButtonPressed;
            _winRightDown = props.IsRightButtonPressed;

            _winStart      = new Microsoft.Maui.Graphics.Point(pt.Position.X, pt.Position.Y);
            _winZoomStart  = _currentScale;
            _wlStartCenter = WindowCenter;
            _wlStartWidth  = WindowWidth;

            _winRoot.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        void OnWinPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_winRoot is null) return;
            if (!_winLeftDown && !_winRightDown) return;

            var pt  = e.GetCurrentPoint(_winRoot);
            var pos = new Microsoft.Maui.Graphics.Point(pt.Position.X, pt.Position.Y);

            var dx = pos.X - _winStart.X;
            var dy = pos.Y - _winStart.Y;

            if (_winLeftDown && (_render3 != null || _render2 != null))
            {
                var newWidth  = Math.Max(1, _wlStartWidth  + (dx * WLWidthSensitivity));
                var newCenter =           _wlStartCenter - (dy * WLCenterSensitivity);
                ApplyWindow(newCenter, newWidth);
            }
            else if (_winRightDown)
            {
                // Zoom-to-cursor
                var factor   = Math.Pow(1.01, -dy);
                var newScale = Math.Clamp(_winZoomStart * factor, MIN_SCALE, MAX_SCALE);
                ZoomAt(pos, newScale);
            }

            e.Handled = true;
        }

        void OnWinPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _winLeftDown  = false;
            _winRightDown = false;
            try { _winRoot?.ReleasePointerCapture(e.Pointer); } catch { }
            e.Handled = true;
        }

        // Zoom so, dass screenPt (Root-Koords) stabil bleibt
        void ZoomAt(Microsoft.Maui.Graphics.Point screenPt, double newScale)
        {
            newScale = Math.Clamp(newScale, MIN_SCALE, MAX_SCALE);

            double cx = Root.Width  * 0.5;
            double cy = Root.Height * 0.5;

            double s = _currentScale;
            double k = (s <= 0) ? 1.0 : newScale / s;

            double newTx = (1 - k) * (screenPt.X - cx) + k * Img.TranslationX;
            double newTy = (1 - k) * (screenPt.Y - cy) + k * Img.TranslationY;

            Img.Scale = newScale;
            Img.TranslationX = newTx;
            Img.TranslationY = newTy;

            _currentScale = newScale;
            ClampTranslation();
        }
    }
}
#endif
