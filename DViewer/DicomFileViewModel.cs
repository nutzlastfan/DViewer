using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls;

namespace DViewer
{
    /// <summary>
    /// ViewModel für eine einzelne DICOM-Datei (eine Seite).
    /// </summary>
    public sealed class DicomFileViewModel : INotifyPropertyChanged, IFrameProviderSink, IWindowingSink
    {
        // --------- Datei / Preview ---------
        private string _fileName = string.Empty;
        public string FileName
        {
            get => _fileName;
            set { if (_fileName == value) return; _fileName = value ?? string.Empty; OnPropertyChanged(); }
        }

        private ImageSource? _image;
        public ImageSource? Image
        {
            get => _image;
            set { if (_image == value) return; _image = value; OnPropertyChanged(); }
        }

        // --------- Multiframe (cine) ---------
        public int FrameCount { get; private set; }

        /// <summary>On-demand Frame-Lieferant (vom Loader gesetzt).</summary>
        public Func<int, ImageSource?>? GetFrameImageSource { get; private set; }

        /// <summary>Optionaler Prefetch-Hinweis (vom Loader gesetzt).</summary>
        public Action<int>? PrefetchFrames { get; private set; }

        /// <summary>Meta: geschätzte Frames/Sekunde (vom Loader gesetzt).</summary>
        public double? FramesPerSecond { get; set; }   // bewusst public set: Loader setzt per Reflection

        // --------- Window/Level ---------
        /// <summary>WL-Renderer: (center,width,frame) -> ImageSource.</summary>
        public Func<double, double, int, ImageSource>? RenderFrameWithWindow { get; private set; }

        public double? DefaultWindowCenter { get; private set; }
        public double? DefaultWindowWidth { get; private set; }

        // IWindowingSink
        public void SetWindowing(Func<double, double, int, ImageSource> render, double? defaultCenter, double? defaultWidth)
        {
            RenderFrameWithWindow = render;
            DefaultWindowCenter = defaultCenter;
            DefaultWindowWidth = defaultWidth;

            OnPropertyChanged(nameof(RenderFrameWithWindow));
            OnPropertyChanged(nameof(DefaultWindowCenter));
            OnPropertyChanged(nameof(DefaultWindowWidth));
        }

        // IFrameProviderSink (einzige saubere Variante – keine Überlastung mehr!)
        public void SetFrameProvider(int frameCount, Func<int, ImageSource?> getFrame, Action<int>? prefetch = null)
        {
            // Multiframe aktiv -> Video deaktivieren
            VideoPath = null;
            VideoMime = null;

            FrameCount = frameCount < 0 ? 0 : frameCount;
            GetFrameImageSource = getFrame;
            PrefetchFrames = prefetch;

            OnPropertyChanged(nameof(FrameCount));
            OnPropertyChanged(nameof(GetFrameImageSource));
            OnPropertyChanged(nameof(PrefetchFrames));
            OnPropertyChanged(nameof(HasMultiFrame));
        }

        // --------- Media (Video) ---------
        private string? _videoPath;
        public string? VideoPath
        {
            get => _videoPath;
            private set
            {
                if (_videoPath == value) return;
                _videoPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasVideo));
                OnPropertyChanged(nameof(HasMultiFrame));
            }
        }

        private string? _videoMime;
        public string? VideoMime
        {
            get => _videoMime;
            private set { if (_videoMime == value) return; _videoMime = value; OnPropertyChanged(); }
        }

        /// <summary>True, wenn ein Video (MediaElement) angezeigt werden soll.</summary>
        public bool HasVideo => !string.IsNullOrEmpty(VideoPath);

        /// <summary>True, wenn mehrere Frames existieren und KEIN Video vorliegt.</summary>
        public bool HasMultiFrame => !HasVideo && FrameCount > 1;

        /// <summary>Setzt Video-Infos; setzt Multiframe-Daten zurück.</summary>
        public void SetVideo(string? path, string? mime = null)
        {
            // Video aktiv -> Multiframe deaktivieren
            FrameCount = 0;
            GetFrameImageSource = null;
            PrefetchFrames = null;

            VideoPath = path;
            VideoMime = mime;

            // Bei Video ist FPS Sache des MediaElements – FramesPerSecond für cine zurücksetzen
            FramesPerSecond = null;
            OnPropertyChanged(nameof(FramesPerSecond));
            OnPropertyChanged(nameof(FrameCount));
            OnPropertyChanged(nameof(GetFrameImageSource));
            OnPropertyChanged(nameof(PrefetchFrames));
            OnPropertyChanged(nameof(HasMultiFrame));
        }

        /// <summary>Alles Media-bezogene zurücksetzen.</summary>
        public void ResetMedia()
        {
            VideoPath = null;
            VideoMime = null;
            FrameCount = 0;
            GetFrameImageSource = null;
            PrefetchFrames = null;
            FramesPerSecond = null;

            OnPropertyChanged(nameof(FrameCount));
            OnPropertyChanged(nameof(GetFrameImageSource));
            OnPropertyChanged(nameof(PrefetchFrames));
            OnPropertyChanged(nameof(FramesPerSecond));
            OnPropertyChanged(nameof(HasVideo));
            OnPropertyChanged(nameof(HasMultiFrame));
        }

        // --------- Metadaten (seitenspezifisch) ---------
        public ObservableCollection<SideRow> Rows { get; } = new();

        public IReadOnlyList<DicomMetadataItem> Metadata { get; private set; } = Array.Empty<DicomMetadataItem>();

        public IReadOnlyDictionary<string, SideRow> RowMap => _rowMap;
        private readonly Dictionary<string, SideRow> _rowMap = new(StringComparer.OrdinalIgnoreCase);

        public void SetMetadata(IEnumerable<DicomMetadataItem> items)
        {
            if (items == null)
            {
                Clear();
                return;
            }

            var list = items
                .Where(m => !string.IsNullOrWhiteSpace(m.TagId))
                .OrderBy(m => m.TagId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Metadata = list;

            Rows.Clear();
            _rowMap.Clear();

            int i = 0;
            foreach (var m in list)
            {
                var row = new SideRow
                {
                    TagId = m.TagId,
                    Name = m.Name ?? string.Empty,
                    Vr = m.Vr ?? string.Empty,
                    Value = m.Value ?? string.Empty,
                    IsAlternate = (i++ % 2) == 1
                };

                Rows.Add(row);
                if (!_rowMap.ContainsKey(row.TagId))
                    _rowMap.Add(row.TagId, row);
            }

            OnPropertyChanged(nameof(Rows));
            OnPropertyChanged(nameof(Metadata));
        }

        public void Clear()
        {
            Metadata = Array.Empty<DicomMetadataItem>();
            Rows.Clear();
            _rowMap.Clear();
            Image = null;
            ResetMedia();

            OnPropertyChanged(nameof(Rows));
            OnPropertyChanged(nameof(Metadata));
        }

        // --------- INotifyPropertyChanged ---------
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
