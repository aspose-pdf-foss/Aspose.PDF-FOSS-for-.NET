using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>Install the glyph shapes collected by a Tr 4-7 text object as the current
    /// clip (PDF 32000 §9.3.6). Intersects with whatever clip was already in force, so a
    /// text clip nested inside a path clip keeps tightening rather than replacing it.
    /// A text object that showed no glyphs still clips - to nothing, which is what the
    /// spec asks for and what keeps a later fill from covering the page.</summary>
    private static void EndTextClip(RenderContext ctx, GraphicsState state)
    {
        var accum = ctx.TextClipAccum;
        if (accum is null) return;
        ctx.TextClipAccum = null;
        ctx.TextClipPaints = false;
        if (state.ClipMask is { } outer)
            for (var i = 0; i < accum.Length; i++)
                accum[i] = (byte)(accum[i] & outer[i]);
        state.ClipMask = accum;
        ctx.ClipMask = accum;
    }

    private static void DrawText(RenderContext ctx, string text, byte[] rawBytes, GraphicsState state)
    {
        var dt = new TextDrawState();
        dt.ctx = ctx;
        dt.text = text;
        dt.rawBytes = rawBytes;
        dt.state = state;
        if (dt.state.RenderingMode == 3) return; // invisible
        // Tr 4-7 (PDF 32000 §9.3.6) add the glyph shapes to the CLIPPING path; 4/5/6 paint
        // as well, 7 paints nothing and clips only. Opening the accumulator here routes the
        // glyph coverage into it (see BlitAlphaMask); EndTextClip installs it at ET.
        // Ignoring these modes is not a cosmetic miss: the usual idiom is "clip to the text,
        // then Do a full-page image", so a missing clip lets that image flood the page and
        // erase everything already drawn.
        if (dt.state.RenderingMode >= 4)
        {
            dt.ctx.TextClipAccum ??= new byte[dt.ctx.PixelW * dt.ctx.PixelH];
            dt.ctx.TextClipPaints = dt.state.RenderingMode != 7;
        }
        if (PageRenderFlags.SuppressText) return; // HTML PNG-background: graphics only
        // An empty decoded string with non-empty rawBytes can still happen for CID fonts whose
        // ToUnicode CMap is missing — we must still walk rawBytes to advance the cursor.
        if (string.IsNullOrEmpty(dt.text) && (dt.rawBytes is null || dt.rawBytes.Length == 0)) return;

        // Inherit the active blend mode for glyph blits.
        dt.ctx.CurrentBlendMode = dt.state.BlendMode;
        dt.ctx.SoftMaskAlpha = dt.state.SoftMask is { } sm__ ? ResolveSoftMaskAlpha(dt.ctx, sm__) : null;

        // Type 3 fonts (PDF 32000 §9.6.5) define each glyph as its own PDF content
        // stream stored under /CharProcs. They show up most often in old dot-matrix
        // report/invoice PDFs, typically as Type 3 fonts with inline-ImageMask glyphs
        // for the addresses, invoice numbers, table rows. Detect and route before
        // the CID/simple-font dispatch — Type 3 has no embedded TTF/Type 1 outlines
        // for our glyph rasteriser to consume.
        if (TryDrawType3Text(dt)) return;

        dt.tm = dt.state.TextMatrix;
        dt.ctm = dt.state.Ctm;
        dt.trm = GraphicsState.MultiplyMatrices(dt.tm, dt.ctm);

        dt.fontSize = Math.Abs(dt.state.FontSize);
        dt.effectiveSize = dt.fontSize * Math.Sqrt(dt.trm[1] * dt.trm[1] + dt.trm[3] * dt.trm[3]);
        if (dt.effectiveSize < 0.5) return;

        ComputeTextPen(dt);
        dt.thSign = dt.state.HorizontalScaling < 0 ? -1.0 : 1.0;
        dt.fsSign = dt.state.FontSize < 0 ? -1.0 : 1.0;
        SetGlyphEmMatrix(dt);

        ResolveTextFont(dt);
        // A Type0 font with a 1-byte custom CMap (codespace <00> <FF>) still shows
        // CIDs, not byte-encoded characters. The simple-font path resolves glyphs
        // through the program's cmap, which a CID-keyed program (bare CFF, or a
        // subset whose codes are CIDs — a one-byte identity CMap) does not have —
        // so a font whose CMap declares a FIXED single-byte codespace routes through
        // the CID path, which steps 1 byte per code and resolves CID→GID directly.
        // A mixed-width CMap (a UTF-8 one declares 1- to 4-byte ranges) cannot be
        // walked at a constant step and keeps the simple routing, as does a
        // CMap-less 1-byte Type0 with a TrueType descendant.
        dt.ctx.TextStroke = StrokesText(dt.state.RenderingMode) ? dt.state : null;
        dt.ctx.TextFills = !StrokesTextOnly(dt.state.RenderingMode);
        try
        {
            DrawTextRuns(dt);
        }
        finally
        {
            dt.ctx.TextStroke = null;
            dt.ctx.TextFills = true;
        }
    }

    /// <summary>Text rendering modes that stroke the glyph outlines (PDF 32000 §9.3.6).</summary>
    private static bool StrokesText(int mode) => mode is 1 or 2 or 5 or 6;

    /// <summary>Text rendering modes that stroke the glyph outlines without filling them.</summary>
    private static bool StrokesTextOnly(int mode) => mode is 1 or 5;

    /// <summary>
    /// Build (and cache) a 256-entry byte→GID map for a simple font whose PDF /Encoding
    /// names glyphs the embedded font exposes by name (a /Differences array of custom
    /// names such as /G42). Returns null when the font has no /Differences or the parser
    /// can't resolve any of the names — callers fall back to the Unicode CMap path.
    /// </summary>
    private static int[]? GetEncodingGidMap(RenderContext ctx, string? fontName, IGlyphOutlineSource? parser)
    {
        // Keyed by the FONT DICT, never the resource name — a form XObject's /T1_0
        // is routinely a different font than the page's /T1_0 (see the GDI+ twin).
        if (fontName is null || parser is null || ctx.FontDicts is null
            || !ctx.FontDicts.TryGetValue(fontName, out var fd)) return null;
        if (ctx.EncodingGidMaps.TryGetValue(fd, out var cached)) return cached;
        var map = BuildEncodingGidMap(ctx.FontDicts, ctx.Reader, fontName, parser);
        ctx.EncodingGidMaps[fd] = map;
        return map;
    }

    /// <summary>How many codes a simple font's encoding covers.</summary>
    private const int SimpleFontCodeCount = 256;

    /// <summary>The codes this run's font blanks (see <see cref="BuildUndefinedCodes"/>),
    /// cached per font dict like the byte→GID map.</summary>
    private static bool[]? GetUndefinedCodes(RenderContext ctx, string? fontName)
    {
        if (fontName is null || ctx.FontDicts is null || !ctx.FontDicts.TryGetValue(fontName, out var fd)) return null;
        if (ctx.UndefinedCodeMaps.TryGetValue(fd, out var cached)) return cached;
        var map = BuildUndefinedCodes(fd, ctx.Reader);
        ctx.UndefinedCodeMaps[fd] = map;
        return map;
    }

    /// <summary>Shared by both renderers: the codes an explicit /Differences array names
    /// /.notdef. Such a code shows nothing at all (PDF 32000 §9.6.6.1), whatever a
    /// substituted face would make of the byte. Only a code the array ITSELF names counts:
    /// a code the array leaves alone keeps whatever the base encoding gives it, and an
    /// encoding with no /BaseEncoding resolves those to .notdef in the name table while the
    /// font still shows its own glyph there - a /Differences that renames a dozen accents
    /// must not blank every letter it never mentioned. Null when nothing is blanked.</summary>
    internal static bool[]? BuildUndefinedCodes(PdfDictionary fontDict, IO.PdfReader reader)
    {
        if (reader.ResolveDict(fontDict.Get("Encoding")) is not { } enc || enc.Get("Differences") is not PdfArray diffs)
            return null;
        bool[]? map = null;
        var code = 0;
        foreach (var item in diffs)
        {
            if (item is PdfInteger start) { code = (int)start.Value; continue; }
            if (item is not PdfName name) continue;
            if (name.Value == ".notdef" && code >= 0 && code < SimpleFontCodeCount)
            {
                map ??= new bool[SimpleFontCodeCount];
                map[code] = true;
            }
            code++;
        }

        return map;
    }

    /// <summary>Shared by both renderers: build the 256-entry byte→GID map for a simple
    /// font. With an /Encoding /Differences array, glyph names resolve through the
    /// embedded font's name table. With NO /Encoding entry at all, the embedded
    /// program's own encoding is the base encoding (PDF 32000 §9.6.6.1) — a
    /// custom-encoded Type 1 / bare-CFF program shows arbitrary glyphs per byte and
    /// the Unicode-cmap fallback would draw the wrong glyphs. Returns null when
    /// neither source yields a map — callers fall back to the Unicode CMap path.</summary>
    internal static int[]? BuildEncodingGidMap(Dictionary<string, PdfDictionary>? fontDicts,
        IO.PdfReader reader, string? fontName, IGlyphOutlineSource? parser)
    {
        if (fontName is null || parser is null || fontDicts is null
            || !fontDicts.TryGetValue(fontName, out var fdict))
            return null;

        // A TrueType program whose only character table is the symbol one is addressed by
        // its codes (PDF 32000 §9.6.6.4): a name read as a character would index that table
        // by a code it was never keyed by - in a subset numbered from 1, "zero" at code 17
        // found the glyph of code 48. A code the table carries takes the table's glyph; the
        // name fills only the codes it lacks. That holds for a font flagged symbolic. A font
        // flagged nonsymbolic over such a table is read by NAME: the encoding's name for the
        // code (its /Differences over the base encoding, the standard names when it has no
        // /Encoding at all) finds the glyph of that name in the program's post table, and
        // only a program without names falls back to the name's character in the table; the
        // table's entry at the code itself comes last. A table whose order disagrees with
        // the names otherwise drew the wrong letters for a font whose encoding renames its
        // codes, and for one that leaves them to the standard names.
        var trueType = parser as Text.GlyphOutlineParser;
        var symbolTable = trueType is { HasSymbolTableOnly: true } ? parser.CMap : null;
        var symbolicOnly = symbolTable is not null && IsSymbolicOnly(fdict, reader);
        var byName = symbolTable is not null && !symbolicOnly;
        var noEncoding = reader.Resolve(fdict.Get("Encoding")) is null;
        if (noEncoding && !byName)
            return parser switch
            {
                Text.Type1GlyphSource t1 => t1.EncodingByteToGid,
                Text.CffGlyphSource cff => cff.EncodingByteToGid,
                _ => null,
            };

        // A name-form /Encoding (/WinAnsiEncoding …) — or a dict carrying only a
        // /BaseEncoding — still names every code's glyph, and for a NAME-KEYED
        // program (CFF /FontFile3, Adobe Type 1) those names are authoritative: a
        // subset's renumbered charset makes the program's own byte cmap arbitrary
        // (a header drew garbled glyphs for its text). TrueType programs
        // keep the historical /Differences-only gate — their name table is a post
        // table that subsetters routinely strip or leave stale.
        var hasDifferences = reader.ResolveDict(fdict.Get("Encoding")) is { } encDict
            && encDict.Get("Differences") is PdfArray;
        if (!hasDifferences && !byName && parser is not (Text.Type1GlyphSource or Text.CffGlyphSource))
            return null;
        var names = ResolveEncoding(fdict, reader);
        if (noEncoding) ApplyBaseEncoding(names, StandardEncodingName);
        var built = new int[256];
        var any = false;
        for (var code = 0; code < 256; code++)
        {
            if (names[code] is not { } n) continue;
            if (n is "space" or "nbspace" or "uni0020" or "uni00A0")
            {
                // A whitespace NAME never paints. Resolving it through the program can
                // land on an arbitrary visible glyph — subset tools renumber glyphs but
                // keep a stale cmap (an Arabic page drew digit chains where its spaces
                // belong). -1 = explicit blank: consumers skip both the cmap fallback
                // (gid != 0) and the paint (gid < 1); the advance still comes from /Widths.
                built[code] = BlankGlyph;
                any = true;
                continue;
            }
            var tableGid = symbolTable is null ? 0 : SymbolTableGid(symbolTable, code, symbolicOnly);
            var gid = tableGid > 0 && !byName ? tableGid : NamedCodeGid(parser, trueType, symbolTable, byName, n, tableGid);
            if (gid != 0)
            {
                built[code] = gid;
                any = true;
            }
        }
        return any ? built : null;
    }

    /// <summary>The map entry of a code that shows nothing and still advances.</summary>
    private const int BlankGlyph = -1;

    private const string StandardEncodingName = "StandardEncoding";

    /// <summary>The glyph a code's NAME finds: through the post table of a nonsymbolic font read
    /// by name, else through the name's character; the table's entry at the code last. A name
    /// a post table with names lacks is not read as a glyph index: a uniXXXX reaches the table
    /// through its character and shows nothing when the table lacks it, a gNN takes the table's
    /// entry at the code, as a symbolic font would; other names still go by their character.</summary>
    private static int NamedCodeGid(IGlyphOutlineSource parser, Text.GlyphOutlineParser? trueType,
        Dictionary<int, int>? symbolTable, bool byName, string name, int tableGid)
    {
        if (byName && trueType!.HasPostNames)
        {
            var named = trueType.GidForPostName(name);
            if (named > 0) return named;
            if (UniNameCharacter(name) is { } ch)
                return symbolTable!.TryGetValue(Text.GlyphOutlineParser.SymbolCodeBase + ch, out var byChar) && byChar > 0
                    ? byChar
                    : BlankGlyph;
            if (Text.GlyphOutlineParser.TryParseGlyphIndexName(name, out _)) return tableGid;
        }
        var gid = parser.GidForName(name);
        return gid == 0 ? tableGid : gid;
    }

    private const string UniNamePrefix = "uni";
    private const int UniNameHexDigits = 4;

    /// <summary>The character a <c>uniXXXX</c> glyph name stands for, or null for any other name.</summary>
    private static int? UniNameCharacter(string name)
    {
        if (name.Length != UniNamePrefix.Length + UniNameHexDigits || !name.StartsWith(UniNamePrefix, StringComparison.Ordinal))
            return null;
        return int.TryParse(name.Substring(UniNamePrefix.Length), System.Globalization.NumberStyles.AllowHexSpecifier,
            System.Globalization.CultureInfo.InvariantCulture, out var ch) ? ch : null;
    }

    private const int SymbolicFontFlag = 1 << 2;
    private const int NonsymbolicFontFlag = 1 << 5;

    /// <summary>True when the font's descriptor flags it symbolic and not also nonsymbolic.</summary>
    private static bool IsSymbolicOnly(PdfDictionary fontDict, IO.PdfReader reader)
    {
        var flags = reader.ResolveDict(fontDict.Get("FontDescriptor"))?.GetInt("Flags") ?? 0;
        return (flags & SymbolicFontFlag) != 0 && (flags & NonsymbolicFontFlag) == 0;
    }

    /// <summary>The glyph a symbol table gives a code, or 0 when it is not the table's to give.
    /// An entry at U+F000 + code is the code's by the table's own convention. An entry at the
    /// bare code is the code's only in a font flagged symbolic alone: a font flagged both ways
    /// keys such a table by character, and its names are read through it as before.</summary>
    private static int SymbolTableGid(Dictionary<int, int> symbolTable, int code, bool symbolicOnly)
    {
        if (symbolTable.TryGetValue(Text.GlyphOutlineParser.SymbolCodeBase + code, out var gid) && gid > 0) return gid;
        return symbolicOnly && symbolTable.TryGetValue(code, out gid) && gid > 0 ? gid : 0;
    }

    /// <summary>
    /// The Adobe registry /Ordering of a Type0 font's descendant ("GB1", "Japan1", ...),
    /// or null when the font is not Type0, is not Adobe-registered, or is Identity-ordered.
    /// Identity is excluded deliberately - see the call site.
    /// </summary>
    private static string? RegistryOrderingOf(PdfDictionary font, IO.PdfReader reader)
    {
        try
        {
            if (font.GetName("Subtype") != "Type0") return null;
            if (reader.Resolve(font.Get("DescendantFonts")) is not PdfArray da || da.Count == 0) return null;
            var cidFont = reader.ResolveDict(da[0]);
            var csi = cidFont is null ? null : reader.ResolveDict(cidFont.Get("CIDSystemInfo"));
            if (csi is null) return null;
            if ((reader.Resolve(csi.Get("Registry")) as PdfString)?.ToText() != "Adobe") return null;
            var ord = (reader.Resolve(csi.Get("Ordering")) as PdfString)?.ToText();
            return string.IsNullOrEmpty(ord) || ord == "Identity" ? null : ord;
        }
        catch { return null; }
    }

    /// <returns>The pen after the run.</returns>
    private static double DrawCidText(RenderContext ctx, byte[] rawBytes, CidFontInfo cidInfo, IGlyphOutlineSource? parser, FontMetrics? fontMetrics, double px, double py, double effectiveSize, double hScale, double charSpacingPx, double wordSpacingPx, byte r, byte g, byte b, byte a)
    {
        var ct = new CidTextDrawState();
        ct.ctx = ctx;
        ct.rawBytes = rawBytes;
        ct.cidInfo = cidInfo;
        ct.parser = parser;
        ct.fontMetrics = fontMetrics;
        ct.py = py;
        ct.effectiveSize = effectiveSize;
        ct.hScale = hScale;
        ct.charSpacingPx = charSpacingPx;
        ct.wordSpacingPx = wordSpacingPx;
        ct.r = r;
        ct.g = g;
        ct.b = b;
        ct.a = a;
        // Predefined legacy national CMaps (GBK-EUC-H, ETen-B5-H, 90ms-RKSJ-H, …)
        // encode their show-strings in a national multi-byte charset, not as Adobe
        // CIDs. Decode the whole run to Unicode and render through the resolved
        // system font / CJK fallback. Handled separately because the charset is
        // mixed-width (1-byte ASCII + 2-byte CJK), unlike the 2-byte CID path below.
        if (ct.cidInfo.LegacyCodepage != 0)
        {
            return DrawLegacyCjkText(ct.ctx, ct.rawBytes, ct.cidInfo, ct.parser, ct.fontMetrics, px, ct.py, ct.effectiveSize,
                ct.hScale, ct.charSpacingPx, ct.wordSpacingPx, ct.r, ct.g, ct.b, ct.a);
        }

        ct.fallback = null;
        if (ct.parser is null)
        {
            var canFallback = ct.cidInfo.IsUnicodeEncoding
                              || (ct.cidInfo.Ordering is not null && ct.cidInfo.Ordering != "Identity");
            // Resolve a system font by the CID ordering/base name (Korea1 -> Malgun,
            // GB1 -> SimSun, Japan1 -> MS Mincho), not the single generic broad-coverage
            // font: that one covers Han but not Hangul, so non-embedded Korean text
            // (UniKS-UTF16-H) was dropped while Chinese on the same page rendered.
            // ResolveNamed falls back to the generic font itself.
            if (canFallback) ct.fallback = CjkFallbackFont.ResolveNamed(ct.cidInfo.CjkBaseFont, ct.cidInfo.Ordering);
        }

        ct.vertical = ct.cidInfo.IsVertical;
        ct.penY = ct.py;
        ct.step = ct.cidInfo.IsTwoByteEncoding ? 2 : 1;
        ct.px = px;
        for (var i = 0; i + ct.step <= ct.rawBytes.Length; i += ct.step)
        {
            if (!DrawCidGlyph(ct, i)) break;
        }
        return ct.px;
    }

    /// <summary>
    /// Render a show-string from a non-embedded predefined legacy national CMap
    /// (GBK-EUC-H, ETen-B5-H, 90ms-RKSJ-H, KSCms-UHC-H, …). The bytes are decoded
    /// to Unicode through the CMap's national codepage, then each character is drawn
    /// with the resolved system font (e.g. Times for the Latin runs these fonts
    /// carry) or, when that lacks the glyph, a broad-coverage CJK fallback (SimSun).
    /// Advances come from the chosen font's own hmtx — the PDF /W is keyed by Adobe
    /// CIDs we never resolve, and its /DW is commonly 500 (half-width), which would
    /// crush full-width CJK.
    /// </summary>
    /// <returns>The pen after the run.</returns>
    private static double DrawLegacyCjkText(RenderContext ctx, byte[] rawBytes, CidFontInfo cidInfo,
        IGlyphOutlineSource? parser, FontMetrics? fontMetrics, double px, double py,
        double effectiveSize, double hScale,
        double charSpacingPx, double wordSpacingPx, byte r, byte g, byte b, byte a)
    {
        var fallback = CjkFallbackFont.ResolveNamed(cidInfo.CjkBaseFont, cidInfo.Ordering);
        var vert = cidInfo.IsVertical;
        var penY = py; // vertical text advances a local Y down the column
        var i = 0;
        while (i < rawBytes.Length)
        {
            // Walk the mixed-width national charset: a lead byte 0x81-0xFE starts a
            // 2-byte code, everything else is single-byte ASCII.
            var step = cidInfo.LegacyByteLength(rawBytes[i]);
            if (step == 2 && i + 1 >= rawBytes.Length) step = 1;
            var code = step == 2 ? ((rawBytes[i] << 8) | rawBytes[i + 1]) : rawBytes[i];
            i += step;

            var uni = cidInfo.LegacyToUnicode(code) ?? -1;
            IGlyphOutlineSource? src = null;
            var gid = 0;
            if (uni >= 0)
            {
                // Latin runs render in the resolved system font; CJK in the fallback.
                if (parser is not null && parser.CMap.TryGetValue(uni, out var g1) && g1 > 0)
                { src = parser; gid = g1; }
                else if (fallback is not null && fallback.CMap.TryGetValue(uni, out var g2) && g2 > 0)
                { src = fallback; gid = g2; }
            }

            if (src is not null)
            {
                // The pen of a column is the glyph's vertical origin, the top middle of its
                // cell, and the face keeps its own forms for a column's brackets.
                var em = effectiveSize * ctx.Scale / 1000.0;
                var outline = src.GetOutline(vert ? CjkFallbackFont.VerticalGlyph(src, gid) : gid);
                if (outline is not null)
                {
                    BlitGlyph(ctx, outline, src.UnitsPerEm, effectiveSize, hScale,
                        vert ? px - CjkFallbackFont.FullWidthOriginX * em * hScale : px,
                        vert ? penY + cidInfo.VertOriginY * em : py, r, g, b, a);
                }
            }

            // Nominal full-/half-width advance (must match ContentStreamParser's cursor
            // advance for these CMaps). Vertical (-V) text advances one em down per glyph.
            if (vert)
            {
                penY += effectiveSize * ctx.Scale + charSpacingPx;
            }
            else
            {
                var charWidth = CjkFallbackFont.AdvanceEm(cidInfo, fontMetrics, code, step);
                px += charWidth / 1000.0 * effectiveSize * ctx.Scale * hScale + charSpacingPx;
                if (uni == ' ') px += wordSpacingPx;
            }
        }
        return px;
    }

    /// <summary>
    /// Render Type 3 font text (PDF 32000 §9.6.5). Each byte in the show string
    /// indexes into the font's encoding (we honour /BaseEncoding name aliases plus
    /// the /Differences override array) to get a glyph name; that name keys into
    /// /CharProcs to get a small PDF content stream which is executed against the
    /// page renderer with a per-glyph CTM = FontMatrix · TextSizeMatrix · Tm · Ctm.
    /// Each glyph's advance comes from /Widths (1000-units-per-em-style) scaled
    /// through FontMatrix and the active font size. The CharProc starts with d0
    /// or d1 (already-known metrics) which our parser treats as no-ops; the rest
    /// is regular PDF that includes inline ImageMask BI/ID/EI sequences (handled
    /// by the OnInlineImage hook installed by RenderContent).
    /// </summary>
    private static void DrawType3Text(RenderContext ctx, byte[] rawBytes,
        GraphicsState state, PdfDictionary fontDict)
    {
        var fontMatrix = ExtractFontMatrix(fontDict);
        var encoding = ResolveEncoding(fontDict, ctx.Reader);
        var charProcs = ctx.Reader.ResolveDict(fontDict.Get("CharProcs"));
        if (charProcs is null) return;
        var widths = ctx.Reader.Resolve(fontDict.Get("Widths")) as PdfArray;
        var firstChar = (int)fontDict.GetInt("FirstChar");
        var walked = 0.0;

        // FontMatrix maps glyph space → text space. To map glyph space directly to
        // page user space we then multiply by Tfs·Tm·Ctm. The Tfs (font size) and
        // optional Th (horizontal scaling, default 100%) form the text size matrix.
        var hScale = state.HorizontalScaling / 100.0;
        var fontSizeMatrix = new[] { state.FontSize * hScale, 0.0, 0.0, state.FontSize, 0.0, 0.0 };

        // Resources for the CharProc's content stream. PDF 32000 §9.6.5.4 says a
        // Type 3 font may have its own /Resources; if absent, use page resources.
        var fontResources = ctx.Reader.ResolveDict(fontDict.Get("Resources"));
        var glyphExtGStates = ResolveExtGStates(fontResources, ctx.Reader);
        // A char proc's Do names an XObject of the font's own /Resources before the page's.
        var fontXObjects = FontXObjects(ctx, fontResources);
        // Fall back to outer ExtGStates so glyphs that reference /GS0 via the page
        // continue to find them. (Type 3 glyphs rarely use ExtGStates but inline
        // image masks are common — those don't need an ExtGState anyway.)

        foreach (var b in rawBytes)
        {
            var glyphName = encoding[b];
            // Always advance Tm by the glyph width, even if the glyph is .notdef
            // or missing — the column layout of the giant @-tiled report headers
            // depends on consistent advances per byte.
            var widthUnits = 0.0;
            if (widths is not null)
            {
                var idx = b - firstChar;
                if (idx >= 0 && idx < widths.Count) widthUnits = NumFrom(widths[idx]);
            }
            // Advance in text space = widthUnits · FontMatrix.a (FontMatrix maps to
            // text-space units; the .a entry is the horizontal scale).
            var advanceTextSpace = widthUnits * fontMatrix[0];

            if (glyphName is not null && glyphName != ".notdef"
                && charProcs.Get(glyphName) is { } cpObj
                && ctx.Reader.ResolveStream(cpObj) is { } cpStream)
            {
                byte[] cpBytes;
                try { cpBytes = ctx.Reader.DecodeStream(cpStream); }
                catch { cpBytes = System.Array.Empty<byte>(); }
                if (cpBytes.Length > 0)
                {
                    // Compose the per-glyph CTM. Order matters — for our PDF row-
                    // vector × matrix convention, v through M1·M2 means apply M1
                    // first. We want glyph → font → text-size → text → page-user.
                    var tmCtm = GraphicsState.MultiplyMatrices(state.TextMatrix, state.Ctm);
                    var sizeTmCtm = GraphicsState.MultiplyMatrices(fontSizeMatrix, tmCtm);
                    var glyphCtm = GraphicsState.MultiplyMatrices(fontMatrix, sizeTmCtm);
                    // The CharProc may have its own /Resources too; merge with the
                    // font's resources (the page renderer will fall back to ctx's
                    // own font/extgstate dicts for unresolved names).
                    // The glyph paints inside whatever clips the text, in the text's
                    // colour - a char proc does not start from a blank state.
                    var pageXObjects = ctx.AllXObjects;
                    ctx.AllXObjects = fontXObjects ?? pageXObjects;
                    try
                    {
                        RenderContent(cpBytes, ctx, glyphExtGStates, glyphCtm,
                            initialClipMask: state.ClipMask, inheritState: state);
                    }
                    finally { ctx.AllXObjects = pageXObjects; }
                }
            }

            // Walk Tm by the glyph's advance so the NEXT glyph in this run is placed
            // correctly - and remember how far, because this walk is only ours.
            var dx = Type3Advance(advanceTextSpace, b, state);
            state.AdvanceTextPosition(dx, 0);
            walked += dx;
        }

        // Put the cursor back. The content-stream parser owns it: it applies the
        // authoritative advance for every show operator, Type 3 included (the metrics
        // builder already pre-scales those widths by FontMatrix). Leaving our own walk in
        // place made every Type 3 glyph advance TWICE, which spread a paragraph so wide
        // it ran off the page - and only Type 3, because it is the one glyph path that
        // needs the shared matrix to position the next glyph rather than a local pen.
        if (walked != 0) state.AdvanceTextPosition(-walked, 0);
    }

    /// <summary>Shared by both renderers: how far a Type 3 glyph moves the text position, in
    /// unscaled text space - its width (already through the FontMatrix) times Tfs, plus Tc, plus
    /// Tw for the single-byte code 32, all times Th (PDF 32000 §9.4.4), as for any other font.
    /// The width alone packed a spaced caption's glyphs together.</summary>
    /// <summary>The XObjects a Type 3 font's char procs can name: the font's own /Resources /XObject over the
    /// page's; null when the font brings none, so the page's stand alone.</summary>
    private static Dictionary<string, PdfStream>? FontXObjects(RenderContext ctx, PdfDictionary? fontResources)
    {
        if (fontResources is null || ctx.Reader.ResolveDict(fontResources.Get("XObject")) is null) return null;
        var merged = ctx.AllXObjects is { } page ? new Dictionary<string, PdfStream>(page) : new Dictionary<string, PdfStream>();
        foreach (var (name, stream) in ResolveAllXObjects(fontResources, ctx.Reader)) merged[name] = stream;
        return merged;
    }

    internal static double Type3Advance(double widthTextSpace, byte code, GraphicsState state)
        => (widthTextSpace * state.FontSize + state.CharSpacing + (code == SpaceCode ? state.WordSpacing : 0))
           * state.HorizontalScaling / PercentScale;

    private const double PercentScale = 100.0;

    /// <summary>
    /// Extract a font's /FontMatrix. PDF 32000 §9.6.5: default is
    /// [0.001 0 0 0.001 0 0] for Type 3 fonts (glyph coords are in 1000 ths of an em).
    /// </summary>
    internal static double[] ExtractFontMatrix(PdfDictionary fontDict)
    {
        if (fontDict.Get("FontMatrix") is PdfArray arr && arr.Count >= 6)
        {
            var m = new double[6];
            for (var i = 0; i < 6; i++) m[i] = NumFrom(arr[i]);
            return m;
        }
        return new[] { 0.001, 0.0, 0.0, 0.001, 0.0, 0.0 };
    }

    /// <summary>
    /// Build a byte→glyph-name map (256 entries) from a font's /Encoding. PDF 32000
    /// §9.6.6: /Encoding can be a name (StandardEncoding, WinAnsiEncoding,
    /// MacRomanEncoding, MacExpertEncoding) or a dict with optional /BaseEncoding
    /// (one of those names; defaults to StandardEncoding) and /Differences (alternating
    /// integer code start and a sequence of glyph names). For Type 3 fonts in
    /// dot-matrix report PDFs the /Differences array names every byte explicitly,
    /// so even if we don't ship a full StandardEncoding table the result is correct.
    /// </summary>
    internal static string?[] ResolveEncoding(PdfDictionary fontDict, IO.PdfReader reader)
    {
        var result = new string?[256];
        for (var i = 0; i < 256; i++) result[i] = ".notdef";

        var encObj = reader.Resolve(fontDict.Get("Encoding"));
        if (encObj is PdfName encName)
        {
            ApplyBaseEncoding(result, encName.Value);
            return result;
        }
        if (encObj is PdfDictionary encDict)
        {
            var baseName = encDict.GetName("BaseEncoding");
            if (baseName is not null) ApplyBaseEncoding(result, baseName);
            if (encDict.Get("Differences") is PdfArray diffs)
            {
                int code = 0;
                foreach (var item in diffs)
                {
                    if (item is PdfInteger pi) { code = (int)pi.Value; }
                    else if (item is PdfName pn && code >= 0 && code < 256)
                    {
                        result[code] = pn.Value;
                        code++;
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Fill the byte→glyph map for a named base encoding, per PDF 32000 Annex D.
    /// The extended range matters: MacRomanEncoding keeps fi/fl at 0xDE/0xDF and
    /// accented letters in 0x80–0x9F, WinAnsi is CP1252 — leaving 0x80–0xFF as
    /// .notdef drops those glyphs from name-keyed (CFF/Type 1) programs.
    /// </summary>
    private static void ApplyBaseEncoding(string?[] result, string baseEncodingName)
    {
        for (var code = 0; code < 256; code++)
        {
            var name = baseEncodingName switch
            {
                "MacRomanEncoding" => Text.PdfEncodings.MacRomanName(code),
                "WinAnsiEncoding" => Text.PdfEncodings.WinAnsiName(code),
                _ => Text.Type1StandardEncoding.GetName(code),
            };
            if (name is not null) result[code] = name;
        }
    }

    /// <summary>
    /// Host face standing in for a simple font whose own program cannot be resolved: the
    /// /BaseFont's own name (subset tag stripped) if the system has it, else Arial. Cached
    /// per render on the context, under a key that cannot collide with a resource name.
    /// Mirrors the GDI+ rasteriser's simple-font fallback.
    /// </summary>
    private static IGlyphOutlineSource? ResolveSimpleFallback(RenderContext ctx, string? fontName)
    {
        var key = "\0substitute:" + (fontName ?? string.Empty);
        if (ctx.FontParsers.TryGetValue(key, out var cached)) return cached.parser;

        string? baseFont = null;
        if (fontName is not null && ctx.FontDicts is not null
            && ctx.FontDicts.TryGetValue(fontName, out var fd))
            baseFont = (ctx.Reader.Resolve(fd.Get("BaseFont")) as PdfName)?.Value;
        // Strip a subset tag ("ABCDEF+Foo" -> "Foo").
        if (baseFont is { Length: > 7 } && baseFont[6] == '+') baseFont = baseFont.Substring(7);

        IGlyphOutlineSource? parser = null;
        // No host carries the standard dingbats face: the built-in one stands in, as in the
        // GDI+ rasteriser.
        var ttf = baseFont == DingbatsFaceName ? Text.FontRepository.DjVuDingbatsProgram : null;
        ttf ??= Text.SystemFontResolver.Resolve(string.IsNullOrEmpty(baseFont) ? "Arial" : baseFont)
                  ?? Text.SystemFontResolver.Resolve("Arial");
        if (ttf is { Length: > 0 })
        {
            try { parser = new Text.GlyphOutlineParser(ttf); } catch { parser = null; }
        }
        ctx.FontParsers[key] = (parser, 1.0);
        return parser;
    }

    /// <returns>The pen after the run.</returns>
    private static double DrawSimpleText(RenderContext ctx, string text, byte[]? rawBytes,
        IGlyphOutlineSource? parser, FontMetrics? fontMetrics, int[]? encGidMap,
        double px, double py, double effectiveSize, double hScale,
        double charSpacingPx, double wordSpacingPx,
        byte r, byte g, byte b, byte a, bool substituted = false, bool[]? undefinedCodes = null,
        bool embeddedProgram = true)
    {
        // When rawBytes is 1:1 with text (typical for simple TT fonts), each char position
        // also corresponds to a single byte. Subset TT fonts embedded in PDFs commonly
        // carry only a Mac Roman cmap (platform 1 / format 6) keyed by the raw byte values
        // from the content stream, not by /ToUnicode-derived chars. Try the byte-keyed lookup
        // first: for non-subset fonts using WinAnsi/StandardEncoding, byte == Unicode for
        // the ASCII range so this still hits the correct glyph. For subset fonts where
        // /ToUnicode maps byte 0x21 to 'T' but cmap[0x54] (the Unicode of 'T') would return
        // whatever glyph the subset tool chose for byte 0x54 (the wrong one), the byte
        // lookup picks the right glyph. Unicode is the fallback for cases like /Differences-
        // encoded fonts where byte 0x80 maps to a non-ASCII Unicode char (0x0105 etc.).
        // A SUBSTITUTED host face knows nothing of the original program's byte-keyed
        // cmap, so its glyphs are only addressable by Unicode. A host face standing in for a
        // font that embeds no program (see HasEmbeddedProgram) shows the character the
        // font's encoding names, and only a code that names none (a symbol face addressed
        // by its codes) is looked up by its byte.
        bool useBytesFallback = !substituted && rawBytes is not null && rawBytes.Length == text.Length;
        bool bytesFirst = useBytesFallback && embeddedProgram;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            int gid = 0;
            if (parser is not null)
            {
                // An explicit /Encoding /Differences is authoritative for a simple font
                // (PDF 32000 §9.6.6.1) and overrides the embedded program's own byte cmap; without
                // this a code like 0x39 ("t" via Differences) wrongly draws the
                // embedded "nine" glyph. encGidMap is non-null only when
                // /Differences exists; a code with no resolvable name yields 0 → fall back.
                if (encGidMap is not null && rawBytes is not null && i < rawBytes.Length)
                    gid = encGidMap[rawBytes[i]];

                if (gid == 0)
                {
                    if (bytesFirst && parser.CMap.TryGetValue(rawBytes![i], out gid) && gid > 0) { /* byte hit */ }
                    else if (parser.CMap.TryGetValue(ch, out gid) && gid > 0) { /* unicode hit */ }
                    else if (useBytesFallback && parser.CMap.TryGetValue(rawBytes![i], out gid) && gid > 0) { /* code hit in a host face */ }
                    else gid = 0;
                }
            }
            // A code the font's own /Differences names /.notdef shows nothing (it still advances).
            var blank = undefinedCodes is not null && rawBytes is not null && i < rawBytes.Length
                && rawBytes[i] < undefinedCodes.Length && undefinedCodes[rawBytes[i]];
            if (parser is not null && gid > 0 && !blank)
            {
                // parser.CMap (TTF cmap) maps Unicode → GID for embedded simple TrueType fonts.
                var outline = parser.GetOutline(gid);
                if (outline is not null)
                {
                    BlitGlyph(ctx, outline, parser.UnitsPerEm, effectiveSize, hScale,
                        px, py, r, g, b, a);
                }
            }

            // Per-char advance: prefer the PDF font dict's /Widths (FontMetrics
            // keys those by byte code 0-255, plus Standard-14 AFM widths).
            // For subset TT fonts (Mac Roman cmap) the byte values in the content
            // stream ARE the /Widths keys; the /ToUnicode-derived char is unrelated.
            // For Standard-14 / WinAnsi fonts byte == Unicode for the ASCII range
            // so byte-keyed lookup is also correct there. For /Differences-mapped
            // Polish/Czech/etc. chars decoded to Unicode above 255, fall back to the
            // TTF hmtx advance via the parser's resolved GID. Without this fallback
            // the chars would all advance by 500 and visibly overlap (or, for subset
            // fonts, render with huge gaps between letters).
            int charWidth = 500;
            int widthCode = useBytesFallback ? rawBytes![i] : ch;
            if (fontMetrics is not null)
                charWidth = fontMetrics.GetWidth(widthCode);
            // A code the font dict gives no width - no /Widths entry, or one of 0, which
            // FontMetrics answers with its default - advances by the glyph that draws it,
            // as does a run drawn in a substitute face with no metrics at all. Without it
            // a broken /Widths of 0 stepped a narrow face by a whole em and opened a gap
            // after the glyph. Same rule as the GDI+ renderer's.
            if ((charWidth == 0 || (ch > 0xFF && (charWidth == 500 || charWidth <= 0))
                 || (substituted && fontMetrics is null)
                 || (fontMetrics is not null && !fontMetrics.HasExplicitWidth(widthCode)))
                && parser is not null && gid > 0)
            {
                var hmtxAdvance = parser.GetAdvanceWidth(gid);
                if (hmtxAdvance > 0 && parser.UnitsPerEm > 0)
                    charWidth = hmtxAdvance * 1000 / parser.UnitsPerEm;
            }
            px += charWidth / 1000.0 * effectiveSize * ctx.Scale * hScale + charSpacingPx;
            // Word spacing belongs to the code 32, not to another code that shows a space.
            if (rawBytes is not null && rawBytes.Length == text.Length ? rawBytes[i] == SpaceCode : ch == ' ') px += wordSpacingPx;
        }
        return px;
    }

    private const byte SpaceCode = 0x20;

    private static CidFontInfo? GetCidFontInfo(RenderContext ctx, string? fontName)
        => GetCidFontInfo(ctx.FontDicts, ctx.Reader, ctx.CidFontInfos, fontName);

    internal static CidFontInfo? GetCidFontInfo(
        Dictionary<string, PdfDictionary>? fontDicts, IO.PdfReader reader,
        Dictionary<string, CidFontInfo?> cache, string? fontName)
    {
        if (fontName is null || fontDicts is null) return null;
        if (cache.TryGetValue(fontName, out var cached)) return cached;

        CidFontInfo? info = null;
        if (fontDicts.TryGetValue(fontName, out var fontDict))
        {
            try { info = CidFontInfo.TryBuild(fontDict, reader); }
            catch { info = null; }
        }
        cache[fontName] = info;
        return info;
    }
}
