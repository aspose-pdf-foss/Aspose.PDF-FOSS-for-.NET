using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Writes one content operation of the re-flowed stream: replaced text with its new advances, everything else verbatim.</summary>
    private bool EmitReflowedOp(ReflowState rf, CrossTextOp o)
    {
        var pl = rf.byOp[o];
        var (tx0, ty0) = SolveTm(rf, o, pl[0].x, BaseY(rf, pl[0].line));
        bool moved = Math.Abs(tx0 - o.TmTx) > 1e-4 || Math.Abs(ty0 - o.TmTy) > 1e-4;
        bool isHead = ReferenceEquals(o, rf.head);
        bool split = pl.Count > 1;
        if (!moved && !isHead && !split)
            return true; // untouched: copied verbatim with the surrounding bytes

        CopyTo(rf, o.OpStart);
        bool wroteTm = false;
        for (int i = 0; i < pl.Count; i++)
        {
            var (px2, li2, bytes2, sw2, off2) = pl[i];
            if (i > 0 || moved || split)
            {
                var (tx, ty) = SolveTm(rf, o, px2, BaseY(rf, li2));
                rf.result.Write(Encoding.ASCII.GetBytes(TmOf(rf, o, tx, ty)));
                wroteTm = true;
            }
            if (sw2 is not null && rf.switchedFace?.TtfData is not null)
            {
                // Font-switched piece: show it in the freshly-embedded Type0
                // subset (2-byte glyph ids), then restore the block's font.
                var pageFonts = GetOrCreatePageFontDict(rf.page.Dict, rf.reader);
                var (resName, hexIds) = Type0FontEmbedder.Embed(
                    pageFonts, rf.switchedFace.TtfData, rf.switchedFamily, sw2,
                    stripSpacesInBaseFont: true);
                rf.result.Write(Encoding.ASCII.GetBytes(
                    $"/{resName} {N(rf, o.FontSize)} Tf <{Compat.ToHexString(hexIds)}> Tj /{o.FontName} {N(rf, o.FontSize)} Tf"));
            }
            else if (isHead || split || o.BytesRewritten)
            {
                // A split piece keeps the TJ kerns that fall inside it. On a
                // justified line those numbers carry most of the inter-word space
                // (around 0.7 em per gap in this corpus), so re-emitting the piece
                // as a bare Tj would visibly close its word gaps and leave the line
                // narrower than every width the wrap was decided with. The kern that
                // sits ON the split boundary belongs to the break and is dropped.
                var inner = KernsInside(rf, o, off2, bytes2.Length);
                if (inner.Count > 0)
                {
                    rf.result.Write("["u8);
                    var at = 0;
                    foreach (var (idx, amount) in inner)
                    {
                        var take = idx - off2 - at;
                        if (take > 0)
                        {
                            WriteStringOperand(rf.result, bytes2[at..(at + take)], o.IsHex);
                            at += take;
                        }
                        rf.result.Write(Encoding.ASCII.GetBytes(N(rf, amount)));
                        rf.result.Write(" "u8);
                    }
                    if (at < bytes2.Length) WriteStringOperand(rf.result, bytes2[at..], o.IsHex);
                    rf.result.Write("] TJ"u8);
                }
                else
                {
                    WriteStringOperand(rf.result, bytes2, o.IsHex);
                    rf.result.Write(" Tj"u8);
                }
            }
            else
            {
                // Moved but INTACT: keep the original operator bytes (kerns and all).
                // A run whose bytes were rewritten took the branch above.
                rf.result.Write(rf.streamBytes, o.OpStart, o.OpEnd - o.OpStart);
            }
        }
        if (wroteTm)
            rf.result.Write(Encoding.ASCII.GetBytes(TmOf(rf, o, o.TmTx, o.TmTy)));
        rf.lastWritePos = o.OpEnd;
        return true;
    }

    /// <summary>Re-flows one affected line: the head line takes the replacement, the following lines take the wrapped remainder.</summary>
    private bool ReflowAffectedLine(ReflowState rf, int j)
    {
        var (o, li, px) = rf.affected[j];
        rf.isHead = j == rf.headIdx;
        rf.wOrig = AdvPage(rf, o, o.Bytes, own: true);
        // A multi-op match folds its consumed ops' span into the head, so the
        // next run's preserved gap is measured from the ORIGINAL match end.
        if (rf.isHead && rf.consumed.Count > 0)
        {
            var lc = rf.consumed[^1];
            rf.wOrig = lc.px + AdvPage(rf, lc.op, lc.op.Bytes, own: true) - px;
        }
        rf.gap = j > 0 && li == rf.prevOrigLine ? px - rf.prevOrigEnd : 0.0;
        if (rf.gap < -1.0 || rf.gap > 3.0 * o.FontSize * TmScaleOf(rf, o) * ScaleOf(rf, o)) rf.gap = 0.0;
        // Crossing a source line break with nothing to separate the words: the break
        // itself was the separator, so it becomes one space. When either side already
        // carries the space 
        // break is already spelled out and adding another would double it.
        if (j > 0 && li != rf.prevOrigLine && rf.prevOrigLine >= 0
            && !o.Text.StartsWith(" ", StringComparison.Ordinal)
            && !(rf.prevOrigText.Length > 0 && char.IsWhiteSpace(rf.prevOrigText[^1])))
            rf.gap = SpaceAdv(rf, o);
        rf.startX = j == 0 ? px : rf.cursor + rf.gap;
        if (rf.isHead && rf.switchedFace is not null)
        {
            if (ReflowSwitchedFaceHead(rf, o, li, px)) return true;
        }
        rf.headParts = new List<(byte[] bytes, int off)>();
        if (rf.isHead)
        {
            int mAt = rf.crossHeadMatchAt >= 0
                ? rf.crossHeadMatchAt
                : rf.seqHeadText is null && rf.head.Bytes.Length == rf.head.Text.Length
                    ? rf.head.Text.IndexOf(rf.search, StringComparison.Ordinal)
                    : -1;
            var repl = mAt >= 0 ? TryEncodeInFont(rf, rf.replacement) : null;
            if (mAt >= 0 && repl is not null)
            {
                if (mAt > 0) rf.headParts.Add((rf.head.Bytes[..mAt], 0));
                rf.headParts.Add((repl, mAt));
                // A cross-line match runs to the end of its own run, so nothing of the
                // head survives past it.
                var after = rf.crossHeadMatchAt >= 0 ? rf.head.Bytes.Length : mAt + rf.search.Length;
                if (after < rf.head.Bytes.Length) rf.headParts.Add((rf.head.Bytes[after..], after));
            }
            else
                rf.headParts.Add((rf.newHeadBytes!, 0));
        }
        else
            rf.headParts.Add((o.Bytes, 0));
        rf.partFirst = true;
        foreach (var (partBytes, partOff) in rf.headParts)
        {
            if (!ReflowHeadPart(rf, partBytes, partOff, o, li, px)) break;
        }
        rf.prevOrigEnd = px + rf.wOrig; rf.prevOrigLine = li; rf.prevOrigText = o.Text;
        return true;
    }

    /// <summary>Turns the re-flowed pieces into the byte inserts and deletions the content stream receives, in stream order.</summary>
    private void BuildReflowInserts(ReflowState rf)
    {
        foreach (var pc in rf.pieces)
        {
            if (!rf.byOp.TryGetValue(pc.op, out var l))
            {
                rf.byOp[pc.op] = l = new List<(double, int, byte[], string?, int)>();
                rf.opOrder.Add(pc.op);
            }
            l.Add((pc.x, pc.line, pc.bytes, pc.sw, pc.off));
        }
        rf.opOrder.Sort((a, b) => a.OpStart.CompareTo(b.OpStart));

        rf.deleteSpans = new List<(int s, int e)>();
        foreach (var c in rf.consumed) rf.deleteSpans.Add((c.op.OpStart, c.op.OpEnd));
        rf.inserts = new List<(int pos, byte[] bytes)>();

        // An operator this pass does NOT rewrite still moves if one before it in the same text
        // block did: showing text advances the pen, so a neighbour whose bytes changed length
        // carries every following show along with it. Pin each such operator to its OWN text
        // matrix — the one it was read at — so it stays where it was drawn. Without this a
        // re-flow drags untouched text off the page (a 612 pt sheet reached 662).
        {
            var rewrittenFrom = new Dictionary<int, int>(); // BtStart -> earliest rewritten OpStart
            foreach (var o in rf.opOrder)
            {
                if (o.BtStart < 0) continue;
                if (!rewrittenFrom.TryGetValue(o.BtStart, out var at) || o.OpStart < at)
                    rewrittenFrom[o.BtStart] = o.OpStart;
            }
            foreach (var o in rf.textOps)
            {
                if (o.BtStart < 0 || rf.byOp.ContainsKey(o)) continue;
                if (!rewrittenFrom.TryGetValue(o.BtStart, out var from) || o.OpStart <= from) continue;
                rf.inserts.Add((o.OpStart, Encoding.ASCII.GetBytes(TmOf(rf, o, o.TmTx, o.TmTy))));
            }
        }

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
            PinUnrewrittenOps(rf);
        }
        rf.deleteSpans.Sort((a, b) => a.s.CompareTo(b.s));
        // Stable sort: two bars inserting before the same BT keep coverage order.
        rf.inserts = rf.inserts
            .Select((g, idx) => (g, idx))
            .OrderBy(t => t.g.pos).ThenBy(t => t.idx)
            .Select(t => t.g)
            .ToList();
    }

    /// <summary>Finds the head line of the match when no line index carried it; false when no operation holds the search string.</summary>
    private bool ResolveReflowHead(ReflowState rf)
    {
        if (rf.headIdx < 0)
        {
            if (!SearchReflowHead(rf)) return false;
        }
        return true;
    }

    /// <summary>Records one text-showing operation's line, x range and bytes for the re-flow.</summary>
    private bool CollectReflowTextOp(ReflowState rf, CrossTextOp o)
    {
        int li = LineOf(rf, o);
        if (li < 0) return true;
        double px = PageX(rf, o);
        if (px < rf.lines[li].lx - 0.5 || px > rf.lines[li].rx + 1.0) return true;
        if (string.IsNullOrEmpty(o.Text)) return true;
        rf.mapped.Add((o, li, px));
        // The caller hands `matchX` as the match LINE's left edge, read from the
        // absorber's fragment view. In a RAGGED-LEFT block that view can name a
        // different line of the paragraph than lines[0] (measured: 80.30
        // against line 0's own 73.58), and the guard below then discards the very run
        // that CARRIES the match - a cross-break occurrence spells the break out, so
        // no single run Contains() it and nothing rescues the run. Read the threshold
        // from line 0 itself, which is the match's line in this view; the two agree in
        // the ordinary case and the guard keeps its meaning.
        double line0Left = Math.Min(rf.matchX, rf.lines[0].lx);
        if (li == 0 && px < line0Left - 0.5)
        {
            // Runs entirely BEFORE the match stay put; the run CONTAINING the
            // match (line drawn as one operator, match mid-run) joins whole —
            // the head rewrite keeps its prefix verbatim at the run's own X.
            if (!o.Text.Contains(rf.search, StringComparison.Ordinal)
                || px + AdvPage(rf, o, o.Bytes, own: true) < rf.matchX + 0.5)
                return true;
        }
        // Type0 (2-byte) runs move whole but are never split or rewritten; the
        // head checks below bail when the MATCH itself sits in a CID run.
        rf.affected.Add((o, li, px));
        return true;
    }

    /// <summary>Pins every operator this pass did not rewrite to its own text matrix, so the re-flow moves only the text it changed.</summary>
    private void PinUnrewrittenOps(ReflowState rf)
    {
        double AdvNoTc(CrossTextOp o2, byte[] bytes2, bool own2)
        {
            var m2 = MetricsOf(rf, o2);
            double w2;
            if (m2 is not null)
            {
                try { w2 = m2.MeasureString(bytes2, o2.FontSize); }
                catch { w2 = o2.FontSize * 0.5 * bytes2.Length; }
            }
            else
                w2 = o2.FontSize * 0.5 * bytes2.Length;
            if (own2) w2 -= o2.KernSum / 1000.0 * o2.FontSize;
            return w2 * TmScaleOf(rf, o2) * ScaleOf(rf, o2);
        }
        var consumedSet = new HashSet<CrossTextOp>();
        foreach (var c in rf.consumed) consumedSet.Add(c.op);
        double fsHead = rf.head.FontSize * TmScaleOf(rf, rf.head) * ScaleOf(rf, rf.head);
        foreach (var r in CollectFillRects(rf.streamBytes))
        {
            if (r.H > 0.15 * fsHead || r.W < 0.5) continue;
            int rli = -1;
            for (int li2 = 0; li2 < rf.lines.Count; li2++)
            {
                // The true op baseline (lines[].y is the absorber's fragment Y,
                // a descent below it).
                double drop = BaseY(rf, li2) - r.Y; // baseline − bar bottom
                if (drop > 0 && drop <= 0.35 * fsHead
                    && r.X + r.W > rf.lines[li2].lx - 0.5 && r.X < rf.lines[li2].rx + 1.0)
                { rli = li2; break; }
            }
            if (rli < 0) continue;
            // Ops the bar covers, at their ORIGINAL positions.
            var covered = new List<(CrossTextOp op, double px)>();
            foreach (var (mo, mli, mpx) in rf.mapped)
            {
                if (mli != rli || consumedSet.Contains(mo) || mo.BtStart < 0) continue;
                double adv = ReferenceEquals(mo, rf.head)
                    ? AdvPage(rf, mo, mo.Bytes, own: true)
                    : AdvNoTc(mo, mo.Bytes, own2: true);
                if (Math.Min(r.X + r.W, mpx + adv) - Math.Max(r.X, mpx) > 0.25)
                    covered.Add((mo, mpx));
            }
            if (covered.Count == 0) continue;
            rf.deleteSpans.Add((r.SpanStart, r.SpanEnd));
            covered.Sort((a, b) => a.px.CompareTo(b.px));
            foreach (var (co, cpx) in covered)
            {
                // Post-reflow placement: the op's first piece; unmoved ops keep
                // their original spot on their own baseline.
                double newX = cpx, baseY = BaseY(rf, rli);
                byte[]? pieceBytes = null; string? pieceSw = null; bool whole = true;
                if (rf.byOp.TryGetValue(co, out var cpl))
                {
                    newX = cpl[0].x; baseY = BaseY(rf, cpl[0].line);
                    pieceBytes = cpl[0].bytes; pieceSw = cpl[0].sw;
                    whole = cpl.Count == 1 && pieceSw is null && !ReferenceEquals(co, rf.head);
                }
                double fsOp = co.FontSize * TmScaleOf(rf, co) * ScaleOf(rf, co);
                double y = baseY - 0.189 * fsOp;
                var sb2 = new StringBuilder();
                if (string.IsNullOrWhiteSpace(co.Text))
                {
                    // Leading newline: the insertion point may abut the previous
                    // token (BtStart precedes the whitespace before BT).
                    sb2.Append("\nq\n0 g\n1 0 0 1 ").Append(N(rf, newX)).Append(' ').Append(N(rf, y))
                       .Append(" cm\nQ\n");
                }
                else
                {
                    double wBar;
                    if (pieceSw is not null && rf.switchedFace is not null)
                    {
                        double wsw;
                        try { wsw = rf.switchedFace.MeasureString(pieceSw, co.FontSize); }
                        catch { wsw = co.FontSize * 0.5 * pieceSw.Length; }
                        wBar = wsw * TmScaleOf(rf, co) * ScaleOf(rf, co);
                    }
                    else if (pieceBytes is not null)
                        wBar = AdvNoTc(co, pieceBytes, own2: whole);
                    else
                        wBar = AdvNoTc(co, co.Bytes, own2: true);
                    sb2.Append("\nq\n0 g\n1 0 0 1 ").Append(N(rf, newX)).Append(' ').Append(N(rf, y))
                       .Append(" cm\n0 0 ").Append(N(rf, wBar)).Append(' ').Append(N(rf, 0.05 * fsOp))
                       .Append(" re\nf*\nQ\n");
                }
                rf.inserts.Add((co.BtStart, Encoding.ASCII.GetBytes(sb2.ToString())));
            }
        }
    }

    /// <summary>Places one part of the re-flowed head text: wrapped onto its line at the pen, in the head's face and size.</summary>
    private bool ReflowHeadPart(ReflowState rf, byte[] partBytes, int partOff, CrossTextOp o, int li, double px)
    {
        if (!rf.partFirst) rf.startX = rf.cursor;
        rf.partFirst = false;
        rf.rest = partBytes;
        rf.wholeOriginal = !rf.isHead && rf.headParts.Count == 1; // full op bytes (kerns apply)
        rf.runStartX = rf.startX;
        // Byte offset of  inside the run, so each piece can be measured
        // with the kerns that belong to it.
        rf.restOff = partOff;
        rf.guard = 0;
        while (true)
        {
            if (!PlaceHeadPartLine(rf, o, li, px)) break;
        }
        return true;
    }

    /// <summary>A head line whose replacement switched face is re-flowed through the substitute face; true when the line was finished here.</summary>
    private bool ReflowSwitchedFaceHead(ReflowState rf, CrossTextOp o, int li, double px)
    {
        rf.matchAt = rf.crossHeadMatchAt >= 0
            ? rf.crossHeadMatchAt
            : rf.seqHeadText is null && rf.head.Bytes.Length == rf.head.Text.Length
                ? rf.head.Text.IndexOf(rf.search, StringComparison.Ordinal)
                : -1;
        rf.headSuffix = Array.Empty<byte>();
        if (rf.matchAt >= 0)
        {
            if (rf.matchAt > 0)
            {
                var prefixBytes = rf.head.Bytes[..rf.matchAt];
                rf.pieces.Add((o, rf.startX, rf.curLi, prefixBytes, null, 0));
                // The surviving prefix keeps its own TJ kerns, which on a JUSTIFIED
                // line carry most of the inter-word space: measuring it without them
                // seats the replacement well left of where its own run began.
                rf.startX += rf.crossHeadMatchAt >= 0
                    ? PieceAdv(rf, o, prefixBytes, 0, prefixBytes.Length)
                    : AdvPage(rf, o, prefixBytes, own: false);
            }
            // A cross-line match runs to the end of its own run: nothing survives it.
            rf.headSuffix = rf.crossHeadMatchAt >= 0
                ? Array.Empty<byte>()
                : rf.head.Bytes[(rf.matchAt + rf.search.Length)..];
        }
        rf.swRest = rf.matchAt >= 0 ? rf.replacement : rf.newHeadText;
        rf.swDoubleSeparator = false;
        {
            var firstSpace = rf.swRest.IndexOf(' ');
            if (firstSpace > 0 && MonospacedRun(rf, o))
            {
                var newWord = 0.0;
                try { newWord = rf.switchedFace!.MeasureString(rf.swRest[..firstSpace], o.FontSize); }
                catch { newWord = 0; }
                var oldWord = OriginalFirstWordWidth(rf, o, rf.search);
                rf.swDoubleSeparator = oldWord > 0 && newWord > oldWord;
            }
        }
        rf.guardSw = 0;
        while (true)
        {
            if (!WrapSwitchedFaceLine(rf, o)) break;
        }
        // The surviving suffix continues from the replacement's end in the ORIGINAL
        // font, wrapping at its own space glyphs like any other retained run.
        if (rf.headSuffix.Length > 0)
        {
            EmitSwitchedFaceSuffix(rf, o);
        }
        rf.prevOrigEnd = px + rf.wOrig; rf.prevOrigLine = li; rf.prevOrigText = o.Text;
        return true;
    }

    /// <summary>Prints the re-flow's notes when the debug variable asks for them.</summary>
    private void ReportReflowDebug(ReflowState rf)
    {
        if (Environment.GetEnvironmentVariable("Q_PIECEDBG") is { Length: > 0 } pdbg)
        {
            var psb = new StringBuilder();
            psb.AppendLine($"--- reflow pass: lines={rf.lines.Count} rightMargin={rf.rightMargin:F3}");
            foreach (var pc in rf.pieces)
            {
                string txt;
                try { txt = pc.sw ?? DecodeString(pc.bytes, pc.op.ToUnicode, pc.op.FontDict, rf.reader); }
                catch { txt = "?"; }
                psb.AppendLine($"    line={pc.line} x={pc.x:F3} baseY={BaseY(rf, pc.line):F2} '{txt}'");
            }
            System.IO.File.AppendAllText(pdbg, psb.ToString());
        }
    }

    /// <summary>Orders the affected operations, picks the head line and its replacement text and face; false when the match cannot be re-flowed.</summary>
    private bool SelectReflowHead(ReflowState rf)
    {
        rf.affected.Sort((a, b) => a.li != b.li ? a.li.CompareTo(b.li) : a.px.CompareTo(b.px));

        // A horizontal gap far wider than a word space is a COLUMN boundary, not a space, and
        // what stands beyond it is not this flow's text. The line view can carry both columns
        // of a two-column row as one line — the absorber joins them across the gap — and
        // repacking through it drags the right-hand column into the left one's flow. The
        // reflow stops at the first such gap.
        for (var j = 1; j < rf.affected.Count; j++)
        {
            if (rf.affected[j].li != rf.affected[j - 1].li) continue;
            var prev = rf.affected[j - 1];
            var prevEnd = prev.px + AdvPage(rf, prev.op, prev.op.Bytes, own: true);
            var columnGap = 3.0 * prev.op.FontSize * TmScaleOf(rf, prev.op) * ScaleOf(rf, prev.op);
            if (rf.affected[j].px - prevEnd <= columnGap) continue;
            rf.affected.RemoveRange(j, rf.affected.Count - j);
            break;
        }

        // The rewritten (head) run is the first line-0 run carrying the match (a prefix
        // inside it is kept). Runs collected ahead of it — e.g. a piece an earlier
        // reflow wrapped in front of a stale match X — flow but keep their bytes.
        // Producers also split a placeholder across CONSECUTIVE operators
        // ("{{" + "Name" + "}}"): then the first op of the spanning sequence is
        // rewritten with the whole replaced text and the rest of the sequence is
        // consumed — its show operators are deleted (the
        // emptied BT..ET shells are kept) and its advance folds into the head's.
        if (rf.affected[0].li != 0) return false;
        rf.headIdx = -1;
        for (int j0 = 0; j0 < rf.affected.Count && rf.affected[j0].li == 0; j0++)
            if (rf.affected[j0].op.Text.Contains(rf.search, StringComparison.Ordinal)) { rf.headIdx = j0; break; }
        rf.consumed = new List<(CrossTextOp op, int li, double px)>();
        rf.seqHeadText = null;
        rf.crossHeadMatchAt = -1;
        if (!ResolveReflowHead(rf)) return false;
        rf.head = rf.affected[rf.headIdx].op;
        if (MetricsOf(rf, rf.head)?.IsCid == true) return false; // rewrite needs 1-byte codes
        rf.newHeadText = rf.seqHeadText
            ?? rf.head.Text.Replace(rf.search, rf.replacement, StringComparison.Ordinal);
        rf.newHeadBytes = null;
        rf.switchedFace = null;
        rf.switchedFamily = "Times New Roman";
        if (HeadWidthsLack(rf, rf.newHeadText))
        {
            var fam = SourceFontFamily(rf.head.FontDict);
            if (!string.IsNullOrEmpty(fam))
            {
                rf.switchedFace = FontRepository.FindFontData(fam!);
                if (rf.switchedFace?.TtfData is not null) rf.switchedFamily = fam!;
            }
            if (rf.switchedFace?.TtfData is null)
                rf.switchedFace = FontRepository.FindFontData("Times New Roman");
            if (rf.switchedFace?.TtfData is null) return false;
        }
        else
        {
            rf.newHeadBytes = TryEncodeInFont(rf, rf.newHeadText);
            if (rf.newHeadBytes is null) return false;
        }
        return true;
    }

    /// <summary>Searches the head line's operations for the match: the operation holding the search string, or the run of operations it crosses; false when none holds it.</summary>
    private bool SearchReflowHead(ReflowState rf)
    {
        rf.cat = new StringBuilder();
        rf.starts = new List<int>();
        rf.l0Count = 0;
        for (int j0 = 0; j0 < rf.affected.Count && rf.affected[j0].li == 0; j0++)
        {
            rf.starts.Add(rf.cat.Length);
            rf.cat.Append(rf.affected[j0].op.Text);
            rf.l0Count++;
        }
        rf.mi = rf.cat.ToString().IndexOf(rf.search, StringComparison.Ordinal);
        rf.mLen = rf.search.Length;
        rf.opCount = rf.l0Count;
        rf.crossedLines = false;
        if (rf.mi < 0)
        {
            // The occurrence can also STRADDLE the paragraph's own line break — the
            // absorber reports it as one match spelling the break out ("leap \ninto
            // electronic"), while the page draws it as runs on two baselines. Extend
            // the concatenation over every line of the flow and match the break as the
            // whitespace it is; the head rewrite then holds the whole replacement and
            // the packer below wraps it back across those baselines.
            rf.cat.Clear();
            rf.starts.Clear();
            var lastLi = -1;
            for (int j0 = 0; j0 < rf.affected.Count; j0++)
            {
                if (lastLi >= 0 && rf.affected[j0].li != lastLi) rf.cat.Append(' ');
                lastLi = rf.affected[j0].li;
                rf.starts.Add(rf.cat.Length);
                rf.cat.Append(rf.affected[j0].op.Text);
            }
            rf.opCount = rf.affected.Count;
            (rf.mi, rf.mLen) = IndexOfAcrossBreaks(rf.cat.ToString(), rf.search);
            if (rf.mi < 0) return false;
            rf.crossedLines = true;
        }
        rf.firstOp = -1;
        rf.lastOp = -1;
        for (int j0 = 0; j0 < rf.opCount; j0++)
        {
            int s = rf.starts[j0], e = s + rf.affected[j0].op.Text.Length;
            if (rf.firstOp < 0 && rf.mi < e) rf.firstOp = j0;
            if (rf.mi + rf.mLen > s) rf.lastOp = j0;
        }
        if (rf.firstOp < 0 || rf.lastOp <= rf.firstOp) return false;
        rf.headIdx = rf.firstOp;
        rf.tailStart = rf.mi + rf.mLen - rf.starts[rf.lastOp];
        if (rf.tailStart < 0) rf.tailStart = 0;
        if (rf.tailStart > rf.affected[rf.lastOp].op.Text.Length) rf.tailStart = rf.affected[rf.lastOp].op.Text.Length;
        rf.headPrefix = rf.affected[rf.firstOp].op.Text[..(rf.mi - rf.starts[rf.firstOp])];
        rf.lastTail = rf.affected[rf.lastOp].op.Text[rf.tailStart..];
        if (rf.crossedLines
            && rf.affected[rf.lastOp].op.Bytes.Length == rf.affected[rf.lastOp].op.Text.Length
            && MetricsOf(rf, rf.affected[rf.lastOp].op)?.IsCid != true)
        {
            ResolveCrossLineHead(rf);
        }
        else
        {
            rf.seqHeadText = rf.headPrefix + rf.replacement + rf.lastTail;
            for (int j0 = rf.firstOp + 1; j0 <= rf.lastOp; j0++) rf.consumed.Add(rf.affected[j0]);
            rf.affected.RemoveRange(rf.firstOp + 1, rf.lastOp - rf.firstOp);
        }
        return true;
    }

    /// <summary>When the match crosses operations, the head is the operation whose text starts it and the rest is consumed.</summary>
    private void ResolveCrossLineHead(ReflowState rf)
    {
        // The match ran off the end of its own line, so the run that finishes it
        // belongs to the NEXT baseline: gluing its surviving tail onto the head
        // would pull that whole line up behind the replacement. Trim the tail run
        // instead and leave it in the flow — the packer then wraps the head across
        // the same two baselines the source used, which is the expected output.
        var tailOp = rf.affected[rf.lastOp].op;
        // Ops consumed by the match run to (but not including) this index.
        int consumeTo = rf.lastOp;
        if (rf.lastTail.Length > 0)
        {
            tailOp.Bytes = tailOp.Bytes[rf.tailStart..];
            tailOp.Text = rf.lastTail;
            tailOp.KernSum = 0;
            tailOp.KernAt = null;
            tailOp.BytesRewritten = true;
        }
        else
        {
            // The match ate the tail run WHOLE ("…leap into" + break + "electronic",
            // where the second line opens with a run that is exactly the rest of the
            // occurrence). Nothing of it survives, so it is consumed like any other
            // run the match spanned. Leaving it out of this branch dropped the whole
            // occurrence into the glue path below, which re-encodes the ENTIRE head
            // run — the untouched prose in front of the match included — in the
            // SUBSTITUTE face, losing the line's own face and its justification
            // kerns and mismeasuring what still fits on it (the expected output
            // keeps "LEAP " on the upper line and spills "INTO ELECTRONIC", we kept
            // "LEAP INTO " and spilled only the last word, 26.28 pt adrift).
            consumeTo = rf.lastOp + 1;
        }
        rf.seqHeadText = rf.headPrefix + rf.replacement;
        if (rf.affected[rf.firstOp].op.Bytes.Length == rf.affected[rf.firstOp].op.Text.Length)
            rf.crossHeadMatchAt = rf.mi - rf.starts[rf.firstOp];
        for (int j0 = rf.firstOp + 1; j0 < consumeTo; j0++) rf.consumed.Add(rf.affected[j0]);
        if (consumeTo > rf.firstOp + 1) rf.affected.RemoveRange(rf.firstOp + 1, consumeTo - rf.firstOp - 1);
    }

    /// <summary>The text left after the switched-face head goes back into the original face on the same line.</summary>
    private void EmitSwitchedFaceSuffix(ReflowState rf, CrossTextOp o)
    {
        int sOff = rf.matchAt + rf.search.Length;
        var restBytes = rf.headSuffix;
        double sx = rf.cursor;
        int guardSx = 0;
        while (true)
        {
            if (++guardSx > 64) break;
            double wRest = AdvPage(rf, o, restBytes, own: false);
            if (sx + wRest <= rf.rightMargin + 0.25 || sx <= LeftOf(rf, rf.curLi) + 0.25)
            {
                rf.pieces.Add((o, sx, rf.curLi, restBytes, null, sOff));
                rf.cursor = sx + wRest;
                break;
            }
            int cut = -1;
            double run = 0;
            for (int k3 = 0; k3 < restBytes.Length; k3++)
            {
                run += AdvPage(rf, o, restBytes[k3..(k3 + 1)], own: false);
                if (sx + run > rf.rightMargin + 0.25) break;
                if (rf.head.Text[sOff + k3] == ' ') cut = k3 + 1;
            }
            if (cut <= 0)
            {
                rf.curLi++; sx = LeftOf(rf, rf.curLi);
                continue;
            }
            rf.pieces.Add((o, sx, rf.curLi, restBytes[..cut], null, sOff));
            restBytes = restBytes[cut..];
            sOff += cut;
            rf.curLi++; sx = LeftOf(rf, rf.curLi);
        }
    }

    /// <summary>Fits as much of the switched-face replacement onto the current line as its width allows, wrapping the rest.</summary>
    private bool WrapSwitchedFaceLine(ReflowState rf, CrossTextOp o)
    {
        if (++rf.guardSw > 64) return false;
        double w2 = SwitchedAdv(rf, rf.swRest);
        // The trailing space overhangs the margin rather than breaking the line.
        if (rf.startX + SwitchedAdv(rf, rf.swRest.TrimEnd()) <= rf.rightMargin + 0.25)
        {
            rf.pieces.Add((o, rf.startX, rf.curLi, Array.Empty<byte>(), rf.swRest, 0));
            rf.cursor = rf.startX + w2 + rf.headAdvPad;
            return false;
        }
        int ks = -1;
        {
            double run = 0;
            for (int k2 = 0; k2 < rf.swRest.Length; k2++)
            {
                // A break is offered at a space whose PRECEDING text fits: the
                // space itself may overhang the margin, so charging it first
                // rejects the last word that actually belongs on the line.
                if (rf.swRest[k2] == ' ' && rf.startX + run <= rf.rightMargin + 0.25) ks = k2 + 1;
                double gw;
                try { gw = rf.switchedFace!.MeasureString(rf.swRest[k2].ToString(), o.FontSize); }
                catch { gw = o.FontSize * 0.5; }
                run += (gw + o.Tc) * TmScaleOf(rf, o) * ScaleOf(rf, o);
                if (rf.startX + run > rf.rightMargin + 0.25) break;
            }
        }
        if (ks <= 0 || ks >= rf.swRest.Length)
        {
            if (rf.startX <= LeftOf(rf, rf.curLi) + 0.25)
            {
                rf.pieces.Add((o, rf.startX, rf.curLi, Array.Empty<byte>(), rf.swRest, 0));
                rf.cursor = rf.startX + w2 + rf.headAdvPad;
                return false;
            }
            rf.curLi++; rf.startX = LeftOf(rf, rf.curLi);
            return true;
        }
        rf.pieces.Add((o, rf.startX, rf.curLi, Array.Empty<byte>(), rf.swRest[..ks], 0));
        // Only the separator that follows the SQUEEZED word doubles; a break
        // further along the replacement consumes its space as usual (e.g.
        // match #1 breaks after "INTO " and its spill opens on the E).
        rf.swRest = rf.swDoubleSeparator && ks - 1 == rf.swRest.IndexOf(' ')
            ? rf.swRest[(ks - 1)..]
            : rf.swRest[ks..];
        rf.swDoubleSeparator = false;
        rf.curLi++; rf.startX = LeftOf(rf, rf.curLi);
        return true;
    }

    /// <summary>Places the next fitting run of a head part on the current line, opening a new line when the width is spent.</summary>
    private bool PlaceHeadPartLine(ReflowState rf, CrossTextOp o, int li, double px)
    {
        if (++rf.guard > 64) return false; // runaway split: fall back
        double w = rf.wholeOriginal
            ? AdvPage(rf, o, rf.rest, own: true)
            : rf.curLi == li
                ? PieceAdv(rf, o, rf.rest, rf.restOff, rf.rest.Length)
                : AdvPage(rf, o, rf.rest, own: false);
        if (rf.startX + w - TrailingSpaceAdv(rf, o, rf.rest) <= rf.rightMargin + 0.25)
        {
            NoteMove(rf, o, rf.startX, rf.curLi, li, rf.rest, null);
            rf.pendingPush = null;
            rf.pieces.Add((o, rf.startX, rf.curLi, rf.rest, null, rf.restOff));
            rf.cursor = rf.startX + w;
            return false;
        }
        int k = LastFittingSpace(rf, o, rf.rest, rf.rightMargin + 0.25 - rf.startX, rf.restOff, rf.curLi == li);
        if (k <= 0 || k >= rf.rest.Length)
        {
            return WrapNonFittingRun(rf, o, li, px, w);
        }
        NoteMove(rf, o, rf.startX, rf.curLi, li, rf.rest[..k], null);
        rf.pendingPush = null;
        rf.pieces.Add((o, rf.startX, rf.curLi, rf.rest[..k], null, rf.restOff));
        // Where the split falls, for the note only — `cursor` belongs to the
        // placement loop and is set when the remainder finally lands.
        var splitX = rf.startX + PieceAdv(rf, o, rf.rest, rf.restOff, k);
        rf.rest = rf.rest[k..];
        rf.restOff += k;
        rf.wholeOriginal = false;
        rf.pendingPush = (OrigX(rf, px, splitX), rf.curLi);
        rf.curLi++; rf.startX = LeftOf(rf, rf.curLi);
        return true;
    }

    /// <summary>A run that fits nowhere on the line: split at the last space, push the remainder to the next line, or wrap the whole run; true to continue the part, false when the line is done.</summary>
    private bool WrapNonFittingRun(ReflowState rf, CrossTextOp o, int li, double px, double w)
    {
        if (rf.startX <= LeftOf(rf, rf.curLi) + 0.25)
        {
            // No split point and already at the line start: place whole
            // (a lone over-wide token must not loop).
            NoteMove(rf, o, rf.startX, rf.curLi, li, rf.rest, null);
            rf.pendingPush = null;
            rf.pieces.Add((o, rf.startX, rf.curLi, rf.rest, null, rf.restOff));
            rf.cursor = rf.startX + w;
            return false;
        }
        // Mid-word run boundary: producers split words across operators
        // ("pre" + "-established"). Wrapping this run alone would break
        // the word across lines, so when neither side of the boundary is
        // a space, pull the previous piece's trailing word-fragment down
        // with it (the fragment becomes its own piece at the new line's
        // start and this run re-tries glued behind it).
        bool IsSpaceGlyph(CrossTextOp so, byte b)
        {
            var c = DecodeString(new[] { b }, so.ToUnicode, so.FontDict, rf.reader);
            return string.IsNullOrWhiteSpace(c);
        }
        if (rf.wholeOriginal && rf.rest.Length > 0 && rf.pieces.Count > 0
            && MetricsOf(rf, o)?.IsCid != true
            && !IsSpaceGlyph(o, rf.rest[0]))
        {
            var prevPc = rf.pieces[^1];
            if (prevPc.line == rf.curLi && prevPc.bytes.Length > 1
                && prevPc.sw is null
                && MetricsOf(rf, prevPc.op)?.IsCid != true
                && !IsSpaceGlyph(prevPc.op, prevPc.bytes[^1]))
            {
                int js = LastFittingSpace(rf, prevPc.op, prevPc.bytes, double.MaxValue);
                if (js > 0 && js < prevPc.bytes.Length)
                {
                    var keep = prevPc.bytes[..js];
                    var tail = prevPc.bytes[js..];
                    rf.pieces[^1] = (prevPc.op, prevPc.x, prevPc.line, keep, null, prevPc.off);
                    rf.pendingPush = (PageX(rf, prevPc.op) + AdvPage(rf, prevPc.op, keep, own: false),
                        prevPc.line);
                    rf.curLi++;
                    double nx = LeftOf(rf, rf.curLi);
                    NoteMove(rf, prevPc.op, nx, rf.curLi, prevPc.line, tail, null);
                    rf.pendingPush = null;
                    rf.pieces.Add((prevPc.op, nx, rf.curLi, tail, null, prevPc.off + js));
                    rf.startX = nx + AdvPage(rf, prevPc.op, tail, own: false);
                    return true;
                }
                if (js <= 0 && prevPc.x > LeftOf(rf, prevPc.line) + 0.25)
                {
                    // The previous piece is a spaceless word fragment
                    // ("{{" pulled up alone ahead of its word): move the
                    // WHOLE piece down so the word stays together —
                    // the greedy reflow works at word granularity.
                    rf.pendingPush = (PageX(rf, prevPc.op), prevPc.line);
                    rf.curLi++;
                    double nx = LeftOf(rf, rf.curLi);
                    NoteMove(rf, prevPc.op, nx, rf.curLi, prevPc.line, prevPc.bytes, null);
                    rf.pendingPush = null;
                    rf.pieces[^1] = (prevPc.op, nx, rf.curLi, prevPc.bytes, null, prevPc.off);
                    rf.startX = nx + AdvPage(rf, prevPc.op, prevPc.bytes,
                        own: ReferenceEquals(prevPc.bytes, prevPc.op.Bytes));
                    return true;
                }
            }
        }
        // No split point on this line: wrap the whole remainder.
        rf.pendingPush = (OrigX(rf, px, rf.startX), rf.curLi);
        rf.curLi++; rf.startX = LeftOf(rf, rf.curLi);
        return true;
    }
}
