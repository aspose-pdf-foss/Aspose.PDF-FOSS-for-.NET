using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;
using GdiState = System.Drawing.Drawing2D.GraphicsState;

namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer
{
    /// <summary>The stages of the group-layer composite: one pixel row.</summary>
    private bool CompositeGroupRow(GroupLayerCompositeState cg, int y)
    {
        cg.dRow = cg.dst.Scan0.ToInt64() + (long)y * cg.dst.Stride + (long)cg.x0 * 4;
        cg.sRow = cg.src.Scan0.ToInt64() + (long)y * cg.src.Stride + (long)cg.x0 * 4;
        System.Runtime.InteropServices.Marshal.Copy((IntPtr)cg.dRow, cg.drow, 0, cg.segBytes);
        System.Runtime.InteropServices.Marshal.Copy((IntPtr)cg.sRow, cg.srow, 0, cg.segBytes);
        if (cg.ko is not null)
            System.Runtime.InteropServices.Marshal.Copy((IntPtr)(cg.ko.Scan0.ToInt64() + (long)y * cg.ko.Stride + (long)cg.x0 * 4), cg.krow!, 0, cg.segBytes);
        cg.dirty = false;
        for (int x = cg.x0; x < cg.x1; x++)
        {
            if (!CompositeGroupPixel(cg, y, x)) break;
        }
        if (cg.dirty)
            System.Runtime.InteropServices.Marshal.Copy(cg.drow, 0, (IntPtr)cg.dRow, cg.segBytes);
        return true;
    }

    /// <summary></summary>
    private bool CompositeGroupPixel(GroupLayerCompositeState cg, int y, int x)
    {
        cg.i = (x - cg.x0) * 4;
        if (cg.krow is not null)
        {
            {
            CompositeKnockoutPixel(cg, y, x);
            return true;
        }
        }
        if (cg.stampMask is not null && cg.stampMask[y * cg.w + x] != 0)
        {
            {
            CompositeStampMaskedPixel(cg, y, x);
            return true;
        }
        }
        cg.sca = cg.covWeight is not null ? cg.covWeight[y * cg.w + x] : cg.srow[cg.i + 3] / 255.0;
        if (cg.sca <= 0.0) return true;
        cg.a = cg.sca * cg.ga; // effective source alpha
        if (cg.softMask is not null) cg.a *= cg.softMask[y * cg.w + x] / 255.0;
        if (cg.a <= 0.0) return true;
        cg.sb = cg.srow[cg.i];
        cg.sg = cg.srow[cg.i + 1];
        cg.sr = cg.srow[cg.i + 2];
        cg.db = cg.drow[cg.i];
        cg.dg = cg.drow[cg.i + 1];
        cg.dr = cg.drow[cg.i + 2];
        cg.dn = cg.drow[cg.i + 3] / 255.0; // backdrop alpha (0 for a transparent group layer, 1 for the page)

        cg.bbr = cg.sr;
        cg.bbg = cg.sg;
        cg.bbb = cg.sb;
        if (cg.mode != Rasterizer.BlendMode.Normal && cg.dn > 0.0)
        {
            var (ibr, ibg, ibb) = Rasterizer.BlendModes.Blend(cg.mode, cg.dr, cg.dg, cg.db, cg.sr, cg.sg, cg.sb);
            cg.bbr = (1 - cg.dn) * cg.sr + cg.dn * ibr;
            cg.bbg = (1 - cg.dn) * cg.sg + cg.dn * ibg;
            cg.bbb = (1 - cg.dn) * cg.sb + cg.dn * ibb;
        }
        cg.outA = cg.a + cg.dn * (1 - cg.a);
        if (cg.outA <= 0.0) return true;
        cg.inv = cg.dn * (1 - cg.a);
        cg.drow[cg.i]     = (byte)((cg.bbb * cg.a + cg.db * cg.inv) / cg.outA + 0.5);
        cg.drow[cg.i + 1] = (byte)((cg.bbg * cg.a + cg.dg * cg.inv) / cg.outA + 0.5);
        cg.drow[cg.i + 2] = (byte)((cg.bbr * cg.a + cg.dr * cg.inv) / cg.outA + 0.5);
        cg.drow[cg.i + 3] = (byte)(cg.outA * 255 + 0.5);
        cg.dirty = true;
        return true;
    }

    /// <summary></summary>
    private void CompositeStampMaskedPixel(GroupLayerCompositeState cg, int y, int x)
    {
        // Replace-semantics for a stamped nested-footprint pixel.
        double aEff = cg.ga;
        if (cg.softMask is not null) aEff *= cg.softMask[y * cg.w + x] / 255.0;
        if (aEff <= 0.0) return;
        double al = cg.srow[cg.i + 3] / 255.0;                 // layer's own alpha
        double dnb = cg.drow[cg.i + 3] / 255.0;                // backdrop alpha
        int lb = cg.srow[cg.i], lg = cg.srow[cg.i + 1], lr2 = cg.srow[cg.i + 2];
        double cbr = lr2, cbg = lg, cbb = lb;
        if (cg.mode != Rasterizer.BlendMode.Normal && dnb > 0.0)
        {
            var (xr, xg, xb) = Rasterizer.BlendModes.Blend(cg.mode, cg.drow[cg.i + 2], cg.drow[cg.i + 1], cg.drow[cg.i], lr2, lg, lb);
            cbr = (1 - dnb) * lr2 + dnb * xr;
            cbg = (1 - dnb) * lg + dnb * xg;
            cbb = (1 - dnb) * lb + dnb * xb;
        }
        double aNew = (1 - aEff) * dnb + aEff * al;
        if (aNew <= 0.0)
        {
            cg.drow[cg.i] = cg.drow[cg.i + 1] = cg.drow[cg.i + 2] = cg.drow[cg.i + 3] = 0;
            cg.dirty = true;
            return;
        }
        cg.drow[cg.i]     = (byte)Compat.Clamp((cg.drow[cg.i] * dnb * (1 - aEff) + cbb * al * aEff) / aNew + 0.5, 0, 255);
        cg.drow[cg.i + 1] = (byte)Compat.Clamp((cg.drow[cg.i + 1] * dnb * (1 - aEff) + cbg * al * aEff) / aNew + 0.5, 0, 255);
        cg.drow[cg.i + 2] = (byte)Compat.Clamp((cg.drow[cg.i + 2] * dnb * (1 - aEff) + cbr * al * aEff) / aNew + 0.5, 0, 255);
        cg.drow[cg.i + 3] = (byte)(aNew * 255 + 0.5);
        cg.dirty = true;
    }

    /// <summary></summary>
    private void CompositeKnockoutPixel(GroupLayerCompositeState cg, int y, int x)
    {
        // Knockout element: the blend mode acts against the group's frozen
        // INITIAL backdrop, never against earlier siblings
        // (PDF 32000 §11.4.5). Q_KO selects what happens to the sibling
        // pixels underneath: "replace" = spec knockout (element over b0
        // replaces the accumulated pixel wherever it has coverage);
        // default = blend-vs-b0 but alpha-composite over the accumulated
        // result, which keeps sibling AA at fractional-coverage edges.
        double ka = (cg.covWeight is not null ? cg.covWeight[y * cg.w + x] : cg.srow[cg.i + 3] / 255.0) * cg.ga;
        if (cg.softMask is not null) ka *= cg.softMask[y * cg.w + x] / 255.0;
        if (ka <= 0.0) return;
        int ksb = cg.srow[cg.i], ksg = cg.srow[cg.i + 1], ksr = cg.srow[cg.i + 2];
        int kdb = cg.krow![cg.i], kdg = cg.krow[cg.i + 1], kdr = cg.krow[cg.i + 2];
        double kdn = cg.krow[cg.i + 3] / 255.0;
        double kbr = ksr, kbg = ksg, kbb = ksb;
        if (cg.mode != Rasterizer.BlendMode.Normal && kdn > 0.0)
        {
            var (zr, zg, zb) = Rasterizer.BlendModes.Blend(cg.mode, kdr, kdg, kdb, ksr, ksg, ksb);
            kbr = (1 - kdn) * ksr + kdn * zr;
            kbg = (1 - kdn) * ksg + kdn * zg;
            kbb = (1 - kdn) * ksb + kdn * zb;
        }
        double kbdn, kbb2, kbg2, kbr2;
        if (cg.koReplace)
        { kbdn = kdn; kbb2 = kdb; kbg2 = kdg; kbr2 = kdr; }
        else
        { kbdn = cg.drow[cg.i + 3] / 255.0; kbb2 = cg.drow[cg.i]; kbg2 = cg.drow[cg.i + 1]; kbr2 = cg.drow[cg.i + 2]; }
        double koutA = ka + kbdn * (1 - ka);
        if (koutA <= 0.0) return;
        double kinv = kbdn * (1 - ka);
        cg.drow[cg.i]     = (byte)((kbb * ka + kbb2 * kinv) / koutA + 0.5);
        cg.drow[cg.i + 1] = (byte)((kbg * ka + kbg2 * kinv) / koutA + 0.5);
        cg.drow[cg.i + 2] = (byte)((kbr * ka + kbr2 * kinv) / koutA + 0.5);
        cg.drow[cg.i + 3] = (byte)(koutA * 255 + 0.5);
        cg.dirty = true;
    }
}
