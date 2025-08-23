// Tabs/WorklistTabViewModel.cs
using FellowOakDicom.Network;
using FellowOakDicom;

namespace DViewer;

public sealed class WorklistTabViewModel : TabBaseVM
{
    PatientRow? _selectedRow;
    public PatientRow? SelectedRow { get => _selectedRow; set => Set(ref _selectedRow, value); }

    protected override System.Collections.Generic.IEnumerable<DicomNode> GetSourceNodes(AppSettings s)
        => s.WorklistNodes;

    public async Task SearchAsync(int timeoutMs = 2000, CancellationToken ct = default)
    {
        if (SelectedNode is null) return;
        IsBusy = true; Results.Clear();
        try
        {
            var s = AppSettingsStore.Instance.Settings;
            var client = CreateClient(SelectedNode, s.LocalAeTitle ?? "DVIEWER");

            var ds = new DicomDataset();
            if (!string.IsNullOrWhiteSpace(PatientName))
                ds.Add(DicomTag.PatientName, PatientName.Trim() + "*");
            else
                ds.Add(DicomTag.PatientName, "");

            if (!string.IsNullOrWhiteSpace(PatientId))
                ds.Add(DicomTag.PatientID, PatientId.Trim());
            else
                ds.Add(DicomTag.PatientID, "");

            // Return keys
            ds.Add(DicomTag.PatientBirthDate, "");
            ds.Add(DicomTag.PatientSpeciesDescription, "");
            ds.Add(DicomTag.PatientBreedDescription, "");

            // SPS-Sequence minimal anfragen
            var sps = new DicomDataset {
                { DicomTag.ScheduledProcedureStepStartDate, "" },
                { DicomTag.Modality, "" }
            };
            ds.Add(new DicomSequence(DicomTag.ScheduledProcedureStepSequence, sps));

            var req = new DicomCFindRequest(
                DicomUID.ModalityWorklistInformationModelFind,
                DicomQueryRetrieveLevel.Patient, // MWL ignoriert das Level faktisch
                DicomPriority.Medium)
            { Dataset = ds };

            req.OnResponseReceived += (_, rsp) =>
            {
                if (!rsp.HasDataset) return;
                var d = rsp.Dataset;
                Results.Add(new PatientRow
                {
                    PatientID = ReadString(d, DicomTag.PatientID),
                    PatientName = ReadString(d, DicomTag.PatientName),
                    Species = ReadString(d, DicomTag.PatientSpeciesDescription),
                    Breed = ReadString(d, DicomTag.PatientBreedDescription),
                    BirthDate = DicomDateToDisplay(ReadString(d, DicomTag.PatientBirthDate)),
                });
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await client.AddRequestAsync(req);
            await client.SendAsync(cts.Token);
        }
        finally { IsBusy = false; }
    }
}