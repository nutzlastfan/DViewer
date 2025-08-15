using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.ApplicationModel;

namespace DViewer
{
    /// <summary>
    /// Eine Vergleichszeile. Links und Rechts sind unabhängig;
    /// es gibt nur minimale PropertyChanged-Signale (Wert + IsDifferent).
    /// </summary>
    public class CombinedMetadataItem : INotifyPropertyChanged
    {
        public string TagId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Vr { get; init; } = string.Empty;

        string _leftValue = string.Empty;
        string _rightValue = string.Empty;

        public string LeftValue
        {
            get => _leftValue;
            set
            {
                if (_leftValue == value) return;
                _leftValue = value ?? string.Empty;

                IsLeftInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, _leftValue);

                SafeRaisePropertyChanged();                    // LeftValue
                SafeRaisePropertyChanged(nameof(IsDifferent)); // hängt von beiden Seiten ab
            }
        }

        public string RightValue
        {
            get => _rightValue;
            set
            {
                if (_rightValue == value) return;
                _rightValue = value ?? string.Empty;

                IsRightInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, _rightValue);

                SafeRaisePropertyChanged();                    // RightValue
                SafeRaisePropertyChanged(nameof(IsDifferent)); // hängt von beiden Seiten ab
            }
        }

        // UI-State
        bool _isAlternate;
        public bool IsAlternate { get => _isAlternate; set { if (_isAlternate == value) return; _isAlternate = value; SafeRaisePropertyChanged(); } }

        bool _isSelected;
        public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; SafeRaisePropertyChanged(); } }

        bool _rowDiffHighlighted;
        public bool IsHighlighted { get => _rowDiffHighlighted; set { if (_rowDiffHighlighted == value) return; _rowDiffHighlighted = value; SafeRaisePropertyChanged(); } }

        // Validierung/Highlights
        bool _isLeftInvalid;
        public bool IsLeftInvalid { get => _isLeftInvalid; private set { if (_isLeftInvalid == value) return; _isLeftInvalid = value; SafeRaisePropertyChanged(); } }

        bool _isRightInvalid;
        public bool IsRightInvalid { get => _isRightInvalid; private set { if (_isRightInvalid == value) return; _isRightInvalid = value; SafeRaisePropertyChanged(); } }

        bool _leftInvalidHighlighted;
        public bool LeftInvalidHighlighted { get => _leftInvalidHighlighted; set { if (_leftInvalidHighlighted == value) return; _leftInvalidHighlighted = value; SafeRaisePropertyChanged(); } }

        bool _rightInvalidHighlighted;
        public bool RightInvalidHighlighted { get => _rightInvalidHighlighted; set { if (_rightInvalidHighlighted == value) return; _rightInvalidHighlighted = value; SafeRaisePropertyChanged(); } }

        public bool IsDifferent =>
            !string.Equals(_leftValue?.Trim(), _rightValue?.Trim(), StringComparison.OrdinalIgnoreCase);

        // Typflags für Editorwahl
        public bool IsDate => string.Equals(Vr, "DA", StringComparison.OrdinalIgnoreCase);
        public bool IsTime => string.Equals(Vr, "TM", StringComparison.OrdinalIgnoreCase);
        public bool IsPersonName => string.Equals(Vr, "PN", StringComparison.OrdinalIgnoreCase);
        public bool IsSex => string.Equals(TagId, "(0010,0040)", StringComparison.OrdinalIgnoreCase);
        public bool IsNumeric => string.Equals(Vr, "IS", StringComparison.OrdinalIgnoreCase) || string.Equals(Vr, "DS", StringComparison.OrdinalIgnoreCase);
        public bool IsGeneric => !(IsDate || IsTime || IsPersonName || IsSex || IsNumeric);

        // DA/TM
        public DateTime? LeftDate { get => HelperFunctions.DicomFormat.ParseDA(LeftValue); set { var s = value.HasValue ? HelperFunctions.DicomFormat.FormatDA(value.Value) : string.Empty; if (LeftValue != s) LeftValue = s; } }
        public DateTime? RightDate { get => HelperFunctions.DicomFormat.ParseDA(RightValue); set { var s = value.HasValue ? HelperFunctions.DicomFormat.FormatDA(value.Value) : string.Empty; if (RightValue != s) RightValue = s; } }

        public TimeSpan? LeftTime { get => HelperFunctions.DicomFormat.ParseTM(LeftValue); set { var s = value.HasValue ? HelperFunctions.DicomFormat.FormatTM(value.Value) : string.Empty; if (LeftValue != s) LeftValue = s; } }
        public TimeSpan? RightTime { get => HelperFunctions.DicomFormat.ParseTM(RightValue); set { var s = value.HasValue ? HelperFunctions.DicomFormat.FormatTM(value.Value) : string.Empty; if (RightValue != s) RightValue = s; } }

        // Sex (0010,0040)
        public string LeftSex { get => (LeftValue ?? string.Empty).Trim().ToUpperInvariant(); set { var v = (value ?? string.Empty).Trim().ToUpperInvariant(); if (LeftValue != v) LeftValue = v; } }
        public string RightSex { get => (RightValue ?? string.Empty).Trim().ToUpperInvariant(); set { var v = (value ?? string.Empty).Trim().ToUpperInvariant(); if (RightValue != v) RightValue = v; } }

        // PN (Alphabetic)
        public string LeftPN_Family { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Family; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Family = value ?? string.Empty; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string LeftPN_Given { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Given; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Given = value ?? string.Empty; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string LeftPN_Middle { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Middle; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Middle = value ?? string.Empty; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string LeftPN_Prefix { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Prefix; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Prefix = value ?? string.Empty; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string LeftPN_Suffix { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Suffix; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Suffix = value ?? string.Empty; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }

        public string RightPN_Family { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Family; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Family = value ?? string.Empty; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string RightPN_Given { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Given; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Given = value ?? string.Empty; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string RightPN_Middle { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Middle; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Middle = value ?? string.Empty; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string RightPN_Prefix { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Prefix; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Prefix = value ?? string.Empty; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string RightPN_Suffix { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Suffix; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Suffix = value ?? string.Empty; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        void SafeRaisePropertyChanged([CallerMemberName] string? n = null)
        {
            if (string.IsNullOrEmpty(n)) return;
            if (MainThread.IsMainThread)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
            else
                MainThread.BeginInvokeOnMainThread(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)));
        }
    }
}
