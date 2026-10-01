using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

/// <summary>
/// Appends text fragments to a PDF page by registering fonts in the page
/// resources and writing content stream operators.
/// </summary>
public sealed partial class TextBuilder
{
    private readonly Page _page;

    /// <summary>Where written operators go, when the caller wants them rather than
    /// the page. The flow uses it to keep a caller-positioned fragment IN ORDER
    /// among the lines around it: its page does not exist yet, and appending it
    /// afterwards would move it to the end of that page's content -- same pixels,
    /// but the extracted text would read in a different order than the reference's.
    /// Null, as it is for every other caller, appends to the page as before.</summary>
    internal Action<byte[]>? ContentSink { get; set; }

    /// <summary>Creates a text builder that writes text onto the given page.</summary>
    public TextBuilder(Page page)
    {
        _page = page;
    }

    /// <summary>Construct a TextBuilder bound to a page plus an operator-collection
    /// sink. The collection is stored only — operators are still emitted to the
    /// page's content stream when AppendText/AppendParagraph are called.</summary>
    public TextBuilder(Page page, BaseOperatorCollection operatorCollection)
    {
        _page = page;
        _ = operatorCollection;
    }

    /// <summary>Append a batch of text fragments in order.</summary>
    public void AppendText(List<TextFragment> textFragments)
    {
        if (textFragments is null) return;
        // When two consecutive fragments sit on the same baseline separated by a
        // genuine word-sized gap (e.g. per-word fragments copied out of a glyph-by-glyph
        // OCR overlay), the horizontal gap alone does not always survive the round-trip:
        // the glyphs are re-emitted at the appending font's natural advances, which can
        // close the gap below the extractor's word-space threshold. Emit an explicit space
        // glyph on the earlier fragment so the copied text re-extracts with its word
        // boundaries intact. Fragments on different lines, abutting fragments (no gap), and
        // over-wide column jumps are left untouched.
        for (var idx = 0; idx < textFragments.Count; idx++)
        {
            var f = textFragments[idx];
            var addSpace = false;
            if (idx + 1 < textFragments.Count)
                addSpace = SameLineWordGap(f, textFragments[idx + 1]);
            AppendText(f, addSpace);
        }
    }

    /// <summary>True when <paramref name="next"/> follows <paramref name="cur"/> on the same
    /// baseline after a positive, word-sized horizontal gap (not an abutting glyph and not a
    /// wide column jump).</summary>
    private static bool SameLineWordGap(TextFragment cur, TextFragment next)
    {
        var cp = cur.Position; var np = next.Position;
        if (cp is null || np is null) return false;
        var fs = cur.TextState.FontSize > 0 ? cur.TextState.FontSize : 12.0;
        if (Math.Abs(np.YIndent - cp.YIndent) > 0.3 * fs) return false; // not same line
        var curEndX = cur.Rectangle is { } r ? r.URX : cp.XIndent;
        var gap = np.XIndent - curEndX;
        return gap > 0.2 * fs && gap < 3.0 * fs;
    }

    /// <summary>Writes the fragment's text to the page's content stream at the fragment's <c>Position</c>, using its text state, and registers the font it needs in the page resources.</summary>
    public void AppendText(TextFragment textFragment) => AppendText(textFragment, false);

    private void AppendText(TextFragment textFragment, bool addTrailingSpace)
    {
        textFragment.AttachedTrailingSpace = addTrailingSpace;
        WriteFragment(textFragment, addTrailingSpace, rewrite: null);
    }

    /// <summary>Write a fragment the LAYOUT ENGINE has already positioned: its segments
    /// are the pieces of one flowed line, so they are chained left to right instead of
    /// each being seated at its own position. The fragment stays attached the same way,
    /// and a rewrite keeps writing it as the line it was laid out as.</summary>
    internal void AppendTextInline(TextFragment textFragment)
    {
        textFragment.AttachedInline = true;
        textFragment.AttachedTrailingSpace = false;
        WriteFragment(textFragment, false, rewrite: null);
    }

