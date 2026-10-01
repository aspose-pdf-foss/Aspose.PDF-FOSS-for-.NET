using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Content;

internal sealed partial class ContentStreamParser
{
    /// <summary>The graphics-state and marked-content operators: the q/Q stack, ExtGState, the CTM, stroke parameters and content tags.</summary>
    private void ProcessStateOperator(List<PdfObject> operands, Dictionary<string, PdfDictionary>? extGStates, string op)
    {
        switch (op)
        {
            // Graphics state
            case "q": _state.Save(); break;
            case "Q": _state.Restore(); break;
            case "gs" when operands.Count >= 1 && operands[0] is PdfName gsName:
                ApplyExtGState(gsName.Value, extGStates);
                break;
            case "cm" when operands.Count >= 6:
                _state.ConcatMatrix(Num(operands[0]), Num(operands[1]),
                    Num(operands[2]), Num(operands[3]),
                    Num(operands[4]), Num(operands[5]));
                break;

            // Line attributes
            case "w" when operands.Count >= 1: _state.LineWidth = Num(operands[0]); break;
            case "J" when operands.Count >= 1: _state.LineCap = Int(operands[0]); break;
            case "j" when operands.Count >= 1: _state.LineJoin = Int(operands[0]); break;
            case "M" when operands.Count >= 1: _state.MiterLimit = Num(operands[0]); break;
            case "i" when operands.Count >= 1: _state.Flatness = Num(operands[0]); break;
            case "d" when operands.Count >= 2 && operands[0] is PdfArray dashArr:
                var dash = new double[dashArr.Count];
                for (var di = 0; di < dashArr.Count; di++) dash[di] = Num(dashArr[di]);
                _state.DashArray = dash;
                _state.DashPhase = Num(operands[1]);
                break;

            // Marked content
            case "BMC" when operands.Count >= 1 && operands[0] is PdfName bmcTag:
                OnMarkedContentBegin?.Invoke(bmcTag.Value, null);
                break;
            case "BDC" when operands.Count >= 2 && operands[0] is PdfName bdcTag:
                var bdcProps = operands[1] as PdfDictionary;
                // PDF 32000 §14.6.2: BDC's second operand can be an inline
                // properties dict *or* a name that resolves through the page
                // Resources./Properties entry. For /OC marked content the
                // emitter overwhelmingly uses the name form (`/OC /MC0 BDC`)
                // so without resolving it the renderer can't tell which OCG
                // a content range belongs to.
                if (bdcProps is null && operands[1] is PdfName bdcPropName)
                    bdcProps = _reader.ResolveDict(_properties?.Get(bdcPropName.Value));
                _state.MarkedContentTag = bdcTag.Value;
                // Check for ActualText
                if (bdcProps is not null)
                {
                    var actualText = bdcProps.Get("ActualText");
                    if (actualText is PdfString ats)
                        _state.ActualText = Compat.Latin1.GetString(ats.Value);
                }
                OnMarkedContentBegin?.Invoke(bdcTag.Value, bdcProps);
                break;
            case "EMC":
                _state.MarkedContentTag = null;
                _state.ActualText = null;
                OnMarkedContentEnd?.Invoke();
                break;
        }
    }

    /// <summary>The colour-space and colour operators, fill and stroke.</summary>
    private void ProcessColorOperator(List<PdfObject> operands, string op)
    {
        switch (op)
        {
            // Color space operators — changing color space clears any pattern that was
            // pinned for the previous space (PDF 32000 §8.6.8: cs/CS resets the colour).
            case "cs" when operands.Count >= 1 && operands[0] is PdfName csName:
                _state.FillColorSpace = csName.Value;
                _state.FillPatternName = null;
                break;
            case "CS" when operands.Count >= 1 && operands[0] is PdfName csStrokeName:
                _state.StrokeColorSpace = csStrokeName.Value;
                _state.StrokePatternName = null;
                break;

            // Fill color (color space-based). For /Pattern cs the last operand is a
            // pattern resource name (/P5 scn); numeric operands before it are tint
            // values for uncoloured (PaintType 2) patterns and aren't used for fills.
            case "sc" or "scn":
                ApplyPatternOrColor(operands, isFill: true);
                break;
            case "SC" or "SCN":
                ApplyPatternOrColor(operands, isFill: false);
                break;

            // Fill color — these implicitly reset the colour space to Device* and therefore
            // drop any pinned pattern. Clearing FillPatternName here prevents a stale pattern
            // from overriding a subsequent solid fill on the same state scope.
            case "g" when operands.Count >= 1:
                _state.FillR = _state.FillG = _state.FillB = Num(operands[0]);
                _state.FillPatternName = null;
                break;
            case "rg" when operands.Count >= 3:
                _state.FillR = Num(operands[0]);
                _state.FillG = Num(operands[1]);
                _state.FillB = Num(operands[2]);
                _state.FillPatternName = null;
                break;
            case "k" when operands.Count >= 4:
                var (fr, fg, fb) = CmykToRgb(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3]));
                _state.FillR = fr; _state.FillG = fg; _state.FillB = fb;
                _state.FillPatternName = null;
                break;

