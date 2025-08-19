using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace DViewer
{
    public partial class FullscreenImagePage : ContentPage, INotifyPropertyChanged
    {
        // --- zoom/pan state ---
        double _currentScale = 1;
        double _startX, _startY;
        bool _pinching;

        // --- window/level state (live-bound to labels) ---
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

        // sensitivity (pixels dragged -> change in WL)
        const double WLWidthSensitivity = 2.0;  // ΔX -> ΔWidth
        const double WLCenterSensitivity = 2.0;  // ΔY -> ΔCenter (negative moves brighten)

        // renderer delegate: (center, width) -> ImageSource
        readonly Func<double, double, ImageSource>? _renderWithWindow;

        // for WL interaction
        double _wlStartCenter, _wlStartWidth;

        // --- patient overlay (bind straight to the page) ---
        public string? PatientNameWithSex { get; }
        public string? Species { get; }
        public string? PatientID { get; }
        public string? BirthDateDisplay { get; }
        public string? OtherPid { get; }

        // -------------------- CONSTRUCTORS --------------------

        // 1) Simple: just show an ImageSource (zoom/pan only)
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
            BindingContext = this;
            NavigationPage.SetHasNavigationBar(this, false);
            Title = title ?? "Vollbild";
            Img.Source = source;

            PatientNameWithSex = patientNameWithSex;
            Species = species;
            PatientID = patientId;
            BirthDateDisplay = birthDateDisplay;
            OtherPid = otherPid;

            // default WL readout (not active without renderer)
            WindowCenter = 0;
            WindowWidth = 0;
        }

        // 2) Full: pass a renderer so we can re-render for Window/Level
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
            BindingContext = this;
            NavigationPage.SetHasNavigationBar(this, false);
            Title = title ?? "Vollbild";

            _renderWithWindow = renderWithWindow;
            WindowCenter = initialCenter;
            WindowWidth = Math.Max(1, initialWidth);

            // render first frame with given WL
            Img.Source = _renderWithWindow(WindowCenter, WindowWidth);

            PatientNameWithSex = patientNameWithSex;
            Species = species;
            PatientID = patientId;
            BirthDateDisplay = birthDateDisplay;
            OtherPid = otherPid;
        }

        // -------------------- GESTURES --------------------

        private async void OnCloseTapped(object? s, TappedEventArgs e)
            => await Navigation.PopModalAsync();

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
                    var newScale = Math.Max(1, _currentScale * e.Scale);
                    Img.Scale = newScale;
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    _pinching = false;
                    _currentScale = Img.Scale;
                    ClampTranslation();
                    break;
            }
        }

        private void OnPanUpdated(object? s, PanUpdatedEventArgs e)
        {
            if (_currentScale <= 1) return; // pan only when zoomed

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

        // 2-finger pan -> Window/Level
        private void OnWlPanUpdated(object? s, PanUpdatedEventArgs e)
        {
            if (_renderWithWindow == null) return;   // no renderer provided
            if (_pinching) return;                   // ignore while pinching (prevents gesture conflict)

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

            // re-render
            Img.Source = _renderWithWindow!(WindowCenter, WindowWidth);
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

        // INotifyPropertyChanged
        public new event PropertyChangedEventHandler? PropertyChanged;
        private new void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
