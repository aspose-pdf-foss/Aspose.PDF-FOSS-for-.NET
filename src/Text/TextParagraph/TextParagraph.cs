using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using System.Globalization;

namespace Aspose.Pdf.Text;

/// <summary>
/// Represents a text paragraph that can be placed on a page via <see cref="TextBuilder.AppendParagraph"/>.
/// Lines are appended via <see cref="AppendLine(TextFragment)"/> or <see cref="AppendLine(string)"/>
/// and rendered top-to-bottom within the <see cref="Rectangle"/> bounding box.
/// </summary>
public sealed partial class TextParagraph
{
    private readonly List<TextFragment> _lines = new();

    /// <summary>The <c>lineSpacing</c> argument of the AppendLine overloads, per
    /// fragment: a gap that opens BELOW the fragment's last line (between it and
    /// the next fragment). Distinct from <see cref="TextState.LineSpacing"/>, which
    /// opens a gap ABOVE every line of its fragment.</summary>
    private readonly Dictionary<TextFragment, double> _spacingBelow = new(ReferenceEqualityComparer.Instance);

    /// <summary>The fragment each visual line of the last <see cref="BuildVisualLines"/>
    /// came from (parallel to its result).</summary>
    private readonly List<TextFragment> _visualLineFragments = new();

    // Per-paragraph cache of glyph-outline parsers, keyed by the font's raw TTF
    // bytes, so non-Standard-14 fonts (e.g. MS Gothic) can be measured and drawn
    // with their actual glyph advances instead of being substituted by Helvetica.
    private readonly Dictionary<byte[], GlyphOutlineParser?> _glyphParsers =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Resolve (and cache) the glyph-outline parser for a text state's
    /// embedded TrueType data, or null when there is none / it fails to parse.</summary>
    private GlyphOutlineParser? GetGlyphParser(TextState ts)
    {
        var ttf = (ts.FontData ?? ts.Font?.SourceFontData)?.TtfData;
        if (ttf is null) return null;
        if (_glyphParsers.TryGetValue(ttf, out var cached)) return cached;
        GlyphOutlineParser? parser = null;
        try { parser = new GlyphOutlineParser(ttf); }
        catch { /* unparseable — fall back to substitution */ }
        _glyphParsers[ttf] = parser;
        return parser;
    }

    /// <summary>True when the run should be drawn/measured with its actual embedded
    /// font rather than a Standard-14 substitute: the family is not a Latin core
    /// family and its glyph data parses. Arial/Times/Courier keep substituting.</summary>
    private bool UsesRealFont(TextState ts)
    {
        var name = !string.IsNullOrEmpty(ts.FontName) ? ts.FontName : ts.Font?.FontName;
        return !TextBuilder.IsStandard14Family(name) && GetGlyphParser(ts) is not null;
    }

    /// <summary>Advance width of a character via the real embedded font's hmtx,
    /// scaled to the font size (matches the /W the CID embedder emits).</summary>
    private static double RealGlyphWidth(char c, double fontSize, GlyphOutlineParser gp)
    {
        var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000;
        var gid = gp.CMap.TryGetValue(c, out var g) ? g : 0;
        return gp.GetAdvanceWidth(gid) * fontSize / upm;
    }

    /// <summary>
    /// The bounding rectangle where the paragraph is rendered on the page.
    /// Text starts at the top of the rectangle and flows downward.
    /// </summary>
    public Rectangle? Rectangle { get; set; }

    /// <summary>
    /// The absolute position for the paragraph. When set, text begins at this position.
    /// If <see cref="Rectangle"/> is also set, <see cref="Rectangle"/> takes precedence.
    /// </summary>
    public Position? Position { get; set; }

    /// <summary>
    /// Formatting options for the paragraph, including word wrap mode.
    /// </summary>
    public TextFormattingOptions FormattingOptions { get; set; } = new();

    /// <summary>Rotation angle in degrees for the entire paragraph.</summary>
    public double Rotation { get; set; }

    /// <summary>The wrapped lines that did not fit in the rectangle when
    /// <see cref="LimitWithBounds"/> cut the paragraph, each as a fragment ready
    /// to be appended to a paragraph on the next page. A line that was broken
    /// by hyphenation keeps its hyphen. Empty when everything fit.</summary>
    public TextFragmentCollection RemainingLines
    {
        get
        {
            // Reading the leftovers lays the paragraph out, exactly as reading
            // Lines does — the two are the halves of one bounds cut.
            SeatedLines();
            return _remainingLines;
        }
    }
    private readonly TextFragmentCollection _remainingLines = new TextFragmentCollection();

