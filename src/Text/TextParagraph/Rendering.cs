using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using System.Globalization;

namespace Aspose.Pdf.Text;

public sealed partial class TextParagraph
{
    /// <summary>
    /// Build the content stream operators for this paragraph and register fonts,
    /// then append them to the page. Called by <see cref="TextBuilder.AppendParagraph"/>.
    /// </summary>
    internal void Render(Page page, Func<string, string> ensureFont,
        Func<FontData, string, (string fontResName, byte[] hexGlyphIds)>? ensureCidFont = null)
    {
        var bytes = BuildContent(page, ensureFont, ensureCidFont);
        if (bytes.Length > 0) page.AddContentStream(bytes);
    }

    /// <summary>
    /// Lay the paragraph out and build its content stream operators (registering
    /// fonts on <paramref name="page"/>). Returns an empty array for a paragraph
    /// with no lines — such a paragraph writes nothing, not even its clip.
    /// Fills <see cref="RemainingLines"/> with the lines a <see cref="LimitWithBounds"/>
    /// cut left over.
    /// </summary>
    internal byte[] BuildContent(Page page, Func<string, string> ensureFont, Func<FontData, string, (string fontResName, byte[] hexGlyphIds)>? ensureCidFont = null)
    {
        var pc = new ParagraphContentState();
        pc.page = page;
        pc.ensureFont = ensureFont;
        pc.ensureCidFont = ensureCidFont;
        pc.clipWidth = null;
        _remainingLines.Clear();

        if (Rectangle is not null)
        {
            pc.startX = Rectangle.LLX + Margin.Left;
            pc.clipWidth = Rectangle.Width - Margin.Left - Margin.Right;
        }
        else if (Position is not null)
            pc.startX = Position.XIndent;
        else
            pc.startX = 0;

        // No content to render — skip entirely (no clipping rect for empty paragraphs)
        if (_lines.Count == 0)
        {
            return Array.Empty<byte>();
        }

        pc.builder = new ContentStreamBuilder();
        pc.builder.SaveState();

        pc.wrapMode = FormattingOptions.WrapMode;

        // A Position-anchored (or anchorless) paragraph that explicitly asks for
        // wrapping breaks its lines against the default paragraph rectangle,
        // which is 500pt wide from the anchor X (a line wraps at X+500
        // even when more would fit before the page edge, and runs past the page
        // margins). Undefined stays unwrapped here so existing single-line
        // layouts are untouched.
        if (pc.clipWidth is null
            && pc.wrapMode is TextFormattingOptions.WordWrapMode.ByWords
                or TextFormattingOptions.WordWrapMode.DiscretionaryHyphenation)
            pc.clipWidth = 500;

        pc.needsWrap = pc.wrapMode != TextFormattingOptions.WordWrapMode.NoWrap && pc.clipWidth is > 0;

        pc.visualLines = BuildVisualLines(pc.needsWrap ? pc.clipWidth!.Value : 0, pc.wrapMode);

        // A Position-anchored paragraph that did NOT ask for wrapping still lays
        // out against the 500pt default paragraph rectangle: glyphs past X+500 are
        // dropped at a character boundary — not wrapped onto a new line, and not
        // drawn on past the box. Lines that fit are left untouched.
        if (Rectangle is null && Position is not null && !pc.needsWrap)
        {
            TrimUnwrappedLinesToBox(pc);
        }

        pc.blockHeight = BlockHeight(pc.visualLines);

        pc.hasRotation = Rotation != 0;

        if (Rectangle is not null)
        {
            SeatBlockInRectangle(pc);
        }
        else if (Position is not null)
            pc.startY = Position.YIndent;
        else
            pc.startY = 0;

        // A Position anchor seats the BOTTOM of the block's descender box at
        // YIndent (so the absorbed last fragment's Rectangle.LLY == YIndent
        // exactly) and earlier lines stack upward by their advances; RenderAbsolute
        // subtracts each line's advance before drawing, hence the blockHeight
        // offset here. A run with an embedded face is already written one
        // descriptor descent above its layout baseline (WrittenDescentLift), which
        // is exactly that seat; Standard-14 text carries no descriptor and is
        // seated here by its AFM descent instead. The rotated path keeps its own
        // local bottom-anchoring (RenderLocal places the last baseline at the cm
        // origin). A paragraph with NO anchor at all behaves as Position (0,0):
        // the block stacks upward from the page origin (bottom-left corner), so
        // all lines stay on the page instead of running below y=0.
        SeatUnrotatedBlock(pc);

        // When paragraph has rotation, use local coordinate system:
        // cm = (cos, sin, -sin, cos, px, py) sets origin at Position,
        // and all coords are local (0,0) = Position.
        if (pc.hasRotation)
        {
            double rad = Rotation * Math.PI / 180.0;
            double cosR = Math.Cos(rad), sinR = Math.Sin(rad);
            pc.builder.SetMatrix(cosR, sinR, -sinR, cosR, pc.startX, pc.startY);
            RenderLocal(pc.builder, pc.visualLines, pc.ensureFont, pc.ensureCidFont, pc.page);
        }
        else
        {
            RenderAbsolute(pc.builder, pc.visualLines, pc.startX, pc.startY, pc.ensureFont, pc.ensureCidFont, pc.page);
        }

        pc.builder.RestoreState();
        return pc.builder.Build();
    }

