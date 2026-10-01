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
    /// <summary>The stages of the pattern fill: resolving the pattern program and painting its tiles.</summary>
    private static bool PaintPatternTiles(PatternFillState pf)
    {
        var patternContext = new RenderContext(pf.ctx.Pixels, pf.ctx.PixelW, pf.ctx.PixelH, pf.ctx.Scale, pf.ctx.MediaBox, pf.ctx.Reader)
        {
            AllXObjects = pf.patXObj,
            FontDicts = pf.patFonts,
            ConvertFontsToUnicodeTtf = pf.ctx.ConvertFontsToUnicodeTtf,
            PdfXOverprintSim = pf.ctx.PdfXOverprintSim,
            PageCtm = pf.ctx.PageCtm,
            Patterns = pf.ctx.Reader.ResolveDict(pf.patResources?.Get("Pattern")) ?? pf.ctx.Patterns,
            Shadings = pf.ctx.Reader.ResolveDict(pf.patResources?.Get("Shading")) ?? pf.ctx.Shadings,
            // Install the path stencil so every SetPixel outside the filled shape is a no-op.
            ClipMask = pf.mask,
        };

        pf.cellBBox = pf.pdict!.Get("BBox") is PdfArray bbArr && bbArr.Count >= 4
            ? new[] { NumFrom(bbArr[0]), NumFrom(bbArr[1]), NumFrom(bbArr[2]), NumFrom(bbArr[3]) }
            : null;
        var tiles = ComputePatternTileRange(pf.edgeTable, pf.ctx, pf.m, pf.xStep, pf.yStep, pf.cellBBox);

        // A fine pattern covering a large area would need more tiles than the per-tile
        // loop is capped at, leaving most of the region unpainted. Rasterise one cell to
        // a device-sized tile and stamp it across the masked region instead.
        if (tiles.RawCount > 8000 &&
            TryStampTiledPattern(pf.ctx, pf.mask, pf.patternContent, patternContext, pf.patExtG, pf.m, pf.xStep, pf.yStep))
            return false;

        for (var j = tiles.JMin; j <= tiles.JMax; j++)
        {
            for (var i = tiles.IMin; i <= tiles.IMax; i++)
            {
                // Shift pattern.Matrix's translation so the content stream's native pattern
                // (0,0) lands at user coord corresponding to pattern (i*XStep, j*YStep).
                var tx = i * pf.xStep;
                var ty = j * pf.yStep;
                var tileMatrix = new[]
                {
                    pf.m[0], pf.m[1], pf.m[2], pf.m[3],
                    pf.m[4] + tx * pf.m[0] + ty * pf.m[2],
                    pf.m[5] + tx * pf.m[1] + ty * pf.m[3],
                };
                // The pattern matrix maps pattern space to the page's DEFAULT user
                // space (PDF 32000 §8.7.3.1) — it is independent of the CTM in force
                // when the fill runs. Composing state.Ctm here double-applied every
                // content transform (the stamp path above already treats the matrix
                // as default-space).
                // The stencil has to be handed in as the tile content's STARTING clip, not
                // just parked on the context: every draw hook re-reads the clip off the
                // graphics state, so a context-only mask is overwritten with null by the
                // first painting operator inside the cell and the tiles then spill past
                // the filled path (chart bars grew until they touched each other).
                RenderContent(pf.patternContent, patternContext, pf.patExtG, tileMatrix, pf.mask);
            }
        }
        return true;
    }

    /// <summary></summary>
    private static bool ResolvePatternProgram(PatternFillState pf, PdfObject patternObj)
    {
        pf.patternStream = patternObj switch
        {
            PdfStream s => s,
            _ => pf.ctx.Reader.ResolveStream(patternObj),
        };
        pf.pdict = pf.patternStream?.Dict ?? pf.ctx.Reader.ResolveDict(patternObj);
        if (pf.pdict is null) return false;
        pf.patternType = (int)pf.pdict.GetInt("PatternType");
        if (pf.patternType is not 1 and not 2) return false;

        pf.mask = new byte[pf.ctx.PixelW * pf.ctx.PixelH];
        ScanlineFiller.BuildMask(pf.edgeTable, pf.mask, pf.ctx.PixelW, pf.ctx.PixelH, pf.evenOdd);
        if (pf.ctx.ClipMask is { } outer)
        {
            for (var i = 0; i < pf.mask.Length; i++)
                if (outer[i] == 0) pf.mask[i] = 0;
        }

        if (pf.patternType == 2)
        {
            FillWithShadingPattern(pf.ctx, pf.pdict, pf.state, pf.mask);
            return false;
        }

        if (pf.patternStream is null) return false;
        try { pf.patternContent = pf.ctx.Reader.DecodeStream(pf.patternStream); }
        catch { return false; }

        pf.patMatrix = pf.pdict.Get("Matrix") as PdfArray;
        pf.m = new double[] { 1, 0, 0, 1, 0, 0 };
        if (pf.patMatrix is { Count: >= 6 })
        {
            for (var i = 0; i < 6; i++) pf.m[i] = NumFrom(pf.patMatrix[i]);
        }
        // The pattern matrix maps into the page's DEFAULT space (PDF 32000 §8.7.3.1), and
        // on a rotated page that space is carried by the page's base transform - so the
        // cell is laid out through both, as the shading branch already composes them.
        // Without it a /Rotate 90 page drew every tile unrotated: a top-to-bottom gradient
        // came out running left to right, one ramp per tile.
        if (pf.ctx.PageCtm is { } pageCtm)
            pf.m = GraphicsState.MultiplyMatrices(pf.m, pageCtm);
        return true;
    }
}