    /// <summary>Save-time rewrite of an attached fragment: the run is written again
    /// from the fragment's current state into the segment the append created.</summary>
    internal void RewriteAttachedFragment(TextFragment fragment)
    {
        if (fragment.AttachedSegment is not { } segment) return;
        WriteFragment(fragment, fragment.AttachedTrailingSpace, segment);
    }

    /// <summary>The effective size and face of <paramref name="segment"/>: its own
    /// when it carries them, else the ones it inherits from the fragment. A Bold or
    /// Italic style selects the styled family member.</summary>
    private static (double fontSize, FontData? fontData) ResolveSegmentFace(
        TextFragment fragment, TextSegment segment)
    {
        var st = segment.TextState;
        var fragFs = fragment.TextState.FontSize > 0 ? (double)fragment.TextState.FontSize : 12.0;
        var fs = st is { FontSizeTouched: true, FontSize: > 0 } ? (double)st.FontSize : fragFs;

        var ownFont = st?.Font is { } sf && !ReferenceEquals(sf, FontInfo.DefaultHelvetica) ? sf : null;
        var fontData = st?.FontData ?? ownFont?.SourceFontData
            ?? fragment.TextState.FontData ?? fragment.TextState.Font?.SourceFontData;
        if (st is not null && ResolveStyledFace(st, fontData) is { } styled) fontData = styled;
        return (fs, fontData);
    }

    /// <summary>The page-space box a seat's glyphs sit in: the run's matrix lowered
    /// by the CURRENT descriptor descent (the position the absorber reports), as wide
    /// as the text measures and <see cref="BackgroundEmHeight"/> ems tall.</summary>
    private static (double x, double y, double w, double h) SeatBox(in SegmentSeat seat,
        TextFragment fragment)
        => (seat.TmX, seat.TmY - seat.CurrentLift,
            SeatWidth(seat, fragment, seat.Text), seat.FontSize * BackgroundEmHeight);

    /// <summary>Per-line font hand-offs for the NEXT WriteCidLinesWithBreaks
    /// call: line index -> (font resource, pre-encoded glyph hex) of the covering
    /// face drawing that line instead of the fragment's own. Cleared after the
    /// write.</summary>
    private Dictionary<int, (string res, byte[] hex)>? _cidLineOverrides;

    // ── Font mapping ────────────────────────────────────────────────


    /// <summary>The tab marker the generator API uses inside a fragment's text.</summary>
    internal const string TabMarker = "#$TAB";

    /// <summary>How much finer than the font size the pen is when a run fakes a bold
    /// weight by stroking its own outline (a 12pt run strokes at 0.2727).</summary>
    private const double SyntheticBoldPenRatio = 44.0;