    /// <summary>Page-space Y of the block's bottom edge inside <see cref="Rectangle"/>
    /// for the paragraph's <see cref="VerticalAlignment"/>.</summary>
    private double BlockBottom(double blockHeight) =>
        VerticalAnchor(Rectangle!.URY - Margin.Top - blockHeight,
            Rectangle.LLY + (Rectangle.Height - blockHeight) / 2,
            Rectangle.LLY + Margin.Bottom);

    /// <summary>The gap that opens ABOVE visual line <paramref name="li"/>: the
    /// largest TextState.LineSpacing among its chunks and its fragment's own state
    /// (zero when none is set). Every line of a fragment carries it — wrapped
    /// continuations included.</summary>
    private double SpacingAbove(List<List<(string text, TextState ts)>> lines, int li)
    {
        double m = li < _visualLineFragments.Count ? _visualLineFragments[li].TextState.LineSpacing : 0;
        foreach (var (_, ts) in lines[li]) if (ts.LineSpacing > m) m = ts.LineSpacing;
        return Math.Max(0, m);
    }

    /// <summary>The gap that opens BELOW visual line <paramref name="li"/>: the
    /// appended lineSpacing of its fragment, on the fragment's LAST line only, and
    /// never below the block's final line.</summary>
    private double SpacingBelow(List<List<(string text, TextState ts)>> lines, int li)
    {
        if (li >= lines.Count - 1 || li >= _visualLineFragments.Count - 1) return 0;
        var frag = _visualLineFragments[li];
        if (ReferenceEquals(frag, _visualLineFragments[li + 1])) return 0;
        return _spacingBelow.TryGetValue(frag, out var below) ? Math.Max(0, below) : 0;
    }

    /// <summary>Vertical distance from the previous line's baseline (the block top
    /// for the first line) to line <paramref name="li"/>'s baseline: the gap below
    /// the previous line, the gap above this line, and this line's font size.</summary>
    private double LineAdvance(List<List<(string text, TextState ts)>> lines, int li) =>
        (li > 0 ? SpacingBelow(lines, li - 1) : 0) + SpacingAbove(lines, li) + LineFontSize(lines[li]);

    /// <summary>Height of the whole block = the sum of its line advances.</summary>
    private double BlockHeight(List<List<(string text, TextState ts)>> lines)
    {
        double h = 0;
        for (int i = 0; i < lines.Count; i++) h += LineAdvance(lines, i);
        return h;
    }

