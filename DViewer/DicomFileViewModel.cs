using System.Collections.Generic;
using Microsoft.Maui.Controls;

namespace DViewer
{
    public class DicomFileViewModel
    {
        public string FileName { get; set; } = string.Empty;

        // kleine Vorschau (kann null sein)
        public ImageSource? Image { get; set; }

        // unveränderliche Momentaufnahme der Metadaten für diese Datei
        public List<DicomMetadataItem> Metadata { get; private set; } = new();

        public void SetMetadata(IEnumerable<DicomMetadataItem> items)
        {
            // defensiv kopieren, nie Referenzen nach außen teilen
            Metadata = new List<DicomMetadataItem>(items ?? []);
        }
    }
}
