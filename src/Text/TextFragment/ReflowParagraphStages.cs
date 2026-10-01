

namespace Aspose.Pdf.Text;

public partial class TextFragment
{
// A stage of the whole-paragraph reflow.
    private static bool SizeCompatible(ReflowParagraphState rp, double lineFs) =>
        lineFs <= 0 || (lineFs <= rp.paraFs * 1.35 && lineFs >= rp.paraFs / 1.35);

    // The budget of re-flow line i. The LAST source line is not capacity-bound — the
    // re-flow runs its remainder out past that line's own extent — so it, and any line
    // past the grid, wraps at the paragraph's width.
    private static double LineBudget(ReflowParagraphState rp, int i, double newFs)
    {
        if (i < 0 || i >= rp.lineCaps.Count - 1) return rp.width;
        var c = rp.lineCaps[i];
        double b = c.seat + c.runW * (c.srcFs > 0 ? newFs / c.srcFs : 1.0);
        return b > 0 && b < rp.width ? b : rp.width;
    }

    /// <summary>Seat the wrapped lines on the paragraph's baselines, write them and re-clip the whole-paragraph case.</summary>
    private void WriteReflowedLines(Page page, ReflowParagraphState rp, string newText)
    {
        rp.baselines = new System.Collections.Generic.List<double>();
        foreach (var l in rp.paraLines)
        {
            // Line anchors are the source lines' own positions, which carry the SOURCE
            // font's descent. That cancels out when the re-flow writes the same font — but
            // a substituted face has its own descent, so anchor on the run's true baseline
            // and let the stand-in's descent apply.
            var anchor = l.y;
            if (rp.reflowFace is not null && (l.f.BaselinePosition ?? l.f.PositionOrNull) is { } bp)
                anchor = bp.YIndent;
            // An anchor is a box BOTTOM, and a box bottom hangs the source font's descent
            // under the baseline. Re-flowing at a SMALLER size shortens that descent, so
            // seating the new lines on the old bottoms would sink the whole block by the
            // difference. The baseline grid is what the re-flow keeps: lift each anchor by
            // the descent the block no longer has. Zero whenever the size is unchanged.
            double srcFs = l.f.TextState.FontSize > 0 ? l.f.TextState.FontSize : rp.fs;
            rp.baselines.Add(anchor + SeatDescentOf(rp.domFont, srcFs) - SeatDescentOf(rp.domFont, rp.domSize));
        }
        rp.pitch = rp.baselines.Count >= 2
            ? (rp.baselines[0] - rp.baselines[^1]) / (rp.baselines.Count - 1)
            : 1.2 * rp.domSize;
        if (rp.pitch <= 0) rp.pitch = 1.2 * rp.domSize;

        foreach (var l in rp.paraLines)
        {
            // The re-absorbed line fragments have ReplaceAdjustment.None (fresh absorber),
            // so this deletes in place via the normal replace machinery without recursing
            // back into paragraph reflow.
            try { l.f.Text = string.Empty; } catch { }
        }

        rp.tb = new TextBuilder(page);
        rp.laidOut = new System.Collections.Generic.List<(string text, double baseline, double width)>();
        rp.maxLineW = 0;
        for (int i = 0; i < rp.wrapped.Count; i++)
        {
            double by = i < rp.baselines.Count ? rp.baselines[i] : rp.baselines[^1] - (i - rp.baselines.Count + 1) * rp.pitch;
            var frag = new TextFragment(rp.wrapped[i]);
            frag.TextState.Font = rp.domFont;
            if (!string.IsNullOrEmpty(rp.domName)) frag.TextState.FontName = rp.domName;
            frag.TextState.FontSize = rp.domSize;
            if (TextState.ForegroundColor is { } fg) frag.TextState.ForegroundColor = fg;
            if (rp.reflowFace is not null)
            {
                frag.TextState.Std14FaceOverride = rp.reflowFace;
                // Write the stand-in with its metrics, so the run reads back with
                // the stand-in's descent under its baseline.
                frag.TextState.EmitStandard14Descriptor = true;
            }
            frag.Position = new Position(rp.leftX, by);
            rp.tb.AppendText(frag);
            double lw;
            if (rp.reflowMeasure is not null) lw = rp.reflowMeasure(rp.wrapped[i], rp.domSize);
            else try { lw = rp.domFont.MeasureString(rp.wrapped[i], rp.domSize); } catch { lw = rp.wrapped[i].Length * rp.domSize * 0.5; }
            rp.laidOut.Add((rp.wrapped[i], by, lw));
            if (lw > rp.maxLineW) rp.maxLineW = lw;
        }
        page.ResetContentsCache();
        if (rp.wrapped.Count > rp.baselines.Count)
            ExpandClipsToReflowBottom(page, rp.paraLines,
                rp.baselines[^1] - rp.pitch * (rp.wrapped.Count - rp.baselines.Count));

        // A whole-paragraph replace re-points THIS fragment at the laid-out block so a caller
        // that reads fragment.Segments / fragment.Rectangle after the assignment (e.g. to add
        // a per-segment underline or a per-fragment highlight) sees the reflowed geometry. Box
        // mirrors the absorber: LLY = the last line's box bottom, URY = the first line's
        // bottom + 1.1 em; URX = the widest line, counting the space its break stands in for.
        if (rp.wholePara && rp.laidOut.Count > 0)
        {
            double firstBaseline = rp.laidOut[0].baseline;
            double lastBaseline = rp.laidOut[^1].baseline;
            double ascentH = 1.1 * rp.domSize;
            _rectangle = new Rectangle(rp.leftX, lastBaseline, rp.leftX + rp.maxLineW, firstBaseline + ascentH);
            _text = newText;
            _segments.Clear();
            foreach (var ln in rp.laidOut)
            {
                var seg = new TextSegment(ln.text);
                seg.TextState.FontSize = rp.domSize;
                if (!string.IsNullOrEmpty(rp.domName)) seg.TextState.FontName = rp.domName;
                seg.TextState.Font = rp.domFont;
                seg.Owner = this;
                seg.Position = new Position(rp.leftX, ln.baseline);
                // Each line gets its own page box — the same 1.1 em band as the block, around
                // THIS line's baseline. Without it a rebuilt segment measures itself at the
                // origin, and a caller decorating per segment (an underline per line) stacks
                // every decoration in the page corner.
                seg.Rectangle = new Rectangle(rp.leftX, ln.baseline,
                    rp.leftX + ln.width, ln.baseline + ascentH);
                seg.TextState.OwnerSegment = seg;
                _segments.Add(seg);
            }
        }
    }

