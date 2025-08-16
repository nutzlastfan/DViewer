using FellowOakDicom;
using FellowOakDicom.Imaging;


// ImageSharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
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

        //private static bool IsOverlayData(DicomItem it) =>
        //    it.Tag.Group >= 0x6000 && it.Tag.Group <= 0x60FF && it.Tag.Element == 0x3000;

        //private static string ReadValueSafe(DicomItem item)
        //{
        //    try
        //    {
        //        if (item.ValueRepresentation == DicomVR.SQ) return "[Sequence]";
        //        if (SkipTags.Contains(item.Tag) || IsOverlayData(item)) return "[Binary]";

        //        if (item is DicomElement el)
        //        {
        //            int count = el.Count;
        //            if (count <= 0) return string.Empty;

        //            int take = Math.Min(count, 16);
        //            var vals = new string[take];
        //            for (int i = 0; i < take; i++)
        //            {
        //                try { vals[i] = el.Get<string>(i) ?? string.Empty; }
        //                catch { vals[i] = string.Empty; }
        //            }
        //            if (count > take) vals[take - 1] += $" …(+{count - take})";
        //            return string.Join("\\", vals);
        //        }

        //        return "[Unsupported]";
        //    }
        //    catch
        //    {
        //        return string.Empty;
        //    }
        //}

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

        //private static readonly HashSet<DicomTag> SkipTags = new()
        //{
        //    DicomTag.PixelData,
        //    DicomTag.WaveformData,
        //    DicomTag.EncapsulatedDocument
        //};

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
            catch { return string.Empty; }
        }

        //public async Task<DicomFileViewModel?> PickAndLoadAsync(CancellationToken ct = default)
        //{
        //    try
        //    {
        //        var pick = await Microsoft.Maui.Storage.FilePicker.Default.PickAsync(
        //            new Microsoft.Maui.Storage.PickOptions { PickerTitle = "DICOM-Datei auswählen" });
        //        if (pick == null) return null;

        //        var path = pick.FullPath ?? pick.FileName;
        //        return await LoadAsync(path, ct).ConfigureAwait(false);
        //    }
        //    catch { return null; }
        //}

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
                                TagId = tag.ToString(),
                                Name = name,
                                Vr = vr,
                                Value = val
                            });
                        }
                        catch { /* Einzelnes Element überspringen */ }
                    }

                    vm.SetMetadata(list);

                    // ===== manuelle Vorschau (plattformübergreifend)
                    try
                    {
                        vm.Image = BuildPreviewImageSource(ds);
                    }
                    catch
                    {
                        vm.Image = null; // Vorschau optional
                    }
                }
                catch
                {
                    vm.SetMetadata(Array.Empty<DicomMetadataItem>());
                    vm.Image = null;
                }

                return vm;
            }, ct).ConfigureAwait(false);
        }

        // --------------------------------------------------------------------
        // Preview: DICOM -> ImageSharp -> MAUI ImageSource
        // --------------------------------------------------------------------
        private static ImageSource? BuildPreviewImageSource(DicomDataset ds)
        {
            if (!ds.Contains(DicomTag.PixelData)) return null;

            // Rohdaten besorgen
            var px = DicomPixelData.Create(ds);
            if (px == null || px.NumberOfFrames == 0) return null;

            // Frame 0
            var frame = px.GetFrame(0);                  // IByteBuffer
            var raw = frame.Data;                      // byte[]
            if (raw == null || raw.Length == 0) return null;

            int width = px.Width;
            int height = px.Height;

            // Zielbild (RGBA)
            using var img = new Image<Rgba32>(width, height);

            // Rescale (optional)
            double slope = GetDouble(ds, DicomTag.RescaleSlope) ?? 1.0;
            double intercept = GetDouble(ds, DicomTag.RescaleIntercept) ?? 0.0;

            // Windowing (optional, erste Werte verwenden)
            var (wc, ww) = GetWindow(ds);

            var photo = px.PhotometricInterpretation?.Value?.ToUpperInvariant() ?? "MONOCHROME2";
            int spp = px.SamplesPerPixel;
            int bits = px.BitsAllocated;
            // Enum: FellowOakDicom.Imaging.PlanarConfiguration
            var planarConfig = px.PlanarConfiguration;
            bool isPlanar = planarConfig == PlanarConfiguration.Planar; // true => RRR..GGG..BBB..

            // Helper für Min/Max-Stretch bei 16-bit wenn kein WC/WW gesetzt ist
            (ushort min16, ushort max16) = ComputeMinMax16(raw, bits, spp);

            if (spp == 1) // Grauwert
            {
                if (bits == 8)
                {
                    bool invert = photo.Contains("MONOCHROME1");
                    int index = 0;

                    img.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < height; y++)
                        {
                            var span = accessor.GetRowSpan(y);
                            for (int x = 0; x < width; x++)
                            {
                                byte g = raw[index++];
                                if (wc.HasValue && ww.HasValue)
                                    g = Window8(g, wc.Value, ww.Value, invert);
                                else if (invert)
                                    g = (byte)(255 - g);

                                span[x] = new Rgba32(g, g, g, 255);
                            }
                        }
                    });
                }
                else if (bits == 16)
                {
                    bool invert = photo.Contains("MONOCHROME1");
                    bool le = BitConverter.IsLittleEndian;

                    int index = 0;
                    img.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < height; y++)
                        {
                            var span = accessor.GetRowSpan(y);
                            for (int x = 0; x < width; x++)
                            {
                                // ushort in little-endian
                                ushort v = le
                                    ? (ushort)(raw[index] | (raw[index + 1] << 8))
                                    : (ushort)((raw[index] << 8) | raw[index + 1]);
                                index += 2;

                                // Rescale
                                double dv = v * slope + intercept;

                                byte g = (wc.HasValue && ww.HasValue)
                                    ? Window16To8(dv, wc.Value, ww.Value, invert)
                                    : Stretch16To8(v, min16, max16, invert);

                                span[x] = new Rgba32(g, g, g, 255);
                            }
                        }
                    });
                }
                else
                {
                    return null; // ungehandelt
                }
            }
            else if (spp == 3) // Farbe
            {
                if (photo.Contains("RGB"))
                {
                    if (bits != 8) return null;

                    if (!isPlanar) // interleaved RGBRGB...
                    {
                        int index = 0;
                        img.ProcessPixelRows(accessor =>
                        {
                            for (int y = 0; y < height; y++)
                            {
                                var span = accessor.GetRowSpan(y);
                                for (int x = 0; x < width; x++)
                                {
                                    byte r = raw[index++];
                                    byte g = raw[index++];
                                    byte b = raw[index++];
                                    span[x] = new Rgba32(r, g, b, 255);
                                }
                            }
                        });
                    }
                    else // planar: RRR.. GGG.. BBB..
                    {
                        int plane = width * height;
                        int rOff = 0;
                        int gOff = plane;
                        int bOff = plane * 2;

                        img.ProcessPixelRows(accessor =>
                        {
                            for (int y = 0; y < height; y++)
                            {
                                var span = accessor.GetRowSpan(y);
                                for (int x = 0; x < width; x++)
                                {
                                    int p = y * width + x;
                                    span[x] = new Rgba32(raw[rOff + p], raw[gOff + p], raw[bOff + p], 255);
                                }
                            }
                        });
                    }
                }
                else if (photo.Contains("YBR_FULL"))
                {
                    if (bits != 8) return null;

                    // interleaved Y Cb Cr
                    int index = 0;
                    img.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < height; y++)
                        {
                            var span = accessor.GetRowSpan(y);
                            for (int x = 0; x < width; x++)
                            {
                                int Y = raw[index++];       // 0..255
                                int Cb = raw[index++] - 128; // -128..127
                                int Cr = raw[index++] - 128;

                                int r = Clamp255(Y + (int)(1.402 * Cr));
                                int g = Clamp255(Y - (int)(0.344136 * Cb) - (int)(0.714136 * Cr));
                                int b = Clamp255(Y + (int)(1.772 * Cb));
                                span[x] = new Rgba32((byte)r, (byte)g, (byte)b, 255);
                            }
                        }
                    });
                }
                else
                {
                    return null; // andere Photometric nicht behandelt
                }
            }
            else
            {
                return null; // nicht unterstützt
            }


            // -> PNG in Stream, dann als ImageSource
            using var ms = new MemoryStream();
            img.Save(ms, new PngEncoder());
            var bytes = ms.ToArray();

            return ImageSource.FromStream(() => new MemoryStream(bytes));
        }

        // ----------- Helpers -------------------------------------------------

        private static (double? wc, double? ww) GetWindow(DicomDataset ds)
        {
            // Ersten Wert nehmen, falls multi-valued
            try
            {
                var centers = ds.GetValues<double>(DicomTag.WindowCenter);
                var widths = ds.GetValues<double>(DicomTag.WindowWidth);
                if (centers?.Length > 0 && widths?.Length > 0)
                    return (centers[0], widths[0]);
            }
            catch { /* egal */ }
            return (null, null);
        }

        private static double? GetDouble(DicomDataset ds, DicomTag tag)
        {
            try { return ds.GetSingleValue<double>(tag); } catch { return null; }
        }

        private static (ushort min, ushort max) ComputeMinMax16(byte[] raw, int bitsAllocated, int spp)
        {
            if (bitsAllocated != 16) return (0, 0);
            int stride = 2 * spp;
            ushort min = ushort.MaxValue, max = ushort.MinValue;
            for (int i = 0; i < raw.Length; i += stride)
            {
                ushort v = (ushort)(raw[i] | (raw[i + 1] << 8));
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (min == max) { min = 0; max = 0xFFFF; }
            return (min, max);
        }

        private static byte Stretch16To8(ushort v, ushort min, ushort max, bool invert)
        {
            if (max <= min) return (byte)(invert ? 255 : 0);
            int val = (int)((v - min) * 255.0 / (max - min));
            if (val < 0) val = 0; if (val > 255) val = 255;
            return invert ? (byte)(255 - val) : (byte)val;
        }

        private static byte Window16To8(double value, double wc, double ww, bool invert)
        {
            // DICOM Windowing (typisch)
            double lo = wc - 0.5 - (ww - 1) / 2.0;
            double hi = wc - 0.5 + (ww - 1) / 2.0;
            double y;
            if (value <= lo) y = 0;
            else if (value > hi) y = 255;
            else y = ((value - (wc - 0.5)) / (ww - 1) + 0.5) * 255.0;

            if (y < 0) y = 0; if (y > 255) y = 255;
            return invert ? (byte)(255 - (byte)y) : (byte)y;
        }

        private static byte Window8(byte g, double wc, double ww, bool invert)
        {
            // 8-bit: einfache WC/WW – skaliert den 0..255-Bereich
            double lo = wc - 0.5 - (ww - 1) / 2.0;
            double hi = wc - 0.5 + (ww - 1) / 2.0;
            double val = g;

            double y;
            if (val <= lo) y = 0;
            else if (val > hi) y = 255;
            else y = ((val - (wc - 0.5)) / (ww - 1) + 0.5) * 255.0;

            if (y < 0) y = 0; if (y > 255) y = 255;
            return invert ? (byte)(255 - (byte)y) : (byte)y;
        }

        private static int Clamp255(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);
    }
}
