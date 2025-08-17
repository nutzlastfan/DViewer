using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.ApplicationModel; // MainThread

namespace DViewer
{
    public sealed class CombinedMetadataItem : INotifyPropertyChanged
    {
        // --- Stammdaten ------------------------------------------------------
        public string TagId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Vr { get; init; } = string.Empty;   // DICOM VR

        // --- Werte (als DICOM-String) ---------------------------------------
        private string _leftValue = string.Empty;
        private string _rightValue = string.Empty;

        public string LeftValue
        {
            get => _leftValue;
            set
            {
                var val = value ?? string.Empty;
                if (_leftValue == val) return;
                _leftValue = val;

                // Validierung
                IsLeftInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, _leftValue);

                // Abhängigkeiten (koalesziert melden)
                SafeRaise(nameof(LeftValue));
                SafeRaise(nameof(LeftText));
                SafeRaise(nameof(IsDifferent));

                if (IsDate) SafeRaise(nameof(LeftDate));
                if (IsTime) SafeRaise(nameof(LeftTime));
                if (IsSex) SafeRaise(nameof(LeftSex));
                if (IsPersonName)
                {
                    SafeRaise(nameof(LeftPN_Family));
                    SafeRaise(nameof(LeftPN_Given));
                    SafeRaise(nameof(LeftPN_Middle));
                    SafeRaise(nameof(LeftPN_Prefix));
                    SafeRaise(nameof(LeftPN_Suffix));
                }
            }
        }

