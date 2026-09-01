using System.IO;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;

namespace Aspose.Pdf.Facades;

public sealed partial class FormEditor
{
    /// <summary>Apply <see cref="Facade"/> to every field in the document.</summary>
    public void DecorateField()
    {
        if (_document?.Form is null) return;
        foreach (var f in _document.Form.Fields)
            ApplyFacade(f);
    }

    /// <summary>Apply <see cref="Facade"/> to every field of the given type.</summary>
    public void DecorateField(FieldType fieldType)
    {
        if (_document?.Form is null) return;
        foreach (var f in _document.Form.Fields)
        {
            if (MapToFacadeType(f.Type) == fieldType)
                ApplyFacade(f);
        }
    }

    /// <summary>Apply <see cref="Facade"/> to a single named field.</summary>
    public void DecorateField(string fieldName)
    {
        if (_document?.Form is null) return;
        var f = _document.Form.FindFieldOrNull(fieldName);
        if (f is not null) ApplyFacade(f);
    }

    // Standard AcroForm /DA font abbreviations (PDF spec, the base-14 set) that
    // are always valid even though FontRepository keys on full family names.
    private static readonly System.Collections.Generic.HashSet<string> StandardDaFontAbbreviations = new(StringComparer.Ordinal)
    {
        "Helv", "HeBo", "HeOb", "HeBO", "Cour", "CoBo", "CoOb", "CoBO",
        "TiRo", "TiBo", "TiIt", "TiBI", "Symb", "ZaDb",
    };

    private static bool IsLoadableFormFont(string fontName)
        => StandardDaFontAbbreviations.Contains(fontName)
           || Aspose.Pdf.Text.FontRepository.TryFindFont(fontName) is not null;

