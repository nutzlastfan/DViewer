using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;

namespace DViewer
{
    public class MainViewModel : INotifyPropertyChanged
    {



        public ObservableCollection<CombinedMetadataItem> CombinedMetadataList { get; private set; } = new();

        // call this after setting Left.Metadata / Right.Metadata
        private void RebuildCombined()
        {
            // 1) alte Abos entfernen
            foreach (var it in CombinedMetadataList)
                it.PropertyChanged -= OnRowPropertyChanged;

            // 2) Dictionaries (nur lokal, keine geteilten Referenzen)
            var leftDict = Left?.Metadata != null ? Left.Metadata.ToDictionary(m => m.TagId) : new Dictionary<string, DicomMetadataItem>();
            var rightDict = Right?.Metadata != null ? Right.Metadata.ToDictionary(m => m.TagId) : new Dictionary<string, DicomMetadataItem>();

            // 3) alle Keys sortiert
            var allKeys = new SortedSet<string>(leftDict.Keys.Concat(rightDict.Keys), StringComparer.Ordinal);

            // 4) neu aufbauen (neue Instanzen, keine Reuse!)
            var fresh = new ObservableCollection<CombinedMetadataItem>();
            int row = 0;
            foreach (var key in allKeys)
            {
                leftDict.TryGetValue(key, out var l);
                rightDict.TryGetValue(key, out var r);

                var item = new CombinedMetadataItem
                {
                    TagId = key,
                    Name = l?.Name ?? r?.Name ?? string.Empty,
                    Vr = l?.Vr ?? r?.Vr ?? string.Empty,
                    LeftValue = l?.Value ?? string.Empty,
                    RightValue = r?.Value ?? string.Empty,
                    IsAlternate = (row++ % 2) == 1
                };

                item.PropertyChanged += OnRowPropertyChanged; // nur 1× pro Build
                fresh.Add(item);
            }

            CombinedMetadataList = fresh;
            Raise(nameof(CombinedMetadataList));
        }

        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Wenn du Editieren auf eine Seite zurückschreiben willst,
            // mach das hier – aber NUR die eine Seite (kein Spiegeln).
            if (sender is not CombinedMetadataItem row) return;
            if (e.PropertyName == nameof(CombinedMetadataItem.LeftValue))
                UpdateBackstore(Left?.Metadata, row.TagId, row.LeftValue);
            else if (e.PropertyName == nameof(CombinedMetadataItem.RightValue))
                UpdateBackstore(Right?.Metadata, row.TagId, row.RightValue);
        }

        private static void UpdateBackstore(List<DicomMetadataItem>? list, string tagId, string newValue)
        {
            if (list == null) return;
            var m = list.FirstOrDefault(x => x.TagId == tagId);
            if (m != null) m.Value = newValue;
            else list.Add(new DicomMetadataItem { TagId = tagId, Name = "", Vr = "", Value = newValue });
        }

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event PropertyChangedEventHandler? PropertyChanged;













        private readonly DicomLoader _loader;

        public MainViewModel(DicomLoader loader)
        {
            _loader = loader;
            Left = new DicomFileViewModel();
            Right = new DicomFileViewModel();

            CombinedMetadataList = new ObservableCollection<CombinedMetadataItem>();

            LoadLeftCommand = new DelegateCommand(async () => await LoadLeftAsync());
            LoadRightCommand = new DelegateCommand(async () => await LoadRightAsync());
        }

        // Dateien
        public DicomFileViewModel Left { get; private set; }
        public DicomFileViewModel Right { get; private set; }

        // Spaltenbreiten (XAML Bindings)
        double _tagWidth = 1.2, _nameWidth = 2.0, _leftWidth = 2.5, _rightWidth = 2.5;
        public double TagWidth { get => _tagWidth; set { if (_tagWidth == value) return; _tagWidth = value; OnPropertyChanged(); } }
        public double NameWidth { get => _nameWidth; set { if (_nameWidth == value) return; _nameWidth = value; OnPropertyChanged(); } }
        public double LeftWidth { get => _leftWidth; set { if (_leftWidth == value) return; _leftWidth = value; OnPropertyChanged(); } }
        public double RightWidth { get => _rightWidth; set { if (_rightWidth == value) return; _rightWidth = value; OnPropertyChanged(); } }

        // Picker-Optionen
        public string[] SexOptions { get; } = { "M", "F", "O", "N", "U" };

