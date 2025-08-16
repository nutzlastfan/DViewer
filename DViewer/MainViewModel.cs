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

            // CollectionView-Quelle (wird von UpdateCombinedMetadataList() befüllt)
            CombinedMetadataList.CollectionChanged += CombinedChanged;

            LoadLeftCommand = new DelegateCommand(async () => await LoadSideAsync(isLeft: true));
            LoadRightCommand = new DelegateCommand(async () => await LoadSideAsync(isLeft: false));

            ApplyFilterCommand = new ParameterCommand(_ => QueueRecalc());
            ClearFilterCommand = new ParameterCommand(_ => { FilterText = string.Empty; QueueRecalc(); });

            SortCommand = new ParameterCommand(p =>
            {
                if (p is string s) ApplySort(s);
            });
        }

        // ---------- Seiten ----------
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

            RebuildCombined();
        }

        // Wird von MainPage.xaml.cs aufgerufen (Datei via Shell/“Öffnen mit”)
        public async Task HandleExternalOpenAsync(string path, bool preferLeftIfEmpty = true, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var vm = await _loader.LoadAsync(path, ct).ConfigureAwait(false);
            if (preferLeftIfEmpty && (Left?.Metadata == null || Left.Metadata.Count == 0))
                Left = vm;
            else if (Right?.Metadata == null || Right.Metadata.Count == 0)
                Right = vm;
            else
                Right = vm; // default: rechts ersetzen

            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(Left));
                OnPropertyChanged(nameof(Right));
                RebuildCombined();
            });
        }

        // ---------- Kombinierte Liste ----------
        // Unveränderte Basisliste (ungefiltert/unsortiert)
        private List<CombinedMetadataItem> _allCombined = new();

        // View für die CollectionView (gefiltert/sortiert aus _allCombined)
        public ObservableCollection<CombinedMetadataItem> CombinedMetadataList { get; } = new();

        private CombinedMetadataItem? _selected;
        public CombinedMetadataItem? SelectedCombinedMetadataItem
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; OnPropertyChanged(); }
        }

        // ---------- Filter ----------
        private string? _filterText;
        public string? FilterText
        {
            get => _filterText;
            set
            {
                if (_filterText == value) return;
                _filterText = value;
                OnPropertyChanged();
                QueueRecalc();   // Filter sofort anwenden (koalesziert)
            }
        }

        private bool _showOnlyDiff;
        public bool ShowOnlyDifferences
        {
            get => _showOnlyDiff;
            set { if (_showOnlyDiff == value) return; _showOnlyDiff = value; OnPropertyChanged(); QueueRecalc(); }
        }

        private bool _showOnlyInvalid;
        public bool ShowOnlyInvalid
        {
            get => _showOnlyInvalid;
            set { if (_showOnlyInvalid == value) return; _showOnlyInvalid = value; OnPropertyChanged(); QueueRecalc(); }
        }

        // Toggles: visuelle Highlights (werden zusätzlich zur Filterung gesetzt)
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

        public ParameterCommand ApplyFilterCommand { get; }
        public ParameterCommand ClearFilterCommand { get; }

        // ---------- Tag-Filter rechts ----------
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

        private TagFilterItem? _selectedTag; // für Anzeigezustand (blau hinterlegt)
        private TagFilterItem? _selectedTagFilter; // tatsächlicher Filter
        public TagFilterItem? SelectedTagFilter
        {
            get => _selectedTagFilter;
            set
            {
                if (_selectedTagFilter == value) return;

                if (_selectedTagFilter != null) _selectedTagFilter.IsSelected = false;
                _selectedTagFilter = value;
                _selectedTag = value;
                OnPropertyChanged();

                if (_selectedTagFilter != null) _selectedTagFilter.IsSelected = true;

                RefreshTagFilterList();
                QueueRecalc();   // Filter sofort anwenden (koalesziert)
            }
        }

        public ParameterCommand ClearTagFilterCommand => new(_ =>
        {
            TagSearchText = string.Empty;
            if (_selectedTag != null) _selectedTag.IsSelected = false;
            _selectedTag = null;
            _selectedTagFilter = null;
            RefreshTagFilterList();
            QueueRecalc();
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

            // Sichtbare Liste mit Auswahl synchronisieren
            foreach (var it in FilteredTagFilters)
                it.IsSelected = _selectedTag != null &&
                                string.Equals(it.TagId, _selectedTag.TagId, StringComparison.OrdinalIgnoreCase );
        }

        // ---------- Sortierung ----------
        public ParameterCommand SortCommand { get; }
        private string _sortCol = "TagId";
        private bool _asc = true;

        private void ApplySort(string col)
        {
            if (string.Equals(col, _sortCol, StringComparison.OrdinalIgnoreCase))
                _asc = !_asc;
            else
            {
                _sortCol = col;
                _asc = true;
            }
            OnPropertyChanged(nameof(TagHeaderText));
            OnPropertyChanged(nameof(NameHeaderText));
            OnPropertyChanged(nameof(LeftHeaderText));
            OnPropertyChanged(nameof(RightHeaderText));
            UpdateCombinedMetadataList();
        }

        public string TagHeaderText => _sortCol == "TagId" ? (_asc ? "Tag ▲" : "Tag ▼") : "Tag";
        public string NameHeaderText => _sortCol == "Name" ? (_asc ? "Name ▲" : "Name ▼") : "Name";
        public string LeftHeaderText => _sortCol == "LeftValue" ? (_asc ? "Links ▲" : "Links ▼") : "Links";
        public string RightHeaderText => _sortCol == "RightValue" ? (_asc ? "Rechts ▲" : "Rechts ▼") : "Rechts";

        // ---------- Spaltenbreiten ----------
        private double _tagW = 1, _nameW = 1, _leftW = 2, _rightW = 2;
        public double TagWidth { get => _tagW; set { if (_tagW == value) return; _tagW = value; OnPropertyChanged(); } }
        public double NameWidth { get => _nameW; set { if (_nameW == value) return; _nameW = value; OnPropertyChanged(); } }
        public double LeftWidth { get => _leftW; set { if (_leftW == value) return; _leftW = value; OnPropertyChanged(); } }
        public double RightWidth { get => _rightW; set { if (_rightW == value) return; _rightW = value; OnPropertyChanged(); } }

        // ---------- Picker-Optionen ----------
        public IReadOnlyList<string> SexOptions { get; } = new[] { "M", "F", "N", "O", "U" };

        // ---------- Combine ----------
        private void RebuildCombined()
        {
            var leftMeta = (IEnumerable<DicomMetadataItem>)(Left?.Metadata ?? Array.Empty<DicomMetadataItem>());
            var rightMeta = (IEnumerable<DicomMetadataItem>)(Right?.Metadata ?? Array.Empty<DicomMetadataItem>());

            var union = leftMeta.Select(m => m.TagId)
                                .Concat(rightMeta.Select(m => m.TagId))
                                .Where(t => !string.IsNullOrEmpty(t))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                                .ToList();

            var leftMap = leftMeta.ToDictionary(m => m.TagId, StringComparer.OrdinalIgnoreCase);
            var rightMap = rightMeta.ToDictionary(m => m.TagId, StringComparer.OrdinalIgnoreCase);

            // Basisliste neu aufbauen (ohne Filter/Sort)
            var basis = new List<CombinedMetadataItem>(union.Count);
            foreach (var tag in union)
            {
                leftMap.TryGetValue(tag, out var l);
                rightMap.TryGetValue(tag, out var r);

                var item = new CombinedMetadataItem
                {
                    TagId = tag,
                    Name = l?.Name ?? r?.Name ?? string.Empty,
                    Vr = l?.Vr ?? r?.Vr ?? string.Empty,
                };

                if (l != null) item.LeftValue = l.Value ?? string.Empty;
                if (r != null) item.RightValue = r.Value ?? string.Empty;

                // Anfangszustände für "nur invalid zeigen"
                item.LeftInvalidHighlighted = ShowOnlyInvalid && item.IsLeftInvalid;
                item.RightInvalidHighlighted = ShowOnlyInvalid && item.IsRightInvalid;

                basis.Add(item);
            }

            // Zebra (Basis, wird später für View neu gesetzt)
            for (int i = 0; i < basis.Count; i++)
                basis[i].IsAlternate = (i % 2) == 1;

            // Tag-Liste rechts
            _allTags.Clear();
            foreach (var tag in union)
            {
                var nm = (leftMap.TryGetValue(tag, out var l) ? l?.Name
                        : rightMap.TryGetValue(tag, out var r) ? r?.Name
                        : null) ?? string.Empty;

                _allTags.Add(new TagFilterItem { TagId = tag, Name = nm });
            }
            RefreshTagFilterList();

            // Basisliste übernehmen und View berechnen
            _allCombined = basis;
            UpdateCombinedMetadataList();
        }

        // Rekonfiguration der View (Filter + Sort) koaleszieren
        private bool _recalcQueued;
        private void QueueRecalc()
        {
            if (_recalcQueued) return;
            _recalcQueued = true;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _recalcQueued = false;
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
                IEnumerable<CombinedMetadataItem> items = _allCombined ?? Enumerable.Empty<CombinedMetadataItem>();

                // Textfilter
                var f = (FilterText ?? string.Empty).Trim();
                if (f.Length > 0)
                {
                    var tokens = f.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    items = items.Where(i => tokens.All(t =>
                        (i.TagId?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.Name?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.LeftValue?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.RightValue?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)));
                }

                // Tag-spezifischer Filter
                if (_selectedTagFilter != null && !string.IsNullOrEmpty(_selectedTagFilter.TagId))
                {
                    var tagId = _selectedTagFilter.TagId;
                    items = items.Where(i => string.Equals(i.TagId, tagId, StringComparison.OrdinalIgnoreCase));
                }

                // Nur Unterschiede
                if (ShowOnlyDifferences)
                    items = items.Where(i => i.IsDifferent);

                // Nur ungültige
                if (ShowOnlyInvalid)
                    items = items.Where(i => i.IsLeftInvalid || i.IsRightInvalid);

                // Sortierung
                items = _sortCol switch
                {
                    "TagId" => _asc ? items.OrderBy(i => i.TagId, StringComparer.OrdinalIgnoreCase)
                                         : items.OrderByDescending(i => i.TagId, StringComparer.OrdinalIgnoreCase),
                    "Name" => _asc ? items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                                         : items.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
                    "LeftValue" => _asc ? items.OrderBy(i => i.LeftValue, StringComparer.OrdinalIgnoreCase)
                                         : items.OrderByDescending(i => i.LeftValue, StringComparer.OrdinalIgnoreCase),
                    "RightValue" => _asc ? items.OrderBy(i => i.RightValue, StringComparer.OrdinalIgnoreCase)
                                         : items.OrderByDescending(i => i.RightValue, StringComparer.OrdinalIgnoreCase),
                    _ => items
                };

                var list = items.ToList();

                // Zebra neu setzen basierend auf gefilterter Sicht
                for (int i = 0; i < list.Count; i++)
                    list[i].IsAlternate = (i % 2) == 1;

                // Alte Items -> RowChanged abmelden (wird auch in CombinedChanged erledigt,
                // aber Clear() liefert ein Reset-Event; wir entkoppeln explizit)
                foreach (var it in CombinedMetadataList)
                    it.PropertyChanged -= RowChanged;

                CombinedMetadataList.Clear();

                foreach (var it in list)
                    CombinedMetadataList.Add(it); // CombinedChanged hängt RowChanged wieder an

                // Sicht-spezifische Highlights setzen
                ApplyRowHighlights(CombinedMetadataList);
                ApplyInvalidHighlight(CombinedMetadataList);

                // Headertexte evtl. neu berechnet (Pfeile)
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

        private void CombinedChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (CombinedMetadataItem it in e.OldItems)
                    it.PropertyChanged -= RowChanged;

            if (e.NewItems != null)
                foreach (CombinedMetadataItem it in e.NewItems)
                    it.PropertyChanged += RowChanged;
        }

        private static readonly HashSet<string> s_filterRelevant = new(StringComparer.Ordinal)
        {
            nameof(CombinedMetadataItem.LeftValue),
            nameof(CombinedMetadataItem.RightValue),
            nameof(CombinedMetadataItem.IsLeftInvalid),
            nameof(CombinedMetadataItem.IsRightInvalid),
            nameof(CombinedMetadataItem.IsDifferent),
        };

        private void RowChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is CombinedMetadataItem row)
            {
                if (e.PropertyName == nameof(CombinedMetadataItem.LeftValue) ||
                    e.PropertyName == nameof(CombinedMetadataItem.IsLeftInvalid))
                    row.LeftInvalidHighlighted = HighlightInvalidValues && row.IsLeftInvalid;

                if (e.PropertyName == nameof(CombinedMetadataItem.RightValue) ||
                    e.PropertyName == nameof(CombinedMetadataItem.IsRightInvalid))
                    row.RightInvalidHighlighted = HighlightInvalidValues && row.IsRightInvalid;

                // Wenn Filter auf Unterschied/Invalid aktiv sind, kann sich Sicht ändern
                if (ShowOnlyDifferences || ShowOnlyInvalid)
                {
                    if (e.PropertyName != null && s_filterRelevant.Contains(e.PropertyName))
                        QueueRecalc();
                }
                else
                {
                    // ansonsten reichen die Highlight-Updates
                }
            }
        }

        private void ApplyRowHighlights(IEnumerable<CombinedMetadataItem> items)
        {
            foreach (var it in items)
                it.IsHighlighted = HighlightDifferences && it.IsDifferent;
        }

        private void ApplyInvalidHighlight(IEnumerable<CombinedMetadataItem> items)
        {
            foreach (var it in items)
            {
                it.LeftInvalidHighlighted = HighlightInvalidValues && it.IsLeftInvalid;
                it.RightInvalidHighlighted = HighlightInvalidValues && it.IsRightInvalid;
            }
        }

        // ---------- INotify ----------
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
