using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DViewer
{
    /// <summary>
    /// Eine reine Zeilen-Repräsentation pro Seite (Links/Rechts).
    /// Keine Verweise auf die jeweils andere Seite → verhindert Überschreiben.
    /// </summary>
    public sealed class SideRow : INotifyPropertyChanged
    {
        // Stammdaten (stabil)
        public string TagId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Vr { get; init; } = string.Empty;

        // Wert auf der jeweiligen Seite
        private string _value = string.Empty;
        public string Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        // UI-Flags (Zebra, Diff-/Invalid-Highlighting etc.)
        private bool _isAlternate;
        public bool IsAlternate
        {
            get => _isAlternate;
            set { if (_isAlternate == value) return; _isAlternate = value; OnPropertyChanged(); }
        }

        private bool _isInvalid;
        public bool IsInvalid
        {
            get => _isInvalid;
            set { if (_isInvalid == value) return; _isInvalid = value; OnPropertyChanged(); }
        }

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set { if (_isHighlighted == value) return; _isHighlighted = value; OnPropertyChanged(); }
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
