using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.ApplicationModel;

namespace DViewer
{
    /// <summary>
    /// Zeile der Vergleichstabelle. Hält NUR Referenzen auf die linke und rechte
    /// Datensatz-Zeile (DicomMetadataItem) und kopiert keine Werte zwischen den Seiten.
    /// Damit kann beim Laden der zweiten Seite nichts mehr überschrieben werden.
    /// </summary>
    public sealed class CombinedMetadataItem : INotifyPropertyChanged, IDisposable
    {
        private readonly DicomMetadataItem? _left;
        private readonly DicomMetadataItem? _right;

        public CombinedMetadataItem(DicomMetadataItem? left, DicomMetadataItem? right)
        {
            _left  = left;
            _right = right;

            // Initiale Validierung
            _isLeftInvalid  = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, LeftValue);
            _isRightInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, RightValue);
        }

        // -------- Stammdaten (aus links, sonst rechts) --------
        public string TagId => _left?.TagId ?? _right?.TagId ?? string.Empty;
        public string Name  => _left?.Name  ?? _right?.Name  ?? string.Empty;
        public string Vr    => _left?.Vr    ?? _right?.Vr    ?? string.Empty;

        // -------- Werte (delegiert an Seiten) --------
        public string LeftValue
        {
            get => _left?.Value ?? string.Empty;
            set
            {
                if (_left == null) return;
                var nv = value ?? string.Empty;
                if (string.Equals(_left.Value ?? string.Empty, nv, StringComparison.Ordinal)) return;

                _left.Value = nv;

                // Validierung & abhängige Properties
                _isLeftInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, _left.Value ?? string.Empty);

                Raise(nameof(LeftValue));
                Raise(nameof(IsDifferent));

                if (IsDate) Raise(nameof(LeftDate));
                if (IsTime) Raise(nameof(LeftTime));
                if (IsSex)  Raise(nameof(LeftSex));
                if (IsPersonName)
                {
                    Raise(nameof(LeftPN_Family));
                    Raise(nameof(LeftPN_Given));
                    Raise(nameof(LeftPN_Middle));
                    Raise(nameof(LeftPN_Prefix));
                    Raise(nameof(LeftPN_Suffix));
                }
            }
        }

        public string RightValue
        {
            get => _right?.Value ?? string.Empty;
            set
            {
                if (_right == null) return;
                var nv = value ?? string.Empty;
                if (string.Equals(_right.Value ?? string.Empty, nv, StringComparison.Ordinal)) return;

                _right.Value = nv;

                _isRightInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, _right.Value ?? string.Empty);

                Raise(nameof(RightValue));
                Raise(nameof(IsDifferent));

                if (IsDate) Raise(nameof(RightDate));
                if (IsTime) Raise(nameof(RightTime));
                if (IsSex)  Raise(nameof(RightSex));
                if (IsPersonName)
                {
                    Raise(nameof(RightPN_Family));
                    Raise(nameof(RightPN_Given));
                    Raise(nameof(RightPN_Middle));
                    Raise(nameof(RightPN_Prefix));
                    Raise(nameof(RightPN_Suffix));
                }
            }
        }

        // -------- Vergleich / Validierung --------
        public bool IsDifferent
            => !string.Equals(LeftValue?.Trim(), RightValue?.Trim(), StringComparison.OrdinalIgnoreCase);

        private bool _isLeftInvalid;
        public bool IsLeftInvalid
        {
            get => _isLeftInvalid;
            private set { if (_isLeftInvalid == value) return; _isLeftInvalid = value; Raise(); }
        }

        private bool _isRightInvalid;
        public bool IsRightInvalid
        {
            get => _isRightInvalid;
            private set { if (_isRightInvalid == value) return; _isRightInvalid = value; Raise(); }
        }

        // -------- UI-State / Highlights --------
        private bool _isAlternate;
        public bool IsAlternate { get => _isAlternate; set { if (_isAlternate == value) return; _isAlternate = value; Raise(); } }

        private bool _isSelected;
        public bool IsSelected  { get => _isSelected;  set { if (_isSelected  == value) return; _isSelected  = value; Raise(); } }

        private bool _isRowHighlighted;
        public bool IsHighlighted { get => _isRowHighlighted; set { if (_isRowHighlighted == value) return; _isRowHighlighted = value; Raise(); } }

        private bool _leftInvalidHighlighted;
        public bool LeftInvalidHighlighted
        {
            get => _leftInvalidHighlighted;
            set { if (_leftInvalidHighlighted == value) return; _leftInvalidHighlighted = value; Raise(); }
        }

        private bool _rightInvalidHighlighted;
        public bool RightInvalidHighlighted
        {
            get => _rightInvalidHighlighted;
            set { if (_rightInvalidHighlighted == value) return; _rightInvalidHighlighted = value; Raise(); }
        }

        // -------- Typ-Flags für Editorwahl (nur aus VR/Tag abgeleitet) --------
        public bool IsDate       => string.Equals(Vr, "DA", StringComparison.OrdinalIgnoreCase);
        public bool IsTime       => string.Equals(Vr, "TM", StringComparison.OrdinalIgnoreCase);
        public bool IsPersonName => string.Equals(Vr, "PN", StringComparison.OrdinalIgnoreCase);
        public bool IsSex        => string.Equals(TagId, "(0010,0040)", StringComparison.OrdinalIgnoreCase);
        public bool IsNumeric    => string.Equals(Vr, "IS", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(Vr, "DS", StringComparison.OrdinalIgnoreCase);
        public bool IsGeneric    => !(IsDate || IsTime || IsPersonName || IsSex || IsNumeric);

        // -------- DA (Date) Wrapper --------
        public DateTime? LeftDate
        {
            get => HelperFunctions.DicomFormat.ParseDA(LeftValue);
            set
            {
                var s = value.HasValue ? HelperFunctions.DicomFormat.FormatDA(value.Value) : string.Empty;
                if (!string.Equals(LeftValue, s, StringComparison.Ordinal)) LeftValue = s;
                Raise();
            }
        }

        public DateTime? RightDate
        {
            get => HelperFunctions.DicomFormat.ParseDA(RightValue);
            set
            {
                var s = value.HasValue ? HelperFunctions.DicomFormat.FormatDA(value.Value) : string.Empty;
                if (!string.Equals(RightValue, s, StringComparison.Ordinal)) RightValue = s;
                Raise();
            }
        }

        // -------- TM (Time) Wrapper --------
        public TimeSpan? LeftTime
        {
            get => HelperFunctions.DicomFormat.ParseTM(LeftValue);
            set
            {
                var s = value.HasValue ? HelperFunctions.DicomFormat.FormatTM(value.Value) : string.Empty;
                if (!string.Equals(LeftValue, s, StringComparison.Ordinal)) LeftValue = s;
                Raise();
            }
        }

        public TimeSpan? RightTime
        {
            get => HelperFunctions.DicomFormat.ParseTM(RightValue);
            set
            {
                var s = value.HasValue ? HelperFunctions.DicomFormat.FormatTM(value.Value) : string.Empty;
                if (!string.Equals(RightValue, s, StringComparison.Ordinal)) RightValue = s;
                Raise();
            }
        }

        // -------- Sex (0010,0040) --------
        public string LeftSex
        {
            get => (LeftValue ?? string.Empty).Trim().ToUpperInvariant();
            set
            {
                var v = (value ?? string.Empty).Trim().ToUpperInvariant();
                if (!string.Equals(LeftValue, v, StringComparison.Ordinal)) LeftValue = v;
                Raise();
            }
        }

        public string RightSex
        {
            get => (RightValue ?? string.Empty).Trim().ToUpperInvariant();
            set
            {
                var v = (value ?? string.Empty).Trim().ToUpperInvariant();
                if (!string.Equals(RightValue, v, StringComparison.Ordinal)) RightValue = v;
                Raise();
            }
        }

        // -------- PN (Person Name) Alphabetic-Komponente --------
        public string LeftPN_Family
        {
            get => HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue).Family;
            set
            {
                var p = HelperFunctions.DicomFormat.ParsePNAlphabetic(LeftValue);
                p.Family = value ?? string.Empty;
                LeftValue = HelperFunctions.DicomFormat.FormatPNAlphabetic(p);
                Raise();
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
                Raise();
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
                Raise();
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
                Raise();
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
                Raise();
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
                Raise();
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
                Raise();
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
                Raise();
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
                Raise();
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
                Raise();
            }
        }

        // -------- INotify / Utilities --------
        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise([CallerMemberName] string? name = null)
        {
            if (string.IsNullOrEmpty(name)) return;

            if (MainThread.IsMainThread)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            else
                MainThread.BeginInvokeOnMainThread(() =>
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
        }

        public void Dispose()
        {
            // aktuell nichts zu lösen – Platzhalter, damit MainViewModel sicher Dispose() rufen kann
        }
    }
}
