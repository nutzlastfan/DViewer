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
using System.Globalization;

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

                        // kein Frames-Rendering für echte Video-TS
                        TrySetFramesOnVm(vm, null, null);
                        return vm;
                    }

                    // 3) Kein „echtes“ Video – cine per Provider (on-demand, LRU)
                    var ts = ds.InternalTransferSyntax ?? DicomTransferSyntax.ImplicitVRLittleEndian;
                    var px = DicomPixelData.Create(ds);
                    if (px != null && px.NumberOfFrames > 1 && !IsVideoTransferSyntax(ts))
                    {
                        var (count, provider, prefetch) = MakeFrameProvider(ds);
                        var fps = GetFps(ds);
                        TrySetFpsOnVm(vm, fps);

                        if (count > 0)
                        {
                            vm.Image = provider(0);
                            TrySetFrameProviderOnVm(vm, count, provider, prefetch);
                        }
                        else
                        {
                            vm.Image = BuildPreviewImageSource(ds);
                            TrySetFrameProviderOnVm(vm, 0, _ => null, _ => { });
                        }
                    }
                    else
                    {
                        // 4) Einzelbild
                        vm.Image = BuildPreviewImageSource(ds);
                        TrySetFrameProviderOnVm(vm, 0, _ => null, _ => { });
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

                if (item is DicomOtherByteFragment frag)
                {
                    if (frag.Fragments.Count == 1)
                        bytes = frag.Fragments[0]?.Data;
                    else if (frag.Fragments.Count > 1)
                        bytes = new CompositeByteBuffer(frag.Fragments.ToArray()).Data;
                }
                else if (item is DicomElement el)
                {
                    bytes = el.Buffer?.Data;
                }

                if (bytes is null || bytes.Length == 0)
                {
                    var px = DicomPixelData.Create(ds);
                    if (px != null && px.NumberOfFrames > 0)
                    {
                        var buf = px.GetFrame(0);
                        bytes = buf?.Data;
                    }
                }

                if (bytes is null || bytes.Length == 0) return;

                var ext = GetVideoExtension(ts!);
                mime = GetVideoMime(ts!);

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

        // --- NEU: LRU-Frame-Provider (on-demand Rendering + kleiner Cache)
        private static (int count, Func<int, ImageSource?> provider, Action<int> prefetch) MakeFrameProvider(DicomDataset ds)
        {
            var px = DicomPixelData.Create(ds);
            var count = px?.NumberOfFrames ?? 0;
            if (count <= 0) return (0, _ => null, _ => { });

            const int CACHE_CAP = 24;
            var cacheList = new LinkedList<int>();
            var cacheMap = new Dictionary<int, byte[]>(capacity: CACHE_CAP);

            var fastPng = new PngEncoder
            {
                CompressionLevel = PngCompressionLevel.NoCompression
            };

            byte[] RenderBytes(int index)
            {
                var diLocal = new DicomImage(ds);
                using var img = diLocal.RenderImage(index);
                using var sharp = img.AsSharpImage();
                using var ms = new MemoryStream();
                sharp.Save(ms, fastPng);
                return ms.ToArray();
            }

            ImageSource FromBytes(byte[] bytes) =>
                ImageSource.FromStream(() => new MemoryStream(bytes));

            var q = new System.Collections.Concurrent.ConcurrentQueue<int>();
            var enq = new System.Collections.Concurrent.ConcurrentDictionary<int, byte>();
            var cts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    if (!q.TryDequeue(out var idx))
                    {
                        await Task.Delay(5, cts.Token);
                        continue;
                    }
                    enq.TryRemove(idx, out _);

                    lock (cacheMap)
                    {
                        if (cacheMap.ContainsKey(idx)) continue;
                    }

                    try
                    {
                        var bytes = RenderBytes(idx);
                        lock (cacheMap)
                        {
                            if (!cacheMap.ContainsKey(idx))
                            {
                                cacheMap[idx] = bytes;
                                cacheList.AddFirst(idx);
                                if (cacheList.Count > CACHE_CAP)
                                {
                                    var drop = cacheList.Last!.Value;
                                    cacheList.RemoveLast();
                                    cacheMap.Remove(drop);
                                }
                            }
                        }
                    }
                    catch { /* still */ }
                }
            }, cts.Token);

            ImageSource? Provider(int index)
            {
                if (index < 0 || index >= count) return null;

                lock (cacheMap)
                {
                    if (cacheMap.TryGetValue(index, out var cached))
                    {
                        var node = cacheList.Find(index);
                        if (node != null) { cacheList.Remove(node); cacheList.AddFirst(node); }
                        return FromBytes(cached);
                    }
                }

                var bytesNow = RenderBytes(index);
                lock (cacheMap)
                {
                    cacheMap[index] = bytesNow;
                    cacheList.AddFirst(index);
                    if (cacheList.Count > CACHE_CAP)
                    {
                        var drop = cacheList.Last!.Value;
                        cacheList.RemoveLast();
                        cacheMap.Remove(drop);
                    }
                }
                return FromBytes(bytesNow);
            }

            void Prefetch(int index)
            {
                const int AHEAD = 12;
                for (int i = 1; i <= AHEAD; i++)
                {
                    int f = index + i;
                    if (f >= count) break;

                    lock (cacheMap)
                    {
                        if (cacheMap.ContainsKey(f)) continue;
                    }
                    if (enq.TryAdd(f, 0))
                        q.Enqueue(f);
                }
            }

            return (count, Provider, Prefetch);
        }

        // --- Saubere VM-Anbindung: bevorzugt Interfaces

        private static void TrySetFrameProviderOnVm(object vm, int frameCount, Func<int, ImageSource?> provider, Action<int> prefetch)
        {
            // 1) Sauber: Interface
            if (vm is IFrameProviderSink sink)
            {
                sink.SetFrameProvider(frameCount, provider, prefetch);
                return;
            }

            // 2) Fallback: exakt typisierte Reflection (vermeidet AmbiguousMatch)
            try
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var funcType = typeof(Func<,>).MakeGenericType(typeof(int), typeof(ImageSource));
                var actionType = typeof(Action<>).MakeGenericType(typeof(int));

                var t = vm.GetType();

                // (int, Func<int,ImageSource>, Action<int>)
                var mi = t.GetMethod("SetFrameProvider", F, binder: null,
                                     types: new[] { typeof(int), funcType, actionType }, modifiers: null);
                if (mi != null) { mi.Invoke(vm, new object?[] { frameCount, provider, prefetch }); return; }

                // (int, Func<int,ImageSource>)
                mi = t.GetMethod("SetFrameProvider", F, binder: null,
                                 types: new[] { typeof(int), funcType }, modifiers: null);
                if (mi != null) { mi.Invoke(vm, new object?[] { frameCount, provider }); return; }
            }
            catch { /* ignore */ }

            // 3) Fallback: Properties
            try
            {
                vm.GetType().GetProperty("FrameCount")?.SetValue(vm, frameCount);
                vm.GetType().GetProperty("GetFrameImageSource")?.SetValue(vm, provider);
                vm.GetType().GetProperty("PrefetchFrames")?.SetValue(vm, prefetch);
            }
            catch { /* still */ }
        }

        private static bool IsVideoTransferSyntax(DicomTransferSyntax ts)
        {
            var uid = ts.UID?.UID ?? string.Empty;

            if (uid == "1.2.840.10008.1.2.4.100" || uid == "1.2.840.10008.1.2.4.101")
                return true; // MPEG-2

            if (uid.StartsWith("1.2.840.10008.1.2.4.10", StringComparison.Ordinal))
                return true; // MPEG-4 AVC/H.264 Familie

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
                return ".mpg";

            if (uid.StartsWith("1.2.840.10008.1.2.4.10", StringComparison.Ordinal))
                return ".h264";

            if (uid.StartsWith("1.2.840.10008.1.2.4.107", StringComparison.Ordinal) ||
                uid.StartsWith("1.2.840.10008.1.2.4.108", StringComparison.Ordinal) ||
                uid.StartsWith("1.2.840.10008.1.2.4.109", StringComparison.Ordinal))
                return ".h265";

            return ".bin";
        }

        private static string GetVideoMime(DicomTransferSyntax ts)
        {
            var uid = ts.UID?.UID ?? string.Empty;

            if (uid == "1.2.840.10008.1.2.4.100" || uid == "1.2.840.10008.1.2.4.101")
                return "video/mpeg";

            if (uid.StartsWith("1.2.840.10008.1.2.4.10", StringComparison.Ordinal))
                return "video/avc";

            if (uid.StartsWith("1.2.840.10008.1.2.4.107", StringComparison.Ordinal) ||
                uid.StartsWith("1.2.840.10008.1.2.4.108", StringComparison.Ordinal) ||
                uid.StartsWith("1.2.840.10008.1.2.4.109", StringComparison.Ordinal))
                return "video/hevc";

            return "application/octet-stream";
        }

        private static byte[] BufferToArray(IByteBuffer buf)
        {
            try
            {
                if (buf is MemoryByteBuffer mbb)
                    return mbb.Data;

                return buf.Data ?? Array.Empty<byte>();
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        // ---------- Multi-Frame (legacy prebuild – wird oben nicht mehr genutzt) ----------

        private static IReadOnlyList<ImageSource>? BuildFrameSources(DicomDataset ds, out double? fps)
        {
            fps = null;
            try
            {
                var px = DicomPixelData.Create(ds);
                if (px == null || px.NumberOfFrames <= 1 || !ds.Contains(DicomTag.PixelData))
                    return null;

                var ts = ds.InternalTransferSyntax ?? DicomTransferSyntax.ImplicitVRLittleEndian;
                if (IsVideoTransferSyntax(ts))
                    return null;

                fps = GetFps(ds);
                int count = Math.Min(px.NumberOfFrames, MAX_PREVIEW_FRAMES);

                var di = new DicomImage(ds);
                var list = new List<ImageSource>(count);

                for (int i = 0; i < count; i++)
                {
                    using var rendered = di.RenderImage(i);
                    using var sharp = rendered.AsSharpImage();
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
            var img = TryRenderFirstFrame(ds);
            if (img != null) return img;
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
                if (ds.TryGetValues<double>(DicomTag.WindowCenter, out var centers) &&
                    ds.TryGetValues<double>(DicomTag.WindowWidth, out var widths) &&
                    centers?.Length > 0 && widths?.Length > 0)
                {
                    var c = centers[0];
                    var w = Math.Max(1, widths[0]);
                    return (c, w);
                }

                if (ds.TryGetValues<string>(DicomTag.WindowCenter, out var centerStrs) &&
                    ds.TryGetValues<string>(DicomTag.WindowWidth, out var widthStrs) &&
                    centerStrs?.Length > 0 && widthStrs?.Length > 0)
                {
                    if (double.TryParse(centerStrs[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var c) &&
                        double.TryParse(widthStrs[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                    {
                        return (c, Math.Max(1, w));
                    }
                }

                var px = DicomPixelData.Create(ds);
                if (px != null)
                {
                    int bits = px.BitsStored;
                    bool signed = px.PixelRepresentation == PixelRepresentation.Signed;

                    double minRaw = signed ? -(1 << (bits - 1)) : 0;
                    double maxRaw = signed ? (1 << (bits - 1)) - 1 : (1 << bits) - 1;

                    ds.TryGetSingleValue<double>(DicomTag.RescaleSlope, out var slope);
                    ds.TryGetSingleValue<double>(DicomTag.RescaleIntercept, out var intercept);
                    if (slope == 0) slope = 1;

                    var min = (minRaw * slope) + intercept;
                    var max = (maxRaw * slope) + intercept;

                    var ww = Math.Max(1, max - min);
                    var wc = min + ww / 2.0;
                    return (wc, ww);
                }
            }
            catch
            {
                // bewusst leer
            }

            return (null, null);
        }

        private static double? GetFps(DicomDataset ds)
        {
            try
            {
                if (ds.TryGetSingleValue<double>(DicomTag.FrameTime, out var frameTimeMs) && frameTimeMs > 0.0)
                    return 1000.0 / frameTimeMs;
            }
            catch { }

            try
            {
                if (ds.TryGetSingleValue<double>(DicomTag.CineRate, out var cine) && cine > 0.0)
                    return cine;
            }
            catch { }

            return null;
        }

        // ---------- optionale ViewModel-Integration ----------

        private static void TrySetFpsOnVm(object vm, double? fps)
        {
            try
            {
                vm.GetType().GetProperty("FramesPerSecond")?.SetValue(vm, fps);
            }
            catch { /* still */ }
        }

        private static void TrySetFramesOnVm(object vm, IReadOnlyList<ImageSource>? frames, double? fps)
        {
            try
            {
                var mi = vm.GetType().GetMethod("SetFrames", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    mi.Invoke(vm, new object?[] { frames, fps });
                }
            }
            catch { /* optional */ }
        }

        private static void TrySetVideoOnVm(object vm, string path, string? mime)
        {
            try
            {
                var mi = vm.GetType().GetMethod("SetVideo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    mi.Invoke(vm, new object?[] { path, mime });
                    return;
                }

                var pPath = vm.GetType().GetProperty("VideoTempPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var pMime = vm.GetType().GetProperty("VideoMime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                pPath?.SetValue(vm, path);
                pMime?.SetValue(vm, mime);

                var onPc = vm.GetType().GetMethod("OnPropertyChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                onPc?.Invoke(vm, new object?[] { "VideoTempPath" });
                onPc?.Invoke(vm, new object?[] { "VideoMime" });
                onPc?.Invoke(vm, new object?[] { "HasVideo" });
            }
            catch { /* optional */ }
        }

        private static Func<double, double, int, ImageSource> MakeWlRenderer(DicomDataset ds)
        {
            var di = new DicomImage(ds); // wiederverwendbar
            var fastPng = new PngEncoder
            {
                CompressionLevel = PngCompressionLevel.NoCompression
            };

            return (center, width, frame) =>
            {
                di.WindowCenter = center;
                di.WindowWidth = Math.Max(1, width);

                using var rendered = di.RenderImage(Math.Max(0, frame));
                using var sharp = rendered.AsSharpImage();
                using var ms = new MemoryStream();
                sharp.Save(ms, fastPng);
                var bytes = ms.ToArray();
                return ImageSource.FromStream(() => new MemoryStream(bytes));
            };
        }

        private static void TrySetWindowingOnVm(object vm,
            Func<double, double, int, ImageSource> renderer, double? wc, double? ww)
        {
            // 1) Sauber: Interface
            if (vm is IWindowingSink wls)
            {
                wls.SetWindowing(renderer, wc, ww);
                return;
            }

            // 2) Fallback: Methode oder Properties
            try
            {
                var mi = vm.GetType().GetMethod("SetWindowing",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null) { mi.Invoke(vm, new object?[] { renderer, wc, ww }); return; }

                vm.GetType().GetProperty("RenderFrameWithWindow")?.SetValue(vm, renderer);
                vm.GetType().GetProperty("DefaultWindowCenter")?.SetValue(vm, wc);
                vm.GetType().GetProperty("DefaultWindowWidth")?.SetValue(vm, ww);
            }
            catch { /* still */ }
        }
    }
}
