using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Whether <paramref name="marker"/> occurs in the first <paramref name="limit"/>
    /// bytes of <paramref name="data"/> (a small forward scan; used for header-region markers).</summary>
    private static bool ContainsMarker(byte[] data, System.ReadOnlySpan<byte> marker, int limit)
    {
        for (int i = 0; i <= limit - marker.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < marker.Length; j++)
                if (data[i + j] != marker[j]) { match = false; break; }
            if (match) return true;
        }
        return false;
    }

    /// <summary>Collect every <see cref="Heading"/> across the document whose
    /// <see cref="Heading.TocPage"/> is <paramref name="tocPage"/>, paired with the
    /// 1-based destination page number shown in its TOC entry. A heading qualifies
    /// when it is flagged <see cref="Heading.IsInList"/> (added to a content page
    /// and listed in the TOC) or when it sits directly on the TOC page itself
    /// (the entries are authored on the TOC page). The destination number is the
    /// heading's <see cref="Heading.DestinationPage"/> index when set, otherwise
    /// the index of the page the heading sits on.</summary>
    /// <summary>Advance the per-level auto-sequence counters for a content
    /// heading and return its printed number prefix ("2  ", "b.a  ", …).
    /// The number is hierarchical: every ancestor level's counter joined with
    /// '.', each part formatted in THIS heading's style — this prints
    /// "b.a" for a LettersLowercase level-2 under the second level-1 heading.
    /// The DEFAULT style is NumeralsArabic; <see cref="NumberingStyle.None"/>
    /// prints NO number but still emits the separator, so a None-styled
    /// heading opens with the bare TWO spaces. The number is
    /// followed by TWO spaces (the standard prefix fragment). A level-N
    /// bump restarts the deeper sequences. StartNumber seeds a level's FIRST
    /// use only — a later heading continues the running sequence (printing
    /// "ii" for the second roman heading even when it asks
    /// for StartNumber=13).</summary>
    private static string NextHeadingPrefix(Dictionary<int, int> counters, Heading heading)
    {
        if (!heading.IsAutoSequence) return "";
        var lvl = heading.Level > 0 ? heading.Level : 1;
        var next = (counters.TryGetValue(lvl, out var c) ? c : heading.StartNumber - 1) + 1;
        counters[lvl] = next;
        var stale = new List<int>();
        foreach (var k in counters.Keys) if (k > lvl) stale.Add(k);
        foreach (var k in stale) counters.Remove(k);
        var parts = new List<string>();
        for (var k = 1; k <= lvl; k++)
            if (counters.TryGetValue(k, out var ck) && ck > 0)
                parts.Add(Heading.FormatNumber(heading.Style, ck));
        return parts.Count > 0 ? string.Join(".", parts) + "  " : "";
    }

    /// <summary>Measure TOC entry text with real Helvetica advances (a crude
    /// half-em estimate over-measures typical entry text and wraps lines that
    /// should stay whole, pushing the page number down a line). CJK
    /// ideographs / kana / fullwidth forms are full-width (1 em) in the CJK
    /// face substituted for them — measuring them as '?'
    /// under-counts the line and mis-sizes the dot leader.</summary>
    private static double MeasureEntry(string s, double fs, string face = "Helvetica")
    {
        double w = 0;
        foreach (var c in s)
        {
            double cw;
            if ((c >= 0x2E80 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0xFF00 && c <= 0xFF60))
                cw = 1000;
            else
                cw = Text.Standard14Fonts.GetWidth(face, c < 256 ? c : '?');
            if (cw < 0) cw = 500;
            w += cw * fs / 1000.0;
        }
        return w;
    }

    /// <summary>Standard-14 Helvetica variant for a TOC level format's font style.</summary>
    private static string EntryFace(Text.FontStyles style) => style switch
    {
        Text.FontStyles.Bold => "Helvetica-Bold",
        Text.FontStyles.Italic => "Helvetica-Oblique",
        Text.FontStyles.Bold | Text.FontStyles.Italic => "Helvetica-BoldOblique",
        _ => "Helvetica",
    };

    private void CollectTocHeadingsFrom(System.Collections.Generic.IEnumerable<BaseParagraph> paragraphs,
        Page tocPage, int pageIdx, bool isTocPage, System.Collections.Generic.List<(Heading, int)> result)
    {
        foreach (var p in paragraphs)
        {
            if (p is Heading h && ReferenceEquals(h.TocPage, tocPage) && (h.IsInList || isTocPage))
            {
                var destIdx = h.DestinationPage is not null ? Pages.IndexOf(h.DestinationPage) : pageIdx;
                if (destIdx <= 0) destIdx = pageIdx;
                result.Add((h, destIdx));
            }
            else if (p is FloatingBox fb)
                CollectTocHeadingsFrom(fb.Paragraphs, tocPage, pageIdx, isTocPage, result);
        }
    }
}