    /// <summary>Everything the layout depends on, as one string: the paragraph's
    /// own properties plus each line's text and the text-state fields the writer
    /// reads. An attached paragraph is re-laid out at save time only when this
    /// differs from the signature its segment was rendered from.</summary>
    internal string LayoutSignature()
    {
        var sb = new System.Text.StringBuilder();
        var ic = CultureInfo.InvariantCulture;
        void R(Rectangle? r) => sb.Append(r is null ? "-" : Compat.Format(ic, $"{r.LLX},{r.LLY},{r.URX},{r.URY}"));
        R(Rectangle);
        sb.Append('|').Append(Position is null ? "-" : Compat.Format(ic, $"{Position.XIndent},{Position.YIndent}"));
        sb.Append('|').Append((int)VerticalAlignment).Append(',').Append((int)HorizontalAlignment)
          .Append(',').Append((int)FormattingOptions.WrapMode)
          .Append(',').Append(Rotation.ToString(ic)).Append(',').Append(LimitWithBounds ? 1 : 0)
          .Append(',').Append(FirstLineIndent.ToString(ic)).Append(',').Append(SubsequentLinesIndent.ToString(ic))
          .Append(',').Append(Justify ? 1 : 0)
          .Append(',').Append(Compat.Format(ic, $"{Margin.Left},{Margin.Bottom},{Margin.Right},{Margin.Top}"))
          .Append(',').Append(ColorKey(BackgroundColor));
        foreach (var line in _lines)
        {
            sb.Append("\n#").Append(line.Text ?? string.Empty).Append('|')
              .Append(_spacingBelow.TryGetValue(line, out var below) ? below.ToString(ic) : "-").Append('|');
            AppendStateSignature(sb, line.TextState);
            foreach (var seg in line.Segments)
            {
                sb.Append("\n  ").Append(seg.Text ?? string.Empty).Append('|');
                AppendStateSignature(sb, seg.TextState);
            }
        }
        return sb.ToString();
    }

    private static string ColorKey(Color? c) =>
        c is null ? "-" : Compat.Format(CultureInfo.InvariantCulture, $"{c.AByte:X2}{c.R:X2}{c.G:X2}{c.B:X2}");

    private static void AppendStateSignature(System.Text.StringBuilder sb, TextState? ts)
    {
        if (ts is null) { sb.Append('-'); return; }
        var ic = CultureInfo.InvariantCulture;
        sb.Append(ts.FontName ?? "-").Append(',').Append(ts.Font?.FontName ?? "-")
          .Append(',').Append(ts.FontData is null ? "-" : ts.FontData.FontName ?? "?")
          .Append(',').Append(ts.FontSize.ToString(ic)).Append(ts.FontSizeTouched ? "!" : "")
          .Append(',').Append(ts.IsBold ? "B" : "").Append(ts.IsItalic ? "I" : "")
          .Append(ts.Underline ? "U" : "").Append(ts.IsStrikeOut ? "S" : "")
          .Append(',').Append(ts.LineSpacing.ToString(ic))
          .Append(',').Append(ts.CharacterSpacing.ToString(ic)).Append(',').Append(ts.WordSpacing.ToString(ic))
          .Append(',').Append(ts.HorizontalScaling.ToString(ic)).Append(',').Append(ts.Rotation.ToString(ic))
          .Append(',').Append(ColorKey(ts.ForegroundColor))
          .Append(',').Append(ColorKey(ts.BackgroundColor))
          .Append(',').Append(ColorKey(ts.StrokingColor))
          .Append(',').Append((int)ts.RenderingMode);
    }

    /// <summary>
    /// Register an ExtGState dict with the requested fill alpha on the page resources
    /// and return its resource name. Caches per (page, alphaByte) so repeated paragraphs
    /// with the same transparency share one entry. Returns null when alpha is 255 (opaque) —
    /// the caller should skip the gs emission entirely in that case.
    /// </summary>
    /// <summary>The name of an ExtGState on the page carrying <paramref name="fill"/>
    /// as <c>ca</c> and, when <paramref name="strokeToo"/>, as <c>CA</c> as well --
    /// an existing entry with exactly those values, else a new one. Null when the
    /// alpha is 1, which needs no state.</summary>
    internal static string? EnsureAlphaExtGState(Page page, double fill, bool strokeToo)
    {
        if (fill >= 1) return null;
        var extGStateDict = EnsureExtGStateDict(page);
        foreach (var key in extGStateDict.Keys)
        {
            var entry = page.Reader.ResolveDict(extGStateDict.Get(key));
            if (entry is null) continue;
            var ca = entry.Get("ca");
            var caMatches = ca is PdfReal r ? Math.Abs(r.Value - fill) < 0.0001
                : ca is PdfInteger i && Math.Abs(i.Value - fill) < 0.0001;
            if (!caMatches) continue;
            var caStroke = entry.Get("CA");
            if (strokeToo ? caStroke is PdfReal sr && Math.Abs(sr.Value - fill) < 0.0001 : caStroke is null)
                return key;
        }
        var n = 1;
        while (extGStateDict.ContainsKey($"GSa{n}")) n++;
        var name = $"GSa{n}";
        var newEntry = new PdfDictionary();
        newEntry.Set("Type", new PdfName("ExtGState"));
        newEntry.Set("ca", new PdfReal(fill));
        if (strokeToo) newEntry.Set("CA", new PdfReal(fill));
        extGStateDict.Set(name, newEntry);
        return name;
    }

