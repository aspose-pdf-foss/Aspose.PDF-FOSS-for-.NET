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
    /// <summary>Page-level inline-model paragraph: a TextFragment or Image followed
    /// by IsInLineParagraph members (fragments, images and graphs) renders as ONE
    /// flowing paragraph, and a lone fragment whose segment styles the single-line styled
    /// writer cannot carry (decorations, links, an embedded face, a wrap, a note)
    /// takes the same engine instead of the fixed-position stamp. HTML members keep
    /// the HTML join.</summary>
    private bool TryLayoutInlineChain(PageContentLayoutState lc, BaseParagraph para)
    {
        static bool InlineMember(BaseParagraph p) =>
            p is Text.TextFragment { IsInLineParagraph: true, HasExplicitPosition: false, XmlGeneratorModel: false }
            || p is Image { IsInLineParagraph: true }
            || p is Drawing.Graph { IsInLineParagraph: true };

        if (para is not (Text.TextFragment or Image)) return false;
        if (para is Text.TextFragment { HasExplicitPosition: true } or Text.TextFragment { XmlGeneratorModel: true })
            return false;
        var members = new List<BaseParagraph> { para };
        var k = lc.paraIdx + 1;
        for (; k < lc.pl.paraList.Count && InlineMember(lc.pl.paraList[k]); k++) members.Add(lc.pl.paraList[k]);
        // An inline HTML member belongs to the HTML join.
        if (k < lc.pl.paraList.Count && lc.pl.paraList[k] is HtmlFragment { IsInLineParagraph: true }) return false;
        if (members.Count == 1
            && !(para is Text.TextFragment lone && NeedsInlineEngine(lone, lc.pl.flow.CurWidth)))
            return false;
        // A chain of images alone keeps the legacy shared-line image layout (its
        // per-image alignment model); the inline engine is for text-bearing lines.
        var anyText = false;
        foreach (var m in members) if (m is Text.TextFragment) { anyText = true; break; }
        if (!anyText) return false;

        var runs = new List<FlowLayout.InlineRun>();
        var notes = new List<(Note note, string marker, double size, bool end)>();
        var align = HorizontalAlignment.Left;
        if (para is Text.TextFragment head)
            align = head.HorizontalAlignment != HorizontalAlignment.Left
                ? head.HorizontalAlignment : head.TextState.HorizontalAlignment;
        for (var g = 0; g < members.Count; g++)
        {
            if (members[g] is Text.TextFragment tf)
                AppendInlineFragmentRuns(tf, g, runs, notes, lc.pl.flow);
            else if (members[g] is Image im && LoadInlineImage(im) is (var data, var w, var h))
                runs.Add(new FlowLayout.InlineRun { ImageData = data, ImageW = w, ImageH = h, Group = g });
            // An inline Graph is a box of its declared size: it follows the text on the
            // line with its top on the line top, advances the pen by its width and gives
            // the line no height (probed: a 150 x 13 graph after a 10 pt fragment sits at
            // the fragment's end, top on the ascent line, the next inline fragment at
            // +150 and the wrapped line 10 below).
            else if (members[g] is Drawing.Graph gr)
                runs.Add(new FlowLayout.InlineRun
                {
                    Graph = gr, ImageData = Array.Empty<byte>(), ImageW = gr.Width, ImageH = gr.Height, Group = g,
                });
        }
        if (runs.Count == 0) return false;
        lc.pl.flow.WriteInlineParagraph(runs, align);
        foreach (var (note, marker, size, end) in notes)
        {
            if (end) lc.pl.flow.QueueEndNote(note, marker, size);
            else lc.pl.flow.QueueMarkedFootnote(note, marker, size);
        }
        lc.paraIdx = k - 1;
        return true;
    }

    /// <summary>True for a multi-segment fragment with differing segment styles
    /// that the single-line styled writer rejects: a segment link, decoration or
    /// embedded face, a newline, a note, or a total width that needs wrapping.</summary>
    private const char NewlineChar = (char)10;

    private static bool NeedsInlineEngine(Text.TextFragment tf, double width)
    {
        if (tf.Segments is not { Count: > 1 } segs) return false;
        if (tf.TabStops is { Count: > 0 } || tf.TextState.RenderingMode != 0) return false;
        // A caller who asked for the segments to flow as runs wants the flow's
        // own wrap (see FlowSegmentedRuns), not the inline model.
        if (tf.TextState.FormattingOptions is { SegmentsFlowAsRuns: true }) return false;
        foreach (var s in segs)
            if (s.Position is not null) return false;
        // A lone fragment with explicit newlines keeps the segment writer (its
        // per-line marker runs are what the absorber-indexing callers count) -
        // unless it carries a LEADING: then its newline segments stand on the
        // inline model's lines (a newline segment is one 10 pt default line, the
        // text lines pitch at size + leading), which the segment writer cannot seat.
        var leaded = tf.TextState.LineSpacing > 0;
        var newline = false;
        foreach (var s in segs)
        {
            if (s.TextState.LineSpacing > 0) leaded = true;
            if ((s.Text ?? string.Empty).IndexOf(NewlineChar) >= 0) newline = true;
        }
        if (newline && leaded) return true;
        if (!Text.TextBuilder.SegmentStylesDiffer(tf, tf.TextState.FontSize)) return false;
        if (!leaded)
            foreach (var s in segs)
                if ((s.Text ?? string.Empty).IndexOf('\n') >= 0) return false;
        var complex = tf.HyperlinkValue is not null || tf.FootNote is not null || tf.EndNote is not null
                      || tf.TextState.Underline || tf.TextState.IsStrikeOut
                      || tf.TextState.FontData is not null || tf.TextState.Font?.SourceFontData is not null;
        double total = 0;
        var parentSize = tf.TextState.FontSize > 0 ? (double)tf.TextState.FontSize : 10;
        foreach (var s in segs)
        {
            var text = s.Text ?? string.Empty;
            if (text.Length == 0) continue;
            var st = s.TextState;
            if (s.Hyperlink is not null || st.Underline || st.IsStrikeOut
                || st.FontData is not null || st.Font?.SourceFontData is not null
                || text.IndexOf('\n') >= 0)
                complex = true;
            var size = st.FontSizeTouched ? (double)st.FontSize : parentSize;
            total += Text.TextPaginator.CreateMeasurer(Text.TextBuilder.MapToStandard14Public(st), size,
                st.FontData ?? st.Font?.SourceFontData)(text);
        }
        return complex || total > width;
    }

    /// <summary>Turn a fragment's segments (and its note marks) into inline runs of
    /// chain group <paramref name="group"/>.</summary>
    private static void AppendInlineFragmentRuns(Text.TextFragment tf, int group,
        List<FlowLayout.InlineRun> runs, List<(Note note, string marker, double size, bool end)> notes,
        FlowLayout flow)
    {
        var parent = tf.TextState;
        var parentSize = parent.FontSize > 0 ? (double)parent.FontSize : 10;
        double maxSize = 0;
        foreach (var seg in tf.Segments)
        {
            if (string.IsNullOrEmpty(seg.Text)) continue;
            var st = seg.TextState;
            var size = st.FontSizeTouched ? (double)st.FontSize : parentSize;
            var leading = st.LineSpacing > 0 ? (double)st.LineSpacing
                : parent.LineSpacing > 0 ? (double)parent.LineSpacing : 0;
            var state = new Text.TextState
            {
                ForegroundColor = st.ForegroundColor ?? parent.ForegroundColor,
                IsBold = st.IsBold || parent.IsBold,
                IsItalic = st.IsItalic || parent.IsItalic,
            };
            var font = st.Font is { } sf && !ReferenceEquals(sf, Text.FontInfo.DefaultHelvetica)
                ? sf : parent.Font;
            if (font is not null) state.Font = font;
            if ((st.FontData ?? parent.FontData) is { } fd) state.FontData = fd;
            runs.Add(new FlowLayout.InlineRun
            {
                Text = seg.Text, Size = size, Pitch = size + leading, Group = group, State = state,
                Link = seg.Hyperlink ?? tf.HyperlinkValue,
                Underline = st.Underline || parent.Underline,
                Strike = st.IsStrikeOut || parent.IsStrikeOut,
            });
            if (size > maxSize) maxSize = size;
        }
        if (maxSize <= 0) maxSize = parentSize;
        foreach (var (note, end) in new[] { (tf.FootNote, false), (tf.EndNote, true) })
        {
            if (note is null) continue;
            var marker = flow.NextFootnoteMarker(note);
            if (marker.Length > 0)
                runs.Add(new FlowLayout.InlineRun
                {
                    Text = marker, Size = maxSize * FlowLayout.MarkerSizeRatio, Group = group, NoteMarker = true,
                    Note = note,
                });
            notes.Add((note, marker, maxSize, end));
        }
    }

    /// <summary>Raster bytes and placed size (points) of an inline image: the first
    /// frame, at its natural size scaled by ImageScale or the Fix box.</summary>
    private static (byte[] data, double width, double height)? LoadInlineImage(Image img)
    {
        byte[]? data = default;
        double width = default;
        double height = default;
        data = Array.Empty<byte>();
        width = height = 0;
        byte[]? bytes = null;
        if (img.ImageStream is not null)
        {
            var pos = img.ImageStream.CanSeek ? img.ImageStream.Position : -1L;
            if (img.ImageStream.CanSeek) img.ImageStream.Position = 0;
            using var mem = new System.IO.MemoryStream();
            img.ImageStream.CopyTo(mem);
            bytes = mem.ToArray();
            if (pos >= 0) img.ImageStream.Position = pos;
        }
        else
            bytes = img.ReadSourceBytes();
        if (bytes is null || bytes.Length < 4) return null;
        var isJpeg = bytes[0] == 0xFF && bytes[1] == 0xD8 && !IsProgressiveJpeg(bytes);
        var isPng = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        var frames = isJpeg || isPng ? new List<byte[]> { bytes } : TryDecodeImageFramesAsPng(bytes);
        if (frames is null || frames.Count == 0) return null;
        data = frames[0];
        if (img.FixWidth > 0 && img.FixHeight > 0)
        {
            width = img.FixWidth;
            height = img.FixHeight;
            return (data, width, height);
        }
        if (TryGetImageNaturalSizePt(data, img.IsApplyResolution) is not (var natW, var natH) || natW <= 0 || natH <= 0)
            return null;
        var scale = img.ImageScale > 0 ? img.ImageScale : 1.0;
        width = natW * scale;
        height = natH * scale;
        if (img.FixWidth > 0) { height *= img.FixWidth / width; width = img.FixWidth; }
        else if (img.FixHeight > 0) { width *= img.FixHeight / height; height = img.FixHeight; }
        return (data, width, height);
    }

    private bool TryLayoutInlineJoinedRun(PageContentLayoutState lc, BaseParagraph para)
    {
        // Consecutive paragraphs chained by IsInLineParagraph render as ONE
        // line: a fragment followed by inline members ("MyBrand" +
        // inline HtmlFragment("tm") + inline TextFragment(" New features!"))
        // must not stack one per line. Joinable members become per-segment
        // styled runs of a single composite fragment — HTML members take the
        // serif HTML body face; text members keep their own state.
        if (para is Text.TextFragment or HtmlFragment
            && lc.paraIdx + 1 < lc.pl.paraList.Count
            && ParagraphInlineFlag(lc.pl.paraList[lc.paraIdx + 1])
            && InlineJoinable(para) is (_, _))
        {
            if (LayoutInlineJoinedRun(lc, para)) return true;
        }
        return false;
    }
}
