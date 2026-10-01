
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>Cascade re-flow stages: the exact mover path, run collection, font resolution, tokenising, packing and emission.</summary>
    private void EmitPackedLines(CascadeState cf)
    {
        cf.baselines = new System.Collections.Generic.List<double>();
        // The delete-and-re-emit fallback re-writes runs at the source lines' own positions,
        // which already carry the source font's descent — unlike the byte-level mover above,
        // which re-anchors operators on their true baselines.
        for (int li = cf.matchLine; li < cf.paraLines.Count; li++) cf.baselines.Add(cf.paraLines[li].y);
        cf.pitch = cf.baselines.Count >= 2
            ? (cf.baselines[0] - cf.baselines[^1]) / (cf.baselines.Count - 1)
            : 1.2 * cf.effFs;
        if (cf.pitch <= 0) cf.pitch = 1.2 * cf.effFs;
        if (cf.packed.Count > cf.baselines.Count)
            cf.appendedBottom = cf.baselines[^1] - cf.pitch * (cf.packed.Count - cf.baselines.Count);

        // Delete the source runs by REGION, one line at a time, at operator granularity:
        // producers that draw one word (or one bare space) per operator defeat text-keyed
        // deletion — the absorber's coalesced segment text (with synthesized gap spaces)
        // never equals any single operator's decode. Every text operator starting inside
        // the line's X-span goes; the match line is cleared only from the match X on, so
        // its prefix stays put.
        for (int li = cf.matchLine; li < cf.paraLines.Count; li++)
        {
            double xmin = (li == cf.matchLine ? cf.myLLX : cf.paraLines[li].lx) - 0.5;
            double xmax = cf.paraLines[li].rx + 1.0;
            if (xmax <= xmin) continue;
            var del = new TextReplacer
            {
                MatchAnyOperator = true,
                TargetY = cf.paraLines[li].y,
                TargetX = (xmin + xmax) / 2,
                TargetXTolerance = (xmax - xmin) / 2,
            };
            del.Replace(cf.page, string.Empty, string.Empty);
        }

        cf.tb = new TextBuilder(cf.page);
        for (int i = 0; i < cf.packed.Count; i++)
        {
            double by = i < cf.baselines.Count ? cf.baselines[i] : cf.baselines[^1] - (i - cf.baselines.Count + 1) * cf.pitch;
            var frag = new TextFragment(cf.packed[i]);
            frag.TextState.Font = cf.font;
            if (cf.domSeg.TextState.FontName is { Length: > 0 } fn) frag.TextState.FontName = fn;
            frag.TextState.FontSize = (float)cf.effFs;
            if (TextState.ForegroundColor is { } fg) frag.TextState.ForegroundColor = fg;
            frag.Position = new Position(i == 0 ? cf.myLLX : LxAt(cf, i), by);
            cf.tb.AppendText(frag);
        }
        cf.page.ResetContentsCache();
    }

    /// <summary></summary>
    private void PackWords(CascadeState cf)
    {
        cf.degenerateMetrics = false;
        try
        {
            cf.degenerateMetrics = cf.font!.SourceFontData is not null;
        }
        catch { }
        cf.packFamily = cf.font!.FontName ?? string.Empty;
        cf.subsetPlusP = cf.packFamily.IndexOf('+');
        if (cf.subsetPlusP >= 0 && cf.subsetPlusP + 1 < cf.packFamily.Length)
            cf.packFamily = cf.packFamily[(cf.subsetPlusP + 1)..];
        cf.cjkBase = !Standard14Fonts.IsStandard14(cf.packFamily);
        cf.packed = new System.Collections.Generic.List<string>();
        cf.cur = new System.Text.StringBuilder();
        cf.curX = cf.myLLX;
        cf.curW = 0;
        cf.spaceW = SpaceW(cf);
        cf.pendSp = 0;
        foreach (var (w, spAfter) in cf.toks)
        {
            double ww = WordW(cf, w);
            double trial = cf.cur.Length == 0 ? ww : cf.curW + cf.pendSp * cf.spaceW + ww;
            if (cf.curX + trial <= cf.rightX + 0.5 || cf.cur.Length == 0)
            {
                if (cf.cur.Length > 0) cf.cur.Append(' ', cf.pendSp);
                cf.cur.Append(w);
                cf.curW = trial;
            }
            else
            {
                // The seam's space run closes this line only while the line still
                // fits its SOURCE line's own extent — a refill line already run out
                // past it sheds the break's space. The MATCH line is the exception:
                // its source extent is void (the replacement rewrote its content), so
                // it keeps the seam space while it fits the paragraph pack budget
                // (measured: line 1 keeps its space at 475 > its source
                // extent 445 but ≤ the 485 budget; lines 2/4 shed theirs at 480 > 410
                // and 445 > 430 while line 3 keeps at 420 ≤ 485).
                var seamRx = cf.packed.Count == 0 ? cf.rightX : RxAt(cf, cf.packed.Count);
                if (cf.pendSp > 0 && cf.curX + cf.curW + cf.pendSp * cf.spaceW <= seamRx + 0.5)
                    cf.cur.Append(' ', cf.pendSp);
                cf.packed.Add(cf.cur.ToString());
                cf.cur.Clear(); cf.cur.Append(w);
                cf.curW = ww; cf.curX = LxAt(cf, cf.packed.Count);
            }
            cf.pendSp = spAfter > 0 ? spAfter : 1;
        }
        if (cf.cur.Length > 0) cf.packed.Add(cf.cur.ToString());
    }

    /// <summary></summary>
    private bool TokeniseSource(CascadeState cf)
    {
        cf.srcJoined = cf.sb.ToString();
        cf.toks = new System.Collections.Generic.List<(string w, int sp)>();
        {
            var iT = 0;
            while (iT < cf.srcJoined.Length)
            {
                if (cf.srcJoined[iT] == ' ')
                {
                    if (cf.toks.Count > 0)
                    {
                        var lastT = cf.toks[^1];
                        cf.toks[^1] = (lastT.w, lastT.sp + 1);
                    }
                    iT++;
                    continue;
                }
                var stT = iT;
                while (iT < cf.srcJoined.Length && cf.srcJoined[iT] != ' ') iT++;
                cf.toks.Add((cf.srcJoined[stT..iT], 0));
            }
        }
        cf.words = new string[cf.toks.Count];
        for (var wi2 = 0; wi2 < cf.toks.Count; wi2++) cf.words[wi2] = cf.toks[wi2].w;
        if (cf.words.Length == 0) return false;
        return true;
    }

    /// <summary></summary>
    private bool ResolveDominantFont(CascadeState cf)
    {
        cf.domSeg = cf.moved[0].seg;
        foreach (var m in cf.moved)
            if (m.seg.Text.Trim().Length > cf.domSeg.Text.Trim().Length) cf.domSeg = m.seg;
        cf.font = cf.domSeg.TextState.Font ?? TextState.Font;
        cf.rawFs = cf.domSeg.TextState.FontSize > 0 ? cf.domSeg.TextState.FontSize : TextState.FontSize;
        cf.effFs = cf.rawFs * cf.ctmScale;
        if (cf.font is null || cf.effFs <= 0.5) return false;
        cf.faceName = cf.font.FontName ?? string.Empty;
        cf.subsetPlus = cf.faceName.IndexOf('+');
        if (cf.subsetPlus >= 0 && cf.subsetPlus + 1 < cf.faceName.Length)
            cf.faceName = cf.faceName[(cf.subsetPlus + 1)..];
        cf.styleComma = cf.faceName.IndexOf(',');
        if (cf.styleComma > 0) cf.faceName = cf.faceName[..cf.styleComma];
        if (cf.faceName.Length > 0
            && FontRepository.TryFindFont(cf.faceName, ignoreCase: true) is { } sysFont)
            cf.font = sysFont;

        cf.leftX = double.MaxValue;
        cf.rightX = 0;
        for (int li = cf.matchLine; li < cf.paraLines.Count; li++)
        {
            if (cf.paraLines[li].lx < cf.leftX) cf.leftX = cf.paraLines[li].lx;
            if (cf.paraLines[li].rx > cf.rightX) cf.rightX = cf.paraLines[li].rx;
        }
        // Never past the sheet: a paragraph an earlier reflow already pushed off the page
        // would otherwise be read as a column that wide and pushed further.
        if (cf.page.MediaBox is { } mbFb && mbFb.URX > 0) cf.rightX = System.Math.Min(cf.rightX, mbFb.URX);
        if (cf.rightX - cf.leftX < 10 || cf.rightX <= cf.myLLX + 5) return false;
        return true;
    }

    /// <summary></summary>
    private bool CollectMovedRuns(CascadeState cf)
    {
        cf.ctmScale = 1.0;
        if (ExtractionCtm is { } ectm)
        {
            var det = System.Math.Abs(ectm.A * ectm.D - ectm.B * ectm.C);
            if (det > 1e-9) cf.ctmScale = System.Math.Sqrt(det);
        }

        cf.moved = new System.Collections.Generic.List<(TextSegment seg, double x, double y)>();
        for (int li = cf.matchLine; li < cf.paraLines.Count; li++)
        {
            foreach (var seg in cf.paraLines[li].f.Segments)
            {
                if (seg.Position is not { } sp) continue;
                if (string.IsNullOrEmpty(seg.Text)) continue;
                if (li == cf.matchLine && sp.XIndent < cf.myLLX - 0.5) continue; // prefix stays
                cf.moved.Add((seg, sp.XIndent, sp.YIndent));
            }
        }
        if (cf.moved.Count == 0) return false;
        cf.moved.Sort((a, b) => b.y != a.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));

        cf.head = cf.moved[0].seg.Text;
        cf.occ = cf.head.IndexOf(cf.oldText, System.StringComparison.Ordinal);
        if (cf.occ < 0) return false;

        cf.sb = new System.Text.StringBuilder();
        cf.sb.Append(cf.head.Replace(cf.oldText, cf.newText, System.StringComparison.Ordinal));
        for (int i = 1; i < cf.moved.Count; i++)
        {
            bool lineBreak = System.Math.Abs(cf.moved[i].y - cf.moved[i - 1].y) > 0.75;
            if (lineBreak && cf.sb.Length > 0 && cf.sb[^1] != ' ' && !cf.moved[i].seg.Text.StartsWith(" "))
                cf.sb.Append(' ');
            cf.sb.Append(cf.moved[i].seg.Text);
        }
        cf.sb.Replace('\u00A0', ' ');
        return true;
    }

    /// <summary></summary>
    private bool TryMoveRuns(CascadeState cf)
    {
        // The mover works from page OPERATORS, so it takes each line on its TRUE baseline.
        // A rect bottom sits a descent below the operator it describes, and slop wide
        // enough to absorb that descent is also wide enough to capture a NEIGHBOURING
        // line's operator and drag it into the reflow. (The merged-line view supplies the
        // COLUMN below; the lines themselves stay the ones this paragraph was grown from,
        // since re-packing a merged view repositions runs the paragraph never owned.)
        var rlines = new System.Collections.Generic.List<(double y, double lx, double rx)>();
        for (int li = cf.matchLine; li < cf.paraLines.Count; li++)
        {
            var by = LineBaseline(cf.paraLines[li]);
            double llx = cf.paraLines[li].lx, lrx = cf.paraLines[li].rx;
            // A line drawn as several operators reaches further than any ONE of its
            // fragments, so take the merged line's span where one covers this baseline.
            // Its SPAN only: the lines re-packed stay the paragraph's own, since packing
            // a merged view repositions runs the paragraph never owned.
            foreach (var b in cf.bandPara)
            {
                if (System.Math.Abs(b.y - by) > 0.75) continue;
                // The REACH only. Pulling the left edge across too would move a line that
                // starts in one column out to the other column's margin.
                if (b.rx > lrx) lrx = b.rx;
                break;
            }
            rlines.Add((by, llx, lrx));
        }
        double pLeft = double.MaxValue, maxRx = 0;
        foreach (var l in cf.paraLines)
        {
            if (l.lx < pLeft) pLeft = l.lx;
            if (l.rx > maxRx) maxRx = l.rx;
        }
        // The merged view knows the paragraph's real edges; the fragment view sees only
        // the runs the absorber happened to split out.
        foreach (var b in cf.bandPara)
        {
            if (b.lx < pLeft) pLeft = b.lx;
            if (b.rx > maxRx) maxRx = b.rx;
        }
        int paraLineCount = System.Math.Max(cf.paraLines.Count, cf.bandPara.Count);
        double rPitch = rlines.Count >= 2 ? rlines[0].y - rlines[1].y : 0;
        if (rPitch <= 0) rPitch = 1.2 * (TextState.FontSize > 0 ? TextState.FontSize : 10);
        // RightAdjustment extends the border past the paragraph's own right edge by the
        // caller's amount. Otherwise the reflow wraps against the PARAGRAPH'S OWN COLUMN
        // — its widest line — which is what a multi-line block already tells you: the
        // reference wraps a left-column paragraph at 264.63 (its own widest line) even
        // though the page runs to 582.56, and wraps a full-width block at its own 365.83.
        // Only a LONE line carries no column of its own; that one reads the page's (see
        // PageTextRightMargin).
        double rightAdj = _replaceOptions?.RightAdjustment ?? 0;
        double rMargin = rightAdj > 0
            ? maxRx + rightAdj
            : paraLineCount >= 2 ? maxRx : System.Math.Max(cf.pageRightMargin, maxRx);
        // Never past the sheet. The column is read from the page's own text, and once one
        // reflow has pushed a line off the page every later one reads that inflated extent
        // as the column and pushes further — a page whose widest line was 580 ran out to
        // 657 on a 612 pt sheet. RightAdjustment is the caller asking for a wider border
        // and keeps its say.
        if (rightAdj <= 0 && cf.page.MediaBox is { } mbClamp && mbClamp.URX > 0)
            rMargin = System.Math.Min(rMargin, mbClamp.URX);
        var mover = new TextReplacer();
        if (mover.ReflowFromMatch(cf.page, cf.oldText, cf.newText, cf.myLLX, rlines, pLeft, rMargin, rPitch,
                _replaceOptions?.AdjustmentNewLineSpacing ?? 0))
        {
            if (mover.ReflowCreatedLines > 0)
            {
                // Mirror the mover's created-line advance (mean pitch below the
                // edited line) in paragraph-line Y space for the clip expansion.
                double meanPitch = rlines.Count >= 2
                    ? (rlines[0].y - rlines[^1].y) / (rlines.Count - 1)
                    : rPitch;
                cf.appendedBottom = rlines[^1].y - meanPitch * mover.ReflowCreatedLines;
            }
            cf.page.ResetContentsCache();
            if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
                Console.Error.WriteLine($"[reflow-path] mover rMargin={rMargin:F2} pLeft={pLeft:F2} rline0={rlines[0].y:F3} paraY0={cf.paraLines[cf.matchLine].y:F3} bp={(cf.paraLines[cf.matchLine].f.BaselinePosition is null ? "null" : cf.paraLines[cf.matchLine].f.BaselinePosition!.YIndent.ToString("F3"))}");
            return true;
        }
        return false;
    }
}
