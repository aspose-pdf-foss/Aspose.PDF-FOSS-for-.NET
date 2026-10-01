using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Content;

internal sealed partial class ContentStreamParser
{
    /// <summary>
    /// True when the referenced ICC stream is a 3-component scanner-class
    /// ('scnr') profile with an 'RGB ' data signature. Such profiles are input
    /// (capture) profiles, never display RGB: their channel encoding is
    /// Lab-like (channel 0 = L/100, channels 1/2 = (a|b + 128)/255 — verified
    /// against a pdfDocs PANTONE tint transform whose 0-tint output is
    /// (1, 0.496, 0.496) = paper white). Treating their components as display
    /// RGB paints cyan bars red in such files.
    /// </summary>
    private bool IsLabEncodedIcc(PdfObject? streamRef)
    {
        if (_reader.ResolveStream(streamRef) is not { } icc) return false;
        var n = (_reader.Resolve(icc.Dict.Get("N")) as PdfInteger)?.Value ?? 0;
        if (n != 3) return false;
        byte[] bytes;
        try { bytes = _reader.DecodeStream(icc); }
        catch { return false; }
        if (bytes.Length < 20) return false;
        // ICC header: bytes 12–15 = device class, 16–19 = data colour space.
        return bytes[12] == (byte)'s' && bytes[13] == (byte)'c' && bytes[14] == (byte)'n' && bytes[15] == (byte)'r'
            && bytes[16] == (byte)'R' && bytes[17] == (byte)'G' && bytes[18] == (byte)'B' && bytes[19] == (byte)' ';
    }

    private void ProcessOperator(string op, List<PdfObject> operands,
        Dictionary<string, PdfDictionary>? fonts,
        Dictionary<string, PdfDictionary>? extGStates)
    {
        switch (op)
        {
            case "q": case "Q": case "gs": case "cm": case "w": case "J": case "j": case "M": case "i": case "d": case "BMC": case "BDC": case "EMC":
                ProcessStateOperator(operands, extGStates, op);
                if (op == "Q") FollowRestoredFont(fonts);
                break;
            case "cs": case "CS": case "sc": case "scn": case "SC": case "SCN": case "g": case "rg": case "k": case "G": case "RG": case "K":
                ProcessColorOperator(operands, op);
                break;
            case "BT": case "ET": case "Tf": case "Tc": case "Tw": case "Tz": case "TL": case "Tr": case "Ts": case "Td": case "TD": case "Tm": case "T*": case "Tj": case "TJ": case "'":
                ProcessTextOperator(operands, fonts, op);
                break;
            case "\"" when operands.Count >= 3 && operands[2] is PdfString dqs:
                _state.WordSpacing = Num(operands[0]);
                _state.CharSpacing = Num(operands[1]);
                _state.MoveToNextLine();
                FireTextShown(dqs.Value, _currentToUnicode);
                break;

            case "Do": case "m": case "l": case "c": case "v": case "y": case "h": case "re": case "S": case "s": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*": case "n": case "sh": case "W": case "W*":
                ProcessPathOperator(operands, op);
                break;
        }
    }

