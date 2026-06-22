using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Finds and replaces text in PDF content streams.
/// Only handles simple WinAnsiEncoding / Latin1 text operators (Tj, TJ, ', ").
/// For CIDFont / Type0 fonts with identity encoding, replacement is limited to
/// characters already present in the font.
/// </summary>
public sealed class TextReplacer
{
    private int _replacementCount;
    private bool _isRegex;
    private Regex? _regexPattern;
    private bool _allowCrossOperator;

    /// <summary>Number of replacements made in the last Replace call.</summary>
    public int ReplacementCount => _replacementCount;

    /// <summary>
    /// When true, stop after the first match. Set by callers to honour
    /// <c>ReplaceTextStrategy.Scope.ReplaceFirst</c>. Default is replace-all.
    /// </summary>
    public bool ReplaceFirstOnly { get; set; }

    /// <summary>
    /// Redaction mode: when a match is fully deleted (replacement is empty),
    /// emit a TJ advance equal to the removed text's width instead of dropping
    /// the show operator, so following text on the same line keeps its position
    /// (no reflow). The glyphs are still gone from the content — extraction can
    /// no longer find them — but the layout is preserved. Used by
    /// <c>RedactionAnnotation.Redact()</c>.
    /// </summary>
    internal bool PreserveAdvanceOnDelete { get; set; }

    // Emit `[ kern ] TJ` — an advance with no glyphs — that moves the text
    // position right by the width of <paramref name="removedBytes"/>, so text
    // after a fully-deleted run stays put. No-op (writes nothing) when metrics
    // are unavailable or the width is negligible.
    private void WriteDeletionAdvance(MemoryStream result, byte[] removedBytes,
        PdfDictionary? fontDict, PdfReader reader, double fontSize)
    {
        if (!PreserveAdvanceOnDelete || fontDict is null || fontSize <= 0) return;
        double width;
        try
        {
            var metrics = FontMetrics.FromFontDict(fontDict, reader);
            if (metrics is null) return;
            width = metrics.MeasureString(removedBytes, fontSize);
        }
        catch { return; }
        WriteAdvance(result, width, fontSize);
    }

    // Width-preserving advance for a fully-deleted TJ array: total advance = sum of
    // the strings' widths minus the kerning numbers (scaled), so the whole operator
    // is replaced by a glyph-less advance of the same width.
    private void WriteDeletionAdvanceTJ(MemoryStream result, PdfArray arr,
        PdfDictionary? fontDict, PdfReader reader, double fontSize)
    {
        if (!PreserveAdvanceOnDelete || fontDict is null || fontSize <= 0) return;
        double width;
        try
        {
            var metrics = FontMetrics.FromFontDict(fontDict, reader);
            if (metrics is null) return;
            width = 0;
            foreach (var el in arr)
            {
                if (el is PdfString ps) width += metrics.MeasureString(ps.Value, fontSize);
                else if (el is PdfInteger pi) width += -pi.Value * fontSize / 1000.0;
                else if (el is PdfReal pr) width += -pr.Value * fontSize / 1000.0;
            }
        }
        catch { return; }
        WriteAdvance(result, width, fontSize);
    }

    private static void WriteAdvance(MemoryStream result, double width, double fontSize)
    {
        if (width <= 0.05) return;
        // PDF TJ: a number is subtracted from the advance (positive = shift left),
        // so a NEGATIVE number advances right by width.
        var kern = (int)Math.Round(-width * 1000.0 / fontSize);
        if (kern == 0) return;
        result.Write(Encoding.ASCII.GetBytes($"[{kern}] TJ"));
    }

    /// <summary>
    /// When set, only replace inside text-showing operators whose composed
    /// page-space Y (Tm.ty × CTM[3] + CTM[5]) is within
    /// <see cref="TargetYTolerance"/> of <see cref="TargetY"/>. Used by the
    /// per-fragment <c>TextFragment.Text</c> setter to scope the replacement
    /// to the operator that produced this fragment, instead of every matching
    /// occurrence on the page (otherwise iterating fragments[i].Text in a loop
    /// re-replaces the substring "X" inside "X-changed" each pass and the
    /// replacement string accumulates).
    /// </summary>
    internal double? TargetY { get; set; }

    /// <summary>
    /// Y-coordinate tolerance for <see cref="TargetY"/> matching, in PDF
    /// points. Default 6pt — wide enough to absorb the descent offset that
    /// <c>TextFragmentAbsorber.ComputeSegmentPosition</c> bakes into
    /// <c>Position.YIndent</c> (~2-3pt for 12pt body fonts), tight enough to
    /// distinguish text on adjacent lines (line height usually ≥ 12pt).
    /// </summary>
    internal double TargetYTolerance { get; set; } = 6.0;

    /// <summary>
    /// When set, only replace inside text-showing operators whose composed
    /// page-space X (Tm.tx × CTM[0] + Tm.ty × CTM[2] + CTM[4]) is within
    /// <see cref="TargetXTolerance"/> of <see cref="TargetX"/>. Used together
    /// with <see cref="TargetY"/> to scope a replacement to a single fragment's
    /// position — needed for region-scoped (rectangle) replacement where several
    /// matches share a baseline Y but only some fall inside the rectangle.
    /// </summary>
    internal double? TargetX { get; set; }

    /// <summary>X-coordinate tolerance for <see cref="TargetX"/>, in PDF points.
    /// Tight enough to distinguish neighbouring words on a line (typically spaced
    /// well beyond this), loose enough to absorb origin-vs-glyph-start rounding.</summary>
    internal double TargetXTolerance { get; set; } = 4.0;

    /// <summary>
    /// When true, a text-showing operator matches only if its entire shown text
    /// equals the search string (not merely contains it). Used by fragment
    /// deletion so removing a short fragment such as "$" does not strip the same
    /// substring out of a longer operator such as "$ 200.00" on the same row.
    /// </summary>
    internal bool MatchWholeOperator { get; set; }

    /// <summary>
    /// Replace all occurrences of <paramref name="search"/> with <paramref name="replacement"/>
    /// in the given page's content stream(s).
    /// </summary>
    public void Replace(Page page, string search, string replacement)
    {
        Replace(page, search, replacement, false);
    }

    /// <summary>
    /// Replace with cross-operator support enabled.
    /// Used when the caller (e.g., TextFragment.Text setter) knows the text exists
    /// as a cross-operator fragment.
    /// </summary>
    public void ReplaceWithCrossOperator(Page page, string search, string replacement)
    {
        _allowCrossOperator = true;
        Replace(page, search, replacement, false);
        _allowCrossOperator = false;
    }

