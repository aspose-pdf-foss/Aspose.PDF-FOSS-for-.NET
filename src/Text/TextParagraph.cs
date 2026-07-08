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
public sealed class TextParagraph
{
    private readonly List<TextFragment> _lines = new();

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

    /// <summary>Lines that didn't fit in the paragraph's rectangle and should
    /// be re-rendered on a subsequent page. Populated by the renderer; FOSS
    /// currently returns an empty collection (text overflow is implicit).</summary>
    public TextFragmentCollection RemainingLines { get; } = new TextFragmentCollection();

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
    /// rectangle's bottom edge), matching the Aspose.Pdf default.
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
            foreach (var line in _lines)
            {
                var fontSize = line.TextState.FontSize;
                var lineHeight = fontSize * 1.2; // approximate line height
                totalHeight += lineHeight;
                var font = line.TextState.Font;
                double lineWidth;
                try { lineWidth = font?.MeasureString(line.Text, fontSize) ?? (line.Text.Length * fontSize * 0.5); }
                catch { lineWidth = line.Text.Length * fontSize * 0.5; }
                if (lineWidth > width) width = lineWidth;
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
            foreach (var l in _lines) col.Add(l);
            return col;
        }
    }

    /// <summary>
    /// Amount to raise a bottom-anchored block so the absorbed fragment
    /// Rectangle.LLY (baseline − descriptor descent) sits at the rect bottom
    /// rather than one descent below it.
    /// Gated to real/embedded fonts: only they carry the descriptor descent the
    /// absorber subtracts, so Standard-14 mapped text gets no lift (keeps
    /// Add_Paragraph_VerticalAlignment_Bottom's bottom-line YIndent == rect LLY).
    /// </summary>
    private double BottomAnchorDescentLift(List<List<(string text, TextState ts)>> visualLines)
    {
        if (VerticalAlignment != VerticalAlignment.Bottom || visualLines.Count == 0)
            return 0;
        var lastLine = visualLines[visualLines.Count - 1];
        var lastTs = lastLine.Count > 0 ? lastLine[0].ts : _lines[_lines.Count - 1].TextState;
        return UsesRealFont(lastTs) ? GetDescentCompensation(lastTs, LineFontSize(lastLine)) : 0;
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

        double totalLeading = 0;
        foreach (var ln in visualLines) totalLeading += LineLeading(ln);
        double startX = Rectangle.LLX + Margin.Left;
        double textY = VerticalAnchor(Rectangle.URY - Margin.Top,
            Rectangle.LLY + (Rectangle.Height - totalLeading) / 2 + totalLeading,
            Rectangle.LLY + Margin.Bottom + totalLeading);

        // Walk to the final visual line — its baseline and right edge.
        double baseline = textY, rightEdge = startX, lineStartX = startX, lineFs = 0;
        for (int li = 0; li < visualLines.Count; li++)
        {
            var line = visualLines[li];
            textY -= LineLeading(line);
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

    /// <summary>Begin a batched edit window. The FOSS line writer applies
    /// changes immediately, so this is a no-op kept for parity with the
    /// PdfContentEditor-style flush model.</summary>
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
        line.TextState.LineSpacing = lineSpacing;
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
        frag.TextState.LineSpacing = lineSpacing;
        _lines.Add(frag);
    }

    /// <summary>Append a text string with per-line spacing using default text state.</summary>
    public void AppendLine(string line, float lineSpacing)
    {
        var frag = new TextFragment(line);
        frag.TextState.LineSpacing = lineSpacing;
        _lines.Add(frag);
    }

    private static double MeasureText(string text, FontInfo? font, double fontSize)
    {
        if (font is not null)
        {
            try { return font.MeasureString(text, fontSize); }
            catch { /* fall through */ }
        }
        return text.Length * fontSize * 0.5;
    }

    /// <summary>
    /// Measure line width using FontData TTF metrics, Standard14 metrics, or fallback.
    /// </summary>
    private double MeasureLineWidth(string text, TextState ts)
    {
        var fontSize = ts.FontSize;

        // Non-Standard-14 embedded fonts (e.g. MS Gothic): measure with their real
        // glyph advances so wrap points match what the CID embedder actually draws.
        if (UsesRealFont(ts))
        {
            var gp = GetGlyphParser(ts)!;
            double rw = 0;
            foreach (var ch in text) rw += RealGlyphWidth(ch, fontSize, gp);
            return rw;
        }

        // Try FontData real metrics first (e.g. system TrueType font).
        var fontData = ts.FontData ?? ts.Font?.SourceFontData;
        if (fontData is { TtfData: not null })
            return fontData.MeasureString(text, fontSize);

        // Try Standard14 metrics via font name.
        var fontName = ts.FontName ?? "Helvetica";
        if (Standard14Fonts.IsStandard14(fontName))
        {
            double w = 0;
            foreach (var ch in text)
            {
                var cw = Standard14Fonts.GetWidth(fontName, ch < 256 ? ch : '?');
                w += (cw >= 0 ? cw : 500) * fontSize / 1000.0;
            }
            return w;
        }

        // Proportional fallback
        return text.Length * fontSize * 0.5;
    }

    private static List<string> WrapText(string text, FontInfo? font, double fontSize,
        double maxWidth, TextFormattingOptions.WordWrapMode wrapMode)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            result.Add(text);
            return result;
        }
        if (wrapMode == TextFormattingOptions.WordWrapMode.ByWords)
            WrapByWords(text, font, fontSize, maxWidth, result);
        else if (wrapMode == TextFormattingOptions.WordWrapMode.DiscretionaryHyphenation)
            WrapWithHyphenation(text, font, fontSize, maxWidth, result);
        else
            result.Add(text);
        return result;
    }

    private static void WrapByWords(string text, FontInfo? font, double fontSize,
        double maxWidth, List<string> result)
    {
        var words = text.Split(' ');
        var currentLine = "";
        foreach (var word in words)
        {
            var candidate = currentLine.Length == 0 ? word : currentLine + " " + word;
            if (MeasureText(candidate, font, fontSize) <= maxWidth)
            {
                currentLine = candidate;
            }
            else
            {
                // The word that triggered the break carries its preceding space onto
                // the next line's start, so the completed line keeps a trailing space
                // (matching the reference wrap). The final line gets none.
                if (currentLine.Length > 0) result.Add(currentLine + " ");
                currentLine = word;
            }
        }
        if (currentLine.Length > 0) result.Add(currentLine);
    }

    private static void WrapWithHyphenation(string text, FontInfo? font, double fontSize,
        double maxWidth, List<string> result)
    {
        var hyphenWidth = MeasureText("-", font, fontSize);
        var spaceWidth = MeasureText(" ", font, fontSize);
        var words = text.Split(' ');
        var currentLine = "";
        var currentWidth = 0.0;

        foreach (var word in words)
        {
            var wordWidth = MeasureText(word, font, fontSize);
            var separatorWidth = currentLine.Length == 0 ? 0 : spaceWidth;

            if (currentWidth + separatorWidth + wordWidth <= maxWidth)
            {
                if (currentLine.Length > 0) { currentLine += " "; currentWidth += spaceWidth; }
                currentLine += word;
                currentWidth += wordWidth;
            }
            else if (currentLine.Length == 0)
            {
                HyphenateWord(word, font, fontSize, maxWidth, hyphenWidth, result, out currentLine, out currentWidth);
            }
            else
            {
                var remainingWidth = maxWidth - currentWidth - spaceWidth;
                int fitChars = remainingWidth > hyphenWidth
                    ? FindHyphenBreak(word, font, fontSize, remainingWidth, hyphenWidth) : 0;
                if (fitChars > 0)
                {
                    currentLine += " " + word[..fitChars] + "-";
                    result.Add(currentLine);
                    var remainder = word[fitChars..];
                    if (MeasureText(remainder, font, fontSize) <= maxWidth)
                    {
                        currentLine = remainder;
                        currentWidth = MeasureText(remainder, font, fontSize);
                    }
                    else
                    {
                        currentLine = "";
                        currentWidth = 0;
                        HyphenateWord(remainder, font, fontSize, maxWidth, hyphenWidth, result, out currentLine, out currentWidth);
                    }
                }
                else
                {
                    result.Add(currentLine);
                    if (wordWidth <= maxWidth) { currentLine = word; currentWidth = wordWidth; }
                    else HyphenateWord(word, font, fontSize, maxWidth, hyphenWidth, result, out currentLine, out currentWidth);
                }
            }
        }
        if (currentLine.Length > 0) result.Add(currentLine);
    }

    private static int FindHyphenBreak(string word, FontInfo? font, double fontSize,
        double availableWidth, double hyphenWidth)
    {
        int best = 0;
        for (int i = 1; i < word.Length; i++)
        {
            if (MeasureText(word[..i], font, fontSize) + hyphenWidth <= availableWidth) best = i;
            else break;
        }
        return best;
    }

    private static void HyphenateWord(string word, FontInfo? font, double fontSize,
        double maxWidth, double hyphenWidth, List<string> result,
        out string remainingLine, out double remainingWidth)
    {
        var pos = 0;
        while (pos < word.Length)
        {
            var remaining = word[pos..];
            var remainW = MeasureText(remaining, font, fontSize);
            if (remainW <= maxWidth) { remainingLine = remaining; remainingWidth = remainW; return; }
            int fitChars = FindHyphenBreak(remaining, font, fontSize, maxWidth, hyphenWidth);
            if (fitChars <= 0) fitChars = 1;
            result.Add(remaining[..fitChars] + "-");
            pos += fitChars;
        }
        remainingLine = "";
        remainingWidth = 0;
    }

    /// <summary>
    /// Build the content stream operators for this paragraph and register fonts.
    /// Called by <see cref="TextBuilder.AppendParagraph"/>.
    /// </summary>
    internal void Render(Page page, Func<string, string> ensureFont,
        Func<FontData, string, (string fontResName, byte[] hexGlyphIds)>? ensureCidFont = null)
    {
        double startX;
        double? clipWidth = null;

        if (Rectangle is not null)
        {
            startX = Rectangle.LLX + Margin.Left;
            clipWidth = Rectangle.Width - Margin.Left - Margin.Right;
        }
        else if (Position is not null)
            startX = Position.XIndent;
        else
            startX = 0;

        // No content to render — skip entirely (no clipping rect for empty paragraphs)
        if (_lines.Count == 0)
        {
            return;
        }

        var builder = new ContentStreamBuilder();
        builder.SaveState();

        var wrapMode = FormattingOptions.WrapMode;

        // A Position-anchored (or anchorless) paragraph that explicitly asks for
        // wrapping breaks its lines against the default paragraph rectangle,
        // which is 500pt wide from the anchor X (probed: a line wraps at X+500
        // even when more would fit before the page edge, and runs past the page
        // margins). Undefined stays unwrapped here so existing single-line
        // layouts are untouched.
        if (clipWidth is null
            && wrapMode is TextFormattingOptions.WordWrapMode.ByWords
                or TextFormattingOptions.WordWrapMode.DiscretionaryHyphenation)
            clipWidth = 500;

        bool needsWrap = wrapMode != TextFormattingOptions.WordWrapMode.NoWrap && clipWidth is > 0;

        // Build the visual lines. Each visual line is a horizontal sequence of
        // runs ("chunks") that share a baseline: a fragment's segments flow onto
        // the same line until a hard '\n' or a word-wrap boundary starts a new
        // one, and each fragment begins a fresh line. Word-wrap measures across
        // segment boundaries via a per-character logical buffer so a break can
        // land inside a later segment ("the" Arial 30 + " quick brown…" MSGothic
        // 10 keeps "the quick" together then wraps the rest).
        var visualLines = BuildVisualLines(needsWrap ? clipWidth!.Value : 0, wrapMode);

        // Per-line leading = max over the line's runs of (FontSize + LineSpacing).
        // With LineSpacing == 0 this is just FontSize, matching the historical
        // single-pitch advance so no-spacing layouts stay byte-identical.
        double totalLeading = 0;
        foreach (var ln in visualLines) totalLeading += LineLeading(ln);

        if (Rectangle is not null)
        {
            // Clip height scales with rendered-line count. Single-line content
            // (empty paragraph, space, short label) keeps the historical
            // first-line-FontBBox sizing — Add_Paragraph_EmptyString/Space/
            // SingleSmallLine assert exactly (LLX, LLY, Width, fontBBox*fs)
            // clip rectangles. Multi-line content sums each line's height by the
            // same BBox factor, then is widened to the total leading so explicit
            // LineSpacing gaps don't push lower lines outside the clip.
            var firstFs = visualLines.Count > 0 ? LineFontSize(visualLines[0]) : _lines[0].TextState.FontSize;
            var firstFontName = (visualLines.Count > 0
                ? LineFontName(visualLines[0])
                : _lines[0].TextState.FontName) ?? "Helvetica";
            int bboxH = Standard14Fonts.GetFontBBoxHeight(firstFontName);
            double bboxFactor = bboxH > 0 ? bboxH / 1000.0 : 1.16;
            double clipHeight;
            if (visualLines.Count <= 1)
            {
                clipHeight = bboxFactor * firstFs;
            }
            else
            {
                double totalContent = 0;
                foreach (var ln in visualLines) totalContent += LineFontSize(ln);
                clipHeight = totalContent * bboxFactor;
            }
            // Ensure the clip covers the full stacked block (matters once
            // LineSpacing spreads the lines beyond the bare glyph heights).
            if (clipHeight < totalLeading) clipHeight = totalLeading;
            // Don't exceed the user's Rectangle height — preserves the contract
            // that the clip never extends beyond the requested bounds.
            if (clipHeight > Rectangle.Height) clipHeight = Rectangle.Height;
            // The clip sits where the text is anchored vertically within the rect.
            double clipY = VerticalAnchor(Rectangle.URY - Margin.Top - clipHeight,
                Rectangle.LLY + (Rectangle.Height - clipHeight) / 2,
                Rectangle.LLY + Margin.Bottom);
            builder.Rectangle(Rectangle.LLX, clipY, Rectangle.Width, clipHeight);
            builder.Clip();
        }

        double startY;
        if (Rectangle is not null)
        {
            // startY is the top of the first line; the block is anchored within
            // the rect per VerticalAlignment (default Bottom = just above LLY).
            // A bottom-anchored block is seated one descent HIGHER than the
            // bare rect bottom, so the absorbed fragment Rectangle.LLY (== baseline
            // − descriptor descent) lands ON Rectangle.LLY+Margin.Bottom rather than
            // one descent below it. The lift is gated to real/embedded
            // fonts — only they carry the descriptor descent the absorber subtracts;
            // Standard-14 mapped text keeps its baseline at the rect bottom.
            double bottomLift = BottomAnchorDescentLift(visualLines);
            startY = VerticalAnchor(Rectangle.URY - Margin.Top,
                Rectangle.LLY + (Rectangle.Height - totalLeading) / 2 + totalLeading,
                Rectangle.LLY + Margin.Bottom + totalLeading + bottomLift);
        }
        else if (Position is not null)
            startY = Position.YIndent;
        else
            startY = 0;

        bool hasRotation = Rotation != 0;

        // A Position anchor seats the BOTTOM of the block's descender box at
        // YIndent: the last line's baseline sits one descent above it and
        // earlier lines stack upward by their leading (so the absorbed last
        // fragment's Rectangle.LLY == YIndent exactly). RenderAbsolute
        // subtracts each line's leading before drawing, hence the totalLeading
        // offset here. The rotated path keeps its own local bottom-anchoring
        // (RenderLocal places the last baseline at the cm origin).
        if (!hasRotation && Rectangle is null && Position is not null)
        {
            var lastLine = visualLines[visualLines.Count - 1];
            var lastTs = lastLine.Count > 0 ? lastLine[0].ts : _lines[_lines.Count - 1].TextState;
            startY += totalLeading + GetDescentCompensation(lastTs, LineFontSize(lastLine));
        }

        // When paragraph has rotation, use local coordinate system:
        // cm = (cos, sin, -sin, cos, px, py) sets origin at Position,
        // and all coords are local (0,0) = Position.
        if (hasRotation)
        {
            double rad = Rotation * Math.PI / 180.0;
            double cosR = Math.Cos(rad), sinR = Math.Sin(rad);
            builder.SetMatrix(cosR, sinR, -sinR, cosR, startX, startY);
            RenderLocal(builder, visualLines, ensureFont, ensureCidFont, page);
        }
        else
        {
            RenderAbsolute(builder, visualLines, startX, startY, ensureFont, ensureCidFont, page);
        }

        builder.RestoreState();
        page.AddContentStream(builder.Build());
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

        // Emit a single run (one TextState) as one-or-more visual lines via the
        // historical per-run wrap, which also handles discretionary hyphenation.
        void AddSingleRun(TextState ts, string runText)
        {
            if (wrap)
            {
                foreach (var hardLine in runText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                    foreach (var wl in WrapText(hardLine, ts.Font, ts.FontSize, maxWidth, wrapMode))
                        result.Add(new List<(string, TextState)> { (wl, ts) });
            }
            else
            {
                // Even with wrapping off (NoWrap / no clip width), an explicit hard
                // newline (\r, \n, \r\n) in the run text is a line break — split on it
                // so a replacement string that embeds Environment.NewLine renders on
                // multiple lines instead of one run.
                foreach (var hardLine in runText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                    result.Add(new List<(string, TextState)> { (hardLine, ts) });
            }
        }

        foreach (var fragment in _lines)
        {
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
                result.Add(new List<(string, TextState)> { (string.Empty, fragment.TextState) });
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
                result.Add(BuildChunks(logical, charTs, start, len, fragment.TextState));
        }

        return result;
    }

    /// <summary>
    /// Greedy word-wrap of the char range [<paramref name="start"/>, <paramref name="end"/>)
    /// of <paramref name="logical"/> into <paramref name="ranges"/>, measuring each
    /// candidate with per-character widths (so mixed-font runs wrap correctly).
    /// The first word of a line is never broken even if it overflows.
    /// </summary>
    private void WrapRange(string logical, List<TextState> charTs,
        int start, int end, double maxWidth, List<(int, int)> ranges)
    {
        if (end <= start) { ranges.Add((start, 0)); return; }

        // Words = maximal runs of non-space characters within the range.
        var words = new List<(int s, int e)>();
        int j = start;
        while (j < end)
        {
            while (j < end && logical[j] == ' ') j++;
            if (j >= end) break;
            int s = j;
            while (j < end && logical[j] != ' ') j++;
            words.Add((s, j));
        }
        if (words.Count == 0) { ranges.Add((start, end - start)); return; }

        int curStart = words[0].s, curEnd = words[0].e;
        for (int k = 1; k < words.Count; k++)
        {
            // Measure from the line start to this word's end (includes the
            // inter-word spaces; excludes any trailing space after the word).
            if (RangeWidth(logical, charTs, curStart, words[k].e) <= maxWidth)
                curEnd = words[k].e;
            else
            {
                ranges.Add((curStart, curEnd - curStart));
                curStart = words[k].s;
                curEnd = words[k].e;
            }
        }
        ranges.Add((curStart, curEnd - curStart));
    }

    /// <summary>Sum of per-character advance widths over [start, end).</summary>
    private double RangeWidth(string logical, List<TextState> charTs, int start, int end)
    {
        double w = 0;
        for (int i = start; i < end && i < charTs.Count; i++)
            w += CharWidth(logical[i], charTs[i]);
        return w;
    }

    /// <summary>Advance width of a single character in the given text state,
    /// using TTF metrics, Standard-14 metrics, or a proportional fallback —
    /// mirroring <see cref="MeasureLineWidth"/>.</summary>
    private double CharWidth(char c, TextState ts)
    {
        if (c == '\n') return 0;
        // Non-Standard-14 embedded fonts (e.g. MS Gothic) are drawn with their real
        // glyphs, so measure them the same way. Latin core families (Arial/Times/
        // Courier) and fonts whose glyph data can't be read are substituted by the
        // Standard-14 font MapToStandard14 resolves — measure that instead.
        if (UsesRealFont(ts))
            return RealGlyphWidth(c, ts.FontSize, GetGlyphParser(ts)!);
        var std14 = TextBuilder.MapToStandard14Public(ts);
        var cw = Standard14Fonts.GetWidth(std14, c < 256 ? c : '?');
        return (cw >= 0 ? cw : 500) * ts.FontSize / 1000.0;
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

    /// <summary>Largest font size among a visual line's chunks (clip-height/baseline sizing).</summary>
    private static double LineFontSize(List<(string text, TextState ts)> line)
    {
        double m = 0;
        foreach (var (_, ts) in line) if (ts.FontSize > m) m = ts.FontSize;
        return m > 0 ? m : 12;
    }

    /// <summary>Vertical advance for a visual line = max over its chunks of
    /// (FontSize + LineSpacing). LineSpacing &lt;= 0 contributes nothing.</summary>
    private static double LineLeading(List<(string text, TextState ts)> line)
    {
        double m = 0;
        foreach (var (_, ts) in line)
        {
            double l = ts.FontSize + (ts.LineSpacing > 0 ? ts.LineSpacing : 0);
            if (l > m) m = l;
        }
        return m > 0 ? m : 12;
    }

    /// <summary>Font name of a visual line's first chunk, used for clip-height BBox lookup.</summary>
    private static string LineFontName(List<(string text, TextState ts)> line)
        => (line.Count > 0 ? line[0].ts.FontName : null) ?? "Helvetica";

    /// <summary>
    /// Register an ExtGState dict with the requested fill alpha on the page resources
    /// and return its resource name. Caches per (page, alphaByte) so repeated paragraphs
    /// with the same transparency share one entry. Returns null when alpha is 255 (opaque) —
    /// the caller should skip the gs emission entirely in that case.
    /// </summary>
    internal static string? EnsureFillAlphaExtGState(Page page, byte alpha)
    {
        if (alpha >= 255) return null;

        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var extGStateDict = page.Reader.ResolveDict(resources.Get("ExtGState"));
        if (extGStateDict is null)
        {
            extGStateDict = new PdfDictionary();
            resources.Set("ExtGState", extGStateDict);
        }

        var caValue = alpha / 255.0;
        var caString = caValue.ToString("0.######", CultureInfo.InvariantCulture);

        // Reuse existing entry with matching /ca (and no /CA mismatch — we only set fill).
        foreach (var key in extGStateDict.Keys)
        {
            var entry = page.Reader.ResolveDict(extGStateDict.Get(key));
            if (entry is null) continue;
            // Match if this entry has the same /ca value AND no /CA setting
            // (stroke alpha defaults to 1.0; we don't want to inherit a stroke setting).
            if (entry.Get("CA") is not null) continue;
            var existing = entry.Get("ca");
            if (existing is PdfReal pr && Math.Abs(pr.Value - caValue) < 0.0001)
                return key;
            if (existing is PdfInteger pi && Math.Abs(pi.Value - caValue) < 0.0001)
                return key;
        }

        // Create a new entry. Pick the first free /GSn name.
        var n = 1;
        while (extGStateDict.ContainsKey($"GSa{n}")) n++;
        var name = $"GSa{n}";

        var newEntry = new PdfDictionary();
        newEntry.Set("Type", new PdfName("ExtGState"));
        newEntry.Set("ca", new PdfReal(caValue));
        extGStateDict.Set(name, newEntry);
        return name;
    }

    /// <summary>
    /// Render paragraph content using absolute page-space coordinates.
    /// Used when the paragraph has no rotation. Preserves the original
    /// top-down positioning approach with Td text positioning.
    /// </summary>
    private void RenderAbsolute(ContentStreamBuilder builder,
        List<List<(string text, TextState ts)>> visualLines,
        double startX, double startY,
        Func<string, string> ensureFont,
        Func<FontData, string, (string fontResName, byte[] hexGlyphIds)>? ensureCidFont,
        Page page)
    {
        // Precompute max line width for bg rects (all lines get uniform width).
        double maxLineWidth = 0;
        bool anyBg = false;
        foreach (var line in visualLines)
        {
            double lineW = 0;
            foreach (var (text, ts) in line)
            {
                if (ts.BackgroundColor is not null) anyBg = true;
                lineW += MeasureLineWidth(text, ts);
            }
            if (lineW > maxLineWidth) maxLineWidth = lineW;
        }

        double textY = startY;
        double minY = Rectangle is not null ? Rectangle.LLY + Margin.Bottom : double.NegativeInfinity;

        for (int li = 0; li < visualLines.Count; li++)
        {
            var line = visualLines[li];
            double lineFs = LineFontSize(line);
            textY -= LineLeading(line);

            if (LimitWithBounds && textY < minY) break;

            // First-line / subsequent-line indent shifts the line's left edge.
            double lineStartX = startX + (li == 0 ? FirstLineIndent : SubsequentLinesIndent);

            double lineWidth = 0;
            foreach (var (text, ts) in line) lineWidth += MeasureLineWidth(text, ts);

            // Background / underline use the line's first chunk as the representative
            // state (single-segment lines — the common case — are unchanged).
            var firstTs = line.Count > 0 ? line[0].ts : _lines[0].TextState;
            // A Position-anchored block paints each line's background on its glyph
            // box (bottom at baseline − descent, so the last line's rect bottom is
            // exactly Position.YIndent); the Rectangle path keeps the historical
            // baseline + fontSize seat its op-level tests pin.
            double bgRectY = Rectangle is null && Position is not null
                ? textY - GetDescentCompensation(firstTs, lineFs)
                : textY + lineFs;

            var bg = firstTs.BackgroundColor;
            if (bg is not null)
            {
                double bgH = lineFs * 1.1;
                double bgW = anyBg ? maxLineWidth : lineWidth;
                builder.SaveState();
                builder.SetFillColor(bg.R / 255.0, bg.G / 255.0, bg.B / 255.0);
                builder.Raw($"{F2T(lineStartX)} {F2T(bgRectY)} {F2T(bgW)} {F2T(bgH)} re");
                builder.Fill();
                builder.RestoreState();
            }

            if (firstTs.IsUnderline)
            {
                double descentComp = GetDescentCompensation(firstTs, lineFs);
                double ulY = bgRectY + descentComp * 0.1;
                double ulH = GetUnderlineThickness(firstTs, lineFs);
                double ulW = lineWidth;
                var fg = firstTs.ForegroundColor;
                double r = fg?.R / 255.0 ?? 0, g = fg?.G / 255.0 ?? 0, b2 = fg?.B / 255.0 ?? 0;
                builder.SaveState();
                builder.SetFillColor(r, g, b2);
                builder.SetMatrix(1, 0, 0, 1, lineStartX, ulY);
                builder.Rectangle(0, 0, ulW, ulH);
                builder.FillEvenOdd();
                builder.RestoreState();
            }

            // Draw the line's chunks left-to-right, sharing the baseline textY.
            double penX = lineStartX;
            foreach (var (rawText, ts) in line)
            {
                // Shape Arabic to connected presentation forms in visual order so the
                // embedded-font path emits the cursive glyphs; a no-op for non-Arabic.
                var text = ArabicShaper.ShapeForDisplay(rawText);
                var fontSize = ts.FontSize;
                var fontName = ts.FontName ?? "Helvetica";
                var fd = ts.FontData ?? ts.Font?.SourceFontData;
                // A fragment that explicitly carries a real font program embeds it
                // (with its descriptor) rather than downgrading to a bare
                // Standard-14 alias dict — the reference embeds an explicitly set
                // FontRepository font even for pure-Latin text, and the absorber
                // needs the descriptor descent to seat the read-back rectangle.
                var needsCid = ensureCidFont is not null &&
                               fd is { TtfData: not null };
                string fontResName;
                byte[]? hexGlyphs = null;
                if (needsCid)
                    (fontResName, hexGlyphs) = ensureCidFont!(fd!, text);
                else
                    fontResName = ensureFont(fontName);

                var fgColor = ts.ForegroundColor;
                var alphaGsName = fgColor is not null ? EnsureFillAlphaExtGState(page, fgColor.AByte) : null;
                if (alphaGsName is not null)
                    builder.SetExtGState(alphaGsName);
                builder.BeginText();
                if (fgColor is not null)
                    builder.SetFillColor(fgColor.R / 255.0, fgColor.G / 255.0, fgColor.B / 255.0);
                builder.SetFont(fontResName, fontSize);
                // Emit Tc/Tw so the line's character/word spacing is applied on render and
                // re-parse; guarded so default (zero) spacing keeps byte-identical output.
                if (ts.CharacterSpacing != 0)
                    builder.SetCharSpacing(ts.CharacterSpacing);
                if (ts.WordSpacing != 0)
                    builder.SetWordSpacing(ts.WordSpacing);
                builder.MoveTextPosition(penX, textY);
                if (hexGlyphs is not null)
                    builder.ShowTextHex(hexGlyphs);
                else
                    builder.ShowText(text);
                builder.EndText();

                penX += MeasureLineWidth(text, ts);
            }
        }
    }

    /// <summary>
    /// Render paragraph content using local coordinates (origin at paragraph position).
    /// Used when the paragraph has rotation — the rotation cm sets the origin.
    /// Each rect gets its own cm translation so that IsRectanglePresent can
    /// match coordinates without being affected by the rotation cm.
    /// </summary>
    private void RenderLocal(ContentStreamBuilder builder,
        List<List<(string text, TextState ts)>> visualLines,
        Func<string, string> ensureFont,
        Func<FontData, string, (string fontResName, byte[] hexGlyphIds)>? ensureCidFont,
        Page page)
    {
        int lineCount = visualLines.Count;

        // In local coords, lines are placed bottom-up: the last line's baseline at
        // Y=0, each earlier line raised by the sum of the leadings below it.
        var localBaseY = new double[lineCount];
        double acc = 0;
        for (int i = lineCount - 1; i >= 0; i--)
        {
            localBaseY[i] = acc;
            acc += LineLeading(visualLines[i]);
        }

        for (int i = 0; i < lineCount; i++)
        {
            var line = visualLines[i];
            double lineFs = LineFontSize(line);
            var firstTs = line.Count > 0 ? line[0].ts : _lines[0].TextState;

            double localBgY = localBaseY[i];
            double descentComp = GetDescentCompensation(firstTs, lineFs);
            double localTextY = localBgY + descentComp;
            double lineStartX = i == 0 ? FirstLineIndent : SubsequentLinesIndent;

            double lineWidth = 0;
            foreach (var (text, ts) in line) lineWidth += MeasureLineWidth(text, ts);

            // Emit background rectangle with cm translation.
            var bg = firstTs.BackgroundColor;
            if (bg is not null)
            {
                double bgH = lineFs * 1.1;
                builder.SaveState();
                builder.SetFillColor(bg.R / 255.0, bg.G / 255.0, bg.B / 255.0);
                builder.SetMatrix(1, 0, 0, 1, lineStartX, localBgY);
                builder.Rectangle(0, 0, lineWidth, bgH);
                builder.FillEvenOdd();
                builder.RestoreState();
            }

            // Emit underline rectangle if this line is underlined.
            if (firstTs.IsUnderline)
            {
                double ulY = localBgY + descentComp * 0.1;
                double ulH = GetUnderlineThickness(firstTs, lineFs);
                var fg = firstTs.ForegroundColor;
                double r = fg?.R / 255.0 ?? 0, g = fg?.G / 255.0 ?? 0, b = fg?.B / 255.0 ?? 0;
                builder.SaveState();
                builder.SetFillColor(r, g, b);
                builder.SetMatrix(1, 0, 0, 1, lineStartX, ulY);
                builder.Rectangle(0, 0, lineWidth, ulH);
                builder.FillEvenOdd();
                builder.RestoreState();
            }

            // Emit the line's chunks left-to-right with Tm positioning.
            double penX = lineStartX;
            foreach (var (rawText, ts) in line)
            {
                // Shape Arabic to connected presentation forms in visual order so the
                // embedded-font path emits the cursive glyphs; a no-op for non-Arabic.
                var text = ArabicShaper.ShapeForDisplay(rawText);
                var fontSize = ts.FontSize;
                var fontName = ts.FontName ?? "Helvetica";
                var fd = ts.FontData ?? ts.Font?.SourceFontData;
                // A fragment that explicitly carries a real font program embeds it
                // (with its descriptor) rather than downgrading to a bare
                // Standard-14 alias dict — the reference embeds an explicitly set
                // FontRepository font even for pure-Latin text, and the absorber
                // needs the descriptor descent to seat the read-back rectangle.
                var needsCid = ensureCidFont is not null &&
                               fd is { TtfData: not null };
                string fontResName;
                byte[]? hexGlyphs = null;
                if (needsCid)
                    (fontResName, hexGlyphs) = ensureCidFont!(fd!, text);
                else
                    fontResName = ensureFont(fontName);

                var fgColor = ts.ForegroundColor;
                var alphaGsName = fgColor is not null ? EnsureFillAlphaExtGState(page, fgColor.AByte) : null;
                if (alphaGsName is not null)
                    builder.SetExtGState(alphaGsName);
                builder.BeginText();
                if (fgColor is not null)
                    builder.SetFillColor(fgColor.R / 255.0, fgColor.G / 255.0, fgColor.B / 255.0);
                builder.SetFont(fontResName, fontSize);
                if (ts.CharacterSpacing != 0)
                    builder.SetCharSpacing(ts.CharacterSpacing);
                if (ts.WordSpacing != 0)
                    builder.SetWordSpacing(ts.WordSpacing);
                builder.SetTextMatrix(1, 0, 0, 1, penX, localTextY);
                if (hexGlyphs is not null)
                    builder.ShowTextHex(hexGlyphs);
                else
                    builder.ShowText(text);
                builder.EndText();

                penX += MeasureLineWidth(text, ts);
            }
        }
    }

    /// <summary>
    /// Get the descent compensation in points for text baseline positioning.
    /// Returns a positive value representing the distance from bg rect bottom
    /// to the text baseline.
    /// </summary>
    private static double GetDescentCompensation(TextState ts, double fontSize)
    {
        // Try TrueType font metrics first.
        var fontData = ts.FontData ?? ts.Font?.SourceFontData;
        if (fontData is { TtfData: not null })
        {
            var (_, descent, _, _) = FontRepository.ReadTtfMetrics(fontData.TtfData);
            if (descent != 0)
                return Math.Abs(descent) / 1000.0 * fontSize;
        }

        // Fall back to Standard14 descent.
        var fontName = ts.FontName ?? "Helvetica";
        var std14Descent = Standard14Fonts.GetDescent(fontName);
        if (std14Descent != 0)
            return Math.Abs(std14Descent) / 1000.0 * fontSize;

        // Default: 20% of font size.
        return fontSize * 0.2;
    }

    /// <summary>
    /// Get the underline thickness in points.
    /// Uses the font's post table underlineThickness metric when available,
    /// otherwise defaults to 5% of font size.
    /// </summary>
    private static double GetUnderlineThickness(TextState ts, double fontSize)
    {
        var fontData = ts.FontData ?? ts.Font?.SourceFontData;
        if (fontData is { TtfData: not null })
        {
            try
            {
                var parser = new TrueTypeParser(fontData.TtfData);
                if (parser.UnderlineThickness > 0)
                {
                    double scale = 1000.0 / (parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000);
                    return parser.UnderlineThickness * scale / 1000.0 * fontSize;
                }
            }
            catch { /* fall through */ }
        }
        return fontSize * 0.05;
    }

    /// <summary>
    /// Format a value with 2 decimal places using truncation (floor).
    /// Produces values like "101.98" from 101.988, matching the public API's
    /// content stream precision for background rectangle coordinates.
    /// </summary>
    private static string F2T(double v)
    {
        // Use string formatting to truncate: format to 3 decimals, then strip last digit.
        var s = v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        // Remove last digit (effectively truncating to 2 decimal places).
        s = s[..^1];
        // Remove trailing zeros and trailing dot for cleaner output.
        if (s.Contains('.'))
        {
            s = s.TrimEnd('0').TrimEnd('.');
        }
        return s;
    }
}