    /// <summary>Pick the paragraph's dominant font and wrap the replacement to the line budgets or the width.</summary>
    private bool WrapInDominantFont(ReflowParagraphState rp, Font myFont)
    {
        rp.domLine = rp.paraLines[0].f;
        foreach (var l in rp.paraLines) if (l.f.Text.Length > rp.domLine.Text.Length) rp.domLine = l.f;
        rp.domFont = rp.domLine.TextState.Font ?? myFont;
        rp.domName = rp.domLine.TextState.FontName ?? TextState.FontName;
        rp.domSize = rp.domLine.TextState.FontSize > 0 ? rp.domLine.TextState.FontSize : (float)rp.fs;
        rp.reflowFace = ResolveSubstituteFace(rp.domFont, rp.replaced);
        rp.reflowMeasure = rp.reflowFace is null ? null : Standard14Measurer(rp.reflowFace);

        rp.lineCaps = new System.Collections.Generic.List<(double seat, double runW, double srcFs)>();
        if (rp.wholePara)
            foreach (var l in rp.paraLines)
            {
                double srcFs = l.f.TextState.FontSize > 0 ? l.f.TextState.FontSize : rp.fs;
                double seat = l.rx - rp.leftX, runW = 0;
                Aspose.Pdf.Rectangle? lastRun = null;
                foreach (TextSegment s in l.f.Segments)
                {
                    if (s.Rectangle is not { } sr || string.IsNullOrEmpty(s.Text)) continue;
                    if (lastRun is null || sr.LLX > lastRun.LLX) lastRun = sr;
                }
                if (lastRun is not null) { seat = lastRun.LLX - rp.leftX; runW = lastRun.URX - lastRun.LLX; }
                rp.lineCaps.Add((seat, runW, srcFs));
            }
        if (rp.wholePara)
        {
            // Shrink the font until the (larger) replacement fits the ORIGINAL rectangle,
            // HOLDING the line count measured at the original size, then re-wrap at the
            // fitted size. Compute the fit from the un-mutated original size (the fresh
            // re-absorb's, not THIS fragment's TextState which a caller may have already
            // shrunk via IsFitRectangle) and the original rectangle, so the result is
            // independent of the caller's font-size loop. Measure with a trailing space per
            // line: reserving one space width past each wrapped line breaks lines slightly
            // earlier and keeps the wrapped lines re-searchable across the line breaks.
            double origSize = rp.domSize;
            double rectH = _rectangle!.Height;
            int nFit = WrapToWidth(rp.replaced, rp.domFont, origSize, rp.width, trailingSpace: true, measure: rp.reflowMeasure is null ? null : t => rp.reflowMeasure(t, origSize)).Count;
            if (nFit < 1) nFit = 1;
            double fitFs = origSize;
            while (fitFs > 1.0 && nFit * 1.2 * fitFs > rectH) fitFs -= 0.5;
            rp.domSize = (float)fitFs;
            // A whole-paragraph re-flow refills the SOURCE line grid, and each source line
            // carries its own capacity. A line's last run keeps the x it was DRAWN at — the
            // run's seat is fixed and only the text inside it shrinks with the font — so
            // line i takes text up to `lastRunSeat + lastRunWidth * (newSize/sourceSize)`.
            // A single-run line collapses to width*(newSize/sourceSize), i.e. the line holds
            // the same words it always did; a many-run line is dominated by the seat and its
            // capacity barely moves with the font size at all. Measured on the expected
            // re-flow over a run-structure bench (1/2/4/8/16 runs per line, early/even/late
            // split points, kerns and word spacing on and off), then confirmed line-for-line
            // on a seven-line Word paragraph re-flowed at half its size.
            rp.wrapped = WrapToBudgets(rp.replaced, rp.domFont, fitFs, i => LineBudget(rp, i, fitFs),
                trailingSpace: true, measure: rp.reflowMeasure is null ? null : t => rp.reflowMeasure(t, fitFs));
        }
        else
        {
            rp.wrapped = WrapToWidth(rp.replaced, rp.domFont, rp.domSize, rp.width, allowCharBreak: rp.ignorePara, measure: rp.reflowMeasure is null ? null : t => rp.reflowMeasure(t, rp.domSize));
        }
        if (rp.wrapped.Count == 0) return false;
        return true;
    }

