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
        CaptureExtractInputs(xs, streamBytes, pageDict, reader, depth, inheritedBounds, cmTx, cmTy, fontSetOnEntry, cmD, cmLinA, cmLinB, cmLinC, cmLinD, cmLinE, cmLinF);
        OpenExtractState(xs);

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
                        case "BDC": case "m": case "cm": case "Do":
                            ExtractMatrixOperator(xs, op);
                            break;
                        case "BMC": case "EMC": case "q": case "Q":
                            ExtractStateOperator(xs, op);
                            break;
                        case "Tr": case "Tf":
                            ExtractFontOperator(xs, op);
                            break;
                        case "Tm": case "BT": case "TL": case "Tz": case "Tc": case "Tw": case "Td": case "TD": case "T*":
                            ExtractTextStateOperator(xs, op);
                            break;
                        case "Tj": case "TJ": case "'": case "\"":
                            ExtractTextShowOperator(xs, op);
                            break;
                        default:
                            ProcessOperator(xs, op, xs.operands, xs.fonts, xs.reader, xs.pageDict,
                                xs.actualText, xs.fontSize, xs.depth, xs.actualTextSingleChar);
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

    private void ProcessOperator(ExtractState xs, string op, List<PdfObject> operands,
        Dictionary<string, PdfDictionary> fonts, PdfReader reader, PdfDictionary pageDict,
        string? actualText, double fontSize, int depth,
        bool actualTextSingleChar = false)
    {
        // UseFontEngineEncoding: decode via the font program's encoding/cmap instead of
        // /ToUnicode (mirrors the local of the same name in the main extraction loop).
        bool useFontEngine = TextSearchOptions?.UseFontEngineEncoding ?? false;
        // Styled single glyph: a one-char /ActualText over a one-glyph show that
        // decodes to the SAME letter differing only in case falls back to the
        // font's own decode (see the main loop's ActualTextYieldsToDecode note).
        if (actualText is not null && !xs.actualTextUsed && actualTextSingleChar
            && (op == "Tj" || op == "TJ") && operands.Count >= 1)
        {
            var d = string.Empty;
            if (operands[0] is PdfString sp)
                d = NormalizeDecoded(DecodeString(sp.Value, xs.currentToUnicode, xs.currentFontDict, reader, useFontEngine));
            else if (operands[0] is PdfArray ap)
                foreach (var it in ap)
                {
                    if (it is not PdfString s2) continue;
                    d += NormalizeDecoded(DecodeString(s2.Value, xs.currentToUnicode, xs.currentFontDict, reader, useFontEngine));
                    if (d.Length > 1) break;
                }
            if (d.Length == 1 && d[0] != actualText[0]
                && char.ToUpperInvariant(d[0]) == char.ToUpperInvariant(actualText[0]))
                actualText = null;
        }
        switch (op)
        {
            case "Tf": case "Tj": case "TJ": case "'":
                ProcessFontAndShowOperator(xs, useFontEngine, operands, fonts, reader, actualText, fontSize, op);
                break;
            case "\"": // Set spacing, move to next line, show string
                RecordLineY(); // see the ' note — keep the line↔Y pairing aligned
                AppendStreamBreak();
                if (operands.Count >= 3 && operands[2] is PdfString str3)
                {
                    if (actualText is not null && !xs.actualTextUsed)
                    {
                        AppendShowText(actualText);
                        xs.actualTextUsed = true;
                    }
                    else if (actualText is null)
                    {
                        AppendShowText(NormalizeDecoded(DecodeString(str3.Value, xs.currentToUnicode, xs.currentFontDict, reader, useFontEngine)));
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
