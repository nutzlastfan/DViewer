using FellowOakDicom.Imaging.Codec;
using FellowOakDicom;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DViewer
{
    public static class HelperFunctions
    {


        public static DicomFile EnsureUncompressed(DicomFile file)
        {
            var srcSyntax = file.FileMetaInfo.TransferSyntax;
            if (srcSyntax.IsEncapsulated)
            {
                DicomTransferSyntax targetSyntax = DicomTransferSyntax.ExplicitVRLittleEndian;
                var transcoderManager = new DefaultTranscoderManager();
                if (!transcoderManager.CanTranscode(srcSyntax, targetSyntax))
                    targetSyntax = DicomTransferSyntax.ImplicitVRLittleEndian;

                var transcoder = new DicomTranscoder(srcSyntax, targetSyntax);
                file = transcoder.Transcode(file);
            }

            return file;
        }

        public static IEnumerable<DicomTagCandidate> GetMissingPublicTags(HashSet<string> alreadyUsedTagIds)
        {
            // VRs, die wir standardmäßig NICHT anbieten wollen (Binär, SQ etc.)
            var skipVr = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "SQ","OB","OW","OF","OD","OL","UN","UT" };

            foreach (var entry in DicomDictionary.Default) // IEnumerable<DicomDictionaryEntry>
            {
                if (entry.Tag.IsPrivate) continue;

                // Gruppenelement 0000 = "Group Length" ausklammern
                if (entry.Tag.Element == 0x0000) continue;

                var candidate = new DicomTagCandidate(entry);

                if (skipVr.Contains(candidate.Vr)) continue;

                if (!alreadyUsedTagIds.Contains(candidate.TagId))
                    yield return candidate;
            }
        }

        public static class DicomFormat
        {
            public static DateTime? ParseDA(string s)
            {
                s = s?.Trim();
                if (string.IsNullOrEmpty(s)) return null;

                // exakt 8 Ziffern
                if (!Regex.IsMatch(s, @"^\d{8}$")) return null;

                return DateTime.TryParseExact(
                    s, "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dt)
                    ? dt
                    : null;
            }

            public static string FormatDA(DateTime dt) =>
                dt.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            // DICOM TM: HH, HHmm, HHmmss, HHmmss.FFFFFF (1–6 Fraction)
            static readonly Regex _tmRx = new(
                @"^(?<hh>\d{2})(?:(?<mm>\d{2})(?:(?<ss>\d{2})(?:\.(?<frac>\d{1,6}))?)?)?$",
                RegexOptions.Compiled);

            public static TimeSpan? ParseTM(string s)
            {
                s = s?.Trim();
                if (string.IsNullOrEmpty(s)) return null;

                var m = _tmRx.Match(s);
                if (!m.Success) return null;

                int hh = int.Parse(m.Groups["hh"].Value);
                int mm = m.Groups["mm"].Success ? int.Parse(m.Groups["mm"].Value) : 0;
                int ss = m.Groups["ss"].Success ? int.Parse(m.Groups["ss"].Value) : 0;

                if (hh is < 0 or > 23 || mm is < 0 or > 59 || ss is < 0 or > 59)
                    return null;

                long micro = 0;
                if (m.Groups["frac"].Success)
                {
                    // auf 6 Stellen auffüllen/abschneiden → Mikrosekunden
                    var frac = m.Groups["frac"].Value.PadRight(6, '0');
                    micro = long.Parse(frac[..6]);            // 0..999999 µs
                }

                // 1 µs = 10 Ticks
                return new TimeSpan(hh, mm, ss) + TimeSpan.FromTicks(micro * 10);
            }

            // Standardformat ohne Fraction (TimePicker liefert ohnehin ganze Sekunden)
            public static string FormatTM(TimeSpan t) =>
                t.ToString(@"hhmmss", CultureInfo.InvariantCulture);


            public struct PNParts
            {
                public string Family;   // Nachname
                public string Given;    // Vorname
                public string Middle;   // Mittelname
                public string Prefix;   // Präfix (Dr., Prof., …)
                public string Suffix;   // Suffix (Jr., III, …)
            }

            public static PNParts ParsePNAlphabetic(string s)
            {
                var parts = new PNParts();
                if (string.IsNullOrWhiteSpace(s)) return parts;

                // multi-valued (\) -> nimm ersten Wert
                var first = s.Split('\\')[0];

                // nur die Alphabetic-Gruppe vor '='
                var alpha = first.Split('=')[0];

                var segs = alpha.Split('^');
                parts.Family = segs.Length > 0 ? segs[0] : string.Empty;
                parts.Given = segs.Length > 1 ? segs[1] : string.Empty;
                parts.Middle = segs.Length > 2 ? segs[2] : string.Empty;
                parts.Prefix = segs.Length > 3 ? segs[3] : string.Empty;
                parts.Suffix = segs.Length > 4 ? segs[4] : string.Empty;
                return parts;
            }

            // Trailing-Empties auslassen (DICOM-konform)
            public static string FormatPNAlphabetic(PNParts p)
            {
                string[] segs =
                {
        p.Family?.Trim() ?? string.Empty,
        p.Given?.Trim()  ?? string.Empty,
        p.Middle?.Trim() ?? string.Empty,
        p.Prefix?.Trim() ?? string.Empty,
        p.Suffix?.Trim() ?? string.Empty
    };

                // trailing leere Felder abschneiden
                int last = segs.Length - 1;
                while (last >= 0 && string.IsNullOrEmpty(segs[last])) last--;
                if (last < 0) return string.Empty;

                return string.Join("^", segs.Take(last + 1));
            }


        }


        // --- VR/Tag-Validator -------------------------------------------------------
        public static class DicomValueValidator
        {
            // Tags, die nur >= 0 zulassen (typische Kandidaten)
            static readonly HashSet<string> NonNegativeTagIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "(0020,0011)", // Series Number (IS)
        "(0020,0012)", // Acquisition Number (IS)
        "(0020,0013)", // Instance Number (IS)
        "(0028,0010)", // Rows (US/IS -> hier String)
        "(0028,0011)", // Columns
        "(0008,0000)", // Group Length
        "(0028,0100)", // Bits Allocated
        "(0028,0101)", // Bits Stored
        "(0028,0102)", // High Bit
        "(0028,0103)", // Pixel Representation
    };

            public static bool IsValidValue(string vr, string tagId, string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return true;

                // Mehrfachwerte per "\" getrennt
                foreach (var part in value.Split('\\'))
                {
                    var s = part.Trim();
                    if (s.Length == 0) continue;
                    if (!IsValidSingle(vr, tagId, s)) return false;
                }
                return true;
            }

            static bool IsValidSingle(string vr, string tagId, string s)
            {
                switch ((vr ?? "").Trim().ToUpperInvariant())
                {
                    case "IS": // Integer String
                        {
                            if (!long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
                                return false;
                            if (NonNegativeTagIds.Contains(tagId) && n < 0) return false;
                            return true;
                        }

                    case "DS": // Decimal String
                        {
                            if (s.Contains(',')) return false; // falscher Dezimaltrenner
                                                               // Dezimalzahl mit optionalem Vorzeichen / Exponent
                            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                                return false;
                            if (NonNegativeTagIds.Contains(tagId) && d < 0) return false;
                            return true;
                        }

                    case "DA": // Date YYYYMMDD
                        {
                            if (s.Length != 8 || !s.All(char.IsDigit)) return false;
                            var y = int.Parse(s.Substring(0, 4));
                            var m = int.Parse(s.Substring(4, 2));
                            var d = int.Parse(s.Substring(6, 2));
                            return m is >= 1 and <= 12 && d is >= 1 and <= 31;
                        }

                    case "TM": // HHMMSS(.ffffff) (Teile optional)
                        {
                            // HH
                            if (s.Length < 2 || !s.Take(2).All(char.IsDigit)) return false;
                            int hh = int.Parse(s.Substring(0, 2));
                            if (hh < 0 || hh > 23) return false;

                            if (s.Length >= 4)
                            {
                                if (!s.Substring(2, 2).All(char.IsDigit)) return false;
                                int mm = int.Parse(s.Substring(2, 2));
                                if (mm < 0 || mm > 59) return false;
                            }
                            if (s.Length >= 6)
                            {
                                if (!s.Substring(4, 2).All(char.IsDigit)) return false;
                                int ss = int.Parse(s.Substring(4, 2));
                                if (ss < 0 || ss > 59) return false;
                            }
                            if (s.Length > 6)
                            {
                                if (s[6] != '.') return false;
                                var frac = s.Substring(7);
                                if (frac.Length == 0 || frac.Length > 6 || !frac.All(char.IsDigit)) return false;
                            }
                            return true;
                        }

                    case "DT": // sehr locker prüfen (YYYYMMDD[HH[MM[SS[.ffffff]]]][±ZZZZ])
                        {
                            // Grundteil
                            var main = s;
                            var tz = "";
                            var plus = s.IndexOf('+'); var minus = s.IndexOf('-');
                            var tzIdx = (plus >= 0 && minus >= 0) ? Math.Min(plus, minus) : Math.Max(plus, minus);
                            if (tzIdx > 0) { main = s.Substring(0, tzIdx); tz = s.Substring(tzIdx + 1); }
                            // Fraction
                            var fracIdx = main.IndexOf('.');
                            if (fracIdx >= 0)
                            {
                                var frac = main[(fracIdx + 1)..];
                                if (frac.Length == 0 || frac.Length > 6 || !frac.All(char.IsDigit)) return false;
                                main = main.Substring(0, fracIdx);
                            }
                            if (!main.All(char.IsDigit)) return false;
                            if (!(main.Length == 4 || main.Length == 6 || main.Length == 8 || main.Length == 10 || main.Length == 12 || main.Length == 14))
                                return false;
                            if (tz.Length > 0 && tz.Length != 4) return false;
                            return true;
                        }

                    case "UI": // OID: Ziffern und Punkte, keine führenden/folgenden Punkte, keine doppelten Punkte
                        {
                            if (!s.All(ch => char.IsDigit(ch) || ch == '.')) return false;
                            if (s.StartsWith('.') || s.EndsWith('.') || s.Contains("..")) return false;
                            return true;
                        }

                    case "CS": // Code String: Großbuchstaben, Ziffern, Unterstrich, Leerzeichen
                        return s.All(ch => ch == ' ' || ch == '_' || (ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'Z'));

                    case "PN":
                        // max 3 Gruppen, je 5 Komponenten – einfache Strukturprüfung
                        var v = (s ?? "");
                        var groups = v.Split('=');
                        if (groups.Length > 3) return false;
                        foreach (var g in groups)
                            if (g.Split('^').Length > 5) return false;
                        return true;

                    default:
                        return true; // für andere VRs vorerst nicht strenger prüfen
                }
            }
        }


    }
}
