using System;
using System.ComponentModel;
using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

using DViewer.Infrastructure;

namespace DViewer
{
    /// <summary>
    /// DatePicker & DICOM Datum (string "yyyyMMdd" & friends) <-> DateTime?
    /// </summary>
    public sealed class NullableDateConverter : IValueConverter
    {
        private static readonly string[] s_formats = new[]
        {
            "yyyyMMdd","yyyy-MM-dd","dd.MM.yyyy","MM/dd/yyyy","dd/MM/yyyy"
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var s = value as string;
                if (string.IsNullOrWhiteSpace(s)) return null;

                if (DateTime.TryParseExact(s, s_formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    return dt;

                if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
                    return dt;
            }
            catch { }
            return null; // DatePicker zeigt dann Placeholder
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is DateTime dt)
                    return dt.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            }
            catch { }
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// TimePicker & DICOM Zeit (string "HHmmss"/"HH:mm:ss") <-> TimeSpan?
    /// </summary>
    public sealed class NullableTimeConverter : IValueConverter
    {
        private static readonly string[] s_formats = new[]
        {
            "HHmmss","HHmm","HH:mm:ss","HH:mm"
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var s = value as string;
                if (string.IsNullOrWhiteSpace(s)) return null;

                foreach (var f in s_formats)
                {
                    if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        return dt.TimeOfDay;
                }

                if (TimeSpan.TryParse(s, out var ts))
                    return ts;
            }
            catch { }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is TimeSpan ts)
                {
                    // DICOM-like HHmmss (ohne Trennzeichen)
                    var hh = ts.Hours.ToString("00");
                    var mm = ts.Minutes.ToString("00");
                    var ss = ts.Seconds.ToString("00");
                    return $"{hh}{mm}{ss}";
                }
            }
            catch { }
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// Nur zurückschreiben, wenn Entry gerade fokussiert ist.
    /// Convert: immer Durchreichen (damit Anzeige stimmt).
    /// ConvertBack: nur wenn parameter==true, sonst DoNothing.
    /// </summary>
    public sealed class OnlyWhenFocusedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {

            //Console.WriteLine(value);
            // WICHTIG: Im Hinweg NIE DoNothing/UnsetValue zurückgeben, sondern immer String
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Nur zurückschreiben, wenn das Entry fokussiert ist
            bool isFocused = parameter switch
            {
                bool b => b,
                string s => bool.TryParse(s, out var b) && b,
                _ => false
            };

