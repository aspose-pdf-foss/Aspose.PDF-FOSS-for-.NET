
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>Styled cascade stages: head resolution, run collection, unit grouping, piece placement and the write.</summary>
    private void WriteStyledLines(StyledCascadeState sc)
    {
        sc.tb = new TextBuilder(sc.page);
        for (int i = 0; i < sc.headIdx; i++)
        {
            var s = sc.headSegs[i];
            Emit(sc, s.Text, s.TextState.Font ?? sc.headFont,
                s.TextState.FontSize > 0 ? s.TextState.FontSize : sc.headFs,
                s.TextState.ForegroundColor, s.Position!.XIndent, s.Position.YIndent);
        }
        if (sc.occ > 0)
            Emit(sc, sc.headSeg.Text[..sc.occ], sc.headFont, sc.headFs, sc.headSeg.TextState.ForegroundColor,
                sc.headX, sc.headSeg.Position!.YIndent);
        foreach (var p in sc.merged)
        {
            var st = sc.runs[p.r];
            Emit(sc, p.text, st.font, st.fs, st.fg, p.x, p.tmY - DescentOf(sc, st.font, st.fs));
        }
        sc.page.ResetContentsCache();
    }

    /// <summary></summary>
    private void PlacePieces(StyledCascadeState sc)
    {
        sc.matchTm = sc.paraLines[sc.matchLine].y;
        sc.tmY = sc.matchTm - sc.newFs;
        sc.x = sc.pLeft; bool lineHas = false;
        sc.pieces = new System.Collections.Generic.List<(string text, int r, double x, double tmY)>();
        for (int u = 0; u < sc.units.Count; u++)
        {
            double unitW = 0;
            foreach (var (t, r) in sc.units[u]) unitW += W(sc, sc.runs[r].font, sc.runs[r].fs, t);
            int gapR = lineHas ? sc.unitGap[u] : -1;
            double gapW = gapR >= 0 ? W(sc, sc.runs[gapR].font, sc.runs[gapR].fs, " ") : 0;
            if (lineHas && sc.x + gapW + unitW > sc.rightMargin + 0.25)
            {
                sc.tmY -= sc.newFs; sc.x = sc.pLeft; lineHas = false; gapR = -1; gapW = 0;
            }
            if (gapR >= 0) { sc.pieces.Add((" ", gapR, sc.x, sc.tmY)); sc.x += gapW; }
            foreach (var (t, r) in sc.units[u])
            {
                sc.pieces.Add((t, r, sc.x, sc.tmY));
                sc.x += W(sc, sc.runs[r].font, sc.runs[r].fs, t);
            }
            lineHas = true;
        }

        sc.merged = new System.Collections.Generic.List<(string text, int r, double x, double tmY)>();
        foreach (var p in sc.pieces)
        {
            if (sc.merged.Count > 0 && sc.merged[^1].r == p.r && System.Math.Abs(sc.merged[^1].tmY - p.tmY) < 0.01)
                sc.merged[^1] = (sc.merged[^1].text + p.text, p.r, sc.merged[^1].x, p.tmY);
            else sc.merged.Add(p);
        }

        // Delete the source runs: the whole match line (its prefix re-emits below at
        // its original coordinates) and every following paragraph line.
        for (int li = sc.matchLine; li < sc.paraLines.Count; li++)
        {
            var del = new TextReplacer
            {
                MatchAnyOperator = true,
                TargetY = sc.paraLines[li].y,
                TargetX = (sc.paraLines[li].lx + sc.paraLines[li].rx) / 2,
                TargetXTolerance = (sc.paraLines[li].rx - sc.paraLines[li].lx) / 2 + 1.0,
            };
            del.Replace(sc.page, string.Empty, string.Empty);
        }
    }

    /// <summary></summary>
    private bool GroupUnits(StyledCascadeState sc)
    {
        sc.units = new System.Collections.Generic.List<System.Collections.Generic.List<(string t, int r)>>();
        sc.unitGap = new System.Collections.Generic.List<int>();
        sc.cur = null;
        sc.pendingGap = -1;
        for (int r = 0; r < sc.runs.Count; r++)
        {
            var parts = sc.runs[r].text.Split(' ');
            for (int pi = 0; pi < parts.Length; pi++)
            {
                if (parts[pi].Length > 0)
                {
                    if (sc.cur is null)
                    {
                        sc.cur = new System.Collections.Generic.List<(string, int)>();
                        sc.units.Add(sc.cur);
                        sc.unitGap.Add(sc.pendingGap);
                    }
                    sc.cur.Add((parts[pi], r));
                }
                if (pi < parts.Length - 1) { sc.cur = null; sc.pendingGap = r; }
            }
        }
        if (sc.units.Count == 0) return false;
        return true;
    }

    /// <summary></summary>
    private bool CollectStyledRuns(StyledCascadeState sc)
    {
        sc.runs = new System.Collections.Generic.List<(string text, Aspose.Pdf.Text.Font? font, double fs, Color? fg)>();
        sc.newFg = TextState.ForegroundColor;
        AddStyledSplit(sc, sc.headSeg.Text[sc.occ..], sc.headSeg);
        for (int i = sc.headIdx + 1; i < sc.headSegs.Count; i++) AddStyledSplit(sc, sc.headSegs[i].Text, sc.headSegs[i]);
        for (int li = sc.matchLine + 1; li < sc.paraLines.Count; li++)
        {
            if (sc.runs.Count > 0 && !sc.runs[^1].text.EndsWith(" ", System.StringComparison.Ordinal))
            {
                var last = sc.runs[^1];
                sc.runs.Add((" ", last.font, last.fs, last.fg));
            }
            foreach (var s in LineSegs(sc, sc.paraLines[li].f)) AddStyledSplit(sc, s.Text, s);
        }
        if (sc.runs.Count == 0) return false;

        sc.pLeft = sc.headSegs[0].Position!.XIndent;
        sc.maxRx = 0; foreach (var l in sc.paraLines) if (l.rx > sc.maxRx) sc.maxRx = l.rx;
        sc.mediaW = sc.page.MediaBox is { } mb ? mb.URX - mb.LLX : 0;
        sc.rightMargin = System.Math.Max(sc.mediaW - sc.pLeft, sc.maxRx);
        if (sc.rightMargin <= sc.pLeft + 20) return false;
        return true;
    }

    /// <summary></summary>
    private bool ResolveHead(StyledCascadeState sc)
    {
        sc.newFont = TextState.Font;
        sc.newFs = TextState.FontSize;
        if (sc.newFont is null || sc.newFs <= 0 || string.IsNullOrEmpty(sc.oldText)) return false;

        sc.headSegs = LineSegs(sc, sc.paraLines[sc.matchLine].f);
        if (sc.headSegs.Count == 0) return false;
        sc.headIdx = -1;
        for (int i = 0; i < sc.headSegs.Count; i++)
            if (sc.headSegs[i].Position!.XIndent <= sc.myLLX + 0.5) sc.headIdx = i; else break;
        if (sc.headIdx < 0) return false;
        sc.headSeg = sc.headSegs[sc.headIdx];
        sc.headFont = sc.headSeg.TextState.Font;
        sc.headFs = sc.headSeg.TextState.FontSize > 0 ? sc.headSeg.TextState.FontSize : sc.newFs;

        sc.restyled = System.Math.Abs(sc.newFs - sc.headFs) > 0.1
            || (Family(sc, sc.newFont.FontName).Length > 0 && Family(sc, sc.headFont?.FontName).Length > 0
                && !string.Equals(Family(sc, sc.newFont.FontName), Family(sc, sc.headFont?.FontName),
                    System.StringComparison.OrdinalIgnoreCase));
        if (!sc.restyled) return false;

        sc.headX = sc.headSeg.Position!.XIndent;
        sc.occ = -1; double bestD = double.MaxValue;
        for (int i = sc.headSeg.Text.IndexOf(sc.oldText, System.StringComparison.Ordinal); i >= 0;
             i = i + 1 <= sc.headSeg.Text.Length - 1
                ? sc.headSeg.Text.IndexOf(sc.oldText, i + 1, System.StringComparison.Ordinal) : -1)
        {
            double d = System.Math.Abs(sc.headX + W(sc, sc.headFont, sc.headFs, sc.headSeg.Text[..i]) - sc.myLLX);
            if (d < bestD) { bestD = d; sc.occ = i; }
        }
        if (sc.occ < 0 || bestD > System.Math.Max(2.0, sc.headFs)) return false;
        return true;
    }
}
