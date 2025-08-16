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

            ApplyFilterCommand = new ParameterCommand(_ => RaiseFilterChanged());
            ClearFilterCommand = new ParameterCommand(_ => { FilterText = string.Empty; RaiseFilterChanged(); });

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

        // optional: externes Öffnen
        public async Task HandleExternalOpenAsync(string path, bool preferLeftIfEmpty = true, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var vm = await _loader.LoadAsync(path, ct).ConfigureAwait(false);
            if (preferLeftIfEmpty && (Left?.Metadata == null || Left.Metadata.Count == 0))
                Left = vm;
            else if (Right?.Metadata == null || Right.Metadata.Count == 0)
                Right = vm;
            else
                Right = vm;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(Left));
                OnPropertyChanged(nameof(Right));
                RebuildCombined();
            });
        }

        // ---------- Kombinierte Liste (Quelle) ----------
        public ObservableCollection<CombinedMetadataItem> CombinedMetadataList { get; } = new();

        private CombinedMetadataItem? _selected;
        public CombinedMetadataItem? SelectedCombinedMetadataItem
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; OnPropertyChanged(); }
        }

        // ---------- Gefilterte Ansicht für das CollectionView ----------
        public IEnumerable<CombinedMetadataItem> FilteredMetadata
        {
            get
            {
                IEnumerable<CombinedMetadataItem> q = CombinedMetadataList;

                if (ShowOnlyDifferences) q = q.Where(i => i.IsDifferent);
                if (ShowOnlyInvalid) q = q.Where(i => i.IsLeftInvalid || i.IsRightInvalid);

                if (SelectedTagFilter != null)
                    q = q.Where(i => string.Equals(i.TagId, SelectedTagFilter.TagId, StringComparison.OrdinalIgnoreCase));

                var f = (FilterText ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(f))
                {
                    var tokens = f.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    q = q.Where(i => tokens.All(t =>
                        (i.TagId?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.Name?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.LeftValue?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.RightValue?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)));
                }

                q = _sortCol switch
                {
                    "TagId" => _asc ? q.OrderBy(i => i.TagId, StringComparer.OrdinalIgnoreCase) : q.OrderByDescending(i => i.TagId, StringComparer.OrdinalIgnoreCase),
                    "Name" => _asc ? q.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase) : q.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
                    "LeftValue" => _asc ? q.OrderBy(i => i.LeftValue, StringComparer.OrdinalIgnoreCase) : q.OrderByDescending(i => i.LeftValue, StringComparer.OrdinalIgnoreCase),
                    "RightValue" => _asc ? q.OrderBy(i => i.RightValue, StringComparer.OrdinalIgnoreCase) : q.OrderByDescending(i => i.RightValue, StringComparer.OrdinalIgnoreCase),
                    _ => q
                };

                return q;
            }
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
                RaiseFilterChanged();
            }
        }

        private bool _showOnlyDiff;
        public bool ShowOnlyDifferences
        {
            get => _showOnlyDiff;
            set { if (_showOnlyDiff == value) return; _showOnlyDiff = value; OnPropertyChanged(); RaiseFilterChanged(); }
        }

        private bool _showOnlyInvalid;
        public bool ShowOnlyInvalid
        {
            get => _showOnlyInvalid;
            set { if (_showOnlyInvalid == value) return; _showOnlyInvalid = value; OnPropertyChanged(); RaiseFilterChanged(); }
        }

        // Toggle: Unterschiede (gelb)
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

        // Toggle: Ungültige (rot)
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
            RaiseFilterChanged();
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

        // ---------- Combine (ohne Überschreiben) ----------
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

            CombinedMetadataList.Clear();
            _allTags.Clear();

            foreach (var tag in union)
            {
                leftMap.TryGetValue(tag, out var l);
                rightMap.TryGetValue(tag, out var r);

                // Combined-Zeile referenziert Quellen semantisch
                var row = new CombinedMetadataItem(l, r)
                {
                    IsAlternate = (CombinedMetadataList.Count % 2) == 1
                };

                CombinedMetadataList.Add(row);

                var nm = l?.Name ?? r?.Name ?? string.Empty;
                _allTags.Add(new TagFilterItem { TagId = tag, Name = nm });
            }

            RefreshTagFilterList();
            ApplyRowHighlights(CombinedMetadataList);
            ApplyInvalidHighlight(CombinedMetadataList);

            // Ansicht aktualisieren
            RaiseFilterChanged();
        }

        private void CombinedChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RaiseFilterChanged();
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

        // koaleszierter Refresh für FilteredMetadata – ersetzt alle alten Aufrufe von RaiseFilterChanged (fehlte bei dir)
        private bool _refreshQueued;
        private void RaiseFilterChanged()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _refreshQueued = false;
                OnPropertyChanged(nameof(FilteredMetadata));
            });
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