    /// <summary>The wrap's right border: the paragraph's extent, the page's column for a lone line, and RightAdjustment.</summary>
    private bool SolveWrapWidth(Page page, ReflowParagraphState rp, Position myPos)
    {
        rp.rightX = 0;
        foreach (var l in rp.paraLines) if (l.rx > rp.rightX) rp.rightX = l.rx;
        // A line drawn as several operators reaches further than any one of its fragments.
        if (rp.bandColumnRight > rp.rightX) rp.rightX = rp.bandColumnRight;
        // ...but never past the sheet (see the cascade's clamp).
        if (page.MediaBox is { } mbCoarse && mbCoarse.URX > 0)
            rp.rightX = System.Math.Min(rp.rightX, mbCoarse.URX);
        rp.pageRect = page.Rect;
        rp.lineHasTail = rp.rightX > rp.myLLX + (_rectangle?.Width ?? 0) + rp.fs;
        if (rp.paraLines.Count == 1 && rp.pageRect is not null && !rp.lineHasTail)
        {
            double lonelyInset = rp.leftX - rp.pageRect.LLX;
            double lonelyRight = rp.pageRect.URX - (lonelyInset > 0 ? lonelyInset : 0);
            foreach (var l in rp.lines0)
            {
                if (ReferenceEquals(l.f, rp.paraLines[0].f)) continue;
                if (System.Math.Abs(l.y - myPos.YIndent) > rp.fs) continue;
                if (l.lx <= rp.myLLX) continue;
                var nfs = l.f.TextState.FontSize > 0 ? l.f.TextState.FontSize : rp.fs;
                var clipped = l.lx - (nfs - 1);
                if (clipped < lonelyRight) lonelyRight = clipped;
            }
            if (lonelyRight > rp.leftX + 10) rp.rightX = lonelyRight;
        }
        if (rp.ignorePara && rp.pageRect is not null)
        {
            double leftInset = rp.leftX - rp.pageRect.LLX;
            double pageRight = rp.pageRect.URX - (leftInset > 0 ? leftInset : 0);
            if (pageRight > rp.leftX + 10 && rp.rightX > pageRight) rp.rightX = pageRight;
        }
        rp.rightAdjust = rp.wholePara ? 0 : (_replaceOptions?.RightAdjustment ?? 0);
        rp.width = (rp.rightX - rp.leftX) + rp.rightAdjust;
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
            Console.Error.WriteLine($"[reflow] paraLines={rp.paraLines.Count} leftX={rp.leftX:F2} rightX={rp.rightX:F2} width={rp.width:F2} wholePara={rp.wholePara} ignorePara={rp.ignorePara} pageRect={(page.Rect is null ? "null" : page.Rect.ToString())}");
        if (rp.width < 10) return false;
        return true;
    }

