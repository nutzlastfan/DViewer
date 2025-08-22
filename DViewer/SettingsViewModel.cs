using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace DViewer
{
    public enum NodeKind { Send, Worklist, QueryRetrieve }

    public sealed class SettingsViewModel : INotifyPropertyChanged
    {
        // ---------- lokale DICOM-Einstellungen (als Strings -> einfache Entry-Bindings) ----------
        string _localAeTitle = "DVIEWER";
        string _localPort = "104";
        string _localStorageFolder = "";
        string _localMaxPdu = "16384";
        bool _localAcceptIncoming;
        bool _localUseTls;

        public string LocalAeTitle { get => _localAeTitle; set => Set(ref _localAeTitle, value); }
        public string LocalPort { get => _localPort; set => Set(ref _localPort, value); }
        public string LocalStorageFolder { get => _localStorageFolder; set => Set(ref _localStorageFolder, value); }
        public string LocalMaxPdu { get => _localMaxPdu; set => Set(ref _localMaxPdu, value); }
        public bool LocalAcceptIncoming { get => _localAcceptIncoming; set => Set(ref _localAcceptIncoming, value); }
        public bool LocalUseTls { get => _localUseTls; set => Set(ref _localUseTls, value); }

        // ---------- Node-Listen ----------
        public ObservableCollection<DicomNode> SendNodes { get; } = new();
        public ObservableCollection<DicomNode> WorklistNodes { get; } = new();
        public ObservableCollection<DicomNode> QueryRetrieveNodes { get; } = new();

        // Auswahl (für Bearbeiten/Löschen/Test)
        DicomNode? _selectedSendNode, _selectedMwNode, _selectedQrNode;
        public DicomNode? SelectedSendNode { get => _selectedSendNode; set => Set(ref _selectedSendNode, value); }
        public DicomNode? SelectedMwNode { get => _selectedMwNode; set => Set(ref _selectedMwNode, value); }
        public DicomNode? SelectedQrNode { get => _selectedQrNode; set => Set(ref _selectedQrNode, value); }

        // ---------- Laden/Speichern ----------
        public async Task LoadAsync()
        {
            var store = AppSettingsStore.Instance;
            await store.LoadAsync();                 // <-- Instanzmethode über Singleton
            var model = store.Settings;              // aktuelle Settings

            LocalAeTitle = model.LocalAeTitle ?? "DVIEWER";
            LocalPort = model.LocalPort.ToString();
            LocalStorageFolder = model.LocalStorageFolder ?? "";
            LocalMaxPdu = model.LocalMaxPdu.ToString();
            LocalAcceptIncoming = model.LocalAcceptIncoming;
            LocalUseTls = model.LocalUseTls;

            Replace(SendNodes, model.SendNodes);
            Replace(WorklistNodes, model.WorklistNodes);
            Replace(QueryRetrieveNodes, model.QueryRetrieveNodes);
        }

        public async Task SaveAsync()
        {
            var store = AppSettingsStore.Instance;

            // Bequem: Änderungen anwenden und direkt persistieren
            await store.UpdateAsync(s =>
            {
                s.LocalAeTitle = LocalAeTitle?.Trim() ?? "DVIEWER";
                s.LocalPort = TryParseInt(LocalPort, 104);
                s.LocalStorageFolder = LocalStorageFolder?.Trim() ?? "";
                s.LocalMaxPdu = TryParseInt(LocalMaxPdu, 16384);
                s.LocalAcceptIncoming = LocalAcceptIncoming;
                s.LocalUseTls = LocalUseTls;

                s.SendNodes = new(SendNodes);
                s.WorklistNodes = new(WorklistNodes);
                s.QueryRetrieveNodes = new(QueryRetrieveNodes);
            });
        }

        // ---------- UI-Helfer ----------
        public async Task BrowseStorageAsync(Page page)
        {
            var suggested = System.IO.Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "dicom");
            if (string.IsNullOrWhiteSpace(LocalStorageFolder))
                LocalStorageFolder = suggested;

            await page.DisplayAlert("Speicherpfad",
                $"Aktueller Pfad:\n{LocalStorageFolder}\n\n(Anpassung per Code/Plattformdialog möglich)", "OK");
        }

        public async Task TestLocalScpAsync(Page page)
        {
            await page.DisplayAlert("Test SCP",
                $"AE: {LocalAeTitle}\nPort: {LocalPort}\nTLS: {(LocalUseTls ? "An" : "Aus")}\n\n(Implementiere hier echten Netzwerk-Test)",
                "OK");
        }

        // ---------- Node-Aktionen ----------
        public async Task AddNodeInteractiveAsync(Page page, NodeKind kind)
        {
            var n = await PromptNodeAsync(page, new DicomNode(), "Neuer DICOM-Knoten");
            if (n == null) return;
            Get(kind).Add(n);
            await SaveAsync();
        }

        public async Task EditSelectedNodeInteractiveAsync(Page page, NodeKind kind)
        {
            var sel = GetSelected(kind);
            if (sel == null) return;

            var edited = await PromptNodeAsync(page, new DicomNode
            {
                AeTitle = sel.AeTitle,
                Host = sel.Host,
                Port = sel.Port,
                CalledAe = sel.CalledAe,
                UseTls = sel.UseTls
            }, "Knoten bearbeiten");

            if (edited == null) return;

            sel.AeTitle = edited.AeTitle;
            sel.Host = edited.Host;
            sel.Port = edited.Port;
            sel.CalledAe = edited.CalledAe;
            sel.UseTls = edited.UseTls;

            OnPropertyChanged(nameof(SelectedSendNode));
            OnPropertyChanged(nameof(SelectedMwNode));
            OnPropertyChanged(nameof(SelectedQrNode));

            await SaveAsync();
        }

        public void RemoveSelectedNode(NodeKind kind)
        {
            var list = Get(kind);
            var sel = GetSelected(kind);
            if (sel != null) list.Remove(sel);
            _ = SaveAsync();
        }

        public async Task TestSelectedNodeAsync(Page page, NodeKind kind)
        {
            var sel = GetSelected(kind);
            if (sel == null) return;
            await page.DisplayAlert("Test Knoten",
                $"{sel.AeTitle} @ {sel.Host}:{sel.Port}\nCalled AE: {sel.CalledAe}\nTLS: {(sel.UseTls ? "An" : "Aus")}\n\n(Implementiere hier echten Echo/C-Echo Test)",
                "OK");
        }

        // ---------- intern ----------
        ObservableCollection<DicomNode> Get(NodeKind kind) => kind switch
        {
            NodeKind.Send => SendNodes,
            NodeKind.Worklist => WorklistNodes,
            NodeKind.QueryRetrieve => QueryRetrieveNodes,
            _ => SendNodes
        };

        DicomNode? GetSelected(NodeKind kind) => kind switch
        {
            NodeKind.Send => SelectedSendNode,
            NodeKind.Worklist => SelectedMwNode,
            NodeKind.QueryRetrieve => SelectedQrNode,
            _ => null
        };

        static void Replace(ObservableCollection<DicomNode> target, IEnumerable<DicomNode>? src)
        {
            target.Clear();
            if (src == null) return;
            foreach (var n in src) target.Add(n);
        }

        static int TryParseInt(string? s, int fallback)
            => int.TryParse(s?.Trim(), out var v) ? v : fallback;

        async Task<DicomNode?> PromptNodeAsync(Page page, DicomNode preset, string title)
        {
            string ae = await page.DisplayPromptAsync(title, "AE Title:", initialValue: preset.AeTitle) ?? "";
            if (string.IsNullOrWhiteSpace(ae)) return null;

            string host = await page.DisplayPromptAsync(title, "Host:", initialValue: preset.Host) ?? "";
            if (string.IsNullOrWhiteSpace(host)) return null;

            string portStr = await page.DisplayPromptAsync(title, "Port:", initialValue: preset.Port.ToString(), keyboard: Keyboard.Numeric) ?? "104";
            _ = int.TryParse(portStr, out int port);

            string called = await page.DisplayPromptAsync(title, "Called AE (optional):", initialValue: preset.CalledAe) ?? "";
            bool useTls = await page.DisplayAlert(title, "TLS verwenden?", "Ja", "Nein");

            return new DicomNode
            {
                AeTitle = ae.Trim(),
                Host = host.Trim(),
                Port = port > 0 ? port : 104,
                CalledAe = called.Trim(),
                UseTls = useTls
            };
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
