using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    private byte[] ReplaceInContentStream(byte[] streamBytes, string search, string replacement,
        PdfDictionary pageDict, PdfReader reader,
        HashSet<int>? processedXObjects = null,
        double initCtmA = 1, double initCtmB = 0, double initCtmC = 0, double initCtmD = 1,
        double initCtmTx = 0, double initCtmTy = 0)
    {
        processedXObjects ??= new HashSet<int>();
        var countBefore = _replacementCount;
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var normalizedSearch = NormalizeForSearch(search);
        var lexer = new PdfLexer(streamBytes);
        var result = new MemoryStream();
        var operands = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();
        string? currentFontName = null;
        Dictionary<int, string>? currentToUnicode = null;
        PdfDictionary? currentFontDict = null;
        double currentFontSize = 12.0;
        var lastWritePos = 0;

        // CTM (current transformation matrix) and TM (text matrix) tracking. Both
        // are 6-element matrices [a b c d tx ty]. CTM accumulates from `cm`
        // operators, push/pop on `q`/`Q`. TM is only meaningful inside BT/ET;
        // reset on BT, mutated by Td/TD/T*/Tm. Td translates in TEXT SPACE so
        // the dy from Td maps to ty += dy * tm.d (for axis-aligned Tm; full
        // matrix math handles rotation/skew correctly via tm composition).
        // Together CTM and TM let TargetY scope a per-fragment replace to the
        // right text-showing operator (page-space Y ≈ ctm.d × tm.ty + ctm.ty).
        double ctmA = initCtmA, ctmB = initCtmB, ctmC = initCtmC, ctmD = initCtmD;
        double ctmTx = initCtmTx, ctmTy = initCtmTy;
        var ctmStack = new Stack<(double, double, double, double, double, double)>();
        double tmA = 1, tmB = 0, tmC = 0, tmD = 1, tmTx = 0, tmTy = 0;
        double tlLeading = 0;
        // Text render mode (Tr), tracked for RequiredRenderMode scoping. Part of
        // the graphics state, so it saves/restores with q/Q.
        int renderMode = 0;
        var trStack = new Stack<int>();
        // Character (Tc) and word (Tw) spacing, tracked so the anchored TJ/Tj
        // splits can reproduce the original pen advance of a partially-kept
        // run (both are per-glyph contributions the font metrics don't know).
        // Text-state parameters, so they save/restore with q/Q.
        double tcSpacing = 0, twSpacing = 0;
        var spacingStack = new Stack<(double tc, double tw)>();

        while (true)
        {
            var startPos = (int)lexer.Position;
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            var endPos = (int)lexer.Position;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add((token.Kind, new PdfInteger(token.IntValue), startPos, endPos));
                    break;
                case TokenKind.Real:
                    operands.Add((token.Kind, new PdfReal(token.RealValue), startPos, endPos));
                    break;
                case TokenKind.LiteralString:
                    operands.Add((token.Kind, new PdfString(token.BytesValue!), startPos, endPos));
                    break;
                case TokenKind.HexString:
                    operands.Add((token.Kind, new PdfString(token.BytesValue!, isHex: true), startPos, endPos));
                    break;
                case TokenKind.Name:
                    operands.Add((token.Kind, new PdfName(token.StringValue!), startPos, endPos));
                    break;
                case TokenKind.ArrayStart:
                {
                    var arr = ParseContentArrayWithPositions(lexer, out var arrEndPos);
                    operands.Add((TokenKind.ArrayStart, arr, startPos, arrEndPos));
                    break;
                }
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "Tf":
                            if (operands.Count >= 2 && operands[0].obj is PdfName fontName)
                            {
                                currentFontName = fontName.Value;
                                if (operands[1].obj is PdfInteger fi) currentFontSize = fi.Value;
                                else if (operands[1].obj is PdfReal fr) currentFontSize = fr.Value;
                                if (fonts.TryGetValue(currentFontName, out var fontDict))
                                {
                                    currentFontDict = fontDict;
                                    currentToUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, reader);
                                }
                                else
                                {
                                    currentFontDict = null;
                                    currentToUnicode = null;
                                }
                            }
                            break;

                        case "Tj":
                            if (operands.Count >= 1 && operands[0].obj is PdfString str
                                && IsAtTargetY(tmTx, tmTy, ctmB, ctmD, ctmTy)
                                && IsAtTargetX(tmTx, tmTy, ctmA, ctmC, ctmTx)
                                && RenderModeMatches(renderMode))
                            {
                                var decoded = DecodeString(str.Value, currentToUnicode, currentFontDict, reader);
                                var normalizedDecoded = NormalizeForSearch(decoded);
                                var effSearch = ResolveRtlSearch(normalizedDecoded, normalizedSearch);
                                if (MatchesSearch(normalizedDecoded, effSearch))
                                {
                                    var newText = ApplyReplace(normalizedDecoded, effSearch, replacement);
                                    if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_REPLDEBUG") == "1")
                                        Console.Error.WriteLine($"[tj-match] decoded='{normalizedDecoded}' search='{effSearch}' new='{newText}' tmY={tmTy:F1} tmX={tmTx:F1}");
                                    // Write everything before this operand
                                    result.Write(streamBytes, lastWritePos, operands[0].startPos - lastWritePos);

                                    if (newText.Length == 0)
                                    {
                                        // Full deletion: normally drop the show operator entirely so no
                                        // empty text-showing operator remains (which would still
                                        // be re-extracted as a zero-length fragment). In redaction
                                        // mode, leave a glyph-less advance so following text on the
                                        // line does not reflow. When KeepEmptyShowOperator is set
                                        // (form-XObject deletion), retain an empty `() Tj` so the
                                        // emptied fragment is still re-extractable as "" — an
                                        // emptied form field stays a zero-length fragment in place.
                                        if (KeepEmptyShowOperator)
                                            result.Write("() Tj"u8);
                                        WriteDeletionAdvance(result, str.Value, currentFontDict, reader, currentFontSize);
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
                                        && WriteAnchoredTJSplit(result, new PdfArray { str }, effSearch, replacement,
                                            currentToUnicode, currentFontDict, currentFontSize,
                                            tmA, tmB, tmC, tmD, tmTx, tmTy, tcSpacing, twSpacing, reader,
                                            NeedsTlmRestore(streamBytes, endPos)))
                                    {
                                        // Anchored split written.
                                    }
                                    else if (ForcedCidFallbackFamily is not null || NeedsFontSwitch(newText, currentToUnicode, currentFontDict, reader, AllowSubsetGlyphFallback))
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
                                              && WriteFontSwitchedTJSplit(result,
                                                  new PdfArray { str }, effSearch, replacement,
                                                  currentToUnicode, currentFontDict, currentFontName, currentFontSize,
                                                  tmA, tmB, tmC, tmD, tmTx, tmTy, tcSpacing, twSpacing, reader, pageDict,
                                                  NeedsTlmRestore(streamBytes, endPos), anchored: true)))
                                            WriteFontSwitchedReplacement(result, newText, currentFontDict,
                                                currentFontName, currentFontSize, pageDict, reader, "Tj", AllowSubsetGlyphFallback, ForcedCidFallbackFamily);
                                    }
                                    else
                                    {
                                        var encoded = EncodeString(newText, currentToUnicode, currentFontDict);
                                        WriteStringOperand(result, encoded, str.IsHex);
                                        result.Write(" Tj"u8);
                                    }
                                    lastWritePos = endPos;
                                }
                            }
                            break;

                        case "TJ":
                            if (operands.Count >= 1 && operands[0].obj is PdfArray arr
                                && IsAtTargetY(tmTx, tmTy, ctmB, ctmD, ctmTy)
                                && IsAtTargetX(tmTx, tmTy, ctmA, ctmC, ctmTx)
                                && RenderModeMatches(renderMode))
                            {
                                // Pre-check: compute what the replaced text would be to decide
                                // font switch BEFORE encoding (avoids round-trip corruption).
                                var tjOrigText = ConcatenateTJText(arr, currentToUnicode, currentFontDict, reader);
                                var tjNormalizedOrig = NormalizeForSearch(tjOrigText);
                                var tjNormalizedSearch = ResolveRtlSearch(tjNormalizedOrig, NormalizeForSearch(search));
                                if (MatchesSearch(tjNormalizedOrig, tjNormalizedSearch))
                                {
                                    var tjReplacedText = MatchAnyOperator
                                        ? replacement
                                        : tjNormalizedOrig.Replace(tjNormalizedSearch, replacement, StringComparison.Ordinal);
                                    result.Write(streamBytes, lastWritePos, operands[0].startPos - lastWritePos);

                                    if (tjReplacedText.Length == 0)
                                    {
                                        // Full deletion: drop the entire TJ operator so no
                                        // empty text-showing operator remains (which would
                                        // still be re-extracted as a zero-length fragment). In
                                        // redaction mode, leave a glyph-less advance so following
                                        // text on the line does not reflow. When
                                        // KeepEmptyShowOperator is set (form-XObject deletion),
                                        // retain an empty `() Tj` so the emptied fragment stays
                                        // re-extractable as "" (see the Tj branch above).
                                        if (KeepEmptyShowOperator)
                                            result.Write("() Tj"u8);
                                        WriteDeletionAdvanceTJ(result, arr, currentFontDict, reader, currentFontSize);
                                        _replacementCount++;
                                    }
                                    else if (AnchorTrailingOnReplace && replacement.Length == 0
                                        // Pure deletion of a proper substring: hoisted above the
                                        // font-switch branch, which a deletion never needs (the
                                        // split re-emits the surviving run from its original
                                        // bytes) but whose empty-replacement embed fails and
                                        // flattens the survivor at the op start.
                                        && tjNormalizedOrig.IndexOf(tjNormalizedSearch, StringComparison.Ordinal)
                                            == tjNormalizedOrig.LastIndexOf(tjNormalizedSearch, StringComparison.Ordinal)
                                        && WriteAnchoredTJSplit(result, arr, search, replacement,
                                            currentToUnicode, currentFontDict, currentFontSize,
                                            tmA, tmB, tmC, tmD, tmTx, tmTy, tcSpacing, twSpacing, reader,
                                            NeedsTlmRestore(streamBytes, endPos)))
                                    {
                                        _replacementCount++;
                                    }
                                    else if (ForcedCidFallbackFamily is not null || NeedsFontSwitch(tjReplacedText, currentToUnicode, currentFontDict, reader, AllowSubsetGlyphFallback))
                                    {
                                        // Preserve any trailing text's position: split the TJ, font-switch
                                        // only the matched run, re-anchor the rest at its original absolute
                                        // Tm. Only under an anchored mode (None / AdjustSpaceWidth) and a
                                        // single occurrence — a reflowing replacement re-encodes the whole
                                        // run instead, closing the gap when the replacement is narrower.
                                        // Falls back to flattening the whole TJ when the split doesn't apply.
                                        var tjFsSingleOccurrence =
                                            tjNormalizedOrig.IndexOf(tjNormalizedSearch, StringComparison.Ordinal)
                                                == tjNormalizedOrig.LastIndexOf(tjNormalizedSearch, StringComparison.Ordinal);
                                        if (!(tjFsSingleOccurrence
                                              && WriteFontSwitchedTJSplit(result, arr, search, replacement,
                                                currentToUnicode, currentFontDict, currentFontName, currentFontSize,
                                                tmA, tmB, tmC, tmD, tmTx, tmTy, tcSpacing, twSpacing, reader, pageDict,
                                                NeedsTlmRestore(streamBytes, endPos),
                                                anchored: AnchorTrailingOnReplace)))
                                            WriteFontSwitchedReplacement(result, tjReplacedText, currentFontDict,
                                                currentFontName, currentFontSize, pageDict, reader, "Tj", AllowSubsetGlyphFallback, ForcedCidFallbackFamily);
                                        _replacementCount++;
                                    }
                                    else if (AnchorTrailingOnReplace
                                        // The anchored split rewrites ONE occurrence and re-anchors
                                        // everything after it verbatim, so it must not run when the
                                        // array holds the search more than once (the later matches
                                        // would survive un-replaced inside the re-anchored tail).
                                        // A pure deletion (empty replacement) takes the split too:
                                        // the kern-compensated array rewrite keeps the RENDERED pen
                                        // in place, but consumers that walk glyph widths only would
                                        // read the trailing run shifted into the deleted span.
                                        && tjNormalizedOrig.IndexOf(tjNormalizedSearch, StringComparison.Ordinal)
                                            == tjNormalizedOrig.LastIndexOf(tjNormalizedSearch, StringComparison.Ordinal)
                                        && WriteAnchoredTJSplit(result, arr, search, replacement,
                                            currentToUnicode, currentFontDict, currentFontSize,
                                            tmA, tmB, tmC, tmD, tmTx, tmTy, tcSpacing, twSpacing, reader,
                                            NeedsTlmRestore(streamBytes, endPos)))
                                    {
                                        // ReplaceAdjustment.None with trailing text: split the TJ and
                                        // re-anchor the tail at its ORIGINAL absolute Tm instead of a
                                        // compensating kern — kern-blind consumers (extraction's
                                        // rect clip, sub-run positions) would misplace every glyph
                                        // after a large kern.
                                        _replacementCount++;
                                    }
                                    else if (TryReplaceTJArray(arr, search, replacement,
                                        currentToUnicode, currentFontDict, reader, currentFontSize, out var newArr))
                                    {
                                        WriteTJArray(result, newArr);
                                        result.Write(" TJ"u8);
                                        _replacementCount++;
                                    }

                                    lastWritePos = endPos;
                                }
                            }
                            break;

                        case "'":
                            // ' implicitly does T* before showing — advance the
                            // text matrix in text space (dy = -leading) so
                            // IsAtTargetY sees the post-T* position.
                            tmTx = -tlLeading * tmC + tmTx;
                            tmTy = -tlLeading * tmD + tmTy;
                            if (operands.Count >= 1 && operands[0].obj is PdfString str2
                                && IsAtTargetY(tmTx, tmTy, ctmB, ctmD, ctmTy)
                                && IsAtTargetX(tmTx, tmTy, ctmA, ctmC, ctmTx)
                                && RenderModeMatches(renderMode))
                            {
                                var decoded = DecodeString(str2.Value, currentToUnicode, currentFontDict, reader);
                                var normalizedDecoded2 = NormalizeForSearch(decoded);
                                if (MatchesSearch(normalizedDecoded2, normalizedSearch))
                                {
                                    var newText = ApplyReplace(normalizedDecoded2, normalizedSearch, replacement);

                                    result.Write(streamBytes, lastWritePos, operands[0].startPos - lastWritePos);
                                    if (ForcedCidFallbackFamily is not null || NeedsFontSwitch(newText, currentToUnicode, currentFontDict, reader, AllowSubsetGlyphFallback))
                                    {
                                        WriteFontSwitchedReplacement(result, newText, currentFontDict,
                                            currentFontName, currentFontSize, pageDict, reader, "'", AllowSubsetGlyphFallback, ForcedCidFallbackFamily);
                                    }
                                    else
                                    {
                                        var encoded = EncodeString(newText, currentToUnicode, currentFontDict);
                                        WriteStringOperand(result, encoded, str2.IsHex);
                                        result.Write(" '"u8);
                                    }
                                    lastWritePos = endPos;
                                }
                            }
                            break;

                        case "BT":
                            tmA = 1; tmB = 0; tmC = 0; tmD = 1; tmTx = 0; tmTy = 0;
                            tlLeading = 0;
                            break;

                        case "Td":
                        case "TD":
                            // Td translates in TEXT SPACE: new TM = [1 0 0 1 dx dy] × current TM.
                            // For ty: newTy = dx*tm.b + dy*tm.d + tm.ty.
                            if (operands.Count >= 2)
                            {
                                double dx = ToDouble(operands[0].obj);
                                double dy = ToDouble(operands[1].obj);
                                tmTx = dx * tmA + dy * tmC + tmTx;
                                tmTy = dx * tmB + dy * tmD + tmTy;
                                if (op == "TD") tlLeading = -dy;
                            }
                            break;

                        case "Tm":
                            // Tm sets the text matrix absolutely.
                            if (operands.Count >= 6)
                            {
                                tmA = ToDouble(operands[0].obj);
                                tmB = ToDouble(operands[1].obj);
                                tmC = ToDouble(operands[2].obj);
                                tmD = ToDouble(operands[3].obj);
                                tmTx = ToDouble(operands[4].obj);
                                tmTy = ToDouble(operands[5].obj);
                            }
                            break;

                        case "TL":
                            if (operands.Count >= 1)
                                tlLeading = ToDouble(operands[0].obj);
                            break;

                        case "T*":
                            // T* is equivalent to `0 -leading Td` — translate dy=-leading in text space.
                            tmTx = -tlLeading * tmC + tmTx;
                            tmTy = -tlLeading * tmD + tmTy;
                            break;

                        case "q":
                            ctmStack.Push((ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy));
                            trStack.Push(renderMode);
                            spacingStack.Push((tcSpacing, twSpacing));
                            break;

                        case "Q":
                            if (ctmStack.Count > 0)
                                (ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy) = ctmStack.Pop();
                            if (trStack.Count > 0)
                                renderMode = trStack.Pop();
                            if (spacingStack.Count > 0)
                                (tcSpacing, twSpacing) = spacingStack.Pop();
                            break;

                        case "Tr":
                            if (operands.Count >= 1)
                                renderMode = (int)ToDouble(operands[0].obj);
                            break;

                        case "Tc":
                            if (operands.Count >= 1)
                                tcSpacing = ToDouble(operands[0].obj);
                            break;

                        case "Tw":
                            if (operands.Count >= 1)
                                twSpacing = ToDouble(operands[0].obj);
                            break;

                        case "cm":
                            if (operands.Count >= 6)
                            {
                                double a = ToDouble(operands[0].obj);
                                double b = ToDouble(operands[1].obj);
                                double c = ToDouble(operands[2].obj);
                                double d = ToDouble(operands[3].obj);
                                double tx = ToDouble(operands[4].obj);
                                double ty = ToDouble(operands[5].obj);
                                // Pre-multiply current CTM by operator matrix per PDF 32000 §8.3.2.
                                var newA = a * ctmA + b * ctmC;
                                var newB = a * ctmB + b * ctmD;
                                var newC = c * ctmA + d * ctmC;
                                var newD = c * ctmB + d * ctmD;
                                var newTx = tx * ctmA + ty * ctmC + ctmTx;
                                var newTy = tx * ctmB + ty * ctmD + ctmTy;
                                ctmA = newA; ctmB = newB; ctmC = newC; ctmD = newD;
                                ctmTx = newTx; ctmTy = newTy;
                            }
                            break;

                        case "Do":
                            // Recurse into the referenced Form XObject with the
                            // current CTM as initial state, so the parent's cm
                            // composition flows into the XObject's text-matrix
                            // math (TargetY scoping needs that for content
                            // authored as `parent: cm Do` + `xobj: Td Tj`).
                            if (operands.Count >= 1 && operands[0].obj is PdfName xobjName)
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
                                        && reader.ResolveName(xobjStream.Dict, "Subtype") == "Form")
                                    {
                                        var xobjBytes = reader.DecodeStream(xobjStream);
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
                                            ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy);
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

                        case "BI":
                            // Write bytes up to (but not including) BI operator
                            result.Write(streamBytes, lastWritePos, startPos - lastWritePos);
                            SkipInlineImage(lexer);
                            lastWritePos = (int)lexer.Position;
                            operands.Clear();
                            continue;
                    }

                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }

        // Write remaining bytes
        if (lastWritePos < streamBytes.Length)
            result.Write(streamBytes, lastWritePos, streamBytes.Length - lastWritePos);

        var output = result.ToArray();

        // Always run cross-operator pass when enabled, even after per-op
        // replacements: per-op handles within-Tj matches, cross-op picks up
        // matches whose decoded text spans separate Tj/TJ operators (e.g.
        // "Page " in one Tj followed by "5 of 10" in another after a Td/Tm).
        // The cross-op routine itself skips single-operator matches so we
        // don't double-process spans the per-op pass already replaced.
        if (_allowCrossOperator)
        {
            var crossResult = TryCrossOperatorReplace(output, search, replacement, pageDict, reader,
                initCtmA, initCtmB, initCtmC, initCtmD, initCtmTx, initCtmTy);
            if (crossResult is not null)
                output = crossResult;
        }

        return output;
    }

    /// <summary>
    /// Cross-operator text replacement: collects text across consecutive Tj/TJ operators,
    /// finds the search string (literal or regex per <see cref="_isRegex"/>) in the
    /// concatenated text, and rewrites the operators. Used to catch matches whose
    /// decoded text spans positioned glyphs across separate Tj/TJ operators —
    /// invisible to the per-operator matcher.
    /// </summary>
    private byte[]? TryCrossOperatorReplace(byte[] streamBytes, string search, string replacement,
        PdfDictionary pageDict, PdfReader reader,
        double initCtmA = 1, double initCtmB = 0, double initCtmC = 0, double initCtmD = 1,
        double initCtmTx = 0, double initCtmTy = 0)
    {
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var normalizedSearch = NormalizeForSearch(search);

        // Collect text operators with everything needed to (a) build a gap-aware
        // concatenation for matching, (b) split a partially-matched first/last operator,
        // and (c) re-anchor / shift following same-line runs: decoded text + raw string
        // bytes, byte span, text matrix + CTM, font state (dict/ToUnicode/size/Tc), TJ
        // kern total, and the byte span of the op's positioning Tm x-operand (when the
        // op is Tm-positioned) so a follower's Tm can be rewritten in place.
        var textOps = CollectTextOps(streamBytes, fonts, reader,
            initCtmA, initCtmB, initCtmC, initCtmD, initCtmTx, initCtmTy);
        return TryCrossOperatorReplaceCore(streamBytes, search, replacement, pageDict, reader,
            normalizedSearch, textOps);
    }
}
