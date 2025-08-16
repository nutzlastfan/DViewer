using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.ApplicationModel;

namespace DViewer
{
    public sealed class CombinedMetadataItem : INotifyPropertyChanged
    {
        // ---- Stammdaten -----------------------------------------------------
        public string TagId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Vr { get; init; } = string.Empty;   // DICOM VR

        // ---- Werte (DICOM-String) ------------------------------------------
        private string _leftValue = string.Empty;
        private string _rightValue = string.Empty;

        public string LeftValue
        {
            get => _leftValue;
            set
            {
                if (_leftValue == value) return;
                _leftValue = value ?? string.Empty;

                // Live-Validierung nur links
                IsLeftInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, _leftValue);

                // Nur gezielte Benachrichtigungen – kein „alles“-Notify
                SafeRaisePropertyChanged();                       // nameof(LeftValue)
                SafeRaisePropertyChanged(nameof(LeftText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
                SafeRaisePropertyChanged(nameof(IsLeftInvalid));

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

                SafeRaisePropertyChanged();                       // nameof(RightValue)
                SafeRaisePropertyChanged(nameof(RightText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
                SafeRaisePropertyChanged(nameof(IsRightInvalid));

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

        // Für Label-Bindings im Grid (keine Editoren)
        public string LeftText => LeftValue;
        public string RightText => RightValue;

        // ---- Vergleich / Validierung ---------------------------------------
        public bool IsDifferent =>
            !string.Equals(_leftValue?.Trim(), _rightValue?.Trim(), StringComparison.OrdinalIgnoreCase);

        private bool _isLeftInvalid;
        public bool IsLeftInvalid
        {
            get => _isLeftInvalid;
            private set { if (_isLeftInvalid == value) return; _isLeftInvalid = value; SafeRaisePropertyChanged(); }
        }

        private bool _isRightInvalid;
        public bool IsRightInvalid
        {
            get => _isRightInvalid;
            private set { if (_isRightInvalid == value) return; _isRightInvalid = value; SafeRaisePropertyChanged(); }
        }

        // ---- UI-Flags -------------------------------------------------------
        private bool _isAlternate;
        public bool IsAlternate
        {
            get => _isAlternate;
            set { if (_isAlternate == value) return; _isAlternate = value; SafeRaisePropertyChanged(); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected == value) return; _isSelected = value; SafeRaisePropertyChanged(); }
        }

        private bool _rowDiffHighlighted;
        public bool IsHighlighted
        {
            get => _rowDiffHighlighted;
            set { if (_rowDiffHighlighted == value) return; _rowDiffHighlighted = value; SafeRaisePropertyChanged(); }
        }

        private bool _leftInvalidHighlighted;
        public bool LeftInvalidHighlighted
        {
            get => _leftInvalidHighlighted;
            set { if (_leftInvalidHighlighted == value) return; _leftInvalidHighlighted = value; SafeRaisePropertyChanged(); }
        }

        private bool _rightInvalidHighlighted;
        public bool RightInvalidHighlighted
        {
            get => _rightInvalidHighlighted;
            set { if (_rightInvalidHighlighted == value) return; _rightInvalidHighlighted = value; SafeRaisePropertyChanged(); }
        }

        // ---- Typflags (steuern Editorwahl in XAML) --------------------------
        public bool IsDate => string.Equals(Vr, "DA", StringComparison.OrdinalIgnoreCase);
        public bool IsTime => string.Equals(Vr, "TM", StringComparison.OrdinalIgnoreCase);
        public bool IsPersonName => string.Equals(Vr, "PN", StringComparison.OrdinalIgnoreCase);
        public bool IsSex => string.Equals(TagId, "(0010,0040)", StringComparison.OrdinalIgnoreCase);
        public bool IsNumeric => string.Equals(Vr, "IS", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(Vr, "DS", StringComparison.OrdinalIgnoreCase);
        public bool IsGeneric => !(IsDate || IsTime || IsPersonName || IsSex || IsNumeric);

        // ---- DA (Date) als nullable Wrapper --------------------------------
        public DateTime? LeftDate
        {
            get => HelperFunctions.DicomFormat.ParseDA(LeftValue);
            set
            {
                var s = value.HasValue ? HelperFunctions.DicomFormat.FormatDA(value.Value) : string.Empty;
                if (LeftValue != s) LeftValue = s;
                // gezielt nachziehen
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(LeftText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        public DateTime? RightDate
        {
            get => HelperFunctions.DicomFormat.ParseDA(RightValue);
            set
            {
                var s = value.HasValue ? HelperFunctions.DicomFormat.FormatDA(value.Value) : string.Empty;
                if (RightValue != s) RightValue = s;
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(RightText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        // ---- TM (Time) als nullable Wrapper --------------------------------
        public TimeSpan? LeftTime
        {
            get => HelperFunctions.DicomFormat.ParseTM(LeftValue);
            set
            {
                var s = value.HasValue ? HelperFunctions.DicomFormat.FormatTM(value.Value) : string.Empty;
                if (LeftValue != s) LeftValue = s;
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(LeftText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        public TimeSpan? RightTime
        {
            get => HelperFunctions.DicomFormat.ParseTM(RightValue);
            set
            {
                var s = value.HasValue ? HelperFunctions.DicomFormat.FormatTM(value.Value) : string.Empty;
                if (RightValue != s) RightValue = s;
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(RightText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        // ---- Sex (0010,0040): M/F/N/O/U ------------------------------------
        public string LeftSex
        {
            get => (LeftValue ?? string.Empty).Trim().ToUpperInvariant();
            set
            {
                var v = (value ?? string.Empty).Trim().ToUpperInvariant();
                if (LeftValue != v) LeftValue = v;
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(LeftText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        public string RightSex
        {
            get => (RightValue ?? string.Empty).Trim().ToUpperInvariant();
            set
            {
                var v = (value ?? string.Empty).Trim().ToUpperInvariant();
                if (RightValue != v) RightValue = v;
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(RightText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        // ---- PN (Person Name) – Alphabetic-Komponente ----------------------
        public string LeftPN_Family
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Family;
            set
            {
                var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue);
                p.Family = value ?? string.Empty;
                LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p);
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(LeftText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        public string LeftPN_Given
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Given;
            set
            {
                var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue);
                p.Given = value ?? string.Empty;
                LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p);
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(LeftText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        public string LeftPN_Middle
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Middle;
            set
            {
                var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue);
                p.Middle = value ?? string.Empty;
                LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p);
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(LeftText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        public string LeftPN_Prefix
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Prefix;
            set
            {
                var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue);
                p.Prefix = value ?? string.Empty;
                LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p);
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(LeftText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        public string LeftPN_Suffix
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Suffix;
            set
            {
                var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue);
                p.Suffix = value ?? string.Empty;
                LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p);
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(LeftText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        public string RightPN_Family
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Family;
            set
            {
                var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue);
                p.Family = value ?? string.Empty;
                RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p);
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(RightText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        public string RightPN_Given
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Given;
            set
            {
                var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue);
                p.Given = value ?? string.Empty;
                RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p);
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(RightText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        public string RightPN_Middle
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Middle;
            set
            {
                var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue);
                p.Middle = value ?? string.Empty;
                RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p);
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(RightText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        public string RightPN_Prefix
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Prefix;
            set
            {
                var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue);
                p.Prefix = value ?? string.Empty;
                RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p);
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(RightText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        public string RightPN_Suffix
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Suffix;
            set
            {
                var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue);
                p.Suffix = value ?? string.Empty;
                RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p);
                SafeRaisePropertyChanged();
                SafeRaisePropertyChanged(nameof(RightText));
                SafeRaisePropertyChanged(nameof(IsDifferent));
            }
        }

        // ---- INotifyPropertyChanged ----------------------------------------
        public event PropertyChangedEventHandler? PropertyChanged;

        private void SafeRaisePropertyChanged([CallerMemberName] string? name = null)
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