    /// <summary>
    /// Replace occurrences of <paramref name="search"/> with <paramref name="replacement"/>
    /// in the given page's content stream(s). When <paramref name="isRegex"/> is <c>true</c>,
    /// <paramref name="search"/> is treated as a regular expression pattern.
    /// </summary>
    public void Replace(Page page, string search, string replacement, bool isRegex)
    {
        _replacementCount = 0;
        if (string.IsNullOrEmpty(search)) return;
        _isRegex = isRegex;
        _regexPattern = isRegex ? new Regex(search) : null;
        var reader = page.Reader;
        var processedXObjects = new HashSet<int>();

        // Walk the page's content stream first — Form XObjects invoked via /Do
        // are processed recursively from inside the walk so the parent's CTM at
        // each Do site flows into the XObject's text-matrix math (TargetY
        // scoping needs that composition; otherwise positions computed by the
        // absorber after the parent's cm don't line up with the XObject's
        // local Tm.ty values).
        var contentStreams = GetContentStreams(page, reader);
        if (contentStreams.Count > 0)
        {
            var combined = CombineStreams(contentStreams);
            var replaced = ReplaceInContentStream(combined, search, replacement,
                page.Dict, reader, processedXObjects);
            if (_replacementCount > 0)
            {
                page.SetContentStream(replaced);
            }
        }

        // Catch-all: any Form XObject in the page's resources that wasn't
        // reached via /Do (e.g. unreferenced legacy entries) still gets a pass
        // with identity CTM, mirroring the prior behaviour.
        ReplaceInFormXObjects(page.Dict, reader, search, replacement, processedXObjects);

        _isRegex = false;
        _regexPattern = null;
    }

    /// <summary>
    /// Replace text across all pages of a document, including Form XObjects.
    /// </summary>
    public void Replace(Document document, string search, string replacement)
        => Replace(document, search, replacement, false);

    /// <summary>
    /// Replace text across all pages of a document, including Form XObjects.
    /// When <paramref name="isRegex"/> is true, <paramref name="search"/> is a .NET regex.
    /// </summary>
    public void Replace(Document document, string search, string replacement, bool isRegex)
    {
        _replacementCount = 0;
        if (string.IsNullOrEmpty(search)) return;
        _isRegex = isRegex;
        _regexPattern = isRegex ? new Regex(search) : null;
        // Enable cross-operator replacement so matches that span separate
        // Tj/TJ operators (decoded text crossing positioning operators) are
        // not silently missed by the per-op matcher.
        var prevAllowCross = _allowCrossOperator;
        _allowCrossOperator = true;
        var processedXObjects = new HashSet<int>(); // track by obj number to avoid double processing

        try
        {
            foreach (var page in document.Pages)
            {
                if (ReplaceFirstOnly && _replacementCount > 0) break;
                var reader = page.Reader;

                // Walk page content first; XObjects are recursed via /Do.
                var contentStreams = GetContentStreams(page, reader);
                if (contentStreams.Count > 0)
                {
                    var combined = CombineStreams(contentStreams);
                    var count = _replacementCount;
                    var replaced = ReplaceInContentStream(combined, search, replacement,
                        page.Dict, reader, processedXObjects);

                    if (_replacementCount > count)
                    {
                        page.SetContentStream(replaced);
                    }
                }

                // Catch-all for XObjects not reached via /Do.
                ReplaceInFormXObjects(page.Dict, reader, search, replacement, processedXObjects);
            }
        }
        finally
        {
            _isRegex = false;
            _regexPattern = null;
            _allowCrossOperator = prevAllowCross;
        }
    }

    /// <summary>
    /// Replace text within a single Form XObject's own content stream (and any
    /// Form XObjects nested in its resources). Used by the TextFragment.Text setter
    /// for fragments extracted via TextFragmentAbsorber.Visit(XForm), which carry a
    /// null SourcePage — the producing operator lives in the form, not the page.
    /// </summary>
    public void Replace(XForm form, string search, string replacement)
    {
        _replacementCount = 0;
        if (form is null || string.IsNullOrEmpty(search)) return;
        var reader = form.Reader;
        var processed = new HashSet<int>();
        // Cross-operator on: form text frequently spans separate Tj/TJ operators.
        var prevAllowCross = _allowCrossOperator;
        _allowCrossOperator = true;
        try
        {
            // Nested Form XObjects first (identity CTM catch-all).
            ReplaceInFormXObjects(form.StreamDict, reader, search, replacement, processed);
            var decoded = form.DecodedBytes;
            var replaced = ReplaceInContentStream(decoded, search, replacement,
                form.StreamDict, reader, processed);
            if (_replacementCount > 0)
                form.SetDecodedContent(replaced);
        }
        finally
        {
            _allowCrossOperator = prevAllowCross;
        }
    }

