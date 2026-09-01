using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>Baseline tolerance (in font sizes) under which a whitespace-only
    /// streamed line counts as sitting ON a text line's baseline — a drawn space
    /// glyph beside the text (4088's "French" + " " + ":"), not a pad row.</summary>
    private const double SameBaselineTol = 0.05;

    /// <summary>True when some line of the page holds two or more tracked show
    /// runs and its letters are RTL-dominant — the case the geometric RTL row
    /// rebuild exists for, whether or not the page otherwise needs sorting.</summary>
    private bool HasRtlMultiSpanLine(string[] lines, int textStartOffset)
    {
        if (_pageRunSpans.Count < 2) return false;
        var off = textStartOffset;
        foreach (var ln in lines)
        {
            int lo = off, hi = off + ln.Length;
            off += ln.Length + 1;
            var spans = 0;
            foreach (var s in _pageRunSpans)
                if (s.Offset >= lo && s.Offset + s.Len <= hi && ++spans > 1) break;
            if (spans < 2) continue;
            int rtl = 0, ltr = 0;
            foreach (var ch in ln)
            {
                if (BidiReorderer.IsRtlChar(ch)) rtl++;
                else if (char.IsLetter(ch)) ltr++;
            }
            if (rtl > ltr && rtl > 0) return true;
        }
        return false;
    }

    /// <summary>A row segment made only of space glyphs (drawn spaces, or the
    /// line-end sentinel a kept single space glyph carries through the sort).</summary>
    private static bool IsSpaceGlyphSegment(string s)
    {
        if (s.Length == 0) return false;
        foreach (var c in s)
            if (c != ' ' && c != EolShowSpaceSentinel && c != '\r') return false;
        return true;
    }

    /// <summary>
    /// Apply RTL reversal to a decoded string from a single Tj/TJ operator.
    /// If the string consists entirely of RTL characters and neutral punctuation/whitespace,
    /// returns the string reversed so that visual-order Hebrew/Arabic becomes logical order.
    /// Otherwise returns the string unchanged.
    /// </summary>
    private static string ApplyRtlIfPureRtl(string text) =>
        IsPureRtlRun(text) ? new string(text.ToCharArray().Reverse().ToArray()) : text;

    /// <summary>True when the run consists of RTL characters plus neutral punctuation and
    /// whitespace only (with at least one RTL char) — the condition under which
    /// <see cref="ApplyRtlIfPureRtl"/> reverses it. The test is char-class based, so it is
    /// invariant under reversal: applied to an already-reversed run it still answers
    /// "was this run reversed at decode time".</summary>
    private static bool IsPureRtlRun(string text)
    {
        if (text.Length == 0) return false;
        bool hasRtl = false;
        foreach (char c in text)
        {
            if (BidiReorderer.IsRtlChar(c))
                hasRtl = true;
            else if (!IsRtlNeutral(c))
                return false; // LTR character found
        }
        return hasRtl;
    }

    private static bool IsRtlNeutral(char c) =>
        c == ' ' || c == '\t' || c == '\n' || c == '\r'
        || (c >= '!' && c <= '/')   // !"#$%&'()*+,-./
        || (c >= ':' && c <= '@')   // :;<=>?@
        || (c >= '[' && c <= '`')   // [\]^_`
        || (c >= '{' && c <= '~');  // {|}~
}
