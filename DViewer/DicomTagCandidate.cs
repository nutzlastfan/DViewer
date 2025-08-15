// DicomTagCandidate.cs
using FellowOakDicom;

namespace DViewer;

public sealed class DicomTagCandidate
{
    public DicomTag Tag { get; }
    public string TagId { get; }  // "(0008,0020)"
    public string Name { get; }   // "Study Date"
    public string Vr { get; }     // "DA" etc.

    public DicomTagCandidate(DicomDictionaryEntry e)
    {
        Tag = e.Tag;
        TagId = e.Tag.ToString();               // "(gggg,eeee)"
        Name = string.IsNullOrWhiteSpace(e.Name) ? e.Keyword : e.Name;
        Vr = (e.ValueRepresentations?.FirstOrDefault()?.Code) ?? string.Empty;
    }

    public override string ToString() => $"{TagId}  {Name}  [{Vr}]";
}