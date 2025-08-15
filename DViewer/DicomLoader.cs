using FellowOakDicom.Imaging;
using FellowOakDicom;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SixLabors.ImageSharp;

namespace DViewer
{
    public interface IDicomLoader
    {
        System.Threading.Tasks.Task<DicomFileViewModel> LoadAsync(string path);
    }



    public class DicomLoader : IDicomLoader
    {
        public async System.Threading.Tasks.Task<DicomFileViewModel> LoadAsync(string path)
        {
            var dicomFile = await DicomFile.OpenAsync(path, FileReadOption.ReadAll);
            dicomFile = HelperFunctions.EnsureUncompressed(dicomFile);
            var dataset = dicomFile.Dataset;

            var metadataList = new List<DicomMetadataItem>();
            var binaryVrToSkip = new[] { DicomVR.OB, DicomVR.OW, DicomVR.OF, DicomVR.UN, DicomVR.UT };

            foreach (var item in dataset)
            {
                try
                {
                    if (item is DicomSequence seq)
                    {
                        var entry = DicomDictionary.Default[seq.Tag];
                        string name = entry?.Name ?? seq.Tag.ToString();
                        string tagId = seq.Tag.ToString();
                        string vr = seq.ValueRepresentation.Code;
                        string val = $"Sequence ({seq.Items.Count} items)";

                        metadataList.Add(new DicomMetadataItem
                        {
                            TagId = tagId,
                            Name = name,
                            Vr = vr,
                            Value = val
                        });
                    }
                    else if (item is DicomElement element)
                    {
                        var entry = DicomDictionary.Default[element.Tag];
                        string name = entry?.Name ?? element.Tag.ToString();
                        string tagId = element.Tag.ToString();
                        string vr = element.ValueRepresentation.Code;
                        string val;

                        if (element.Tag == DicomTag.PixelData)
                        {
                            val = "<Pixel Data>";
                        }
                        else if (binaryVrToSkip.Contains(element.ValueRepresentation))
                        {
                            val = $"<{element.ValueRepresentation.Code} data>";
                        }
                        else
                        {
                            try
                            {
                                if (element.Count > 1)
                                {
                                    var values = dataset.GetValues<string>(element.Tag);
                                    val = (values != null && values.Length > 0) ? string.Join("\\", values) : string.Empty;
                                }
                                else
                                {
                                    val = dataset.GetSingleValueOrDefault<string>(element.Tag, string.Empty);
                                }
                            }
                            catch
                            {
                                val = "<unreadable value>";
                            }
                        }

                        if (string.IsNullOrWhiteSpace(val))
                            continue;

                        metadataList.Add(new DicomMetadataItem
                        {
                            TagId = tagId,
                            Name = name,
                            Vr = vr,
                            Value = val
                        });
                    }
                }
                catch
                {
                    // einzelnes Tag überspringen
                }
            }

            // Pixeldata
            var pixelData = DicomPixelData.Create(dataset);
            var frame = pixelData.GetFrame(0);
            byte[] rawBytes = frame.Data;

            bool isSigned = pixelData.PixelRepresentation == PixelRepresentation.Signed;
            int bitsAllocated = pixelData.BitsAllocated;
            int width = pixelData.Width;
            int height = pixelData.Height;

            var dicomImage = new DicomImage(dataset);
            double windowCenter = dicomImage.WindowCenter;
            double windowWidth = dicomImage.WindowWidth;

            var photo = dataset.GetSingleValueOrDefault(DicomTag.PhotometricInterpretation, "MONOCHROME2");
            bool invert = photo == "MONOCHROME1";

            byte[] pixels8 = new byte[width * height];

            if (bitsAllocated == 16)
            {
                for (int i = 0; i < width * height; i++)
                {
                    int offset = i * 2;
                    int rawValue = isSigned
                        ? BitConverter.ToInt16(rawBytes, offset)
                        : BitConverter.ToUInt16(rawBytes, offset);

                    double lower = windowCenter - 0.5 - (windowWidth - 1) / 2.0;
                    double upper = windowCenter - 0.5 + (windowWidth - 1) / 2.0;
                    double display;
                    if (rawValue <= lower) display = 0;
                    else if (rawValue > upper) display = 255;
                    else
                        display = ((rawValue - (windowCenter - 0.5)) / (windowWidth - 1) + 0.5) * 255.0;

                    byte b = (byte)(Math.Clamp(display, 0, 255));
                    pixels8[i] = invert ? (byte)(255 - b) : b;
                }
            }
            else if (bitsAllocated == 8)
            {
                for (int i = 0; i < width * height; i++)
                {
                    byte b = rawBytes[i];
                    pixels8[i] = invert ? (byte)(255 - b) : b;
                }
            }
            else
            {
                throw new NotSupportedException($"BitsAllocated={bitsAllocated} nicht unterstützt.");
            }

            using var imageSharp = SixLabors.ImageSharp.Image.LoadPixelData<L8>(pixels8, width, height);
            using var temp = new MemoryStream();
            await imageSharp.SaveAsPngAsync(temp);
            byte[] imageBytes = temp.ToArray();
            var imageSource = ImageSource.FromStream(() => new MemoryStream(imageBytes));

            var vm = new DicomFileViewModel
            {
                FileName = Path.GetFileName(path),
                Image = imageSource
            };
            vm.SetMetadata(metadataList);


            //UpdateWindowTitle();

            return vm;
        }
//        private void UpdateWindowTitle()
//        {
//#if WINDOWS || MACCATALYST
//            // immer auf dem UI-Thread arbeiten
//            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
//            {
//                try
//                {
//                    var win = Application.Current?.Windows?.FirstOrDefault();
//                    if (win == null) return;

//                    var left = Left?.FileName ?? "—";
//                    var right = Right?.FileName ?? "—";
//                    var title = $"DViewer  —  Links: {left}   |   Rechts: {right}";

//#if WINDOWS
//                    // WinUI-spezifisch absichern
//                    if (win?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWin)
//                    {
//                        nativeWin.Title = title;
//                    }
//                    else
//                    {
//                        // Fallback (MAUI abstrahiert das ab .NET 8 teilweise)
//                        win.Title = title;
//                    }
//#else
//            win.Title = title;
//#endif
//                }
//                catch
//                {
//                    // niemals crashen, wenn Titel setzen fehlschlägt
//                }
//            });
//#endif
//        }

    }
}
