
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>Hyphenation reflow for a RESTYLED replacement (the caller assigned a
    /// new font/size before setting the text). The reflow
    /// model: the match line's prefix keeps its exact position; the
    /// replacement and every following run drop onto a FRESH baseline one new-font-size
    /// step below the match baseline, flowing from the paragraph's left margin with
    /// greedy word-wrap against (page width − left inset). Retained source text keeps
    /// its own font/size; only replacement spans switch to the new style. Wrap units
    /// split at spaces and may span styles (a replacement glued to source text wraps
    /// as one unit).</summary>
    private bool StyledCascadeFromMatch(Page page,
        System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> paraLines,
        int matchLine, double myLLX, string oldText, string newText)
    {
        var newFont = TextState.Font;
        double newFs = TextState.FontSize;
        if (newFont is null || newFs <= 0 || string.IsNullOrEmpty(oldText)) return false;

        static string Family(string? n)
        {
            if (string.IsNullOrEmpty(n)) return string.Empty;
            var s = n;
            int plus = s.IndexOf('+');
            if (plus >= 0 && plus + 1 < s.Length) s = s[(plus + 1)..];
            int comma = s.IndexOf(',');
            if (comma > 0) s = s[..comma];
            int dash = s.IndexOf('-');
            if (dash > 0) s = s[..dash];
            return s.Replace(" ", string.Empty);
        }

        System.Collections.Generic.List<TextSegment> LineSegs(TextFragment f)
        {
            var list = new System.Collections.Generic.List<TextSegment>();
            foreach (var seg in f.Segments)
                if (seg.Position is not null && !string.IsNullOrEmpty(seg.Text)) list.Add(seg);
            list.Sort((a, b) => a.Position!.XIndent.CompareTo(b.Position!.XIndent));
            return list;
        }

        var headSegs = LineSegs(paraLines[matchLine].f);
        if (headSegs.Count == 0) return false;
        int headIdx = -1;
        for (int i = 0; i < headSegs.Count; i++)
            if (headSegs[i].Position!.XIndent <= myLLX + 0.5) headIdx = i; else break;
        if (headIdx < 0) return false;
        var headSeg = headSegs[headIdx];
        var headFont = headSeg.TextState.Font;
        double headFs = headSeg.TextState.FontSize > 0 ? headSeg.TextState.FontSize : newFs;

        // Only a genuine restyle takes this path; a same-style replacement stays on
        // the byte-level run mover (which positions that case exactly).
        bool restyled = System.Math.Abs(newFs - headFs) > 0.1
            || (Family(newFont.FontName).Length > 0 && Family(headFont?.FontName).Length > 0
                && !string.Equals(Family(newFont.FontName), Family(headFont?.FontName),
                    System.StringComparison.OrdinalIgnoreCase));
        if (!restyled) return false;

        double W(Aspose.Pdf.Text.Font? f, double fs, string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (f is not null) { try { return f.MeasureString(s, fs); } catch { } }
            return s.Length * fs * 0.5;
        }
        static double DescentOf(Aspose.Pdf.Text.Font? f, double fs)
        {
            double d = 0;
            try
            {
                if (f?.SourceFontData?.TtfData is { } ttf)
                {
                    // hhea descender — the SAME value the Type0 embed's descriptor
                    // carries, so emitted targets round-trip through the absorber.
                    d = TextBuilder.HheaDescentPerMille(ttf);
                    if (d == 0) (_, d, _, _) = FontRepository.ReadTtfMetrics(ttf);
                }
                if (d == 0) d = f?.GetMetrics()?.Descent ?? 0;
            }
            catch { }
            return d != 0 ? System.Math.Abs(d) / 1000.0 * fs : 0;
        }

        // Char index of the match inside the head run, located by measuring prefixes
        // against the match X (the match may be any occurrence within the run).
        double headX = headSeg.Position!.XIndent;
        int occ = -1; double bestD = double.MaxValue;
        for (int i = headSeg.Text.IndexOf(oldText, System.StringComparison.Ordinal); i >= 0;
             i = i + 1 <= headSeg.Text.Length - 1
                ? headSeg.Text.IndexOf(oldText, i + 1, System.StringComparison.Ordinal) : -1)
        {
            double d = System.Math.Abs(headX + W(headFont, headFs, headSeg.Text[..i]) - myLLX);
            if (d < bestD) { bestD = d; occ = i; }
        }
        if (occ < 0 || bestD > System.Math.Max(2.0, headFs)) return false;

        // Styled run stream: source text keeps its style; every oldText occurrence
        // becomes newText in the new style.
        var runs = new System.Collections.Generic.List<(string text, Aspose.Pdf.Text.Font? font, double fs, Color? fg)>();
        var newFg = TextState.ForegroundColor;
        void AddStyledSplit(string s, TextSegment src)
        {
            var f = src.TextState.Font ?? headFont;
            double fs = src.TextState.FontSize > 0 ? src.TextState.FontSize : headFs;
            var fg = src.TextState.ForegroundColor;
            int p = 0;
            while (true)
            {
                int q = s.IndexOf(oldText, p, System.StringComparison.Ordinal);
                if (q < 0) { if (p < s.Length) runs.Add((s[p..], f, fs, fg)); break; }
                if (q > p) runs.Add((s[p..q], f, fs, fg));
                runs.Add((newText, newFont, newFs, newFg));
                p = q + oldText.Length;
            }
        }
        AddStyledSplit(headSeg.Text[occ..], headSeg);
        for (int i = headIdx + 1; i < headSegs.Count; i++) AddStyledSplit(headSegs[i].Text, headSegs[i]);
        for (int li = matchLine + 1; li < paraLines.Count; li++)
        {
            if (runs.Count > 0 && !runs[^1].text.EndsWith(" ", System.StringComparison.Ordinal))
            {
                var last = runs[^1];
                runs.Add((" ", last.font, last.fs, last.fg));
            }
            foreach (var s in LineSegs(paraLines[li].f)) AddStyledSplit(s.Text, s);
        }
        if (runs.Count == 0) return false;

        // Wrap geometry: left = the match line's left margin; right mirrors the left
        // inset against the page width (never tighter than the paragraph's extent).
        // The line's leftmost RUN X (the fragment rect's LLX can degrade to 0).
        double pLeft = headSegs[0].Position!.XIndent;
        double maxRx = 0; foreach (var l in paraLines) if (l.rx > maxRx) maxRx = l.rx;
        double mediaW = page.MediaBox is { } mb ? mb.URX - mb.LLX : 0;
        double rightMargin = System.Math.Max(mediaW - pLeft, maxRx);
        if (rightMargin <= pLeft + 20) return false;

        // Tokenize into wrap units (split at spaces; units may span styles). The
        // style of the space BEFORE each unit is recorded for gap measurement.
        var units = new System.Collections.Generic.List<System.Collections.Generic.List<(string t, int r)>>();
        var unitGap = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<(string t, int r)>? cur = null;
        int pendingGap = -1;
        for (int r = 0; r < runs.Count; r++)
        {
            var parts = runs[r].text.Split(' ');
            for (int pi = 0; pi < parts.Length; pi++)
            {
                if (parts[pi].Length > 0)
                {
                    if (cur is null)
                    {
                        cur = new System.Collections.Generic.List<(string, int)>();
                        units.Add(cur);
                        unitGap.Add(pendingGap);
                    }
                    cur.Add((parts[pi], r));
                }
                if (pi < parts.Length - 1) { cur = null; pendingGap = r; }
            }
        }
        if (units.Count == 0) return false;

        // Greedy flow: first fresh baseline one new-size step below the match
        // baseline; every wrapped line steps by the new size.
        // The re-absorbed line Y is already the run's Tm baseline.
        double matchTm = paraLines[matchLine].y;
        double tmY = matchTm - newFs;
        double x = pLeft; bool lineHas = false;
        var pieces = new System.Collections.Generic.List<(string text, int r, double x, double tmY)>();
        for (int u = 0; u < units.Count; u++)
        {
            double unitW = 0;
            foreach (var (t, r) in units[u]) unitW += W(runs[r].font, runs[r].fs, t);
            int gapR = lineHas ? unitGap[u] : -1;
            double gapW = gapR >= 0 ? W(runs[gapR].font, runs[gapR].fs, " ") : 0;
            if (lineHas && x + gapW + unitW > rightMargin + 0.25)
            {
                tmY -= newFs; x = pLeft; lineHas = false; gapR = -1; gapW = 0;
            }
            if (gapR >= 0) { pieces.Add((" ", gapR, x, tmY)); x += gapW; }
            foreach (var (t, r) in units[u])
            {
                pieces.Add((t, r, x, tmY));
                x += W(runs[r].font, runs[r].fs, t);
            }
            lineHas = true;
        }

        // Merge same-style neighbours on a line into single show pieces.
        var merged = new System.Collections.Generic.List<(string text, int r, double x, double tmY)>();
        foreach (var p in pieces)
        {
            if (merged.Count > 0 && merged[^1].r == p.r && System.Math.Abs(merged[^1].tmY - p.tmY) < 0.01)
                merged[^1] = (merged[^1].text + p.text, p.r, merged[^1].x, p.tmY);
            else merged.Add(p);
        }

        // Delete the source runs: the whole match line (its prefix re-emits below at
        // its original coordinates) and every following paragraph line.
        for (int li = matchLine; li < paraLines.Count; li++)
        {
            var del = new TextReplacer
            {
                MatchAnyOperator = true,
                TargetY = paraLines[li].y,
                TargetX = (paraLines[li].lx + paraLines[li].rx) / 2,
                TargetXTolerance = (paraLines[li].rx - paraLines[li].lx) / 2 + 1.0,
            };
            del.Replace(page, string.Empty, string.Empty);
        }

        var tb = new TextBuilder(page);
        void Emit(string text, Aspose.Pdf.Text.Font? f, double fs, Color? fg, double px, double py)
        {
            if (string.IsNullOrEmpty(text) || f is null) return;
            var frag = new TextFragment(text);
            frag.TextState.Font = f;
            frag.TextState.FontSize = (float)fs;
            if (fg is not null) frag.TextState.ForegroundColor = fg;
            frag.Position = new Position(px, py);
            tb.AppendText(frag);
        }
        for (int i = 0; i < headIdx; i++)
        {
            var s = headSegs[i];
            Emit(s.Text, s.TextState.Font ?? headFont,
                s.TextState.FontSize > 0 ? s.TextState.FontSize : headFs,
                s.TextState.ForegroundColor, s.Position!.XIndent, s.Position.YIndent);
        }
        if (occ > 0)
            Emit(headSeg.Text[..occ], headFont, headFs, headSeg.TextState.ForegroundColor,
                headX, headSeg.Position.YIndent);
        foreach (var p in merged)
        {
            var st = runs[p.r];
            Emit(p.text, st.font, st.fs, st.fg, p.x, p.tmY - DescentOf(st.font, st.fs));
        }
        page.ResetContentsCache();
        return true;
    }

    private bool CascadeFromMatch(Page page,
        System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> paraLines,
        int matchLine, double myLLX, string oldText, string newText,
        double pageRightMargin,
        System.Collections.Generic.List<(double y, double lx, double rx)> bandPara,
        out double appendedBottom)
    {
        // Lowest baseline (paragraph-line Y space) of any line the repack CREATED
        // below the paragraph's last existing baseline; NaN when everything fit.
        appendedBottom = double.NaN;
        if (matchLine < 0 || matchLine >= paraLines.Count) return false;

        // A replacement RESTYLED by the caller (font/size assigned before the text)
        // can't ride the byte-level run mover — the rewritten run must switch to the
        // new face. The restyled content drops onto a FRESH line below
        // the match (the prefix keeps its line) and flows at the new size.
        if (StyledCascadeFromMatch(page, paraLines, matchLine, myLLX, oldText, newText))
        {
            if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
                Console.Error.WriteLine("[reflow-path] styled");
            return true;
        }

        // Exact path first: MOVE the original runs (keeping their bytes,
        // fonts, kerning and per-run Tc) and rewrite only the matched operator,
        // re-encoded in its own font. Positions are then preserved to hundredths
        // of a point. Falls back to the coarser delete-and-re-emit below when the page
        // structure defeats it (CID font, replacement glyphs missing from the subset,
        // match not carried by a single run).
        {
            // The mover works from page OPERATORS, so it takes each line on its TRUE baseline.
            // A rect bottom sits a descent below the operator it describes, and slop wide
            // enough to absorb that descent is also wide enough to capture a NEIGHBOURING
            // line's operator and drag it into the reflow. (The merged-line view supplies the
            // COLUMN below; the lines themselves stay the ones this paragraph was grown from,
            // since re-packing a merged view repositions runs the paragraph never owned.)
            var rlines = new System.Collections.Generic.List<(double y, double lx, double rx)>();
            for (int li = matchLine; li < paraLines.Count; li++)
            {
                var by = LineBaseline(paraLines[li]);
                double llx = paraLines[li].lx, lrx = paraLines[li].rx;
                // A line drawn as several operators reaches further than any ONE of its
                // fragments, so take the merged line's span where one covers this baseline.
                // Its SPAN only: the lines re-packed stay the paragraph's own, since packing
                // a merged view repositions runs the paragraph never owned.
                foreach (var b in bandPara)
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
            foreach (var l in paraLines)
            {
                if (l.lx < pLeft) pLeft = l.lx;
                if (l.rx > maxRx) maxRx = l.rx;
            }
            // The merged view knows the paragraph's real edges; the fragment view sees only
            // the runs the absorber happened to split out.
            foreach (var b in bandPara)
            {
                if (b.lx < pLeft) pLeft = b.lx;
                if (b.rx > maxRx) maxRx = b.rx;
            }
            int paraLineCount = System.Math.Max(paraLines.Count, bandPara.Count);
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
                : paraLineCount >= 2 ? maxRx : System.Math.Max(pageRightMargin, maxRx);
            // Never past the sheet. The column is read from the page's own text, and once one
            // reflow has pushed a line off the page every later one reads that inflated extent
            // as the column and pushes further — a page whose widest line was 580 ran out to
            // 657 on a 612 pt sheet. RightAdjustment is the caller asking for a wider border
            // and keeps its say.
            if (rightAdj <= 0 && page.MediaBox is { } mbClamp && mbClamp.URX > 0)
                rMargin = System.Math.Min(rMargin, mbClamp.URX);
            var mover = new TextReplacer();
            if (mover.ReflowFromMatch(page, oldText, newText, myLLX, rlines, pLeft, rMargin, rPitch,
                    _replaceOptions?.AdjustmentNewLineSpacing ?? 0))
            {
                if (mover.ReflowCreatedLines > 0)
                {
                    // Mirror the mover's created-line advance (mean pitch below the
                    // edited line) in paragraph-line Y space for the clip expansion.
                    double meanPitch = rlines.Count >= 2
                        ? (rlines[0].y - rlines[^1].y) / (rlines.Count - 1)
                        : rPitch;
                    appendedBottom = rlines[^1].y - meanPitch * mover.ReflowCreatedLines;
                }
                page.ResetContentsCache();
                if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
                    Console.Error.WriteLine($"[reflow-path] mover rMargin={rMargin:F2} pLeft={pLeft:F2} rline0={rlines[0].y:F3} paraY0={paraLines[matchLine].y:F3} bp={(paraLines[matchLine].f.BaselinePosition is null ? "null" : paraLines[matchLine].f.BaselinePosition!.YIndent.ToString("F3"))}");
                return true;
            }
        }
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
            Console.Error.WriteLine("[reflow-path] cascade-fallback");

        // Effective (page-space) font scale: producers that draw each run in its own
        // q/cm/BT..ET/Q block size text via Tm with the CTM shrinking it back; measuring
        // or re-emitting at the raw Tm size would be wrong by the CTM factor.
        double ctmScale = 1.0;
        if (ExtractionCtm is { } ectm)
        {
            var det = System.Math.Abs(ectm.A * ectm.D - ectm.B * ectm.C);
            if (det > 1e-9) ctmScale = System.Math.Sqrt(det);
        }

        // Collect the segments to re-flow, each one source run: on the match line those
        // at/after the match X, on the following paragraph lines all of them.
        var moved = new System.Collections.Generic.List<(TextSegment seg, double x, double y)>();
        for (int li = matchLine; li < paraLines.Count; li++)
        {
            foreach (var seg in paraLines[li].f.Segments)
            {
                if (seg.Position is not { } sp) continue;
                if (string.IsNullOrEmpty(seg.Text)) continue;
                if (li == matchLine && sp.XIndent < myLLX - 0.5) continue; // prefix stays
                moved.Add((seg, sp.XIndent, sp.YIndent));
            }
        }
        if (moved.Count == 0) return false;
        moved.Sort((a, b) => b.y != a.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));

        // The first moved segment must carry the matched token (a match hidden mid-run
        // with a prefix inside the same run is left to the in-place replace path).
        var head = moved[0].seg.Text;
        int occ = head.IndexOf(oldText, System.StringComparison.Ordinal);
        if (occ < 0) return false;

        // Combined text from the match onward. Same-line neighbours concatenate verbatim
        // (their spacing rides in the runs); a line break is a word boundary. NBSPs fold
        // to plain spaces — producers that pad word gaps with U+00A0 would otherwise glue
        // the NBSP onto the next word through the space-split below, and the re-emitted
        // line would never phrase-match a plain-space search.
        var sb = new System.Text.StringBuilder();
        sb.Append(head.Replace(oldText, newText, System.StringComparison.Ordinal));
        for (int i = 1; i < moved.Count; i++)
        {
            bool lineBreak = System.Math.Abs(moved[i].y - moved[i - 1].y) > 0.75;
            if (lineBreak && sb.Length > 0 && sb[^1] != ' ' && !moved[i].seg.Text.StartsWith(" "))
                sb.Append(' ');
            sb.Append(moved[i].seg.Text);
        }
        sb.Replace('\u00A0', ' ');

        // Measure/emit in the paragraph's dominant face at the effective size.
        var domSeg = moved[0].seg;
        foreach (var m in moved)
            if (m.seg.Text.Trim().Length > domSeg.Text.Trim().Length) domSeg = m.seg;
        var font = domSeg.TextState.Font ?? TextState.Font;
        double rawFs = domSeg.TextState.FontSize > 0 ? domSeg.TextState.FontSize : TextState.FontSize;
        double effFs = rawFs * ctmScale;
        if (font is null || effFs <= 0.5) return false;
        // Prefer the SYSTEM face of the same family for measuring and re-emission. The
        // source font is typically an embedded SUBSET whose width table is keyed by its
        // custom byte codes, so measuring Unicode text against it mis-indexes the widths;
        // the system face carries the true advances (the reflow is measured
        // with these), and embedding it makes the absorber read the same metrics back, so
        // the re-emitted words land at consistent positions.
        var faceName = font.FontName ?? string.Empty;
        int subsetPlus = faceName.IndexOf('+');
        if (subsetPlus >= 0 && subsetPlus + 1 < faceName.Length)
            faceName = faceName[(subsetPlus + 1)..];
        int styleComma = faceName.IndexOf(',');
        if (styleComma > 0) faceName = faceName[..styleComma];
        if (faceName.Length > 0
            && FontRepository.TryFindFont(faceName, ignoreCase: true) is { } sysFont)
            font = sysFont;

        double leftX = double.MaxValue, rightX = 0;
        for (int li = matchLine; li < paraLines.Count; li++)
        {
            if (paraLines[li].lx < leftX) leftX = paraLines[li].lx;
            if (paraLines[li].rx > rightX) rightX = paraLines[li].rx;
        }
        // Never past the sheet: a paragraph an earlier reflow already pushed off the page
        // would otherwise be read as a column that wide and pushed further.
        if (page.MediaBox is { } mbFb && mbFb.URX > 0) rightX = System.Math.Min(rightX, mbFb.URX);
        if (rightX - leftX < 10 || rightX <= myLLX + 5) return false;

        // Greedy pack: first line from the match X, continuation lines from their own
        // ORIGINAL left margin (hanging-indent items keep the continuation indent);
        // lines created beyond the paragraph continue at the last line's indent.
        double LxAt(int i2)
        {
            int li2 = matchLine + i2;
            return li2 < paraLines.Count ? paraLines[li2].lx : paraLines[^1].lx;
        }
        // Tokenise KEEPING each gap's space run: a double space is preserved
        // where the replacement's own trailing space meets the source's
        // (a "text.  1" tail), and a break seam's space stays on the closing
        // line when it still fits that line's extent.
        var srcJoined = sb.ToString();
        var toks = new System.Collections.Generic.List<(string w, int sp)>();
        {
            var iT = 0;
            while (iT < srcJoined.Length)
            {
                if (srcJoined[iT] == ' ')
                {
                    if (toks.Count > 0)
                    {
                        var lastT = toks[^1];
                        toks[^1] = (lastT.w, lastT.sp + 1);
                    }
                    iT++;
                    continue;
                }
                var stT = iT;
                while (iT < srcJoined.Length && srcJoined[iT] != ' ') iT++;
                toks.Add((srcJoined[stT..iT], 0));
            }
        }
        var words = new string[toks.Count];
        for (var wi2 = 0; wi2 < toks.Count; wi2++) words[wi2] = toks[wi2].w;
        if (words.Length == 0) return false;
        // A repository CJK face can come back with a FLAT 1-em-per-unit width table
        // (space = a full em, surrogate pairs = two) — degenerate for packing Latin
        // replacement text. Detect it by the space width and fall through to the raw
        // font program's own advances (Latin ~0.5 em, a surrogate pair one '?' pair).
        // A repository face measures through its RAW program metrics: the dict-based
        // Metrics of a system face routes standard families to the Helvetica AFM
        // (5-10% off Arial's true advances - an 82-char token measured 531
        // instead of its drawn 600.7 and never wrapped), and a repository CJK face
        // comes back with a FLAT 1-em-per-unit table.
        bool degenerateMetrics = false;
        try
        {
            degenerateMetrics = font.SourceFontData is not null;
        }
        catch { }
        // A CJK-family face writes a LATIN replacement in the face's OWN half-width
        // Latin cells: the re-emitted subset carries a flat 500/1000 /W
        // for every ASCII glyph — space and digits included — so a 47-char
        // sentence spans exactly 235 pt at fs 10 and its tail run seats at 325
        // (measured from the expected content stream and font /W).
        // The CJK glyphs keep the face's full-width em advances.
        var packFamily = font.FontName ?? string.Empty;
        var subsetPlusP = packFamily.IndexOf('+');
        if (subsetPlusP >= 0 && subsetPlusP + 1 < packFamily.Length)
            packFamily = packFamily[(subsetPlusP + 1)..];
        bool cjkBase = !Standard14Fonts.IsStandard14(packFamily);
        double MixedW(string w)
        {
            double t = 0;
            for (var mi = 0; mi < w.Length; mi++)
            {
                var mc = w[mi];
                if (mc < 0x100)
                {
                    t += effFs * 0.5;   // half-width Latin cell of the CJK face
                    continue;
                }
                if (char.IsHighSurrogate(mc) && mi + 1 < w.Length && char.IsLowSurrogate(w[mi + 1]))
                {
                    mi++;
                    t += effFs;   // a supplementary CJK glyph advances a full em
                    continue;
                }
                t += effFs;       // BMP CJK: full-width cell
            }
            return t;
        }
        double SpaceW()
        {
            if (cjkBase) return effFs * 0.5;   // the CJK face's half-width space cell
            try
            {
                return degenerateMetrics
                    ? font.SourceFontData!.MeasureString(" ", effFs)
                    : font.MeasureString(" ", effFs);
            }
            catch { return effFs * 0.25; }
        }
        double WordW(string w)
        {
            if (cjkBase) return MixedW(w);
            try
            {
                return degenerateMetrics
                    ? font.SourceFontData!.MeasureString(w, effFs)
                    : font.MeasureString(w, effFs);
            }
            catch { return w.Length * effFs * 0.5; }
        }
        // The SOURCE line's own right extent, for the seam-space rule below; lines
        // beyond the grid read the paragraph width.
        double RxAt(int i2)
        {
            int li2 = matchLine + i2;
            return li2 < paraLines.Count ? paraLines[li2].rx : rightX;
        }
        var packed = new System.Collections.Generic.List<string>();
        var cur = new System.Text.StringBuilder();
        double curX = myLLX, curW = 0, spaceW = SpaceW();
        var pendSp = 0;   // the space run between the previous token and the next
        foreach (var (w, spAfter) in toks)
        {
            double ww = WordW(w);
            double trial = cur.Length == 0 ? ww : curW + pendSp * spaceW + ww;
            if (curX + trial <= rightX + 0.5 || cur.Length == 0)
            {
                if (cur.Length > 0) cur.Append(' ', pendSp);
                cur.Append(w);
                curW = trial;
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
                var seamRx = packed.Count == 0 ? rightX : RxAt(packed.Count);
                if (pendSp > 0 && curX + curW + pendSp * spaceW <= seamRx + 0.5)
                    cur.Append(' ', pendSp);
                packed.Add(cur.ToString());
                cur.Clear(); cur.Append(w);
                curW = ww; curX = LxAt(packed.Count);
            }
            pendSp = spAfter > 0 ? spAfter : 1;
        }
        if (cur.Length > 0) packed.Add(cur.ToString());

        // Existing baselines from the match line down; extend below by the pitch if the
        // packed text needs more lines than the paragraph had.
        var baselines = new System.Collections.Generic.List<double>();
        // The delete-and-re-emit fallback re-writes runs at the source lines' own positions,
        // which already carry the source font's descent — unlike the byte-level mover above,
        // which re-anchors operators on their true baselines.
        for (int li = matchLine; li < paraLines.Count; li++) baselines.Add(paraLines[li].y);
        double pitch = baselines.Count >= 2
            ? (baselines[0] - baselines[^1]) / (baselines.Count - 1)
            : 1.2 * effFs;
        if (pitch <= 0) pitch = 1.2 * effFs;
        if (packed.Count > baselines.Count)
            appendedBottom = baselines[^1] - pitch * (packed.Count - baselines.Count);

        // Delete the source runs by REGION, one line at a time, at operator granularity:
        // producers that draw one word (or one bare space) per operator defeat text-keyed
        // deletion — the absorber's coalesced segment text (with synthesized gap spaces)
        // never equals any single operator's decode. Every text operator starting inside
        // the line's X-span goes; the match line is cleared only from the match X on, so
        // its prefix stays put.
        for (int li = matchLine; li < paraLines.Count; li++)
        {
            double xmin = (li == matchLine ? myLLX : paraLines[li].lx) - 0.5;
            double xmax = paraLines[li].rx + 1.0;
            if (xmax <= xmin) continue;
            var del = new TextReplacer
            {
                MatchAnyOperator = true,
                TargetY = paraLines[li].y,
                TargetX = (xmin + xmax) / 2,
                TargetXTolerance = (xmax - xmin) / 2,
            };
            del.Replace(page, string.Empty, string.Empty);
        }

        // Re-emit the packed lines.
        var tb = new TextBuilder(page);
        for (int i = 0; i < packed.Count; i++)
        {
            double by = i < baselines.Count ? baselines[i] : baselines[^1] - (i - baselines.Count + 1) * pitch;
            var frag = new TextFragment(packed[i]);
            frag.TextState.Font = font;
            if (domSeg.TextState.FontName is { Length: > 0 } fn) frag.TextState.FontName = fn;
            frag.TextState.FontSize = (float)effFs;
            if (TextState.ForegroundColor is { } fg) frag.TextState.ForegroundColor = fg;
            frag.Position = new Position(i == 0 ? myLLX : LxAt(i), by);
            tb.AppendText(frag);
        }
        page.ResetContentsCache();
        return true;
    }
}