    /// <summary>Replace per line, detect the whole-paragraph case and read the paragraph's band geometry.</summary>
    private bool ReplacePerLine(Page page, ReflowParagraphState rp, string oldText, string newText)
    {
        // Continuous flow anchors the re-emitted block at the flow's leftmost x.
        if (rp.ignorePara)
            foreach (var l in rp.paraLines) if (l.lx < rp.leftX) rp.leftX = l.lx;
        rp.origParts = new System.Collections.Generic.List<string>();
        rp.newParts = new System.Collections.Generic.List<string>();
        foreach (var l in rp.paraLines)
        {
            var t = l.f.Text.Trim();
            rp.origParts.Add(t);
            rp.newParts.Add(t.Replace(oldText, newText, System.StringComparison.Ordinal));
        }
        rp.origText = string.Join(" ", rp.origParts);
        rp.replaced = string.Join(" ", rp.newParts);
        rp.wholePara = false;
        if (rp.replaced == rp.origText)
        {
            static string Squash(string s) =>
                System.Text.RegularExpressions.Regex.Replace(s, @"\s+", string.Empty);
            if (Squash(oldText) == Squash(rp.origText)) { rp.replaced = newText; rp.wholePara = true; }
            // An occurrence that STRADDLES one of the paragraph's line breaks is a real
            // match of this paragraph even though no single line carries it: the absorber
            // spells the break out ("leap \ninto electronic") and the flow reads it as the
            // word gap it is. Replace it in the joined text and let the cascade below wrap
            // the result back across those baselines.
            else if (StraddlingReplace(rp.origText, oldText, newText) is { } acrossBreak)
                rp.replaced = acrossBreak;
            else return false;
        }

        rp.lonelyOverflow = rp.paraLines.Count == 1
            && _rectangle is { } myRect
            && myRect.Width >= (rp.paraLines[0].rx - rp.paraLines[0].lx) * 0.9
            && MeasureOrEstimate(TextState.Font!, newText, rp.fs, false) > (rp.paraLines[0].rx - rp.myLLX) * 1.05;
        rp.paraLeftX = double.MaxValue;
        rp.paraRightX = 0;
        foreach (var l in rp.paraLines)
        {
            if (l.lx < rp.paraLeftX) rp.paraLeftX = l.lx;
            if (l.rx > rp.paraRightX) rp.paraRightX = l.rx;
        }
        rp.pageCol = PageTextRightMargin(page, rp.lines0, rp.paraLeftX, rp.paraRightX);
        rp.bands = MergeBaselines(rp.bandSource, rp.spanLx);
        rp.bandPara = GrowBandParagraph(rp.bands, LineBaseline(rp.paraLines[rp.myCol - rp.lo]), rp.myLLX,
            rp.paraPitch * rp.stepPitchTol, rp.xtol);
        rp.bandColumnRight = 0;
        foreach (var b in rp.bandPara) if (b.rx > rp.bandColumnRight) rp.bandColumnRight = b.rx;
        return true;
    }