        public string RightValue
        {
            get => _rightValue;
            set
            {
                var val = value ?? string.Empty;
                if (_rightValue == val) return;
                _rightValue = val;

                IsRightInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, _rightValue);

                SafeRaise(nameof(RightValue));
                SafeRaise(nameof(RightText));
                SafeRaise(nameof(IsDifferent));

                if (IsDate) SafeRaise(nameof(RightDate));
                if (IsTime) SafeRaise(nameof(RightTime));
                if (IsSex) SafeRaise(nameof(RightSex));
                if (IsPersonName)
                {
                    SafeRaise(nameof(RightPN_Family));
                    SafeRaise(nameof(RightPN_Given));
                    SafeRaise(nameof(RightPN_Middle));
                    SafeRaise(nameof(RightPN_Prefix));
                    SafeRaise(nameof(RightPN_Suffix));
                }
            }
        }

        // --- Anzeige-Text je Seite (für Sort/Filter im VM) ------------------
        public string LeftText => GetDisplayText(_leftValue);
        public string RightText => GetDisplayText(_rightValue);

        private string GetDisplayText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            if (IsDate)
            {
                var d = HelperFunctions.DicomFormat.ParseDA(raw);
                return d.HasValue ? d.Value.ToString("dd.MM.yyyy") : string.Empty;
            }

            if (IsTime)
            {
                var t = HelperFunctions.DicomFormat.ParseTM(raw);
                return t.HasValue ? t.Value.ToString(@"hh\:mm\:ss") : string.Empty;
            }

            if (IsPersonName)
            {
                var pn = HelperFunctions.DicomFormat.ParsePNAlphabetic(raw);
                var parts = new List<string>(5);
                if (!string.IsNullOrWhiteSpace(pn.Family)) parts.Add(pn.Family);
                if (!string.IsNullOrWhiteSpace(pn.Given)) parts.Add(pn.Given);
                if (!string.IsNullOrWhiteSpace(pn.Middle)) parts.Add(pn.Middle);
                if (!string.IsNullOrWhiteSpace(pn.Prefix)) parts.Add(pn.Prefix);
                if (!string.IsNullOrWhiteSpace(pn.Suffix)) parts.Add(pn.Suffix);
                return string.Join(" ", parts);
            }

            // Sex / Numeric / Generic: Rohwert anzeigen
            return raw;
        }

        // --- UI-State --------------------------------------------------------
        private bool _isAlternate;
        public bool IsAlternate
        {
            get => _isAlternate;
            set { if (_isAlternate == value) return; _isAlternate = value; SafeRaise(); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected == value) return; _isSelected = value; SafeRaise(); }
        }

        private bool _rowDiffHighlighted;
        public bool IsHighlighted
        {
            get => _rowDiffHighlighted;
            set { if (_rowDiffHighlighted == value) return; _rowDiffHighlighted = value; SafeRaise(); }
        }

        // --- Validierung / Highlights ---------------------------------------
        public bool IsDifferent =>
            !string.Equals(_leftValue?.Trim(), _rightValue?.Trim(), StringComparison.OrdinalIgnoreCase);

        private bool _isLeftInvalid;
        public bool IsLeftInvalid
        {
            get => _isLeftInvalid;
            private set { if (_isLeftInvalid == value) return; _isLeftInvalid = value; SafeRaise(); }
        }

        private bool _isRightInvalid;
        public bool IsRightInvalid
        {
            get => _isRightInvalid;
            private set { if (_isRightInvalid == value) return; _isRightInvalid = value; SafeRaise(); }
        }

        private bool _leftInvalidHighlighted;
        public bool LeftInvalidHighlighted
        {
            get => _leftInvalidHighlighted;
            set { if (_leftInvalidHighlighted == value) return; _leftInvalidHighlighted = value; SafeRaise(); }
        }

        private bool _rightInvalidHighlighted;
        public bool RightInvalidHighlighted
        {
            get => _rightInvalidHighlighted;
            set { if (_rightInvalidHighlighted == value) return; _rightInvalidHighlighted = value; SafeRaise(); }
        }

        // --- Typflags --------------------------------------------------------
        public bool IsDate => string.Equals(Vr, "DA", StringComparison.OrdinalIgnoreCase);
        public bool IsTime => string.Equals(Vr, "TM", StringComparison.OrdinalIgnoreCase);
        public bool IsPersonName => string.Equals(Vr, "PN", StringComparison.OrdinalIgnoreCase);
        public bool IsSex => string.Equals(TagId, "(0010,0040)", StringComparison.OrdinalIgnoreCase);
        public bool IsNumeric => string.Equals(Vr, "IS", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(Vr, "DS", StringComparison.OrdinalIgnoreCase);
        public bool IsGeneric => !(IsDate || IsTime || IsPersonName || IsSex || IsNumeric);

        // --- DA (Date) -------------------------------------------------------
        public DateTime? LeftDate
        {
            get => HelperFunctions.DicomFormat.ParseDA(LeftValue);
            set
            {
                var s = value.HasValue ? HelperFunctions.DicomFormat.FormatDA(value.Value) : string.Empty;
                if (LeftValue != s) LeftValue = s;
            }
        }

        public DateTime? RightDate
        {
            get => HelperFunctions.DicomFormat.ParseDA(RightValue);
            set
            {
                var s = value.HasValue ? HelperFunctions.DicomFormat.FormatDA(value.Value) : string.Empty;
                if (RightValue != s) RightValue = s;
            }
        }

        // --- TM (Time) -------------------------------------------------------
        public TimeSpan? LeftTime
        {
            get => HelperFunctions.DicomFormat.ParseTM(LeftValue);
            set
            {
                var s = value.HasValue ? HelperFunctions.DicomFormat.FormatTM(value.Value) : string.Empty;
                if (LeftValue != s) LeftValue = s;
            }
        }

        public TimeSpan? RightTime
        {
            get => HelperFunctions.DicomFormat.ParseTM(RightValue);
            set
            {
                var s = value.HasValue ? HelperFunctions.DicomFormat.FormatTM(value.Value) : string.Empty;
                if (RightValue != s) RightValue = s;
            }
        }

        // --- SEX (0010,0040) -------------------------------------------------
        public string LeftSex
        {
            get => (LeftValue ?? string.Empty).Trim().ToUpperInvariant();
            set
            {
                var v = (value ?? string.Empty).Trim().ToUpperInvariant();
                if (LeftValue != v) LeftValue = v;
            }
        }

        public string RightSex
        {
            get => (RightValue ?? string.Empty).Trim().ToUpperInvariant();
            set
            {
                var v = (value ?? string.Empty).Trim().ToUpperInvariant();
                if (RightValue != v) RightValue = v;
            }
        }

        // --- PN (Alphabetic) -------------------------------------------------
        public string LeftPN_Family
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Family;
            set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Family = value ?? ""; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); }
        }
        public string LeftPN_Given
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Given;
            set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Given = value ?? ""; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); }
        }
        public string LeftPN_Middle
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Middle;
            set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Middle = value ?? ""; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); }
        }
        public string LeftPN_Prefix
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Prefix;
            set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Prefix = value ?? ""; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); }
        }
        public string LeftPN_Suffix
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Suffix;
            set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue); p.Suffix = value ?? ""; LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); }
        }

        public string RightPN_Family
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Family;
            set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Family = value ?? ""; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); }
        }
        public string RightPN_Given
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Given;
            set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Given = value ?? ""; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); }
        }
        public string RightPN_Middle
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Middle;
            set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Middle = value ?? ""; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); }
        }
        public string RightPN_Prefix
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Prefix;
            set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Prefix = value ?? ""; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); }
        }
        public string RightPN_Suffix
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue).Suffix;
            set { var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(RightValue); p.Suffix = value ?? ""; RightValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p); }
        }

        // --------------------------------------------------------------------
        public event PropertyChangedEventHandler? PropertyChanged;

        // === Koaleszierter PropertyChanged-Dispatcher ========================
        private readonly HashSet<string> _pendingProps = new(StringComparer.Ordinal);
        private bool _dispatchScheduled;

        private void SafeRaise([CallerMemberName] string? name = null)
        {
            if (string.IsNullOrEmpty(name)) return;

            lock (_pendingProps)
            {
                _pendingProps.Add(name);
                if (_dispatchScheduled) return;
                _dispatchScheduled = true;

                MainThread.BeginInvokeOnMainThread(FlushPropertyChangedQueue);
            }
        }

        private void FlushPropertyChangedQueue()
        {
            string[] toRaise;
            lock (_pendingProps)
            {
                _dispatchScheduled = false;
                if (_pendingProps.Count == 0) return;
                toRaise = new string[_pendingProps.Count];
                _pendingProps.CopyTo(toRaise);
                _pendingProps.Clear();
            }

            var handler = PropertyChanged;
            if (handler == null) return;

            foreach (var p in toRaise)
                handler(this, new PropertyChangedEventArgs(p));
        }
    }
}