    private void ApplyFacade(Field field)
    {
        // A custom font must be resolvable: a standard PDF /DA font abbreviation (Helv,
        // ZaDb, Symb, …), a Standard-14 face, or a system font found via FontRepository.
        // Otherwise fail loudly — DecorateField throws rather than writing a /DA referencing
        // a missing font. (A field whose existing /DA names ZapfDingbats — the checkbox
        // glyph font — must not trip this guard.)
        if (!string.IsNullOrEmpty(Facade.CustomFont) && !IsLoadableFormFont(Facade.CustomFont!))
        {
            throw new ArgumentException("Could not load specified font : " + Facade.CustomFont);
        }

        // Active implementation: rewrite the field's /DA when the facade sets a font or size.
        // Color/alignment changes are recorded on the field's /MK dict but not currently
        // re-emitted into the appearance stream.
        if (Facade.FontSize > 0 || !string.IsNullOrEmpty(Facade.CustomFont)
            || Facade.Font != FontStyle.Helvetica)
        {
            // A caller-specified CustomFont must be loadable: a standard PDF font
            // abbreviation, or a font name FontRepository can resolve (Standard-14,
            // a registered source, or a host system font). An unknown family is an
            // error rather than a silent fall-through to the default font.
            if (!string.IsNullOrEmpty(Facade.CustomFont) && !IsLoadableFormFont(Facade.CustomFont!))
                throw new ArgumentException($"Could not load specified font : {Facade.CustomFont}");
            string fontName;
            if (!string.IsNullOrEmpty(Facade.CustomFont))
            {
                fontName = Facade.CustomFont!;
            }
            else if (Facade.Font == FontStyle.CjkFont)
            {
                // The CJK facade font is a real embeddable face (Bitstream CyberCJK):
                // route through the DefaultAppearance setter so the composite font is
                // embedded into /DR and the /DA re-pointed at it.
                var cjk = Aspose.Pdf.Text.FontRepository.TryFindFont("BitstreamCyberCJK");
                if (cjk is null)
                    throw new ArgumentException("Could not load specified font : BitstreamCyberCJK");
                cjk.IsEmbedded = true; // composite face goes into the form's /DR
                var tcCjk = Facade.TextColor;
                var cjkColor = tcCjk.A != 0 ? tcCjk : System.Drawing.Color.Black;
                field.DefaultAppearance = new Aspose.Pdf.Annotations.DefaultAppearance(
                    cjk, Facade.FontSize > 0 ? Facade.FontSize : 12, cjkColor);
                goto daDone;
            }
            else
            {
                // Facade.Font (the base-14 enum) maps to a standard /DA
                // abbreviation registered in the AcroForm /DR so the /DA resolves.
                var (abbr, baseFont, subtype) = DaFontFor(Facade.Font);
                fontName = abbr;
                _document?.Form?.RegisterDefaultResourceFont(abbr, baseFont, subtype);
            }
            var fontSize = Facade.FontSize > 0 ? Facade.FontSize : 0f;
            // The facade text colour rides in the /DA operation (yellow → "1 1 0 rg").
            var tc = Facade.TextColor;
            var colorOp = tc.A != 0
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.###} {1:0.###} {2:0.###} rg", tc.R / 255.0, tc.G / 255.0, tc.B / 255.0)
                : "0 g";
            var da = $"/{fontName} {fontSize.ToString("G", System.Globalization.CultureInfo.InvariantCulture)} Tf {colorOp}";
            field.Dict.Set("DA", new PdfString(System.Text.Encoding.UTF8.GetBytes(da)));
            // A field that already carries a value keeps showing the OLD font until
            // its appearance is rebuilt — regenerate it under the new /DA.
            if (field.Type is Forms.FieldType.Text or Forms.FieldType.Choice
                or Forms.FieldType.ComboBox or Forms.FieldType.ListBox)
            {
                field.ResetDefaultAppearanceCache();
                field.Dict.Remove("AP");
                foreach (var kid in field.AllKids())
                    kid.Remove("AP");
                field.GenerateAppearance();
            }
        daDone: ;
        }

        // Border style/width → /BS on each widget (WidgetAnnotation.Border resolves
        // Style from /BS /S). Written on the kid widgets so per-widget Border surfaces it.
        if (Facade.BorderStyle != FormFieldFacade.BorderStyleUndefined || Facade.BorderWidth > 0)
            ApplyBorderStyle(field);

        // Re-emit a checkbox's on/off appearance from the facade (background, border,
        // glyph colour and style) when the facade carries any visible decoration —
        // a background/border/text colour, a border width, or an explicit button style.
        if (field.Type == Forms.FieldType.CheckBox &&
            (Facade.BackgroundColor.A != 0 || Facade.BorderColor.A != 0 || Facade.TextColor.A != 0
             || Facade.BorderWidth > 0 || Facade.ButtonStyle != FormFieldFacade.CheckBoxStyleUndefined))
            DecorateCheckBox(field);

        // A text / choice field's facade border colour is recorded on /MK /BC and drawn
        // into the widget appearance — the value text the appearance already carries is
        // kept; a stroked rectangle at the facade width is appended so the decorated
        // border renders (checkboxes regenerate their whole face above instead).
        else if (field.Type is Forms.FieldType.Text or Forms.FieldType.Choice
                 or Forms.FieldType.ComboBox or Forms.FieldType.ListBox
                 && (Facade.BorderColor.A != 0 || Facade.BorderWidth > 0))
            DecorateTextBorder(field);

        // A decorated push button gets its face REBUILT: background fill (/MK /BG,
        // default button grey) plus the facade border — the caption is dropped, as
        // a decorated-button face carries no caption text.
        else if (field.Type == Forms.FieldType.Button
                 && (Facade.BorderColor.A != 0 || Facade.BorderWidth > 0))
            DecorateButton(field);
    }

