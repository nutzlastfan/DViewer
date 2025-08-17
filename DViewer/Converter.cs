using FellowOakDicom;
using FellowOakDicom.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.ComponentModel;
using System.Globalization;
using Image = SixLabors.ImageSharp.Image;

namespace DViewer
{

    public sealed class FormatDateDisplayConverter : IValueConverter
    {
        // Erwartet DateTime? und liefert "dd.MM.yyyy" oder ""
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is DateTime dt ? dt.ToString("dd.MM.yyyy") : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class FormatTimeDisplayConverter : IValueConverter
    {
        // Erwartet TimeSpan? und liefert "HH:mm:ss" oder ""
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is TimeSpan ts ? ts.ToString(@"hh\:mm\:ss") : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }





    public sealed class NullableDateConverter : IValueConverter
    {
        // DateTime?  -> DateTime (Picker braucht non-null)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is DateTime dt ? dt : DateTime.Today;
        }

        // DateTime   -> DateTime? (zurück ins ViewModel)
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is DateTime dt ? (DateTime?)dt : null;
        }
    }

    public sealed class NullableTimeConverter : IValueConverter
    {
        // TimeSpan? -> TimeSpan (Picker braucht non-null)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is TimeSpan ts ? ts : TimeSpan.Zero;
        }

        // TimeSpan  -> TimeSpan? (zurück ins ViewModel)
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is TimeSpan ts ? (TimeSpan?)ts : null;
        }
    }

    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class TagFilterOption : INotifyPropertyChanged
    {
        public string TagId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Display => $"({TagId}) {Name}";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public override string ToString() => Display;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // Converter star width
    public class DoubleToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is double d)
                return new GridLength(d, GridUnitType.Star);
            return new GridLength(1, GridUnitType.Star);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is GridLength gl && gl.IsStar)
                return gl.Value;
            return 0.0;
        }
    }

    // Alternating background converter
    public class AlternateBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isAlt && isAlt)
                return Microsoft.Maui.Graphics.Color.FromArgb("#DDDDDD"); // gut sichtbares Hellgrau
            return Colors.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

}