    /// <summary>Background colour painted behind the whole paragraph block: one
    /// rectangle the height of the block, as wide as the widest line plus
    /// <see cref="BackgroundPad"/> (or the content width when a line overflows),
    /// seated at the block's bottom-left. Null paints nothing.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>The clip the paragraph writes around its block is taller than the
    /// block by this factor (1.16 = a 1-em line plus 0.16 em of line box), measured
    /// from the block bottom — it runs past the rectangle top when the block is
    /// taller than the rectangle, and stops at the rectangle bottom when the block
    /// overflows below it.</summary>
    private const double ClipLineBoxFactor = 1.16;

    /// <summary>Extra width (points) the paragraph background carries past the
    /// widest line, when that line fits the content width.</summary>
    private const double BackgroundPad = 1.0;

    /// <summary>A bold style on a face that has no bold member is synthesised by
    /// stroking the glyph outlines with a pen this fraction of the font size
    /// (a 10 pt run strokes at 0.28).</summary>
    private const double SyntheticBoldPenFactor = 0.028;

    /// <summary>The page this paragraph was appended to via
    /// <see cref="TextBuilder.AppendParagraph"/>; the paragraph stays attached,
    /// so edits made after the append reach the page at save time.</summary>
    internal Page? AttachedPage { get; set; }

    /// <summary>The content-stream segment holding this paragraph's operators
    /// on <see cref="AttachedPage"/> (null while the paragraph has produced no
    /// content yet); re-rendered in place when the paragraph changes.</summary>
    internal PdfStream? AttachedSegment { get; set; }

    /// <summary>The <see cref="LayoutSignature"/> the attached segment was
    /// rendered from — a later mismatch means the paragraph must be re-laid out.</summary>
    internal string? AttachedSignature { get; set; }

    /// <summary>Diagnostic counter incremented whenever the renderer performs
    /// a layout-positioning pass for this paragraph. Tests assert this stays
    /// at 1 to guard against quadratic layout work.</summary>
    public int updatePositioningCalls { get; set; }

    /// <summary>
    /// Horizontal alignment of text within the paragraph rectangle.
    /// </summary>
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>
    /// Vertical alignment of text within the paragraph rectangle. Defaults to
    /// <see cref="VerticalAlignment.Bottom"/> (the text block sits just above the
    /// rectangle's bottom edge).
    /// </summary>
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Bottom;

    /// <summary>
    /// Margin around the paragraph.
    /// </summary>
    public MarginInfo Margin { get; set; } = new();

    /// <summary>
    /// When true, text is limited to the bounds of the paragraph rectangle.
    /// Lines that exceed the rectangle height are not rendered.
    /// </summary>
    public bool LimitWithBounds { get; set; }

    /// <summary>
    /// The symbol a hyphenating wrap writes at the break. The default "-" can be
    /// set to <see cref="string.Empty"/> so a word is split with no visible mark
    /// (one more character then fits on the line).
    /// </summary>
    public string HyphenSymbol { get; set; } = "-";

    /// <summary>
    /// The calculated bounding rectangle of the paragraph text.
    /// Estimates height based on line count and font sizes.
    /// </summary>
    public Rectangle TextRectangle
    {
        get
        {
            double x = Position?.XIndent ?? Rectangle?.LLX ?? 0;
            double y = Position?.YIndent ?? Rectangle?.LLY ?? 0;
            double width = 0;
            double totalHeight = 0;
            var seated = SeatedLines();
            for (int i = 0; i < seated.Count; i++)
            {
                var line = seated[i];
                var fontSize = line.TextState.FontSize;
                // Same pitch the renderer stacks lines by (font size plus the
                // state's gap above, plus the appended gap below all but the last
                // line) — the reported height must equal the drawn block height, or
                // a caller that seats the paragraph at (URY − TextRectangle.Height)
                // gets a block that misses the target by the difference.
                totalHeight += fontSize + Math.Max(0, line.TextState.LineSpacing);
                if (i < seated.Count - 1 && _spacingBelow.TryGetValue(line, out var below))
                    totalHeight += below;
                var font = line.TextState.Font;
                double lineWidth;
                try { lineWidth = font?.MeasureString(line.Text, fontSize) ?? (line.Text.Length * fontSize * 0.5); }
                catch { lineWidth = line.Text.Length * fontSize * 0.5; }
                if (lineWidth > width) width = lineWidth;
            }
            // The renderer seats the block's descender box at the anchor, so the
            // drawn height also carries the last line's descent.
            if (seated.Count > 0)
            {
                var lastTs = seated[seated.Count - 1].TextState;
                totalHeight += GetDescentCompensation(lastTs, lastTs.FontSize);
            }
            return new Rectangle(x, y, x + width, y + totalHeight);
        }
    }

