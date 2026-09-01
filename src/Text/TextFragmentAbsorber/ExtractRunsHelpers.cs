using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
// The run extraction's helpers: the font-usage guard, the path bounding box and the pending clip.
    // Strict font-usage guard (mirrors TextAbsorber.EnsureFontSet): a
    // text-showing operator before any Tf in the page content stream is a
    // malformed document — surface IncorrectFontUsageException instead of
    // best-effort output. Form XObjects (depth > 0) inherit the caller's
    // graphics state, so the guard applies to the page level only.
    private static void EnsureFontSet(ExtractRunsState xr, string op)
    {
        if (xr.strictFonts && xr.depth == 0 && xr.currentFontNameForGuard is null)
            throw new IncorrectFontUsageException(
                $"Document error: {op} operator without preceding Tf - no font set for the text segment");
    }

    private static void AddPathPoint(ExtractRunsState xr, double px, double py, Matrix m)
    {
        var (dx, dy) = ApplyCtm(px, py, m);
        if (dx < xr.pathMinX) xr.pathMinX = dx;
        if (dy < xr.pathMinY) xr.pathMinY = dy;
        if (dx > xr.pathMaxX) xr.pathMaxX = dx;
        if (dy > xr.pathMaxY) xr.pathMaxY = dy;
    }

    private static void ResetPathBbox(ExtractRunsState xr)
    {
        xr.pathMinX = double.PositiveInfinity; xr.pathMinY = double.PositiveInfinity;
        xr.pathMaxX = double.NegativeInfinity; xr.pathMaxY = double.NegativeInfinity;
        xr.pathSubpaths = 0;
    }

    private static void ApplyPendingClip(ExtractRunsState xr)
    {
        if (!xr.pendingClip) return;
        xr.pendingClip = false;
        if (double.IsInfinity(xr.pathMinX)) return; // empty path — nothing to intersect
        var c = (xr.pathMinX, xr.pathMinY, xr.pathMaxX, xr.pathMaxY);
        if (xr.currentClip is { } prev)
            c = (Math.Max(prev.Llx, c.pathMinX), Math.Max(prev.Lly, c.pathMinY),
                 Math.Min(prev.Urx, c.pathMaxX), Math.Min(prev.Ury, c.pathMaxY));
        xr.currentClip = c;
    }
}