    /// <summary>Rebuild a push button's /AP /N as a caption-less decorated face:
    /// the widget's /MK /BG background (default button grey) filled, then the facade
    /// border stroked at the facade width. /MK /BC records the border colour.</summary>
    private void DecorateButton(Field field)
    {
        if (_document is null) return;
        var color = Facade.BorderColor.A != 0 ? Facade.BorderColor : System.Drawing.Color.Black;
        int bw = Facade.BorderWidth > 0 ? (int)Facade.BorderWidth : 1;
        var widgets = new System.Collections.Generic.List<PdfDictionary>(field.AllKids());
        if (widgets.Count == 0) widgets.Add(field.Dict);

        foreach (var widget in widgets)
        {
            if (_document.Reader.Resolve(widget.Get("Rect")) is not PdfArray ra || ra.Count < 4) continue;
            var rect = Rectangle.FromPdfArray(ra);
            double w = rect.Width, h = rect.Height;
            if (w <= 0 || h <= 0) continue;
            WriteMk(widget, "BC", color);

            // Background: the widget's own /MK /BG, defaulting to the standard grey chrome.
            double bgR = 0.75, bgG = 0.75, bgB = 0.75;
            var mk = _document.Reader.ResolveDict(widget.Get("MK"));
            if (mk is not null && _document.Reader.Resolve(mk.Get("BG")) is PdfArray bg && bg.Count >= 1)
            {
                double C(int i) => _document.Reader.Resolve(bg[i]) switch
                {
                    PdfReal r => r.Value,
                    PdfInteger n => n.Value,
                    _ => 0,
                };
                if (bg.Count >= 3) { bgR = C(0); bgG = C(1); bgB = C(2); }
                else { bgR = bgG = bgB = C(0); }
            }

            double hbw = bw / 2.0;
            var sb = new System.Text.StringBuilder();
            sb.Append("q\n");
            sb.Append($"{Num(bgR)} {Num(bgG)} {Num(bgB)} rg\n");
            sb.Append($"0 0 {Num(w)} {Num(h)} re\nf\n");
            sb.Append($"{Col(color.R)} {Col(color.G)} {Col(color.B)} RG\n");
            sb.Append($"{Num(bw)} w\n");
            sb.Append($"{Num(hbw)} {Num(hbw)} {Num(w - bw)} {Num(h - bw)} re\n");
            sb.Append("S\n");
            sb.Append("Q\n");
            var faceBytes = System.Text.Encoding.Latin1.GetBytes(sb.ToString());

            var apDict = new PdfDictionary();
            apDict.Set("Type", new PdfName("XObject"));
            apDict.Set("Subtype", new PdfName("Form"));
            var bbox = new PdfArray();
            bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
            bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
            apDict.Set("BBox", bbox);
            apDict.Set("Length", new PdfInteger(faceBytes.Length));
            var newAp = new PdfDictionary();
            newAp.Set("N", new PdfStream(apDict, faceBytes));
            widget.Set("AP", newAp);
        }
    }

    /// <summary>Record the facade border colour on each of a non-button field's widgets
    /// (/MK /BC) and append a stroked border rectangle to the widget's existing /AP /N so
    /// the decorated border renders without disturbing the value text already drawn there.</summary>
    private void DecorateTextBorder(Field field)
    {
        if (_document is null) return;
        var color = Facade.BorderColor.A != 0 ? Facade.BorderColor : System.Drawing.Color.Black;
        int bw = Facade.BorderWidth > 0 ? (int)Facade.BorderWidth : 1;
        var widgets = new System.Collections.Generic.List<PdfDictionary>(field.AllKids());
        if (widgets.Count == 0) widgets.Add(field.Dict);

        foreach (var widget in widgets)
        {
            if (_document.Reader.Resolve(widget.Get("Rect")) is not PdfArray ra || ra.Count < 4) continue;
            var rect = Rectangle.FromPdfArray(ra);
            double w = rect.Width, h = rect.Height;
            if (w <= 0 || h <= 0) continue;
            WriteMk(widget, "BC", color);

            double hbw = bw / 2.0;
            var sb = new System.Text.StringBuilder();
            sb.Append("\nq\n");
            sb.Append($"{Col(color.R)} {Col(color.G)} {Col(color.B)} RG\n");
            sb.Append($"{Num(bw)} w\n");
            sb.Append($"{Num(hbw)} {Num(hbw)} {Num(w - bw)} {Num(h - bw)} re\n");
            sb.Append("S\n");
            sb.Append("Q\n");
            var borderBytes = System.Text.Encoding.Latin1.GetBytes(sb.ToString());

            var ap = _document.Reader.ResolveDict(widget.Get("AP"));
            var n = ap is null ? null : _document.Reader.ResolveStream(ap.Get("N"));
            if (n is not null)
            {
                var existing = _document.Reader.DecodeStream(n);
                var combined = new byte[existing.Length + borderBytes.Length];
                System.Array.Copy(existing, combined, existing.Length);
                System.Array.Copy(borderBytes, 0, combined, existing.Length, borderBytes.Length);
                n.ReplaceData(combined);
                n.Dict.Remove("Filter");
                n.Dict.Set("Length", new PdfInteger(combined.Length));
            }
            else
            {
                var apDict = new PdfDictionary();
                apDict.Set("Type", new PdfName("XObject"));
                apDict.Set("Subtype", new PdfName("Form"));
                var bbox = new PdfArray();
                bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
                bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
                apDict.Set("BBox", bbox);
                apDict.Set("Length", new PdfInteger(borderBytes.Length));
                var newAp = new PdfDictionary();
                newAp.Set("N", new PdfStream(apDict, borderBytes));
                widget.Set("AP", newAp);
            }
        }
    }