    private void ApplyExtGState(string name, Dictionary<string, PdfDictionary>? extGStates)
    {
        _state.ExtGStateName = name;

        if (extGStates is null || !extGStates.TryGetValue(name, out var gsDict))
            return;

        // Fill opacity (ca)
        var ca = gsDict.Get("ca");
        if (ca is PdfReal caR) _state.FillAlpha = caR.Value;
        else if (ca is PdfInteger caI) _state.FillAlpha = caI.Value;

        // Stroke opacity (CA)
        var sCA = gsDict.Get("CA");
        if (sCA is PdfReal scaR) _state.StrokeAlpha = scaR.Value;
        else if (sCA is PdfInteger scaI) _state.StrokeAlpha = scaI.Value;

        // Blend mode (BM)
        var bm = gsDict.Get("BM");
        if (bm is PdfName bmName) _state.BlendMode = bmName.Value;

        // Overprint (OP for stroke, op for fill)
        var opStroke = gsDict.Get("OP");
        if (opStroke is PdfBoolean opS) _state.OverprintStroke = opS.Value;

        var opFill = gsDict.Get("op");
        if (opFill is PdfBoolean opF) _state.OverprintFill = opF.Value;

        // Line width (LW)
        var lw = gsDict.Get("LW");
        if (lw is PdfReal lwR) _state.LineWidth = lwR.Value;
        else if (lw is PdfInteger lwI) _state.LineWidth = lwI.Value;

        // Line cap (LC)
        var lc = gsDict.Get("LC");
        if (lc is PdfInteger lcI) _state.LineCap = (int)lcI.Value;

        // Line join (LJ)
        var lj = gsDict.Get("LJ");
        if (lj is PdfInteger ljI) _state.LineJoin = (int)ljI.Value;

        // Miter limit (ML)
        var ml = gsDict.Get("ML");
        if (ml is PdfReal mlR) _state.MiterLimit = mlR.Value;
        else if (ml is PdfInteger mlI) _state.MiterLimit = mlI.Value;

        // Flatness (FL)
        var fl = gsDict.Get("FL");
        if (fl is PdfReal flR) _state.Flatness = flR.Value;
        else if (fl is PdfInteger flI) _state.Flatness = flI.Value;

        // Font (Font array: [fontRef size])
        var font = gsDict.Get("Font") as PdfArray;
        if (font is { Count: >= 2 })
        {
            if (font[1] is PdfReal fSize) _state.FontSize = fSize.Value;
            else if (font[1] is PdfInteger fSizeI) _state.FontSize = fSizeI.Value;
        }

        // Soft mask (/SMask). Per PDF 32000 §11.6.5.4 this is either the name
        // /None (clear the mask) or a soft-mask dictionary {/Type /Mask, /S, /G,
        // /BC?, /TR?}. The mask group is rendered in the CTM that's active when
        // gs runs, NOT at paint-time, so we snapshot Ctm here.
        var smaskObj = gsDict.Get("SMask");
        if (smaskObj is PdfName smaskName && smaskName.Value == "None")
        {
            _state.SoftMask = null;
        }
        else
        {
            var smaskDict = _reader.ResolveDict(smaskObj);
            if (smaskDict is not null && smaskDict.GetName("Type") is null or "Mask")
            {
                _state.SoftMask = new SoftMaskInfo
                {
                    Dict = smaskDict,
                    Subtype = smaskDict.GetName("S") ?? "Luminosity",
                    Ctm = (double[])_state.Ctm.Clone(),
                };
            }
        }
    }

