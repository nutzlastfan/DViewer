using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace DViewer
{
    public class DicomFileViewModel : INotifyPropertyChanged
    {
        public DicomFileViewModel()
        {
            MetadataList.CollectionChanged += MetadataList_CollectionChanged;
        }

        // Datei-/Vorschauinfos
        private string _fileName = string.Empty;
        public string FileName { get => _fileName; set { if (_fileName == value) return; _fileName = value; OnPropertyChanged(); } }

        private ImageSource? _image;
        public ImageSource? Image { get => _image; set { if (_image == value) return; _image = value; OnPropertyChanged(); } }

        // >>> HIER: die Liste aller Tags/Werte dieser Seite <<<
        public ObservableCollection<DicomMetadataItem> MetadataList { get; } = new();

        // Praktisch für Vergleiche/Filter: Map TagId -> Value
        public IReadOnlyDictionary<string, string> MetadataDictionary =>
            MetadataList
                .GroupBy(m => m.TagId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        // Optional: zum kompletten Ersetzen (z.B. im Loader)
        public void SetMetadata(IEnumerable<DicomMetadataItem> items)
        {
            // Events der alten Items lösen
            foreach (var old in _attachedItems)
                old.PropertyChanged -= Item_PropertyChanged;
            _attachedItems.Clear();

            MetadataList.Clear();
            foreach (var it in items)
                MetadataList.Add(it); // CollectionChanged sorgt für Abo
            OnPropertyChanged(nameof(MetadataDictionary));
        }

        // --- Verkabelung, damit Änderungen am einzelnen Item nach oben „durchpiepen“ ---
        private readonly List<DicomMetadataItem> _attachedItems = new();

        private void MetadataList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var o in e.OldItems.OfType<DicomMetadataItem>())
                {
                    o.PropertyChanged -= Item_PropertyChanged;
                    _attachedItems.Remove(o);
                }
            }
            if (e.NewItems != null)
            {
                foreach (var n in e.NewItems.OfType<DicomMetadataItem>())
                {
                    n.PropertyChanged += Item_PropertyChanged;
                    _attachedItems.Add(n);
                }
            }

            // Dictionary & UI aktualisieren
            OnPropertyChanged(nameof(MetadataDictionary));
            OnPropertyChanged(nameof(MetadataList));
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Wenn TagId/Value/Vr/Name sich ändern, Dictionary neu „melden“
            if (e.PropertyName is nameof(DicomMetadataItem.TagId)
                               or nameof(DicomMetadataItem.Value)
                               or nameof(DicomMetadataItem.Vr)
                               or nameof(DicomMetadataItem.Name))
            {
                OnPropertyChanged(nameof(MetadataDictionary));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
