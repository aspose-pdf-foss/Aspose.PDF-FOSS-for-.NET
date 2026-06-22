using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Extracts text from PDF pages by parsing content streams.
/// </summary>
public sealed class TextAbsorber
{
    private readonly StringBuilder _text = new();
    // Track Y positions for each text line to enable visual-order sorting.
    // Each entry corresponds to a line boundary in _text.
    private readonly List<double> _lineYPositions = new();
    private double _currentLineY = double.NaN;
    // Counts text-showing operators (Tj/TJ/'/") seen while extracting the current
    // page (including nested Form XObjects). Used to emit the "no text operators"
    // diagnostic when TextSearchOptions.LogTextExtractionErrors is enabled.
    private int _textShowingOpCount;

    /// <summary>
    /// The extracted text after calling Visit(). Full Trim() is applied to
    /// strip both trailing newline sentinels emitted by the extraction loop
    /// and any spurious trailing spaces from gap-detection between BT/ET blocks.
    /// </summary>
    public string Text => _text.ToString().Trim();

    /// <summary>
    /// The extracted text with only trailing \r\n stripped (preserving trailing
    /// spaces from the source glyph stream). Used by LowCode extractors that
    /// need exact byte parity with the content stream.
    /// </summary>
    internal string RawText => _text.ToString().TrimEnd('\r', '\n');

    /// <summary>
    /// Gets or sets the text extraction options.
    /// </summary>
    public TextExtractionOptions ExtractionOptions { get; set; } = new TextExtractionOptions();

    /// <summary>
    /// Gets or sets the text search options used during extraction.
    /// </summary>
    public TextSearchOptions TextSearchOptions { get; set; } = new TextSearchOptions();

    /// <summary>
    /// Initializes a new TextAbsorber with default settings.
    /// </summary>
    public TextAbsorber() { }

    /// <summary>
    /// Initializes a new TextAbsorber with the specified extraction options.
    /// </summary>
    public TextAbsorber(TextExtractionOptions extractionOptions)
    {
        ExtractionOptions = extractionOptions ?? new TextExtractionOptions();
    }

    /// <summary>Initializes with text-search options.</summary>
    public TextAbsorber(TextSearchOptions textSearchOptions)
    {
        TextSearchOptions = textSearchOptions ?? new TextSearchOptions();
    }

    /// <summary>Initializes with both extraction and search options.</summary>
    public TextAbsorber(TextExtractionOptions extractionOptions, TextSearchOptions textSearchOptions)
    {
        ExtractionOptions = extractionOptions ?? new TextExtractionOptions();
        TextSearchOptions = textSearchOptions ?? new TextSearchOptions();
    }

    /// <summary>Errors recorded during extraction.</summary>
    public List<TextExtractionError> Errors { get; } = new();

    /// <summary>Whether any extraction error was recorded.</summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>
    /// Extract text from a single page.
    /// </summary>
    public void Visit(Page page)
    {
        var reader = GetReader(page);
        var contentStreams = GetContentStreams(page, reader);

        // Track starting positions for this page
        var textStart = _text.Length;
        var yStart = _lineYPositions.Count;
        _currentLineY = double.NaN;
        _textShowingOpCount = 0;

        foreach (var streamBytes in contentStreams)
            ExtractTextFromContentStream(streamBytes, page.Dict, reader);

        // Sort this page's text lines by visual order (Y coordinate, top to bottom)
        SortLinesByY(textStart, yStart);

        // Diagnostic: a page that draws only images/graphics has no text-showing
        // operators. When the caller opted into error logging, surface this as a
        // recorded extraction error (matches Aspose.PDF for .NET behaviour).
        if ((TextSearchOptions?.LogTextExtractionErrors ?? false) && _textShowingOpCount == 0)
        {
            const string msg = "Text showing operators aren't found on the page.";
            Errors.Add(new TextExtractionError
            {
                PageIndex = page.Number,
                Message = msg,
                Description = msg,
                Summary = msg,
                Location = new TextExtractionErrorLocation { PageNumber = page.Number },
            });
        }
    }

    /// <summary>
    /// Record the Y position of the current line (before emitting a newline).
    /// </summary>
    private void RecordLineY()
    {
        if (!double.IsNaN(_currentLineY))
            _lineYPositions.Add(_currentLineY);
    }

    /// <summary>
    /// Sort recently extracted text lines by Y coordinate (top-to-bottom visual order).
    /// Only sorts text added after startOffset.
    /// </summary>
    private void SortLinesByY(int textStartOffset, int yStartIndex)
    {
        // Record the last line's Y position
        RecordLineY();

        var yCount = _lineYPositions.Count - yStartIndex;
        if (yCount < 2) return;

        // Extract only the page's text
        var pageText = _text.ToString(textStartOffset, _text.Length - textStartOffset);
        var lines = pageText.Split('\n');

        // Build Y positions for this page's lines
        var pageYs = new List<double>();
        for (int i = yStartIndex; i < _lineYPositions.Count && pageYs.Count < lines.Length; i++)
            pageYs.Add(_lineYPositions[i]);
        while (pageYs.Count < lines.Length)
            pageYs.Add(double.NaN);

        // Check if lines are already in visual order (Y descending = top to bottom).
        bool needsSort = false;
        for (int i = 1; i < pageYs.Count; i++)
        {
            if (!double.IsNaN(pageYs[i]) && !double.IsNaN(pageYs[i - 1]) &&
                pageYs[i] > pageYs[i - 1] + 200.0) // Y jumped UP by >~3 inches — major out-of-order block
            {
                needsSort = true;
                break;
            }
        }

        // Even if sort isn't needed, check if same-Y lines need merging
        bool hasSameYLines = false;
        if (!needsSort)
        {
            for (int i = 1; i < pageYs.Count; i++)
            {
                if (!double.IsNaN(pageYs[i]) && !double.IsNaN(pageYs[i - 1]) &&
                    Math.Abs(pageYs[i] - pageYs[i - 1]) < Math.Max(2.0, Math.Abs(pageYs[i]) * 0.01))
                {
                    hasSameYLines = true;
                    break;
                }
            }
            if (!hasSameYLines) return;
        }

        // Create (y, index, line) tuples and sort by Y descending (top of page first)
        var indexed = new List<(double y, int idx, string line)>();
        for (int i = 0; i < lines.Length; i++)
            indexed.Add((pageYs[i], i, lines[i]));

        // Stable sort by Y descending; lines with NaN Y keep their relative order
        indexed.Sort((a, b) =>
        {
            if (double.IsNaN(a.y) && double.IsNaN(b.y)) return a.idx.CompareTo(b.idx);
            if (double.IsNaN(a.y)) return 1; // NaN goes last
            if (double.IsNaN(b.y)) return -1;
            var cmp = b.y.CompareTo(a.y); // descending Y = top first
            return cmp != 0 ? cmp : a.idx.CompareTo(b.idx); // preserve order for same Y
        });

        // Replace the page portion of _text with sorted text.
        // Merge lines with the same Y position (within tolerance) using spaces.
        _text.Remove(textStartOffset, _text.Length - textStartOffset);
        for (int i = 0; i < indexed.Count; i++)
        {
            if (i > 0)
            {
                var prevY = indexed[i - 1].y;
                var curY = indexed[i].y;
                // If both have valid Y and are on the same visual line, use space separator
                if (!double.IsNaN(prevY) && !double.IsNaN(curY) &&
                    Math.Abs(prevY - curY) < Math.Max(2.0, Math.Abs(prevY) * 0.01))
                {
                    _text.Append("      "); // column separator
                }
                else
                {
                    _text.Append("\r\n");
                }
            }
            _text.Append(indexed[i].line);
        }
    }

    /// <summary>
    /// Apply RTL reversal to a decoded string from a single Tj/TJ operator.
    /// If the string consists entirely of RTL characters and neutral punctuation/whitespace,
    /// returns the string reversed so that visual-order Hebrew/Arabic becomes logical order.
    /// Otherwise returns the string unchanged.
    /// </summary>
    private static string ApplyRtlIfPureRtl(string text)
    {
        if (text.Length == 0) return text;
        bool hasRtl = false;
        foreach (char c in text)
        {
            if (BidiReorderer.IsRtlChar(c))
                hasRtl = true;
            else if (IsRtlNeutral(c))
                { /* neutral — allowed in RTL runs */ }
            else
                return text; // LTR character found — leave unchanged
        }
        if (!hasRtl) return text;
        return new string(text.ToCharArray().Reverse().ToArray());
    }

    private static bool IsRtlNeutral(char c) =>
        c == ' ' || c == '\t' || c == '\n' || c == '\r'
        || (c >= '!' && c <= '/')   // !"#$%&'()*+,-./
        || (c >= ':' && c <= '@')   // :;<=>?@
        || (c >= '[' && c <= '`')   // [\]^_`
        || (c >= '{' && c <= '~');  // {|}~

    /// <summary>
    /// Extract text from all pages of a document.
    /// </summary>
    /// <summary>
    /// Extract text from a Form XObject.
    /// </summary>
    public void Visit(XForm form)
    {
        if (form is null) throw new ArgumentNullException(nameof(form));
        var streamBytes = form.DecodedBytes;
        if (streamBytes.Length == 0) return;

        // XForm has its own dict (with Resources) — use a reader from
        // the page that owns this XForm for object resolution.
        var reader = form.Reader;
        var dict = form.StreamDict;

        var textStart = _text.Length;
        var yStart = _lineYPositions.Count;
        _currentLineY = double.NaN;

        ExtractTextFromContentStream(streamBytes, dict, reader);
        SortLinesByY(textStart, yStart);
    }

    public void Visit(Document pdf)
    {
        var pageTexts = new List<string>();
        var isPure = ExtractionOptions?.FormattingMode
            != TextExtractionOptions.TextFormattingMode.Raw;
        foreach (var page in pdf.Pages)
        {
            _text.Clear();
            _lineYPositions.Clear();
            Visit(page);
            var pageText = _text.ToString().Trim('\r', '\n');
            if (pageText.Length > 0)
            {
                // Pure mode: pad each line to a consistent width so column
                // layout is preserved visually. The Aspose.PDF for .NET Pure mode
                // does this to maintain fixed-width column alignment.
                if (isPure)
                    pageText = PadLinesToFixedWidth(pageText);
                pageTexts.Add(pageText);
            }
        }
        _text.Clear();
        _text.Append(string.Join("\r\n", pageTexts));
        if (pageTexts.Count > 0)
            _text.Append("\r\n");
    }

