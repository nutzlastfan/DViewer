// Tabs/QueryRetrieveTabViewModel.cs
using FellowOakDicom.Network;
using FellowOakDicom;

namespace DViewer;

public sealed class QueryRetrieveTabViewModel : TabBaseVM
{
    PatientRow? _selectedRow;
    public PatientRow? SelectedRow { get => _selectedRow; set => Set(ref _selectedRow, value); }

    protected override System.Collections.Generic.IEnumerable<DicomNode> GetSourceNodes(AppSettings s)
        => s.QueryRetrieveNodes;

    public async Task SearchAsync(int timeoutMs = 2000, CancellationToken ct = default)
    {
        if (SelectedNode is null) return;
        IsBusy = true; Results.Clear();
        try
        {
            var s = AppSettingsStore.Instance.Settings;
            var client = CreateClient(SelectedNode, s.LocalAeTitle ?? "DVIEWER");
            var keys = new DicomDataset {
                { DicomTag.QueryRetrieveLevel, "STUDY" },
                { DicomTag.PatientID, string.IsNullOrWhiteSpace(PatientId) ? "" : PatientId.Trim() },
                { DicomTag.PatientName, string.IsNullOrWhiteSpace(PatientName) ? "*" : PatientName.Trim() + "*" },
                { DicomTag.PatientBirthDate, "" },
                { DicomTag.PatientSpeciesDescription, "" },
                { DicomTag.PatientBreedDescription, "" },
                { DicomTag.StudyInstanceUID, "" }
            };

            var req = new DicomCFindRequest(
                DicomUID.StudyRootQueryRetrieveInformationModelFind,
                DicomQueryRetrieveLevel.Study,
                DicomPriority.Medium)
            { Dataset = keys };

            req.OnResponseReceived += (_, rsp) =>
            {
                if (!rsp.HasDataset) return;
                var ds = rsp.Dataset;
                Results.Add(new PatientRow
                {
                    PatientID = ReadString(ds, DicomTag.PatientID),
                    PatientName = ReadString(ds, DicomTag.PatientName),
                    Species = ReadString(ds, DicomTag.PatientSpeciesDescription),
                    Breed = ReadString(ds, DicomTag.PatientBreedDescription),
                    BirthDate = DicomDateToDisplay(ReadString(ds, DicomTag.PatientBirthDate)),
                    StudyInstanceUID = ReadString(ds, DicomTag.StudyInstanceUID)
                });
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await client.AddRequestAsync(req);
            await client.SendAsync(cts.Token);
        }
        finally { IsBusy = false; }
    }

    // Platzhalter – C-MOVE braucht laufenden lokalen SCP:
    public async Task RetrieveSelectedAsync()
    {
        if (SelectedRow is null) return;
        await Application.Current!.MainPage!.DisplayAlert(
            "Retrieve",
            "C-MOVE benötigt einen laufenden lokalen C-STORE-SCP (Incoming aktiviert). " +
            "Sobald der SCP läuft, kann hier ein C-MOVE implementiert werden.",
            "OK");
    }
}
