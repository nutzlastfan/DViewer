using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace DViewer
{
    /// <summary>
    /// ViewModel für eine einzelne DICOM-Datei (eine Seite).
    /// Enthält:
    /// - Dateiname
    /// - Vorschaubild
    /// - Zeilen (SideRow) der Metadaten ausschließlich für diese Seite
    /// 
    /// WICHTIG:
    /// Wir behalten eine schreibgeschützte Metadata-Liste zur
    /// Abwärtskompatibilität bei, verwenden aber intern ausschließlich <see cref="Rows"/>.
    /// </summary>
    public sealed class DicomFileViewModel : INotifyPropertyChanged
    {
        private string _fileName = string.Empty;
        public string FileName
        {
            get => _fileName;
            set { if (_fileName == value) return; _fileName = value ?? string.Empty; OnPropertyChanged(); }
        }

        private ImageSource? _image;
        public ImageSource? Image
        {
            get => _image;
            set { if (_image == value) return; _image = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Reine, seitenspezifische Zeilen zum Binden in der UI.
        /// </summary>
        public ObservableCollection<SideRow> Rows { get; } = new();

        /// <summary>
        /// Optional/Legacy: Roh-Metadaten wie vorher (nur lesen).
        /// </summary>
        public IReadOnlyList<DicomMetadataItem> Metadata { get; private set; } = Array.Empty<DicomMetadataItem>();

        /// <summary>
        /// Schnelles Lookup der Zeilen nach TagId.
        /// </summary>
        public IReadOnlyDictionary<string, SideRow> RowMap => _rowMap;
        private readonly Dictionary<string, SideRow> _rowMap = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Befüllt die Seite mit Metadaten (ersetzt den bisherigen Inhalt).
        /// </summary>
        public void SetMetadata(IEnumerable<DicomMetadataItem> items)
        {
            if (items == null)
            {
                Clear();
                return;
            }

            var list = items
                .Where(m => !string.IsNullOrWhiteSpace(m.TagId))
                .OrderBy(m => m.TagId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Metadata = list;

            // Rows neu aufbauen
            Rows.Clear();
            _rowMap.Clear();

            int i = 0;
            foreach (var m in list)
            {
                var row = new SideRow
                {
                    TagId = m.TagId,
                    Name = m.Name ?? string.Empty,
                    Vr = m.Vr ?? string.Empty,
                    Value = m.Value ?? string.Empty,
                    IsAlternate = (i++ % 2) == 1
                };

                Rows.Add(row);
                if (!_rowMap.ContainsKey(row.TagId))
                    _rowMap.Add(row.TagId, row);
            }

            OnPropertyChanged(nameof(Rows));
            OnPropertyChanged(nameof(Metadata));
        }

        /// <summary>
        /// Setzt alles zurück (z.B. bei Fehlern).
        /// </summary>
        public void Clear()
        {
            Metadata = Array.Empty<DicomMetadataItem>();
            Rows.Clear();
            _rowMap.Clear();
            Image = null;

            OnPropertyChanged(nameof(Rows));
            OnPropertyChanged(nameof(Metadata));
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
