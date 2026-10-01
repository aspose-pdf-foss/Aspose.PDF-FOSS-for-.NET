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
    /// <summary>The stages of the CID text draw: one glyph code at a time.</summary>
    private void DrawCidGlyph(GdiCidTextDrawState ct, int i, SolidBrush brush)
    {
        int code = ct.step == 2 ? (ct.rawBytes[i] << 8) | ct.rawBytes[i + 1] : ct.rawBytes[i];
        int c = ct.cid.CodeToCid(code);
        // The /W table is keyed by Adobe CIDs. A Unicode CMap (Uni*-UTF16/UCS2)
        // hands back the codepoint, so map it to the collection's real CID for
        // the width lookup — the authored half-width Latin runs (/W 1..96 = 500
        // in a Korea1 invoice) are otherwise missed and set at /DW 1000.
        int widthKey = c;
        if (ct.cid.IsUnicodeEncoding && ct.cid.Ordering is not null && ct.cid.Ordering != "Identity"
            && Text.AdobeCidTables.UnicodeToCid(ct.cid.Ordering, c) is int realCid)
            widthKey = realCid;
        int charWidth = ct.metrics?.GetWidth(widthKey) ?? 1000;

        // Vertical writing (-V CMap, PDF 32000 §9.7.4.3): the pen runs DOWN the
        // column. Each glyph's origin is displaced by the position vector
        // v = (vx, vy) — default (w0/2, /DW2 vy) — so the glyph centres on the
        // column axis with its body below the pen; the pen then advances by the
        // vertical displacement w1 (default /DW2, per-CID /W2 override).
        var glyphTm = ct.tm;
        double w1y = 0;
        if (ct.vertical)
        {
            var (w1, vx, vy) = ct.cid.VerticalMetrics(c, charWidth);
            w1y = w1;
            glyphTm = GraphicsState.MultiplyMatrices(
                new double[] { 1, 0, 0, 1, -vx / 1000.0 * ct.tfs, -vy / 1000.0 * ct.tfs }, ct.tm);
        }

        if (ct.parser is not null)
        {
            int gid = ct.parser is CffGlyphSource cff && cff.IsCidKeyed ? cff.CidToGid(c) : ct.cid.ResolveGid(c);
            // Some producers show CIDs the embedded CID-keyed CFF never defines
            // (a constant high byte over a small identity charset). Paint the
            // low-byte glyph instead; only reached when the charset
            // lookup missed, so valid CIDs are untouched.
            if (gid == 0 && c > 0xFF && ct.parser is CffGlyphSource cffLow && cffLow.IsCidKeyed)
                gid = cffLow.CidToGid(c & 0xFF);
            if (gid > 0)
                PaintGlyph(ct.parser, gid, glyphTm, ct.ctm, ct.tfs, ct.th, ct.state.Rise, ct.hScale, ct.upm, brush);
        }
        else if (ct.fallback is not null)
        {
            int fbGid;
            if (ct.cid.IsUnicodeEncoding)
                ct.fallback.CMap.TryGetValue(c, out fbGid);
            else
                fbGid = Text.CjkFallbackFont.ResolveFallbackGid(ct.cid.Ordering, c, ct.fallback);
            if (fbGid > 0)
                PaintGlyph(ct.fallback, fbGid, glyphTm, ct.ctm, ct.tfs, ct.th, ct.state.Rise, ct.hScale, ct.fbUpm, brush);
        }

        if (ct.vertical)
        {
            // Advance down: w1 is negative (downward) in glyph space; Tc adds to
            // the travel. Tz applies to horizontal displacements only (§9.3.4).
            double ty = w1y / 1000.0 * ct.tfs - ct.state.CharSpacing;
            ct.tm = GraphicsState.MultiplyMatrices(new double[] { 1, 0, 0, 1, 0, ty }, ct.tm);
        }
        else
        {
            // Tw applies only to the SINGLE-BYTE code 32 (PDF 32000 §9.3.3) —
            // a 2-byte <0020> space in a UTF16/UCS2 CMap never takes it.
            double tx = (charWidth / 1000.0 * ct.tfs + ct.state.CharSpacing + (ct.step == 1 && code == 32 ? ct.state.WordSpacing : 0)) * ct.th;
            ct.tm = GraphicsState.MultiplyMatrices(new double[] { 1, 0, 0, 1, tx, 0 }, ct.tm);
        }
    }
}
