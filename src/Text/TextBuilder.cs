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
        foreach (var f in textFragments) AppendText(f);
    }

    public void AppendText(TextFragment textFragment)
    {
        var fragment = textFragment;
        var text = fragment.Text;

        // If FontData was set via implicit FontData→FontInfo conversion on TextState.Font,
        // propagate it to FontData so the font gets embedded properly.
        if (fragment.TextState.FontData is null && fragment.TextState.Font?.SourceFontData is { } srcFd)
            fragment.TextState.FontData = srcFd;

        // Route every embedded-TTF fragment through the CIDFont (Type0 /
        // Identity-H) path so glyph advances align with the Aspose.PDF for .NET metrics.
        // The earlier attempt at this regressed two distinct-position tests
        // because the multi-segment branch below emitted ShowText(literal)
        // against the Identity-H font, producing nonsense glyph IDs from each
        // pair of ASCII bytes. The branch now encodes each segment's text as
        // 2-byte glyph IDs via the same parser the fragment-level path uses,
        // so the CID route is safe for both single- and multi-segment
        // fragments. TextAbsorber.DecodeWithToUnicode round-trips the text
        // for extraction via the emitted /ToUnicode CMap.
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
            (fontResName, hexGlyphIds) = EnsureEmbeddedCIDFont(fontData, text);
        }
        else if (fragment.TextState.FontData is { TtfData: not null } fontData2)
        {
            fontResName = EnsureEmbeddedTrueTypeFont(fontData2);
        }
        else
        {
            var baseFontName = MapToStandard14(fragment.TextState);
            fontResName = EnsureFontResource(baseFontName);
        }

        var fontSize = fragment.TextState.FontSize;
        var x = fragment.Position?.XIndent ?? 0;
        var y = fragment.Position?.YIndent ?? 0;

        // Compute descent compensation for embedded fonts.
        // The absorber's Position.Y = Td.Y + descent*fs/1000 (descent is negative),
        // so we must write Td.Y = user_y - descent*fs/1000 to round-trip correctly.
        double descentComp = ComputeDescentCompensation(fragment.TextState, fontSize);

        var builder = new ContentStreamBuilder();
        builder.SaveState();

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

        if (hasDistinctPositions)
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
                    ? ComputeDescentCompensation(fragment.TextState, segFs)
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
        }
        else
        {
            var fg = fragment.TextState.ForegroundColor;
            if (fg is not null)
            {
                var fgGs = TextParagraph.EnsureFillAlphaExtGState(_page, fg.AByte);
                if (fgGs is not null) builder.SetExtGState(fgGs);
                builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
            }

            var sc = fragment.TextState.StrokingColor;
            if (sc is not null)
                builder.SetStrokeColor(sc.R / 255.0, sc.G / 255.0, sc.B / 255.0);

            builder.BeginText();
            builder.SetFont(fontResName, fontSize);
            if (fragment.TextState.RenderingMode != Aspose.Pdf.Text.TextRenderingMode.FillText)
                builder.SetTextRenderingMode((int)fragment.TextState.RenderingMode);

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

        _page.AddContentStream(builder.Build());
        fragment.LastWrittenText = text;
        _page.RegisterAttachedFragment(fragment);

        // Underline/strikeout are drawn as thin rectangles at save time. The
        // TextState flags are typically set before the fragment is attached to a
        // page, so the property setters' own registration (which needs a SourcePage)
        // is skipped — register here now that the fragment lives on this page. The
        // flag may sit on the fragment's TextState or on any of its segments.
        bool underline = fragment.TextState.Underline;
        bool strikeOut = fragment.TextState.StrikeOut;
        if (fragment.Segments is { } segs)
        {
            foreach (var seg in segs)
            {
                if (seg.TextState.Underline) underline = true;
                if (seg.TextState.StrikeOut) strikeOut = true;
            }
        }
        if (underline) _page.RegisterUnderlineFragment(fragment);
        if (strikeOut) _page.RegisterStrikeOutFragment(fragment);
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
            var bytes = new byte[lines[i].Length * 2];
            for (var k = 0; k < lines[i].Length; k++)
            {
                int gid = 0;
                if (glyphParser.CMap.TryGetValue(lines[i][k], out var mapped)) gid = mapped;
                bytes[k * 2] = (byte)(gid >> 8);
                bytes[k * 2 + 1] = (byte)(gid & 0xFF);
            }
            builder.ShowTextHex(bytes);
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
        // Aspose.PDF for .NET: segment-level overrides fragment-level.
        var fragBg = fragment.TextState.BackgroundColor;
        double curX = defaultX;
        double curY = defaultY;

        foreach (var seg in fragment.Segments)
        {
            var bg = seg.TextState.BackgroundColor ?? fragBg;
            if (bg is not null)
            {
                var segX = seg.Position?.XIndent ?? curX;
                var segY = seg.Position?.YIndent ?? curY;
                var segFs = seg.TextState.FontSize > 0 ? seg.TextState.FontSize : fontSize;

                // Estimate segment width from char count × avg glyph width.
                // Use Standard14Fonts metrics when available for better accuracy.
                double segW;
                var fontName = seg.TextState.FontName ?? fragment.TextState.FontName;
                if (!string.IsNullOrEmpty(fontName) && Standard14Fonts.IsStandard14(fontName!))
                {
                    segW = 0;
                    foreach (var ch in seg.Text)
                    {
                        var cw = Standard14Fonts.GetWidth(fontName!, ch < 256 ? ch : '?');
                        segW += (cw >= 0 ? cw : 500) * segFs / 1000.0;
                    }
                }
                else
                {
                    segW = seg.Text.Length * segFs * 0.5; // proportional fallback
                }

                // Rectangle Y = baseline − descent (bottom of text box).
                // Height = fontSize (em square) — approximate but sufficient.
                var descent = Standard14Fonts.GetDescent(fontName ?? "Helvetica");
                var descentPt = descent * segFs / 1000.0; // negative
                var rectH = segFs;

                // Rotate the background box with the text. A non-zero rotation maps
                // the box through a cm about the text origin so the highlight stays
                // aligned to the rotated baseline (matching the glyphs drawn via the
                // rotated text matrix); rotation 0 reduces to the axis-aligned box.
                var rotation = seg.TextState.Rotation != 0 ? seg.TextState.Rotation : fragment.TextState.Rotation;
                builder.SaveState();
                builder.SetFillColor(bg.R / 255.0, bg.G / 255.0, bg.B / 255.0);
                if (Math.Abs(rotation % 360.0) > 1e-9)
                {
                    var rad = rotation * Math.PI / 180.0;
                    double cos = Math.Cos(rad), sin = Math.Sin(rad);
                    builder.SetMatrix(cos, sin, -sin, cos, segX, segY);
                    builder.Rectangle(0, descentPt, segW, rectH);
                }
                else
                {
                    builder.Rectangle(segX, segY + descentPt, segW, rectH);
                }
                builder.Fill();
                builder.RestoreState();
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
        // So no compensation needed.
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

        // Generate a subset tag (6 uppercase letters + '+')
        var tag = GenerateSubsetTag();
        var baseFontName = $"{tag}+{fontData.FontName}";

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

        var tag = GenerateSubsetTag();
        var baseFontName = $"{tag}+{fontData.FontName}";

        var name = "F1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"F{++counter}";

        var ttfData = fontData.TtfData!;
        var glyphParser = new GlyphOutlineParser(ttfData);
        var (ascent, descent, flags, _) = FontRepository.ReadTtfMetrics(ttfData);

        // Map text characters to glyph IDs and build the hex string
        var usedGlyphs = new Dictionary<int, int>(); // charCode → glyphId
        var hexBytes = new List<byte>();
        foreach (var ch in text)
        {
            int gid = 0;
            if (glyphParser.CMap.TryGetValue(ch, out var mapped))
                gid = mapped;
            usedGlyphs.TryAdd(ch, gid);
            // CIDFont uses 2-byte glyph IDs
            hexBytes.Add((byte)(gid >> 8));
            hexBytes.Add((byte)(gid & 0xFF));
        }

        // Build /W (per-glyph advance widths) from the TTF's hmtx. Without this,
        // every emitted glyph rendered at the default 500/1000 em advance, so
        // non-ASCII text (e.g. Hindi / Japanese / Korean / Tamil bands)
        // drifted progressively to the right of where Aspose.PDF for .NET
        // placed it. Widths are in CIDFont units (1000/em); scale the raw TTF
        // advance from the font's own unitsPerEm. Format: [startCID [w0 w1 ...]]
        // -- we emit one [gid [width]] entry per used glyph for simplicity; the
        // viewer is required to support that form.
        var wArray = new PdfArray();
        var upm = glyphParser.UnitsPerEm > 0 ? glyphParser.UnitsPerEm : 1000;
        foreach (var (_, gid) in usedGlyphs)
        {
            var rawAdvance = glyphParser.GetAdvanceWidth(gid);
            var pdfWidth = (int)Math.Round(rawAdvance * 1000.0 / upm);
            var widthArr = new PdfArray();
            widthArr.Add(new PdfInteger(pdfWidth));
            wArray.Add(new PdfInteger(gid));
            wArray.Add(widthArr);
        }

        // Build ToUnicode CMap
        var toUnicode = BuildToUnicodeCMap(usedGlyphs);

        // FontDescriptor
        var descriptorDict = new PdfDictionary();
        descriptorDict.Set("Type", new PdfName("FontDescriptor"));
        descriptorDict.Set("FontName", new PdfName(baseFontName));
        descriptorDict.Set("Flags", new PdfInteger(flags | 4)); // Symbolic
        descriptorDict.Set("Ascent", new PdfInteger(ascent));
        descriptorDict.Set("Descent", new PdfInteger(descent));
        descriptorDict.Set("ItalicAngle", new PdfInteger(0));
        descriptorDict.Set("CapHeight", new PdfInteger((int)(ascent * 0.8)));
        descriptorDict.Set("StemV", new PdfInteger(80));
        var bboxArr = new PdfArray();
        bboxArr.Add(new PdfInteger(0)); bboxArr.Add(new PdfInteger(descent));
        bboxArr.Add(new PdfInteger(1000)); bboxArr.Add(new PdfInteger(ascent));
        descriptorDict.Set("FontBBox", bboxArr);

        var fontFileStream = new PdfStream(new PdfDictionary(), ttfData);
        fontFileStream.Dict.Set("Length1", new PdfInteger(ttfData.Length));
        descriptorDict.Set("FontFile2", fontFileStream);

        // CIDFont dictionary (DescendantFont)
        var cidFont = new PdfDictionary();
        cidFont.Set("Type", new PdfName("Font"));
        cidFont.Set("Subtype", new PdfName("CIDFontType2"));
        cidFont.Set("BaseFont", new PdfName(baseFontName));
        var cidSystemInfo = new PdfDictionary();
        cidSystemInfo.Set("Registry", new PdfString(System.Text.Encoding.ASCII.GetBytes("Adobe")));
        cidSystemInfo.Set("Ordering", new PdfString(System.Text.Encoding.ASCII.GetBytes("Identity")));
        cidSystemInfo.Set("Supplement", new PdfInteger(0));
        cidFont.Set("CIDSystemInfo", cidSystemInfo);
        cidFont.Set("FontDescriptor", descriptorDict);
        cidFont.Set("DW", new PdfInteger(500));
        if (wArray.Count > 0)
            cidFont.Set("W", wArray);
        // CIDToGIDMap: Identity mapping
        cidFont.Set("CIDToGIDMap", new PdfName("Identity"));

        // Type0 (composite) font
        var type0Font = new PdfDictionary();
        type0Font.Set("Type", new PdfName("Font"));
        type0Font.Set("Subtype", new PdfName("Type0"));
        type0Font.Set("BaseFont", new PdfName(baseFontName));
        type0Font.Set("Encoding", new PdfName("Identity-H"));
        var descendantFonts = new PdfArray();
        descendantFonts.Add(cidFont);
        type0Font.Set("DescendantFonts", descendantFonts);
        type0Font.Set("ToUnicode", new PdfStream(new PdfDictionary(),
            System.Text.Encoding.ASCII.GetBytes(toUnicode)));

        fontDict.Set(name, type0Font);
        return (name, hexBytes.ToArray());
    }

    /// <summary>Build a ToUnicode CMap for character→glyph mappings.</summary>
    private static string BuildToUnicodeCMap(Dictionary<int, int> usedGlyphs)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("/CIDInit /ProcSet findresource begin");
        sb.AppendLine("12 dict begin");
        sb.AppendLine("begincmap");
        sb.AppendLine("/CIDSystemInfo");
        sb.AppendLine("<< /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def");
        sb.AppendLine("/CMapName /Adobe-Identity-UCS def");
        sb.AppendLine("/CMapType 2 def");
        sb.AppendLine("1 begincodespacerange");
        sb.AppendLine("<0000> <FFFF>");
        sb.AppendLine("endcodespacerange");
        sb.AppendLine($"{usedGlyphs.Count} beginbfchar");
        foreach (var (charCode, gid) in usedGlyphs)
        {
            sb.AppendLine($"<{gid:X4}> <{charCode:X4}>");
        }
        sb.AppendLine("endbfchar");
        sb.AppendLine("endcmap");
        sb.AppendLine("CMapName currentdict /CMap defineresource pop");
        sb.AppendLine("end");
        sb.AppendLine("end");
        return sb.ToString();
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
    /// many real PDFs (33772, 31527) ship a page with no own /Resources and the
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

    private string EnsureFontResource(string baseFontName)
    {
        var fontDict = GetOrCreateOwnFontDict();

        // Check if this base font is already registered
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
                if (string.Equals(existing, baseFontName, StringComparison.Ordinal))
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
        fontDict.Set(name, font);

        return name;
    }
}
