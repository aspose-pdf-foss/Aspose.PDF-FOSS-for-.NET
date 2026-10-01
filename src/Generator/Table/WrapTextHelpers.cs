using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
// The helpers of the table text wrap: the width measure, starting a line with a word, and the zero-width split.
    // Measure with real Helvetica AFM widths instead of a flat 0.5 em
    // estimate — the old estimate let noticeably more characters per
    // line than GDI+ and under-counted page breaks for long cell text.
    // A caller that sized the column itself passes the very measure it used,
    // so the column and the wrap agree to the last bit.
    private static double WrapMeasureWidth(WrapTextState wt, string s, double sz) => wt.measure is null ? MeasureWidthDefault(s, sz) : wt.measure(s);

    // A single word wider than the column splits at character level
    // ("Jurisdiction" in a squeezed 24 pt column renders as
    // "Juris/dictio/n"), filling each line to the width. A hyphen or en-dash
    // inside the word is a soft break opportunity tried FIRST ("B13-9876"
    // wraps to "B13-"/"9876"); only a segment still too wide char-splits.
    private static void StartWithWord(WrapTextState wt, string word, double wordW)
    {
        // A column auto-fit to exactly this word's width must accept it — the
        // width comparison tolerates the last-bit error the pad add/subtract
        // round-trip introduces.
        if (wordW <= wt.availWidth + 1e-6) { wt.currentLine = word; wt.currentWidth = wordW; return; }
        // HTML layout: a word too wide for its column spills past the cell edge —
        // the column was sized knowing that, and breaking it would show a split
        // a browser never shows.
        if (wt.overflowLongWords) { wt.currentLine = word; wt.currentWidth = wordW; return; }
        if (word.IndexOf('-') > 0 || word.IndexOf('–') > 0)
        {
            var segs = new List<string>();
            var start = 0;
            for (var ci = 0; ci < word.Length; ci++)
                if (word[ci] is '-' or '–' || ci == word.Length - 1)
                {
                    segs.Add(word.Substring(start, ci - start + 1));
                    start = ci + 1;
                }
            if (segs.Count > 1)
            {
                wt.currentLine = ""; wt.currentWidth = 0;
                foreach (var seg in segs)
                {
                    var segW = WrapMeasureWidth(wt, seg, wt.fontSize);
                    if (wt.currentLine.Length == 0) { StartWithWord(wt, seg, segW); continue; }
                    if (wt.currentWidth + segW <= wt.availWidth + 1e-6)
                    {
                        wt.currentLine += seg;
                        wt.currentWidth += segW;
                    }
                    else
                    {
                        wt.lines.Add(wt.currentLine);
                        StartWithWord(wt, seg, segW);
                    }
                }
                return;
            }
        }
        var cur = ""; double cw = 0;
        foreach (var ch in word)
        {
            var chW = WrapMeasureWidth(wt, ch.ToString(), wt.fontSize);
            if (cur.Length > 0 && cw + chW > wt.availWidth + 1e-6)
            {
                wt.lines.Add(cur);
                cur = ""; cw = 0;
            }
            cur += ch; cw += chW;
        }
        wt.currentLine = cur;
        wt.currentWidth = cw;
    }

    // U+200B is invisible and carries no advance, but it IS a legal wrap point. A
    // word that will not fit whole is retried at its zero-width spaces, so a line
    // packs the way a browser packs it instead of pushing the whole run down.
    private static bool TryZeroWidthSplit(WrapTextState wt, string word)
    {
        if (word.IndexOf(ZeroWidthSpace) < 0) return false;
        var segs = new List<string>();
        var segStart = 0;
        for (var ci = 0; ci < word.Length; ci++)
            if (word[ci] == ZeroWidthSpace || ci == word.Length - 1)
            {
                segs.Add(word.Substring(segStart, ci - segStart + 1));
                segStart = ci + 1;
            }
        if (segs.Count < 2) return false;
        var needSpace = true;
        foreach (var seg in segs)
        {
            // A segment that is nothing but the break character carries no ink and
            // no box: breaking AT a zero-width space must not leave an empty line.
            if (seg.Trim(ZeroWidthSpace).Length == 0)
            {
                if (wt.currentLine.Length > 0) wt.currentLine += seg;
                continue;
            }
            var segW = WrapMeasureWidth(wt, seg, wt.fontSize);
            if (wt.currentLine.Length == 0) { StartWithWord(wt, seg, segW); needSpace = false; continue; }
            var add = (needSpace ? wt.spaceW : 0) + segW;
            if (wt.currentWidth + add <= wt.availWidth + 1e-6)
            {
                wt.currentLine += (needSpace ? " " : "") + seg;
                wt.currentWidth += add;
            }
            else
            {
                wt.lines.Add(wt.currentLine);
                wt.currentLine = ""; wt.currentWidth = 0;
                StartWithWord(wt, seg, segW);
            }
            needSpace = false;
        }
        return true;
    }
}
