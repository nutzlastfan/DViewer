using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace DViewer
{
    public sealed class DicomTestResult
    {
        public bool   Success       { get; init; }
        public string Status        { get; init; } = "";
        public string Message       { get; init; } = "";
        public int    RoundtripMs   { get; init; }
        public int    ResponsesSeen { get; init; }
    }

    public sealed class DicomNetworkTester
    {
        // ---- C-ECHO ---------------------------------------------------------
        public async Task<DicomTestResult> TestEchoAsync(
            DicomNode node,
            string callingAe,
            int timeoutMs = 5000,
            CancellationToken cancellationToken = default)
        {
            var client = CreateClient(node, callingAe);
            var req    = new DicomCEchoRequest();

            DicomStatus? lastStatus = null;
            req.OnResponseReceived += (_, rsp) => lastStatus = rsp.Status;

            var sw = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                await client.AddRequestAsync(req);
                await client.SendAsync(cts.Token);

                sw.Stop();
                bool ok = lastStatus == DicomStatus.Success;
                return new DicomTestResult
                {
                    Success       = ok,
                    Status        = (lastStatus ?? DicomStatus.Pending).ToString(),
                    Message       = ok ? "C-ECHO erfolgreich." : "C-ECHO fehlgeschlagen.",
                    RoundtripMs   = (int)sw.ElapsedMilliseconds,
                    ResponsesSeen = lastStatus != null ? 1 : 0
                };
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new DicomTestResult { Success = false, Status = "Timeout", Message = $"Timeout nach {timeoutMs} ms.", RoundtripMs = (int)sw.ElapsedMilliseconds };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new DicomTestResult { Success = false, Status = "Error", Message = ex.Message, RoundtripMs = (int)sw.ElapsedMilliseconds };
            }
        }

        // ---- MWL C-FIND -----------------------------------------------------
        public async Task<DicomTestResult> TestWorklistAsync(
             DicomNode node, string callingAe, int timeoutMs = 2000,
             CancellationToken cancellationToken = default)
        {
            var client = CreateClient(node, callingAe);

            var ds = new DicomDataset { { DicomTag.PatientName, "*" } };
            var sps = new DicomDataset
            {
                { DicomTag.ScheduledProcedureStepStartDate, "" },
                { DicomTag.Modality, "" }
            };
            ds.Add(new DicomSequence(DicomTag.ScheduledProcedureStepSequence, sps));

            var req = new DicomCFindRequest(
                DicomUID.ModalityWorklistInformationModelFind,
                DicomQueryRetrieveLevel.Patient,
                DicomPriority.Medium)
            {
                Dataset = ds
            };

            int responses = 0;
            DicomStatus? last = null;
            req.OnResponseReceived += (_, rsp) =>
            {
                last = rsp.Status;
                if (rsp.HasDataset) responses++;
            };

            var sw = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                await client.AddRequestAsync(req).ConfigureAwait(false);
                await client.SendAsync(cts.Token).ConfigureAwait(false);

                sw.Stop();
                bool ok = last is not null && (last == DicomStatus.Success || last.State == DicomState.Pending);
                return new DicomTestResult
                {
                    Success = ok,
                    Status = (last ?? DicomStatus.Pending).ToString(),
                    Message = ok ? $"MWL C-FIND ok. Antworten: {responses}" : "MWL C-FIND fehlgeschlagen.",
                    RoundtripMs = (int)sw.ElapsedMilliseconds,
                    ResponsesSeen = responses
                };
            }
            catch (DicomAssociationRejectedException rex)
            {
                sw.Stop();
                string called = string.IsNullOrWhiteSpace(node.CalledAe) ? node.AeTitle : node.CalledAe;
                string hint = rex.RejectReason switch
                {
                    DicomRejectReason.CalledAENotRecognized =>
                        $"CALLED AE '{called}' unbekannt beim Server. AE/Port prüfen.",
                    DicomRejectReason.CallingAENotRecognized =>
                        $"Dein CALLING AE '{callingAe}' ist am Server nicht freigeschaltet.",
                    DicomRejectReason.NoReasonGiven =>
                        "Association abgelehnt (häufig: falscher AE oder TLS/Port-Mismatch).",
                    _ => $"Association rejected ({rex.RejectResult}/{rex.Source}/{rex.RejectReason})."
                };

                return new DicomTestResult
                {
                    Success = false,
                    Status = $"{rex.RejectReason}/{rex.Source}/{rex.RejectReason}",
                    Message = hint,
                    RoundtripMs = (int)sw.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new DicomTestResult
                {
                    Success = false,
                    Status = "Timeout",
                    Message = $"Timeout nach {timeoutMs} ms.",
                    RoundtripMs = (int)sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new DicomTestResult
                {
                    Success = false,
                    Status = "Error",
                    Message = ex.Message,
                    RoundtripMs = (int)sw.ElapsedMilliseconds
                };
            }
        }


    

        // ---- Q/R C-FIND (Study Root) ---------------------------------------
        public async Task<DicomTestResult> TestQueryRetrieveAsync(
            DicomNode node,
            string callingAe,
            int timeoutMs = 5000,
            CancellationToken cancellationToken = default)
        {
            var client = CreateClient(node, callingAe);

            var ds = new DicomDataset
            {
                { DicomTag.QueryRetrieveLevel, "STUDY" },
                { DicomTag.PatientName, "*" },
                { DicomTag.StudyInstanceUID, "" },
                { DicomTag.StudyDate, "" },
                { DicomTag.StudyTime, "" },
                { DicomTag.StudyID, "" },
                { DicomTag.AccessionNumber, "" }
            };

            var req = new DicomCFindRequest(
                DicomUID.StudyRootQueryRetrieveInformationModelFind,
                DicomQueryRetrieveLevel.Study,
                DicomPriority.Medium)
            {
                Dataset = ds
            };

            int responses = 0;
            DicomStatus? last = null;
            req.OnResponseReceived += (_, rsp) =>
            {
                last = rsp.Status;
                if (rsp.HasDataset) responses++;
            };

            var sw = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                await client.AddRequestAsync(req);
                await client.SendAsync(cts.Token);

                sw.Stop();
                bool ok = last is not null && (last == DicomStatus.Success || last.State == DicomState.Pending);
                return new DicomTestResult
                {
                    Success       = ok,
                    Status        = (last ?? DicomStatus.Pending).ToString(),
                    Message       = ok ? $"Q/R C-FIND ok. Antworten: {responses}" : "Q/R C-FIND fehlgeschlagen.",
                    RoundtripMs   = (int)sw.ElapsedMilliseconds,
                    ResponsesSeen = responses
                };
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new DicomTestResult { Success = false, Status = "Timeout", Message = $"Timeout nach {timeoutMs} ms.", RoundtripMs = (int)sw.ElapsedMilliseconds };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new DicomTestResult { Success = false, Status = "Error", Message = ex.Message, RoundtripMs = (int)sw.ElapsedMilliseconds };
            }
        }

        // ---- Port-Check für lokalen SCP ------------------------------------
        public static Task<bool> IsTcpPortFreeAsync(int port)
        {
            return Task.Run(() =>
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Any, port);
                    listener.Start();
                    listener.Stop();
                    return true;
                }
                catch { return false; }
            });
        }

        // ---- intern ---------------------------------------------------------
        private static IDicomClient CreateClient(DicomNode node, string callingAe)
        {
            var calledAe = string.IsNullOrWhiteSpace(node.CalledAe) ? node.AeTitle : node.CalledAe;

            var client = DicomClientFactory.Create(
                host: node.Host,
                port: node.Port,
                useTls: node.UseTls,
                callingAe: callingAe,
                calledAe: calledAe
            );

            return client;
        }
    }
}
