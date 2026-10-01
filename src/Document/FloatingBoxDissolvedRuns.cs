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
// Run collection for a dissolved floating box: styled-run flushes and the fragment/heading run appenders.
    private static bool InlineOf(BaseParagraph p) =>
        p is Text.TextFragment ptf ? ptf.IsInLineParagraph : p.IsInLineParagraph;

    // A child needs the styled-run engine when the legacy
    // writers would drop its decorations: an explicit label,
    // an inline join (either side), per-segment colour /
    // underline / superscript, or a footnote.
    private static bool SegStyled(Text.TextState st) => st.Underline
        || st.ForegroundColor is not null || st.Superscript;

    private static void FlushStyled(DissolvedBoxState db, FlowLayout flow)
    {
        if (db.styRuns.Count > 0)
            flow.WriteStyledParagraph(db.styRuns, db.styLs, db.styBackground, db.styAlign);
        foreach (var (n, marker, sz) in db.styNotes)
            flow.QueueFootnote(n, marker, sz, db.styLastChild);
        var closeAfter = db.styNotes.Count > 0 && flow.ShouldCloseAfterChild(db.styLastChild);
        db.styRuns = new List<FlowLayout.StyledRun>();
        db.styNotes = new List<(Note, string, double)>();
        db.styLs = 0;
        db.styBaseSize = 0;
        db.styBackground = null;
        db.styAlign = HorizontalAlignment.Left;
        // The planner closes the page after the child whose note the band
        // could not take whole.
        if (closeAfter) flow.ForceNewPage();
    }

    private static void AppendFragmentRuns(DissolvedBoxState db, FlowLayout flow, Text.TextFragment tf)
    {
        var parent = tf.TextState;
        if (db.styRuns.Count == 0)
            db.styAlign = tf.HorizontalAlignment != HorizontalAlignment.Left
                ? tf.HorizontalAlignment : parent.HorizontalAlignment;
        if (parent.LineSpacing > db.styLs) db.styLs = parent.LineSpacing;
        db.styBackground ??= parent.BackgroundColor;
        // The fragment's own size: its largest segment size — a text-less
        // segment counts (an empty inline owner sized 12 carries a 6 pt
        // mark) — else the fragment's, else the builder default.
        double tfBase = 0;
        var hasOwnText = false;
        foreach (var seg in tf.Segments)
        {
            if (!seg.TextState.Superscript && seg.TextState.FontSizeTouched)
                tfBase = Math.Max(tfBase, seg.TextState.FontSize);
            hasOwnText |= !string.IsNullOrEmpty(seg.Text);
        }
        var joinsLine = tf.IsInLineParagraph && db.styRuns.Count > 0;
        foreach (var seg in tf.Segments)
        {
            var st = seg.TextState;
            if (st.LineSpacing > db.styLs) db.styLs = st.LineSpacing;
            if (string.IsNullOrEmpty(seg.Text)) continue;
            if (IsBreakSegment(seg) && HasBreakSegmentBesideText(tf))
            {
                db.styRuns.Add(new FlowLayout.StyledRun
                {
                    HardBreak = true, Text = string.Empty,
                    Size = Document.FlowLayout.XmlDefaultFontSize,
                });
                continue;
            }
            // A segment with no explicit size falls back to the
            // document-builder default point size (10), not the
            // Standard-14 12 — an untyped FloatingBox fragment
            // renders at the builder default.
            var size = st.FontSizeTouched ? (double)st.FontSize
                : parent.FontSizeTouched ? parent.FontSize : 10;
            var merged = new Text.TextState
            {
                ForegroundColor = st.ForegroundColor ?? parent.ForegroundColor,
                Underline = st.Underline || parent.Underline,
                IsBold = st.IsBold || parent.IsBold,
                IsItalic = st.IsItalic || parent.IsItalic,
            };
            var font = st.Font?.SourceFontData is not null ? st.Font
                : parent.Font?.SourceFontData is not null ? parent.Font : null;
            if (font is not null) merged.Font = font;
            if (st.FontData is not null) merged.FontData = st.FontData;
            else if (parent.FontData is not null) merged.FontData = parent.FontData;
            if (!st.Superscript && size > db.styBaseSize) db.styBaseSize = size;
            db.styRuns.Add(new FlowLayout.StyledRun
            {
                Text = seg.Text, Size = size, State = merged,
                Sup = st.Superscript, Link = seg.Hyperlink, InlineStart = joinsLine,
            });
            joinsLine = false;
        }
        if (tf.FootNote is { Paragraphs.Count: > 0 } fn)
        {
            // The mark: half ITS fragment's size — its text's, else the
            // fragment's own (the builder default 10 pt for an unsized
            // one; an inline-joined fragment does not borrow the size of
            // the paragraph it joins) — always in the Standard-14 sans.
            var markSize = tfBase > 0 ? tfBase : parent.FontSizeTouched ? parent.FontSize : 10;
            // A text-less inline fragment joins the previous line at its
            // box top and is as tall as its own size plus leading.
            var joinH = tf.IsInLineParagraph && !hasOwnText ? markSize + parent.LineSpacing : 0;
            var marker = flow.NextFootnoteMarker(fn);
            var markState = new Text.TextState { ForegroundColor = fn.TextState?.ForegroundColor };
            if (marker.Length > 0)
                db.styRuns.Add(new FlowLayout.StyledRun
                {
                    Text = marker, Size = markSize, State = markState,
                    Sup = true, NoteMark = true, Note = fn, JoinHeight = joinH,
                });
            db.styNotes.Add((fn, marker, markSize));
        }
    }

    private static void AppendHeadingRuns(DissolvedBoxState db, Dictionary<int, int> headingAutoCounters, Heading h)
    {
        var parent = h.TextState;
        var ownerLeft = h.Margin?.Left ?? 0;
        if (parent.LineSpacing > db.styLs) db.styLs = parent.LineSpacing;
        if (db.styRuns.Count == 0)
        {
            // The label renders only when the heading STARTS the
            // paragraph — inline-joined headings show no number.
            var label = h.UserLabel?.Text
                ?? NextHeadingPrefix(headingAutoCounters, h).TrimEnd();
            if (!string.IsNullOrEmpty(label))
            {
                var lblState = new Text.TextState
                {
                    IsBold = h.UserLabel?.TextState.IsBold ?? false,
                };
                db.styRuns.Add(new FlowLayout.StyledRun
                {
                    Text = label,
                    Size = parent.FontSize > 0 ? parent.FontSize : 10,
                    State = lblState, OwnerLeft = ownerLeft, TabAfter = 20,
                });
            }
        }
        foreach (var seg in h.Segments)
        {
            var st = seg.TextState;
            if (st.LineSpacing > db.styLs) db.styLs = st.LineSpacing;
            if (string.IsNullOrEmpty(seg.Text)) continue;
            var size = st.FontSizeTouched ? (double)st.FontSize
                : parent.FontSizeTouched ? parent.FontSize : 10;
            var merged = new Text.TextState
            {
                ForegroundColor = st.ForegroundColor ?? parent.ForegroundColor,
                Underline = st.Underline || parent.Underline,
                IsBold = st.IsBold || parent.IsBold,
                IsItalic = st.IsItalic || parent.IsItalic,
            };
            var font = st.Font?.SourceFontData is not null ? st.Font
                : parent.Font?.SourceFontData is not null ? parent.Font : null;
            if (font is not null) merged.Font = font;
            if (!st.Superscript && size > db.styBaseSize) db.styBaseSize = size;
            db.styRuns.Add(new FlowLayout.StyledRun
            {
                Text = seg.Text, Size = size, State = merged,
                Sup = st.Superscript, OwnerLeft = ownerLeft,
            });
        }
    }
}