    /// <summary>
    /// The lines (text fragments) appended to this paragraph, as a 1-based
    /// collection. Accessing this lays the paragraph out so each appended
    /// fragment's last segment carries the page-space bounds (right edge and
    /// baseline) of its final wrapped line — the anchor callers use to append
    /// trailing content (e.g. leader dots) right after the text.
    /// </summary>
    internal TextFragmentCollection Lines
    {
        get
        {
            UpdatePositioning();
            var col = new TextFragmentCollection();
            foreach (var l in SeatedLines()) col.Add(l);
            return col;
        }
    }

    /// <summary>The page-space lift a run is written at, above its layout baseline:
    /// an embedded face is written one descriptor descent UP so the absorbed
    /// position (baseline − descriptor descent) reads back exactly at the layout
    /// baseline — every line lifts by ITS OWN face's descent, so mixed-face blocks
    /// keep their layout pitch on read-back. Standard-14 text carries no
    /// descriptor and is written at the layout baseline itself.</summary>
    private static double WrittenDescentLift(FontData? fd, double fontSize)
    {
        if (fd?.TtfData is null) return 0;
        var d = TextBuilder.HheaDescentPerMille(fd.TtfData);
        if (d == 0) d = FontRepository.ReadTtfMetrics(fd.TtfData).descent;
        return -d * fontSize / 1000.0;
    }

    /// <summary>Pick a value by the paragraph's vertical alignment within its
    /// rectangle: <paramref name="top"/> for Top, centre, or the default bottom.</summary>
    private double VerticalAnchor(double top, double center, double bottom) =>
        VerticalAlignment switch
        {
            VerticalAlignment.Top => top,
            VerticalAlignment.Center => center,
            _ => bottom,
        };

    /// <summary>Lay the paragraph out and write the last appended fragment's
    /// last segment's <see cref="TextSegment.Rectangle"/> (right edge) and
    /// <see cref="TextSegment.Position"/> (baseline) from the final wrapped
    /// line, using the same wrap/leading math as <see cref="Render"/> so the
    /// reported anchor matches where the text is actually drawn.</summary>
    /// <summary>The appended fragments the layout actually seats inside the
    /// rectangle. Without <see cref="LimitWithBounds"/> that is every appended
    /// fragment; with it, a fragment whose every line falls below the content
    /// bottom is not seated at all — it belongs to <see cref="RemainingLines"/>,
    /// so neither <see cref="Lines"/> nor <see cref="TextRectangle"/> reports it
    /// (a rectangle too short for one line reports an EMPTY text rectangle).</summary>
    private List<TextFragment> SeatedLines()
    {
        if (!LimitWithBounds || Rectangle is null || _lines.Count == 0) return _lines;

        var wrapMode = FormattingOptions.WrapMode;
        double clipWidth = Rectangle.Width - Margin.Left - Margin.Right;
        bool needsWrap = wrapMode != TextFormattingOptions.WordWrapMode.NoWrap && clipWidth > 0;
        var visualLines = BuildVisualLines(needsWrap ? clipWidth : 0, wrapMode);
        if (visualLines.Count == 0) return _lines;

        double blockHeight = BlockHeight(visualLines);
        double textY = BlockBottom(blockHeight) + blockHeight;
        double minY = Rectangle.LLY + Margin.Bottom;

        var seated = new List<TextFragment>();
        _remainingLines.Clear();
        for (int li = 0; li < visualLines.Count; li++)
        {
            textY -= LineAdvance(visualLines, li);
            if (textY < minY)
            {
                for (int ri = li; ri < visualLines.Count; ri++)
                    _remainingLines.Add(RemainingLineFragment(visualLines[ri]));
                break;
            }
            if (li >= _visualLineFragments.Count) break;
            var frag = _visualLineFragments[li];
            if (seated.Count == 0 || !ReferenceEquals(seated[seated.Count - 1], frag)) seated.Add(frag);
        }
        return seated;
    }

