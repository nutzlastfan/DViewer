using System;
using System.ComponentModel;
using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace DViewer
{
    // ====== DICOM Date (DA) <-> DateTime ======
    public sealed class NullableDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var s = value as string ?? string.Empty;
                var dt = HelperFunctions.DicomFormat.ParseDA(s);
                return dt ?? DateTime.Today; // DatePicker braucht einen Wert
            }
            catch { return DateTime.Today; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is DateTime dt)
                    return HelperFunctions.DicomFormat.FormatDA(dt);
            }
            catch { }
            return Binding.DoNothing;
        }
    }

    // ====== DICOM Time (TM) <-> TimeSpan ======
    public sealed class NullableTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var s = value as string ?? string.Empty;
                var ts = HelperFunctions.DicomFormat.ParseTM(s);
                return ts ?? TimeSpan.Zero; // TimePicker braucht einen Wert
            }
            catch { return TimeSpan.Zero; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is TimeSpan ts)
                    return HelperFunctions.DicomFormat.FormatTM(ts);
            }
            catch { }
            return Binding.DoNothing;
        }
    }

    // ====== Nur zurückschreiben, wenn Entry fokussiert ist ======
    public sealed class OnlyWhenFocusedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.ToString() ?? string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isFocused = parameter is bool b && b;
            if (!isFocused) return Binding.DoNothing;
            return value?.ToString() ?? string.Empty;
        }
    }

    // ====== GridLength (Star) aus double/int ======
    // NICHT sealed, damit StarConverter davon erben kann
    public class DoubleToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is double d && d > 0) return new GridLength(d, GridUnitType.Star);
                if (value is int i && i > 0) return new GridLength(i, GridUnitType.Star);
            }
            catch { }
            return new GridLength(1, GridUnitType.Star);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // Alias (falls XAML an manchen Stellen StarConverter erwartet)
    public class StarConverter : DoubleToGridLengthConverter { }

    // ====== Zebra-Hintergrund ======
    public sealed class AlternateBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isAlt && isAlt) return Color.FromArgb("#F2F2F2");
            return Colors.White;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // ====== Anzeige-Formatter (read-only) ======
    public sealed class FormatDateDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var s = value as string ?? string.Empty;
                var dt = HelperFunctions.DicomFormat.ParseDA(s);
                return dt.HasValue ? dt.Value.ToString("dd.MM.yyyy", culture) : string.Empty;
            }
            catch { return string.Empty; }
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// Liefert true, wenn value == null oder leere Zeichenkette; sonst false.
    /// Optionaler Parameter "Invert": wenn "true", wird invertiert.
    /// </summary>
    public sealed class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNullOrEmpty = value is null || (value is string s && string.IsNullOrEmpty(s));

            // Parameter "Invert" erlaubt Invertierung (z.B. ConverterParameter="true")
            bool invert = false;
            if (parameter is bool b) invert = b;
            else if (parameter is string str && bool.TryParse(str, out var p)) invert = p;

            return invert ? !isNullOrEmpty : isNullOrEmpty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Komfort-Variante: true, wenn NICHT null/leer.
    /// </summary>
    public sealed class NotNullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => !(value is null || (value is string s && string.IsNullOrEmpty(s)));

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class FormatTimeDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var s = value as string ?? string.Empty;
                var ts = HelperFunctions.DicomFormat.ParseTM(s);
                return ts.HasValue ? ts.Value.ToString(@"HH\:mm\:ss", culture) : string.Empty;
            }
            catch { return string.Empty; }
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    // (Optional) POCO für Tag-Filter in XAML
    public sealed class TagFilterOption : INotifyPropertyChanged
    {
        public string TagId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Display => $"({TagId}) {Name}";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
