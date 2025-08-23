using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace DViewer
{
    public partial class MainPage : ContentPage
    {
        private readonly SettingsViewModel _settingsVm = new();
        private MainViewModel? VM => BindingContext as MainViewModel;

        // Vollbild-Flags (Overlay)
        private bool _leftFullscreenVisible;
        public bool LeftFullscreenVisible
        {
            get => _leftFullscreenVisible;
            set
            {
                if (_leftFullscreenVisible == value) return;
                _leftFullscreenVisible = value;
                OnPropertyChanged(nameof(LeftFullscreenVisible));
            }
        }

        private bool _rightFullscreenVisible;
        public bool RightFullscreenVisible
        {
            get => _rightFullscreenVisible;
            set
            {
                if (_rightFullscreenVisible == value) return;
                _rightFullscreenVisible = value;
                OnPropertyChanged(nameof(RightFullscreenVisible));
            }
        }

        public QueryRetrieveTabViewModel QueryVM { get; } = new();
        public WorklistTabViewModel WorklistVM { get; } = new();

        // einmalige Initialisierung beim Anzeigen
        private bool _initialized;

        // --- DI-Konstruktor ---
        public MainPage(MainViewModel vm)
        {
            InitializeComponent();
            BindingContext = vm;
            App.MainVM = vm;

            // Settings-Panel an ViewModel binden
            if (SettingsRoot != null)
                SettingsRoot.BindingContext = _settingsVm;

            WireFullscreenEvents();
            _ = ProcessPendingOpensAsync();
        }

        // --- Fallback-Konstruktor (ohne DI) ---
        public MainPage()
        {
            InitializeComponent();

            if (BindingContext is null)
            {
                var loader = new DicomLoader();
                var vm = new MainViewModel(loader);
                BindingContext = vm;
                App.MainVM = vm;
            }

            if (SettingsRoot != null)
                SettingsRoot.BindingContext = _settingsVm;

            WireFullscreenEvents();
            _ = ProcessPendingOpensAsync();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Datei-Opens weiter abarbeiten
            _ = ProcessPendingOpensAsync();

            // Initiales Laden (einmalig)
            if (!_initialized)
            {
                _initialized = true;
                await EnsureSettingsLoadedAndBindAsync();
            }
        }

        private async Task EnsureSettingsLoadedAndBindAsync()
        {
            // 1) Settings einlesen (inkl. Listen) -> füllt _settingsVm
            await _settingsVm.LoadAsync();

            // 2) Nodes auch in die Tab-VMs spiegeln (aus Persistenz)
            var store = AppSettingsStore.Instance; // wurde eben in LoadAsync() befüllt
            QueryVM.LoadNodesFromSettings(() => store.Settings);
            WorklistVM.LoadNodesFromSettings(() => store.Settings);
        }

        private void WireFullscreenEvents()
        {
            // kleine Viewer -> Overlay toggeln
            LeftViewer.FullscreenRequested += (_, __) => LeftFullscreenVisible = true;
            RightViewer.FullscreenRequested += (_, __) => RightFullscreenVisible = true;

            // Fullscreen-Viewer -> Overlay schließen
            LeftViewerFull.FullscreenRequested += (_, __) => LeftFullscreenVisible = false;
            RightViewerFull.FullscreenRequested += (_, __) => RightFullscreenVisible = false;
        }

        // ----------------- Externe Öffnungen (Dateiverknüpfung) -----------------
        private static bool _processingOpens;
        private async Task ProcessPendingOpensAsync()
        {
            if (_processingOpens || VM is null) return;
            _processingOpens = true;
            try
            {
                while (App.PendingOpens.TryDequeue(out var path))
                    await AskSideAndOpenAsync(path);
            }
            finally
            {
                _processingOpens = false;
            }
        }

        private async Task AskSideAndOpenAsync(string path)
        {
            if (VM is null || string.IsNullOrWhiteSpace(path)) return;

            var file = System.IO.Path.GetFileName(path);
            var choice = await DisplayActionSheet(
                $"Datei öffnen: {file}",
                "Abbrechen",
                null,
                "Links ersetzen",
                "Rechts ersetzen");

            if (choice == "Links ersetzen")
                await VM.OpenFileToSideAsync(path, toLeft: true);
            else if (choice == "Rechts ersetzen")
                await VM.OpenFileToSideAsync(path, toLeft: false);
        }

        // ----------------- UI-Handler -----------------
        private void OnOpenLeftClicked(object? sender, EventArgs e)
        {
            if (VM?.LoadLeftCommand is { } cmd && cmd.CanExecute(null))
                cmd.Execute(null);
        }

        private void OnOpenRightClicked(object? sender, EventArgs e)
        {
            if (VM?.LoadRightCommand is { } cmd && cmd.CanExecute(null))
                cmd.Execute(null);
        }

        private void OnSortClicked(object? sender, EventArgs e)
        {
            if (VM?.SortCommand is null) return;
            if (sender is Button btn && btn.CommandParameter is string col && VM.SortCommand.CanExecute(col))
                VM.SortCommand.Execute(col);
        }

        private void OnTagFilterSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (VM is null) return;
            var selected = e.CurrentSelection?.FirstOrDefault();
            VM.SelectedTagFilter = selected as MainViewModel.TagFilterItem;
        }

        private void OnClearTagFilterClicked(object? sender, EventArgs e)
        {
            if (VM is null) return;
            VM.SelectedTagFilter = null;
        }

        private void OnCombinedSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (VM is null) return;
            var selected = e.CurrentSelection?.FirstOrDefault();
            VM.SelectedCombinedMetadataItem = selected as CombinedMetadataItem;
        }

        private void OnCloseLeftFullscreen(object? sender, EventArgs e) => LeftFullscreenVisible = false;
        private void OnCloseRightFullscreen(object? sender, EventArgs e) => RightFullscreenVisible = false;

        private async void OnAddTagLeftOverlayClicked(object? sender, EventArgs e)
            => await DisplayAlert("Tag hinzufügen", "Neuen DICOM-Tag für LINKS hinzufügen (Verdrahtung folgt).", "OK");

        private async void OnAddTagRightOverlayClicked(object? sender, EventArgs e)
            => await DisplayAlert("Tag hinzufügen", "Neuen DICOM-Tag für RECHTS hinzufügen (Verdrahtung folgt).", "OK");

        private void OnSaveLeftClicked(object sender, EventArgs e) { /* TODO */ }
        private void OnSaveRightClicked(object sender, EventArgs e) { /* TODO */ }

        private async void OnAddDicomTagClicked(object? sender, EventArgs e)
        {
            if (VM is null) return;
            var side = await DisplayActionSheet("Tag wohin hinzufügen?", "Abbrechen", null, "Links", "Rechts");
            if (side is not ("Links" or "Rechts")) return;
            bool toLeft = side == "Links";
            await DisplayAlert("Hinweis", "Verdrahtung mit dem vorhandenen Tag-Dialog kann ich sofort ergänzen, sobald klar ist, wie das Ergebnis übergeben wird (Event/Task/Messaging).", "OK");
        }

        // Query/Retrieve & MWL Tab
        private async void OnQuerySearch(object? s, EventArgs e) => await QueryVM.SearchAsync();
        private async void OnQueryRetrieve(object? s, EventArgs e) => await QueryVM.RetrieveSelectedAsync();
        private async void OnMwlSearch(object? s, EventArgs e) => await WorklistVM.SearchAsync();

        // ------- Buttons: Lokaler Knoten -------
        private async void OnBrowseStorageClicked(object sender, EventArgs e)
            => await _settingsVm.BrowseStorageAsync(this);

        private async void OnTestLocalDicomClicked(object sender, EventArgs e)
            => await _settingsVm.TestLocalScpAsync(this);

        private async void OnSaveLocalDicomClicked(object sender, EventArgs e)
            => await _settingsVm.SaveAsync();

        // ------- SEND-Nodes -------
        private async void OnAddSendNodeClicked(object s, EventArgs e) => await _settingsVm.AddNodeInteractiveAsync(this, NodeKind.Send);
        private async void OnEditSendNodeClicked(object s, EventArgs e) => await _settingsVm.EditSelectedNodeInteractiveAsync(this, NodeKind.Send);
        private void OnRemoveSendNodeClicked(object s, EventArgs e) => _settingsVm.RemoveSelectedNode(NodeKind.Send);
        private async void OnTestSendNodeClicked(object s, EventArgs e) => await _settingsVm.TestSelectedNodeAsync(this, NodeKind.Send);

        // ------- MWL-Nodes -------
        private async void OnAddMwNodeClicked(object s, EventArgs e) => await _settingsVm.AddNodeInteractiveAsync(this, NodeKind.Worklist);
        private async void OnEditMwNodeClicked(object s, EventArgs e) => await _settingsVm.EditSelectedNodeInteractiveAsync(this, NodeKind.Worklist);
        private void OnRemoveMwNodeClicked(object s, EventArgs e) => _settingsVm.RemoveSelectedNode(NodeKind.Worklist);
        private async void OnTestMwNodeClicked(object s, EventArgs e) => await _settingsVm.TestSelectedNodeAsync(this, NodeKind.Worklist);

        // ------- Q/R-Nodes -------
        private async void OnAddQrNodeClicked(object s, EventArgs e) => await _settingsVm.AddNodeInteractiveAsync(this, NodeKind.QueryRetrieve);
        private async void OnEditQrNodeClicked(object s, EventArgs e) => await _settingsVm.EditSelectedNodeInteractiveAsync(this, NodeKind.QueryRetrieve);
        private void OnRemoveQrNodeClicked(object s, EventArgs e) => _settingsVm.RemoveSelectedNode(NodeKind.QueryRetrieve);
        private async void OnTestQrNodeClicked(object s, EventArgs e) => await _settingsVm.TestSelectedNodeAsync(this, NodeKind.QueryRetrieve);
    }
}