    private void UpdatePositioning()
    {
        if (_lines.Count == 0 || Rectangle is null) return;
        var lastFrag = _lines[_lines.Count - 1];
        if (lastFrag.Segments.Count == 0) return;

        var wrapMode = FormattingOptions.WrapMode;
        double clipWidth = Rectangle.Width - Margin.Left - Margin.Right;
        bool needsWrap = wrapMode != TextFormattingOptions.WordWrapMode.NoWrap && clipWidth > 0;
        var visualLines = BuildVisualLines(needsWrap ? clipWidth : 0, wrapMode);
        if (visualLines.Count == 0) return;

        double blockHeight = BlockHeight(visualLines);
        double startX = Rectangle.LLX + Margin.Left;
        double textY = BlockBottom(blockHeight) + blockHeight;

        // Walk to the final visual line — its baseline and right edge.
        double baseline = textY, rightEdge = startX, lineStartX = startX, lineFs = 0;
        for (int li = 0; li < visualLines.Count; li++)
        {
            var line = visualLines[li];
            textY -= LineAdvance(visualLines, li);
            baseline = textY;
            lineFs = LineFontSize(line);
            lineStartX = startX + (li == 0 ? FirstLineIndent : SubsequentLinesIndent);
            double lineWidth = 0;
            foreach (var (text, ts) in line) lineWidth += MeasureLineWidth(text, ts);
            rightEdge = lineStartX + lineWidth;
        }

        var lastSeg = lastFrag.Segments[lastFrag.Segments.Count]; // 1-based
        lastSeg.Rectangle = new Rectangle(lineStartX, baseline, rightEdge, baseline + lineFs);
        lastSeg.Position = new Position(lineStartX, baseline);
    }

    /// <summary>First-line indent in points. Stored only — the FOSS line
    /// flow doesn't honour it at write time.</summary>
    public float FirstLineIndent { get; set; }

    /// <summary>Indent (points) applied to every line except the first.
    /// Stored only.</summary>
    public float SubsequentLinesIndent { get; set; }

    /// <summary>When true, paragraph lines fill the rectangle width by
    /// adjusting word spacing. Stored only — the FOSS line writer uses
    /// the configured HorizontalAlignment instead.</summary>
    public bool Justify { get; set; }

    /// <summary>Begin a batched edit window. An attached paragraph is laid out
    /// once when appended and again only at save time if it changed, so there is
    /// no per-edit work to batch — kept for symmetry with the flush model.</summary>
    public void BeginEdit() { }

    /// <summary>End a batched edit window started by <see cref="BeginEdit"/>.
    /// No-op (see <see cref="BeginEdit"/>).</summary>
    public void EndEdit() { }

    /// <summary>
    /// Append a text fragment as a new line in the paragraph.
    /// </summary>
    public void AppendLine(TextFragment line)
    {
        _lines.Add(line);
    }

    /// <summary>Append a TextFragment line and override its text state.</summary>
    public void AppendLine(TextFragment line, TextState textState)
    {
        if (line is null) return;
        if (textState is not null) line.TextState.ApplyChangesFrom(textState);
        _lines.Add(line);
    }

    /// <summary>Append a TextFragment with text-state override and per-line spacing.</summary>
    public void AppendLine(TextFragment line, TextState textState, float lineSpacing)
    {
        if (line is null) return;
        if (textState is not null) line.TextState.ApplyChangesFrom(textState);
        _spacingBelow[line] = lineSpacing;
        _lines.Add(line);
    }

    /// <summary>
    /// Append a text string as a new line in the paragraph using default text state.
    /// </summary>
    public void AppendLine(string line)
    {
        _lines.Add(new TextFragment(line));
    }

    /// <summary>
    /// Append a text string with the given text state as a new line.
    /// </summary>
    public void AppendLine(string line, TextState textState)
    {
        _lines.Add(new TextFragment(line, textState: textState));
    }

    /// <summary>Append a text string with text state and per-line spacing.</summary>
    public void AppendLine(string line, TextState textState, float lineSpacing)
    {
        var frag = new TextFragment(line, textState: textState);
        _spacingBelow[frag] = lineSpacing;
        _lines.Add(frag);
    }

    /// <summary>Append a text string with per-line spacing using default text state.</summary>
    public void AppendLine(string line, float lineSpacing)
    {
        var frag = new TextFragment(line);
        _spacingBelow[frag] = lineSpacing;
        _lines.Add(frag);
    }