    /// <summary>Grow the paragraph up and down over the column's contiguous same-margin, same-size lines.</summary>
    private void GrowParagraph(ReflowParagraphState rp)
    {
        rp.leftX = rp.lines0[rp.myIdx].lx;

        rp.xtol = 3.0;
        rp.ignorePara = _replaceOptions?.IgnoreParagraphs ?? false;
        rp.paraFs = rp.lines0[rp.myIdx].f.TextState.FontSize;
        if (rp.paraFs <= 0) rp.paraFs = rp.fs;
        rp.colLines = new System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)>();
        rp.myCol = 0;
        for (int i = 0; i < rp.lines0.Count; i++)
        {
            if (i != rp.myIdx && (rp.lines0[i].lx > rp.lines0[rp.myIdx].rx || rp.lines0[i].rx < rp.lines0[rp.myIdx].lx)) continue;
            if (i == rp.myIdx) rp.myCol = rp.colLines.Count;
            rp.colLines.Add(rp.lines0[i]);
        }
        rp.lo = rp.myCol;
        rp.hi = rp.myCol;
        rp.maxHang = 40.0;
        rp.stepPitchTol = 1.35;
        rp.paraPitch = ParagraphPitch(rp.colLines, rp.myCol, rp.fs);
        rp.maxMergeGap = rp.ignorePara ? 3 * rp.fs : rp.paraPitch * rp.stepPitchTol;
        rp.downLx = rp.colLines[rp.myCol].lx;
        rp.hangStepped = false;
        while (rp.hi + 1 < rp.colLines.Count)
        {
            double gap = rp.colLines[rp.hi].y - rp.colLines[rp.hi + 1].y;
            if (!(gap > 0 && gap <= rp.maxMergeGap) || !SizeCompatible(rp, rp.colLines[rp.hi + 1].f.TextState.FontSize))
                break;
            double lx = rp.colLines[rp.hi + 1].lx;
            if (rp.ignorePara || System.Math.Abs(lx - rp.downLx) <= rp.xtol) { rp.hi++; continue; }
            if (!rp.hangStepped && rp.hi == rp.myCol && lx - rp.downLx > rp.xtol && lx - rp.downLx <= rp.maxHang)
            {
                double nextGap = rp.hi + 2 < rp.colLines.Count ? rp.colLines[rp.hi + 1].y - rp.colLines[rp.hi + 2].y : gap;
                if (nextGap > 0 && gap <= rp.stepPitchTol * nextGap)
                {
                    rp.hangStepped = true; rp.downLx = lx; rp.hi++; continue;
                }
            }
            break;
        }
        rp.upLx = rp.colLines[rp.myCol].lx;
        while (rp.lo - 1 >= 0)
        {
            double gap = rp.colLines[rp.lo - 1].y - rp.colLines[rp.lo].y;
            if (!(gap > 0 && gap <= rp.maxMergeGap) || !SizeCompatible(rp, rp.colLines[rp.lo - 1].f.TextState.FontSize))
                break;
            double lx = rp.colLines[rp.lo - 1].lx;
            if (rp.ignorePara || System.Math.Abs(lx - rp.upLx) <= rp.xtol) { rp.lo--; continue; }
            if (rp.upLx - lx > rp.xtol && rp.upLx - lx <= rp.maxHang)
            {
                double refGap = rp.lo < rp.hi ? rp.colLines[rp.lo].y - rp.colLines[rp.lo + 1].y : gap;
                if (refGap > 0 && gap <= rp.stepPitchTol * refGap) { rp.lo--; break; }
            }
            break;
        }

