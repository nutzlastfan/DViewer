using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.ApplicationModel; // MainThread

namespace DViewer
{
    public sealed class CombinedMetadataItem : INotifyPropertyChanged
    {
        // --- Stammdaten ---
        public string TagId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Vr { get; init; } = string.Empty;   // DICOM VR

        // --- Werte (als DICOM-String) ---
        private string _leftValue = string.Empty;
        private string _rightValue = string.Empty;

        public string LeftValue
        {
            get => _leftValue;
            set
            {
                if (value == null) value = string.Empty;
                if (_leftValue == value) return;

                // vor Änderung: IsDifferent alt merken
                var oldDifferent = !string.Equals(_leftValue.Trim(), _rightValue.Trim(), StringComparison.OrdinalIgnoreCase);

                _leftValue = value;

                // Validierung neu
                var newLeftInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, _leftValue);
                if (_isLeftInvalid != newLeftInvalid)
                {
                    _isLeftInvalid = newLeftInvalid;
                    SafeRaisePropertyChanged(nameof(IsLeftInvalid));
                }

                // LeftValue selbst
                SafeRaisePropertyChanged(nameof(LeftValue));

                // IsDifferent nur raisen, wenn sich das Ergebnis geändert hat
                var newDifferent = !string.Equals(_leftValue.Trim(), _rightValue.Trim(), StringComparison.OrdinalIgnoreCase);
                if (oldDifferent != newDifferent)
                    SafeRaisePropertyChanged(nameof(IsDifferent));

                // Abhängig abbildende Wrapper (nur 1x je Name dank Koaleszierung)
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

                FlushBatchIfNeeded();
            }
        }

        public string RightValue
        {
            get => _rightValue;
            set
            {
                if (value == null) value = string.Empty;
                if (_rightValue == value) return;

                var oldDifferent = !string.Equals(_leftValue.Trim(), _rightValue.Trim(), StringComparison.OrdinalIgnoreCase);

                _rightValue = value;

                var newRightInvalid = !HelperFunctions.DicomValueValidator.IsValidValue(Vr, TagId, _rightValue);
                if (_isRightInvalid != newRightInvalid)
                {
                    _isRightInvalid = newRightInvalid;
                    SafeRaisePropertyChanged(nameof(IsRightInvalid));
                }

                SafeRaisePropertyChanged(nameof(RightValue));

                var newDifferent = !string.Equals(_leftValue.Trim(), _rightValue.Trim(), StringComparison.OrdinalIgnoreCase);
                if (oldDifferent != newDifferent)
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

                FlushBatchIfNeeded();
            }
        }

        // --- UI-State ---
        private bool _isAlternate;
        public bool IsAlternate { get => _isAlternate; set => Set(ref _isAlternate, value); }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

        private bool _rowDiffHighlighted;
        public bool IsHighlighted { get => _rowDiffHighlighted; set => Set(ref _rowDiffHighlighted, value); }

        // --- Validierung/Highlights ---
        public bool IsDifferent => !string.Equals(_leftValue?.Trim(), _rightValue?.Trim(), StringComparison.OrdinalIgnoreCase);

        private bool _isLeftInvalid;
        public bool IsLeftInvalid { get => _isLeftInvalid; private set => Set(ref _isLeftInvalid, value); }

        private bool _isRightInvalid;
        public bool IsRightInvalid { get => _isRightInvalid; private set => Set(ref _isRightInvalid, value); }

        private bool _leftInvalidHighlighted;
        public bool LeftInvalidHighlighted { get => _leftInvalidHighlighted; set => Set(ref _leftInvalidHighlighted, value); }

        private bool _rightInvalidHighlighted;
        public bool RightInvalidHighlighted { get => _rightInvalidHighlighted; set => Set(ref _rightInvalidHighlighted, value); }

        // --- Typflags ---
        public bool IsDate => string.Equals(Vr, "DA", StringComparison.OrdinalIgnoreCase);
        public bool IsTime => string.Equals(Vr, "TM", StringComparison.OrdinalIgnoreCase);
        public bool IsPersonName => string.Equals(Vr, "PN", StringComparison.OrdinalIgnoreCase);
        public bool IsSex => string.Equals(TagId, "(0010,0040)", StringComparison.OrdinalIgnoreCase);
        public bool IsNumeric => string.Equals(Vr, "IS", StringComparison.OrdinalIgnoreCase) || string.Equals(Vr, "DS", StringComparison.OrdinalIgnoreCase);
        public bool IsGeneric => !(IsDate || IsTime || IsPersonName || IsSex || IsNumeric);

        // --- DA (nullable) ---
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

        // --- TM (nullable) ---
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

        // --- Sex ---
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

        // --- PN (Alphabetic) ---
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

        // --- Koaleszierung / Batch ---
        private int _batchDepth = 0;
        private readonly HashSet<string> _batchedProps = new(StringComparer.Ordinal);

        /// <summary>Beginnt einen Batch; beim Dispose werden die gesammelten PropertyChanged-Events (je Property exakt 1x) auf dem UI-Thread gefeuert.</summary>
        public IDisposable BeginBatch() => new BatchScope(this);

        private sealed class BatchScope : IDisposable
        {
            private readonly CombinedMetadataItem _owner;
            private bool _disposed;
            public BatchScope(CombinedMetadataItem owner) { _owner = owner; _owner._batchDepth++; }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner._batchDepth--;
                _owner.FlushBatchIfNeeded();
            }
        }

        private void FlushBatchIfNeeded()
        {
            if (_batchDepth > 0) return;
            if (_batchedProps.Count == 0) return;

            var toRaise = new List<string>(_batchedProps);
            _batchedProps.Clear();

            void RaiseAll()
            {
                foreach (var p in toRaise)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
            }

            if (MainThread.IsMainThread) RaiseAll();
            else MainThread.BeginInvokeOnMainThread(RaiseAll);
        }

        private void SafeRaisePropertyChanged([CallerMemberName] string? name = null)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_batchDepth > 0)
            {
                _batchedProps.Add(name);
                return;
            }

            void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            if (MainThread.IsMainThread) Raise();
            else MainThread.BeginInvokeOnMainThread(Raise);
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            SafeRaisePropertyChanged(name);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
