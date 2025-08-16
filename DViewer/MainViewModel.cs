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

            CombinedMetadataList.CollectionChanged += CombinedChanged;

            LoadLeftCommand = new DelegateCommand(async () => await LoadSideAsync(isLeft: true));
            LoadRightCommand = new DelegateCommand(async () => await LoadSideAsync(isLeft: false));

            ApplyFilterCommand = new ParameterCommand(_ => UpdateCombinedMetadataList());
            ClearFilterCommand = new ParameterCommand(_ =>
            {
                FilterText = string.Empty;
                UpdateCombinedMetadataList();
            });

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

        // Wird von MainPage.xaml.cs (App.PendingOpens etc.) genutzt
        public async Task HandleExternalOpenAsync(string path, bool preferLeftIfEmpty = true, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var vm = await _loader.LoadAsync(path, ct).ConfigureAwait(false);

            // Zu welcher Seite?
            if (preferLeftIfEmpty && (Left?.Metadata == null || Left.Metadata.Count == 0))
                Left = vm;
            else if (Right?.Metadata == null || Right.Metadata.Count == 0)
                Right = vm;
            else
                Right = vm; // standardmäßig rechts ersetzen

            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(Left));
                OnPropertyChanged(nameof(Right));
                RebuildCombined();
            });
        }

        // ---------- Kombinierte (anzeigbare) Liste ----------
        public ObservableCollection<CombinedMetadataItem> CombinedMetadataList { get; } = new();

        private CombinedMetadataItem? _selected;
        public CombinedMetadataItem? SelectedCombinedMetadataItem
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                OnPropertyChanged();

                // Auswahlflag in Rows setzen
                foreach (var it in CombinedMetadataList)
                    it.IsSelected = ReferenceEquals(it, _selected);
            }
        }

        // Basisliste (ungefiltert/unsortiert) für die Vergleichstabelle
        private readonly List<CombinedMetadataItem> _allCombined = new();

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
                UpdateCombinedMetadataList();
            }
        }

        private bool _showOnlyDiff;
        public bool ShowOnlyDifferences
        {
            get => _showOnlyDiff;
            set
            {
                if (_showOnlyDiff == value) return;
                _showOnlyDiff = value;
                OnPropertyChanged();
                UpdateCombinedMetadataList();
            }
        }

        private bool _showOnlyInvalid;
        public bool ShowOnlyInvalid
        {
            get => _showOnlyInvalid;
            set
            {
                if (_showOnlyInvalid == value) return;
                _showOnlyInvalid = value;
                OnPropertyChanged();
                UpdateCombinedMetadataList();
            }
        }

        // Toggle: Unterschiede hervorheben (gelb)
        private bool _highlightDifferences;
        public bool HighlightDifferences
        {
            get => _highlightDifferences;
            set
            {
                if (_highlightDifferences == value) return;
                _highlightDifferences = value;
                OnPropertyChanged();
                ApplyRowHighlights(CombinedMetadataList); // Markierungen neu setzen
            }
        }

        // Toggle: Ungültige Werte markieren (rot)
        private bool _highlightInvalidValues;
        public bool HighlightInvalidValues
        {
            get => _highlightInvalidValues;
            set
            {
                if (_highlightInvalidValues == value) return;
                _highlightInvalidValues = value;
                OnPropertyChanged();
                ApplyInvalidHighlight(CombinedMetadataList); // Markierungen neu setzen
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

                UpdateCombinedMetadataList(); // Filter sofort anwenden
            }
        }

        public ParameterCommand ClearTagFilterCommand => new(_ =>
        {
            TagSearchText = string.Empty;
            if (_selectedTagFilter != null) _selectedTagFilter.IsSelected = false;
            _selectedTagFilter = null;
            RefreshTagFilterList();
            UpdateCombinedMetadataList();
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

        // ---------- Sortierung ----------
        public ParameterCommand SortCommand { get; }
        private string _currentSortColumn = "TagId";
        private bool _ascending = true;

        private void ApplySort(string col)
        {
            if (string.Equals(col, _currentSortColumn, StringComparison.OrdinalIgnoreCase))
                _ascending = !_ascending;
            else
            {
                _currentSortColumn = col;
                _ascending = true;
            }
            OnPropertyChanged(nameof(TagHeaderText));
            OnPropertyChanged(nameof(NameHeaderText));
            OnPropertyChanged(nameof(LeftHeaderText));
            OnPropertyChanged(nameof(RightHeaderText));

            UpdateCombinedMetadataList();
        }

        public string TagHeaderText => _currentSortColumn == "TagId" ? (_ascending ? "Tag ▲" : "Tag ▼") : "Tag";
        public string NameHeaderText => _currentSortColumn == "Name" ? (_ascending ? "Name ▲" : "Name ▼") : "Name";
        public string LeftHeaderText => _currentSortColumn == "LeftValue" ? (_ascending ? "Links ▲" : "Links ▼") : "Links";
        public string RightHeaderText => _currentSortColumn == "RightValue" ? (_ascending ? "Rechts ▲" : "Rechts ▼") : "Rechts";

        // ---------- Spaltenbreiten ----------
        private double _tagW = 1, _nameW = 1, _leftW = 2, _rightW = 2;
        public double TagWidth { get => _tagW; set { if (_tagW == value) return; _tagW = value; OnPropertyChanged(); } }
        public double NameWidth { get => _nameW; set { if (_nameW == value) return; _nameW = value; OnPropertyChanged(); } }
        public double LeftWidth { get => _leftW; set { if (_leftW == value) return; _leftW = value; OnPropertyChanged(); } }
        public double RightWidth { get => _rightW; set { if (_rightW == value) return; _rightW = value; OnPropertyChanged(); } }

        // ---------- Picker-Optionen ----------
        public IReadOnlyList<string> SexOptions { get; } = new[] { "M", "F", "N", "O", "U" };

        // ---------- Aufbauen der Basismenge ----------
        private void RebuildCombined()
        {
            _allCombined.Clear();

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

            foreach (var tag in union)
            {
                leftMap.TryGetValue(tag, out var l);
                rightMap.TryGetValue(tag, out var r);

                var row = new CombinedMetadataItem
                {
                    TagId = tag,
                    Name = l?.Name ?? r?.Name ?? string.Empty,
                    Vr = l?.Vr ?? r?.Vr ?? string.Empty
                };

                // <<< wichtig: initiales Setzen OHNE Events
                row.SetInitialValues(l?.Value, r?.Value);

                // Highlights initial
                row.IsHighlighted = HighlightDifferences && row.IsDifferent;
                row.LeftInvalidHighlighted = HighlightInvalidValues && row.IsLeftInvalid;
                row.RightInvalidHighlighted = HighlightInvalidValues && row.IsRightInvalid;

                _allCombined.Add(row);
            }

            // Tag-Liste rechts aktualisieren
            _allTags.Clear();
            foreach (var tag in union)
            {
                var nm = (leftMap.TryGetValue(tag, out var l) ? l?.Name
                         : rightMap.TryGetValue(tag, out var r) ? r?.Name
                         : null) ?? string.Empty;
                _allTags.Add(new TagFilterItem { TagId = tag, Name = nm });
            }
            RefreshTagFilterList();

            UpdateCombinedMetadataList();
        }

        // ---------- Anzeige-Liste auf Basisliste anwenden ----------
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
                        (i.LeftValue?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.RightValue?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)));
                }

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

                // Sortierung
                items = _currentSortColumn switch
                {
                    "TagId" => _ascending ? items.OrderBy(i => i.TagId, StringComparer.OrdinalIgnoreCase) : items.OrderByDescending(i => i.TagId, StringComparer.OrdinalIgnoreCase),
                    "Name" => _ascending ? items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase) : items.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
                    "LeftValue" => _ascending ? items.OrderBy(i => i.LeftValue, StringComparer.OrdinalIgnoreCase) : items.OrderByDescending(i => i.LeftValue, StringComparer.OrdinalIgnoreCase),
                    "RightValue" => _ascending ? items.OrderBy(i => i.RightValue, StringComparer.OrdinalIgnoreCase) : items.OrderByDescending(i => i.RightValue, StringComparer.OrdinalIgnoreCase),
                    _ => items
                };

                var list = items.ToList();

                // Zebra
                for (int i = 0; i < list.Count; i++)
                    list[i].IsAlternate = (i % 2) == 1;

                // Alte Handler lösen
                foreach (var it in CombinedMetadataList)
                    it.PropertyChanged -= RowChanged;

                // Liste ersetzen
                CombinedMetadataList.Clear();
                foreach (var it in list)
                {
                    CombinedMetadataList.Add(it);
                    it.PropertyChanged -= RowChanged; // doppelte Verknüpfung vermeiden
                    it.PropertyChanged += RowChanged;
                }

                // Auswahl beibehalten (falls möglich)
                if (SelectedCombinedMetadataItem != null && !CombinedMetadataList.Contains(SelectedCombinedMetadataItem))
                    SelectedCombinedMetadataItem = null;

                // Highlights anwenden
                ApplyRowHighlights(CombinedMetadataList);
                ApplyInvalidHighlight(CombinedMetadataList);

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

        private void CombinedChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (CombinedMetadataItem it in e.OldItems)
                    it.PropertyChanged -= RowChanged;

            if (e.NewItems != null)
                foreach (CombinedMetadataItem it in e.NewItems)
                    it.PropertyChanged += RowChanged;
        }

        private void RowChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is CombinedMetadataItem row)
            {
                // Live-Highlights bei Änderungen an Werten/Validität
                if (e.PropertyName is nameof(CombinedMetadataItem.LeftValue)
                    or nameof(CombinedMetadataItem.RightValue)
                    or nameof(CombinedMetadataItem.IsLeftInvalid)
                    or nameof(CombinedMetadataItem.IsRightInvalid)
                    or nameof(CombinedMetadataItem.IsDifferent))
                {
                    row.IsHighlighted = HighlightDifferences && row.IsDifferent;
                    row.LeftInvalidHighlighted = HighlightInvalidValues && row.IsLeftInvalid;
                    row.RightInvalidHighlighted = HighlightInvalidValues && row.IsRightInvalid;
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
                MainThread.BeginInvokeOnMainThread(() =>
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
        }
    }
}
