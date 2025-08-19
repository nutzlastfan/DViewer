using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace DViewer
{
    public sealed class CombinedMetadataItem : INotifyPropertyChanged
    {
        public string TagId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Vr { get; set; } = string.Empty;

        private object? _leftValue;
        public object? LeftValue
        {
            get => _leftValue;
            set
            {
                if (Equals(_leftValue, value)) return;
                _leftValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LeftText));
                OnPropertyChanged(nameof(IsDifferent));
            }
        }

        private object? _rightValue;
        public object? RightValue
        {
            get => _rightValue;
            set
            {
                if (Equals(_rightValue, value)) return;
                _rightValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RightText));
                OnPropertyChanged(nameof(IsDifferent));
            }
        }

        // --- String-Wrapper für die UI (Entry.Text bindet hieran) ---
        public string LeftText
        {
            get => ValueToString(LeftValue, Vr);
            set
            {
                var newObj = StringToValue(value, Vr);
                if (Equals(_leftValue, newObj)) return;
                _leftValue = newObj;
                OnPropertyChanged(nameof(LeftValue));
                OnPropertyChanged(); // LeftText
                OnPropertyChanged(nameof(IsDifferent));
                UpdateInvalidFlags();
            }
        }

        public string RightText
        {
            get => ValueToString(RightValue, Vr);
            set
            {
                var newObj = StringToValue(value, Vr);
                if (Equals(_rightValue, newObj)) return;
                _rightValue = newObj;
                OnPropertyChanged(nameof(RightValue));
                OnPropertyChanged(); // RightText
                OnPropertyChanged(nameof(IsDifferent));
                UpdateInvalidFlags();
            }
        }

        public bool IsDifferent =>
            !string.Equals(ValueToString(LeftValue, Vr), ValueToString(RightValue, Vr), StringComparison.Ordinal);

        // --- Flags, die dein ViewModel verwendet ---
        private bool _isLeftInvalid;
        public bool IsLeftInvalid
        {
            get => _isLeftInvalid;
            set { if (_isLeftInvalid == value) return; _isLeftInvalid = value; OnPropertyChanged(); }
        }

        private bool _isRightInvalid;
        public bool IsRightInvalid
        {
            get => _isRightInvalid;
            set { if (_isRightInvalid == value) return; _isRightInvalid = value; OnPropertyChanged(); }
        }

        private bool _isAlternate;
        public bool IsAlternate
        {
            get => _isAlternate;
            set { if (_isAlternate == value) return; _isAlternate = value; OnPropertyChanged(); }
        }

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set { if (_isHighlighted == value) return; _isHighlighted = value; OnPropertyChanged(); }
        }

        private bool _leftInvalidHighlighted;
        public bool LeftInvalidHighlighted
        {
            get => _leftInvalidHighlighted;
            set { if (_leftInvalidHighlighted == value) return; _leftInvalidHighlighted = value; OnPropertyChanged(); }
        }

        private bool _rightInvalidHighlighted;
        public bool RightInvalidHighlighted
        {
            get => _rightInvalidHighlighted;
            set { if (_rightInvalidHighlighted == value) return; _rightInvalidHighlighted = value; OnPropertyChanged(); }
        }

        // --- Hilfen ---
        private static string ValueToString(object? v, string vr)
        {
            if (v is null) return string.Empty;
            if (v is string s) return s;

            // Nur sehr defensiv formatieren; wir belassen i.d.R. Strings
            return v switch
            {
                DateTime dt when vr == "DA" => dt.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                TimeSpan ts when vr == "TM" => $"{ts.Hours:00}{ts.Minutes:00}{ts.Seconds:00}",
                _ => v.ToString() ?? string.Empty
            };
        }

        private static object StringToValue(string? s, string vr)
        {
            // WICHTIG: Wir geben standardmäßig den String zurück,
            // damit dein Dataset unverändert als String arbeiten kann.
            // (Wenn du später strikt typisieren willst, kannst du hier je VR parsen.)
            s ??= string.Empty;
            return s;
        }

        private void UpdateInvalidFlags()
        {
            // Platzhalter: hier ggf. Validierung je VR einhängen
            // Aktuell keine automatische Markierung
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