    /// <summary>
    /// Process Form XObjects referenced from a page/XObject's Resources/XObject dict.
    /// Updates each XObject's content stream in-place (via the reader cache).
    /// </summary>
    private void ReplaceInFormXObjects(PdfDictionary dict, PdfReader reader,
        string search, string replacement, HashSet<int> processed)
    {
        var resources = reader.ResolveDict(dict.Get("Resources"));
        if (resources is null) return;
        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;

        foreach (var key in xobjects.Keys)
        {
            var xobjRef = xobjects.Get(key);

            // Only deduplicate indirect refs (we need object number)
            if (xobjRef is PdfIndirectRef indRef && !processed.Add(indRef.ObjectNumber))
                continue;

            var xobjStream = reader.ResolveStream(xobjRef);
            if (xobjStream is null || xobjStream.Dict.GetName("Subtype") != "Form") continue;

            // Recursively process nested XObjects within this Form XObject
            ReplaceInFormXObjects(xobjStream.Dict, reader, search, replacement, processed);

            // Process the Form XObject's own content
            var decoded = reader.DecodeStream(xobjStream);
            var countBefore = _replacementCount;
            var replaced = ReplaceInContentStream(decoded, search, replacement,
                xobjStream.Dict, reader, processed);

            if (_replacementCount > countBefore)
            {
                // Update the cached PdfStream with modified (uncompressed) content.
                // Remove the existing filter so PdfWriter will re-compress correctly.
                xobjStream.Dict.Remove("Filter");
                xobjStream.Dict.Remove("DecodeParms");
                xobjStream.ReplaceData(replaced);
            }
        }
    }

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
                                && IsAtTargetY(tmTy, ctmD, ctmTy)
                                && IsAtTargetX(tmTx, tmTy, ctmA, ctmC, ctmTx))
                            {
                                var decoded = DecodeString(str.Value, currentToUnicode, currentFontDict, reader);
                                var normalizedDecoded = NormalizeForSearch(decoded);
                                if (MatchesSearch(normalizedDecoded, normalizedSearch))
                                {
                                    var newText = ApplyReplace(normalizedDecoded, normalizedSearch, replacement);
                                    // Write everything before this operand
                                    result.Write(streamBytes, lastWritePos, operands[0].startPos - lastWritePos);

                                    if (newText.Length == 0)
                                    {
                                        // Full deletion: drop the show operator entirely so no
                                        // empty text-showing operator remains (which would still
                                        // be re-extracted as a zero-length fragment). In redaction
                                        // mode, leave a glyph-less advance so following text on the
                                        // line does not reflow.
                                        WriteDeletionAdvance(result, str.Value, currentFontDict, reader, currentFontSize);
                                    }
                                    else if (NeedsFontSwitch(newText, currentToUnicode, currentFontDict, reader))
                                    {
                                        // Switch to a standard font for the replacement text, then restore
                                        var fallbackFont = EnsureStandardFont(pageDict, reader);
                                        var fs = currentFontSize.ToString("F1", CultureInfo.InvariantCulture);
                                        result.Write(Encoding.ASCII.GetBytes(
                                            $"/{fallbackFont} {fs} Tf "));
                                        var latin = Encoding.Latin1.GetBytes(newText);
                                        WriteStringOperand(result, latin, false);
                                        result.Write(Encoding.ASCII.GetBytes(
                                            $" Tj /{currentFontName} {fs} Tf"));
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
                                && IsAtTargetY(tmTy, ctmD, ctmTy)
                                && IsAtTargetX(tmTx, tmTy, ctmA, ctmC, ctmTx))
                            {
                                // Pre-check: compute what the replaced text would be to decide
                                // font switch BEFORE encoding (avoids round-trip corruption).
                                var tjOrigText = ConcatenateTJText(arr, currentToUnicode, currentFontDict, reader);
                                var tjNormalizedOrig = NormalizeForSearch(tjOrigText);
                                var tjNormalizedSearch = NormalizeForSearch(search);
                                if (MatchesSearch(tjNormalizedOrig, tjNormalizedSearch))
                                {
                                    var tjReplacedText = tjNormalizedOrig.Replace(tjNormalizedSearch, replacement, StringComparison.Ordinal);
                                    result.Write(streamBytes, lastWritePos, operands[0].startPos - lastWritePos);

                                    if (tjReplacedText.Length == 0)
                                    {
                                        // Full deletion: drop the entire TJ operator so no
                                        // empty text-showing operator remains (which would
                                        // still be re-extracted as a zero-length fragment). In
                                        // redaction mode, leave a glyph-less advance so following
                                        // text on the line does not reflow.
                                        WriteDeletionAdvanceTJ(result, arr, currentFontDict, reader, currentFontSize);
                                        _replacementCount++;
                                    }
                                    else if (NeedsFontSwitch(tjReplacedText, currentToUnicode, currentFontDict, reader))
                                    {
                                        var fallbackFont = EnsureStandardFont(pageDict, reader);
                                        var fs = currentFontSize.ToString("F1", CultureInfo.InvariantCulture);
                                        result.Write(Encoding.ASCII.GetBytes(
                                            $"/{fallbackFont} {fs} Tf "));
                                        var latin = Encoding.Latin1.GetBytes(tjReplacedText);
                                        WriteStringOperand(result, latin, false);
                                        result.Write(Encoding.ASCII.GetBytes(
                                            $" Tj /{currentFontName} {fs} Tf"));
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
                                && IsAtTargetY(tmTy, ctmD, ctmTy)
                                && IsAtTargetX(tmTx, tmTy, ctmA, ctmC, ctmTx))
                            {
                                var decoded = DecodeString(str2.Value, currentToUnicode, currentFontDict, reader);
                                var normalizedDecoded2 = NormalizeForSearch(decoded);
                                if (MatchesSearch(normalizedDecoded2, normalizedSearch))
                                {
                                    var newText = ApplyReplace(normalizedDecoded2, normalizedSearch, replacement);

                                    result.Write(streamBytes, lastWritePos, operands[0].startPos - lastWritePos);
                                    if (NeedsFontSwitch(newText, currentToUnicode, currentFontDict, reader))
                                    {
                                        var fallbackFont = EnsureStandardFont(pageDict, reader);
                                        var fs = currentFontSize.ToString("F1", CultureInfo.InvariantCulture);
                                        result.Write(Encoding.ASCII.GetBytes(
                                            $"/{fallbackFont} {fs} Tf "));
                                        var latin = Encoding.Latin1.GetBytes(newText);
                                        WriteStringOperand(result, latin, false);
                                        result.Write(Encoding.ASCII.GetBytes(
                                            $" ' /{currentFontName} {fs} Tf"));
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
                                        && xobjStream.Dict.GetName("Subtype") == "Form")
                                    {
                                        var xobjBytes = reader.DecodeStream(xobjStream);
                                        var beforeXobj = _replacementCount;
                                        var xobjReplaced = ReplaceInContentStream(xobjBytes,
                                            search, replacement,
                                            xobjStream.Dict, reader, processedXObjects,
                                            ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy);
                                        if (_replacementCount > beforeXobj)
                                        {
                                            xobjStream.Dict.Remove("Filter");
                                            xobjStream.Dict.Remove("DecodeParms");
                                            xobjStream.ReplaceData(xobjReplaced);
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
            var crossResult = TryCrossOperatorReplace(output, search, replacement, pageDict, reader);
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
        PdfDictionary pageDict, PdfReader reader)
    {
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var normalizedSearch = NormalizeForSearch(search);

        // Collect text operators: (decodedText, operandStart, operatorEnd)
        var textOps = new List<(string text, int opStart, int opEnd)>();
        var lexer2 = new PdfLexer(streamBytes);
        var ops2 = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();
        Dictionary<int, string>? curToUnicode = null;
        PdfDictionary? curFontDict = null;
        string? curFontName = null;
        double curFontSize = 12;

        while (true)
        {
            var sp = (int)lexer2.Position;
            var tok = lexer2.NextToken();
            if (tok.Kind == TokenKind.Eof) break;
            var ep = (int)lexer2.Position;

            switch (tok.Kind)
            {
                case TokenKind.Integer:
                case TokenKind.Real:
                case TokenKind.Name:
                    ops2.Add((tok.Kind, tok.Kind == TokenKind.Name ? new PdfName(tok.StringValue!) :
                        tok.Kind == TokenKind.Integer ? new PdfInteger(tok.IntValue) :
                        (PdfObject)new PdfReal(tok.RealValue), sp, ep));
                    break;
                case TokenKind.LiteralString:
                    ops2.Add((tok.Kind, new PdfString(tok.BytesValue!), sp, ep));
                    break;
                case TokenKind.HexString:
                    ops2.Add((tok.Kind, new PdfString(tok.BytesValue!, isHex: true), sp, ep));
                    break;
                case TokenKind.ArrayStart:
                    var arr = ParseContentArrayWithPositions(lexer2, out var aep);
                    ops2.Add((TokenKind.ArrayStart, arr, sp, aep));
                    break;
                case TokenKind.Keyword:
                    var op = tok.StringValue!;
                    if (op == "Tf" && ops2.Count >= 2 && ops2[0].obj is PdfName fn)
                    {
                        curFontName = fn.Value;
                        if (ops2[1].obj is PdfInteger fsi) curFontSize = fsi.Value;
                        else if (ops2[1].obj is PdfReal fsr) curFontSize = fsr.Value;
                        if (fonts.TryGetValue(curFontName, out var fd))
                        { curFontDict = fd; curToUnicode = TextAbsorber.ParseToUnicodeFromDict(fd, reader); }
                        else { curFontDict = null; curToUnicode = null; }
                    }
                    else if (op is "Tj" or "'" && ops2.Count >= 1 && ops2[0].obj is PdfString s)
                    {
                        var decoded = DecodeString(s.Value, curToUnicode, curFontDict, reader);
                        textOps.Add((decoded, ops2[0].startPos, ep));
                    }
                    else if (op == "TJ" && ops2.Count >= 1 && ops2[0].obj is PdfArray tjArr)
                    {
                        var sb = new StringBuilder();
                        foreach (var item in tjArr)
                            if (item is PdfString ps)
                                sb.Append(DecodeString(ps.Value, curToUnicode, curFontDict, reader));
                        textOps.Add((sb.ToString(), ops2[0].startPos, ep));
                    }
                    ops2.Clear();
                    break;
                default:
                    ops2.Clear();
                    break;
            }
        }

        // Concatenate all text and search for the pattern
        var allText = new StringBuilder();
        foreach (var (text, _, _) in textOps)
            allText.Append(text);

        var fullText = NormalizeForSearch(allText.ToString());

        // Enumerate match spans as (start, length) — literal IndexOf or regex match.
        (int idx, int len) NextMatch(int from)
        {
            if (_isRegex && _regexPattern is not null)
            {
                var m = _regexPattern.Match(fullText, from);
                return m.Success ? (m.Index, m.Length) : (-1, 0);
            }
            var i = fullText.IndexOf(normalizedSearch, from, StringComparison.Ordinal);
            return i < 0 ? (-1, 0) : (i, normalizedSearch.Length);
        }

        var (searchIdx, searchLen) = NextMatch(0);
        if (searchIdx < 0) return null;

        // Find which operators span this match
        var charPos = 0;
        var result = new MemoryStream();
        var lastWrite = 0;
        var replaced = false;

        while (searchIdx >= 0)
        {
            if (ReplaceFirstOnly && replaced) break;

            // Find operator range for this match
            charPos = 0;
            int firstOp = -1, lastOp = -1;
            for (var i = 0; i < textOps.Count; i++)
            {
                var opTextLen = textOps[i].text.Length;
                if (charPos + opTextLen > searchIdx && firstOp < 0)
                    firstOp = i;
                if (charPos + opTextLen >= searchIdx + searchLen)
                { lastOp = i; break; }
                charPos += opTextLen;
            }

            // Skip matches that fall entirely inside ONE operator — the per-op
            // pass already handled those (or chose not to). Cross-op only adds
            // value for spans that cover multiple operators.
            if (firstOp >= 0 && lastOp >= 0 && firstOp != lastOp)
            {
                // Write everything before the first matched operator
                result.Write(streamBytes, lastWrite, textOps[firstOp].opStart - lastWrite);

                // Replace operator-by-operator: first gets replacement text,
                // rest get empty strings. Preserves inter-operator positioning (Td/Tm).
                for (var oi = firstOp; oi <= lastOp; oi++)
                {
                    if (oi > firstOp)
                    {
                        // Write gap between operators (positioning commands like Td, Tm)
                        var gapStart = textOps[oi - 1].opEnd;
                        var gapEnd = textOps[oi].opStart;
                        if (gapEnd > gapStart)
                            result.Write(streamBytes, gapStart, gapEnd - gapStart);
                    }

                    if (oi == firstOp)
                    {
                        // First operator: emit font switch + replacement text
                        var fallbackFont = EnsureStandardFont(pageDict, reader);
                        var fs = curFontSize.ToString("F1", CultureInfo.InvariantCulture);
                        result.Write(Encoding.ASCII.GetBytes($"/{fallbackFont} {fs} Tf "));
                        var latin = Encoding.Latin1.GetBytes(replacement);
                        WriteStringOperand(result, latin, false);
                        result.Write(Encoding.ASCII.GetBytes(" Tj "));
                        if (curFontName is not null)
                            result.Write(Encoding.ASCII.GetBytes($"/{curFontName} {fs} Tf "));
                    }
                    else
                    {
                        // Subsequent operators: emit empty string
                        result.Write(Encoding.ASCII.GetBytes("() Tj "));
                    }
                }

                lastWrite = textOps[lastOp].opEnd;
                _replacementCount++;
                replaced = true;
            }

            // Look for next occurrence (advance past the matched span; skipped
            // single-op matches still need to advance past their length to avoid
            // an infinite loop on regex zero-width corner cases).
            (searchIdx, searchLen) = NextMatch(searchIdx + Math.Max(searchLen, 1));
        }

        if (!replaced) return null;

        if (lastWrite < streamBytes.Length)
            result.Write(streamBytes, lastWrite, streamBytes.Length - lastWrite);

        return result.ToArray();
    }

    private bool TryReplaceTJArray(PdfArray arr, string search, string replacement,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, PdfReader reader,
        double fontSize, out PdfArray newArr)
    {
        // First, concatenate all string parts to see if search text spans them.
        // Large negative kernings are treated as synthetic word-space, mirroring
        // the TextFragmentAbsorber reader — but only when the next PdfString
        // doesn't already begin with ' ', so we don't double-up the space.
        var fullText = new StringBuilder();
        var parts = new List<(int index, string text, bool isHex)>();

        bool NextStringStartsWithSpace(int from)
        {
            for (var j = from + 1; j < arr.Count; j++)
            {
                if (arr[j] is PdfString ps)
                {
                    // Decode (not raw byte compare) — CID/Type0 fonts map non-0x20
                    // bytes to the space glyph via ToUnicode/encoding tables.
                    var decodedPeek = DecodeString(ps.Value, toUnicode, fontDict, reader);
                    return decodedPeek.Length > 0 && decodedPeek[0] == ' ';
                }
            }
            return false;
        }

        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfString s)
            {
                var decoded = DecodeString(s.Value, toUnicode, fontDict, reader);
                parts.Add((i, decoded, s.IsHex));
                fullText.Append(decoded);
            }
            else if ((arr[i] is PdfInteger adj && adj.Value < -190)
                  || (arr[i] is PdfReal adjR && adjR.Value < -190))
            {
                if (!NextStringStartsWithSpace(i))
                    fullText.Append(' ');
            }
        }

        var combinedText = fullText.ToString();
        var normalizedCombined = NormalizeForSearch(combinedText);
        var normalizedSearch = NormalizeForSearch(search);
        if (!MatchesSearch(normalizedCombined, normalizedSearch))
        {
            newArr = arr;
            return false;
        }

        // Locate the match span so we can rewrite only the matched region and
        // keep everything after it intact. Preserving the suffix structure keeps
        // downstream glyph positions aligned with the original layout instead of
        // flattening the whole TJ (which shifts after-match glyphs when the
        // replacement width differs from the matched region width).
        int matchStart = _isRegex && _regexPattern is not null
            ? _regexPattern.Match(normalizedCombined).Index
            : normalizedCombined.IndexOf(normalizedSearch, StringComparison.Ordinal);
        int matchLen = _isRegex && _regexPattern is not null
            ? _regexPattern.Match(normalizedCombined).Length
            : normalizedSearch.Length;

        // Flat-string fallback (used when match position is unavailable or when
        // match covers the whole TJ — splitting adds no value). The TJ caller
        // owns the _replacementCount increment, so this path must NOT call
        // ApplyReplace (which would double-count).
        PdfArray FlatReplace()
        {
            var replacedText = _isRegex && _regexPattern is not null
                ? _regexPattern.Replace(normalizedCombined, replacement)
                : normalizedCombined.Replace(normalizedSearch, replacement, StringComparison.Ordinal);
            var replacedBytes = EncodeString(replacedText, toUnicode, fontDict);
            var useHex = parts.Count > 0 && parts[0].isHex;
            var flat = new PdfArray();
            flat.Add(new PdfString(replacedBytes, useHex));
            return flat;
        }

        if (matchStart < 0 || matchStart + matchLen > combinedText.Length)
        {
            newArr = FlatReplace();
            return true;
        }

        // Replace-all across multiple occurrences: the structured single-match
        // path below only rewrites the first match (keeping the suffix intact),
        // so when every match must be replaced and more than one is present,
        // fall back to a flat replacement that substitutes them all.
        bool multipleMatches = _isRegex && _regexPattern is not null
            ? _regexPattern.Matches(normalizedCombined).Count > 1
            : CountOccurrences(normalizedCombined, normalizedSearch) > 1;
        if (!ReplaceFirstOnly && multipleMatches)
        {
            newArr = FlatReplace();
            return true;
        }

        // Build a per-character map (combinedText char index → arr element index).
        // Must use the SAME rule as the concatenation loop above: a synthetic
        // space is only appended for a large negative kerning when the next
        // PdfString doesn't already start with a space.  Keep the two in sync.
        var charMap = new List<int>(combinedText.Length);
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfString sm)
            {
                var n = DecodeString(sm.Value, toUnicode, fontDict, reader).Length;
                for (var k = 0; k < n; k++) charMap.Add(i);
            }
            else if ((arr[i] is PdfInteger ia && ia.Value < -190)
                  || (arr[i] is PdfReal ra && ra.Value < -190))
            {
                if (!NextStringStartsWithSpace(i))
                    charMap.Add(-1); // synthetic space
            }
        }

        // Prefix/suffix text (unchanged portions on either side of the match).
        var prefixText = combinedText.Substring(0, matchStart);
        var suffixStart = matchStart + matchLen;
        var suffixText = combinedText.Substring(suffixStart);

        // If suffix is empty, flat-replace is equivalent (nothing to push back).
        if (suffixText.Length == 0)
        {
            newArr = FlatReplace();
            return true;
        }

        // Map match boundaries back to the TJ-array coordinates (arrIdx + byte
        // offset inside that string) so the width-compensation helper can
        // identify the matched slice of each PdfString.
        int startArrIdx = charMap[matchStart];
        int endArrIdx = charMap[matchStart + matchLen - 1];
        // Offset-inside-string = count of prior chars mapped to the same arrIdx
        // before the match boundary.
        int CountCharsUpTo(int stop, int arrIdx)
        {
            var c = 0;
            for (var k = 0; k < stop; k++)
                if (charMap[k] == arrIdx) c++;
            return c;
        }
        int startOffset = CountCharsUpTo(matchStart, startArrIdx);
        int endOffset = CountCharsUpTo(matchStart + matchLen - 1, endArrIdx);

        // Emit:  [ (prefix + replacement)  <compensation-kerning>  (suffix) ]
        //
        // Two sub-strings for the unchanged + replaced portion and the tail, with
        // an optional integer kerning between them that compensates for the width
        // change caused by the replacement. This keeps the post-match glyph row
        // at its original X — the behaviour that tests using ReplaceAdjustment.None
        // depend on.  When the replacement width matches the original matched
        // region (including any within-match kerning) the compensation is zero
        // and the kerning element is omitted.
        var useHex2 = parts.Count > 0 && parts[0].isHex;

        // Compute the width change the replacement introduces, in PDF
        // text-space (1/1000 em) units, so we can emit it as a TJ kerning.
        int kernCompensation = ComputeTJReplaceKern(arr, startArrIdx, startOffset,
            endArrIdx, endOffset, replacement,
            toUnicode, fontDict, reader, fontSize);

        newArr = new PdfArray();

        // Emit the prefix by COPYING the original TJ-array elements before the
        // match — this preserves the original inter-element kerns (including
        // big-negative kerns that were synthesized into spaces in `combinedText`
        // for matching purposes). Only the matched region itself is replaced.
        // The string element containing the match start contributes its leading
        // bytes (chars before startOffset) followed by the replacement bytes.
        for (var i = 0; i < startArrIdx; i++)
            newArr.Add(arr[i]);

        // Build the prefix-and-replacement bytes from the matched string's
        // leading slice + the replacement text.
        byte[] preRepBytes;
        if (arr[startArrIdx] is PdfString startStr && startOffset > 0)
        {
            // Decode just the prefix bytes (chars before startOffset) and
            // re-encode together with the replacement.
            var preBytes = new byte[startOffset];
            Buffer.BlockCopy(startStr.Value, 0, preBytes, 0, startOffset);
            var preStr = DecodeString(preBytes, toUnicode, fontDict, reader);
            preRepBytes = EncodeString(preStr + replacement, toUnicode, fontDict);
        }
        else
        {
            preRepBytes = EncodeString(replacement, toUnicode, fontDict);
        }
        newArr.Add(new PdfString(preRepBytes, useHex2));

        if (kernCompensation != 0)
        {
            // Split a single large compensation into several smaller kernings
            // so none individually trips the reader's word-break heuristic
            // (adj < -190 becomes synthetic space). Using chunks of |adj| ≤ 180
            // keeps each step below the threshold while still summing to the
            // needed advance correction. Only negative (push-right) splitting
            // matters here — positive kernings never trigger the heuristic.
            const int SafeChunk = 180;
            int remaining = kernCompensation;
            if (remaining < 0)
            {
                while (remaining < -SafeChunk)
                {
                    newArr.Add(new PdfInteger(-SafeChunk));
                    remaining += SafeChunk;
                }
                if (remaining != 0) newArr.Add(new PdfInteger(remaining));
            }
            else
            {
                // Positive kernings are already safe (advance shrink).
                newArr.Add(new PdfInteger(remaining));
            }
        }

        // Emit the suffix by COPYING the original TJ-array elements after the
        // match end, rather than collapsing them into a single PdfString. This
        // preserves the original kerning values (including big-negative kerns
        // that were synthesized into spaces in `combinedText` for matching
        // purposes) so subsequent text stays at its original X position. The
        // first PdfString after the match needs its leading bytes trimmed
        // when the match ended partway through it.
        bool firstSuffixString = true;
        for (var i = endArrIdx; i < arr.Count; i++)
        {
            var el = arr[i];
            if (i == endArrIdx)
            {
                // For the string containing the match end, emit only the bytes
                // AFTER the match.
                if (el is not PdfString endStr) continue;
                int trimStart = endOffset + 1;
                if (trimStart >= endStr.Value.Length) continue;
                var tail = new byte[endStr.Value.Length - trimStart];
                Buffer.BlockCopy(endStr.Value, trimStart, tail, 0, tail.Length);
                newArr.Add(new PdfString(tail, endStr.IsHex));
                firstSuffixString = false;
            }
            else
            {
                newArr.Add(el);
                if (el is PdfString) firstSuffixString = false;
            }
        }

        // If no suffix elements were emitted (match ended exactly at the last
        // string with no tail bytes), append an empty PdfString so the array
        // structure remains valid. Otherwise, if we emitted only kerns and no
        // PdfString (rare — match consumed the final string and only kerns
        // followed), append an empty string.
        if (firstSuffixString)
            newArr.Add(new PdfString(System.Array.Empty<byte>(), useHex2));
        return true;
    }

    /// <summary>
    /// Compute a TJ kerning adjustment (in 1/1000 em units, PDF sign convention:
    /// positive = shift left, i.e. shrink advance) that compensates for the
    /// width change between the matched region in the original TJ and the
    /// replacement string.  Returns 0 when the widths match (or when metrics
    /// aren't available — caller then emits no kerning, preserving the current
    /// behaviour for the no-font-metrics fallback path).
    /// </summary>
    private int ComputeTJReplaceKern(PdfArray arr,
        int startArrIdx, int startOffset, int endArrIdx, int endOffset,
        string replacement,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, PdfReader reader,
        double fontSize)
    {
        if (fontDict is null || fontSize <= 0) return 0;
        FontMetrics? metrics;
        try { metrics = FontMetrics.FromFontDict(fontDict, reader); }
        catch { return 0; }
        if (metrics is null) return 0;

        // --- Original matched-region width ---
        // Walk [startArrIdx,endArrIdx] summing (a) per-string glyph widths of
        // chars inside the match span and (b) kerning items between strings in
        // the span.  Widths come from MeasureString on byte sub-slices so
        // Type1/TrueType width tables are honoured.
        double origAdvance = 0;
        for (var i = startArrIdx; i <= endArrIdx; i++)
        {
            var el = arr[i];
            if (el is PdfString ps)
            {
                var bytes = ps.Value;
                int byteStart = 0, byteEnd = bytes.Length;
                if (i == startArrIdx && startOffset > 0)
                    byteStart = Math.Min(startOffset, bytes.Length);
                if (i == endArrIdx && endOffset + 1 < bytes.Length)
                    byteEnd = endOffset + 1;
                if (byteEnd > byteStart)
                {
                    var slice = new byte[byteEnd - byteStart];
                    Buffer.BlockCopy(bytes, byteStart, slice, 0, slice.Length);
                    try { origAdvance += metrics.MeasureString(slice, fontSize); }
                    catch { return 0; }
                }
            }
            else if (i > startArrIdx && i < endArrIdx)
            {
                // Kerning inside the match span (both edges are strings).
                // Spec: TJ number operand is subtracted from current advance,
                // scaled by fontSize/1000.
                double adj = el switch
                {
                    PdfInteger pi => pi.Value,
                    PdfReal pr => pr.Value,
                    _ => 0
                };
                origAdvance += -adj * fontSize / 1000.0;
            }
        }

        // --- Replacement width ---
        double newAdvance;
        try
        {
            var repBytes = EncodeString(replacement, toUnicode, fontDict);
            newAdvance = metrics.MeasureString(repBytes, fontSize);
        }
        catch { return 0; }

        // Delta in PDF points → back to 1/1000 em.  Positive delta means the
        // replacement is narrower than the original; we need a NEGATIVE TJ
        // kerning so the following text is pushed forward to the original X.
        var deltaPt = origAdvance - newAdvance;
        if (Math.Abs(deltaPt) < 0.05) return 0; // below visible threshold
        var kern = (int)Math.Round(-deltaPt * 1000.0 / fontSize);
        // Clamp to the PDF spec's reasonable range to avoid pathological values
        // from bad metrics: ±10000 is already a massive advance delta (~10em).
        if (kern > 10000) kern = 10000;
        if (kern < -10000) kern = -10000;
        return kern;
    }

    private static string DecodeString(byte[] bytes, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader? reader = null)
    {
        // Delegate to TextAbsorber for consistent decoding (handles /Differences, named encodings, etc.)
        if (reader is not null)
            return TextAbsorber.DecodeStringPublic(bytes, toUnicode, fontDict, reader);

        if (toUnicode is not null)
        {
            var isCid = fontDict?.GetName("Subtype") == "Type0";
            var sb = new StringBuilder();
            if (isCid && bytes.Length >= 2)
            {
                for (var i = 0; i + 1 < bytes.Length; i += 2)
                {
                    var code = (bytes[i] << 8) | bytes[i + 1];
                    sb.Append(toUnicode.TryGetValue(code, out var mapped) ? mapped : "\uFFFD");
                }
            }
            else
            {
                foreach (var b in bytes)
                    sb.Append(toUnicode.TryGetValue(b, out var mapped) ? mapped : ((char)b).ToString());
            }
            return sb.ToString();
        }

        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    /// Normalize text for search comparison: apply NFKD decomposition to map
    /// Arabic presentation forms to base characters, matching TextFragmentAbsorber behavior.
    /// </summary>
    private static string NormalizeForSearch(string text)
    {
        bool hasPresentationForms = false;
        foreach (var ch in text)
        {
            if ((ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF'))
            {
                hasPresentationForms = true;
                break;
            }
        }
        if (!hasPresentationForms) return text;
        return text.Normalize(System.Text.NormalizationForm.FormKD);
    }

    /// <summary>
    /// Check that the page-space Y of the current text matrix
    /// (Tm.ty × CTM[3] + CTM[5]) is within tolerance of <see cref="TargetY"/>.
    /// Returns true unconditionally when TargetY is unset (page-wide replace,
    /// the default behaviour). Only handles axis-aligned scale+translate CTMs;
    /// rotation/skew degrades to "no replacement" which is safer than
    /// page-wide for the per-fragment use case.
    /// </summary>
    private bool IsAtTargetY(double tmTy, double ctmD, double ctmTy)
    {
        if (TargetY is not double targetY) return true;
        var pageY = ctmD * tmTy + ctmTy;
        return Math.Abs(pageY - targetY) <= TargetYTolerance;
    }

    /// <summary>
    /// Check that the page-space X of the current text-matrix origin
    /// (Tm.tx × CTM[0] + Tm.ty × CTM[2] + CTM[4]) is within tolerance of
    /// <see cref="TargetX"/>. Returns true unconditionally when TargetX is unset
    /// (the default — X is not scoped). Companion to <see cref="IsAtTargetY"/>.
    /// </summary>
    private bool IsAtTargetX(double tmTx, double tmTy, double ctmA, double ctmC, double ctmTx)
    {
        if (TargetX is not double targetX) return true;
        var pageX = ctmA * tmTx + ctmC * tmTy + ctmTx;
        return Math.Abs(pageX - targetX) <= TargetXTolerance;
    }

    private static double ToDouble(PdfObject obj) => obj switch
    {
        PdfInteger pi => pi.Value,
        PdfReal pr => pr.Value,
        _ => 0
    };

    /// <summary>Check if <paramref name="text"/> contains a match for the current search.</summary>
    private bool MatchesSearch(string text, string normalizedSearch)
    {
        if (ReplaceFirstOnly && _replacementCount > 0) return false;
        if (MatchWholeOperator)
            return string.Equals(text, normalizedSearch, StringComparison.Ordinal);
        if (_isRegex && _regexPattern is not null)
            return _regexPattern.IsMatch(text);
        return text.Contains(normalizedSearch, StringComparison.Ordinal);
    }

    /// <summary>Replace matches in <paramref name="text"/> for the current search.
    /// Honours <see cref="ReplaceFirstOnly"/>.</summary>
    private string ApplyReplace(string text, string normalizedSearch, string replacement)
    {
        if (_isRegex && _regexPattern is not null)
        {
            if (ReplaceFirstOnly)
            {
                var match = _regexPattern.Match(text);
                if (!match.Success) return text;
                _replacementCount++;
                return string.Concat(text.AsSpan(0, match.Index), replacement,
                    text.AsSpan(match.Index + match.Length));
            }
            _replacementCount += _regexPattern.Matches(text).Count;
            return _regexPattern.Replace(text, replacement);
        }
        if (ReplaceFirstOnly)
        {
            int idx = text.IndexOf(normalizedSearch, StringComparison.Ordinal);
            if (idx < 0) return text;
            _replacementCount++;
            return string.Concat(text.AsSpan(0, idx), replacement,
                text.AsSpan(idx + normalizedSearch.Length));
        }
        _replacementCount += CountOccurrences(text, normalizedSearch);
        return text.Replace(normalizedSearch, replacement, StringComparison.Ordinal);
    }

    private static string ConcatenateTJText(PdfArray arr, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader)
    {
        // Pre-scan: collect negative adjustments for dynamic word-break detection.
        // Same algorithm as TextFragmentAbsorber: if all adjustments are uniformly
        // large (character spacing), don't insert spaces.
        var sb = new StringBuilder();
        int lastLen = 0;
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfString s)
            {
                var decoded = DecodeString(s.Value, toUnicode, fontDict, reader);
                sb.Append(decoded);
                lastLen = decoded.Length;
            }
            else
            {
                double v = 0;
                if (arr[i] is PdfInteger ai) v = ai.Value;
                else if (arr[i] is PdfReal ar) v = ar.Value;
                if (v < -190 && lastLen != 1 && (sb.Length == 0 || sb[^1] != ' '))
                    sb.Append(' ');
            }
        }
        return sb.ToString();
    }

    private static string EnsureStandardFont(PdfDictionary pageDict, PdfReader reader)
    {
        const string fallbackName = "_AsposePdfHlv";
        var resources = pageDict.Get("Resources") as PdfDictionary;
        resources ??= reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            pageDict.Set("Resources", resources);
        }
        var fonts = resources.Get("Font") as PdfDictionary;
        fonts ??= reader.ResolveDict(resources.Get("Font"));
        if (fonts is null)
        {
            fonts = new PdfDictionary();
            resources.Set("Font", fonts);
        }
        if (fonts.Get(fallbackName) is null)
        {
            var fontDict = new PdfDictionary();
            fontDict.Set("Type", new PdfName("Font"));
            fontDict.Set("Subtype", new PdfName("Type1"));
            fontDict.Set("BaseFont", new PdfName("Helvetica"));
            fontDict.Set("Encoding", new PdfName("WinAnsiEncoding"));
            fonts.Set(fallbackName, fontDict);
        }
        return fallbackName;
    }

    private static double GetCurrentFontSize(List<(TokenKind kind, PdfObject obj, int startPos, int endPos)> operands)
    {
        // Font size is typically the second operand before a Tf operator,
        // but here we're in a Tj context. Default to 12 if unknown.
        return 12.0;
    }

    /// <summary>
    /// Build a reverse map from Unicode characters to CID codes, including NFKD-decomposed
    /// variants so base Arabic characters (e.g., U+0627 Alef) can map to presentation form
    /// codes (e.g., U+FE8E → code N) that exist in the font's ToUnicode CMap.
    /// </summary>
    /// <remarks>
    /// Two-pass approach: first adds single-character NFKD decompositions (e.g., U+FEF3 → U+064A),
    /// then multi-character ones (e.g., U+FE8B → U+064A + U+0654). This ensures that plain
    /// presentation forms (like Yeh U+FEF1-FEF4) are preferred over compound forms
    /// (like Yeh-with-Hamza U+FE89-FE8C) when both decompose to the same base character.
    /// </remarks>
    private static Dictionary<string, int> BuildReverseMap(Dictionary<int, string> toUnicode)
    {
        var reverseMap = new Dictionary<string, int>();

        // Pass 0: direct Unicode string → code (no decomposition)
        foreach (var (code, unicode) in toUnicode)
            reverseMap.TryAdd(unicode, code);

        // Pass 1: single-char NFKD decompositions (plain presentation forms, e.g. U+FEF3 → U+064A)
        foreach (var (code, unicode) in toUnicode)
        {
            if (unicode.Length != 1) continue;
            var ch = unicode[0];
            if ((ch < '\uFB50' || ch > '\uFDFF') && (ch < '\uFE70' || ch > '\uFEFF')) continue;

            var decomposed = unicode.Normalize(System.Text.NormalizationForm.FormKD);
            if (decomposed.Length == 1)
                reverseMap.TryAdd(decomposed, code);
        }

        // Pass 2: multi-char NFKD decompositions (compound forms, e.g. U+FE8B → U+064A + U+0654)
        // Only adds base characters that weren't already mapped in pass 1.
        foreach (var (code, unicode) in toUnicode)
        {
            if (unicode.Length != 1) continue;
            var ch = unicode[0];
            if ((ch < '\uFB50' || ch > '\uFDFF') && (ch < '\uFE70' || ch > '\uFEFF')) continue;

            var decomposed = unicode.Normalize(System.Text.NormalizationForm.FormKD);
            if (decomposed.Length > 1)
            {
                foreach (var dc in decomposed)
                    reverseMap.TryAdd(dc.ToString(), code);
            }
        }

        return reverseMap;
    }

    private static bool NeedsFontSwitch(string text, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader? reader = null)
    {
        var isCid = fontDict?.GetName("Subtype") == "Type0";

        // CID/Type0 fonts use 2-byte character codes.  If there is no ToUnicode
        // map we cannot build a reverse map, so we must switch to a standard font
        // for any replacement text.
        if (isCid && toUnicode is null)
            return true;

        if (toUnicode is not null)
        {
            var reverseMap = BuildReverseMap(toUnicode);

            if (text.Any(ch => !reverseMap.ContainsKey(ch.ToString())))
                return true;
        }

        // Non-CID fonts with /Encoding containing /Differences: if any replacement
        // character's Latin1 byte value is remapped by the Differences array, the
        // round-trip will produce wrong glyphs — switch to a standard font.
        if (!isCid && fontDict is not null && reader is not null)
        {
            var encodingObj = fontDict.Get("Encoding");
            PdfDictionary? encodingDict = null;
            if (encodingObj is PdfDictionary ed) encodingDict = ed;
            else if (encodingObj is not null) encodingDict = reader.ResolveDict(encodingObj);

            if (encodingDict is not null)
            {
                var diffsArr = encodingDict.Get("Differences") as PdfArray;
                if (diffsArr is null)
                {
                    var resolved = reader.Resolve(encodingDict.Get("Differences"));
                    diffsArr = resolved as PdfArray;
                }
                if (diffsArr is not null)
                {
                    // Build set of byte codes that are remapped by Differences
                    var remappedCodes = new HashSet<int>();
                    var code = 0;
                    for (var i = 0; i < diffsArr.Count; i++)
                    {
                        if (diffsArr[i] is PdfInteger pi)
                            code = (int)pi.Value;
                        else if (diffsArr[i] is PdfName)
                            remappedCodes.Add(code++);
                    }

                    // Check if any replacement character's Latin1 byte is remapped
                    foreach (var ch in text)
                    {
                        var b = (int)(ch <= 0xFF ? ch : 0x3F);
                        if (remappedCodes.Contains(b))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    private static byte[] EncodeString(string text, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict)
    {
        if (toUnicode is not null)
        {
            // Build reverse map with NFKD fallback for Arabic presentation forms
            var reverseMap = BuildReverseMap(toUnicode);

            // If the font stores Arabic presentation forms in its ToUnicode map,
            // the content stream uses visual (reversed) order for RTL text.
            // Reverse RTL replacement text to match this visual-order convention.
            if (HasArabicPresentationForms(toUnicode) && IsArabicText(text))
            {
                var chars = text.ToCharArray();
                Array.Reverse(chars);
                text = new string(chars);
            }

            var isCid = fontDict?.GetName("Subtype") == "Type0";
            var result = new List<byte>();

            foreach (var ch in text)
            {
                var s = ch.ToString();
                if (reverseMap.TryGetValue(s, out var code))
                {
                    if (isCid)
                    {
                        result.Add((byte)((code >> 8) & 0xFF));
                        result.Add((byte)(code & 0xFF));
                    }
                    else
                    {
                        result.Add((byte)(code & 0xFF));
                    }
                }
                else
                {
                    // Fallback: use character value directly
                    if (isCid)
                    {
                        result.Add((byte)((ch >> 8) & 0xFF));
                        result.Add((byte)(ch & 0xFF));
                    }
                    else
                    {
                        result.Add((byte)ch);
                    }
                }
            }

            return result.ToArray();
        }

        return Encoding.Latin1.GetBytes(text);
    }

    /// <summary>
    /// Check if a ToUnicode map contains Arabic presentation form characters,
    /// indicating the font stores RTL text in visual (reversed) order.
    /// </summary>
    private static bool HasArabicPresentationForms(Dictionary<int, string> toUnicode)
    {
        foreach (var unicode in toUnicode.Values)
        {
            if (unicode.Length != 1) continue;
            var ch = unicode[0];
            if ((ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF'))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if text contains Arabic/Hebrew characters that would be rendered RTL.
    /// </summary>
    private static bool IsArabicText(string text)
    {
        foreach (var ch in text)
        {
            // Arabic block (U+0600-U+06FF), Arabic Supplement (U+0750-U+077F),
            // Arabic Extended (U+08A0-U+08FF), Arabic Presentation Forms
            if ((ch >= '\u0600' && ch <= '\u06FF') || (ch >= '\u0750' && ch <= '\u077F') ||
                (ch >= '\u08A0' && ch <= '\u08FF') ||
                (ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF'))
                return true;
        }
        return false;
    }

    private static void WriteStringOperand(MemoryStream ms, byte[] data, bool isHex)
    {
        if (isHex)
        {
            ms.WriteByte((byte)'<');
            ms.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(data)));
            ms.WriteByte((byte)'>');
        }
        else
        {
            ms.WriteByte((byte)'(');
            foreach (var b in data)
            {
                if (b == '(' || b == ')' || b == '\\')
                {
                    ms.WriteByte((byte)'\\');
                    ms.WriteByte(b);
                }
                else if (b == 0x0D) // CR — escape to prevent PdfLexer CR→LF normalization
                {
                    ms.WriteByte((byte)'\\');
                    ms.WriteByte((byte)'r');
                }
                else if (b == 0x0A) // LF
                {
                    ms.WriteByte((byte)'\\');
                    ms.WriteByte((byte)'n');
                }
                else
                {
                    ms.WriteByte(b);
                }
            }
            ms.WriteByte((byte)')');
        }
    }

    private static void WriteTJArray(MemoryStream ms, PdfArray arr)
    {
        ms.WriteByte((byte)'[');
        for (var i = 0; i < arr.Count; i++)
        {
            if (i > 0) ms.WriteByte((byte)' ');
            switch (arr[i])
            {
                case PdfString s:
                    WriteStringOperand(ms, s.Value, s.IsHex);
                    break;
                case PdfInteger n:
                    ms.Write(Encoding.ASCII.GetBytes(n.Value.ToString()));
                    break;
                case PdfReal r:
                    ms.Write(Encoding.ASCII.GetBytes(r.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)));
                    break;
            }
        }
        ms.WriteByte((byte)']');
    }

    private static int CountOccurrences(string text, string search)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(search, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += search.Length;
        }
        return count;
    }

    private static PdfArray ParseContentArrayWithPositions(PdfLexer lexer, out int endPos)
    {
        var array = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof)
            {
                endPos = (int)lexer.Position;
                return array;
            }
            switch (t.Kind)
            {
                case TokenKind.Integer: array.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: array.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: array.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: array.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: array.Add(new PdfName(t.StringValue!)); break;
            }
        }
    }

    private static byte[] CombineStreams(List<byte[]> streams)
    {
        if (streams.Count == 1) return streams[0];
        var total = 0;
        foreach (var s in streams) total += s.Length + 1; // +1 for separator newline
        var result = new byte[total];
        var offset = 0;
        foreach (var s in streams)
        {
            s.CopyTo(result, offset);
            offset += s.Length;
            result[offset++] = (byte)'\n';
        }
        return result;
    }

    private static List<byte[]> GetContentStreams(Page page, PdfReader reader)
    {
        var result = new List<byte[]>();
        var contentsObj = reader.Resolve(page.Dict.Get("Contents"));

        if (contentsObj is PdfStream stream)
        {
            result.Add(reader.DecodeStream(stream));
        }
        else if (contentsObj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                    result.Add(reader.DecodeStream(s));
            }
        }

        return result;
    }

    private static void SkipInlineImage(PdfLexer lexer)
    {
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.Keyword && t.StringValue == "ID") break;
        }

        var pos = lexer.Position + 1;
        var len = lexer.Length;

        while (pos < len - 2)
        {
            var b = lexer.ByteAt(pos);
            if (b is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20 &&
                lexer.ByteAt(pos + 1) == (byte)'E' &&
                lexer.ByteAt(pos + 2) == (byte)'I')
            {
                var after = pos + 3;
                if (after >= len || lexer.ByteAt(after) is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20)
                {
                    lexer.Position = after;
                    return;
                }
            }
            pos++;
        }
        lexer.Position = len;
    }

    /// <summary>
    /// Make ResolveFonts accessible for text replacement.
    /// </summary>
    internal static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
        => TextAbsorber.ResolveFonts(pageDict, reader);
}
