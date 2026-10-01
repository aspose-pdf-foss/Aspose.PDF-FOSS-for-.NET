using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
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
        _currentLineCmTy = 0;
        _currentLineEffFs = double.NaN;
        _currentLineIsRotated = false;
        _currentLineDescent = 0.2;
        _currentLineDevY = double.NaN;
        _currentLineRowX = double.NaN;
        _rowXLineOffset = -1;
        _effectiveSearchRect = null; // form streams are not page-rotated
        // No TrimTrailingLineSpaces pass runs on a standalone form visit, so a
        // masked space would never be restored — keep masking off here.
        _maskEolShowSpaces = false;

        ExtractTextFromContentStream(streamBytes, dict, reader);
        SortLinesByY(textStart, yStart);
    }

    /// <summary>Extracts the text of every page of the document into <c>Text</c>, replacing any earlier result; pages are joined with line breaks.</summary>
    public void Visit(Document pdf)
    {
        var pageTexts = new List<string>();
        var isPure = ExtractionOptions?.FormattingMode
            != TextExtractionOptions.TextFormattingMode.Raw;
        foreach (var page in pdf.Pages)
        {
            _text.Clear();
            _lineYPositions.Clear();
        _lineXPositions.Clear();
        _lineFontSizes.Clear();
        _lineIsRotated.Clear();
        _lineDescents.Clear();
            Visit(page);
            var pageText = _text.ToString().Trim('\r', '\n');
            // Pure mode: pad each line to a consistent width so column
            // layout is preserved visually. Pure mode
            // does this to maintain fixed-width COLUMN alignment — so only pad
            // when this page actually shows column structure (some line needed
            // inter-run gap spaces). A single-column page (one run per line,
            // e.g. plain paragraphs) is NOT padded; blanket-padding appended
            // dozens of trailing spaces to every short line.
            if (pageText.Length > 0 && isPure && _sawIntraLineGapSpaces)
                pageText = PadLinesToFixedWidth(pageText);
            // A text-less page (e.g. image only) still contributes its empty entry,
            // so the whole-document join keeps a page separator for it — such
            // a page shows as a blank line between its neighbours.
            pageTexts.Add(pageText);
        }
        // Trailing text-less pages don't add dangling separators.
        while (pageTexts.Count > 0 && pageTexts[^1].Length == 0)
            pageTexts.RemoveAt(pageTexts.Count - 1);
        _text.Clear();
        _text.Append(string.Join("\r\n", pageTexts));
        if (pageTexts.Count > 0)
            _text.Append("\r\n");
    }

    /// <summary>
    /// Pad each line with trailing spaces to a fixed width (~80 chars).
    /// In Pure mode column layouts produce
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
        _lineXPositions.Clear();
        _lineFontSizes.Clear();
        _lineIsRotated.Clear();
        _lineDescents.Clear();
        _currentLineY = double.NaN;
        _currentLineCmTy = 0;
        _currentLineEffFs = double.NaN;
        _currentLineIsRotated = false;
        _currentLineDescent = 0.2;
        _currentLineDevY = double.NaN;
        _currentLineRowX = double.NaN;
        _rowXLineOffset = -1;
    }

    /// <summary>Join a page's content streams into one buffer with newline
    /// separators, per the spec's single-logical-stream model.</summary>
    private static byte[] CombineContentStreams(List<byte[]> streams)
    {
        if (streams.Count == 1) return streams[0];
        var total = 0;
        foreach (var s in streams) total += s.Length + 1;
        var buf = new byte[total];
        var pos = 0;
        foreach (var s in streams)
        {
            Array.Copy(s, 0, buf, pos, s.Length);
            pos += s.Length;
            buf[pos++] = (byte)'\n';
        }
        return buf;
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

    /// <summary>Pre-scan companion to the grid: the cell width plus the page's
    /// leftmost text X (grid origin). MinX tracks Tm/Td/cm X translation the
    /// same way the extraction loop does (scale-free approximation).</summary>
    private static (double cell, double cellCeil, double minX, double domFs, bool rotDominant) EstimatePageGrid(List<byte[]> streams, PdfDictionary pageDict, PdfReader reader, double scaleFactor = 1.0, double[]? bounds = null)
    {
        var pg = new PageGridState();
        pg.sumW = 0; pg.cnt = 0;
        pg.rotChars = 0;
        pg.uprightChars = 0;
        pg.rawBySize = new Dictionary<double, double>();
        pg.widthPerSize = new Dictionary<double, double>();
        pg.pureWidthPerSize = new Dictionary<double, double>();
        pg.pureCharsPerSize = new Dictionary<double, int>();
        pg.avgWidthPerSize = new Dictionary<double, double>();
        pg.avgCharsPerSize = new Dictionary<double, int>();
        pg.minX = double.NaN;
        pg.minXAny = double.NaN;
        pg.charsPerSize = new Dictionary<double, int>();
        pg.pageFonts = ResolveFonts(pageDict, reader);

        // First pass: the page's own content streams only (unchanged behaviour).
        foreach (var streamBytes in streams)
            Scan(pg, reader, bounds, streamBytes, pg.pageFonts, pageDict, 1, 0, 0, 1, 0, 0, 0, recurse: false);

        // Rescue pass: a page whose direct stream carries almost no text draws it
        // through Form XObjects. Re-measure descending into those forms so the grid
        // gets sized (otherwise cell = 0 and Pure spacing falls back to the coarser
        // gap heuristic). Only mixed-content pages with real direct text (cnt >= 8)
        // keep the original, calibration-preserving estimate.
        if (pg.cnt < 8)
        {
            pg.sumW = 0; pg.cnt = 0; pg.rotChars = 0; pg.uprightChars = 0; pg.minX = double.NaN; pg.minXAny = double.NaN;
            pg.rawBySize.Clear(); pg.widthPerSize.Clear(); pg.pureWidthPerSize.Clear();
            pg.pureCharsPerSize.Clear(); pg.avgWidthPerSize.Clear(); pg.avgCharsPerSize.Clear();
            pg.charsPerSize.Clear();
            foreach (var streamBytes in streams)
                Scan(pg, reader, bounds, streamBytes, pg.pageFonts, pageDict, 1, 0, 0, 1, 0, 0, 0, recurse: true);
        }

        pg.rotDom = pg.rotChars > pg.uprightChars;
        pg.gridMinX = pg.rotDom ? pg.minXAny : pg.minX;
        if (pg.cnt < 8) return (0, 0, pg.gridMinX, 0, pg.rotDom);

        pg.domSize = 0; var domCount = -1;
        foreach (var kv in pg.charsPerSize)
        {
            if (kv.Value > domCount || (kv.Value == domCount && kv.Key < pg.domSize))
            {
                pg.domSize = kv.Key; domCount = kv.Value;
            }
        }
        // Calibrated rule (22 controlled trials): the grid cell
        // is scaleFactor · 0.6·(F−2) — F = the ceiled-size bucket holding the
        // most characters (sizes CEIL to integer buckets BEFORE the counts
        // aggregate: an 8.04pt report grids at the 9-bucket cell 4.2). There
        // is NO mean-advance branch on this path. Only the explicit AUTO mode
        // (ScaleFactor = 0) sets the cell to the page's capped mean glyph
        // advance: kern-inclusive run widths (backward jumps excluded), Tz/Tc
        // applied, drawn spaces included, adjacency-aware synthesized spaces,
        // per-run cap 0.6·fsTrue — measured over the dominant bucket only.
        // Blank-row thresholds still key on the RAW dominant size (line
        // heights are untransformed).
        if (pg.domSize > 2.5)
        {
            var rawDom = pg.rawBySize.TryGetValue(pg.domSize, out var rv) ? rv : pg.domSize;
            var ac = pg.avgCharsPerSize.GetValueOrDefault(pg.domSize);
            var aw = pg.avgWidthPerSize.GetValueOrDefault(pg.domSize);
            var sf = scaleFactor > 0 ? scaleFactor : 1.0;
            var cell = sf * 0.6 * (pg.domSize - 2);
            if (scaleFactor == 0 && ac > 0) cell = aw / ac;
            if (GridDebug)
            {
                var dc = pg.charsPerSize.GetValueOrDefault(pg.domSize);
                var dw = pg.widthPerSize.GetValueOrDefault(pg.domSize);
                var pc = pg.pureCharsPerSize.GetValueOrDefault(pg.domSize);
                var pw = pg.pureWidthPerSize.GetValueOrDefault(pg.domSize);
                Console.Error.WriteLine($"[cell] dom={pg.domSize} raw={rawDom:F2} chars={dc} width={dw:F1} "
                    + $"avg={(dc > 0 ? dw / dc : 0):F3} pureAvg={(pc > 0 ? pw / pc : 0):F3} "
                    + $"nsAvg={(ac > 0 ? aw / ac : 0):F3} cell={cell:F3} "
                    + $"legacy={0.6 * (rawDom - 2):F3} legacyCeil={0.6 * (pg.domSize - 2):F3}");
                foreach (var kv in pg.charsPerSize)
                    Console.Error.WriteLine($"[cell]   bucket={kv.Key} chars={kv.Value} width={pg.widthPerSize.GetValueOrDefault(kv.Key):F1} avg={(kv.Value > 0 ? pg.widthPerSize.GetValueOrDefault(kv.Key) / kv.Value : 0):F3} pureAvg={(pg.pureCharsPerSize.GetValueOrDefault(kv.Key) > 0 ? pg.pureWidthPerSize.GetValueOrDefault(kv.Key) / pg.pureCharsPerSize.GetValueOrDefault(kv.Key) : 0):F3}");
            }
            return (cell, sf * 0.6 * (pg.domSize - 2), pg.gridMinX, rawDom, pg.rotDom);
        }
        return (pg.sumW / pg.cnt, pg.sumW / pg.cnt, pg.gridMinX, 0, pg.rotDom);
    }

    /// <summary>Approximate glyph count from a show-string's byte length: 2-byte codes for a
    /// composite (CID/Identity-H) font, one byte per glyph otherwise. Keeps the mean-advance
    /// estimate from halving the cell width on CID pages.</summary>
    private static int GlyphCount(int byteLen, FontMetrics metrics)
        => metrics.IsCid ? (byteLen + 1) / 2 : byteLen;

    /// <summary>Count and measure the drawn SPACE glyphs of a show string (simple fonts:
    /// byte 0x20; composite fonts are left alone — their space CID isn't identifiable
    /// without decoding). The mean-advance cell population excludes them.</summary>
    private static (int count, double width) DrawnSpaces(
        byte[] bytes, FontMetrics metrics, double fsAdv, double horizScale)
    {
        if (metrics.IsCid) return (0, 0);
        var n = 0;
        foreach (var b in bytes)
            if (b == 0x20) n++;
        if (n == 0) return (0, 0);
        return (n, n * metrics.GetWidth(0x20) * fsAdv / 1000.0 * horizScale);
    }
}
