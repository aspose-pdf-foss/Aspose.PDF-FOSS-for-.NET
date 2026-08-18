using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

/// <summary>
/// Appends text fragments to a PDF page by registering fonts in the page
/// resources and writing content stream operators.
/// </summary>
public sealed class TextBuilder
{
    private readonly Page _page;

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

    /// <summary>
    /// Append a text fragment to the page.
    /// Registers a Standard 14 font in the page resources and writes the
    /// BT … ET content stream.
    /// </summary>
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

    public void AppendText(TextFragment textFragment) => AppendText(textFragment, false);

    private void AppendText(TextFragment textFragment, bool addTrailingSpace)
    {
        var fragment = textFragment;
        var text = fragment.Text + (addTrailingSpace ? " " : "");

        // If FontData was set via implicit FontData→FontInfo conversion on TextState.Font,
        // propagate it to FontData so the font gets embedded properly.
        if (fragment.TextState.FontData is null && fragment.TextState.Font?.SourceFontData is { } srcFd)
            fragment.TextState.FontData = srcFd;

        // Route every embedded-TTF fragment through the CIDFont (Type0 /
        // Identity-H) path so glyph advances align with the font's own metrics.
        // Pitfall: the multi-segment branch below originally emitted
        // ShowText(literal) against the Identity-H font, producing
        // nonsense glyph IDs from each
        // pair of ASCII bytes. The branch now encodes each segment's text as
        // 2-byte glyph IDs via the same parser the fragment-level path uses,
        // so the CID route is safe for both single- and multi-segment
        // fragments. TextAbsorber.DecodeWithToUnicode round-trips the text
        // for extraction via the emitted /ToUnicode CMap.
        // A Bold/Italic FontStyle on a repository-resolved (non-core) face selects
        // the styled family member (Times New Roman + Bold|Italic → the Bold Italic
        // face); the embedded /BaseFont then reports
        // family+styles. Genuine Core-14 names keep the Standard-14 mapping below.
        if (fragment.TextState.IsBold || fragment.TextState.IsItalic)
        {
            var family = fragment.TextState.FontData?.FontName
                ?? fragment.TextState.Font?.FontName ?? fragment.TextState.FontName;
            if (!string.IsNullOrEmpty(family) && !Standard14Fonts.IsCoreName(family)
                && !family.Contains("Bold", StringComparison.OrdinalIgnoreCase)
                && !family.Contains("Italic", StringComparison.OrdinalIgnoreCase))
            {
                var styleSuffix = (fragment.TextState.IsBold ? " Bold" : string.Empty)
                    + (fragment.TextState.IsItalic ? " Italic" : string.Empty);
                var spaced = System.Text.RegularExpressions.Regex.Replace(family, "(?<=[a-z])(?=[A-Z])", " ");
                var styled = FontRepository.FindFontData(family + styleSuffix)
                    ?? (spaced != family ? FontRepository.FindFontData(spaced + styleSuffix) : null);
                // Only accept a face that really carries the requested style — the
                // resolver falls back to the regular face when no styled file exists.
                var wantTag = fragment.TextState.IsBold ? "Bold" : "Italic";
                if (styled?.TtfData is not null
                    && styled.FontName?.Contains(wantTag, StringComparison.OrdinalIgnoreCase) == true)
                    fragment.TextState.FontData = styled;
            }
        }

        var needsCid = fragment.TextState.FontData is { TtfData: not null };

        // Arabic is cursive: the embedded-font path resolves each character through the
        // font's cmap to a single glyph, so the base letters must first be replaced with
        // their contextual presentation forms (and lam-alef ligatures) and reordered to
        // visual order. Without this an embedded Arabic font renders disjoint, isolated,
        // logical-order letters. Only the CID path benefits — Standard-14 fonts have no
        // Arabic glyphs regardless.
        if (needsCid)
            text = ArabicShaper.ShapeForDisplay(text);

        string fontResName;
        byte[]? hexGlyphIds = null;

        if (needsCid)
        {
            var fontData = fragment.TextState.FontData!;
            // A font whose cmap lacks glyphs for the text is
            // silently substituted with a covering host face (Thai → Tahoma, Han →
            // SimSun, …) — otherwise every missing char writes glyph 0 and the
            // duplicate ToUnicode entries garble extraction.
            if (!FontRepository.CoversText(fontData.TtfData, text))
            {
                var substitute = FontRepository.SubstituteForMissingGlyphs(text, fragment.TextState.Font);
                if (substitute?.TtfData is not null) fontData = substitute;
            }
            (fontResName, hexGlyphIds) = EnsureEmbeddedCIDFont(fontData, text);
        }
        else if (fragment.TextState.FontData is { TtfData: not null } fontData2)
        {
            fontResName = EnsureEmbeddedTrueTypeFont(fontData2);
        }
        else
        {
            var baseFontName = fragment.TextState.Std14FaceOverride
                ?? MapToStandard14(fragment.TextState);
            fontResName = EnsureFontResource(baseFontName, fragment.TextState.EmitStandard14Descriptor,
                fragment.TextState.Std14Widths);
        }

        var fontSize = fragment.TextState.FontSize;
        var x = fragment.Position?.XIndent ?? 0;
        var y = fragment.Position?.YIndent ?? 0;

        // Compute descent compensation for embedded fonts.
        // The absorber's Position.Y = Td.Y + descent*fs/1000 (descent is negative),
        // so we must write Td.Y = user_y - descent*fs/1000 to round-trip correctly.
        double descentComp = needsCid
            ? ComputeCidDescentCompensation(fragment.TextState, fontSize)
            : ComputeDescentCompensation(fragment.TextState, fontSize);

        var builder = new ContentStreamBuilder();
        builder.SaveState();

        // Tab-stop line: the text is a sequence of runs separated by #$TAB markers,
        // each run seated against its stop — ending at it, centred on it, or starting
        // from it — with the stop's leader drawn across the gap the tab opened.
        if (fragment.TabStops is { Count: > 0 } stops
            && fragment.Text.Contains(TabMarker, StringComparison.Ordinal))
        {
            AppendTabbedLine(builder, fragment, stops, fontResName, fontSize, x, y, descentComp);
            builder.RestoreState();
            _page.AddContentStream(builder.Build());
            _page.ResetContentsCache();
            return;
        }

        // Emit background-colour rectangles for any segment with BackgroundColor.
        EmitBackgroundRectangles(builder, fragment, fontResName, fontSize, x, y);

        // Check if segments have distinct positions — if so, render each segment separately.
        var hasDistinctPositions = false;
        if (fragment.Segments.Count > 1)
        {
            foreach (var seg in fragment.Segments)
            {
                if (seg.Position is not null &&
                    (Math.Abs(seg.Position.XIndent - x) > 0.01 || Math.Abs(seg.Position.YIndent - y) > 0.01))
                {
                    hasDistinctPositions = true;
                    break;
                }
            }
        }

        // Segments that carry their OWN font/size/style flow sequentially, each in
        // its own BT block with its own font resource (a bold 50pt word
        // inside a 30pt sentence is its own run; a newline starts a
        // fresh baseline one font-size step down with an empty fragment-font marker
        // run at the line start).
        var hasStyledSegments = !hasDistinctPositions
            && SegmentStylesDiffer(fragment, fontSize);

        if (hasStyledSegments)
        {
            AppendStyledSegments(fragment, builder, fontResName, fontSize, descentComp, x, y);
        }
        else if (hasDistinctPositions)
        {
            // Per-segment glyph encoder for the CID path -- needed because the
            // distinct-position branch can't reuse the fragment-level
            // hexGlyphIds (each segment has its own text + position). Cache the
            // parser once across all segments.
            GlyphOutlineParser? segGlyphParser = null;
            if (needsCid)
                segGlyphParser = new GlyphOutlineParser(fragment.TextState.FontData!.TtfData!);

            // Render each segment in its own BT.ET block at its position.
            foreach (var seg in fragment.Segments)
            {
                var segX = seg.Position?.XIndent ?? x;
                var segY = seg.Position?.YIndent ?? y;
                var segFs = seg.TextState.FontSize > 0 ? seg.TextState.FontSize : fontSize;
                var segFg = seg.TextState.ForegroundColor ?? fragment.TextState.ForegroundColor;
                var segSc = seg.TextState.StrokingColor ?? fragment.TextState.StrokingColor;
                var segDescentComp = segFs != fontSize
                    ? (needsCid
                        ? ComputeCidDescentCompensation(fragment.TextState, segFs)
                        : ComputeDescentCompensation(fragment.TextState, segFs))
                    : descentComp;

                if (segFg is not null)
                {
                    var segGs = TextParagraph.EnsureFillAlphaExtGState(_page, segFg.AByte);
                    if (segGs is not null) builder.SetExtGState(segGs);
                    builder.SetFillColor(segFg.R / 255.0, segFg.G / 255.0, segFg.B / 255.0);
                }
                if (segSc is not null)
                    builder.SetStrokeColor(segSc.R / 255.0, segSc.G / 255.0, segSc.B / 255.0);

                builder.BeginText();
                builder.SetFont(fontResName, segFs);
                builder.MoveTextPosition(segX, segY - segDescentComp);
                // Shape Arabic to connected presentation forms in visual order; a
                // no-op for non-Arabic. Per-segment shaping resolves joining within
                // each segment (cross-segment joining is not modelled here).
                var segText = ArabicShaper.ShapeForDisplay(seg.Text);
                if (segGlyphParser is not null && !string.IsNullOrEmpty(segText))
                {
                    // CID path: encode the segment text as 2-byte glyph IDs.
                    // Without this, ShowText(literal) would emit `(segText) Tj`
                    // against an Identity-H font, which the renderer reads as
                    // pairs of bytes and resolves to nonsense glyph IDs.
                    var bytes = new byte[segText.Length * 2];
                    for (var k = 0; k < segText.Length; k++)
                    {
                        var gid = segGlyphParser.CMap.TryGetValue(segText[k], out var g) ? g : 0;
                        bytes[k * 2] = (byte)(gid >> 8);
                        bytes[k * 2 + 1] = (byte)(gid & 0xFF);
                    }
                    builder.ShowTextHex(bytes);
                }
                else
                {
                    builder.ShowText(segText);
                }
                builder.EndText();
            }
            // Per-segment (glyph-by-glyph) fragments write each segment separately, so the
            // trailing word-space appended to `text` above is never rendered. Emit it as its
            // own space glyph just past the fragment's right edge so the copied word keeps a
            // word boundary on re-extraction.
            if (addTrailingSpace)
            {
                var endX = fragment.Rectangle is { } rr ? rr.URX : x;
                builder.BeginText();
                builder.SetFont(fontResName, fontSize);
                builder.MoveTextPosition(endX, y - descentComp);
                if (segGlyphParser is not null)
                {
                    var gid = segGlyphParser.CMap.TryGetValue(' ', out var g) ? g : 0;
                    builder.ShowTextHex(new[] { (byte)(gid >> 8), (byte)(gid & 0xFF) });
                }
                else
                {
                    builder.ShowText(" ");
                }
                builder.EndText();
            }
        }
        else
        {
            var fg = fragment.TextState.ForegroundColor;
            if (fg?.PatternColorSpace is Aspose.Pdf.Drawing.GradientAxialShading grad)
            {
                // A gradient foreground paints the run through a PatternType-2 shading
                // pattern whose matrix spans the run's advance box, axis running in the
                // text's logical direction (an RTL run starts its gradient at the right).
                var patName = EmitTextGradientPattern(grad, fragment, text, hexGlyphIds, fontSize, x, y);
                if (patName is not null) builder.Raw($"/Pattern cs /{patName} scn\n");
                else builder.SetFillColor(0, 0, 0);
            }
            else if (fg is not null)
            {
                var fgGs = TextParagraph.EnsureFillAlphaExtGState(_page, fg.AByte);
                if (fgGs is not null) builder.SetExtGState(fgGs);
                builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
            }

            var sc = fragment.TextState.StrokingColor;
            if (sc is not null)
                builder.SetStrokeColor(sc.R / 255.0, sc.G / 255.0, sc.B / 255.0);

            // A STROKING render mode also needs its pen: the default line width is a
            // full point, which at text sizes floods the glyphs into blobs. Producers
            // that fake a bold weight this way — filling and stroking the same regular
            // face rather than switching to a bold one — set the pen to a fraction of
            // the size, and the replacement has to keep that relationship for the run to
            // come back the same weight.
            var mode = fragment.TextState.RenderingMode;
            var strokes = mode is Aspose.Pdf.Text.TextRenderingMode.StrokeText
                or Aspose.Pdf.Text.TextRenderingMode.FillThenStrokeText
                or Aspose.Pdf.Text.TextRenderingMode.StrokeTextAndAddPathToClipping
                or Aspose.Pdf.Text.TextRenderingMode.FillThenStrokeTextAndAddPathToClipping;
            if (strokes) builder.SetLineWidth(fontSize / SyntheticBoldPenRatio);

            builder.BeginText();
            builder.SetFont(fontResName, fontSize);
            if (mode != Aspose.Pdf.Text.TextRenderingMode.FillText)
                builder.SetTextRenderingMode((int)mode);
            // Emit Tc/Tw so the requested character/word spacing is actually applied when
            // the page is rendered or re-parsed (without these operators the run renders at
            // default spacing). The fragment's own q/Q scope confines them to this run.
            if (fragment.TextState.CharacterSpacing != 0)
                builder.SetCharSpacing(fragment.TextState.CharacterSpacing);
            if (fragment.TextState.WordSpacing != 0)
                builder.SetWordSpacing(fragment.TextState.WordSpacing);
            // Horizontal scaling (Tz): stretch/compress the run's glyph advances. Without
            // this the renderer draws at 100% regardless of TextState.HorizontalScaling.
            if (Math.Abs(fragment.TextState.HorizontalScaling - 100) > 1e-9)
                builder.SetHorizontalScaling(fragment.TextState.HorizontalScaling);

            // \n inside the fragment text needs explicit T* breaks: PDF's Tj
            // operator renders the whole string on one line (newline chars are
            // either dropped or drawn as .notdef). Without splitting, a multi-
            // line input collapses to a single overflowing line.
            var normalised = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var hasNewlines = normalised.IndexOf('\n') >= 0;
            // An explicit LineSpacing is extra leading on top of the glyph height
            // (line pitch = fontSize + LineSpacing, matching the generator paginator);
            // otherwise fall back to the default 1.2x leading.
            var lineHeight = fragment.TextState.LineSpacing > 0
                ? fontSize + fragment.TextState.LineSpacing
                : fontSize * 1.2;
            if (hasNewlines) builder.SetLeading(lineHeight);
            // A non-zero TextState.Rotation rotates the run about its position via a
            // text matrix; the descent shift (applied straight down in the unrotated
            // case) is rotated to stay perpendicular to the rotated baseline.
            var rotation = fragment.TextState.Rotation;
            if (Math.Abs(rotation % 360.0) > 1e-9)
            {
                var rad = rotation * Math.PI / 180.0;
                double cos = Math.Cos(rad), sin = Math.Sin(rad);
                builder.SetTextMatrix(cos, sin, -sin, cos,
                    x + descentComp * sin, y - descentComp * cos);
            }
            else if (Math.Abs(fragment.TextState.SourceTmScale - 1.0) > 1e-9)
            {
                // Text drawn under a horizontally scaled matrix: a replacement put in
                // its place carries the same scale, so it occupies the same width.
                builder.SetTextMatrix(fragment.TextState.SourceTmScale, 0, 0, 1, x, y - descentComp);
            }
            else
            {
                builder.MoveTextPosition(x, y - descentComp);
            }

            if (hexGlyphIds is not null)
            {
                // For CID fonts the hex glyph stream is byte-aligned (2 bytes/glyph)
                // but newlines came in as char positions, not byte positions. Re-build
                // per-line hex slices from the original text using the same mapping.
                if (hasNewlines)
                    WriteCidLinesWithBreaks(builder, fragment.TextState.FontData!, normalised);
                else
                    builder.ShowTextHex(hexGlyphIds);
            }
            else
            {
                var lines = normalised.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i > 0) builder.NextLine();
                    if (lines[i].Length > 0) builder.ShowText(lines[i]);
                }
            }

