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
using DViewer.Infrastructure;
using CommunityToolkit.Maui.Views;

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
            ClearFilterCommand = new ParameterCommand(_ =>
            {
                FilterText = string.Empty;
                SelectedTagFilter = null;
                ShowOnlyDifferences = false;
                ShowOnlyInvalid = false;
                RaiseFilterChanged();
            });

            SortCommand = new ParameterCommand(p => { if (p is string s) ApplySort(s); });

            LeftPrevFrameCommand = new DelegateCommand(async () => { StepLeftFrame(-1); await Task.CompletedTask; }, () => LeftHasMultiFrame && LeftCanPrev);
            LeftNextFrameCommand = new DelegateCommand(async () => { StepLeftFrame(+1); await Task.CompletedTask; }, () => LeftHasMultiFrame && LeftCanNext);
            RightPrevFrameCommand = new DelegateCommand(async () => { StepRightFrame(-1); await Task.CompletedTask; }, () => RightHasMultiFrame && RightCanPrev);
            RightNextFrameCommand = new DelegateCommand(async () => { StepRightFrame(+1); await Task.CompletedTask; }, () => RightHasMultiFrame && RightCanNext);

            LeftPlayPauseCommand = new ParameterCommand(_ => ToggleLeftPlayback());
            RightPlayPauseCommand = new ParameterCommand(_ => ToggleRightPlayback());
        }

        // --- Multiframe/Video (Links/ Rechts) ---
        private int _leftFrameIndex;
        private int _rightFrameIndex;
        private IDispatcherTimer? _leftTimer;
        private IDispatcherTimer? _rightTimer;

        // Commands für Frames
        public DelegateCommand LeftPrevFrameCommand { get; }
        public DelegateCommand LeftNextFrameCommand { get; }
        public DelegateCommand RightPrevFrameCommand { get; }
        public DelegateCommand RightNextFrameCommand { get; }

        // Play/Pause (Timer für Multiframe – nicht für echtes Video)
        public ParameterCommand LeftPlayPauseCommand { get; }
        public ParameterCommand RightPlayPauseCommand { get; }


        // Video-Flags/Quellen
        public bool LeftHasVideo => !string.IsNullOrEmpty(Left?.VideoPath);
        public bool RightHasVideo => !string.IsNullOrEmpty(Right?.VideoPath);
        public string? LeftVideoPath => Left?.VideoPath;
        public string? RightVideoPath => Right?.VideoPath;

        // Multiframe-Infos
        public int LeftFrameCount => Left?.FrameCount ?? 0;
        public int RightFrameCount => Right?.FrameCount ?? 0;
        public bool LeftHasMultiFrame => !LeftHasVideo && LeftFrameCount > 1;
        public bool RightHasMultiFrame => !RightHasVideo && RightFrameCount > 1;

        public MediaSource? LeftVideoSource => string.IsNullOrEmpty(Left?.VideoPath) ? null : MediaSource.FromFile(Left.VideoPath);
        public MediaSource? RightVideoSource => string.IsNullOrEmpty(Right?.VideoPath) ? null : MediaSource.FromFile(Right.VideoPath);

        // Aktueller Frameindex (mit Clamp + Anzeige-Update)
        public int LeftFrameIndex
        {
            get => _leftFrameIndex;
            set => SetLeftFrame(value);
        }
        public int RightFrameIndex
        {
            get => _rightFrameIndex;
            set => SetRightFrame(value);
        }

        // UI-Helfer für Buttons
        public bool LeftIsPlaying => _leftTimer?.IsRunning == true;
        public bool RightIsPlaying => _rightTimer?.IsRunning == true;
        public bool LeftCanPrev => LeftHasMultiFrame && LeftFrameIndex > 0;
        public bool LeftCanNext => LeftHasMultiFrame && LeftFrameIndex < LeftFrameCount - 1;
        public bool RightCanPrev => RightHasMultiFrame && RightFrameIndex > 0;
        public bool RightCanNext => RightHasMultiFrame && RightFrameIndex < RightFrameCount - 1;


        private void SetLeftFrame(int idx)
        {
            var count = LeftFrameCount;
            idx = (count <= 0) ? 0 : Math.Max(0, Math.Min(idx, count - 1));
            if (_leftFrameIndex == idx) return;

            _leftFrameIndex = idx;
            UpdateLeftDisplayedFrame();

            OnPropertyChanged(nameof(LeftFrameIndex));
            OnPropertyChanged(nameof(LeftCanPrev));
            OnPropertyChanged(nameof(LeftCanNext));
            RaiseFrameCommandsCanExecute();
        }

        private void SetRightFrame(int idx)
        {
            var count = RightFrameCount;
            idx = (count <= 0) ? 0 : Math.Max(0, Math.Min(idx, count - 1));
            if (_rightFrameIndex == idx) return;

            _rightFrameIndex = idx;
            UpdateRightDisplayedFrame();

            OnPropertyChanged(nameof(RightFrameIndex));
            OnPropertyChanged(nameof(RightCanPrev));
            OnPropertyChanged(nameof(RightCanNext));
            RaiseFrameCommandsCanExecute();
        }

        private void StepLeftFrame(int delta) => SetLeftFrame(LeftFrameIndex + delta);
        private void StepRightFrame(int delta) => SetRightFrame(RightFrameIndex + delta);

        private void UpdateLeftDisplayedFrame()
        {
            if (Left == null || LeftHasVideo) return;         // Video zeigt MediaElement
            if (LeftFrameCount <= 0) return;
            try
            {
                var src = Left.GetFrameImageSource?.Invoke(_leftFrameIndex);
                if (src != null)
                {
                    Left.Image = src;
                    OnPropertyChanged(nameof(Left));
                }
            }
            catch { /* still */ }
        }

        private void UpdateRightDisplayedFrame()
        {
            if (Right == null || RightHasVideo) return;
            if (RightFrameCount <= 0) return;
            try
            {
                var src = Right.GetFrameImageSource?.Invoke(_rightFrameIndex);
                if (src != null)
                {
                    Right.Image = src;
                    OnPropertyChanged(nameof(Right));
                }
            }
            catch { /* still */ }
        }


        private void ToggleLeftPlayback()
        {
            if (!LeftHasMultiFrame) return;

            if (_leftTimer == null)
            {
                _leftTimer = Application.Current?.Dispatcher.CreateTimer();
                _leftTimer!.Interval = TimeSpan.FromMilliseconds(100); // ~10 fps
                _leftTimer.Tick += (_, __) =>
                {
                    if (LeftFrameCount <= 0) return;
                    var n = LeftFrameIndex + 1;
                    if (n >= LeftFrameCount) n = 0;
                    SetLeftFrame(n);
                };
            }

            if (_leftTimer.IsRunning) _leftTimer.Stop();
            else _leftTimer.Start();

            OnPropertyChanged(nameof(LeftIsPlaying));
        }

        private void ToggleRightPlayback()
        {
            if (!RightHasMultiFrame) return;

            if (_rightTimer == null)
            {
                _rightTimer = Application.Current?.Dispatcher.CreateTimer();
                _rightTimer!.Interval = TimeSpan.FromMilliseconds(100);
                _rightTimer.Tick += (_, __) =>
                {
                    if (RightFrameCount <= 0) return;
                    var n = RightFrameIndex + 1;
                    if (n >= RightFrameCount) n = 0;
                    SetRightFrame(n);
                };
            }

            if (_rightTimer.IsRunning) _rightTimer.Stop();
            else _rightTimer.Start();

            OnPropertyChanged(nameof(RightIsPlaying));
        }



        private void ResetMediaStateForSide(DicomFileViewModel? vm, bool left)
        {
            if (left)
            {
                _leftTimer?.Stop();
                _leftFrameIndex = 0;
                UpdateLeftDisplayedFrame(); // zeigt Frame 0 (bei Multiframe)
            }
            else
            {
                _rightTimer?.Stop();
                _rightFrameIndex = 0;
                UpdateRightDisplayedFrame();
            }
        }

        private void RaiseMediaChanged(bool left)
        {
            if (left)
            {
                OnPropertyChanged(nameof(LeftHasVideo));
                OnPropertyChanged(nameof(LeftVideoPath));
                OnPropertyChanged(nameof(LeftVideoSource));   // <-- NEU
                OnPropertyChanged(nameof(LeftFrameCount));
                OnPropertyChanged(nameof(LeftHasMultiFrame));
                OnPropertyChanged(nameof(LeftFrameIndex));
                OnPropertyChanged(nameof(LeftIsPlaying));
                OnPropertyChanged(nameof(LeftCanPrev));
                OnPropertyChanged(nameof(LeftCanNext));
                RaiseFrameCommandsCanExecute();
            }
            else
            {
                OnPropertyChanged(nameof(RightHasVideo));
                OnPropertyChanged(nameof(RightVideoPath));
                OnPropertyChanged(nameof(RightVideoSource));   // <-- NEU
                OnPropertyChanged(nameof(RightFrameCount));
                OnPropertyChanged(nameof(RightHasMultiFrame));
                OnPropertyChanged(nameof(RightFrameIndex));
                OnPropertyChanged(nameof(RightIsPlaying));
                OnPropertyChanged(nameof(RightCanPrev));
                OnPropertyChanged(nameof(RightCanNext));
                RaiseFrameCommandsCanExecute();
            }
        }

        private void RaiseFrameCommandsCanExecute()
        {
            LeftPrevFrameCommand.RaiseCanExecuteChanged();
            LeftNextFrameCommand.RaiseCanExecuteChanged();
            RightPrevFrameCommand.RaiseCanExecuteChanged();
            RightNextFrameCommand.RaiseCanExecuteChanged();
        }

        // ---------- kleine Hilfen ----------
        private static string S(object? v) => v?.ToString() ?? string.Empty;

        private static string GetTag(DicomFileViewModel? vm, string tagId)
            => vm?.Metadata?.FirstOrDefault(m => string.Equals(m.TagId, tagId, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;

        private static string GetAny(DicomFileViewModel? vm, params string[] tagIds)
        {
            if (vm?.Metadata == null) return string.Empty;
            foreach (var t in tagIds)
            {
                var v = vm.Metadata.FirstOrDefault(m => string.Equals(m.TagId, t, StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return string.Empty;
        }

        private static string FormatDate(string da)
        {
            // erwartet yyyyMMdd -> dd.MM.yyyy
            if (string.IsNullOrWhiteSpace(da)) return string.Empty;
            var s = new string(da.Where(char.IsDigit).ToArray());
            if (s.Length >= 8 &&
                int.TryParse(s.Substring(0, 4), out var y) &&
                int.TryParse(s.Substring(4, 2), out var m) &&
                int.TryParse(s.Substring(6, 2), out var d))
            {
                return $"{d:00}.{m:00}.{y:0000}";
            }
            return da;
        }

        private static string FormatTime(string tm)
        {
            // erwartet HHmmss[.fff...] -> HH:MM:SS
            if (string.IsNullOrWhiteSpace(tm)) return string.Empty;
            var s = new string(tm.Where(char.IsDigit).ToArray());
            if (s.Length >= 4)
            {
                var hh = s.Substring(0, 2);
                var mm = s.Substring(2, 2);
                var ss = s.Length >= 6 ? s.Substring(4, 2) : "00";
                return $"{hh}:{mm}:{ss}";
            }
            return tm;
        }

        private static string ComposeSpecies(DicomFileViewModel? vm)
        {
            // DICOM Vet (häufig):
            // (0010,2201) Patient Species Description
            // (0010,2292) Patient Breed Description  (manchmal 2202 je nach Hersteller)
            var species = GetAny(vm, "(0010,2201)");
            var breed = GetAny(vm, "(0010,2292)", "(0010,2202)");
            if (!string.IsNullOrWhiteSpace(species) && !string.IsNullOrWhiteSpace(breed))
                return $"{species} ({breed})";
            return species ?? string.Empty;
        }

        private static string ComposeUidSummary(DicomFileViewModel? vm)
        {
            var list = new List<string>();
            void Add(string tag)
            {
                var v = GetTag(vm, tag);
                if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
            }
            Add("(0008,0016)"); // SOP Class UID
            Add("(0008,0018)"); // SOP Instance UID
            Add("(0020,000D)"); // Study Instance UID
            Add("(0020,000E)"); // Series Instance UID
            return string.Join(" | ", list);
        }

        private static string NameWithSex(DicomFileViewModel? vm)
        {
            var name = GetTag(vm, "(0010,0010)");
            var sex = GetTag(vm, "(0010,0040)").Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(sex)) return $"{name} ({sex})";
            return name;
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

            using (UIUpdateGuard.Begin())
            {
                if (isLeft) Left = vm; else Right = vm;
                //OnPropertyChanged(nameof(Left));
                //OnPropertyChanged(nameof(Right));

                // Nur die geänderte Seite notifyen ist sauberer, beides geht aber auch.
                OnPropertyChanged(isLeft ? nameof(Left) : nameof(Right));


                RaiseOverlayChanged();
                RebuildCombined();
                RaiseMediaChanged(isLeft);
                ResetMediaStateForSide(vm, isLeft);
            }
        }

        // NEU: gezielt auf eine Seite laden (für Dateiverknüpfung/externen Open)
        public async Task OpenFileToSideAsync(string path, bool toLeft, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var vm = await _loader.LoadAsync(path, ct).ConfigureAwait(false);

            MainThread.BeginInvokeOnMainThread(() =>
            {

                using (UIUpdateGuard.Begin())
                {
                    if (toLeft)
                    {
                        Left = vm;
                        OnPropertyChanged(nameof(Left));
                    }
                    else
                    {
                        Right = vm;
                        OnPropertyChanged(nameof(Right));
                    }

                    RaiseOverlayChanged();
                    RebuildCombined();

                    // ❗️WICHTIG: richtige Seite angeben
                    RaiseMediaChanged(toLeft);
                    ResetMediaStateForSide(vm, toLeft);
                }


            });
        }

        // (weiterhin verfügbar, falls extern irgendwo verwendet)
        public async Task HandleExternalOpenAsync(string path, bool preferLeftIfEmpty = true, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var vm = await _loader.LoadAsync(path, ct).ConfigureAwait(false);

            bool changedLeft;
            if (preferLeftIfEmpty && (Left?.Metadata == null || Left.Metadata.Count == 0))
            {
                Left = vm; changedLeft = true;
            }
            else if (Right?.Metadata == null || Right.Metadata.Count == 0)
            {
                Right = vm; changedLeft = false;
            }
            else
            {
                Right = vm; changedLeft = false;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                using (UIUpdateGuard.Begin())
                {
                    OnPropertyChanged(changedLeft ? nameof(Left) : nameof(Right));
                    RaiseOverlayChanged();
                    RebuildCombined();

                    // ❗️WICHTIG: richtige Seite angeben
                    RaiseMediaChanged(changedLeft);
                    ResetMediaStateForSide(vm, changedLeft);
                }
            });
        }


        // ---------- Overlay-Properties ----------
        // Links
        public string LeftPatientNameWithSex => NameWithSex(Left);
        public string LeftSpecies => ComposeSpecies(Left);
        public string LeftPatientID => GetTag(Left, "(0010,0020)");
        public string LeftBirthDateDisplay => FormatDate(GetTag(Left, "(0010,0030)"));
        public string LeftOtherPid => GetTag(Left, "(0010,1000)");
        public string LeftStudyDateDisplay => FormatDate(GetTag(Left, "(0008,0020)"));
        public string LeftStudyTimeDisplay => FormatTime(GetTag(Left, "(0008,0030)"));
        public string LeftUidSummary => ComposeUidSummary(Left);

        // Rechts
        public string RightPatientNameWithSex => NameWithSex(Right);
        public string RightSpecies => ComposeSpecies(Right);
        public string RightPatientID => GetTag(Right, "(0010,0020)");
        public string RightBirthDateDisplay => FormatDate(GetTag(Right, "(0010,0030)"));
        public string RightOtherPid => GetTag(Right, "(0010,1000)");
        public string RightStudyDateDisplay => FormatDate(GetTag(Right, "(0008,0020)"));
        public string RightStudyTimeDisplay => FormatTime(GetTag(Right, "(0008,0030)"));
        public string RightUidSummary => ComposeUidSummary(Right);

        private void RaiseOverlayChanged()
        {
            OnPropertyChanged(nameof(LeftPatientNameWithSex));
            OnPropertyChanged(nameof(LeftSpecies));
            OnPropertyChanged(nameof(LeftPatientID));
            OnPropertyChanged(nameof(LeftBirthDateDisplay));
            OnPropertyChanged(nameof(LeftOtherPid));
            OnPropertyChanged(nameof(LeftStudyDateDisplay));
            OnPropertyChanged(nameof(LeftStudyTimeDisplay));
            OnPropertyChanged(nameof(LeftUidSummary));

            OnPropertyChanged(nameof(RightPatientNameWithSex));
            OnPropertyChanged(nameof(RightSpecies));
            OnPropertyChanged(nameof(RightPatientID));
            OnPropertyChanged(nameof(RightBirthDateDisplay));
            OnPropertyChanged(nameof(RightOtherPid));
            OnPropertyChanged(nameof(RightStudyDateDisplay));
            OnPropertyChanged(nameof(RightStudyTimeDisplay));
            OnPropertyChanged(nameof(RightUidSummary));
        }

        // ---------- Stammliste ----------
        public ObservableCollection<CombinedMetadataItem> CombinedMetadataList { get; } = new();

        private CombinedMetadataItem? _selected;
        public CombinedMetadataItem? SelectedCombinedMetadataItem
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value; OnPropertyChanged(); }
        }

        // ---------- Gefilterte Sicht ----------
        private IReadOnlyList<CombinedMetadataItem> _filteredItems = Array.Empty<CombinedMetadataItem>();
        public IReadOnlyList<CombinedMetadataItem> FilteredItems
        {
            get => _filteredItems;
            private set { _filteredItems = value; OnPropertyChanged(); }
        }

        // Kompatibilität
        public IEnumerable<CombinedMetadataItem> FilteredMetadata => FilteredItems;

        // ---------- Tag-Filter Liste ----------
        public sealed class TagFilterItem : INotifyPropertyChanged
        {
            public string TagId { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string Display => $"{TagId} - {Name}";

            private bool _isSelected;
            public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); } }

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
            set { if (_tagSearchText == value) return; _tagSearchText = value; OnPropertyChanged(); RefreshTagFilterList(); }
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

        private void RefreshTagFilterList()
        {
            using (UIUpdateGuard.Begin())
            {
                FilteredTagFilters.Clear();

                IEnumerable<TagFilterItem> q = _allTags;
                var f = (TagSearchText ?? string.Empty).Trim();
                if (f.Length > 0)
                {
                    var tokens = f.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    q = q.Where(i => tokens.All(t =>
                        (i.TagId?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.Name?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)));
                }

                foreach (var it in q.Take(250))
                    FilteredTagFilters.Add(it);

                OnPropertyChanged(nameof(FilteredTagFilters));
            }
        }

        // ---------- Combine (Stammliste aufbauen) ----------
        private bool _suppressRowChanged;
        private bool _building;

        private void RebuildCombined()
        {
            using (UIUpdateGuard.Begin())
            {
                _building = true;
                try
                {
                    var leftMeta = (IEnumerable<DicomMetadataItem>)(Left?.Metadata ?? Array.Empty<DicomMetadataItem>());
                    var rightMeta = (IEnumerable<DicomMetadataItem>)(Right?.Metadata ?? Array.Empty<DicomMetadataItem>());

                    var leftMap = leftMeta.ToDictionary(m => m.TagId, StringComparer.OrdinalIgnoreCase);
                    var rightMap = rightMeta.ToDictionary(m => m.TagId, StringComparer.OrdinalIgnoreCase);

                    _suppressRowChanged = true;
                    try
                    {
                        CombinedMetadataList.Clear();

                        var allTagIds = leftMap.Keys
                            .Union(rightMap.Keys, StringComparer.OrdinalIgnoreCase)
                            .OrderBy<string, string>(t => t, StringComparer.OrdinalIgnoreCase);

                        foreach (var tagId in allTagIds)
                        {
                            leftMap.TryGetValue(tagId, out var l);
                            rightMap.TryGetValue(tagId, out var r);

                            var name = l?.Name ?? r?.Name ?? string.Empty;
                            var vr = l?.Vr ?? r?.Vr ?? string.Empty;

                            CombinedMetadataItem row = new()
                            {
                                TagId = tagId,
                                Name = name,
                                Vr = vr,
                                LeftValue = l?.Value ?? string.Empty,
                                RightValue = r?.Value ?? string.Empty
                            };

                            CombinedMetadataList.Add(row);
                        }

                        ApplyRowHighlights(CombinedMetadataList);
                        ApplyInvalidHighlight(CombinedMetadataList);
                    }
                    finally { _suppressRowChanged = false; }

                    // Tag-Liste links
                    _allTags.Clear();
                    foreach (var g in CombinedMetadataList
                        .GroupBy(r => r.TagId, StringComparer.OrdinalIgnoreCase)
                        .OrderBy<IGrouping<string, CombinedMetadataItem>, string>(g => g.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        _allTags.Add(new TagFilterItem
                        {
                            TagId = g.Key,
                            Name = g.FirstOrDefault()?.Name ?? string.Empty
                        });
                    }
                    RefreshTagFilterList();

                    RecomputeFilteredItems();
                }
                finally { _building = false; }
            }
        }

        private string _sortCol = "TagId";
        private bool _asc = true;

        public string TagHeaderText => _sortCol == "TagId" ? (_asc ? "Tag ▲" : "Tag ▼") : "Tag";
        public string NameHeaderText => _sortCol == "Name" ? (_asc ? "Name ▲" : "Name ▼") : "Name";
        public string LeftHeaderText => _sortCol == "LeftValue" ? (_asc ? "Links ▲" : "Links ▼") : "Links";
        public string RightHeaderText => _sortCol == "RightValue" ? (_asc ? "Rechts ▲" : "Rechts ▼") : "Rechts";

        public ParameterCommand SortCommand { get; }
        private void ApplySort(string col)
        {
            if (string.Equals(col, _sortCol, StringComparison.OrdinalIgnoreCase))
                _asc = !_asc;
            else { _sortCol = col; _asc = true; }

            OnPropertyChanged(nameof(TagHeaderText));
            OnPropertyChanged(nameof(NameHeaderText));
            OnPropertyChanged(nameof(LeftHeaderText));
            OnPropertyChanged(nameof(RightHeaderText));

            RaiseFilterChanged();
        }

        public ParameterCommand ApplyFilterCommand { get; }
        public ParameterCommand ClearFilterCommand { get; }

        private string? _filterText;
        public string? FilterText
        {
            get => _filterText;
            set { if (_filterText == value) return; _filterText = value; OnPropertyChanged(); RaiseFilterChanged(); }
        }

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

        // ---------- Ansicht neu berechnen ----------
        private void RecomputeFilteredItems()
        {
            IEnumerable<CombinedMetadataItem> q = CombinedMetadataList;

            var f = (FilterText ?? string.Empty).Trim();
            if (f.Length > 0)
            {
                var tokens = f.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                q = q.Where(i => tokens.All(t =>
                    (i.TagId?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (i.Name?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    S(i.LeftValue).Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    S(i.RightValue).Contains(t, StringComparison.OrdinalIgnoreCase)));
            }

            if (SelectedTagFilter != null)
                q = q.Where(i => string.Equals(i.TagId, SelectedTagFilter.TagId, StringComparison.OrdinalIgnoreCase));

            if (ShowOnlyDifferences) q = q.Where(i => i.IsDifferent);
            if (ShowOnlyInvalid) q = q.Where(i => i.IsLeftInvalid || i.IsRightInvalid);

            q = _sortCol switch
            {
                "TagId" => _asc
                                ? q.OrderBy<CombinedMetadataItem, string>(i => i.TagId, StringComparer.OrdinalIgnoreCase)
                                : q.OrderByDescending<CombinedMetadataItem, string>(i => i.TagId, StringComparer.OrdinalIgnoreCase),
                "Name" => _asc
                                ? q.OrderBy<CombinedMetadataItem, string>(i => i.Name, StringComparer.OrdinalIgnoreCase)
                                : q.OrderByDescending<CombinedMetadataItem, string>(i => i.Name, StringComparer.OrdinalIgnoreCase),
                "LeftValue" => _asc
                                ? q.OrderBy<CombinedMetadataItem, string>(i => S(i.LeftValue), StringComparer.OrdinalIgnoreCase)
                                : q.OrderByDescending<CombinedMetadataItem, string>(i => S(i.LeftValue), StringComparer.OrdinalIgnoreCase),
                "RightValue" => _asc
                                ? q.OrderBy<CombinedMetadataItem, string>(i => S(i.RightValue), StringComparer.OrdinalIgnoreCase)
                                : q.OrderByDescending<CombinedMetadataItem, string>(i => S(i.RightValue), StringComparer.OrdinalIgnoreCase),
                _ => q
            };

            var list = q.ToList();

            _suppressRowChanged = true;
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var it = list[i];
                    it.IsAlternate = (i % 2) == 1;
                    it.IsHighlighted = HighlightDifferences && it.IsDifferent;
                    it.LeftInvalidHighlighted = HighlightInvalidValues && it.IsLeftInvalid;
                    it.RightInvalidHighlighted = HighlightInvalidValues && it.IsRightInvalid;
                }
            }
            finally { _suppressRowChanged = false; }

            FilteredItems = list;
        }

        private static readonly HashSet<string> s_filterRelevant = new(StringComparer.Ordinal)
        {
            nameof(CombinedMetadataItem.TagId),
            nameof(CombinedMetadataItem.Name),
            nameof(CombinedMetadataItem.LeftValue),
            nameof(CombinedMetadataItem.RightValue),
            nameof(CombinedMetadataItem.IsLeftInvalid),
            nameof(CombinedMetadataItem.IsRightInvalid),
            nameof(CombinedMetadataItem.IsDifferent),
        };

        // ---- Robust: Handler-Management, auch bei Reset ----
        private readonly HashSet<CombinedMetadataItem> _subscribedRows = new();

        private void CombinedChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var it in _subscribedRows)
                    it.PropertyChanged -= RowChanged;
                _subscribedRows.Clear();

                foreach (var it in CombinedMetadataList)
                {
                    it.PropertyChanged += RowChanged;
                    _subscribedRows.Add(it);
                }
                return;
            }

            if (e.OldItems != null)
                foreach (CombinedMetadataItem it in e.OldItems)
                    if (_subscribedRows.Remove(it))
                        it.PropertyChanged -= RowChanged;

            if (e.NewItems != null)
                foreach (CombinedMetadataItem it in e.NewItems)
                    if (_subscribedRows.Add(it))
                        it.PropertyChanged += RowChanged;
        }

        private void RowChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressRowChanged) return;

            if (sender is CombinedMetadataItem row)
            {
                if (e.PropertyName is nameof(CombinedMetadataItem.LeftValue)
                                    or nameof(CombinedMetadataItem.IsLeftInvalid))
                {
                    row.LeftInvalidHighlighted = HighlightInvalidValues && row.IsLeftInvalid;
                    row.IsHighlighted = HighlightDifferences && row.IsDifferent;
                }
                else if (e.PropertyName is nameof(CombinedMetadataItem.RightValue)
                                       or nameof(CombinedMetadataItem.IsRightInvalid))
                {
                    row.RightInvalidHighlighted = HighlightInvalidValues && row.IsRightInvalid;
                    row.IsHighlighted = HighlightDifferences && row.IsDifferent;
                }
            }

            if (e.PropertyName != null && s_filterRelevant.Contains(e.PropertyName))
                RaiseFilterChanged();
        }

        private void ApplyRowHighlights(IEnumerable<CombinedMetadataItem> items)
        {
            _suppressRowChanged = true;
            try
            {
                foreach (var it in items)
                    it.IsHighlighted = HighlightDifferences && it.IsDifferent;
            }
            finally { _suppressRowChanged = false; }
        }

        private void ApplyInvalidHighlight(IEnumerable<CombinedMetadataItem> items)
        {
            _suppressRowChanged = true;
            try
            {
                foreach (var it in items)
                {
                    it.LeftInvalidHighlighted = HighlightInvalidValues && it.IsLeftInvalid;
                    it.RightInvalidHighlighted = HighlightInvalidValues && it.IsRightInvalid;
                }
            }
            finally { _suppressRowChanged = false; }
        }

        // ---------- Defer & Coalesce ----------
        private CancellationTokenSource? _uiUpdateCts;
        private void RaiseFilterChanged()
        {
            _uiUpdateCts?.Cancel();
            var cts = new CancellationTokenSource();
            _uiUpdateCts = cts;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await Task.Yield();
                    await Task.Delay(10, cts.Token);
                    if (cts.IsCancellationRequested) return;

                    using (UIUpdateGuard.Begin())
                    {
                        RecomputeFilteredItems();
                    }
                }
                catch { /* still */ }
                finally
                {
                    if (ReferenceEquals(_uiUpdateCts, cts))
                        _uiUpdateCts = null;
                }
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

        // ---------- UI Flags ----------
        private bool _highlightDifferences = true;
        public bool HighlightDifferences
        {
            get => _highlightDifferences;
            set { if (_highlightDifferences == value) return; _highlightDifferences = value; OnPropertyChanged(); RecomputeFilteredItems(); }
        }

        private bool _highlightInvalidValues = true;
        public bool HighlightInvalidValues
        {
            get => _highlightInvalidValues;
            set { if (_highlightInvalidValues == value) return; _highlightInvalidValues = value; OnPropertyChanged(); RecomputeFilteredItems(); }
        }

        // ---------- Spaltenbreiten ----------
        private double _tagWidth = 1;
        public double TagWidth { get => _tagWidth; set { if (Math.Abs(_tagWidth - value) < double.Epsilon) return; _tagWidth = value; OnPropertyChanged(); } }

        private double _nameWidth = 2;
        public double NameWidth { get => _nameWidth; set { if (Math.Abs(_nameWidth - value) < double.Epsilon) return; _nameWidth = value; OnPropertyChanged(); } }

        private double _leftWidth = 2;
        public double LeftWidth { get => _leftWidth; set { if (Math.Abs(_leftWidth - value) < double.Epsilon) return; _leftWidth = value; OnPropertyChanged(); } }

        private double _rightWidth = 2;
        public double RightWidth { get => _rightWidth; set { if (Math.Abs(_rightWidth - value) < double.Epsilon) return; _rightWidth = value; OnPropertyChanged(); } }

        // ---------- SexOptions ----------
        private static readonly string[] s_sexOptions = new[] { "", "M", "F", "O" };
        public IReadOnlyList<string> SexOptions => s_sexOptions;

        // ---------- MISSING TAG hinzufügen (NEU) ----------
        //public void AddMissingTagToSide(DicomTagCandidate cand, bool toLeft)
        //{
        //    if (cand == null) return;

        //    var list = toLeft ? Left?.Metadata : Right?.Metadata;
        //    if (list == null) return;

        //    var tagId = cand.TagId?.Trim();
        //    if (string.IsNullOrWhiteSpace(tagId)) return;

        //    // nichts doppelt
        //    if (list.Any(m => string.Equals(m.TagId, tagId, StringComparison.OrdinalIgnoreCase)))
        //        return;

        //    var vr = cand.Vr ?? string.Empty; // Property-Name aus deiner bestehenden Klasse
        //    var name = cand.Name ?? tagId;

        //    list.Add(new DicomMetadataItem
        //    {
        //        TagId = tagId,
        //        Name = name,
        //        Vr = vr,
        //        Value = string.Empty
        //    });

        //    // kombinierten View refreshen
        //    RebuildCombined();
        //}
    }

 
}