    /// <summary>
    /// Dispatch <c>scn</c>/<c>SCN</c> to pattern or solid-colour handling based on the
    /// current colour space. Pattern operands end in a <see cref="PdfName"/>; solid
    /// colours use 1/3/4 numerics (gray/RGB/CMYK).
    /// </summary>
    private void ApplyPatternOrColor(List<PdfObject> operands, bool isFill)
    {
        var cs = isFill ? _state.FillColorSpace : _state.StrokeColorSpace;
        if ((cs == "Pattern" || (cs is not null && _patternColorSpaces.Contains(cs)))
            && operands.Count >= 1 && operands[^1] is PdfName patName)
        {
            if (isFill) _state.FillPatternName = patName.Value;
            else _state.StrokePatternName = patName.Value;
            return;
        }
        // A bare /Pattern colour space: route to the pattern renderer for both shading
        // patterns (PatternType 2) and coloured tiling patterns (PatternType 1), which
        // the renderer now rasterises (FillWith[Tiling|Shading]Pattern). Without this a
        // bare-pattern `scn` carries no numeric colour operands, so the fill falls back
        // to the last solid colour (typically black) and paints the whole region opaque.
        if (cs is not null && _barePatternColorSpaces.Contains(cs)
            && operands.Count >= 1 && operands[^1] is PdfName barePat
            && IsRenderablePattern(barePat.Value))
        {
            if (isFill) _state.FillPatternName = barePat.Value;
            else _state.StrokePatternName = barePat.Value;
            return;
        }
        // Clear any lingering pattern so switching from pattern fill to solid doesn't
        // leave the old pattern name overriding subsequent rgb/g/k operators.
        if (isFill) _state.FillPatternName = null;
        else _state.StrokePatternName = null;

        // /Separation and /DeviceN colorspaces: the scn operands are tint values
        // that the colorspace's tint transform function turns into colour
        // components in the alternate space. Without this, `1 scn` on a
        // /Separation /PANTONE 1805 C space defaults to gray=1.0 (white) and any
        // orange text drawn that way renders invisible against a white background.
        // Scanner-class ICC space fed OUT-OF-RANGE operands: some producers
        // (pdfDocs) write RAW Lab values against such a space ("100 -1 -1 scn",
        // "0 -1 -1 scn") — impossible for the profile's [0,1] input range, so
        // the components can't be display RGB. Clamp to [0,1] and decode the
        // profile's Lab-like channel encoding: (1,0,0) = Lab(100,−128,−128) →
        // process cyan; (0,0,0) → a deep blue.
        // IN-RANGE operands on the same profile class stay plain RGB (many
        // scanned files use scnr profiles with ordinary colour components).
        // Direct [/Lab] colorspace: operands ARE L,a,b — convert, don't clamp as RGB.
        if (cs is not null && _labColorSpaces.Contains(cs) && operands.Count >= 3)
        {
            var (dlr, dlg, dlb) = LabColor.ToRgb(Num(operands[0]), Num(operands[1]), Num(operands[2]));
            if (isFill) { _state.FillR = dlr; _state.FillG = dlg; _state.FillB = dlb; }
            else { _state.StrokeR = dlr; _state.StrokeG = dlg; _state.StrokeB = dlb; }
            return;
        }
        if (cs is not null && _labEncColorSpaces.Contains(cs) && operands.Count >= 3)
        {
            var outOfRange = false;
            for (var i = 0; i < 3 && !outOfRange; i++)
            {
                var v = Num(operands[i]);
                if (v < -0.01 || v > 1.5) outOfRange = true;
            }
            if (outOfRange)
            {
                double Ch(int i) { var v = Num(operands[i]); return v < 0 ? 0 : v > 1 ? 1 : v; }
                var (lr, lg, lb) = LabColor.ToRgb(Ch(0) * 100.0, Ch(1) * 255.0 - 128.0, Ch(2) * 255.0 - 128.0);
                if (isFill) { _state.FillR = lr; _state.FillG = lg; _state.FillB = lb; }
                else { _state.StrokeR = lr; _state.StrokeG = lg; _state.StrokeB = lb; }
                return;
            }
            // fall through: ordinary components, ApplyColorOperands maps them as RGB
        }

        if (cs is not null && _tintColorSpaces.TryGetValue(cs, out var tintInfo))
        {
            var inputs = new double[operands.Count];
            for (var i = 0; i < operands.Count; i++) inputs[i] = Num(operands[i]);
            var altComponents = tintInfo.tint.Evaluate(inputs);
            if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_CSDEBUG") == "1")
                Console.Error.WriteLine($"[scn] cs={cs} tint inputs=[{string.Join(",", inputs)}] alt={(altComponents is null ? "NULL" : string.Join(",", altComponents))} space={tintInfo.altSpace}");
            if (altComponents is null) return;
            ApplyAltSpaceComponents(altComponents, tintInfo.altSpace, isFill);
            return;
        }
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_CSDEBUG") == "1")
            Console.Error.WriteLine($"[scn] cs={cs ?? "null"} NO-TINT operands={operands.Count} fill={isFill}");

        ApplyColorOperands(operands, isFill);
    }

    // True when the named pattern resolves to a pattern the renderer can rasterise:
    // a shading pattern (PatternType 2), or a coloured tiling pattern (PatternType 1)
    // whose cell makes direct marks the renderer draws. Both are handled by
    // FillWith[Tiling|Shading]Pattern; anything else (or an unresolvable pattern)
    // returns false so a bare-pattern fill keeps its solid colour rather than pinning
    // a pattern the renderer can't draw.
    private bool IsRenderablePattern(string patternName)
    {
        if (_patterns is null) return false;
        var pat = _reader.Resolve(_patterns.Get(patternName));
        var dict = pat switch
        {
            PdfStream s => s.Dict,
            PdfDictionary d => d,
            _ => null,
        };
        if (dict is null) return false;
        var type = (int)dict.GetInt("PatternType");
        if (type == 2) return true;
        // A tiling pattern (PatternType 1) is only worth pinning when its cell paints
        // with operators the renderer actually rasterises. Cells whose only mark-making
        // operator is `sh` (e.g. a soft-shadow built from free-form/Coons MESH shadings,
        // which the renderer skips) would tile to nothing — pinning them would erase the
        // region, whereas the solid-colour fallback approximates it. So require at least
        // one direct fill/stroke/text/XObject paint operator in the cell content.
        if (type != 1 || pat is not PdfStream cell) return false;
        byte[] content;
        try { content = _reader.DecodeStream(cell); } catch { return false; }
        return TilingCellHasDirectMarks(content);
    }

