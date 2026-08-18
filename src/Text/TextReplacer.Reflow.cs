using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    internal bool ReflowFromMatch(Page page, string search, string replacement,
        double matchX, IReadOnlyList<(double y, double lx, double rx)> lines,
        double leftX, double rightMargin, double pitch, double newLineSpacingFactor = 0)
    {
        ReflowCreatedLines = 0;
        if (lines.Count == 0 || string.IsNullOrEmpty(search)) return false;
        var reader = page.Reader;
        var contentStreams = GetContentStreams(page, reader);
        if (contentStreams.Count == 0) return false;
        var streamBytes = CombineStreams(contentStreams);
        var fonts = TextAbsorber.ResolveFonts(page.Dict, reader);
        var (rA, rB, rC, rD, rTx, rTy) = PageRotationSeed(page);
        var textOps = CollectTextOps(streamBytes, fonts, reader, rA, rB, rC, rD, rTx, rTy);
        if (textOps.Count == 0) return false;

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
        double PageX(CrossTextOp o) => o.CtmA * o.TmTx + o.CtmC * o.TmTy + o.CtmTx;
        double PageY(CrossTextOp o) => o.CtmB * o.TmTx + o.CtmD * o.TmTy + o.CtmTy;
        double ScaleOf(CrossTextOp o)
        {
            var det = Math.Abs(o.CtmA * o.CtmD - o.CtmB * o.CtmC);
            return det > 1e-12 ? Math.Sqrt(det) : 1.0;
        }
        // The text-matrix scale multiplies every advance: producers often set
        // `/F 1 Tf` and carry the size in Tm (e.g. `33 0 0 33 … Tm`).
        double TmScaleOf(CrossTextOp o) => Math.Sqrt(o.TmA * o.TmA + o.TmB * o.TmB);
        // Page-space advance of a byte run under an op's font state (glyph widths +
        // per-glyph Tc; the op's own TJ kerns only when measuring its full bytes).
        double AdvPage(CrossTextOp o, byte[] bytes, bool own)
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
            // Tc applies once per shown GLYPH — a Type0 code is two bytes.
            var glyphs = m?.IsCid == true ? (bytes.Length + 1) / 2 : bytes.Length;
            w += o.Tc * glyphs;
            if (own) w -= o.KernSum / 1000.0 * o.FontSize;
            return w * TmScaleOf(o) * ScaleOf(o);
        }

        // Assign ops to the paragraph lines. The op baseline (Tm origin) sits a couple
        // of points ABOVE the absorber's fragment Y (descent offset), so match by
        // nearest line within half the pitch.
        double yTol = Math.Max(4.0, Math.Min(6.0, pitch * 0.45));
        int LineOf(CrossTextOp o)
        {
            double py = PageY(o); int best = -1; double bestD = yTol;
            for (int li = 0; li < lines.Count; li++)
            {
                double d = Math.Abs(py - lines[li].y);
                if (d < bestD) { bestD = d; best = li; }
            }
            return best;
        }

        var affected = new List<(CrossTextOp op, int li, double px)>();
        // Every op mapped to a paragraph line (including pre-match line-0 runs that
        // never move) — the underline-regeneration pass below re-emits a bar per
        // covered op whether or not the op itself was repositioned.
        var mapped = new List<(CrossTextOp op, int li, double px)>();
        foreach (var o in textOps)
        {
            int li = LineOf(o);
            if (li < 0) continue;
            double px = PageX(o);
            if (px < lines[li].lx - 0.5 || px > lines[li].rx + 1.0) continue;
            if (string.IsNullOrEmpty(o.Text)) continue;
            mapped.Add((o, li, px));
            if (li == 0 && px < matchX - 0.5)
            {
                // Runs entirely BEFORE the match stay put; the run CONTAINING the
                // match (line drawn as one operator, match mid-run) joins whole —
                // the head rewrite keeps its prefix verbatim at the run's own X.
                if (!o.Text.Contains(search, StringComparison.Ordinal)
                    || px + AdvPage(o, o.Bytes, own: true) < matchX + 0.5)
                    continue;
            }
            // Type0 (2-byte) runs move whole but are never split or rewritten; the
            // head checks below bail when the MATCH itself sits in a CID run.
            affected.Add((o, li, px));
        }
        if (affected.Count == 0) return false;
        affected.Sort((a, b) => a.li != b.li ? a.li.CompareTo(b.li) : a.px.CompareTo(b.px));

        // The rewritten (head) run is the first line-0 run carrying the match (a prefix
        // inside it is kept). Runs collected ahead of it — e.g. a piece an earlier
        // reflow wrapped in front of a stale match X — flow but keep their bytes.
        // Producers also split a placeholder across CONSECUTIVE operators
        // ("{{" + "Name" + "}}"): then the first op of the spanning sequence is
        // rewritten with the whole replaced text and the rest of the sequence is
        // consumed — its show operators are deleted (the
        // emptied BT..ET shells are kept) and its advance folds into the head's.
        if (affected[0].li != 0) return false;
        int headIdx = -1;
        for (int j0 = 0; j0 < affected.Count && affected[j0].li == 0; j0++)
            if (affected[j0].op.Text.Contains(search, StringComparison.Ordinal)) { headIdx = j0; break; }
        var consumed = new List<(CrossTextOp op, int li, double px)>();
        string? seqHeadText = null;
        if (headIdx < 0)
        {
            var cat = new StringBuilder();
            var starts = new List<int>();
            int l0Count = 0;
            for (int j0 = 0; j0 < affected.Count && affected[j0].li == 0; j0++)
            {
                starts.Add(cat.Length);
                cat.Append(affected[j0].op.Text);
                l0Count++;
            }
            int mi = cat.ToString().IndexOf(search, StringComparison.Ordinal);
            if (mi < 0) return false;
            int firstOp = -1, lastOp = -1;
            for (int j0 = 0; j0 < l0Count; j0++)
            {
                int s = starts[j0], e = s + affected[j0].op.Text.Length;
                if (firstOp < 0 && mi < e) firstOp = j0;
                if (mi + search.Length > s) lastOp = j0;
            }
            if (firstOp < 0 || lastOp <= firstOp) return false;
            headIdx = firstOp;
            seqHeadText = affected[firstOp].op.Text[..(mi - starts[firstOp])]
                + replacement
                + affected[lastOp].op.Text[(mi + search.Length - starts[lastOp])..];
            for (int j0 = firstOp + 1; j0 <= lastOp; j0++) consumed.Add(affected[j0]);
            affected.RemoveRange(firstOp + 1, lastOp - firstOp);
        }
        var head = affected[headIdx].op;
        if (MetricsOf(head)?.IsCid == true) return false; // rewrite needs 1-byte codes
        var newHeadText = seqHeadText
            ?? head.Text.Replace(search, replacement, StringComparison.Ordinal);
                // Encode the rewritten run in its ORIGINAL font. A subset's ToUnicode often
        // omits the space glyph's code (only 'real' glyphs get mapped), so recover the
        // space code from the paragraph's own bytes: any byte in an affected run of the
        // same font that DECODES space-like is the font's space. Bail (so the caller
        // falls back) when any character has no code at all.
        byte[]? TryEncodeInFont(string text)
        {
            if (head.ToUnicode is null)
            {
                foreach (var ch in text) if (ch > 0xFF) return null;
                return EncodeString(text, null, head.FontDict);
            }
            var rev = BuildReverseMap(head.ToUnicode);
            int spaceCode = -1;
            foreach (var (o, _, _) in affected)
            {
                if (!ReferenceEquals(o.FontDict, head.FontDict)) continue;
                foreach (var b in o.Bytes)
                {
                    var ch = DecodeString(new[] { b }, o.ToUnicode, o.FontDict, reader);
                    if (ch is " " or "\u00A0") { spaceCode = b; break; }
                }
                if (spaceCode >= 0) break;
            }
            var outBytes = new List<byte>(text.Length);
            foreach (var ch in text)
            {
                if (rev.TryGetValue(ch.ToString(), out var code) && code >= 0 && code <= 0xFF)
                    outBytes.Add((byte)code);
                else if ((ch == ' ' || ch == '\u00A0') && spaceCode >= 0)
                    outBytes.Add((byte)spaceCode);
                else
                    return null;
            }
            return outBytes.ToArray();
        }
        // Head font choice: keep the original face iff its OWN
        // width data covers every replacement character (a Word-emitted system-font
        // reference zeroes /Widths for glyphs the doc never drew — digits in a
        // prose-only bold face). Otherwise substitute a fresh system face of the
        // same family+style, embedded as a subsetted Type0/Identity-H, measured by
        // its raw TTF advances (the emitted /W integers are the same
        // fractional hmtx values).
        byte[]? newHeadBytes = null;
        FontData? switchedFace = null;
        string switchedFamily = "Times New Roman";
        // The doc's OWN /Widths are trusted here even for a non-embedded
        // system-font reference: a zero-width replacement char means the
        // face is substituted, not that the metadata is merely sparse. (The facade
        // path's SimpleFontMissingGlyphChars deliberately requires an embedded
        // program — different semantics.)
        bool HeadWidthsLack(string text)
        {
            var fd = head.FontDict;
            if (fd is null || fd.GetName("Subtype") == "Type0") return false;
            if (reader.Resolve(fd.Get("Widths")) is not PdfArray widths) return false;
            if (reader.Resolve(fd.Get("FirstChar")) is not PdfInteger fci) return false;
            int fc = (int)fci.Value;
            foreach (var ch in text)
            {
                if (ch == ' ') continue;
                if (ch >= 0x100) return true;
                if (ch < fc || ch >= fc + widths.Count) return true;
                var wv = reader.Resolve(widths[ch - fc]);
                double width = wv is PdfInteger wi ? wi.Value : wv is PdfReal wr ? wr.Value : 0;
                if (width == 0) return true;
            }
            return false;
        }
        if (HeadWidthsLack(newHeadText))
        {
            var fam = SourceFontFamily(head.FontDict);
            if (!string.IsNullOrEmpty(fam))
            {
                switchedFace = FontRepository.FindFontData(fam!);
                if (switchedFace?.TtfData is not null) switchedFamily = fam!;
            }
            if (switchedFace?.TtfData is null)
                switchedFace = FontRepository.FindFontData("Times New Roman");
            if (switchedFace?.TtfData is null) return false;
        }
        else
        {
            newHeadBytes = TryEncodeInFont(newHeadText);
            if (newHeadBytes is null) return false;
        }
        // Page-space advance of the switched head text: exact TTF advances + Tc per
        // glyph. When the switched replacement SHRINKS the line, the following
        // run's gap is padded by the REPLACED text's Tc allowance as well
        // (followers sit Tc·len(old) right of the drawn end; a growing
        // replacement or an un-switched one abuts exactly).
        double SwitchedAdv(string text)
        {
            double w;
            try { w = switchedFace!.MeasureString(text, head.FontSize); }
            catch { w = head.FontSize * 0.5 * text.Length; }
            w += head.Tc * text.Length;
            return w * TmScaleOf(head) * ScaleOf(head);
        }
        double headAdvPad = switchedFace is not null && replacement.Length < search.Length
            ? head.Tc * search.Length * TmScaleOf(head) * ScaleOf(head)
            : 0.0;

        // Baseline page-Y per line, from each line's first affected op; missing lines
        // (fully emptied by the shift) interpolate from the previous baseline.
        var lineBaseY = new double?[lines.Count];
        foreach (var (o, li, _) in mapped) lineBaseY[li] ??= PageY(o);
        for (int li = 1; li < lines.Count; li++) lineBaseY[li] ??= lineBaseY[li - 1] - pitch;
        // Lines the wrap CREATES (beyond the paragraph's existing baselines) advance by
        // TextReplaceOptions.AdjustmentNewLineSpacing × the match run's page font size
        // when the caller set it; otherwise by the MEAN of the paragraph's pitches below
        // the edited line (a single-line paragraph keeps the caller's
        // 1.2-em fallback). Existing baselines never move.
        double newPitch = newLineSpacingFactor > 0
            ? newLineSpacingFactor * head.FontSize * TmScaleOf(head) * ScaleOf(head)
            : lines.Count >= 2
                ? (lines[0].y - lines[^1].y) / (lines.Count - 1)
                : pitch;
        double BaseY(int li) => li < lines.Count
            ? lineBaseY[li]!.Value
            : lineBaseY[^1]!.Value - newPitch * (li - lines.Count + 1);

        // Greedy repack with MULTI-SPLIT: any run that crosses the right margin \u2014
        // including the rewritten match run, whose replacement can be several lines
        // long \u2014 splits at the last fitting space glyph as many times as needed; each
        // remainder continues from the paragraph's left margin on the next baseline.
        // A split piece re-emits as a plain Tj (the original TJ kerns are dropped for
        // the pieces; sub-point intra-run shifts, invisible to the layout checks).
        int LastFittingSpace(CrossTextOp o, byte[] bytes, double budget)
        {
            var m = MetricsOf(o);
            // CID runs never split: 2-byte codes make per-byte space scanning wrong.
            if (m is null || m.IsCid || bytes.Length < 2) return -1;
            double run = 0; int lastFit = -1;
            for (int k = 0; k < bytes.Length; k++)
            {
                double gw;
                try { gw = m.MeasureString(new[] { bytes[k] }, o.FontSize); }
                catch { gw = o.FontSize * 0.5; }
                run += (gw + o.Tc) * TmScaleOf(o) * ScaleOf(o);
                if (run > budget) break;
                var ch = DecodeString(new[] { bytes[k] }, o.ToUnicode, o.FontDict, reader);
                if (ch is " " or " ") lastFit = k;
            }
            return lastFit < 0 ? -1 : lastFit + 1; // piece keeps the space glyph
        }

        // Wrapped pieces land at their line's ORIGINAL left X (a hanging-indent item's
        // head line is dedented relative to its continuation lines;
        // each existing baseline keeps its own left margin). Lines created beyond the paragraph
        // continue at the last line's indent.
        double LeftOf(int li2) => li2 < lines.Count ? lines[li2].lx : lines[^1].lx;
        var pieces = new List<(CrossTextOp op, double x, int line, byte[] bytes, string? sw)>();
        double cursor = 0; int curLi = 0;
        double prevOrigEnd = 0; int prevOrigLine = -1;
        for (int j = 0; j < affected.Count; j++)
        {
            var (o, li, px) = affected[j];
            bool isHead = j == headIdx;
            double wOrig = AdvPage(o, o.Bytes, own: true);
            // A multi-op match folds its consumed ops' span into the head, so the
            // next run's preserved gap is measured from the ORIGINAL match end.
            if (isHead && consumed.Count > 0)
            {
                var lc = consumed[^1];
                wOrig = lc.px + AdvPage(lc.op, lc.op.Bytes, own: true) - px;
            }
            double gap = j > 0 && li == prevOrigLine ? px - prevOrigEnd : 0.0;
            if (gap < -1.0 || gap > 3.0 * o.FontSize * TmScaleOf(o) * ScaleOf(o)) gap = 0.0;
            double startX = j == 0 ? px : cursor + gap;
            if (isHead && switchedFace is not null)
            {
                // Font-switched head: flow the replacement TEXT, measured with the
                // substitute face's raw TTF advances; split greedily at spaces.
                var swRest = newHeadText;
                int guardSw = 0;
                while (true)
                {
                    if (++guardSw > 64) return false;
                    double w2 = SwitchedAdv(swRest);
                    if (startX + w2 <= rightMargin + 0.25)
                    {
                        pieces.Add((o, startX, curLi, Array.Empty<byte>(), swRest));
                        cursor = startX + w2 + headAdvPad;
                        break;
                    }
                    int ks = -1;
                    {
                        double run = 0;
                        for (int k2 = 0; k2 < swRest.Length; k2++)
                        {
                            double gw;
                            try { gw = switchedFace.MeasureString(swRest[k2].ToString(), o.FontSize); }
                            catch { gw = o.FontSize * 0.5; }
                            run += (gw + o.Tc) * TmScaleOf(o) * ScaleOf(o);
                            if (startX + run > rightMargin + 0.25) break;
                            if (swRest[k2] == ' ') ks = k2 + 1;
                        }
                    }
                    if (ks <= 0 || ks >= swRest.Length)
                    {
                        if (startX <= LeftOf(curLi) + 0.25)
                        {
                            pieces.Add((o, startX, curLi, Array.Empty<byte>(), swRest));
                            cursor = startX + w2 + headAdvPad;
                            break;
                        }
                        curLi++; startX = LeftOf(curLi);
                        continue;
                    }
                    pieces.Add((o, startX, curLi, Array.Empty<byte>(), swRest[..ks]));
                    swRest = swRest[ks..];
                    curLi++; startX = LeftOf(curLi);
                }
                prevOrigEnd = px + wOrig; prevOrigLine = li;
                continue;
            }
            var rest = isHead ? newHeadBytes! : o.Bytes;
            bool wholeOriginal = !isHead; // still the op's full bytes (kerns apply)
            int guard = 0;
            while (true)
            {
                if (++guard > 64) return false; // runaway split: fall back
                double w = AdvPage(o, rest, own: wholeOriginal);
                if (startX + w <= rightMargin + 0.25)
                {
                    pieces.Add((o, startX, curLi, rest, null));
                    cursor = startX + w;
                    break;
                }
                int k = LastFittingSpace(o, rest, rightMargin + 0.25 - startX);
                if (k <= 0 || k >= rest.Length)
                {
                    if (startX <= LeftOf(curLi) + 0.25)
                    {
                        // No split point and already at the line start: place whole
                        // (a lone over-wide token must not loop).
                        pieces.Add((o, startX, curLi, rest, null));
                        cursor = startX + w;
                        break;
                    }
                    // Mid-word run boundary: producers split words across operators
                    // ("pre" + "-established"). Wrapping this run alone would break
                    // the word across lines, so when neither side of the boundary is
                    // a space, pull the previous piece's trailing word-fragment down
                    // with it (the fragment becomes its own piece at the new line's
                    // start and this run re-tries glued behind it).
                    bool IsSpaceGlyph(CrossTextOp so, byte b)
                    {
                        var c = DecodeString(new[] { b }, so.ToUnicode, so.FontDict, reader);
                        return string.IsNullOrWhiteSpace(c);
                    }
                    if (wholeOriginal && rest.Length > 0 && pieces.Count > 0
                        && MetricsOf(o)?.IsCid != true
                        && !IsSpaceGlyph(o, rest[0]))
                    {
                        var prevPc = pieces[^1];
                        if (prevPc.line == curLi && prevPc.bytes.Length > 1
                            && prevPc.sw is null
                            && MetricsOf(prevPc.op)?.IsCid != true
                            && !IsSpaceGlyph(prevPc.op, prevPc.bytes[^1]))
                        {
                            int js = LastFittingSpace(prevPc.op, prevPc.bytes, double.MaxValue);
                            if (js > 0 && js < prevPc.bytes.Length)
                            {
                                var keep = prevPc.bytes[..js];
                                var tail = prevPc.bytes[js..];
                                pieces[^1] = (prevPc.op, prevPc.x, prevPc.line, keep, null);
                                curLi++;
                                double nx = LeftOf(curLi);
                                pieces.Add((prevPc.op, nx, curLi, tail, null));
                                startX = nx + AdvPage(prevPc.op, tail, own: false);
                                continue;
                            }
                            if (js <= 0 && prevPc.x > LeftOf(prevPc.line) + 0.25)
                            {
                                // The previous piece is a spaceless word fragment
                                // ("{{" pulled up alone ahead of its word): move the
                                // WHOLE piece down so the word stays together —
                                // the greedy reflow works at word granularity.
                                curLi++;
                                double nx = LeftOf(curLi);
                                pieces[^1] = (prevPc.op, nx, curLi, prevPc.bytes, null);
                                startX = nx + AdvPage(prevPc.op, prevPc.bytes,
                                    own: ReferenceEquals(prevPc.bytes, prevPc.op.Bytes));
                                continue;
                            }
                        }
                    }
                    // No split point on this line: wrap the whole remainder.
                    curLi++; startX = LeftOf(curLi);
                    continue;
                }
                pieces.Add((o, startX, curLi, rest[..k], null));
                rest = rest[k..];
                wholeOriginal = false;
                curLi++; startX = LeftOf(curLi);
            }
            prevOrigEnd = px + wOrig; prevOrigLine = li;
        }

        // Rewrite the stream. Pieces group back to their source op; ops are edited in
        // byte order regardless of reading order.
        string N(double v) => Math.Round(v, 5).ToString("0.#####", CultureInfo.InvariantCulture);
        (double tx, double ty) SolveTm(CrossTextOp o, double px, double py)
        {
            var det = o.CtmA * o.CtmD - o.CtmB * o.CtmC;
            if (Math.Abs(det) < 1e-12) return (o.TmTx, o.TmTy);
            var dx = px - o.CtmTx; var dy = py - o.CtmTy;
            return ((o.CtmD * dx - o.CtmC * dy) / det, (-o.CtmB * dx + o.CtmA * dy) / det);
        }
        string TmOf(CrossTextOp o, double tx, double ty) =>
            $" {N(o.TmA)} {N(o.TmB)} {N(o.TmC)} {N(o.TmD)} {N(tx)} {N(ty)} Tm ";

        var byOp = new Dictionary<CrossTextOp, List<(double x, int line, byte[] bytes, string? sw)>>();
        var opOrder = new List<CrossTextOp>();
        foreach (var pc in pieces)
        {
            if (!byOp.TryGetValue(pc.op, out var l))
            {
                byOp[pc.op] = l = new List<(double, int, byte[], string?)>();
                opOrder.Add(pc.op);
            }
            l.Add((pc.x, pc.line, pc.bytes, pc.sw));
        }
        opOrder.Sort((a, b) => a.OpStart.CompareTo(b.OpStart));

        // Byte-level edits OUTSIDE the rewritten op spans: consumed multi-op match
        // show-ops are deleted (their BT..ET shells
        // survive), and underline bars are deleted + regenerated (below).
        var deleteSpans = new List<(int s, int e)>();
        foreach (var c in consumed) deleteSpans.Add((c.op.OpStart, c.op.OpEnd));
        var inserts = new List<(int pos, byte[] bytes)>();

        // ---- Underline regeneration ----
        // A Word-style underline is a lone thin filled rect just below a baseline.
        // On the match line and the paragraph lines below it, every such bar whose
        // span covers text ops is deleted (re+paint only, so an already-regenerated
        // group keeps its q/0 g/cm/Q husk) and ONE bar is re-emitted per covered
        // show-op before that op's BT: X = the op's post-reflow start, bottom =
        // baseline − 0.189·fs, H = 0.05·fs, W = the op's re-measured advance (glyph
        // widths − TJ kerns, NO Tc — decimal-exact at tol 0.001).
        // Whitespace-only covered ops get an empty q/0 g/cm/Q group.
        {
            double AdvNoTc(CrossTextOp o2, byte[] bytes2, bool own2)
            {
                var m2 = MetricsOf(o2);
                double w2;
                if (m2 is not null)
                {
                    try { w2 = m2.MeasureString(bytes2, o2.FontSize); }
                    catch { w2 = o2.FontSize * 0.5 * bytes2.Length; }
                }
                else
                    w2 = o2.FontSize * 0.5 * bytes2.Length;
                if (own2) w2 -= o2.KernSum / 1000.0 * o2.FontSize;
                return w2 * TmScaleOf(o2) * ScaleOf(o2);
            }
            var consumedSet = new HashSet<CrossTextOp>();
            foreach (var c in consumed) consumedSet.Add(c.op);
            double fsHead = head.FontSize * TmScaleOf(head) * ScaleOf(head);
            foreach (var r in CollectFillRects(streamBytes))
            {
                if (r.H > 0.15 * fsHead || r.W < 0.5) continue;
                int rli = -1;
                for (int li2 = 0; li2 < lines.Count; li2++)
                {
                    // The true op baseline (lines[].y is the absorber's fragment Y,
                    // a descent below it).
                    double drop = BaseY(li2) - r.Y; // baseline − bar bottom
                    if (drop > 0 && drop <= 0.35 * fsHead
                        && r.X + r.W > lines[li2].lx - 0.5 && r.X < lines[li2].rx + 1.0)
                    { rli = li2; break; }
                }
                if (rli < 0) continue;
                // Ops the bar covers, at their ORIGINAL positions.
                var covered = new List<(CrossTextOp op, double px)>();
                foreach (var (mo, mli, mpx) in mapped)
                {
                    if (mli != rli || consumedSet.Contains(mo) || mo.BtStart < 0) continue;
                    double adv = ReferenceEquals(mo, head)
                        ? AdvPage(mo, mo.Bytes, own: true)
                        : AdvNoTc(mo, mo.Bytes, own2: true);
                    if (Math.Min(r.X + r.W, mpx + adv) - Math.Max(r.X, mpx) > 0.25)
                        covered.Add((mo, mpx));
                }
                if (covered.Count == 0) continue;
                deleteSpans.Add((r.SpanStart, r.SpanEnd));
                covered.Sort((a, b) => a.px.CompareTo(b.px));
                foreach (var (co, cpx) in covered)
                {
                    // Post-reflow placement: the op's first piece; unmoved ops keep
                    // their original spot on their own baseline.
                    double newX = cpx, baseY = BaseY(rli);
                    byte[]? pieceBytes = null; string? pieceSw = null; bool whole = true;
                    if (byOp.TryGetValue(co, out var cpl))
                    {
                        newX = cpl[0].x; baseY = BaseY(cpl[0].line);
                        pieceBytes = cpl[0].bytes; pieceSw = cpl[0].sw;
                        whole = cpl.Count == 1 && pieceSw is null && !ReferenceEquals(co, head);
                    }
                    double fsOp = co.FontSize * TmScaleOf(co) * ScaleOf(co);
                    double y = baseY - 0.189 * fsOp;
                    var sb2 = new StringBuilder();
                    if (string.IsNullOrWhiteSpace(co.Text))
                    {
                        // Leading newline: the insertion point may abut the previous
                        // token (BtStart precedes the whitespace before BT).
                        sb2.Append("\nq\n0 g\n1 0 0 1 ").Append(N(newX)).Append(' ').Append(N(y))
                           .Append(" cm\nQ\n");
                    }
                    else
                    {
                        double wBar;
                        if (pieceSw is not null && switchedFace is not null)
                        {
                            double wsw;
                            try { wsw = switchedFace.MeasureString(pieceSw, co.FontSize); }
                            catch { wsw = co.FontSize * 0.5 * pieceSw.Length; }
                            wBar = wsw * TmScaleOf(co) * ScaleOf(co);
                        }
                        else if (pieceBytes is not null)
                            wBar = AdvNoTc(co, pieceBytes, own2: whole);
                        else
                            wBar = AdvNoTc(co, co.Bytes, own2: true);
                        sb2.Append("\nq\n0 g\n1 0 0 1 ").Append(N(newX)).Append(' ').Append(N(y))
                           .Append(" cm\n0 0 ").Append(N(wBar)).Append(' ').Append(N(0.05 * fsOp))
                           .Append(" re\nf*\nQ\n");
                    }
                    inserts.Add((co.BtStart, Encoding.ASCII.GetBytes(sb2.ToString())));
                }
            }
        }
        deleteSpans.Sort((a, b) => a.s.CompareTo(b.s));
        // Stable sort: two bars inserting before the same BT keep coverage order.
        inserts = inserts
            .Select((g, idx) => (g, idx))
            .OrderBy(t => t.g.pos).ThenBy(t => t.idx)
            .Select(t => t.g)
            .ToList();

        var result = new MemoryStream();
        int lastWritePos = 0;
        int delIdx = 0, insIdx = 0;
        // Copy [lastWritePos, to) verbatim, splicing in the sorted insertions and
        // skipping the sorted deletion spans that fall inside the range.
        void CopyTo(int to)
        {
            while (true)
            {
                int nextDel = delIdx < deleteSpans.Count ? deleteSpans[delIdx].s : int.MaxValue;
                int nextIns = insIdx < inserts.Count ? inserts[insIdx].pos : int.MaxValue;
                int next = Math.Min(nextDel, nextIns);
                if (next >= to) break;
                if (next > lastWritePos)
                    result.Write(streamBytes, lastWritePos, next - lastWritePos);
                if (lastWritePos < next) lastWritePos = next;
                if (nextIns <= nextDel)
                {
                    result.Write(inserts[insIdx].bytes);
                    insIdx++;
                }
                else
                {
                    if (deleteSpans[delIdx].e > lastWritePos) lastWritePos = deleteSpans[delIdx].e;
                    delIdx++;
                }
            }
            if (to > lastWritePos)
            {
                result.Write(streamBytes, lastWritePos, to - lastWritePos);
                lastWritePos = to;
            }
        }

        foreach (var o in opOrder)
        {
            var pl = byOp[o];
            var (tx0, ty0) = SolveTm(o, pl[0].x, BaseY(pl[0].line));
            bool moved = Math.Abs(tx0 - o.TmTx) > 1e-4 || Math.Abs(ty0 - o.TmTy) > 1e-4;
            bool isHead = ReferenceEquals(o, head);
            bool split = pl.Count > 1;
            if (!moved && !isHead && !split)
                continue; // untouched: copied verbatim with the surrounding bytes

            CopyTo(o.OpStart);
            bool wroteTm = false;
            for (int i = 0; i < pl.Count; i++)
            {
                var (px2, li2, bytes2, sw2) = pl[i];
                if (i > 0 || moved || split)
                {
                    var (tx, ty) = SolveTm(o, px2, BaseY(li2));
                    result.Write(Encoding.ASCII.GetBytes(TmOf(o, tx, ty)));
                    wroteTm = true;
                }
                if (sw2 is not null && switchedFace?.TtfData is not null)
                {
                    // Font-switched piece: show it in the freshly-embedded Type0
                    // subset (2-byte glyph ids), then restore the block's font.
                    var pageFonts = GetOrCreatePageFontDict(page.Dict, reader);
                    var (resName, hexIds) = Type0FontEmbedder.Embed(
                        pageFonts, switchedFace.TtfData, switchedFamily, sw2,
                        stripSpacesInBaseFont: true);
                    result.Write(Encoding.ASCII.GetBytes(
                        $"/{resName} {N(o.FontSize)} Tf <{Convert.ToHexString(hexIds)}> Tj /{o.FontName} {N(o.FontSize)} Tf"));
                }
                else if (isHead || split)
                {
                    WriteStringOperand(result, bytes2, o.IsHex);
                    result.Write(" Tj"u8);
                }
                else
                {
                    // Moved but intact: keep the original operator bytes (kerns and all).
                    result.Write(streamBytes, o.OpStart, o.OpEnd - o.OpStart);
                }
            }
            if (wroteTm)
                result.Write(Encoding.ASCII.GetBytes(TmOf(o, o.TmTx, o.TmTy)));
            lastWritePos = o.OpEnd;
        }
        CopyTo(streamBytes.Length);

        int maxLi = 0;
        foreach (var pc in pieces) if (pc.line > maxLi) maxLi = pc.line;
        ReflowCreatedLines = Math.Max(0, maxLi + 1 - lines.Count);

        _replacementCount = 1;
        page.SetContentStream(result.ToArray());
        return true;
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
        // Text render mode (Tr), tracked for RequiredRenderMode scoping. Part of
        // the graphics state, so it saves/restores with q/Q.
        int renderMode = 0;
        var trStack = new Stack<int>();
        // Character (Tc) and word (Tw) spacing, tracked so the anchored TJ/Tj
        // splits can reproduce the original pen advance of a partially-kept
        // run (both are per-glyph contributions the font metrics don't know).
        // Text-state parameters, so they save/restore with q/Q.
        double tcSpacing = 0, twSpacing = 0;
        var spacingStack = new Stack<(double tc, double tw)>();

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
                                && IsAtTargetY(tmTx, tmTy, ctmB, ctmD, ctmTy)
                                && IsAtTargetX(tmTx, tmTy, ctmA, ctmC, ctmTx)
                                && RenderModeMatches(renderMode))
                            {
                                var decoded = DecodeString(str.Value, currentToUnicode, currentFontDict, reader);
                                var normalizedDecoded = NormalizeForSearch(decoded);
                                var effSearch = ResolveRtlSearch(normalizedDecoded, normalizedSearch);
                                if (MatchesSearch(normalizedDecoded, effSearch))
                                {
                                    var newText = ApplyReplace(normalizedDecoded, effSearch, replacement);
                                    if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_REPLDEBUG") == "1")
                                        Console.Error.WriteLine($"[tj-match] decoded='{normalizedDecoded}' search='{effSearch}' new='{newText}' tmY={tmTy:F1} tmX={tmTx:F1}");
                                    // Write everything before this operand
                                    result.Write(streamBytes, lastWritePos, operands[0].startPos - lastWritePos);

                                    if (newText.Length == 0)
                                    {
                                        // Full deletion: normally drop the show operator entirely so no
                                        // empty text-showing operator remains (which would still
                                        // be re-extracted as a zero-length fragment). In redaction
                                        // mode, leave a glyph-less advance so following text on the
                                        // line does not reflow. When KeepEmptyShowOperator is set
                                        // (form-XObject deletion), retain an empty `() Tj` so the
                                        // emptied fragment is still re-extractable as "" — an
                                        // emptied form field stays a zero-length fragment in place.
                                        if (KeepEmptyShowOperator)
                                            result.Write("() Tj"u8);
                                        WriteDeletionAdvance(result, str.Value, currentFontDict, reader, currentFontSize);
                                    }
                                    else if (AnchorTrailingOnReplace && replacement.Length == 0
                                        // Pure deletion of a proper substring under an anchored
                                        // mode: the surviving text must keep its exact position,
                                        // so split around the match and re-anchor the tail at its
                                        // original absolute Tm (same rule as the TJ branch below).
                                        // Checked BEFORE the font-switch branch: a deletion never
                                        // needs a switch — the split re-emits the surviving run
                                        // from its original bytes — while the switch path would
                                        // flatten the survivor at the op start. A plain re-encode
                                        // would likewise slide it left by the removed advance.
                                        && normalizedDecoded.IndexOf(effSearch, StringComparison.Ordinal)
                                            == normalizedDecoded.LastIndexOf(effSearch, StringComparison.Ordinal)
                                        && WriteAnchoredTJSplit(result, new PdfArray { str }, effSearch, replacement,
                                            currentToUnicode, currentFontDict, currentFontSize,
                                            tmA, tmB, tmC, tmD, tmTx, tmTy, tcSpacing, twSpacing, reader,
                                            NeedsTlmRestore(streamBytes, endPos)))
                                    {
                                        // Anchored split written.
                                    }
                                    else if (NeedsFontSwitch(newText, currentToUnicode, currentFontDict, reader, AllowSubsetGlyphFallback))
                                    {
                                        // Anchored modes (None / AdjustSpaceWidth): a match that is a
                                        // proper substring of the shown string must not re-flow the
                                        // rest of the run — split it like the TJ path, font-switch only
                                        // the matched span and re-anchor the tail at its original
                                        // position. Flatten (whole-run re-encode) only when the split
                                        // doesn't apply (no tail, ambiguous mapping, multi-occurrence).
                                        var singleOccurrence = effSearch.Length > 0
                                            && normalizedDecoded.IndexOf(effSearch, StringComparison.Ordinal)
                                                == normalizedDecoded.LastIndexOf(effSearch, StringComparison.Ordinal);
                                        if (!(AnchorTrailingOnReplace && singleOccurrence
                                              && WriteFontSwitchedTJSplit(result,
                                                  new PdfArray { str }, effSearch, replacement,
                                                  currentToUnicode, currentFontDict, currentFontName, currentFontSize,
                                                  tmA, tmB, tmC, tmD, tmTx, tmTy, tcSpacing, twSpacing, reader, pageDict,
                                                  NeedsTlmRestore(streamBytes, endPos), anchored: true)))
                                            WriteFontSwitchedReplacement(result, newText, currentFontDict,
                                                currentFontName, currentFontSize, pageDict, reader, "Tj", AllowSubsetGlyphFallback);
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
                                && IsAtTargetY(tmTx, tmTy, ctmB, ctmD, ctmTy)
                                && IsAtTargetX(tmTx, tmTy, ctmA, ctmC, ctmTx)
                                && RenderModeMatches(renderMode))
                            {
                                // Pre-check: compute what the replaced text would be to decide
                                // font switch BEFORE encoding (avoids round-trip corruption).
                                var tjOrigText = ConcatenateTJText(arr, currentToUnicode, currentFontDict, reader);
                                var tjNormalizedOrig = NormalizeForSearch(tjOrigText);
                                var tjNormalizedSearch = ResolveRtlSearch(tjNormalizedOrig, NormalizeForSearch(search));
                                if (MatchesSearch(tjNormalizedOrig, tjNormalizedSearch))
                                {
                                    var tjReplacedText = MatchAnyOperator
                                        ? replacement
                                        : tjNormalizedOrig.Replace(tjNormalizedSearch, replacement, StringComparison.Ordinal);
                                    result.Write(streamBytes, lastWritePos, operands[0].startPos - lastWritePos);

                                    if (tjReplacedText.Length == 0)
                                    {
                                        // Full deletion: drop the entire TJ operator so no
                                        // empty text-showing operator remains (which would
                                        // still be re-extracted as a zero-length fragment). In
                                        // redaction mode, leave a glyph-less advance so following
                                        // text on the line does not reflow. When
                                        // KeepEmptyShowOperator is set (form-XObject deletion),
                                        // retain an empty `() Tj` so the emptied fragment stays
                                        // re-extractable as "" (see the Tj branch above).
                                        if (KeepEmptyShowOperator)
                                            result.Write("() Tj"u8);
                                        WriteDeletionAdvanceTJ(result, arr, currentFontDict, reader, currentFontSize);
                                        _replacementCount++;
                                    }
                                    else if (AnchorTrailingOnReplace && replacement.Length == 0
                                        // Pure deletion of a proper substring: hoisted above the
                                        // font-switch branch, which a deletion never needs (the
                                        // split re-emits the surviving run from its original
                                        // bytes) but whose empty-replacement embed fails and
                                        // flattens the survivor at the op start.
                                        && tjNormalizedOrig.IndexOf(tjNormalizedSearch, StringComparison.Ordinal)
                                            == tjNormalizedOrig.LastIndexOf(tjNormalizedSearch, StringComparison.Ordinal)
                                        && WriteAnchoredTJSplit(result, arr, search, replacement,
                                            currentToUnicode, currentFontDict, currentFontSize,
                                            tmA, tmB, tmC, tmD, tmTx, tmTy, tcSpacing, twSpacing, reader,
                                            NeedsTlmRestore(streamBytes, endPos)))
                                    {
                                        _replacementCount++;
                                    }
                                    else if (NeedsFontSwitch(tjReplacedText, currentToUnicode, currentFontDict, reader, AllowSubsetGlyphFallback))
                                    {
                                        // Preserve any trailing text's position: split the TJ, font-switch
                                        // only the matched run, re-anchor the rest at its original absolute
                                        // Tm. Only under an anchored mode (None / AdjustSpaceWidth) and a
                                        // single occurrence — a reflowing replacement re-encodes the whole
                                        // run instead, closing the gap when the replacement is narrower.
                                        // Falls back to flattening the whole TJ when the split doesn't apply.
                                        var tjFsSingleOccurrence =
                                            tjNormalizedOrig.IndexOf(tjNormalizedSearch, StringComparison.Ordinal)
                                                == tjNormalizedOrig.LastIndexOf(tjNormalizedSearch, StringComparison.Ordinal);
                                        if (!(tjFsSingleOccurrence
                                              && WriteFontSwitchedTJSplit(result, arr, search, replacement,
                                                currentToUnicode, currentFontDict, currentFontName, currentFontSize,
                                                tmA, tmB, tmC, tmD, tmTx, tmTy, tcSpacing, twSpacing, reader, pageDict,
                                                NeedsTlmRestore(streamBytes, endPos),
                                                anchored: AnchorTrailingOnReplace)))
                                            WriteFontSwitchedReplacement(result, tjReplacedText, currentFontDict,
                                                currentFontName, currentFontSize, pageDict, reader, "Tj", AllowSubsetGlyphFallback);
                                        _replacementCount++;
                                    }
                                    else if (AnchorTrailingOnReplace
                                        // The anchored split rewrites ONE occurrence and re-anchors
                                        // everything after it verbatim, so it must not run when the
                                        // array holds the search more than once (the later matches
                                        // would survive un-replaced inside the re-anchored tail).
                                        // A pure deletion (empty replacement) takes the split too:
                                        // the kern-compensated array rewrite keeps the RENDERED pen
                                        // in place, but consumers that walk glyph widths only would
                                        // read the trailing run shifted into the deleted span.
                                        && tjNormalizedOrig.IndexOf(tjNormalizedSearch, StringComparison.Ordinal)
                                            == tjNormalizedOrig.LastIndexOf(tjNormalizedSearch, StringComparison.Ordinal)
                                        && WriteAnchoredTJSplit(result, arr, search, replacement,
                                            currentToUnicode, currentFontDict, currentFontSize,
                                            tmA, tmB, tmC, tmD, tmTx, tmTy, tcSpacing, twSpacing, reader,
                                            NeedsTlmRestore(streamBytes, endPos)))
                                    {
                                        // ReplaceAdjustment.None with trailing text: split the TJ and
                                        // re-anchor the tail at its ORIGINAL absolute Tm instead of a
                                        // compensating kern — kern-blind consumers (extraction's
                                        // rect clip, sub-run positions) would misplace every glyph
                                        // after a large kern.
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
                                && IsAtTargetY(tmTx, tmTy, ctmB, ctmD, ctmTy)
                                && IsAtTargetX(tmTx, tmTy, ctmA, ctmC, ctmTx)
                                && RenderModeMatches(renderMode))
                            {
                                var decoded = DecodeString(str2.Value, currentToUnicode, currentFontDict, reader);
                                var normalizedDecoded2 = NormalizeForSearch(decoded);
                                if (MatchesSearch(normalizedDecoded2, normalizedSearch))
                                {
                                    var newText = ApplyReplace(normalizedDecoded2, normalizedSearch, replacement);

                                    result.Write(streamBytes, lastWritePos, operands[0].startPos - lastWritePos);
                                    if (NeedsFontSwitch(newText, currentToUnicode, currentFontDict, reader, AllowSubsetGlyphFallback))
                                    {
                                        WriteFontSwitchedReplacement(result, newText, currentFontDict,
                                            currentFontName, currentFontSize, pageDict, reader, "'", AllowSubsetGlyphFallback);
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
                            trStack.Push(renderMode);
                            spacingStack.Push((tcSpacing, twSpacing));
                            break;

                        case "Q":
                            if (ctmStack.Count > 0)
                                (ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy) = ctmStack.Pop();
                            if (trStack.Count > 0)
                                renderMode = trStack.Pop();
                            if (spacingStack.Count > 0)
                                (tcSpacing, twSpacing) = spacingStack.Pop();
                            break;

                        case "Tr":
                            if (operands.Count >= 1)
                                renderMode = (int)ToDouble(operands[0].obj);
                            break;

                        case "Tc":
                            if (operands.Count >= 1)
                                tcSpacing = ToDouble(operands[0].obj);
                            break;

                        case "Tw":
                            if (operands.Count >= 1)
                                twSpacing = ToDouble(operands[0].obj);
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
                                        && reader.ResolveName(xobjStream.Dict, "Subtype") == "Form")
                                    {
                                        var xobjBytes = reader.DecodeStream(xobjStream);
                                        var beforeXobj = _replacementCount;
                                        // A Form XObject without its own /Resources inherits the
                                        // invoking page's (legacy-style PDFs): resolve fonts and
                                        // nested XObjects against the parent dict, else Tf lookups
                                        // inside the form come up empty and the anchored-split /
                                        // metrics paths silently degrade to metric-less rewrites.
                                        var xobjOwnRes = reader.ResolveDict(xobjStream.Dict.Get("Resources"));
                                        var xobjScope = xobjOwnRes is null ? pageDict : xobjStream.Dict;
                                        var xobjReplaced = ReplaceInContentStream(xobjBytes,
                                            search, replacement,
                                            xobjScope, reader, processedXObjects,
                                            ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy);
                                        if (_replacementCount > beforeXobj)
                                        {
                                            xobjStream.Dict.Remove("Filter");
                                            xobjStream.Dict.Remove("DecodeParms");
                                            xobjStream.ReplaceData(xobjReplaced);
                                            // ReplaceData only mutates the in-memory stream;
                                            // Save re-emits an existing object solely when it
                                            // is registered dirty, so an unmarked XObject edit
                                            // silently reverts on save.
                                            if (objNum is int xn)
                                                reader.OwnerDocument?.MarkDirty(xn, xobjStream);
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
            var crossResult = TryCrossOperatorReplace(output, search, replacement, pageDict, reader,
                initCtmA, initCtmB, initCtmC, initCtmD, initCtmTx, initCtmTy);
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
        PdfDictionary pageDict, PdfReader reader,
        double initCtmA = 1, double initCtmB = 0, double initCtmC = 0, double initCtmD = 1,
        double initCtmTx = 0, double initCtmTy = 0)
    {
        var fonts = TextAbsorber.ResolveFonts(pageDict, reader);
        var normalizedSearch = NormalizeForSearch(search);

        // Collect text operators with everything needed to (a) build a gap-aware
        // concatenation for matching, (b) split a partially-matched first/last operator,
        // and (c) re-anchor / shift following same-line runs: decoded text + raw string
        // bytes, byte span, text matrix + CTM, font state (dict/ToUnicode/size/Tc), TJ
        // kern total, and the byte span of the op's positioning Tm x-operand (when the
        // op is Tm-positioned) so a follower's Tm can be rewritten in place.
        var textOps = CollectTextOps(streamBytes, fonts, reader,
            initCtmA, initCtmB, initCtmC, initCtmD, initCtmTx, initCtmTy);
        return TryCrossOperatorReplaceCore(streamBytes, search, replacement, pageDict, reader,
            normalizedSearch, textOps);
    }
}