    /// <summary>Regenerate a checkbox's /AP /N and /AP /D appearances from the facade:
    /// a filled background, a stroked border at the facade width, and the on-state glyph
    /// (ZapfDingbats) in the facade text colour. The operator order matches the standard
    /// decorated-checkbox face.</summary>
    private void DecorateCheckBox(Field field, bool useCaptionGlyph = false)
    {
        if (_document is null) return;
        var widgets = new System.Collections.Generic.List<PdfDictionary>(field.AllKids());
        if (widgets.Count == 0) widgets.Add(field.Dict);

        // The "Cross" style and the undefined default both draw the stroked-X mark;
        // the other styles draw a ZapfDingbats glyph.
        int style = Facade.ButtonStyle == FormFieldFacade.CheckBoxStyleUndefined
            ? FormFieldFacade.CheckBoxStyleCross : Facade.ButtonStyle;
        // An add-time face keeps the legacy minimum-1 border; a decorated existing
        // checkbox takes the facade width verbatim — an explicit 0 draws NO border
        // stroke (only the border colour selection remains in the stream).
        // An add-time face takes the legacy minimum-1 border. A decorated existing
        // checkbox uses the facade width when it sets one; an unset facade width
        // (BorderWidthUndefined) falls back to the WIDGET's own declared /BS /W,
        // and only a widget that itself declares zero draws no border stroke.
        double facadeBw = Facade.BorderWidth > 0 ? Facade.BorderWidth : -1;
        bool dashed = Facade.BorderStyle == FormFieldFacade.BorderStyleDashed;

        foreach (var widget in widgets)
        {
            if (_document.Reader.Resolve(widget.Get("Rect")) is not PdfArray ra || ra.Count < 4) continue;
            var rect = Rectangle.FromPdfArray(ra);
            double w = rect.Width, h = rect.Height;
            if (w <= 0 || h <= 0) continue;

            var bw = facadeBw >= 0 ? facadeBw : WidgetBorderWidth(widget);
            if (useCaptionGlyph && bw <= 0) bw = 1;

            // Preserve the existing on-state name (the non-Off key in /AP /N), falling
            // back to /AS, then "On".
            var onName = "On";
            if (_document.Reader.ResolveDict(widget.Get("AP")) is { } apOld &&
                _document.Reader.ResolveDict(apOld.Get("N")) is { } nOld)
            {
                foreach (var k in nOld.Keys) if (k != "Off") { onName = k; break; }
            }
            else if (widget.GetName("AS") is { } asn && asn != "Off") onName = asn;

            // Resolve colours: an explicitly-set facade colour wins, otherwise an unset
            // colour preserves the widget's existing /MK value (so e.g. setting only the
            // border colour keeps the original background), otherwise a sensible default.
            var bg = Facade.BackgroundColor.A != 0 ? Facade.BackgroundColor
                : (ReadMkColor(widget, "BG") ?? System.Drawing.Color.White);
            var border = Facade.BorderColor.A != 0 ? Facade.BorderColor
                : (ReadMkColor(widget, "BC") ?? System.Drawing.Color.Black);
            var text = Facade.TextColor.A != 0 ? Facade.TextColor : System.Drawing.Color.Black;

            char glyph = useCaptionGlyph ? CaptionCharFor(style) : (char)style;
            // A decorated existing checkbox keeps the caption glyph its widget already
            // declares (/MK /CA) — but only while the facade leaves the button style
            // unset; an explicitly chosen style replaces the authored mark.
            bool glyphFromCaption = false;
            if (!useCaptionGlyph &&
                Facade.ButtonStyle == FormFieldFacade.CheckBoxStyleUndefined &&
                _document.Reader.ResolveDict(widget.Get("MK")) is { } mkOld &&
                _document.Reader.Resolve(mkOld.Get("CA")) is PdfString caOld &&
                caOld.Value is { Length: > 0 } caBytes)
            {
                glyph = (char)caBytes[0];
                glyphFromCaption = true;
            }
            var n = new PdfDictionary();
            n.Set(onName, BuildCheckBoxFace(w, h, bw, style, glyph, bg, border, text, withMark: true, dashed, glyphFromCaption));
            n.Set("Off", BuildCheckBoxFace(w, h, bw, style, glyph, bg, border, text, withMark: false, dashed, glyphFromCaption));
            var d = new PdfDictionary();
            d.Set(onName, BuildCheckBoxFace(w, h, bw, style, glyph, bg, border, text, withMark: true, dashed, glyphFromCaption));
            d.Set("Off", BuildCheckBoxFace(w, h, bw, style, glyph, bg, border, text, withMark: false, dashed, glyphFromCaption));
            var ap = new PdfDictionary();
            ap.Set("N", n);
            ap.Set("D", d);
            widget.Set("AP", ap);
            if (useCaptionGlyph)
            {
                // The add-time face records its caption on /MK /CA so the loaded field
                // reports NormalCaption and derives its BoxStyle from it.
                if (_document.Reader.ResolveDict(widget.Get("MK")) is not { } mkCa)
                {
                    mkCa = new PdfDictionary();
                    widget.Set("MK", mkCa);
                }
                mkCa.Set("CA", new PdfString(new[] { (byte)glyph }));
            }

            // Record the decoration on /MK (/BG background, /BC border) and the widget
            // /C (the glyph/text colour) so the loaded field surfaces them via
            // Characteristics.Background/Border and Field.Color.
            WriteMk(widget, "BG", bg);
            WriteMk(widget, "BC", border);
            WriteWidgetColor(widget, text);
        }
    }

