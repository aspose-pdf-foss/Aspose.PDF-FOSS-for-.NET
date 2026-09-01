using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>
    /// Processes one content stream, appending extracted text. Returns whether a font is
    /// set in the graphics state at the end of the stream, so a page's multiple content
    /// streams (which share one graphics state) can thread the "font set" flag between them.
    /// </summary>
    private bool ExtractTextFromContentStream(byte[] streamBytes, PdfDictionary pageDict, PdfReader reader,
        int depth = 0, double[]? inheritedBounds = null, double cmTx = 0, double cmTy = 0,
        bool fontSetOnEntry = false, double cmD = 1,
        double cmLinA = 1, double cmLinB = 0, double cmLinC = 0, double cmLinD = 1,
        double cmLinE = 0, double cmLinF = 0)
    {
        if (depth > 10) return fontSetOnEntry; // prevent infinite recursion
        var xs = new ExtractState();
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

        while (true)
        {
            var token = xs.lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    xs.operands.Add(new PdfInteger(token.IntValue));
                    break;
                case TokenKind.Real:
                    xs.operands.Add(new PdfReal(token.RealValue));
                    break;
                case TokenKind.LiteralString:
                    xs.operands.Add(new PdfString(token.BytesValue!));
                    break;
                case TokenKind.HexString:
                    xs.operands.Add(new PdfString(token.BytesValue!, isHex: true));
                    break;
                case TokenKind.Name:
                    xs.operands.Add(new PdfName(token.StringValue!));
                    break;
                case TokenKind.Boolean:
                    xs.operands.Add(token.BoolValue ? PdfBoolean.True : PdfBoolean.False);
                    break;
                case TokenKind.ArrayStart:
                {
                    var array = ParseContentArray(xs.lexer);
                    xs.operands.Add(array);
                    break;
                }
                case TokenKind.DictStart:
                {
                    var dict = ParseContentDict(xs.lexer);
                    xs.operands.Add(dict);
                    break;
                }
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "BI": // Begin inline image — skip until EI
                            SkipInlineImage(xs.lexer);
                            xs.operands.Clear();
                            continue;
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
                        case "BMC":
                            break;
                        // Of all the path operators only `m` validates its operand
                        // count: a moveto with 0, 1 or 5 operands throws, while a
                        // malformed `l`/`re`/`c` parses leniently (measured on
                        // synthetic streams). An `m` the lexer split off a fused
                        // lexeme is damaged-stream salvage, not an authored
                        // operator — those stay lenient (a corrupt-flate page whose
                        // salvage tail is junk must still extract).
                        case "m" when xs.operands.Count != 2 && !xs.lexer.LastKeywordFused:
                            throw new System.ArgumentException("Invalid parameters count for m operator.");
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
                        default:
                            ProcessOperator(op, xs.operands, xs.fonts, xs.reader, xs.pageDict,
                                ref xs.currentFontName, ref xs.currentToUnicode, ref xs.currentFontDict,
                                xs.actualText, ref xs.actualTextUsed, xs.fontSize, xs.depth, xs.actualTextSingleChar);
                            break;
                    }
                    xs.operands.Clear();
                    break;
                }
                default:
                    xs.operands.Clear();
                    break;
            }
        }
        return xs.fontSet;
    }

    /// <summary>
    /// Strict font-usage guard for a text-showing operator: if no font is set in the
    /// current graphics state the content stream is malformed (no preceding Tf), so throw
    /// <see cref="IncorrectFontUsageException"/> — unless the caller opted into tolerant
    /// extraction via <see cref="Text.TextSearchOptions.IgnoreResourceFontErrors"/>.
    /// </summary>
    private int _currentPageNumber;

    private void ProcessOperator(string op, List<PdfObject> operands,
        Dictionary<string, PdfDictionary> fonts, PdfReader reader, PdfDictionary pageDict,
        ref string? currentFontName, ref Dictionary<int, string>? currentToUnicode,
        ref PdfDictionary? currentFontDict,
        string? actualText, ref bool actualTextUsed, double fontSize, int depth,
        bool actualTextSingleChar = false)
    {
        // UseFontEngineEncoding: decode via the font program's encoding/cmap instead of
        // /ToUnicode (mirrors the local of the same name in the main extraction loop).
        bool useFontEngine = TextSearchOptions?.UseFontEngineEncoding ?? false;
        // Styled single glyph: a one-char /ActualText over a one-glyph show that
        // decodes to the SAME letter differing only in case falls back to the
        // font's own decode (see the main loop's ActualTextYieldsToDecode note).
        if (actualText is not null && !actualTextUsed && actualTextSingleChar
            && (op == "Tj" || op == "TJ") && operands.Count >= 1)
        {
            var d = string.Empty;
            if (operands[0] is PdfString sp)
                d = NormalizeDecoded(DecodeString(sp.Value, currentToUnicode, currentFontDict, reader, useFontEngine));
            else if (operands[0] is PdfArray ap)
                foreach (var it in ap)
                {
                    if (it is not PdfString s2) continue;
                    d += NormalizeDecoded(DecodeString(s2.Value, currentToUnicode, currentFontDict, reader, useFontEngine));
                    if (d.Length > 1) break;
                }
            if (d.Length == 1 && d[0] != actualText[0]
                && char.ToUpperInvariant(d[0]) == char.ToUpperInvariant(actualText[0]))
                actualText = null;
        }
        switch (op)
        {
            case "Tf": // Set font
                if (operands.Count >= 1 && operands[0] is PdfName fontName)
                {
                    currentFontName = fontName.Value;
                    if (fonts.TryGetValue(currentFontName, out var fontDict))
                    {
                        currentFontDict = fontDict;
                        currentToUnicode = useFontEngine ? null : ParseToUnicode(fontDict, reader);
                    }
                    else
                    {
                        currentFontDict = null;
                        currentToUnicode = null;
                    }
                }
                break;

            case "Tj": // Show string
                if (operands.Count >= 1 && operands[0] is PdfString str)
                {
                    if (actualText is not null)
                    {
                        if (!actualTextUsed)
                        {
                            AppendShowText(actualText);
                            actualTextUsed = true;
                        }
                    }
                    else
                    {
                        AppendShowText(NormalizeDecoded(DecodeString(str.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
                    }
                }
                break;

            case "TJ": // Show string array (with positioning)
                if (operands.Count >= 1 && operands[0] is PdfArray arr)
                {
                    if (actualText is not null)
                    {
                        if (!actualTextUsed)
                        {
                            AppendShowText(actualText);
                            actualTextUsed = true;
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
                                AppendShowText(NormalizeDecoded(DecodeString(s.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
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
                    if (actualText is not null && !actualTextUsed)
                    {
                        AppendShowText(actualText);
                        actualTextUsed = true;
                    }
                    else if (actualText is null)
                    {
                        AppendShowText(NormalizeDecoded(DecodeString(str2.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
                    }
                }
                break;

            case "\"": // Set spacing, move to next line, show string
                RecordLineY(); // see the ' note — keep the line↔Y pairing aligned
                AppendStreamBreak();
                if (operands.Count >= 3 && operands[2] is PdfString str3)
                {
                    if (actualText is not null && !actualTextUsed)
                    {
                        AppendShowText(actualText);
                        actualTextUsed = true;
                    }
                    else if (actualText is null)
                    {
                        AppendShowText(NormalizeDecoded(DecodeString(str3.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
                    }
                }
                break;

            // Td, TD, Tm, T* are handled before ProcessOperator in the caller switch;
            // they should not reach here. Fall through without action if they do.
            case "Td" or "TD":
            case "Tm":
            case "T*":
                break;

            // cm, q, Q, Do are handled in the outer keyword switch (with CTM context)
            case "cm":
            case "q":
            case "Q":
                break;

            // Do is handled in the outer keyword switch (with CTM context)
            case "Do":
                break;
        }
    }

    // Cache of glyph-id → Unicode maps built per Type0 font dictionary so a page's
    // repeated decode calls parse the font program once.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, GidToUnicodeEntry> _gidToUnicodeCache = new();

    // Per-font-dict cache of CidFontInfo for the legacy-CMap decode branch (an entry with
    // LegacyCodepage == 0 means "not a legacy national CMap" and is cached as null).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, CidFontInfo?> _legacyCidCache = new();

    // Cache of byte-code → Unicode maps recovered from a simple font's embedded program
    // post-table glyph names (built once per font dict). Null Map = font has no usable
    // post names / no embedded program.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, PostNameMapEntry> _postNameCache = new();

    // A /ToUnicode CMap is decoded and parsed ONCE per stream object: extraction
    // meets the same font at every Tf (hundreds of times on a dense page), and
    // re-parsing it each time dominated whole-document absorb time. Keyed by the
    // resolved STREAM instance — an edit that swaps in a new /ToUnicode gets a new
    // key, so the cache can never serve a stale map for changed bytes.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfStream, Dictionary<int, string>>
        _toUnicodeCache = new();

    // Per-page (offset, x) of each output line's first tracked run — the input to
    // the leading-column padding pass. Reset in Visit().
    private readonly List<(int offset, double x)> _pageLineStarts = new();

    private readonly List<RunSpan> _pageRunSpans = new();

    // Page grid origin (leftmost text X) from the pre-scan; NaN when unknown.
    private double _pageMinX = double.NaN;

    // TextSearchOptions.Rectangle mapped from viewer to media coordinates for the
    // page being visited (equal to the raw rectangle on an unrotated page).
    private Rectangle? _effectiveSearchRect;

    // Grid ladder cache: one page uses one cell width, so keeping the last
    // ladder covers every call. Per-thread because absorbers run in parallel.
    [ThreadStatic] private static double[]? _gridStops;
    [ThreadStatic] private static double _gridStopsOrigin;
    [ThreadStatic] private static double _gridStopsCell;
    [ThreadStatic] private static int _gridStopsCount;

    private static PdfReader GetReader(Page page) => page.Reader;

}
