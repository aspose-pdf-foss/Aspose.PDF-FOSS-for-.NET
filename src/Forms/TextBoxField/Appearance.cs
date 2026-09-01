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
                // The inset is one unit plus the border on both sides: 3 for the
                // usual 1-unit /BS border, 1 for a border-less widget (a generator
                // text box: 20 pt high -> 15.4252 pt, 80 pt wide -> 27.5747 pt cap
                // for "Green", probed 2026-08-23).
                var inset = AutoSizeInset();
                if (text.Length == 0)
                {
                    // An empty value has nothing to fit: the builder default size.
                    fontSize = EmptyValueAutoSize;
                }
                else
                {
                    double textEm = 0;
                    foreach (char c in text) textEm += GetGlyphWidthEm(c, fontName);
                    if (textEm <= 0) textEm = System.Math.Max(1, text.Length) * 0.5;
                    var widthCap = (w - inset) / (textEm * 1.031);
                    var hInner = h - inset;
                    var heightCap = 0.82809 * hInner;
                    if (heightCap >= 7) heightCap = 0.811851 * hInner;
                    fontSize = System.Math.Max(4, System.Math.Min(widthCap, heightCap));
                }
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
        else if (IsMultiline && text.Length > 0 && this is not RichTextBoxField
                 && (!Scrollable || Field.FitIntoRectangle || TextBoxField.FitIntoRectangle))
        {
            // (Rich text is exempt: a rich-text field keeps its /DA size
            // even when the value overflows a DoNotScroll box — a 3-line 20 pt value
            // in a 72 pt box still steps its lines at the full 20 pt pitch.)
            // A multiline value that OVERFLOWS its box shrinks to fit when the field
            // cannot scroll (DoNotScroll, /Ff bit 24) or FitIntoRectangle asks for it:
            // a 10 pt /DA re-sizes down to ~8 for a three-line value in
            // a 36 pt box rather than dropping the lines below the border.
            // The fitted size budgets each display line 1.5 em of BOX height —
            // fs = h / (1.5 · n) — measured EXACT (a 36.003 pt box
            // holding three lines fits at 36.003/4.5 = 8.000667). n depends on the
            // size through soft wrap, so the law is applied to a fixed point; the
            // /DA size stays a ceiling, and only a value whose drawn stack would
            // lose lines (h − 2 < n · line pitch, the emit cull below) shrinks.
            var nDa = System.Math.Max(1, WrapMultilineText(text, fontName, fontSize, w - 2).Count);
            if (h - 2 < nDa * ApLineHeight(fontName, fontSize))
            {
                var n = nDa;
                for (var iter = 0; iter < 4; iter++)
                {
                    var s = System.Math.Max(4, h / (1.5 * n));
                    var n2 = System.Math.Max(1, WrapMultilineText(text, fontName, s, w - 2).Count);
                    if (n2 == n) { n = n2; break; }
                    n = n2;
                }
                var fitted = System.Math.Max(4, h / (1.5 * n));
                if (fitted < fontSize) fontSize = fitted;
            }
        }

        // Honour the global TextBoxField auto-fit clamps, but ONLY over a size the field
        // worked out for itself: they bound the AUTO-FIT, not the document. A /DA that
        // names its own size keeps it - measured with the globals
        // pinned to 15/15, where a 10 pt field still writes "Tf 10" and steps its lines by
        // 10 pt. Applying them unconditionally let one field''s clamp follow the process
        // into every later document, because the properties are STATIC.
        if (!DaFontSizePinned)
        {
            if (TextBoxField.MinFontSize > 0) fontSize = System.Math.Max(fontSize, TextBoxField.MinFontSize);
            if (TextBoxField.MaxFontSize > 0) fontSize = System.Math.Min(fontSize, TextBoxField.MaxFontSize);
        }

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
                // '\n'/'\r' always breaks; line text is kept verbatim.
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
                // A PLAIN multiline field trims trailing whitespace at each line
                // break; a RICH-TEXT field (Ff bit 26) keeps the line verbatim —
                // its trailing space belongs to the /RV run (the expected /AP
                // shows "Value to be filled in " with the space on the rich field
                // and "Value to be filled in" on the plain one).
                var lines = rawLines.ToArray();
                if (!IsRichText)
                    for (int li = 0; li < lines.Length; li++)
                        lines[li] = lines[li].TrimEnd();

                // A /DA font the caller OPENED (its program travels with the field)
                // paces the block on the size itself, not on the face's bbox: the
                // pitch is a flat 1.2 em and the first line box hangs one em from
                // the inner top with the baseline riding the face's own typographic
                // descent above that box's bottom. A font named rather than opened
                // (Standard-14, a system family) keeps the bbox model below, whose
                // pitch is the font PROGRAM's head-bbox height × size (the /DR
                // FontFile2 face, an AFM box for Standard-14, else the system face).
                // Both measured; an explicit style line-height
                // (rich-text fields) outranks either.
                var authoredFace = DrEmbeddedFaceProgram(fontName);
                // A face the caller NAMED in a Style string paces by its own line pitch
                // (StyleFacePitchEm); a face loaded into /DR keeps the bbox model.
                var styleNamed = StyleNamedFace && authoredFace is null && !legacyComposite;
                var lineHeight = StyleLineHeightPt ?? DsLineHeightPt() ?? (authoredFace is not null
                    ? fontSize * AuthoredFacePitchEm
                    : legacyComposite
                        ? fontSize * 1.15
                        : styleNamed
                            ? fontSize * StyleFacePitchEm(fontName, fontSize)
                            : ApLineHeight(fontName, fontSize));
                // First baseline: 2pt below the box top's line slot — H − 2 − L.
                // The composite generator instead seats the first line box at the
                // top with the baseline a typographic descent above its bottom.
                var firstY = authoredFace is not null
                    ? h - WidgetBorderInset - fontSize
                        + System.Math.Abs(
                            Aspose.Pdf.Text.FontRepository.ReadTtfTypoDescentEm(authoredFace)) * fontSize
                    : legacyComposite
                        ? h - lineHeight - ReadTypoDescentEm(fontName) * fontSize
                        : styleNamed && StyleLineHeightPt is null
                            // A style-named block hangs its first baseline one FONT SIZE
                            // below the box top, whatever the face: measured 62 / 52 / 65
                            // in a 72 pt box at 10 / 20 / 7 pt, identical across every face
                            // probed.
                            ? h - fontSize
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
                var slComposite = compositeCmap is not null
                                  || ResolveDrFontDict(fontName)?.GetName("Subtype") == "Type0";
                // A style-named face centres its single line on the box above and seats
                // the baseline at its foot - no descender term: measured exactly on
                // Helvetica (30.255 at 10 pt, 24.51 at 20) and on every other face probed.
                var slStyleNamed = StyleNamedFace && !slComposite
                                   && StyleLineHeightPt is null && DsLineHeightPt() is null
                                   && DrEmbeddedFaceProgram(fontName) is null;
                var slY = slComposite
                    ? h / 2 - fontSize * 0.3
                    : slStyleNamed
                        ? (h - fontSize * StyleFaceSingleLineBoxEm(fontName, fontSize)) / 2
                        : (h - (StyleLineHeightPt ?? DsLineHeightPt() ?? ApLineHeight(fontName, fontSize))) / 2
                          + fontSize * ApDescent(fontName) / 1000.0;
                textBody = $"{Format(tx)} {Format(slY)} Td\n{ShowOp(text)} Tj\n";
            }

            // The colour op precedes Tf — the writer sets the /DA colour
            // before selecting the font (a content parser that snapshots state per
            // Tf then sees the first run in the field colour).
            content = BuildBorderPrelude(w, h) +
                      $"/Tx BMC\nq\nBT\n{ExtractDaColor(da)}\n/{(uniFontDict is not null ? uniRes : fontName)} {Format(fontSize)} Tf\n" +
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
