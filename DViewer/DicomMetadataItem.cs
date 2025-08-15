namespace DViewer
{
    public class DicomMetadataItem
    {
        public string TagId { get; init; } = string.Empty; // z.B. (0008,0020)
        public string Name { get; init; } = string.Empty;
        public string Vr { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
    }
}
