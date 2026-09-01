using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
    /// <summary>
    /// Inject a `R G B rg` operator immediately before the first text-showing
    /// operator whose decoded string contains <paramref name="text"/>, so the
    /// rendered glyphs pick up the new fill colour. The rg is scoped by the
    /// containing graphics-state block (BT/ET sets text-rendering colour from
    /// the current fill colour at the BT call site, so subsequent text within
    /// the same BT block also picks up the new value — this is fine for
    /// per-fragment colour changes since each fragment's text-showing op is
    /// what we're targeting).
    /// </summary>
    /// <summary>Whether the last <see cref="ModifyForegroundColor"/> call found a
    /// matching show operator and injected the colour (callers use it to retry
    /// with a wider positional scope).</summary>
    public bool LastForegroundColorApplied { get; private set; }

    /// <param name="nearestX">Fallback mode. The strict pass anchors a recolour on a show
    /// operator whose pen sits at <paramref name="targetX"/>; when an EARLIER replacement on the
    /// same line has already moved the run, that anchor misses by the width delta. Rather than
    /// fall back to "the first operator on the line that contains the text" — which repaints the
    /// wrong occurrence whenever a line carries several — this mode picks the occurrence whose
    /// pen is NEAREST the recorded X.</param>
    public void ModifyForegroundColor(Page page, string text, Color color, double? targetY = null,
        double? targetX = null, bool nearestX = false, int? renderingMode = null)
    {
        LastForegroundColorApplied = false;
        var reader = page.Reader;
        if (reader is null) return;

        if (ModifyForegroundColorInFormXObjects(page.Dict, reader, text, color, targetY, targetX,
                1, 0, 0, 1, 0, 0, nearestX, renderingMode))
        {
            LastForegroundColorApplied = true;
            return;
        }

        var contentStreams = GetContentStreams(page, reader);
        if (contentStreams.Count == 0) return;

        var combined = CombineStreams(contentStreams);
        var modified = ModifyForegroundColorInStream(combined, text, color, targetY, targetX,
            page.Dict, reader, 1, 0, 0, 1, 0, 0, nearestX, renderingMode);
        if (modified is not null)
        {
            // The whole rewritten page content is bracketed in a single q…Q pair
            // (idempotent — content already opening with q is left alone); the
            // recolor wraps the page once and keeps every
            // original operator in place.
            page.SetContentStream(TextReplacer.WrapInGraphicsState(modified));
            LastForegroundColorApplied = true;
        }
    }

    private bool ModifyForegroundColorInFormXObjects(PdfDictionary dict, PdfReader reader,
        string text, Color color, double? targetY, double? targetX,
        double ctmA, double ctmB, double ctmC, double ctmD, double ctmTx, double ctmTy,
        bool nearestX = false, int? renderingMode = null)
    {
        var resources = reader.ResolveDict(dict.Get("Resources"));
        if (resources is null) return false;
        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return false;

        foreach (var key in xobjects.Keys)
        {
            var xobjStream = reader.ResolveStream(xobjects.Get(key));
            if (xobjStream is null) continue;
            if (xobjStream.Dict.GetName("Subtype") != "Form") continue;

            var streamData = reader.DecodeStream(xobjStream);
            var modified = ModifyForegroundColorInStream(streamData, text, color, targetY, targetX,
                xobjStream.Dict, reader, ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy, nearestX, renderingMode);
            if (modified is not null)
            {
                xobjStream.Dict.Remove("Filter");
                xobjStream.Dict.Remove("DecodeParms");
                xobjStream.Dict.Set("Length", new PdfInteger(modified.Length));
                xobjStream.ReplaceData(modified);
                return true;
            }

            if (ModifyForegroundColorInFormXObjects(xobjStream.Dict, reader, text, color, targetY,
                    targetX, ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy, nearestX, renderingMode))
                return true;
        }
        return false;
    }

    private byte[]? ModifyForegroundColorInStream(byte[] streamBytes, string text, Color color,
        double? targetY, double? targetX, PdfDictionary pageDict, PdfReader reader,
        double initCtmA, double initCtmB, double initCtmC, double initCtmD,
        double initCtmTx, double initCtmTy, bool nearestX = false, int? renderingMode = null)
    {
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();
        Dictionary<int, string>? currentToUnicode = null;
        string? currentFontName = null;
        FontMetrics? currentMetrics = null;
        double fontSize = 0, charSpacing = 0, wordSpacing = 0, hScaling = 1.0;
        // Raw components of the pending TJ array (strings + kern adjustments), kept
        // for the pen-advance computation below.
        List<object>? tjItems = null;

        // CTM/TM tracking — same approach as TextReplacer.ReplaceInContentStream so
        // targetY scopes the color injection to the right text-showing op when the
        // same text occurs at multiple positions on the page.
        double ctmA = initCtmA, ctmB = initCtmB, ctmC = initCtmC, ctmD = initCtmD;
        double ctmTx = initCtmTx, ctmTy = initCtmTy;
        var ctmStack = new Stack<(double, double, double, double, double, double)>();
        double tmA = 1, tmB = 0, tmC = 0, tmD = 1, tmTx = 0, tmTy = 0;
        double tlLeading = 0;
        const double yTolerance = 6.0;
        // Pen X in text space: the line matrix origin (tmTx) plus the glyph advances
        // of the show operators already drawn on the line. tmTx itself stays the LINE
        // matrix (Td/TD/Tm/T* semantics unchanged); penTx is what a show operator's
        // real start X is, so X-scoping can tell apart same-text runs on one line.
        double penTx = 0;

        // Track the active fill colour so a substring recolour can restore the surrounding
        // glyphs to whatever colour was in effect (default black) when splitting a run.
        double fillR = 0, fillG = 0, fillB = 0;
        // ...and the VERBATIM source text of the operator that set it, so the restore
        // re-emits the producer's own form (`0 0 0 rg` stays `0 0 0 rg`, never
        // collapsing to `0 g`). Null until a fill-colour operator has been seen.
        string? fillOpText = null;
        // Active text rendering mode (Tr). A replacement carrying a TextState writes
        // its own mode before the run and restores this one after it.
        int trMode = 0;

        bool MatchesY() => !targetY.HasValue
            || Math.Abs(ctmD * tmTy + ctmTy - targetY.Value) <= yTolerance;

        // X scoping (same formula/tolerance as TextReplacer.IsAtTargetX): lets a
        // short segment (e.g. a lone space) recolour ITS OWN show operator instead
        // of the first operator on the line whose decoded text merely contains it.
        const double xTolerance = 4.0;
        bool MatchesX() => !targetX.HasValue
            || Math.Abs(ctmA * penTx + ctmC * tmTy + ctmTx - targetX.Value) <= xTolerance;
        // A show operator whose start X coincides with the target segment's X to
        // half a point IS that segment's operator — the segment position was
        // measured from it. Trusted over the decoded-text containment check,
        // whose ToUnicode interpretation can disagree with the absorber's for
        // exotic CID maps (observed: a space run decoding as '=').
        bool GeometricallyExact() => targetX.HasValue
            && Math.Abs(ctmA * penTx + ctmC * tmTy + ctmTx - targetX.Value) <= 0.5;

        // Nearest-X fallback state: the best candidate rewrite seen so far and how far
        // its occurrence sits from the recorded X.
        byte[]? bestResult = null;
        double bestGap = double.MaxValue;

        // Distance from targetX of the occurrence PickOccurrence last chose; the
        // nearest-X fallback ranks candidate operators by it.
        double lastOccurrenceGap = double.MaxValue;

        // ★ Which OCCURRENCE of the text inside this show operator the target segment is.
        // A line that draws the same replacement twice ("… 12345.  The 12345 …") is one
        // show operator with two matches, and each carries its own segment — anchoring on
        // the operator's start X alone would give them both the first one, so the second
        // recolour lands on glyphs that already have it and the real second occurrence is
        // never reached. The pen at each occurrence is the operator's pen plus the advance
        // of everything drawn before it (kerns included).
        // Returns the char offset to split at, or -1 when none of them is the target.
        int PickOccurrence(string decoded, List<object>? arrayItems, byte[]? singleRun)
        {
            var first = decoded.IndexOf(text, StringComparison.Ordinal);
            if (first < 0 || !targetX.HasValue) return first;
            var nearestAt = -1;
            var nearestGap = double.MaxValue;
            for (var at = first; at >= 0; at = decoded.IndexOf(text, at + 1, StringComparison.Ordinal))
            {
                var pen = penTx + PrefixAdvance(arrayItems, singleRun, at) * tmA;
                var gap = Math.Abs(ctmA * pen + ctmC * tmTy + ctmTx - targetX.Value);
                if (gap < nearestGap) { nearestGap = gap; nearestAt = at; }
            }
            if (nearestAt < 0) return -1;
            lastOccurrenceGap = nearestGap;
            return nearestX || nearestGap <= xTolerance ? nearestAt : -1;
        }

        // Advance of the first `chars` shown characters of this operator. Only a 1:1
        // byte↔char run can be measured this way, which is the same restriction the
        // colour split itself works under; anything else reports 0 so the caller keeps
        // its whole-operator behaviour.
        double PrefixAdvance(List<object>? arrayItems, byte[]? singleRun, int chars)
        {
            if (chars <= 0) return 0;
            double total = 0;
            var left = chars;
            if (arrayItems is null)
            {
                if (singleRun is null || singleRun.Length < chars) return 0;
                var head = new byte[chars];
                Array.Copy(singleRun, head, chars);
                return StringAdvance(head);
            }
            foreach (var item in arrayItems)
            {
                if (left <= 0) break;
                if (item is byte[] runBytes)
                {
                    var take = Math.Min(left, runBytes.Length);
                    var head = new byte[take];
                    Array.Copy(runBytes, head, take);
                    total += StringAdvance(head);
                    left -= take;
                }
                else if (item is double kern)
                {
                    total -= kern / 1000.0 * fontSize * hScaling;
                }
            }
            return total;
        }

        // Advance of one shown string in text-space units (mirrors
        // ContentStreamParser's cursor math: per-code width + Tc, + Tw on the
        // single-byte space code, scaled by Tz). CID (2-byte) fonts consume the
        // bytes pairwise through the /W-keyed metrics.
        double StringAdvance(byte[] bytes)
        {
            if (bytes.Length == 0 || fontSize <= 0) return 0;
            double total = 0;
            if (currentMetrics is { IsCid: true })
            {
                for (var i = 0; i + 1 < bytes.Length; i += 2)
                {
                    var cid = (bytes[i] << 8) | bytes[i + 1];
                    total += (currentMetrics.GetWidth(cid) / 1000.0 * fontSize + charSpacing) * hScaling;
                }
            }
            else
            {
                foreach (var b in bytes)
                {
                    var w = currentMetrics?.GetWidth(b) ?? 500;
                    total += (w / 1000.0 * fontSize + charSpacing
                        + (b == 0x20 ? wordSpacing : 0)) * hScaling;
                }
            }
            return total;
        }


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
                    // Collect the TJ array's text into a single PdfString operand so the
                    // TJ branch below can match it (mirrors ModifyFontSizeInStream /
                    // FindTfNameRange). Without this, `[ (hi world) -180 ... ] TJ` runs
                    // would never be seen by the colour matcher and no `rg` is injected.
                    // The raw components are kept for the pen-advance computation.
                    var arrTexts = new StringBuilder();
                    tjItems = new List<object>();
                    while (true)
                    {
                        var t = lexer.NextToken();
                        if (t.Kind == TokenKind.Eof) goto streamDone;
                        if (t.Kind == TokenKind.ArrayEnd) break;
                        if (t.Kind == TokenKind.LiteralString || t.Kind == TokenKind.HexString)
                        {
                            var strBytes = t.BytesValue;
                            if (strBytes is not null)
                            {
                                arrTexts.Append(DecodeTextString(strBytes, currentToUnicode));
                                tjItems.Add(strBytes);
                            }
                        }
                        else if (t.Kind == TokenKind.Integer) tjItems.Add((double)t.IntValue);
                        else if (t.Kind == TokenKind.Real) tjItems.Add(t.RealValue);
                    }
                    operands.Add((TokenKind.ArrayStart, new PdfString(
                        Cp1252.GetBytes(arrTexts.ToString())), startPos, (int)lexer.Position));
                    break;
                }
                case TokenKind.Keyword:
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "BT":
                            tmA = 1; tmB = 0; tmC = 0; tmD = 1; tmTx = 0; tmTy = 0;
                            tlLeading = 0;
                            penTx = 0;
                            break;
                        case "Td":
                        case "TD":
                            if (operands.Count >= 2)
                            {
                                double dx = ToDouble(operands[0].obj);
                                double dy = ToDouble(operands[1].obj);
                                tmTx = dx * tmA + dy * tmC + tmTx;
                                tmTy = dx * tmB + dy * tmD + tmTy;
                                if (op == "TD") tlLeading = -dy;
                                penTx = tmTx;
                            }
                            break;
                        case "Tm":
                            if (operands.Count >= 6)
                            {
                                tmA = ToDouble(operands[0].obj);
                                tmB = ToDouble(operands[1].obj);
                                tmC = ToDouble(operands[2].obj);
                                tmD = ToDouble(operands[3].obj);
                                tmTx = ToDouble(operands[4].obj);
                                tmTy = ToDouble(operands[5].obj);
                                penTx = tmTx;
                            }
                            break;
                        case "TL":
                            if (operands.Count >= 1) tlLeading = ToDouble(operands[0].obj);
                            break;
                        case "T*":
                            tmTx = -tlLeading * tmC + tmTx;
                            tmTy = -tlLeading * tmD + tmTy;
                            penTx = tmTx;
                            break;
                        case "Tc":
                            if (operands.Count >= 1) charSpacing = ToDouble(operands[^1].obj);
                            break;
                        case "Tw":
                            if (operands.Count >= 1) wordSpacing = ToDouble(operands[^1].obj);
                            break;
                        case "Tz":
                            if (operands.Count >= 1) hScaling = ToDouble(operands[^1].obj) / 100.0;
                            break;
                        case "q":
                            ctmStack.Push((ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy));
                            break;
                        case "Q":
                            if (ctmStack.Count > 0)
                                (ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy) = ctmStack.Pop();
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
                            // Recurse into Form XObjects with current CTM as initial state.
                            if (operands.Count >= 1 && operands[0].obj is PdfName xobjName)
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
                                            ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy);
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
                        case "Tf":
                            if (operands.Count >= 2 && operands[0].obj is PdfName fn)
                            {
                                currentFontName = fn.Value;
                                fontSize = ToDouble(operands[^1].obj);
                                if (fonts.TryGetValue(currentFontName, out var fontDict))
                                {
                                    currentToUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, reader);
                                    try { currentMetrics = FontMetrics.FromFontDict(fontDict, reader); }
                                    catch { currentMetrics = null; }
                                }
                                else
                                {
                                    currentToUnicode = null;
                                    currentMetrics = null;
                                }
                            }
                            break;
                        case "rg":
                            if (operands.Count >= 3)
                            {
                                fillR = ToDouble(operands[^3].obj);
                                fillG = ToDouble(operands[^2].obj);
                                fillB = ToDouble(operands[^1].obj);
                                fillOpText = Verbatim(streamBytes, operands[^3].startPos, endPos);
                            }
                            break;
                        case "g":
                            if (operands.Count >= 1)
                            {
                                fillR = fillG = fillB = ToDouble(operands[^1].obj);
                                fillOpText = Verbatim(streamBytes, operands[^1].startPos, endPos);
                            }
                            break;
                        case "k":
                            if (operands.Count >= 4)
                            {
                                double c = ToDouble(operands[^4].obj), m = ToDouble(operands[^3].obj);
                                double y = ToDouble(operands[^2].obj), kk = ToDouble(operands[^1].obj);
                                fillR = (1 - c) * (1 - kk);
                                fillG = (1 - m) * (1 - kk);
                                fillB = (1 - y) * (1 - kk);
                                fillOpText = Verbatim(streamBytes, operands[^4].startPos, endPos);
                            }
                            break;
                        case "Tr":
                            if (operands.Count >= 1) trMode = (int)ToDouble(operands[^1].obj);
                            break;
                        case "Tj":
                        case "'":
                        case "\"":
                            // ' and " move to the next line before showing.
                            if (op is "'" or "\"")
                            {
                                tmTx = -tlLeading * tmC + tmTx;
                                tmTy = -tlLeading * tmD + tmTy;
                                penTx = tmTx;
                            }
                            if (operands.Count >= 1 && operands[^1].obj is PdfString s)
                            {
                                var occ = MatchesY()
                                    ? PickOccurrence(DecodeTextString(s.Value, currentToUnicode), null, s.Value)
                                    : -1;
                                if (MatchesY() && (MatchesX() || occ >= 0))
                                {
                                    var decoded = DecodeTextString(s.Value, currentToUnicode);
                                    // In nearest-X mode every candidate is only a candidate:
                                    // skip building the rewrite for one that is already further
                                    // from the target than the best seen, so a page with many
                                    // occurrences does not copy the whole stream per occurrence.
                                    if ((decoded.Contains(text) || GeometricallyExact())
                                        && !(nearestX && targetX.HasValue && lastOccurrenceGap >= bestGap))
                                    {
                                        // When the match is only part of the run, split the show
                                        // operator so the new colour applies to the matched glyphs
                                        // alone and the surrounding glyphs keep the active fill
                                        // colour (consecutive Tj operators advance the text matrix
                                        // automatically, so the split preserves positioning).
                                        var split = SplitColorRun(streamBytes,
                                            operands[^1].startPos, operands[^1].endPos,
                                            text, color, (fillR, fillG, fillB), occ,
                                            renderingMode, trMode)
                                            // Whole-run recolour: wrap the show operator with the new
                                            // fill colour AND a trailing restore to the colour that was
                                            // active before it, so the recolour doesn't leak onto the
                                            // subsequent text (endPos is just past the show keyword).
                                            ?? InjectColorAround(streamBytes, operands[^1].startPos,
                                                endPos, color, (fillR, fillG, fillB), fillOpText,
                                                renderingMode, trMode);
                                        if (!nearestX) return split;
                                        if (lastOccurrenceGap < bestGap)
                                        {
                                            bestGap = lastOccurrenceGap;
                                            bestResult = split;
                                        }
                                    }
                                }
                                // Advances live in Tm-space; the tracked tm coordinates are
                                // Tm-applied (Td folds tmA in), so scale the advance the same way.
                                penTx += StringAdvance(s.Value) * tmA;
                            }
                            break;
                        case "TJ":
                            // The TJ array's text was concatenated into a single PdfString
                            // operand by the ArrayStart handler above.
                            if (operands.Count >= 1 && operands[^1].obj is PdfString tjText)
                            {
                                var tjOcc = MatchesY()
                                    ? PickOccurrence(DecodeTextString(tjText.Value, currentToUnicode), tjItems, null)
                                    : -1;
                                if (MatchesY() && (MatchesX() || tjOcc >= 0))
                                {
                                    var decoded = DecodeTextString(tjText.Value, currentToUnicode);
                                    if ((decoded.Contains(text) || GeometricallyExact())
                                        && !(nearestX && targetX.HasValue && lastOccurrenceGap >= bestGap))
                                    {
                                        // Same rule as the Tj branch: recolour only the matched
                                        // glyphs. A TJ array carries a whole line, so colouring
                                        // the operator as a unit repaints the words either side
                                        // of the match too.
                                        var splitTj = SplitShowRunTJ(streamBytes,
                                            operands[^1].startPos, operands[^1].endPos,
                                            text, RgOps(color), RestoreFillOps(fillR, fillG, fillB),
                                            currentToUnicode, tjOcc)
                                            ?? InjectColorAround(streamBytes, operands[^1].startPos,
                                                endPos, color, (fillR, fillG, fillB), fillOpText,
                                                renderingMode, trMode);
                                        if (!nearestX) return splitTj;
                                        if (lastOccurrenceGap < bestGap)
                                        {
                                            bestGap = lastOccurrenceGap;
                                            bestResult = splitTj;
                                        }
                                    }
                                }
                                if (tjItems is not null)
                                    foreach (var item in tjItems)
                                    {
                                        if (item is byte[] strBytes)
                                            penTx += StringAdvance(strBytes) * tmA;
                                        else if (item is double kern)
                                            penTx -= kern / 1000.0 * fontSize * hScaling * tmA;
                                    }
                            }
                            tjItems = null;
                            break;
                    }
                    operands.Clear();
                    break;
                default:
                    operands.Clear();
                    break;
            }
        }
        streamDone:
        return bestResult;
    }

    /// <summary>The operator source text exactly as the producer wrote it, so a restore
    /// can re-emit that form rather than a normalised equivalent.</summary>
    private static string Verbatim(byte[] stream, int start, int end)
    {
        if (start < 0 || end > stream.Length || end <= start) return string.Empty;
        return Encoding.ASCII.GetString(stream, start, end - start).Trim();
    }

    private static double ToDouble(PdfObject obj) => obj switch
    {
        PdfInteger pi => pi.Value,
        PdfReal pr => pr.Value,
        _ => 0
    };

    /// <summary>
    /// Split a literal-string show operator so a matched substring is recoloured to
    /// <paramref name="color"/> while the prefix/suffix glyphs keep the active fill colour
    /// (<paramref name="activeColor"/>). Returns null — letting the caller fall back to a
    /// whole-run colour injection — when the match spans the whole run, the operand isn't a
    /// plain parenthesised literal, or it contains escapes/parentheses the simple byte-offset
    /// split can't safely handle.
    /// </summary>
    private static byte[]? SplitColorRun(byte[] original, int litStart, int litEnd,
        string text, Color color, (double r, double g, double b) activeColor,
        int occurrenceCharIndex = -1, int? renderingMode = null, int activeRenderingMode = 0)
    {
        // Operand must be a plain "(...)" literal.
        if (litEnd - litStart < 2
            || original[litStart] != (byte)'(' || original[litEnd - 1] != (byte)')')
            return null;

        int innerStart = litStart + 1;
        int innerLen = litEnd - 1 - innerStart;
        if (innerLen <= 0) return null;

        // Bail on any escape or nested parenthesis — the char↔byte offset mapping below
        // assumes a 1:1, single-byte literal (true for the WinAnsi runs produced by
        // text replacement).
        for (int i = innerStart; i < innerStart + innerLen; i++)
        {
            byte b = original[i];
            if (b == (byte)'\\' || b == (byte)'(' || b == (byte)')') return null;
        }

        var innerBytes = new byte[innerLen];
        Array.Copy(original, innerStart, innerBytes, 0, innerLen);
        var inner = Cp1252.GetString(innerBytes);
        // The caller identifies WHICH occurrence belongs to the segment being recoloured;
        // -1 means it had no positional anchor, so the first one is taken.
        int idx = occurrenceCharIndex >= 0 && occurrenceCharIndex + text.Length <= inner.Length
                  && string.CompareOrdinal(inner, occurrenceCharIndex, text, 0, text.Length) == 0
            ? occurrenceCharIndex
            : inner.IndexOf(text, StringComparison.Ordinal);
        if (idx < 0) return null;
        // Whole-run match → let the caller inject a single colour before the operator.
        if (idx == 0 && text.Length == inner.Length) return null;

        string prefix = inner.Substring(0, idx);
        string suffix = inner.Substring(idx + text.Length);

        string Rg(double r, double g, double b) => string.Format(CultureInfo.InvariantCulture,
            "{0:F3} {1:F3} {2:F3} rg", r, g, b);

        // Lead with a space so the first emitted token never abuts the preceding operator
        // (e.g. "Tm" running straight into "(prefix)" or "1.000", which the lexer mis-parses).
        var sb = new StringBuilder(" ");
        if (prefix.Length > 0) sb.Append('(').Append(prefix).Append(") Tj ");
        sb.Append(Rg(color.R / 255.0, color.G / 255.0, color.B / 255.0));
        // A requested rendering mode rides with the colour so the matched glyphs are
        // shown the way the caller's TextState asks (0 = fill, 3 = invisible).
        if (renderingMode is int rm) sb.Append(' ').Append(rm).Append(" Tr");
        sb.Append(" (").Append(text).Append(')');
        if (suffix.Length > 0)
        {
            // The original Tj keyword that follows this operand will show the suffix.
            sb.Append(" Tj");
            if (renderingMode is not null) sb.Append(' ').Append(activeRenderingMode).Append(" Tr");
            sb.Append(' ').Append(Rg(activeColor.r, activeColor.g, activeColor.b))
              .Append(" (").Append(suffix).Append(')');
        }

        var replacement = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[original.Length - (litEnd - litStart) + replacement.Length];
        Array.Copy(original, 0, result, 0, litStart);
        Array.Copy(replacement, 0, result, litStart, replacement.Length);
        Array.Copy(original, litEnd, result, litStart + replacement.Length, original.Length - litEnd);
        return result;
    }

    /// <summary>
    /// Split a TJ ARRAY so only the matched substring is shown under
    /// <paramref name="beforeOps"/>, with <paramref name="afterOps"/> restoring whatever state
    /// those changed for the glyphs that follow. The wrapping operators are the caller's: a
    /// fill colour for a recolour, a `Tf` for a font or size change.
    /// The array is cut into up to three arrays shown by their own TJ operators — consecutive
    /// show operators continue from the pen where the previous one left off, and each kern
    /// number stays in the group of the glyph it positions, so the split is
    /// positionally identical to the original. Returns null (caller falls back to applying the
    /// change to the whole operator) when the match spans the whole array, isn't found, or the
    /// array holds a string this simple 1:1 byte↔char split cannot address (escapes,
    /// multi-byte codes).
    /// </summary>
    /// <summary>`/Res size Tf`, spaced so it never abuts a neighbour.</summary>
    private static string TfOps(string res, double size) => string.Format(
        CultureInfo.InvariantCulture, " /{0} {1} Tf ", res,
        size.ToString("0.####", CultureInfo.InvariantCulture));

    /// <summary>`R G B rg` for <paramref name="color"/>, spaced so it never abuts a neighbour.</summary>
    private static string RgOps(Color color)
    {
        static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        return string.Format(CultureInfo.InvariantCulture, " {0} {1} {2} rg ",
            N(color.R / 255.0), N(color.G / 255.0), N(color.B / 255.0));
    }

    /// <summary>The operator that puts the fill colour back to what was active. Written as
    /// `v g` when the prior fill was a gray, matching how producers write a default-black
    /// reset, otherwise as `r g b rg`.</summary>
    private static string RestoreFillOps(double r, double g, double b)
    {
        static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        return r == g && g == b
            ? string.Format(CultureInfo.InvariantCulture, " {0} g ", N(r))
            : string.Format(CultureInfo.InvariantCulture, " {0} {1} {2} rg ", N(r), N(g), N(b));
    }

    private static byte[]? SplitShowRunTJ(byte[] original, int arrStart, int arrEnd,
        string text, string beforeOps, string afterOps,
        Dictionary<int, string>? toUnicode, int occurrenceCharIndex = -1)
    {
        if (arrEnd <= arrStart || arrEnd > original.Length) return null;

        // (isString, byteStart, byteEnd, charStart, charLen) in array order.
        var items = new List<(bool isString, int start, int end, int charStart, int charLen)>();
        var lexer = new PdfLexer(original) { Position = arrStart };
        if (lexer.NextToken().Kind != TokenKind.ArrayStart) return null;
        int chars = 0;
        while (true)
        {
            var itemStart = (int)lexer.Position;
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd) break;
            var itemEnd = (int)lexer.Position;
            if (itemEnd > arrEnd) return null;
            switch (t.Kind)
            {
                case TokenKind.LiteralString:
                {
                    var bytes = t.BytesValue;
                    if (bytes is null) return null;
                    // The literal must be a plain, escape-free (…) run: the char offsets
                    // below index straight into its bytes.
                    if (original[itemEnd - 1] != (byte)')') return null;
                    var innerStart = itemStart;
                    while (innerStart < itemEnd && original[innerStart] != (byte)'(') innerStart++;
                    if (innerStart >= itemEnd) return null;
                    if (itemEnd - 1 - (innerStart + 1) != bytes.Length) return null;
                    for (int i = innerStart + 1; i < itemEnd - 1; i++)
                        if (original[i] is 0x5C or 0x28 or 0x29) return null;   // backslash, ( , )
                    var decodedItem = DecodeTextString(bytes, toUnicode);
                    if (decodedItem.Length != bytes.Length) return null;
                    items.Add((true, innerStart + 1, itemEnd - 1, chars, bytes.Length));
                    chars += bytes.Length;
                    break;
                }
                case TokenKind.Integer:
                case TokenKind.Real:
                    items.Add((false, itemStart, itemEnd, chars, 0));
                    break;
                case TokenKind.Eof:
                    return null;
                default:
                    return null; // hex string or anything else — not addressable here
            }
        }
        if (chars == 0) return null;

        var whole = new StringBuilder(chars);
        foreach (var it in items)
        {
            if (!it.isString) continue;
            var raw = new byte[it.end - it.start];
            Array.Copy(original, it.start, raw, 0, raw.Length);
            whole.Append(DecodeTextString(raw, toUnicode));
        }
        var all = whole.ToString();
        // The caller identifies WHICH occurrence belongs to the segment being recoloured;
        // -1 means it had no positional anchor, so the first one is taken.
        int idx = occurrenceCharIndex >= 0 && occurrenceCharIndex + text.Length <= all.Length
                  && string.CompareOrdinal(all, occurrenceCharIndex, text, 0, text.Length) == 0
            ? occurrenceCharIndex
            : all.IndexOf(text, StringComparison.Ordinal);
        if (idx < 0) return null;
        if (idx == 0 && text.Length == all.Length) return null; // whole run → caller wraps

        // Emit the items whose characters fall in [from, to) as one array. A kern number
        // belongs to the group its FOLLOWING glyph is in, which is where the cursor sits.
        void EmitGroup(StringBuilder outSb, int from, int to)
        {
            outSb.Append('[');
            foreach (var it in items)
            {
                if (!it.isString)
                {
                    if (it.charStart >= from && it.charStart < to) AppendRaw(outSb, original, it.start, it.end);
                    continue;
                }
                int s = Math.Max(it.charStart, from);
                int e = Math.Min(it.charStart + it.charLen, to);
                if (e <= s) continue;
                outSb.Append('(');
                AppendRaw(outSb, original, it.start + (s - it.charStart), it.start + (e - it.charStart));
                outSb.Append(')');
            }
            outSb.Append(']');
        }

        static void AppendRaw(StringBuilder outSb, byte[] src, int from, int to)
        {
            for (int i = from; i < to; i++) outSb.Append((char)src[i]);
        }

        // Lead with a space so the first token never abuts the preceding operator.
        var sb = new StringBuilder(" ");
        if (idx > 0) { EmitGroup(sb, 0, idx); sb.Append(" TJ "); }
        sb.Append(beforeOps);
        EmitGroup(sb, idx, idx + text.Length);
        sb.Append(" TJ ").Append(afterOps);
        // The original TJ keyword that follows this operand shows the tail — which may be
        // empty, and an empty array is a valid (no-op) TJ operand.
        EmitGroup(sb, idx + text.Length, chars);

        var replacement = Encoding.Latin1.GetBytes(sb.ToString());
        var result = new byte[original.Length - (arrEnd - arrStart) + replacement.Length];
        Array.Copy(original, 0, result, 0, arrStart);
        Array.Copy(replacement, 0, result, arrStart, replacement.Length);
        Array.Copy(original, arrEnd, result, arrStart + replacement.Length, original.Length - arrEnd);
        return result;
    }

    /// <summary>
    /// Wrap a text-showing operator with a fill-colour change and a matching restore:
    /// inject `R G B rg` immediately before the show operand (at <paramref name="rgInsertPos"/>)
    /// and restore the previously-active fill colour immediately after the show keyword (at
    /// <paramref name="restorePos"/>). Without the restore the recolour leaks onto every
    /// subsequent glyph in the same BT block. The restore is emitted as `v g` (SetGray) when
    /// the prior fill was a gray (r==g==b) — matching how producers write a default-black
    /// reset — otherwise as `r g b rg`.
    /// </summary>
    private static byte[] InjectColorAround(byte[] original, int rgInsertPos, int restorePos,
        Color color, (double r, double g, double b) restore, string? restoreOpText = null,
        int? renderingMode = null, int activeRenderingMode = 0)
    {
        // Leading space separates the injected rg from the preceding token
        // (e.g. "Tc" runs straight into "1.000" without it, which the lexer
        // mis-parses as one keyword "Tc1.000"); trailing space separates the
        // rg from the following PdfString '(' delimiter. Components are written
        // minimally ("1 0 0 rg", not "1.000 0.000 0.000 rg") — the exact
        // form asserted verbatim by operator-comparing consumers.
        static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var before = string.Format(CultureInfo.InvariantCulture, " {0} {1} {2} rg ",
            N(color.R / 255.0), N(color.G / 255.0), N(color.B / 255.0));
        // A requested rendering mode rides with the colour, so a replacement whose
        // TextState asks for visible text shows through an invisible (Tr 3) source run
        // and the surrounding glyphs keep the mode they had.
        if (renderingMode is int mode) before += mode.ToString(CultureInfo.InvariantCulture) + " Tr ";
        // Restore the producer's OWN colour operator verbatim when one was seen, so
        // `0 0 0 rg` comes back as `0 0 0 rg` instead of collapsing to `0 g`.
        string restoreColor = restoreOpText
            ?? ((restore.r == restore.g && restore.g == restore.b)
                ? string.Format(CultureInfo.InvariantCulture, "{0} g", N(restore.r))
                : string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} rg",
                    N(restore.r), N(restore.g), N(restore.b)));
        string after = renderingMode is null
            ? " " + restoreColor + " "
            : string.Format(CultureInfo.InvariantCulture, " {0} Tr {1} ",
                activeRenderingMode, restoreColor);
        var beforeBytes = Encoding.ASCII.GetBytes(before);
        var afterBytes = Encoding.ASCII.GetBytes(after);

        var result = new byte[original.Length + beforeBytes.Length + afterBytes.Length];
        int pos = 0;
        Array.Copy(original, 0, result, pos, rgInsertPos); pos += rgInsertPos;
        Array.Copy(beforeBytes, 0, result, pos, beforeBytes.Length); pos += beforeBytes.Length;
        Array.Copy(original, rgInsertPos, result, pos, restorePos - rgInsertPos); pos += restorePos - rgInsertPos;
        Array.Copy(afterBytes, 0, result, pos, afterBytes.Length); pos += afterBytes.Length;
        Array.Copy(original, restorePos, result, pos, original.Length - restorePos);
        return result;
    }
}
