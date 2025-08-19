using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using DViewer.Infrastructure;

namespace DViewer
{
    public partial class MainPage : ContentPage
    {
        private MainViewModel? VM => BindingContext as MainViewModel;

        // ----------------- Konstruktoren -----------------
        // DI-Konstruktor (falls du AppHost/ServiceProvider verwendest)
        public MainPage(MainViewModel vm)
        {
            InitializeComponent();
            BindingContext = vm;
            App.MainVM = vm;
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

            _ = ProcessPendingOpensAsync();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _ = ProcessPendingOpensAsync();
        }

        // ----------------- Externe Öffnungen (Dateiverknüpfung) -----------------
        private static bool _processingOpens;
        private async Task ProcessPendingOpensAsync()
        {
            if (_processingOpens || VM is null) return;
            _processingOpens = true;
            try
            {
                // nacheinander abarbeiten, für jede Datei Ziel-Seite wählen
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
            VM.SelectedTagFilter = selected as MainViewModel.TagFilterItem; // VM stößt intern RaiseFilterChanged() an
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

        // --- Preview-Tap links ---
        private async void OnLeftPreviewTapped(object? sender, TappedEventArgs e)
        {
            var vm = VM;
            if (vm?.Left?.Image == null) return;

            try
            {
                await Navigation.PushModalAsync(
                    new FullscreenImagePage(
                        source: vm.Left.Image,
                        title: "Left",
                        patientNameWithSex: vm.LeftPatientNameWithSex,
                        species: vm.LeftSpecies,
                        patientId: vm.LeftPatientID,
                        birthDateDisplay: vm.LeftBirthDateDisplay,
                        otherPid: vm.LeftOtherPid));
            }
            catch { /* still */ }
        }

        // --- Preview-Tap rechts ---
        private async void OnRightPreviewTapped(object? sender, TappedEventArgs e)
        {
            var vm = VM;
            if (vm?.Right?.Image == null) return;

            try
            {
                await Navigation.PushModalAsync(
                    new FullscreenImagePage(
                        source: vm.Right.Image,
                        title: "Right",
                        patientNameWithSex: vm.RightPatientNameWithSex,
                        species: vm.RightSpecies,
                        patientId: vm.RightPatientID,
                        birthDateDisplay: vm.RightBirthDateDisplay,
                        otherPid: vm.RightOtherPid));
            }
            catch { /* still */ }
        }


        // Add-Tag Overlay (Links)
        private async void OnAddTagLeftOverlayClicked(object? sender, EventArgs e)
        {
            // TODO: Hier deinen bestehenden Add-Dialog aufrufen und Zielseite = Links übergeben.
            // Beispiel (falls vorhanden): await Navigation.PushAsync(new AddDicomTagPage(toLeft: true));
            await DisplayAlert("Tag hinzufügen", "Neuen DICOM-Tag für LINKS hinzufügen (Verdrahtung folgt).", "OK");
        }

        // Add-Tag Overlay (Rechts)
        private async void OnAddTagRightOverlayClicked(object? sender, EventArgs e)
        {
            // TODO: Hier deinen bestehenden Add-Dialog aufrufen und Zielseite = Rechts übergeben.
            // Beispiel (falls vorhanden): await Navigation.PushAsync(new AddDicomTagPage(toLeft: false));
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

        // --- Preview-Tap rechts ---
        //private async void OnRightPreviewTapped(object? sender, TappedEventArgs e)
        //{
        //    if (VM?.Right?.Image != null)
        //    {
        //        try
        //        {
        //            await Navigation.PushAsync(new ContentPage
        //            {
        //                Content = new Image { Source = VM.Right.Image, Aspect = Aspect.AspectFit },
        //                Title = "Vorschau Rechts"
        //            });
        //        }
        //        catch { /* still */ }
        //    }
        //}

        // --- (Optional) Toolbar: "DICOM-Tag hinzufügen" ---
        // Hier nur Platzhalter – wenn dein vorhandener Dialog ein Ergebnis (DicomTagCandidate)
        // liefert, rufe danach VM.AddMissingTagToSide(candidate, toLeft) auf.
        private async void OnAddDicomTagClicked(object? sender, EventArgs e)
        {
            if (VM is null) return;

            var side = await DisplayActionSheet("Tag wohin hinzufügen?", "Abbrechen", null, "Links", "Rechts");
            if (side is not ("Links" or "Rechts")) return;
            bool toLeft = side == "Links";

            // >>> Hier deinen bestehenden Dialog aufrufen und das Ergebnis "candidate" abholen <<<
            // Beispiel:
            // var page = new AddDicomTagPage();
            // var candidate = await page.GetResultAsync();
            // if (candidate != null) VM.AddMissingTagToSide(candidate, toLeft);

            await DisplayAlert("Hinweis", "Verdrahtung mit dem vorhandenen Tag-Dialog kann ich sofort ergänzen, sobald du mir sagst, wie er das Ergebnis übergibt (Event/Task/Messaging).", "OK");
        }
    }
}
