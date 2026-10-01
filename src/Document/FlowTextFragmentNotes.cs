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
    private sealed partial class FlowLayout
    {
        /// <summary>After the lines are laid out: a foot- or end-note's superscript marker and queued body, the fragment's own hyperlink box, and the per-segment link boxes.</summary>
        private void WriteFragmentNotesAndLinks(FlowTextFragmentState wtf, Text.TextFragment tf)
        {
            // A FootNote / EndNote on the fragment: emit its superscript reference
            // marker right after the last laid-out glyph and queue the note body
            // for the band on this slot (foot) or the flow's last page (end).
            foreach (var (note, isEndNote) in new[] { (tf.FootNote, false), (tf.EndNote, true) })
            {
                if (note is null) continue;
                var marker = NextFootnoteMarker(note);
                var lastLine = wtf.allLines.Count > 0 ? wtf.allLines[^1] : string.Empty;
                var markerMeasurer = Text.TextPaginator.CreateMeasurer(wtf.baseFont, wtf.fontSize,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData);
                var markerBaseline = _lastBodyBaseline ?? wtf.nonEmbeddedLastBaseline;
                if (markerBaseline.HasValue && marker.Length > 0)
                {
                    var markerSize = wtf.fontSize * MarkerSizeRatio;
                    var markerState = new Text.TextState
                    {
                        Font = tf.TextState.Font,
                        FontData = tf.TextState.FontData,
                        ForegroundColor = note.TextState?.ForegroundColor,
                    };
                    var markerX = CurLeft + markerMeasurer(lastLine);
                    _pendingEmbeddedRenders.Add((_currentSlot,
                        markerX, 0, marker, markerState, markerSize,
                        markerBaseline.Value
                        + MarkerBaselineRise(wtf.baseFont, wtf.fontSize, wtf.lineHeight, markerSize)));
                    // The line's text top is its box bottom plus the font size; the
                    // marker hangs from it.
                    var markerLineTop = _curY + wtf.fontSize;
                    var markerW = Text.TextPaginator.CreateMeasurer(wtf.baseFont, markerSize,
                        tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData)(marker);
                    _noteMarkLine[note] = (_currentSlot, markerLineTop);
                    QueueNoteLink(note, markerX, markerLineTop, markerW, markerSize);
                }
                if (isEndNote) QueueEndNote(note, marker, wtf.fontSize);
                else QueueMarkedFootnote(note, marker, wtf.fontSize);
            }

            if (wtf.fragHyperlink is not null && wtf.allLines.Count > 0)
            {
                // A paragraph-level Hyperlink's box hugs the TEXT it was set on, not the
                // content band: "some text" at Helvetica 10 sits under a
                // link 43.35 pt wide (its exact advance), where the band is 415 (probed
                // 2026-08-26 on a TextFragment and on an HtmlFragment alike).
                var linkMeasure = Text.TextPaginator.CreateMeasurer(wtf.baseFont, wtf.fontSize,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData);
                var linkW = 0.0;
                foreach (var line in wtf.allLines)
                {
                    var lw = linkMeasure(line);
                    if (lw > linkW) linkW = lw;
                }
                if (linkW <= 0 || linkW > wtf.contentWidth) linkW = wtf.contentWidth;
                _pendingLinks.Add((wtf.fragSlot,
                    new Rectangle(CurLeft, wtf.fragTop - wtf.lineHeight, CurLeft + linkW, wtf.fragTop),
                    wtf.fragHyperlink));
            }

            if (wtf.segHyperlinks is { Count: > 0 })
            {
                // Locate each wrapped line's character span within the fragment text so a
                // segment range [a,b) can be split across the lines it covers (wrapping
                // drops the break space, so lines are matched sequentially by content).
                var lineStart = new int[wtf.allLines.Count];
                var lineEnd = new int[wtf.allLines.Count];
                int scan = 0;
                for (int li = 0; li < wtf.allLines.Count; li++)
                {
                    var ln = wtf.allLines[li];
                    int at = ln.Length == 0 ? scan : wtf.rawText.IndexOf(ln, Math.Min(scan, wtf.rawText.Length), StringComparison.Ordinal);
                    if (at < 0) at = scan;
                    lineStart[li] = at;
                    lineEnd[li] = at + ln.Length;
                    scan = lineEnd[li];
                }
                foreach (var (a, b, h) in wtf.segHyperlinks)
                {
                    for (int li = 0; li < wtf.allLines.Count; li++)
                    {
                        var ln = wtf.allLines[li];
                        int ov0 = Math.Max(a, lineStart[li]);
                        int ov1 = Math.Min(b, lineEnd[li]);
                        if (ov1 <= ov0) continue;
                        var prefix = ln.Substring(0, ov0 - lineStart[li]);
                        var run = ln.Substring(ov0 - lineStart[li], ov1 - ov0);
                        var x0 = CurLeft + MeasureText(prefix, wtf.baseFont, wtf.fontSize);
                        var w = MeasureText(run, wtf.baseFont, wtf.fontSize);
                        var (sgAbove, sgBelow) = LinkBoxExtent(tf.TextState, wtf.fontSize);
                        var sgBase = (wtf.fragFirstBaseline ?? wtf.fragTop - wtf.fontSize) - wtf.lineHeight * li;
                        _pendingLinks.Add((wtf.fragSlot,
                            new Rectangle(x0, sgBase - sgBelow, x0 + w, sgBase + sgAbove), h));
                    }
                }
            }
        }
    }
}
