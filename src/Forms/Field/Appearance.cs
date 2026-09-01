using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public partial class Field
{
    /// <summary>Drop the cached DefaultAppearance so the next read re-parses the
    /// (possibly rewritten) /DA string. Used by facades that edit /DA directly.</summary>
    internal void ResetDefaultAppearanceCache() => _defaultAppearance = null;

    /// <summary>Build the DefaultAppearance from the field's stored /DA string
    /// (inherited through /Parent up to the AcroForm default). The FontName is the
    /// /DA RESOURCE name (e.g. "TiBI", "Helv") — callers resolve it against
    /// <see cref="Form.DefaultResources"/>. Falls back to Helvetica 12 when no /DA
    /// parses.</summary>
    private Aspose.Pdf.Annotations.DefaultAppearance ParseDefaultAppearance()
    {
        var da = FindInheritedDaString(_dict, 0);
        if (da is not null)
        {
            var m = System.Text.RegularExpressions.Regex.Match(da,
                @"/(\S+)\s+([\d.]+)\s+Tf");
            if (m.Success && double.TryParse(m.Groups[2].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var size))
            {
                var color = System.Drawing.Color.Black;
                var cm = System.Text.RegularExpressions.Regex.Match(da,
                    @"([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+rg");
                if (cm.Success)
                    color = System.Drawing.Color.FromArgb(
                        (int)(double.Parse(cm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 255),
                        (int)(double.Parse(cm.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) * 255),
                        (int)(double.Parse(cm.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) * 255));
                // A "0 Tf" size means auto-size; keep the raw 0.
                return new Aspose.Pdf.Annotations.DefaultAppearance(m.Groups[1].Value, size, color);
            }
        }
        return new Aspose.Pdf.Annotations.DefaultAppearance();
    }

    private string? FindInheritedDaString(PdfDictionary dict, int depth)
    {
        if (depth > 8) return null;
        if (_reader.Resolve(dict.Get("DA")) is PdfString ps)
            return System.Text.Encoding.Latin1.GetString(ps.Value);
        if (_reader.ResolveDict(dict.Get("Parent")) is { } parent)
            return FindInheritedDaString(parent, depth + 1);
        return null;
    }

    /// <summary>
    /// Generate a default <c>/AP /N</c> appearance stream for this field's widget
    /// so it renders without relying on a viewer's NeedAppearances pass. Overridden
    /// per field type; the base implementation is a no-op (fields with no visible
    /// representation, or types that build their appearance elsewhere).
    /// </summary>
    internal virtual void GenerateAppearance() { }

    /// <summary>Build an /AP dictionary (with /N) rendering this field's current value
    /// for an extra widget of the given size — used by multi-widget construction
    /// (<see cref="Form.AddFieldAppearance"/>). The base implementation returns null
    /// (no value appearance); value-bearing field types override it.</summary>
    internal virtual PdfDictionary? BuildWidgetApDict(double w, double h) => null;

    /// <summary>Format a coordinate for a content stream, invariant culture.</summary>
    private protected static string FmtNum(double v) =>
        v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Escape a string for use as a PDF literal-string operand.</summary>
    private protected static string EscapePdfText(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    /// <summary>Bind the widget to the rectangle the generator laid it out at:
    /// size the /Rect, (re)build the appearance for that size and list the widget
    /// on the page once.</summary>
    internal void PlaceGeneratorWidget(Page page, Rectangle rect)
    {
        if (this is TextBoxField tb) tb.ApplyGeneratorDefaultAppearance();
        Rect = rect;
        _dict.Remove("AP");
        GenerateAppearance();
        var annots = _reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
        if (annots is null) { annots = new PdfArray(); page.Dict.Set("Annots", annots); }
        foreach (var a in annots)
            if (ReferenceEquals(_reader.Resolve(a), _dict)) return;
        annots.Add(_dict);
    }

    /// <summary>Resolve the widget rectangle's width/height. Returns false when
    /// there is no usable (positive-area) rectangle.</summary>
    private protected bool TryWidgetSize(out double w, out double h)
    {
        w = h = 0;
        if (Reader.Resolve(Dict.Get("Rect")) is not PdfArray rectArr || rectArr.Count < 4)
            return false;
        var r = Rectangle.FromPdfArray(rectArr);
        w = r.Width; h = r.Height;
        return w > 0 && h > 0;
    }

    /// <summary>Parse the field's <c>/DA</c> default-appearance string for a font
    /// resource name and size, defaulting to Helvetica 12. A size of 0 in /DA
    /// means auto-size — the caller decides what size to substitute.</summary>
    private protected void ParseDefaultAppearance(out string fontName, out double fontSize)
        => ParseDefaultAppearance(out fontName, out fontSize, defaultSize: 12);

    private protected void ParseDefaultAppearance(out string fontName, out double fontSize, double defaultSize)
    {
        fontName = "Helv";
        fontSize = defaultSize;
        if (Reader.Resolve(Dict.Get("DA")) is not PdfString daStr) return;
        var parts = daStr.ToText().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] != "Tf" || i < 2) continue;
            fontName = parts[i - 2].TrimStart('/');
            double.TryParse(parts[i - 1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s);
            if (s > 0) fontSize = s;
        }
    }

    /// <summary>Extract the fill-colour operator (g/rg/k) from a /DA string,
    /// or <paramref name="fallback"/> when none is present.</summary>
    private protected static string ExtractDaColor(string da, string fallback = "0 g")
    {
        if (string.IsNullOrEmpty(da)) return fallback;
        var p = da.Split(new[] { ' ', '\n', '\t', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < p.Length; i++)
        {
            if (p[i] == "g" && i >= 1) return $"{p[i - 1]} g";
            if (p[i] == "rg" && i >= 3) return $"{p[i - 3]} {p[i - 2]} {p[i - 1]} rg";
            if (p[i] == "k" && i >= 4) return $"{p[i - 4]} {p[i - 3]} {p[i - 2]} {p[i - 1]} k";
        }
        return fallback;
    }

    /// <summary>Wrap a content string in a Form XObject appearance stream sized
    /// to the supplied bounding box.</summary>
    private protected static PdfStream MakeApXObject(string content, double w, double h,
        PdfDictionary? resources = null)
    {
        var stream = new PdfStream(new PdfDictionary(), System.Text.Encoding.Latin1.GetBytes(content));
        stream.Dict.Set("Type", new PdfName("XObject"));
        stream.Dict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
        bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        stream.Dict.Set("BBox", bbox);
        stream.Dict.Set("Resources", (PdfObject?)resources ?? new PdfDictionary());
        return stream;
    }

    /// <summary>Build a <c>/Font</c> resource dictionary mapping the given resource
    /// name to a Standard-14 base font, so an appearance stream that references it
    /// renders text (the renderer resolves a font only from declared resources).
    /// The /DR aliases written by Acrobat ("Helv" / "HeBo" / "ZaDb" / "TiRo" / ...)
    /// are translated to their PostScript base-font names; an unknown alias falls
    /// back to Helvetica.</summary>
    private protected static PdfDictionary MakeStandardFontResources(string fontName)
    {
        var alias = string.IsNullOrEmpty(fontName) ? "Helv" : fontName;
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(StandardBaseFont(alias)));
        var fonts = new PdfDictionary();
        fonts.Set(alias, font);
        var res = new PdfDictionary();
        res.Set("Font", fonts);
        return res;
    }

    /// <summary>The Acrobat /DR font aliases that name a Standard-14 face.</summary>
    private static readonly HashSet<string> StandardFontAliases = new(StringComparer.Ordinal)
    {
        "Helv", "HeBo", "HeOb", "HeBO", "TiRo", "TiBo", "TiIt", "TiBI",
        "Cour", "CoBo", "CoOb", "CoBO", "ZaDb", "Symb",
    };

    /// <summary>Build the <c>/Resources</c> dictionary for a regenerated text-field
    /// appearance so the <c>/{fontName} Tf</c> operator in the content stream
    /// resolves. Sibling fonts and non-font resources from
    /// <paramref name="existingRes"/> are carried over unchanged; when the
    /// <c>/DA</c> font name isn't already declared there, a Standard-14 entry under
    /// that name is synthesised. The AcroForm <c>/DR</c> face is deliberately not
    /// pulled in: a /DR font is often an embedded subset that only carries the
    /// field's original glyphs, so rendering a freshly-set value through it would
    /// drop characters the subset never included.</summary>
    private protected PdfDictionary BuildTextAppearanceResources(string fontName, PdfDictionary? existingRes,
        PdfDictionary? compositeFont = null)
    {
        var existingFonts = existingRes is null ? null : Reader.ResolveDict(existingRes.Get("Font"));

        var fonts = new PdfDictionary();
        if (existingFonts is not null)
            foreach (var key in existingFonts.Keys)
                fonts.Set(key, existingFonts.Get(key)!);

        if (compositeFont is not null)
            fonts.Set(fontName, compositeFont);
        else if (!fonts.ContainsKey(fontName))
            fonts.Set(fontName, MakeAppearanceFont(fontName));

        var res = new PdfDictionary();
        res.Set("Font", fonts);
        if (existingRes is not null)
            foreach (var key in existingRes.Keys)
                if (key != "Font")
                    res.Set(key, existingRes.Get(key)!);
        return res;
    }

    /// <summary>Synthesise a <c>/Font</c> dictionary for an appearance resource.
    /// A known Acrobat alias (Helv, TiRo, ...) maps to its Standard-14 PostScript
    /// base font. Any other name that resolves to an installed face is kept verbatim
    /// as the <c>/BaseFont</c> so a <c>/DA</c> naming a real font (e.g. "Arial")
    /// round-trips that name; a name that resolves to nothing — typically a bare
    /// subset tag whose <c>/DA</c> font isn't a usable family — falls back to
    /// Helvetica so the value still renders instead of vanishing.</summary>
    private static PdfDictionary MakeAppearanceFont(string fontName)
    {
        string baseFont;
        if (StandardFontAliases.Contains(fontName))
            baseFont = StandardBaseFont(fontName);
        else if (Aspose.Pdf.Text.SystemFontResolver.Resolve(fontName) is not null)
            baseFont = fontName;
        else
            baseFont = "Helvetica";
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFont));
        // Tag the encoding so the WinAnsi-encoded appearance text (see the appearance
        // content build) maps its 0x80-0x9F bytes to the right glyphs on both render and
        // text-extraction, rather than the font's default StandardEncoding.
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        // A face that is NOT one of the Standard 14 has no metrics a consumer can look
        // up by name, so a bare dictionary leaves every reader guessing: the extractor
        // seats such a run ON its baseline for want of a descent, so the appearance
        // ships the face's real metrics instead (a Tahoma appearance carries /Ascent
        // 1000 /Descent -206). Describe it properly - /TrueType, its widths and a
        // descriptor - while leaving the program itself unembedded.
        if (!StandardFontAliases.Contains(fontName) && !IsStandardBaseFontName(baseFont)
            && Aspose.Pdf.Text.SystemFontResolver.Resolve(fontName) is { } face)
            DescribeSimpleTrueType(font, baseFont, face);
        else
            DescribeStandard14(font, baseFont);
        return font;
    }

    /// <summary>Attach the AFM descriptor of a Standard-14 face. One is shipped
    /// even though nothing is embedded - a Helvetica appearance carries /Ascent 718
    /// /Descent -207 and a Courier one 629 / -157 - and without it a consumer reading the
    /// appearance back has no descent to seat the run by.</summary>
    private static void DescribeStandard14(PdfDictionary font, string baseFont)
    {
        var ascent = Aspose.Pdf.Text.Standard14Fonts.GetAscent(baseFont);
        var descent = Aspose.Pdf.Text.Standard14Fonts.GetDescent(baseFont);
        var box = Aspose.Pdf.Text.Standard14Fonts.GetFontBBox(baseFont);
        if (ascent <= 0 || box is null) return;
        var bbox = new PdfArray();
        foreach (var v in box) bbox.Add(new PdfInteger(v));
        var fd = new PdfDictionary();
        fd.Set("Type", new PdfName("FontDescriptor"));
        fd.Set("FontName", new PdfName(baseFont));
        // Symbolic (bit 3) for the two pi faces, nonsymbolic otherwise.
        var symbolic = baseFont is "Symbol" or "ZapfDingbats";
        fd.Set("Flags", new PdfInteger(symbolic ? 4 : 32));
        fd.Set("FontBBox", bbox);
        fd.Set("ItalicAngle", new PdfInteger(0));
        fd.Set("Ascent", new PdfInteger(ascent));
        fd.Set("Descent", new PdfInteger(descent > 0 ? -descent : descent));
        fd.Set("CapHeight", new PdfInteger(ascent));
        fd.Set("StemV", new PdfInteger(80));
        font.Set("FontDescriptor", fd);
    }

    /// <summary>True for one of the fourteen CANONICAL Standard-14 PostScript names,
    /// whose metrics every consumer already knows and which have no file to describe.
    /// Deliberately NOT Standard14Fonts.IsStandard14, which also answers true for an
    /// ALIAS of a real installed family (Arial, Times New Roman): those do have a face
    /// behind them and it gets described.</summary>
    private static readonly HashSet<string> Standard14BaseNames = new(StringComparer.Ordinal)
    {
        "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
        "Symbol", "ZapfDingbats",
    };

    private static bool IsStandardBaseFontName(string baseFont) =>
        Standard14BaseNames.Contains(baseFont);

    /// <summary>Turn a bare appearance font dictionary into a described (but still
    /// unembedded) simple TrueType font: /Widths over the WinAnsi range plus a
    /// /FontDescriptor carrying the face's own metrics. The ascent and descent are
    /// quantised the way a PDF writer quantises them - scaled to 1000 units and
    /// FLOORED per value, which is what the appearance emits.</summary>
    private static void DescribeSimpleTrueType(PdfDictionary font, string baseFont, byte[] face)
    {
        try
        {
            var ttf = new Aspose.Pdf.Text.TrueTypeParser(face);
            ttf.Parse();
            if (ttf.UnitsPerEm <= 0 || ttf.GlyphWidths.Length == 0) return;
            double scale = 1000.0 / ttf.UnitsPerEm;

            const int firstChar = 32, lastChar = 255;
            var widths = new PdfArray();
            for (var code = firstChar; code <= lastChar; code++)
            {
                var ch = Aspose.Pdf.Text.Cp1252.GetString(new[] { (byte)code });
                var w = 0.0;
                if (ch.Length == 1 && ttf.CMap.TryGetValue(ch[0], out var gid)
                    && gid > 0 && gid < ttf.GlyphWidths.Length)
                    w = System.Math.Round(ttf.GlyphWidths[gid] * scale);
                widths.Add(new PdfInteger((long)w));
            }

            var bbox = new PdfArray();
            foreach (var v in ttf.BBox) bbox.Add(new PdfInteger((long)System.Math.Floor(v * scale)));

            var fd = new PdfDictionary();
            fd.Set("Type", new PdfName("FontDescriptor"));
            fd.Set("FontName", new PdfName(baseFont));
            // Nonsymbolic (bit 3); italic (bit 7) from the head macStyle.
            var flags = 32 | ((ttf.MacStyle & 2) != 0 ? 64 : 0);
            fd.Set("Flags", new PdfInteger(flags));
            fd.Set("FontBBox", bbox);
            fd.Set("ItalicAngle", new PdfInteger(0));
            fd.Set("Ascent", new PdfInteger((long)System.Math.Floor(ttf.Ascent * scale)));
            fd.Set("Descent", new PdfInteger(-(long)System.Math.Floor(System.Math.Abs(ttf.Descent) * scale)));
            fd.Set("CapHeight", new PdfInteger((long)System.Math.Floor(
                (ttf.CapHeight > 0 ? ttf.CapHeight : ttf.Ascent) * scale)));
            fd.Set("StemV", new PdfInteger((ttf.MacStyle & 1) != 0 ? 160 : 80));

            font.Set("Subtype", new PdfName("TrueType"));
            font.Set("FirstChar", new PdfInteger(firstChar));
            font.Set("LastChar", new PdfInteger(lastChar));
            font.Set("Widths", widths);
            font.Set("FontDescriptor", fd);
        }
        catch { }
    }

    /// <summary>Resolve a /DA font name against the AcroForm /DR /Font dictionary.
    /// Referencing the actual /DR font (instead of synthesising a Type1 stand-in)
    /// keeps embedded composite faces working in regenerated appearances.</summary>
    private protected PdfDictionary? ResolveDrFontDict(string fontName)
    {
        try
        {
            var acro = Reader.ResolveDict(Reader.Catalog.Get("AcroForm"));
            var dr = acro is null ? null : Reader.ResolveDict(acro.Get("DR"));
            var fontDict = dr is null ? null : Reader.ResolveDict(dr.Get("Font"));
            return fontDict is null ? null : Reader.ResolveDict(fontDict.Get(fontName));
        }
        catch { return null; }
    }

    /// <summary>For a composite (/Type0, Identity-H, CIDToGIDMap Identity) /DA font,
    /// load the embedded face's char→glyph cmap so a regenerated appearance can encode
    /// the value as 2-byte glyph ids. Null when the font isn't composite/embedded.</summary>
    private protected System.Collections.Generic.Dictionary<int, int>? LoadCompositeCmap(PdfDictionary? type0)
        => LoadCompositeParser(type0)?.CMap;

    private protected Aspose.Pdf.Text.TrueTypeParser? LoadCompositeParser(PdfDictionary? type0)
    {
        try
        {
            if (type0?.GetName("Subtype") != "Type0") return null;
            var desc = Reader.Resolve(type0.Get("DescendantFonts")) as PdfArray;
            var cid = desc is { Count: > 0 } ? Reader.ResolveDict(desc[0]) : null;
            var fd = cid is null ? null : Reader.ResolveDict(cid.Get("FontDescriptor"));
            var ff = fd is null ? null : Reader.ResolveStream(fd.Get("FontFile2"));
            if (ff is null) return null;
            var ttf = Reader.DecodeStream(ff);
            var parser = new Aspose.Pdf.Text.TrueTypeParser(ttf);
            parser.Parse();
            return parser;
        }
        catch { return null; }
    }

    /// <summary>The /DR font adapted for use in a regenerated value appearance. A
    /// composite face is referenced through a shallow clone whose /BaseFont is the
    /// FAMILY name ("BitstreamCyberCJK") — the fill-time subset embeds
    /// under the family name, while the /DR default keeps the PostScript name.</summary>
    private protected PdfDictionary? AppearanceFontFromDr(string fontName)
    {
        var dr = ResolveDrFontDict(fontName);
        // Simple fonts keep the synthesized WinAnsi Type1 stand-in (the appearance
        // text is Cp1252-encoded); only a composite face must be carried verbatim.
        if (dr is null || dr.GetName("Subtype") != "Type0") return null;
        var family = LoadCompositeParser(dr)?.FamilyName;
        if (string.IsNullOrEmpty(family) || family == "Unknown") return dr;
        var clone = new PdfDictionary();
        foreach (var k in dr.Keys) clone.Set(k, dr.Get(k)!);
        clone.Set("BaseFont", new PdfName(family!.Replace(" ", "")));
        return clone;
    }

    /// <summary>Map an Acrobat /DR alias to its Standard-14 PostScript base-font name.</summary>
    private protected static string StandardBaseFont(string alias) => alias switch
    {
        "Helv" => "Helvetica",
        "HeBo" => "Helvetica-Bold",
        "HeOb" => "Helvetica-Oblique",
        "HeBO" => "Helvetica-BoldOblique",
        "TiRo" => "Times-Roman",
        "TiBo" => "Times-Bold",
        "TiIt" => "Times-Italic",
        "TiBI" => "Times-BoldItalic",
        "Cour" => "Courier",
        "CoBo" => "Courier-Bold",
        "CoOb" => "Courier-Oblique",
        "CoBO" => "Courier-BoldOblique",
        "ZaDb" => "ZapfDingbats",
        "Symb" => "Symbol",
        _ => "Helvetica",
    };

    /// <summary>Coerce a resolved numeric PDF object (integer or real) to a double.</summary>
    private protected static double? AsNumber(PdfObject? obj) => obj switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        _ => null,
    };

    /// <summary>Emit background-fill and/or border-stroke operators from the
    /// widget's <c>/MK</c> appearance-characteristics dictionary (<c>/BG</c>,
    /// <c>/BC</c>). Empty when neither colour is present.</summary>
    private protected string BuildMkBackgroundAndBorder(double w, double h)
    {
        var mk = Reader.ResolveDict(Dict.Get("MK"));
        if (mk is null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        if (MkColorOperator(mk.Get("BG"), fill: true) is { } bg)
            sb.Append($"{bg} 0 0 {FmtNum(w)} {FmtNum(h)} re f ");
        if (MkColorOperator(mk.Get("BC"), fill: false) is { } bc)
            sb.Append($"{bc} 1 w 0.5 0.5 {FmtNum(w - 1)} {FmtNum(h - 1)} re S ");
        return sb.ToString();
    }

    private protected string? MkColorOperator(PdfObject? entry, bool fill)
    {
        if (Reader.Resolve(entry) is not PdfArray arr) return null;
        var vals = new System.Collections.Generic.List<double>();
        foreach (var it in arr)
            if (AsNumber(Reader.Resolve(it)) is { } d) vals.Add(d);
        if (vals.Count == 0) return null; // empty array = transparent / no paint
        var nums = string.Join(" ", vals.ConvertAll(FmtNum));
        return vals.Count switch
        {
            1 => $"{nums} {(fill ? "g" : "G")}",
            3 => $"{nums} {(fill ? "rg" : "RG")}",
            4 => $"{nums} {(fill ? "k" : "K")}",
            _ => null,
        };
    }

    /// <summary>Move the field's first widget rectangle to the supplied point (top-left corner).</summary>
    public void SetPosition(Aspose.Pdf.Point point)
    {
        if (point is null) return;
        var rect = Rect;
        if (rect is null) return;
        var width = rect.URX - rect.LLX;
        var height = rect.URY - rect.LLY;
        // The point is the widget's LOWER-LEFT corner, not its upper-left: a field
        // 72 high moved to (225, 355) reports /Rect [225 355 525 427] from the
        // reference, so the box grows UPWARD from the point.
        var newRect = new PdfArray();
        newRect.Add(new PdfReal(point.X));
        newRect.Add(new PdfReal(point.Y));
        newRect.Add(new PdfReal(point.X + width));
        newRect.Add(new PdfReal(point.Y + height));
        Dict.Set("Rect", newRect);
    }
}