            return value?.ToString() ?? string.Empty;
        }
    }




    /// <summary>
    /// Wandelt bool -> "▶" (Play) / "⏸" (Pause) um.
    /// </summary>
    public sealed class PlayPauseTextConverter : IValueConverter
    {
        public string PlayText { get; set; } = "▶";
        public string PauseText { get; set; } = "⏸";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isPlaying = value is bool b ? b
                : (value is string s && bool.TryParse(s, out var parsed) && parsed);

            return isPlaying ? PauseText : PlayText;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
            {
                if (s == PauseText) return true;
                if (s == PlayText) return false;
            }
            return Binding.DoNothing;
        }
    }


    /// <summary>
    /// Zieht 1 vom eingegebenen Zahlenwert ab.
    /// Nicht-positive, null- oder ungültige Werte -> 0.
    /// Gibt standardmäßig double zurück (praktisch für Slider.*).
    /// </summary>
    public sealed class MinusOneConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // robust gegen null / Strings / verschiedene Zahlentypen
            if (value == null) return 0d;

            try
            {
                double n = value switch
                {
                    sbyte v => v,
                    byte v => v,
                    short v => v,
                    ushort v => v,
                    int v => v,
                    uint v => v,
                    long v => v,
                    ulong v => v,
                    float v => v,
                    double v => v,
                    decimal v => (double)v,
                    string s => double.TryParse(s, NumberStyles.Any, culture, out var d) ? d : 0d,
                    _ => 0d
                };

                var result = n - 1d;
                if (result < 0d) result = 0d;

                // Slider.Maximum/Minimum erwarten double -> wir geben double zurück
                return result;
            }
            catch
            {
                return 0d;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Normalerweise nicht gebraucht (OneWay-Binding).
            // Zur Vollständigkeit: +1 und in Zieltyp zurückkonvertieren.
            double d = value is IConvertible ? System.Convert.ToDouble(value, culture) : 0d;
            d = d + 1d;

            if (targetType == typeof(int) || targetType == typeof(int?)) return (int)Math.Round(d);
            if (targetType == typeof(double) || targetType == typeof(double?)) return d;
            if (targetType == typeof(float) || targetType == typeof(float?)) return (float)d;
            if (targetType == typeof(long) || targetType == typeof(long?)) return (long)Math.Round(d);

            return d; // Fallback
        }
    }



    /// <summary>
    /// Kehrt boolsche Werte um (true -> false, false/null/kein-bool -> true).
    /// Null und Nicht-Bool behandeln wir als false, damit die Inversion true ergibt
    /// und keine Binding-Warnungen auftreten.
    /// </summary>
    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var b = (value as bool?) ?? false; // null => false
            return !b;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var b = (value as bool?) ?? false;
            return !b;
        }
    }


    // ====== Zebra-Hintergrund ======
    /// <summary> Abwechselnde Hintergrundfarbe anhand bool </summary>
    public sealed class AlternateBackgroundConverter : IValueConverter
    {
        public Color EvenColor { get; set; } = Color.FromArgb("#00000000"); // transparent
        public Color OddColor { get; set; } = Color.FromArgb("#0A000000"); // leicht abgesetzt

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is bool isAlt && isAlt) return OddColor;
            }
            catch { }
            return EvenColor;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
    // ====== DICOM Date (DA) -> "dd.MM.yyyy" (nur Anzeige) ======
    /// <summary> Anzeigeformat für DICOM Datum (string -> "dd.MM.yyyy") </summary>
    public sealed class FormatDateDisplayConverter : IValueConverter
    {
        private static readonly string[] s_formats = new[]
        {
            "yyyyMMdd","yyyy-MM-dd","dd.MM.yyyy","MM/dd/yyyy","dd/MM/yyyy"
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var s = value as string;
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;

                if (DateTime.TryParseExact(s, s_formats, CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out var dt))
                    return dt.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);

                if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
                    return dt.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
            }
            catch { }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    public sealed class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNullOrEmpty = value is null || (value is string s && string.IsNullOrEmpty(s));
            bool invert = false;
            if (parameter is bool b) invert = b;
            else if (parameter is string str && bool.TryParse(str, out var p)) invert = p;

            return invert ? !isNullOrEmpty : isNullOrEmpty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class NotNullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool notNullOrNotEmpty = !(value is null || (value is string s && string.IsNullOrEmpty(s)));
            bool invert = false;
            if (parameter is bool b) invert = b;
            else if (parameter is string str && bool.TryParse(str, out var p)) invert = p;

            return invert ? !notNullOrNotEmpty : notNullOrNotEmpty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary> Anzeigeformat für DICOM Zeit (string -> "HH:mm:ss") </summary>
    public sealed class FormatTimeDisplayConverter : IValueConverter
    {
        private static readonly string[] s_formats = new[]
        {
            "HHmmss","HHmm","HH:mm:ss","HH:mm"
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var s = value as string;
                if (string.IsNullOrWhiteSpace(s)) return string.Empty;

                foreach (var f in s_formats)
                {
                    if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        return dt.ToString(f.Contains("s") ? "HH:mm:ss" : "HH:mm", CultureInfo.CurrentCulture);
                }

                if (TimeSpan.TryParse(s, out var ts))
                    return ts.ToString(ts.Seconds > 0 ? @"hh\:mm\:ss" : @"hh\:mm");
            }
            catch { }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }


    // ====== GridLength (Star) aus double/int ======
    // NICHT sealed, damit StarConverter davon erben kann
    /// <summary>
    /// Konvertiert double -> GridLength.Star (z.B. 2 -> "2*")
    /// </summary>
    public  class DoubleToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is double d)
                    return new GridLength(d, GridUnitType.Star);
                if (value is int i)
                    return new GridLength(i, GridUnitType.Star);
                if (value is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                    return new GridLength(dv, GridUnitType.Star);
            }
            catch { }
            return new GridLength(1, GridUnitType.Star);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    // Alias (falls XAML an manchen Stellen StarConverter erwartet)
    public class StarConverter : DoubleToGridLengthConverter { }


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
