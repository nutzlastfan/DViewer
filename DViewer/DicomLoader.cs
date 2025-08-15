using FellowOakDicom;
using FellowOakDicom.Imaging;


// ImageSharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using Image = SixLabors.ImageSharp.Image;

namespace DViewer
{
    public class DicomLoader
    {
        // Große/Binär-Tags weglassen
        private static readonly HashSet<DicomTag> SkipTags = new()
        {
            DicomTag.PixelData,
            DicomTag.WaveformData,
            DicomTag.EncapsulatedDocument
        };

        private static bool IsOverlayData(DicomItem it) =>
            it.Tag.Group >= 0x6000 && it.Tag.Group <= 0x60FF && it.Tag.Element == 0x3000;

        private static string ReadValueSafe(DicomItem item)
        {
            try
            {
                if (item.ValueRepresentation == DicomVR.SQ) return "[Sequence]";
                if (SkipTags.Contains(item.Tag) || IsOverlayData(item)) return "[Binary]";

                if (item is DicomElement el)
                {
                    int count = el.Count;
                    if (count <= 0) return string.Empty;

                    int take = Math.Min(count, 16);
                    var vals = new string[take];
                    for (int i = 0; i < take; i++)
                    {
                        try { vals[i] = el.Get<string>(i) ?? string.Empty; }
                        catch { vals[i] = string.Empty; }
                    }
                    if (count > take) vals[take - 1] += $" …(+{count - take})";
                    return string.Join("\\", vals);
                }

                return "[Unsupported]";
            }
            catch
            {
                return string.Empty;
            }
        }

        public async Task<DicomFileViewModel?> PickAndLoadAsync(CancellationToken ct = default)
        {
            try
            {
                var pick = await Microsoft.Maui.Storage.FilePicker.Default.PickAsync(
                    new Microsoft.Maui.Storage.PickOptions { PickerTitle = "DICOM-Datei auswählen" });
                if (pick == null) return null;

                var path = pick.FullPath ?? pick.FileName;
                return await LoadAsync(path, ct).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        public async Task<DicomFileViewModel> LoadAsync(string path, CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var vm = new DicomFileViewModel
                {
                    FileName = System.IO.Path.GetFileName(path),
                    Image = null
                };

                var list = new List<DicomMetadataItem>();

                try
                {
                    var dicom = DicomFile.Open(path);
                    var ds = dicom.Dataset;

                    foreach (var item in ds)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            var tag = item.Tag;
                            var name = tag.DictionaryEntry?.Name ?? string.Empty;
                            var vr = item.ValueRepresentation?.Code ?? item.ValueRepresentation?.ToString() ?? string.Empty;
                            var val = ReadValueSafe(item);

                            list.Add(new DicomMetadataItem
                            {
                                TagId = tag.ToString(), // "(gggg,eeee)"
                                Name = name,
                                Vr = vr,
                                Value = val
                            });
                        }
                        catch
                        {
                            // einzelnes Element überspringen
                        }
                    }

                    vm.SetMetadata(list);

                    // Preview optional & best effort (blockiert nicht das Lesen der Metadaten)
                    try
                    {
                        if (ds.Contains(DicomTag.PixelData))
                        {
                            var di = new DicomImage(ds);
                            var rendered = di.RenderImage(); // IImage

                            // *** ImageSharp aus dem Render-Result ziehen ***
                            Image? sharp = null;

                            // 1) Bevorzugt: AsSharpImage() (Erweiterungsmethode aus FellowOakDicom.Imaging.ImageSharp)
                            var asSharp = rendered.GetType().GetMethod("AsSharpImage", Type.EmptyTypes);
                            if (asSharp != null)
                            {
                                sharp = asSharp.Invoke(rendered, null) as Image;
                            }

                            // 2) Fallback: öffentliche Property "Image" (manche Builds besitzen die)
                            if (sharp == null)
                            {
                                var prop = rendered.GetType().GetProperty("Image");
                                if (prop?.GetValue(rendered) is Image imgProp)
                                    sharp = imgProp;
                            }

                            if (sharp != null)
                            {
                                // Als PNG in Memory -> MAUI ImageSource
                                using var ms = new MemoryStream();
                                sharp.Save(ms, new PngEncoder());
                                var bytes = ms.ToArray();

                                vm.Image = ImageSource.FromStream(() => new MemoryStream(bytes));
                            }
                            else
                            {
                                // Keine kompatible Ausgabe -> Vorschau leer lassen (kein Crash)
                                vm.Image = null;
                            }
                        }
                    }
                    catch
                    {
                        vm.Image = null; // Vorschau ist optional
                    }
                }
                catch
                {
                    vm.SetMetadata(new List<DicomMetadataItem>());
                    vm.Image = null;
                }

                return vm;
            }, ct).ConfigureAwait(false);
        }
    }
}
