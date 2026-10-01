using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
    /// <summary>The text-object, text-matrix and spacing operators the target-position test reads.</summary>
    private void TrackColorTextMatrix(ForegroundColorState fc, string op)
    {
        switch (op)
        {
            case "BT":
                fc.tmA = 1; fc.tmB = 0; fc.tmC = 0; fc.tmD = 1; fc.tmTx = 0; fc.tmTy = 0;
                fc.tlLeading = 0;
                fc.penTx = 0;
                break;
            case "Td":
            case "TD":
                if (fc.operands.Count >= 2)
                {
                    double dx = ToDouble(fc.operands[0].obj);
                    double dy = ToDouble(fc.operands[1].obj);
                    fc.tmTx = dx * fc.tmA + dy * fc.tmC + fc.tmTx;
                    fc.tmTy = dx * fc.tmB + dy * fc.tmD + fc.tmTy;
                    if (op == "TD") fc.tlLeading = -dy;
                    fc.penTx = fc.tmTx;
                }
                break;
            case "Tm":
                if (fc.operands.Count >= 6)
                {
                    fc.tmA = ToDouble(fc.operands[0].obj);
                    fc.tmB = ToDouble(fc.operands[1].obj);
                    fc.tmC = ToDouble(fc.operands[2].obj);
                    fc.tmD = ToDouble(fc.operands[3].obj);
                    fc.tmTx = ToDouble(fc.operands[4].obj);
                    fc.tmTy = ToDouble(fc.operands[5].obj);
                    fc.penTx = fc.tmTx;
                }
                break;
            case "TL":
                if (fc.operands.Count >= 1) fc.tlLeading = ToDouble(fc.operands[0].obj);
                break;
            case "T*":
                fc.tmTx = -fc.tlLeading * fc.tmC + fc.tmTx;
                fc.tmTy = -fc.tlLeading * fc.tmD + fc.tmTy;
                fc.penTx = fc.tmTx;
                break;
            case "Tc":
                if (fc.operands.Count >= 1) fc.charSpacing = ToDouble(fc.operands[^1].obj);
                break;
            case "Tw":
                if (fc.operands.Count >= 1) fc.wordSpacing = ToDouble(fc.operands[^1].obj);
                break;
            case "Tz":
                if (fc.operands.Count >= 1) fc.hScaling = ToDouble(fc.operands[^1].obj) / 100.0;
                break;
        }
    }

    /// <summary>The q/Q stack, the CTM, the font, the fill colour and the render mode.</summary>
    private void TrackColorGraphicsState(ForegroundColorState fc, int endPos, byte[] streamBytes, PdfReader reader, string op)
    {
        switch (op)
        {
            case "q":
                fc.ctmStack.Push((fc.ctmA, fc.ctmB, fc.ctmC, fc.ctmD, fc.ctmTx, fc.ctmTy));
                break;
            case "Q":
                if (fc.ctmStack.Count > 0)
                    (fc.ctmA, fc.ctmB, fc.ctmC, fc.ctmD, fc.ctmTx, fc.ctmTy) = fc.ctmStack.Pop();
                break;
            case "cm":
                if (fc.operands.Count >= 6)
                {
                    double a = ToDouble(fc.operands[0].obj);
                    double b = ToDouble(fc.operands[1].obj);
                    double c = ToDouble(fc.operands[2].obj);
                    double d = ToDouble(fc.operands[3].obj);
                    double tx = ToDouble(fc.operands[4].obj);
                    double ty = ToDouble(fc.operands[5].obj);
                    var newA = a * fc.ctmA + b * fc.ctmC;
                    var newB = a * fc.ctmB + b * fc.ctmD;
                    var newC = c * fc.ctmA + d * fc.ctmC;
                    var newD = c * fc.ctmB + d * fc.ctmD;
                    var newTx = tx * fc.ctmA + ty * fc.ctmC + fc.ctmTx;
                    var newTy = tx * fc.ctmB + ty * fc.ctmD + fc.ctmTy;
                    fc.ctmA = newA; fc.ctmB = newB; fc.ctmC = newC; fc.ctmD = newD;
                    fc.ctmTx = newTx; fc.ctmTy = newTy;
                }
                break;
            case "Tf":
                if (fc.operands.Count >= 2 && fc.operands[0].obj is PdfName fn)
                {
                    fc.currentFontName = fn.Value;
                    fc.fontSize = ToDouble(fc.operands[^1].obj);
                    if (fc.fonts.TryGetValue(fc.currentFontName, out var fontDict))
                    {
                        fc.currentToUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, reader);
                        try { fc.currentMetrics = FontMetrics.FromFontDict(fontDict, reader); }
                        catch { fc.currentMetrics = null; }
                    }
                    else
                    {
                        fc.currentToUnicode = null;
                        fc.currentMetrics = null;
                    }
                }
                break;
            case "rg":
                if (fc.operands.Count >= 3)
                {
                    fc.fillR = ToDouble(fc.operands[^3].obj);
                    fc.fillG = ToDouble(fc.operands[^2].obj);
                    fc.fillB = ToDouble(fc.operands[^1].obj);
                    fc.fillOpText = Verbatim(streamBytes, fc.operands[^3].startPos, endPos);
                }
                break;
            case "g":
                if (fc.operands.Count >= 1)
                {
                    fc.fillR = fc.fillG = fc.fillB = ToDouble(fc.operands[^1].obj);
                    fc.fillOpText = Verbatim(streamBytes, fc.operands[^1].startPos, endPos);
                }
                break;
            case "k":
                if (fc.operands.Count >= 4)
                {
                    double c = ToDouble(fc.operands[^4].obj), m = ToDouble(fc.operands[^3].obj);
                    double y = ToDouble(fc.operands[^2].obj), kk = ToDouble(fc.operands[^1].obj);
                    fc.fillR = (1 - c) * (1 - kk);
                    fc.fillG = (1 - m) * (1 - kk);
                    fc.fillB = (1 - y) * (1 - kk);
                    fc.fillOpText = Verbatim(streamBytes, fc.operands[^4].startPos, endPos);
                }
                break;
            case "Tr":
                if (fc.operands.Count >= 1) fc.trMode = (int)ToDouble(fc.operands[^1].obj);
                break;
        }
    }

    /// <summary>The Do operator: recolour inside a form XObject under the composed CTM.</summary>
    private byte[]? RecolorInFormXObject(ForegroundColorState fc, byte[] streamBytes, string text, Color color, double? targetY, double? targetX, PdfDictionary pageDict, PdfReader reader, string op)
    {
        switch (op)
        {
            case "Do":
                // Recurse into Form XObjects with current CTM as initial state.
                if (fc.operands.Count >= 1 && fc.operands[0].obj is PdfName xobjName)
                {
                    var pageRes = reader.ResolveDict(pageDict.Get("Resources"));
                    var xobjsDict = pageRes is null ? null
                        : reader.ResolveDict(pageRes.Get("XObject"));
                    var xobjRef = xobjsDict?.Get(xobjName.Value);
                    if (xobjRef is not null)
                    {
                        var xobjStream = reader.ResolveStream(xobjRef);
                        if (xobjStream is not null
                            && xobjStream.Dict.GetName("Subtype") == "Form")
                        {
                            var xobjBytes = reader.DecodeStream(xobjStream);
                            var modified = ModifyForegroundColorInStream(xobjBytes,
                                text, color, targetY, targetX,
                                xobjStream.Dict, reader,
                                fc.ctmA, fc.ctmB, fc.ctmC, fc.ctmD, fc.ctmTx, fc.ctmTy);
                            if (modified is not null)
                            {
                                xobjStream.Dict.Remove("Filter");
                                xobjStream.Dict.Remove("DecodeParms");
                                xobjStream.Dict.Set("Length", new PdfInteger(modified.Length));
                                xobjStream.ReplaceData(modified);
                                return streamBytes; // signal "modified" — we changed the XObject
                            }
                        }
                    }
                }
                break;
        }
        return null;
    }

    /// <summary>The Tj, ' and " operators: recolour the shown string when it carries the target.</summary>
    private byte[]? RecolorShownString(ForegroundColorState fc, int endPos, byte[] streamBytes, string text, Color color, double? targetX, bool nearestX, int? renderingMode, string op)
    {
        switch (op)
        {
            case "Tj":
            case "'":
            case "\"":
                // ' and " move to the next line before showing.
                if (op is "'" or "\"")
                {
                    fc.tmTx = -fc.tlLeading * fc.tmC + fc.tmTx;
                    fc.tmTy = -fc.tlLeading * fc.tmD + fc.tmTy;
                    fc.penTx = fc.tmTx;
                }
                if (fc.operands.Count >= 1 && fc.operands[^1].obj is PdfString s)
                {
                    var occ = MatchesY(fc)
                        ? PickOccurrence(fc, DecodeTextString(s.Value, fc.currentToUnicode), null, s.Value)
                        : -1;
                    if (MatchesY(fc) && (MatchesX(fc) || occ >= 0))
                    {
                        var decoded = DecodeTextString(s.Value, fc.currentToUnicode);
                        // In nearest-X mode every candidate is only a candidate:
                        // skip building the rewrite for one that is already further
                        // from the target than the best seen, so a page with many
                        // occurrences does not copy the whole stream per occurrence.
                        if ((decoded.Contains(text) || GeometricallyExact(fc))
                            && !(nearestX && targetX.HasValue && fc.lastOccurrenceGap >= fc.bestGap))
                        {
                            // When the match is only part of the run, split the show
                            // operator so the new colour applies to the matched glyphs
                            // alone and the surrounding glyphs keep the active fill
                            // colour (consecutive Tj operators advance the text matrix
                            // automatically, so the split preserves positioning).
                            var split = SplitColorRun(streamBytes,
                                fc.operands[^1].startPos, fc.operands[^1].endPos,
                                text, color, (fc.fillR, fc.fillG, fc.fillB), occ,
                                renderingMode, fc.trMode)
                                // Whole-run recolour: wrap the show operator with the new
                                // fill colour AND a trailing restore to the colour that was
                                // active before it, so the recolour doesn't leak onto the
                                // subsequent text (endPos is just past the show keyword).
                                ?? InjectColorAround(streamBytes, fc.operands[^1].startPos,
                                    endPos, color, (fc.fillR, fc.fillG, fc.fillB), fc.fillOpText,
                                    renderingMode, fc.trMode);
                            if (!nearestX) return split;
                            if (fc.lastOccurrenceGap < fc.bestGap)
                            {
                                fc.bestGap = fc.lastOccurrenceGap;
                                fc.bestResult = split;
                            }
                        }
                    }
                    // Advances live in Tm-space; the tracked tm coordinates are
                    // Tm-applied (Td folds tmA in), so scale the advance the same way.
                    fc.penTx += StringAdvance(fc, s.Value) * fc.tmA;
                }
                break;
        }
        return null;
    }

    /// <summary>The TJ operator: recolour the concatenated array when it carries the target.</summary>
    private byte[]? RecolorShownArray(ForegroundColorState fc, int endPos, byte[] streamBytes, string text, Color color, double? targetX, bool nearestX, int? renderingMode, string op)
    {
        switch (op)
        {
            case "TJ":
                // The TJ array's text was concatenated into a single PdfString
                // operand by the ArrayStart handler above.
                if (fc.operands.Count >= 1 && fc.operands[^1].obj is PdfString tjText)
                {
                    var tjOcc = MatchesY(fc)
                        ? PickOccurrence(fc, DecodeTextString(tjText.Value, fc.currentToUnicode), fc.tjItems, null)
                        : -1;
                    if (MatchesY(fc) && (MatchesX(fc) || tjOcc >= 0))
                    {
                        var decoded = DecodeTextString(tjText.Value, fc.currentToUnicode);
                        if ((decoded.Contains(text) || GeometricallyExact(fc))
                            && !(nearestX && targetX.HasValue && fc.lastOccurrenceGap >= fc.bestGap))
                        {
                            // Same rule as the Tj branch: recolour only the matched
                            // glyphs. A TJ array carries a whole line, so colouring
                            // the operator as a unit repaints the words either side
                            // of the match too.
                            var splitTj = SplitShowRunTJ(streamBytes,
                                fc.operands[^1].startPos, fc.operands[^1].endPos,
                                text, RgOps(color), RestoreFillOps(fc.fillR, fc.fillG, fc.fillB),
                                fc.currentToUnicode, tjOcc)
                                ?? InjectColorAround(streamBytes, fc.operands[^1].startPos,
                                    endPos, color, (fc.fillR, fc.fillG, fc.fillB), fc.fillOpText,
                                    renderingMode, fc.trMode);
                            if (!nearestX) return splitTj;
                            if (fc.lastOccurrenceGap < fc.bestGap)
                            {
                                fc.bestGap = fc.lastOccurrenceGap;
                                fc.bestResult = splitTj;
                            }
                        }
                    }
                    if (fc.tjItems is not null)
                        foreach (var item in fc.tjItems)
                        {
                            if (item is byte[] strBytes)
                                fc.penTx += StringAdvance(fc, strBytes) * fc.tmA;
                            else if (item is double kern)
                                fc.penTx -= kern / 1000.0 * fc.fontSize * fc.hScaling * fc.tmA;
                        }
                }
                fc.tjItems = null;
                break;
        }
        return null;
    }
}