    /// <summary>Write one line of tab-stopped text. A stop's position is measured from
    /// the line's own left edge; the run that FOLLOWS a marker is seated against that
    /// stop by its alignment, and the stop's leader fills from wherever the pen stood
    /// to where the run begins.</summary>
    private void AppendTabbedLine(ContentStreamBuilder builder, TextFragment fragment,
        TabStops stops, string fontResName, double fontSize, double x, double y, double descentComp)
    {
        // Runs: the text before the first marker (if any), then one per marker.
        var parts = fragment.Text.Split(new[] { TabMarker }, StringSplitOptions.None);
        var fg = fragment.TextState.ForegroundColor;

        double MeasureRun(string t)
        {
            var fd = fragment.TextState.FontData ?? fragment.TextState.Font?.SourceFontData;
            if (fd is { TtfData: not null }) return fd.MeasureString(t, fontSize);
            var face = fragment.TextState.Std14FaceOverride ?? MapToStandard14(fragment.TextState);
            double w = 0;
            foreach (var ch in t)
            {
                var cw = Standard14Fonts.GetWidth(face, ch < 256 ? ch : '?');
                w += (cw >= 0 ? cw : 500) * fontSize / 1000.0;
            }
            return w;
        }

        void Show(string t, double atX)
        {
            if (t.Length == 0) return;
            builder.BeginText();
            builder.SetFont(fontResName, fontSize);
            if (fg is not null) builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
            builder.MoveTextPosition(atX, y - descentComp);
            builder.ShowText(t);
            builder.EndText();
        }

        // The leader rides just under the baseline, like an underline.
        double leaderY = y - descentComp - fontSize * 0.12;
        void Leader(TabLeaderType type, double fromX, double toX)
        {
            if (type == TabLeaderType.None || toX - fromX < 1) return;
            builder.SaveState();
            if (fg is not null) builder.SetStrokeColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
            builder.SetLineWidth(fontSize * 0.05);
            if (type == TabLeaderType.Dash) builder.SetDashPattern(new double[] { fontSize * 0.25 }, 0);
            else if (type == TabLeaderType.Dot) builder.SetDashPattern(new double[] { fontSize * 0.06, fontSize * 0.35 }, 0);
            builder.MoveTo(fromX, leaderY);
            builder.LineTo(toX, leaderY);
            builder.Stroke();
            builder.RestoreState();
        }

        double penX = x;
        // Text ahead of the first marker simply starts the line.
        if (parts.Length > 0 && parts[0].Length > 0)
        {
            Show(parts[0], penX);
            penX += MeasureRun(parts[0]);
        }
        for (var i = 1; i < parts.Length; i++)
        {
            var run = parts[i];
            var stop = i - 1 < stops.Count ? stops[i - 1] : null;
            var stopX = stop is null ? penX : x + stop.Position;
            var runW = MeasureRun(run);
            var runX = stop?.AlignmentType switch
            {
                TabAlignmentType.Right => stopX - runW,
                TabAlignmentType.Center => stopX - runW / 2,
                _ => stopX,
            };
            if (runX < penX) runX = penX;
            if (stop is not null) Leader(stop.LeaderType, penX, runX);
            Show(run, runX);
            penX = runX + runW;
        }
    }

    /// <summary>
    /// Append a text paragraph to the page. The paragraph renders its lines
    /// within its bounding rectangle (or at its position) and registers fonts
    /// in the page resources. The paragraph stays ATTACHED to the page: lines
    /// appended, segments removed and properties changed after this call are
    /// laid out again at save time and replace what was written here.
    /// </summary>
    public void AppendParagraph(TextParagraph textParagraph)
    {
        var paragraph = textParagraph;
        WriteParagraph(paragraph);
        paragraph.AttachedPage = _page;
        _page.RegisterAttachedParagraph(paragraph);
    }

    /// <summary>Lay <paramref name="paragraph"/> out and write its operators —
    /// into its existing segment when it already has one on this page, else as
    /// a new content-stream segment — and record the layout signature the
    /// written content corresponds to.</summary>
    private void WriteParagraph(TextParagraph paragraph)
    {
        var bytes = paragraph.BuildContent(_page,
            baseFontName =>
            {
                var mapped = MapToStandard14(baseFontName);
                return EnsureFontResource(mapped);
            },
            (fontData, text) => EnsureEmbeddedCIDFont(fontData, text));
        paragraph.updatePositioningCalls++;
        if (paragraph.AttachedSegment is { } segment)
        {
            segment.ReplaceData(bytes);
        }
        else if (bytes.Length > 0)
        {
            _page.AddContentStream(bytes);
            paragraph.AttachedSegment = _page.LastContentStreamSegment();
        }
        // A previously materialised operator view of the page would flush the
        // stale operators back over the rewritten segment at save.
        _page.ResetContentsCache();
        paragraph.AttachedSignature = paragraph.LayoutSignature();
    }