        // Vergleichsliste fürs Grid
        //public ObservableCollection<CombinedMetadataItem> CombinedMetadataList { get; }

        // Toggles
        bool _highlightDifferences;
        public bool HighlightDifferences
        {
            get => _highlightDifferences;
            set { if (_highlightDifferences == value) return; _highlightDifferences = value; OnPropertyChanged(); ApplyHighlights(); }
        }

        bool _highlightInvalidValues;
        public bool HighlightInvalidValues
        {
            get => _highlightInvalidValues;
            set { if (_highlightInvalidValues == value) return; _highlightInvalidValues = value; OnPropertyChanged(); ApplyHighlights(); }
        }

        // Commands
        public ICommand LoadLeftCommand { get; }
        public ICommand LoadRightCommand { get; }

        public async Task LoadLeftAsync()
        {
            var vm = await _loader.PickAndLoadAsync();
            if (vm == null) return;

            Left = vm; OnPropertyChanged(nameof(Left));
            RebuildCombined();
        }

        public async Task LoadRightAsync()
        {
            var vm = await _loader.PickAndLoadAsync();
            if (vm == null) return;

            Right = vm; OnPropertyChanged(nameof(Right));
            RebuildCombined();
        }

        // Externes "Öffnen mit..."
        public async Task HandleExternalOpenAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var vm = await _loader.LoadAsync(path);
            if (string.IsNullOrEmpty(Left?.FileName))
            {
                Left = vm; OnPropertyChanged(nameof(Left));
            }
            else
            {
                Right = vm; OnPropertyChanged(nameof(Right));
            }
            RebuildCombined();
        }

        private void xRebuildCombined()
        {
            // 1) SNAPSHOTS bauen (nie Objekte teilen!)
            var left = (Left?.Metadata ?? Enumerable.Empty<DicomMetadataItem>()).ToDictionary(m => m.TagId, StringComparer.OrdinalIgnoreCase);
            var right = (Right?.Metadata ?? Enumerable.Empty<DicomMetadataItem>()).ToDictionary(m => m.TagId, StringComparer.OrdinalIgnoreCase);

            // 2) Keys vereinigen & sortieren
            var keys = left.Keys.Union(right.Keys, StringComparer.OrdinalIgnoreCase)
                                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                                .ToList();

            // 3) Liste neu erstellen (kein Reuse alter Items → keine Seiteneffekte)
            CombinedMetadataList.Clear();

            int row = 0;
            foreach (var key in keys)
            {
                left.TryGetValue(key, out var l);
                right.TryGetValue(key, out var r);

                var item = new CombinedMetadataItem
                {
                    TagId = key,
                    Name = l?.Name ?? r?.Name ?? string.Empty,
                    Vr = l?.Vr ?? r?.Vr ?? string.Empty,
                    IsAlternate = (row % 2) == 1
                };

                // Nur Strings kopieren, KEINE Links auf l/r
                item.LeftValue = l?.Value ?? string.Empty;
                item.RightValue = r?.Value ?? string.Empty;

                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(CombinedMetadataItem.LeftValue)
                        or nameof(CombinedMetadataItem.RightValue)
                        or nameof(CombinedMetadataItem.IsLeftInvalid)
                        or nameof(CombinedMetadataItem.IsRightInvalid)
                        or nameof(CombinedMetadataItem.IsDifferent))
                    {
                        // lokale Markierungen aktualisieren
                        item.IsHighlighted = HighlightDifferences && item.IsDifferent;
                        item.LeftInvalidHighlighted = HighlightInvalidValues && item.IsLeftInvalid;
                        item.RightInvalidHighlighted = HighlightInvalidValues && item.IsRightInvalid;
                    }
                };

                CombinedMetadataList.Add(item);
                row++;
            }

            ApplyHighlights();
        }

        private void ApplyHighlights()
        {
            int i = 0;
            foreach (var it in CombinedMetadataList)
            {
                it.IsAlternate = (i++ % 2) == 1;
                it.IsHighlighted = HighlightDifferences && it.IsDifferent;
                it.LeftInvalidHighlighted = HighlightInvalidValues && it.IsLeftInvalid;
                it.RightInvalidHighlighted = HighlightInvalidValues && it.IsRightInvalid;
            }
        }

        // INotifyPropertyChanged
        //public event PropertyChangedEventHandler? PropertyChanged;
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