    // Scan a (decoded) tiling-cell content stream for any direct paint operator,
    // skipping string / hex-string / comment regions so bytes inside them are not
    // mistaken for operators. A lightweight tokenizer is enough: operators are
    // whitespace/delimiter-separated regular-character runs.
    private static bool TilingCellHasDirectMarks(byte[] c)
    {
        int i = 0, n = c.Length;
        var tok = new System.Text.StringBuilder();
        bool CheckTok()
        {
            if (tok.Length == 0) return false;
            bool hit = _directPaintOps.Contains(tok.ToString());
            tok.Clear();
            return hit;
        }
        while (i < n)
        {
            byte b = c[i];
            if (b == (byte)'(') // literal string — skip with balanced parens + escapes
            {
                if (CheckTok()) return true;
                int depth = 1; i++;
                while (i < n && depth > 0)
                {
                    if (c[i] == (byte)'\\') { i += 2; continue; }
                    if (c[i] == (byte)'(') depth++;
                    else if (c[i] == (byte)')') depth--;
                    i++;
                }
                continue;
            }
            if (b == (byte)'%') // comment to end-of-line
            {
                if (CheckTok()) return true;
                while (i < n && c[i] != (byte)'\n' && c[i] != (byte)'\r') i++;
                continue;
            }
            if (b == (byte)'<')
            {
                if (CheckTok()) return true;
                if (i + 1 < n && c[i + 1] == (byte)'<') { i += 2; continue; } // dict open
                i++; // hex string — skip to '>'
                while (i < n && c[i] != (byte)'>') i++;
                i++;
                continue;
            }
            if (b == (byte)'>' || b == (byte)'[' || b == (byte)']'
                || b == (byte)'{' || b == (byte)'}' || b == (byte)'/')
            {
                if (CheckTok()) return true;
                i++;
                continue;
            }
            if (b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or 0)
            {
                if (CheckTok()) return true;
                i++;
                continue;
            }
            tok.Append((char)b);
            i++;
        }
        return CheckTok();
    }

