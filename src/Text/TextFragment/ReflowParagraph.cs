
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>The /Descent (1/1000 units, negative) of the first page font
    /// resource matching this fragment's family — the same value the absorber
    /// used when it anchored the original segments' positions. Zero when no
    /// matching resource carries one.</summary>
    private double SourceDescentUnits(Page page)
    {
        var reader = page.Reader;
        var fonts = Aspose.Pdf.Text.TextAbsorber.ResolveFonts(page.Dict, reader);
        var family = TextState.FontName ?? "";
        // A page can carry several same-family faces with different descents
        // (a subset "…+ArialMT" title next to the paragraph's "Arial"): the
        // fragment's own face — exact family equality — wins over a substring
        // relative; the loose match is only the no-exact-hit fallback.
        double loose = 0;
        foreach (var (_, fd) in fonts)
        {
            var bf = fd.GetName("BaseFont") ?? "";
            var plus = bf.IndexOf('+');
            if (plus >= 0 && plus + 1 < bf.Length) bf = bf[(plus + 1)..];
            var bfFamily = bf.Split('-')[0].Split(',')[0];
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(bfFamily)) continue;
            var exact = string.Equals(bfFamily, family, StringComparison.OrdinalIgnoreCase);
            if (!exact && !bfFamily.Contains(family, StringComparison.OrdinalIgnoreCase)
                && !family.Contains(bfFamily, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var fm = FontMetrics.FromFontDict(fd, reader);
                if (fm is not null && fm.Descent != 0)
                {
                    if (exact) return fm.Descent;
                    if (loose == 0) loose = fm.Descent;
                }
            }
            catch { }
        }
        return loose;
    }

    /// <summary>Measure text with the SOURCE font resources' own width tables
    /// (the tables re-absorption measures with), instead of a host-face
    /// approximation. Returns null when the page has no font of this family;
    /// the returned function yields a negative value for unencodable text.</summary>
    private Func<string, double, double>? BuildSourceMeasurer(Page page)
    {
        var reader = page.Reader;
        var fonts = Aspose.Pdf.Text.TextAbsorber.ResolveFonts(page.Dict, reader);
        var family = TextState.FontName ?? "";
        var cands = new List<(FontMetrics M, bool Cid, Dictionary<char, int>? Rev)>();
        foreach (var (_, fd) in fonts)
        {
            var bf = fd.GetName("BaseFont") ?? "";
            var plus = bf.IndexOf('+');
            if (plus >= 0 && plus + 1 < bf.Length) bf = bf[(plus + 1)..];
            var bfFamily = bf.Split('-')[0].Split(',')[0];
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(bfFamily)
                || (!bfFamily.Contains(family, StringComparison.OrdinalIgnoreCase)
                    && !family.Contains(bfFamily, StringComparison.OrdinalIgnoreCase)))
                continue;
            FontMetrics? fm;
            try { fm = FontMetrics.FromFontDict(fd, reader); } catch { continue; }
            if (fm is null) continue;
            var cid = fd.GetName("Subtype") == "Type0";
            Dictionary<char, int>? rev = null;
            if (cid)
            {
                var tu = Aspose.Pdf.Text.TextAbsorber.ParseToUnicodeFromDict(fd, reader);
                if (tu is null) continue;
                rev = new Dictionary<char, int>();
                foreach (var (code, str) in tu)
                    if (str.Length == 1 && !rev.ContainsKey(str[0])) rev[str[0]] = code;
            }
            cands.Add((fm, cid, rev));
        }
        if (cands.Count == 0) return null;
        return (s, sz) =>
        {
            foreach (var (fm, cid, rev) in cands)
            {
                double w = 0;
                var ok = true;
                foreach (var c in s)
                {
                    var code = cid ? (rev!.TryGetValue(c, out var cc) ? cc : -1) : c;
                    if (code < 0) { ok = false; break; }
                    var gw = fm.GetWidth(code);
                    if (gw <= 0 && c != ' ') { ok = false; break; }
                    w += gw;
                }
                if (ok) return w * sz / 1000.0;
            }
            return -1;
        };
    }

    private bool TryReflowParagraph(Page page, string oldText, string newText)
    {
        if (_position is not { } myPos || TextState.Font is not { } myFont || TextState.FontSize <= 0)
            return false;
        double fs = TextState.FontSize;

        // Precompute geometry so Position (non-null after this filter) isn't re-dereferenced.
        var lines0 = new System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)>();
        // Full left span per line (min of the rect edge and the leftmost visible
        // segment) — a back-jump line's rect can START RIGHT of its own earlier-drawn
        // segments, and the match-containment test below must still accept a match
        // inside those segments.
        var spanLx = new System.Collections.Generic.Dictionary<TextFragment, double>();
        // The page's lines. The `.+` regex sweep is the primary reading, but it is a REGEX
        // over the page's assembled text: a producer that draws each line in its own
        // BT/ET with no separator between them leaves nothing for `.+` to stop at, so the
        // whole page comes back as ONE fragment and the paragraph can never be located.
        // The plain absorber reads the same page as one fragment per line, so fall back to
        // it whenever the regex collapses the page.
        var absorbers = new System.Collections.Generic.List<TextFragmentAbsorber>();
        {
            var rx0 = new TextFragmentAbsorber(".+", new TextSearchOptions(true));
            // The line fragments absorbed here are deleted in place (see below); pin
            // ReplaceAdjustment.None so the deletion never shifts other same-line
            // content, independent of the absorber's ShiftRestOfLine default.
            rx0.TextReplaceOptions = new TextReplaceOptions(TextReplaceOptions.ReplaceAdjustment.None);
            absorbers.Add(rx0);
        }
        var abs = absorbers[0];
        page.Accept(abs);
        if (abs.TextFragments.Count <= 1)
        {
            var plain = new TextFragmentAbsorber();
            plain.TextReplaceOptions = new TextReplaceOptions(TextReplaceOptions.ReplaceAdjustment.None);
            plain.Visit(page);
            if (plain.TextFragments.Count > abs.TextFragments.Count) abs = plain;
        }
        // The same lines WITHOUT the blank-only filter, used only to read the column's
        // geometry. A blank line is a baseline of the block like any other; dropping it
        // turns one line pitch into two and the block breaks there, so a paragraph that
        // merely has an empty line in it reads as two short blocks and the reflow wraps
        // against one line's extent instead of the column's.
        var bandSource = new System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)>();
        foreach (TextFragment f in abs.TextFragments)
        {
            var p = f.PositionOrNull;
            if (p is null) continue;
            var rect = f.Rectangle;
            if (rect is null) continue;
            if (string.IsNullOrWhiteSpace(f.Text))
            {
                bandSource.Add((f, p.YIndent, rect.LLX, rect.URX));
                spanLx[f] = rect.LLX;
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
            spanLx[f] = System.Math.Min(rect.LLX, lx);
            lines0.Add((f, p.YIndent, lx, rect.URX));
            bandSource.Add((f, p.YIndent, lx, rect.URX));
        }
        if (lines0.Count == 0) return false;
        // Top-to-bottom (PDF Y grows upward, so higher YIndent = higher on page).
        lines0.Sort((a, b) => b.y.CompareTo(a.y));

        // Find the re-absorbed line that CONTAINS this fragment. The fragment's own
        // LLX is the X of the matched token, which may sit mid-line (e.g. "{{Name}}"
        // embedded in flowing text), so match by Y proximity plus X-within-[lx,rx]
        // rather than assuming the fragment starts at the line's left margin.
        double myLLX = _rectangle!.LLX;
        int myIdx = -1; double best = fs;
        for (int i = 0; i < lines0.Count; i++)
        {
            double dy = System.Math.Abs(lines0[i].y - myPos.YIndent);
            bool xin = myLLX >= spanLx[lines0[i].f] - 5 && myLLX <= lines0[i].rx + 5;
            if (dy <= best && xin && dy < fs) { best = dy; myIdx = i; }
        }
        // A SIBLING replacement may already have reflowed this page, so the position this
        // fragment recorded when it was absorbed can name a line that no longer exists. Re-anchor
        // on the text instead: the line that still carries the search text is this fragment's
        // line wherever the reflow moved it. Only when exactly one line carries it — two
        // candidates and the position was the only thing that told them apart.
        if (myIdx < 0)
        {
            int byText = -1;
            for (int i = 0; i < lines0.Count; i++)
            {
                if (lines0[i].f.Text.IndexOf(oldText, System.StringComparison.Ordinal) < 0) continue;
                if (byText >= 0) { byText = -1; break; }
                byText = i;
            }
            if (byText >= 0) myIdx = byText;
        }
        if (myIdx < 0) return false;
        double leftX = lines0[myIdx].lx;

        // Grow the paragraph up/down over contiguous same-left-margin lines (one line pitch apart).
        // A line is only merged if it shares the left margin AND is close in font SIZE: a bigger
        // heading (e.g. a 24pt bold title above 12pt body, same left margin) is a SEPARATE
        // paragraph, so merging it would collapse it to body size on reflow. Same-size paragraphs
        // (the common case) are unaffected.
        const double xtol = 3.0;
        // IgnoreParagraphs = continuous-flow reflow: the replacement flows through the WHOLE text
        // block, ignoring paragraph boundaries. Grow across all contiguous same-size lines
        // regardless of left-margin changes so the entire block reflows as one unit and cascades
        // down naturally (no separate push-down of trailing paragraphs needed). Default mode keeps
        // the strict same-left-margin grow.
        bool ignorePara = _replaceOptions?.IgnoreParagraphs ?? false;
        double paraFs = lines0[myIdx].f.TextState.FontSize;
        if (paraFs <= 0) paraFs = fs;
        bool SizeCompatible(double lineFs) =>
            lineFs <= 0 || (lineFs <= paraFs * 1.35 && lineFs >= paraFs / 1.35);
        // The page's lines interleave by Y once it has more than one COLUMN, so the entry
        // directly below the match in reading order can belong to the column beside it. Such
        // a line is neither part of this paragraph nor a break in it 
        // next line simply sits further down the list 
        // horizontal span MEETS the match line's and steps over the rest.
        var colLines = new System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)>();
        int myCol = 0;
        for (int i = 0; i < lines0.Count; i++)
        {
            if (i != myIdx && (lines0[i].lx > lines0[myIdx].rx || lines0[i].rx < lines0[myIdx].lx)) continue;
            if (i == myIdx) myCol = colLines.Count;
            colLines.Add(lines0[i]);
        }
        int lo = myCol, hi = myCol;
        // Hanging-indent lists (numbered/bulleted items): the item's FIRST line sits at a
        // dedented margin and its continuation lines share a deeper indent. The whole
        // item reflows as one paragraph, so grouping accepts one indent step
        // down from the match line (establishing the continuation indent) and, growing up
        // from a continuation line, the single dedented head line (then stops). An indent
        // step going UP is the tail of the PREVIOUS item — never merged. Any step must
        // keep the paragraph's OWN line pitch (≤1.35×): a dedented line a line-and-a-half
        // away (a salutation above an indented body, a heading) is a separate paragraph.
        const double maxHang = 40.0;
        const double stepPitchTol = 1.35;
        // The paragraph's OWN line pitch, measured from the contiguous run of lines
        // around the match. A gap materially wider than it is a PARAGRAPH BREAK (the
        // blank line between two blocks) — merging across it would let the reflow pull
        // the next paragraph's opening words up onto this paragraph's last line, which
        // must never happen. Continuous-flow mode deliberately ignores breaks.
        double paraPitch = ParagraphPitch(colLines, myCol, fs);
        double maxMergeGap = ignorePara ? 3 * fs : paraPitch * stepPitchTol;
        double downLx = colLines[myCol].lx;
        bool hangStepped = false;
        while (hi + 1 < colLines.Count)
        {
            double gap = colLines[hi].y - colLines[hi + 1].y;
            if (!(gap > 0 && gap <= maxMergeGap) || !SizeCompatible(colLines[hi + 1].f.TextState.FontSize))
                break;
            double lx = colLines[hi + 1].lx;
            if (ignorePara || System.Math.Abs(lx - downLx) <= xtol) { hi++; continue; }
            if (!hangStepped && hi == myCol && lx - downLx > xtol && lx - downLx <= maxHang)
            {
                double nextGap = hi + 2 < colLines.Count ? colLines[hi + 1].y - colLines[hi + 2].y : gap;
                if (nextGap > 0 && gap <= stepPitchTol * nextGap)
                {
                    hangStepped = true; downLx = lx; hi++; continue;
                }
            }
            break;
        }
        double upLx = colLines[myCol].lx;
        while (lo - 1 >= 0)
        {
            double gap = colLines[lo - 1].y - colLines[lo].y;
            if (!(gap > 0 && gap <= maxMergeGap) || !SizeCompatible(colLines[lo - 1].f.TextState.FontSize))
                break;
            double lx = colLines[lo - 1].lx;
            if (ignorePara || System.Math.Abs(lx - upLx) <= xtol) { lo--; continue; }
            if (upLx - lx > xtol && upLx - lx <= maxHang)
            {
                double refGap = lo < hi ? colLines[lo].y - colLines[lo + 1].y : gap;
                if (refGap > 0 && gap <= stepPitchTol * refGap) { lo--; break; }
            }
            break;
        }

        var paraLines = colLines.GetRange(lo, hi - lo + 1);
        // Continuous flow anchors the re-emitted block at the flow's leftmost x.
        if (ignorePara)
            foreach (var l in paraLines) if (l.lx < leftX) leftX = l.lx;
        // Replace PER LINE (mirroring the per-fragment absorber), then reunite — an occurrence
        // split across a line break isn't a single-line match and is left intact (a
        // per-fragment replace also misses line-straddling occurrences).
        var origParts = new System.Collections.Generic.List<string>();
        var newParts = new System.Collections.Generic.List<string>();
        foreach (var l in paraLines)
        {
            var t = l.f.Text.Trim();
            origParts.Add(t);
            newParts.Add(t.Replace(oldText, newText, System.StringComparison.Ordinal));
        }
        var origText = string.Join(" ", origParts);
        var replaced = string.Join(" ", newParts);
        // Whole-paragraph replacement: when the matched fragment IS the entire paragraph
        // (oldText spans every line, e.g. a paragraph->paragraph+paragraph replace), no
        // single-line Replace fires, so replaced==origText. Detect that by comparing the
        // paragraph body to oldText ignoring all whitespace (robust to reconstruction
        // spacing differences) and re-wrap the replacement directly. Otherwise there is no
        // within-line occurrence in this paragraph and sibling fragments must no-op.
        bool wholePara = false;
        if (replaced == origText)
        {
            static string Squash(string s) =>
                System.Text.RegularExpressions.Regex.Replace(s, @"\s+", string.Empty);
            if (Squash(oldText) == Squash(origText)) { replaced = newText; wholePara = true; }
            // An occurrence that STRADDLES one of the paragraph's line breaks is a real
            // match of this paragraph even though no single line carries it: the absorber
            // spells the break out ("leap \ninto electronic") and the flow reads it as the
            // word gap it is. Replace it in the joined text and let the cascade below wrap
            // the result back across those baselines.
            else if (StraddlingReplace(origText, oldText, newText) is { } acrossBreak)
                replaced = acrossBreak;
            else return false;
        }

        // Mid-token replacement (default flow): cascade from the MATCH
        // position — the paragraph lines above the match and the match line's prefix
        // stay untouched; text from the match onward re-packs onto the EXISTING baselines.
        // When the cascade can't handle the page's structure (CID font, cross-run match,
        // glyphs missing from the subset…) FALL THROUGH to the whole-paragraph re-wrap
        // below — bailing out entirely would leave the plain in-place replace to grow the
        // line past the page edge.
        // A LONE line has no following baselines to re-pack onto, so a replacement
        // that overflows it cannot cascade — it needs the free-space re-wrap below,
        // which takes its column from the page rather than from the line.
        // ...and only when the match IS that line: a token embedded in a longer line
        // still has its line-mates to re-pack against, so it cascades as before.
        var lonelyOverflow = paraLines.Count == 1
            && _rectangle is { } myRect
            && myRect.Width >= (paraLines[0].rx - paraLines[0].lx) * 0.9
            && MeasureOrEstimate(TextState.Font!, newText, fs, false) > (paraLines[0].rx - myLLX) * 1.05;
        double paraLeftX = double.MaxValue, paraRightX = 0;
        foreach (var l in paraLines)
        {
            if (l.lx < paraLeftX) paraLeftX = l.lx;
            if (l.rx > paraRightX) paraRightX = l.rx;
        }
        double pageCol = PageTextRightMargin(page, lines0, paraLeftX, paraRightX);
        // The same paragraph, grown over MERGED lines rather than over absorbed fragments —
        // this is what the mover wraps against, and the only view in which a line drawn as
        // several operators has one left edge and one right edge.
        var bands = MergeBaselines(bandSource, spanLx);
        var bandPara = GrowBandParagraph(bands, LineBaseline(paraLines[myCol - lo]), myLLX,
            paraPitch * stepPitchTol, xtol);
        double bandColumnRight = 0;
        foreach (var b in bandPara) if (b.rx > bandColumnRight) bandColumnRight = b.rx;
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
            Console.Error.WriteLine($"[reflow-para] lines0={lines0.Count} bands={bands.Count} bandPara={bandPara.Count} bandRight={bandColumnRight:F2} paraPitch={paraPitch:F2} fs={fs:F2} paraLines={paraLines.Count} pageCol={pageCol:F2} lonely={lonelyOverflow} myLLX={myLLX:F2} matchBase={LineBaseline(paraLines[myCol - lo]):F2}");
        if (!wholePara && !ignorePara && !lonelyOverflow
            && CascadeFromMatch(page, paraLines, myCol - lo, myLLX, oldText, newText,
                pageCol, bandPara,
                out var cascadeBottom))
        {
            ExpandClipsToReflowBottom(page, paraLines, cascadeBottom);
            return true;
        }

        double rightX = 0;
        foreach (var l in paraLines) if (l.rx > rightX) rightX = l.rx;
        // A line drawn as several operators reaches further than any one of its fragments.
        if (bandColumnRight > rightX) rightX = bandColumnRight;
        // ...but never past the sheet (see the cascade's clamp).
        if (page.MediaBox is { } mbCoarse && mbCoarse.URX > 0)
            rightX = System.Math.Min(rightX, mbCoarse.URX);
        // Continuous-flow (IgnoreParagraphs): page-bound the wrap width. A previous longer
        // replacement can leave an over-wide unbreakable-token line, and re-absorbing that inflated
        // max-URX would compound the overflow. Cap the right border at the page's usable right edge
        // (mirror the left inset) so the flow wraps within the page instead of running off it.
        var pageRect = page.Rect;
        // A one-line "paragraph" carries no column width of its own: a lone token
        // sitting in free space would re-wrap to its own token width. Such a flow
        // takes the page as its column — the left inset mirrored on the right —
        // and stops short of the nearest text to its right on the same line, whose
        // own size sets the gap. (The MOVER reads the page's own text column instead;
        // this path re-emits into free space, where there is no column to read.)
        // A LONE line wraps at ITS OWN right edge when it HAS one. Probed on a synthetic
        // 400 pt sheet whose single line of text ends at 161: every replacement length wraps
        // the tail at 161 - a 5-character one pushes the last word over, a 20-character one
        // puts the replacement itself on a fresh line - and none of them reaches the sheet.
        // The page column is for the case this path was written for: a match that IS the
        // whole line, sitting in free space with no text of its own to the right and so no
        // edge to read. Wrapping THAT to its own extent would re-wrap a token to its own
        // width. So the page stands in only when the line carries nothing past the match.
        var lineHasTail = rightX > myLLX + (_rectangle?.Width ?? 0) + fs;
        if (paraLines.Count == 1 && pageRect is not null && !lineHasTail)
        {
            double lonelyInset = leftX - pageRect.LLX;
            double lonelyRight = pageRect.URX - (lonelyInset > 0 ? lonelyInset : 0);
            foreach (var l in lines0)
            {
                if (ReferenceEquals(l.f, paraLines[0].f)) continue;
                if (System.Math.Abs(l.y - myPos.YIndent) > fs) continue;
                if (l.lx <= myLLX) continue;
                var nfs = l.f.TextState.FontSize > 0 ? l.f.TextState.FontSize : fs;
                var clipped = l.lx - (nfs - 1);
                if (clipped < lonelyRight) lonelyRight = clipped;
            }
            if (lonelyRight > leftX + 10) rightX = lonelyRight;
        }
        if (ignorePara && pageRect is not null)
        {
            double leftInset = leftX - pageRect.LLX;
            double pageRight = pageRect.URX - (leftInset > 0 ? leftInset : 0);
            if (pageRight > leftX + 10 && rightX > pageRight) rightX = pageRight;
        }
        // RightAdjustment extends the wrap border to the right so a longer replacement
        // re-flows into more lines against the widened margin. It applies only to the
        // mid-line-token reflow; a whole-paragraph replace re-wraps to the paragraph's own
        // width and ignores RightAdjustment.
        double rightAdjust = wholePara ? 0 : (_replaceOptions?.RightAdjustment ?? 0);
        double width = (rightX - leftX) + rightAdjust;
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
            Console.Error.WriteLine($"[reflow] paraLines={paraLines.Count} leftX={leftX:F2} rightX={rightX:F2} width={width:F2} wholePara={wholePara} ignorePara={ignorePara} pageRect={(page.Rect is null ? "null" : page.Rect.ToString())}");
        if (width < 10) return false;

        // Re-flow in the paragraph's dominant font (the fragment carrying the most text),
        // so a lone bold word doesn't bold the whole paragraph and vice-versa.
        var domLine = paraLines[0].f;
        foreach (var l in paraLines) if (l.f.Text.Length > domLine.Text.Length) domLine = l.f;
        var domFont = domLine.TextState.Font ?? myFont;
        var domName = domLine.TextState.FontName ?? TextState.FontName;
        float domSize = domLine.TextState.FontSize > 0 ? domLine.TextState.FontSize : (float)fs;
        // A source font that cannot encode the replacement is substituted, and the
        // whole re-flow — wrap, widths and the written lines — runs in the stand-in.
        var reflowFace = ResolveSubstituteFace(domFont, replaced);
        var reflowMeasure = reflowFace is null ? null : Standard14Measurer(reflowFace);

        // Per source line: the seat of its LAST run (left-relative), that run's own width,
        // and the size it was drawn at — the three numbers the line-budget law needs.
        var lineCaps = new System.Collections.Generic.List<(double seat, double runW, double srcFs)>();
        if (wholePara)
            foreach (var l in paraLines)
            {
                double srcFs = l.f.TextState.FontSize > 0 ? l.f.TextState.FontSize : fs;
                double seat = l.rx - leftX, runW = 0;
                Aspose.Pdf.Rectangle? lastRun = null;
                foreach (TextSegment s in l.f.Segments)
                {
                    if (s.Rectangle is not { } sr || string.IsNullOrEmpty(s.Text)) continue;
                    if (lastRun is null || sr.LLX > lastRun.LLX) lastRun = sr;
                }
                if (lastRun is not null) { seat = lastRun.LLX - leftX; runW = lastRun.URX - lastRun.LLX; }
                lineCaps.Add((seat, runW, srcFs));
            }
        // The budget of re-flow line i. The LAST source line is not capacity-bound — the
        // re-flow runs its remainder out past that line's own extent — so it, and any line
        // past the grid, wraps at the paragraph's width.
        double LineBudget(int i, double newFs)
        {
            if (i < 0 || i >= lineCaps.Count - 1) return width;
            var c = lineCaps[i];
            double b = c.seat + c.runW * (c.srcFs > 0 ? newFs / c.srcFs : 1.0);
            return b > 0 && b < width ? b : width;
        }

        System.Collections.Generic.List<string> wrapped;
        if (wholePara)
        {
            // Shrink the font until the (larger) replacement fits the ORIGINAL rectangle,
            // HOLDING the line count measured at the original size, then re-wrap at the
            // fitted size. Compute the fit from the un-mutated original size (the fresh
            // re-absorb's, not THIS fragment's TextState which a caller may have already
            // shrunk via IsFitRectangle) and the original rectangle, so the result is
            // independent of the caller's font-size loop. Measure with a trailing space per
            // line: reserving one space width past each wrapped line breaks lines slightly
            // earlier and keeps the wrapped lines re-searchable across the line breaks.
            double origSize = domSize;
            double rectH = _rectangle!.Height;
            int nFit = WrapToWidth(replaced, domFont, origSize, width, trailingSpace: true, measure: reflowMeasure is null ? null : t => reflowMeasure(t, origSize)).Count;
            if (nFit < 1) nFit = 1;
            double fitFs = origSize;
            while (fitFs > 1.0 && nFit * 1.2 * fitFs > rectH) fitFs -= 0.5;
            domSize = (float)fitFs;
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
            wrapped = WrapToBudgets(replaced, domFont, fitFs, i => LineBudget(i, fitFs),
                trailingSpace: true, measure: reflowMeasure is null ? null : t => reflowMeasure(t, fitFs));
        }
        else
        {
            wrapped = WrapToWidth(replaced, domFont, domSize, width, allowCharBreak: ignorePara, measure: reflowMeasure is null ? null : t => reflowMeasure(t, domSize));
        }
        if (wrapped.Count == 0) return false;

        var baselines = new System.Collections.Generic.List<double>();
        foreach (var l in paraLines)
        {
            // Line anchors are the source lines' own positions, which carry the SOURCE
            // font's descent. That cancels out when the re-flow writes the same font — but
            // a substituted face has its own descent, so anchor on the run's true baseline
            // and let the stand-in's descent apply.
            var anchor = l.y;
            if (reflowFace is not null && (l.f.BaselinePosition ?? l.f.PositionOrNull) is { } bp)
                anchor = bp.YIndent;
            // An anchor is a box BOTTOM, and a box bottom hangs the source font's descent
            // under the baseline. Re-flowing at a SMALLER size shortens that descent, so
            // seating the new lines on the old bottoms would sink the whole block by the
            // difference. The baseline grid is what the re-flow keeps: lift each anchor by
            // the descent the block no longer has. Zero whenever the size is unchanged.
            double srcFs = l.f.TextState.FontSize > 0 ? l.f.TextState.FontSize : fs;
            baselines.Add(anchor + SeatDescentOf(domFont, srcFs) - SeatDescentOf(domFont, domSize));
        }
        double pitch = baselines.Count >= 2
            ? (baselines[0] - baselines[^1]) / (baselines.Count - 1)
            : 1.2 * domSize;
        if (pitch <= 0) pitch = 1.2 * domSize;

        foreach (var l in paraLines)
        {
            // The re-absorbed line fragments have ReplaceAdjustment.None (fresh absorber),
            // so this deletes in place via the normal replace machinery without recursing
            // back into paragraph reflow.
            try { l.f.Text = string.Empty; } catch { }
        }

        var tb = new TextBuilder(page);
        var laidOut = new System.Collections.Generic.List<(string text, double baseline, double width)>();
        double maxLineW = 0;
        for (int i = 0; i < wrapped.Count; i++)
        {
            double by = i < baselines.Count ? baselines[i] : baselines[^1] - (i - baselines.Count + 1) * pitch;
            var frag = new TextFragment(wrapped[i]);
            frag.TextState.Font = domFont;
            if (!string.IsNullOrEmpty(domName)) frag.TextState.FontName = domName;
            frag.TextState.FontSize = domSize;
            if (TextState.ForegroundColor is { } fg) frag.TextState.ForegroundColor = fg;
            if (reflowFace is not null)
            {
                frag.TextState.Std14FaceOverride = reflowFace;
                // Write the stand-in with its metrics, so the run reads back with
                // the stand-in's descent under its baseline.
                frag.TextState.EmitStandard14Descriptor = true;
            }
            frag.Position = new Position(leftX, by);
            tb.AppendText(frag);
            double lw;
            if (reflowMeasure is not null) lw = reflowMeasure(wrapped[i], domSize);
            else try { lw = domFont.MeasureString(wrapped[i], domSize); } catch { lw = wrapped[i].Length * domSize * 0.5; }
            laidOut.Add((wrapped[i], by, lw));
            if (lw > maxLineW) maxLineW = lw;
        }
        page.ResetContentsCache();
        if (wrapped.Count > baselines.Count)
            ExpandClipsToReflowBottom(page, paraLines,
                baselines[^1] - pitch * (wrapped.Count - baselines.Count));

        // A whole-paragraph replace re-points THIS fragment at the laid-out block so a caller
        // that reads fragment.Segments / fragment.Rectangle after the assignment (e.g. to add
        // a per-segment underline or a per-fragment highlight) sees the reflowed geometry. Box
        // mirrors the absorber: LLY = the last line's box bottom, URY = the first line's
        // bottom + 1.1 em; URX = the widest line, counting the space its break stands in for.
        if (wholePara && laidOut.Count > 0)
        {
            double firstBaseline = laidOut[0].baseline;
            double lastBaseline = laidOut[^1].baseline;
            double ascentH = 1.1 * domSize;
            _rectangle = new Rectangle(leftX, lastBaseline, leftX + maxLineW, firstBaseline + ascentH);
            _text = newText;
            _segments.Clear();
            foreach (var ln in laidOut)
            {
                var seg = new TextSegment(ln.text);
                seg.TextState.FontSize = domSize;
                if (!string.IsNullOrEmpty(domName)) seg.TextState.FontName = domName;
                seg.TextState.Font = domFont;
                seg.Owner = this;
                seg.Position = new Position(leftX, ln.baseline);
                // Each line gets its own page box — the same 1.1 em band as the block, around
                // THIS line's baseline. Without it a rebuilt segment measures itself at the
                // origin, and a caller decorating per segment (an underline per line) stacks
                // every decoration in the page corner.
                seg.Rectangle = new Rectangle(leftX, ln.baseline,
                    leftX + ln.width, ln.baseline + ascentH);
                seg.TextState.OwnerSegment = seg;
                _segments.Add(seg);
            }
        }
        return true;
    }
}
