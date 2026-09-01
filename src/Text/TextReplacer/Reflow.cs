using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Find <paramref name="needle"/> in <paramref name="hay"/> treating every
    /// run of whitespace in either as one break: the absorber spells a line-straddling
    /// match with the newline it crossed, while the page's own runs carry a plain space
    /// (or nothing at all). Returns the start index and, through
    /// <paramref name="length"/>, how much of the haystack the match covers; -1 when the
    /// text is not there.</summary>
    private static int IndexOfAcrossBreaks(string hay, string needle, out int length)
    {
        length = 0;
        if (string.IsNullOrEmpty(needle)) return -1;
        for (var start = 0; start < hay.Length; start++)
        {
            var i = start;
            var j = 0;
            var ok = true;
            while (j < needle.Length)
            {
                if (char.IsWhiteSpace(needle[j]))
                {
                    while (j < needle.Length && char.IsWhiteSpace(needle[j])) j++;
                    var spanned = 0;
                    while (i < hay.Length && char.IsWhiteSpace(hay[i])) { i++; spanned++; }
                    // A break the page never drew (two runs butted together) still counts:
                    // the newline in the search text stands for the break, not for ink.
                    if (spanned == 0 && i > start && i < hay.Length && j < needle.Length
                        && hay[i] != needle[j]) { ok = false; break; }
                    continue;
                }
                if (i >= hay.Length || hay[i] != needle[j]) { ok = false; break; }
                i++; j++;
            }
            if (!ok) continue;
            length = i - start;
            return start;
        }
        return -1;
    }

    internal bool ReflowFromMatch(Page page, string search, string replacement,
        double matchX, IReadOnlyList<(double y, double lx, double rx)> lines,
        double leftX, double rightMargin, double pitch, double newLineSpacingFactor = 0)
    {
        ReflowCreatedLines = 0;
        if (lines.Count == 0 || string.IsNullOrEmpty(search)) return false;
        var rf = new ReflowState();
        rf.page = page;
        rf.search = search;
        rf.replacement = replacement;
        rf.matchX = matchX;
        rf.lines = lines;
        rf.leftX = leftX;
        rf.rightMargin = rightMargin;
        rf.pitch = pitch;
        rf.newLineSpacingFactor = newLineSpacingFactor;
        rf.reader = rf.page.Reader;
        rf.contentStreams = GetContentStreams(rf.page, rf.reader);
        if (rf.contentStreams.Count == 0) return false;
        rf.streamBytes = CombineStreams(rf.contentStreams);
        rf.fonts = TextAbsorber.ResolveFonts(rf.page.Dict, rf.reader);
        var (rA, rB, rC, rD, rTx, rTy) = PageRotationSeed(rf.page);
        rf.textOps = CollectTextOps(rf.streamBytes, rf.fonts, rf.reader, rA, rB, rC, rD, rTx, rTy);
        if (rf.textOps.Count == 0) return false;

        rf.metricsCache = new Dictionary<PdfDictionary, FontMetrics?>();
        rf.yTol = Math.Max(1.0, Math.Min(3.0, rf.pitch * 0.2));
        rf.affected = new List<(CrossTextOp op, int li, double px)>();
        rf.mapped = new List<(CrossTextOp op, int li, double px)>();
        foreach (var o in rf.textOps)
        {
            int li = LineOf(rf, o);
            if (li < 0) continue;
            double px = PageX(rf, o);
            if (px < rf.lines[li].lx - 0.5 || px > rf.lines[li].rx + 1.0) continue;
            if (string.IsNullOrEmpty(o.Text)) continue;
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
                    continue;
            }
            // Type0 (2-byte) runs move whole but are never split or rewritten; the
            // head checks below bail when the MATCH itself sits in a CID run.
            rf.affected.Add((o, li, px));
        }
        if (rf.affected.Count == 0) return false;
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
        if (rf.headIdx < 0)
        {
            var cat = new StringBuilder();
            var starts = new List<int>();
            int l0Count = 0;
            for (int j0 = 0; j0 < rf.affected.Count && rf.affected[j0].li == 0; j0++)
            {
                starts.Add(cat.Length);
                cat.Append(rf.affected[j0].op.Text);
                l0Count++;
            }
            int mi = cat.ToString().IndexOf(rf.search, StringComparison.Ordinal);
            int mLen = rf.search.Length;
            int opCount = l0Count;
            var crossedLines = false;
            if (mi < 0)
            {
                // The occurrence can also STRADDLE the paragraph's own line break — the
                // absorber reports it as one match spelling the break out ("leap \ninto
                // electronic"), while the page draws it as runs on two baselines. Extend
                // the concatenation over every line of the flow and match the break as the
                // whitespace it is; the head rewrite then holds the whole replacement and
                // the packer below wraps it back across those baselines.
                cat.Clear();
                starts.Clear();
                var lastLi = -1;
                for (int j0 = 0; j0 < rf.affected.Count; j0++)
                {
                    if (lastLi >= 0 && rf.affected[j0].li != lastLi) cat.Append(' ');
                    lastLi = rf.affected[j0].li;
                    starts.Add(cat.Length);
                    cat.Append(rf.affected[j0].op.Text);
                }
                opCount = rf.affected.Count;
                mi = IndexOfAcrossBreaks(cat.ToString(), rf.search, out mLen);
                if (mi < 0) return false;
                crossedLines = true;
            }
            int firstOp = -1, lastOp = -1;
            for (int j0 = 0; j0 < opCount; j0++)
            {
                int s = starts[j0], e = s + rf.affected[j0].op.Text.Length;
                if (firstOp < 0 && mi < e) firstOp = j0;
                if (mi + mLen > s) lastOp = j0;
            }
            if (firstOp < 0 || lastOp <= firstOp) return false;
            rf.headIdx = firstOp;
            var tailStart = mi + mLen - starts[lastOp];
            if (tailStart < 0) tailStart = 0;
            if (tailStart > rf.affected[lastOp].op.Text.Length) tailStart = rf.affected[lastOp].op.Text.Length;
            var headPrefix = rf.affected[firstOp].op.Text[..(mi - starts[firstOp])];
            var lastTail = rf.affected[lastOp].op.Text[tailStart..];
            if (crossedLines
                && rf.affected[lastOp].op.Bytes.Length == rf.affected[lastOp].op.Text.Length
                && MetricsOf(rf, rf.affected[lastOp].op)?.IsCid != true)
            {
                // The match ran off the end of its own line, so the run that finishes it
                // belongs to the NEXT baseline: gluing its surviving tail onto the head
                // would pull that whole line up behind the replacement. Trim the tail run
                // instead and leave it in the flow — the packer then wraps the head across
                // the same two baselines the source used.
                var tailOp = rf.affected[lastOp].op;
                // Ops consumed by the match run to (but not including) this index.
                int consumeTo = lastOp;
                if (lastTail.Length > 0)
                {
                    tailOp.Bytes = tailOp.Bytes[tailStart..];
                    tailOp.Text = lastTail;
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
                    // kerns and mismeasuring what still fits on it (the expected
                    // result keeps "LEAP " on the upper line and spills "INTO ELECTRONIC";
                    // the glue path kept "LEAP INTO " and spilled only the last word,
                    // 26.28 pt adrift).
                    consumeTo = lastOp + 1;
                }
                rf.seqHeadText = headPrefix + rf.replacement;
                if (rf.affected[firstOp].op.Bytes.Length == rf.affected[firstOp].op.Text.Length)
                    rf.crossHeadMatchAt = mi - starts[firstOp];
                for (int j0 = firstOp + 1; j0 < consumeTo; j0++) rf.consumed.Add(rf.affected[j0]);
                if (consumeTo > firstOp + 1) rf.affected.RemoveRange(firstOp + 1, consumeTo - firstOp - 1);
            }
            else
            {
                rf.seqHeadText = headPrefix + rf.replacement + lastTail;
                for (int j0 = firstOp + 1; j0 <= lastOp; j0++) rf.consumed.Add(rf.affected[j0]);
                rf.affected.RemoveRange(firstOp + 1, lastOp - firstOp);
            }
        }
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
        rf.headAdvPad = rf.switchedFace is not null && rf.replacement.Length < rf.search.Length
            ? rf.head.Tc * rf.search.Length * TmScaleOf(rf, rf.head) * ScaleOf(rf, rf.head)
            : 0.0;

        rf.lineBaseY = new double?[rf.lines.Count];
        foreach (var (o, li, _) in rf.mapped) rf.lineBaseY[li] ??= PageY(rf, o);
        for (int li = 1; li < rf.lines.Count; li++) rf.lineBaseY[li] ??= rf.lineBaseY[li - 1] - rf.pitch;
        rf.newPitch = rf.newLineSpacingFactor > 0
            ? rf.newLineSpacingFactor * rf.head.FontSize * TmScaleOf(rf, rf.head) * ScaleOf(rf, rf.head)
            : rf.lines.Count >= 2
                ? (rf.lines[0].y - rf.lines[^1].y) / (rf.lines.Count - 1)
                : rf.pitch;
        rf.pieces = new List<(CrossTextOp op, double x, int line, byte[] bytes, string? sw, int off)>();
        rf.cursor = 0; int curLi = 0;
        rf.prevOrigEnd = 0; int prevOrigLine = -1; string prevOrigText = string.Empty;

        rf.logNotes = rf.page.Reader?.OwnerDocument?.EnableNotificationLogging == true;
        rf.notes = rf.logNotes ? new List<string>() : null;
        rf.pendingPush = null;
        for (int j = 0; j < rf.affected.Count; j++)
        {
            var (o, li, px) = rf.affected[j];
            bool isHead = j == rf.headIdx;
            double wOrig = AdvPage(rf, o, o.Bytes, own: true);
            // A multi-op match folds its consumed ops' span into the head, so the
            // next run's preserved gap is measured from the ORIGINAL match end.
            if (isHead && rf.consumed.Count > 0)
            {
                var lc = rf.consumed[^1];
                wOrig = lc.px + AdvPage(rf, lc.op, lc.op.Bytes, own: true) - px;
            }
            double gap = j > 0 && li == prevOrigLine ? px - rf.prevOrigEnd : 0.0;
            if (gap < -1.0 || gap > 3.0 * o.FontSize * TmScaleOf(rf, o) * ScaleOf(rf, o)) gap = 0.0;
            // Crossing a source line break with nothing to separate the words: the break
            // itself was the separator, so it becomes one space. When either side already
            // carries the space 
            // break is already spelled out and adding another would double it.
            if (j > 0 && li != prevOrigLine && prevOrigLine >= 0
                && !o.Text.StartsWith(" ", StringComparison.Ordinal)
                && !(prevOrigText.Length > 0 && char.IsWhiteSpace(prevOrigText[^1])))
                gap = SpaceAdv(rf, o);
            double startX = j == 0 ? px : rf.cursor + gap;
            if (isHead && rf.switchedFace is not null)
            {
                // Only the REPLACEMENT changes face. The bytes of the run that survive on
                // either side of the match are source text the caller never touched, so they
                // keep their original font resource — the run is split exactly
                // this way (a replacement lands in a fresh "CalibriBold" while the
                // trailing "'s Mental Golf DISC Style" stays in the document's own
                // Calibri-Bold, and its rectangle keeps that descriptor's deeper descent).
                // Re-dressing the survivors as well moved them by the descent difference.
                // Needs a 1:1 byte↔char run (guaranteed non-CID here) and a single-op match.
                int matchAt = rf.crossHeadMatchAt >= 0
                    ? rf.crossHeadMatchAt
                    : rf.seqHeadText is null && rf.head.Bytes.Length == rf.head.Text.Length
                        ? rf.head.Text.IndexOf(rf.search, StringComparison.Ordinal)
                        : -1;
                var headSuffix = Array.Empty<byte>();
                if (matchAt >= 0)
                {
                    if (matchAt > 0)
                    {
                        var prefixBytes = rf.head.Bytes[..matchAt];
                        rf.pieces.Add((o, startX, curLi, prefixBytes, null, 0));
                        // The surviving prefix keeps its own TJ kerns, which on a JUSTIFIED
                        // line carry most of the inter-word space: measuring it without them
                        // seats the replacement well left of where its own run began.
                        startX += rf.crossHeadMatchAt >= 0
                            ? PieceAdv(rf, o, prefixBytes, 0, prefixBytes.Length)
                            : AdvPage(rf, o, prefixBytes, own: false);
                    }
                    // A cross-line match runs to the end of its own run: nothing survives it.
                    headSuffix = rf.crossHeadMatchAt >= 0
                        ? Array.Empty<byte>()
                        : rf.head.Bytes[(matchAt + rf.search.Length)..];
                }
                // Flow the replacement TEXT, measured with the substitute face's raw TTF
                // advances; split greedily at spaces.
                var swRest = matchAt >= 0 ? rf.replacement : rf.newHeadText;
                // A MONOSPACED source keeps its cell grid: where the substituted word is
                // WIDER than the cells it replaces, it is squeezed back with a
                // Tz and the separator that follows it is re-emitted at the head of the next
                // line, so the break carries a space on BOTH sides. Measured on two source
                // documents: one face is DejaVuSansMono ("leap" 24.000 wide against
                // Times "LEAP" 24.90, so it squeezes) and the spill reads " INTO
                // ELECTRONIC", seating the E one space further in - 130.211 from a 101.420
                // left, where a consumed separator gives 127.696. doc2's OpenSans-Light is
                // proportional ("leap" 18.9937) and never squeezes, and its spill reads
                // "INTO ELECTRONIC" with no such space. A replacement whose word is
                // NARROWER than the original is not stretched, and does not double either.
                var swDoubleSeparator = false;
                {
                    var firstSpace = swRest.IndexOf(' ');
                    if (firstSpace > 0 && MonospacedRun(rf, o))
                    {
                        var newWord = 0.0;
                        try { newWord = rf.switchedFace!.MeasureString(swRest[..firstSpace], o.FontSize); }
                        catch { newWord = 0; }
                        var oldWord = OriginalFirstWordWidth(rf, o, rf.search);
                        swDoubleSeparator = oldWord > 0 && newWord > oldWord;
                    }
                }
                int guardSw = 0;
                while (true)
                {
                    if (++guardSw > 64) return false;
                    double w2 = SwitchedAdv(rf, swRest);
                    // The trailing space overhangs the margin rather than breaking the line.
                    if (startX + SwitchedAdv(rf, swRest.TrimEnd()) <= rf.rightMargin + 0.25)
                    {
                        rf.pieces.Add((o, startX, curLi, Array.Empty<byte>(), swRest, 0));
                        rf.cursor = startX + w2 + rf.headAdvPad;
                        break;
                    }
                    int ks = -1;
                    {
                        double run = 0;
                        for (int k2 = 0; k2 < swRest.Length; k2++)
                        {
                            // A break is offered at a space whose PRECEDING text fits: the
                            // space itself may overhang the margin, so charging it first
                            // rejects the last word that actually belongs on the line.
                            if (swRest[k2] == ' ' && startX + run <= rf.rightMargin + 0.25) ks = k2 + 1;
                            double gw;
                            try { gw = rf.switchedFace.MeasureString(swRest[k2].ToString(), o.FontSize); }
                            catch { gw = o.FontSize * 0.5; }
                            run += (gw + o.Tc) * TmScaleOf(rf, o) * ScaleOf(rf, o);
                            if (startX + run > rf.rightMargin + 0.25) break;
                        }
                    }
                    if (ks <= 0 || ks >= swRest.Length)
                    {
                        if (startX <= LeftOf(rf, curLi) + 0.25)
                        {
                            rf.pieces.Add((o, startX, curLi, Array.Empty<byte>(), swRest, 0));
                            rf.cursor = startX + w2 + rf.headAdvPad;
                            break;
                        }
                        curLi++; startX = LeftOf(rf, curLi);
                        continue;
                    }
                    rf.pieces.Add((o, startX, curLi, Array.Empty<byte>(), swRest[..ks], 0));
                    // Only the separator that follows the SQUEEZED word doubles; a break
                    // further along the replacement consumes its space as usual (e.g.
                    // match #1 breaks after "INTO " and its spill opens on the E).
                    swRest = swDoubleSeparator && ks - 1 == swRest.IndexOf(' ')
                        ? swRest[(ks - 1)..]
                        : swRest[ks..];
                    swDoubleSeparator = false;
                    curLi++; startX = LeftOf(rf, curLi);
                }
                // The surviving suffix continues from the replacement's end in the ORIGINAL
                // font, wrapping at its own space glyphs like any other retained run.
                if (headSuffix.Length > 0)
                {
                    int sOff = matchAt + rf.search.Length;
                    var restBytes = headSuffix;
                    double sx = rf.cursor;
                    int guardSx = 0;
                    while (true)
                    {
                        if (++guardSx > 64) break;
                        double wRest = AdvPage(rf, o, restBytes, own: false);
                        if (sx + wRest <= rf.rightMargin + 0.25 || sx <= LeftOf(rf, curLi) + 0.25)
                        {
                            rf.pieces.Add((o, sx, curLi, restBytes, null, sOff));
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
                            curLi++; sx = LeftOf(rf, curLi);
                            continue;
                        }
                        rf.pieces.Add((o, sx, curLi, restBytes[..cut], null, sOff));
                        restBytes = restBytes[cut..];
                        sOff += cut;
                        curLi++; sx = LeftOf(rf, curLi);
                    }
                }
                rf.prevOrigEnd = px + wOrig; prevOrigLine = li; prevOrigText = o.Text;
                continue;
            }
            // The head run is emitted as up to THREE runs: the source bytes that survive
            // BEFORE the match, the replacement, and the source bytes that survive AFTER it.
            // The replacement is written as its own run, and that boundary is what
            // lets a later restyle name it: a replacement glued into one show with the text
            // around it can only be resized by resizing that text too. Needs a 1:1 byte-char
            // run (guaranteed non-CID here) and a single-operator match.
            var headParts = new List<(byte[] bytes, int off)>();
            if (isHead)
            {
                int mAt = rf.crossHeadMatchAt >= 0
                    ? rf.crossHeadMatchAt
                    : rf.seqHeadText is null && rf.head.Bytes.Length == rf.head.Text.Length
                        ? rf.head.Text.IndexOf(rf.search, StringComparison.Ordinal)
                        : -1;
                var repl = mAt >= 0 ? TryEncodeInFont(rf, rf.replacement) : null;
                if (mAt >= 0 && repl is not null)
                {
                    if (mAt > 0) headParts.Add((rf.head.Bytes[..mAt], 0));
                    headParts.Add((repl, mAt));
                    // A cross-line match runs to the end of its own run, so nothing of the
                    // head survives past it.
                    var after = rf.crossHeadMatchAt >= 0 ? rf.head.Bytes.Length : mAt + rf.search.Length;
                    if (after < rf.head.Bytes.Length) headParts.Add((rf.head.Bytes[after..], after));
                }
                else
                    headParts.Add((rf.newHeadBytes!, 0));
            }
            else
                headParts.Add((o.Bytes, 0));
            var partFirst = true;
            foreach (var (partBytes, partOff) in headParts)
            {
                if (!partFirst) startX = rf.cursor;
                partFirst = false;
                    var rest = partBytes;
                    bool wholeOriginal = !isHead && headParts.Count == 1; // full op bytes (kerns apply)
                // Where this run starts in the SHIFTED layout, so a split point can be
                // mapped back to the source document's coordinates for the push-down note.
                double runStartX = startX;
                double OrigX(double shiftedX) => px + (shiftedX - runStartX);
                // Byte offset of  inside the run, so each piece can be measured
                // with the kerns that belong to it.
                    int restOff = partOff;
                int guard = 0;
                while (true)
                {
                    if (++guard > 64) return false; // runaway split: fall back
                    double w = wholeOriginal
                        ? AdvPage(rf, o, rest, own: true)
                        : curLi == li
                            ? PieceAdv(rf, o, rest, restOff, rest.Length)
                            : AdvPage(rf, o, rest, own: false);
                    if (startX + w - TrailingSpaceAdv(rf, o, rest) <= rf.rightMargin + 0.25)
                    {
                        NoteMove(rf, o, startX, curLi, li, rest, null);
                        rf.pendingPush = null;
                        rf.pieces.Add((o, startX, curLi, rest, null, restOff));
                        rf.cursor = startX + w;
                        break;
                    }
                    int k = LastFittingSpace(rf, o, rest, rf.rightMargin + 0.25 - startX, restOff, curLi == li);
                    if (k <= 0 || k >= rest.Length)
                    {
                        if (startX <= LeftOf(rf, curLi) + 0.25)
                        {
                            // No split point and already at the line start: place whole
                            // (a lone over-wide token must not loop).
                            NoteMove(rf, o, startX, curLi, li, rest, null);
                            rf.pendingPush = null;
                            rf.pieces.Add((o, startX, curLi, rest, null, restOff));
                            rf.cursor = startX + w;
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
                            var c = DecodeString(new[] { b }, so.ToUnicode, so.FontDict, rf.reader);
                            return string.IsNullOrWhiteSpace(c);
                        }
                        if (wholeOriginal && rest.Length > 0 && rf.pieces.Count > 0
                            && MetricsOf(rf, o)?.IsCid != true
                            && !IsSpaceGlyph(o, rest[0]))
                        {
                            var prevPc = rf.pieces[^1];
                            if (prevPc.line == curLi && prevPc.bytes.Length > 1
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
                                    curLi++;
                                    double nx = LeftOf(rf, curLi);
                                    NoteMove(rf, prevPc.op, nx, curLi, prevPc.line, tail, null);
                                    rf.pendingPush = null;
                                    rf.pieces.Add((prevPc.op, nx, curLi, tail, null, prevPc.off + js));
                                    startX = nx + AdvPage(rf, prevPc.op, tail, own: false);
                                    continue;
                                }
                                if (js <= 0 && prevPc.x > LeftOf(rf, prevPc.line) + 0.25)
                                {
                                    // The previous piece is a spaceless word fragment
                                    // ("{{" pulled up alone ahead of its word): move the
                                    // WHOLE piece down so the word stays together —
                                    // the greedy reflow works at word granularity.
                                    rf.pendingPush = (PageX(rf, prevPc.op), prevPc.line);
                                    curLi++;
                                    double nx = LeftOf(rf, curLi);
                                    NoteMove(rf, prevPc.op, nx, curLi, prevPc.line, prevPc.bytes, null);
                                    rf.pendingPush = null;
                                    rf.pieces[^1] = (prevPc.op, nx, curLi, prevPc.bytes, null, prevPc.off);
                                    startX = nx + AdvPage(rf, prevPc.op, prevPc.bytes,
                                        own: ReferenceEquals(prevPc.bytes, prevPc.op.Bytes));
                                    continue;
                                }
                            }
                        }
                        // No split point on this line: wrap the whole remainder.
                        rf.pendingPush = (OrigX(startX), curLi);
                        curLi++; startX = LeftOf(rf, curLi);
                        continue;
                    }
                    NoteMove(rf, o, startX, curLi, li, rest[..k], null);
                    rf.pendingPush = null;
                    rf.pieces.Add((o, startX, curLi, rest[..k], null, restOff));
                    // Where the split falls, for the note only — `cursor` belongs to the
                    // placement loop and is set when the remainder finally lands.
                    var splitX = startX + PieceAdv(rf, o, rest, restOff, k);
                    rest = rest[k..];
                    restOff += k;
                    wholeOriginal = false;
                    rf.pendingPush = (OrigX(splitX), curLi);
                    curLi++; startX = LeftOf(rf, curLi);
                }
            }
            rf.prevOrigEnd = px + wOrig; prevOrigLine = li; prevOrigText = o.Text;
        }

        rf.byOp = new Dictionary<CrossTextOp, List<(double x, int line, byte[] bytes, string? sw, int off)>>();
        rf.opOrder = new List<CrossTextOp>();
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
        rf.deleteSpans.Sort((a, b) => a.s.CompareTo(b.s));
        // Stable sort: two bars inserting before the same BT keep coverage order.
        rf.inserts = rf.inserts
            .Select((g, idx) => (g, idx))
            .OrderBy(t => t.g.pos).ThenBy(t => t.idx)
            .Select(t => t.g)
            .ToList();

        rf.result = new MemoryStream();
        rf.lastWritePos = 0;
        rf.delIdx = 0;
        rf.insIdx = 0;
        foreach (var o in rf.opOrder)
        {
            var pl = rf.byOp[o];
            var (tx0, ty0) = SolveTm(rf, o, pl[0].x, BaseY(rf, pl[0].line));
            bool moved = Math.Abs(tx0 - o.TmTx) > 1e-4 || Math.Abs(ty0 - o.TmTy) > 1e-4;
            bool isHead = ReferenceEquals(o, rf.head);
            bool split = pl.Count > 1;
            if (!moved && !isHead && !split)
                continue; // untouched: copied verbatim with the surrounding bytes

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
                        $"/{resName} {N(rf, o.FontSize)} Tf <{Convert.ToHexString(hexIds)}> Tj /{o.FontName} {N(rf, o.FontSize)} Tf"));
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
        }
        CopyTo(rf, rf.streamBytes.Length);

        rf.maxLi = 0;
        foreach (var pc in rf.pieces) if (pc.line > rf.maxLi) rf.maxLi = pc.line;
        ReflowCreatedLines = Math.Max(0, rf.maxLi + 1 - rf.lines.Count);

        // Only a reflow that is actually committed reports its moves.
        if (rf.notes is { Count: > 0 })
            rf.page.NotificationLog += string.Join("\r\n", rf.notes) + "\r\n";

        if (Environment.GetEnvironmentVariable("Q_PIECEDBG") is { Length: > 0 } pdbg)
        {
            var psb = new StringBuilder();
            psb.AppendLine($"--- reflow pass: lines={rf.lines.Count} rightMargin={rightMargin:F3}");
            foreach (var pc in rf.pieces)
            {
                string txt;
                try { txt = pc.sw ?? DecodeString(pc.bytes, pc.op.ToUnicode, pc.op.FontDict, rf.reader); }
                catch { txt = "?"; }
                psb.AppendLine($"    line={pc.line} x={pc.x:F3} baseY={BaseY(rf, pc.line):F2} '{txt}'");
            }
            System.IO.File.AppendAllText(pdbg, psb.ToString());
        }

        _replacementCount = 1;
        rf.page.SetContentStream(rf.result.ToArray());
        return true;
    }

}
