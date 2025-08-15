using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace DViewer
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // ------------------------------------------------------------
        // Felder / Ctor
        // ------------------------------------------------------------
        private readonly IDicomLoader _loader;

        public MainViewModel(IDicomLoader loader)
        {
            _loader = loader;

            LoadLeftCommand = new DelegateCommand(async () => await PickAndLoadAsync(isLeft: true));
            LoadRightCommand = new DelegateCommand(async () => await PickAndLoadAsync(isLeft: false));

            ApplyFilterCommand = new DelegateCommand(() => { UpdateCombinedMetadataList(); return Task.CompletedTask; });
            SortCommand = new ParameterCommand(p => { ApplySort(p?.ToString() ?? "TagId"); });
            ClearTagFilterCommand = new DelegateCommand(() => { SelectedTagFilter = null; TagSearchText = string.Empty; UpdateFilteredTagFilters(); return Task.CompletedTask; });
            OpenAddTagPopupCommand = new ParameterCommand(async p => await OpenAddTagPopupAsync((Page)p), p => Left != null || Right != null);

            RebuildAllCombined();
        }
        // alterniert, wenn beide Seiten schon belegt sind
        private bool _alternateExternal;

        public async Task HandleExternalOpenAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                var vm = await _loader.LoadAsync(path);

                bool leftEmpty = Left == null || (Left.MetadataList?.Count ?? 0) == 0;
                bool rightEmpty = Right == null || (Right.MetadataList?.Count ?? 0) == 0;

                if (leftEmpty)
                {
                    Left = vm;
                }
                else if (rightEmpty)
                {
                    Right = vm;
                }
                else
                {
                    // beide belegt -> abwechselnd ersetzen
                    if (_alternateExternal) Left = vm; else Right = vm;
                    _alternateExternal = !_alternateExternal;
                }
            }
            catch (Exception ex)
            {
                // optionaler Hinweis im UI
                await Application.Current?.MainPage?.DisplayAlert(
                    "Fehler",
                    $"Datei konnte nicht geladen werden:\n{ex.Message}",
                    "OK");
            }
        }
        // ------------------------------------------------------------
        // Linke/rechte Datei + Laden
        // ------------------------------------------------------------
        private DicomFileViewModel _left = new();
        private DicomFileViewModel _right = new();

        public DicomFileViewModel Left
        {
            get => _left;
            private set
            {
                if (_left == value) return;
                UnsubscribeFrom(_left);
                _left = value ?? new();
                SubscribeTo(_left);
                OnPropertyChanged();
                RebuildAllCombined();
                UpdateWindowTitle();
            }
        }

        public DicomFileViewModel Right
        {
            get => _right;
            private set
            {
                if (_right == value) return;
                UnsubscribeFrom(_right);
                _right = value ?? new();
                SubscribeTo(_right);
                OnPropertyChanged();
                RebuildAllCombined();
                UpdateWindowTitle();
            }
        }

        private async Task PickAndLoadAsync(bool isLeft)
        {
            try
            {
                var result = await FilePicker.Default.PickAsync(new PickOptions
                {
                    FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.WinUI,       new[] { ".dcm" } },
                        { DevicePlatform.MacCatalyst, new[] { ".dcm" } },
                        { DevicePlatform.iOS,         new[] { ".dcm" } },
                        { DevicePlatform.Android,     new[] { ".dcm" } },
                    }),
                    PickerTitle = isLeft ? "DICOM-Datei (links) auswählen" : "DICOM-Datei (rechts) auswählen"
                });
                if (result == null) return;

                if (isLeft) Left = await _loader.LoadAsync(result.FullPath);
                else Right = await _loader.LoadAsync(result.FullPath);

                OnPropertyChanged(nameof(MetadataDifferences));
            }
            catch (Exception ex)
            {
                await Application.Current.MainPage.DisplayAlert("Fehler", $"{(isLeft ? "Links" : "Rechts")} laden fehlgeschlagen: {ex.Message}", "OK");
            }
        }

        // ------------------------------------------------------------
        // Collections & Filter/Sort
        // ------------------------------------------------------------
        public ObservableCollection<CombinedMetadataItem> CombinedMetadataList { get; } = new();

        private List<CombinedMetadataItem> _allCombined = new();
        private readonly HashSet<CombinedMetadataItem> _attached = new();

        private bool _building;
        private bool _updatingList;
        private bool _suppressBackProp;

        // Sortierung
        private string _currentSortColumn = "TagId";
        private bool _ascending = true;

        private IEnumerable<CombinedMetadataItem> ApplyFilterAndSort(IEnumerable<CombinedMetadataItem> items)
        {
            IEnumerable<CombinedMetadataItem> q = items;

            // optionaler Textfilter (Tag/Name/Left/Right)
            var filter = FilterText?.Trim();
            if (!string.IsNullOrWhiteSpace(filter))
            {
                q = q.Where(i =>
                    (i.TagId?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (i.Name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (i.LeftValue?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (i.RightValue?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
            }

            // Tag-Filter (falls gesetzt)
            if (SelectedTagFilter != null)
            {
                q = q.Where(i => string.Equals(i.TagId, SelectedTagFilter.TagId, StringComparison.OrdinalIgnoreCase)
                              || i.Name.Contains(SelectedTagFilter.Name, StringComparison.OrdinalIgnoreCase));
            }

            // Sortierung
            Func<CombinedMetadataItem, object?> keySel = _currentSortColumn switch
            {
                "TagId" => i => i.TagId,
                "Name" => i => i.Name,
                "LeftValue" => i => i.LeftValue,
                "RightValue" => i => i.RightValue,
                _ => i => i.TagId
            };
            q = _ascending ? q.OrderBy(keySel) : q.OrderByDescending(keySel);

            return q.ToList();
        }

        private void ApplySort(string col)
        {
            if (_currentSortColumn == col) _ascending = !_ascending;
            else { _currentSortColumn = col; _ascending = true; }

            OnPropertyChanged(nameof(TagHeaderText));
            OnPropertyChanged(nameof(NameHeaderText));
            OnPropertyChanged(nameof(LeftHeaderText));
            OnPropertyChanged(nameof(RightHeaderText));
            UpdateCombinedMetadataList();
        }

        public string TagHeaderText => _currentSortColumn == "TagId" ? $"Tag {(_ascending ? "▲" : "▼")}" : "Tag";
        public string NameHeaderText => _currentSortColumn == "Name" ? $"Name {(_ascending ? "▲" : "▼")}" : "Name";
        public string LeftHeaderText => _currentSortColumn == "LeftValue" ? $"Links {(_ascending ? "▲" : "▼")}" : "Links";
        public string RightHeaderText => _currentSortColumn == "RightValue" ? $"Rechts {(_ascending ? "▲" : "▼")}" : "Rechts";

        // ------------------------------------------------------------
        // Rebuild & Update
        // ------------------------------------------------------------
        private void SubscribeTo(DicomFileViewModel vm)
        {
            if (vm == null) return;
            vm.PropertyChanged += ChildPropertyChanged;
            vm.MetadataList.CollectionChanged += Metadata_CollectionChanged;
        }
        private void UnsubscribeFrom(DicomFileViewModel vm)
        {
            if (vm == null) return;
            vm.PropertyChanged -= ChildPropertyChanged;
            vm.MetadataList.CollectionChanged -= Metadata_CollectionChanged;
        }
        private void ChildPropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DicomFileViewModel.MetadataList))
                RebuildAllCombined();
        }
        private void Metadata_CollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
        {
            RebuildAllCombined();
        }

        public void RebuildAllCombined()
        {
            if (_building) return;
            _building = true;
            try
            {
                // Alle bisherigen Handler lösen
                foreach (var it in _attached)
                    it.PropertyChanged -= CombinedItem_PropertyChanged;
                _attached.Clear();

                _suppressBackProp = true;

                _allCombined = BuildCombinedItems(Left, Right);

                UpdateCombinedMetadataList();
            }
            finally
            {
                _suppressBackProp = false;
                _building = false;
            }
        }

        private List<CombinedMetadataItem> BuildCombinedItems(DicomFileViewModel left, DicomFileViewModel right)
        {
            // pro Tag genau ein Wert je Seite (erste Vorkommen)
            var leftBy = left?.MetadataList?.GroupBy(m => m.TagId).ToDictionary(g => g.Key, g => (val: g.First().Value, name: g.First().Name, vr: g.First().Vr))
                          ?? new Dictionary<string, (string val, string name, string vr)>();
            var rightBy = right?.MetadataList?.GroupBy(m => m.TagId).ToDictionary(g => g.Key, g => (val: g.First().Value, name: g.First().Name, vr: g.First().Vr))
                          ?? new Dictionary<string, (string val, string name, string vr)>();

            var tags = new SortedSet<string>(leftBy.Keys.Concat(rightBy.Keys), StringComparer.OrdinalIgnoreCase);

            var list = new List<CombinedMetadataItem>(tags.Count);
            foreach (var tag in tags)
            {
                leftBy.TryGetValue(tag, out var l);
                rightBy.TryGetValue(tag, out var r);

                var name = l.name ?? r.name ?? string.Empty;
                var vr = l.vr ?? r.vr ?? string.Empty;

                list.Add(new CombinedMetadataItem
                {
                    TagId = tag,
                    Name = name,
                    Vr = vr,
                    LeftValue = l.val ?? string.Empty,
                    RightValue = r.val ?? string.Empty
                });
            }
            return list;
        }

        private void UpdateCombinedMetadataList()
        {
            if (_updatingList) return;
            _updatingList = true;
            try
            {
                var view = ApplyFilterAndSort(_allCombined).ToList();

                // Handler: abklemmen, was nicht mehr sichtbar ist
                foreach (var it in _attached.Where(a => !view.Contains(a)).ToList())
                {
                    it.PropertyChanged -= CombinedItem_PropertyChanged;
                    _attached.Remove(it);
                }
                // Neue Sicht: anhängen, aber nur einmal
                foreach (var it in view)
                {
                    if (!_attached.Contains(it))
                    {
                        it.PropertyChanged += CombinedItem_PropertyChanged;
                        _attached.Add(it);
                    }
                }

                CombinedMetadataList.Clear();
                for (int i = 0; i < view.Count; i++)
                {
                    var it = view[i];
                    it.IsAlternate = (i % 2) == 1;
                    CombinedMetadataList.Add(it);
                }

                ApplyHighlight(CombinedMetadataList);
                ApplyInvalidHighlight(CombinedMetadataList);
            }
            finally
            {
                _updatingList = false;
            }
        }

        // Live-Row-Highlight
        private void ApplyRowHighlight(CombinedMetadataItem it)
        {
            it.IsHighlighted = HighlightDifferences && it.IsDifferent;
            it.LeftInvalidHighlighted = HighlightInvalidValues && it.IsLeftInvalid;
            it.RightInvalidHighlighted = HighlightInvalidValues && it.IsRightInvalid;
        }

        private void CombinedItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_building || _updatingList || _suppressBackProp) return;
            if (sender is not CombinedMetadataItem it) return;

            if (e.PropertyName == nameof(CombinedMetadataItem.LeftValue))
            {
                ApplyRowHighlight(it);
                UpdateUnderlyingMetadata(it, isLeft: true);
            }
            else if (e.PropertyName == nameof(CombinedMetadataItem.RightValue))
            {
                ApplyRowHighlight(it);
                UpdateUnderlyingMetadata(it, isLeft: false);
            }
            else if (e.PropertyName == nameof(CombinedMetadataItem.IsLeftInvalid) ||
                     e.PropertyName == nameof(CombinedMetadataItem.IsRightInvalid))
            {
                ApplyRowHighlight(it);
            }
        }

        private void UpdateUnderlyingMetadata(CombinedMetadataItem it, bool isLeft)
        {
            var list = isLeft ? Left?.MetadataList : Right?.MetadataList;
            if (list == null) return;

            // Suche nur nach TagId (Name/VR kommen ggf. von anderer Seite)
            var src = list.FirstOrDefault(m => string.Equals(m.TagId, it.TagId, StringComparison.OrdinalIgnoreCase));

            var newVal = isLeft ? it.LeftValue : it.RightValue;
            if (src == null)
            {
                // noch nicht vorhanden -> hinzufügen
                list.Add(new DicomMetadataItem
                {
                    TagId = it.TagId,
                    Name = it.Name,
                    Vr = it.Vr,
                    Value = newVal
                });
            }
            else if (!string.Equals(src.Value, newVal, StringComparison.Ordinal))
            {
                src.Value = newVal;
            }
        }

        // ------------------------------------------------------------
        // Highlights-Schalter
        // ------------------------------------------------------------
        private bool _highlightDifferences;
        public bool HighlightDifferences
        {
            get => _highlightDifferences;
            set
            {
                if (_highlightDifferences == value) return;
                _highlightDifferences = value;
                OnPropertyChanged();
                ApplyHighlight(CombinedMetadataList);
            }
        }

        private void ApplyHighlight(IEnumerable<CombinedMetadataItem> items)
        {
            bool on = HighlightDifferences;
            foreach (var it in items) it.IsHighlighted = on && it.IsDifferent;
        }

        private bool _highlightInvalidValues;
        public bool HighlightInvalidValues
        {
            get => _highlightInvalidValues;
            set
            {
                if (_highlightInvalidValues == value) return;
                _highlightInvalidValues = value;
                OnPropertyChanged();
                ApplyInvalidHighlight(CombinedMetadataList);
            }
        }

        private void ApplyInvalidHighlight(IEnumerable<CombinedMetadataItem> items)
        {
            bool on = HighlightInvalidValues;
            foreach (var it in items)
            {
                it.LeftInvalidHighlighted = on && it.IsLeftInvalid;
                it.RightInvalidHighlighted = on && it.IsRightInvalid;
            }
        }

        // ------------------------------------------------------------
        // Tag-Filterliste (wie gehabt)
        // ------------------------------------------------------------
        public ObservableCollection<TagFilterOption> AvailableTagFilters { get; } = new();
        public ObservableCollection<TagFilterOption> FilteredTagFilters { get; } = new();

        private string _tagSearchText = string.Empty;
        public string TagSearchText
        {
            get => _tagSearchText;
            set { if (_tagSearchText == value) return; _tagSearchText = value; OnPropertyChanged(); UpdateFilteredTagFilters(); }
        }

        private TagFilterOption? _selectedTagFilter;
        public TagFilterOption? SelectedTagFilter
        {
            get => _selectedTagFilter;
            set
            {
                if (_selectedTagFilter == value) return;
                if (_selectedTagFilter != null) _selectedTagFilter.IsSelected = false;
                _selectedTagFilter = value;
                if (_selectedTagFilter != null) _selectedTagFilter.IsSelected = true;
                OnPropertyChanged();
                UpdateCombinedMetadataList();
            }
        }

        private void UpdateFilteredTagFilters()
        {
            var search = TagSearchText?.Trim() ?? string.Empty;
            IEnumerable<TagFilterOption> q = AvailableTagFilters;
            if (!string.IsNullOrWhiteSpace(search))
            {
                q = q.Where(t =>
                    t.TagId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            FilteredTagFilters.Clear();
            foreach (var t in q.OrderBy(t => t.TagId))
                FilteredTagFilters.Add(t);
        }

        // ------------------------------------------------------------
        // Fenster-Titel (optional)
        // ------------------------------------------------------------
        private void UpdateWindowTitle()
        {
            // no-op on mobile; implement on WinUI/Mac falls gewünscht
        }

        // ------------------------------------------------------------
        // Commands & UI
        // ------------------------------------------------------------
        public ICommand LoadLeftCommand { get; }
        public ICommand LoadRightCommand { get; }
        public ICommand ApplyFilterCommand { get; }
        public ICommand SortCommand { get; }
        public ICommand ClearTagFilterCommand { get; }
        public ICommand OpenAddTagPopupCommand { get; }

        // ---- UI-Support: Picker-Optionen ----
        public IReadOnlyList<string> SexOptions { get; } = new[] { "M", "F", "N", "O", "U" };

        // ---- UI-Support: dynamische Spaltenbreiten (werden via StarConverter gebunden) ----
        private double _tagWidth = 1;
        public double TagWidth
        {
            get => _tagWidth;
            set { if (_tagWidth == value) return; _tagWidth = value; OnPropertyChanged(); }
        }

        private double _nameWidth = 2;
        public double NameWidth
        {
            get => _nameWidth;
            set { if (_nameWidth == value) return; _nameWidth = value; OnPropertyChanged(); }
        }

        private double _leftWidth = 3;
        public double LeftWidth
        {
            get => _leftWidth;
            set { if (_leftWidth == value) return; _leftWidth = value; OnPropertyChanged(); }
        }

        private double _rightWidth = 3;
        public double RightWidth
        {
            get => _rightWidth;
            set { if (_rightWidth == value) return; _rightWidth = value; OnPropertyChanged(); }
        }


        // Filter-Textbox oben links
        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set { if (_filterText == value) return; _filterText = value; OnPropertyChanged(); }
        }

        // Auswahl
        private CombinedMetadataItem? _selectedCombinedMetadataItem;
        public CombinedMetadataItem? SelectedCombinedMetadataItem
        {
            get => _selectedCombinedMetadataItem;
            set { if (_selectedCombinedMetadataItem == value) return; _selectedCombinedMetadataItem = value; OnPropertyChanged(); }
        }

        // Dummy – Platzhalter für evtl. Anzeige
        public string MetadataDifferences => string.Empty;

        // ------------------------------------------------------------
        // INotifyPropertyChanged
        // ------------------------------------------------------------
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ------------------------------------------------------------
        // Add-Tag-Popup (Platzhalter, deine existierende Logik weiterverwenden)
        // ------------------------------------------------------------
        private Task OpenAddTagPopupAsync(Page host) => Task.CompletedTask;
    }
}
