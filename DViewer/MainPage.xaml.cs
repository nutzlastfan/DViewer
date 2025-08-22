using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace DViewer
{
    public partial class MainPage : ContentPage
    {

        readonly SettingsViewModel _settingsVm = new();
        private MainViewModel? VM => BindingContext as MainViewModel;

        // -------- Vollbild-Flags (für Overlay) --------
        private bool _leftFullscreenVisible;
        public bool LeftFullscreenVisible
        {
            get => _leftFullscreenVisible;
            set { if (_leftFullscreenVisible == value) return; _leftFullscreenVisible = value; OnPropertyChanged(nameof(LeftFullscreenVisible)); }
        }

        private bool _rightFullscreenVisible;
        public bool RightFullscreenVisible
        {
            get => _rightFullscreenVisible;
            set { if (_rightFullscreenVisible == value) return; _rightFullscreenVisible = value; OnPropertyChanged(nameof(RightFullscreenVisible)); }
        }

        // ----------------- Konstruktoren -----------------
        // DI-Konstruktor (falls du AppHost/ServiceProvider verwendest)
        public MainPage(MainViewModel vm)
        {
            InitializeComponent();
            BindingContext = vm;
            App.MainVM = vm;

            WireFullscreenEvents();
            _ = ProcessPendingOpensAsync();
        }

        // Fallback ohne DI – stellt sicher, dass ein BindingContext existiert
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

            WireFullscreenEvents();
            _ = ProcessPendingOpensAsync();
        }

        private void WireFullscreenEvents()
        {
            // kleine Viewer -> Overlay toggeln
            LeftViewer.FullscreenRequested += (_, __) => LeftFullscreenVisible = true;
            RightViewer.FullscreenRequested += (_, __) => RightFullscreenVisible = true;

            // Fullscreen-Viewer -> Overlay schließen (gleiche Taste)
            LeftViewerFull.FullscreenRequested += (_, __) => LeftFullscreenVisible = false;
            RightViewerFull.FullscreenRequested += (_, __) => RightFullscreenVisible = false;
        }

        protected async override void OnAppearing()
        {
            base.OnAppearing();
            _ = ProcessPendingOpensAsync();


            await _settingsVm.LoadAsync();
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
        // --- Button "Links öffnen" ---
        private void OnOpenLeftClicked(object? sender, EventArgs e)
        {
            if (VM?.LoadLeftCommand is { } cmd && cmd.CanExecute(null))
                cmd.Execute(null);
        }

        // --- Button "Rechts öffnen" ---
        private void OnOpenRightClicked(object? sender, EventArgs e)
        {
            if (VM?.LoadRightCommand is { } cmd && cmd.CanExecute(null))
                cmd.Execute(null);
        }

        // --- Sortier-Buttons in der Tabellenkopfleiste ---
        private void OnSortClicked(object? sender, EventArgs e)
        {
            if (VM?.SortCommand is null) return;

            if (sender is Button btn && btn.CommandParameter is string col && VM.SortCommand.CanExecute(col))
                VM.SortCommand.Execute(col);
        }

        // --- Auswahl links in der Tag-Filterliste ---
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

        // --- Auswahl in der Vergleichstabelle rechts ---
        private void OnCombinedSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (VM is null) return;
            var selected = e.CurrentSelection?.FirstOrDefault();
            VM.SelectedCombinedMetadataItem = selected as CombinedMetadataItem;
        }

        // Vollbild schließen Buttons
        private void OnCloseLeftFullscreen(object? sender, EventArgs e) => LeftFullscreenVisible = false;
        private void OnCloseRightFullscreen(object? sender, EventArgs e) => RightFullscreenVisible = false;

        // Add-Tag Overlay (Links)
        private async void OnAddTagLeftOverlayClicked(object? sender, EventArgs e)
        {
            await DisplayAlert("Tag hinzufügen", "Neuen DICOM-Tag für LINKS hinzufügen (Verdrahtung folgt).", "OK");
        }

        // Add-Tag Overlay (Rechts)
        private async void OnAddTagRightOverlayClicked(object? sender, EventArgs e)
        {
            await DisplayAlert("Tag hinzufügen", "Neuen DICOM-Tag für RECHTS hinzufügen (Verdrahtung folgt).", "OK");
        }

        private void OnSaveLeftClicked(object sender, EventArgs e)
        {
            // TODO: save left side
        }

        private void OnSaveRightClicked(object sender, EventArgs e)
        {
            // TODO: save right side
        }

        // (Optional) Toolbar: "DICOM-Tag hinzufügen"
        private async void OnAddDicomTagClicked(object? sender, EventArgs e)
        {
            if (VM is null) return;

            var side = await DisplayActionSheet("Tag wohin hinzufügen?", "Abbrechen", null, "Links", "Rechts");
            if (side is not ("Links" or "Rechts")) return;
            bool toLeft = side == "Links";

            await DisplayAlert("Hinweis", "Verdrahtung mit dem vorhandenen Tag-Dialog kann ich sofort ergänzen, sobald du mir sagst, wie er das Ergebnis übergibt (Event/Task/Messaging).", "OK");
        }


        // ------- Buttons: Lokaler Knoten -------
        private async void OnBrowseStorageClicked(object sender, EventArgs e)
            => await _settingsVm.BrowseStorageAsync(this);

        private async void OnTestLocalDicomClicked(object sender, EventArgs e)
            => await _settingsVm.TestLocalScpAsync(this);

        private async void OnSaveLocalDicomClicked(object sender, EventArgs e)
            => await _settingsVm.SaveAsync();

        // ------- SEND-Nodes -------
        private async void OnAddSendNodeClicked(object s, EventArgs e)
            => await _settingsVm.AddNodeInteractiveAsync(this, NodeKind.Send);

        private async void OnEditSendNodeClicked(object s, EventArgs e)
            => await _settingsVm.EditSelectedNodeInteractiveAsync(this, NodeKind.Send);

        private void OnRemoveSendNodeClicked(object s, EventArgs e)
            => _settingsVm.RemoveSelectedNode(NodeKind.Send);

        private async void OnTestSendNodeClicked(object s, EventArgs e)
            => await _settingsVm.TestSelectedNodeAsync(this, NodeKind.Send);

        // ------- MWL-Nodes -------
        private async void OnAddMwNodeClicked(object s, EventArgs e)
            => await _settingsVm.AddNodeInteractiveAsync(this, NodeKind.Worklist);

        private async void OnEditMwNodeClicked(object s, EventArgs e)
            => await _settingsVm.EditSelectedNodeInteractiveAsync(this, NodeKind.Worklist);

        private void OnRemoveMwNodeClicked(object s, EventArgs e)
            => _settingsVm.RemoveSelectedNode(NodeKind.Worklist);

        private async void OnTestMwNodeClicked(object s, EventArgs e)
            => await _settingsVm.TestSelectedNodeAsync(this, NodeKind.Worklist);

        // ------- Q/R-Nodes -------
        private async void OnAddQrNodeClicked(object s, EventArgs e)
            => await _settingsVm.AddNodeInteractiveAsync(this, NodeKind.QueryRetrieve);

        private async void OnEditQrNodeClicked(object s, EventArgs e)
            => await _settingsVm.EditSelectedNodeInteractiveAsync(this, NodeKind.QueryRetrieve);

        private void OnRemoveQrNodeClicked(object s, EventArgs e)
            => _settingsVm.RemoveSelectedNode(NodeKind.QueryRetrieve);

        private async void OnTestQrNodeClicked(object s, EventArgs e)
            => await _settingsVm.TestSelectedNodeAsync(this, NodeKind.QueryRetrieve);
    }
}


