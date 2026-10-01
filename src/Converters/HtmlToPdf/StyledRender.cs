using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Render a styled-class data-font document (see
    /// <see cref="TryParseStyledDataFontDoc"/>) through the styled HTML engine's
    /// emission: A4 with the width grown to the content extent, one Y-flipped clipped
    /// frame, one 11-op text block per run (color, BT, Tf, color, Tm reset, Tm position,
    /// TJ, Tm reset, Tm identity, 0 g, ET), embedded Type0 faces, and CSS letter-spacing
    /// as per-glyph TJ segmentation whose adjustment is the float32 of the 3-decimal-
    /// rounded spacing. Geometry and margin model: page margins 90/72;
    /// body margin defaults to 6pt (auto → 0) and its top COLLAPSES (max) with the
    /// element chain; block gap = descLB + max(adjoining margins incl. div boundaries)
    /// + ascLB; line height is the font-independent 1.5·round(0.78125·size); the page
    /// width is max(595, rightmost text extent + 90). Returns null when a face fails
    /// to parse or the content overruns one page.</summary>
    internal static Document? RenderStyledDataFontDoc(StyledNode bodyNode)
    {
        var sy = new StyledDataDocState();
        sy.bodyNode = bodyNode;
        sy.marginTop = 72.0;
        sy.marginLeftLay = 90.0;

        sy.bodyWidth = sy.bodyNode.Style.TryGetValue("width", out var bw) ? StyledLen(bw) : 0;
        if (sy.bodyWidth <= 0) sy.bodyWidth = 595.0 - sy.marginLeftLay - 90.0;
        sy.bodyMarginLeft = sy.bodyNode.Style.TryGetValue("margin-left", out var bml) ? StyledLen(bml) : 6.0;
        sy.bodyMarginTop = sy.bodyNode.Style.TryGetValue("margin-top", out var bmt) ? StyledLen(bmt) : 6.0;

        sy.glyphParsers = new Dictionary<string, Text.GlyphOutlineParser>(StringComparer.Ordinal);
        sy.faceMetrics = new Dictionary<string, (double winAsc, double winDesc, double upm)>(StringComparer.Ordinal);
        // Font-independent CSS "normal" line height (LH(9)=10.5, LH(11.52)=13.5,
        // LH(12)=13.5), with win-metric half-leading baselines.
        sy.runsOut = new List<(double y, double x, string text, StyledNode p)>();
        sy.maxUrx = 0.0;
        sy.y = sy.marginTop;                     // top-down; baseline set per leaf
        sy.pendingMargins = new List<double> { sy.bodyMarginTop };
        sy.prevDesc = 0.0;

        if (!SdWalkLayout(sy, sy.bodyNode) || sy.runsOut.Count == 0) return null;
        return BuildStyledPage(sy.runsOut, sy.maxUrx);
    }

    /// <summary>Emit the laid-out runs as the fixed op pattern onto a fresh
    /// single-page document whose width is the content extent + the right margin.</summary>
    private static Document? BuildStyledPage(
        List<(double y, double x, string text, StyledNode p)> runsOut, double maxUrx)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        const double pageHeight = 842.0;
        const double marginLeft = 90.0;
        var pageWidth = Math.Max(595.0, maxUrx + 90.0);
        if (runsOut.Any(r => r.y > pageHeight - 60)) return null;   // beyond one page: legacy flow

        var doc = Document.Create();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        var fontDict = Table.ResolvePageFontDict(page);

        static string F(double v) => ((double)(float)v).ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
        static string FD(double v) => v.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append("q\n");
        sb.Append($"1 0 0 -1 0 {FD(pageHeight)} cm\n");
        sb.Append("q\nQ\nq\n");
        sb.Append($"{FD(marginLeft)} 0 {F(pageWidth - marginLeft)} {FD(pageHeight)} re\n");
        sb.Append("W*\nn\nq\nq\n");

        foreach (var (y, x, text, p) in runsOut)
        {
            var ls = p.Style.TryGetValue("letter-spacing", out var lsv) ? StyledLen(lsv) : 0.0;
            var lsF = (double)(float)Math.Round(ls, 3);
            var (rr, gg, bb) = ((byte)0, (byte)0, (byte)0);
            if (p.Style.TryGetValue("color", out var col))
            {
                var cm = Regex.Match(col, @"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)");
                if (cm.Success)
                    (rr, gg, bb) = (byte.Parse(cm.Groups[1].Value), byte.Parse(cm.Groups[2].Value),
                        byte.Parse(cm.Groups[3].Value));
            }
            var (res, hex) = Text.Type0FontEmbedder.Embed(fontDict, p.Ttf!,
                StyledFontDisplayName(p.FontKey), text, stripSpacesInBaseFont: true);
            sb.Append($"{F(rr / 255.0)} {F(gg / 255.0)} {F(bb / 255.0)} rg").Append('\n');
            sb.Append("BT\n");
            sb.Append($"/{res} {p.FontSizePt.ToString("0.000", ic)} Tf\n");
            sb.Append($"{FD(rr / 255.0)} {FD(gg / 255.0)} {FD(bb / 255.0)} rg").Append('\n');
            sb.Append("1 0 0 -1 0 0 Tm\n");
            sb.Append($"1 0 0 -1 {F(x)} {F(y)} Tm\n");
            sb.Append(BuildStyledTj(hex, lsF, p.FontSizePt));
            sb.Append("1 0 0 -1 0 0 Tm\n");
            sb.Append("1 0 0 1 0 0 Tm\n");
            sb.Append("0 g\n");
            sb.Append("ET\n");
        }

        sb.Append("Q\nQ\nQ\nq\nq\nQ\nQ\nQ\n");
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        return doc;
    }

    /// <summary>Human display name for an embedded data-face key ("firasans-medium" →
    /// "FiraSans-Medium", "firasans|bold" → "FiraSans Bold") — the space-stripped form
    /// becomes the /BaseFont name.</summary>
    private static string StyledFontDisplayName(string key)
    {
        var parts = key.Split('|');
        // Preserve the hyphenated form the @font-face declared (family case is lost in
        // the key; recapitalize per segment — cosmetic only, the test reads structure).
        var display = string.Join("-", parts[0].Split('-').Select(seg =>
            seg.Length == 0 ? seg : char.ToUpperInvariant(seg[0]) + seg.Substring(1)));
        return parts.Length > 1 ? display + " " + char.ToUpperInvariant(parts[1][0]) + parts[1].Substring(1) : display;
    }

    /// <summary>Build the TJ op for one line: letter-spaced text emits every glyph as its
    /// own 2-byte hex segment with the spacing adjustment (−spacing/size·1000) between
    /// consecutive glyphs and none after the last; unspaced text is one whole segment.</summary>
    private static string BuildStyledTj(byte[] hexGlyphIds, double letterSpacingPt, double fontSizePt)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append('[');
        var n = hexGlyphIds.Length / 2;
        if (letterSpacingPt <= 0 || n <= 1)
        {
            sb.Append('<');
            foreach (var b in hexGlyphIds) sb.Append(b.ToString("X2"));
            sb.Append('>');
        }
        else
        {
            var adj = (-letterSpacingPt / fontSizePt * 1000.0).ToString("0.##########", ic);
            for (var i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ').Append(adj).Append(' ');
                sb.Append('<');
                sb.Append(hexGlyphIds[i * 2].ToString("X2"));
                sb.Append(hexGlyphIds[i * 2 + 1].ToString("X2"));
                sb.Append('>');
            }
        }
        sb.Append("] TJ\n");
        return sb.ToString();
    }

    /// <summary>True when every non-WinAnsi character of the line sits in the Unicode
    /// Specials block (U+FFF0–U+FFFF — the U+FFFD a mojibake decode leaves): such a
    /// line stays in the flow face and only the replacement glyph re-faces.</summary>
    private static bool OnlySpecialsNonAnsi(string s)
    {
        var any = false;
        foreach (var ch in s)
        {
            if (ch <= 0x7F || Text.Cp1252.TryGetByte(ch) is not null) continue;
            if (ch is < '￰' or > '￿') return false;
            any = true;
        }
        return any;
    }

    /// <summary>True when the run contains any character the WinAnsi (Cp1252) Tf/Tj path
    /// cannot encode — Cyrillic, Greek, Armenian, Arabic, Hebrew, CJK, … . Such a run must
    /// go through an embedded Unicode face or its non-Latin characters flatten to '?'.</summary>
    private static bool NeedsUnicode(string s)
    {
        foreach (var ch in s)
            if (ch > 0x7F && Text.Cp1252.TryGetByte(ch) is null) return true;
        return false;
    }

    /// <summary>Convert the embedded RTL segments of a MIXED LTR+RTL line to visual order
    /// in place: each maximal run of RTL characters (with any neutrals bounded by RTL chars
    /// on both sides) is shaped/reversed via <see cref="ToVisualRtl"/> while the LTR text
    /// around it keeps its logical position. The extraction-side logicalizer reverses the
    /// per-run transformation, so round-tripped text stays token-identical.</summary>
    private static string VisualizeMixedRtl(string s)
    {
        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (!Text.BidiReorderer.IsRtlChar(s[i])) { sb.Append(s[i]); i++; continue; }
            // Extend to the LAST RTL char of this cluster, keeping internal neutrals
            // (spaces, digits, punctuation between two RTL words) inside the segment.
            int end = i, j = i;
            while (j < s.Length)
            {
                if (Text.BidiReorderer.IsRtlChar(s[j])) { end = j; j++; }
                else if (s[j] == ' ' || s[j] == ' ' || char.IsPunctuation(s[j]) || char.IsDigit(s[j])) j++;
                else break;
            }
            sb.Append(ToVisualRtl(s.Substring(i, end - i + 1)));
            i = end + 1;
        }
        return sb.ToString();
    }

    /// <summary>A line of a RIGHT-TO-LEFT paragraph in visual order: UAX #9 at paragraph level 1
    /// (the Latin words move to the visual left, the neutrals between them and the Arabic follow
    /// the paragraph), then every Arabic run takes its contextual forms.</summary>
    private static string VisualizeRtlParagraph(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var (text, _) in VisualizeRtlParagraphSegments(s, _ => false)) sb.Append(text);
        return sb.ToString();
    }

    /// <summary>The same visual order cut into emphasis segments: consecutive visual characters whose
    /// logical positions share <paramref name="boldAt"/>, each segment's Arabic runs shaped from their
    /// logical text (a bold Arabic word inside a regular line keeps its own face and forms).</summary>
    private static List<(string text, bool bold)> VisualizeRtlParagraphSegments(string s, Func<int, bool> boldAt)
    {
        var (vis, perm) = Text.BidiReorderer.ToVisualOnRtlParagraph(s);
        var segs = new List<(string text, bool bold)>();
        if (vis.Length == 0) return segs;
        // a line without a reorder maps every visual position onto itself
        if (perm is null)
        {
            perm = new int[vis.Length];
            for (var k = 0; k < perm.Length; k++) perm[k] = k;
        }
        var i = 0;
        while (i < vis.Length)
        {
            var bold = boldAt(perm[i]);
            var j = i;
            while (j < vis.Length && boldAt(perm[j]) == bold) j++;
            segs.Add((ShapeArabicRuns(s, vis, perm, i, j), bold));
            i = j;
        }
        return segs;
    }

    /// <summary>The visual slice [<paramref name="from"/>, <paramref name="to"/>) with each maximal run of
    /// Arabic letters (and the spaces between them) replaced by the contextual forms of its LOGICAL text.</summary>
    private static string ShapeArabicRuns(string logical, string vis, int[] perm, int from, int to)
    {
        var sb = new StringBuilder(to - from);
        var i = from;
        while (i < to)
        {
            if (!Text.ArabicTextShaper.ContainsArabic(vis[i].ToString())) { sb.Append(vis[i]); i++; continue; }
            int end = i, j = i;
            while (j < to)
            {
                if (Text.ArabicTextShaper.ContainsArabic(vis[j].ToString())) { end = j; j++; }
                else if (vis[j] == ' ' || vis[j] == (char)0xA0) j++;
                else break;
            }
            int lo = int.MaxValue, hi = -1;
            for (var k = i; k <= end; k++) { lo = Math.Min(lo, perm[k]); hi = Math.Max(hi, perm[k]); }
            sb.Append(Text.ArabicTextShaper.Shape(logical.Substring(lo, hi - lo + 1)));
            i = end + 1;
        }
        return sb.ToString();
    }

    /// <summary>True when the line is entirely RTL (Arabic/Hebrew/…) letters plus neutral
    /// punctuation/whitespace — the case where the run can be written wholesale in visual order.
    /// Mixed LTR+RTL lines need full bidi and fall through to the Standard-14 path.</summary>
    private static bool IsPureRtl(string s)
    {
        var hasRtl = false;
        foreach (var c in s)
        {
            if (Text.BidiReorderer.IsRtlChar(c)) hasRtl = true;
            else if (c == ' ' || c == '\t' || (c >= '!' && c <= '@')
                     || (c >= '[' && c <= '`') || (c >= '{' && c <= '~'))
            { /* neutral */ }
            else return false;
        }
        return hasRtl;
    }

    /// <summary>Convert a pure-RTL logical string to the VISUAL order drawn left-to-right:
    /// Arabic gets contextual shaping (which already emits visual order); other RTL scripts
    /// (Hebrew, …) are simply reversed.</summary>
    private static string ToVisualRtl(string s)
    {
        if (Text.ArabicTextShaper.ContainsArabic(s)) return Text.ArabicTextShaper.Shape(s);
        // Reverse the line run-wise: DIGIT sequences — including their internal
        // separators (14:00-16:30, 1/11/2014, 99.5%) — read left-to-right inside
        // an RTL line, so they keep their logical order while everything else
        // reverses around them.
        var runs = new List<string>();
        var i = 0;
        while (i < s.Length)
        {
            int j;
            if (char.IsDigit(s[i]))
            {
                j = i + 1;
                while (j < s.Length && (char.IsDigit(s[j])
                    || (s[j] is ':' or '/' or '-' or '.' or ','
                        && j + 1 < s.Length && char.IsDigit(s[j + 1]))))
                    j++;
                runs.Add(s[i..j]);
            }
            else
            {
                j = i + 1;
                while (j < s.Length && !char.IsDigit(s[j])) j++;
                var seg = s[i..j].ToCharArray();
                System.Array.Reverse(seg);
                runs.Add(new string(seg));
            }
            i = j;
        }
        runs.Reverse();
        return string.Concat(runs);
    }

    /// <summary>Emit a single positioned text run at (<paramref name="x"/>,<paramref name="y"/>).
    /// A pure Arabic/Hebrew or CJK run is written in visual order through an embedded Type0/CID
    /// face (the Standard-14 fonts would collapse it to '?'); everything else uses the WinAnsi
    /// Tf/Tj path. Used for list markers, which may themselves be non-Latin (a CSS ::before
    /// generated Arabic marker).</summary>
    private static void EmitPositionedRun(Page page, string fontRes, double fontSize, double x, double y, string text, double shear = 0)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        // (a synthetic italic: the upright glyphs sheared right by the slant, see SyntheticItalicShear)
        var tmSlant = shear != 0 ? shear.ToString("0.###", inv) : "0";
        var isRtl = IsPureRtl(text);
        var visual = isRtl ? ToVisualRtl(text)
            : Text.BidiReorderer.ContainsRtl(text) ? VisualizeMixedRtl(text) : text;
        var uniFont = NeedsUnicode(text) ? ResolveUnicodeFont(visual) : null;
        var ttf = uniFont?.SourceFontData?.TtfData;
        var sb = new StringBuilder();
        sb.AppendLine("BT");
        if (ttf is not null
            && page.Dict.Get("Resources") as Core.PdfDictionary is { } res
            && res.Get("Font") as Core.PdfDictionary is { } fontDict)
        {
            var (rn, hex) = Text.Type0FontEmbedder.Embed(
                fontDict, ttf, uniFont!.FontName ?? "Unicode", visual, stripSpacesInBaseFont: true);
            sb.Append($"/{rn} {fontSize.ToString("F1", inv)} Tf ");
            sb.Append($"1 0 {tmSlant} 1 {x.ToString("F2", inv)} {y.ToString("F2", inv)} Tm ");
            sb.Append('<').Append(Compat.ToHexString(hex)).Append("> Tj ");
        }
        else
        {
            sb.Append($"/{fontRes} {fontSize.ToString("F1", inv)} Tf ");
            sb.Append($"1 0 {tmSlant} 1 {x.ToString("F2", inv)} {y.ToString("F2", inv)} Tm ");
            sb.Append($"({EscapePdfString(text)}) Tj ");
        }
        sb.AppendLine("ET");
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    // Broad-Unicode faces (installed on most Windows systems) tried in order; the first
    // whose embedded program covers every non-WinAnsi char in the run is used.
    private static readonly string[] UnicodeFallbackFonts =
        { "Arial", "SimSun", "Malgun Gothic", "Microsoft YaHei", "MS Gothic", "Arial Unicode MS",
          // Script-specific faces shipped with Windows 10/11, tried after the broad CJK
          // set: Indic, Myanmar, Ethiopic/NKo, Canadian Syllabics, Thaana, Syriac,
          // Thai/Lao/Khmer, and historic scripts (Gothic, Old Italic, …).
          "Nirmala UI", "Myanmar Text", "Ebrima", "Gadugi", "MV Boli", "Estrangelo Edessa",
          "Leelawadee UI", "Segoe UI Historic" };

    /// <summary>Resolve an embedded Unicode fallback face that covers every non-WinAnsi
    /// character in <paramref name="text"/>, or null when none is available.</summary>
    private static Text.Font? ResolveUnicodeFont(string text)
    {
        // The REPLACEMENT CHARACTER resolves through Microsoft Sans Serif ahead of
        // the broad-CJK candidates — a mojibake U+FFFD draws with
        // that face's replacement glyph inside an otherwise serif line.
        if (text.Length == 1 && text[0] == '�')
        {
            if (!_uniFontCache.TryGetValue("Microsoft Sans Serif", out var mssEntry))
            {
                Text.Font? mss = null; Dictionary<int, int>? mssCmap = null;
                try
                {
                    mss = Text.FontRepository.TryFindFont("Microsoft Sans Serif");
                    if (mss?.SourceFontData?.TtfData is { } mssTtf)
                        mssCmap = new Text.GlyphOutlineParser(mssTtf).CMap;
                }
                catch { mss = null; mssCmap = null; }
                mssEntry = (mss, mssCmap);
                _uniFontCache["Microsoft Sans Serif"] = mssEntry;
            }
            if (mssEntry.font?.SourceFontData is not null
                && mssEntry.cmap is { } mssMap
                && mssMap.TryGetValue(0xFFFD, out var mssGid) && mssGid != 0)
                return mssEntry.font;
        }
        // Symbol-font private-use runs (U+F0xx — a symbol face's chars offset
        // into the PUA, e.g. Wingdings' box glyphs): only the symbol face's own
        // (3,0) cmap covers them, so resolve Wingdings ahead of the Unicode
        // fallbacks when every char in the run is in that block.
        var allSymbolPua = text.Length > 0;
        foreach (var ch in text)
            if (ch < '' || ch > '') { allSymbolPua = false; break; }
        if (allSymbolPua)
        {
            if (!_symbolPuaProbed)
                lock (_symbolPuaLock)
                    if (!_symbolPuaProbed)
                    {
                        try
                        {
                            _symbolPuaFont = Text.FontRepository.FindFont("Wingdings")
                                is { SourceFontData: not null } wd ? wd : null;
                        }
                        catch { _symbolPuaFont = null; /* fall through to the Unicode fallbacks */ }
                        _symbolPuaProbed = true;
                    }
            if (_symbolPuaFont is not null) return _symbolPuaFont;
        }
        foreach (var name in UnicodeFallbackFonts)
        {
            if (!_uniFontCache.TryGetValue(name, out var entry))
            {
                Text.Font? f = null; Dictionary<int, int>? cmap = null;
                try
                {
                    f = Text.FontRepository.TryFindFont(name);
                    if (f?.SourceFontData?.TtfData is { } ttf) cmap = new Text.GlyphOutlineParser(ttf).CMap;
                }
                catch { f = null; cmap = null; }
                entry = (f, cmap);
                _uniFontCache[name] = entry;
            }
            if (entry.font?.SourceFontData is null || entry.cmap is null) continue;
            var covers = true;
            foreach (var ch in text)
            {
                if (ch <= 0x7F || Text.Cp1252.TryGetByte(ch) is not null) continue;
                if (entry.cmap.TryGetValue(ch, out var gid) && gid != 0) continue;
                // A CJK radical counts as covered when the face carries its unified
                // ideograph — the draw side maps it the same way (GlyphIdOrLookAlike),
                // so SimSun keeps a run whose only gap is the radical form.
                var alt = Text.GlyphOutlineParser.RadicalLookAlike(ch);
                if (alt != 0 && entry.cmap.TryGetValue(alt, out var altGid) && altGid != 0) continue;
                covers = false; break;
            }
            if (covers) return entry.font;
        }
        return null;
    }
}
