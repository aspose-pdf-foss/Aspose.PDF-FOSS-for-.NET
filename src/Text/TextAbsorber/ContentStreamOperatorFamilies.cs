using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>The state operators the extractor tracks: marked content opened and closed, the q/Q stack.</summary>
    private void ExtractStateOperator(ExtractState xs, string op)
    {
        switch (op)
        {
            case "BMC":
                break;
            case "EMC":
            {
                // Emit ActualText if it wasn't already emitted by text operators
                if (xs.actualText is not null && !xs.actualTextUsed)
                    AppendShowText(xs.actualText);
                xs.actualText = null;
                xs.actualTextUsed = false;
                xs.atSpan = null;
                xs.atOffset = 0;
                break;
            }
            case "q":
                xs.cmStack.Push((xs.localCmTx, xs.localCmTy, xs.localCmD));
                xs.cmFullStack.Push((xs.cmLa, xs.cmLb, xs.cmLc, xs.cmLd, xs.cmLe, xs.cmLf));
                xs.gsStack.Push((xs.fontSet, xs.fontSize, xs.currentFontName, xs.currentFontDict,
                    xs.currentToUnicode, xs.currentMetrics, xs.currentFontNonAgl,
                    xs.charSpacing, xs.wordSpacing, xs.leading, xs.textRenderMode, xs.horizScale));
                break;
            case "Q":
                if (xs.cmStack.Count > 0) (xs.localCmTx, xs.localCmTy, xs.localCmD) = xs.cmStack.Pop();
                if (xs.cmFullStack.Count > 0) (xs.cmLa, xs.cmLb, xs.cmLc, xs.cmLd, xs.cmLe, xs.cmLf) = xs.cmFullStack.Pop();
                if (xs.gsStack.Count > 0)
                    (xs.fontSet, xs.fontSize, xs.currentFontName, xs.currentFontDict, xs.currentToUnicode,
                     xs.currentMetrics, xs.currentFontNonAgl, xs.charSpacing, xs.wordSpacing, xs.leading,
                     xs.textRenderMode, xs.horizScale) = xs.gsStack.Pop();
                break;
        }
    }

    /// <summary>The text-state operators: the text object, leading, scale, spacing and the line moves.</summary>
    private void ExtractTextStateOperator(ExtractState xs, string op)
    {
        switch (op)
        {
            case "Tm":
                ApplyTextMatrixOp(xs);
                break;
            case "BT":
                BeginTextOp(xs);
                break;
            case "TL":
                if (xs.operands.Count >= 1)
                    xs.leading = GetNumber(xs.operands[0]);
                break;
            case "Tz":
                if (xs.operands.Count >= 1)
                    xs.horizScale = GetNumber(xs.operands[0]) / 100.0;
                break;
            case "Tc":
                if (xs.operands.Count >= 1)
                    xs.charSpacing = GetNumber(xs.operands[0]);
                break;
            case "Tw":
                if (xs.operands.Count >= 1)
                    xs.wordSpacing = GetNumber(xs.operands[0]);
                break;
            case "Td" or "TD":
                MoveTextLineOp(xs, op);
                break;
            case "T*":
                NextTextLineOp(xs);
                break;
        }
    }

    /// <summary>The text-showing operators, each run handed to the show-text stages.</summary>
    private void ExtractTextShowOperator(ExtractState xs, string op)
    {
        switch (op)
        {
            case "Tj":
                ShowTextOp(xs, op);
                break;
            case "TJ":
                ShowTextArrayOp(xs, op);
                break;
            case "'":
            case "\"":
                ShowTextSpacedNextLineOp(xs, op);
                break;
        }
    }

    /// <summary>The operators that move the extractor's frame: marked-content properties, a stray move, the CTM and a form XObject walked in its own matrix.</summary>
    private void ExtractMatrixOperator(ExtractState xs, string op)
    {
        switch (op)
        {
            case "BDC" when xs.operands.Count >= 2:
            {
                // Check for ActualText in marked content properties
                if (xs.operands[1] is PdfDictionary props)
                {
                    var at = props.Get("ActualText");
                    if (at is PdfString ats)
                    {
                        var atDecoded = DecodeTextString(ats.Value);
                        xs.actualText = CollapseTwoCharLigature(atDecoded);
                        xs.actualTextUsed = false;
                        xs.actualTextSingleChar = atDecoded.Length == 1;
                        xs.atSpan = atDecoded;
                        xs.atOffset = 0;
                    }
                }
                break;
            }
            // Of all the path operators only `m` validates its operand
            // count: a moveto with 0, 1 or 5 operands throws, while a
            // malformed `l`/`re`/`c` parses leniently (measured on
            // synthetic streams). An `m` the lexer split off a fused
            // lexeme is damaged-stream salvage, not an authored
            // operator — those stay lenient (a corrupt-flate page whose
            // salvage tail is junk must still extract).
            case "m" when xs.operands.Count != 2 && !xs.lexer.LastKeywordFused:
                throw new System.ArgumentException("Invalid parameters count for m operator.");
            case "cm" when xs.operands.Count >= 6:
                xs.localCmTx += GetNumber(xs.operands[4]);
                // Compose the Y transform (axis-aligned): with the CTM in effect
                // y_dev = D·y + T, appending "a b c d e f cm" gives
                // y_dev = (D·d)·y + (D·f + T).
                xs.localCmTy += xs.localCmD * GetNumber(xs.operands[5]);
                xs.localCmD *= GetNumber(xs.operands[3]);
                {
                    // Full composition CTM' = M_new × CTM (row-vector convention).
                    var na = GetNumber(xs.operands[0]); var nb = GetNumber(xs.operands[1]);
                    var nc = GetNumber(xs.operands[2]); var nd = GetNumber(xs.operands[3]);
                    var ne = GetNumber(xs.operands[4]); var nf = GetNumber(xs.operands[5]);
                    var a2 = na * xs.cmLa + nb * xs.cmLc; var b2 = na * xs.cmLb + nb * xs.cmLd;
                    var c2 = nc * xs.cmLa + nd * xs.cmLc; var d2 = nc * xs.cmLb + nd * xs.cmLd;
                    var e2 = ne * xs.cmLa + nf * xs.cmLc + xs.cmLe; var f2 = ne * xs.cmLb + nf * xs.cmLd + xs.cmLf;
                    xs.cmLa = a2; xs.cmLb = b2; xs.cmLc = c2; xs.cmLd = d2; xs.cmLe = e2; xs.cmLf = f2;
                }
                break;
            case "Do" when xs.operands.Count >= 1 && xs.operands[0] is PdfName doName:
            {
                var xobjs = ResolveXObjects(xs.pageDict, xs.reader);
                if (xobjs is not null)
                {
                    var xstr = xs.reader.ResolveStream(xobjs.Get(doName.Value));
                    if (xstr is not null && xs.reader.ResolveName(xstr.Dict, "Subtype") == "Form")
                    {
                        var xbytes = xs.reader.DecodeStream(xstr);
                        // A form XObject inherits the graphics state (incl. font) at the Do.
                        ExtractTextFromContentStream(xbytes, xstr.Dict, xs.reader, xs.depth + 1,
                            xs.pageBounds, xs.localCmTx, xs.localCmTy, xs.fontSet, xs.localCmD,
                            xs.cmLa, xs.cmLb, xs.cmLc, xs.cmLd, xs.cmLe, xs.cmLf);
                    }
                }
                break;
            }
        }
    }

    /// <summary>The render mode and the font selection, with the metrics the font brings.</summary>
    private void ExtractFontOperator(ExtractState xs, string op)
    {
        switch (op)
        {
            case "Tr" when xs.operands.Count >= 1:
                xs.textRenderMode = (int)GetNumber(xs.operands[0]);
                break;
            case "Tf" when xs.operands.Count >= 2:
                xs.fontSize = GetNumber(xs.operands[1]);
                xs.fontSet = true;
                if (xs.operands[0] is PdfName tfFontName)
                {
                    xs.currentFontName = tfFontName.Value;
                    if (xs.fonts.TryGetValue(xs.currentFontName, out var tfFontDict))
                    {
                        xs.currentFontDict = tfFontDict;
                        xs.currentToUnicode = xs.useFontEngine ? null : ParseToUnicode(tfFontDict, xs.reader);
                        xs.currentMetrics = FontMetrics.FromFontDict(tfFontDict, xs.reader);
                        xs.currentFontNonAgl = (TextSearchOptions?.LogTextExtractionErrors ?? false)
                            && DifferencesNotAglCompliant(tfFontDict, xs.reader);
                    }
                    else
                    {
                        xs.currentFontDict = null;
                        xs.currentToUnicode = null;
                        xs.currentMetrics = null;
                        xs.currentFontNonAgl = false;
                    }
                }
                break;
        }
    }

    /// <summary>The extractor's starting state for one content stream: its fonts, lexer, text state, CTM stacks and run trackers.</summary>
    private void OpenExtractState(ExtractState xs)
    {
        xs.fonts = ResolveFonts(xs.pageDict, xs.reader);
        xs.lexer = new PdfLexer(xs.streamBytes);
        xs.operands = new List<PdfObject>();
        xs.currentFontName = null;
        xs.currentToUnicode = null;
        xs.useFontEngine = TextSearchOptions?.UseFontEngineEncoding ?? false;
        xs.currentFontDict = null;
        xs.actualText = null;
        xs.actualTextUsed = false;
        xs.atSpan = null;
        xs.atOffset = 0;
        xs.actualTextSingleChar = false;
        xs.fontSize = 12;
        xs.tmD = 1.0;
        xs.tmA = 1.0;
        xs.leading = 0.0;
        xs.tlmX = 0;
        xs.tmOriginX = 0;
        xs.tx = 0;
        xs.lastRunEndX = double.NaN;
        xs.lastRunEndDevX = double.NaN;
        xs.lastRunEndPageX = double.NaN;
        xs.lastRunStartPageX = double.NaN;
        xs.pendingReorderSpaceY = double.NaN;
        xs.rawInlineScripts = ExtractionOptions?.FormattingMode == TextExtractionOptions.TextFormattingMode.Raw;
        xs.lastDecodedLength = 0;
        xs.lastRunEstWidth = 0;
        xs.lastHadMetrics = false;
        xs.prevTmY = double.NaN;
        xs.currentMetrics = null;
        xs.currentFontNonAgl = false;
        xs.horizScale = 1.0;
        xs.charSpacing = 0;
        xs.wordSpacing = 0;
        xs.tmY = 0;
        xs.tmN = 1.0;
        xs.tmRotated = false;
        xs.tmAr = 1;
        xs.tmBr = 0;
        xs.tmCr = 0;
        xs.tmDr = 1;
        xs.tmE = 0;
        xs.tmF = 0;
        xs.textRenderMode = 0;
        xs.lastRenderedY = double.NaN;
        xs.lastRenderedCmTy = double.NaN;
        xs.lastRenderedFs = 0;
        xs.dedupPrevText = string.Empty;
        xs.dedupPrevOffset = -1;
        xs.dedupPrevLlx = 0;
        xs.dedupPrevLly = 0;
        xs.dedupPrevUrx = -1;
        xs.dedupPrevUry = -1;
        xs.pageBoundsActive = TextSearchOptions?.LimitToPageBounds == true;
        xs.pageBounds = xs.inheritedBounds ?? (xs.pageBoundsActive ? GetPageMediaBox(xs.pageDict, xs.reader) : null);
        xs.skipText = false;
        xs.openLineSkip = false;
        xs.searchRect = _effectiveSearchRect ?? TextSearchOptions?.Rectangle;
        xs.clipRect = xs.searchRect;
        xs.blankClip = false;
        if (xs.pageBounds is not null)
        {
            var pb = new Rectangle(xs.pageBounds[0] - 1, xs.pageBounds[1] - 1, xs.pageBounds[2] + 1, xs.pageBounds[3] + 1);
            if (xs.clipRect is null) { xs.clipRect = pb; xs.blankClip = true; }
            else
                xs.clipRect = new Rectangle(Math.Max(xs.clipRect.LLX, pb.LLX), Math.Max(xs.clipRect.LLY, pb.LLY),
                    Math.Min(xs.clipRect.URX, pb.URX), Math.Min(xs.clipRect.URY, pb.URY));
        }
        xs.localCmTx = xs.cmTx;
        xs.localCmTy = xs.cmTy;
        xs.localCmD = xs.cmD;
        xs.cmStack = new Stack<(double tx, double ty, double d)>();
        xs.cmLa = xs.cmLinA;
        xs.cmLb = xs.cmLinB;
        xs.cmLc = xs.cmLinC;
        xs.cmLd = xs.cmLinD;
        xs.cmLe = xs.cmLinE;
        xs.cmLf = xs.cmLinF;
        xs.cmFullStack = new Stack<(double a, double b, double c, double d, double e, double f)>();
        xs.fontSet = xs.fontSetOnEntry;
        xs.gsStack = new Stack<(bool fontSet, double fontSize, string? fontName,
            PdfDictionary? fontDict, Dictionary<int, string>? toUnicode, FontMetrics? metrics,
            bool nonAgl, double charSpacing, double wordSpacing, double leading,
            int renderMode, double horizScale)>();
    }

    /// <summary>The stream, its owner, the recursion depth and the inherited matrix the extractor was called with.</summary>
    private static void CaptureExtractInputs(ExtractState xs, byte[] streamBytes, PdfDictionary pageDict, PdfReader reader, int depth, double[]? inheritedBounds, double cmTx, double cmTy, bool fontSetOnEntry, double cmD, double cmLinA, double cmLinB, double cmLinC, double cmLinD, double cmLinE, double cmLinF)
    {
        xs.streamBytes = streamBytes;
        xs.pageDict = pageDict;
        xs.reader = reader;
        xs.depth = depth;
        xs.inheritedBounds = inheritedBounds;
        xs.cmTx = cmTx;
        xs.cmTy = cmTy;
        xs.fontSetOnEntry = fontSetOnEntry;
        xs.cmD = cmD;
        xs.cmLinA = cmLinA;
        xs.cmLinB = cmLinB;
        xs.cmLinC = cmLinC;
        xs.cmLinD = cmLinD;
        xs.cmLinE = cmLinE;
        xs.cmLinF = cmLinF;
    }

    /// <summary>The font selection and the text-showing operators of the actual-text pass.</summary>
    private void ProcessFontAndShowOperator(ExtractState xs, bool useFontEngine, List<PdfObject> operands, Dictionary<string, PdfDictionary> fonts, PdfReader reader, string? actualText, double fontSize, string op)
    {
        switch (op)
        {
            case "Tf": // Set font
                if (operands.Count >= 1 && operands[0] is PdfName fontName)
                {
                    xs.currentFontName = fontName.Value;
                    if (fonts.TryGetValue(xs.currentFontName, out var fontDict))
                    {
                        xs.currentFontDict = fontDict;
                        xs.currentToUnicode = useFontEngine ? null : ParseToUnicode(fontDict, reader);
                    }
                    else
                    {
                        xs.currentFontDict = null;
                        xs.currentToUnicode = null;
                    }
                }
                break;

            case "Tj": // Show string
                if (operands.Count >= 1 && operands[0] is PdfString str)
                {
                    if (actualText is not null)
                    {
                        if (!xs.actualTextUsed)
                        {
                            AppendShowText(actualText);
                            xs.actualTextUsed = true;
                        }
                    }
                    else
                    {
                        AppendShowText(NormalizeDecoded(DecodeString(str.Value, xs.currentToUnicode, xs.currentFontDict, reader, useFontEngine)));
                    }
                }
                break;

            case "TJ": // Show string array (with positioning)
                if (operands.Count >= 1 && operands[0] is PdfArray arr)
                {
                    if (actualText is not null)
                    {
                        if (!xs.actualTextUsed)
                        {
                            AppendShowText(actualText);
                            xs.actualTextUsed = true;
                        }
                    }
                    else
                    {
                        // Use font-size-relative threshold: 25% of font size in thousandths
                        var spaceThreshold = -(fontSize * 250 / fontSize); // -250 units (normalized)
                        // Simplified: -250 works well for most fonts at any size
                        foreach (var item in arr)
                        {
                            if (item is PdfString s)
                            {
                                AppendShowText(NormalizeDecoded(DecodeString(s.Value, xs.currentToUnicode, xs.currentFontDict, reader, useFontEngine)));
                            }
                            else if (item is PdfInteger adj && adj.Value < -200)
                            {
                                if (_text.Length == 0 || _text[^1] != ' ')
                                    _text.Append(' ');
                            }
                            else if (item is PdfReal adjR && adjR.Value < -200)
                            {
                                if (_text.Length == 0 || _text[^1] != ' ')
                                    _text.Append(' ');
                            }
                        }
                    }
                }
                break;

            case "'": // Move to next line and show string
                // Record the finished line's Y before breaking — an unrecorded break
                // desynchronizes the line↔Y pairing SortLinesByY relies on.
                RecordLineY();
                AppendStreamBreak();
                if (operands.Count >= 1 && operands[0] is PdfString str2)
                {
                    if (actualText is not null && !xs.actualTextUsed)
                    {
                        AppendShowText(actualText);
                        xs.actualTextUsed = true;
                    }
                    else if (actualText is null)
                    {
                        AppendShowText(NormalizeDecoded(DecodeString(str2.Value, xs.currentToUnicode, xs.currentFontDict, reader, useFontEngine)));
                    }
                }
                break;

        }
    }
}