    /// <summary>Build a decorated-checkbox appearance face: a filled background, a
    /// stroked border at the facade width, and (for the on state) the mark — a stroked
    /// diagonal "X" for the Cross style, otherwise a ZapfDingbats glyph. The operator
    /// order is fixed: background, border, then mark.</summary>
    /// <summary>The standard checkbox caption character for a facade button style —
    /// the ZapfDingbats code every viewer maps to the mark ("4" check, "8" cross, …).</summary>
    private static char CaptionCharFor(int style) => style switch
    {
        FormFieldFacade.CheckBoxStyleCheck => '4',
        FormFieldFacade.CheckBoxStyleCircle => 'l',
        FormFieldFacade.CheckBoxStyleCross => '8',
        FormFieldFacade.CheckBoxStyleDiamond => 'u',
        FormFieldFacade.CheckBoxStyleSquare => 'n',
        FormFieldFacade.CheckBoxStyleStar => 'H',
        _ => '4',
    };

    // The mark glyph's design box is treated as a fixed 2/3-em square: the font size
    // fills the border-inset extent and the pen centres that square in the widget box.
    private const double CheckMarkEmFraction = 2.0 / 3.0;

    private PdfStream BuildCheckBoxFace(double w, double h, double bw, int style, char glyph,
        System.Drawing.Color bg, System.Drawing.Color border, System.Drawing.Color text, bool withMark,
        bool dashed = false, bool glyphFromCaption = false)
    {
        double hbw = bw / 2.0;
        // The cross mark is drawn as stroked diagonals, every other style as its
        // ZapfDingbats glyph — including when the style comes from the widget's own
        // caption (a caption of the cross character still draws the diagonals).
        bool isCross = glyphFromCaption
            ? glyph == CaptionCharFor(FormFieldFacade.CheckBoxStyleCross)
            : style == FormFieldFacade.CheckBoxStyleCross;
        var sb = new System.Text.StringBuilder();
        sb.Append("q\n");
        sb.Append($"{Col(bg.R)} {Col(bg.G)} {Col(bg.B)} rg\n");
        sb.Append($"0 0 {Num(w)} {Num(h)} re\n");
        sb.Append("f\n");
        sb.Append("q\n");
        sb.Append($"{Col(border.R)} {Col(border.G)} {Col(border.B)} RG\n");
        // A zero border width selects the stroke colour but draws nothing.
        if (bw > 0)
        {
            sb.Append($"{Num(hbw)} {Num(hbw)} {Num(w - bw)} {Num(h - bw)} re\n");
            sb.Append($"{Num(bw)} w\n");
            if (dashed) sb.Append("[3 3] 0 d\n");
            sb.Append("s\n");
        }
        sb.Append("Q\n");
        sb.Append("Q\n");
        // Both states clip to the border-inset box; the off state closes with an
        // empty save/restore where the on state draws its mark.
        sb.Append($"{Num(bw)} {Num(bw)} {Num(w - 2 * bw)} {Num(h - 2 * bw)} re\n");
        sb.Append("W\n");
        sb.Append("n\n");
        sb.Append("q\n");
        if (withMark)
        {
            if (isCross)
            {
                // Two stroked diagonals from (2bw,2bw)→(w-2bw,h-2bw) and (w-2bw,2bw)→(2bw,h-2bw).
                double lo = 2 * bw, hix = w - 2 * bw, hiy = h - 2 * bw;
                sb.Append($"{Col(text.R)} {Col(text.G)} {Col(text.B)} RG\n");
                sb.Append($"{Num(lo)} {Num(lo)} m\n");
                sb.Append($"{Num(hix)} {Num(hiy)} l\n");
                sb.Append("S\n");
                sb.Append($"{Num(hix)} {Num(lo)} m\n");
                sb.Append($"{Num(lo)} {Num(hiy)} l\n");
                sb.Append("S\n");
            }
            else
            {
                // ZapfDingbats glyph centred in the widget box: the size fills the
                // border-inset extent of the SHORTER side, and the pen seats the fixed
                // 2/3-em glyph square centred on each axis independently.
                double fontSize = Math.Min(w, h) - 2 * bw;
                double tdX = (w - fontSize * CheckMarkEmFraction) / 2.0;
                double tdY = (h - fontSize * CheckMarkEmFraction) / 2.0;
                sb.Append($"{Col(text.R)} {Col(text.G)} {Col(text.B)} rg\n");
                sb.Append("BT\n");
                sb.Append($"{Td5(tdX)} {Td5(tdY)} Td\n");
                sb.Append($"/ZaDb {fontSize.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)} Tf\n");
                // Glyph keyed by the caller's character (the /Encoding /Differences maps a
                // style-value key to the dingbat; a caption char resolves via the base
                // encoding). Emit the raw byte so the round-tripped text length is 1.
                sb.Append($"({glyph}) Tj\n");
                sb.Append("ET\n");
            }
        }
        sb.Append("Q\n");
        var bytes = System.Text.Encoding.Latin1.GetBytes(sb.ToString());

        var apDict = new PdfDictionary();
        apDict.Set("Type", new PdfName("XObject"));
        apDict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        apDict.Set("BBox", bbox);
        apDict.Set("Length", new PdfInteger(bytes.Length));
        var zadb = new PdfDictionary();
        zadb.Set("Type", new PdfName("Font"));
        zadb.Set("Subtype", new PdfName("Type1"));
        zadb.Set("BaseFont", new PdfName("ZapfDingbats"));
        // Map the style char codes (1..6) to the corresponding ZapfDingbats glyph names
        // so the mark renders even though it is keyed by the style value.
        var enc = new PdfDictionary();
        enc.Set("Type", new PdfName("Encoding"));
        var diff = new PdfArray();
        diff.Add(new PdfInteger(FormFieldFacade.CheckBoxStyleCheck)); diff.Add(new PdfName("a20"));
        diff.Add(new PdfInteger(FormFieldFacade.CheckBoxStyleCircle)); diff.Add(new PdfName("a71"));
        diff.Add(new PdfInteger(FormFieldFacade.CheckBoxStyleDiamond)); diff.Add(new PdfName("a73"));
        diff.Add(new PdfInteger(FormFieldFacade.CheckBoxStyleSquare)); diff.Add(new PdfName("a72"));
        diff.Add(new PdfInteger(FormFieldFacade.CheckBoxStyleStar)); diff.Add(new PdfName("a35"));
        enc.Set("Differences", diff);
        zadb.Set("Encoding", enc);
        var fonts = new PdfDictionary();
        fonts.Set("ZaDb", zadb);
        var resources = new PdfDictionary();
        resources.Set("Font", fonts);
        apDict.Set("Resources", resources);
        return new PdfStream(apDict, bytes);
    }

