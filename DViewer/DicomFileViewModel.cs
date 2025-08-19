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
    /// Enthält:
    /// - Dateiname
    /// - Vorschaubild (Image)
    /// - Metadaten (Rows + read-only Metadata)
    /// - Media-Infos: Video (.mp4/.mpg) ODER Multiframe (FrameCount + Frame-Bildprovider)
    /// </summary>
    public sealed class DicomFileViewModel : INotifyPropertyChanged
    {
        // --------- Datei / Preview ---------
        private string _fileName = string.Empty;
        public string FileName
        {
            get => _fileName;
            set { if (_fileName == value) return; _fileName = value ?? string.Empty; OnPropertyChanged(); }
        }

        private ImageSource? _image;
        /// <summary>Aktuell angezeigte Vorschaugrafik (z.B. Frame 0 oder selektierter Frame).</summary>
        public ImageSource? Image
        {
            get => _image;
            set { if (ReferenceEquals(_image, value)) return; _image = value; OnPropertyChanged(); }
        }

        // --------- Media (Video / Multiframe) ---------

        /// <summary>Pfad zu extrahiertem Videostream (z.B. .mp4). Null, wenn kein Video.</summary>
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
        private string? _videoPath;

        /// <summary>Optionales MIME des Videos (z.B. "video/mp4").</summary>
        public string? VideoMime
        {
            get => _videoMime;
            private set { if (_videoMime == value) return; _videoMime = value; OnPropertyChanged(); }
        }
        private string? _videoMime;

        /// <summary>Anzahl Frames (nur Multiframe, nicht Video).</summary>
        public int FrameCount
        {
            get => _frameCount;
            private set
            {
                if (_frameCount == value) return;
                _frameCount = value < 0 ? 0 : value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMultiFrame));
            }
        }
        private int _frameCount;

        /// <summary>Delegate: Liefert für einen Frameindex ein ImageSource (nur Multiframe).</summary>
        public Func<int, ImageSource?>? GetFrameImageSource
        {
            get => _getFrameImageSource;
            private set
            {
                if (_getFrameImageSource == value) return;
                _getFrameImageSource = value;
                OnPropertyChanged();
            }
        }
        private Func<int, ImageSource?>? _getFrameImageSource;

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

            VideoPath = path;
            VideoMime = mime;
        }

        /// <summary>Setzt Multiframe-Infos (Frame-Anzahl + Provider); setzt Video-Daten zurück.</summary>
        public void SetFrameProvider(int frameCount, Func<int, ImageSource?>? provider)
        {
            // Multiframe aktiv -> Video deaktivieren
            VideoPath = null;
            VideoMime = null;

            FrameCount = frameCount < 0 ? 0 : frameCount;
            GetFrameImageSource = provider;
        }

        /// <summary>Alles Media-bezogene zurücksetzen.</summary>
        public void ResetMedia()
        {
            VideoPath = null;
            VideoMime = null;
            FrameCount = 0;
            GetFrameImageSource = null;
        }

        // --------- Metadaten (seitenspezifisch) ---------

        /// <summary>Reine, seitenspezifische Zeilen zum Binden in der UI.</summary>
        public ObservableCollection<SideRow> Rows { get; } = new();

        /// <summary>Legacy: Roh-Metadaten wie vorher (nur lesen).</summary>
        public IReadOnlyList<DicomMetadataItem> Metadata { get; private set; } = Array.Empty<DicomMetadataItem>();

        /// <summary>Schnelles Lookup der Zeilen nach TagId.</summary>
        public IReadOnlyDictionary<string, SideRow> RowMap => _rowMap;
        private readonly Dictionary<string, SideRow> _rowMap = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Befüllt die Seite mit Metadaten (ersetzt den bisherigen Inhalt).</summary>
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

            // Rows neu aufbauen
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

        /// <summary>Setzt alles zurück (z.B. bei Fehlern).</summary>
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
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