            // Stroke color
            case "G" when operands.Count >= 1:
                _state.StrokeR = _state.StrokeG = _state.StrokeB = Num(operands[0]);
                _state.StrokePatternName = null;
                break;
            case "RG" when operands.Count >= 3:
                _state.StrokeR = Num(operands[0]);
                _state.StrokeG = Num(operands[1]);
                _state.StrokeB = Num(operands[2]);
                _state.StrokePatternName = null;
                break;
            case "K" when operands.Count >= 4:
                var (sr, sg, sb) = CmykToRgb(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3]));
                _state.StrokeR = sr; _state.StrokeG = sg; _state.StrokeB = sb;
                _state.StrokePatternName = null;
                break;

        }
    }

    /// <summary>The text object, its state, its positioning and the strings it shows.</summary>
    /// <summary>Reads the font text is shown in from here on: how its codes read and how far each advances.</summary>
    private void SelectFont(string fontName, Dictionary<string, PdfDictionary>? fonts)
    {
        _currentFontKey = fontName;
        if (fonts is null || !fonts.TryGetValue(fontName, out var fontDict)) return;
        _currentToUnicode = Text.TextAbsorber.ParseToUnicodeFromDict(fontDict, _reader)
            ?? BuildEncodingToUnicode(fontDict, _reader);
        try { _currentMetrics = Text.FontMetrics.FromFontDict(fontDict, _reader); }
        catch { _currentMetrics = null; }
        try { _currentCidInfo = Text.CidFontInfo.TryBuild(fontDict, _reader); }
        catch { _currentCidInfo = null; }
    }

    /// <summary>The font is part of the graphics state (PDF 32000 §8.4.1): restoring the state restores the font text was
    /// shown in before it was saved - a header drawn in its own font inside q/Q leaves the text after it in the font it had.</summary>
    private void FollowRestoredFont(Dictionary<string, PdfDictionary>? fonts)
    {
        if (_state.FontName is { } name && name != _currentFontKey) SelectFont(name, fonts);
    }

    private void ProcessTextOperator(List<PdfObject> operands, Dictionary<string, PdfDictionary>? fonts, string op)
    {
        switch (op)
        {
            // Text object
            case "BT":
                _state.InTextObject = true;
                _state.SetTextMatrix(1, 0, 0, 1, 0, 0);
                break;
            case "ET":
                _state.InTextObject = false;
                break;

            // Text state
            case "Tf" when operands.Count >= 2:
                var fontName = (operands[0] as PdfName)?.Value;
                _state.FontName = fontName;
                _state.FontSize = Num(operands[1]);
                if (fontName is not null) SelectFont(fontName, fonts);
                break;
            case "Tc" when operands.Count >= 1: _state.CharSpacing = Num(operands[0]); break;
            case "Tw" when operands.Count >= 1: _state.WordSpacing = Num(operands[0]); break;
            case "Tz" when operands.Count >= 1: _state.HorizontalScaling = Num(operands[0]); break;
            case "TL" when operands.Count >= 1: _state.Leading = Num(operands[0]); break;
            case "Tr" when operands.Count >= 1: _state.RenderingMode = Int(operands[0]); break;
            case "Ts" when operands.Count >= 1: _state.Rise = Num(operands[0]); break;

            // Text positioning
            case "Td" when operands.Count >= 2:
                _state.MoveTextPosition(Num(operands[0]), Num(operands[1]));
                break;
            case "TD" when operands.Count >= 2:
                _state.Leading = -Num(operands[1]);
                _state.MoveTextPosition(Num(operands[0]), Num(operands[1]));
                break;
            case "Tm" when operands.Count >= 6:
                _state.SetTextMatrix(Num(operands[0]), Num(operands[1]),
                    Num(operands[2]), Num(operands[3]),
                    Num(operands[4]), Num(operands[5]));
                break;
            case "T*":
                _state.MoveToNextLine();
                break;

            // Text showing
            case "Tj" when operands.Count >= 1 && operands[0] is PdfString s:
                FireTextShown(s.Value, _currentToUnicode);
                break;
            case "TJ" when operands.Count >= 1 && operands[0] is PdfArray arr:
            {
                // In vertical writing mode (-V CMap) a TJ numeric adjustment displaces
                // the VERTICAL coordinate — a positive number moves the next glyph down
                // (PDF 32000 §9.4.3); Tz applies to horizontal displacements only.
                var tjVertical = _currentCidInfo is { IsVertical: true };
                foreach (var item in arr)
                {
                    if (item is PdfString ts)
                        FireTextShown(ts.Value, _currentToUnicode);
                    else if (item is PdfInteger pi)
                    {
                        if (tjVertical)
                            _state.AdvanceTextPosition(0, -pi.Value / 1000.0 * _state.FontSize);
                        else
                            _state.AdvanceTextPosition(
                                -pi.Value / 1000.0 * _state.FontSize * (_state.HorizontalScaling / 100.0), 0);
                    }
                    else if (item is PdfReal pr)
                    {
                        if (tjVertical)
                            _state.AdvanceTextPosition(0, -pr.Value / 1000.0 * _state.FontSize);
                        else
                            _state.AdvanceTextPosition(
                                -pr.Value / 1000.0 * _state.FontSize * (_state.HorizontalScaling / 100.0), 0);
                    }
                }
                break;
            }
            case "'" when operands.Count >= 1 && operands[0] is PdfString qs:
                _state.MoveToNextLine();
                FireTextShown(qs.Value, _currentToUnicode);
                break;
        }
    }

    /// <summary>The path construction, painting, clipping, shading and XObject operators.</summary>
    private void ProcessPathOperator(List<PdfObject> operands, string op)
    {
        switch (op)
        {
            // XObject (images, forms)
            case "Do" when operands.Count >= 1 && operands[0] is PdfName xName:
                OnImageDrawn?.Invoke(xName.Value, _state);
                break;

            // Path construction — accumulate segments
            case "m" when operands.Count >= 2:
                _pathSegments.Add(new PathCommand(PathOp.MoveTo, Num(operands[0]), Num(operands[1])));
                _subpathOpen = true;
                break;
            case "l" when operands.Count >= 2 && _subpathOpen:
                _pathSegments.Add(new PathCommand(PathOp.LineTo, Num(operands[0]), Num(operands[1])));
                break;
            case "c" when operands.Count >= 6 && _subpathOpen:
                _pathSegments.Add(new PathCommand(PathOp.CurveTo,
                    Num(operands[0]), Num(operands[1]),
                    Num(operands[2]), Num(operands[3]),
                    Num(operands[4]), Num(operands[5])));
                break;
            case "v" when operands.Count >= 4 && _subpathOpen:
                _pathSegments.Add(new PathCommand(PathOp.CurveToV,
                    Num(operands[0]), Num(operands[1]),
                    Num(operands[2]), Num(operands[3])));
                break;
            case "y" when operands.Count >= 4 && _subpathOpen:
                _pathSegments.Add(new PathCommand(PathOp.CurveToY,
                    Num(operands[0]), Num(operands[1]),
                    Num(operands[2]), Num(operands[3])));
                break;
            case "h":
                _pathSegments.Add(new PathCommand(PathOp.Close));
                break;
            case "re" when operands.Count >= 4:
                _pathSegments.Add(new PathCommand(PathOp.Rect,
                    Num(operands[0]), Num(operands[1]),
                    Num(operands[2]), Num(operands[3])));
                _subpathOpen = true;
                break;
            case "m" or "l" or "c" or "v" or "y" or "re":
                _pathBroken = true;
                break; // insufficient operands — ignore

            // Path painting — a W/W* seen since the last `m` gets applied here, after
            // the paint runs (per §8.5.4.2: "the W and W* operators do not actually
            // change the current clipping path until after the painting operator").
            case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "n":
                OnPathPainted?.Invoke(op, _state, _pathSegments);
                if (_pendingClipEvenOdd is { } clipRule)
                {
                    // A partial (op-dropped) path must not become a clip: the missing
                    // segments would leave a sliver that erases the whole group.
                    if (!_pathBroken)
                        OnPathClipped?.Invoke(clipRule, _state, _pathSegments);
                    _pendingClipEvenOdd = null;
                }
                _pathSegments.Clear();
                _subpathOpen = false;
                _pathBroken = false;
                break;

            // Shading-paint operator — fills the current clipping region with the
            // named shading. No path is constructed; the shading is clipped by
            // whatever W/W* was installed in an enclosing q/Q frame.
            case "sh" when operands.Count >= 1 && operands[0] is PdfName shName:
                OnShadingPainted?.Invoke(shName.Value, _state);
                break;

            // Clipping — flag the current path; the intersection happens at the
            // next painting operator so the path's still available to hand over.
            case "W":
                _pendingClipEvenOdd = false;
                break;
            case "W*":
                _pendingClipEvenOdd = true;
                break;

        }
    }
}
