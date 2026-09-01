using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>Split runs whose glyphs sit further apart than a column-gap threshold
    /// (Tc-spread table layouts draw several columns in ONE show op). Pieces keep
    /// per-char geometry (rebased) and the continuations carry GapSplit so the
    /// search text gets exactly one boundary space between them.</summary>
    private static void SplitRunsAtCharGaps(List<RawTextRun> runs)
    {
        for (var ri = 0; ri < runs.Count; ri++)
        {
            var run = runs[ri];
            var cum = run.CharCumWidths;
            var ends = run.CharEndPositions;
            var n = run.Text.Length;
            if (cum is null || ends is null || n < 2) continue;
            if (cum.Length < n || ends.Length < n) continue;
            var tmN = Math.Sqrt(run.TmA * run.TmA + run.TmB * run.TmB);
            var det = Math.Abs(run.Ctm.A * run.Ctm.D - run.Ctm.B * run.Ctm.C);
            var dev = (tmN > 1e-9 ? tmN : 1.0) * (det > 1e-12 ? Math.Sqrt(det) : 1.0) * run.HScaling;
            var effFs = run.FontSize * dev;
            // Column hops only: measured column spreads run 10-14em; everything
            // below ~4em (justified spaces, tracked titles, glued abbreviations)
            // stays inside the run.
            var threshold = Math.Max(24.0, 4.0 * effFs);
            List<int>? cuts = null;
            var wideGaps = 0;
            double minWide = double.MaxValue, maxWide = 0;
            for (var i = 0; i + 1 < n; i++)
            {
                // CharEndPositions fold Tc (and Tw for spaces) into the char's
                // advance; add them back so the INK gap between glyphs is measured.
                var pad = run.CharSpacing + (run.Text[i] == ' ' ? run.WordSpacing : 0.0);
                // A glyph whose measured ink width is ~0 wasn't really measured
                // (missing widths/exotic cmap) — its "gap" is the raw advance and
                // means nothing; never cut there.
                var inkW = ends[i] - cum[i] - pad;
                if (inkW <= 0.01) continue;
                var gap = (cum[i + 1] - ends[i] + pad) * dev;
                var spaceAdj = run.Text[i] == ' ' || run.Text[i + 1] == ' ';
                // A run is fragmented only when the
                // column spread is carried by SPACING OPERATORS (Tc/Tw push pairs
                // apart and kerns pull token interiors back together — a table
                // row). Kern-only spreads (monospace manifests, glued headers)
                // stay whole even at 100pt gaps. An explicit space glyph already
                // separates words, so no cut lands next to one.
                var spacingCarried = (Math.Abs(run.CharSpacing) + Math.Abs(run.WordSpacing)) * dev >= 1.0;
                if (spaceAdj) continue;
                if (spacingCarried && gap >= threshold)
                {
                    (cuts ??= new List<int>()).Add(i + 1);
                    wideGaps++;
                    if (gap < minWide) minWide = gap;
                    if (gap > maxWide) maxWide = gap;
                }
            }
            if (cuts is null) continue;
            // Letter-tracked display text gaps EVERY glyph pair by a similar
            // amount — that is styling, not columns. Columns show up as a MIX of
            // tight pairs and wide hops. Only split mixed runs.
            if (wideGaps == n - 1 && maxWide < minWide * 1.5) continue;

            cuts.Add(n);
            var pieces = new List<RawTextRun>(cuts.Count);
            var start = 0;
            foreach (var cut in cuts)
            {
                var len = cut - start;
                if (len <= 0) { start = cut; continue; }
                var baseAdv = cum[start];
                var subCum = new double[len];
                var subEnds = new double[len];
                for (var k = 0; k < len; k++)
                {
                    subCum[k] = cum[start + k] - baseAdv;
                    subEnds[k] = ends[start + k] - baseAdv;
                }
                // The last char's recorded end folds its trailing Tc/Tw pad in;
                // the piece's INK width must not carry it (rects would span the
                // column gap to the next piece).
                var lastPad = run.CharSpacing
                    + (run.Text[cut - 1] == ' ' ? run.WordSpacing : 0.0);
                var pieceWidth = Math.Max(0, subEnds[len - 1] - lastPad);
                // Trim the pad off the recorded end too, so match rectangles
                // measure the ink, not the column hop to the next piece.
                subEnds[len - 1] = pieceWidth;
                pieces.Add(run with
                {
                    Text = run.Text.Substring(start, len),
                    X = run.X + run.TmA * baseAdv,
                    Y = run.Y + run.TmB * baseAdv,
                    Width = pieceWidth,
                    CharCumWidths = subCum,
                    CharEndPositions = subEnds,
                    GapSplit = start > 0,
                });
                start = cut;
            }
            if (pieces.Count > 1)
            {
                runs.RemoveAt(ri);
                runs.InsertRange(ri, pieces);
                ri += pieces.Count - 1;
            }
        }
    }

    /// <summary>Split runs against <see cref="TextSearchOptions.ExcludeRectangles"/>:
    /// characters inside an excluded area are dropped and the remaining glyphs
    /// continue as separate pieces, so each kept stretch surfaces as its own
    /// fragment. A character is excluded when its glyph box overlaps the
    /// rectangle by more than half a point in BOTH axes — a rectangle that
    /// merely touches a line's band (or a character edge) leaves it alone.</summary>
    private static void SplitRunsByExcludeRects(List<RawTextRun> runs, Rectangle[] excludeRects)
    {
        for (var ri = 0; ri < runs.Count; ri++)
        {
            var run = runs[ri];
            var n = run.Text.Length;
            if (n == 0 || run.Text == "\r\n") continue;

            // Vertical glyph band in page space (descent..ascent along the Tm up-axis).
            var fs = run.FontSize;
            double descentOffset = 0, ascentHeight = fs;
            if (run.Metrics is not null && run.Metrics.Descent != 0)
                descentOffset = run.Metrics.Descent * fs / 1000.0;
            if (run.Metrics is not null && run.Metrics.Ascent > 0)
                ascentHeight = run.Metrics.Ascent * fs / 1000.0;
            var (_, by1) = ApplyCtm(run.X + run.TmC * descentOffset, run.Y + run.TmD * descentOffset, run.Ctm);
            var (_, by2) = ApplyCtm(run.X + run.TmC * ascentHeight, run.Y + run.TmD * ascentHeight, run.Ctm);
            var bandLly = Math.Min(by1, by2);
            var bandUry = Math.Max(by1, by2);
            var bandH = bandUry - bandLly;

            const double tol = 0.5;
            var dbg = Environment.GetEnvironmentVariable("ASPOSE_FOSS_EXCLDEBUG") == "1";
            var active = new List<Rectangle>();
            foreach (var er in excludeRects)
            {
                if (er is null || er.IsEmpty) continue;
                var overlapV = Math.Min(bandUry, er.URY) - Math.Max(bandLly, er.LLY);
                if (overlapV > tol) active.Add(er);
            }
            if (dbg) Console.Error.WriteLine($"[excl] run '{(run.Text.Length > 24 ? run.Text.Substring(0, 24) : run.Text)}' X={run.X:0.#} Y={run.Y:0.#} band={bandLly:0.#}..{bandUry:0.#} active={active.Count} cum={(run.CharCumWidths?.Length.ToString() ?? "null")} ends={(run.CharEndPositions?.Length.ToString() ?? "null")} n={n}");
            if (active.Count == 0) continue;

            // Per-character page-space X positions (cumulative advances include Tc/Tw).
            var cum = run.CharCumWidths;
            var ends = run.CharEndPositions;
            if (ends is not null && ends.Length < n) ends = null;
            var charX = new double[n + 1];
            var haveCum = cum is not null && cum.Length > n;
            if (cum is not null && cum.Length > n)
            {
                for (var i = 0; i <= n; i++)
                {
                    var (px, _) = ApplyCtm(run.X + run.TmA * cum[i] * run.HScaling,
                        run.Y + run.TmB * cum[i] * run.HScaling, run.Ctm);
                    charX[i] = px;
                }
            }
            else
            {
                var totalW = run.Width > 0 ? run.Width : EstimateWidth(run.Text, fs);
                for (var i = 0; i <= n; i++)
                {
                    var cw = totalW * i / n;
                    var (px, _) = ApplyCtm(run.X + run.TmA * cw * run.HScaling,
                        run.Y + run.TmB * cw * run.HScaling, run.Ctm);
                    charX[i] = px;
                }
            }

            var excluded = new bool[n];
            var any = false;
            for (var i = 0; i < n; i++)
            {
                // A character belongs to the excluded area when its END lands
                // inside the rectangle — a glyph merely straddling the RIGHT edge
                // stays with the text after the area, while one straddling the
                // left edge (its end inside) is consumed by the area.
                var cend = Math.Max(charX[i], charX[i + 1]);
                foreach (var er in active)
                {
                    if (cend > er.LLX + tol && cend <= er.URX + tol) { excluded[i] = true; any = true; break; }
                }
            }
            if (!any) continue;

            // Without per-char advances the pieces cannot be re-based; drop the run
            // only when everything is excluded, otherwise keep it whole.
            if (!haveCum)
            {
                var all = true;
                for (var i = 0; i < n; i++) if (!excluded[i]) { all = false; break; }
                if (all) { runs.RemoveAt(ri); ri--; }
                continue;
            }

            var pieces = new List<RawTextRun>();
            var start = -1;
            for (var i = 0; i <= n; i++)
            {
                var keep = i < n && !excluded[i];
                if (keep && start < 0) start = i;
                if (!keep && start >= 0)
                {
                    var len = i - start;
                    var baseAdv = cum![start];
                    var subCum = new double[len];
                    var subEnds = new double[len];
                    for (var k = 0; k < len; k++)
                    {
                        subCum[k] = cum[start + k] - baseAdv;
                        // No recorded ink ends: the next char's start is the end.
                        subEnds[k] = ends is not null
                            ? ends[start + k] - baseAdv
                            : cum[start + k + 1] - baseAdv;
                    }
                    var lastPad = run.CharSpacing
                        + (run.Text[i - 1] == ' ' ? run.WordSpacing : 0.0);
                    var pieceWidth = Math.Max(0, subEnds[len - 1] - lastPad);
                    subEnds[len - 1] = pieceWidth;
                    pieces.Add(run with
                    {
                        Text = run.Text.Substring(start, len),
                        X = run.X + run.TmA * baseAdv,
                        Y = run.Y + run.TmB * baseAdv,
                        Width = pieceWidth,
                        CharCumWidths = subCum,
                        CharEndPositions = ends is not null ? subEnds : null,
                        GapSplit = start > 0,
                    });
                    start = -1;
                }
            }
            runs.RemoveAt(ri);
            runs.InsertRange(ri, pieces);
            ri += pieces.Count - 1;
        }
    }

    /// <summary>
    /// Share of a box's own extent that may hang outside the clip region in effect
    /// before the box counts as clipped away.
    /// </summary>
    /// <remarks>
    /// Measured in 0.002 pt baseline / pen sweeps under a fixed
    /// <c>re W n</c> band (Helvetica, Helvetica-Bold, Times-Roman, Courier and Symbol,
    /// 4 pt to 36 pt, clip bands 10 to 100 pt tall at integral and fractional
    /// positions): a run's reported line box may lose
    /// <list type="bullet">
    ///   <item>10 % of its HEIGHT below the clip's bottom edge,</item>
    ///   <item>10 % of its height plus a flat <see cref="ClipTopExtraSlack"/> above the top edge,</item>
    ///   <item>10 % of its WIDTH left of the left edge,</item>
    ///   <item>10 % of ONE AVERAGE CHARACTER right of the right edge.</item>
    /// </list>
    /// Every threshold moves with the clip's own edges and depends on neither the
    /// clip's size, nor the glyphs drawn, nor the font - a 12 pt Helvetica box hangs
    /// exactly 1.32 pt below a band's bottom and 1.52 pt above its top whatever the
    /// band is. A whitespace-only run obeys the identical rule.
    /// </remarks>
    private const double ClipSlackFraction = 0.1;

    /// <summary>
    /// Flat extra slack the TOP edge alone is given, on top of
    /// <see cref="ClipSlackFraction"/> of the box height (measured: 0.2 pt at every
    /// font size from 4 to 20 pt and for every face probed).
    /// </summary>
    private const double ClipTopExtraSlack = 0.2;

    /// <summary>
    /// A box that lands EXACTLY on one of the thresholds is still visible (the
    /// comparison includes the boundary), so the amounts are compared
    /// with a rounding guard: a box whose width comes out an ulp under 6 pt must not
    /// flip a 0.6 pt overhang into "hidden". Far below any distance the page can mean.
    /// </summary>
    private const double ClipSlackEpsilon = 1e-9;

    /// <summary>
    /// True when the clip region <paramref name="clip"/> hides the box
    /// [<paramref name="llx"/>..<paramref name="urx"/>] x
    /// [<paramref name="lly"/>..<paramref name="ury"/>] that a run of
    /// <paramref name="text"/> drew - see <see cref="ClipSlackFraction"/> for the law.
    /// </summary>
    private static bool IsHiddenByClip(RawTextRun run, string text,
        double llx, double lly, double urx, double ury,
        (double Llx, double Lly, double Urx, double Ury) clip)
    {
        var h = ury - lly;
        if (h <= 0) return false;
        if (Overhangs(clip.Lly - lly, ClipSlackFraction * h)) return true;
        if (Overhangs(ury - clip.Ury, ClipSlackFraction * h + ClipTopExtraSlack)) return true;

        var w = urx - llx;
        if (w < 0) return false;
        // A box that pokes past NEITHER side edge is safe whatever the tested width
        // works out to, since trimming only ever SHRINKS it. Settle that first: the
        // trim measures the run's characters, and most runs on a page sit well inside
        // the clip that was set for them.
        if (clip.Llx - llx <= 0 && urx - clip.Urx <= 0) return false;

        var (keep, count) = TestedCharCount(text);
        var testW = w;
        if (keep < text.Length && TextReadsLeftToRight(run))
        {
            var full = MeasureRunPrefix(run, text.Length);
            var kept = MeasureRunPrefix(run, keep);
            if (full > 0 && kept > 0 && kept < full) testW = w * (kept / full);
        }
        if (testW <= 0 || count <= 0) return false;
        return Overhangs(clip.Llx - llx, ClipSlackFraction * testW)
            || Overhangs(llx + testW - clip.Urx, ClipSlackFraction * (testW / count));
    }

    /// <summary>Is <paramref name="amount"/> past <paramref name="slack"/> by more than rounding?</summary>
    private static bool Overhangs(double amount, double slack) => amount > slack + ClipSlackEpsilon;

    /// <summary>
    /// Marks runs whose glyph box a LATER text run's ink covers
    /// (stacked duplicate draws report every copy but the last as Invisible —
    /// the occluder needn't match text, font or colour; coverage above ~55% of the
    /// victim's area hides it). Candidates are found through a coarse position
    /// grid, so only occluders drawn at (near-)the-same spot are considered — the
    /// duplicate-stack shape this rule exists for; a large body of text far from
    /// the victim's centre never scans the whole page.
    /// </summary>
    /// <summary>The glyph box the occlusion pass reasons over: the baseline-anchored
    /// band from the nominal descent to the nominal cap height, in ems. Approximations
    /// of a Latin face's real extents (Arial: descent 0.21 em, cap 0.716 em), chosen
    /// so the band tracks the ink rather than the full 1 em body.</summary>
    private const double OcclusionBoxDescentEm = 0.2;

    private const double OcclusionBoxCapHeightEm = 0.7;

    /// <summary>How much of a run's box later ink must cover before the run reports
    /// Invisible. A simple majority with slack for the box approximation above: a
    /// stacked duplicate covers ~100%, a neighbouring glyph's kern overlap a few
    /// percent — the verdict is insensitive to the exact value between those, and
    /// 0.55 keeps "more than half hidden" strictly true.</summary>
    private const double OcclusionCoverageFraction = 0.55;

    /// <summary>Boxes below this area (pt²) are noise — a zero-advance mark or a
    /// degenerate matrix — and take no occlusion verdict.</summary>
    private const double OcclusionMinBoxArea = 0.01;

    /// <summary>Spatial-hash cell for the occlusion pass, in device points. About one
    /// body-text glyph box: victims look up their centre cell ±1, so an occluder must
    /// register in every cell it spans to be found from anywhere under it.</summary>
    private const double OcclusionGridCellPt = 8.0;

    /// <summary>An occluder spanning more cells than this is skipped: at 8 pt cells
    /// this is ~a full page of coverage (4096 ≈ 76×54 cells on Letter), and a box
    /// that large is a watermark or background, not the stacked-text shape the rule
    /// reads — registering it everywhere would only swamp the grid.</summary>
    private const long OcclusionOccluderCellCap = 4096;
}
