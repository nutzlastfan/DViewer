using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

namespace DViewer
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        private readonly DicomLoader _loader;

        public MainViewModel(DicomLoader loader)
        {
            _loader = loader;

            CombinedMetadataList.CollectionChanged += OnCombinedCollectionChanged;

            LoadLeftCommand = new DelegateCommand(async () => await LoadSideAsync(isLeft: true));
            LoadRightCommand = new DelegateCommand(async () => await LoadSideAsync(isLeft: false));

            ApplyFilterCommand = new ParameterCommand(_ => RaiseFilterChanged());
            ClearFilterCommand = new ParameterCommand(_ => { FilterText = string.Empty; RaiseFilterChanged(); });

            SortCommand = new ParameterCommand(p =>
            {
                if (p is string s) ApplySort(s);
            });
        }

        // ============== Seiten / Laden ==============

        public DicomFileViewModel Left { get; private set; } = new();
        public DicomFileViewModel Right { get; private set; } = new();

        public DelegateCommand LoadLeftCommand { get; }
        public DelegateCommand LoadRightCommand { get; }

        private async Task LoadSideAsync(bool isLeft, CancellationToken ct = default)
        {
            var vm = await _loader.PickAndLoadAsync(ct);
            if (vm == null) return;

            if (isLeft) Left = vm; else Right = vm;

            OnPropertyChanged(nameof(Left));
            OnPropertyChanged(nameof(Right));

            RebuildCombined(); // <- immer vollständig neu berechnen (kein Überschreiben)
        }

        // Wird von MainPage.xaml.cs genutzt für "Öffnen mit..."
        public async Task HandleExternalOpenAsync(string path, bool preferLeftIfEmpty = true, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var vm = await _loader.LoadAsync(path, ct).ConfigureAwait(false);

            if (preferLeftIfEmpty && (Left?.Metadata == null || Left.Metadata.Count == 0))
                Left = vm;
            else if (Right?.Metadata == null || Right.Metadata.Count == 0)
                Right = vm;
            else
                Right = vm; // Default: rechts ersetzen

            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(Left));
                OnPropertyChanged(nameof(Right));
                RebuildCombined();
            });
        }

        // ============== Kombinierte Liste ==============

        // Master (ungefiltert/unsortiert)
        private List<CombinedMetadataItem> _allCombined = new();

        // Anzeige-Liste (ItemsSource im UI)
        public ObservableCollection<CombinedMetadataItem> CombinedMetadataList { get; } = new();

        private CombinedMetadataItem? _selected;
        public CombinedMetadataItem? SelectedCombinedMetadataItem
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; OnPropertyChanged(); }
        }

        private void RebuildCombined()
        {
            // Alte Events sauber lösen
            foreach (var it in CombinedMetadataList)
                it.PropertyChanged -= OnRowPropertyChanged;

            CombinedMetadataList.Clear();
            _allCombined.Clear();

            var leftMeta = (IEnumerable<DicomMetadataItem>)(Left?.Metadata ?? Array.Empty<DicomMetadataItem>());
            var rightMeta = (IEnumerable<DicomMetadataItem>)(Right?.Metadata ?? Array.Empty<DicomMetadataItem>());

            // Tag-Menge bilden (Case-insensitive)
            var tags = leftMeta.Select(m => m.TagId)
                               .Concat(rightMeta.Select(m => m.TagId))
                               .Where(t => !string.IsNullOrEmpty(t))
                               .Distinct(StringComparer.OrdinalIgnoreCase)
                               .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                               .ToList();

            var leftMap = leftMeta.ToDictionary(m => m.TagId, StringComparer.OrdinalIgnoreCase);
            var rightMap = rightMeta.ToDictionary(m => m.TagId, StringComparer.OrdinalIgnoreCase);

            foreach (var tag in tags)
            {
                leftMap.TryGetValue(tag, out var l);
                rightMap.TryGetValue(tag, out var r);

                // NEUE Instanz je Tag – keine Referenzübernahme -> verhindert „Überschreiben“
                var row = new CombinedMetadataItem
                {
                    TagId = tag,
                    Name = l?.Name ?? r?.Name ?? string.Empty,
                    Vr = l?.Vr ?? r?.Vr ?? string.Empty,
                    // Werte explizit setzen
                    // (Setter in CombinedMetadataItem kümmern sich um Validierung/Dependents)
                };
                if (l != null) row.LeftValue = l.Value ?? string.Empty;
                if (r != null) row.RightValue = r.Value ?? string.Empty;

                // Initiale Highlights (gemäß aktuellen Toggles)
                row.IsHighlighted = HighlightDifferences && row.IsDifferent;
                row.LeftInvalidHighlighted = HighlightInvalidValues && row.IsLeftInvalid;
                row.RightInvalidHighlighted = HighlightInvalidValues && row.IsRightInvalid;

                row.PropertyChanged += OnRowPropertyChanged;
                _allCombined.Add(row);
            }

            // Zebra setzen anhand späterer Anzeige-Reihenfolge (passiert in UpdateCombinedMetadataList)
            UpdateCombinedMetadataList();

            // Tag-Filterliste rechts aktualisieren
            _allTags.Clear();
            foreach (var tag in tags)
            {
                var nm = (leftMap.TryGetValue(tag, out var l) ? l?.Name
                         : rightMap.TryGetValue(tag, out var r) ? r?.Name
                         : null) ?? string.Empty;
                _allTags.Add(new TagFilterItem { TagId = tag, Name = nm });
            }
            RefreshTagFilterList();
        }

        private void OnCombinedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (CombinedMetadataItem it in e.OldItems)
                    it.PropertyChanged -= OnRowPropertyChanged;

            if (e.NewItems != null)
                foreach (CombinedMetadataItem it in e.NewItems)
                    it.PropertyChanged += OnRowPropertyChanged;
        }

        // Row-Änderungen (Diff/Invalid/Filter-Recalc)
        private static readonly HashSet<string> s_filterRelevant = new(StringComparer.Ordinal)
        {
            nameof(CombinedMetadataItem.LeftValue),
            nameof(CombinedMetadataItem.RightValue),
            nameof(CombinedMetadataItem.IsLeftInvalid),
            nameof(CombinedMetadataItem.IsRightInvalid),
            nameof(CombinedMetadataItem.IsDifferent),
        };

        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is CombinedMetadataItem row)
            {
                if (e.PropertyName is nameof(CombinedMetadataItem.LeftValue) or nameof(CombinedMetadataItem.RightValue))
                {
                    // Diff aktualisieren
                    row.IsHighlighted = HighlightDifferences && row.IsDifferent;
                }

                if (e.PropertyName is nameof(CombinedMetadataItem.LeftValue) or nameof(CombinedMetadataItem.IsLeftInvalid))
                {
                    row.LeftInvalidHighlighted = HighlightInvalidValues && row.IsLeftInvalid;
                }

                if (e.PropertyName is nameof(CombinedMetadataItem.RightValue) or nameof(CombinedMetadataItem.IsRightInvalid))
                {
                    row.RightInvalidHighlighted = HighlightInvalidValues && row.IsRightInvalid;
                }
            }

            if (e.PropertyName != null && s_filterRelevant.Contains(e.PropertyName))
                RaiseFilterChanged();
        }

        // ============== Filter / Suche / Sortierung ==============

        // Textfilter
        private string? _filterText;
        public string? FilterText
        {
            get => _filterText;
            set
            {
                if (_filterText == value) return;
                _filterText = value;
                OnPropertyChanged();
                RaiseFilterChanged();
            }
        }

        // Nur Anzeige-Highlights (gelb) umschalten
        private bool _highlightDifferences;
        public bool HighlightDifferences
        {
            get => _highlightDifferences;
            set
            {
                if (_highlightDifferences == value) return;
                _highlightDifferences = value;
                OnPropertyChanged();
                ApplyRowHighlights(CombinedMetadataList);
            }
        }

        // Nur Anzeige-Highlights (rot) umschalten
        private bool _highlightInvalidValues;
        public bool HighlightInvalidValues
        {
            get => _highlightInvalidValues;
            set
            {
                if (_highlightInvalidValues == value) return;
                _highlightInvalidValues = value;
                OnPropertyChanged();
                ApplyInvalidHighlights(CombinedMetadataList);
            }
        }

        // Optional: Anzeige „nur Unterschiede“ / „nur Ungültige“
        private bool _showOnlyDifferences;
        public bool ShowOnlyDifferences
        {
            get => _showOnlyDifferences;
            set { if (_showOnlyDifferences == value) return; _showOnlyDifferences = value; OnPropertyChanged(); RaiseFilterChanged(); }
        }

        private bool _showOnlyInvalid;
        public bool ShowOnlyInvalid
        {
            get => _showOnlyInvalid;
            set { if (_showOnlyInvalid == value) return; _showOnlyInvalid = value; OnPropertyChanged(); RaiseFilterChanged(); }
        }

        public ParameterCommand ApplyFilterCommand { get; }
        public ParameterCommand ClearFilterCommand { get; }

        // Tag-Filter rechts
        public sealed class TagFilterItem : INotifyPropertyChanged
        {
            public string TagId { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string Display => $"{TagId} - {Name}";

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? n = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private readonly ObservableCollection<TagFilterItem> _allTags = new();
        public ObservableCollection<TagFilterItem> FilteredTagFilters { get; } = new();

        private string? _tagSearchText;
        public string? TagSearchText
        {
            get => _tagSearchText;
            set
            {
                if (_tagSearchText == value) return;
                _tagSearchText = value;
                OnPropertyChanged();
                RefreshTagFilterList();
            }
        }

        private TagFilterItem? _selectedTagFilter;
        public TagFilterItem? SelectedTagFilter
        {
            get => _selectedTagFilter;
            set
            {
                if (_selectedTagFilter == value) return;

                if (_selectedTagFilter != null) _selectedTagFilter.IsSelected = false;
                _selectedTagFilter = value;
                OnPropertyChanged();

                if (_selectedTagFilter != null) _selectedTagFilter.IsSelected = true;

                RaiseFilterChanged();
            }
        }

        public ParameterCommand ClearTagFilterCommand => new(_ =>
        {
            TagSearchText = string.Empty;
            if (_selectedTagFilter != null) _selectedTagFilter.IsSelected = false;
            _selectedTagFilter = null;
            RefreshTagFilterList();
            RaiseFilterChanged();
        });

        private void RefreshTagFilterList()
        {
            var txt = (TagSearchText ?? string.Empty).Trim();
            IEnumerable<TagFilterItem> q = _allTags;

            if (!string.IsNullOrEmpty(txt))
            {
                q = q.Where(t =>
                    t.TagId.Contains(txt, StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Contains(txt, StringComparison.OrdinalIgnoreCase));
            }

            FilteredTagFilters.Clear();
            foreach (var it in q.Take(200))
                FilteredTagFilters.Add(it);

            foreach (var it in FilteredTagFilters)
                it.IsSelected = _selectedTagFilter != null &&
                                string.Equals(it.TagId, _selectedTagFilter.TagId, StringComparison.OrdinalIgnoreCase);
        }

        // Sortierung
        public ParameterCommand SortCommand { get; }
        private string _currentSortColumn = "TagId";
        private bool _ascending = true;

        private void ApplySort(string column)
        {
            if (string.Equals(column, _currentSortColumn, StringComparison.OrdinalIgnoreCase))
                _ascending = !_ascending;
            else
            {
                _currentSortColumn = column;
                _ascending = true;
            }

            OnPropertyChanged(nameof(TagHeaderText));
            OnPropertyChanged(nameof(NameHeaderText));
            OnPropertyChanged(nameof(LeftHeaderText));
            OnPropertyChanged(nameof(RightHeaderText));

            RaiseFilterChanged();
        }

        public string TagHeaderText => _currentSortColumn == "TagId" ? (_ascending ? "Tag ▲" : "Tag ▼") : "Tag";
        public string NameHeaderText => _currentSortColumn == "Name" ? (_ascending ? "Name ▲" : "Name ▼") : "Name";
        public string LeftHeaderText => _currentSortColumn == "LeftValue" ? (_ascending ? "Links ▲" : "Links ▼") : "Links";
        public string RightHeaderText => _currentSortColumn == "RightValue" ? (_ascending ? "Rechts ▲" : "Rechts ▼") : "Rechts";

        // Spaltenbreiten (Star-Converter nutzt diese Doubles)
        private double _tagW = 1, _nameW = 1, _leftW = 2, _rightW = 2;
        public double TagWidth { get => _tagW; set { if (_tagW == value) return; _tagW = value; OnPropertyChanged(); } }
        public double NameWidth { get => _nameW; set { if (_nameW == value) return; _nameW = value; OnPropertyChanged(); } }
        public double LeftWidth { get => _leftW; set { if (_leftW == value) return; _leftW = value; OnPropertyChanged(); } }
        public double RightWidth { get => _rightW; set { if (_rightW == value) return; _rightW = value; OnPropertyChanged(); } }

        // Picker-Optionen
        public IReadOnlyList<string> SexOptions { get; } = new[] { "M", "F", "N", "O", "U" };

        // ============== Filter-Anwendung auf Anzeige-Liste ==============

        private bool _updateQueued;
        private void RaiseFilterChanged()
        {
            if (_updateQueued) return;
            _updateQueued = true;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _updateQueued = false;
                UpdateCombinedMetadataList();
            });
        }

        private bool _updatingList;
        private void UpdateCombinedMetadataList()
        {
            if (_updatingList) return;
            _updatingList = true;
            try
            {
                IEnumerable<CombinedMetadataItem> items = _allCombined;

                // Textfilter
                var f = (FilterText ?? string.Empty).Trim();
                if (f.Length > 0)
                {
                    var tokens = f.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    items = items.Where(i => tokens.All(t =>
                        (i.TagId?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.Name?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.LeftText?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.RightText?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)));
                }

                // Nur Unterschiede / nur Ungültige (optional)
                if (ShowOnlyDifferences)
                    items = items.Where(i => i.IsDifferent);
                if (ShowOnlyInvalid)
                    items = items.Where(i => i.IsLeftInvalid || i.IsRightInvalid);

                // Tag-Filter
                if (SelectedTagFilter != null && !string.IsNullOrEmpty(SelectedTagFilter.TagId))
                {
                    var tagId = SelectedTagFilter.TagId;
                    items = items.Where(i => string.Equals(i.TagId, tagId, StringComparison.OrdinalIgnoreCase));
                }

                // Sort
                items = _currentSortColumn switch
                {
                    "TagId" => _ascending
                        ? items.OrderBy(i => i.TagId, StringComparer.OrdinalIgnoreCase)
                        : items.OrderByDescending(i => i.TagId, StringComparer.OrdinalIgnoreCase),
                    "Name" => _ascending
                        ? items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                        : items.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
                    "LeftValue" => _ascending
                        ? items.OrderBy(i => i.LeftText, StringComparer.OrdinalIgnoreCase)
                        : items.OrderByDescending(i => i.LeftText, StringComparer.OrdinalIgnoreCase),
                    "RightValue" => _ascending
                        ? items.OrderBy(i => i.RightText, StringComparer.OrdinalIgnoreCase)
                        : items.OrderByDescending(i => i.RightText, StringComparer.OrdinalIgnoreCase),
                    _ => items
                };

                var list = items.ToList();

                // Zebra (anzeigereihenfolge-basiert)
                for (int i = 0; i < list.Count; i++)
                    list[i].IsAlternate = (i % 2) == 1;

                // Anzeige-Liste ersetzen (Events sauber managen)
                foreach (var it in CombinedMetadataList)
                    it.PropertyChanged -= OnRowPropertyChanged;

                CombinedMetadataList.Clear();
                foreach (var it in list)
                {
                    CombinedMetadataList.Add(it);
                    it.PropertyChanged -= OnRowPropertyChanged; // doppelt verhindern
                    it.PropertyChanged += OnRowPropertyChanged;
                }

                // Highlights anwenden
                ApplyRowHighlights(CombinedMetadataList);
                ApplyInvalidHighlights(CombinedMetadataList);

                OnPropertyChanged(nameof(CombinedMetadataList));
                OnPropertyChanged(nameof(TagHeaderText));
                OnPropertyChanged(nameof(NameHeaderText));
                OnPropertyChanged(nameof(LeftHeaderText));
                OnPropertyChanged(nameof(RightHeaderText));
            }
            finally
            {
                _updatingList = false;
            }
        }

        private void ApplyRowHighlights(IEnumerable<CombinedMetadataItem> rows)
        {
            foreach (var it in rows)
                it.IsHighlighted = HighlightDifferences && it.IsDifferent;
        }

        private void ApplyInvalidHighlights(IEnumerable<CombinedMetadataItem> rows)
        {
            foreach (var it in rows)
            {
                it.LeftInvalidHighlighted = HighlightInvalidValues && it.IsLeftInvalid;
                it.RightInvalidHighlighted = HighlightInvalidValues && it.IsRightInvalid;
            }
        }

        // ============== INotifyPropertyChanged ==============

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (MainThread.IsMainThread)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            else
                MainThread.BeginInvokeOnMainThread(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
        }
    }
}