        rp.paraLines = rp.colLines.GetRange(rp.lo, rp.hi - rp.lo + 1);
    }

    /// <summary>Absorb the page's lines and find the one that contains this fragment.</summary>
    private bool LocateMatchLine(Page page, ReflowParagraphState rp, Position myPos, string oldText)
    {
        rp.lines0 = new System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)>();
        rp.spanLx = new System.Collections.Generic.Dictionary<TextFragment, double>();
        rp.absorbers = new System.Collections.Generic.List<TextFragmentAbsorber>();
        {
            var rx0 = new TextFragmentAbsorber(".+", new TextSearchOptions(true));
            // The line fragments absorbed here are deleted in place (see below); pin
            // ReplaceAdjustment.None so the deletion never shifts other same-line
            // content, independent of the absorber's ShiftRestOfLine default.
            rx0.TextReplaceOptions = new TextReplaceOptions(TextReplaceOptions.ReplaceAdjustment.None);
            rp.absorbers.Add(rx0);
        }
        rp.abs = rp.absorbers[0];
        page.Accept(rp.abs);
        if (rp.abs.TextFragments.Count <= 1)
        {
            var plain = new TextFragmentAbsorber();
            plain.TextReplaceOptions = new TextReplaceOptions(TextReplaceOptions.ReplaceAdjustment.None);
            plain.Visit(page);
            if (plain.TextFragments.Count > rp.abs.TextFragments.Count) rp.abs = plain;
        }
        rp.bandSource = new System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)>();
        foreach (TextFragment f in rp.abs.TextFragments)
        {
            var p = f.PositionOrNull;
            if (p is null) continue;
            var rect = f.Rectangle;
            if (rect is null) continue;
            if (string.IsNullOrWhiteSpace(f.Text))
            {
                rp.bandSource.Add((f, p.YIndent, rect.LLX, rect.URX));
                rp.spanLx[f] = rect.LLX;
                continue;
            }
            // The fragment rect's left edge lies about where the line's text starts in
            // BOTH directions: leading padding-space glyphs pull it LEFT of the visible
            // text (hanging indents padded from the wrap margin), and a back-jump line
            // (later-drawn run first in reading order) anchors it RIGHT of earlier-drawn
            // segments. The leftmost VISIBLE (non-blank) segment is the truth either way.
            double lx = rect.LLX;
            double vis = double.MaxValue;
            foreach (var sg in f.Segments)
                if (sg.Position is { } sp && !string.IsNullOrWhiteSpace(sg.Text) && sp.XIndent < vis)
                    vis = sp.XIndent;
            if (vis < double.MaxValue) lx = vis;
            rp.spanLx[f] = System.Math.Min(rect.LLX, lx);
            rp.lines0.Add((f, p.YIndent, lx, rect.URX));
            rp.bandSource.Add((f, p.YIndent, lx, rect.URX));
        }
        if (rp.lines0.Count == 0) return false;
        // Top-to-bottom (PDF Y grows upward, so higher YIndent = higher on page).
        rp.lines0.Sort((a, b) => b.y.CompareTo(a.y));

        rp.myLLX = _rectangle!.LLX;
        rp.myIdx = -1; double best = rp.fs;
        for (int i = 0; i < rp.lines0.Count; i++)
        {
            double dy = System.Math.Abs(rp.lines0[i].y - myPos.YIndent);
            bool xin = rp.myLLX >= rp.spanLx[rp.lines0[i].f] - 5 && rp.myLLX <= rp.lines0[i].rx + 5;
            if (dy <= best && xin && dy < rp.fs) { best = dy; rp.myIdx = i; }
        }
        // A SIBLING replacement may already have reflowed this page, so the position this
        // fragment recorded when it was absorbed can name a line that no longer exists. Re-anchor
        // on the text instead: the line that still carries the search text is this fragment's
        // line wherever the reflow moved it. Only when exactly one line carries it — two
        // candidates and the position was the only thing that told them apart.
        if (rp.myIdx < 0)
        {
            int byText = -1;
            for (int i = 0; i < rp.lines0.Count; i++)
            {
                if (rp.lines0[i].f.Text.IndexOf(oldText, System.StringComparison.Ordinal) < 0) continue;
                if (byText >= 0) { byText = -1; break; }
                byText = i;
            }
            if (byText >= 0) rp.myIdx = byText;
        }
        if (rp.myIdx < 0) return false;
        return true;
    }
}
