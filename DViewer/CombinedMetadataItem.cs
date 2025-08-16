using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.ApplicationModel;

namespace DViewer
{
    /// <summary>
    /// Ein reines View-Model für eine Zeile (Tag).
    /// Enthält KEINE Referenzen auf Left/Right-Quellobjekte.
    /// </summary>
    public sealed class CombinedMetadataItem : INotifyPropertyChanged
    {
        // Stammdaten
        public string TagId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Vr { get; init; } = string.Empty;

        // --- Werte (als DICOM-String) ---------------------------------------
        private string _leftValue = string.Empty;
        private string _rightValue = string.Empty;

        /// <summary>
        /// Beim initialen Befüllen/Neuaufbau keine Events schießen.
        /// </summary>
        private bool _suppressNotifications;

        /// <summary>
        /// Initiale/komplette Befüllung ohne irgendwelche Folgeeffekte.
        /// </summary>
        public void SetInitialValues(string? left, string? right)
        {
            _suppressNotifications = true;

            _leftValue = left ?? string.Empty;
            _rightValue = right ?? string.Empty;

            // Validierungsflags direkt berechnen (ohne Events)
            _isLeftInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, _leftValue);
            _isRightInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, _rightValue);

            _suppressNotifications = false;

            // ein einziges konsolidiertes Refresh für die UI
            SafeRaisePropertyChanged(nameof(LeftValue));
            SafeRaisePropertyChanged(nameof(RightValue));
            SafeRaisePropertyChanged(nameof(IsDifferent));
        }

        public string LeftValue
        {
            get => _leftValue;
            set
            {
                if (_leftValue == value) return;
                _leftValue = value ?? string.Empty;

                // Validierung
                IsLeftInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, _leftValue);

                // UI-Updates
                SafeRaisePropertyChanged(); // LeftValue
                SafeRaisePropertyChanged(nameof(IsDifferent));

                if (IsDate) SafeRaisePropertyChanged(nameof(LeftDate));
                if (IsTime) SafeRaisePropertyChanged(nameof(LeftTime));
                if (IsSex) SafeRaisePropertyChanged(nameof(LeftSex));
                if (IsPersonName)
                {
                    SafeRaisePropertyChanged(nameof(LeftPN_Family));
                    SafeRaisePropertyChanged(nameof(LeftPN_Given));
                    SafeRaisePropertyChanged(nameof(LeftPN_Middle));
                    SafeRaisePropertyChanged(nameof(LeftPN_Prefix));
                    SafeRaisePropertyChanged(nameof(LeftPN_Suffix));
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

                SafeRaisePropertyChanged(); // RightValue
                SafeRaisePropertyChanged(nameof(IsDifferent));

                if (IsDate) SafeRaisePropertyChanged(nameof(RightDate));
                if (IsTime) SafeRaisePropertyChanged(nameof(RightTime));
                if (IsSex) SafeRaisePropertyChanged(nameof(RightSex));
                if (IsPersonName)
                {
                    SafeRaisePropertyChanged(nameof(RightPN_Family));
                    SafeRaisePropertyChanged(nameof(RightPN_Given));
                    SafeRaisePropertyChanged(nameof(RightPN_Middle));
                    SafeRaisePropertyChanged(nameof(RightPN_Prefix));
                    SafeRaisePropertyChanged(nameof(RightPN_Suffix));
                }
            }
        }

        // UI-State / Markierungen
        private bool _isAlternate;
        public bool IsAlternate { get => _isAlternate; set { if (_isAlternate == value) return; _isAlternate = value; SafeRaisePropertyChanged(); } }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; SafeRaisePropertyChanged(); } }

        private bool _rowDiffHighlighted;
        public bool IsHighlighted { get => _rowDiffHighlighted; set { if (_rowDiffHighlighted == value) return; _rowDiffHighlighted = value; SafeRaisePropertyChanged(); } }

        // Validierung / Diff
        public bool IsDifferent => !string.Equals(_leftValue?.Trim(), _rightValue?.Trim(), StringComparison.OrdinalIgnoreCase);

        private bool _isLeftInvalid;
        public bool IsLeftInvalid { get => _isLeftInvalid; private set { if (_isLeftInvalid == value) return; _isLeftInvalid = value; SafeRaisePropertyChanged(); } }

        private bool _isRightInvalid;
        public bool IsRightInvalid { get => _isRightInvalid; private set { if (_isRightInvalid == value) return; _isRightInvalid = value; SafeRaisePropertyChanged(); } }

        private bool _leftInvalidHighlighted;
        public bool LeftInvalidHighlighted { get => _leftInvalidHighlighted; set { if (_leftInvalidHighlighted == value) return; _leftInvalidHighlighted = value; SafeRaisePropertyChanged(); } }

        private bool _rightInvalidHighlighted;
        public bool RightInvalidHighlighted { get => _rightInvalidHighlighted; set { if (_rightInvalidHighlighted == value) return; _rightInvalidHighlighted = value; SafeRaisePropertyChanged(); } }

        // Typflags
        public bool IsDate => string.Equals(Vr, "DA", StringComparison.OrdinalIgnoreCase);
        public bool IsTime => string.Equals(Vr, "TM", StringComparison.OrdinalIgnoreCase);
        public bool IsPersonName => string.Equals(Vr, "PN", StringComparison.OrdinalIgnoreCase);
        public bool IsSex => string.Equals(TagId, "(0010,0040)", StringComparison.OrdinalIgnoreCase);
        public bool IsNumeric => string.Equals(Vr, "IS", StringComparison.OrdinalIgnoreCase) || string.Equals(Vr, "DS", StringComparison.OrdinalIgnoreCase);
        public bool IsGeneric => !(IsDate || IsTime || IsPersonName || IsSex || IsNumeric);

        // DA/TM Wrapper (ohne zusätzliche Events in den Settern)
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
        public string LeftSex { get => (LeftValue ?? "").Trim().ToUpperInvariant(); set { var v = (value ?? "").Trim().ToUpperInvariant(); if (LeftValue != v) LeftValue = v; } }
        public string RightSex { get => (RightValue ?? "").Trim().ToUpperInvariant(); set { var v = (value ?? "").Trim().ToUpperInvariant(); if (RightValue != v) RightValue = v; } }

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

        private void SafeRaisePropertyChanged([CallerMemberName] string? name = null)
        {
            if (_suppressNotifications) return;
            if (string.IsNullOrEmpty(name)) return;

            if (MainThread.IsMainThread)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            else
                MainThread.BeginInvokeOnMainThread(() =>
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
        }
    }
}
