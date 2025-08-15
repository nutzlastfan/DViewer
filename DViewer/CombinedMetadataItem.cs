using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.ApplicationModel; // MainThread

namespace DViewer
{
    public class CombinedMetadataItem : INotifyPropertyChanged
    {
        // Stammdaten
        public string TagId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Vr { get; init; } = string.Empty;

        // Werte (DICOM-Strings)
        private string _leftValue = string.Empty;
        private string _rightValue = string.Empty;

        public string LeftValue
        {
            get => _leftValue;
            set
            {
                if (_leftValue == value) return;
                _leftValue = value ?? string.Empty;

                // Live-Validierung
                IsLeftInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, _leftValue);

                // UI-Updates
                OnPropertyChanged(); // nameof(LeftValue)
                OnPropertyChanged(nameof(IsDifferent));
                if (IsDate) OnPropertyChanged(nameof(LeftDate));
                if (IsTime) OnPropertyChanged(nameof(LeftTime));
                if (IsSex) OnPropertyChanged(nameof(LeftSex));
                if (IsPersonName)
                {
                    OnPropertyChanged(nameof(LeftPN_Family));
                    OnPropertyChanged(nameof(LeftPN_Given));
                    OnPropertyChanged(nameof(LeftPN_Middle));
                    OnPropertyChanged(nameof(LeftPN_Prefix));
                    OnPropertyChanged(nameof(LeftPN_Suffix));
                }
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

                OnPropertyChanged(); // nameof(RightValue)
                OnPropertyChanged(nameof(IsDifferent));
                if (IsDate) OnPropertyChanged(nameof(RightDate));
                if (IsTime) OnPropertyChanged(nameof(RightTime));
                if (IsSex) OnPropertyChanged(nameof(RightSex));
                if (IsPersonName)
                {
                    OnPropertyChanged(nameof(RightPN_Family));
                    OnPropertyChanged(nameof(RightPN_Given));
                    OnPropertyChanged(nameof(RightPN_Middle));
                    OnPropertyChanged(nameof(RightPN_Prefix));
                    OnPropertyChanged(nameof(RightPN_Suffix));
                }
            }
        }

        // UI-State
        private bool _isAlternate;
        public bool IsAlternate { get => _isAlternate; set { if (_isAlternate == value) return; _isAlternate = value; OnPropertyChanged(); } }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); } }

        private bool _rowDiffHighlighted;
        public bool IsHighlighted { get => _rowDiffHighlighted; set { if (_rowDiffHighlighted == value) return; _rowDiffHighlighted = value; OnPropertyChanged(); } }

        // Validierung / Highlights
        public bool IsDifferent => !string.Equals(_leftValue?.Trim(), _rightValue?.Trim(), StringComparison.OrdinalIgnoreCase);

        private bool _isLeftInvalid;
        public bool IsLeftInvalid { get => _isLeftInvalid; private set { if (_isLeftInvalid == value) return; _isLeftInvalid = value; OnPropertyChanged(); } }

        private bool _isRightInvalid;
        public bool IsRightInvalid { get => _isRightInvalid; private set { if (_isRightInvalid == value) return; _isRightInvalid = value; OnPropertyChanged(); } }

        private bool _leftInvalidHighlighted;
        public bool LeftInvalidHighlighted { get => _leftInvalidHighlighted; set { if (_leftInvalidHighlighted == value) return; _leftInvalidHighlighted = value; OnPropertyChanged(); } }

        private bool _rightInvalidHighlighted;
        public bool RightInvalidHighlighted { get => _rightInvalidHighlighted; set { if (_rightInvalidHighlighted == value) return; _rightInvalidHighlighted = value; OnPropertyChanged(); } }

        // Typflags (für Editorwahl)
        public bool IsDate => string.Equals(Vr, "DA", StringComparison.OrdinalIgnoreCase);
        public bool IsTime => string.Equals(Vr, "TM", StringComparison.OrdinalIgnoreCase);
        public bool IsPersonName => string.Equals(Vr, "PN", StringComparison.OrdinalIgnoreCase);
        public bool IsSex => string.Equals(TagId, "(0010,0040)", StringComparison.OrdinalIgnoreCase);
        public bool IsNumeric => string.Equals(Vr, "IS", StringComparison.OrdinalIgnoreCase) || string.Equals(Vr, "DS", StringComparison.OrdinalIgnoreCase);
        public bool IsGeneric => !(IsDate || IsTime || IsPersonName || IsSex || IsNumeric);

        // DA (Date)
        public DateTime? LeftDate
        {
            get => HelperFunctions.DicomFormat.ParseDA(LeftValue);
            set { var s = value.HasValue ? HelperFunctions.DicomFormat.FormatDA(value.Value) : string.Empty; if (LeftValue != s) LeftValue = s; }
        }
        public DateTime? RightDate
        {
            get => HelperFunctions.DicomFormat.ParseDA(RightValue);
            set { var s = value.HasValue ? HelperFunctions.DicomFormat.FormatDA(value.Value) : string.Empty; if (RightValue != s) RightValue = s; }
        }

        // TM (Time)
        public TimeSpan? LeftTime
        {
            get => HelperFunctions.DicomFormat.ParseTM(LeftValue);
            set { var s = value.HasValue ? HelperFunctions.DicomFormat.FormatTM(value.Value) : string.Empty; if (LeftValue != s) LeftValue = s; }
        }
        public TimeSpan? RightTime
        {
            get => HelperFunctions.DicomFormat.ParseTM(RightValue);
            set { var s = value.HasValue ? HelperFunctions.DicomFormat.FormatTM(value.Value) : string.Empty; if (RightValue != s) RightValue = s; }
        }

        // Sex
        public string LeftSex
        {
            get => (LeftValue ?? string.Empty).Trim().ToUpperInvariant();
            set { var v = (value ?? string.Empty).Trim().ToUpperInvariant(); if (LeftValue != v) LeftValue = v; }
        }
        public string RightSex
        {
            get => (RightValue ?? string.Empty).Trim().ToUpperInvariant();
            set { var v = (value ?? string.Empty).Trim().ToUpperInvariant(); if (RightValue != v) RightValue = v; }
        }

        // PN (Alphabetic)
        public string LeftPN_Family { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Family; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Family = value ?? ""; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string LeftPN_Given { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Given; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Given = value ?? ""; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string LeftPN_Middle { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Middle; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Middle = value ?? ""; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string LeftPN_Prefix { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Prefix; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Prefix = value ?? ""; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string LeftPN_Suffix { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Suffix; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Suffix = value ?? ""; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }

        public string RightPN_Family { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Family; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Family = value ?? ""; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string RightPN_Given { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Given; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Given = value ?? ""; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string RightPN_Middle { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Middle; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Middle = value ?? ""; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string RightPN_Prefix { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Prefix; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Prefix = value ?? ""; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }
        public string RightPN_Suffix { get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Suffix; set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Suffix = value ?? ""; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); } }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (MainThread.IsMainThread)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            else
                MainThread.BeginInvokeOnMainThread(() =>
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
        }
    }
}