    /// <summary>
    /// Pad each line with trailing spaces to a fixed width (~80 chars).
    /// This matches Aspose.PDF for .NET Pure mode behavior where column layouts produce
    /// fixed-width lines for consistent visual alignment. Lines longer than
    /// the target width are left unchanged. Only pads when the page has
    /// multiple lines (single-line pages are left as-is to avoid inflating
    /// short text extractions).
    /// </summary>
    private static string PadLinesToFixedWidth(string text)
    {
        const int targetWidth = 80;
        var lines = text.Split('\n');
        // Only pad pages with multiple lines — single-line pages are short
        // text fragments that shouldn't be padded to 80 chars.
        if (lines.Length < 3) return text;

        var sb = new StringBuilder(text.Length + lines.Length * 5);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            sb.Append(line);
            var padding = targetWidth - line.Length;
            if (padding > 0)
                sb.Append(' ', padding);
            if (i < lines.Length - 1)
                sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Clears the extracted text and resets the absorber state so it can be reused.
    /// </summary>
    public void Reset()
    {
        _text.Clear();
        _lineYPositions.Clear();
        _currentLineY = double.NaN;
    }

    /// <summary>Check if a text position is within the page's MediaBox/CropBox.</summary>
    private bool IsWithinPageBounds(double x, double y, PdfDictionary pageDict, PdfReader reader)
    {
        if (TextSearchOptions?.LimitToPageBounds != true) return true;
        var mb = GetPageMediaBox(pageDict, reader);
        if (mb is null) return true;
        return x >= mb[0] - 1 && x <= mb[2] + 1 && y >= mb[1] - 1 && y <= mb[3] + 1;
    }

    private static double[]? GetPageMediaBox(PdfDictionary pageDict, PdfReader reader)
    {
        // Try CropBox first, then MediaBox
        var box = reader.Resolve(pageDict.Get("CropBox")) as PdfArray
               ?? reader.Resolve(pageDict.Get("MediaBox")) as PdfArray;
        if (box is null || box.Count < 4) return null;
        static double getNum(PdfObject? obj) => obj switch
        {
            Core.PdfInteger i => i.Value,
            Core.PdfReal r => r.Value,
            _ => 0
        };
        return [getNum(box[0]), getNum(box[1]), getNum(box[2]), getNum(box[3])];
    }

    private void ExtractTextFromContentStream(byte[] streamBytes, PdfDictionary pageDict, PdfReader reader,
        int depth = 0, double[]? inheritedBounds = null, double cmTx = 0, double cmTy = 0)
    {
        if (depth > 10) return; // prevent infinite recursion
        var fonts = ResolveFonts(pageDict, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        string? currentFontName = null;
        Dictionary<int, string>? currentToUnicode = null;
        // UseFontEngineEncoding: decode via the font program's encoding/cmap instead of /ToUnicode.
        bool useFontEngine = TextSearchOptions?.UseFontEngineEncoding ?? false;
        PdfDictionary? currentFontDict = null;
        string? actualText = null;
        var actualTextUsed = false;
        double fontSize = 12;
        double tmD = 1.0;
        double leading = 0.0;
        double tlmX = 0;
        double tx = 0;
        double lastRunEndX = double.NaN;
        int lastDecodedLength = 0;
        double lastRunEstWidth = 0;
        bool lastHadMetrics = false;
        double prevTmY = double.NaN;
        FontMetrics? currentMetrics = null;
        double horizScale = 1.0;
        double tmY = 0;
        // Track the Y at which the most recent Tj/TJ/'/" actually rendered so we can
        // distinguish "new logical line" (large Y delta) from "same row, repositioned
        // by Tm for a different column" (small Y delta). Used to suppress false
        // line-breaks from ' and " after an absolute-position Tm.
        double lastRenderedY = double.NaN;
        bool pageBoundsActive = TextSearchOptions?.LimitToPageBounds == true;
        // Use inherited page bounds for Form XObjects (they don't have their own MediaBox)
        double[]? pageBounds = inheritedBounds ?? (pageBoundsActive ? GetPageMediaBox(pageDict, reader) : null);
        bool skipText = false;
        var searchRect = TextSearchOptions?.Rectangle;
        // CTM tracking for cm operator — accumulates with inherited CTM from parent
        double localCmTx = cmTx, localCmTy = cmTy;
        var cmStack = new Stack<(double tx, double ty)>();

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add(new PdfInteger(token.IntValue));
                    break;
                case TokenKind.Real:
                    operands.Add(new PdfReal(token.RealValue));
                    break;
                case TokenKind.LiteralString:
                    operands.Add(new PdfString(token.BytesValue!));
                    break;
                case TokenKind.HexString:
                    operands.Add(new PdfString(token.BytesValue!, isHex: true));
                    break;
                case TokenKind.Name:
                    operands.Add(new PdfName(token.StringValue!));
                    break;
                case TokenKind.Boolean:
                    operands.Add(token.BoolValue ? PdfBoolean.True : PdfBoolean.False);
                    break;
                case TokenKind.ArrayStart:
                {
                    var array = ParseContentArray(lexer);
                    operands.Add(array);
                    break;
                }
                case TokenKind.DictStart:
                {
                    var dict = ParseContentDict(lexer);
                    operands.Add(dict);
                    break;
                }
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "BI": // Begin inline image — skip until EI
                            SkipInlineImage(lexer);
                            operands.Clear();
                            continue;
                        case "BDC" when operands.Count >= 2:
                        {
                            // Check for ActualText in marked content properties
                            if (operands[1] is PdfDictionary props)
                            {
                                var at = props.Get("ActualText");
                                if (at is PdfString ats)
                                {
                                    actualText = DecodeTextString(ats.Value);
                                    actualTextUsed = false;
                                }
                            }
                            break;
                        }
                        case "BMC":
                            break;
                        case "EMC":
                        {
                            // Emit ActualText if it wasn't already emitted by text operators
                            if (actualText is not null && !actualTextUsed)
                                _text.Append(actualText);
                            actualText = null;
                            actualTextUsed = false;
                            break;
                        }
                        case "cm" when operands.Count >= 6:
                            localCmTx += GetNumber(operands[4]);
                            localCmTy += GetNumber(operands[5]);
                            break;
                        case "q":
                            cmStack.Push((localCmTx, localCmTy));
                            break;
                        case "Q":
                            if (cmStack.Count > 0) (localCmTx, localCmTy) = cmStack.Pop();
                            break;
                        case "Do" when operands.Count >= 1 && operands[0] is PdfName doName:
                        {
                            var xobjs = ResolveXObjects(pageDict, reader);
                            if (xobjs is not null)
                            {
                                var xstr = reader.ResolveStream(xobjs.Get(doName.Value));
                                if (xstr is not null && xstr.Dict.GetName("Subtype") == "Form")
                                {
                                    var xbytes = reader.DecodeStream(xstr);
                                    ExtractTextFromContentStream(xbytes, xstr.Dict, reader, depth + 1,
                                        pageBounds, localCmTx, localCmTy);
                                }
                            }
                            break;
                        }
                        case "Tf" when operands.Count >= 2:
                            fontSize = GetNumber(operands[1]);
                            if (operands[0] is PdfName tfFontName)
                            {
                                currentFontName = tfFontName.Value;
                                if (fonts.TryGetValue(currentFontName, out var tfFontDict))
                                {
                                    currentFontDict = tfFontDict;
                                    currentToUnicode = useFontEngine ? null : ParseToUnicode(tfFontDict, reader);
                                    currentMetrics = FontMetrics.FromFontDict(tfFontDict, reader);
                                }
                                else
                                {
                                    currentFontDict = null;
                                    currentToUnicode = null;
                                    currentMetrics = null;
                                }
                            }
                            break;
                        case "Tm":
                            // Track scale components to interpret Td/TD displacements correctly.
                            // Many PDFs use a tiny-scale Tm (e.g. d=0.015) and large Td values;
                            // the actual page displacement is d * ty (or a * tx), not ty (tx) alone.
                            if (operands.Count >= 6)
                            {
                                var newTmY = GetNumber(operands[5]);
                                tmD = Math.Abs(GetNumber(operands[3]));

                                // Emit newline when Tm repositions to a different Y line.
                                // Compare against lastRenderedY (where the previous Tj/'/"
                                // actually PUT ink) rather than just prevTmY — the tracking Y
                                // can differ from the rendered Y by a full 'leading' when the
                                // previous BT/ET block used the '/(") operator to step down.
                                // Only do this for upright text (tmD > 0). Rotated text (tmD ≈ 0,
                                // e.g. 90° rotation [0 fs -fs 0 e f]) has meaningless f-value
                                // differences that would generate false line breaks.
                                var tmYThreshold = Math.Max(1.0, fontSize * 0.3);
                                var refY = !double.IsNaN(lastRenderedY) ? lastRenderedY : prevTmY;
                                // After a ' or " operator, the actual rendered Y is tmY - leading,
                                // but a subsequent Tm's newTmY is compared with the refY directly.
                                // For same-row column layouts the Tm targets Y ≈ previous Tm's Y
                                // (before its '), so the above refY==lastRenderedY path would
                                // fire a newline incorrectly. Fall back to prevTmY when the
                                // difference to lastRenderedY is exactly ~leading.
                                if (!double.IsNaN(prevTmY) && !double.IsNaN(lastRenderedY)
                                    && Math.Abs(Math.Abs(newTmY - lastRenderedY) - leading) < tmYThreshold)
                                {
                                    refY = prevTmY;
                                }
                                bool tmSameRow = tmD > 0 && !double.IsNaN(refY)
                                                 && Math.Abs(newTmY - refY) <= tmYThreshold;
                                if (tmD > 0 && !double.IsNaN(refY) && !tmSameRow &&
                                    _text.Length > 0 && _text[^1] != '\n')
                                {
                                    RecordLineY();
                                    _text.Append("\r\n");
                                }
                                // Track absolute page-space Y for line sorting
                                _currentLineY = newTmY;
                                prevTmY = newTmY;
                                tmY = newTmY;
                                tlmX = GetNumber(operands[4]);
                                tx = tlmX;
                                // Check page bounds for LimitToPageBounds filtering
                                // Apply accumulated CTM translation to convert local coords to page space
                                if (pageBounds is not null)
                                {
                                    var pageY = newTmY + localCmTy;
                                    var pageX = tlmX + localCmTx;
                                    skipText = pageY < pageBounds[1] - 1 || pageY > pageBounds[3] + 1 ||
                                               pageX < pageBounds[0] - 1 || pageX > pageBounds[2] + 1;
                                }
                                // Check search rectangle filter
                                if (!skipText && searchRect is not null)
                                {
                                    var pageY = newTmY + localCmTy;
                                    var pageX = tlmX + localCmTx;
                                    skipText = pageY < searchRect.LLY || pageY > searchRect.URY ||
                                               pageX < searchRect.LLX || pageX > searchRect.URX;
                                }
                                // Reset gap-detection only when the Tm actually moved to a new
                                // logical row. For same-row Tm (column reposition) keep
                                // lastRunEndX so the ' / " / Tj that follows can insert
                                // proportional spaces reflecting the visible column gap.
                                if (!tmSameRow) lastRunEndX = double.NaN;
                            }
                            break;
                        case "BT":
                            // PDF spec ISO 32000-1 §9.4.1: BT initializes only the text matrix
                            // and text line matrix to identity. All other text state (leading,
                            // char/word spacing, horizontal scaling, rendering mode, font size)
                            // persists across BT/ET per §9.3.  Earlier we zeroed leading here
                            // and wiped lastRunEndX, which caused the downstream
                            // Tm-vs-lastRenderedY heuristic to miss same-row column
                            // repositioning whenever a fresh BT block preceded the Tm (typical
                            // for column-per-BT PDF layouts). Keep lastRunEndX alive — the next
                            // Tm will decide whether to clear it based on row change.
                            tlmX = 0;
                            tx = 0;
                            tmY = 0;
                            tmD = 1.0;
                            lastRunEstWidth = 0;
                            horizScale = 1.0; // Tz resets to 100% at start of text object
                            break;
                        case "TL":
                            if (operands.Count >= 1)
                                leading = GetNumber(operands[0]);
                            break;
                        case "Tz":
                            if (operands.Count >= 1)
                                horizScale = GetNumber(operands[0]) / 100.0;
                            break;
                        case "Td" or "TD":
                        {
                            if (operands.Count >= 2)
                            {
                                var rawTy = GetNumber(operands[1]);
                                if (op == "TD") leading = -rawTy; // TD sets TL = -ty
                                var rawTx = GetNumber(operands[0]);
                                // PDF spec: Td updates the text LINE matrix, then sets Tm = Tlm.
                                // After Td, the text cursor resets to the new line origin.
                                // Keep rawTx unscaled: both Td advances and MeasureString widths
                                // use the same coordinate system (text space via fontSize from Tf).
                                tlmX += rawTx;

                                tx = tlmX;
                                // Compute actual page-space y-displacement: ty * tmD
                                // (tmD is the y-scale component from the most recent Tm)
                                var pageDisp = Math.Abs(rawTy * (tmD > 0 ? tmD : 1.0));
                                tmY += rawTy * (tmD > 0 ? tmD : 1.0);
                                if (pageDisp > 0.5)
                                {
                                    RecordLineY();
                                    _text.Append("\r\n");
                                    // Update currentLineY with Td displacement
                                    if (!double.IsNaN(_currentLineY))
                                        _currentLineY += rawTy * (tmD > 0 ? tmD : 1.0);
                                    lastRunEndX = double.NaN;
                                }
                                // Check page bounds with CTM
                                if (pageBounds is not null)
                                {
                                    var pageY = tmY + localCmTy;
                                    var pageX = tlmX + localCmTx;
                                    skipText = pageY < pageBounds[1] - 1 || pageY > pageBounds[3] + 1 ||
                                               pageX < pageBounds[0] - 1 || pageX > pageBounds[2] + 1;
                                }
                                // Check search rectangle filter
                                if (!skipText && searchRect is not null)
                                {
                                    var pageY = tmY + localCmTy;
                                    var pageX = tlmX + localCmTx;
                                    skipText = pageY < searchRect.LLY || pageY > searchRect.URY ||
                                               pageX < searchRect.LLX || pageX > searchRect.URX;
                                }
                            }
                            break;
                        }
                        case "T*":
                        {
                            // Equivalent to 0 -TL Td; apply same scale-aware threshold
                            // T* resets text cursor to line origin (x=0 advance in Td terms)
                            tx = tlmX;
                            var pageDisp = Math.Abs(leading * (tmD > 0 ? tmD : 1.0));
                            if (pageDisp > 0.5)
                            {
                                RecordLineY();
                                _text.Append("\r\n");
                                if (!double.IsNaN(_currentLineY))
                                    _currentLineY -= leading * (tmD > 0 ? tmD : 1.0);
                                lastRunEndX = double.NaN;
                            }
                            break;
                        }
                        case "Tj":
                        {
                            _textShowingOpCount++;
                            if (skipText) break;
                            if (operands.Count >= 1 && operands[0] is PdfString tjStr)
                            {
                                if (actualText is not null)
                                {
                                    if (!actualTextUsed)
                                    {
                                        _text.Append(actualText);
                                        actualTextUsed = true;
                                        // tx is not updated for ActualText; reset lastRunEndX so the
                                        // next regular Tj/TJ doesn't compute a bogus gap.
                                        lastRunEndX = double.NaN;
                                    }
                                }
                                else
                                {
                                    var decoded = ApplyRtlIfPureRtl(NormalizeDecoded(DecodeString(tjStr.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
                                    // Insert space for significant inter-word gap.
                                    // With proper text line matrix tracking, gap = tx - lastRunEndX
                                    // represents the actual visual gap between text runs (in user space).
                                    // A word space is typically ~fontSize * 0.25; we use a lower threshold
                                    // to catch narrow word spaces while avoiding false positives.
                                    if (!double.IsNaN(lastRunEndX)
                                        && _text.Length > 0 && _text[^1] != ' ' && _text[^1] != '\n')
                                    {
                                        var gap = tx - lastRunEndX;
                                        // Use a threshold based on font size. Lower threshold for runs
                                        // with font metrics since tlmX tracking gives accurate gaps.
                                        // Cumulative font metric imprecision over long runs can narrow
                                        // the apparent gap, so use 0.10 * fontSize to catch narrow spaces.
                                        var threshold = (lastHadMetrics || currentMetrics != null)
                                            ? fontSize * 0.10
                                            : fontSize * 0.4;
                                        var spaces = ComputeSpaceCount(gap, threshold, fontSize);
                                        for (int si = 0; si < spaces; si++) _text.Append(' ');
                                    }
                                    // Avoid double spaces: if a space was just emitted and the decoded text
                                    // starts with a space, skip the leading space.
                                    if (_text.Length > 0 && _text[^1] == ' ' && decoded.Length > 0 && decoded[0] == ' ')
                                        decoded = decoded.Substring(1);
                                    _text.Append(decoded);
                                    var measuredWidth = currentMetrics?.MeasureString(tjStr.Value, fontSize);
                                    var width = (measuredWidth ?? (fontSize * 0.5 * decoded.Length)) * horizScale;
                                    lastRunEndX = tx + width;
                                    lastRunEstWidth = width;
                                    lastHadMetrics = measuredWidth.HasValue;
                                    lastDecodedLength = decoded.Length;
                                    tx += width;
                                    // Track rendered Y so subsequent '/"/'Tm' can distinguish
                                    // same-row column repositioning from real line advances.
                                    lastRenderedY = tmY;
                                }
                            }
                            break;
                        }
                        case "TJ":
                        {
                            _textShowingOpCount++;
                            if (skipText) break;
                            if (operands.Count >= 1 && operands[0] is PdfArray tjArr)
                            {
                                if (actualText is not null)
                                {
                                    if (!actualTextUsed)
                                    {
                                        _text.Append(actualText);
                                        actualTextUsed = true;
                                        lastRunEndX = double.NaN;
                                    }
                                }
                                else
                                {
                                    double tjWidth = 0;
                                    int tjDecodedLen = 0;
                                    var tjFirst = true;
                                    // Buffer the TJ text so we can apply per-operator RTL reversal
                                    // after collecting all sub-strings (mirrors TypeScript applyRtl on TJ).
                                    var tjBuf = new StringBuilder();
                                    foreach (var item in tjArr)
                                    {
                                        if (item is PdfString tjS)
                                        {
                                            var tjDecoded = NormalizeDecoded(DecodeString(tjS.Value, currentToUnicode, currentFontDict, reader, useFontEngine));
                                            // Insert inter-word space before first sub-string if gap detected.
                                            // With proper tlmX tracking, gap is the actual visual gap.
                                            if (tjFirst && !double.IsNaN(lastRunEndX)
                                                && _text.Length > 0 && _text[^1] != ' ' && _text[^1] != '\n')
                                            {
                                                var tjGap = tx - lastRunEndX;
                                                var tjThreshold = fontSize * 0.15;
                                                var tjSpaces = ComputeSpaceCount(tjGap, tjThreshold, fontSize);
                                                for (int si = 0; si < tjSpaces; si++) _text.Append(' ');
                                            }
                                            tjFirst = false;
                                            tjBuf.Append(tjDecoded);
                                            tjWidth += (currentMetrics?.MeasureString(tjS.Value, fontSize)
                                                       ?? (fontSize * 0.5 * tjS.Value.Length)) * horizScale;
                                            tjDecodedLen += tjDecoded.Length;
                                        }
                                        else
                                        {
                                            var adj = GetNumber(item);
                                            tjWidth += -adj * fontSize / 1000.0;
                                            if (adj < -190 && (tjBuf.Length == 0 || tjBuf[^1] != ' '))
                                                tjBuf.Append(' ');
                                        }
                                    }
                                    // Apply per-operator RTL reversal: if all decoded TJ chars are RTL/neutral,
                                    // reverse to convert visual order to logical order (Hebrew, Arabic).
                                    var tjText = ApplyRtlIfPureRtl(tjBuf.ToString());
                                    // Avoid double spaces between previous run and this TJ block.
                                    if (_text.Length > 0 && _text[^1] == ' ' && tjText.Length > 0 && tjText[0] == ' ')
                                        tjText = tjText.Substring(1);
                                    _text.Append(tjText);
                                    lastRunEndX = tx + tjWidth;
                                    lastRunEstWidth = tjWidth;
                                    lastDecodedLength = tjDecodedLen;
                                    tx += tjWidth;
                                    // Track rendered Y for subsequent line-break suppression logic
                                    lastRenderedY = tmY;
                                }
                            }
                            break;
                        }
                        case "'":
                        case "\"":
                        {
                            _textShowingOpCount++;
                            // PDF spec: ' is "move to next line and show string" — equivalent to T* then Tj.
                            //          " is "set word/char spacing, move to next line, show string" —
                            //          operands = aw, ac, string.
                            // The operator advances the text line matrix by -leading in y.
                            // Historically we unconditionally emitted \r\n, but when a preceding Tm
                            // has just repositioned to a different column's Y (same visual row),
                            // the post-' Y may still be on the SAME logical line. Compare with
                            // lastRenderedY to decide.
                            if (skipText) { break; }
                            PdfString? qStr = null;
                            if (op == "'" && operands.Count >= 1) qStr = operands[0] as PdfString;
                            else if (op == "\"" && operands.Count >= 3) qStr = operands[2] as PdfString;

                            // Move text line matrix down by leading (pre-text position).
                            var newY = tmY - leading * (tmD > 0 ? tmD : 1.0);
                            tmY = newY;
                            tx = tlmX;

                            // Decide whether to emit a newline. If we have no prior rendered Y
                            // or the new Y is meaningfully below the last rendered Y, we are on
                            // a new logical line — emit \r\n. Otherwise (same Y ± ~fontSize*0.3)
                            // we are continuing the same row from a different column.
                            var yThreshold = Math.Max(1.0, fontSize * 0.3);
                            bool sameRow = !double.IsNaN(lastRenderedY)
                                           && Math.Abs(newY - lastRenderedY) <= yThreshold;
                            if (!sameRow)
                            {
                                if (_text.Length > 0 && _text[^1] != '\n')
                                {
                                    RecordLineY();
                                    _text.Append("\r\n");
                                }
                                lastRunEndX = double.NaN; // new line, reset gap tracking
                            }

                            if (qStr is not null)
                            {
                                if (actualText is not null)
                                {
                                    if (!actualTextUsed)
                                    {
                                        _text.Append(actualText);
                                        actualTextUsed = true;
                                    }
                                }
                                else
                                {
                                    var decoded = ApplyRtlIfPureRtl(NormalizeDecoded(
                                        DecodeString(qStr.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
                                    // Same-row continuation: insert proportional spaces for the
                                    // horizontal gap (Pure mode), mirrors Tj/TJ gap logic.
                                    if (sameRow && !double.IsNaN(lastRunEndX)
                                        && _text.Length > 0 && _text[^1] != ' ' && _text[^1] != '\n')
                                    {
                                        var gap = tx - lastRunEndX;
                                        var threshold = fontSize * 0.2;
                                        var spaces = ComputeSpaceCount(gap, threshold, fontSize);
                                        for (int si = 0; si < spaces; si++) _text.Append(' ');
                                    }
                                    _text.Append(decoded);
                                    var measuredWidth = currentMetrics?.MeasureString(qStr.Value, fontSize);
                                    var width = (measuredWidth ?? (fontSize * 0.5 * decoded.Length)) * horizScale;
                                    lastRunEndX = tx + width;
                                    lastRunEstWidth = width;
                                    lastHadMetrics = measuredWidth.HasValue;
                                    lastDecodedLength = decoded.Length;
                                    tx += width;
                                    lastRenderedY = newY;
                                }
                            }
                            _currentLineY = newY;
                            break;
                        }
                        default:
                            ProcessOperator(op, operands, fonts, reader, pageDict,
                                ref currentFontName, ref currentToUnicode, ref currentFontDict,
                                actualText, ref actualTextUsed, fontSize, depth);
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
    }

    private void ProcessOperator(string op, List<PdfObject> operands,
        Dictionary<string, PdfDictionary> fonts, PdfReader reader, PdfDictionary pageDict,
        ref string? currentFontName, ref Dictionary<int, string>? currentToUnicode,
        ref PdfDictionary? currentFontDict,
        string? actualText, ref bool actualTextUsed, double fontSize, int depth)
    {
        // UseFontEngineEncoding: decode via the font program's encoding/cmap instead of
        // /ToUnicode (mirrors the local of the same name in the main extraction loop).
        bool useFontEngine = TextSearchOptions?.UseFontEngineEncoding ?? false;
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
                            _text.Append(actualText);
                            actualTextUsed = true;
                        }
                    }
                    else
                    {
                        _text.Append(NormalizeDecoded(DecodeString(str.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
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
                            _text.Append(actualText);
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
                                _text.Append(NormalizeDecoded(DecodeString(s.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
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
                _text.Append("\r\n");
                if (operands.Count >= 1 && operands[0] is PdfString str2)
                {
                    if (actualText is not null && !actualTextUsed)
                    {
                        _text.Append(actualText);
                        actualTextUsed = true;
                    }
                    else if (actualText is null)
                    {
                        _text.Append(NormalizeDecoded(DecodeString(str2.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
                    }
                }
                break;

            case "\"": // Set spacing, move to next line, show string
                _text.Append("\r\n");
                if (operands.Count >= 3 && operands[2] is PdfString str3)
                {
                    if (actualText is not null && !actualTextUsed)
                    {
                        _text.Append(actualText);
                        actualTextUsed = true;
                    }
                    else if (actualText is null)
                    {
                        _text.Append(NormalizeDecoded(DecodeString(str3.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
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

    /// <summary>
    /// Decode a byte string using the font's encoding. Used by both TextAbsorber and TextFragmentAbsorber.
    /// </summary>
    internal static string DecodeStringPublic(byte[] bytes, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader, bool useFontEngineEncoding = false)
        => NormalizeDecoded(DecodeString(bytes, toUnicode, fontDict, reader, useFontEngineEncoding));

    /// <summary>
    /// Normalize a decoded text string for extraction: replace U+00A0 (non-breaking space)
    /// with regular space so that double-space suppression works correctly.
    /// </summary>
    private static string NormalizeDecoded(string s) =>
        s.IndexOf('\u00A0') >= 0 ? s.Replace('\u00A0', ' ') : s;

    private static string DecodeString(byte[] bytes, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader, bool useFontEngineEncoding = false)
    {
        // Resolve Differences encoding upfront (used as fallback below)
        Dictionary<int, string>? differences = null;
        string? baseEncodingName = null;

        var encodingObj = fontDict?.Get("Encoding");
        PdfDictionary? encodingDict = null;
        if (encodingObj is PdfDictionary ed)
            encodingDict = ed;
        else if (encodingObj is not null)
            encodingDict = reader.ResolveDict(encodingObj);

        if (encodingDict is not null)
        {
            differences = ParseDifferencesEncoding(encodingDict, reader);
            baseEncodingName = encodingDict.GetName("BaseEncoding");
        }

        // 1. ToUnicode CMap — highest priority; Differences used as fallback for unmapped codes
        if (toUnicode is not null)
        {
            return DecodeWithToUnicode(bytes, toUnicode, fontDict, reader, differences, baseEncodingName);
        }

        // Identity-H / Identity-V — 2-byte CID encoding
        // Also handle Uni*-UCS2-* / Uni*-UTF16-* predefined CMaps (2-byte big-endian → Unicode codepoint)
        if (fontDict?.GetName("Subtype") == "Type0")
        {
            var cidEncoding = fontDict.GetName("Encoding");
            if (cidEncoding is not null && (
                cidEncoding == "Identity-H" || cidEncoding == "Identity-V" ||
                cidEncoding.Contains("-UCS2-") || cidEncoding.Contains("-UTF16-")))
            {
                // Try to get Adobe CID collection ordering for predefined table lookup
                var cidOrdering = GetCidOrdering(fontDict, reader);
                // Only when the caller asked to decode via the font engine's encoding do we
                // recover Unicode from the embedded program's cmap (inverting glyph id →
                // Unicode). By default a CID font without /ToUnicode keeps the raw-code
                // fallback, matching the established extraction behaviour.
                var gidToUnicode = useFontEngineEncoding
                    ? GetEmbeddedGidToUnicode(fontDict, reader)
                    : null;
                return DecodeCidString(bytes, toUnicode, cidOrdering, gidToUnicode);
            }
        }

        // 2. Differences from Encoding dict
        if (differences is not null)
        {
            var sb = new StringBuilder();
            foreach (var b in bytes)
            {
                if (differences.TryGetValue(b, out var mapped))
                    sb.Append(mapped);
                else
                    sb.Append(DecodeByteWithEncoding(b, baseEncodingName));
            }
            return sb.ToString();
        }

        // 3. BaseEncoding from Encoding dict (no Differences)
        if (baseEncodingName is not null)
            return DecodeWithNamedEncoding(bytes, baseEncodingName);

        // 4. Encoding is a name
        var encoding = fontDict?.GetName("Encoding");
        if (encoding is not null)
            return DecodeWithNamedEncoding(bytes, encoding);

        // 5. Check for Symbol or ZapfDingbats built-in font encoding
        var baseFont = fontDict?.GetName("BaseFont");
        if (baseFont is not null)
        {
            var cleanName = baseFont.Contains('+') ? baseFont.Substring(baseFont.IndexOf('+') + 1) : baseFont;
            if (cleanName == "Symbol")
            {
                var sb = new StringBuilder(bytes.Length);
                foreach (var b in bytes)
                    sb.Append(SymbolEncoding.TryGetValue(b, out var ch) ? ch : (char)b);
                return sb.ToString();
            }
            if (cleanName == "ZapfDingbats")
            {
                var sb = new StringBuilder(bytes.Length);
                foreach (var b in bytes)
                    sb.Append(ZapfDingbatsEncoding.TryGetValue(b, out var ch) ? ch : (char)b);
                return sb.ToString();
            }
        }

        // 6. Default: WinAnsiEncoding
        return DecodeWithNamedEncoding(bytes, null);
    }

    private static string DecodeWithNamedEncoding(byte[] bytes, string? encoding)
    {
        if (encoding == "MacRomanEncoding")
        {
            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
                sb.Append(DecodeByteWithEncoding(b, "MacRomanEncoding"));
            return sb.ToString();
        }

        // WinAnsiEncoding or null (default)
        if (encoding == "WinAnsiEncoding" || encoding is null)
        {
            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
                sb.Append(DecodeByteWithEncoding(b, "WinAnsiEncoding"));
            return sb.ToString();
        }

        // Identity-H / Identity-V and other 2-byte predefined CJK CMaps
        if (encoding == "Identity-H" || encoding == "Identity-V" ||
            encoding.Contains("-UCS2-") || encoding.Contains("-UTF16-"))
            return DecodeCidString(bytes, null);

        // Unknown encoding — treat as WinAnsi
        var sb2 = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
            sb2.Append(DecodeByteWithEncoding(b, "WinAnsiEncoding"));
        return sb2.ToString();
    }

    private static char DecodeByteWithEncoding(byte b, string? encoding)
    {
        if (encoding == "MacRomanEncoding")
        {
            if (b < 128)
                return (char)b;
            return MacRomanEncoding.TryGetValue(b, out var ch) ? ch : (char)b;
        }

        // WinAnsiEncoding (default)
        if (b < 128)
            return (char)b;
        return WinAnsiEncoding.TryGetValue(b, out var wch) ? wch : (char)b;
    }

    /// <summary>
    /// Parse the /Differences array from an encoding dictionary.
    /// Returns a map from byte code to Unicode string, or null if no Differences found.
    /// </summary>
    internal static Dictionary<int, string>? ParseDifferencesEncoding(PdfDictionary encodingDict, PdfReader reader)
    {
        var diffObj = encodingDict.Get("Differences");
        PdfArray? diffArray = null;

        if (diffObj is PdfArray arr)
            diffArray = arr;
        else if (diffObj is not null)
        {
            // Could be an indirect reference
            var resolved = reader.Resolve(diffObj);
            if (resolved is PdfArray resolvedArr)
                diffArray = resolvedArr;
        }

        if (diffArray is null || diffArray.Count == 0)
            return null;

        var map = new Dictionary<int, string>();
        var currentCode = 0;

        foreach (var item in diffArray)
        {
            if (item is PdfInteger intVal)
            {
                currentCode = (int)intVal.Value;
            }
            else if (item is PdfName nameVal)
            {
                var glyphName = nameVal.Value;
                var resolved = ResolveGlyphName(glyphName);
                if (resolved is not null)
                    map[currentCode] = resolved;
                else
                    map[currentCode] = ((char)currentCode).ToString(); // fallback to code point
                currentCode++;
            }
        }

        return map.Count > 0 ? map : null;
    }

    /// <summary>
    /// Resolve an Adobe glyph name to its Unicode string representation.
    /// Supports dictionary lookup, uni&lt;XXXX&gt; and u&lt;XXXX&gt; patterns.
    /// </summary>
    internal static string? ResolveGlyphName(string name)
    {
        // Single ASCII character — return as-is
        if (name.Length == 1) return name;

        // Dictionary lookup
        if (GlyphNameToUnicode.TryGetValue(name, out var unicode))
            return unicode;

        // uni<XXXX> form — explicit Unicode codepoint(s), groups of 4 hex digits
        if (name.Length >= 7 && name.StartsWith("uni", StringComparison.Ordinal))
        {
            var hex = name.Substring(3);
            if (hex.Length % 4 == 0 && IsAllHex(hex))
            {
                var sb = new StringBuilder();
                for (int i = 0; i < hex.Length; i += 4)
                    sb.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
                return sb.Length > 0 ? sb.ToString() : null;
            }
        }

        // u<XXXX> form — single codepoint, 4-6 hex digits
        if (name.Length >= 5 && name.Length <= 7 && name[0] == 'u' && IsAllHex(name.Substring(1)))
            return char.ConvertFromUtf32(Convert.ToInt32(name.Substring(1), 16));

        // G<number> form — glyph index used as character code (common in subset fonts)
        // e.g. /G65 → 'A', /G32 → ' ', /G147 → U+201C via WinAnsi
        if (name.Length >= 2 && name[0] == 'G')
        {
            var suffix = name.Substring(1);
            if (suffix.Length > 0 && suffix.All(char.IsAsciiDigit))
            {
                var code = int.Parse(suffix);
                if (code < 128)
                    return ((char)code).ToString();
                if (code < 256)
                {
                    // Map through WinAnsiEncoding for 128-255
                    if (WinAnsiEncoding.TryGetValue((byte)code, out var wch))
                        return wch.ToString();
                }
                return char.ConvertFromUtf32(code);
            }
        }

        return null;
    }

    private static bool IsAllHex(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                return false;
        }
        return s.Length > 0;
    }

    private static string DecodeWithToUnicode(byte[] bytes, Dictionary<int, string> map,
        PdfDictionary? fontDict, PdfReader reader,
        Dictionary<int, string>? differences = null, string? baseEncodingName = null)
    {
        var isCid = fontDict?.GetName("Subtype") == "Type0";
        var sb = new StringBuilder();
        var i = 0;

        while (i < bytes.Length)
        {
            // Try 2-byte lookup first (handles CIDFonts and mixed encodings)
            if (i + 1 < bytes.Length)
            {
                var code2 = (bytes[i] << 8) | bytes[i + 1];
                if (map.TryGetValue(code2, out var mapped2))
                {
                    sb.Append(mapped2);
                    i += 2;
                    continue;
                }
            }

            // Try 1-byte lookup
            var code1 = bytes[i];
            if (map.TryGetValue(code1, out var mapped1))
            {
                sb.Append(mapped1);
                i++;
                continue;
            }

            // Try Differences encoding as fallback (single byte)
            if (differences is not null && differences.TryGetValue(code1, out var diffMapped))
            {
                sb.Append(diffMapped);
                i++;
                continue;
            }

            // Fallback for CID fonts: interpret 2-byte value as direct Unicode (UCS-2/UTF-16)
            if (isCid && i + 1 < bytes.Length)
            {
                var code = (bytes[i] << 8) | bytes[i + 1];
                if (code is > 0 and < 0xD800 or > 0xDFFF and <= 0xFFFF)
                    sb.Append((char)code);
                else
                    sb.Append('\uFFFD');
                i += 2;
            }
            else
            {
                sb.Append(DecodeByteWithEncoding(bytes[i], baseEncodingName));
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts the CIDSystemInfo /Ordering from the first DescendantFont of a Type0 font.
    /// Returns null if not available or if Registry is not "Adobe".
    /// </summary>
    private static string? GetCidOrdering(PdfDictionary type0FontDict, PdfReader reader)
    {
        if (reader is null) return null;
        var descObj = reader.Resolve(type0FontDict.Get("DescendantFonts"));
        if (descObj is not PdfArray descArr || descArr.Count == 0) return null;
        var cidFontDict = reader.ResolveDict(descArr[0]);
        if (cidFontDict is null) return null;
        var cidSystemInfo = reader.ResolveDict(cidFontDict.Get("CIDSystemInfo"));
        if (cidSystemInfo is null) return null;
        // Registry and Ordering are PDF strings (not names)
        var registryObj = cidSystemInfo.Get("Registry");
        var registry = registryObj is PdfString rs ? rs.ToText() : (registryObj is PdfName rn ? rn.Value : null);
        if (registry != "Adobe") return null;
        var orderingObj = cidSystemInfo.Get("Ordering");
        return orderingObj is PdfString os ? os.ToText() : (orderingObj is PdfName on2 ? on2.Value : null);
    }

    private static string DecodeCidString(byte[] bytes, Dictionary<int, string>? toUnicode,
        string? cidOrdering = null, Dictionary<int, int>? gidToUnicode = null)
    {
        if (toUnicode is not null)
            return DecodeWithToUnicode(bytes, toUnicode, null, null!);

        var sb = new StringBuilder();
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            var code = (bytes[i] << 8) | bytes[i + 1];
            // Try Adobe predefined CID collection lookup first
            if (cidOrdering is not null)
            {
                var unicode = AdobeCidTables.LookupCid(cidOrdering, code);
                if (unicode is not null)
                {
                    sb.Append(char.ConvertFromUtf32(unicode.Value));
                    continue;
                }
            }
            // Identity ordering with no ToUnicode: reverse-map the glyph id to Unicode
            // through the embedded font program's cmap (built once per font).
            if (gidToUnicode is not null && gidToUnicode.TryGetValue(code, out var u))
            {
                sb.Append(char.ConvertFromUtf32(u));
                continue;
            }
            sb.Append((char)code);
        }
        return sb.ToString();
    }

    // Cache of glyph-id → Unicode maps built from an embedded CIDFontType2 program, keyed
    // by the Type0 font dictionary so a page's repeated decode calls parse the font once.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, Dictionary<int, int>> _gidToUnicodeCache = new();

    /// <summary>
    /// Build a glyph-id → Unicode map for an Identity-encoded Type0 font that lacks a
    /// /ToUnicode CMap, by inverting the embedded TrueType program's cmap (and threading the
    /// CID→GID mapping when /CIDToGIDMap is a stream). Returns an empty map when the font has
    /// no usable embedded program. Cached per font dictionary.
    /// </summary>
    private static Dictionary<int, int>? GetEmbeddedGidToUnicode(PdfDictionary fontDict, PdfReader reader)
    {
        if (_gidToUnicodeCache.TryGetValue(fontDict, out var cached))
            return cached.Count > 0 ? cached : null;

        var map = new Dictionary<int, int>();
        try
        {
            var descArr = reader.Resolve(fontDict.Get("DescendantFonts")) as PdfArray;
            var descendant = descArr is { Count: > 0 } ? reader.ResolveDict(descArr[0]) : null;
            var fd = descendant is null ? null : reader.ResolveDict(descendant.Get("FontDescriptor"));
            var ff2 = fd?.Get("FontFile2") ?? fd?.Get("FontFile3");
            var stream = ff2 is null ? null : reader.ResolveStream(ff2);
            if (stream is not null)
            {
                var data = reader.DecodeStream(stream);
                var parser = new TrueTypeParser(data);
                parser.Parse();
                // parser.CMap is Unicode → glyph id; invert it. When several codepoints map
                // to the SAME glyph (e.g. a hyphen glyph reachable from both U+002D
                // hyphen-minus and U+00AD soft-hyphen), prefer the SMALLEST codepoint — the
                // canonical ASCII/base character — instead of letting iteration order decide.
                // Otherwise font-engine extraction can yield the compatibility variant (soft
                // hyphen) and break searches that expect the base char.
                var gidToUni = new Dictionary<int, int>(parser.CMap.Count);
                foreach (var kv in parser.CMap)
                    if (!gidToUni.TryGetValue(kv.Value, out var existing) || kv.Key < existing)
                        gidToUni[kv.Value] = kv.Key;

                // CIDToGIDMap: Identity (default) means CID == GID, so the 2-byte code is
                // already the glyph id. A stream maps CID → GID as packed big-endian uint16s.
                var c2g = descendant!.Get("CIDToGIDMap");
                var c2gStream = c2g is not null ? reader.ResolveStream(c2g) : null;
                if (c2gStream is not null)
                {
                    var cg = reader.DecodeStream(c2gStream);
                    for (int cid = 0; cid * 2 + 1 < cg.Length; cid++)
                    {
                        int gid = (cg[cid * 2] << 8) | cg[cid * 2 + 1];
                        if (gid != 0 && gidToUni.TryGetValue(gid, out var u)) map[cid] = u;
                    }
                }
                else
                {
                    foreach (var kv in gidToUni) map[kv.Key] = kv.Value;
                }
            }
        }
        catch { /* best-effort: leave the map empty so the caller falls back */ }

        _gidToUnicodeCache.AddOrUpdate(fontDict, map);
        return map.Count > 0 ? map : null;
    }

    /// <summary>
    /// Resolve XObject resources by walking up the page tree hierarchy.
    /// Returns the first XObject dict found (page-level takes priority over parent).
    /// </summary>
    internal static PdfDictionary? ResolveXObjects(PdfDictionary dict, PdfReader reader)
    {
        var current = dict;
        int depth = 0;
        while (current is not null && depth < 6)
        {
            var resources = reader.ResolveDict(current.Get("Resources"));
            if (resources is not null)
            {
                var xobjs = reader.ResolveDict(resources.Get("XObject"));
                if (xobjs is not null) return xobjs;
            }
            current = reader.ResolveDict(current.Get("Parent"));
            depth++;
        }
        return null;
    }

    internal static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        CollectFontsFromHierarchy(pageDict, reader, result, depth: 0);
        return result;
    }

    /// <summary>
    /// Collect fonts by walking up the page tree, allowing parent Resources to
    /// provide fonts not defined in the page's own Resources dict.
    /// Page-level fonts override parent fonts of the same name.
    /// </summary>
    private static void CollectFontsFromHierarchy(PdfDictionary dict, PdfReader reader,
        Dictionary<string, PdfDictionary> result, int depth)
    {
        if (depth > 6) return; // guard against infinite loops

        // Walk parent first (lower priority), then overlay with this node's fonts
        var parentRef = dict.Get("Parent");
        if (parentRef is not null)
        {
            var parentDict = reader.ResolveDict(parentRef);
            if (parentDict is not null)
                CollectFontsFromHierarchy(parentDict, reader, result, depth + 1);
        }

        var resources = reader.ResolveDict(dict.Get("Resources"));
        if (resources is null) return;

        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return;

        foreach (var key in fontDict.Keys)
        {
            var font = reader.ResolveDict(fontDict.Get(key));
            if (font is not null)
                result[key] = font; // page-level overrides parent
        }
    }

    internal static Dictionary<int, string>? ParseToUnicodeFromDict(PdfDictionary fontDict, PdfReader reader) =>
        ParseToUnicode(fontDict, reader);

    /// <summary>For diagnostics only: expose ParseCMap publicly.</summary>
    internal static Dictionary<int, string> ParseCMapPublic(string cmapText) => ParseCMap(cmapText);

    private static Dictionary<int, string>? ParseToUnicode(PdfDictionary fontDict, PdfReader reader)
    {
        var toUnicodeObj = fontDict.Get("ToUnicode");
        if (toUnicodeObj is null) return null;

        var stream = reader.ResolveStream(toUnicodeObj);
        if (stream is null) return null;

        var decoded = reader.DecodeStream(stream);
        var text = Encoding.ASCII.GetString(decoded);

        return ParseCMap(text);
    }

    internal static Dictionary<int, string> ParseCMap(string cmapText)
    {
        var map = new Dictionary<int, string>();
        // Normalize: ensure section markers are on their own lines.
        // This handles CMaps where all content is on a single line (space-separated).
        cmapText = Regex.Replace(cmapText,
            @"(begin|end)(bfchar|bfrange)",
            "\n$1$2\n",
            RegexOptions.IgnoreCase);
        var lines = cmapText.Split('\n');

        var inBfChar = false;
        var inBfRange = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.Contains("beginbfchar", StringComparison.Ordinal))
            {
                inBfChar = true;
                continue;
            }
            if (line.Contains("endbfchar", StringComparison.Ordinal))
            {
                inBfChar = false;
                continue;
            }
            if (line.Contains("beginbfrange", StringComparison.Ordinal))
            {
                inBfRange = true;
                continue;
            }
            if (line.Contains("endbfrange", StringComparison.Ordinal))
            {
                inBfRange = false;
                continue;
            }

            if (inBfChar)
            {
                // A line may contain multiple pairs: <code> <unicode> <code> <unicode> .
                var tokens = ExtractHexTokens(line);
                for (var k = 0; k + 1 < tokens.Count; k += 2)
                {
                    var code = ParseHexInt(tokens[k]);
                    var unicode = HexToString(tokens[k + 1]);
                    map[code] = unicode;
                }
            }
            else if (inBfRange)
            {
                var tokens = ExtractHexTokens(line);
                if (tokens.Count >= 3)
                {
                    var start = ParseHexInt(tokens[0]);
                    var end = ParseHexInt(tokens[1]);

                    // Check if line contains array form: <start> <end> [<d0> <d1> .]
                    var arrayStart = line.IndexOf('[');
                    if (arrayStart >= 0)
                    {
                        // Array form: each code maps to successive array entries
                        var arrayTokens = tokens.Skip(2).ToList(); // tokens from inside array
                        for (var code = start; code <= end; code++)
                        {
                            var idx = code - start;
                            if (idx < arrayTokens.Count)
                                map[code] = HexToString(arrayTokens[idx]);
                        }
                    }
                    else
                    {
                        // Sequential form: start code maps to startUnicode, next codes increment
                        var startUnicode = ParseHexInt(tokens[2]);
                        for (var code = start; code <= end; code++)
                        {
                            var cp = startUnicode + (code - start);
                            if (cp is >= 0xD800 and <= 0xDFFF || cp > 0x10FFFF)
                                continue; // skip invalid surrogate codepoints
                            map[code] = char.ConvertFromUtf32(cp);
                        }
                    }
                }
            }
        }

        return map;
    }

    private static List<string> ExtractHexTokens(string line)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] == '<')
            {
                var end = line.IndexOf('>', i);
                if (end > i)
                {
                    tokens.Add(line[(i + 1)..end].Replace(" ", ""));
                    i = end + 1;
                    continue;
                }
            }
            i++;
        }
        return tokens;
    }

    private static int ParseHexInt(string hex)
    {
        if (long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var val))
            return val > int.MaxValue ? 0 : (int)val;
        return 0;
    }

    // Known ligature sequences → single Unicode ligature characters.
    // When a single glyph code maps to a multi-char decomposition in a ToUnicode CMap,
    // replace with the ligature character to match Aspose.PDF for .NET behavior.
    private static readonly Dictionary<string, string> LigatureSequences = new()
    {
        ["fi"] = "\uFB01",
        ["fl"] = "\uFB02",
        ["ff"] = "\uFB00",
        ["ffi"] = "\uFB03",
        ["ffl"] = "\uFB04",
    };

    private static string HexToString(string hex)
    {
        var sb = new StringBuilder();
        for (var i = 0; i + 3 < hex.Length; i += 4)
        {
            var codePoint = ParseHexInt(hex[i..(i + 4)]);
            if (codePoint is >= 0xD800 and <= 0xDFFF || codePoint > 0x10FFFF)
                continue; // skip invalid surrogate codepoints
            sb.Append(char.ConvertFromUtf32(codePoint));
        }
        if (sb.Length == 0 && hex.Length >= 2)
        {
            // 2-digit hex = single byte
            sb.Append((char)ParseHexInt(hex));
        }
        var result = sb.ToString();
        // When a single glyph maps to a known ligature decomposition, use the ligature character
        if (result.Length >= 2 && LigatureSequences.TryGetValue(result, out var ligature))
            return ligature;
        return result;
    }

    /// <summary>
    /// Decode a PDF text string (handles BOM for UTF-16BE, otherwise Latin1).
    /// </summary>
    private static string DecodeTextString(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        return Encoding.Latin1.GetString(bytes);
    }

    private static PdfDictionary ParseContentDict(PdfLexer lexer)
    {
        var dict = new PdfDictionary();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.DictEnd || t.Kind == TokenKind.Eof) break;
            if (t.Kind != TokenKind.Name) continue;
            var key = t.StringValue!;
            var val = lexer.NextToken();
            if (val.Kind == TokenKind.DictEnd) break;
            PdfObject value = val.Kind switch
            {
                TokenKind.Integer => new PdfInteger(val.IntValue),
                TokenKind.Real => new PdfReal(val.RealValue),
                TokenKind.Name => new PdfName(val.StringValue!),
                TokenKind.LiteralString => new PdfString(val.BytesValue!),
                TokenKind.HexString => new PdfString(val.BytesValue!, isHex: true),
                TokenKind.Boolean => val.BoolValue ? PdfBoolean.True : PdfBoolean.False,
                _ => PdfNull.Instance,
            };
            dict.Set(key, value);
        }
        return dict;
    }

    private static PdfArray ParseContentArray(PdfLexer lexer)
    {
        var array = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind)
            {
                case TokenKind.Integer:
                    array.Add(new PdfInteger(t.IntValue));
                    break;
                case TokenKind.Real:
                    array.Add(new PdfReal(t.RealValue));
                    break;
                case TokenKind.LiteralString:
                    array.Add(new PdfString(t.BytesValue!));
                    break;
                case TokenKind.HexString:
                    array.Add(new PdfString(t.BytesValue!, isHex: true));
                    break;
                case TokenKind.Name:
                    array.Add(new PdfName(t.StringValue!));
                    break;
            }
        }
        return array;
    }

    // Compute number of spaces to emit for an inter-run gap.
    // Raw mode always emits at most 1 space (no visual formatting reconstruction).
    // Pure mode emits proportional spaces so column layout is preserved:
    //   count ≈ round(gap / spaceWidth), where spaceWidth is the typical space glyph width
    //   (~0.25 * fontSize for most Latin fonts). Clamped to avoid runaway widths.
    // Returns 0 when gap is below the threshold (no space should be inserted).
    private int ComputeSpaceCount(double gap, double threshold, double fontSize)
    {
        if (gap <= threshold) return 0;
        if (ExtractionOptions?.FormattingMode != TextExtractionOptions.TextFormattingMode.Raw)
        {
            // Pure mode: one space per ~0.217 * fontSize of gap width.
            // Matches Aspose.PDF for .NET Pure mode column-spacing output.
            var spaceWidth = Math.Max(fontSize * 0.217, 0.5);
            var count = (int)Math.Round(gap / spaceWidth);
            if (count < 1) count = 1;
            if (count > 40) count = 40;
            return count;
        }
        return 1;
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

    private static PdfReader GetReader(Page page) => page.Reader;

    /// <summary>
    /// Skip inline image data (BI . ID &lt;data&gt; EI) per PDF spec §8.9.7.
    /// </summary>
    internal static void SkipInlineImage(PdfLexer lexer)
    {
        // Consume tokens until the ID keyword (image data start), capturing the
        // dictionary keys needed to size the data.
        int imgW = 0, imgH = 0, imgBpc = 8, imgColors = 1; bool imgFlate = false;
        string? key = null;
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.Keyword && t.StringValue == "ID") break;
            if (t.Kind == TokenKind.Name)
            {
                var n = t.StringValue!;
                switch (n)
                {
                    case "RGB": case "DeviceRGB": if (key is "CS" or "ColorSpace") imgColors = 3; break;
                    case "CMYK": case "DeviceCMYK": if (key is "CS" or "ColorSpace") imgColors = 4; break;
                    case "G": case "DeviceGray": if (key is "CS" or "ColorSpace") imgColors = 1; break;
                    case "Fl": case "FlateDecode": if (key is "F" or "Filter") imgFlate = true; break;
                }
                key = n;
            }
            else if (t.Kind == TokenKind.Integer)
            {
                int v = (int)t.IntValue;
                switch (key) { case "W": case "Width": imgW = v; break; case "H": case "Height": imgH = v; break;
                    case "BPC": case "BitsPerComponent": imgBpc = v; break; case "Colors": imgColors = v; break; }
                key = null;
            }
            else key = null;
        }

        long dataStart0 = lexer.Position + 1; // one whitespace byte after ID
        long lenAll = lexer.Length;

        // Preferred for Flate-compressed data: probe each whitespace-delimited "EI"
        // candidate by inflating ID..candidate; the real EI is the earliest position
        // whose data inflates to the full raw image size. A stray "EI" byte pair inside
        // the compressed stream truncates the deflate stream → inflate fails, so it's
        // skipped. This stops the lexer desyncing and dropping every operator after the
        // image (nested-table grid lines were all lost after an inline image).
        if (imgFlate && imgW > 0 && imgH > 0)
        {
            int bytesPerRow = (imgW * imgColors * imgBpc + 7) / 8;
            int expected = imgH * bytesPerRow; // lower bound (a row predictor only adds bytes)
            int tailLen = (int)Math.Max(0, lenAll - dataStart0);
            var tail = new byte[tailLen];
            for (int i = 0; i < tailLen; i++) tail[i] = lexer.ByteAt(dataStart0 + i);
            for (int p = 1; p < tailLen - 1; p++)
            {
                if (tail[p] != (byte)'E' || tail[p + 1] != (byte)'I') continue;
                if (p + 2 < tailLen && !IsWhitespace(tail[p + 2])) continue;
                var slice = new byte[p];
                Array.Copy(tail, 0, slice, 0, p);
                try
                {
                    var inflated = Aspose.Pdf.IO.Filters.FlateDecodeFilter.Decode(slice, null);
                    if (inflated.Length >= expected) { lexer.Position = dataStart0 + p + 2; return; }
                }
                catch { /* truncated deflate at this candidate — keep scanning */ }
            }
        }

        // After ID, spec mandates one whitespace byte before raw data.
        // Scan raw bytes for 'E' 'I' followed by whitespace/EOF.
        // Many real-world PDFs don't have whitespace BEFORE "EI" (the image data
        // ends immediately before the E), so we check both patterns:
        //   1. Standard: whitespace + EI + whitespace (spec-compliant)
        //   2. Relaxed: any-byte + EI + whitespace (common in practice)
        var pos = lexer.Position + 1; // skip the whitespace byte after ID
        var len = lexer.Length;

        while (pos < len - 1)
        {
            if (lexer.ByteAt(pos) == (byte)'E' && lexer.ByteAt(pos + 1) == (byte)'I')
            {
                var after = pos + 2;
                if (after >= len || IsWhitespace(lexer.ByteAt(after)))
                {
                    // Verify this is the real EI by checking that what follows
                    // looks like valid PDF operators (not random image data).
                    // A valid operator context after EI would be: Q, BT, numbers, /, etc.
                    if (after < len)
                    {
                        // Skip whitespace after EI
                        var checkPos = after;
                        while (checkPos < len && IsWhitespace(lexer.ByteAt(checkPos)))
                            checkPos++;
                        if (checkPos < len)
                        {
                            var nextByte = lexer.ByteAt(checkPos);
                            // Valid PDF operator starts: letter, number, /, (, <, [
                            bool looksValid = (nextByte >= (byte)'A' && nextByte <= (byte)'Z')
                                || (nextByte >= (byte)'a' && nextByte <= (byte)'z')
                                || (nextByte >= (byte)'0' && nextByte <= (byte)'9')
                                || nextByte == (byte)'/' || nextByte == (byte)'('
                                || nextByte == (byte)'<' || nextByte == (byte)'['
                                || nextByte == (byte)'-' || nextByte == (byte)'.';
                            if (!looksValid) { pos++; continue; }
                        }
                    }
                    lexer.Position = after;
                    return;
                }
            }
            pos++;
        }
        lexer.Position = len; // consume everything if EI not found

        static bool IsWhitespace(byte b) =>
            b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;
    }

    private static double GetNumber(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    // ────────────────────────────────────────────────────────────────────────
    // WinAnsiEncoding table (codes 128-255) — PDF spec Table D.1
    // ────────────────────────────────────────────────────────────────────────
    private static readonly Dictionary<byte, char> WinAnsiEncoding = new()
    {
        [128] = '\u20AC', // Euro sign
        [130] = '\u201A', // single low-9 quotation mark
        [131] = '\u0192', // f with hook
        [132] = '\u201E', // double low-9 quotation mark
        [133] = '\u2026', // horizontal ellipsis
        [134] = '\u2020', // dagger
        [135] = '\u2021', // double dagger
        [136] = '\u02C6', // modifier letter circumflex accent
        [137] = '\u2030', // per mille sign
        [138] = '\u0160', // S with caron
        [139] = '\u2039', // single left-pointing angle quotation mark
        [140] = '\u0152', // OE ligature
        [142] = '\u017D', // Z with caron
        [145] = '\u2018', // left single quotation mark
        [146] = '\u2019', // right single quotation mark
        [147] = '\u201C', // left double quotation mark
        [148] = '\u201D', // right double quotation mark
        [149] = '\u2022', // bullet
        [150] = '\u2013', // en dash
        [151] = '\u2014', // em dash
        [152] = '\u02DC', // small tilde
        [153] = '\u2122', // trade mark sign
        [154] = '\u0161', // s with caron
        [155] = '\u203A', // single right-pointing angle quotation mark
        [156] = '\u0153', // oe ligature
        [158] = '\u017E', // z with caron
        [159] = '\u0178', // Y with diaeresis
        [160] = '\u00A0', // no-break space
        [161] = '\u00A1', // inverted exclamation mark
        [162] = '\u00A2', // cent sign
        [163] = '\u00A3', // pound sign
        [164] = '\u00A4', // currency sign
        [165] = '\u00A5', // yen sign
        [166] = '\u00A6', // broken bar
        [167] = '\u00A7', // section sign
        [168] = '\u00A8', // diaeresis
        [169] = '\u00A9', // copyright sign
        [170] = '\u00AA', // feminine ordinal indicator
        [171] = '\u00AB', // left-pointing double angle quotation mark
        [172] = '\u00AC', // not sign
        [173] = '\u00AD', // soft hyphen
        [174] = '\u00AE', // registered sign
        [175] = '\u00AF', // macron
        [176] = '\u00B0', // degree sign
        [177] = '\u00B1', // plus-minus sign
        [178] = '\u00B2', // superscript two
        [179] = '\u00B3', // superscript three
        [180] = '\u00B4', // acute accent
        [181] = '\u00B5', // micro sign
        [182] = '\u00B6', // pilcrow sign
        [183] = '\u00B7', // middle dot
        [184] = '\u00B8', // cedilla
        [185] = '\u00B9', // superscript one
        [186] = '\u00BA', // masculine ordinal indicator
        [187] = '\u00BB', // right-pointing double angle quotation mark
        [188] = '\u00BC', // vulgar fraction one quarter
        [189] = '\u00BD', // vulgar fraction one half
        [190] = '\u00BE', // vulgar fraction three quarters
        [191] = '\u00BF', // inverted question mark
        [192] = '\u00C0', // A with grave
        [193] = '\u00C1', // A with acute
        [194] = '\u00C2', // A with circumflex
        [195] = '\u00C3', // A with tilde
        [196] = '\u00C4', // A with diaeresis
        [197] = '\u00C5', // A with ring above
        [198] = '\u00C6', // AE
        [199] = '\u00C7', // C with cedilla
        [200] = '\u00C8', // E with grave
        [201] = '\u00C9', // E with acute
        [202] = '\u00CA', // E with circumflex
        [203] = '\u00CB', // E with diaeresis
        [204] = '\u00CC', // I with grave
        [205] = '\u00CD', // I with acute
        [206] = '\u00CE', // I with circumflex
        [207] = '\u00CF', // I with diaeresis
        [208] = '\u00D0', // Eth
        [209] = '\u00D1', // N with tilde
        [210] = '\u00D2', // O with grave
        [211] = '\u00D3', // O with acute
        [212] = '\u00D4', // O with circumflex
        [213] = '\u00D5', // O with tilde
        [214] = '\u00D6', // O with diaeresis
        [215] = '\u00D7', // multiplication sign
        [216] = '\u00D8', // O with stroke
        [217] = '\u00D9', // U with grave
        [218] = '\u00DA', // U with acute
        [219] = '\u00DB', // U with circumflex
        [220] = '\u00DC', // U with diaeresis
        [221] = '\u00DD', // Y with acute
        [222] = '\u00DE', // Thorn
        [223] = '\u00DF', // sharp s
        [224] = '\u00E0', // a with grave
        [225] = '\u00E1', // a with acute
        [226] = '\u00E2', // a with circumflex
        [227] = '\u00E3', // a with tilde
        [228] = '\u00E4', // a with diaeresis
        [229] = '\u00E5', // a with ring above
        [230] = '\u00E6', // ae
        [231] = '\u00E7', // c with cedilla
        [232] = '\u00E8', // e with grave
        [233] = '\u00E9', // e with acute
        [234] = '\u00EA', // e with circumflex
        [235] = '\u00EB', // e with diaeresis
        [236] = '\u00EC', // i with grave
        [237] = '\u00ED', // i with acute
        [238] = '\u00EE', // i with circumflex
        [239] = '\u00EF', // i with diaeresis
        [240] = '\u00F0', // eth
        [241] = '\u00F1', // n with tilde
        [242] = '\u00F2', // o with grave
        [243] = '\u00F3', // o with acute
        [244] = '\u00F4', // o with circumflex
        [245] = '\u00F5', // o with tilde
        [246] = '\u00F6', // o with diaeresis
        [247] = '\u00F7', // division sign
        [248] = '\u00F8', // o with stroke
        [249] = '\u00F9', // u with grave
        [250] = '\u00FA', // u with acute
        [251] = '\u00FB', // u with circumflex
        [252] = '\u00FC', // u with diaeresis
        [253] = '\u00FD', // y with acute
        [254] = '\u00FE', // thorn
        [255] = '\u00FF', // y with diaeresis
    };

    // ────────────────────────────────────────────────────────────────────────
    // MacRomanEncoding table (codes 128-255) — PDF spec Table D.2
    // ────────────────────────────────────────────────────────────────────────
    private static readonly Dictionary<byte, char> MacRomanEncoding = new()
    {
        [128] = '\u00C4', // A with diaeresis
        [129] = '\u00C5', // A with ring above
        [130] = '\u00C7', // C with cedilla
        [131] = '\u00C9', // E with acute
        [132] = '\u00D1', // N with tilde
        [133] = '\u00D6', // O with diaeresis
        [134] = '\u00DC', // U with diaeresis
        [135] = '\u00E1', // a with acute
        [136] = '\u00E0', // a with grave
        [137] = '\u00E2', // a with circumflex
        [138] = '\u00E4', // a with diaeresis
        [139] = '\u00E3', // a with tilde
        [140] = '\u00E5', // a with ring above
        [141] = '\u00E7', // c with cedilla
        [142] = '\u00E9', // e with acute
        [143] = '\u00E8', // e with grave
        [144] = '\u00EA', // e with circumflex
        [145] = '\u00EB', // e with diaeresis
        [146] = '\u00ED', // i with acute
        [147] = '\u00EC', // i with grave
        [148] = '\u00EE', // i with circumflex
        [149] = '\u00EF', // i with diaeresis
        [150] = '\u00F1', // n with tilde
        [151] = '\u00F3', // o with acute
        [152] = '\u00F2', // o with grave
        [153] = '\u00F4', // o with circumflex
        [154] = '\u00F6', // o with diaeresis
        [155] = '\u00F5', // o with tilde
        [156] = '\u00FA', // u with acute
        [157] = '\u00F9', // u with grave
        [158] = '\u00FB', // u with circumflex
        [159] = '\u00FC', // u with diaeresis
        [160] = '\u2020', // dagger
        [161] = '\u00B0', // degree sign
        [162] = '\u00A2', // cent sign
        [163] = '\u00A3', // pound sign
        [164] = '\u00A7', // section sign
        [165] = '\u2022', // bullet
        [166] = '\u00B6', // pilcrow sign
        [167] = '\u00DF', // sharp s
        [168] = '\u00AE', // registered sign
        [169] = '\u00A9', // copyright sign
        [170] = '\u2122', // trade mark sign
        [171] = '\u00B4', // acute accent
        [172] = '\u00A8', // diaeresis
        [174] = '\u00C6', // AE
        [175] = '\u00D8', // O with stroke
        [177] = '\u00B1', // plus-minus sign
        [180] = '\u00A5', // yen sign
        [181] = '\u00B5', // micro sign
        [187] = '\u00AA', // feminine ordinal indicator
        [188] = '\u00BA', // masculine ordinal indicator
        [190] = '\u00E6', // ae
        [191] = '\u00F8', // o with stroke
        [192] = '\u00BF', // inverted question mark
        [193] = '\u00A1', // inverted exclamation mark
        [194] = '\u00AC', // not sign
        [196] = '\u0192', // f with hook
        [199] = '\u00AB', // left-pointing double angle quotation mark
        [200] = '\u00BB', // right-pointing double angle quotation mark
        [201] = '\u2026', // horizontal ellipsis
        [202] = '\u00A0', // no-break space
        [203] = '\u00C0', // A with grave
        [204] = '\u00C3', // A with tilde
        [205] = '\u00D5', // O with tilde
        [206] = '\u0152', // OE ligature
        [207] = '\u0153', // oe ligature
        [208] = '\u2013', // en dash
        [209] = '\u2014', // em dash
        [210] = '\u201C', // left double quotation mark
        [211] = '\u201D', // right double quotation mark
        [212] = '\u2018', // left single quotation mark
        [213] = '\u2019', // right single quotation mark
        [214] = '\u00F7', // division sign
        [215] = '\u25CA', // lozenge
        [218] = '\u00FF', // y with diaeresis
        [219] = '\u0178', // Y with diaeresis
        [220] = '\u2044', // fraction slash
        [222] = '\uFB01', // fi ligature
        [223] = '\uFB02', // fl ligature
        [226] = '\u00AE', // registered sign (alt)
        [227] = '\u00A9', // copyright sign (alt)
        [228] = '\u2122', // trade mark sign (alt)
        [229] = '\u00B4', // acute accent (alt)
        [230] = '\u00A8', // diaeresis (alt)
        [232] = '\u00C8', // E with grave
        [233] = '\u00CA', // E with circumflex
        [234] = '\u00CB', // E with diaeresis
        [235] = '\u00CC', // I with grave
        [236] = '\u00CD', // I with acute
        [237] = '\u00CE', // I with circumflex
        [238] = '\u00CF', // I with diaeresis
        [241] = '\u00D2', // O with grave
        [242] = '\u00D3', // O with acute
        [243] = '\u00D4', // O with circumflex
        [245] = '\u00D2', // O with grave (alt)
        [246] = '\u00DA', // U with acute
        [247] = '\u00DB', // U with circumflex
        [248] = '\u00D9', // U with grave
        [249] = '\u0131', // dotless i
        [250] = '\u02C6', // modifier letter circumflex accent
        [251] = '\u02DC', // small tilde
        [252] = '\u00AF', // macron
        [253] = '\u02D8', // breve
        [254] = '\u02D9', // dot above
        [255] = '\u02DA', // ring above
    };

    // ────────────────────────────────────────────────────────────────────────
    // Adobe Glyph List (core subset) — glyph name to Unicode mapping
    // ────────────────────────────────────────────────────────────────────────
    internal static readonly Dictionary<string, string> GlyphNameToUnicode = new(StringComparer.Ordinal)
    {
        // ASCII printable characters
        ["space"] = "\u0020",
        ["exclam"] = "\u0021",
        ["quotedbl"] = "\u0022",
        ["numbersign"] = "\u0023",
        ["dollar"] = "\u0024",
        ["percent"] = "\u0025",
        ["ampersand"] = "\u0026",
        ["quotesingle"] = "\u0027",
        ["parenleft"] = "\u0028",
        ["parenright"] = "\u0029",
        ["asterisk"] = "\u002A",
        ["plus"] = "\u002B",
        ["comma"] = "\u002C",
        ["hyphen"] = "\u002D",
        ["period"] = "\u002E",
        ["slash"] = "\u002F",
        ["zero"] = "\u0030",
        ["one"] = "\u0031",
        ["two"] = "\u0032",
        ["three"] = "\u0033",
        ["four"] = "\u0034",
        ["five"] = "\u0035",
        ["six"] = "\u0036",
        ["seven"] = "\u0037",
        ["eight"] = "\u0038",
        ["nine"] = "\u0039",
        ["colon"] = "\u003A",
        ["semicolon"] = "\u003B",
        ["less"] = "\u003C",
        ["equal"] = "\u003D",
        ["greater"] = "\u003E",
        ["question"] = "\u003F",
        ["at"] = "\u0040",
        ["A"] = "\u0041",
        ["B"] = "\u0042",
        ["C"] = "\u0043",
        ["D"] = "\u0044",
        ["E"] = "\u0045",
        ["F"] = "\u0046",
        ["G"] = "\u0047",
        ["H"] = "\u0048",
        ["I"] = "\u0049",
        ["J"] = "\u004A",
        ["K"] = "\u004B",
        ["L"] = "\u004C",
        ["M"] = "\u004D",
        ["N"] = "\u004E",
        ["O"] = "\u004F",
        ["P"] = "\u0050",
        ["Q"] = "\u0051",
        ["R"] = "\u0052",
        ["S"] = "\u0053",
        ["T"] = "\u0054",
        ["U"] = "\u0055",
        ["V"] = "\u0056",
        ["W"] = "\u0057",
        ["X"] = "\u0058",
        ["Y"] = "\u0059",
        ["Z"] = "\u005A",
        ["bracketleft"] = "\u005B",
        ["backslash"] = "\u005C",
        ["bracketright"] = "\u005D",
        ["asciicircum"] = "\u005E",
        ["underscore"] = "\u005F",
        ["grave"] = "\u0060",
        ["a"] = "\u0061",
        ["b"] = "\u0062",
        ["c"] = "\u0063",
        ["d"] = "\u0064",
        ["e"] = "\u0065",
        ["f"] = "\u0066",
        ["g"] = "\u0067",
        ["h"] = "\u0068",
        ["i"] = "\u0069",
        ["j"] = "\u006A",
        ["k"] = "\u006B",
        ["l"] = "\u006C",
        ["m"] = "\u006D",
        ["n"] = "\u006E",
        ["o"] = "\u006F",
        ["p"] = "\u0070",
        ["q"] = "\u0071",
        ["r"] = "\u0072",
        ["s"] = "\u0073",
        ["t"] = "\u0074",
        ["u"] = "\u0075",
        ["v"] = "\u0076",
        ["w"] = "\u0077",
        ["x"] = "\u0078",
        ["y"] = "\u0079",
        ["z"] = "\u007A",
        ["braceleft"] = "\u007B",
        ["bar"] = "\u007C",
        ["braceright"] = "\u007D",
        ["asciitilde"] = "\u007E",

        // Common punctuation and symbols
        ["bullet"] = "\u2022",
        ["endash"] = "\u2013",
        ["emdash"] = "\u2014",
        ["quoteleft"] = "\u2018",
        ["quoteright"] = "\u2019",
        ["quotedblleft"] = "\u201C",
        ["quotedblright"] = "\u201D",
        ["quotesinglbase"] = "\u201A",
        ["quotedblbase"] = "\u201E",
        ["dagger"] = "\u2020",
        ["daggerdbl"] = "\u2021",
        ["ellipsis"] = "\u2026",
        ["perthousand"] = "\u2030",
        ["guilsinglleft"] = "\u2039",
        ["guilsinglright"] = "\u203A",
        ["trademark"] = "\u2122",
        ["minus"] = "\u2212",
        ["Euro"] = "\u20AC",

        // Latin-1 supplement
        ["exclamdown"] = "\u00A1",
        ["cent"] = "\u00A2",
        ["sterling"] = "\u00A3",
        ["currency"] = "\u00A4",
        ["yen"] = "\u00A5",
        ["brokenbar"] = "\u00A6",
        ["section"] = "\u00A7",
        ["dieresis"] = "\u00A8",
        ["copyright"] = "\u00A9",
        ["ordfeminine"] = "\u00AA",
        ["guillemotleft"] = "\u00AB",
        ["logicalnot"] = "\u00AC",
        ["registered"] = "\u00AE",
        ["macron"] = "\u00AF",
        ["degree"] = "\u00B0",
        ["plusminus"] = "\u00B1",
        ["twosuperior"] = "\u00B2",
        ["threesuperior"] = "\u00B3",
        ["acute"] = "\u00B4",
        ["mu"] = "\u00B5",
        ["paragraph"] = "\u00B6",
        ["periodcentered"] = "\u00B7",
        ["cedilla"] = "\u00B8",
        ["onesuperior"] = "\u00B9",
        ["ordmasculine"] = "\u00BA",
        ["guillemotright"] = "\u00BB",
        ["onequarter"] = "\u00BC",
        ["onehalf"] = "\u00BD",
        ["threequarters"] = "\u00BE",
        ["questiondown"] = "\u00BF",

        // Accented uppercase
        ["Agrave"] = "\u00C0",
        ["Aacute"] = "\u00C1",
        ["Acircumflex"] = "\u00C2",
        ["Atilde"] = "\u00C3",
        ["Adieresis"] = "\u00C4",
        ["Aring"] = "\u00C5",
        ["AE"] = "\u00C6",
        ["Ccedilla"] = "\u00C7",
        ["Egrave"] = "\u00C8",
        ["Eacute"] = "\u00C9",
        ["Ecircumflex"] = "\u00CA",
        ["Edieresis"] = "\u00CB",
        ["Igrave"] = "\u00CC",
        ["Iacute"] = "\u00CD",
        ["Icircumflex"] = "\u00CE",
        ["Idieresis"] = "\u00CF",
        ["Eth"] = "\u00D0",
        ["Ntilde"] = "\u00D1",
        ["Ograve"] = "\u00D2",
        ["Oacute"] = "\u00D3",
        ["Ocircumflex"] = "\u00D4",
        ["Otilde"] = "\u00D5",
        ["Odieresis"] = "\u00D6",
        ["multiply"] = "\u00D7",
        ["Oslash"] = "\u00D8",
        ["Ugrave"] = "\u00D9",
        ["Uacute"] = "\u00DA",
        ["Ucircumflex"] = "\u00DB",
        ["Udieresis"] = "\u00DC",
        ["Yacute"] = "\u00DD",
        ["Thorn"] = "\u00DE",
        ["germandbls"] = "\u00DF",

        // Accented lowercase
        ["agrave"] = "\u00E0",
        ["aacute"] = "\u00E1",
        ["acircumflex"] = "\u00E2",
        ["atilde"] = "\u00E3",
        ["adieresis"] = "\u00E4",
        ["aring"] = "\u00E5",
        ["ae"] = "\u00E6",
        ["ccedilla"] = "\u00E7",
        ["egrave"] = "\u00E8",
        ["eacute"] = "\u00E9",
        ["ecircumflex"] = "\u00EA",
        ["edieresis"] = "\u00EB",
        ["igrave"] = "\u00EC",
        ["iacute"] = "\u00ED",
        ["icircumflex"] = "\u00EE",
        ["idieresis"] = "\u00EF",
        ["eth"] = "\u00F0",
        ["ntilde"] = "\u00F1",
        ["ograve"] = "\u00F2",
        ["oacute"] = "\u00F3",
        ["ocircumflex"] = "\u00F4",
        ["otilde"] = "\u00F5",
        ["odieresis"] = "\u00F6",
        ["divide"] = "\u00F7",
        ["oslash"] = "\u00F8",
        ["ugrave"] = "\u00F9",
        ["uacute"] = "\u00FA",
        ["ucircumflex"] = "\u00FB",
        ["udieresis"] = "\u00FC",
        ["yacute"] = "\u00FD",
        ["thorn"] = "\u00FE",
        ["ydieresis"] = "\u00FF",

        // Latin Extended-A
        ["Amacron"] = "\u0100", ["amacron"] = "\u0101",
        ["Abreve"] = "\u0102", ["abreve"] = "\u0103",
        ["Aogonek"] = "\u0104", ["aogonek"] = "\u0105",
        ["Cacute"] = "\u0106", ["cacute"] = "\u0107",
        ["Ccircumflex"] = "\u0108", ["ccircumflex"] = "\u0109",
        ["Cdotaccent"] = "\u010A", ["cdotaccent"] = "\u010B",
        ["Ccaron"] = "\u010C", ["ccaron"] = "\u010D",
        ["Dcaron"] = "\u010E", ["dcaron"] = "\u010F",
        ["Dcroat"] = "\u0110", ["dcroat"] = "\u0111",
        ["Emacron"] = "\u0112", ["emacron"] = "\u0113",
        ["Ebreve"] = "\u0114", ["ebreve"] = "\u0115",
        ["Edotaccent"] = "\u0116", ["edotaccent"] = "\u0117",
        ["Eogonek"] = "\u0118", ["eogonek"] = "\u0119",
        ["Ecaron"] = "\u011A", ["ecaron"] = "\u011B",
        ["Gcircumflex"] = "\u011C", ["gcircumflex"] = "\u011D",
        ["Gbreve"] = "\u011E", ["gbreve"] = "\u011F",
        ["Gdotaccent"] = "\u0120", ["gdotaccent"] = "\u0121",
        ["Gcommaaccent"] = "\u0122", ["gcommaaccent"] = "\u0123",
        ["Hcircumflex"] = "\u0124", ["hcircumflex"] = "\u0125",
        ["Hbar"] = "\u0126", ["hbar"] = "\u0127",
        ["Itilde"] = "\u0128", ["itilde"] = "\u0129",
        ["Imacron"] = "\u012A", ["imacron"] = "\u012B",
        ["Ibreve"] = "\u012C", ["ibreve"] = "\u012D",
        ["Iogonek"] = "\u012E", ["iogonek"] = "\u012F",
        ["Idotaccent"] = "\u0130", ["dotlessi"] = "\u0131",
        ["IJ"] = "\u0132", ["ij"] = "\u0133",
        ["Jcircumflex"] = "\u0134", ["jcircumflex"] = "\u0135",
        ["Kcommaaccent"] = "\u0136", ["kcommaaccent"] = "\u0137",
        ["kgreenlandic"] = "\u0138",
        ["Lacute"] = "\u0139", ["lacute"] = "\u013A",
        ["Lcommaaccent"] = "\u013B", ["lcommaaccent"] = "\u013C",
        ["Lcaron"] = "\u013D", ["lcaron"] = "\u013E",
        ["Ldot"] = "\u013F", ["ldot"] = "\u0140",
        ["Lslash"] = "\u0141", ["lslash"] = "\u0142",
        ["Nacute"] = "\u0143", ["nacute"] = "\u0144",
        ["Ncommaaccent"] = "\u0145", ["ncommaaccent"] = "\u0146",
        ["Ncaron"] = "\u0147", ["ncaron"] = "\u0148",
        ["napostrophe"] = "\u0149",
        ["Eng"] = "\u014A", ["eng"] = "\u014B",
        ["Omacron"] = "\u014C", ["omacron"] = "\u014D",
        ["Obreve"] = "\u014E", ["obreve"] = "\u014F",
        ["Ohungarumlaut"] = "\u0150", ["ohungarumlaut"] = "\u0151",
        ["OE"] = "\u0152", ["oe"] = "\u0153",
        ["Racute"] = "\u0154", ["racute"] = "\u0155",
        ["Rcommaaccent"] = "\u0156", ["rcommaaccent"] = "\u0157",
        ["Rcaron"] = "\u0158", ["rcaron"] = "\u0159",
        ["Sacute"] = "\u015A", ["sacute"] = "\u015B",
        ["Scircumflex"] = "\u015C", ["scircumflex"] = "\u015D",
        ["Scedilla"] = "\u015E", ["scedilla"] = "\u015F",
        ["Scaron"] = "\u0160", ["scaron"] = "\u0161",
        ["Tcommaaccent"] = "\u0162", ["tcommaaccent"] = "\u0163",
        ["Tcaron"] = "\u0164", ["tcaron"] = "\u0165",
        ["Tbar"] = "\u0166", ["tbar"] = "\u0167",
        ["Utilde"] = "\u0168", ["utilde"] = "\u0169",
        ["Umacron"] = "\u016A", ["umacron"] = "\u016B",
        ["Ubreve"] = "\u016C", ["ubreve"] = "\u016D",
        ["Uring"] = "\u016E", ["uring"] = "\u016F",
        ["Uhungarumlaut"] = "\u0170", ["uhungarumlaut"] = "\u0171",
        ["Uogonek"] = "\u0172", ["uogonek"] = "\u0173",
        ["Wcircumflex"] = "\u0174", ["wcircumflex"] = "\u0175",
        ["Ycircumflex"] = "\u0176", ["ycircumflex"] = "\u0177",
        ["Ydieresis"] = "\u0178",
        ["Zacute"] = "\u0179", ["zacute"] = "\u017A",
        ["Zdotaccent"] = "\u017B", ["zdotaccent"] = "\u017C",
        ["Zcaron"] = "\u017D", ["zcaron"] = "\u017E",
        ["longs"] = "\u017F",

        // Latin Extended-B
        ["florin"] = "\u0192",
        ["Aringacute"] = "\u01FA", ["aringacute"] = "\u01FB",
        ["AEacute"] = "\u01FC", ["aeacute"] = "\u01FD",

        // Spacing Modifier Letters
        ["circumflex"] = "\u02C6", ["caron"] = "\u02C7",
        ["breve"] = "\u02D8", ["dotaccent"] = "\u02D9",
        ["ring"] = "\u02DA", ["ogonek"] = "\u02DB",
        ["tilde"] = "\u02DC", ["hungarumlaut"] = "\u02DD",

        // Greek
        ["Alpha"] = "\u0391", ["Beta"] = "\u0392", ["Gamma"] = "\u0393", ["Delta"] = "\u0394",
        ["Epsilon"] = "\u0395", ["Zeta"] = "\u0396", ["Eta"] = "\u0397", ["Theta"] = "\u0398",
        ["Iota"] = "\u0399", ["Kappa"] = "\u039A", ["Lambda"] = "\u039B", ["Mu"] = "\u039C",
        ["Nu"] = "\u039D", ["Xi"] = "\u039E", ["Omicron"] = "\u039F", ["Pi"] = "\u03A0",
        ["Rho"] = "\u03A1", ["Sigma"] = "\u03A3", ["Tau"] = "\u03A4", ["Upsilon"] = "\u03A5",
        ["Phi"] = "\u03A6", ["Chi"] = "\u03A7", ["Psi"] = "\u03A8", ["Omega"] = "\u03A9",
        ["alpha"] = "\u03B1", ["beta"] = "\u03B2", ["gamma"] = "\u03B3", ["delta"] = "\u03B4",
        ["epsilon"] = "\u03B5", ["zeta"] = "\u03B6", ["eta"] = "\u03B7", ["theta"] = "\u03B8",
        ["iota"] = "\u03B9", ["kappa"] = "\u03BA", ["lambda"] = "\u03BB",
        ["nu"] = "\u03BD", ["xi"] = "\u03BE", ["omicron"] = "\u03BF", ["pi"] = "\u03C0",
        ["rho"] = "\u03C1", ["sigma"] = "\u03C3", ["tau"] = "\u03C4", ["upsilon"] = "\u03C5",
        ["phi"] = "\u03C6", ["chi"] = "\u03C7", ["psi"] = "\u03C8", ["omega"] = "\u03C9",
        ["sigma1"] = "\u03C2", ["theta1"] = "\u03D1", ["Upsilon1"] = "\u03D2",
        ["phi1"] = "\u03D5", ["omega1"] = "\u03D6",
        // Greek with tonos / dialytika (modern Greek, AGL standard names)
        ["Alphatonos"] = "\u0386", ["Epsilontonos"] = "\u0388",
        ["Etatonos"] = "\u0389", ["Iotatonos"] = "\u038A",
        ["Omicrontonos"] = "\u038C", ["Upsilontonos"] = "\u038E",
        ["Omegatonos"] = "\u038F",
        ["Iotadieresis"] = "\u03AA", ["Upsilondieresis"] = "\u03AB",
        ["alphatonos"] = "\u03AC", ["epsilontonos"] = "\u03AD",
        ["etatonos"] = "\u03AE", ["iotatonos"] = "\u03AF",
        ["upsilondieresistonos"] = "\u03B0",
        ["iotadieresis"] = "\u03CA", ["upsilondieresis"] = "\u03CB",
        ["omicrontonos"] = "\u03CC", ["upsilontonos"] = "\u03CD",
        ["omegatonos"] = "\u03CE",
        ["iotadieresistonos"] = "\u0390",

        // Cyrillic (afii series)
        ["afii10017"] = "\u0410", ["afii10018"] = "\u0411", ["afii10019"] = "\u0412", ["afii10020"] = "\u0413",
        ["afii10021"] = "\u0414", ["afii10022"] = "\u0415", ["afii10023"] = "\u0401", ["afii10024"] = "\u0416",
        ["afii10025"] = "\u0417", ["afii10026"] = "\u0418", ["afii10027"] = "\u0419", ["afii10028"] = "\u041A",
        ["afii10029"] = "\u041B", ["afii10030"] = "\u041C", ["afii10031"] = "\u041D", ["afii10032"] = "\u041E",
        ["afii10033"] = "\u041F", ["afii10034"] = "\u0420", ["afii10035"] = "\u0421", ["afii10036"] = "\u0422",
        ["afii10037"] = "\u0423", ["afii10038"] = "\u0424", ["afii10039"] = "\u0425", ["afii10040"] = "\u0426",
        ["afii10041"] = "\u0427", ["afii10042"] = "\u0428", ["afii10043"] = "\u0429", ["afii10044"] = "\u042A",
        ["afii10045"] = "\u042B", ["afii10046"] = "\u042C", ["afii10047"] = "\u042D", ["afii10048"] = "\u042E",
        ["afii10049"] = "\u042F",
        ["afii10065"] = "\u0430", ["afii10066"] = "\u0431", ["afii10067"] = "\u0432", ["afii10068"] = "\u0433",
        ["afii10069"] = "\u0434", ["afii10070"] = "\u0435", ["afii10071"] = "\u0451", ["afii10072"] = "\u0436",
        ["afii10073"] = "\u0437", ["afii10074"] = "\u0438", ["afii10075"] = "\u0439", ["afii10076"] = "\u043A",
        ["afii10077"] = "\u043B", ["afii10078"] = "\u043C", ["afii10079"] = "\u043D", ["afii10080"] = "\u043E",
        ["afii10081"] = "\u043F", ["afii10082"] = "\u0440", ["afii10083"] = "\u0441", ["afii10084"] = "\u0442",
        ["afii10085"] = "\u0443", ["afii10086"] = "\u0444", ["afii10087"] = "\u0445", ["afii10088"] = "\u0446",
        ["afii10089"] = "\u0447", ["afii10090"] = "\u0448", ["afii10091"] = "\u0449", ["afii10092"] = "\u044A",
        ["afii10093"] = "\u044B", ["afii10094"] = "\u044C", ["afii10095"] = "\u044D", ["afii10096"] = "\u044E",
        ["afii10097"] = "\u044F",
        // Additional Cyrillic (Bulgarian, Serbian, Ukrainian)
        ["afii10050"] = "\u0490", ["afii10098"] = "\u0491",
        ["afii10051"] = "\u0402", ["afii10099"] = "\u0452",
        ["afii10052"] = "\u0403", ["afii10100"] = "\u0453",
        ["afii10053"] = "\u0404", ["afii10101"] = "\u0454",
        ["afii10054"] = "\u0405", ["afii10102"] = "\u0455",
        ["afii10055"] = "\u0406", ["afii10103"] = "\u0456",
        ["afii10056"] = "\u0407", ["afii10104"] = "\u0457",
        ["afii10057"] = "\u0408", ["afii10105"] = "\u0458",
        ["afii10058"] = "\u0409", ["afii10106"] = "\u0459",
        ["afii10059"] = "\u040A", ["afii10107"] = "\u045A",
        ["afii10060"] = "\u040B", ["afii10108"] = "\u045B",
        ["afii10061"] = "\u040C", ["afii10109"] = "\u045C",
        ["afii10062"] = "\u040E", ["afii10110"] = "\u045E",
        ["afii10145"] = "\u040F", ["afii10193"] = "\u045F",
        ["afii10146"] = "\u0462", ["afii10194"] = "\u0463",
        ["afii10147"] = "\u0472", ["afii10195"] = "\u0473",
        ["afii10148"] = "\u0474", ["afii10196"] = "\u0475",

        // General Punctuation & Typography
        ["afii00208"] = "\u2015",
        ["onedotenleader"] = "\u2024", ["twodotenleader"] = "\u2025",
        ["minute"] = "\u2032", ["second"] = "\u2033",
        ["sfthyphen"] = "\u00AD",

        // Mathematical \u2014 common operators and relations
        ["radical"] = "\u221A", ["infinity"] = "\u221E", ["integral"] = "\u222B",
        ["approxequal"] = "\u2248", ["notequal"] = "\u2260",
        ["lessequal"] = "\u2264", ["greaterequal"] = "\u2265",
        ["partialdiff"] = "\u2202", ["summation"] = "\u2211",
        ["product"] = "\u220F", ["lozenge"] = "\u25CA",
        ["middot"] = "\u00B7",
        // Set-theory and additional math (Adobe Glyph List standard names)
        ["universal"] = "\u2200", ["existential"] = "\u2203",
        ["element"] = "\u2208", ["notelement"] = "\u2209",
        ["suchthat"] = "\u220B",
        ["minus"] = "\u2212", ["plusminus"] = "\u00B1", ["multiply"] = "\u00D7", ["divide"] = "\u00F7",
        ["asteriskmath"] = "\u2217", ["proportional"] = "\u221D",
        ["angle"] = "\u2220", ["logicaland"] = "\u2227", ["logicalor"] = "\u2228",
        ["intersection"] = "\u2229", ["union"] = "\u222A",
        ["therefore"] = "\u2234", ["similar"] = "\u223C",
        ["congruent"] = "\u2245", ["equivalence"] = "\u2261",
        ["propersubset"] = "\u2282", ["propersuperset"] = "\u2283",
        ["notsubset"] = "\u2284",
        ["reflexsubset"] = "\u2286", ["reflexsuperset"] = "\u2287",
        ["perpendicular"] = "\u22A5",
        ["dotmath"] = "\u22C5", ["bullet"] = "\u2022",
        // Arrows (single)
        ["arrowleft"] = "\u2190", ["arrowup"] = "\u2191",
        ["arrowright"] = "\u2192", ["arrowdown"] = "\u2193",
        ["arrowboth"] = "\u2194", ["arrowupdn"] = "\u2195",
        ["arrowupdnbse"] = "\u21A8",
        ["carriagereturn"] = "\u21B5",
        // Arrows (double)
        ["arrowdblleft"] = "\u21D0", ["arrowdblup"] = "\u21D1",
        ["arrowdblright"] = "\u21D2", ["arrowdbldown"] = "\u21D3",
        ["arrowdblboth"] = "\u21D4",

        // Currency
        ["euro"] = "\u20AC", ["afii08941"] = "\u20AC",

        // Ligatures
        ["fi"] = "\uFB01", ["fl"] = "\uFB02",
        ["ff"] = "\uFB00", ["ffi"] = "\uFB03", ["ffl"] = "\uFB04",

        // Letterlike Symbols
        ["afii61664"] = "\u200B", ["afii301"] = "\u200E", ["afii299"] = "\u200F",
        ["numero"] = "\u2116", ["estimated"] = "\u212E",

        // Box drawing / Geometric
        ["square"] = "\u25A1", ["triagup"] = "\u25B2", ["triagrt"] = "\u25BA",
        ["triagdn"] = "\u25BC", ["triaglf"] = "\u25C4",

        // Dingbats (common)
        ["a1"] = "\u2701", ["a2"] = "\u2702", ["a3"] = "\u2703", ["a4"] = "\u2704",
        ["a5"] = "\u260E", ["a6"] = "\u2706", ["a7"] = "\u2707", ["a8"] = "\u2708",
        ["a9"] = "\u2709", ["a10"] = "\u261B", ["a11"] = "\u261E",

        // Miscellaneous
        ["notdef"] = "\uFFFD", [".notdef"] = "\uFFFD",
        ["null"] = "\u0000", ["CR"] = "\u000D",

        // Additional common glyphs
        ["nbspace"] = "\u00A0", ["nonbreakingspace"] = "\u00A0",
        ["softhyphen"] = "\u00AD",
        ["fraction"] = "\u2044",
    };

    // ────────────────────────────────────────────────────────────────────────
    // Symbol font encoding — full 189-entry table
    // Source: Adobe Symbol font mapping, PDF32000_2008 §D.5
    // ────────────────────────────────────────────────────────────────────────
    private static readonly Dictionary<byte, char> SymbolEncoding = new()
    {
        // 0x20-0x3F: spacing, operators, digits
        [0x20] = '\u0020', // space
        [0x21] = '\u0021', // exclam
        [0x22] = '\u2200', // universal
        [0x23] = '\u0023', // numbersign
        [0x24] = '\u2203', // existential
        [0x25] = '\u0025', // percent
        [0x26] = '\u0026', // ampersand
        [0x27] = '\u220B', // suchthat
        [0x28] = '\u0028', // parenleft
        [0x29] = '\u0029', // parenright
        [0x2A] = '\u2217', // asteriskmath
        [0x2B] = '\u002B', // plus
        [0x2C] = '\u002C', // comma
        [0x2D] = '\u2212', // minus
        [0x2E] = '\u002E', // period
        [0x2F] = '\u002F', // slash
        [0x30] = '\u0030', [0x31] = '\u0031', [0x32] = '\u0032', [0x33] = '\u0033',
        [0x34] = '\u0034', [0x35] = '\u0035', [0x36] = '\u0036', [0x37] = '\u0037',
        [0x38] = '\u0038', [0x39] = '\u0039', // 0-9
        [0x3A] = '\u003A', // colon
        [0x3B] = '\u003B', // semicolon
        [0x3C] = '\u003C', // less
        [0x3D] = '\u003D', // equal
        [0x3E] = '\u003E', // greater
        [0x3F] = '\u003F', // question
        // 0x40: congruent
        [0x40] = '\u2245', // congruent
        // 0x41-0x5A: Greek uppercase
        [0x41] = '\u0391', // Alpha
        [0x42] = '\u0392', // Beta
        [0x43] = '\u03A7', // Chi
        [0x44] = '\u0394', // Delta
        [0x45] = '\u0395', // Epsilon
        [0x46] = '\u03A6', // Phi
        [0x47] = '\u0393', // Gamma
        [0x48] = '\u0397', // Eta
        [0x49] = '\u0399', // Iota
        [0x4A] = '\u03D1', // theta1
        [0x4B] = '\u039A', // Kappa
        [0x4C] = '\u039B', // Lambda
        [0x4D] = '\u039C', // Mu
        [0x4E] = '\u039D', // Nu
        [0x4F] = '\u039F', // Omicron
        [0x50] = '\u03A0', // Pi
        [0x51] = '\u0398', // Theta
        [0x52] = '\u03A1', // Rho
        [0x53] = '\u03A3', // Sigma
        [0x54] = '\u03A4', // Tau
        [0x55] = '\u03A5', // Upsilon
        [0x56] = '\u03C2', // sigma1
        [0x57] = '\u03A9', // Omega
        [0x58] = '\u039E', // Xi
        [0x59] = '\u03A8', // Psi
        [0x5A] = '\u0396', // Zeta
        [0x5B] = '\u005B', // bracketleft
        [0x5C] = '\u2234', // therefore
        [0x5D] = '\u005D', // bracketright
        [0x5E] = '\u22A5', // perpendicular
        [0x5F] = '\u005F', // underscore
        [0x60] = '\uF8E5', // radicalex (PUA)
        // 0x61-0x7A: Greek lowercase
        [0x61] = '\u03B1', // alpha
        [0x62] = '\u03B2', // beta
        [0x63] = '\u03C7', // chi
        [0x64] = '\u03B4', // delta
        [0x65] = '\u03B5', // epsilon
        [0x66] = '\u03C6', // phi
        [0x67] = '\u03B3', // gamma
        [0x68] = '\u03B7', // eta
        [0x69] = '\u03B9', // iota
        [0x6A] = '\u03D5', // phi1
        [0x6B] = '\u03BA', // kappa
        [0x6C] = '\u03BB', // lambda
        [0x6D] = '\u03BC', // mu
        [0x6E] = '\u03BD', // nu
        [0x6F] = '\u03BF', // omicron
        [0x70] = '\u03C0', // pi
        [0x71] = '\u03B8', // theta
        [0x72] = '\u03C1', // rho
        [0x73] = '\u03C3', // sigma
        [0x74] = '\u03C4', // tau
        [0x75] = '\u03C5', // upsilon
        [0x76] = '\u03D6', // omega1
        [0x77] = '\u03C9', // omega
        [0x78] = '\u03BE', // xi
        [0x79] = '\u03C8', // psi
        [0x7A] = '\u03B6', // zeta
        [0x7B] = '\u007B', // braceleft
        [0x7C] = '\u007C', // bar
        [0x7D] = '\u007D', // braceright
        [0x7E] = '\u223C', // similar
        // 0xA0-0xFE: extended symbols
        [0xA0] = '\u20AC', // Euro
        [0xA1] = '\u03D2', // Upsilon1
        [0xA2] = '\u2032', // prime
        [0xA3] = '\u2264', // lessequal
        [0xA4] = '\u2044', // fraction
        [0xA5] = '\u221E', // infinity
        [0xA6] = '\u0192', // florin
        [0xA7] = '\u2663', // club
        [0xA8] = '\u2666', // diamond
        [0xA9] = '\u2665', // heart
        [0xAA] = '\u2660', // spade
        [0xAB] = '\u2194', // arrowboth
        [0xAC] = '\u2190', // arrowleft
        [0xAD] = '\u2191', // arrowup
        [0xAE] = '\u2192', // arrowright
        [0xAF] = '\u2193', // arrowdown
        [0xB0] = '\u00B0', // degree
        [0xB1] = '\u00B1', // plusminus
        [0xB2] = '\u2033', // second
        [0xB3] = '\u2265', // greaterequal
        [0xB4] = '\u00D7', // multiply
        [0xB5] = '\u221D', // proportional
        [0xB6] = '\u2202', // partialdiff
        [0xB7] = '\u2022', // bullet
        [0xB8] = '\u00F7', // divide
        [0xB9] = '\u2260', // notequal
        [0xBA] = '\u2261', // equivalence
        [0xBB] = '\u2248', // approxequal
        [0xBC] = '\u2026', // ellipsis
        [0xBD] = '\uF8E6', // arrowvertex (PUA)
        [0xBE] = '\uF8E7', // arrowhorizex (PUA)
        [0xBF] = '\u21B5', // carriagereturn
        [0xC0] = '\u2135', // aleph
        [0xC1] = '\u2111', // Ifraktur
        [0xC2] = '\u211C', // Rfraktur
        [0xC3] = '\u2118', // weierstrass
        [0xC4] = '\u2297', // circlemultiply
        [0xC5] = '\u2295', // circleplus
        [0xC6] = '\u2205', // emptyset
        [0xC7] = '\u2229', // intersection
        [0xC8] = '\u222A', // union
        [0xC9] = '\u2283', // propersuperset
        [0xCA] = '\u2287', // reflexsuperset
        [0xCB] = '\u2284', // notsubset
        [0xCC] = '\u2282', // propersubset
        [0xCD] = '\u2286', // reflexsubset
        [0xCE] = '\u2208', // element
        [0xCF] = '\u2209', // notelement
        [0xD0] = '\u2220', // angle
        [0xD1] = '\u2207', // gradient
        [0xD2] = '\uF6DA', // registerserif (PUA)
        [0xD3] = '\uF6D9', // copyrightserif (PUA)
        [0xD4] = '\uF6DB', // trademarkserif (PUA)
        [0xD5] = '\u220F', // product
        [0xD6] = '\u221A', // radical
        [0xD7] = '\u22C5', // dotmath
        [0xD8] = '\u00AC', // logicalnot
        [0xD9] = '\u2227', // logicaland
        [0xDA] = '\u2228', // logicalor
        [0xDB] = '\u21D4', // arrowdblboth
        [0xDC] = '\u21D0', // arrowdblleft
        [0xDD] = '\u21D1', // arrowdblup
        [0xDE] = '\u21D2', // arrowdblright
        [0xDF] = '\u21D3', // arrowdbldown
        [0xE0] = '\u25CA', // lozenge
        [0xE1] = '\u2329', // angleleft
        [0xE2] = '\uF8E8', // registersans (PUA)
        [0xE3] = '\uF8E9', // copyrightsans (PUA)
        [0xE4] = '\uF8EA', // trademarksans (PUA)
        [0xE5] = '\u2211', // summation
        [0xE6] = '\uF8EB', // parenlefttp (PUA)
        [0xE7] = '\uF8EC', // parenleftex (PUA)
        [0xE8] = '\uF8ED', // parenleftbt (PUA)
        [0xE9] = '\uF8EE', // bracketlefttp (PUA)
        [0xEA] = '\uF8EF', // bracketleftex (PUA)
        [0xEB] = '\uF8F0', // bracketleftbt (PUA)
        [0xEC] = '\uF8F1', // bracelefttp (PUA)
        [0xED] = '\uF8F2', // braceleftmid (PUA)
        [0xEE] = '\uF8F3', // braceleftbt (PUA)
        [0xEF] = '\uF8F4', // braceex (PUA)
        [0xF1] = '\u232A', // angleright
        [0xF2] = '\u222B', // integral
        [0xF3] = '\u2320', // integraltp
        [0xF4] = '\uF8F5', // integralex (PUA)
        [0xF5] = '\u2321', // integralbt
        [0xF6] = '\uF8F6', // parenrighttp (PUA)
        [0xF7] = '\uF8F7', // parenrightex (PUA)
        [0xF8] = '\uF8F8', // parenrightbt (PUA)
        [0xF9] = '\uF8F9', // bracketrighttp (PUA)
        [0xFA] = '\uF8FA', // bracketrightex (PUA)
        [0xFB] = '\uF8FB', // bracketrightbt (PUA)
        [0xFC] = '\uF8FC', // bracerighttp (PUA)
        [0xFD] = '\uF8FD', // bracerightmid (PUA)
        [0xFE] = '\uF8FE', // bracerightbt (PUA)
    };

    // ────────────────────────────────────────────────────────────────────────
    // ZapfDingbats font encoding — full 202-entry table
    // Source: Adobe ZapfDingbats font mapping, PDF32000_2008 §D.6
    // ────────────────────────────────────────────────────────────────────────
    private static readonly Dictionary<byte, char> ZapfDingbatsEncoding = new()
    {
        [0x20] = '\u0020', // space
        [0x21] = '\u2701', [0x22] = '\u2702', [0x23] = '\u2703', [0x24] = '\u2704',
        [0x25] = '\u260E', [0x26] = '\u2706', [0x27] = '\u2707', [0x28] = '\u2708',
        [0x29] = '\u2709', [0x2A] = '\u261B', [0x2B] = '\u261E', [0x2C] = '\u270C',
        [0x2D] = '\u270D', [0x2E] = '\u270E', [0x2F] = '\u270F',
        [0x30] = '\u2710', [0x31] = '\u2711', [0x32] = '\u2712', [0x33] = '\u2713',
        [0x34] = '\u2714', [0x35] = '\u2715', [0x36] = '\u2716', [0x37] = '\u2717',
        [0x38] = '\u2718', [0x39] = '\u2719', [0x3A] = '\u271A', [0x3B] = '\u271B',
        [0x3C] = '\u271C', [0x3D] = '\u271D', [0x3E] = '\u271E', [0x3F] = '\u271F',
        [0x40] = '\u2720', [0x41] = '\u2721', [0x42] = '\u2722', [0x43] = '\u2723',
        [0x44] = '\u2724', [0x45] = '\u2725', [0x46] = '\u2726', [0x47] = '\u2727',
        [0x48] = '\u2605', [0x49] = '\u2729', [0x4A] = '\u272A', [0x4B] = '\u272B',
        [0x4C] = '\u272C', [0x4D] = '\u272D', [0x4E] = '\u272E', [0x4F] = '\u272F',
        [0x50] = '\u2730', [0x51] = '\u2731', [0x52] = '\u2732', [0x53] = '\u2733',
        [0x54] = '\u2734', [0x55] = '\u2735', [0x56] = '\u2736', [0x57] = '\u2737',
        [0x58] = '\u2738', [0x59] = '\u2739', [0x5A] = '\u273A', [0x5B] = '\u273B',
        [0x5C] = '\u273C', [0x5D] = '\u273D', [0x5E] = '\u273E', [0x5F] = '\u273F',
        [0x60] = '\u2740', [0x61] = '\u2741', [0x62] = '\u2742', [0x63] = '\u2743',
        [0x64] = '\u2744', [0x65] = '\u2745', [0x66] = '\u2746', [0x67] = '\u2747',
        [0x68] = '\u2748', [0x69] = '\u2749', [0x6A] = '\u274A', [0x6B] = '\u274B',
        [0x6C] = '\u25CF', [0x6D] = '\u274D', [0x6E] = '\u25A0', [0x6F] = '\u274F',
        [0x70] = '\u2750', [0x71] = '\u2751', [0x72] = '\u2752', [0x73] = '\u25B2',
        [0x74] = '\u25BC', [0x75] = '\u25C6', [0x76] = '\u2756', [0x77] = '\u25D7',
        [0x78] = '\u2758', [0x79] = '\u2759', [0x7A] = '\u275A', [0x7B] = '\u275B',
        [0x7C] = '\u275C', [0x7D] = '\u275D', [0x7E] = '\u275E',
        [0x80] = '\u2768', [0x81] = '\u2769', [0x82] = '\u276A', [0x83] = '\u276B',
        [0x84] = '\u276C', [0x85] = '\u276D', [0x86] = '\u276E', [0x87] = '\u276F',
        [0x88] = '\u2770', [0x89] = '\u2771', [0x8A] = '\u2772', [0x8B] = '\u2773',
        [0x8C] = '\u2774', [0x8D] = '\u2775',
        [0xA1] = '\u2761', [0xA2] = '\u2762', [0xA3] = '\u2763', [0xA4] = '\u2764',
        [0xA5] = '\u2765', [0xA6] = '\u2766', [0xA7] = '\u2767',
        [0xA8] = '\u2663', [0xA9] = '\u2666', [0xAA] = '\u2665', [0xAB] = '\u2660',
        [0xAC] = '\u2460', [0xAD] = '\u2461', [0xAE] = '\u2462', [0xAF] = '\u2463',
        [0xB0] = '\u2464', [0xB1] = '\u2465', [0xB2] = '\u2466', [0xB3] = '\u2467',
        [0xB4] = '\u2468', [0xB5] = '\u2469',
        [0xB6] = '\u2776', [0xB7] = '\u2777', [0xB8] = '\u2778', [0xB9] = '\u2779',
        [0xBA] = '\u277A', [0xBB] = '\u277B', [0xBC] = '\u277C', [0xBD] = '\u277D',
        [0xBE] = '\u277E', [0xBF] = '\u277F',
        [0xC0] = '\u2780', [0xC1] = '\u2781', [0xC2] = '\u2782', [0xC3] = '\u2783',
        [0xC4] = '\u2784', [0xC5] = '\u2785', [0xC6] = '\u2786', [0xC7] = '\u2787',
        [0xC8] = '\u2788', [0xC9] = '\u2789',
        [0xCA] = '\u278A', [0xCB] = '\u278B', [0xCC] = '\u278C', [0xCD] = '\u278D',
        [0xCE] = '\u278E', [0xCF] = '\u278F',
        [0xD0] = '\u2790', [0xD1] = '\u2791', [0xD2] = '\u2792', [0xD3] = '\u2793',
        [0xD4] = '\u2794', [0xD5] = '\u2192', [0xD6] = '\u2194', [0xD7] = '\u2195',
        [0xD8] = '\u2798', [0xD9] = '\u2799', [0xDA] = '\u279A', [0xDB] = '\u279B',
        [0xDC] = '\u279C', [0xDD] = '\u279D', [0xDE] = '\u279E', [0xDF] = '\u279F',
        [0xE0] = '\u27A0', [0xE1] = '\u27A1', [0xE2] = '\u27A2', [0xE3] = '\u27A3',
        [0xE4] = '\u27A4', [0xE5] = '\u27A5', [0xE6] = '\u27A6', [0xE7] = '\u27A7',
        [0xE8] = '\u27A8', [0xE9] = '\u27A9', [0xEA] = '\u27AA', [0xEB] = '\u27AB',
        [0xEC] = '\u27AC', [0xED] = '\u27AD', [0xEE] = '\u27AE', [0xEF] = '\u27AF',
        [0xF1] = '\u27B1', [0xF2] = '\u27B2', [0xF3] = '\u27B3', [0xF4] = '\u27B4',
        [0xF5] = '\u27B5', [0xF6] = '\u27B6', [0xF7] = '\u27B7', [0xF8] = '\u27B8',
        [0xF9] = '\u27B9', [0xFA] = '\u27BA', [0xFB] = '\u27BB', [0xFC] = '\u27BC',
        [0xFD] = '\u27BD', [0xFE] = '\u27BE',
    };
}