    /// <summary>
    /// Build the paragraph's visual lines. Each fragment's segments flow
    /// horizontally on a shared baseline; a hard '\n' inside a run or a word-wrap
    /// boundary starts a new visual line, and every fragment starts a fresh one.
    /// Returns a list of lines, each a left-to-right list of (text, TextState)
    /// chunks. <paramref name="maxWidth"/> &gt; 0 enables word-wrap.
    /// </summary>
    private List<List<(string text, TextState ts)>> BuildVisualLines(
        double maxWidth, TextFormattingOptions.WordWrapMode wrapMode)
    {
        bool wrap = wrapMode != TextFormattingOptions.WordWrapMode.NoWrap && maxWidth > 0;
        var result = new List<List<(string, TextState)>>();
        _visualLineFragments.Clear();
        TextFragment? current = null;
        void Emit(List<(string, TextState)> line) { result.Add(line); _visualLineFragments.Add(current!); }

        // Emit a single run (one TextState) as one-or-more visual lines via the
        // historical per-run wrap, which also handles discretionary hyphenation.
        void AddSingleRun(TextState ts, string runText)
        {
            if (wrap)
            {
                foreach (var hardLine in runText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                    foreach (var wl in WrapText(hardLine, ts.Font, ts.FontSize, maxWidth, wrapMode))
                        Emit(new List<(string, TextState)> { (wl, ts) });
            }
            else
            {
                // Even with wrapping off (NoWrap / no clip width), an explicit hard
                // newline (\r, \n, \r\n) in the run text is a line break — split on it
                // so a replacement string that embeds Environment.NewLine renders on
                // multiple lines instead of one run.
                foreach (var hardLine in runText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                    Emit(new List<(string, TextState)> { (hardLine, ts) });
            }
        }

        foreach (var fragment in _lines)
        {
            current = fragment;
            // A fragment built from constructor text seeds a single segment whose
            // own TextState can lag later edits to fragment.TextState, so when the
            // fragment carries no explicitly-added segments (Count <= 1) the
            // fragment-level TextState is authoritative.
            if (fragment.Segments.Count <= 1)
            {
                AddSingleRun(fragment.TextState, fragment.Text ?? string.Empty);
                continue;
            }

            // Explicitly-added segments: the segments' own TextStates are
            // authoritative. A single non-empty segment (the rest being the empty
            // seed) is still one run — keep it on the per-run path so hyphenation
            // works; only genuinely multi-run fragments need the cross-segment flow.
            var realSegs = new List<TextSegment>();
            foreach (var s in fragment.Segments)
                if (!string.IsNullOrEmpty(s.Text)) realSegs.Add(s);

            if (realSegs.Count <= 1)
            {
                var seg = realSegs.Count == 1 ? realSegs[0] : null;
                AddSingleRun(seg?.TextState ?? fragment.TextState, seg?.Text ?? fragment.Text ?? string.Empty);
                continue;
            }

            // Multi-segment: segments flow horizontally on a shared baseline.
            var runs = new List<(string text, TextState ts)>();
            foreach (var seg in realSegs)
                runs.Add((seg.Text ?? string.Empty, seg.TextState ?? fragment.TextState));

            // Logical char buffer with a parallel per-char TextState list, line
            // breaks normalised to '\n'. Wrapping/measuring operate on this so a
            // break can land anywhere, including inside a later segment.
            var sb = new System.Text.StringBuilder();
            var charTs = new List<TextState>();
            foreach (var (rt, ts) in runs)
            {
                var norm = rt.Replace("\r\n", "\n").Replace('\r', '\n');
                foreach (var c in norm) { sb.Append(c); charTs.Add(ts); }
            }
            var logical = sb.ToString();

            // Empty fragment still yields one (empty) visual line for clip-height parity.
            if (logical.Length == 0)
            {
                Emit(new List<(string, TextState)> { (string.Empty, fragment.TextState) });
                continue;
            }

            // Split on hard '\n' breaks, word-wrapping each hard line within maxWidth.
            var ranges = new List<(int start, int len)>();
            int pos = 0;
            while (true)
            {
                int nl = logical.IndexOf('\n', pos);
                int hardEnd = nl < 0 ? logical.Length : nl;
                if (wrap) WrapRange(logical, charTs, pos, hardEnd, maxWidth, ranges);
                else ranges.Add((pos, hardEnd - pos));
                if (nl < 0) break;
                pos = nl + 1;
                if (pos > logical.Length) break;
            }

            foreach (var (start, len) in ranges)
                Emit(BuildChunks(logical, charTs, start, len, fragment.TextState));
        }

        return result;
    }

    /// <summary>Split a char range into consecutive same-TextState chunks for
    /// horizontal rendering. An empty range yields a single empty chunk.</summary>
    private static List<(string, TextState)> BuildChunks(string logical, List<TextState> charTs,
        int start, int len, TextState fallback)
    {
        var chunks = new List<(string, TextState)>();
        if (len <= 0) { chunks.Add((string.Empty, fallback)); return chunks; }
        int i = start, end = start + len;
        while (i < end)
        {
            var ts = charTs[i];
            int s = i;
            while (i < end && ReferenceEquals(charTs[i], ts)) i++;
            chunks.Add((logical.Substring(s, i - s), ts));
        }
        return chunks;
    }

}