    /// <summary>Gives <paramref name="to"/> every ExtGState <paramref name="from"/>
    /// holds, under the same names, where it has none of that name: a page a flow
    /// spills onto paints with the states the flow ensured on the page it began.</summary>
    internal static void CarryExtGStates(Page from, Page to)
    {
        var resources = from.Reader.ResolveDict(from.Dict.Get("Resources"));
        if (resources is null || from.Reader.ResolveDict(resources.Get("ExtGState")) is not { } source) return;
        var target = EnsureExtGStateDict(to);
        foreach (var key in source.Keys)
            if (!target.ContainsKey(key) && source.Get(key) is { } state)
                target.Set(key, state);
    }

    /// <summary>The page's ExtGState resource dictionary, made when missing.</summary>
    private static PdfDictionary EnsureExtGStateDict(Page page)
    {
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
        return extGStateDict;
    }

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
    private void RenderAbsolute(ContentStreamBuilder builder, List<List<(string text, TextState ts)>> visualLines, double startX, double startY, Func<string, string> ensureFont, Func<FontData, string, (string fontResName, byte[] hexGlyphIds)>? ensureCidFont, Page page)
    {
        var ra = new AbsoluteRenderState();
        ra.builder = builder;
        ra.visualLines = visualLines;
        ra.startX = startX;
        ra.startY = startY;
        ra.ensureFont = ensureFont;
        ra.ensureCidFont = ensureCidFont;
        ra.page = page;
        ra.maxLineWidth = 0;
        ra.anyBg = false;
        foreach (var line in ra.visualLines)
        {
            double lineW = 0;
            foreach (var (text, ts) in line)
            {
                if (ts.BackgroundColor is not null) ra.anyBg = true;
                lineW += MeasureLineWidth(text, ts);
            }
            if (lineW > ra.maxLineWidth) ra.maxLineWidth = lineW;
        }

        ra.textY = ra.startY;
        ra.minY = Rectangle is not null ? Rectangle.LLY + Margin.Bottom : double.NegativeInfinity;

        for (int li = 0; li < ra.visualLines.Count; li++)
        {
            if (!RenderAbsoluteLine(ra, li)) break;
        }
    }

    /// <summary>A visual line that did not fit, as a fragment for the next page:
    /// one segment per run, each carrying a copy of its run's text state (font,
    /// size, colours) so the continuation renders exactly as the cut line would
    /// have. A hyphenated break keeps its hyphen.</summary>
    private static TextFragment RemainingLineFragment(List<(string text, TextState ts)> line)
    {
        var fragment = new TextFragment();
        foreach (var (text, ts) in line)
        {
            var seg = new TextSegment(text);
            seg.TextState.ApplyChangesFrom(ts);
            fragment.Segments.Add(seg);
        }
        if (line.Count > 0) fragment.TextState.ApplyChangesFrom(line[0].ts);
        return fragment;
    }

    /// <summary>
    /// Render paragraph content using local coordinates (origin at paragraph position).
    /// Used when the paragraph has rotation — the rotation cm sets the origin.
    /// Each rect gets its own cm translation so that IsRectanglePresent can
    /// match coordinates without being affected by the rotation cm.
    /// </summary>
    private void RenderLocal(ContentStreamBuilder builder, List<List<(string text, TextState ts)>> visualLines, Func<string, string> ensureFont, Func<FontData, string, (string fontResName, byte[] hexGlyphIds)>? ensureCidFont, Page page)
    {
        var rl = new LocalRenderState();
        rl.builder = builder;
        rl.visualLines = visualLines;
        rl.ensureFont = ensureFont;
        rl.ensureCidFont = ensureCidFont;
        rl.page = page;
        rl.lineCount = rl.visualLines.Count;

        rl.localBaseY = new double[rl.lineCount];
        rl.acc = 0;
        for (int i = rl.lineCount - 1; i >= 0; i--)
        {
            rl.localBaseY[i] = rl.acc;
            if (i > 0) rl.acc += LineAdvance(rl.visualLines, i);
        }

        for (int i = 0; i < rl.lineCount; i++)
        {
            RenderLocalLine(rl, i);
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
