// AddDicomTagViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

namespace DViewer;

public enum AddTarget { Cancel, Left, Right, Both }

public sealed class AddTagResult
{
    public AddTarget Target { get; }
    public DicomTagCandidate Candidate { get; }
    public string Value { get; }

    public AddTagResult(AddTarget target, DicomTagCandidate candidate, string value)
    {
        Target = target; Candidate = candidate; Value = value;
    }
}

public sealed class AddDicomTagViewModel : INotifyPropertyChanged
{
    public ObservableCollection<DicomTagCandidate> All { get; } = new();
    public ObservableCollection<DicomTagCandidate> Filtered { get; } = new();

    string _filter = "";
    public string Filter { get => _filter; set { if (_filter != value) { _filter = value; OnPropertyChanged(); ApplyFilter(); } } }

    DicomTagCandidate? _selected;
    public DicomTagCandidate? Selected { get => _selected; set { _selected = value; OnPropertyChanged(); UpdateVrHint(); } }

    string _value = "";
    public string Value { get => _value; set { _value = value; OnPropertyChanged(); } }

    string _vrHint = "";
    public string VrHint { get => _vrHint; private set { _vrHint = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    readonly TaskCompletionSource<AddTagResult?> _tcs = new();
    public Task<AddTagResult?> Result => _tcs.Task;

    public AddDicomTagViewModel(IEnumerable<DicomTagCandidate> candidates)
    {
        foreach (var c in candidates.OrderBy(c => c.TagId)) { All.Add(c); Filtered.Add(c); }
    }

    void ApplyFilter()
    {
        var f = (Filter ?? "").Trim();
        Filtered.Clear();
        foreach (var c in All.Where(c =>
            string.IsNullOrEmpty(f) ||
            c.TagId.Contains(f, StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
            c.Vr.Contains(f, StringComparison.OrdinalIgnoreCase)))
            Filtered.Add(c);
    }

    void UpdateVrHint()
    {
        VrHint = Selected?.Vr switch
        {
            "DA" => "Format: yyyyMMdd (z.B. 20250806)",
            "TM" => "Format: HHmmss[.ffffff] (z.B. 142409.156000)",
            "PN" => "Format: Family^Given^Middle^Prefix^Suffix",
            "CS" => "Kodierter String (z.B. M/F/O …)",
            "IS" => "Integer String (nur Ziffern, optional Vorzeichen)",
            "DS" => "Decimal String (Punkt als Dezimaltrenner)",
            _ => "Freitext (roh, wie im DICOM vorgesehen)",
        };
    }

    public void Cancel() => _tcs.TrySetResult(null);
    public void AddLeft() => Commit(AddTarget.Left);
    public void AddRight() => Commit(AddTarget.Right);
    public void AddBoth() => Commit(AddTarget.Both);

    void Commit(AddTarget target)
    {
        if (Selected == null) { _tcs.TrySetResult(null); return; }
        _tcs.TrySetResult(new AddTagResult(target, Selected, Value ?? string.Empty));
    }
}
