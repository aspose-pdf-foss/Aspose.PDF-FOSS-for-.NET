using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>The Tj operator: replace inside one shown string at the target position.</summary>
    private void ReplaceShownString(ReflowReplaceState rr, int endPos, byte[] streamBytes, string replacement, PdfDictionary pageDict, PdfReader reader, string op)
    {
        switch (op)
        {
            case "Tj":
                if (rr.operands.Count >= 1 && rr.operands[0].obj is PdfString str
                    && IsAtTargetY(rr.tmTx, rr.tmTy, rr.ctmB, rr.ctmD, rr.ctmTy)
                    && IsAtTargetX(rr.tmTx, rr.tmTy, rr.ctmA, rr.ctmC, rr.ctmTx)
                    && RenderModeMatches(rr.renderMode))
                {
                    var decoded = DecodeString(str.Value, rr.currentToUnicode, rr.currentFontDict, reader);
                    var normalizedDecoded = NormalizeForSearch(decoded);
                    var effSearch = ResolveRtlSearch(normalizedDecoded, rr.normalizedSearch);
                    if (MatchesSearch(normalizedDecoded, effSearch))
                    {
                        var newText = ApplyReplace(normalizedDecoded, effSearch, replacement);
                        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_REPLDEBUG") == "1")
                            Console.Error.WriteLine($"[tj-match] decoded='{normalizedDecoded}' search='{effSearch}' new='{newText}' tmY={rr.tmTy:F1} tmX={rr.tmTx:F1}");
                        // Write everything before this operand
                        rr.result.Write(streamBytes, rr.lastWritePos, rr.operands[0].startPos - rr.lastWritePos);

                        if (newText.Length == 0)
                        {
                            // Full deletion: normally drop the show operator entirely so no
                            // empty text-showing operator remains (which would still
                            // be re-extracted as a zero-length fragment). When
                            // KeepEmptyShowOperator is set (form-XObject deletion),
                            // retain an empty `() Tj` so the emptied fragment is still
                            // re-extractable as "" — an emptied form field stays a
                            // zero-length fragment in place.
                            if (KeepEmptyShowOperator)
                                rr.result.Write("() Tj"u8);
                        }
                        else if (AnchorTrailingOnReplace && replacement.Length == 0
                            // Pure deletion of a proper substring under an anchored
                            // mode: the surviving text must keep its exact position,
                            // so split around the match and re-anchor the tail at its
                            // original absolute Tm (same rule as the TJ branch below).
                            // Checked BEFORE the font-switch branch: a deletion never
                            // needs a switch — the split re-emits the surviving run
                            // from its original bytes — while the switch path would
                            // flatten the survivor at the op start. A plain re-encode
                            // would likewise slide it left by the removed advance.
                            && normalizedDecoded.IndexOf(effSearch, StringComparison.Ordinal)
                                == normalizedDecoded.LastIndexOf(effSearch, StringComparison.Ordinal)
                            && WriteAnchoredTJSplit(rr.result, new PdfArray { str }, effSearch, replacement,
                                rr.currentToUnicode, rr.currentFontDict, rr.currentFontSize,
                                rr.tmA, rr.tmB, rr.tmC, rr.tmD, rr.tmTx, rr.tmTy, rr.tcSpacing, rr.twSpacing, reader,
                                NeedsTlmRestore(streamBytes, endPos)))
                        {
                            // Anchored split written.
                        }
                        else if (ForcedCidFallbackFamily is not null || NeedsFontSwitch(newText, rr.currentToUnicode, rr.currentFontDict, reader, AllowSubsetGlyphFallback))
                        {
                            // Anchored modes (None / AdjustSpaceWidth): a match that is a
                            // proper substring of the shown string must not re-flow the
                            // rest of the run — split it like the TJ path, font-switch only
                            // the matched span and re-anchor the tail at its original
                            // position. Flatten (whole-run re-encode) only when the split
                            // doesn't apply (no tail, ambiguous mapping, multi-occurrence).
                            var singleOccurrence = effSearch.Length > 0
                                && normalizedDecoded.IndexOf(effSearch, StringComparison.Ordinal)
                                    == normalizedDecoded.LastIndexOf(effSearch, StringComparison.Ordinal);
                            if (!(AnchorTrailingOnReplace && singleOccurrence
                                  && WriteFontSwitchedTJSplit(rr.result,
                                      new PdfArray { str }, effSearch, replacement,
                                      rr.currentToUnicode, rr.currentFontDict, rr.currentFontName, rr.currentFontSize,
                                      rr.tmA, rr.tmB, rr.tmC, rr.tmD, rr.tmTx, rr.tmTy, rr.tcSpacing, rr.twSpacing, reader, pageDict,
                                      NeedsTlmRestore(streamBytes, endPos), anchored: true)))
                                WriteFontSwitchedReplacement(rr.result, newText, rr.currentFontDict,
                                    rr.currentFontName, rr.currentFontSize, pageDict, reader, "Tj", AllowSubsetGlyphFallback, ForcedCidFallbackFamily);
                        }
                        else
                        {
                            var encoded = EncodeString(newText, rr.currentToUnicode, rr.currentFontDict);
                            WriteStringOperand(rr.result, encoded, str.IsHex);
                            rr.result.Write(" Tj"u8);
                        }
                        rr.lastWritePos = endPos;
                    }
                }
                break;

        }
    }

    /// <summary>The TJ operator: replace inside a kerned array at the target position.</summary>
    private void ReplaceShownArray(ReflowReplaceState rr, int endPos, byte[] streamBytes, string search, string replacement, PdfDictionary pageDict, PdfReader reader, string op)
    {
        switch (op)
        {
            case "TJ":
                if (rr.operands.Count >= 1 && rr.operands[0].obj is PdfArray arr
                    && IsAtTargetY(rr.tmTx, rr.tmTy, rr.ctmB, rr.ctmD, rr.ctmTy)
                    && IsAtTargetX(rr.tmTx, rr.tmTy, rr.ctmA, rr.ctmC, rr.ctmTx)
                    && RenderModeMatches(rr.renderMode))
                {
                    // Pre-check: compute what the replaced text would be to decide
                    // font switch BEFORE encoding (avoids round-trip corruption).
                    var tjOrigText = ConcatenateTJText(arr, rr.currentToUnicode, rr.currentFontDict, reader);
                    var tjNormalizedOrig = NormalizeForSearch(tjOrigText);
                    var tjNormalizedSearch = ResolveRtlSearch(tjNormalizedOrig, NormalizeForSearch(search));
                    if (MatchesSearch(tjNormalizedOrig, tjNormalizedSearch))
                    {
                        RewriteShownArray(rr, arr, tjNormalizedOrig, tjNormalizedSearch, endPos, streamBytes, search, replacement, pageDict, reader);
                    }
                }
                break;

        }
    }

    /// <summary>The ' operator: advance a line, then replace as Tj does.</summary>
    private void ReplaceNextLineString(ReflowReplaceState rr, int endPos, byte[] streamBytes, string replacement, PdfDictionary pageDict, PdfReader reader, string op)
    {
        switch (op)
        {
            case "'":
                // ' implicitly does T* before showing — advance the
                // text matrix in text space (dy = -leading) so
                // IsAtTargetY sees the post-T* position.
                rr.tmTx = -rr.tlLeading * rr.tmC + rr.tmTx;
                rr.tmTy = -rr.tlLeading * rr.tmD + rr.tmTy;
                if (rr.operands.Count >= 1 && rr.operands[0].obj is PdfString str2
                    && IsAtTargetY(rr.tmTx, rr.tmTy, rr.ctmB, rr.ctmD, rr.ctmTy)
                    && IsAtTargetX(rr.tmTx, rr.tmTy, rr.ctmA, rr.ctmC, rr.ctmTx)
                    && RenderModeMatches(rr.renderMode))
                {
                    var decoded = DecodeString(str2.Value, rr.currentToUnicode, rr.currentFontDict, reader);
                    var normalizedDecoded2 = NormalizeForSearch(decoded);
                    if (MatchesSearch(normalizedDecoded2, rr.normalizedSearch))
                    {
                        var newText = ApplyReplace(normalizedDecoded2, rr.normalizedSearch, replacement);

                        rr.result.Write(streamBytes, rr.lastWritePos, rr.operands[0].startPos - rr.lastWritePos);
                        if (ForcedCidFallbackFamily is not null || NeedsFontSwitch(newText, rr.currentToUnicode, rr.currentFontDict, reader, AllowSubsetGlyphFallback))
                        {
                            WriteFontSwitchedReplacement(rr.result, newText, rr.currentFontDict,
                                rr.currentFontName, rr.currentFontSize, pageDict, reader, "'", AllowSubsetGlyphFallback, ForcedCidFallbackFamily);
                        }
                        else
                        {
                            var encoded = EncodeString(newText, rr.currentToUnicode, rr.currentFontDict);
                            WriteStringOperand(rr.result, encoded, str2.IsHex);
                            rr.result.Write(" '"u8);
                        }
                        rr.lastWritePos = endPos;
                    }
                }
                break;

        }
    }

    /// <summary>The text-object and text-matrix operators the target-position test reads.</summary>
    private void TrackTextMatrix(ReflowReplaceState rr, string op)
    {
        switch (op)
        {
            case "BT":
                rr.tmA = 1; rr.tmB = 0; rr.tmC = 0; rr.tmD = 1; rr.tmTx = 0; rr.tmTy = 0;
                rr.tlLeading = 0;
                break;

            case "Td":
            case "TD":
                // Td translates in TEXT SPACE: new TM = [1 0 0 1 dx dy] × current TM.
                // For ty: newTy = dx*tm.b + dy*tm.d + tm.ty.
                if (rr.operands.Count >= 2)
                {
                    double dx = ToDouble(rr.operands[0].obj);
                    double dy = ToDouble(rr.operands[1].obj);
                    rr.tmTx = dx * rr.tmA + dy * rr.tmC + rr.tmTx;
                    rr.tmTy = dx * rr.tmB + dy * rr.tmD + rr.tmTy;
                    if (op == "TD") rr.tlLeading = -dy;
                }
                break;

            case "Tm":
                // Tm sets the text matrix absolutely.
                if (rr.operands.Count >= 6)
                {
                    rr.tmA = ToDouble(rr.operands[0].obj);
                    rr.tmB = ToDouble(rr.operands[1].obj);
                    rr.tmC = ToDouble(rr.operands[2].obj);
                    rr.tmD = ToDouble(rr.operands[3].obj);
                    rr.tmTx = ToDouble(rr.operands[4].obj);
                    rr.tmTy = ToDouble(rr.operands[5].obj);
                }
                break;

            case "TL":
                if (rr.operands.Count >= 1)
                    rr.tlLeading = ToDouble(rr.operands[0].obj);
                break;

            case "T*":
                // T* is equivalent to `0 -leading Td` — translate dy=-leading in text space.
                rr.tmTx = -rr.tlLeading * rr.tmC + rr.tmTx;
                rr.tmTy = -rr.tlLeading * rr.tmD + rr.tmTy;
                break;

        }
    }

    /// <summary>The font, spacing, render-mode and CTM operators, saved and restored with q/Q.</summary>
    private void TrackGraphicsState(ReflowReplaceState rr, PdfReader reader, string op)
    {
        switch (op)
        {
            case "Tf":
                if (rr.operands.Count >= 2 && rr.operands[0].obj is PdfName fontName)
                {
                    rr.currentFontName = fontName.Value;
                    if (rr.operands[1].obj is PdfInteger fi) rr.currentFontSize = fi.Value;
                    else if (rr.operands[1].obj is PdfReal fr) rr.currentFontSize = fr.Value;
                    if (rr.fonts.TryGetValue(rr.currentFontName, out var fontDict))
                    {
                        rr.currentFontDict = fontDict;
                        rr.currentToUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, reader);
                    }
                    else
                    {
                        rr.currentFontDict = null;
                        rr.currentToUnicode = null;
                    }
                }
                break;

            case "q":
                rr.ctmStack.Push((rr.ctmA, rr.ctmB, rr.ctmC, rr.ctmD, rr.ctmTx, rr.ctmTy));
                rr.trStack.Push(rr.renderMode);
                rr.spacingStack.Push((rr.tcSpacing, rr.twSpacing));
                break;

            case "Q":
                if (rr.ctmStack.Count > 0)
                    (rr.ctmA, rr.ctmB, rr.ctmC, rr.ctmD, rr.ctmTx, rr.ctmTy) = rr.ctmStack.Pop();
                if (rr.trStack.Count > 0)
                    rr.renderMode = rr.trStack.Pop();
                if (rr.spacingStack.Count > 0)
                    (rr.tcSpacing, rr.twSpacing) = rr.spacingStack.Pop();
                break;

            case "Tr":
                if (rr.operands.Count >= 1)
                    rr.renderMode = (int)ToDouble(rr.operands[0].obj);
                break;

            case "Tc":
                if (rr.operands.Count >= 1)
                    rr.tcSpacing = ToDouble(rr.operands[0].obj);
                break;

            case "Tw":
                if (rr.operands.Count >= 1)
                    rr.twSpacing = ToDouble(rr.operands[0].obj);
                break;

            case "cm":
                if (rr.operands.Count >= 6)
                {
                    double a = ToDouble(rr.operands[0].obj);
                    double b = ToDouble(rr.operands[1].obj);
                    double c = ToDouble(rr.operands[2].obj);
                    double d = ToDouble(rr.operands[3].obj);
                    double tx = ToDouble(rr.operands[4].obj);
                    double ty = ToDouble(rr.operands[5].obj);
                    // Pre-multiply current CTM by operator matrix per PDF 32000 §8.3.2.
                    var newA = a * rr.ctmA + b * rr.ctmC;
                    var newB = a * rr.ctmB + b * rr.ctmD;
                    var newC = c * rr.ctmA + d * rr.ctmC;
                    var newD = c * rr.ctmB + d * rr.ctmD;
                    var newTx = tx * rr.ctmA + ty * rr.ctmC + rr.ctmTx;
                    var newTy = tx * rr.ctmB + ty * rr.ctmD + rr.ctmTy;
                    rr.ctmA = newA; rr.ctmB = newB; rr.ctmC = newC; rr.ctmD = newD;
                    rr.ctmTx = newTx; rr.ctmTy = newTy;
                }
                break;

        }
    }

    /// <summary>The Do operator: recurse into a form XObject under the composed CTM.</summary>
    private void ReplaceInFormXObject(ReflowReplaceState rr, string search, string replacement, PdfDictionary pageDict, PdfReader reader, HashSet<int> processedXObjects, string op)
    {
        switch (op)
        {
            case "Do":
                // Recurse into the referenced Form XObject with the
                // current CTM as initial state, so the parent's cm
                // composition flows into the XObject's text-matrix
                // math (TargetY scoping needs that for content
                // authored as `parent: cm Do` + `xobj: Td Tj`).
                if (rr.operands.Count >= 1 && rr.operands[0].obj is PdfName xobjName)
                {
                    var pageRes = reader.ResolveDict(pageDict.Get("Resources"));
                    var xobjsDict = pageRes is null ? null
                        : reader.ResolveDict(pageRes.Get("XObject"));
                    var xobjRef = xobjsDict?.Get(xobjName.Value);
                    int? objNum = (xobjRef as PdfIndirectRef)?.ObjectNumber;
                    bool firstVisit = objNum is null || processedXObjects.Add(objNum.Value);
                    if (firstVisit && xobjRef is not null)
                    {
                        var xobjStream = reader.ResolveStream(xobjRef);
                        if (xobjStream is not null
                            && reader.ResolveName(xobjStream.Dict, "Subtype") == "Form"
                            && FormMayShowText(xobjStream, reader, out var xobjBytes))
                        {
                            var beforeXobj = _replacementCount;
                            // A Form XObject without its own /Resources inherits the
                            // invoking page's (legacy-style PDFs): resolve fonts and
                            // nested XObjects against the parent dict, else Tf lookups
                            // inside the form come up empty and the anchored-split /
                            // metrics paths silently degrade to metric-less rewrites.
                            var xobjOwnRes = reader.ResolveDict(xobjStream.Dict.Get("Resources"));
                            var xobjScope = xobjOwnRes is null ? pageDict : xobjStream.Dict;
                            var xobjReplaced = ReplaceInContentStream(xobjBytes,
                                search, replacement,
                                xobjScope, reader, processedXObjects,
                                rr.ctmA, rr.ctmB, rr.ctmC, rr.ctmD, rr.ctmTx, rr.ctmTy);
                            if (_walkSawText) rr.sawText = true;
                            else xobjStream.ShowsNoText = true;
                            if (_replacementCount > beforeXobj)
                            {
                                xobjStream.Dict.Remove("Filter");
                                xobjStream.Dict.Remove("DecodeParms");
                                xobjStream.ReplaceData(xobjReplaced);
                                // ReplaceData only mutates the in-memory stream;
                                // Save re-emits an existing object solely when it
                                // is registered dirty, so an unmarked XObject edit
                                // silently reverts on save.
                                if (objNum is int xn)
                                    reader.OwnerDocument?.MarkDirty(xn, xobjStream);
                            }
                        }
                    }
                }
                break;

        }
    }

    /// <summary>Consume one lexed token: operands stack up, a keyword dispatches to its operator family.</summary>
    private bool ConsumeReplaceToken(ReflowReplaceState rr, Token token, int startPos, byte[] streamBytes, string search, string replacement, PdfDictionary pageDict, PdfReader reader, HashSet<int> processedXObjects)
    {
        var endPos = (int)rr.lexer.Position;

        switch (token.Kind)
        {
            case TokenKind.Integer:
                rr.operands.Add((token.Kind, new PdfInteger(token.IntValue), startPos, endPos));
                break;
            case TokenKind.Real:
                rr.operands.Add((token.Kind, new PdfReal(token.RealValue), startPos, endPos));
                break;
            case TokenKind.LiteralString:
                rr.operands.Add((token.Kind, new PdfString(token.BytesValue!), startPos, endPos));
                break;
            case TokenKind.HexString:
                rr.operands.Add((token.Kind, new PdfString(token.BytesValue!, isHex: true), startPos, endPos));
                break;
            case TokenKind.Name:
                rr.operands.Add((token.Kind, new PdfName(token.StringValue!), startPos, endPos));
                break;
            case TokenKind.ArrayStart:
            {
                (var arr, var arrEndPos) = ParseContentArrayWithPositions(rr.lexer);
                rr.operands.Add((TokenKind.ArrayStart, arr, startPos, arrEndPos));
                break;
            }
            case TokenKind.Keyword:
            {
                var op = token.StringValue!;
                switch (op)
                {
                    case "Tf": case "q": case "Q": case "Tr": case "Tc": case "Tw": case "cm":
                        TrackGraphicsState(rr, reader, op);
                        break;
                    case "Tj":
                        rr.sawText = true;
                        ReplaceShownString(rr, endPos, streamBytes, replacement, pageDict, reader, op);
                        break;
                    case "TJ":
                        rr.sawText = true;
                        ReplaceShownArray(rr, endPos, streamBytes, search, replacement, pageDict, reader, op);
                        break;
                    case "'":
                        rr.sawText = true;
                        ReplaceNextLineString(rr, endPos, streamBytes, replacement, pageDict, reader, op);
                        break;
                    case "BT": case "Td": case "TD": case "Tm": case "TL": case "T*":
                        TrackTextMatrix(rr, op);
                        break;
                    case "Do":
                        ReplaceInFormXObject(rr, search, replacement, pageDict, reader, processedXObjects, op);
                        break;
                    case "BI":
                        // Write bytes up to (but not including) BI operator
                        rr.result.Write(streamBytes, rr.lastWritePos, startPos - rr.lastWritePos);
                        SkipInlineImage(rr.lexer);
                        rr.lastWritePos = (int)rr.lexer.Position;
                        rr.operands.Clear();
                        return true;
                }

                rr.operands.Clear();
                break;
            }
            default:
                rr.operands.Clear();
                break;
        }
        return true;
    }
}
