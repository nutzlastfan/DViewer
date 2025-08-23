// Tabs/Shared.cs
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace DViewer;

public sealed class PatientRow
{
    public string PatientID { get; init; } = "";
    public string PatientName { get; init; } = "";
    public string Species { get; init; } = ""; // (0010,2201)
    public string Breed { get; init; } = ""; // (0010,2292)
    public string BirthDate { get; init; } = ""; // (0010,0030)
    public string StudyInstanceUID { get; init; } = ""; // für Q/R
}

public abstract class TabBaseVM : INotifyPropertyChanged
{
    public ObservableCollection<DicomNode> Nodes { get; } = new();
    DicomNode? _selectedNode;
    public DicomNode? SelectedNode { get => _selectedNode; set => Set(ref _selectedNode, value); }

    string _patientName = "";
    public string PatientName { get => _patientName; set => Set(ref _patientName, value); }

    string _patientId = "";
    public string PatientId { get => _patientId; set => Set(ref _patientId, value); }

    public ObservableCollection<PatientRow> Results { get; } = new();

    bool _isBusy;
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

    public void LoadNodesFromSettings(Func<AppSettings> get)
    {
        Nodes.Clear();
        var s = get();
        foreach (var n in GetSourceNodes(s))
            Nodes.Add(n);
        if (Nodes.Count > 0 && SelectedNode is null) SelectedNode = Nodes[0];
    }

    protected abstract System.Collections.Generic.IEnumerable<DicomNode> GetSourceNodes(AppSettings s);

    //protected static string Get(Age: bool) => ""; // not used, but you can add helpers

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false; field = value; OnPropertyChanged(name); return true;
    }

    protected static IDicomClient CreateClient(DicomNode node, string callingAe)
    {
        var calledAe = string.IsNullOrWhiteSpace(node.CalledAe) ? node.AeTitle : node.CalledAe;
        return DicomClientFactory.Create(
            host: node.Host, port: node.Port, useTls: node.UseTls,
            callingAe: callingAe, calledAe: calledAe);
    }

    protected static string ReadString(DicomDataset ds, DicomTag tag)
        => ds.TryGetString(tag, out var s) ? s : "";

    protected static string DicomDateToDisplay(string yyyymmdd)
        => string.IsNullOrWhiteSpace(yyyymmdd) || yyyymmdd.Length < 8
           ? ""
           : $"{yyyymmdd.Substring(6, 2)}.{yyyymmdd.Substring(4, 2)}.{yyyymmdd.Substring(0, 4)}";
}
