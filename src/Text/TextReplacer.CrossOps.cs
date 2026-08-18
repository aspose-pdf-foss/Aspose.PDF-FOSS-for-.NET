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
        // Seed the CTM with the caller's context (the Do-site CTM when this stream is a
        // recursed Form XObject) so TargetY/TargetX scoping sees page-space positions.
        double ctmA = initCtmA, ctmB = initCtmB, ctmC = initCtmC, ctmD = initCtmD;
        double ctmTx = initCtmTx, ctmTy = initCtmTy;
        var ctmStack = new Stack<(double, double, double, double, double, double)>();
        // Pending positioning-Tm record, consumed by the next text-showing op.
        var pendingTm = (has: false, xStart: 0, xEnd: 0, xVal: 0.0);

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
                    else if (op is "Tj" or "'" && ops2.Count >= 1 && ops2[0].obj is PdfString s)
                    {
                        if (op == "'") { tmTx = -tlLeading * tmC + tmTx; tmTy = -tlLeading * tmD + tmTy; pendingTm.has = false; }
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
                        });
                        pendingTm.has = false;
                    }
                    else if (op == "TJ" && ops2.Count >= 1 && ops2[0].obj is PdfArray tjArr)
                    {
                        var sb = new StringBuilder();
                        var byteBuf = new MemoryStream();
                        double kernSum = 0;
                        bool isHex = false; bool firstStr = true;
                        foreach (var item in tjArr)
                        {
                            if (item is PdfString ps)
                            {
                                sb.Append(DecodeString(ps.Value, curToUnicode, curFontDict, reader));
                                byteBuf.Write(ps.Value, 0, ps.Value.Length);
                                if (firstStr) { isHex = ps.IsHex; firstStr = false; }
                            }
                            else if (item is PdfInteger ki) kernSum += ki.Value;
                            else if (item is PdfReal kr) kernSum += kr.Value;
                        }
                        textOps.Add(new CrossTextOp
                        {
                            Text = sb.ToString(), Bytes = byteBuf.ToArray(), IsHex = isHex,
                            OpStart = ops2[0].startPos, OpEnd = ep,
                            TmA = tmA, TmB = tmB, TmC = tmC, TmD = tmD, TmTx = tmTx, TmTy = tmTy,
                            CtmA = ctmA, CtmB = ctmB, CtmC = ctmC, CtmD = ctmD, CtmTx = ctmTx, CtmTy = ctmTy,
                            FontDict = curFontDict, FontName = curFontName, ToUnicode = curToUnicode,
                            FontSize = curFontSize, Tc = curTc, KernSum = kernSum, BtStart = curBtStart,
                            TmPositioned = pendingTm.has, TmXTokStart = pendingTm.xStart,
                            TmXTokEnd = pendingTm.xEnd, TmXVal = pendingTm.xVal,
                        });
                        pendingTm.has = false;
                    }
                    else if (op == "BT") { tmA = 1; tmB = 0; tmC = 0; tmD = 1; tmTx = 0; tmTy = 0; tlLeading = 0; pendingTm.has = false; curBtStart = sp; }
                    else if ((op == "Td" || op == "TD") && ops2.Count >= 2)
                    {
                        double dx = ToDouble(ops2[0].obj), dy = ToDouble(ops2[1].obj);
                        tmTx = dx * tmA + dy * tmC + tmTx;
                        tmTy = dx * tmB + dy * tmD + tmTy;
                        if (op == "TD") tlLeading = -dy;
                        pendingTm.has = false; // Td-positioned: inherits the line chain, no Tm patch
                    }
                    else if (op == "Tm" && ops2.Count >= 6)
                    {
                        tmA = ToDouble(ops2[0].obj); tmB = ToDouble(ops2[1].obj);
                        tmC = ToDouble(ops2[2].obj); tmD = ToDouble(ops2[3].obj);
                        tmTx = ToDouble(ops2[4].obj); tmTy = ToDouble(ops2[5].obj);
                        pendingTm = (true, ops2[4].startPos, ops2[4].endPos, tmTx);
                    }
                    else if (op == "TL" && ops2.Count >= 1) tlLeading = ToDouble(ops2[0].obj);
                    else if (op == "T*") { tmTx = -tlLeading * tmC + tmTx; tmTy = -tlLeading * tmD + tmTy; pendingTm.has = false; }
                    else if (op == "q") ctmStack.Push((ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy));
                    else if (op == "Q") { if (ctmStack.Count > 0) (ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy) = ctmStack.Pop(); }
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
                        fo.FontName, fo.FontSize, pageDict, reader, "Tj", AllowSubsetGlyphFallback);
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
        public int CharStart = -1;
        public bool TmPositioned;
        public int TmXTokStart, TmXTokEnd;
        public double TmXVal;
        /// <summary>Byte offset of the enclosing BT keyword (-1 when none seen);
        /// graphics injected for this op (regenerated underlines) go BEFORE it,
        /// since path operators are illegal inside a text object.</summary>
        public int BtStart = -1;
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

        var tjRule = TjBreakRuleOf(arr, toUnicode, fontDict, reader);
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfString s)
            {
                var decoded = DecodeString(s.Value, toUnicode, fontDict, reader);
                parts.Add((i, decoded, s.IsHex));
                fullText.Append(decoded);
            }
            else if ((arr[i] is PdfInteger adj && tjRule.Breaks(adj.Value))
                  || (arr[i] is PdfReal adjR && tjRule.Breaks(adjR.Value)))
            {
                if (fullText.Length > 0 && fullText[^1] != ' ')
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
        // Must use the SAME rule as the concatenation loop above — keep in sync.
        var charMap = new List<int>(combinedText.Length);
        var lastMapCh = '\0';
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfString sm)
            {
                var decMap = DecodeString(sm.Value, toUnicode, fontDict, reader);
                for (var k = 0; k < decMap.Length; k++) charMap.Add(i);
                if (decMap.Length > 0) lastMapCh = decMap[^1];
            }
            else if ((arr[i] is PdfInteger ia && tjRule.Breaks(ia.Value))
                  || (arr[i] is PdfReal ra && tjRule.Breaks(ra.Value)))
            {
                if (lastMapCh != '\0' && lastMapCh != ' ')
                {
                    charMap.Add(-1); // synthetic space
                    lastMapCh = ' ';
                }
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
            // (adj ≤ −130 becomes synthetic space). Using chunks of |adj| ≤ 120
            // keeps each step below the threshold while still summing to the
            // needed advance correction. Only negative (push-right) splitting
            // matters here — positive kernings never trigger the heuristic.
            const int SafeChunk = 120;
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

    /// <summary>True when <paramref name="s"/> contains a right-to-left script
    /// character (Hebrew / Arabic + presentation forms). Such text is frequently
    /// stored in the content stream in VISUAL (reversed) order, so a logical-order
    /// search term won't match the decoded run directly.</summary>
    private static bool IsRtlSearch(string s)
    {
        foreach (var c in s)
            if ((c >= '֐' && c <= '׿')   // Hebrew
                || (c >= '؀' && c <= 'ۿ') // Arabic
                || (c >= 'יִ' && c <= '﻿')) // Hebrew/Arabic presentation forms
                return true;
        return false;
    }

    /// <summary>Resolve the search variant actually present in <paramref name="runText"/>.
    /// Returns the original <paramref name="search"/> when it matches directly; for an
    /// RTL term that doesn't (the run is stored visually reversed) returns the reversed
    /// term when THAT is present, so the visual slice can be matched and replaced.
    /// Regex searches are returned unchanged (RTL-regex is not modelled).</summary>
    private string ResolveRtlSearch(string runText, string search)
    {
        if (_isRegex || string.IsNullOrEmpty(search)) return search;
        if (runText.Contains(search, StringComparison.Ordinal)) return search;
        if (IsRtlSearch(search))
        {
            var rev = new string(search.Reverse().ToArray());
            if (runText.Contains(rev, StringComparison.Ordinal)) return rev;
        }
        return search;
    }

    /// <summary>Check if <paramref name="text"/> contains a match for the current search.</summary>
    private bool MatchesSearch(string text, string normalizedSearch)
    {
        if (ReplaceFirstOnly && _replacementCount > 0) return false;
        if (MatchAnyOperator) return true;
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
    /// Synthetic-space eligibility for a TJ array — MUST stay in sync with
    /// TextFragmentAbsorber/TextAbsorber: one space per
    /// adjustment ≤ −130/1000 em iff the array is "armed" — any ≥2-glyph piece,
    /// or any glyph that is NOT an uppercase letter or punctuation (font type is
    /// irrelevant) — and not the letter-tracking shape (>10 pieces all
    /// single-glyph). The only per-gap suppression is a space glyph immediately
    /// left of the gap.
    /// </summary>
    private static bool TjSynthEligible(PdfArray arr, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader)
    {
        var isType0 = fontDict?.GetName("Subtype") == "Type0";
        var pieces = 0;
        var multiGlyph = false;
        foreach (var el in arr)
            if (el is PdfString ps0)
            {
                pieces++;
                if (ps0.Value.Length >= (isType0 ? 4 : 2)) multiGlyph = true;
            }
        if (pieces < 2) return false;
        // Letter-tracking shape: >10 pieces, all single-glyph → collapse.
        if (pieces > 10 && !multiGlyph) return false;
        if (multiGlyph) return true;
        foreach (var el in arr)
        {
            if (el is not PdfString ps) continue;
            var dec = DecodeString(ps.Value, toUnicode, fontDict, reader);
            if (dec.Length >= 2) return true;
            foreach (var c in dec)
                if (!char.IsUpper(c) && !char.IsPunctuation(c))
                    return true;
        }
        return false;
    }

    /// <summary>Per-array TJ word-break rule for the REPLACE paths. DELIBERATELY
    /// NARROWER than the absorbers' (which add median-relative letter-tracking
    /// breaks and backward-jump breaks): only the corpus-validated armed −130
    /// rule. The absorbers' extra synthetic spaces sit at spliced element
    /// boundaries, and the replace/kern-compensation path re-anchors trailing
    /// text wrongly around them (deleting one bracketed token slid the next
    /// token onto the deleted token's X). A search string containing such a
    /// space simply no-ops here (not found) — safe; a wrong re-anchor moves
    /// text.</summary>
    internal readonly struct TjBreakRule
    {
        public readonly bool Eligible;
        public TjBreakRule(bool eligible) { Eligible = eligible; }
        public bool Breaks(double v) => Eligible && v <= -130;
    }

    private static TjBreakRule TjBreakRuleOf(PdfArray arr, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader)
        => new TjBreakRule(TjSynthEligible(arr, toUnicode, fontDict, reader));

    private static string ConcatenateTJText(PdfArray arr, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader)
    {
        // Armed-array synthetic-space rule (see TjBreakRule for why the
        // absorbers' wider rules are not mirrored here).
        var rule = TjBreakRuleOf(arr, toUnicode, fontDict, reader);
        var sb = new StringBuilder();
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is PdfString s)
            {
                sb.Append(DecodeString(s.Value, toUnicode, fontDict, reader));
            }
            else
            {
                double v = 0;
                if (arr[i] is PdfInteger ai) v = ai.Value;
                else if (arr[i] is PdfReal ar) v = ar.Value;
                if (rule.Breaks(v) && sb.Length > 0 && sb[^1] != ' ')
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

    /// <summary>Resolve (creating if absent) the page/XObject's own /Resources /Font
    /// dictionary so a fallback font can be registered locally.</summary>
    private static PdfDictionary GetOrCreatePageFontDict(PdfDictionary pageDict, PdfReader reader)
    {
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
        return fonts;
    }

    /// <summary>Family name usable for a font lookup, derived from a /BaseFont by
    /// stripping a 6-char subset tag ("ABCDEF+Name").</summary>
    private static string? SourceFontFamily(PdfDictionary? fontDict)
    {
        var bf = fontDict?.GetName("BaseFont");
        if (string.IsNullOrEmpty(bf)) return null;
        var plus = bf!.IndexOf('+');
        if (plus == 6) bf = bf.Substring(plus + 1);
        return bf;
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
            if (ch >= '　' && ch <= '鿿') return true;
        return false;
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
        PdfDictionary pageDict, PdfReader reader, string text, PdfDictionary? sourceFontDict)
    {
        var candidates = new List<string?> { SourceFontFamily(sourceFontDict) };
        // SimSun is the default Han substitute (even with
        // FangSong available, CJK replacements read back as SimSun); a
        // FangSong result comes from the SOURCE font family candidate above.
        if (ContainsCjk(text))
        {
            candidates.Add("SimSun");
            candidates.Add("FangSong");
            candidates.Add("MS Gothic");
        }
        candidates.Add("Arial");
        candidates.Add("TimesNewRoman");

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
            try { t = FontRepository.GetTtfData(c!); } catch { t = null; }
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
    /// Font-switch a TJ whose matched run needs a fallback font, PRESERVING the position
    /// of text that follows the match in the same TJ array. The matched run is re-emitted
    /// in the fallback (CID) font; the trailing run is re-anchored with an ABSOLUTE Tm at
    /// its ORIGINAL local X so a following fragment keeps its
    /// absolute position regardless of the replacement width. Handles the match-at-start
    /// case (no prefix text before the match in the same TJ); returns false otherwise so
    /// the caller flattens the whole TJ (unchanged behaviour).</summary>
    /// <summary>Same-font TJ split for ReplaceAdjustment.None: rewrite the matched span
    /// with the replacement re-encoded in the op's OWN font and re-anchor the trailing
    /// elements at their original absolute Tm X, so trailing text keeps its exact
    /// position regardless of the replacement's width. A compensating kern would keep
    /// the RENDERED position but mislead kern-blind consumers (the extraction rect clip
    /// and sub-run positions walk glyph widths only), so the split is preferred.
    /// Handles matches that start and end at string-element boundaries (the shape
    /// one-char-per-element producers emit); returns false otherwise so the caller
    /// falls back to the kern-compensated array rewrite.</summary>
    /// <summary>Whether a TJ split's re-anchored suffix must be followed by a
    /// line-matrix restore: look ahead for the next operator that consumes text
    /// position. Relative positioning (Td/TD/T*/'/") computes from the Tlm that was
    /// live at the rewritten op, so the restore is REQUIRED — without it the next
    /// Td-positioned line inherits the suffix X and shifts by the re-anchor delta.
    /// A bare show op (Tj/TJ) instead continues from the suffix's pen, so a restore
    /// would misplace it; an absolute Tm, BT/ET, or end-of-stream makes the
    /// clobbered Tlm irrelevant.</summary>
    private static bool NeedsTlmRestore(byte[] streamBytes, int fromPos)
    {
        var lexer = new PdfLexer(streamBytes) { Position = fromPos };
        try
        {
            while (true)
            {
                var token = lexer.NextToken();
                if (token.Kind == TokenKind.Eof) return false;
                if (token.Kind != TokenKind.Keyword) continue;
                switch (token.StringValue)
                {
                    case "Td": case "TD": case "T*": case "'": case "\"":
                        return true;
                    case "Tj": case "TJ": case "Tm": case "BT": case "ET":
                        return false;
                }
            }
        }
        catch { return false; }
    }

    /// <summary>
    /// Split of a TJ/Tj text run around a matched span: the untouched head (original
    /// elements/kerns, possibly ending with a partial string element), the trailing
    /// run to re-anchor (possibly starting with a partial string element), and the
    /// text-space pen advance from the op's origin to the suffix start AS ORIGINALLY
    /// DRAWN (glyph widths + kerns + per-glyph Tc + per-space Tw), so the suffix can
    /// be re-anchored at its exact pre-replacement position.
    /// </summary>
    private sealed class TjSplitPlan
    {
        public PdfArray Head = new();
        public PdfArray Suffix = new();
        public double SuffixAdvX;
        public bool IsHex;
        /// <summary>Pen displacement of the kern elements that separated the
        /// matched run from the suffix (folded into <see cref="SuffixAdvX"/>).</summary>
        public double LeadingGap;
    }

    /// <summary>
    /// Analyze a TJ array (a plain Tj string is a one-element array) for an anchored
    /// split around <paramref name="search"/>. Handles matches that start or end
    /// mid-element by splitting that element's bytes, provided its byte→char mapping
    /// is unambiguous (1 byte/char simple encoding or 2 bytes/char CID). Returns null
    /// when the match isn't found or the boundaries can't be mapped to bytes.
    /// </summary>
    private TjSplitPlan? ComputeTjSplit(PdfArray arr, string search,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, double fontSize,
        double tc, double tw, PdfReader reader)
    {
        if (fontDict is null || fontSize <= 0 || string.IsNullOrEmpty(search)) return null;
        FontMetrics? metrics;
        try { metrics = FontMetrics.FromFontDict(fontDict, reader); } catch { return null; }
        if (metrics is null) return null;
        bool isCid = metrics.IsCid;

        // Advance of a byte run as originally drawn: glyph widths plus per-glyph Tc
        // and, for single-byte encodings, per-space (byte 0x20) Tw — the PDF text
        // state contributions FontMetrics doesn't know about.
        double AdvOf(byte[] bytes)
        {
            if (bytes.Length == 0) return 0;
            double w = metrics!.MeasureString(bytes, fontSize);
            int glyphs = isCid ? bytes.Length / 2 : bytes.Length;
            w += glyphs * tc;
            if (!isCid && tw != 0)
                foreach (var b in bytes)
                    if (b == 0x20) w += tw;
            return w;
        }

        // Per-element char-start in the concatenated text (mirroring ConcatenateTJText's
        // synthetic-space rule) and the pen advance before each element (kern-aware).
        var charStart = new int[arr.Count];
        var localXBefore = new double[arr.Count];
        var decoded = new string?[arr.Count];
        var sb = new StringBuilder();
        var tjRule = TjBreakRuleOf(arr, toUnicode, fontDict, reader);
        double localX = 0;
        for (int i = 0; i < arr.Count; i++)
        {
            charStart[i] = sb.Length; localXBefore[i] = localX;
            if (arr[i] is PdfString s)
            {
                var dec = DecodeString(s.Value, toUnicode, fontDict, reader);
                decoded[i] = dec;
                sb.Append(dec);
                try { localX += AdvOf(s.Value); } catch { return null; }
            }
            else
            {
                double v = arr[i] is PdfInteger ai ? ai.Value : arr[i] is PdfReal ar2 ? ar2.Value : 0;
                if (tjRule.Breaks(v) && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                localX += -v * fontSize / 1000.0;
            }
        }
        var concat = sb.ToString();
        int matchStart = concat.IndexOf(search, StringComparison.Ordinal);
        int matchEnd;
        if (matchStart >= 0)
            matchEnd = matchStart + search.Length;
        else
        {
            // Normalized fallback (Arabic presentation forms): offsets aren't
            // byte-mappable in general, so only the match-at-start shape is kept
            // (pre-existing behaviour).
            var nn = NormalizeForSearch(concat);
            if (nn.IndexOf(NormalizeForSearch(search), StringComparison.Ordinal) != 0) return null;
            matchStart = 0;
            matchEnd = Math.Min(search.Length, concat.Length);
        }

        // Byte offset of a char offset within element i; -1 when the mapping is
        // ambiguous (decoded length doesn't line up with the byte count).
        int ByteOff(int i, int charOff)
        {
            var dec = decoded[i]!;
            var bytes = ((PdfString)arr[i]).Value;
            if (charOff == 0) return 0;
            if (charOff == dec.Length) return bytes.Length;
            if (bytes.Length == dec.Length) return charOff;          // 1 byte/char
            if (bytes.Length == dec.Length * 2) return charOff * 2;  // 2-byte CID
            return -1;
        }

        // Locate the elements containing the match start/end. A boundary that
        // falls exactly between elements belongs to the LATER element for the
        // start (offset 0) and the EARLIER one for the end (offset = length),
        // so partial slices stay minimal.
        int startEl = -1, endEl = -1, startOff = 0, endOff = 0;
        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not PdfString || decoded[i] is null) continue;
            int len = decoded[i]!.Length;
            if (startEl < 0 && matchStart >= charStart[i] && matchStart < charStart[i] + len)
            { startEl = i; startOff = matchStart - charStart[i]; }
            if (matchEnd > charStart[i] && matchEnd <= charStart[i] + len)
            { endEl = i; endOff = matchEnd - charStart[i]; }
        }
        if (startEl < 0) return null;
        if (endEl < 0)
        {
            // Match ends at/after the last text — trailing run is empty only if
            // it really ends past every string element.
            for (int i = arr.Count - 1; i >= 0; i--)
                if (arr[i] is PdfString && decoded[i] is not null)
                {
                    if (matchEnd < charStart[i] + decoded[i]!.Length) return null;
                    endEl = i; endOff = decoded[i]!.Length;
                    break;
                }
            if (endEl < 0) return null;
        }

        int startByte = ByteOff(startEl, startOff);
        int endByte = ByteOff(endEl, endOff);
        if (startByte < 0 || endByte < 0) return null;

        var plan = new TjSplitPlan();
        foreach (var el in arr)
            if (el is PdfString ps0) { plan.IsHex = ps0.IsHex; break; }

        // Head: whole elements before the match plus the pre-match slice.
        for (int i = 0; i < startEl; i++) plan.Head.Add(arr[i]);
        if (startByte > 0)
            plan.Head.Add(new PdfString(((PdfString)arr[startEl]).Value[..startByte], plan.IsHex));

        // Suffix: the post-match slice plus the whole elements after it.
        var endBytes = ((PdfString)arr[endEl]).Value;
        if (endByte < endBytes.Length)
            plan.Suffix.Add(new PdfString(endBytes[endByte..], plan.IsHex));
        for (int i = endEl + 1; i < arr.Count; i++) plan.Suffix.Add(arr[i]);

        plan.SuffixAdvX = localXBefore[endEl] + AdvOf(endBytes[..endByte]);

        // Fold the suffix's LEADING kerns into the anchor advance: the re-anchor Tm
        // must sit at the first trailing GLYPH's position. A kern left at the array
        // head would displace the pen after the Tm, and consumers that take a
        // fragment's origin from the operation start would report the pre-kern
        // position instead of where the trailing text actually is.
        while (plan.Suffix.Count > 0 && plan.Suffix[0] is not PdfString)
        {
            double kv = plan.Suffix[0] is PdfInteger ki2 ? ki2.Value
                : plan.Suffix[0] is PdfReal kr2 ? kr2.Value : 0;
            plan.LeadingGap += -kv * fontSize / 1000.0;
            plan.Suffix.RemoveAt(0);
        }
        plan.SuffixAdvX += plan.LeadingGap;
        return plan;
    }

    /// <summary>Emit the suffix run re-anchored at its original absolute position:
    /// Tm translated along the text matrix's X axis by the original advance, the
    /// suffix TJ, then a Tlm restore when relative positioning follows.</summary>
    private static void WriteReanchoredSuffix(MemoryStream result, TjSplitPlan plan,
        double tmA, double tmB, double tmC, double tmD, double tmTx, double tmTy,
        bool restoreTlm)
    {
        string N(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);
        // The pen advances along the text matrix's X axis: origin' = Tm·(advX, 0).
        // Adding advX to tmTx alone breaks rotated matrices (0 b -c 0), where the
        // advance lands in the Y component through tmB. Leading space: the bytes
        // copied before this op can end in a keyword ("… Tm") with no trailing
        // delimiter, and "Tm0 0.99 …" would lex as an unknown operator.
        double advX = plan.SuffixAdvX;
        result.Write(Encoding.ASCII.GetBytes(
            $" {N(tmA)} {N(tmB)} {N(tmC)} {N(tmD)} {N(tmTx + tmA * advX)} {N(tmTy + tmB * advX)} Tm "));
        WriteTJArray(result, plan.Suffix);
        result.Write(" TJ"u8);
        // Restore the line matrix: the suffix's absolute Tm also moved Tlm, but any
        // following RELATIVE positioning (Td/TD/T*/'/") computes from the Tlm that
        // was live at this op. Without the restore, the next Td-positioned line
        // inherits the suffix X and every later line shifts by the re-anchor delta.
        if (restoreTlm)
            result.Write(Encoding.ASCII.GetBytes(
                $" {N(tmA)} {N(tmB)} {N(tmC)} {N(tmD)} {N(tmTx)} {N(tmTy)} Tm"));
    }

    private bool WriteAnchoredTJSplit(MemoryStream result, PdfArray arr, string search, string replacement,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, double fontSize,
        double tmA, double tmB, double tmC, double tmD, double tmTx, double tmTy,
        double tc, double tw, PdfReader reader, bool restoreTlm)
    {
        var plan = ComputeTjSplit(arr, search, toUnicode, fontDict, fontSize, tc, tw, reader);
        if (plan is null) return false;

        if (replacement.Length > 0
            && NeedsFontSwitch(replacement, toUnicode, fontDict, reader, allowGlyphFallback: false))
            return false;

        // Head: untouched leading run plus the re-encoded replacement, one TJ.
        var headArr = new PdfArray();
        foreach (var el in plan.Head) headArr.Add(el);
        if (replacement.Length > 0)
            headArr.Add(new PdfString(EncodeString(replacement, toUnicode, fontDict), plan.IsHex));
        if (headArr.Count > 0)
        {
            WriteTJArray(result, headArr);
            result.Write(" TJ "u8);
        }

        if (plan.Suffix.Count > 0)
        {
            // A pure deletion re-anchors the suffix at the pen position right
            // after the deleted glyphs' widths: a SMALL kern separating the
            // match from the trailing run is typography deleted with the match,
            // not kept as a gap. A wide kern is layout (a tab-stop / column
            // separator, same idea as the column-kern rule in the font-switch
            // path) — the suffix keeps its original position. (A replacement
            // always keeps the gap — the new text fills the matched span.)
            if (replacement.Length == 0 && plan.LeadingGap < 0.5 * fontSize)
                plan.SuffixAdvX -= plan.LeadingGap;
            WriteReanchoredSuffix(result, plan, tmA, tmB, tmC, tmD, tmTx, tmTy, restoreTlm);
        }
        return true;
    }

    private bool WriteFontSwitchedTJSplit(MemoryStream result, PdfArray arr, string search, string replacement,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, string? fontName, double fontSize,
        double tmA, double tmB, double tmC, double tmD, double tmTx, double tmTy,
        double tc, double tw, PdfReader reader, PdfDictionary pageDict, bool restoreTlm,
        bool anchored)
    {
        if (string.IsNullOrEmpty(fontName)) return false;
        var plan = ComputeTjSplit(arr, search, toUnicode, fontDict, fontSize, tc, tw, reader);
        // No trailing text → nothing to re-anchor → let the caller flatten.
        if (plan is null || plan.Suffix.Count == 0) return false;

        // Under a reflowing mode the run normally flattens (trailing text closes up
        // behind the replacement). But a trailing run separated by a COLUMN-width
        // kern is an independently placed block (a tab-stop / form-column layout),
        // not line flow — it keeps its own position, so split and re-anchor it.
        if (!anchored && plan.LeadingGap < 2 * fontSize) return false;

        // Resolve the switched font BEFORE any output so a failed embed leaves the
        // result stream untouched (the caller then flattens).
        var cid = EmbedTimesCidForRun(pageDict, reader, replacement, fontDict);
        if (cid is not { } c) return false;

        // Untouched leading run replays first (original font still selected), putting
        // the pen exactly at the match start.
        if (plan.Head.Count > 0)
        {
            WriteTJArray(result, plan.Head);
            result.Write(" TJ "u8);
        }

        // Font-switched replacement for the matched run (drawn at the current pen).
        var fs = fontSize.ToString("0.####", CultureInfo.InvariantCulture);
        result.Write(Encoding.ASCII.GetBytes($"/{c.resName} {fs} Tf <"));
        result.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(c.hexIds)));
        result.Write(Encoding.ASCII.GetBytes("> Tj "));

        // Trailing run back in the original font, re-anchored at its original
        // absolute position independent of the replacement width.
        result.Write(Encoding.ASCII.GetBytes($"/{fontName} {fs} Tf"));
        WriteReanchoredSuffix(result, plan, tmA, tmB, tmC, tmD, tmTx, tmTy, restoreTlm);
        return true;
    }

    /// <summary>
    /// Write a font-switched replacement show operator. For non-Latin1 text
    /// (Cyrillic/CJK) embed a Type0 CID fallback so the run renders + is
    /// searchable; otherwise fall back to the Standard-14 Helvetica + Latin1 path
    /// (unchanged behaviour for Latin replacements). Restores the original font
    /// afterwards. <paramref name="showOp"/> is "Tj" or "'".
    /// </summary>
    private static void WriteFontSwitchedReplacement(MemoryStream result, string newText,
        PdfDictionary? currentFontDict, string? currentFontName, double currentFontSize,
        PdfDictionary pageDict, PdfReader reader, string showOp, bool allowGlyphFallback = false)
    {
        var fs = currentFontSize.ToString("F1", CultureInfo.InvariantCulture);
        if (newText.Any(c => c > 0xFF))
        {
            var cid = TryEmbedCidFallback(pageDict, reader, newText, currentFontDict);
            if (cid is { } c)
            {
                result.Write(Encoding.ASCII.GetBytes($"/{c.resName} {fs} Tf <"));
                result.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(c.hexIds)));
                result.Write(Encoding.ASCII.GetBytes($"> {showOp} /{currentFontName} {fs} Tf"));
                return;
            }
        }
        // Latin replacement whose glyphs are absent from the source subset font: substitute
        // the whole run in a Times New Roman Type0/CID subset, so the
        // missing glyphs render AND the run stays searchable via the embedder's /ToUnicode.
        else if (allowGlyphFallback && SimpleFontMissingGlyphChars(currentFontDict, reader, newText).Length > 0)
        {
            var times = EmbedTimesCidForRun(pageDict, reader, newText, currentFontDict);
            if (times is { } t)
            {
                result.Write(Encoding.ASCII.GetBytes($"/{t.resName} {fs} Tf <"));
                result.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(t.hexIds)));
                result.Write(Encoding.ASCII.GetBytes($"> {showOp} /{currentFontName} {fs} Tf"));
                return;
            }
        }
        // Standard-font substitution for a run the source subset can't faithfully show (its
        // glyph is present by width but absent from the font's ToUnicode, so the encoding
        // can't be confirmed). Record the family the fragment should REPORT for the default
        // no-character behaviour (source family if installed, else Times New Roman). This is
        // a REPORT ONLY — the glyphs stay on this cheap Standard-14 path (no font embedded,
        // file size unaffected), and only the TextFragment.Text setter reads the record; the
        // facade ReplaceText path never surfaces it, so its output is byte-for-byte unchanged.
        if (allowGlyphFallback && IsEmbeddedSimpleFont(currentFontDict, reader))
            RecordSwitchedFont(ResolveReportedFallbackFamily(currentFontDict));
        var fallbackFont = EnsureStandardFont(pageDict, reader);
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_REPLDEBUG") == "1")
            Console.Error.WriteLine($"[fallback-emit] newText='{newText}' font={currentFontName} fs={fs}");
        result.Write(Encoding.ASCII.GetBytes($"/{fallbackFont} {fs} Tf "));
        var latin = Encoding.Latin1.GetBytes(newText);
        WriteStringOperand(result, latin, false);
        result.Write(Encoding.ASCII.GetBytes($" {showOp} /{currentFontName} {fs} Tf"));
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
        PdfDictionary? fontDict, PdfReader? reader = null, bool allowGlyphFallback = false)
    {
        var isCid = fontDict?.GetName("Subtype") == "Type0";

        // CID/Type0 fonts use 2-byte character codes.  If there is no ToUnicode
        // map we cannot build a reverse map, so we must switch to a standard font
        // for any replacement text.
        if (isCid && toUnicode is null)
            return true;

        // A simple (non-CID) font with NO ToUnicode is single-byte WinAnsi/Latin1:
        // it physically cannot encode a character outside Latin-1 (> 0xFF), so a
        // Cyrillic/Hebrew/CJK replacement must switch fonts (→ CID fallback in
        // WriteFontSwitchedReplacement). Without this, the reverse-map check below
        // is skipped and EncodeString silently Latin1-encodes the char to '?'.
        if (!isCid && toUnicode is null && text.Any(ch => ch > 0xFF))
            return true;

        if (toUnicode is not null)
        {
            var reverseMap = BuildReverseMap(toUnicode);

            if (text.Any(ch => !reverseMap.ContainsKey(ch.ToString())))
                return true;
        }

        // Base-encoded simple subset that lacks an embedded glyph for a replacement char
        // (a Type1/TrueType subset embeds only the glyphs it draws; the /Widths entry is 0
        // for the rest). Without a switch the missing glyphs render blank. Fires only for a
        // plain base encoding, so /Differences fonts fall through to the remap check below.
        // Gated to the facade ReplaceText path (allowGlyphFallback) — the TextFragment.Text
        // setter manages the font itself, so an auto-switch there would shift following text.
        if (allowGlyphFallback && !isCid && SimpleFontMissingGlyphChars(fontDict, reader, text).Length > 0)
            return true;

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

    /// <summary>
    /// Characters in <paramref name="text"/> for which a base-encoded simple (non-CID)
    /// subset font has NO embedded glyph. A subset embeds only the glyphs it draws and
    /// zeroes the /Widths entry (or omits the code from /FirstChar../LastChar) for the
    /// rest — so a width of 0 / an out-of-range code marks an absent glyph. Only applied
    /// to a plain base encoding (WinAnsi/Standard/MacRoman name, no /Differences); a
    /// /Differences font is left to the remap check in <see cref="NeedsFontSwitch"/> so
    /// this never over-fires. Returns empty when coverage can't be judged (no /Widths,
    /// Type0, unknown encoding) — never guess a switch. Space is ignored (word gap, not
    /// a drawn glyph).
    /// </summary>
    /// <summary>
    /// True when the font is an EMBEDDED (FontFile/2/3) simple (non-Type0) font — a subset
    /// that embeds only the glyphs it draws. When such a font can't faithfully show a
    /// replacement char, the default no-character behaviour substitutes and REPORTS a
    /// fallback face. Used only to gate that report (not the rendering), so it deliberately
    /// covers /Differences subsets too; a non-embedded system-font reference is excluded
    /// (its real installed face has the glyph, so no substitution is reported).
    /// </summary>
    private static bool IsEmbeddedSimpleFont(PdfDictionary? fontDict, PdfReader? reader)
    {
        if (fontDict is null || reader is null) return false;
        if (fontDict.GetName("Subtype") == "Type0") return false;
        var descriptor = reader.ResolveDict(fontDict.Get("FontDescriptor"));
        return descriptor is not null &&
            (descriptor.Get("FontFile") is not null || descriptor.Get("FontFile2") is not null
             || descriptor.Get("FontFile3") is not null);
    }

    /// <summary>The family the fragment should REPORT after a default no-character
    /// substitution: the source font's own family when it's installed (kept, like an
    /// Arial subset → Arial), else Times New Roman (source not available to expand).</summary>
    private static string ResolveReportedFallbackFamily(PdfDictionary? fontDict)
    {
        var src = SourceFontFamily(fontDict);
        if (!string.IsNullOrEmpty(src))
        {
            try { var t = FontRepository.GetTtfData(src!); if (t is { Length: > 12 }) return src!; }
            catch { /* not installed → fall through to Times */ }
        }
        return "TimesNewRoman";
    }

    private static char[] SimpleFontMissingGlyphChars(PdfDictionary? fontDict, PdfReader? reader, string text)
    {
        if (fontDict is null || reader is null) return Array.Empty<char>();
        if (fontDict.GetName("Subtype") == "Type0") return Array.Empty<char>();

        // Only an EMBEDDED font's /Widths tell the truth about glyph presence: a subset
        // embeds only the glyphs it draws (0-width for the rest). A NON-embedded font
        // (a system-font reference like "Arial,Bold") often ships /Widths only for the
        // codes it happens to use, but the real installed face still has every glyph — a
        // 0 width there is missing metadata, not a missing glyph. So gate on an embedded
        // FontFile/FontFile2/FontFile3; otherwise never treat a 0 width as absent.
        var descriptor = reader.ResolveDict(fontDict.Get("FontDescriptor"));
        bool embedded = descriptor is not null &&
            (descriptor.Get("FontFile") is not null || descriptor.Get("FontFile2") is not null
             || descriptor.Get("FontFile3") is not null);
        if (!embedded) return Array.Empty<char>();

        // Only a plain base-encoding name (no Differences) has a code==WinAnsi-byte
        // mapping we can trust here.
        var enc = fontDict.Get("Encoding");
        string? encName = enc as PdfName is { } pn ? pn.Value
            : (reader.ResolveDict(enc)?.Get("Differences") is null
                ? reader.ResolveDict(enc)?.GetName("BaseEncoding")
                : null);
        if (encName is not ("WinAnsiEncoding" or "StandardEncoding" or "MacRomanEncoding"))
            return Array.Empty<char>();

        if (reader.Resolve(fontDict.Get("Widths")) is not PdfArray widths) return Array.Empty<char>();
        if (reader.Resolve(fontDict.Get("FirstChar")) is not PdfInteger fc) return Array.Empty<char>();
        int firstChar = (int)fc.Value;
        int lastChar = firstChar + widths.Count - 1;

        var missing = new List<char>();
        foreach (var ch in text.Distinct())
        {
            if (ch == ' ') continue;
            // Map char → single-byte code. ASCII (0x20-0x7E) is identity under WinAnsi/
            // Standard/MacRoman; Latin-1 (0xA0-0xFF) ≈ WinAnsi. Anything else can't be a
            // single-byte code here, so treat as absent-from-this-font.
            if (ch >= 0x100) { missing.Add(ch); continue; }
            int code = ch;
            if (code < firstChar || code > lastChar) { missing.Add(ch); continue; }
            var w = reader.Resolve(widths[code - firstChar]);
            double width = w is PdfInteger wi ? wi.Value : w is PdfReal wr ? wr.Value : 0;
            if (width == 0) missing.Add(ch);
        }
        return missing.ToArray();
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
        foreach (var fam in new[] { SourceFontFamily(sourceFontDict), "TimesNewRoman", "Times New Roman", "Times" })
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

        static bool IsWs(byte b) => b is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;

        // First choice: whitespace-EI-whitespace. Fallback: EI-whitespace whose
        // following bytes read as ordinary operator text - a Flate inline-image
        // payload can END FLUSH against the EI with no separator before it.
        long fallback = -1;
        while (pos < len - 1)
        {
            if (lexer.ByteAt(pos) == (byte)'E' && lexer.ByteAt(pos + 1) == (byte)'I')
            {
                var after = pos + 2;
                var afterWs = after >= len || IsWs(lexer.ByteAt(after));
                if (afterWs)
                {
                    if (pos > 0 && IsWs(lexer.ByteAt(pos - 1)))
                    {
                        lexer.Position = after;
                        return;
                    }
                    if (fallback < 0)
                    {
                        // Plausibility: the next 16 bytes are printable/whitespace.
                        var ok = true;
                        for (var k = after; k < Math.Min(after + 16, len); k++)
                        {
                            var nb = lexer.ByteAt(k);
                            if (!IsWs(nb) && (nb < 0x20 || nb > 0x7E)) { ok = false; break; }
                        }
                        if (ok) fallback = after;
                    }
                }
            }
            pos++;
        }
        lexer.Position = fallback >= 0 ? fallback : len;
    }

    /// <summary>
    /// Make ResolveFonts accessible for text replacement.
    /// </summary>
    internal static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
        => TextAbsorber.ResolveFonts(pageDict, reader);
}
