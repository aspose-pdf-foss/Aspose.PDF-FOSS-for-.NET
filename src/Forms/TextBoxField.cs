using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public class TextBoxField : Field
{
    internal TextBoxField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Static default applied to all newly-created TextBoxFields'
    /// auto-fit appearance. Stored only; tests set this to clamp the
    /// max font size for the whole form.</summary>
    public static new double MaxFontSize { get; set; }

    /// <summary>Static default applied to all newly-created TextBoxFields'
    /// auto-fit appearance — the lower bound for the auto-sized font. Stored
    /// only; together with <see cref="MaxFontSize"/> a caller can pin the
    /// appearance font size for the whole form.</summary>
    public static new double MinFontSize { get; set; }

    /// <summary>Static default applied to all newly-created TextBoxFields'
    /// FitIntoRectangle flag. Stored only.</summary>
    public static new bool FitIntoRectangle { get; set; }

    protected override void SetValue(string? value)
        => SetValue(value, validateFormat: true);

    /// <summary>Set the field value, optionally bypassing the /AA/F built-in
    /// format validation. The Facades Form.FillField path fills raw — a value
    /// the field's number/date formatter cannot parse is still stored and
    /// rendered verbatim — while the DOM Value setter keeps the
    /// reject-invalid semantics.</summary>
    internal void SetValue(string? value, bool validateFormat)
    {
        // A value that the field's built-in format action cannot accept (e.g. a
        // non-date string assigned to an AFDate-formatted field) is rejected,
        // leaving the previous value in place.
        if (validateFormat && !FieldFormatScript.IsValueValid(Dict, Reader, value ?? string.Empty))
            return;
        base.SetValue(value);
        // /V holds the raw typed value (per PDF convention). The visible
        // appearance uses what the field's /AA/F Format JavaScript would
        // produce — for the small set of built-in Acrobat formatters
        // (AFDate_FormatEx, AFNumber_Format, AFPercent_Format, ...) we
        // pattern-match the call and apply the equivalent transform.
        // Falls through to the raw value when no Format action is set or
        // when the script isn't a recognised built-in.
        var displayValue = FieldFormatScript.Apply(Dict, Reader, value ?? "");
        RegenerateAppearance(displayValue);

        // Multi-widget field: refresh each extra widget kid's /AP so every visual
        // widget shows the updated value (a bare widget kid carries no /T or /FT).
        foreach (var kid in AllKids())
        {
            if (kid.ContainsKey("T") || kid.ContainsKey("FT")) continue;
            if (Reader.Resolve(kid.Get("Rect")) is not PdfArray kr || kr.Count < 4) continue;
            var kidRect = Rectangle.FromPdfArray(kr);
            var kidAp = BuildWidgetApDict(kidRect.Width, kidRect.Height, kid);
            if (kidAp is not null) kid.Set("AP", kidAp);
        }

        // A value change fires the form's calculate event (Acrobat semantics).
        TriggerRecalculation();
    }

    /// <summary>Persist a calculated result into this field's /V and refresh its
    /// appearance (formatted through any /AA/F action), without re-entering the
    /// calculation trigger. Used by the form recalculation pass.</summary>
    internal void ApplyCalculatedValue(string rawValue)
    {
        base.SetValue(rawValue);
        var displayValue = FieldFormatScript.Apply(Dict, Reader, rawValue);
        RegenerateAppearance(displayValue);
        foreach (var kid in AllKids())
        {
            if (kid.ContainsKey("T") || kid.ContainsKey("FT")) continue;
            if (Reader.Resolve(kid.Get("Rect")) is not PdfArray kr || kr.Count < 4) continue;
            var kidRect = Rectangle.FromPdfArray(kr);
            var kidAp = BuildWidgetApDict(kidRect.Width, kidRect.Height, kid);
            if (kidAp is not null) kid.Set("AP", kidAp);
        }
    }

    /// <summary>When the widget rectangle changes, regenerate the /AP/N appearance so the
    /// value re-lays-out inside the new box. Only fires when
    /// the field already carries an appearance and a value — construction and empty fields
    /// are left untouched.</summary>
    internal override void OnRectChanged()
    {
        if (Dict.Get("AP") is not null)
        {
            var v = Value;
            if (!string.IsNullOrEmpty(v))
            {
                var displayValue = FieldFormatScript.Apply(Dict, Reader, v!);
                RegenerateAppearance(displayValue);
            }
        }

        // Static-XFA form: mirror the new widget geometry into the XFA template and
        // re-render the page's static form content at the new positions. Guarded so a
        // malformed XFA packet can never break a plain AcroForm rectangle move.
        try { OwnerDocument?.Form.SyncXfaWidgetGeometry(this); } catch { /* keep the /Rect change */ }
    }

    /// <summary>
    /// Rebuild the /AP/N appearance stream to display the current value text.
    /// Uses the font/size from the /DA (default appearance) string.
    /// </summary>
    /// <summary>True when the current /AP was generated by this session (Value
    /// setter / rect change / Form.Add) rather than loaded from the document.
    /// Such appearances are safe to rebuild when later property changes
    /// (Multiline, alignment, border) invalidate them.</summary>
    private bool _apAutoGenerated;

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
        // Parse DA for font name and size. A field with no own /DA inherits it up the
        // /Parent chain and finally from the AcroForm /DA — only if none is found at all
        // do we fall back to a fixed Helvetica 12. (The inherited /DA commonly carries a
        // size of 0, i.e. auto-size, which the fixed default would otherwise mask.)
        var da = ResolveInheritedDa() ?? "/Helv 12 Tf 0 g";
        string fontName = "Helv";
        double fontSize = 12;
        var daParts = da.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < daParts.Length; i++)
        {
            if (daParts[i] == "Tf" && i >= 2)
            {
                fontName = daParts[i - 2].TrimStart('/');
                double.TryParse(daParts[i - 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out fontSize);
            }
        }

        // Get the widget rect for BBox
        var rectArr = Reader.Resolve(Dict.Get("Rect")) as PdfArray;
        double llx = 0, lly = 0, urx = 100, ury = 20;
        if (rectArr is { Count: >= 4 })
        {
            var r = Rectangle.FromPdfArray(rectArr);
            llx = r.LLX; lly = r.LLY; urx = r.URX; ury = r.URY;
        }
        double w = urx - llx, h = ury - lly;

        // /MK /R rotated appearance: the text lays out in the ROTATED box (width and
        // height swap for 90/270) and the form's /Matrix turns it into the rect.
        var apRotation = AppearanceRotation();
        if (apRotation is 90 or 270) (w, h) = (h, w);

        // Resolve the composite / Unicode appearance face BEFORE any measurement so
        // the auto-size and alignment math below sees the same advances the shown
        // hex run will use (GetGlyphWidthEm consults _uniAppearanceTtf for chars
        // beyond WinAnsi).
        var needsComposite = false;
        foreach (var ch in text) if (ch > 'ÿ') { needsComposite = true; break; }
        var drFontDict = needsComposite ? ResolveDrFontDict(fontName) : null;
        var compositeCmap = LoadCompositeCmap(drFontDict);

        // No composite /DR face but the value still needs glyphs beyond WinAnsi
        // (Hebrew, Cyrillic, CJK …): embed the /DA font's system face — falling
        // back to Arial — as a Type0/Identity-H font in the appearance resources
        // and show the value as CID hex. RTL values are painted in visual order.
        byte[]? uniTtf = null;
        string uniFamily = "";
        PdfDictionary? uniFontDict = null;
        var uniRes = "";
        if (needsComposite && compositeCmap is null)
        {
            uniFamily = NormalizeStdFontName(fontName);
            uniTtf = Aspose.Pdf.Text.SystemFontResolver.Resolve(uniFamily)
                     ?? Aspose.Pdf.Text.SystemFontResolver.Resolve("Arial");
            // The /DA face must actually cover the value's beyond-WinAnsi chars —
            // a CJK fill through a Latin /DA font needs a script face instead.
            if ((uniTtf is not { Length: > 12 } || !CoversBeyondAnsi(uniTtf, text))
                && Aspose.Pdf.Stamps.TextStamp.TryResolveCjkTtf(text) is { } cjk)
            {
                uniTtf = cjk.ttf;
                uniFamily = cjk.name;
            }
            if (uniTtf is { Length: > 12 })
            {
                uniFontDict = new PdfDictionary();
                (uniRes, _) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(uniFontDict, uniTtf, uniFamily, "");
                _uniAppearanceTtf = uniTtf;
            }
            else
            {
                uniTtf = null;
            }
        }

        // /DA size 0 means auto-size: pick the largest size whose glyph run still fits.
        if (fontSize <= 0)
        {
            if (IsMultiline && text.Length > 0)
            {
                // Multiline auto-size: the value word-wraps inside the box, so the largest
                // fitting size is the one whose wrapped lines each fit the inner width AND
                // whose stacked line boxes fit the inner height.
                fontSize = AutoFitMultilineSize(text, w, h, fontName);
            }
            else
            {
                // Single-line auto-size: the largest size whose run still fits the inner
                // box, matching the standard viewer's variable-text fitting. Both axes carry
                // a 3-unit inset. The width limit measures the run at its real per-glyph
                // advances (DR /Widths → embedded hmtx → Core-14 AFM) with a small
                // reserved inter-glyph allowance; the height limit
                // scales the inner height by a font factor that steps at 7pt (this
                // step is what the fitted size depends on).
                double textEm = 0;
                foreach (char c in text) textEm += GetGlyphWidthEm(c, fontName);
                if (textEm <= 0) textEm = System.Math.Max(1, text.Length) * 0.5;
                var widthCap = (w - 3) / (textEm * 1.031);
                var hInner = h - 3;
                var heightCap = 0.82809 * hInner;
                if (heightCap >= 7) heightCap = 0.811851 * hInner;
                fontSize = System.Math.Max(4, System.Math.Min(widthCap, heightCap));
            }
        }
        else if (TextBoxField.FitIntoRectangle && !IsMultiline && text.Length > 0)
        {
            // FitIntoRectangle: shrink the /DA size so the value fits the widget — measure
            // the run at its real per-glyph advances (DR /Widths → embedded hmtx → system
            // face) and cap to whichever of the inner width / height is binding. Never grow
            // beyond the nominal /DA size.
            double textEm = 0;
            foreach (char c in text) textEm += GetGlyphWidthEm(c, fontName);
            if (textEm <= 0) textEm = text.Length * 0.5;
            var widthCap = (w - 4) / textEm;
            var heightCap = h * 0.83;
            fontSize = System.Math.Max(4, System.Math.Min(fontSize, System.Math.Min(widthCap, heightCap)));
        }

        // Honour the global TextBoxField auto-fit clamps: when a
        // caller pins MinFontSize / MaxFontSize the effective appearance font size
        // is bounded to that range — e.g. Min==Max==15 forces 15 regardless of the
        // /DA size. Unset (0) bounds leave the size untouched.
        if (TextBoxField.MinFontSize > 0) fontSize = System.Math.Max(fontSize, TextBoxField.MinFontSize);
        if (TextBoxField.MaxFontSize > 0) fontSize = System.Math.Min(fontSize, TextBoxField.MaxFontSize);

        // Build the appearance content stream. Encode the text as Windows-1252 (WinAnsi)
        // — the appearance font is declared with /WinAnsiEncoding — so "smart" code points
        // (— • œ Ÿ … the 0x80-0x9F range) survive instead of being lost by Latin1, which
        // can't represent them. WinAnsi agrees with Latin1 on ASCII and 0xA0-0xFF, so plain
        // values are unaffected.
        // A composite /DA font (embedded CJK face in /DR) shows its value as
        // 2-byte glyph-id hex strings under Identity-H — but only when the value
        // actually NEEDS it (chars beyond WinAnsi): Latin-fillable text keeps the
        // Cp1252 literal path even when the /DA names a composite face.
        string ShowOp(string s)
        {
            if (compositeCmap is not null)
            {
                var sb = new System.Text.StringBuilder(s.Length * 4 + 2);
                sb.Append('<');
                foreach (var ch in s)
                    sb.Append((compositeCmap.TryGetValue(ch, out var gid) ? gid : 0).ToString("X4"));
                sb.Append('>');
                return sb.ToString();
            }
            if (uniFontDict is not null)
            {
                var (_, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                    uniFontDict, uniTtf!, uniFamily, Aspose.Pdf.Text.BidiText.ToVisualOrder(s));
                var sb = new System.Text.StringBuilder(hex.Length * 2 + 2);
                sb.Append('<');
                foreach (var b in hex) sb.Append(b.ToString("X2"));
                sb.Append('>');
                return sb.ToString();
            }
            var esc = s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            return $"({esc})";
        }

        string content;
        if (ForceCombs && MaxLen > 0 && !IsMultiline)
        {
            content = BuildCombAppearanceContent(text, w, h, fontName, fontSize);
        }
        else
        {
            // Multiline fields lay the value out top-down, one line per visual line, with a
            // fixed 1.2× line pitch; single-line fields vertical-centre on one baseline.
            string textBody;
            if (IsMultiline)
            {
                // A COMPOSITE /DR font goes through a different generator than the
                // simple-font path: it keeps the legacy pitch/first-line model
                // (1.15× size, typo-descent seat) and the w−4 wrap the composite
                // appearance was calibrated against. Keyed on the /DR font's own
                // subtype — an ASCII value through a Type0 face takes it too.
                var legacyComposite = compositeCmap is not null
                    || ResolveDrFontDict(fontName)?.GetName("Subtype") == "Type0";

                // Word-wrap the value into the usable width (W−2 for the simple-font
                // generator; the glyph run may reach the right inset); explicit
                // '\n'/'\r' always breaks; each line is trailing-trimmed.
                var rawLines = WrapMultilineText(text, fontName, fontSize, legacyComposite ? w - 4 : w - 2);
                if (!legacyComposite)
                {
                    // A value that ENDS with a line break contributes exactly one
                    // trailing empty segment, which is discarded; a value with no
                    // space anywhere collapses its empty segments entirely.
                    if (rawLines.Count > 0 && rawLines[^1].Length == 0
                        && (text.EndsWith("\n") || text.EndsWith("\r")))
                        rawLines.RemoveAt(rawLines.Count - 1);
                    if (!text.Contains(' '))
                        rawLines = rawLines.Where(l => l.Length > 0).ToList();
                }
                var lines = rawLines.ToArray();
                for (int li = 0; li < lines.Length; li++)
                    lines[li] = lines[li].TrimEnd();

                // Line pitch is the font PROGRAM's head-bbox height × size (the
                // /DR FontFile2 face, an AFM box for Standard-14, else the system
                // face) — unless the field's style string set an explicit
                // line-height (rich-text fields).
                var lineHeight = StyleLineHeightPt ?? DsLineHeightPt() ?? (legacyComposite
                    ? fontSize * 1.15
                    : ApLineHeight(fontName, fontSize));
                // First baseline: 2pt below the box top's line slot — H − 2 − L.
                // The composite generator instead seats the first line box at the
                // top with the baseline a typographic descent above its bottom.
                var firstY = legacyComposite
                    ? h - lineHeight - ReadTypoDescentEm(fontName) * fontSize
                    : h - 2 - lineHeight;

                // TextVerticalAlignment shifts the whole line block down by the
                // unused vertical slack (all of it for Bottom, half for Center).
                // Only meaningful while the block actually fits the box.
                var slack = h - lines.Length * lineHeight;
                if (slack > 0)
                {
                    if (TextVerticalAlignment == VerticalAlignment.Center) firstY -= slack / 2;
                    else if (TextVerticalAlignment == VerticalAlignment.Bottom) firstY -= slack;
                }

                // Per-line quadding from /Q (0=left, 1=centre, 2=right): each line's
                // start x is offset by its own measured width — right/centre-aligned
                // multiline values (e.g. RTL paragraphs) line up on their margin.
                int mq = (int)Dict.GetInt("Q");
                double LineX(string line)
                {
                    if (mq is not (1 or 2) || line.Length == 0) return 2;
                    double em = 0;
                    foreach (char c in line) em += GetGlyphWidthEm(c, fontName);
                    var lw = em * fontSize;
                    return mq == 2 ? System.Math.Max(2, w - lw - 2)
                                   : System.Math.Max(2, (w - lw) / 2);
                }

                var bt = new System.Text.StringBuilder();
                double prevX = LineX(lines.Length > 0 ? lines[0] : "");
                bt.Append($"{Format(prevX)} {Format(firstY)} Td\n");
                for (int li = 0; li < lines.Length; li++)
                {
                    // Lines whose baseline falls below the box are clipped away by the
                    // appearance BBox; stop emitting them so the visible appearance
                    // matches regardless of whether a renderer honours the BBox clip.
                    if (firstY - li * lineHeight < 0) break;
                    if (li > 0)
                    {
                        var x = LineX(lines[li]);
                        bt.Append($"{Format(x - prevX)} {Format(-lineHeight)} Td\n");
                        prevX = x;
                    }
                    bt.Append($"{ShowOp(lines[li])} Tj\n");
                }
                textBody = bt.ToString();
            }
            else
            {
                // A single-line field paints its value on one baseline: fold line
                // breaks (a whitespace-only import, a pasted CRLF) into spaces so
                // no control byte reaches the shown string.
                text = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

                // Horizontal alignment from /Q (0=left, 1=centre, 2=right). Right/centre
                // fields offset the baseline start by the measured run width.
                double tx = 2;
                int q = (int)Dict.GetInt("Q");
                if (q is 1 or 2 && text.Length > 0)
                {
                    double textEm = 0;
                    foreach (char c in text) textEm += GetGlyphWidthEm(c, fontName);
                    double textWidth = textEm * fontSize;
                    tx = q == 2 ? System.Math.Max(2, w - textWidth - 2)
                               : System.Math.Max(2, (w - textWidth) / 2);
                }
                // Single-line vertical centring: the line slot (height L) centres in
                // the box and the baseline sits the descender depth above its bottom.
                // The composite generator keeps its legacy optical-centre baseline.
                var slY = compositeCmap is not null
                          || ResolveDrFontDict(fontName)?.GetName("Subtype") == "Type0"
                    ? h / 2 - fontSize * 0.3
                    : (h - (StyleLineHeightPt ?? DsLineHeightPt() ?? ApLineHeight(fontName, fontSize))) / 2
                      + fontSize * ApDescent(fontName) / 1000.0;
                textBody = $"{Format(tx)} {Format(slY)} Td\n{ShowOp(text)} Tj\n";
            }

            content = BuildBorderPrelude(w, h) +
                      $"/Tx BMC\nq\nBT\n/{(uniFontDict is not null ? uniRes : fontName)} {Format(fontSize)} Tf\n{ExtractDaColor(da)}\n" +
                      textBody + "ET\nQ\nEMC\n";
        }
        _uniAppearanceTtf = null;
        var contentBytes = Aspose.Pdf.Text.Cp1252.GetBytes(content);

        // Create the appearance stream
        var apStream = new PdfStream(new PdfDictionary(), contentBytes);
        apStream.Dict.Set("Type", new PdfName("XObject"));
        apStream.Dict.Set("Subtype", new PdfName("Form"));
        var bboxArr = new PdfArray();
        bboxArr.Add(new PdfReal(0)); bboxArr.Add(new PdfReal(0));
        bboxArr.Add(new PdfReal(w)); bboxArr.Add(new PdfReal(h));
        apStream.Dict.Set("BBox", bboxArr);
        ApplyAppearanceRotation(apStream, apRotation);

        // Carry font resources into the appearance so the value text renders.
        // Prefer reusing the resources from a previously-built /AP/N (preserves any
        // embedded font); otherwise declare the /DA font as standard-14 Helvetica —
        // the renderer resolves a font only from the appearance's own resources.
        PdfDictionary? resolvedRes = null;
        var existingAp = Reader.ResolveDict(Dict.Get("AP"));
        if (existingAp is not null)
        {
            var existingN = Reader.ResolveStream(existingAp.Get("N"));
            if (existingN is not null)
                resolvedRes = Reader.ResolveDict(existingN.Dict.Get("Resources"));
        }
        if (uniFontDict is not null)
        {
            // The embedded fallback face IS the appearance's font set — carrying the
            // old WinAnsi resources over would just shadow it.
            var uniResDict = new PdfDictionary();
            uniResDict.Set("Font", uniFontDict);
            apStream.Dict.Set("Resources", uniResDict);
            // A font embedded for a fill lives in the AcroForm default resources
            // too (Acrobat's convention) — document-level font enumeration only
            // walks page resources and /DR, not appearance streams.
            MirrorFillFontIntoDr(uniFontDict.Get(uniRes));
        }
        else
        {
            // A simple (non-composite) /DR font that carries its own program metrics
            // (TrueType Verdana, Arial, …) is referenced verbatim: the synthesized
            // /Type1 stand-in would make renderers substitute a default face with
            // different metrics for any non-Standard-14 family.
            PdfDictionary? apFont = null;
            if (compositeCmap is not null)
                apFont = AppearanceFontFromDr(fontName);
            else if (ResolveDrFontDict(fontName) is { } drSimple && drSimple.GetName("Subtype") == "TrueType")
                apFont = drSimple;
            apStream.Dict.Set("Resources", BuildTextAppearanceResources(fontName, resolvedRes, apFont));
        }

        // Build a fresh /AP dict. Modifying the resolved (possibly-indirect)
        // /AP dict in place leaves the writer with no signal to re-emit it on
        // incremental save — the field's own dict is dirty-tracked but the
        // separate /AP indirect object isn't. Inlining a new direct dict means
        // the field's re-serialised body carries the updated /N reference, and
        // the new appearance stream gets promoted to its own indirect object
        // by PdfWriter.WriteDictionary.
        var newApDict = new PdfDictionary();
        var oldApDict = Reader.ResolveDict(Dict.Get("AP"));
        if (oldApDict is not null)
        {
            foreach (var key in oldApDict.Keys)
            {
                if (key == "N") continue; // we're replacing /N
                newApDict.Set(key, oldApDict.Get(key)!);
            }
        }
        newApDict.Set("N", apStream);
        Dict.Set("AP", newApDict);
        _apAutoGenerated = true;
    }

    /// <summary>Resolve the field's effective /DA: its own, else the nearest /Parent that
    /// carries one, else the AcroForm-level /DA. Returns null when no /DA exists anywhere
    /// (the caller then applies a fixed default).</summary>
    private string? ResolveInheritedDa()
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

    /// <summary>Sum of per-glyph advances of <paramref name="run"/> as a fraction of the em.</summary>
    private double MeasureRunEm(string run, string fontName)
    {
        double em = 0;
        foreach (char c in run) em += GetGlyphWidthEm(c, fontName);
        return em;
    }

    /// <summary>Greedy word-wrap of a multiline field value into lines no wider than
    /// <paramref name="availWidth"/> at <paramref name="fontSize"/>. Explicit newlines always
    /// break; a single word wider than the box is left on its own (overflowing) line.</summary>
    private System.Collections.Generic.List<string> WrapMultilineText(
        string text, string fontName, double fontSize, double availWidth)
    {
        var lines = new System.Collections.Generic.List<string>();
        double spaceW = GetGlyphWidthEm(' ', fontName) * fontSize;
        // Normalise every hard line break to '\n' first: a field value may separate lines
        // with '\r' or '\r\n' (the PDF form convention, e.g. FillField) as well as '\n'.
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var para in normalized.Split('\n'))
        {
            // A paragraph that already fits is kept verbatim — preserving its exact spacing
            // (leading/inner spaces) — so only genuine overflow is re-flowed by word-wrap.
            if (MeasureRunEm(para, fontName) * fontSize <= availWidth) { lines.Add(para); continue; }

            var words = para.Split(' ');
            var cur = new System.Text.StringBuilder();
            double curW = 0;
            foreach (var word in words)
            {
                double wordW = MeasureRunEm(word, fontName) * fontSize;
                if (cur.Length == 0) { cur.Append(word); curW = wordW; }
                else if (curW + spaceW + wordW <= availWidth) { cur.Append(' ').Append(word); curW += spaceW + wordW; }
                else { lines.Add(cur.ToString()); cur.Clear(); cur.Append(word); curW = wordW; }
            }
            lines.Add(cur.ToString());
        }
        return lines;
    }

    /// <summary>Auto-size font for a multiline text field:
    /// <c>Tf = min(cap, (h - inset) / (N · 1.14 · L))</c> where <c>N</c> is the number of
    /// display lines after word-wrap, <c>L</c> is the font's line-height factor (the
    /// FontBBox height ÷ 1000 read from the embedded AcroForm /DR descriptor when the font
    /// is embedded, else a 1.20 default), and <c>cap</c> is 12 for a value with ≥ 2 hard
    /// lines or the single-line ceiling 1525/128 (≈ 11.914) for a single hard line. Width
    /// never caps the size directly — it only matters by forcing wraps that raise N.</summary>
    private double AutoFitMultilineSize(string text, double w, double h, string fontName)
    {
        double availW = w - 4;
        const double inset = 2;
        double L = GetFontLineHeightEm(fontName);
        double perLine = 1.14 * L;

        // Hard-line count picks the cap; a single hard line uses the 1525/128 ceiling even
        // when it soft-wraps onto several display lines.
        int hardLines = 1;
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var ch in normalized) if (ch == '\n') hardLines++;
        double cap = hardLines >= 2 ? 12.0 : 1525.0 / 128.0;

        // The display-line count N(s) grows as s grows (more soft-wraps), so N(s)·perLine·s
        // is monotonic in s: search downward from the cap for the largest size whose wrapped
        // lines still fit the inner height. For a value that never wraps (N = hard lines) this
        // yields the closed form (h − inset)/(N·perLine); for a wrapping value it stops at the
        // largest self-consistent size (a fixed-point iteration can converge to a non-maximal
        // size, so a monotone search is used instead).
        double budget = h - inset;
        for (double s = cap; s >= 4; s -= 0.02)
        {
            int n = System.Math.Max(1, WrapMultilineText(text, fontName, s, availW).Count);
            if (n * perLine * s <= budget)
                return s;
        }
        return 4;
    }

    /// <summary>The font's line-height factor <c>L</c> (rendered line pitch ÷ font size): the
    /// FontBBox height ÷ 1000 from the embedded /DR font descriptor when available, else a
    /// 1.20 default (the value used for a freshly-named/system font).</summary>
    private double GetFontLineHeightEm(string fontName)
    {
        if (GetFontBBoxHeightEmFromDR(fontName) is { } bbox && bbox > 0) return bbox;
        return 1.20;
    }

    /// <summary>FontBBox height (yMax − yMin) ÷ 1000 read from the AcroForm
    /// /DR/Font/&lt;name&gt;/FontDescriptor/FontBBox, or null when the named font has no
    /// embedded descriptor (e.g. a fresh standard font not yet embedded).</summary>
    private double? GetFontBBoxHeightEmFromDR(string fontName)
    {
        PdfDictionary? acro;
        try { acro = Reader.ResolveDict(Reader.Catalog.Get("AcroForm")); }
        catch { return null; }
        var dr = acro is null ? null : Reader.ResolveDict(acro.Get("DR"));
        var fonts = dr is null ? null : Reader.ResolveDict(dr.Get("Font"));
        var font = fonts is null ? null : Reader.ResolveDict(fonts.Get(fontName));
        var fd = font is null ? null : Reader.ResolveDict(font.Get("FontDescriptor"));
        if (fd is null) return null;
        if (Reader.Resolve(fd.Get("FontBBox")) is not PdfArray bbox || bbox.Count < 4) return null;
        double y0 = ArrayNum(bbox[1]), y1 = ArrayNum(bbox[3]);
        double hgt = (y1 - y0) / 1000.0;
        return hgt > 0 ? hgt : null;
    }

    private static double ArrayNum(PdfObject o) => o switch
    {
        PdfReal r => r.Value,
        PdfInteger n => n.Value,
        _ => 0.0,
    };

    /// <summary>Advance width of a character as a fraction of the em, read from the field's
    /// /DA font: first the PDF-level /Widths on the AcroForm /DR font dict (the authoritative
    /// widths for the named comb font), then the embedded /DA font (hmtx), then the Core-14
    /// AFM widths for a standard font name, then the named system face, else ~0.5.</summary>
    /// <summary>AFM FontBBox heights (per-mille) for the Standard-14 faces — the
    /// appearance line pitch for a base-14 /DA font is size × height / 1000.</summary>
    private static double? Std14BboxHeight(string baseName) => baseName switch
    {
        "Helvetica" or "Helvetica-Oblique" => 1156,
        "Helvetica-Bold" or "Helvetica-BoldOblique" => 1190,
        "Times-Roman" => 1116,
        "Times-Bold" => 1153,
        "Times-Italic" => 1100,
        "Times-BoldItalic" => 1139,
        "Courier" or "Courier-Oblique" => 1055,
        "Courier-Bold" or "Courier-BoldOblique" => 1051,
        "Symbol" => 1303,
        "ZapfDingbats" => 963,
        _ => null,
    };

    /// <summary>AFM descender depth (per-mille, positive) for the Standard-14 faces.</summary>
    private static double Std14Descent(string baseName) => baseName switch
    {
        "Times-Roman" or "Times-Bold" or "Times-Italic" or "Times-BoldItalic" => 217,
        "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => 194,
        _ => 207,
    };

    /// <summary>Head-table bounding-box height of a TrueType program as an em
    /// fraction ((yMax − yMin) / unitsPerEm), or null when unparsable.</summary>
    private static double? HeadBboxHeightEm(byte[] ttf)
    {
        try
        {
            var p = new Aspose.Pdf.Text.TrueTypeParser(ttf);
            p.Parse();
            var h = p.BBox[3] - p.BBox[1];
            if (h <= 0 || p.UnitsPerEm <= 0) return null;
            return (double)h / p.UnitsPerEm;
        }
        catch { return null; }
    }

    /// <summary>The appearance line pitch for the field's /DA font: size × the head
    /// bounding-box HEIGHT of the actual font PROGRAM — the embedded /DR FontFile2
    /// when present, an AFM FontBBox for a Standard-14 base font, else the system
    /// face resolved by name, else Times New Roman.</summary>
    private protected double ApLineHeight(string fontName, double fontSize)
    {
        var dr = ResolveDrFontDict(fontName);
        if (dr is not null)
        {
            try
            {
                var fd = Reader.ResolveDict(dr.Get("FontDescriptor"));
                var ff2 = fd is null ? null : Reader.ResolveStream(fd.Get("FontFile2"));
                if (ff2 is not null && HeadBboxHeightEm(Reader.DecodeStream(ff2)) is { } em)
                    return fontSize * em;
            }
            catch { }
        }
        var baseName = NormalizeStdFontName(fontName);
        if ((dr is null || dr.GetName("Subtype") == "Type1") && Std14BboxHeight(baseName) is { } bb)
            return fontSize * bb / 1000.0;
        var sys = Aspose.Pdf.Text.SystemFontResolver.Resolve(
            dr?.GetName("BaseFont") is { Length: > 0 } bf
                ? Aspose.Pdf.Text.SystemFontResolver.NormalizeBaseFontName(bf) : baseName);
        if (sys is not null && HeadBboxHeightEm(sys) is { } em2) return fontSize * em2;
        if (Std14BboxHeight(baseName) is { } bb2) return fontSize * bb2 / 1000.0;
        var tnr = Aspose.Pdf.Text.SystemFontResolver.Resolve("Times New Roman");
        if (tnr is not null && HeadBboxHeightEm(tnr) is { } em3) return fontSize * em3;
        return fontSize * 1.15;
    }

    /// <summary>The /DA font's descender depth as a positive per-mille value, from
    /// the /DR FontDescriptor /Descent when present, else the AFM table.</summary>
    private protected double ApDescent(string fontName)
    {
        try
        {
            var dr = ResolveDrFontDict(fontName);
            var fd = dr is null ? null : Reader.ResolveDict(dr.Get("FontDescriptor"));
            if (fd is not null && fd.ContainsKey("Descent"))
            {
                var d = Aspose.Pdf.Functions.PdfArrayHelper.GetDoubleFromDict(fd, "Descent", 0);
                if (d != 0) return System.Math.Abs(d);
            }
        }
        catch { }
        return Std14Descent(NormalizeStdFontName(fontName));
    }

    /// <summary>Set while a Unicode (embedded Type0) appearance is being generated:
    /// the face whose advances the shown CID run will carry, so measurement of
    /// beyond-WinAnsi chars agrees with the paint exactly.</summary>
    private byte[]? _uniAppearanceTtf;

    /// <summary>True when <paramref name="ttf"/> has a real glyph for every
    /// beyond-WinAnsi char of <paramref name="text"/> (sampled up to 64).</summary>
    private static bool CoversBeyondAnsi(byte[] ttf, string text)
    {
        try
        {
            var parser = _uniWidthParsers.GetValue(ttf, static t => new Aspose.Pdf.Text.GlyphOutlineParser(t));
            var checked_ = 0;
            for (var i = 0; i < text.Length && checked_ < 64; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                if (cp <= 0xFF) continue;
                checked_++;
                if (!parser.CMap.TryGetValue(cp, out var gid) || gid == 0) return false;
            }
            return true;
        }
        catch { return false; }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], Aspose.Pdf.Text.GlyphOutlineParser>
        _uniWidthParsers = new();

    private double GetGlyphWidthEm(char c, string fontName)
    {
        if (c > 'ÿ' && _uniAppearanceTtf is { } uttf)
        {
            try
            {
                var parser = _uniWidthParsers.GetValue(uttf, static t => new Aspose.Pdf.Text.GlyphOutlineParser(t));
                if (parser.CMap.TryGetValue(c, out var gid) && gid != 0)
                {
                    var upm = parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000;
                    return System.Math.Round(parser.GetAdvanceWidth(gid) * 1000.0 / upm) / 1000.0;
                }
            }
            catch { /* fall through to the default estimate */ }
        }
        if (GetGlyphWidthFromDR(fontName, c) is { } drw) return drw;
        var ttf = DefaultAppearance?.EmbeddedFont?.SourceFontData?.TtfData;
        if (ttf is { Length: > 0 })
        {
            var (_, _, _, widths) = Aspose.Pdf.Text.FontRepository.ReadTtfMetrics(ttf);
            if (c < 256 && widths[c] > 0) return widths[c] / 1000.0;
        }
        // Core-14 AFM widths (Helv/Helvetica/Arial, TiRo/Times, Cour/Courier, …) — the
        // authoritative metrics for a non-embedded standard font named in /DA.
        var std = Aspose.Pdf.Text.Standard14Fonts.GetWidth(NormalizeStdFontName(fontName), c);
        if (std > 0) return std / 1000.0;
        var resolved = Aspose.Pdf.Text.SystemFontResolver.Resolve(fontName);
        if (resolved is { Length: > 0 })
        {
            var (_, _, _, widths) = Aspose.Pdf.Text.FontRepository.ReadTtfMetrics(resolved);
            if (c < 256 && widths[c] > 0) return widths[c] / 1000.0;
        }
        return 0.5;
    }

    /// <summary>Map the abbreviated AcroForm /DA font names to their Core-14 base names so
    /// the AFM width tables resolve (Helv→Helvetica, TiRo→Times-Roman, Cour→Courier, …).</summary>
    private static string NormalizeStdFontName(string fontName) => fontName switch
    {
        "Helv" => "Helvetica",
        "HeBO" => "Helvetica-BoldOblique",
        "HeBo" => "Helvetica-Bold",
        "HeOb" => "Helvetica-Oblique",
        "TiRo" => "Times-Roman",
        "TiBo" => "Times-Bold",
        "TiIt" => "Times-Italic",
        "TiBI" => "Times-BoldItalic",
        "Cour" => "Courier",
        _ => fontName,
    };

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

    /// <summary>Read a simple font's PDF /Widths entry for a character from the AcroForm
    /// /DR /Font dictionary (the default-resources font named by /DA). Returns the advance
    /// as an em fraction, or null when the font is absent / composite / has no usable entry.</summary>
    private double? GetGlyphWidthFromDR(string fontName, char c)
    {
        PdfDictionary? acro;
        try { acro = Reader.ResolveDict(Reader.Catalog.Get("AcroForm")); }
        catch { return null; }
        var dr = acro is null ? null : Reader.ResolveDict(acro.Get("DR"));
        var fonts = dr is null ? null : Reader.ResolveDict(dr.Get("Font"));
        var font = fonts is null ? null : Reader.ResolveDict(fonts.Get(fontName));
        if (font is null) return null;
        if (font.GetName("Subtype") == "Type0") return null; // composite widths not handled here
        int first = (int)font.GetInt("FirstChar");
        if (Reader.Resolve(font.Get("Widths")) is not PdfArray warr) return null;
        int idx = c - first;
        if (idx < 0 || idx >= warr.Count) return null;
        double wv = warr[idx] switch { PdfReal r => r.Value, PdfInteger n => n.Value, _ => 0.0 };
        return wv > 0 ? wv / 1000.0 : null;
    }

    /// <summary>The colour-set operator (<c>r g b rg</c> / <c>g g</c> / <c>c m y k k</c>) parsed
    /// out of a /DA string, so the appearance paints the field's configured text colour rather
    /// than always black. Falls back to <paramref name="fallback"/> when /DA carries no colour.</summary>
    /// <summary>Build a value /AP dict for an extra widget rect of size w×h, rendering
    /// the field's current single-line value (used by multi-widget construction).</summary>
    internal override PdfDictionary? BuildWidgetApDict(double w, double h) => BuildWidgetApDict(w, h, null);

    /// <summary>As <see cref="BuildWidgetApDict(double,double)"/> but driven by a specific
    /// widget's own /DA (font, size and colour) when <paramref name="widgetDict"/> is supplied —
    /// so a multi-widget field renders each widget in its configured appearance.</summary>
    internal PdfDictionary? BuildWidgetApDict(double w, double h, PdfDictionary? widgetDict)
    {
        var text = FieldFormatScript.Apply(Dict, Reader, Value ?? string.Empty);

        // /MK /R rotated widget: lay out in the rotated box, rotate via the form /Matrix.
        var apRotation = AppearanceRotation(widgetDict);
        if (apRotation is 90 or 270) (w, h) = (h, w);

        var daSrc = (widgetDict?.Get("DA") as PdfString) ?? (Dict.Get("DA") as PdfString);
        var da = daSrc is not null ? daSrc.ToText() : "/Helv 12 Tf 0 g";
        string fontName = "Helv";
        double fontSize = 12;
        var daParts = da.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < daParts.Length; i++)
            if (daParts[i] == "Tf" && i >= 2)
            {
                fontName = daParts[i - 2].TrimStart('/');
                double.TryParse(daParts[i - 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out fontSize);
            }
        if (fontSize <= 0)
            fontSize = System.Math.Max(4, System.Math.Min((w - 4) / (System.Math.Max(1, text.Length) * 0.5), h * 0.83));

        // Values beyond WinAnsi (Hebrew, CJK …) get the same embedded-Type0
        // treatment as RegenerateAppearance: private face, CID hex, visual order.
        var widgetNeedsUni = false;
        foreach (var ch in text) if (ch > 'ÿ') { widgetNeedsUni = true; break; }
        byte[]? wuTtf = null;
        var wuFam = "";
        PdfDictionary? wuFonts = null;
        var wuRes = "";
        if (widgetNeedsUni)
        {
            wuFam = NormalizeStdFontName(fontName);
            wuTtf = Aspose.Pdf.Text.SystemFontResolver.Resolve(wuFam)
                    ?? Aspose.Pdf.Text.SystemFontResolver.Resolve("Arial");
            if ((wuTtf is not { Length: > 12 } || !CoversBeyondAnsi(wuTtf, text))
                && Aspose.Pdf.Stamps.TextStamp.TryResolveCjkTtf(text) is { } cjk)
            {
                wuTtf = cjk.ttf;
                wuFam = cjk.name;
            }
            if (wuTtf is { Length: > 12 })
            {
                wuFonts = new PdfDictionary();
                (wuRes, _) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(wuFonts, wuTtf, wuFam, "");
                _uniAppearanceTtf = wuTtf;
            }
            else
            {
                wuTtf = null;
            }
        }

        string content;
        if (ForceCombs && MaxLen > 0 && !IsMultiline)
        {
            // Comb widget: lay the value out one glyph per cell, taking the border colour
            // from this widget's own /MK /BC (a per-widget characteristic).
            content = BuildCombAppearanceContent(text, w, h, fontName, fontSize,
                ResolveCombBorderRgb(widgetDict ?? Dict));
        }
        else
        {
            var shown = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            // Widget quadding from /Q (widget kid first, else the field).
            double tx = 2;
            int q = (int)((widgetDict?.Get("Q") is PdfInteger wq ? wq.Value : Dict.GetInt("Q")));
            if (q is 1 or 2 && shown.Length > 0)
            {
                double em = 0;
                foreach (char c in shown) em += GetGlyphWidthEm(c, fontName);
                var tw = em * fontSize;
                tx = q == 2 ? System.Math.Max(2, w - tw - 2) : System.Math.Max(2, (w - tw) / 2);
            }
            string showOp;
            if (wuFonts is not null)
            {
                var (_, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                    wuFonts, wuTtf!, wuFam, Aspose.Pdf.Text.BidiText.ToVisualOrder(shown));
                var sb = new System.Text.StringBuilder(hex.Length * 2 + 2);
                sb.Append('<');
                foreach (var b in hex) sb.Append(b.ToString("X2"));
                sb.Append('>');
                showOp = sb.ToString();
            }
            else
            {
                showOp = "(" + shown.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ")";
            }
            var wY = (h - ApLineHeight(fontName, fontSize)) / 2 + fontSize * ApDescent(fontName) / 1000.0;
            content = $"/Tx BMC\nq\nBT\n/{(wuFonts is not null ? wuRes : fontName)} {Format(fontSize)} Tf\n{ExtractDaColor(da)}\n{Format(tx)} {Format(wY)} Td\n{showOp} Tj\nET\nQ\nEMC\n";
        }
        _uniAppearanceTtf = null;
        var apStream = new PdfStream(new PdfDictionary(), Aspose.Pdf.Text.Cp1252.GetBytes(content));
        apStream.Dict.Set("Type", new PdfName("XObject"));
        apStream.Dict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        apStream.Dict.Set("BBox", bbox);
        ApplyAppearanceRotation(apStream, apRotation);
        if (wuFonts is not null)
        {
            var wuResDict = new PdfDictionary();
            wuResDict.Set("Font", wuFonts);
            apStream.Dict.Set("Resources", wuResDict);
        }
        else
        {
            // Same rule as RegenerateAppearance: a simple TrueType /DR font is carried
            // verbatim so the widget draws the real face instead of a substituted one.
            PdfDictionary? widgetApFont = null;
            if (ResolveDrFontDict(fontName) is { } drTt && drTt.GetName("Subtype") == "TrueType")
                widgetApFont = drTt;
            apStream.Dict.Set("Resources", BuildTextAppearanceResources(fontName, null, widgetApFont));
        }

        var apDict = new PdfDictionary();
        apDict.Set("N", apStream);
        return apDict;
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

    private protected static string Format(double v) =>
        v.ToString("G", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Resolve the typographic descender (signed em ratio) used to place
    /// the first multiline baseline. Prefers the font embedded via the field's
    /// DefaultAppearance (its /DA name may already be an embedded-resource alias
    /// that no longer resolves by family name); otherwise loads the named system
    /// face; falls back to a typical Latin descent.</summary>
    private double ReadTypoDescentEm(string fontName)
    {
        var ttf = DefaultAppearance?.EmbeddedFont?.SourceFontData?.TtfData;
        if (ttf is { Length: > 0 })
        {
            var d = Aspose.Pdf.Text.FontRepository.ReadTtfTypoDescentEm(ttf);
            if (d != 0) return d;
        }
        var resolved = Aspose.Pdf.Text.SystemFontResolver.Resolve(fontName);
        if (resolved is not null)
        {
            var d = Aspose.Pdf.Text.FontRepository.ReadTtfTypoDescentEm(resolved);
            if (d != 0) return d;
        }
        return -0.207; // Helvetica-class default
    }

    /// <summary>
    /// Creates a new text box field on the specified page with the given rectangle.
    /// </summary>
    public TextBoxField(Page page, Rectangle rect)
        : base(BuildTextFieldDict(rect), page.Reader)
    {
    }

    /// <summary>
    /// Creates a new text box field associated with the document (page assigned via Form.Add).
    /// </summary>
    public TextBoxField(Document doc, Rectangle rect)
        : base(BuildTextFieldDict(rect), doc.Pages[1].Reader)
    {
    }

    /// <summary>Document-bound text box without a rectangle. Caller sets
    /// <see cref="Width"/> / <see cref="Height"/> or attaches via Form.Add.</summary>
    public TextBoxField(Document doc)
        : base(BuildTextFieldDict(new Rectangle(0, 0, 0, 0)), doc.Pages[1].Reader)
    {
    }

    /// <summary>Multi-widget text field — first rectangle is used for the widget
    /// dictionary; additional rectangles are stored on /Kids per the
    /// multi-widget contract.</summary>
    public TextBoxField(Page page, Rectangle[] rects)
        : base(BuildTextFieldDict(rects is { Length: > 0 } ? rects[0] : new Rectangle(0, 0, 0, 0)),
               page.Reader)
    {
        if (rects is null || rects.Length <= 1) return;
        var kids = new Aspose.Pdf.Core.PdfArray();
        for (var i = 1; i < rects.Length; i++)
        {
            var kid = new PdfDictionary();
            kid.Set("Subtype", new PdfName("Widget"));
            kid.Set("Rect", MakeRectArray(rects[i]));
            kids.Add(kid);
        }
        Dict.Set("Kids", kids);
    }

    /// <summary>
    /// Parameterless ctor — creates an unbound text box field. Layout
    /// dimensions come from <see cref="Width"/> / <see cref="Height"/>;
    /// the field is later attached to a page (via Form.Add) or to a layout
    /// container that places it (via <c>cell.Paragraphs.Add(field)</c>).
    /// </summary>
    public TextBoxField()
        : base(BuildTextFieldDict(new Rectangle(0, 0, 0, 0)), IO.PdfReader.Empty)
    {
    }

    private static PdfDictionary BuildTextFieldDict(Rectangle rect)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Widget"));
        dict.Set("FT", new PdfName("Tx"));
        dict.Set("Rect", MakeRectArray(rect));
        dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes("/Helv 12 Tf 0 g")));
        return dict;
    }

    /// <summary>Layout width when the field is placed inside a layout
    /// container (table cell, paragraph). Stored only; the actual widget
    /// rectangle is set when the field is attached to a page.</summary>
    public new double Width { get; set; }

    /// <summary>Layout height when the field is placed inside a layout
    /// container. On a page-bound field (a real widget rectangle exists) setting it
    /// also resizes the rectangle like an annotation — bottom edge anchored — so a
    /// caller-supplied Height genuinely shrinks/grows the widget box.</summary>
    public new double Height
    {
        get => _layoutHeight > 0 ? _layoutHeight : base.Height;
        set
        {
            _layoutHeight = value;
            if (Rect is { } r && r.Width > 0 && r.Height > 0)
                base.Height = value;
        }
    }
    private double _layoutHeight;

    /// <summary>Multiline flag (bit 13 of /Ff). Setter mirrors the
    /// existing read-only <see cref="IsMultiline"/>.</summary>
    public bool Multiline
    {
        get => IsMultiline;
        set
        {
            var f = FieldFlags;
            if (value) f |= (1 << 12);
            else f &= ~(1 << 12);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    public int MaxLen
    {
        get => (int)Dict.GetInt("MaxLen");
        set => Dict.Set("MaxLen", new Aspose.Pdf.Core.PdfInteger(value));
    }
    public bool IsMultiline => (FieldFlags & (1 << 12)) != 0;
    public bool IsPassword => (FieldFlags & (1 << 13)) != 0;

    /// <summary>
    /// Vertical alignment of the text inside the field's appearance.
    /// Stored in-memory; persisted to /AP regeneration
    /// when the appearance pipeline reads this hint.
    /// </summary>
    public VerticalAlignment TextVerticalAlignment { get; set; } = VerticalAlignment.None;

    /// <summary>True when the Tx field has the rich-text flag (bit 26 of /Ff) set.</summary>
    public bool IsRichText => (FieldFlags & (1 << 25)) != 0;

    /// <summary>
    /// Text justification: 0 = Left, 1 = Center, 2 = Right.
    /// </summary>
    public int Justification => (int)Dict.GetInt("Q");

    /// <summary>Bit 25 of /Ff (Comb): when set, the field is divided into
    /// MaxLen equal-width combs. Requires Multiline=false and MaxLen>0.</summary>
    public bool ForceCombs
    {
        get => (FieldFlags & (1 << 24)) != 0;
        set
        {
            var f = FieldFlags;
            if (value) f |= (1 << 24);
            else f &= ~(1 << 24);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>Inverse of bit 24 of /Ff (DoNotScroll). When false the field
    /// will not scroll horizontally / vertically beyond its visible region.</summary>
    public bool Scrollable
    {
        get => (FieldFlags & (1 << 23)) == 0;
        set
        {
            var f = FieldFlags;
            if (value) f &= ~(1 << 23);
            else f |= (1 << 23);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>Inverse of bit 23 of /Ff (DoNotSpellCheck).</summary>
    public bool SpellCheck
    {
        get => (FieldFlags & (1 << 22)) == 0;
        set
        {
            var f = FieldFlags;
            if (value) f &= ~(1 << 22);
            else f |= (1 << 22);
            Dict.Set("Ff", new Aspose.Pdf.Core.PdfInteger(f));
        }
    }

    /// <summary>The text value stored in /V.</summary>
    public override string? Value
    {
        get
        {
            // Auto-calculate: a field with an /AA/C calculate action reports its
            // recomputed value (Acrobat auto-calculates on read).
            if (Dict.Get("AA") is not null
                && FieldCalculateScript.ComputeValue(Dict, Reader) is { } computed)
                return computed;
            if (Reader.Resolve(Dict.Get("V")) is PdfString s) return s.ToText();
            // /V is inheritable, but only a PURE widget kid (no /T of its own)
            // reads it up the /Parent chain — a NAMED child field's missing /V
            // stays null (an ancestor's value is not this field's; inheriting
            // one would push bogus values into e.g. the XFA datasets sync).
            if (!Dict.ContainsKey("T"))
            {
                var parent = Reader.ResolveDict(Dict.Get("Parent"));
                while (parent is not null)
                {
                    if (Reader.Resolve(parent.Get("V")) is PdfString ps) return ps.ToText();
                    parent = Reader.ResolveDict(parent.Get("Parent"));
                }
            }
            return null;
        }
        set => SetValue(value);
    }

    /// <summary>Encode <paramref name="code"/> as a placeholder barcode in the
    /// field value. The FOSS pipeline does not currently render a glyph form;
    /// the string is stored verbatim so callers can round-trip the value.</summary>
    public void AddBarcode(string code) => SetValue(code);

    /// <summary>Stub for image overlay support. Cross-platform — no GDI
    /// rasterization happens.</summary>
    public void AddImage(System.Drawing.Image image) { _ = image; }
}

/// <summary>
/// A Tx form field that carries the rich-text flag (bit 26 of /Ff). Stores rich-text
/// content via /RV and exposes <see cref="Value"/> as the plain-text representation
/// from /V. Mirrors the public type.
/// </summary>
public sealed class RichTextBoxField : TextBoxField
{
    internal RichTextBoxField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Construct a rich-text field on the given page rectangle.</summary>
    public RichTextBoxField(Page page, Rectangle rect) : base(page, rect)
    {
        // Set the rich-text bit (PDF table-228 bit 26).
        Dict.Set("Ff", new PdfInteger(FieldFlags | (1 << 25)));
    }

    /// <summary>The rich-text representation of the field value (PDF /RV entry). Setting it also
    /// derives the plain-text /V and regenerates the /AP: the XHTML runs (bold spans, <c>&lt;br/&gt;</c>
    /// line breaks) are laid out with a font per style (regular vs bold) so the appearance shows the
    /// styled text.</summary>
    public string? RichTextValue
    {
        get => Dict.Get("RV") is PdfString s ? s.ToText() : null;
        set
        {
            Dict.Set("RV", value is null ? null! : new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
            if (!string.IsNullOrEmpty(value)) ApplyRichText(value!);
        }
    }

    /// <summary>One styled run of the rich text: a piece of literal text (with its bold flag)
    /// or a hard line break.</summary>
    private sealed class RtRun { public string Text = ""; public bool Bold; public bool Break; }

    /// <summary>Parse the /RV XHTML, set the plain-text /V, and regenerate the /AP so the styled
    /// runs render with a regular / bold font and honour <c>&lt;br/&gt;</c> breaks.</summary>
    private void ApplyRichText(string rv)
    {
        var runs = new System.Collections.Generic.List<RtRun>();
        // Unstyled rich text lays out in the viewer default face — Helvetica; an
        // explicit font-family style (the styled-span producers write one) overrides.
        string family = "Helvetica";
        double size = 12;
        try
        {
            // Tolerate HTML-style void tags (<br>, <hr>) that aren't well-formed XML by
            // self-closing them before parsing — viewers accept them in /RV. A PAIRED
            // closer (<br></br>) must lose its closer first, or self-closing the opener
            // leaves an orphan </br> that breaks the whole parse.
            var xml = System.Text.RegularExpressions.Regex.Replace(
                rv, @"</\s*(br|hr)\s*>", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            xml = System.Text.RegularExpressions.Regex.Replace(
                xml, @"<(br|hr)(\s[^>/]*)?>", "<$1$2/>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var xdoc = System.Xml.Linq.XDocument.Parse(xml);
            System.Xml.Linq.XElement? p = null;
            foreach (var e in xdoc.Descendants())
                if (e.Name.LocalName == "p") { p = e; break; }
            p ??= xdoc.Root;
            if (p is not null)
            {
                var style = (string?)p.Attribute("style") ?? "";
                var fm = System.Text.RegularExpressions.Regex.Match(style, @"font-family:\s*'?([^;']+)'?");
                if (fm.Success) family = fm.Groups[1].Value.Trim();
                var sm = System.Text.RegularExpressions.Regex.Match(style, @"font-size:\s*([\d.]+)\s*pt");
                if (sm.Success) double.TryParse(sm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out size);
                WalkRichText(p, false, runs);
            }
        }
        catch { return; } // malformed /RV — leave the field value/appearance untouched

        var plain = new System.Text.StringBuilder();
        foreach (var r in runs) plain.Append(r.Break ? "\n" : r.Text);
        Dict.Set("V", new PdfString(System.Text.Encoding.UTF8.GetBytes(plain.ToString())));

        RegenerateRichAppearance(runs, family, size <= 0 ? 12 : size);
    }

    /// <summary>Recursively collect styled text runs: a bold-weighted <c>&lt;span&gt;</c> marks its
    /// content bold; <c>&lt;br/&gt;</c> is a line break; other decorations (e.g. underline) don't
    /// change the font, so their text merges with the surrounding run.</summary>
    private static void WalkRichText(System.Xml.Linq.XElement el, bool bold, System.Collections.Generic.List<RtRun> runs)
    {
        foreach (var node in el.Nodes())
        {
            if (node is System.Xml.Linq.XText t)
                runs.Add(new RtRun { Text = t.Value, Bold = bold });
            else if (node is System.Xml.Linq.XElement ce)
            {
                if (ce.Name.LocalName == "br") { runs.Add(new RtRun { Break = true }); continue; }
                var childBold = bold
                    || ce.Name.LocalName == "b"
                    || ((string?)ce.Attribute("style") ?? "").Replace(" ", "").Contains("font-weight:bold");
                WalkRichText(ce, childBold, runs);
            }
        }
    }

    /// <summary>Build the /AP/N stream for a list of styled runs: a regular and a bold font resource
    /// (only those actually used), one <c>Tf</c> per font change (so consecutive same-font runs merge
    /// into a single fragment), and a negative-Y <c>Td</c> for each line break.</summary>
    private void RegenerateRichAppearance(System.Collections.Generic.List<RtRun> runs, string family, double size)
    {
        var rectArr = Reader.Resolve(Dict.Get("Rect")) as PdfArray;
        double w = 100, h = 20;
        if (rectArr is { Count: >= 4 })
        {
            var r = Rectangle.FromPdfArray(rectArr);
            w = r.URX - r.LLX; h = r.URY - r.LLY;
        }

        const string regRes = "FR", boldRes = "FB";
        var lineHeight = size * 1.15;
        var firstY = h - lineHeight + 0.207 * size;

        var sb = new System.Text.StringBuilder();
        sb.Append("/Tx BMC\nq\nBT\n0 0 0 rg\n");
        sb.Append($"2 {Format(firstY)} Td\n");
        string? curFont = null;
        bool usedReg = false, usedBold = false;
        foreach (var run in runs)
        {
            if (run.Break) { sb.Append($"0 {Format(-lineHeight)} Td\n"); continue; }
            if (run.Text.Length == 0) continue;
            var res = run.Bold ? boldRes : regRes;
            if (res != curFont) { sb.Append($"/{res} {Format(size)} Tf\n"); curFont = res; }
            if (run.Bold) usedBold = true; else usedReg = true;
            // One Tj per word (trailing whitespace kept with its word) — text
            // extractors then report the appearance as per-word fragments.
            foreach (System.Text.RegularExpressions.Match word in
                     System.Text.RegularExpressions.Regex.Matches(run.Text, @"\S+\s*|\s+"))
            {
                var esc = word.Value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                sb.Append($"({esc}) Tj\n");
            }
        }
        sb.Append("ET\nQ\nEMC\n");

        var apStream = new PdfStream(new PdfDictionary(), Aspose.Pdf.Text.Cp1252.GetBytes(sb.ToString()));
        apStream.Dict.Set("Type", new PdfName("XObject"));
        apStream.Dict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        apStream.Dict.Set("BBox", bbox);

        var fonts = new PdfDictionary();
        if (usedReg) fonts.Set(regRes, MakeRichFont(family));
        // The styled variant's name carries a style comma ("Arial,Bold"). A BARE
        // /BaseFont's comma is a separator that FontInfo.FontName normalises away
        // ("ArialBold"), while a subset-tagged one is genuine and kept verbatim —
        // readers must report the styled family exactly, so tag the styled variant
        // (a rich-text /AP reports "Arial,Bold"). The Standard-14 default family
        // instead takes its dash-styled sibling name verbatim ("Helvetica-Bold").
        if (usedBold) fonts.Set(boldRes, MakeRichFont(
            family == "Helvetica" ? "Helvetica-Bold"
                                  : RichSubsetTag() + "+" + family + ",Bold"));
        var res2 = new PdfDictionary();
        res2.Set("Font", fonts);
        apStream.Dict.Set("Resources", res2);

        var newAp = new PdfDictionary();
        newAp.Set("N", apStream);
        Dict.Set("AP", newAp);
    }

    /// <summary>6-uppercase-letter subset tag (PDF 32000 §9.6.4) for the styled rich-text
    /// appearance font.</summary>
    private static string RichSubsetTag()
    {
        var random = new System.Random();
        var chars = new char[6];
        for (int i = 0; i < 6; i++)
            chars[i] = (char)('A' + random.Next(26));
        return new string(chars);
    }

    /// <summary>A Type1 appearance font whose /BaseFont is the given name verbatim (e.g. "Arial"
    /// or "Arial,Bold") so a reader reports that exact family/style.</summary>
    private static PdfDictionary MakeRichFont(string baseFont)
    {
        var f = new PdfDictionary();
        f.Set("Type", new PdfName("Font"));
        f.Set("Subtype", new PdfName("Type1"));
        f.Set("BaseFont", new PdfName(baseFont));
        f.Set("Encoding", new PdfName("WinAnsiEncoding"));
        return f;
    }

    /// <summary>The formatted (rich-text) representation; alias for <see cref="RichTextValue"/>.</summary>
    public string? FormattedValue
    {
        get => RichTextValue;
        set => RichTextValue = value;
    }

    /// <summary>Plain-text value rendered by this field (/V entry). Assigning a rich-text
    /// body (an XHTML <c>&lt;body&gt;</c> fragment) routes through <see cref="RichTextValue"/>
    /// — /RV keeps the markup verbatim and /V gets the derived plain text. Assigning plain
    /// text keeps /RV in sync by wrapping the text in a default-styled body (the field's
    /// /DA font and size), so the formatted and plain representations never diverge.
    /// A real OVERRIDE (not a shadow): callers fill rich fields through base-typed
    /// <c>Field</c> references, and the rich sync must fire for them too.</summary>
    public override string? Value
    {
        get => Dict.Get("V") is PdfString s ? s.ToText() : null;
        set
        {
            if (!string.IsNullOrEmpty(value) && LooksLikeRichBody(value!))
            {
                RichTextValue = value;
                return;
            }
            // Plain text keeps /V byte-exact (values are round-tripped verbatim by
            // export paths — no newline or escaping normalisation may leak in);
            // /RV and the styled appearance are derived as side-effects.
            Dict.Set("V", new PdfString(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty)));
            if (string.IsNullOrEmpty(value)) return;
            Dict.Set("RV", new PdfString(System.Text.Encoding.UTF8.GetBytes(SynthesizeRichBody(value!))));
            var da = DefaultAppearance;
            var family = ResolveDaFamily(da.FontName);
            var size = da.FontSize > 0 ? da.FontSize : 12;
            var runs = new System.Collections.Generic.List<RtRun>();
            var first = true;
            foreach (var line in value!.Replace("\r\n", "\n").Split('\n', '\r'))
            {
                if (!first) runs.Add(new RtRun { Break = true });
                runs.Add(new RtRun { Text = line });
                first = false;
            }
            RegenerateRichAppearance(runs, family, size);
        }
    }

    /// <summary>A value that already is rich-text markup (an XHTML body or a full XML
    /// document) rather than literal text.</summary>
    private static bool LooksLikeRichBody(string value)
    {
        var t = value.TrimStart();
        return t.StartsWith("<body", System.StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("<?xml", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Wrap plain text in the /RV XHTML shape Acrobat writes, styled with the
    /// field's /DA font family and size so the appearance matches the plain rendering.</summary>
    private string SynthesizeRichBody(string plain)
    {
        var da = DefaultAppearance;
        var family = ResolveDaFamily(da.FontName);
        var size = da.FontSize > 0 ? da.FontSize : 12;
        var escaped = new System.Text.StringBuilder();
        foreach (var ch in plain.Replace("\r\n", "\n"))
            escaped.Append(ch switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '\n' => "<br/>",
                _ => ch.ToString(),
            });
        return "<body xfa:APIVersion=\"Acroform:2.7.0.0\" xfa:spec=\"2.1\""
             + " xmlns=\"http://www.w3.org/1999/xhtml\""
             + " xmlns:xfa=\"http://www.xfa.org/schema/xfa-data/1.0/\">"
             + "<p dir=\"ltr\" style=\"margin-top:0pt;margin-bottom:0pt;font-family:" + family
             + ";font-size:" + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "pt\">"
             + escaped + "</p></body>";
    }

    /// <summary>Map the /DA resource alias to a face name via the form's /DR font
    /// dictionary (/BaseFont), falling back to the well-known Acrobat aliases.</summary>
    private string ResolveDaFamily(string daFontName)
    {
        if (string.IsNullOrEmpty(daFontName)) return "Helvetica";
        if (ResolveDrFontDict(daFontName) is { } fd && fd.GetName("BaseFont") is { } bf)
        {
            var name = bf;
            var plus = name.IndexOf('+');
            if (plus == 6) name = name.Substring(7);
            var comma = name.IndexOf(',');
            if (comma > 0) name = name.Substring(0, comma);
            return name;
        }
        return daFontName switch
        {
            "Helv" => "Helvetica",
            "TiRo" => "Times New Roman",
            "Cour" => "Courier",
            _ => daFontName,
        };
    }

    private string _style = string.Empty;

    /// <summary>CSS-style style string applied to the rich-text fragments — e.g.
    /// <c>font: 'Tahoma' bold 10pt</c>. Setting it updates the field's /DA (font family, size and
    /// bold/italic style) so the generated appearance uses the requested font instead of the
    /// default.</summary>
    public string Style
    {
        get => _style;
        set { _style = value ?? string.Empty; ApplyStyleToDefaultAppearance(_style); }
    }

    /// <summary>Parse the style string and write the matching /DA (font + size). Two syntaxes
    /// are accepted: the shorthand <c>font: 'Family' [bold] [italic] Npt</c> and longhand CSS
    /// declarations (<c>font-family: X; font-size: Npt; line-height: Npt</c>). The font family
    /// may contain spaces; it is resolved to a face name the appearance generator can both
    /// render and re-resolve. A <c>line-height</c> becomes the multiline appearance's line
    /// pitch (see the multiline appearance builder).</summary>
    private void ApplyStyleToDefaultAppearance(string style)
    {
        if (string.IsNullOrEmpty(style)) return;
        string? family = null;
        double size = 0;
        var m = System.Text.RegularExpressions.Regex.Match(style,
            @"font\s*:\s*'([^']*)'(.*?)([\d.]+)\s*pt",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            family = m.Groups[1].Value.Trim();
            double.TryParse(m.Groups[3].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out size);
        }
        else
        {
            var fm = System.Text.RegularExpressions.Regex.Match(style,
                @"font-family\s*:\s*'?([^;'}]+)'?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (fm.Success) family = fm.Groups[1].Value.Trim();
            var sm = System.Text.RegularExpressions.Regex.Match(style,
                @"font-size\s*:\s*([\d.]+)\s*pt", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (sm.Success)
                double.TryParse(sm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out size);
        }
        var lm = System.Text.RegularExpressions.Regex.Match(style,
            @"line-height\s*:\s*([\d.]+)\s*pt", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (lm.Success && double.TryParse(lm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lh) && lh > 0)
            StyleLineHeightPt = lh;
        if (family is null || size <= 0) return;
        DefaultAppearance = new Aspose.Pdf.Annotations.DefaultAppearance(family, (float)size,
            System.Drawing.Color.Black);
    }

    /// <summary>Text justification.</summary>
    public Aspose.Pdf.Annotations.Justification Justify { get; set; } = Aspose.Pdf.Annotations.Justification.Left;
}

/// <summary>Visual style of the checkbox glyph (/MK /CA mapping).</summary>
public enum BoxStyle
{
    Circle,
    Check,
    Cross,
    Diamond,
    Square,
    Star,
}

/// <summary>Outer shape of a checkbox / radio-button widget border.</summary>
public enum BoxShape
{
    Square,
    Circle,
}
