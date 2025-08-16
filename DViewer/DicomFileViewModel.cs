using System.Collections.Generic;
using Microsoft.Maui.Controls;

namespace DViewer
{
    public sealed class DicomFileViewModel
    {
        public string FileName { get; set; } = string.Empty;

        // Vorschau (kann null sein)
        public ImageSource? Image { get; set; }

        // Metadaten (read-only Property; intern setzbar)
        public IReadOnlyList<DicomMetadataItem> Metadata { get; private set; } = new List<DicomMetadataItem>();

        public void SetMetadata(IEnumerable<DicomMetadataItem> items)
        {
            Metadata = items is IReadOnlyList<DicomMetadataItem> ro
                ? ro
                : new List<DicomMetadataItem>(items);
        }
    }
}
