using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
// Page-grid scan helpers: the show-x seat and the out-of-bounds test.
    private static void SeeShowX(GridScanState gs, PageGridState pg)
    {
        // Rotated keeps the projection+cmTx frame (mirrors the runtime);
        // upright tlmX is already the composed device X.
        var x = gs.preRot ? gs.tlmX + gs.cmTx : gs.tlmX;
        // A SIDEWAYS page reads along the negative device axis, so its
        // whole grid frame is negative and is kept separately - the
        // caller takes it once rotation is known to dominate.
        if (double.IsNaN(pg.minXAny) || x < pg.minXAny) pg.minXAny = x;
        // Text drawn at negative X (template/title-block junk on
        // engineering sheets with a shifted MediaBox) can't occupy a
        // grid column and never anchors the grid origin.
        if (x < 0) return;
        if (double.IsNaN(pg.minX) || x < pg.minX) pg.minX = x;
    }

    // True when the current show position falls outside the measuring
    // window (page bounds under LimitToPageBounds) — its glyphs are
    // clipped from the output, so they must not vote in the grid.
    private static bool ShowOutOfBounds(GridScanState gs)
    {
        if (gs.bounds is null) return false;
        var x = gs.preRot ? gs.tlmX + gs.cmTx : gs.tlmX;
        if (x < gs.bounds[0] - 1 || x > gs.bounds[2] + 1) return true;
        return !gs.preRot && (gs.tlmY < gs.bounds[1] - 1 || gs.tlmY > gs.bounds[3] + 1);
    }

    /// <summary></summary>
    private static void ScanOperator(GridScanState gs, PageGridState pg, string op)
    {
        if (op == "Tf")
        {
            if (gs.operands.Count >= 2 && gs.operands[0] is Core.PdfName fn
                && gs.fonts.TryGetValue(fn.Value, out var fdict))
            {
                try { gs.metrics = FontMetrics.FromFontDict(fdict, gs.reader); } catch { gs.metrics = null; }
                gs.fontSize = Math.Abs(GetNumber(gs.operands[1]));
            }
        }
        else if (op == "BT")
        {
            gs.fsScale = Math.Sqrt(gs.cmC * gs.cmC + gs.cmD * gs.cmD);
            if (gs.fsScale < 0.001) gs.fsScale = 1.0;
            var nab = Math.Sqrt(gs.cmA * gs.cmA + gs.cmB * gs.cmB);
            if (nab < 0.001) nab = 1.0;
            gs.preRot = Math.Abs(gs.cmB) > 0.001 && Math.Abs(gs.cmD) < 0.1 * Math.Abs(gs.cmB);
            gs.tmScaleX = nab;
            gs.tlmX = gs.preRot ? RotatedReadX(gs.cmA, gs.cmB, gs.cmE, gs.cmF) : gs.cmE;
            gs.tlmY = gs.cmF;
        }
        else if (op == "Tm" && gs.operands.Count >= 6)
        {
            var mA = GetNumber(gs.operands[0]); var mB = GetNumber(gs.operands[1]);
            var mC = GetNumber(gs.operands[2]); var mD = GetNumber(gs.operands[3]);
            var mE = GetNumber(gs.operands[4]); var mF = GetNumber(gs.operands[5]);
            // Compose with the CTM linear part (mirrors the extraction loop).
            var cEa = mA * gs.cmA + mB * gs.cmC; var cEb = mA * gs.cmB + mB * gs.cmD;
            var cEc = mC * gs.cmA + mD * gs.cmC; var cEd = mC * gs.cmB + mD * gs.cmD;
            var cEe = mE * gs.cmA + mF * gs.cmC + gs.cmE; var cEf = mE * gs.cmB + mF * gs.cmD + gs.cmF;
            gs.fsScale = Math.Sqrt(cEc * cEc + cEd * cEd);
            if (gs.fsScale < 0.001) gs.fsScale = 1.0;
            var nab2 = Math.Sqrt(cEa * cEa + cEb * cEb);
            if (nab2 < 0.001) nab2 = 1.0;
            gs.preRot = Math.Abs(cEb) > 0.001 && Math.Abs(cEd) < 0.1 * Math.Abs(cEb);
            gs.tmScaleX = nab2;
            gs.tlmX = gs.preRot ? RotatedReadX(cEa, cEb, cEe, cEf) : cEe;
            gs.tlmY = cEf;
        }
        else if ((op == "Td" || op == "TD") && gs.operands.Count >= 2)
        {
            gs.tlmX += GetNumber(gs.operands[0]) * gs.tmScaleX;
            gs.tlmY += GetNumber(gs.operands[1]) * gs.fsScale;
            if (op == "TD") gs.preTL = -GetNumber(gs.operands[1]) * gs.fsScale;
        }
        else if (op == "TL" && gs.operands.Count >= 1) { gs.preTL = GetNumber(gs.operands[0]) * gs.fsScale; }
        else if (op == "T*") { gs.tlmY -= gs.preTL; }
        else if (op == "Tz" && gs.operands.Count >= 1)
        {
            var hs = GetNumber(gs.operands[0]) / 100.0;
            if (hs > 0.01 && hs < 100) gs.preHorizScale = hs;
        }
        else if (op == "Tc" && gs.operands.Count >= 1) { gs.preTc = GetNumber(gs.operands[0]); }
        else if (op == "Tw" && gs.operands.Count >= 1) { gs.preTw = GetNumber(gs.operands[0]); }
        else if (op == "q") { gs.cmStack.Push(gs.cmTx); gs.cmFullStack.Push((gs.cmA, gs.cmB, gs.cmC, gs.cmD, gs.cmE, gs.cmF)); }
        else if (op == "Q")
        {
            if (gs.cmStack.Count > 0) gs.cmTx = gs.cmStack.Pop();
            if (gs.cmFullStack.Count > 0) (gs.cmA, gs.cmB, gs.cmC, gs.cmD, gs.cmE, gs.cmF) = gs.cmFullStack.Pop();
        }
        else if (op == "cm" && gs.operands.Count >= 6)
        {
            gs.cmTx += GetNumber(gs.operands[4]);
            var na = GetNumber(gs.operands[0]); var nb = GetNumber(gs.operands[1]);
            var nc = GetNumber(gs.operands[2]); var nd = GetNumber(gs.operands[3]);
            var ne = GetNumber(gs.operands[4]); var nf = GetNumber(gs.operands[5]);
            var a2 = na * gs.cmA + nb * gs.cmC; var b2 = na * gs.cmB + nb * gs.cmD;
            var c2 = nc * gs.cmA + nd * gs.cmC; var d2 = nc * gs.cmB + nd * gs.cmD;
            var e2 = ne * gs.cmA + nf * gs.cmC + gs.cmE; var f2 = ne * gs.cmB + nf * gs.cmD + gs.cmF;
            gs.cmA = a2; gs.cmB = b2; gs.cmC = c2; gs.cmD = d2; gs.cmE = e2; gs.cmF = f2;
        }
        else if (op == "BI") { SkipInlineImage(gs.lexer); }
        else if (op == "Do" && gs.recurse && gs.rdepth < 6
            && gs.operands.Count >= 1 && gs.operands[0] is Core.PdfName doName)
        {
            ScanFormXObject(gs, pg, doName);
        }
        else if (op == "Tj" || op == "'" || op == "\"")
        {
            ScanShowString(gs, pg, op);
        }
        else if (op == "TJ" && gs.operands.Count >= 1 && gs.operands[0] is Core.PdfArray arr
            && !ShowOutOfBounds(gs))
        {
            ScanShowArray(gs, pg, arr);
        }
    }

    /// <summary></summary>
    private static void ScanFormXObject(GridScanState gs, PageGridState pg, Core.PdfName doName)
    {
        // A page can draw all its text inside a Form XObject
        // (a shifted-MediaBox wrapper); measure that text too so
        // the grid is sized instead of falling back to gap
        // spacing. Recurse with the form's own fonts and the CTM
        // in effect at the Do (form /Matrix ignored, as in the
        // extraction loop).
        var xobjs = ResolveXObjects(gs.resDict, gs.reader);
        var xstr = xobjs is not null ? gs.reader.ResolveStream(xobjs.Get(doName.Value)) : null;
        if (xstr is not null && gs.reader.ResolveName(xstr.Dict, "Subtype") == "Form")
        {
            var xbytes = gs.reader.DecodeStream(xstr);
            var formFonts = ResolveFonts(xstr.Dict, gs.reader);
            Scan(pg, gs.reader, gs.bounds, xbytes, formFonts, xstr.Dict, gs.cmA, gs.cmB, gs.cmC, gs.cmD, gs.cmE, gs.cmF, gs.rdepth + 1, gs.recurse);
        }
    }
}