    /// <summary>The widget's own declared border width (/BS /W), defaulting to the
    /// PDF default of 1 when it declares none (PDF 32000 §12.5.4, Table 166).</summary>
    private double WidgetBorderWidth(PdfDictionary widget)
    {
        if (_document is null) return 1;
        if (_document.Reader.ResolveDict(widget.Get("BS")) is { } bs
            && _document.Reader.Resolve(bs.Get("W")) is { } wObj)
        {
            return wObj switch
            {
                PdfInteger wi => wi.Value,
                PdfReal wr => wr.Value,
                _ => 1,
            };
        }
        return 1;
    }

    /// <summary>Read a /MK colour entry (/BG or /BC) as a System.Drawing.Color, or null
    /// when absent/empty (a 0-length array = transparent/no colour).</summary>
    private System.Drawing.Color? ReadMkColor(PdfDictionary widget, string key)
    {
        if (_document is null) return null;
        var mk = _document.Reader.ResolveDict(widget.Get("MK"));
        if (mk is null) return null;
        if (_document.Reader.Resolve(mk.Get(key)) is not PdfArray arr || arr.Count == 0) return null;
        double[] c = new double[arr.Count];
        for (int i = 0; i < arr.Count; i++)
            c[i] = arr[i] switch { PdfReal r => r.Value, PdfInteger n => n.Value, _ => 0.0 };
        return c.Length switch
        {
            1 => GrayToColor(c[0]),
            3 => System.Drawing.Color.FromArgb(To255(c[0]), To255(c[1]), To255(c[2])),
            4 => CmykToColor(c[0], c[1], c[2], c[3]),
            _ => (System.Drawing.Color?)null,
        };
    }

