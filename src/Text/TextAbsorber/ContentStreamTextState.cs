using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private void ApplyTextMatrixOp(ExtractState xs)
    {
        // Track scale components to interpret Td/TD displacements correctly.
        // Many PDFs use a tiny-scale Tm (e.g. d=0.015) and large Td values;
        // the actual page displacement is d * ty (or a * tx), not ty (tx) alone.
        if (xs.operands.Count >= 6)
        {
            var newTmY = GetNumber(xs.operands[5]);
            xs.tmD = Math.Abs(GetNumber(xs.operands[3]));
            xs.tmA = Math.Abs(GetNumber(xs.operands[0]));
            var tmBraw = GetNumber(xs.operands[1]);
            var tmCraw = GetNumber(xs.operands[2]);
            var tmDraw = GetNumber(xs.operands[3]);
            var tmEraw = GetNumber(xs.operands[4]);
            var tmFraw = GetNumber(xs.operands[5]);
            // Effective text direction = Tm × CTM. A page that rotates
            // content via `cm` (deskewed scan, landscape form) keeps an
            // identity Tm; only the composed matrix shows it sideways.
            var cEa = GetNumber(xs.operands[0]) * xs.cmLa + tmBraw * xs.cmLc;
            var cEb = GetNumber(xs.operands[0]) * xs.cmLb + tmBraw * xs.cmLd;
            var cEc = tmCraw * xs.cmLa + tmDraw * xs.cmLc;
            var cEd = tmCraw * xs.cmLb + tmDraw * xs.cmLd;
            var cEe = tmEraw * xs.cmLa + tmFraw * xs.cmLc + xs.cmLe;
            var cEf = tmEraw * xs.cmLb + tmFraw * xs.cmLd + xs.cmLf;
            xs.tmAr = cEa; xs.tmBr = cEb; xs.tmCr = cEc; xs.tmDr = cEd;
            xs.tmE = cEe; xs.tmF = cEf;
            xs.tmN = Math.Sqrt(tmCraw * tmCraw + tmDraw * tmDraw);
            if (xs.tmN < 0.001) xs.tmN = 1.0;
            // Rotation test on the composed direction, tolerant of the
            // slight skew a deskewed scan carries (|d| ≪ |b|).
            xs.tmRotated = Math.Abs(cEb) > 0.001 && Math.Abs(cEd) < 0.1 * Math.Abs(cEb);
            if (xs.tmRotated)
            {
                // Line coordinate along the up-axis (c,d): successive
                // visual lines of sideways text differ along it, and the
                // sign keeps "later line" = smaller coordinate so the
                // Y-descending sort yields reading order. tmN is the
                // axis norm - the per-line effective font size scale.
                xs.tmN = Math.Sqrt(cEc * cEc + cEd * cEd);
                if (xs.tmN < 0.001) xs.tmN = 1.0;
                newTmY = RotatedRowY(cEc, cEd, cEe, cEf);
            }

            // Emit newline when Tm repositions to a different Y line.
            // Compare against lastRenderedY (where the previous Tj/'/"
            // actually PUT ink) rather than just prevTmY — the tracking Y
            // can differ from the rendered Y by a full 'leading' when the
            // previous BT/ET block used the '/(") operator to step down.
            // Only do this for upright text (tmD > 0). Rotated text (tmD ≈ 0,
            // e.g. 90° rotation [0 fs -fs 0 e f]) has meaningless f-value
            // differences that would generate false line breaks.
            var tmYThreshold = Math.Max(1.0, xs.fontSize * 0.3 * (xs.tmRotated ? xs.tmN : 1.0));
            // refY = where the last text landed. lastRenderedY / prevTmY are
            // per-content-stream locals, so they reset to NaN when text
            // continues inside a Form XObject drawn via Do (a common way to
            // place a diagram/overlay). Fall back to the instance-level
            // _currentLineY (which survives the recursion) so the XObject's
            // first positioned run still line-breaks against the outer text
            // instead of gluing onto it (e.g. floor-plan letters merging into
            // the paragraph line above them).
            var refY = !double.IsNaN(xs.lastRenderedY) ? xs.lastRenderedY
                     : !double.IsNaN(xs.prevTmY) ? xs.prevTmY
                     : _currentLineY;
            // After a ' or " operator, the actual rendered Y is tmY - leading,
            // but a subsequent Tm's newTmY is compared with the refY directly.
            // For same-row column layouts the Tm targets Y ≈ previous Tm's Y
            // (before its '), so the above refY==lastRenderedY path would
            // fire a newline incorrectly. Fall back to prevTmY when the
            // difference to lastRenderedY is exactly ~leading.
            if (!double.IsNaN(xs.prevTmY) && !double.IsNaN(xs.lastRenderedY)
                && Math.Abs(Math.Abs(newTmY - xs.lastRenderedY) - xs.leading) < tmYThreshold)
            {
                refY = xs.prevTmY;
            }
            bool tmSameRow = (xs.tmD > 0 || xs.tmRotated) && !double.IsNaN(refY)
                             && Math.Abs(newTmY - refY) <= tmYThreshold;
            // A page that steps rows with q/cm translations keeps an
            // IDENTICAL Tm in every BT block — the row change lives only
            // in the CTM Y-translation. Compare it against the CTM in
            // effect when the last text rendered; a moved translation is
            // a row change even though the Tm Y matches.
            if (tmSameRow && !xs.tmRotated && !double.IsNaN(xs.lastRenderedCmTy)
                && Math.Abs(xs.localCmTy - xs.lastRenderedCmTy) > tmYThreshold)
            {
                tmSameRow = false;
            }
            if (GridDebug)
                Console.Error.WriteLine($"[tm] newY={newTmY:F1} refY={refY:F1} same={tmSameRow} rot={xs.tmRotated} lastRendY={xs.lastRenderedY:F1} prevTmY={xs.prevTmY:F1}");
            // A line the bounds/rectangle filter drops contributes NOTHING —
            // no break, no line/gap tracking. The stream then reads as if the
            // filtered block never existed, so a later run back on the open
            // line's baseline CONTINUES that line (the extractor joins
            // "3 Tl-base-11pcs" + "11-delig basiskookset" this way when
            // the rows between them fall outside the page bounds).
            // UPRIGHT text only — sideways pages keep the original
            // always-track flow (their per-column Tm churn is not line
            // structure). A same-row Tm INHERITS the line's verdict: the
            // filter decides once per line at its first run, so a later
            // bigger-size run on a kept line near the window's top edge
            // stays kept (a kept date) and a dropped line stays dropped.
            var tmFiltered = !xs.tmRotated
                && (tmSameRow ? xs.openLineSkip : LineFiltered(xs, newTmY));
            if ((xs.tmD > 0 || xs.tmRotated) && !double.IsNaN(refY) && !tmSameRow &&
                !tmFiltered && _text.Length > 0 && _text[^1] != '\n')
            {
                RecordLineY();
                AppendStreamBreak();
            }
            // Track absolute page-space Y for line sorting: keep
            // _currentLineY in text space, but snapshot the CTM Y offset
            // in effect now so RecordLineY can emit page-space Y. Only inside
            // a Form XObject (depth > 0) — page-content Y tracking is left
            // byte-identical to avoid disturbing the common extraction path.
            // A FILTERED line leaves the tracking state (the open line's
            // baseline, the gap-detection anchors) untouched.
            if (!tmFiltered)
            {
                _currentLineY = newTmY;
                _currentLineCmTy = xs.tmRotated ? 0 : LineCmAdjust(xs.depth, xs.localCmD, xs.localCmTy, _currentLineY);
                xs.prevTmY = newTmY;
                xs.openLineSkip = false;
            }
            xs.tmY = newTmY;
            if (xs.tmRotated)
            {
                // Advance axis for sideways text is the origin projected
                // on the composed direction vector (a,b) — so the
                // word-gap / column-grid logic sees real line offsets.
                var n2 = Math.Sqrt(cEa * cEa + cEb * cEb);
                if (n2 < 0.001) n2 = 1.0;
                xs.tlmX = RotatedReadX(cEa, cEb, cEe, cEf);
                // Reading-axis scale: |a| is ~0 for sideways text, which
                // would freeze runPageX at the block origin (every run in
                // the BT block reporting the same grid X). The advance
                // axis norm |(a,b)| is the true per-unit X scale.
                xs.tmA = n2;
            }
            else
            {
                xs.tlmX = GetNumber(xs.operands[4]);
            }
            xs.tmOriginX = xs.tlmX;
            xs.tx = xs.tlmX;
            // Line-level bounds + rectangle filter (rotation-aware).
            // Upright pages carry the per-line verdict computed above;
            // sideways pages evaluate fresh, as before.
            xs.skipText = xs.tmRotated ? LineFiltered(xs, newTmY) : tmFiltered;
            // Reset gap-detection only when the Tm actually moved to a new
            // logical row. For same-row Tm (column reposition) keep
            // lastRunEndX so the ' / " / Tj that follows can insert
            // proportional spaces reflecting the visible column gap.
            // A filtered UPRIGHT line keeps the anchors: a later run back
            // on the open line still measures its gap from that line's end.
            if (xs.tmRotated || !xs.skipText)
            {
                if (!tmSameRow) { xs.lastRunEndX = double.NaN; xs.lastRunEndPageX = double.NaN; } xs.lastRunEndDevX = double.NaN;
            }
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private void BeginTextOp(ExtractState xs)
    {
        // PDF spec ISO 32000-1 §9.4.1: BT initializes only the text matrix
        // and text line matrix to identity. All other text state (leading,
        // char/word spacing, horizontal scaling, rendering mode, font size)
        // persists across BT/ET per §9.3.  Earlier we zeroed leading here
        // and wiped lastRunEndX, which caused the downstream
        // Tm-vs-lastRenderedY heuristic to miss same-row column
        // repositioning whenever a fresh BT block preceded the Tm (typical
        // for column-per-BT PDF layouts). Keep lastRunEndX alive — the next
        // Tm will decide whether to clear it based on row change.
        xs.tlmX = 0;
        xs.tmOriginX = 0;
        xs.tx = 0;
        xs.tmY = 0;
        xs.tmD = 1.0;
        xs.tmA = 1.0;
        xs.tmN = 1.0;
        // BT sets Tm to identity, so the effective direction IS the CTM.
        xs.tmAr = xs.cmLa; xs.tmBr = xs.cmLb; xs.tmCr = xs.cmLc; xs.tmDr = xs.cmLd; xs.tmE = xs.cmLe; xs.tmF = xs.cmLf;
        xs.tmRotated = Math.Abs(xs.cmLb) > 0.001 && Math.Abs(xs.cmLd) < 0.1 * Math.Abs(xs.cmLb);
        if (xs.tmRotated)
        {
            xs.tmN = Math.Sqrt(xs.cmLc * xs.cmLc + xs.cmLd * xs.cmLd);
            if (xs.tmN < 0.001) xs.tmN = 1.0;
            var n2bt = Math.Sqrt(xs.cmLa * xs.cmLa + xs.cmLb * xs.cmLb);
            if (n2bt < 0.001) n2bt = 1.0;
            xs.tmA = n2bt;
            xs.tmY = RotatedRowY(xs.cmLc, xs.cmLd, xs.cmLe, xs.cmLf);
            xs.tlmX = RotatedReadX(xs.cmLa, xs.cmLb, xs.cmLe, xs.cmLf);
            xs.tmOriginX = xs.tlmX;
            xs.tx = xs.tlmX;
        }
        xs.lastRunEstWidth = 0;
        xs.horizScale = 1.0; // Tz resets to 100% at start of text object
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private void MoveTextLineOp(ExtractState xs, string op)
    {
    if (xs.operands.Count >= 2)
    {
        var rawTy = GetNumber(xs.operands[1]);
        if (op == "TD") xs.leading = -rawTy; // TD sets TL = -ty
        var rawTx = GetNumber(xs.operands[0]);
        // PDF spec: Td updates the text LINE matrix, then sets Tm = Tlm.
        // After Td, the text cursor resets to the new line origin.
        // Keep rawTx unscaled: both Td advances and MeasureString widths
        // use the same coordinate system (text space via fontSize from Tf).
        xs.tlmX += rawTx;
        xs.tmE += rawTx * xs.tmAr + rawTy * xs.tmCr;
        xs.tmF += rawTx * xs.tmBr + rawTy * xs.tmDr;

        xs.tx = xs.tlmX;
        // Compute actual page-space y-displacement: ty * tmD
        // (tmD is the y-scale component from the most recent Tm)
        var pageDisp = Math.Abs(rawTy * (xs.tmRotated ? xs.tmN : xs.tmD > 0 ? xs.tmD : xs.tmN));
        // Sideways: re-derive the row coordinate from the moved origin
        // rather than stepping it by the axis norm - the two agree only
        // on an exactly axis-aligned rotation (see RotatedRowY).
        if (xs.tmRotated) xs.tmY = RotatedRowY(xs.tmCr, xs.tmDr, xs.tmE, xs.tmF);
        else xs.tmY += rawTy * (xs.tmD > 0 ? xs.tmD : xs.tmN);
        // Raw mode: sub/superscript hops stay inline. DOWNWARD moves
        // break past ~0.42 em (a subscript dip is ~0.16 em; a fraction
        // denominator / summation lower bound ~0.6 em+); UPWARD moves
        // break only past ~1.5 em (superscripts, returns from a
        // subscript, and raised summation bounds all continue the line).
        var fsScaleTd = xs.tmRotated ? xs.tmN : xs.tmD > 0 ? xs.tmD : xs.tmN;
        var sDispTd = rawTy * fsScaleTd;
        var tdBreakTol = !xs.rawInlineScripts ? 0.5
            : sDispTd < 0 ? Math.Max(0.5, 0.42 * xs.fontSize * Math.Abs(fsScaleTd))
            : Math.Max(0.5, 1.5 * xs.fontSize * Math.Abs(fsScaleTd));
        // Line-level bounds + rectangle filter (rotation-aware).
        // Evaluated FIRST on upright pages: a filtered line contributes
        // no break and no tracking-state change (see the Tm note); a
        // sub-tolerance Td stays on its line and INHERITS the verdict.
        // Sideways pages keep the original always-track flow.
        if (xs.tmRotated || pageDisp > tdBreakTol)
            xs.skipText = LineFiltered(xs, xs.tmY);
        else
            xs.skipText = xs.openLineSkip;
        if (!xs.tmRotated && pageDisp > tdBreakTol && !xs.skipText) xs.openLineSkip = false;
        if (pageDisp > tdBreakTol && (xs.tmRotated || !xs.skipText))
        {
            RecordLineY();
            AppendStreamBreak();
            // Mirror the absolute text-space baseline (tmY, already advanced
            // above). Assigning rather than incrementing keeps _currentLineY
            // correct across a BT that reset tmY to 0 — e.g. cell-per-BT pages
            // ("BT x y Td (cell) Tj ET" repeated), where incrementing by the
            // absolute Td turned line Ys into a runaway cumulative sum.
            _currentLineY = xs.tmY;
            _currentLineCmTy = xs.tmRotated ? 0 : LineCmAdjust(xs.depth, xs.localCmD, xs.localCmTy, _currentLineY);
            xs.lastRunEndX = double.NaN; xs.lastRunEndDevX = double.NaN; xs.lastRunEndPageX = double.NaN;
        }
    }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private void NextTextLineOp(ExtractState xs)
    {
    // Equivalent to 0 -TL Td: move the text line matrix down by the
    // current leading and reset the cursor to the line origin. Mirror
    // the Td handler so tmY, the line-break detection and the
    // page-bounds / search-rectangle filters all advance with the new
    // baseline. (The earlier version left tmY stale, so a Tj after a
    // run of T* operators was positioned and filtered against the Y of
    // a line several rows above, dropping in-rectangle text.)
    xs.tx = xs.tlmX;
    var disp = xs.leading * (xs.tmRotated ? xs.tmN : xs.tmD > 0 ? xs.tmD : xs.tmN);
    var pageDisp = Math.Abs(disp);
    xs.tmE += -xs.leading * xs.tmCr;
    xs.tmF += -xs.leading * xs.tmDr;
    if (xs.tmRotated) xs.tmY = RotatedRowY(xs.tmCr, xs.tmDr, xs.tmE, xs.tmF);
    else xs.tmY -= disp;
    // See the Td note: Raw mode keeps sub/superscript-scale hops
    // inline (T* moves DOWN by the leading, so the downward tol applies).
    var tstarBreakTol = xs.rawInlineScripts
        ? Math.Max(0.5, 0.42 * xs.fontSize * Math.Abs(xs.tmRotated ? xs.tmN : xs.tmD > 0 ? xs.tmD : xs.tmN))
        : 0.5;
    // Re-evaluate the line-level filters at the new baseline FIRST —
    // on upright pages a filtered line contributes no break and no
    // tracking change (see the Tm note); a sub-tolerance T* inherits.
    // Sideways pages keep the original always-track flow.
    if (xs.tmRotated || pageDisp > tstarBreakTol)
        xs.skipText = LineFiltered(xs, xs.tmY);
    else
        xs.skipText = xs.openLineSkip;
    if (!xs.tmRotated && pageDisp > tstarBreakTol && !xs.skipText) xs.openLineSkip = false;
    if (pageDisp > tstarBreakTol && (xs.tmRotated || !xs.skipText))
    {
        RecordLineY();
        AppendStreamBreak();
        // See the Td note: mirror the absolute baseline (survives a BT
        // that zeroed tmY).
        _currentLineY = xs.tmY;
        _currentLineCmTy = xs.tmRotated ? 0 : LineCmAdjust(xs.depth, xs.localCmD, xs.localCmTy, _currentLineY);
        xs.lastRunEndX = double.NaN; xs.lastRunEndDevX = double.NaN; xs.lastRunEndPageX = double.NaN;
    }
    }
}
