using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Modifies text state properties (font size, etc.) in PDF content streams.
/// Finds the Tf operator associated with a given text string and updates its size parameter.
/// </summary>
internal sealed class TextStateModifier
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
    public void ModifyForegroundColor(Page page, string text, Color color, double? targetY = null)
    {
        var reader = page.Reader;
        if (reader is null) return;

        if (ModifyForegroundColorInFormXObjects(page.Dict, reader, text, color, targetY,
                1, 0, 0, 1, 0, 0))
            return;

        var contentStreams = GetContentStreams(page, reader);
        if (contentStreams.Count == 0) return;

        var combined = CombineStreams(contentStreams);
        var modified = ModifyForegroundColorInStream(combined, text, color, targetY,
            page.Dict, reader, 1, 0, 0, 1, 0, 0);
        if (modified is not null)
            page.SetContentStream(modified);
    }

    private bool ModifyForegroundColorInFormXObjects(PdfDictionary dict, PdfReader reader,
        string text, Color color, double? targetY,
        double ctmA, double ctmB, double ctmC, double ctmD, double ctmTx, double ctmTy)
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
            var modified = ModifyForegroundColorInStream(streamData, text, color, targetY,
                xobjStream.Dict, reader, ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy);
            if (modified is not null)
            {
                xobjStream.Dict.Remove("Filter");
                xobjStream.Dict.Remove("DecodeParms");
                xobjStream.Dict.Set("Length", new PdfInteger(modified.Length));
                xobjStream.ReplaceData(modified);
                return true;
            }

            if (ModifyForegroundColorInFormXObjects(xobjStream.Dict, reader, text, color, targetY,
                    ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy))
                return true;
        }
        return false;
    }

    private byte[]? ModifyForegroundColorInStream(byte[] streamBytes, string text, Color color,
        double? targetY, PdfDictionary pageDict, PdfReader reader,
        double initCtmA, double initCtmB, double initCtmC, double initCtmD,
        double initCtmTx, double initCtmTy)
    {
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();
        Dictionary<int, string>? currentToUnicode = null;
        string? currentFontName = null;

        // CTM/TM tracking — same approach as TextReplacer.ReplaceInContentStream so
        // targetY scopes the color injection to the right text-showing op when the
        // same text occurs at multiple positions on the page.
        double ctmA = initCtmA, ctmB = initCtmB, ctmC = initCtmC, ctmD = initCtmD;
        double ctmTx = initCtmTx, ctmTy = initCtmTy;
        var ctmStack = new Stack<(double, double, double, double, double, double)>();
        double tmA = 1, tmB = 0, tmC = 0, tmD = 1, tmTx = 0, tmTy = 0;
        double tlLeading = 0;
        const double yTolerance = 6.0;

        // Track the active fill colour so a substring recolour can restore the surrounding
        // glyphs to whatever colour was in effect (default black) when splitting a run.
        double fillR = 0, fillG = 0, fillB = 0;

        bool MatchesY() => !targetY.HasValue
            || Math.Abs(ctmD * tmTy + ctmTy - targetY.Value) <= yTolerance;

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
                    var arrTexts = new StringBuilder();
                    while (true)
                    {
                        var t = lexer.NextToken();
                        if (t.Kind == TokenKind.Eof) goto streamDone;
                        if (t.Kind == TokenKind.ArrayEnd) break;
                        if (t.Kind == TokenKind.LiteralString || t.Kind == TokenKind.HexString)
                        {
                            var strBytes = t.BytesValue;
                            if (strBytes is not null)
                                arrTexts.Append(DecodeTextString(strBytes, currentToUnicode));
                        }
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
                            }
                            break;
                        case "TL":
                            if (operands.Count >= 1) tlLeading = ToDouble(operands[0].obj);
                            break;
                        case "T*":
                            tmTx = -tlLeading * tmC + tmTx;
                            tmTy = -tlLeading * tmD + tmTy;
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
                                            text, color, targetY,
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
                                if (fonts.TryGetValue(currentFontName, out var fontDict))
                                    currentToUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, reader);
                                else
                                    currentToUnicode = null;
                            }
                            break;
                        case "rg":
                            if (operands.Count >= 3)
                            {
                                fillR = ToDouble(operands[^3].obj);
                                fillG = ToDouble(operands[^2].obj);
                                fillB = ToDouble(operands[^1].obj);
                            }
                            break;
                        case "g":
                            if (operands.Count >= 1)
                                fillR = fillG = fillB = ToDouble(operands[^1].obj);
                            break;
                        case "k":
                            if (operands.Count >= 4)
                            {
                                double c = ToDouble(operands[^4].obj), m = ToDouble(operands[^3].obj);
                                double y = ToDouble(operands[^2].obj), kk = ToDouble(operands[^1].obj);
                                fillR = (1 - c) * (1 - kk);
                                fillG = (1 - m) * (1 - kk);
                                fillB = (1 - y) * (1 - kk);
                            }
                            break;
                        case "Tj":
                        case "'":
                        case "\"":
                            if (operands.Count >= 1 && operands[^1].obj is PdfString s
                                && MatchesY())
                            {
                                var decoded = DecodeTextString(s.Value, currentToUnicode);
                                if (decoded.Contains(text))
                                {
                                    // When the match is only part of the run, split the show
                                    // operator so the new colour applies to the matched glyphs
                                    // alone and the surrounding glyphs keep the active fill
                                    // colour (consecutive Tj operators advance the text matrix
                                    // automatically, so the split preserves positioning).
                                    var split = SplitColorRun(streamBytes,
                                        operands[^1].startPos, operands[^1].endPos,
                                        text, color, (fillR, fillG, fillB));
                                    if (split is not null) return split;
                                    return InjectColorBefore(streamBytes, operands[^1].startPos, color);
                                }
                            }
                            break;
                        case "TJ":
                            // The TJ array's text was concatenated into a single PdfString
                            // operand by the ArrayStart handler above.
                            if (operands.Count >= 1 && operands[^1].obj is PdfString tjText
                                && MatchesY())
                            {
                                var decoded = DecodeTextString(tjText.Value, currentToUnicode);
                                if (decoded.Contains(text))
                                    return InjectColorBefore(streamBytes, operands[^1].startPos, color);
                            }
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
        return null;
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
        string text, Color color, (double r, double g, double b) activeColor)
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
        int idx = inner.IndexOf(text, StringComparison.Ordinal);
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
        sb.Append(Rg(color.R / 255.0, color.G / 255.0, color.B / 255.0)).Append(" (").Append(text).Append(')');
        if (suffix.Length > 0)
        {
            // The original Tj keyword that follows this operand will show the suffix.
            sb.Append(" Tj ").Append(Rg(activeColor.r, activeColor.g, activeColor.b))
              .Append(" (").Append(suffix).Append(')');
        }

        var replacement = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[original.Length - (litEnd - litStart) + replacement.Length];
        Array.Copy(original, 0, result, 0, litStart);
        Array.Copy(replacement, 0, result, litStart, replacement.Length);
        Array.Copy(original, litEnd, result, litStart + replacement.Length, original.Length - litEnd);
        return result;
    }

    private static byte[] InjectColorBefore(byte[] original, int insertPos, Color color)
    {
        // Leading space separates the injected rg from the preceding token
        // (e.g. "Tc" runs straight into "1.000" without it, which the lexer
        // mis-parses as one keyword "Tc1.000"); trailing space separates the
        // rg from the following PdfString '(' delimiter.
        var rg = string.Format(CultureInfo.InvariantCulture, " {0:F3} {1:F3} {2:F3} rg ",
            color.R / 255.0, color.G / 255.0, color.B / 255.0);
        var rgBytes = Encoding.ASCII.GetBytes(rg);
        var result = new byte[original.Length + rgBytes.Length];
        Array.Copy(original, 0, result, 0, insertPos);
        Array.Copy(rgBytes, 0, result, insertPos, rgBytes.Length);
        Array.Copy(original, insertPos, result, insertPos + rgBytes.Length, original.Length - insertPos);
        return result;
    }

    /// <summary>
    /// Change the font size of the Tf operator that immediately precedes the first
    /// occurrence of <paramref name="text"/> in the page's content stream(s).
    /// </summary>
    public void ModifyFontSize(Page page, string text, double oldSize, double newSize)
    {
        var reader = page.Reader;
        if (reader is null) return;

        // First try Form XObjects (text is often inside XObjects, not page content directly)
        if (ModifyInFormXObjects(page.Dict, reader, text, oldSize, newSize))
            return;

        // Then try the page's own content stream
        var contentStreams = GetContentStreams(page, reader);
        if (contentStreams.Count == 0) return;

        var combined = CombineStreams(contentStreams);
        var modified = ModifyFontSizeInStream(combined, text, oldSize, newSize, page.Dict, reader);
        if (modified is not null)
        {
            page.SetContentStream(modified);
        }
    }

    private bool ModifyInFormXObjects(PdfDictionary dict, PdfReader reader,
        string text, double oldSize, double newSize)
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
            var modified = ModifyFontSizeInStream(streamData, text, oldSize, newSize, xobjStream.Dict, reader);
            if (modified is not null)
            {
                xobjStream.Dict.Remove("Filter");
                xobjStream.Dict.Remove("DecodeParms");
                xobjStream.Dict.Set("Length", new PdfInteger(modified.Length));
                xobjStream.ReplaceData(modified);
                return true;
            }

            // Recurse into nested Form XObjects
            if (ModifyInFormXObjects(xobjStream.Dict, reader, text, oldSize, newSize))
                return true;
        }
        return false;
    }

    private byte[]? ModifyFontSizeInStream(byte[] streamBytes, string text, double oldSize,
        double newSize, PdfDictionary pageDict, PdfReader reader)
    {
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();

        // Track the position of the most recent Tf operator
        int lastTfSizeStart = -1;
        int lastTfSizeEnd = -1;
        double lastTfSize = 0;
        double tmScaleY = 1; // text matrix vertical scale factor
        string? currentFontName = null;
        Dictionary<int, string>? currentToUnicode = null;

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
                    // Collect array elements (for TJ operator)
                    var arrTexts = new StringBuilder();
                    int arrStringCount = 0;
                    while (true)
                    {
                        var t = lexer.NextToken();
                        if (t.Kind == TokenKind.Eof) goto done;
                        if (t.Kind == TokenKind.ArrayEnd) break;
                        if (t.Kind == TokenKind.LiteralString || t.Kind == TokenKind.HexString)
                        {
                            var strBytes = t.BytesValue;
                            if (strBytes is not null)
                            {
                                arrTexts.Append(DecodeTextString(strBytes, currentToUnicode));
                                arrStringCount++;
                            }
                        }
                    }
                    // Store the concatenated text from the array as an operand
                    operands.Add((TokenKind.ArrayStart, new PdfString(
                        Cp1252.GetBytes(arrTexts.ToString())), startPos, (int)lexer.Position));
                    break;
                }
                case TokenKind.DictStart:
                {
                    int depth = 1;
                    while (depth > 0)
                    {
                        var t = lexer.NextToken();
                        if (t.Kind == TokenKind.Eof) goto done;
                        if (t.Kind == TokenKind.DictStart) depth++;
                        if (t.Kind == TokenKind.DictEnd) depth--;
                    }
                    operands.Clear();
                    break;
                }
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "Tf":
                            if (operands.Count >= 2)
                            {
                                if (operands[0].obj is PdfName fn)
                                {
                                    currentFontName = fn.Value;
                                    if (fonts.TryGetValue(currentFontName, out var fontDict))
                                        currentToUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, reader);
                                    else
                                        currentToUnicode = null;
                                }
                                // Record position of the size operand
                                lastTfSizeStart = operands[1].startPos;
                                lastTfSizeEnd = operands[1].endPos;
                                if (operands[1].obj is PdfInteger pi)
                                    lastTfSize = pi.Value;
                                else if (operands[1].obj is PdfReal pr)
                                    lastTfSize = pr.Value;
                            }
                            break;

                        case "Tm":
                            // Tm: a b c d e f — text matrix; effective font size = Tf_size * sqrt(c² + d²)
                            if (operands.Count >= 6)
                            {
                                double c = 0, d = 0;
                                if (operands[2].obj is PdfReal cr2) c = cr2.Value;
                                else if (operands[2].obj is PdfInteger ci2) c = ci2.Value;
                                if (operands[3].obj is PdfReal dr2) d = dr2.Value;
                                else if (operands[3].obj is PdfInteger di2) d = di2.Value;
                                tmScaleY = Math.Sqrt(c * c + d * d);
                                if (tmScaleY < 0.001) tmScaleY = 1;
                            }
                            break;

                        case "Tj":
                        case "'":
                        case "\"":
                            if (operands.Count >= 1 && operands[^1].obj is PdfString textStr)
                            {
                                var decoded = DecodeTextString(textStr.Value, currentToUnicode);
                                var effectiveSize = lastTfSize * tmScaleY;
                                if (decoded.Contains(text) &&
                                    Math.Abs(effectiveSize - oldSize) < 0.5 &&
                                    lastTfSizeStart >= 0)
                                {
                                    // Scale the Tf size proportionally
                                    var actualNewTfSize = newSize / tmScaleY;
                                    return PatchFontSize(streamBytes, lastTfSizeStart, lastTfSizeEnd, actualNewTfSize);
                                }
                            }
                            break;

                        case "TJ":
                            // TJ array: text was decoded during array parsing
                            if (operands.Count >= 1 && operands[^1].obj is PdfString tjText)
                            {
                                var decoded = DecodeTextString(tjText.Value, currentToUnicode);
                                var effectiveSizeTj = lastTfSize * tmScaleY;
                                if (decoded.Contains(text) &&
                                    Math.Abs(effectiveSizeTj - oldSize) < 0.5 &&
                                    lastTfSizeStart >= 0)
                                {
                                    var actualNewTfSizeTj = newSize / tmScaleY;
                                    return PatchFontSize(streamBytes, lastTfSizeStart, lastTfSizeEnd, actualNewTfSizeTj);
                                }
                            }
                            break;
                    }
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }
        done:
        return null; // text not found
    }

    private static byte[] PatchFontSize(byte[] original, int sizeStart, int sizeEnd, double newSize)
    {
        // Skip leading whitespace in the size range to preserve it
        while (sizeStart < sizeEnd && (original[sizeStart] == ' ' || original[sizeStart] == '\t'
            || original[sizeStart] == '\r' || original[sizeStart] == '\n'))
            sizeStart++;

        // Use enough precision so size * tmScale rounds back to the intended value
        string sizeStr;
        if (newSize == Math.Floor(newSize))
            sizeStr = ((int)newSize).ToString(CultureInfo.InvariantCulture);
        else
            sizeStr = newSize.ToString("R", CultureInfo.InvariantCulture); // round-trip format
        var sizeBytes = Encoding.ASCII.GetBytes(sizeStr);

        var result = new byte[original.Length - (sizeEnd - sizeStart) + sizeBytes.Length];
        Array.Copy(original, 0, result, 0, sizeStart);
        Array.Copy(sizeBytes, 0, result, sizeStart, sizeBytes.Length);
        Array.Copy(original, sizeEnd, result, sizeStart + sizeBytes.Length, original.Length - sizeEnd);
        return result;
    }

    /// <summary>
    /// Rewrite the page (or a Form XObject) content so the text run matching
    /// <paramref name="text"/> is shown with <paramref name="newFont"/>: a subset
    /// of the new font is embedded into the document and the run's active Tf
    /// operator is repointed at the freshly registered resource. Mirrors the
    /// match-by-decoded-text approach used by ModifyFontSize / ModifyForegroundColor.
    /// </summary>
    public void ModifyFont(Page page, string text, Font newFont, double? targetY = null)
    {
        var reader = page.Reader;
        if (reader is null) return;
        var ttf = newFont?.SourceFontData?.TtfData;
        if (ttf is null || ttf.Length == 0) return;   // only fonts carrying a program
        var doc = reader.OwnerDocument;
        if (doc is null) return;

        // PDF /BaseFont names carry no spaces or style separators
        // ("Courier New" + Bold|Italic -> "CourierNewBoldItalic"), which is also what
        // the read-back path strips back to for FontName.
        var baseName = (newFont!.FontName ?? "Font").Replace(" ", "").Replace("-", "");

        // Text frequently lives inside a Form XObject (e.g. `q /Fm0 Do Q`).
        if (ModifyFontInFormXObjects(page.Dict, reader, doc, text, ttf, baseName))
            return;

        var contentStreams = GetContentStreams(page, reader);
        if (contentStreams.Count == 0) return;
        var combined = CombineStreams(contentStreams);
        var range = FindTfNameRange(combined, text, page.Dict, reader);
        if (range is null) return;

        var resName = UniqueFontResName(page.Dict, reader);
        FontEmbedder.Embed(doc, ttf, resName, baseName).AddToResources(page.Dict, reader);
        page.SetContentStream(PatchName(combined, range.Value.start, range.Value.end, resName));
    }

    private bool ModifyFontInFormXObjects(PdfDictionary dict, PdfReader reader,
        Document doc, string text, byte[] ttf, string baseFontName)
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
            var range = FindTfNameRange(streamData, text, xobjStream.Dict, reader);
            if (range is not null)
            {
                var resName = UniqueFontResName(xobjStream.Dict, reader);
                FontEmbedder.Embed(doc, ttf, resName, baseFontName).AddToResources(xobjStream.Dict, reader);
                var modified = PatchName(streamData, range.Value.start, range.Value.end, resName);
                xobjStream.Dict.Remove("Filter");
                xobjStream.Dict.Remove("DecodeParms");
                xobjStream.Dict.Set("Length", new PdfInteger(modified.Length));
                xobjStream.ReplaceData(modified);
                return true;
            }

            if (ModifyFontInFormXObjects(xobjStream.Dict, reader, doc, text, ttf, baseFontName))
                return true;
        }
        return false;
    }

    /// <summary>Walk a content stream and return the byte range of the font-name
    /// operand of the Tf that is active when the first text-showing operator whose
    /// decoded string contains <paramref name="text"/> is reached.</summary>
    private (int start, int end)? FindTfNameRange(byte[] streamBytes, string text,
        PdfDictionary pageDict, PdfReader reader)
    {
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();
        int lastTfNameStart = -1, lastTfNameEnd = -1;
        Dictionary<int, string>? currentToUnicode = null;
        // Only a simple (single-byte) font can be swapped for our simple WinAnsi
        // embedded font by repointing Tf: the shown bytes are reinterpreted under
        // the new font's encoding. A Type0/CID font shows 2-byte glyph IDs that a
        // simple font can't represent, so such runs are left untouched.
        bool currentFontIsSimple = false;

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
                    var arrTexts = new StringBuilder();
                    while (true)
                    {
                        var t = lexer.NextToken();
                        if (t.Kind == TokenKind.Eof) goto done;
                        if (t.Kind == TokenKind.ArrayEnd) break;
                        if (t.Kind == TokenKind.LiteralString || t.Kind == TokenKind.HexString)
                        {
                            var strBytes = t.BytesValue;
                            if (strBytes is not null)
                                arrTexts.Append(DecodeTextString(strBytes, currentToUnicode));
                        }
                    }
                    operands.Add((TokenKind.ArrayStart, new PdfString(
                        Cp1252.GetBytes(arrTexts.ToString())), startPos, (int)lexer.Position));
                    break;
                }
                case TokenKind.DictStart:
                {
                    int depth = 1;
                    while (depth > 0)
                    {
                        var t = lexer.NextToken();
                        if (t.Kind == TokenKind.Eof) goto done;
                        if (t.Kind == TokenKind.DictStart) depth++;
                        if (t.Kind == TokenKind.DictEnd) depth--;
                    }
                    operands.Clear();
                    break;
                }
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "Tf":
                            if (operands.Count >= 2 && operands[0].obj is PdfName fn)
                            {
                                if (fonts.TryGetValue(fn.Value, out var fontDict))
                                {
                                    currentToUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, reader);
                                    currentFontIsSimple = fontDict.GetName("Subtype") != "Type0";
                                }
                                else
                                {
                                    currentToUnicode = null;
                                    currentFontIsSimple = false;
                                }
                                lastTfNameStart = operands[0].startPos;
                                lastTfNameEnd = operands[0].endPos;
                            }
                            break;
                        case "Tj":
                        case "'":
                        case "\"":
                        case "TJ":
                            if (operands.Count >= 1 && operands[^1].obj is PdfString showStr)
                            {
                                var decoded = DecodeTextString(showStr.Value, currentToUnicode);
                                if (decoded.Contains(text) && lastTfNameStart >= 0 && currentFontIsSimple)
                                    return (lastTfNameStart, lastTfNameEnd);
                            }
                            break;
                    }
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }
        done:
        return null;
    }

    /// <summary>Pick a /Font resource key not already present in the container's
    /// resolved /Resources/Font dictionary.</summary>
    private static string UniqueFontResName(PdfDictionary containerDict, PdfReader reader)
    {
        var resources = containerDict.Get("Resources") as PdfDictionary
            ?? reader.ResolveDict(containerDict.Get("Resources"));
        var fontDict = resources is null ? null
            : (resources.Get("Font") as PdfDictionary ?? reader.ResolveDict(resources.Get("Font")));
        var existing = new HashSet<string>();
        if (fontDict is not null)
            foreach (var k in fontDict.Keys) existing.Add(k);
        var n = 0;
        string name;
        do { name = "AsF" + n++; } while (existing.Contains(name));
        return name;
    }

    private static byte[] PatchName(byte[] original, int nameStart, int nameEnd, string resName)
    {
        while (nameStart < nameEnd && (original[nameStart] == ' ' || original[nameStart] == '\t'
            || original[nameStart] == '\r' || original[nameStart] == '\n'))
            nameStart++;
        var nameBytes = Encoding.ASCII.GetBytes("/" + resName);
        var result = new byte[original.Length - (nameEnd - nameStart) + nameBytes.Length];
        Array.Copy(original, 0, result, 0, nameStart);
        Array.Copy(nameBytes, 0, result, nameStart, nameBytes.Length);
        Array.Copy(original, nameEnd, result, nameStart + nameBytes.Length, original.Length - nameEnd);
        return result;
    }

    private static string DecodeTextString(byte[] bytes, Dictionary<int, string>? toUnicode)
    {
        if (toUnicode is not null && toUnicode.Count > 0)
        {
            var sb = new StringBuilder();
            // Try 2-byte codes first (CID fonts)
            if (bytes.Length >= 2 && bytes.Length % 2 == 0)
            {
                bool allMapped = true;
                for (int i = 0; i < bytes.Length; i += 2)
                {
                    int code = (bytes[i] << 8) | bytes[i + 1];
                    if (toUnicode.TryGetValue(code, out var ch))
                        sb.Append(ch);
                    else
                    {
                        allMapped = false;
                        break;
                    }
                }
                if (allMapped) return sb.ToString();
                sb.Clear();
            }
            // 1-byte codes
            foreach (var b in bytes)
            {
                if (toUnicode.TryGetValue(b, out var ch))
                    sb.Append(ch);
                else
                    sb.Append((char)b);
            }
            return sb.ToString();
        }
        // Default: WinAnsiEncoding / Latin1
        return Cp1252.GetString(bytes);
    }

    private static List<byte[]> GetContentStreams(Page page, PdfReader reader)
    {
        var result = new List<byte[]>();
        var contentsObj = reader.Resolve(page.Dict.Get("Contents"));
        if (contentsObj is PdfStream stream)
            result.Add(reader.DecodeStream(stream));
        else if (contentsObj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null) result.Add(reader.DecodeStream(s));
            }
        }
        return result;
    }

    private static byte[] CombineStreams(List<byte[]> streams)
    {
        if (streams.Count == 1) return streams[0];
        var total = 0;
        foreach (var s in streams) total += s.Length + 1;
        var result = new byte[total];
        var pos = 0;
        foreach (var s in streams)
        {
            Array.Copy(s, 0, result, pos, s.Length);
            pos += s.Length;
            result[pos++] = (byte)'\n';
        }
        return result;
    }
}
