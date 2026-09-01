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
    /// by IsInLineParagraph members (fragments and images) renders as ONE flowing
    /// paragraph, and a lone fragment whose segment styles the single-line styled
    /// writer cannot carry (decorations, links, an embedded face, a wrap, a note)
    /// takes the same engine instead of the fixed-position stamp. HTML members keep
    /// the HTML join.</summary>
    private bool TryLayoutInlineChain(List<BaseParagraph> paraList, ref int paraIdx,
        BaseParagraph para, FlowLayout flow)
    {
        static bool InlineMember(BaseParagraph p) =>
            p is Text.TextFragment { IsInLineParagraph: true, HasExplicitPosition: false, XmlGeneratorModel: false }
            || p is Image { IsInLineParagraph: true };

        if (para is not (Text.TextFragment or Image)) return false;
        if (para is Text.TextFragment { HasExplicitPosition: true } or Text.TextFragment { XmlGeneratorModel: true })
            return false;
        var members = new List<BaseParagraph> { para };
        var k = paraIdx + 1;
        for (; k < paraList.Count && InlineMember(paraList[k]); k++) members.Add(paraList[k]);
        // An inline HTML member belongs to the HTML join.
        if (k < paraList.Count && paraList[k] is HtmlFragment { IsInLineParagraph: true }) return false;
        if (members.Count == 1
            && !(para is Text.TextFragment lone && NeedsInlineEngine(lone, flow.CurWidth)))
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
                AppendInlineFragmentRuns(tf, g, runs, notes, flow);
            else if (members[g] is Image im && LoadInlineImage(im, out var data, out var w, out var h))
                runs.Add(new FlowLayout.InlineRun { ImageData = data, ImageW = w, ImageH = h, Group = g });
        }
        if (runs.Count == 0) return false;
        flow.WriteInlineParagraph(runs, align);
        foreach (var (note, marker, size, end) in notes)
        {
            if (end) flow.QueueEndNote(note, marker, size);
            else flow.QueueMarkedFootnote(note, marker, size);
        }
        paraIdx = k - 1;
        return true;
    }

    /// <summary>True for a multi-segment fragment with differing segment styles
    /// that the single-line styled writer rejects: a segment link, decoration or
    /// embedded face, a newline, a note, or a total width that needs wrapping.</summary>
    private static bool NeedsInlineEngine(Text.TextFragment tf, double width)
    {
        if (tf.Segments is not { Count: > 1 } segs) return false;
        if (tf.TabStops is { Count: > 0 } || tf.TextState.RenderingMode != 0) return false;
        foreach (var s in segs)
            if (s.Position is not null) return false;
        if (!Text.TextBuilder.SegmentStylesDiffer(tf, tf.TextState.FontSize)) return false;
        // A lone fragment with explicit newlines keeps the segment writer (its
        // per-line marker runs are what the absorber-indexing callers count).
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
    private static bool LoadInlineImage(Image img, out byte[] data, out double width, out double height)
    {
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
        if (bytes is null || bytes.Length < 4) return false;
        var isJpeg = bytes[0] == 0xFF && bytes[1] == 0xD8 && !IsProgressiveJpeg(bytes);
        var isPng = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        var frames = isJpeg || isPng ? new List<byte[]> { bytes } : TryDecodeImageFramesAsPng(bytes);
        if (frames is null || frames.Count == 0) return false;
        data = frames[0];
        if (img.FixWidth > 0 && img.FixHeight > 0)
        {
            width = img.FixWidth;
            height = img.FixHeight;
            return true;
        }
        if (!TryGetImageNaturalSizePt(data, img.IsApplyResolution, out var natW, out var natH) || natW <= 0 || natH <= 0)
            return false;
        var scale = img.ImageScale > 0 ? img.ImageScale : 1.0;
        width = natW * scale;
        height = natH * scale;
        if (img.FixWidth > 0) { height *= img.FixWidth / width; width = img.FixWidth; }
        else if (img.FixHeight > 0) { width *= img.FixHeight / height; height = img.FixHeight; }
        return true;
    }

    private bool TryLayoutInlineJoinedRun(List<BaseParagraph> paraList, ref int paraIdx,
        BaseParagraph para, FlowLayout flow)
    {
        // Consecutive paragraphs chained by IsInLineParagraph render as ONE
        // line: a fragment followed by inline members ("MyBrand" +
        // inline HtmlFragment("tm") + inline TextFragment(" New features!"))
        // must not stack one per line. Joinable members become per-segment
        // styled runs of a single composite fragment — HTML members take the
        // serif HTML body face; text members keep their own state.
        if (para is Text.TextFragment or HtmlFragment
            && paraIdx + 1 < paraList.Count
            && ParagraphInlineFlag(paraList[paraIdx + 1])
            && InlineJoinable(para, out _, out _))
        {
            var members = new List<BaseParagraph> { para };
            var k = paraIdx + 1;
            for (; k < paraList.Count && ParagraphInlineFlag(paraList[k])
                   && InlineJoinable(paraList[k], out _, out _); k++)
                members.Add(paraList[k]);
            if (members.Count > 1)
            {
                var joined = new Text.TextFragment();
                var anyHtmlStyled = false;
                foreach (var member in members)
                {
                    InlineJoinable(member, out var mText, out var mSerif);
                    var seg = new Text.TextSegment(mText);
                    if (member is Text.TextFragment mf)
                        seg.TextState.ApplyChangesFrom(mf.TextState);
                    else if (member is HtmlFragment mh
                        && System.Text.RegularExpressions.Regex.Match(mh.HtmlContent ?? "",
                            @"<span\b[^>]*style\s*=\s*(['""])(?<s>[^'""]*)\1",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                            is { Success: true } mSty)
                    {
                        // an inline HTML member styles its run from its
                        // outermost span's own CSS
                        anyHtmlStyled = true;
                        var css = mSty.Groups["s"].Value;
                        var fsm2 = System.Text.RegularExpressions.Regex.Match(css,
                            @"font-size\s*:\s*([\d.]+)\s*(pt|px)",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (fsm2.Success)
                        {
                            var v = double.Parse(fsm2.Groups[1].Value,
                                System.Globalization.CultureInfo.InvariantCulture);
                            seg.TextState.FontSize = (float)(fsm2.Groups[2].Value
                                .Equals("px", StringComparison.OrdinalIgnoreCase) ? v * 0.75 : v);
                        }
                        var fam = System.Text.RegularExpressions.Regex.Match(css,
                            @"font-family\s*:\s*['""]?([^;'""]+)",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (fam.Success) seg.TextState.FontName = fam.Groups[1].Value.Trim();
                        var col = System.Text.RegularExpressions.Regex.Match(css,
                            @"(?<![-\w])color\s*:\s*([^;]+)",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (col.Success && Converters.HtmlToPdfConverter
                                .ParseCssColor(col.Groups[1].Value.Trim()) is { } cc)
                            seg.TextState.ForegroundColor = cc;
                        if (System.Text.RegularExpressions.Regex.IsMatch(css,
                                @"font-style\s*:\s*italic",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            seg.TextState.IsItalic = true;
                        if (System.Text.RegularExpressions.Regex.IsMatch(css,
                                @"text-decoration\s*:\s*line-through",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            seg.TextState.IsStrikeOut = true;
                    }
                    else if (mSerif)
                        seg.TextState.FontName = "TimesNewRoman";
                    joined.Segments.Add(seg);
                }
                if (flow.TryWriteStyledSegmentsLine(joined))
                {
                    paraIdx = k - 1;
                    return true;
                }
                // Too wide for one line: CSS-styled inline members flow as
                // ONE wrapped paragraph. The first line sets on the leading
                // the opening fragment declares, the rest on the HTML
                // 1.12-em rhythm.
                if (anyHtmlStyled)
                {
                    var styRuns2 = new List<FlowLayout.StyledRun>();
                    double maxFs2 = 0, introLs = 0;
                    foreach (var member in members)
                        if (member is Text.TextFragment lsf)
                        {
                            if (lsf.TextState.LineSpacing > introLs) introLs = lsf.TextState.LineSpacing;
                            foreach (Text.TextSegment lss in lsf.Segments)
                                if (lss.TextState.LineSpacing > introLs) introLs = lss.TextState.LineSpacing;
                        }
                    foreach (var seg in joined.Segments)
                    {
                        if (string.IsNullOrEmpty(seg.Text)) continue;
                        var sz = seg.TextState.FontSizeTouched ? (double)seg.TextState.FontSize : 12.0;
                        if (sz > maxFs2) maxFs2 = sz;
                        styRuns2.Add(new FlowLayout.StyledRun
                        {
                            Text = seg.Text, Size = sz, State = seg.TextState,
                        });
                    }
                    if (styRuns2.Count > 0 && maxFs2 > 0)
                    {
                        // members wrap ATOMICALLY: one joins the current line
                        // only when it fits whole, else it opens the next —
                        // and a member longer than a full line word-wraps
                        // alone. Greedy grouping, then one write per line.
                        double RunWidth(FlowLayout.StyledRun r)
                        {
                            var f = r.State.IsItalic ? "Helvetica-Oblique" : "Helvetica";
                            try
                            {
                                return Text.FontRepository.TryFindFont(f)
                                    ?.MeasureString(r.Text, r.Size) ?? r.Text.Length * r.Size * 0.5;
                            }
                            catch { return r.Text.Length * r.Size * 0.5; }
                        }
                        var lineGroups = new List<List<FlowLayout.StyledRun>> { new() };
                        var lw = 0.0;
                        foreach (var r in styRuns2)
                        {
                            var w = RunWidth(r);
                            if (lineGroups[^1].Count > 0 && lw + w > flow.CurWidth + 0.5)
                            { lineGroups.Add(new()); lw = 0; }
                            lineGroups[^1].Add(r);
                            lw += w;
                        }
                        // the first line sets on the leading the opening
                        // fragment declares (its box closes at the baseline);
                        // the rest keep the HTML 1.12-em rhythm
                        var htmlLead = maxFs2 * 0.12;
                        for (var lg = 0; lg < lineGroups.Count; lg++)
                            flow.WriteStyledParagraph(lineGroups[lg],
                                lg == 0 && introLs > 0
                                    ? introLs - 0.2075 * maxFs2 : htmlLead);
                        paraIdx = k - 1;
                        return true;
                    }
                }
            }
        }
        return false;
    }
}
