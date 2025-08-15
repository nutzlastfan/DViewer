using Microsoft.Maui.Controls;

namespace DViewer
{
    public partial class FullscreenImagePage : ContentPage
    {
        double _currentScale = 1;
        double _startX, _startY;


            public FullscreenImagePage(ImageSource source, string? title = null)
            {
                InitializeComponent();            // <— muss da sein
                NavigationPage.SetHasNavigationBar(this, false);
                Title = title ?? "Vollbild";
                Img.Source = source;              // <— wird aus x:Name generiert
            }


            // --- Gesten ---
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
            if (e.Status == GestureStatus.Started)
            {
                Img.AnchorX = e.ScaleOrigin.X;
                Img.AnchorY = e.ScaleOrigin.Y;
            }
            else if (e.Status == GestureStatus.Running)
            {
                var newScale = Math.Max(1, _currentScale * e.Scale);
                Img.Scale = newScale;
            }
            else if (e.Status == GestureStatus.Completed)
            {
                _currentScale = Img.Scale;
                ClampTranslation(); // nach dem Zoomen begrenzen
            }
        }

        private void OnPanUpdated(object? s, PanUpdatedEventArgs e)
        {
            if (_currentScale <= 1) return; // nur pannen, wenn gezoomt

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

        void ClampTranslation()
        {
            if (_currentScale <= 1) { Img.TranslationX = Img.TranslationY = 0; return; }

            // einfache Begrenzung basierend auf verfügbarer Fläche
            double contentW = Root.Width;
            double contentH = Root.Height;

            // Img.Width/Height sind nach Layout gesetzt; Absicherung:
            if (Img.Width <= 0 || Img.Height <= 0 || contentW <= 0 || contentH <= 0) return;

            double scaledW = Img.Width * _currentScale;
            double scaledH = Img.Height * _currentScale;

            double maxX = Math.Max(0, (scaledW - contentW) / 2);
            double maxY = Math.Max(0, (scaledH - contentH) / 2);

            Img.TranslationX = Math.Clamp(Img.TranslationX, -maxX, maxX);
            Img.TranslationY = Math.Clamp(Img.TranslationY, -maxY, maxY);
        }
    }
}
