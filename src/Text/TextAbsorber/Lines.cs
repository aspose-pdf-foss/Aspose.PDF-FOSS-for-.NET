using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
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
        double sumW = 0; int cnt = 0;
        int rotChars = 0, uprightChars = 0;
        var rawBySize = new Dictionary<double, double>();
        var widthPerSize = new Dictionary<double, double>();
        // Pure glyph advances (kern adjustments excluded, synthesized spaces
        // not counted): diagnostic population, kept separate from the
        // kern-inclusive sums the gap heuristics were calibrated on.
        var pureWidthPerSize = new Dictionary<double, double>();
        var pureCharsPerSize = new Dictionary<double, int>();
        // The mean-advance population the cell rule averages:
        // kern-inclusive run widths WITHOUT the drawn space glyphs (their
        // advances and counts both come out), synthesized adjustment spaces
        // counted, per-run 0.6 em cap. Calibrated on three-way evidence: a
        // kern-gap French daily needs the kerns counted, a resume with drawn
        // spaces needs them excluded, a rotated CID report needs the formula
        // term to win the min().
        var avgWidthPerSize = new Dictionary<double, double>();
        var avgCharsPerSize = new Dictionary<double, int>();
        var minX = double.NaN;
        var minXAny = double.NaN;
        var charsPerSize = new Dictionary<double, int>();
        var pageFonts = ResolveFonts(pageDict, reader);

        // Scan one content stream, accumulating glyph advances and font-size
        // populations into the shared maps above. `recurse` gates descent into
        // Form XObjects (Do) so the extra measurement only kicks in for pages
        // whose direct content stream carries too little text — see the
        // two-pass invocation after the definition. `fonts`/`resDict` are the
        // font and resource dictionaries in scope for THIS stream (a form
        // supplies its own), and icm* is the CTM in effect at the stream's
        // start (identity for page content, the CTM at the Do for a form —
        // the form's own /Matrix is ignored, matching the extraction loop).
        void Scan(byte[] streamBytes, Dictionary<string, PdfDictionary> fonts,
            PdfDictionary resDict, double icmA, double icmB, double icmC,
            double icmD, double icmE, double icmF, int rdepth, bool recurse)
        {
            var lexer = new PdfLexer(streamBytes);
            var operands = new List<PdfObject>();
            FontMetrics? metrics = null; double fontSize = 12;
            double tlmX = 0, cmTx = icmE;
            // Baseline Y (device, approximate) and leading — only consulted for
            // the bounds check, mirroring the X tracking's level of fidelity.
            double tlmY = icmF, preTL = 0;
            // Rotated mirror of the extraction loop: for sideways text (rotation in
            // the Tm or in the CTM) the reading-axis X is the composed origin
            // projected on (a,b), Td advances scale by |(a,b)|, and the dominant
            // font size counts in DEVICE units — otherwise the pre-scan minX/cell
            // disagree with the runtime grid coordinates.
            double tmScaleX = 1.0;
            double fsScale = 1.0;
            // Horizontal scaling (Tz, percent/100): condensed text draws — and
            // measures — narrower than the font's nominal advances.
            double preHorizScale = 1.0;
            // Character/word spacing (Tc/Tw, text-space units): the
            // segment measure includes them — a negative Tc condenses every
            // advance the mean-advance cell averages.
            double preTc = 0, preTw = 0;
            bool preRot = false;
            double cmA = icmA, cmB = icmB, cmC = icmC, cmD = icmD, cmE = icmE, cmF = icmF;
            var cmFullStack = new Stack<(double a, double b, double c, double d, double e, double f)>();
            var cmStack = new Stack<double>();
            void SeeShowX()
            {
                // Rotated keeps the projection+cmTx frame (mirrors the runtime);
                // upright tlmX is already the composed device X.
                var x = preRot ? tlmX + cmTx : tlmX;
                // A SIDEWAYS page reads along the negative device axis, so its
                // whole grid frame is negative and is kept separately - the
                // caller takes it once rotation is known to dominate.
                if (double.IsNaN(minXAny) || x < minXAny) minXAny = x;
                // Text drawn at negative X (template/title-block junk on
                // engineering sheets with a shifted MediaBox) can't occupy a
                // grid column and never anchors the grid origin.
                if (x < 0) return;
                if (double.IsNaN(minX) || x < minX) minX = x;
            }
            // True when the current show position falls outside the measuring
            // window (page bounds under LimitToPageBounds) — its glyphs are
            // clipped from the output, so they must not vote in the grid.
            bool ShowOutOfBounds()
            {
                if (bounds is null) return false;
                var x = preRot ? tlmX + cmTx : tlmX;
                if (x < bounds[0] - 1 || x > bounds[2] + 1) return true;
                return !preRot && (tlmY < bounds[1] - 1 || tlmY > bounds[3] + 1);
            }
            while (true)
            {
                var tok = lexer.NextToken();
                if (tok.Kind == TokenKind.Eof) break;
                switch (tok.Kind)
                {
                    case TokenKind.Integer: operands.Add(new Core.PdfInteger(tok.IntValue)); break;
                    case TokenKind.Real: operands.Add(new Core.PdfReal(tok.RealValue)); break;
                    case TokenKind.LiteralString: operands.Add(new Core.PdfString(tok.BytesValue!)); break;
                    case TokenKind.HexString: operands.Add(new Core.PdfString(tok.BytesValue!, isHex: true)); break;
                    case TokenKind.Name: operands.Add(new Core.PdfName(tok.StringValue!)); break;
                    case TokenKind.ArrayStart: operands.Add(ParseContentArray(lexer)); break;
                    case TokenKind.Keyword:
                        var op = tok.StringValue!;
                        if (op == "Tf")
                        {
                            if (operands.Count >= 2 && operands[0] is Core.PdfName fn
                                && fonts.TryGetValue(fn.Value, out var fdict))
                            {
                                try { metrics = FontMetrics.FromFontDict(fdict, reader); } catch { metrics = null; }
                                fontSize = Math.Abs(GetNumber(operands[1]));
                            }
                        }
                        else if (op == "BT")
                        {
                            fsScale = Math.Sqrt(cmC * cmC + cmD * cmD);
                            if (fsScale < 0.001) fsScale = 1.0;
                            var nab = Math.Sqrt(cmA * cmA + cmB * cmB);
                            if (nab < 0.001) nab = 1.0;
                            preRot = Math.Abs(cmB) > 0.001 && Math.Abs(cmD) < 0.1 * Math.Abs(cmB);
                            tmScaleX = nab;
                            tlmX = preRot ? RotatedReadX(cmA, cmB, cmE, cmF) : cmE;
                            tlmY = cmF;
                        }
                        else if (op == "Tm" && operands.Count >= 6)
                        {
                            var mA = GetNumber(operands[0]); var mB = GetNumber(operands[1]);
                            var mC = GetNumber(operands[2]); var mD = GetNumber(operands[3]);
                            var mE = GetNumber(operands[4]); var mF = GetNumber(operands[5]);
                            // Compose with the CTM linear part (mirrors the extraction loop).
                            var cEa = mA * cmA + mB * cmC; var cEb = mA * cmB + mB * cmD;
                            var cEc = mC * cmA + mD * cmC; var cEd = mC * cmB + mD * cmD;
                            var cEe = mE * cmA + mF * cmC + cmE; var cEf = mE * cmB + mF * cmD + cmF;
                            fsScale = Math.Sqrt(cEc * cEc + cEd * cEd);
                            if (fsScale < 0.001) fsScale = 1.0;
                            var nab2 = Math.Sqrt(cEa * cEa + cEb * cEb);
                            if (nab2 < 0.001) nab2 = 1.0;
                            preRot = Math.Abs(cEb) > 0.001 && Math.Abs(cEd) < 0.1 * Math.Abs(cEb);
                            tmScaleX = nab2;
                            tlmX = preRot ? RotatedReadX(cEa, cEb, cEe, cEf) : cEe;
                            tlmY = cEf;
                        }
                        else if ((op == "Td" || op == "TD") && operands.Count >= 2)
                        {
                            tlmX += GetNumber(operands[0]) * tmScaleX;
                            tlmY += GetNumber(operands[1]) * fsScale;
                            if (op == "TD") preTL = -GetNumber(operands[1]) * fsScale;
                        }
                        else if (op == "TL" && operands.Count >= 1) { preTL = GetNumber(operands[0]) * fsScale; }
                        else if (op == "T*") { tlmY -= preTL; }
                        else if (op == "Tz" && operands.Count >= 1)
                        {
                            var hs = GetNumber(operands[0]) / 100.0;
                            if (hs > 0.01 && hs < 100) preHorizScale = hs;
                        }
                        else if (op == "Tc" && operands.Count >= 1) { preTc = GetNumber(operands[0]); }
                        else if (op == "Tw" && operands.Count >= 1) { preTw = GetNumber(operands[0]); }
                        else if (op == "q") { cmStack.Push(cmTx); cmFullStack.Push((cmA, cmB, cmC, cmD, cmE, cmF)); }
                        else if (op == "Q")
                        {
                            if (cmStack.Count > 0) cmTx = cmStack.Pop();
                            if (cmFullStack.Count > 0) (cmA, cmB, cmC, cmD, cmE, cmF) = cmFullStack.Pop();
                        }
                        else if (op == "cm" && operands.Count >= 6)
                        {
                            cmTx += GetNumber(operands[4]);
                            var na = GetNumber(operands[0]); var nb = GetNumber(operands[1]);
                            var nc = GetNumber(operands[2]); var nd = GetNumber(operands[3]);
                            var ne = GetNumber(operands[4]); var nf = GetNumber(operands[5]);
                            var a2 = na * cmA + nb * cmC; var b2 = na * cmB + nb * cmD;
                            var c2 = nc * cmA + nd * cmC; var d2 = nc * cmB + nd * cmD;
                            var e2 = ne * cmA + nf * cmC + cmE; var f2 = ne * cmB + nf * cmD + cmF;
                            cmA = a2; cmB = b2; cmC = c2; cmD = d2; cmE = e2; cmF = f2;
                        }
                        else if (op == "BI") { SkipInlineImage(lexer); }
                        else if (op == "Do" && recurse && rdepth < 6
                            && operands.Count >= 1 && operands[0] is Core.PdfName doName)
                        {
                            // A page can draw all its text inside a Form XObject
                            // (a shifted-MediaBox wrapper); measure that text too so
                            // the grid is sized instead of falling back to gap
                            // spacing. Recurse with the form's own fonts and the CTM
                            // in effect at the Do (form /Matrix ignored, as in the
                            // extraction loop).
                            var xobjs = ResolveXObjects(resDict, reader);
                            var xstr = xobjs is not null ? reader.ResolveStream(xobjs.Get(doName.Value)) : null;
                            if (xstr is not null && reader.ResolveName(xstr.Dict, "Subtype") == "Form")
                            {
                                var xbytes = reader.DecodeStream(xstr);
                                var formFonts = ResolveFonts(xstr.Dict, reader);
                                Scan(xbytes, formFonts, xstr.Dict, cmA, cmB, cmC, cmD, cmE, cmF, rdepth + 1, recurse);
                            }
                        }
                        else if (op == "Tj" || op == "'" || op == "\"")
                        {
                            if (op != "Tj") tlmY -= preTL; // ' and " imply T*
                            var s = operands.LastOrDefault(o => o is Core.PdfString) as Core.PdfString;
                            if (s is not null && metrics is not null && !ShowOutOfBounds())
                            {
                                SeeShowX();
                                // Grid buckets CEIL the effective size BEFORE aggregation
                                // (9.2pt and 9.7pt text pools into one 10pt
                                // bucket; 11.01pt grids as 12pt). Advances still measure at
                                // the true size.
                                var fsTrue = fontSize * fsScale;
                                // Round before ceiling: matrix-composition float dirt
                                // (12.0000001) must not bump a whole bucket.
                                var fsDev = Math.Ceiling(Math.Round(fsTrue, 3));
                                if (!rawBySize.TryGetValue(fsDev, out var rw) || fsTrue < rw) rawBySize[fsDev] = fsTrue;
                                // Advances scale by the ADVANCE-axis norm — on rotated pages a
                                // run's horizontal stretch is independent of its font size.
                                var fsAdv = fontSize * (preRot ? tmScaleX : fsScale);
                                var w1 = metrics.MeasureString(s.Value, fsAdv) * preHorizScale;
                                sumW += w1;
                                var g = GlyphCount(s.Value.Length, metrics);
                                cnt += g;
                                if (preRot) rotChars += g; else uprightChars += g;
                                charsPerSize[fsDev] = charsPerSize.GetValueOrDefault(fsDev) + g;
                                widthPerSize[fsDev] = widthPerSize.GetValueOrDefault(fsDev)
                                    + Math.Min(w1, 0.6 * fsTrue * g);
                                pureCharsPerSize[fsDev] = pureCharsPerSize.GetValueOrDefault(fsDev) + g;
                                pureWidthPerSize[fsDev] = pureWidthPerSize.GetValueOrDefault(fsDev)
                                    + Math.Min(w1, 0.6 * fsTrue * g);
                                var (nsp, wsp) = DrawnSpaces(s.Value, metrics, fsAdv, preHorizScale);
                                var tcW = (preTc * g + preTw * nsp) * (preRot ? tmScaleX : fsScale) * preHorizScale;
                                avgCharsPerSize[fsDev] = avgCharsPerSize.GetValueOrDefault(fsDev) + g;
                                avgWidthPerSize[fsDev] = avgWidthPerSize.GetValueOrDefault(fsDev)
                                    + Math.Min(Math.Max(w1 + tcW, 0), 0.6 * fsTrue * g);
                            }
                        }
                        else if (op == "TJ" && operands.Count >= 1 && operands[0] is Core.PdfArray arr
                            && !ShowOutOfBounds())
                        {
                            var sawString = false;
                            // The estimate averages NET run widths — glyph advances plus
                            // the array's kern adjustments — over the run's PHYSICAL text
                            // length, which includes the word spaces its kern rule will
                            // synthesize (an adjustment at word depth becomes a char).
                            double arrW = 0; var arrG = 0; double arrFsTrue = 0, arrFsDev = 0;
                            double arrWPure = 0; var arrSp = 0; double arrSpW = 0;
                            // Mean-advance numerator: glyph widths plus kern adjustments,
                            // EXCLUDING large positive ones — a positive adjustment past
                            // ~0.1 em is a backward pen JUMP (RTL layout), not kerning, and
                            // would cancel real ink out of the average, collapsing the cell.
                            // Small positive tightening kerns stay in the sum.
                            double arrWAvg = 0;
                            var arrMulti = false; var arrDeep = 0; var arrAdjCnt = 0; var arrSynth = 0;
                            // Word-space synthesis chars for the MEAN-ADVANCE population:
                            // a deep kern only reads as a word space when it does NOT
                            // adjoin a drawn space glyph (justified text kerns beside its
                            // real spaces; those gaps are already counted by the glyph).
                            var arrSynthAvg = 0; var pendingSynth = 0; var prevEndsSpace = false;
                            foreach (var it in arr)
                            {
                                if (it is Core.PdfString ps && metrics is not null)
                                {
                                    sawString = true;
                                    arrFsTrue = fontSize * fsScale;
                                    arrFsDev = Math.Ceiling(Math.Round(arrFsTrue, 3));
                                    if (!rawBySize.TryGetValue(arrFsDev, out var rw2) || arrFsTrue < rw2) rawBySize[arrFsDev] = arrFsTrue;
                                    // Advance-axis norm (see the Tj note).
                                    var arrFsAdv = fontSize * (preRot ? tmScaleX : fsScale);
                                    var wPiece = metrics.MeasureString(ps.Value, arrFsAdv) * preHorizScale;
                                    arrW += wPiece;
                                    arrWPure += wPiece;
                                    arrWAvg += wPiece;
                                    var g2 = GlyphCount(ps.Value.Length, metrics);
                                    arrG += g2;
                                    var (nsp2, wsp2) = DrawnSpaces(ps.Value, metrics, arrFsAdv, preHorizScale);
                                    arrSp += nsp2; arrSpW += wsp2;
                                    var simple = !metrics.IsCid && ps.Value.Length > 0;
                                    if (pendingSynth > 0 && !(simple && ps.Value[0] == 0x20))
                                        arrSynthAvg += pendingSynth;
                                    pendingSynth = 0;
                                    prevEndsSpace = simple && ps.Value[^1] == 0x20;
                                    if (g2 >= 2) arrMulti = true;
                                }
                                else if (it is not Core.PdfString && metrics is not null)
                                {
                                    var adj = GetNumber(it);
                                    arrW -= adj * fontSize * (preRot ? tmScaleX : fsScale) * preHorizScale / 1000.0;
                                    if (adj < 100)
                                        arrWAvg -= adj * fontSize * (preRot ? tmScaleX : fsScale) * preHorizScale / 1000.0;
                                    arrAdjCnt++;
                                    if (adj <= -130) arrDeep++;
                                    if (adj < -190 || (arrMulti && adj <= -130))
                                    {
                                        arrSynth++;
                                        if (!prevEndsSpace) pendingSynth++;
                                    }
                                }
                            }
                            if (sawString)
                            {
                                // Positioning arrays (word-depth kerns are the norm) don't
                                // synthesize; mirror the runtime rule's shape.
                                if (!arrMulti && arrAdjCnt >= 3 && arrDeep * 2 >= arrAdjCnt) { arrSynth = 0; arrSynthAvg = 0; }
                                if (arrW < 0) arrW = 0;
                                var chars2 = arrG + arrSynth;
                                sumW += arrW;
                                cnt += chars2;
                                if (preRot) rotChars += arrG; else uprightChars += arrG;
                                charsPerSize[arrFsDev] = charsPerSize.GetValueOrDefault(arrFsDev) + chars2;
                                widthPerSize[arrFsDev] = widthPerSize.GetValueOrDefault(arrFsDev)
                                    + Math.Min(arrW, 0.6 * arrFsTrue * chars2);
                                pureCharsPerSize[arrFsDev] = pureCharsPerSize.GetValueOrDefault(arrFsDev) + arrG;
                                pureWidthPerSize[arrFsDev] = pureWidthPerSize.GetValueOrDefault(arrFsDev)
                                    + Math.Min(arrWPure, 0.6 * arrFsTrue * arrG);
                                var avgChars = arrG + arrSynthAvg;
                                var arrTcW = (preTc * arrG + preTw * arrSp) * (preRot ? tmScaleX : fsScale) * preHorizScale;
                                avgCharsPerSize[arrFsDev] = avgCharsPerSize.GetValueOrDefault(arrFsDev) + avgChars;
                                avgWidthPerSize[arrFsDev] = avgWidthPerSize.GetValueOrDefault(arrFsDev)
                                    + Math.Min(Math.Max(arrWAvg + arrTcW, 0), 0.6 * arrFsTrue * avgChars);
                                SeeShowX();
                            }
                        }
                        operands.Clear();
                        break;
                    default: operands.Clear(); break;
                }
            }
        }

        // First pass: the page's own content streams only (unchanged behaviour).
        foreach (var streamBytes in streams)
            Scan(streamBytes, pageFonts, pageDict, 1, 0, 0, 1, 0, 0, 0, recurse: false);

        // Rescue pass: a page whose direct stream carries almost no text draws it
        // through Form XObjects. Re-measure descending into those forms so the grid
        // gets sized (otherwise cell = 0 and Pure spacing falls back to the coarser
        // gap heuristic). Only mixed-content pages with real direct text (cnt >= 8)
        // keep the original, calibration-preserving estimate.
        if (cnt < 8)
        {
            sumW = 0; cnt = 0; rotChars = 0; uprightChars = 0; minX = double.NaN; minXAny = double.NaN;
            rawBySize.Clear(); widthPerSize.Clear(); pureWidthPerSize.Clear();
            pureCharsPerSize.Clear(); avgWidthPerSize.Clear(); avgCharsPerSize.Clear();
            charsPerSize.Clear();
            foreach (var streamBytes in streams)
                Scan(streamBytes, pageFonts, pageDict, 1, 0, 0, 1, 0, 0, 0, recurse: true);
        }

        var rotDom = rotChars > uprightChars;
        var gridMinX = rotDom ? minXAny : minX;
        if (cnt < 8) return (0, 0, gridMinX, 0, rotDom);

        // Dominant font size: most characters; tie → smallest size.
        double domSize = 0; var domCount = -1;
        foreach (var kv in charsPerSize)
        {
            if (kv.Value > domCount || (kv.Value == domCount && kv.Key < domSize))
            {
                domSize = kv.Key; domCount = kv.Value;
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
        if (domSize > 2.5)
        {
            var rawDom = rawBySize.TryGetValue(domSize, out var rv) ? rv : domSize;
            var ac = avgCharsPerSize.GetValueOrDefault(domSize);
            var aw = avgWidthPerSize.GetValueOrDefault(domSize);
            var sf = scaleFactor > 0 ? scaleFactor : 1.0;
            var cell = sf * 0.6 * (domSize - 2);
            if (scaleFactor == 0 && ac > 0) cell = aw / ac;
            if (GridDebug)
            {
                var dc = charsPerSize.GetValueOrDefault(domSize);
                var dw = widthPerSize.GetValueOrDefault(domSize);
                var pc = pureCharsPerSize.GetValueOrDefault(domSize);
                var pw = pureWidthPerSize.GetValueOrDefault(domSize);
                Console.Error.WriteLine($"[cell] dom={domSize} raw={rawDom:F2} chars={dc} width={dw:F1} "
                    + $"avg={(dc > 0 ? dw / dc : 0):F3} pureAvg={(pc > 0 ? pw / pc : 0):F3} "
                    + $"nsAvg={(ac > 0 ? aw / ac : 0):F3} cell={cell:F3} "
                    + $"legacy={0.6 * (rawDom - 2):F3} legacyCeil={0.6 * (domSize - 2):F3}");
                foreach (var kv in charsPerSize)
                    Console.Error.WriteLine($"[cell]   bucket={kv.Key} chars={kv.Value} width={widthPerSize.GetValueOrDefault(kv.Key):F1} avg={(kv.Value > 0 ? widthPerSize.GetValueOrDefault(kv.Key) / kv.Value : 0):F3} pureAvg={(pureCharsPerSize.GetValueOrDefault(kv.Key) > 0 ? pureWidthPerSize.GetValueOrDefault(kv.Key) / pureCharsPerSize.GetValueOrDefault(kv.Key) : 0):F3}");
            }
            return (cell, sf * 0.6 * (domSize - 2), gridMinX, rawDom, rotDom);
        }
        return (sumW / cnt, sumW / cnt, gridMinX, 0, rotDom);
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
