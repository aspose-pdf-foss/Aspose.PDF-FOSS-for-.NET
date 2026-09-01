using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One character's advance in the named face at full font-unit
    /// precision, in milli-em (units/upm×1000). Characters the face cannot map
    /// fall back to the Times New Roman metric (the CSS fallback face a viewer
    /// would substitute); half an em when even that fails.</summary>
    internal static double StlCharAdvanceMilli(string faceName, int cp)
    {
        if (cp == 0x00A0) cp = 0x20;
        var face = PosFace(faceName);
        var gid = face.parser is not null && face.parser.CMap.TryGetValue(cp, out var g) ? g : 0;
        if (face.parser is not null && gid != 0)
            return face.parser.GetAdvanceWidth(gid) * 1000.0 / face.upm;
        return StlFallbackAdvanceMilli(cp);
    }

    /// <summary>The CSS fallback face's (Times New Roman) advance for one
    /// character, milli-em. A character Times cannot map falls back to the
    /// ideograph rule — a CJK glyph advances a FULL em in every real CJK face
    /// (and in this measurement model); the half-em guess is only for unmapped
    /// non-ideographs.</summary>
    internal static double StlFallbackAdvanceMilli(int cp)
    {
        if (cp == 0x00A0) cp = 0x20;
        var fb = PosFace("Times New Roman");
        var gid = fb.parser is not null && fb.parser.CMap.TryGetValue(cp, out var g) ? g : 0;
        if (fb.parser is not null && gid != 0)
            return fb.parser.GetAdvanceWidth(gid) * 1000.0 / fb.upm;
        return StlIdeograph(cp) ? 1000.0 : 500.0;
    }

    /// <summary>True when the token holds any full-width codepoint, surrogate pairs included.</summary>
    private static bool HasFullWidthCp(string word)
    {
        for (var i = 0; i < word.Length; i++)
        {
            int cp = word[i];
            if (char.IsHighSurrogate(word[i]) && i + 1 < word.Length && char.IsLowSurrogate(word[i + 1]))
            {
                cp = char.ConvertToUtf32(word[i], word[i + 1]);
                i++;
            }
            if (IsFullWidthCp(cp)) return true;
        }
        return false;
    }

    /// <summary>The string the writers actually put on the page for <paramref name="s"/>.
    /// An Arabic run is drawn in its CONTEXTUAL presentation forms (every RTL line goes
    /// through <c>ToVisualRtl</c> -> <see cref="Text.ArabicTextShaper.Shape"/> before it is
    /// emitted), and a joined form is narrower than the isolated letter, so measuring the raw
    /// code points over-measures the run by the whole shaping saving: the wrap breaks lines
    /// early and a right-anchored line stops short of its edge by that much. Shaping is per
    /// WORD - a space breaks the join - so a measure that sums word by word stays additive.
    /// A face carrying no presentation forms keeps the raw text: for it the shaped string is
    /// all unmapped glyphs, which measures worse than the base letters do.</summary>
    private static string ShapedAsDrawn(
        (byte[]? ttf, Text.GlyphOutlineParser? parser, double upm) face, string s)
    {
        if (face.parser is null || !Text.ArabicTextShaper.ContainsArabic(s)) return s;
        var shaped = Text.ArabicTextShaper.Shape(s);
        foreach (var ch in shaped)
            if (ch is >= '\uFB50' and <= '\uFEFF' && !face.parser.CMap.ContainsKey(ch))
                return s;
        return shaped;
    }

    /// <summary>The dash-delimited unbreakable segments of a text: pieces bounded by
    /// spaces and by after-dash positions (a segment keeps its trailing dash). The
    /// widest of these is a line's min-content — the quirks CSS-run wrap limit.</summary>
    private static IEnumerable<string> DashSegments(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
            {
                if (i > start) yield return text[start..i];
                start = i + 1;
            }
            else if (text[i] == '-')
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        }
        if (start < text.Length) yield return text[start..];
    }

    /// <summary>Greedy word wrap with REAL font advances (the metric flow's wrap — the
    /// legacy estimate breaks lines that should stay whole). Breaks at ordinary spaces only;
    /// non-breaking spaces bind their words.</summary>
    /// <summary>A CSS font-size value in points: absolute keywords at the UA's
    /// px mapping (small = 13px, medium = 16px, ...), the relative keywords
    /// against the UA 16px base, or any parseable length.</summary>
    private static bool TryParseCssFontSize(string v, out double pt)
    {
        pt = v.Trim().ToLowerInvariant() switch
        {
            "xx-small" => 9 * 0.75,
            "x-small" => 10 * 0.75,
            "small" => 13 * 0.75,
            "medium" => 16 * 0.75,
            "large" => 18 * 0.75,
            "x-large" => 24 * 0.75,
            "xx-large" => 32 * 0.75,
            "larger" => 19.2 * 0.75,      // 1.2 x the 16px UA base
            "smaller" => 13.33 * 0.75,
            _ => 0,
        };
        if (pt > 0) return true;
        return TryParseLength(v, out pt) && pt > 0;
    }

    /// <summary>Parse a legacy font size attribute ("2", "+1", "-1") to points.
    /// A signed value is relative to the default size 3.</summary>
    private static bool TryParseHtmlFontSize(string raw, out double pt)
    {
        pt = 0;
        raw = raw.Trim();
        if (raw.Length == 0) return false;
        var rel = raw[0] is '+' or '-';
        if (!int.TryParse(raw, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out var n)) return false;
        var idx = Math.Clamp(rel ? 3 + n : n, 1, 7);
        pt = HtmlFontSizeLadderPx[idx - 1] * 0.75;
        return true;
    }

    /// <summary>Concatenate two `style` attribute values, keeping the FIRST declaration
    /// of each property so consumers that read either the first or the last occurrence
    /// of a property agree.</summary>
    private static string MergeStyleFirstWins(string first, string second)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        foreach (var block in new[] { first, second })
            foreach (Match d in StyleDeclRx.Matches(block))
            {
                var prop = d.Groups[1].Value.Trim();
                if (!seen.Add(prop)) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(prop).Append(':').Append(d.Groups[2].Value.Trim());
            }
        return sb.Length > 0 ? sb.ToString() : first;
    }

    private static string DecodeEntities(string text)
    {
        // Numeric first (ConvertFromUtf32 also covers astral-plane references), then the
        // full HTML named-entity table. &nbsp; becomes a real no-break space (U+00A0) so
        // Trim() leaves it in place; an &nbsp;-only paragraph is a deliberate vertical
        // spacer in many CMS-generated HTMLs and should occupy a line.
        text = Regex.Replace(text, @"&#(\d+);", m =>
            int.TryParse(m.Groups[1].Value, out var code) ? char.ConvertFromUtf32(Cp1252Ref(code)) : m.Value);
        text = Regex.Replace(text, @"&#x([0-9A-Fa-f]+);", m =>
            int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var code)
                ? char.ConvertFromUtf32(Cp1252Ref(code)) : m.Value);
        // A numeric reference missing its semicolon still decodes (the WHATWG
        // parse-error recovery): form generators emit "&#8202<div".
        text = Regex.Replace(text, @"&#(\d+)(?![\d;])", m =>
            int.TryParse(m.Groups[1].Value, out var code) ? char.ConvertFromUtf32(Cp1252Ref(code)) : m.Value);
        return text.Contains('&') ? System.Net.WebUtility.HtmlDecode(text) : text;
    }

    /// <summary>An HTML numeric character reference in 128–159 refers to the
    /// Windows-1252 glyph at that byte (the WHATWG parser rule), not the C1 control
    /// block — legacy filing HTML writes &amp;#146; for the apostrophe ’.</summary>
    private static int Cp1252Ref(int code) => code switch
    {
        0x80 => 0x20AC, 0x82 => 0x201A, 0x83 => 0x0192, 0x84 => 0x201E, 0x85 => 0x2026,
        0x86 => 0x2020, 0x87 => 0x2021, 0x88 => 0x02C6, 0x89 => 0x2030, 0x8A => 0x0160,
        0x8B => 0x2039, 0x8C => 0x0152, 0x8E => 0x017D, 0x91 => 0x2018, 0x92 => 0x2019,
        0x93 => 0x201C, 0x94 => 0x201D, 0x95 => 0x2022, 0x96 => 0x2013, 0x97 => 0x2014,
        0x98 => 0x02DC, 0x99 => 0x2122, 0x9A => 0x0161, 0x9B => 0x203A, 0x9C => 0x0153,
        0x9E => 0x017E, 0x9F => 0x0178,
        _ => code,
    };

    /// <summary>Greedy wrap where the first <paramref name="narrowLines"/> lines fit a
    /// narrower box — the ones running beside a left-floated image — and every line
    /// after them takes the full measure.</summary>
    private static string[] WordWrapPastFloat(string text, double narrowWidth,
        double fullWidth, int narrowLines, double charWidth)
    {
        if (string.IsNullOrEmpty(text)) return [""];
        var cw = Math.Max(charWidth, 1);
        var result = new List<string>();
        var remaining = text;
        while (remaining.Length > 0)
        {
            var maxChars = (int)((result.Count < narrowLines ? narrowWidth : fullWidth) / cw);
            if (maxChars <= 0) maxChars = 1;
            if (remaining.Length <= maxChars) { result.Add(remaining); break; }
            var breakAt = remaining.LastIndexOf(' ', maxChars);
            if (breakAt <= 0) breakAt = maxChars;
            result.Add(remaining[..breakAt]);
            remaining = remaining[breakAt..].TrimStart();
        }
        return result.Count == 0 ? [""] : result.ToArray();
    }

    private static string[] WordWrap(string text, double maxWidth, double charWidth)
    {
        if (string.IsNullOrEmpty(text)) return [""];
        var maxChars = (int)(maxWidth / Math.Max(charWidth, 1));
        if (maxChars <= 0) maxChars = 1;
        if (text.Length <= maxChars) return [text];

        var result = new List<string>();
        var remaining = text;
        while (remaining.Length > maxChars)
        {
            var breakAt = remaining.LastIndexOf(' ', maxChars);
            if (breakAt <= 0) breakAt = maxChars;
            result.Add(remaining[..breakAt]);
            remaining = remaining[breakAt..].TrimStart();
        }
        if (remaining.Length > 0) result.Add(remaining);
        return result.ToArray();
    }
    
    /// <summary>OS/2 usWinAscent and usWinAscent+usWinDescent as fractions of em for a
    /// resolvable face; null when the family or its metrics are unavailable.</summary>
    private static (double asc, double sum)? WinMetricsFor(string family)
    {
        if (string.IsNullOrEmpty(family)) return null;
        if (_winMetricsCache.TryGetValue(family, out var cached)) return cached;
        (double, double)? m = null;
        try
        {
            var ttf = Text.FontRepository.GetTtfData(family);
            if (ttf is not null)
            {
                var tp = new Text.TrueTypeParser(ttf);
                tp.Parse();
                if (tp.UsWinAscent > 0 && tp.UnitsPerEm > 0)
                    m = ((double)tp.UsWinAscent / tp.UnitsPerEm,
                         (double)(tp.UsWinAscent + tp.UsWinDescent) / tp.UnitsPerEm);
            }
        }
        catch { /* face without usable metrics: stay on the legacy model */ }
        _winMetricsCache[family] = m;
        return m;
    }

    /// <summary>Margin box (pt) from an inline style declaration — the `margin`
    /// shorthand first, then longhands override — with em lengths resolved against
    /// <paramref name="emPt"/> (an inline body margin's em is the body's own font
    /// size, not the converter's 11 pt default that TryParseLength assumes).</summary>
    private static (double top, double right, double bottom, double left) ParseInlineMarginBox(
        string decl, double emPt)
    {
        double Len(string v)
        {
            v = v.Trim();
            var em = Regex.Match(v, @"^(-?(?:\d+(?:\.\d+)?|\.\d+))\s*em$", RegexOptions.IgnoreCase);
            if (em.Success)
                return double.Parse(em.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture) * emPt;
            return v == "0" ? 0 : TryParseLength(v, out var pt) ? pt : 0;
        }
        double top = 0, right = 0, bottom = 0, left = 0;
        var sh = Regex.Match(decl, @"(?<![-\w])margin\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (sh.Success)
        {
            var parts = sh.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is >= 1 and <= 4)
            {
                var v = new double[parts.Length];
                for (var i = 0; i < parts.Length; i++) v[i] = Len(parts[i]);
                (top, right, bottom, left) = parts.Length switch
                {
                    1 => (v[0], v[0], v[0], v[0]),
                    2 => (v[0], v[1], v[0], v[1]),
                    3 => (v[0], v[1], v[2], v[1]),
                    _ => (v[0], v[1], v[2], v[3]),
                };
            }
        }
        foreach (var (name, set) in new (string, Action<double>)[]
                 {
                     ("margin-top", x => top = x), ("margin-right", x => right = x),
                     ("margin-bottom", x => bottom = x), ("margin-left", x => left = x),
                 })
        {
            var m = Regex.Match(decl, @"(?<![-\w])" + name + @"\s*:\s*([^;]+)",
                RegexOptions.IgnoreCase);
            if (m.Success) set(Len(m.Groups[1].Value));
        }
        return (top, right, bottom, left);
    }

    /// <summary>Split a line into runs for Thai mark stacking: each tone mark
    /// (U+0E48..U+0E4C) directly following an ABOVE vowel (U+0E31, U+0E34..U+0E37,
    /// U+0E47) becomes its own raised zero-advance chunk. Null when the line has
    /// no such pair — callers keep their single-run emit byte-for-byte.</summary>
    private static List<(string Text, bool Raised)>? SplitThaiStackedTones(string text)
    {
        static bool AboveVowel(char c) => c == 'ั' || (c >= 'ิ' && c <= 'ื') || c == '็';
        static bool ToneMark(char c) => c >= '่' && c <= '์';
        List<(string Text, bool Raised)>? chunks = null;
        var start = 0;
        for (var i = 1; i < text.Length; i++)
            if (ToneMark(text[i]) && AboveVowel(text[i - 1]))
            {
                chunks ??= new();
                if (i > start) chunks.Add((text[start..i], false));
                chunks.Add((text[i].ToString(), true));
                start = i + 1;
            }
        if (chunks is null) return null;
        if (start < text.Length) chunks.Add((text[start..], false));
        return chunks;
    }

    /// <summary>Intrinsic pixel size of a JPEG from its SOF marker (0 pair when
    /// the stream is not parseable).</summary>
    private static (int w, int h) JpegDims(byte[] jpg)
    {
        if (jpg.Length < 4 || jpg[0] != 0xFF || jpg[1] != 0xD8) return (0, 0);
        var i = 2;
        while (i + 9 < jpg.Length)
        {
            if (jpg[i] != 0xFF) { i++; continue; }
            var marker = jpg[i + 1];
            if (marker == 0xFF) { i++; continue; }
            // standalone markers carry no length payload
            if (marker is >= 0xD0 and <= 0xD9) { i += 2; continue; }
            var len = (jpg[i + 2] << 8) | jpg[i + 3];
            if (len < 2) return (0, 0);
            // SOF0..SOF15 (minus DHT/JPG/DAC): frame header holds the size
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                var h = (jpg[i + 5] << 8) | jpg[i + 6];
                var w = (jpg[i + 7] << 8) | jpg[i + 8];
                return (w, h);
            }
            i += 2 + len;
        }
        return (0, 0);
    }

    /// <summary>Vertical components of a box shorthand + longhands (px).</summary>
    private static (double top, double bottom) DomBoxTB(HtmlNode el, string box,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        double top = 0, bottom = 0;
        var sh = DomDecl(el, box, css);
        if (!string.IsNullOrEmpty(sh))
        {
            var parts = sh.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts.Length)
            {
                case 1: top = bottom = ParsePxValue(parts[0]); break;
                case 2: top = bottom = ParsePxValue(parts[0]); break;
                case 3: top = ParsePxValue(parts[0]); bottom = ParsePxValue(parts[2]); break;
                case 4: top = ParsePxValue(parts[0]); bottom = ParsePxValue(parts[2]); break;
            }
        }
        var t2 = DomDecl(el, box + "-top", css);
        if (!string.IsNullOrEmpty(t2)) top = ParsePxValue(t2);
        var b2 = DomDecl(el, box + "-bottom", css);
        if (!string.IsNullOrEmpty(b2)) bottom = ParsePxValue(b2);
        return (top, bottom);
    }
    /// <summary>The installed face an stl_ CSS font-family stack resolves to for
    /// measurement: first family of the stack, quotes and any "ABCDEF+" subset tag
    /// stripped; null when no installed font matches (extent pinning stays off).
    /// Shared with PdfToHtmlConverter so the save-side pin and the re-import measure
    /// with the same metrics.</summary>
    internal static string? ResolveStlFace(string familyStack)
    {
        if (string.IsNullOrEmpty(familyStack)) return null;
        var fam = familyStack.Split(',')[0].Trim().Trim('"', '\'').Trim();
        fam = Regex.Replace(fam, @"^[A-Z]{6}\+", "");
        return fam.Length > 0 && PosFace(fam).parser is not null ? fam : null;
    }

    /// <summary>See <see cref="MeasureFaceText"/> — exposed for the save-side extent pin.</summary>
    internal static double MeasureStlNaturalText(string faceName, string s, double fontSizePt)
        => MeasureFaceText(faceName, s, fontSizePt);

    /// <summary>A full-em CJK character: ideographs, kana/radicals, compatibility
    /// ideographs, fullwidth forms and the ideographic space. Also the IE-model
    /// word boundary — the em-compensation dialect charges word-spacing between
    /// two adjacent such characters exactly as at a drawn space.</summary>
    internal static bool StlIdeograph(int cp) =>
        (cp >= 0x2E80 && cp <= 0x9FFF)
        || (cp >= 0xF900 && cp <= 0xFAFF)
        || (cp >= 0xFF01 && cp <= 0xFF60)
        || cp == 0x3000;

    /// <summary>Text advance in the stl_ measurement model:
    /// per-glyph advances at full font-unit precision (units/upm, NOT the 1000-grid
    /// rounding of <see cref="MeasureFaceText"/>) evaluated at the FLOOR-3-DECIMALS
    /// quantized font size — the resolved CSS size quantizes to 0.001pt
    /// toward zero before measuring, while letter/word-spacing stay at the raw size.
    /// Both the save-side extent pin and the stl_ re-import measure with this so the
    /// letter-spacing classes and the reconstructed page width agree.</summary>
    internal static double MeasureStlExactText(string faceName, string s, double rawFontSizePt)
    {
        var face = PosFace(faceName);
        return MeasureParsedExact(face.parser, face.upm, s, rawFontSizePt);
    }

    /// <summary>Half an em — the last-resort advance for a codepoint no face on the
    /// machine can map.</summary>
    private const double UnmappedAdvanceEm = 0.5;

    /// <summary>The advance a codepoint the run's own face cannot map takes: the metric
    /// of the SUBSTITUTE face the draw path picks for it, so the measured extent and the
    /// drawn extent agree. A half-em guess is half an ideograph's full-width advance,
    /// which is what left a CJK page's widest line — and with it the reflow sheet's
    /// width — materially under its own drawn ink.</summary>
    private static double UnmappedAdvance(int cp, double fsEff)
    {
        var sub = PosFace(PosFaceNameFor(cp));
        if (sub.parser is not null && sub.parser.CMap.TryGetValue(cp, out var sgid) && sgid != 0)
            return sub.parser.GetAdvanceWidth(sgid) * fsEff / sub.upm;
        return UnmappedAdvanceEm * fsEff;
    }

    /// <summary>Text advance in a named face (via the PosFace cache), using the same
    /// rounded 1000-unit advances the embedded font declares. Unknown faces/glyphs
    /// fall back to a half-em estimate.</summary>
    /// <summary>A codepoint that advances a full em in CJK typography: unified
    /// ideographs (+ext A), compatibility ideographs, kana, hangul syllables,
    /// CJK symbols/punctuation and the fullwidth forms.</summary>
    private static bool IsFullWidthCp(int cp) =>
        cp is (>= 0x4E00 and <= 0x9FFF) or (>= 0x3400 and <= 0x4DBF)
            or (>= 0xF900 and <= 0xFAFF) or (>= 0x3000 and <= 0x33FF)
            or (>= 0x2E80 and <= 0x2FDF)   // CJK + Kangxi radicals — full-width glyphs
            or (>= 0xAC00 and <= 0xD7AF) or (>= 0xFF00 and <= 0xFF60)
            or (>= 0x20000 and <= 0x2FA1F);

    /// <summary>Line-box height (pt) under the metric model.</summary>
    private static double MetricLineHeight(double sizePt, double metricSum)
        => Math.Round(sizePt / 0.75 * metricSum, MidpointRounding.AwayFromZero) * 0.75;

    /// <summary>Baseline offset below the line-box top under the metric model.</summary>
    private static double MetricBaselineDrop(double sizePt, double lineHeight, (double asc, double sum) m)
        => (lineHeight - sizePt * m.sum) / 2 + sizePt * m.asc;

    // Thai mark-stacking geometry, measured on the expected render (Tahoma 11 pt):
    // a tone mark over an above vowel seats 2.42 pt higher than the baseline run
    // (0.220 em) and a small nudge right of the pen (1.64 pt = 0.149 em).
    private const double ThaiToneRaiseEm = 2.42 / 11.0;

    private const double ThaiToneNudgeEm = 1.64 / 11.0;

    private static double MaxSpaceWordWidth(string text, string face, double sizePt)
    {
        double mx = 0;
        foreach (var w in text.Split(' '))
            mx = Math.Max(mx, MeasureFaceText(face, w, sizePt));
        return mx;
    }

    // Adjacent tables stacked in ONE wrapper cell sit this far apart
    // (measured: the register's section wrappers at 145.5 -> 146.7).
    private const double WrapperSiblingGapPt = 1.2;

    // The legacy <font size=1..7> ladder in px (0.75 pt/px). Standard browser
    // mapping except size 1 = 9px — measured on the references: size1 headers draw
    // 6.75 pt, size2 9.75 (13px), size3 12 (16px), size4 13.5 (18px), size5 18 (24px).
    private static readonly double[] HtmlFontSizeLadderPx = { 9, 13, 16, 18, 24, 32, 48 };

    /// <summary>CSS `font-size: larger`: 1.2 x the current computed size, no
    /// rounding (measured on the newsletter title: 13px body → 15.6px = 11.7 pt,
    /// whose line box still px-rounds to 18px).</summary>
    private static double HtmlLargerStepPt(double pt) => pt * 1.2;

    /// <summary>Detect the legacy WRAPPER-TABLE idiom: a table whose every row is
    /// a single td holding only nested tables (whitespace/tbody chrome aside).
    /// Yields the wrapper tag's attribute text and the child tables in order.</summary>
    /// <summary>A legacy color ATTRIBUTE value: like CSS, but a bare 6-digit hex
    /// ("CCCCCC") counts — the browsers' error-tolerant attribute parser.</summary>
    private static Color? AttrColor(string v)
    {
        v = v.Trim();
        var c = ParseCssColor(v);
        if (c is null && Regex.IsMatch(v, @"^[0-9a-fA-F]{6}$")) c = ParseCssColor("#" + v);
        return c;
    }

    private static double EstimateNestedTableHeight(string html, double rowPitch)
    {
        var inner = ExtractNestedTables(html, out var subs);
        var h = Regex.Matches(inner, @"<tr\b", RegexOptions.IgnoreCase).Count * rowPitch;
        foreach (var sub in subs) h += EstimateNestedTableHeight(sub, rowPitch);
        return h;
    }

    /// <summary>Metric-flow table renderer: real HTML table geometry — default
    /// cellspacing 2px (1.5 pt) and cellpadding 1px (0.75 pt), stylesheet cell font,
    /// win-metric line boxes with half-leading baselines, middle vertical alignment,
    /// column widths from width-% attributes / inline-table spans / content, and
    /// row-at-a-time pagination (continuation pages resume at the raw content top).
    /// Emits positioned runs directly and advances the flow cursor to the table
    /// bottom. Only the metric flow calls this; the legacy generator-table path is
    /// untouched.</summary>
    // RTL grid anchoring: the table's RIGHT edge sits this far inside the page's
    // right edge (measured 91.78 on the widened RTL sheet — the widest grid's
    // LEFT edge then lands exactly on the 90 pt page margin).
    private const double RtlGridRightInsetPt = 91.78;

    // The RTL diagram arm's entry lift under the UA serif flow: its row constants
    // were calibrated at the legacy flow's entry, which spends this much less
    // between the preceding text block and the section (the h6 bottom margin the
    // legacy flow drops). Measured on the diagram report's
    // title label: ink at 103.11 with the lift, 115.35 without (expected 103.11).
    private const double DgUaEntryLiftPt = 12.24;

    // Faces the HTML engine actually resolves — a face outside
    // this set falls back to the flow default exactly like an unknown family
    // (probed: face="David" cells draw the UA serif; 'arial narrow' falls to the
    // flow face on the class-framework sheets).
    private static readonly HashSet<string> SourceEngineFaces =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Arial", "Helvetica", "Verdana", "Times New Roman", "Courier New",
            "Courier", "Tahoma", "Georgia", "Trebuchet MS", "Calibri", "SimSun",
            "MS Gothic",
        };

    // The image viewport: a cell photo wider than this draws
    // scaled down to it, preserving aspect (measured on the SSRS report export:
    // the 1024×768 px JPEG — 768 pt natural — lands exactly 612×459 pt, the
    // 8.5 in viewport width, and the sheet widens to hold it).
    private const double JpegViewportPt = 612.0;

}