            builder.EndText();
        }

        builder.RestoreState();

        if (fragment.TextState.MarkedContentTag is { } mcTag)
            _page.AddMarkedContentStream(builder.Build(), mcTag, fragment.TextState.MarkedContentMcid);
        else
            _page.AddContentStream(builder.Build());
        // Record the fragment's LOGICAL text, not the display form: Arabic input
        // is shaped into presentation forms above, and storing the shaped string
        // makes the save-time sync see a phantom text change — TextReplacer then
        // re-writes the run without shaping.
        fragment.LastWrittenText = fragment.Text + (addTrailingSpace ? " " : "");
        _page.RegisterAttachedFragment(fragment);

        // Underline/strikeout are drawn as thin rectangles at save time. The
        // TextState flags are typically set before the fragment is attached to a
        // page, so the property setters' own registration (which needs a SourcePage)
        // is skipped — register here now that the fragment lives on this page. The
        // flag may sit on the fragment's TextState or on any of its segments.
        // Only a REQUESTED underline is drawn. One the absorber merely observed under the
        // source describes the page as it already is — re-emitting it for a replacement
        // lays a second copy over the original rule.
        bool underline = fragment.TextState.UnderlineRequested;
        bool strikeOut = fragment.TextState.StrikeOut;
        if (fragment.Segments is { } segs)
        {
            foreach (var seg in segs)
            {
                if (seg.TextState.UnderlineRequested) underline = true;
                if (seg.TextState.StrikeOut) strikeOut = true;
            }
        }
        if (underline) _page.RegisterUnderlineFragment(fragment);
        if (strikeOut) _page.RegisterStrikeOutFragment(fragment);
    }

    /// <summary>Register a PatternType-2 (axial shading) pattern spanning the run's
    /// advance box and return its resource name. The gradient axis runs in the text's
    /// logical direction, so an RTL run's gradient starts at its right edge.</summary>
    private string? EmitTextGradientPattern(Aspose.Pdf.Drawing.GradientAxialShading grad,
        TextFragment fragment, string shapedText, byte[]? hexGlyphIds, double fontSize, double x, double y)
    {
        var width = MeasureRunWidth(fragment, shapedText, hexGlyphIds, fontSize);
        if (width <= 0) return null;

        var rtl = ArabicShaper.ContainsArabic(fragment.Text);
        var pattern = new PdfDictionary();
        pattern.Set("Type", new PdfName("Pattern"));
        pattern.Set("PatternType", new PdfInteger(2));
        pattern.Set("Shading", Aspose.Pdf.Drawing.Shape.BuildAxialShadingDict(grad));
        var matrix = new PdfArray();
        foreach (var v in new[] { rtl ? -width : width, 0, 0, fontSize, rtl ? x + width : x, y })
            matrix.Add(new PdfReal(v));
        pattern.Set("Matrix", matrix);
        return _page.AddPattern(pattern);
    }

    /// <summary>Advance width of the run in points: embedded-font glyph advances for
    /// the CID path, Standard-14 AFM widths otherwise.</summary>
    private static double MeasureRunWidth(TextFragment fragment, string shapedText,
        byte[]? hexGlyphIds, double fontSize)
    {
        if (hexGlyphIds is not null && fragment.TextState.FontData?.TtfData is { } ttf)
        {
            try
            {
                var parser = new GlyphOutlineParser(ttf);
                double units = 0;
                for (var i = 0; i + 1 < hexGlyphIds.Length; i += 2)
                    units += parser.GetAdvanceWidth((hexGlyphIds[i] << 8) | hexGlyphIds[i + 1]);
                if (parser.UnitsPerEm > 0 && units > 0)
                    return units * fontSize / parser.UnitsPerEm;
            }
            catch
            {
                // Unparseable program — fall through to the AFM estimate.
            }
        }

        var name = fragment.TextState.FontName ?? "Helvetica";
        double sum = 0;
        foreach (var ch in shapedText)
        {
            var w = ch <= 255 ? Standard14Fonts.GetWidth(name, ch) : -1;
            if (w <= 0) w = 500;
            sum += w;
        }
        return sum * fontSize / 1000.0;
    }

    /// <summary>
    /// Emit a multi-line CID-encoded text run, breaking lines on '\n'. For each
    /// non-empty line, look up the per-character glyph IDs from the embedded TTF
    /// and write them as a 2-byte hex stream; emit a `T*` (next-line) between
    /// lines so the page-level leading set on the builder advances the cursor.
    /// </summary>
    private static void WriteCidLinesWithBreaks(ContentStreamBuilder builder, FontData fontData, string normalised)
    {
        var glyphParser = new GlyphOutlineParser(fontData.TtfData!);
        var lines = normalised.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) builder.NextLine();
            if (lines[i].Length == 0) continue;
            // Iterate by codepoint so surrogate pairs (emoji, CJK Ext-B) map to ONE glyph.
            var line = lines[i];
            var bytes = new System.Collections.Generic.List<byte>(line.Length * 2);
            for (var k = 0; k < line.Length; k++)
            {
                int cp = line[k];
                if (char.IsHighSurrogate(line[k]) && k + 1 < line.Length && char.IsLowSurrogate(line[k + 1]))
                {
                    cp = char.ConvertToUtf32(line[k], line[k + 1]);
                    k++;
                }
                int gid = 0;
                if (glyphParser.CMap.TryGetValue(cp, out var mapped)) gid = mapped;
                bytes.Add((byte)(gid >> 8));
                bytes.Add((byte)(gid & 0xFF));
            }
            builder.ShowTextHex(bytes.ToArray());
        }
    }

    /// <summary>
    /// For each segment whose TextState.BackgroundColor is set, emit a filled
    /// rectangle behind the text. The rectangle's position comes from the
    /// segment's Position (or falls back to the fragment's position) and its
    /// width is estimated from glyph metrics. This must be called BEFORE
    /// BT so the rectangles render behind the glyphs.
    /// </summary>
    private void EmitBackgroundRectangles(ContentStreamBuilder builder, TextFragment fragment,
        string fontResName, double fontSize, double defaultX, double defaultY)
    {
        // Check fragment-level background first, then per-segment.
        // Segment-level overrides fragment-level.
        var fragBg = fragment.TextState.BackgroundColor;
        double curX = defaultX;
        double curY = defaultY;

        // Whether the render takes the per-segment styled path (each segment its
        // own font/size) or the uniform fragment path — the highlight measures
        // with the same state the glyphs are drawn with.
        var styledSegs = SegmentStylesDiffer(fragment, fontSize);

        foreach (var seg in fragment.Segments)
        {
            var bg = seg.TextState.BackgroundColor ?? fragBg;
            if (bg is not null)
            {
                var segX = seg.Position?.XIndent ?? curX;
                var segY = seg.Position?.YIndent ?? curY;
                var segFs = styledSegs && seg.TextState.FontSize > 0 ? seg.TextState.FontSize : fontSize;

                var fontName = (styledSegs ? seg.TextState.FontName : null) ?? fragment.TextState.FontName;
                var fd = (styledSegs ? seg.TextState.FontData ?? seg.TextState.Font?.SourceFontData : null)
                    ?? fragment.TextState.FontData ?? fragment.TextState.Font?.SourceFontData;
                double LineWidth(string s)
                {
                    if (fd is { TtfData: not null })
                        return fd.MeasureString(s, segFs);
                    if (!string.IsNullOrEmpty(fontName) && Standard14Fonts.IsStandard14(fontName!))
                    {
                        double w = 0;
                        foreach (var ch in s)
                        {
                            var cw = Standard14Fonts.GetWidth(fontName!, ch < 256 ? ch : '?');
                            w += (cw >= 0 ? cw : 500) * segFs / 1000.0;
                        }
                        return w;
                    }
                    return s.Length * segFs * 0.5; // proportional fallback
                }

                // The box: 1.1 em tall, bottom at the fragment's anchor Y (the
                // descent line the position round-trip is anchored to) — the same
                // shape the paragraph flow emits. The rect is written in a LOCAL
                // frame placed by a cm (rotation folded in), colour set before the
                // cm so the cm immediately precedes the rectangle operands.
                var rectH = segFs * 1.1;
                // Line pitch for a multi-line (\n-joined) chunk — matches the AppendText render.
                var bgLineHeight = seg.TextState.LineSpacing > 0 ? segFs + seg.TextState.LineSpacing
                    : fragment.TextState.LineSpacing > 0 ? segFs + fragment.TextState.LineSpacing
                    : segFs * 1.2;

                var rotation = seg.TextState.Rotation != 0 ? seg.TextState.Rotation : fragment.TextState.Rotation;
                var rad = rotation * Math.PI / 180.0;
                double cos = Math.Cos(rad), sin = Math.Sin(rad);
                // One filled rectangle behind each rendered line (a chunk from the
                // paginator arrives as its wrapped lines joined by \n). A multi-line
                // block tiles its rectangles at the full line pitch so the highlight
                // is continuous; a single line uses the 1.1-em box.
                var segLines = seg.Text.Replace("\r\n", "\n").Split('\n');
                var rh = segLines.Length > 1 ? bgLineHeight : rectH;
                // DrawTextRectangleBorder strokes the same box in the state's
                // stroking colour — the colour pair is written before the matrix,
                // and the box is painted with one fill-and-stroke.
                var borderColor = fragment.TextState.DrawTextRectangleBorder
                    ? seg.TextState.StrokingColor ?? fragment.TextState.StrokingColor
                    : null;
                for (var li = 0; li < segLines.Length; li++)
                {
                    var lw = LineWidth(segLines[li]);
                    if (lw <= 0) continue;
                    builder.SaveState();
                    builder.SetFillColor(bg.R / 255.0, bg.G / 255.0, bg.B / 255.0);
                    if (borderColor is not null)
                        builder.SetStrokeColor(borderColor.R / 255.0, borderColor.G / 255.0, borderColor.B / 255.0);
                    builder.SetMatrix(cos, sin, -sin, cos, segX, segY - li * bgLineHeight);
                    builder.Rectangle(0, 0, lw, rh);
                    if (borderColor is not null) builder.FillAndStrokeEvenOdd();
                    else builder.Fill();
                    builder.RestoreState();
                }
            }
            // Advance curX/curY for next segment (basic horizontal flow)
            curX += seg.Text.Length * fontSize * 0.5;
        }
    }

    /// <summary>
    /// Compute the descent offset (in points) that the absorber will add to Position.Y.
    /// Returns descentOffset = descent * fontSize / 1000 (negative for typical fonts).
    /// For Standard14 fonts (no font descriptor in PDF), the absorber does not apply
    /// descent to Position, so this returns 0.
    /// </summary>
    /// <summary>Descent compensation for the CID (Type0) embed path. The Type0
    /// descriptor writes the hhea descender, so the writer must
    /// compensate with the SAME value the absorber will read back — using the OS/2
    /// typographic descent here would shift every round-tripped Y by their delta.</summary>
    internal static double ComputeCidDescentCompensation(TextState state, double fontSize)
    {
        if (state.FontData is { TtfData: not null } fd)
        {
            var d = HheaDescentPerMille(fd.TtfData);
            if (d != 0) return d * fontSize / 1000.0;
        }
        return ComputeDescentCompensation(state, fontSize);
    }

    /// <summary>hhea descender normalized to a 1000-unit em (0 when unparsable).</summary>
    internal static double HheaDescentPerMille(byte[] ttf)
    {
        try
        {
            var ttp = new TrueTypeParser(ttf);
            ttp.Parse();
            if (ttp.Descent != 0 && ttp.UnitsPerEm > 0)
                // Truncate exactly like the Type0 descriptor writer does — the
                // absorber reads the INTEGER /Descent back, and the compensation
                // must cancel it to the last hundredth of a point.
                return (int)((double)ttp.Descent * 1000 / ttp.UnitsPerEm);
        }
        catch { }
        return 0;
    }

    private static double ComputeDescentCompensation(TextState state, double fontSize)
    {
        // Embedded TrueType fonts have a descriptor with Descent — absorber uses it.
        if (state.FontData is { TtfData: not null } fontData)
        {
            var (_, descent, _, _) = FontRepository.ReadTtfMetrics(fontData.TtfData);
            if (descent != 0)
                return descent * fontSize / 1000.0;
        }
        // Standard14 fonts: absorber only applies descent when run.Metrics is not null
        // and Descent != 0. For bare Type1 Standard14 font dicts (no descriptor),
        // run.Metrics comes from the font descriptor — which we don't emit for Standard14.
        // So no compensation needed. (The hOCR overlay opts into descriptor emission
        // via TextState.EmitStandard14Descriptor and lifts its baselines itself.)
        return 0;
    }

    private static bool HasNonWinAnsiChars(string text)
    {
        foreach (var c in text)
            if (c > 0xFF) return true;
        return false;
    }

    // ── Font mapping ────────────────────────────────────────────────

    /// <summary>
    /// Map TextState font properties to a Standard 14 base font name.
    /// Exposed so Document.cs pagination can compute glyph widths without
    /// duplicating the bold/italic resolution logic.
    /// </summary>
    internal static string MapToStandard14Public(TextState state) => MapToStandard14(state);

    /// <summary>
    /// True when the font name belongs to a Latin core family that
    /// <see cref="MapToStandard14(string)"/> resolves to a real Standard-14 font
    /// (Helvetica/Arial, Times, Courier, Symbol, ZapfDingbats). Fonts that fall
    /// through to the Helvetica fallback (e.g. MS Gothic) return false, so callers
    /// can embed and use their actual glyphs instead of substituting Helvetica.
    /// </summary>
    internal static bool IsStandard14Family(string? name)
    {
        if (string.IsNullOrEmpty(name)) return true; // unset → default Helvetica
        var n = name.ToLowerInvariant().Replace(" ", "").Replace("-", "");
        return n.StartsWith("arial", StringComparison.Ordinal)
            || n.StartsWith("helvetica", StringComparison.Ordinal)
            || n.StartsWith("times", StringComparison.Ordinal)
            || n.StartsWith("courier", StringComparison.Ordinal)
            || n is "serif" or "monospace" or "symbol" or "zapfdingbats" or "dingbats";
    }


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
    /// Map TextState font properties to a Standard 14 base font name.
    /// </summary>
    private static string MapToStandard14(TextState state)
    {
        var name = state.FontName?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            // Choose based on bold/italic flags
            return (state.IsBold, state.IsItalic) switch
            {
                (true, true) => "Helvetica-BoldOblique",
                (true, false) => "Helvetica-Bold",
                (false, true) => "Helvetica-Oblique",
                _ => "Helvetica"
            };
        }

        // Normalize for comparison
        var lower = name.ToLowerInvariant().Replace(" ", "");

        // Helvetica / Arial family
        if (lower is "helvetica" or "arial")
            return PickVariant("Helvetica", state);
        if (lower is "helvetica-bold" or "helveticabold" or "arialbold")
            return "Helvetica-Bold";
        if (lower is "helvetica-oblique" or "helveticaoblique" or "arialitalic")
            return "Helvetica-Oblique";
        if (lower is "helvetica-boldoblique" or "helveticaboldoblique" or "arialbolditalic")
            return "Helvetica-BoldOblique";

        // Times family
        if (lower is "times-roman" or "timesroman" or "timesnewroman" or "times" or "serif")
            return PickTimesVariant(state);
        if (lower is "times-bold" or "timesbold")
            return "Times-Bold";
        if (lower is "times-italic" or "timesitalic")
            return "Times-Italic";
        if (lower is "times-bolditalic" or "timesbolditalic")
            return "Times-BoldItalic";

        // Courier family
        if (lower is "courier" or "couriernew" or "monospace")
            return PickCourierVariant(state);
        if (lower is "courier-bold" or "courierbold")
            return "Courier-Bold";
        if (lower is "courier-oblique" or "courieroblique")
            return "Courier-Oblique";
        if (lower is "courier-boldoblique" or "courierboldoblique")
            return "Courier-BoldOblique";

        // Symbol / ZapfDingbats
        if (lower is "symbol")
            return "Symbol";
        if (lower is "zapfdingbats" or "dingbats")
            return "ZapfDingbats";

        // Prefix fallback for PostScript / subset / suffixed family names the exact
        // aliases above miss — e.g. "TimesNewRomanPSMT", "ArialMT", "CourierNewPSMT",
        // "ABCDEF+TimesNewRoman". Strip a subset prefix, then match the family stem so
        // a preserved Times/Arial/Courier font keeps its family instead of collapsing
        // to the Helvetica fallback.
        var stem = lower;
        var plus = stem.IndexOf('+');
        if (plus >= 0 && plus + 1 < stem.Length) stem = stem.Substring(plus + 1);
        if (stem.StartsWith("times", StringComparison.Ordinal))
            return PickTimesVariant(state);
        if (stem.StartsWith("arial", StringComparison.Ordinal) || stem.StartsWith("helvetica", StringComparison.Ordinal))
            return PickVariant("Helvetica", state);
        if (stem.StartsWith("courier", StringComparison.Ordinal))
            return PickCourierVariant(state);

        // Fallback: Helvetica
        return PickVariant("Helvetica", state);
    }

    /// <summary>
    /// Map a font name string to a Standard 14 base font name (no bold/italic flags).
    /// </summary>
    private static string MapToStandard14(string fontName)
    {
        var ts = new TextState { FontName = fontName };
        return MapToStandard14(ts);
    }

    private static string PickVariant(string family, TextState state) =>
        (state.IsBold, state.IsItalic) switch
        {
            (true, true) => $"{family}-BoldOblique",
            (true, false) => $"{family}-Bold",
            (false, true) => $"{family}-Oblique",
            _ => family
        };

    private static string PickTimesVariant(TextState state) =>
        (state.IsBold, state.IsItalic) switch
        {
            (true, true) => "Times-BoldItalic",
            (true, false) => "Times-Bold",
            (false, true) => "Times-Italic",
            _ => "Times-Roman"
        };

    private static string PickCourierVariant(TextState state) =>
        (state.IsBold, state.IsItalic) switch
        {
            (true, true) => "Courier-BoldOblique",
            (true, false) => "Courier-Bold",
            (false, true) => "Courier-Oblique",
            _ => "Courier"
        };

    /// <summary>
    /// Append a text paragraph to the page. The paragraph renders its lines
    /// within its bounding rectangle (or at its position) and registers fonts
    /// in the page resources.
    /// </summary>
    public void AppendParagraph(TextParagraph textParagraph)
    {
        var paragraph = textParagraph;
        paragraph.Render(_page,
            baseFontName =>
            {
                var mapped = MapToStandard14(baseFontName);
                return EnsureFontResource(mapped);
            },
            (fontData, text) => EnsureEmbeddedCIDFont(fontData, text));
        paragraph.updatePositioningCalls++;
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
    private void AppendStyledSegments(TextFragment fragment, ContentStreamBuilder builder,
        string fragResName, double fontSize, double fragDescentComp, double x, double y)
    {
        var lineH = fontSize > 0 ? fontSize : 12.0;
        double curX = x, curY = y;
        var lineStarted = false;

        void EmitMarker()
        {
            builder.BeginText();
            builder.SetFont(fragResName, fontSize > 0 ? fontSize : 12.0);
            builder.MoveTextPosition(curX, curY - fragDescentComp);
            builder.ShowText(string.Empty);
            builder.EndText();
        }

        foreach (var seg in fragment.Segments)
        {
            var segState = seg.TextState;
            var segText = seg.Text ?? string.Empty;
            if (segText.Length == 0)
            {
                // The parameterless-ctor empty segment surfaces as its own empty run.
                builder.BeginText();
                builder.SetFont(fragResName, fontSize > 0 ? fontSize : 12.0);
                builder.MoveTextPosition(curX, curY - fragDescentComp);
                builder.ShowText(string.Empty);
                builder.EndText();
                continue;
            }

            var segFs = segState is { FontSizeTouched: true, FontSize: > 0 }
                ? segState.FontSize
                : (fontSize > 0 ? fontSize : 12.0);
            var segFont = segState?.Font is { } sfnt && !ReferenceEquals(sfnt, FontInfo.DefaultHelvetica)
                ? sfnt
                : fragment.TextState.Font;
            var segData = segState?.FontData ?? segFont?.SourceFontData;

            // Styled-face upgrade (Bold/Italic selects the styled family member).
            if (segState is not null && (segState.IsBold || segState.IsItalic))
            {
                var family = segData?.FontName ?? segFont?.FontName ?? segState.FontName;
                if (!string.IsNullOrEmpty(family) && !Standard14Fonts.IsCoreName(family)
                    && !family.Contains("Bold", StringComparison.OrdinalIgnoreCase)
                    && !family.Contains("Italic", StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = (segState.IsBold ? " Bold" : string.Empty)
                        + (segState.IsItalic ? " Italic" : string.Empty);
                    var spaced = System.Text.RegularExpressions.Regex.Replace(family, "(?<=[a-z])(?=[A-Z])", " ");
                    var styled = FontRepository.FindFontData(family + suffix)
                        ?? (spaced != family ? FontRepository.FindFontData(spaced + suffix) : null);
                    var wantTag = segState.IsBold ? "Bold" : "Italic";
                    if (styled?.TtfData is not null
                        && styled.FontName?.Contains(wantTag, StringComparison.OrdinalIgnoreCase) == true)
                        segData = styled;
                }
            }

            var segFg = segState?.ForegroundColor ?? fragment.TextState.ForegroundColor;
            var lines = segText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var li = 0; li < lines.Length; li++)
            {
                if (li > 0)
                {
                    curY -= lineH;
                    curX = x;
                    lineStarted = false;
                }
                if (!lineStarted)
                {
                    EmitMarker();
                    lineStarted = true;
                }
                var piece = lines[li];
                if (piece.Length == 0) continue;

                double segComp = 0;
                string resName;
                byte[]? hexIds = null;
                if (segData?.TtfData is not null)
                {
                    (resName, hexIds) = EnsureEmbeddedCIDFont(segData, piece);
                    var (_, d, _, _) = FontRepository.ReadTtfMetrics(segData.TtfData);
                    if (d != 0) segComp = d * segFs / 1000.0;
                }
                else
                {
                    var segMapState = segState ?? fragment.TextState;
                    resName = EnsureFontResource(MapToStandard14(segMapState));
                }

                if (segFg is not null)
                    builder.SetFillColor(segFg.R / 255.0, segFg.G / 255.0, segFg.B / 255.0);
                builder.BeginText();
                builder.SetFont(resName, segFs);
                builder.MoveTextPosition(curX, curY - segComp);
                if (hexIds is not null) builder.ShowTextHex(hexIds);
                else builder.ShowText(piece);
                builder.EndText();

                // Advance the cursor by the piece's measured width in ITS face.
                double w;
                try
                {
                    if (segData is not null)
                        w = FontInfo.FromFontData(segData).MeasureString(piece, segFs);
                    else if (segFont is not null)
                        w = segFont.MeasureString(piece, segFs);
                    else
                        w = piece.Length * segFs * 0.5;
                }
                catch { w = piece.Length * segFs * 0.5; }
                curX += w;
            }
        }
    }

    // ── Embedded TrueType font registration ────────────────────────

    /// <summary>
    /// Register an embedded TrueType font in the page resources.
    /// Creates font dict, font descriptor with FontFile2, and glyph widths.
    /// Returns the resource name.
    /// </summary>
    private string EnsureEmbeddedTrueTypeFont(FontData fontData)
    {
        // Walks /Parent for inherited /Resources and clones into the page's own
        // dict so the new font lives locally — see GetOrCreateOwnResources.
        var fontDict = GetOrCreateOwnFontDict();

        // Generate a subset tag (6 uppercase letters + '+'). Spaces drop out of the
        // /BaseFont name ("Times New Roman Bold Italic" → TimesNewRomanBoldItalic).
        var tag = GenerateSubsetTag();
        var baseFontName = $"{tag}+{fontData.FontName?.Replace(" ", string.Empty)}";

        // Check if already registered
        foreach (var key in fontDict.Keys)
        {
            var entry = fontDict.Get(key) as PdfDictionary;
            if (entry is not null && entry.GetName("BaseFont") == baseFontName)
                return key;
        }

        // Find unique resource name
        var name = "F1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"F{++counter}";

        // Read TTF metrics
        var ttfData = fontData.TtfData!;
        var (ascent, descent, flags, widths) = FontRepository.ReadTtfMetrics(ttfData);

        // Build /FontDescriptor
        var descriptorDict = new PdfDictionary();
        descriptorDict.Set("Type", new PdfName("FontDescriptor"));
        descriptorDict.Set("FontName", new PdfName(baseFontName));
        descriptorDict.Set("Flags", new PdfInteger(flags | 32)); // Nonsymbolic
        descriptorDict.Set("Ascent", new PdfInteger(ascent));
        descriptorDict.Set("Descent", new PdfInteger(descent));
        descriptorDict.Set("ItalicAngle", new PdfInteger(0));
        descriptorDict.Set("CapHeight", new PdfInteger((int)(ascent * 0.8)));
        descriptorDict.Set("StemV", new PdfInteger(80));
        var bboxArr = new PdfArray();
        bboxArr.Add(new PdfInteger(0));
        bboxArr.Add(new PdfInteger(descent));
        bboxArr.Add(new PdfInteger(1000));
        bboxArr.Add(new PdfInteger(ascent));
        descriptorDict.Set("FontBBox", bboxArr);

        // Embed raw TTF as FontFile2
        var fontFileStream = new PdfStream(new PdfDictionary(), ttfData);
        fontFileStream.Dict.Set("Length1", new PdfInteger(ttfData.Length));
        descriptorDict.Set("FontFile2", fontFileStream);

        // Build /Widths array (WinAnsi: chars 32-255)
        var widthsArray = new PdfArray();
        for (int i = 32; i < 256; i++)
            widthsArray.Add(new PdfInteger(widths[i]));

        // Build the TrueType font dictionary
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("TrueType"));
        font.Set("BaseFont", new PdfName(baseFontName));
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        font.Set("FirstChar", new PdfInteger(32));
        font.Set("LastChar", new PdfInteger(255));
        font.Set("Widths", widthsArray);
        font.Set("FontDescriptor", descriptorDict);

        fontDict.Set(name, font);
        return name;
    }

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

    private static string GenerateSubsetTag()
    {
        var random = new Random();
        var chars = new char[6];
        for (int i = 0; i < 6; i++)
            chars[i] = (char)('A' + random.Next(26));
        return new string(chars);
    }

    // ── Font resource registration ──────────────────────────────────

    /// <summary>
    /// Ensure the Standard 14 font is registered in the page's /Resources /Font
    /// dictionary. Returns the resource name (e.g. "F1").
    /// </summary>
    /// <summary>
    /// Find or create a font resource on a page by base font name.
    /// Used by Page.FlushAttachedFragments to regenerate content streams.
    /// </summary>
    internal static string FindOrCreateFontResource(Page page, string baseFontName)
    {
        var builder = new TextBuilder(page);
        var mapped = MapToStandard14(baseFontName);
        return builder.EnsureFontResource(mapped);
    }

    /// <summary>
    /// Resolve the page's own /Resources dict, creating one (with the inherited
    /// resources shallow-cloned in) if the page doesn't already have its own.
    /// PDF 32000 §7.7.3.4 makes /Resources an inheritable page attribute, so
    /// many real PDFs ship a page with no own /Resources and the
    /// fonts living on the parent /Pages dict. Blindly replacing those with a
    /// new empty dict for our font registration dropped every inherited font,
    /// which made Document.Save+render lose the original page content (only
    /// the appended fragment rendered).
    /// </summary>
    private PdfDictionary GetOrCreateOwnResources()
    {
        var resources = _page.Reader.ResolveDict(_page.Dict.Get("Resources"));
        if (resources is not null) return resources;

        // Walk the /Parent chain for an inherited /Resources to shallow-clone.
        var parent = _page.Reader.ResolveDict(_page.Dict.Get("Parent"));
        for (var depth = 0; parent is not null && depth < 32; depth++)
        {
            var inherited = _page.Reader.ResolveDict(parent.Get("Resources"));
            if (inherited is not null)
            {
                resources = new PdfDictionary();
                foreach (var k in inherited.Keys)
                    resources.Set(k, inherited.Get(k)!);
                _page.Dict.Set("Resources", resources);
                return resources;
            }
            parent = _page.Reader.ResolveDict(parent.Get("Parent"));
        }

        resources = new PdfDictionary();
        _page.Dict.Set("Resources", resources);
        return resources;
    }

    /// <summary>
    /// Resolve the page's /Resources /Font dict, creating or shallow-cloning so
    /// the entry is locally mutable. Sibling of <see cref="GetOrCreateOwnResources"/>
    /// — the parent's Font dict needs cloning before we drop a new font entry
    /// into it; without the clone the new font would be added to the inherited
    /// dict and leak across every other page that shares it.
    /// </summary>
    private PdfDictionary GetOrCreateOwnFontDict()
    {
        var resources = GetOrCreateOwnResources();
        var fontDict = _page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
            return fontDict;
        }
        // If the Font dict came from inherited Resources, clone before mutating.
        // We detect "came from inherited" via the simple heuristic that the dict
        // doesn't appear under our own page's /Resources entry yet (it does after
        // GetOrCreateOwnResources cloned the top-level Resources but the Font
        // entry still references the parent's dict). Always re-Set after clone.
        if (!ReferenceEquals(_page.Reader.Resolve(resources.Get("Font")), fontDict)
            || IsSharedWithParent(resources, fontDict))
        {
            var cloned = new PdfDictionary();
            foreach (var k in fontDict.Keys) cloned.Set(k, fontDict.Get(k)!);
            resources.Set("Font", cloned);
            return cloned;
        }
        return fontDict;
    }

    /// <summary>
    /// Returns true when this resources/font pair was carried over from the
    /// inherited /Pages /Resources (i.e. the Font dict has been seen on the
    /// parent rather than freshly created locally). In practice we only know
    /// "freshly created" when the entry is missing; everything else is treated
    /// as inherited and gets cloned.
    /// </summary>
    private bool IsSharedWithParent(PdfDictionary resources, PdfDictionary fontDict)
    {
        var parent = _page.Reader.ResolveDict(_page.Dict.Get("Parent"));
        for (var depth = 0; parent is not null && depth < 32; depth++)
        {
            var pres = _page.Reader.ResolveDict(parent.Get("Resources"));
            if (pres is not null && ReferenceEquals(_page.Reader.ResolveDict(pres.Get("Font")), fontDict))
                return true;
            parent = _page.Reader.ResolveDict(parent.Get("Parent"));
        }
        return false;
    }

    private string EnsureFontResource(string baseFontName, bool withDescriptor = false,
        double[]? widths = null)
    {
        var fontDict = GetOrCreateOwnFontDict();

        // Check if this base font is already registered (a descriptor-carrying
        // request must not reuse a descriptor-less entry, and vice versa — the
        // absorber's rect geometry differs between the two).
        foreach (var key in fontDict.Keys)
        {
            var entry = fontDict.Get(key) as PdfDictionary;
            if (entry is null)
            {
                // May be an indirect ref — try resolving
                var raw = fontDict.Get(key);
                entry = _page.Reader.ResolveDict(raw);
            }

            if (entry is not null)
            {
                var existing = entry.GetName("BaseFont");
                if (string.Equals(existing, baseFontName, StringComparison.Ordinal)
                    && (entry.Get("FontDescriptor") is not null) == withDescriptor
                    && (entry.Get("Widths") is not null) == (widths is not null))
                    return key;
            }
        }

        // Find a unique resource name
        var name = "F1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"F{++counter}";

        // Create the font dictionary
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFontName));
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        if (widths is not null)
        {
            // The face's own advances, carried on the dict so a reader measures the
            // run the way it was laid out rather than through the core table.
            var arr = new PdfArray();
            for (var c = 32; c <= 255; c++) arr.Add(new PdfReal(widths[c]));
            font.Set("FirstChar", new PdfInteger(32));
            font.Set("LastChar", new PdfInteger(255));
            font.Set("Widths", arr);
        }
        if (withDescriptor)
        {
            // Line-box metrics (1.1-em box on the AFM descent) — the values
            // reported for Standard-14 overlay text. The descriptor
            // makes the absorber's rect carry real ascent/descent for THESE runs
            // only; ordinary descriptor-less Standard-14 output is unaffected.
            var desc = new PdfDictionary();
            desc.Set("Type", new PdfName("FontDescriptor"));
            desc.Set("FontName", new PdfName(baseFontName));
            desc.Set("Flags", new PdfInteger(32));
            var faceDescent = Standard14Fonts.GetWrittenFaceDescent(baseFontName);
            desc.Set("Ascent", new PdfInteger(1100 + faceDescent));
            desc.Set("Descent", new PdfInteger(faceDescent));
            var cap = Standard14Fonts.GetCapHeight(baseFontName);
            if (cap > 0) desc.Set("CapHeight", new PdfInteger(cap));
            desc.Set("StemV", new PdfInteger(80));
            font.Set("FontDescriptor", desc);
        }
        fontDict.Set(name, font);

        return name;
    }
}