    /// <summary>Save-time sync of an attached paragraph: when anything the layout
    /// depends on changed since it was written, lay it out again into the same
    /// segment. An unchanged paragraph costs nothing — no second positioning pass.</summary>
    internal static void SyncAttachedParagraph(TextParagraph paragraph)
    {
        if (paragraph.AttachedPage is not { } page) return;
        if (paragraph.AttachedSignature == paragraph.LayoutSignature()) return;
        new TextBuilder(page).WriteParagraph(paragraph);
    }

    /// <summary>True when a fragment's NON-EMPTY segments resolve to at least two
    /// different effective styles (font family, size, bold, italic) — the shape that
    /// needs the per-segment sequential writer. A single styled segment (or segments
    /// that merely inherit the fragment state) keeps the ordinary writer.</summary>
    internal static bool SegmentStylesDiffer(TextFragment fragment, double fragFontSize)
    {
        if (fragment.Segments.Count < 2) return false;
        (string font, double fs, bool b, bool i)? first = null;
        foreach (var seg in fragment.Segments)
        {
            var st = seg.TextState;
            if (st is null || string.IsNullOrEmpty(seg.Text)) continue;
            var fs = st is { FontSizeTouched: true, FontSize: > 0 }
                ? (double)st.FontSize
                : (fragFontSize > 0 ? fragFontSize : 12.0);
            var fontName = st.Font is { } sf && !ReferenceEquals(sf, FontInfo.DefaultHelvetica)
                ? sf.FontName ?? string.Empty
                : fragment.TextState.Font?.FontName ?? string.Empty;
            var cur = (fontName, fs, st.IsBold, st.IsItalic);
            if (first is null) { first = cur; continue; }
            var f0 = first.Value;
            if (!string.Equals(f0.font, cur.fontName, StringComparison.Ordinal)
                || Math.Abs(f0.fs - cur.fs) > 0.01 || f0.b != cur.IsBold || f0.i != cur.IsItalic)
                return true;
        }
        return false;
    }

    /// <summary>Sequential writer for a fragment whose segments carry their own
    /// font/size/style: each segment (and each newline-split piece) is its own BT
    /// block in its own font resource; newlines drop one font-size step and re-start
    /// at the fragment X with an empty fragment-font marker run.</summary>
    private void AppendStyledSegments(TextFragment fragment, ContentStreamBuilder builder, string fragResName, double fontSize, double fragDescentComp, double x, double y)
    {
        var ss = new StyledSegmentsState();
        ss.fragment = fragment;
        ss.builder = builder;
        ss.fragResName = fragResName;
        ss.fontSize = fontSize;
        ss.fragDescentComp = fragDescentComp;
        ss.x = x;
        ss.y = y;
        ss.lineH = ss.fontSize > 0 ? ss.fontSize : 12.0;
        ss.curX = ss.x;
        ss.curY = ss.y;
        ss.lineStarted = false;

        foreach (var seg in ss.fragment.Segments)
        {
            AppendStyledSegment(ss, seg);
        }
    }

    // ── Embedded TrueType font registration ────────────────────────

    /// <summary>
    /// Embed a TrueType font as a CIDFont (Type0 composite font) for Unicode text.
    /// Returns the font resource name and the hex-encoded glyph IDs for the text.
    /// </summary>
    private (string fontResName, byte[] hexGlyphIds) EnsureEmbeddedCIDFont(FontData fontData, string text)
    {
        // Walks /Parent for inherited /Resources and clones into the page's own
        // dict so the new font lives locally — see GetOrCreateOwnResources.
        var fontDict = GetOrCreateOwnFontDict();
        // Delegate the Type0/CIDFontType2 construction to the shared embedder. Pass the
        // font name through unchanged (stripSpaces:false) so the generator's /BaseFont
        // stays byte-for-byte as before.
        return Type0FontEmbedder.Embed(fontDict, fontData.TtfData!, fontData.FontName, text);
    }

    // ── Font resource registration ──────────────────────────────────

}
