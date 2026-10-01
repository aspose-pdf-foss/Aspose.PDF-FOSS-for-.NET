using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>The stages of the CID text draw: one glyph (its width, vertical origin, outline or fallback draw, and advance).</summary>
    private static bool DrawCidGlyph(CidTextDrawState ct, int i)
    {
        var code = ct.step == 2 ? (ct.rawBytes[i] << 8) | ct.rawBytes[i + 1] : ct.rawBytes[i];
        // Custom CMaps (non-Identity-H) map byte-codes to CIDs via cidchar/
        // cidrange blocks. Predefined Identity-H/V CMaps are pass-through.
        var cid = ct.cidInfo.CodeToCid(code);

        // The advance width is needed BEFORE the draw in vertical mode: the default
        // position vector v is (w0/2, /DW2[0]).
        var swWidthKey = cid;
        if (ct.cidInfo.IsUnicodeEncoding && ct.cidInfo.Ordering is not null && ct.cidInfo.Ordering != "Identity"
            && AdobeCidTables.UnicodeToCid(ct.cidInfo.Ordering, cid) is int swRealCid)
            swWidthKey = swRealCid;
        var charWidth = ct.fontMetrics?.GetWidth(swWidthKey) ?? 1000;

        // In vertical mode the pen sits on the VERTICAL origin, so the glyph's own
        // (horizontal) origin is the pen minus the position vector v. Text space has y
        // up and the raster has y down, so -vy in text space is +vy in device pixels.
        var drawX = ct.px;
        var drawY = ct.py;
        var vAdvancePx = 0.0;
        if (ct.vertical)
        {
            var (w1y, vx, vy) = ct.cidInfo.VerticalMetrics(swWidthKey, charWidth);
            var em = ct.effectiveSize * ct.ctx.Scale / 1000.0;
            drawX = ct.px - vx * em * ct.hScale;
            drawY = ct.penY + vy * em;
            vAdvancePx = Math.Abs(w1y) * em;
        }

        if (ct.parser is not null)
        {
            // CID-keyed CFF subsets renumber glyphs (GID 1..N following the Charset
            // order) and the descendant CIDFontType0 dict has no /CIDToGIDMap.
            // Resolve through the CFF's own charset in that case.
            var gid = ct.parser is CffGlyphSource cff && cff.IsCidKeyed
                ? cff.CidToGid(cid)
                : ct.cidInfo.ResolveGid(cid);
            // Out-of-charset CID with a constant high byte over a small identity
            // charset: paint the low-byte glyph instead (see the GDI+
            // renderer's DrawCidText for the full note).
            if (gid == 0 && cid > 0xFF && ct.parser is CffGlyphSource cffLow && cffLow.IsCidKeyed)
                gid = cffLow.CidToGid(cid & 0xFF);
            if (gid > 0)
            {
                var outline = ct.parser.GetOutline(gid);
                if (outline is not null)
                {
                    BlitGlyph(ct.ctx, outline, ct.parser.UnitsPerEm, ct.effectiveSize, ct.hScale,
                        drawX, drawY, ct.r, ct.g, ct.b, ct.a);
                }
            }
        }
        else if (ct.fallback is not null)
        {
            // Two paths into the fallback font's cmap:
            // - Uni*-UCS2-* / Uni*-UTF16-* encodings: the 2-byte input is
            //   already a Unicode codepoint. Look up directly.
            // - Identity-H/V or bytecode-CMaps: input is an Adobe CID.
            //   Adobe-table → Unicode → fallback cmap.
            int fallbackGid;
            if (ct.cidInfo.IsUnicodeEncoding)
            {
                ct.fallback.CMap.TryGetValue(cid, out fallbackGid);
            }
            else
            {
                fallbackGid = CjkFallbackFont.ResolveFallbackGid(ct.cidInfo.Ordering, cid, ct.fallback);
            }
            if (fallbackGid > 0)
            {
                var outline = ct.fallback.GetOutline(fallbackGid);
                if (outline is not null)
                {
                    BlitGlyph(ct.ctx, outline, ct.fallback.UnitsPerEm, ct.effectiveSize, ct.hScale,
                        drawX, drawY, ct.r, ct.g, ct.b, ct.a);
                }
            }
        }

        // Advance. Horizontal: /W width + Tc, with Tw only for the SINGLE-BYTE
        // code 32 (PDF 32000 §9.3.3) - a 2-byte <0020> in a UTF16/UCS2 CMap never takes
        // it. Vertical: step DOWN the column by the /W2 (or /DW2) displacement.
        if (ct.vertical)
        {
            ct.penY += vAdvancePx + ct.charSpacingPx;
        }
        else
        {
            ct.px += charWidth / 1000.0 * ct.effectiveSize * ct.ctx.Scale * ct.hScale + ct.charSpacingPx;
            if (ct.step == 1 && cid == 32) ct.px += ct.wordSpacingPx;
        }
        return true;
    }
}