    // Map alternate-space output (from a /Separation or /DeviceN tint function)
    // to the renderer's RGB graphics-state slots. /DeviceCMYK and /DeviceRGB
    // are the alternate spaces almost every Pantone spec uses;
    // /DeviceGray covers the few one-component cases. ICCBased alternates
    // fall through to whatever component count the caller passes — best-
    // effort, since we don't run the ICC profile.
    private void ApplyAltSpaceComponents(double[] comp, string altSpace, bool isFill)
    {
        double r, g, b;
        switch (altSpace)
        {
            case "DeviceCMYK" when comp.Length >= 4:
                (r, g, b) = CmykToRgb(comp[0], comp[1], comp[2], comp[3]);
                break;
            case "DeviceRGB" when comp.Length >= 3:
                r = comp[0]; g = comp[1]; b = comp[2];
                break;
            case "DeviceGray" when comp.Length >= 1:
                r = g = b = comp[0];
                break;
            case "Lab" when comp.Length >= 3:
                (r, g, b) = LabColor.ToRgb(comp[0], comp[1], comp[2]);
                break;
            case "LabEnc" when comp.Length >= 3:
            {
                // Lab-encoded scanner ICC channels (see IsLabEncodedIcc).
                double C(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
                (r, g, b) = LabColor.ToRgb(C(comp[0]) * 100.0, C(comp[1]) * 255.0 - 128.0, C(comp[2]) * 255.0 - 128.0);
                break;
            }
            default:
                // Unknown / unsupported alternate: pick whichever interpretation
                // matches the component count so we at least pass something
                // through the pipeline instead of silently dropping the colour.
                if (comp.Length >= 4) { (r, g, b) = CmykToRgb(comp[0], comp[1], comp[2], comp[3]); }
                else if (comp.Length >= 3) { r = comp[0]; g = comp[1]; b = comp[2]; }
                else if (comp.Length >= 1) { r = g = b = comp[0]; }
                else return;
                break;
        }
        if (isFill) { _state.FillR = r; _state.FillG = g; _state.FillB = b; }
        else { _state.StrokeR = r; _state.StrokeG = g; _state.StrokeB = b; }
    }

    // Resolve the alternate colorspace family name from a /Separation or
    // /DeviceN array's third entry: either a direct name (/DeviceCMYK,
    // /DeviceRGB, /DeviceGray, /CalGray, /CalRGB) or an array whose first
    // element is the family name (/ICCBased, /Lab). Returns null when the
    // family is unrecognised, in which case the colorspace is skipped.
    private string? ResolveAltSpaceName(PdfObject? obj)
    {
        var resolved = _reader.Resolve(obj);
        if (resolved is PdfName n)
        {
            if (n.Value == "DeviceCMYK" || n.Value == "DeviceRGB" || n.Value == "DeviceGray")
                return n.Value;
            // CalGray → 1 component; treat as DeviceGray for our renderer.
            if (n.Value == "CalGray") return "DeviceGray";
            if (n.Value == "CalRGB") return "DeviceRGB";
            return null;
        }
        if (resolved is PdfArray a && a.Count > 0 && a[0] is PdfName fam)
        {
            // /ICCBased [/ICCBased <stream>] — the stream's /N entry gives the
            // component count, but we don't run ICC profiles; fall back to
            // CMYK if N=4, RGB if N=3, Gray if N=1.
            if (fam.Value == "ICCBased" && a.Count > 1 && _reader.ResolveStream(a[1]) is { } iccStream)
            {
                // A scanner-class Lab-encoded ICC alternate: the tint function's output
                // components are L/100, (a+128)/255, (b+128)/255 — route them through
                // the LabEnc decode instead of reading them as display RGB (a spot
                // colour resolving to teal otherwise comes out mauve).
                if (IsLabEncodedIcc(a[1])) return "LabEnc";
                var nObj = iccStream.Dict.Get("N");
                var iccN = nObj switch
                {
                    PdfInteger pi => (int)pi.Value,
                    PdfReal pr => (int)pr.Value,
                    _ => 0,
                };
                return iccN switch { 1 => "DeviceGray", 3 => "DeviceRGB", 4 => "DeviceCMYK", _ => null };
            }
            if (fam.Value == "CalGray") return "DeviceGray";
            if (fam.Value == "CalRGB") return "DeviceRGB";
            if (fam.Value == "Lab") return "Lab";
            return null;
        }
        return null;
    }

    private void ApplyColorOperands(List<PdfObject> operands, bool isFill)
    {
        // Map numeric operands to RGB based on operand count:
        // 1 operand = gray, 3 = RGB, 4 = CMYK
        if (operands.Count >= 4)
        {
            var (cr, cg, cb) = CmykToRgb(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3]));
            if (isFill) { _state.FillR = cr; _state.FillG = cg; _state.FillB = cb; }
            else { _state.StrokeR = cr; _state.StrokeG = cg; _state.StrokeB = cb; }
        }
        else if (operands.Count >= 3)
        {
            var r = Num(operands[0]); var g = Num(operands[1]); var b = Num(operands[2]);
            if (isFill) { _state.FillR = r; _state.FillG = g; _state.FillB = b; }
            else { _state.StrokeR = r; _state.StrokeG = g; _state.StrokeB = b; }
        }
        else if (operands.Count >= 1 && operands[0] is not PdfName)
        {
            var gray = Num(operands[0]);
            if (isFill) { _state.FillR = _state.FillG = _state.FillB = gray; }
            else { _state.StrokeR = _state.StrokeG = _state.StrokeB = gray; }
        }
    }

    private static (double r, double g, double b) CmykToRgb(double c, double m, double y, double k)
    {
        double r = default;
        double g = default;
        double b = default;
        // ICC-style conversion (see CmykToRgbLut): the spec's algebraic formula lands
        // far from the profile-based colours real-world rasterisers emit — pure K black,
        // for instance, converts to a dark grey near (30, 30, 30), not (0, 0, 0).
        var (rb, gb, bb) = Aspose.Pdf.Devices.CmykToRgbLut.Convert(c, m, y, k);
        r = rb / 255.0;
        g = gb / 255.0;
        b = bb / 255.0;
        return (r, g, b);
    }
}