    /// <summary>Write a /MK colour entry (/BG or /BC) as an RGB array.</summary>
    private void WriteMk(PdfDictionary widget, string key, System.Drawing.Color color)
    {
        if (_document is null) return;
        var mk = _document.Reader.ResolveDict(widget.Get("MK"));
        if (mk is null) { mk = new PdfDictionary(); widget.Set("MK", mk); }
        var arr = new PdfArray();
        arr.Add(new PdfReal(color.R / 255.0));
        arr.Add(new PdfReal(color.G / 255.0));
        arr.Add(new PdfReal(color.B / 255.0));
        mk.Set(key, arr);
    }

    /// <summary>Write the widget's /C (annotation colour) as an RGB array — DecorateField
    /// records the glyph/text colour here so Field.Color surfaces it after a reload.</summary>
    private static void WriteWidgetColor(PdfDictionary widget, System.Drawing.Color color)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(color.R / 255.0));
        arr.Add(new PdfReal(color.G / 255.0));
        arr.Add(new PdfReal(color.B / 255.0));
        widget.Set("C", arr);
    }

    private static int To255(double v) => (int)System.Math.Round(System.Math.Clamp(v, 0, 1) * 255);

    private static System.Drawing.Color GrayToColor(double g) { int v = To255(g); return System.Drawing.Color.FromArgb(v, v, v); }

    private static System.Drawing.Color CmykToColor(double c, double m, double y, double k)
        => System.Drawing.Color.FromArgb(To255((1 - c) * (1 - k)), To255((1 - m) * (1 - k)), To255((1 - y) * (1 - k)));

    private static string Col(byte b) => Num(b / 255.0);

    private static string Num(double v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private static string Td5(double v) => v.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Write the facade's border style/width as a /BS dictionary on the
    /// field's widget annotations (kids, or the field dict itself when it is a
    /// single-widget leaf).</summary>
    private void ApplyBorderStyle(Field field)
    {
        var style = Facade.BorderStyle switch
        {
            FormFieldFacade.BorderStyleDashed => "D",
            FormFieldFacade.BorderStyleBeveled => "B",
            FormFieldFacade.BorderStyleInset => "I",
            FormFieldFacade.BorderStyleUnderline => "U",
            _ => "S",
        };
        var targets = new System.Collections.Generic.List<PdfDictionary>();
        foreach (var kid in field.AllKids()) targets.Add(kid);
        if (targets.Count == 0) targets.Add(field.Dict);

        foreach (var t in targets)
        {
            // A facade that sets no width keeps the widget's own declared width —
            // inventing the default 1 would turn an authored borderless widget
            // (/BS /W 0) into a stroked one.
            var width = Facade.BorderWidth > 0 ? (int)Facade.BorderWidth : (int)WidgetBorderWidth(t);
            var bs = new PdfDictionary();
            bs.Set("Type", new PdfName("Border"));
            bs.Set("W", new PdfInteger(width));
            bs.Set("S", new PdfName(style));
            t.Set("BS", bs);
        }
    }
}
