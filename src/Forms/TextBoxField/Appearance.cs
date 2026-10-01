using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public partial class TextBoxField
{
    /// <summary>Background fill and border stroke drawn before the /Tx text block,
    /// from the widget's appearance characteristics: /MK/BG fills the whole BBox,
    /// /MK/BC strokes a rect inset by half the /BS border width (dashed when
    /// /BS/S is D, with the /BS/D pattern). Empty when the widget declares
    /// neither colour — painting nothing then.</summary>
    private string BuildBorderPrelude(double w, double h)
    {
        var mk = Reader.ResolveDict(Dict.Get("MK"));
        if (mk is null) return string.Empty;

        string RgbOf(PdfArray arr, string op)
        {
            double[] v = new double[arr.Count];
            for (int i = 0; i < arr.Count; i++) v[i] = Aspose.Pdf.Functions.PdfArrayHelper.GetDouble(arr, i);
            var (r, g, b) = arr.Count switch
            {
                1 => (v[0], v[0], v[0]),
                4 => ((1 - v[0]) * (1 - v[3]), (1 - v[1]) * (1 - v[3]), (1 - v[2]) * (1 - v[3])),
                _ => (v[0], v.Length > 1 ? v[1] : 0, v.Length > 2 ? v[2] : 0),
            };
            return $"{Format(r)} {Format(g)} {Format(b)} {op}";
        }

        var sb = new System.Text.StringBuilder();
        if (Reader.Resolve(mk.Get("BG")) is PdfArray bg && bg.Count > 0)
            sb.Append($"q\n{RgbOf(bg, "rg")}\n0 0 {Format(w)} {Format(h)} re\nf\nQ\n");

        if (Reader.Resolve(mk.Get("BC")) is PdfArray bc && bc.Count > 0)
        {
            // Border width from /BS/W (1 when the /BS omits it), dash from /BS/S==D.
            double bw = 1;
            string dash = "";
            if (Reader.ResolveDict(Dict.Get("BS")) is { } bs)
            {
                if (bs.ContainsKey("W")) bw = Aspose.Pdf.Functions.PdfArrayHelper.GetDoubleFromDict(bs, "W", 1);
                if (bs.GetName("S") == "D")
                {
                    dash = "[3] 0 d\n";
                    if (Reader.Resolve(bs.Get("D")) is PdfArray d && d.Count > 0)
                    {
                        var parts = new System.Collections.Generic.List<string>();
                        for (int i = 0; i < d.Count; i++) parts.Add(Format(Aspose.Pdf.Functions.PdfArrayHelper.GetDouble(d, i)));
                        dash = $"[{string.Join(" ", parts)}] 0 d\n";
                    }
                }
            }
            if (bw > 0)
                sb.Append($"q\n{RgbOf(bc, "RG")}\n{Format(bw)} w\n{dash}" +
                          $"{Format(bw / 2)} {Format(bw / 2)} {Format(w - bw)} {Format(h - bw)} re\nS\nQ\n");
        }
        return sb.ToString();
    }

    /// <summary>The widget's /MK /R appearance rotation (degrees counterclockwise),
    /// normalized and snapped to 0/90/180/270. Read from the specific widget kid when
    /// supplied, else from the field's own dict.</summary>
    private protected int AppearanceRotation(PdfDictionary? widgetDict = null)
    {
        var mk = Reader.ResolveDict((widgetDict ?? Dict).Get("MK"));
        if (mk is null) return 0;
        var r = Reader.Resolve(mk.Get("R")) switch
        {
            PdfInteger ri => (int)ri.Value,
            PdfReal rr => (int)rr.Value,
            _ => 0,
        };
        r %= 360;
        if (r < 0) r += 360;
        return r / 90 * 90;
    }

    /// <summary>Stamp the /Matrix implementing the /MK /R appearance rotation on a
    /// generated appearance form. The BBox is expected to already be the ROTATED
    /// layout box (width/height swapped for 90/270); the standard appearance-to-rect
    /// mapping (BBox transformed by Matrix → mapped onto /Rect) places it.</summary>
    private protected static void ApplyAppearanceRotation(PdfStream apStream, int rotation)
    {
        if (rotation is not (90 or 180 or 270)) return;
        double[] v = rotation switch
        {
            90 => [0, 1, -1, 0, 0, 0],
            180 => [-1, 0, 0, -1, 0, 0],
            _ => [0, -1, 1, 0, 0, 0],
        };
        var m = new PdfArray();
        foreach (var d in v) m.Add(new PdfReal(d));
        apStream.Dict.Set("Matrix", m);
    }

    private protected void RegenerateAppearance(string text)
    {
        var ap = new TextBoxAppearanceState();
        ResolveAppearanceInputs(ap, text);

        // /DA size 0 means auto-size: pick the largest size whose glyph run still fits.
        FitAppearanceFontSize(ap, text);

        if (ForceCombs && MaxLen > 0 && !IsMultiline)
        {
            ap.content = BuildCombAppearanceContent(text, ap.w, ap.h, ap.fontName, ap.fontSize);
        }
        else
        {
            // Multiline fields lay the value out top-down, one line per visual line, with a
            // fixed 1.2× line pitch; single-line fields vertical-centre on one baseline.
            if (IsMultiline)
            {
                BuildMultilineBody(ap, text);
            }
            else
            {
                BuildSingleLineBody(ap, text);
            }

            // The colour op precedes Tf — the writer sets the /DA colour
            // before selecting the font (a content parser that snapshots state per
            // Tf then sees the first run in the field colour).
            ap.content = BuildBorderPrelude(ap.w, ap.h) +
                      $"/Tx BMC\nq\nBT\n{ExtractDaColor(ap.da)}\n/{(ap.uniFontDict is not null ? ap.uniRes : ap.fontName)} {Format(ap.fontSize)} Tf\n" +
                      ap.textBody + "ET\nQ\nEMC\n";
        }
        WriteAppearanceStream(ap);
    }

    /// <summary>Resolve the field's effective /DA: its own, else the nearest /Parent that
    /// carries one, else the AcroForm-level /DA. Returns null when no /DA exists anywhere
    /// (the caller then applies a fixed default).</summary>
    private protected string? ResolveInheritedDa()
    {
        if (Dict.Get("DA") is PdfString own) return own.ToText();
        var node = Dict;
        for (int guard = 0; guard < 32; guard++)
        {
            var parent = Reader.ResolveDict(node.Get("Parent"));
            if (parent is null) break;
            if (parent.Get("DA") is PdfString pda) return pda.ToText();
            node = parent;
        }
        try
        {
            var acro = Reader.ResolveDict(Reader.Catalog.Get("AcroForm"));
            if (acro?.Get("DA") is PdfString ada) return ada.ToText();
        }
        catch { /* no catalog/AcroForm — fall through */ }
        return null;
    }

    /// <summary>Build the appearance content for a comb field (Ff bit 25): the value is
    /// laid out one character per equal-width cell. The widget
    /// is divided into <see cref="MaxLen"/> cells by vertical rules at full-width steps
    /// (w/MaxLen); the glyphs are centred in inner cells stepped by (w-2)/MaxLen (the 1-unit
    /// inset on each side). Each character is positioned with its own Td so the cell layout
    /// is exact regardless of glyph widths.</summary>
    private string BuildCombAppearanceContent(string text, double w, double h, string fontName, double fontSize)
        => BuildCombAppearanceContent(text, w, h, fontName, fontSize, ResolveCombBorderRgb(Dict));

    /// <summary>The widget's /MK /BC border colour as an RGB triple, or null when the field
    /// has no border characteristic (then the comb appearance draws no border or dividers).</summary>
    private double[]? ResolveCombBorderRgb(PdfDictionary? dict)
    {
        var mk = Reader.ResolveDict(dict?.Get("MK"));
        if (mk is null || Reader.Resolve(mk.Get("BC")) is not PdfArray bc || bc.Count == 0) return null;
        var c = new double[3];
        for (int i = 0; i < 3; i++)
        {
            var v = i < bc.Count ? AsNumber(Reader.Resolve(bc[i])) : (i == 0 ? 0 : (double?)null);
            c[i] = v ?? (bc.Count == 1 ? (AsNumber(Reader.Resolve(bc[0])) ?? 0) : 0);
        }
        return c;
    }

    /// <summary>Build a comb-field appearance (Ff bit 25): the value laid out one glyph per
    /// equal-width cell. A white background is
    /// filled first; when <paramref name="borderRgb"/> is set the widget is stroked and divided
    /// by vertical comb rules. Inside the /Tx marked content the box is re-filled, optionally
    /// re-bordered, clipped to the inner rect, and each glyph is centred in its cell — the
    /// inter-glyph Td is the inner cell width adjusted by half the advance difference of the two
    /// glyphs it spans, so a centred glyph run yields exact per-cell advances (for an
    /// equal-width run, e.g. digits, the adjustment is zero and the step is constant).</summary>
    private string BuildCombAppearanceContent(string text, double w, double h, string fontName,
        double fontSize, double[]? borderRgb)
    {
        int maxLen = MaxLen;
        if (maxLen <= 0) maxLen = System.Math.Max(1, text.Length);
        if (text.Length > maxLen) text = text.Substring(0, maxLen);

        double dividerStep = w / maxLen;        // full-width cells → divider rules
        double cellStep = (w - 2) / maxLen;     // inner cells (1-unit inset) → glyph stepping
        double inset = 1;
        bool bordered = borderRgb is not null;
        string GrayOrRgb(string op) => borderRgb is null ? "" :
            (borderRgb[0] == borderRgb[1] && borderRgb[1] == borderRgb[2]
                ? $"{Format(borderRgb[0])} {op.ToUpperInvariant()[0]}"            // gray shortcut
                : $"{Format(borderRgb[0])} {Format(borderRgb[1])} {Format(borderRgb[2])} {op}");

        var sb = new System.Text.StringBuilder();
        // White background fill (DeviceGray), full widget rect.
        sb.Append("1 g\n");
        sb.Append($"0 0 {Format(w)} {Format(h)} re\n");
        sb.Append("f\n");
        if (bordered)
        {
            // Outer border + comb divider rules (stroked, outside the marked content).
            sb.Append($"{GrayOrRgb("G")}\n");
            sb.Append($"0.5 0.5 {Format(w - 1)} {Format(h - 1)} re\n");
            sb.Append("s\n");
            for (int k = 1; k < maxLen; k++)
            {
                double x = k * dividerStep;
                sb.Append($"{Format(x)} {Format(h - 1)} m\n");
                sb.Append($"{Format(x)} 0.5 l\n");
            }
            sb.Append("s\n");
        }

        // Marked-content text: re-fill the box white, optionally re-border, clip to the inner
        // box, then centre each glyph in its cell.
        sb.Append("/Tx BMC\nq\nq\n");
        sb.Append("1 1 1 rg\n");
        sb.Append($"0 0 {Format(w)} {Format(h)} re\n");
        sb.Append("f\n");
        if (bordered)
        {
            sb.Append("q\n");
            sb.Append($"{GrayOrRgb("RG")}\n");
            sb.Append($"0.5 0.5 {Format(w - 1)} {Format(h - 1)} re\n");
            sb.Append("1 w\n");
            sb.Append("s\n");
            sb.Append("Q\n");
        }
        sb.Append("Q\n");
        sb.Append($"1 1 {Format(w - 2)} {Format(h - 2)} re\nW\nn\n");
        sb.Append("BT\n");
        sb.Append("0 0 0 rg\n");
        sb.Append($"/{fontName} {Format(fontSize)} Tf\n");
        double baselineY = h / 2 - fontSize * 0.3;
        // Comb alignment (/Q): a right/centre-justified comb packs the value into the
        // trailing cells, so the first glyph starts `startCell` cells in.
        int startCell = 0;
        int q = (int)Dict.GetInt("Q");
        if (q == 2) startCell = System.Math.Max(0, maxLen - text.Length);
        else if (q == 1) startCell = System.Math.Max(0, (maxLen - text.Length) / 2);

        if (text.Length > 0)
        {
            double w0 = GetGlyphWidthEm(text[0], fontName) * fontSize;
            // Baseline Td starts at the alignment column (0 for left); the following
            // two Tds apply the fixed inset and the intra-cell centring offset. For a
            // left comb startCell is 0, so this is byte-identical to the prior output.
            sb.Append($"{Format(startCell * cellStep)} {Format(baselineY)} Td\n");
            sb.Append($"{Format(inset)} 0 Td\n");
            sb.Append($"{Format((cellStep - w0) / 2 - inset)} 0 Td\n");
            sb.Append($"({EscapePdf(text[0].ToString())}) Tj\n");
            // Subsequent glyphs: one inner-cell width plus the half-difference of the two
            // glyphs' advances (centres each glyph in its own cell).
            for (int k = 1; k < text.Length; k++)
            {
                double wPrev = GetGlyphWidthEm(text[k - 1], fontName) * fontSize;
                double wCur = GetGlyphWidthEm(text[k], fontName) * fontSize;
                sb.Append($"{Format(cellStep + (wPrev - wCur) / 2)} 0 Td\n");
                sb.Append($"({EscapePdf(text[k].ToString())}) Tj\n");
            }
        }
        else
        {
            sb.Append($"0 {Format(baselineY)} Td\n");
        }
        sb.Append("ET\nQ\nEMC\n");
        return sb.ToString();
    }

    private static string EscapePdf(string s)
        => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    /// <summary>Register a font embedded for a value fill in the AcroForm /DR /Font
    /// under a fresh composite-style name (C{n}_0). The same font object stays
    /// referenced from the appearance's own resources; /DR carries it so
    /// document-level font enumeration reports the fill face. No-op without an
    /// AcroForm /DR /Font or when this exact font object is already registered.</summary>
    private void MirrorFillFontIntoDr(object? fontObj)
    {
        if (fontObj is not PdfDictionary font) return;
        PdfDictionary? acro;
        try { acro = Reader.ResolveDict(Reader.Catalog.Get("AcroForm")); }
        catch (InvalidOperationException) { return; }
        var dr = acro is null ? null : Reader.ResolveDict(acro.Get("DR"));
        var fontDict = dr is null ? null : Reader.ResolveDict(dr.Get("Font"));
        if (fontDict is null) return;
        var n = 0;
        foreach (var key in fontDict.Keys)
        {
            if (ReferenceEquals(Reader.ResolveDict(fontDict.Get(key)), font)) return;
            if (key.Length > 1 && key[0] == 'C' && key.Contains('_')) n++;
        }
        fontDict.Set($"C{n}_0", font);
    }

    /// <summary>The colour-set operator (<c>r g b rg</c> / <c>g g</c> / <c>c m y k k</c>) parsed
    /// out of a /DA string, so the appearance paints the field's configured text colour rather
    /// than always black. Falls back to <c>fallback</c> when /DA carries no colour.</summary>
    /// <summary>Build a value /AP dict for an extra widget rect of size w×h, rendering
    /// the field's current single-line value (used by multi-widget construction).</summary>
    internal override PdfDictionary? BuildWidgetApDict(double w, double h) => BuildWidgetApDict(w, h, null);

    /// <summary>As <see cref="BuildWidgetApDict(double,double)"/> but driven by a specific
    /// widget's own /DA (font, size and colour) when <paramref name="widgetDict"/> is supplied —
    /// so a multi-widget field renders each widget in its configured appearance.</summary>
    internal PdfDictionary? BuildWidgetApDict(double w, double h, PdfDictionary? widgetDict)
    {
        var wa = new WidgetApState();
        wa.w = w;
        wa.h = h;
        wa.widgetDict = widgetDict;
        wa.text = FieldFormatScript.Apply(Dict, Reader, Value ?? string.Empty);

        wa.apRotation = AppearanceRotation(wa.widgetDict);
        if (wa.apRotation is 90 or 270) (wa.w, wa.h) = (wa.h, wa.w);

        wa.daSrc = (wa.widgetDict?.Get("DA") as PdfString) ?? (Dict.Get("DA") as PdfString);
        wa.da = wa.daSrc is not null ? wa.daSrc.ToText() : "/Helv 12 Tf 0 g";
        wa.fontName = "Helv";
        wa.fontSize = 12;
        wa.daParts = wa.da.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < wa.daParts.Length; i++)
            if (wa.daParts[i] == "Tf" && i >= 2)
            {
                wa.fontName = wa.daParts[i - 2].TrimStart('/');
                double.TryParse(wa.daParts[i - 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out wa.fontSize);
            }
        if (wa.fontSize <= 0)
            wa.fontSize = System.Math.Max(4, System.Math.Min((wa.w - 4) / (System.Math.Max(1, wa.text.Length) * 0.5), wa.h * 0.83));

        wa.widgetNeedsUni = false;
        foreach (var ch in wa.text) if (ch > 'ÿ') { wa.widgetNeedsUni = true; break; }
        wa.wuTtf = null;
        wa.wuFam = "";
        wa.wuFonts = null;
        wa.wuRes = "";
        if (wa.widgetNeedsUni)
        {
            ResolveUnicodeWidgetFont(wa);
        }

        if (ForceCombs && MaxLen > 0 && !IsMultiline)
        {
            // Comb widget: lay the value out one glyph per cell, taking the border colour
            // from this widget's own /MK /BC (a per-widget characteristic).
            wa.content = BuildCombAppearanceContent(wa.text, wa.w, wa.h, wa.fontName, wa.fontSize,
                ResolveCombBorderRgb(wa.widgetDict ?? Dict));
        }
        else
        {
            BuildPlainWidgetContent(wa);
        }
        _uniAppearanceTtf = null;
        wa.apStream = new PdfStream(new PdfDictionary(), Aspose.Pdf.Text.Cp1252.GetBytes(wa.content));
        wa.apStream.Dict.Set("Type", new PdfName("XObject"));
        wa.apStream.Dict.Set("Subtype", new PdfName("Form"));
        wa.bbox = new PdfArray();
        wa.bbox.Add(new PdfReal(0)); wa.bbox.Add(new PdfReal(0)); wa.bbox.Add(new PdfReal(wa.w)); wa.bbox.Add(new PdfReal(wa.h));
        wa.apStream.Dict.Set("BBox", wa.bbox);
        ApplyAppearanceRotation(wa.apStream, wa.apRotation);
        if (wa.wuFonts is not null)
        {
            var wuResDict = new PdfDictionary();
            wuResDict.Set("Font", wa.wuFonts);
            wa.apStream.Dict.Set("Resources", wuResDict);
        }
        else
        {
            // Same rule as RegenerateAppearance: a simple TrueType /DR font is carried
            // verbatim so the widget draws the real face instead of a substituted one.
            PdfDictionary? widgetApFont = null;
            if (ResolveDrFontDict(wa.fontName) is { } drTt && drTt.GetName("Subtype") == "TrueType")
                widgetApFont = drTt;
            wa.apStream.Dict.Set("Resources", BuildTextAppearanceResources(wa.fontName, null, widgetApFont));
        }

        wa.apDict = new PdfDictionary();
        wa.apDict.Set("N", wa.apStream);
        return wa.apDict;
    }

    /// <summary>Build the appearance for the current value when none exists yet,
    /// so a freshly-added text field renders its (possibly empty) value box.</summary>
    internal override void GenerateAppearance()
    {
        // Keep an appearance loaded from the source document untouched, but rebuild
        // one this session generated itself (e.g. by the Value setter): properties
        // set after Value — Multiline, TextVerticalAlignment, Border, /MK colours —
        // must be reflected when the field is finally added to the form.
        if (!_apAutoGenerated && Reader.ResolveDict(Dict.Get("AP")) is not null) return;
        var displayValue = FieldFormatScript.Apply(Dict, Reader, Value ?? "");
        RegenerateAppearance(displayValue);
    }

    /// <summary>Resolve the typographic descender (signed em ratio) used to place
    /// the first multiline baseline. Prefers the font embedded via the field's
    /// DefaultAppearance (its /DA name may already be an embedded-resource alias
    /// that no longer resolves by family name); otherwise loads the named system
    /// face; falls back to a typical Latin descent.</summary>
    /// <summary>The face PROGRAM the /DA font carries in the form resources, or null
    /// when the /DA names a font instead of shipping one (Standard-14, a system family).
    /// A shipped face is one the caller opened and handed to the field, and its
    /// appearance is paced differently from a named one. Reads through a composite
    /// (/Type0) wrapper to its descendant, which is where an authored face lands.</summary>
    private byte[]? DrEmbeddedFaceProgram(string fontName)
    {
        try
        {
            var dr = ResolveDrFontDict(fontName);
            if (dr is null) return null;
            var host = dr;
            if (dr.GetName("Subtype") == "Type0")
            {
                var desc = Reader.Resolve(dr.Get("DescendantFonts")) as PdfArray;
                host = desc is { Count: > 0 } ? Reader.ResolveDict(desc[0]) : null;
                if (host is null) return null;
            }
            var fd = Reader.ResolveDict(host.Get("FontDescriptor"));
            var ff2 = fd is null ? null : Reader.ResolveStream(fd.Get("FontFile2"));
            if (ff2 is null) return null;
            var program = Reader.DecodeStream(ff2);
            return program is { Length: > 0 } ? program : null;
        }
        catch { return null; }
    }

    /// <summary>The widget border the appearance keeps clear at the top and bottom of
    /// its box, in points — the inner box is H - 2.</summary>
    private const double WidgetBorderInset = 2.0;
}
