using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class TextBuilder
{
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
    /// Register an embedded TrueType font in the page resources.
    /// Creates font dict, font descriptor with FontFile2, and glyph widths.
    /// Returns the resource name.
    /// </summary>
    private static bool RefuseUnlicensedEmbedding(FontData fontData, Page? page)
        => FontData.RefuseEmbedding(fontData, page?.Reader?.OwnerDocument);

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

    private static string GenerateSubsetTag()
    {
        var random = new Random();
        var chars = new char[6];
        for (int i = 0; i < 6; i++)
            chars[i] = (char)('A' + random.Next(26));
        return new string(chars);
    }

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
