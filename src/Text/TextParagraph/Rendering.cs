using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
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
    internal byte[] BuildContent(Page page, Func<string, string> ensureFont,
        Func<FontData, string, (string fontResName, byte[] hexGlyphIds)>? ensureCidFont = null)
    {
        double startX;
        double? clipWidth = null;
        _remainingLines.Clear();

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
            return Array.Empty<byte>();
        }

        var builder = new ContentStreamBuilder();
        builder.SaveState();

        var wrapMode = FormattingOptions.WrapMode;

        // A Position-anchored (or anchorless) paragraph that explicitly asks for
        // wrapping breaks its lines against the default paragraph rectangle,
        // which is 500pt wide from the anchor X (a line wraps at X+500
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

        // A Position-anchored paragraph that did NOT ask for wrapping still lays
        // out against the 500pt default paragraph rectangle: glyphs past X+500 are
        // dropped at a character boundary — not wrapped onto a new line, and not
        // drawn on past the box. Lines that fit are left untouched.
        if (Rectangle is null && Position is not null && !needsWrap)
        {
            for (int li = 0; li < visualLines.Count; li++)
            {
                var line = visualLines[li];
                double run = 0; bool cut = false;
                var kept = new List<(string text, TextState ts)>();
                foreach (var (text, ts) in line)
                {
                    if (cut) break;
                    int keep = text.Length;
                    for (int ci = 0; ci < text.Length; ci++)
                    {
                        var w = CharWidth(text[ci], ts);
                        if (run + w > 500.0001) { keep = ci; cut = true; break; }
                        run += w;
                    }
                    if (keep > 0) kept.Add((text.Substring(0, keep), ts));
                }
                if (cut) visualLines[li] = kept;
            }
        }

        // The block: each line advances by its own font size plus the LineSpacing
        // of the line above it (a line's spacing opens the gap BELOW it; the last
        // line's spacing is not part of the block).
        double blockHeight = BlockHeight(visualLines);

        bool hasRotation = Rotation != 0;

        double startY;
        if (Rectangle is not null)
        {
            // The block is seated in the rect per VerticalAlignment (default
            // Bottom = its bottom ON LLY + Margin.Bottom); startY is its top.
            double blockBottom = BlockBottom(blockHeight);
            startY = blockBottom + blockHeight;

            // Paragraph background: one box under the whole block, as wide as the
            // widest line plus the pad — or exactly the content width when a line
            // overflows it. Written in a local frame (cm + re) like the fragment
            // highlight, before the clip so it is the page's first rectangle.
            if (BackgroundColor is { } pbg && !hasRotation)
            {
                double maxLineWidth = 0;
                foreach (var ln in visualLines)
                {
                    double w = 0;
                    foreach (var (text, ts) in ln) w += MeasureLineWidth(text, ts);
                    if (w > maxLineWidth) maxLineWidth = w;
                }
                double bgWidth = maxLineWidth <= clipWidth!.Value ? maxLineWidth + BackgroundPad : clipWidth.Value;
                builder.SaveState();
                builder.SetFillColor(pbg.R / 255.0, pbg.G / 255.0, pbg.B / 255.0);
                builder.SetMatrix(1, 0, 0, 1, startX, blockBottom);
                builder.Rectangle(0, 0, bgWidth, blockHeight);
                builder.FillEvenOdd();
                builder.RestoreState();
            }

            // The clip spans the content width and rises ClipLineBoxFactor × the
            // block height from the block bottom — past the rect top when the block
            // is taller than the rect — and never reaches below the rect bottom.
            double clipBottom = Math.Max(blockBottom, Rectangle.LLY + Margin.Bottom);
            double clipTop = blockBottom + blockHeight * ClipLineBoxFactor;
            if (clipTop > clipBottom)
            {
                builder.Rectangle(startX, clipBottom, clipWidth!.Value, clipTop - clipBottom);
                builder.Clip();
            }
        }
        else if (Position is not null)
            startY = Position.YIndent;
        else
            startY = 0;

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
        if (!hasRotation && Rectangle is null)
        {
            var lastLine = visualLines[visualLines.Count - 1];
            var lastTs = lastLine.Count > 0 ? lastLine[0].ts : _lines[_lines.Count - 1].TextState;
            var lastFd = lastTs.FontData ?? lastTs.Font?.SourceFontData;
            bool lifted = ensureCidFont is not null && lastFd is { TtfData: not null };
            startY += blockHeight + (lifted ? 0 : GetDescentCompensation(lastTs, LineFontSize(lastLine)));
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
        return builder.Build();
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
        void R(Rectangle? r) => sb.Append(r is null ? "-" : string.Create(ic, $"{r.LLX},{r.LLY},{r.URX},{r.URY}"));
        R(Rectangle);
        sb.Append('|').Append(Position is null ? "-" : string.Create(ic, $"{Position.XIndent},{Position.YIndent}"));
        sb.Append('|').Append((int)VerticalAlignment).Append(',').Append((int)HorizontalAlignment)
          .Append(',').Append((int)FormattingOptions.WrapMode)
          .Append(',').Append(Rotation.ToString(ic)).Append(',').Append(LimitWithBounds ? 1 : 0)
          .Append(',').Append(FirstLineIndent.ToString(ic)).Append(',').Append(SubsequentLinesIndent.ToString(ic))
          .Append(',').Append(Justify ? 1 : 0)
          .Append(',').Append(string.Create(ic, $"{Margin.Left},{Margin.Bottom},{Margin.Right},{Margin.Top}"))
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
        c is null ? "-" : string.Create(CultureInfo.InvariantCulture, $"{c.AByte:X2}{c.R:X2}{c.G:X2}{c.B:X2}");

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
            textY -= LineAdvance(visualLines, li);

            // A bounds-limited paragraph stops at the first line whose baseline
            // falls below the content bottom; that line and the rest are handed
            // back as RemainingLines for the caller to continue elsewhere.
            if (LimitWithBounds && textY < minY)
            {
                for (int ri = li; ri < visualLines.Count; ri++)
                    _remainingLines.Add(RemainingLineFragment(visualLines[ri]));
                break;
            }

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
            // A lifted run (embedded face) already has its layout baseline at the
            // box bottom, so its box starts at textY itself.
            var firstFd = firstTs.FontData ?? firstTs.Font?.SourceFontData;
            bool firstLifted = ensureCidFont is not null && firstFd is { TtfData: not null };
            double bgRectY = Rectangle is null && Position is not null
                ? textY - (firstLifted ? 0 : GetDescentCompensation(firstTs, lineFs))
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
                var fd = ts.FontData ?? ts.Font?.SourceFontData;
                // A Bold/Italic style on a repository face selects the styled family
                // member when the family has one (Arial Bold); a family without it
                // keeps its regular face and synthesises the bold weight below.
                var styled = TextBuilder.ResolveStyledFace(ts, fd);
                var syntheticBold = ts.IsBold && styled is null && fd is { TtfData: not null };
                if (styled is not null) fd = styled;
                // A fragment that explicitly carries a real font program embeds it
                // (with its descriptor) rather than downgrading to a bare
                // Standard-14 alias dict — an explicitly set FontRepository font
                // is embedded even for pure-Latin text, and the absorber
                // needs the descriptor descent to seat the read-back rectangle.
                var needsCid = ensureCidFont is not null &&
                               fd is { TtfData: not null };
                string fontResName;
                byte[]? hexGlyphs = null;
                if (needsCid)
                    (fontResName, hexGlyphs) = ensureCidFont!(fd!, text);
                else
                    fontResName = ensureFont(TextBuilder.MapToStandard14Public(ts));

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
                // Synthesised bold: fill AND stroke the outlines with a pen
                // proportional to the size, reset to the defaults after the run so
                // the following regular lines read back a 1-pt pen.
                if (syntheticBold)
                {
                    builder.SetLineWidth(fontSize * SyntheticBoldPenFactor);
                    builder.SetTextRenderingMode((int)TextRenderingMode.FillThenStrokeText);
                }
                // The run is written one descriptor descent above its layout
                // baseline when its face carries one (see WrittenDescentLift).
                builder.MoveTextPosition(penX, textY + (needsCid ? WrittenDescentLift(fd, fontSize) : 0));
                if (hexGlyphs is not null)
                    builder.ShowTextHex(hexGlyphs);
                else
                    builder.ShowText(text);
                if (syntheticBold)
                {
                    builder.SetLineWidth(1);
                    builder.SetTextRenderingMode((int)TextRenderingMode.FillText);
                }
                builder.EndText();

                penX += MeasureLineWidth(text, ts);
            }
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
    private void RenderLocal(ContentStreamBuilder builder,
        List<List<(string text, TextState ts)>> visualLines,
        Func<string, string> ensureFont,
        Func<FontData, string, (string fontResName, byte[] hexGlyphIds)>? ensureCidFont,
        Page page)
    {
        int lineCount = visualLines.Count;

        // In local coords, lines are placed bottom-up: the last line's baseline at
        // Y=0, each earlier line raised by the advances of the lines below it.
        var localBaseY = new double[lineCount];
        double acc = 0;
        for (int i = lineCount - 1; i >= 0; i--)
        {
            localBaseY[i] = acc;
            if (i > 0) acc += LineAdvance(visualLines, i);
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

            // Emit background rectangle with cm translation. A rotated line folds
            // its rotation into the same cm — the box stays (0, 0, w, h) in the
            // line's local frame and turns with the text.
            var bg = firstTs.BackgroundColor;
            if (bg is not null)
            {
                double bgH = lineFs * 1.1;
                var bgRad = firstTs.Rotation * Math.PI / 180.0;
                double bgCos = Math.Cos(bgRad), bgSin = Math.Sin(bgRad);
                builder.SaveState();
                builder.SetFillColor(bg.R / 255.0, bg.G / 255.0, bg.B / 255.0);
                builder.SetMatrix(bgCos, bgSin, -bgSin, bgCos, lineStartX, localBgY);
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
                // Standard-14 alias dict — an explicitly set FontRepository font
                // is embedded even for pure-Latin text, and the absorber
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
