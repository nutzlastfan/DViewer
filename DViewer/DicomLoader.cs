using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Maui.Controls;            // ImageSource
using Microsoft.Maui.Storage;             // FileSystem

using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;

// ImageSharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace DViewer
{
    public class DicomLoader
    {
        private const int MAX_PREVIEW_FRAMES = 300; // Safety-Limit für cine

        // Große/Binär-Tags nicht in die Metadatenliste aufnehmen
        private static readonly HashSet<DicomTag> SkipTags = new()
        {
            DicomTag.PixelData,
            DicomTag.WaveformData,
            DicomTag.EncapsulatedDocument
        };

        // ---------- Public API ----------

        public async Task<DicomFileViewModel?> PickAndLoadAsync(CancellationToken ct = default)
        {
            try
            {
                var pick = await FilePicker.Default.PickAsync(
                    new PickOptions { PickerTitle = "DICOM-Datei auswählen" });
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


                    // Window/Level Default aus dem Datensatz holen
                    var (wc0, ww0) = GetWindow(ds);

                    // WL-Renderer erzeugen und ins VM injizieren
                    var wlRenderer = MakeWlRenderer(ds);
                    TrySetWindowingOnVm(vm, wlRenderer, wc0 ?? 50.0, ww0 ?? 350.0);


                    // 1) Metadaten sammeln
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
                                TagId = tag.ToString(),
                                Name = name,
                                Vr = vr,
                                Value = val
                            });
                        }
                        catch { /* einzelnes Item ignorieren */ }
                    }

                    vm.SetMetadata(list);

                    // 2) Video erkennen & extrahieren (MPEG-4/H.264, MPEG-2, HEVC)
                    string? videoPath = null;
                    string? videoMime = null;
                    TryExtractEncapsulatedVideo(dicom, out videoPath, out videoMime);

                    if (!string.IsNullOrEmpty(videoPath))
                    {
                        // Video-Infos optional ins VM spiegeln (nur falls vorhanden)
                        TrySetVideoOnVm(vm, videoPath!, videoMime);

                        // Versuche zusätzlich ein Standbild (Frame 0) zu rendern (für Preview)
                        //vm.Image = TryRenderFirstFrame(ds);
                        // Kein Frames-Rendering für echte Video-TS
                        TrySetFramesOnVm(vm, null, null);
                        return vm;
                    }

                    // 3) Kein „echtes“ Video – versuche cine (Multi-Frame) zu rendern
                    // 3) Kein „echtes“ Video – cine per Provider (keine Vorberechnung)
                    var ts = ds.InternalTransferSyntax ?? DicomTransferSyntax.ImplicitVRLittleEndian;
                    var px = FellowOakDicom.Imaging.DicomPixelData.Create(ds);
                    if (px != null && px.NumberOfFrames > 1 && !IsVideoTransferSyntax(ts))
                    {
                        var (count, provider) = MakeFrameProvider(ds);
                        if (count > 0)
                        {
                            vm.Image = provider(0);                         // Frame 0 als Preview
                            TrySetFrameProviderOnVm(vm, count, provider);   // <<< WICHTIG
                        }
                        else
                        {
                            vm.Image = BuildPreviewImageSource(ds);
                            TrySetFrameProviderOnVm(vm, 0, _ => null);
                        }
                    }
                    else
                    {
                        // 4) Fallback: Einzelbild
                        vm.Image = BuildPreviewImageSource(ds);
                        TrySetFrameProviderOnVm(vm, 0, _ => null);
                    }
                }
                catch
                {
                    vm.SetMetadata(Array.Empty<DicomMetadataItem>());
                    vm.Image = null;
                    TrySetFramesOnVm(vm, null, null);
                }

                return vm;
            }, ct).ConfigureAwait(false);
        }

        // ---------- Video-Erkennung & -Extraktion ----------

        /// <summary>
        /// Erkennt MPEG-4/H.264, MPEG-2 oder HEVC Transfer Syntaxen und schreibt den
        /// zusammenhängenden Bitstream in eine Temp-Datei. Liefert Pfad + MIME zurück.
        /// </summary>
        private static void TryExtractEncapsulatedVideo(DicomFile file, out string? tempPath, out string? mime)
        {
            tempPath = null;
            mime = null;

            try
            {
                if (file?.Dataset is null) return;

                var ts = file.FileMetaInfo?.TransferSyntax;
                if (ts is null || !ts.IsEncapsulated || !IsVideoTransferSyntax(ts)) return;

                var ds = file.Dataset;

                // PixelData holen
                var item = ds.GetDicomItem<DicomItem>(DicomTag.PixelData);
                if (item is null) return;

                byte[]? bytes = null;

                // 1) Bevorzugt: komplette Bytefolge aus Fragmenten zusammensetzen
                if (item is DicomOtherByteFragment frag)
                {
                    if (frag.Fragments.Count == 1)
                    {
                        bytes = frag.Fragments[0]?.Data;
                    }
                    else if (frag.Fragments.Count > 1)
                    {
                        // alle Fragmente hintereinander
                        bytes = new CompositeByteBuffer(frag.Fragments.ToArray()).Data;
                    }
                }
                // 2) Fallback: Manche Implementierungen legen es als DicomElement mit Buffer ab
                else if (item is DicomElement el)
                {
                    bytes = el.Buffer?.Data;
                }

                // 3) Alternativ-Fallback: über DicomPixelData Frame(0)
                if (bytes is null || bytes.Length == 0)
                {
                    var px = FellowOakDicom.Imaging.DicomPixelData.Create(ds);
                    if (px != null && px.NumberOfFrames > 0)
                    {
                        var buf = px.GetFrame(0);
                        bytes = buf?.Data;
                    }
                }

                if (bytes is null || bytes.Length == 0) return;

                // Datei im Cache ablegen
                var ext = GetVideoExtension(ts);
                mime = GetVideoMime(ts);

                var fileName = $"{Guid.NewGuid():N}{ext}";
                var fullPath = Path.Combine(FileSystem.CacheDirectory, fileName);
                File.WriteAllBytes(fullPath, bytes);

                tempPath = fullPath;
            }
            catch
            {
                tempPath = null;
                mime = null;
            }

        }




        // --- NEU: einfachen LRU-Frame-Provider bauen (on-demand Rendering + kleiner Cache)
        private static (int count, Func<int, ImageSource?> provider) MakeFrameProvider(DicomDataset ds)
        {
            var px = FellowOakDicom.Imaging.DicomPixelData.Create(ds);
            var count = px?.NumberOfFrames ?? 0;
            if (count <= 0) return (0, _ => null);

            // Ein DicomImage ist wiederverwendbar
            var di = new FellowOakDicom.Imaging.DicomImage(ds);

            // kleiner LRU-Cache
            const int CACHE_CAP = 12;
            var cache = new LinkedList<int>();
            var map = new Dictionary<int, byte[]>(capacity: CACHE_CAP);

            ImageSource FromBytes(byte[] bytes) =>
                ImageSource.FromStream(() => new MemoryStream(bytes));

            ImageSource? RenderFrame(int index)
            {
                if (index < 0 || index >= count) return null;

                if (map.TryGetValue(index, out var cached))
                {
                    // refresh LRU
                    var node = cache.Find(index);
                    if (node != null)
                    {
                        cache.Remove(node);
                        cache.AddFirst(node);
                    }
                    return FromBytes(cached);
                }

                using var rendered = di.RenderImage(index);
                using var sharp = rendered.AsSharpImage();
                using var ms = new MemoryStream();
                sharp.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                var bytes = ms.ToArray();

                // in Cache legen
                if (!map.ContainsKey(index))
                {
                    cache.AddFirst(index);
                    map[index] = bytes;
                    if (cache.Count > CACHE_CAP)
                    {
                        int drop = cache.Last!.Value;
                        cache.RemoveLast();
                        map.Remove(drop);
                    }
                }
                return FromBytes(bytes);
            }

            return (count, RenderFrame);
        }

        // --- NEU: korrekte VM-Anbindung (statt SetFrames)
        private static void TrySetFrameProviderOnVm(object vm, int frameCount, Func<int, ImageSource?> provider)
        {
            try
            {
                var mi = vm.GetType().GetMethod("SetFrameProvider",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null) { mi.Invoke(vm, new object?[] { frameCount, provider }); return; }

                // Fallback: direkte Properties, wenn vorhanden
                vm.GetType().GetProperty("FrameCount")?.SetValue(vm, frameCount);
                vm.GetType().GetProperty("GetFrameImageSource")?.SetValue(vm, provider);
            }
            catch { /* still */ }
        }

        private static bool IsVideoTransferSyntax(DicomTransferSyntax ts)
        {
            // Sichere Erkennung über UID-String-Präfixe (robust gegen FO-DICOM-Versionen)
            // MPEG-2: 1.2.840.10008.1.2.4.100 / .101
            // MPEG-4 AVC/H.264: 1.2.840.10008.1.2.4.102 .. .106 (verschiedene Profile/Level/Vorsätze)
            // HEVC/H.265: 1.2.840.10008.1.2.4.107 .. .109 (je nach DICOM Version)
            var uid = ts.UID?.UID ?? string.Empty;

            if (uid == "1.2.840.10008.1.2.4.100" || uid == "1.2.840.10008.1.2.4.101")
                return true; // MPEG-2

            if (uid.StartsWith("1.2.840.10008.1.2.4.10", StringComparison.Ordinal))
                return true; // MPEG-4 AVC/H.264 Familie (.102-.106 etc.)

            if (uid.StartsWith("1.2.840.10008.1.2.4.107", StringComparison.Ordinal) ||
                uid.StartsWith("1.2.840.10008.1.2.4.108", StringComparison.Ordinal) ||
                uid.StartsWith("1.2.840.10008.1.2.4.109", StringComparison.Ordinal))
                return true; // HEVC/H.265 Varianten

            return false;
        }

        private static string GetVideoExtension(DicomTransferSyntax ts)
        {
            var uid = ts.UID?.UID ?? string.Empty;

            if (uid == "1.2.840.10008.1.2.4.100" || uid == "1.2.840.10008.1.2.4.101")
                return ".mpg"; // MPEG-2 Program Stream (naheliegend)

            if (uid.StartsWith("1.2.840.10008.1.2.4.10", StringComparison.Ordinal))
                return ".h264"; // AVC Elementary Stream (ohne MP4-Container)

            if (uid.StartsWith("1.2.840.10008.1.2.4.107", StringComparison.Ordinal) ||
                uid.StartsWith("1.2.840.10008.1.2.4.108", StringComparison.Ordinal) ||
                uid.StartsWith("1.2.840.10008.1.2.4.109", StringComparison.Ordinal))
                return ".h265"; // HEVC Elementary Stream

            return ".bin";
        }

        private static string GetVideoMime(DicomTransferSyntax ts)
        {
            var uid = ts.UID?.UID ?? string.Empty;

            if (uid == "1.2.840.10008.1.2.4.100" || uid == "1.2.840.10008.1.2.4.101")
                return "video/mpeg";

            if (uid.StartsWith("1.2.840.10008.1.2.4.10", StringComparison.Ordinal))
                return "video/avc";     // H.264 elementary stream

            if (uid.StartsWith("1.2.840.10008.1.2.4.107", StringComparison.Ordinal) ||
                uid.StartsWith("1.2.840.10008.1.2.4.108", StringComparison.Ordinal) ||
                uid.StartsWith("1.2.840.10008.1.2.4.109", StringComparison.Ordinal))
                return "video/hevc";    // H.265 elementary stream

            return "application/octet-stream";
        }

        private static byte[] BufferToArray(IByteBuffer buf)
        {
            try
            {
                // Schneller Pfad: ohne Kopie
                if (buf is MemoryByteBuffer mbb)
                    return mbb.Data;

                // Universeller Pfad: FO-DICOM stellt hier die Daten als Array bereit
                return buf.Data ?? Array.Empty<byte>();
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        // ---------- Multi-Frame (cine) ----------

        private static IReadOnlyList<ImageSource>? BuildFrameSources(DicomDataset ds, out double? fps)
        {
            fps = null;
            try
            {
                var px = DicomPixelData.Create(ds);
                if (px == null || px.NumberOfFrames <= 1 || !ds.Contains(DicomTag.PixelData))
                    return null;

                // Wenn Transfer Syntax „Video“ ist, behandeln wir es nicht als cine-Frames
                var ts = ds.InternalTransferSyntax ?? DicomTransferSyntax.ImplicitVRLittleEndian;
                if (IsVideoTransferSyntax(ts))
                    return null;

                fps = GetFps(ds);
                int count = Math.Min(px.NumberOfFrames, MAX_PREVIEW_FRAMES);

                var di = new DicomImage(ds); // FO-DICOM rendert pro Frame
                var list = new List<ImageSource>(count);

                for (int i = 0; i < count; i++)
                {
                    using var rendered = di.RenderImage(i);
                    using var sharp = rendered.AsSharpImage(); // erfordert FO-DICOM ImageSharp
                    using var ms = new MemoryStream();
                    sharp.Save(ms, new PngEncoder());
                    var png = ms.ToArray();
                    list.Add(ImageSource.FromStream(() => new MemoryStream(png)));
                }

                return list;
            }
            catch
            {
                return null;
            }
        }

        // ---------- Einzelbild-Preview ----------

        private static ImageSource? BuildPreviewImageSource(DicomDataset ds)
        {
            // Robust: erst FO-DICOM rendern lassen (funktioniert für viele komprimierte Bilder)
            var img = TryRenderFirstFrame(ds);
            if (img != null) return img;

            // Fallback auf manuelles Decoding nur wenn wirklich nötig – hier ausgelassen,
            // weil RenderImage bereits der verlässlichere Weg ist.
            return null;
        }

        private static ImageSource? TryRenderFirstFrame(DicomDataset ds)
        {
            try
            {
                if (!ds.Contains(DicomTag.PixelData)) return null;
                var px = DicomPixelData.Create(ds);
                if (px == null || px.NumberOfFrames == 0) return null;

                var di = new DicomImage(ds);
                using var rendered = di.RenderImage(0);
                using var sharp = rendered.AsSharpImage();
                using var ms = new MemoryStream();
                sharp.Save(ms, new PngEncoder());
                var bytes = ms.ToArray();
                return ImageSource.FromStream(() => new MemoryStream(bytes));
            }
            catch
            {
                return null;
            }
        }

        // ---------- Helpers ----------

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
            catch { return string.Empty; }
        }

        private static bool IsOverlayData(DicomItem it) =>
            it.Tag.Group >= 0x6000 && it.Tag.Group <= 0x60FF && it.Tag.Element == 0x3000;

        private static (double? wc, double? ww) GetWindow(DicomDataset ds)
        {
            try
            {
                var centers = ds.GetValues<double>(DicomTag.WindowCenter);
                var widths = ds.GetValues<double>(DicomTag.WindowWidth);
                if (centers?.Length > 0 && widths?.Length > 0)
                    return (centers[0], widths[0]);
            }
            catch { }
            return (null, null);
        }

        private static double? GetFps(DicomDataset ds)
        {
            try
            {
                // (0018,1063) FrameTime in ms
                if (ds.TryGetSingleValue<double>(DicomTag.FrameTime, out var frameTimeMs) && frameTimeMs > 0.0)
                    return 1000.0 / frameTimeMs;
            }
            catch { }

            try
            {
                // (0018,0040) Cine Rate in fps
                if (ds.TryGetSingleValue<double>(DicomTag.CineRate, out var cine) && cine > 0.0)
                    return cine;
            }
            catch { }

            return null;
        }

        // ---------- optionale ViewModel-Integration (fail-safe via Reflection) ----------

        private static void TrySetFramesOnVm(object vm, IReadOnlyList<ImageSource>? frames, double? fps)
        {
            try
            {
                var mi = vm.GetType().GetMethod("SetFrames", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    // Signatur erwartet (IReadOnlyList<ImageSource>?, double?)
                    mi.Invoke(vm, new object?[] { frames, fps });
                }
            }
            catch { /* optional */ }
        }

        private static void TrySetVideoOnVm(object vm, string path, string? mime)
        {
            try
            {
                // bevorzugt eine Methode SetVideo(string? path, string? mime)
                var mi = vm.GetType().GetMethod("SetVideo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    mi.Invoke(vm, new object?[] { path, mime });
                    return;
                }

                // alternativ Properties direkt setzen, falls vorhanden
                var pPath = vm.GetType().GetProperty("VideoTempPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var pMime = vm.GetType().GetProperty("VideoMime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                pPath?.SetValue(vm, path);
                pMime?.SetValue(vm, mime);

                // HasVideo o.ä. ist üblicherweise computed; ansonsten PropertyChanged selbst raisen
                var onPc = vm.GetType().GetMethod("OnPropertyChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                onPc?.Invoke(vm, new object?[] { "VideoTempPath" });
                onPc?.Invoke(vm, new object?[] { "VideoMime" });
                onPc?.Invoke(vm, new object?[] { "HasVideo" });
            }
            catch { /* optional */ }
        }



private static Func<double, double, int, ImageSource> MakeWlRenderer(DicomDataset ds)
{
    var di = new FellowOakDicom.Imaging.DicomImage(ds); // wiederverwendbar
    return (center, width, frame) =>
    {
        di.WindowCenter = center;
        di.WindowWidth  = Math.Max(1, width);
        using var rendered = di.RenderImage(Math.Max(0, frame));
        using var sharp    = rendered.AsSharpImage();
        using var ms       = new MemoryStream();
        sharp.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        var bytes = ms.ToArray();
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    };
}

        private static void TrySetWindowingOnVm(object vm,
            Func<double, double, int, ImageSource> renderer, double? wc, double? ww)
        {
            try
            {
                // bevorzugt eine Methode SetWindowing(...)
                var mi = vm.GetType().GetMethod("SetWindowing",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null) { mi.Invoke(vm, new object?[] { renderer, wc, ww }); return; }

                // Fallback: direkte Properties, falls vorhanden
                vm.GetType().GetProperty("RenderFrameWithWindow")?.SetValue(vm, renderer);
                vm.GetType().GetProperty("DefaultWindowCenter")?.SetValue(vm, wc);
                vm.GetType().GetProperty("DefaultWindowWidth")?.SetValue(vm, ww);
            }
            catch { /* still */ }
        }
    }
}
