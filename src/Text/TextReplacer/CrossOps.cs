using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Walk a content stream and collect every text-showing operator with the
    /// full state needed to match, measure, split, or re-anchor it: decoded text + raw
    /// bytes, byte span, text matrix + composed CTM, font state (dict/ToUnicode/size/Tc),
    /// TJ kern total, and the positioning-Tm operand span. Shared by the cross-operator
    /// replace and the run-move reflow.</summary>
    private List<CrossTextOp> CollectTextOps(byte[] streamBytes,
        Dictionary<string, PdfDictionary> fonts, PdfReader reader,
        double initCtmA = 1, double initCtmB = 0, double initCtmC = 0, double initCtmD = 1,
        double initCtmTx = 0, double initCtmTy = 0)
    {
        var textOps = new List<CrossTextOp>();
        var lexer2 = new PdfLexer(streamBytes);
        var ops2 = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();
        Dictionary<int, string>? curToUnicode = null;
        PdfDictionary? curFontDict = null;
        string? curFontName = null;
        double curFontSize = 12;
        double curTc = 0;
        int curBtStart = -1;
        double tmA = 1, tmB = 0, tmC = 0, tmD = 1, tmTx = 0, tmTy = 0, tlLeading = 0;
        // The LINE matrix's translation. Td/TD/T*/' move the LINE, and the text matrix is then
        // reset to it; only showing text moves the text matrix away from the line. Tracking one
        // translation for both is right only while shows never advance it — once they do, a Td
        // measured from the advanced position doubles the run's width into the next line.
        double tlmTx = 0, tlmTy = 0;
        // Seed the CTM with the caller's context (the Do-site CTM when this stream is a
        // recursed Form XObject) so TargetY/TargetX scoping sees page-space positions.
        double ctmA = initCtmA, ctmB = initCtmB, ctmC = initCtmC, ctmD = initCtmD;
        double ctmTx = initCtmTx, ctmTy = initCtmTy;
        var ctmStack = new Stack<(double, double, double, double, double, double)>();
        var tsStack = new Stack<(double size, string? name, PdfDictionary? dict,
            Dictionary<int, string>? toUni, double tc, double tw, double leading)>();
        double curTw = 0;
        // Pending positioning-Tm record, consumed by the next text-showing op.
        var pendingTm = (has: false, xStart: 0, xEnd: 0, xVal: 0.0);
        var pendingTd = (has: false, xStart: 0, xEnd: 0, xVal: 0.0);

        // Showing text ADVANCES the text matrix by what it drew (PDF 32000-1 §9.4.4). Without
        // that, consecutive shows inside one BT block all report the first one's origin: a
        // producer that lays a line out as `(word) Tj (word) Tj …`, advancing on the glyphs
        // rather than re-positioning, gives every run of the line the same X. Every consumer
        // that asks where a run sits then reads the line's start for all of them.
        var metricsByDict = new Dictionary<PdfDictionary, FontMetrics?>();
        FontMetrics? MetricsFor(PdfDictionary? fd)
        {
            if (fd is null) return null;
            if (metricsByDict.TryGetValue(fd, out var m)) return m;
            FontMetrics? built = null;
            try { built = FontMetrics.FromFontDict(fd, reader); } catch { }
            metricsByDict[fd] = built;
            return built;
        }
        void AdvanceText(byte[] bytes, double kern1000)
        {
            var m = MetricsFor(curFontDict);
            if (m is null) return;
            double w;
            try { w = m.MeasureString(bytes, curFontSize); } catch { return; }
            var glyphs = m.IsCid ? (bytes.Length + 1) / 2 : bytes.Length;
            w += curTc * glyphs;
            if (!m.IsCid && curTw != 0)
                foreach (var b in bytes)
                    if (b == 32) w += curTw;
            w -= kern1000 / 1000.0 * curFontSize;
            tmTx += w * tmA;
            tmTy += w * tmB;
        }

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
                    else if (op == "Tc" && ops2.Count >= 1)
                        curTc = ToDouble(ops2[0].obj);
                    else if (op == "Tw" && ops2.Count >= 1)
                        curTw = ToDouble(ops2[0].obj);
                    else if (op is "Tj" or "'" && ops2.Count >= 1 && ops2[0].obj is PdfString s)
                    {
                        if (op == "'") { tlmTx = -tlLeading * tmC + tlmTx; tlmTy = -tlLeading * tmD + tlmTy; tmTx = tlmTx; tmTy = tlmTy; pendingTm.has = false; }
                        var decoded = DecodeString(s.Value, curToUnicode, curFontDict, reader);
                        textOps.Add(new CrossTextOp
                        {
                            Text = decoded, Bytes = s.Value, IsHex = s.IsHex,
                            OpStart = ops2[0].startPos, OpEnd = ep,
                            TmA = tmA, TmB = tmB, TmC = tmC, TmD = tmD, TmTx = tmTx, TmTy = tmTy,
                            CtmA = ctmA, CtmB = ctmB, CtmC = ctmC, CtmD = ctmD, CtmTx = ctmTx, CtmTy = ctmTy,
                            FontDict = curFontDict, FontName = curFontName, ToUnicode = curToUnicode,
                            FontSize = curFontSize, Tc = curTc, BtStart = curBtStart,
                            TmPositioned = pendingTm.has, TmXTokStart = pendingTm.xStart,
                            TmXTokEnd = pendingTm.xEnd, TmXVal = pendingTm.xVal,
                            TdPositioned = pendingTd.has, TdXTokStart = pendingTd.xStart,
                            TdXTokEnd = pendingTd.xEnd, TdXVal = pendingTd.xVal,
                        });
                        pendingTm.has = false;
                        pendingTd.has = false;
                        AdvanceText(s.Value, 0);
                    }
                    else if (op == "TJ" && ops2.Count >= 1 && ops2[0].obj is PdfArray tjArr)
                    {
                        var sb = new StringBuilder();
                        var byteBuf = new MemoryStream();
                        double kernSum = 0;
                        bool isHex = false; bool firstStr = true;
                        // Where each kern falls WITHIN the run, so a wrap can measure a
                        // prefix at its true width. A justified line carries most of its
                        // inter-word space in these numbers (this corpus has -700-odd per
                        // gap), and a prefix measured from glyph advances alone comes out
                        // far too narrow — narrow enough to keep a word that must
                        // wrap.
                        var kernAt = new List<(int byteIndex, double amount)>();
                        foreach (var item in tjArr)
                        {
                            if (item is PdfString ps)
                            {
                                sb.Append(DecodeString(ps.Value, curToUnicode, curFontDict, reader));
                                byteBuf.Write(ps.Value, 0, ps.Value.Length);
                                if (firstStr) { isHex = ps.IsHex; firstStr = false; }
                            }
                            else if (item is PdfInteger ki)
                            {
                                kernSum += ki.Value;
                                kernAt.Add(((int)byteBuf.Length, ki.Value));
                            }
                            else if (item is PdfReal kr)
                            {
                                kernSum += kr.Value;
                                kernAt.Add(((int)byteBuf.Length, kr.Value));
                            }
                        }
                        textOps.Add(new CrossTextOp
                        {
                            Text = sb.ToString(), Bytes = byteBuf.ToArray(), IsHex = isHex,
                            OpStart = ops2[0].startPos, OpEnd = ep,
                            TmA = tmA, TmB = tmB, TmC = tmC, TmD = tmD, TmTx = tmTx, TmTy = tmTy,
                            CtmA = ctmA, CtmB = ctmB, CtmC = ctmC, CtmD = ctmD, CtmTx = ctmTx, CtmTy = ctmTy,
                            FontDict = curFontDict, FontName = curFontName, ToUnicode = curToUnicode,
                            FontSize = curFontSize, Tc = curTc, KernSum = kernSum, KernAt = kernAt,
                            BtStart = curBtStart,
                            TmPositioned = pendingTm.has, TmXTokStart = pendingTm.xStart,
                            TmXTokEnd = pendingTm.xEnd, TmXVal = pendingTm.xVal,
                            TdPositioned = pendingTd.has, TdXTokStart = pendingTd.xStart,
                            TdXTokEnd = pendingTd.xEnd, TdXVal = pendingTd.xVal,
                        });
                        pendingTm.has = false;
                        pendingTd.has = false;
                        AdvanceText(textOps[^1].Bytes, kernSum);
                    }
                    else if (op == "BT") { tmA = 1; tmB = 0; tmC = 0; tmD = 1; tmTx = 0; tmTy = 0; tlmTx = 0; tlmTy = 0; tlLeading = 0; pendingTm.has = false; pendingTd.has = false; curBtStart = sp; }
                    else if ((op == "Td" || op == "TD") && ops2.Count >= 2)
                    {
                        double dx = ToDouble(ops2[0].obj), dy = ToDouble(ops2[1].obj);
                        tlmTx = dx * tmA + dy * tmC + tlmTx;
                        tlmTy = dx * tmB + dy * tmD + tlmTy;
                        tmTx = tlmTx; tmTy = tlmTy;
                        if (op == "TD") tlLeading = -dy;
                        pendingTm.has = false; // Td-positioned: inherits the line chain, no Tm patch
                        pendingTd = (true, ops2[0].startPos, ops2[0].endPos, dx);
                    }
                    else if (op == "Tm" && ops2.Count >= 6)
                    {
                        tmA = ToDouble(ops2[0].obj); tmB = ToDouble(ops2[1].obj);
                        tmC = ToDouble(ops2[2].obj); tmD = ToDouble(ops2[3].obj);
                        tmTx = ToDouble(ops2[4].obj); tmTy = ToDouble(ops2[5].obj);
                        tlmTx = tmTx; tlmTy = tmTy;
                        pendingTm = (true, ops2[4].startPos, ops2[4].endPos, tmTx);
                        pendingTd.has = false;
                    }
                    else if (op == "TL" && ops2.Count >= 1) tlLeading = ToDouble(ops2[0].obj);
                    else if (op == "T*") { tlmTx = -tlLeading * tmC + tlmTx; tlmTy = -tlLeading * tmD + tlmTy; tmTx = tlmTx; tmTy = tlmTy; pendingTm.has = false; }
                    // The text state rides in the graphics state (PDF 32000-1 Table 52), so
                    // `q`/`Q` save and restore the font, its SIZE and the spacing parameters
                    // as well as the CTM. A `q /F 1 Tf ... Q` block that leaks its size out
                    // makes every later run measure at 1 pt, and a re-flow then never wraps.
                    else if (op == "q")
                    {
                        ctmStack.Push((ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy));
                        tsStack.Push((curFontSize, curFontName, curFontDict, curToUnicode, curTc, curTw, tlLeading));
                    }
                    else if (op == "Q")
                    {
                        if (ctmStack.Count > 0) (ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy) = ctmStack.Pop();
                        if (tsStack.Count > 0)
                            (curFontSize, curFontName, curFontDict, curToUnicode, curTc, curTw, tlLeading) = tsStack.Pop();
                    }
                    else if (op == "cm" && ops2.Count >= 6)
                    {
                        double a = ToDouble(ops2[0].obj), b = ToDouble(ops2[1].obj), c = ToDouble(ops2[2].obj);
                        double dd = ToDouble(ops2[3].obj), tx = ToDouble(ops2[4].obj), ty = ToDouble(ops2[5].obj);
                        double nA = a * ctmA + b * ctmC, nB = a * ctmB + b * ctmD;
                        double nC = c * ctmA + dd * ctmC, nD = c * ctmB + dd * ctmD;
                        double nTx = tx * ctmA + ty * ctmC + ctmTx, nTy = tx * ctmB + ty * ctmD + ctmTy;
                        ctmA = nA; ctmB = nB; ctmC = nC; ctmD = nD; ctmTx = nTx; ctmTy = nTy;
                    }
                    ops2.Clear();
                    break;
                default:
                    ops2.Clear();
                    break;
            }
        }
        return textOps;
    }

    /// <summary>Collect every single-rectangle fill — a lone `re` painted by
    /// f/F/f*/B/B*/b/b* with no other path segment — with its page-space rect
    /// (axis-aligned CTM) and the byte span covering `re`'s first operand through
    /// the painting operator. Underline bars drawn by word processors take exactly
    /// this shape; multi-segment paths are skipped so a real outline never
    /// qualifies for deletion.</summary>
    private static List<FillRectOp> CollectFillRects(byte[] streamBytes)
    {
        var rects = new List<FillRectOp>();
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<(double val, int startPos)>();
        double ctmA = 1, ctmB = 0, ctmC = 0, ctmD = 1, ctmTx = 0, ctmTy = 0;
        var ctmStack = new Stack<(double, double, double, double, double, double)>();
        // Pending path: the current subpath ops since the last paint/clear.
        (bool has, double x, double y, double w, double h, int spanStart) pending = default;
        bool pathDirty = false;

        while (true)
        {
            var sp = (int)lexer.Position;
            var tok = lexer.NextToken();
            if (tok.Kind == TokenKind.Eof) break;
            var ep = (int)lexer.Position;
            switch (tok.Kind)
            {
                case TokenKind.Integer:
                    operands.Add((tok.IntValue, sp));
                    break;
                case TokenKind.Real:
                    operands.Add((tok.RealValue, sp));
                    break;
                case TokenKind.Keyword:
                {
                    var op = tok.StringValue!;
                    switch (op)
                    {
                        case "q":
                            ctmStack.Push((ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy));
                            break;
                        case "Q":
                            if (ctmStack.Count > 0)
                                (ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy) = ctmStack.Pop();
                            break;
                        case "cm" when operands.Count >= 6:
                        {
                            double a = operands[0].val, b = operands[1].val, c = operands[2].val;
                            double d = operands[3].val, tx = operands[4].val, ty = operands[5].val;
                            double nA = a * ctmA + b * ctmC, nB = a * ctmB + b * ctmD;
                            double nC = c * ctmA + d * ctmC, nD = c * ctmB + d * ctmD;
                            double nTx = tx * ctmA + ty * ctmC + ctmTx, nTy = tx * ctmB + ty * ctmD + ctmTy;
                            ctmA = nA; ctmB = nB; ctmC = nC; ctmD = nD; ctmTx = nTx; ctmTy = nTy;
                            break;
                        }
                        case "re" when operands.Count >= 4:
                            if (pending.has) pathDirty = true; // second rect in one path
                            pending = (true, operands[^4].val, operands[^3].val,
                                operands[^2].val, operands[^1].val, operands[^4].startPos);
                            break;
                        case "m" or "l" or "c" or "v" or "y" or "h":
                            pathDirty = true;
                            break;
                        case "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                            if (pending.has && !pathDirty
                                && Math.Abs(ctmB) < 1e-9 && Math.Abs(ctmC) < 1e-9)
                            {
                                double x0 = ctmA * pending.x + ctmTx, y0 = ctmD * pending.y + ctmTy;
                                double x1 = ctmA * (pending.x + pending.w) + ctmTx;
                                double y1 = ctmD * (pending.y + pending.h) + ctmTy;
                                rects.Add(new FillRectOp
                                {
                                    X = Math.Min(x0, x1), Y = Math.Min(y0, y1),
                                    W = Math.Abs(x1 - x0), H = Math.Abs(y1 - y0),
                                    SpanStart = pending.spanStart, SpanEnd = ep,
                                });
                            }
                            pending = default; pathDirty = false;
                            break;
                        case "n" or "S" or "s":
                            pending = default; pathDirty = false;
                            break;
                        // W/W* leave the path pending for the following paint/n op.
                    }
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }
        return rects;
    }

    private byte[]? TryCrossOperatorReplaceCore(byte[] streamBytes, string search, string replacement,
        PdfDictionary pageDict, PdfReader reader, string normalizedSearch, List<CrossTextOp> textOps)
    {
        // Advance (in text-space units) a byte string renders with an op's font state:
        // glyph widths + per-glyph Tc − TJ kerns (kern applied only when measuring the
        // op's own full bytes).
        var metricsCache = new Dictionary<PdfDictionary, FontMetrics?>();
        FontMetrics? MetricsOf(CrossTextOp o)
        {
            if (o.FontDict is null) return null;
            if (metricsCache.TryGetValue(o.FontDict, out var m)) return m;
            FontMetrics? built = null;
            try { built = FontMetrics.FromFontDict(o.FontDict, reader); } catch { }
            metricsCache[o.FontDict] = built;
            return built;
        }
        double Adv(CrossTextOp o, byte[] bytes, bool own)
        {
            var m = MetricsOf(o);
            double w;
            if (m is not null)
            {
                try { w = m.MeasureString(bytes, o.FontSize); }
                catch { w = o.FontSize * 0.5 * bytes.Length; }
            }
            else
                w = o.FontSize * 0.5 * bytes.Length;
            var glyphs = m?.IsCid == true ? (bytes.Length + 1) / 2 : bytes.Length;
            w += o.Tc * glyphs;
            if (own) w -= o.KernSum / 1000.0 * o.FontSize;
            return w;
        }

        // Gap-aware concatenation: like the absorber, insert a synthetic space between
        // two same-line ops separated by a word-sized positioning gap (text drawn
        // word-per-Tm with no space glyphs), so a spaced phrase can match across ops.
        // Synthetic chars map to op −1 and are trimmed off the match edges.
        var allText = new StringBuilder();
        var charToOp = new List<int>();
        for (var i = 0; i < textOps.Count; i++)
        {
            var cur = textOps[i];
            if (i > 0 && allText.Length > 0)
            {
                var prev = textOps[i - 1];
                bool sameCtm = Math.Abs(cur.CtmA - prev.CtmA) < 1e-6 && Math.Abs(cur.CtmC - prev.CtmC) < 1e-6
                    && Math.Abs(cur.CtmD - prev.CtmD) < 1e-6 && Math.Abs(cur.CtmTx - prev.CtmTx) < 1e-6
                    && Math.Abs(cur.CtmTy - prev.CtmTy) < 1e-6;
                bool horizontal = Math.Abs(cur.TmB) <= Math.Abs(cur.TmA) && Math.Abs(prev.TmB) <= Math.Abs(prev.TmA);
                if (sameCtm && horizontal && Math.Abs(cur.TmTy - prev.TmTy) < 2.0)
                {
                    var gap = cur.TmTx - (prev.TmTx + Adv(prev, prev.Bytes, own: true));
                    var fs = cur.FontSize > 0 ? cur.FontSize : 12.0;
                    var lastChar = allText[^1];
                    var nextChar = cur.Text.Length > 0 ? cur.Text[0] : '\0';
                    if (gap > 0.2 * fs && gap <= 3.0 * fs && lastChar != ' ' && nextChar != ' ')
                    {
                        charToOp.Add(-1);
                        allText.Append(' ');
                    }
                }
            }
            cur.CharStart = allText.Length;
            foreach (var _ in cur.Text) charToOp.Add(i);
            allText.Append(cur.Text);
        }

        var fullText = NormalizeForSearch(allText.ToString());

        // Enumerate match spans as (start, length) — regex match, or a literal scan that
        // is ELASTIC over the synthetic gap-spaces (charToOp < 0): a synthetic space
        // matches a needle space OR nothing, so both "05 DEC 2012" and the fragment's
        // segment-joined "05DEC2012" find the same span.
        (int idx, int len) NextMatch(int from)
        {
            if (_isRegex && _regexPattern is not null)
            {
                var m = _regexPattern.Match(fullText, from);
                return m.Success ? (m.Index, m.Length) : (-1, 0);
            }
            if (normalizedSearch.Length == 0) return (-1, 0);
            for (var st = Math.Max(0, from); st < fullText.Length; st++)
            {
                int h = st, n = 0;
                while (n < normalizedSearch.Length && h < fullText.Length)
                {
                    if (fullText[h] == normalizedSearch[n]) { h++; n++; continue; }
                    if (h < charToOp.Count && charToOp[h] < 0) { h++; continue; } // skip synthetic space
                    break;
                }
                if (n == normalizedSearch.Length) return (st, h - st);
            }
            return (-1, 0);
        }

        var (searchIdx, searchLen) = NextMatch(0);
        if (searchIdx < 0) return null;

        string FormatNum(double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);

        // Byte-level patches (follower Tm x rewrites), applied while copying.
        var patches = new SortedList<int, (int end, byte[] text)>();
        var result = new MemoryStream();
        var lastWrite = 0;
        void CopyRange(int to)
        {
            while (patches.Count > 0)
            {
                var start = patches.Keys[0];
                if (start >= to) break;
                var (pEnd, pText) = patches.Values[0];
                patches.RemoveAt(0);
                if (start < lastWrite) continue; // inside an already-replaced span
                result.Write(streamBytes, lastWrite, start - lastWrite);
                result.Write(pText, 0, pText.Length);
                lastWrite = pEnd;
            }
            if (to > lastWrite)
            {
                result.Write(streamBytes, lastWrite, to - lastWrite);
                lastWrite = to;
            }
        }

        // Can every char of the replacement be encoded in the op's own font? (Reverse
        // ToUnicode coverage; keeps the replacement in the source face — and measured
        // with the source metrics — instead of switching to a fallback font.)
        bool CanEncodeInFont(CrossTextOp o, string text)
        {
            if (o.ToUnicode is null)
                return text.All(c => c <= 0xFF); // simple Latin1 encoding
            var reverse = BuildReverseMap(o.ToUnicode);
            return text.All(c => reverse.ContainsKey(c.ToString()));
        }

        var replaced = false;
        while (searchIdx >= 0)
        {
            if (ReplaceFirstOnly && replaced) break;

            // Trim synthetic gap-space chars off the match edges and locate the ops.
            var msIdx = searchIdx;
            var meIdx = searchIdx + searchLen - 1;
            while (msIdx < meIdx && msIdx < charToOp.Count && charToOp[msIdx] < 0) msIdx++;
            while (meIdx > msIdx && meIdx < charToOp.Count && charToOp[meIdx] < 0) meIdx--;
            int firstOp = msIdx < charToOp.Count ? charToOp[msIdx] : -1;
            int lastOp = meIdx < charToOp.Count ? charToOp[meIdx] : -1;

            bool inTarget = firstOp < 0 ||
                (IsAtTargetY(textOps[firstOp].TmTx, textOps[firstOp].TmTy,
                             textOps[firstOp].CtmB, textOps[firstOp].CtmD, textOps[firstOp].CtmTy)
                 && IsAtTargetX(textOps[firstOp].TmTx, textOps[firstOp].TmTy,
                                textOps[firstOp].CtmA, textOps[firstOp].CtmC, textOps[firstOp].CtmTx));

            // Matches inside ONE operator belong to the per-op pass; cross-op only adds
            // value for spans covering multiple operators.
            if (inTarget && firstOp >= 0 && lastOp >= 0 && firstOp != lastOp)
            {
                var fo = textOps[firstOp];
                var lo = textOps[lastOp];
                var prefixText = fo.Text.Substring(0, Math.Clamp(msIdx - fo.CharStart, 0, fo.Text.Length));
                var matchedLastLen = Math.Clamp(meIdx - lo.CharStart + 1, 0, lo.Text.Length);
                var suffixText = lo.Text.Substring(matchedLastLen);

                var prefixBytes = prefixText.Length > 0 ? EncodeString(prefixText, fo.ToUnicode, fo.FontDict) : Array.Empty<byte>();
                var suffixBytes = suffixText.Length > 0 ? EncodeString(suffixText, lo.ToUnicode, lo.FontDict) : Array.Empty<byte>();
                var matchedLastBytes = matchedLastLen > 0 ? EncodeString(lo.Text.Substring(0, matchedLastLen), lo.ToUnicode, lo.FontDict) : Array.Empty<byte>();

                // What the matched glyphs ADVANCED, summed through the ops' own fonts
                // (and so through their own codes). A Td-chained line places every later
                // glyph off the chain rather than off these advances, so nothing moves
                // the tail when the replacement is wider - see the follower shift below.
                var advMatched = 0.0;
                if (ShiftFollowersByAdvance)
                {
                    var matchedFirstText = fo.Text.Substring(Math.Min(prefixText.Length, fo.Text.Length));
                    if (matchedFirstText.Length > 0)
                        advMatched += Adv(fo, EncodeString(matchedFirstText, fo.ToUnicode, fo.FontDict), own: false);
                    for (var oi = firstOp + 1; oi < lastOp; oi++)
                        advMatched += Adv(textOps[oi], textOps[oi].Bytes, own: true);
                    if (matchedLastBytes.Length > 0)
                        advMatched += Adv(lo, matchedLastBytes, own: false);
                }

                // Copy everything before the first matched operator.
                CopyRange(fo.OpStart);

                // Prefix (kept head of the first op) stays in the original font at the
                // original pen position.
                if (prefixBytes.Length > 0)
                {
                    WriteStringOperand(result, prefixBytes, fo.IsHex);
                    result.Write(Encoding.ASCII.GetBytes(" Tj "));
                }

                // Replacement: re-encoded into the source font when its glyphs map
                // (source-metric width, keeps the face); otherwise the font-switch path.
                double advRepl;
                if (replacement.Length > 0 && CanEncodeInFont(fo, replacement))
                {
                    var replBytes = EncodeString(replacement, fo.ToUnicode, fo.FontDict);
                    WriteStringOperand(result, replBytes, fo.IsHex);
                    result.Write(Encoding.ASCII.GetBytes(" Tj "));
                    advRepl = Adv(fo, replBytes, own: false);
                }
                else if (replacement.Length > 0)
                {
                    WriteFontSwitchedReplacement(result, replacement, fo.FontDict,
                        fo.FontName, fo.FontSize, pageDict, reader, "Tj", AllowSubsetGlyphFallback, ForcedCidFallbackFamily);
                    result.WriteByte((byte)' ');
                    double est = 0;
                    foreach (var ch in replacement)
                    {
                        var cw = ch <= 0xFF ? Standard14Fonts.GetWidth("Helvetica", ch) : 0;
                        est += cw > 0 ? cw : 500;
                    }
                    advRepl = est / 1000.0 * fo.FontSize;
                }
                else
                    advRepl = 0;

                // Middle operators: keep their positioning/state gaps, blank the text.
                for (var oi = firstOp + 1; oi < lastOp; oi++)
                {
                    lastWrite = textOps[oi - 1].OpEnd;
                    CopyRange(textOps[oi].OpStart);
                    result.Write(Encoding.ASCII.GetBytes("() Tj "));
                }

                // Last operator: keep the tail after the match, re-anchored with an
                // absolute Tm. Reflow mode puts it at match-start + replacement width
                // (the same-line reflow); otherwise it keeps its original X.
                lastWrite = textOps[lastOp - 1 >= firstOp ? lastOp - 1 : firstOp].OpEnd;
                CopyRange(lo.OpStart);
                var advPrefix = prefixBytes.Length > 0 ? Adv(fo, prefixBytes, own: false) : 0;
                var advMatchedLast = matchedLastBytes.Length > 0 ? Adv(lo, matchedLastBytes, own: false) : 0;
                var oldSuffixTmX = lo.TmTx + advMatchedLast;
                var newSuffixTmX = ReflowLineOnReplace ? fo.TmTx + advPrefix + advRepl : oldSuffixTmX;
                if (suffixBytes.Length > 0)
                {
                    // Leading space: the copied gap can end in a keyword ("… Tc") with no
                    // trailing delimiter, and "Tc1 0 0 …" would lex as an unknown keyword.
                    result.Write(Encoding.ASCII.GetBytes(
                        $" {FormatNum(lo.TmA)} {FormatNum(lo.TmB)} {FormatNum(lo.TmC)} {FormatNum(lo.TmD)} {FormatNum(newSuffixTmX)} {FormatNum(lo.TmTy)} Tm "));
                    WriteStringOperand(result, suffixBytes, lo.IsHex);
                    result.Write(Encoding.ASCII.GetBytes(" Tj"));
                }
                else
                    result.Write(Encoding.ASCII.GetBytes("() Tj"));

                lastWrite = lo.OpEnd;
                _replacementCount++;
                replaced = true;

                // Same-line reflow: shift following absolute-Tm runs on this line left by
                // the width delta so words split across runs stay joined. Td-positioned
                // followers inherit the shift through the re-anchored suffix Tm.
                var delta = oldSuffixTmX - newSuffixTmX;
                if (Math.Abs(delta) > 0.01)
                {
                    for (var j = lastOp + 1; j < textOps.Count; j++)
                    {
                        var fl = textOps[j];
                        if (!fl.TmPositioned) continue;
                        if (fl.TmXTokStart < lastWrite) continue;
                        bool sameCtm = Math.Abs(fl.CtmA - lo.CtmA) < 1e-6 && Math.Abs(fl.CtmC - lo.CtmC) < 1e-6
                            && Math.Abs(fl.CtmD - lo.CtmD) < 1e-6 && Math.Abs(fl.CtmTx - lo.CtmTx) < 1e-6
                            && Math.Abs(fl.CtmTy - lo.CtmTy) < 1e-6;
                        if (!sameCtm || Math.Abs(fl.TmTy - lo.TmTy) >= 2.0) continue;
                        if (fl.TmXVal <= fo.TmTx) continue;
                        patches[fl.TmXTokStart] = (fl.TmXTokEnd,
                            Encoding.ASCII.GetBytes(FormatNum(fl.TmXVal - delta)));
                    }
                }

                // AdjustSpaceWidth: the words AFTER the replacement sit behind its new
                // advance even when the line is not reflowed. A Tm-positioned line re-states
                // an absolute Tm at the shifted x before every following glyph; on a
                // Td-chained line one relative move carries them all, so nudge the first
                // Td after the match and let the chain do the rest.
                if (ShiftFollowersByAdvance && !ReflowLineOnReplace)
                {
                    var advDelta = advRepl - advMatched;
                    if (Math.Abs(advDelta) > 0.01)
                        for (var j = lastOp + 1; j < textOps.Count; j++)
                        {
                            var fl = textOps[j];
                            if (Math.Abs(fl.TmTy - lo.TmTy) >= 2.0) continue;
                            if (!fl.TdPositioned || fl.TdXTokStart < lastWrite) continue;
                            patches[fl.TdXTokStart] = (fl.TdXTokEnd,
                                Encoding.ASCII.GetBytes(FormatNum(fl.TdXVal + advDelta)));
                            _shiftedFollowers = true;
                            break;
                        }
                }
            }

            // Advance past the matched span (skipped single-op matches still advance
            // to avoid an infinite loop on regex zero-width corner cases).
            (searchIdx, searchLen) = NextMatch(searchIdx + Math.Max(searchLen, 1));
        }

        if (!replaced) return null;

        CopyRange(streamBytes.Length);

        return result.ToArray();
    }

    /// <summary>Per-text-operator record for <see cref="TryCrossOperatorReplace"/>.</summary>
    private sealed class CrossTextOp
    {
        public string Text = "";
        public byte[] Bytes = Array.Empty<byte>();
        public bool IsHex;
        public int OpStart, OpEnd;
        public double TmA = 1, TmB, TmC, TmD = 1, TmTx, TmTy;
        public double CtmA = 1, CtmB, CtmC, CtmD = 1, CtmTx, CtmTy;
        public PdfDictionary? FontDict;
        public string? FontName;
        public Dictionary<int, string>? ToUnicode;
        public double FontSize = 12, Tc, KernSum;
        /// <summary>Each TJ kern paired with the byte offset it applies at, so a prefix
        /// of the run can be measured at the width it actually occupies.</summary>
        public List<(int byteIndex, double amount)>? KernAt;
        public int CharStart = -1;
        public bool TmPositioned;
        public int TmXTokStart, TmXTokEnd;
        public double TmXVal;
        /// <summary>The <c>tx</c> operand of the <c>Td</c>/<c>TD</c> that placed this op,
        /// when one did. A Td-chained line states each glyph RELATIVE to the last, so
        /// adding to this single number carries every later glyph with it - which is how
        /// a follower shift reaches a line that states no absolute Tm of its own.</summary>
        public bool TdPositioned;
        public int TdXTokStart, TdXTokEnd;
        public double TdXVal;
        /// <summary>Byte offset of the enclosing BT keyword (-1 when none seen);
        /// graphics injected for this op (regenerated underlines) go BEFORE it,
        /// since path operators are illegal inside a text object.</summary>
        public int BtStart = -1;
        /// <summary>Set when <see cref="Bytes"/> has been REWRITTEN and no longer spells
        /// what the source operator holds - the cross-break trim is the one edit that does
        /// this to a run other than the head. A run the flow merely MOVES is re-emitted by
        /// copying its original operator bytes verbatim, which would silently discard the
        /// trim, so such a run has to be re-encoded from <see cref="Bytes"/> instead.</summary>
        public bool BytesRewritten;
    }

    /// <summary>A single-rectangle fill (`re` immediately painted by f/f*/F/B/B*/b/b*)
    /// found by <see cref="CollectFillRects"/>: the page-space rect (axis-aligned CTM
    /// assumed) plus the byte span from the `re`'s first operand through the painting
    /// operator, so the whole construct can be deleted from the stream.</summary>
    private sealed class FillRectOp
    {
        public double X, Y, W, H;
        public int SpanStart, SpanEnd;
    }

    /// <summary>
    /// Check that the page-space Y of the current text matrix
    /// (Tm.ty × CTM[3] + CTM[5]) is within tolerance of <see cref="TargetY"/>.
    /// Returns true unconditionally when TargetY is unset (page-wide replace,
    /// the default behaviour). Only handles axis-aligned scale+translate CTMs;
    /// rotation/skew degrades to "no replacement" which is safer than
    /// page-wide for the per-fragment use case.
    /// </summary>
    private bool IsAtTargetY(double tmTx, double tmTy, double ctmB, double ctmD, double ctmTy)
    {
        if (TargetY is not double targetY) return true;
        // Full Y row of the CTM: the ctmB×tmTx cross-term matters on rotated pages
        // (page /Rotate seeds a 90°/270° CTM where Y comes from the text-space X).
        var pageY = ctmB * tmTx + ctmD * tmTy + ctmTy;
        return Math.Abs(pageY - targetY) <= TargetYTolerance;
    }

    /// <summary>
    /// True when <see cref="RequiredRenderMode"/> is unset (mode-agnostic, the
    /// default), or the current text render mode equals it. Lets invisible-fragment
    /// deletion target only the Tr-3 copy of overlapping visible/invisible text.
    /// </summary>
    private bool RenderModeMatches(int renderMode)
        => RequiredRenderMode is not int required || renderMode == required;

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

    /// <summary>Replace matches in <paramref name="text"/> for the current search.
    /// Honours <see cref="ReplaceFirstOnly"/>.</summary>
    private string ApplyReplace(string text, string normalizedSearch, string replacement)
    {
        if (MatchAnyOperator)
        {
            _replacementCount++;
            return replacement;
        }
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

    /// <summary>
    /// Embed a Type0/CID fallback font — from the source font's own family when
    /// installed, else a script-appropriate face — that contains the glyphs for
    /// <paramref name="text"/>, and return its resource name plus the 2-byte
    /// glyph-id string. Used when the source font can't encode a non-Latin1
    /// replacement (Cyrillic/CJK not in its subset) so the run renders AND stays
    /// searchable via the embedder's /ToUnicode CMap. Re-embeds the source font's
    /// own family when installed (e.g. TimesNewRoman / FangSong / SimHei), else a
    /// script-appropriate face. Returns null when no suitable TTF is available
    /// (caller keeps the Standard-14 Latin path).
    /// </summary>
    private static (string resName, byte[] hexIds)? TryEmbedCidFallback(
        PdfDictionary pageDict, PdfReader reader, string text, PdfDictionary? sourceFontDict,
        string? forcedFamily = null)
    {
        var srcFamily = SourceFontFamily(sourceFontDict);
        // A forced family (the explicit-ReplaceFonts assignment redress) replaces the
        // whole family-preserving walk: the substitution scan already chose the face.
        var candidates = forcedFamily is { Length: > 0 }
            ? new List<string?> { forcedFamily }
            : new List<string?> { srcFamily };
        if (forcedFamily is { Length: > 0 })
        {
            candidates.Add("TimesNewRoman");
            candidates.Add("Arial");
            return EmbedFirstCovering(pageDict, reader, text, candidates);
        }
        // A legacy-codepage family name carries a charset suffix the installed face
        // does not ("FangSong_GB2312" names the same family as "FangSong") - the
        // reference preserves the FAMILY: its replacement reads back as FangSong.
        var suffixCut = srcFamily?.IndexOf('_') ?? -1;
        if (suffixCut > 0) candidates.Add(srcFamily!.Substring(0, suffixCut));
        // SimSun is the default Han substitute (even with
        // FangSong available in the SYSTEM, CJK replacements read back as SimSun); a
        // FangSong result comes from the source family above or from a
        // CALLER-REGISTERED source, which outranks the system default (measured:
        // with the test-data Fonts folder registered, a SimHei-sourced replacement
        // whose family is not installed reads back as the folder's FangSong).
        if (ContainsCjk(text))
        {
            if (FontRepository.FindRegisteredCoveringFamily(text) is { } regFamily)
                candidates.Add(regFamily);
            candidates.Add("SimSun");
            candidates.Add("FangSong");
            candidates.Add("MS Gothic");
        }
        // The standard substitute is Times New Roman (probed: a Cyrillic and a Latin-1
        // replacement into an unresolvable CID face both come back in Times); Arial is the
        // last resort for glyphs Times lacks.
        candidates.Add("TimesNewRoman");
        candidates.Add("Arial");

        return EmbedFirstCovering(pageDict, reader, text, candidates);
    }

    /// <summary>Embed the first candidate family that resolves and covers every
    /// non-ASCII glyph of <paramref name="text"/> (first resolvable as best-effort
    /// when none covers), as a Type0/CID subset in the page's font dict.</summary>
    private static (string resName, byte[] hexIds)? EmbedFirstCovering(
        PdfDictionary pageDict, PdfReader reader, string text, List<string?> candidates)
    {
        // The non-ASCII characters that actually need a glyph in the fallback face.
        var need = text.Where(c => c > 0x7F).Distinct().ToArray();

        byte[]? ttf = null;
        var family = "Arial";
        byte[]? firstAvail = null;
        var firstFamily = "";
        foreach (var c in candidates)
        {
            if (string.IsNullOrEmpty(c)) continue;
            byte[]? t;
            try { t = FontRepository.GetTtfDataForSubstitution(c!); } catch { t = null; }
            if (t is not { Length: > 12 }) continue;
            if (firstAvail is null) { firstAvail = t; firstFamily = c!; }
            // Prefer a face that actually covers every needed non-ASCII glyph —
            // the source family may be a Latin-only subset with no Hebrew/CJK.
            try
            {
                var gp = new GlyphOutlineParser(t);
                if (need.All(ch => gp.CMap.TryGetValue(ch, out var g) && g != 0))
                { ttf = t; family = c!; break; }
            }
            catch { /* unparseable — skip */ }
        }
        if (ttf is null) { ttf = firstAvail; family = firstFamily; } // best-effort
        if (ttf is null) return null;

        try
        {
            var fonts = GetOrCreatePageFontDict(pageDict, reader);
            var (resName, hexIds) = Type0FontEmbedder.Embed(fonts, ttf, family, text, stripSpacesInBaseFont: true);
            return (resName, hexIds);
        }
        catch { return null; }
    }

    /// <summary>
    /// Embed Times New Roman as a Type0/Identity-H subset covering <paramref name="text"/>
    /// and return its resource name + 2-byte glyph-id string. Used to font-switch a run
    /// whose source subset lacks glyphs for (Latin) replacement chars by substituting
    /// the whole run in Times. Returns null when Times isn't resolvable.
    /// </summary>
    private static (string resName, byte[] hexIds)? EmbedTimesCidForRun(
        PdfDictionary pageDict, PdfReader reader, string text, PdfDictionary? sourceFontDict)
    {
        // Prefer re-embedding the SOURCE font's own family when it's installed (keep the
        // family, e.g. an Arial subset → Arial), else fall back to Times New Roman
        // (the source family isn't available to expand, e.g. Bookman/Folio not installed).
        byte[]? ttf = null;
        string family = "TimesNewRoman";
        // The stand-in keeps the replaced run's weight and slope, so a bold run does not
        // come back regular; the unstyled names close the list for the faces that do not
        // resolve a styled variant.
        var styled = "TimesNewRoman" + TimesStyleSuffix(sourceFontDict, reader);
        foreach (var fam in new[] { SourceFontFamily(sourceFontDict), styled, "TimesNewRoman", "Times New Roman", "Times" })
        {
            if (string.IsNullOrEmpty(fam)) continue;
            byte[]? t;
            try { t = FontRepository.GetTtfData(fam!); } catch { t = null; }
            if (t is { Length: > 12 }) { ttf = t; family = fam!; break; }
        }
        if (ttf is null) return null;
        try
        {
            var fonts = GetOrCreatePageFontDict(pageDict, reader);
            var emb = Type0FontEmbedder.Embed(fonts, ttf, family, text, stripSpacesInBaseFont: true);
            RecordSwitchedFont(family);
            return emb;
        }
        catch { return null; }
    }

    /// <summary>
    /// Make ResolveFonts accessible for text replacement.
    /// </summary>
    internal static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
        => TextAbsorber.ResolveFonts(pageDict, reader);
}
