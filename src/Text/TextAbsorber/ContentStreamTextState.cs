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
        var mo = new TextMatrixOpState();
        mo.xs = xs;
        // Track scale components to interpret Td/TD displacements correctly.
        // Many PDFs use a tiny-scale Tm (e.g. d=0.015) and large Td values;
        // the actual page displacement is d * ty (or a * tx), not ty (tx) alone.
        if (!(mo.xs.operands.Count >= 6)) return;
        mo.newTmY = GetNumber(mo.xs.operands[5]);
        mo.xs.tmD = Math.Abs(GetNumber(mo.xs.operands[3]));
        mo.xs.tmA = Math.Abs(GetNumber(mo.xs.operands[0]));
        mo.tmBraw = GetNumber(mo.xs.operands[1]);
        mo.tmCraw = GetNumber(mo.xs.operands[2]);
        mo.tmDraw = GetNumber(mo.xs.operands[3]);
        mo.tmEraw = GetNumber(mo.xs.operands[4]);
        mo.tmFraw = GetNumber(mo.xs.operands[5]);
        mo.cEa = GetNumber(mo.xs.operands[0]) * mo.xs.cmLa + mo.tmBraw * mo.xs.cmLc;
        mo.cEb = GetNumber(mo.xs.operands[0]) * mo.xs.cmLb + mo.tmBraw * mo.xs.cmLd;
        mo.cEc = mo.tmCraw * mo.xs.cmLa + mo.tmDraw * mo.xs.cmLc;
        mo.cEd = mo.tmCraw * mo.xs.cmLb + mo.tmDraw * mo.xs.cmLd;
        mo.cEe = mo.tmEraw * mo.xs.cmLa + mo.tmFraw * mo.xs.cmLc + mo.xs.cmLe;
        mo.cEf = mo.tmEraw * mo.xs.cmLb + mo.tmFraw * mo.xs.cmLd + mo.xs.cmLf;
        mo.xs.tmAr = mo.cEa; mo.xs.tmBr = mo.cEb; mo.xs.tmCr = mo.cEc; mo.xs.tmDr = mo.cEd;
        mo.xs.tmE = mo.cEe; mo.xs.tmF = mo.cEf;
        mo.xs.tmN = Math.Sqrt(mo.tmCraw * mo.tmCraw + mo.tmDraw * mo.tmDraw);
        if (mo.xs.tmN < 0.001) mo.xs.tmN = 1.0;
        // Rotation test on the composed direction, tolerant of the
        // slight skew a deskewed scan carries (|d| ≪ |b|).
        mo.xs.tmRotated = Math.Abs(mo.cEb) > 0.001 && Math.Abs(mo.cEd) < 0.1 * Math.Abs(mo.cEb);
        if (mo.xs.tmRotated)
        {
            // Line coordinate along the up-axis (c,d): successive
            // visual lines of sideways text differ along it, and the
            // sign keeps "later line" = smaller coordinate so the
            // Y-descending sort yields reading order. tmN is the
            // axis norm - the per-line effective font size scale.
            mo.xs.tmN = Math.Sqrt(mo.cEc * mo.cEc + mo.cEd * mo.cEd);
            if (mo.xs.tmN < 0.001) mo.xs.tmN = 1.0;
            mo.newTmY = RotatedRowY(mo.cEc, mo.cEd, mo.cEe, mo.cEf);
        }

        mo.tmYThreshold = Math.Max(1.0, mo.xs.fontSize * 0.3 * (mo.xs.tmRotated ? mo.xs.tmN : 1.0));
        FilterTextMatrixRow(mo);
        mo.xs.tmY = mo.newTmY;
        SeatTextMatrixOrigin(mo);
        mo.xs.tmOriginX = mo.xs.tlmX;
        mo.xs.tx = mo.xs.tlmX;
        // Line-level bounds + rectangle filter (rotation-aware).
        // Upright pages carry the per-line verdict computed above;
        // sideways pages evaluate fresh, as before.
        mo.xs.skipText = mo.xs.tmRotated ? LineFiltered(mo.xs, mo.newTmY) : mo.tmFiltered;
        // Reset gap-detection only when the Tm actually moved to a new
        // logical row. For same-row Tm (column reposition) keep
        // lastRunEndX so the ' / " / Tj that follows can insert
        // proportional spaces reflecting the visible column gap.
        // A filtered UPRIGHT line keeps the anchors: a later run back
        // on the open line still measures its gap from that line's end.
        if (mo.xs.tmRotated || !mo.xs.skipText)
        {
            if (!mo.tmSameRow) { mo.xs.lastRunEndX = double.NaN; mo.xs.lastRunEndPageX = double.NaN; } mo.xs.lastRunEndDevX = double.NaN;
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
