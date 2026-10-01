using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // A note's text is set smaller than the body by at least this many points.
    private const double NoteSizeStep = 0.5;

    // A note opens with its label - a number (not a longer figure) or a reference mark - then
    // its text; a label set as a superscript a little left of the text is a line of its own,
    // ending at the label (or at another mark set beside it).
    private static readonly Regex NoteLabel = new(@"^\s*(?:\d{1,3}(?!\d)|[^\sA-Za-z0-9]{1,3})", RegexOptions.Compiled);

    /// <summary>Whether a line is small print at the foot of its column: set smaller than the
    /// body, with no body text below it in the column, and in no box ruled round text (a box's small
    /// print is its own text). A note's label there opens no list.</summary>
    private static bool IsFootLine(PageAnalysisState pa, Line line)
        => line.Size <= pa.bodySize - NoteSizeStep
           && !pa.frames.Any(f => InsideBox(line, f.LLX, f.LLY, f.URX, f.URY))
           && !pa.lines.Any(l => l.Running == 0 && l.Frags.Count > 0 && l.ColumnRight == line.ColumnRight
                                 && Math.Abs(l.Size - pa.bodySize) <= NoteSizeStep && l.Y < line.Y);

    // A footnote's own label: a number, maybe with a full stop, then a space.
    private static readonly Regex NoteNumber = new(@"^\d{1,3}\.?\s", RegexOptions.Compiled);

    /// <summary>Whether a small-print line at the foot of its column ends the footnote above it: it starts
    /// left of all the note's lines (a legend under the notes), or it opens the next footnote - it starts
    /// with a note's number, and the footnotes' hanging indent is set in spaces: the line before it, a
    /// wrapped line of the note above, starts with more spaces, or opens a one-line note of its own just
    /// as this one does.</summary>
    private static bool EndsNote(PageAnalysisState pa, Line line)
    {
        if (pa.prev is not { Frags.Count: > 0 } prev || line.Frags.Count == 0 || !IsFootLine(pa, line)) return false;
        if (pa.paraLines is { Count: >= 2 } lines && lines.All(l => Math.Abs(l.MinX - lines[0].MinX) <= AlignTolerance)
            && lines[0].MinX - line.MinX > IndentStep)
            return true;
        // (a note's number raised before its text, in a show of its own, stands with no blank in the line's text)
        var text = string.Concat(line.Frags.Select(f => f.Text));
        var leading = text.Length - text.TrimStart().Length;
        if (!NoteNumber.IsMatch(text.TrimStart()) && !OpensWithRaisedMark(line)) return false;
        var before = string.Concat(prev.Frags.Select(f => f.Text));
        var beforeLeading = before.Length - before.TrimStart().Length;
        return beforeLeading > leading || (beforeLeading == leading && (NoteNumber.IsMatch(before.TrimStart()) || OpensWithRaisedMark(prev)));
    }

    /// <summary>Footnotes: the paragraphs at the foot of a column set smaller than the body,
    /// each opening with a note label, with no body text below them in their column. They are
    /// Note elements and read after the page's other blocks: a paragraph the page breaks goes
    /// on from the foot of the text, not from the foot of the notes. Small print below the notes
    /// (a legend of the marks the page uses) goes on being read after them.</summary>
    private static void MoveNotesLast(PageAnalysisState pa)
    {
        var notes = new List<int>();
        for (var i = 0; i < pa.result.Count; i++)
        {
            var b = pa.result[i];
            // (an entry of a table of contents is no note, however small and numbered)
            if (b.Kind != BlockKind.Paragraph || b.Toc || b.Lines is not { Count: > 0 } lines) continue;
            if (!lines.All(l => l.Size <= pa.bodySize - NoteSizeStep)) continue;
            if (!NoteLabel.IsMatch(string.Concat(lines[0].Frags.Select(f => f.Text)))) continue;
            if (!IsFootLine(pa, lines[0])) continue;
            b.Note = true;
            pa.result[i] = b;
            notes.Add(i);
        }
        if (notes.Count == 0) return;
        var lowestNote = notes.Min(i => pa.result[i].Y);
        for (var i = 0; i < pa.result.Count; i++)
        {
            var b = pa.result[i];
            if (b.Note || b.Kind != BlockKind.Paragraph || b.Lines is not { Count: > 0 } lines || lines[0].Y >= lowestNote) continue;
            if (lines.All(l => l.Size <= pa.bodySize - NoteSizeStep) && IsFootLine(pa, lines[0])) notes.Add(i);
        }
        var moved = notes.Select(i => pa.result[i]).ToList();
        var movedLines = notes.Select(i => pa.blockLines[i]).ToList();
        foreach (var i in notes.OrderByDescending(i => i))
        {
            pa.result.RemoveAt(i);
            pa.blockLines.RemoveAt(i);
        }
        pa.result.AddRange(moved);
        pa.blockLines.AddRange(movedLines);
    }
}
