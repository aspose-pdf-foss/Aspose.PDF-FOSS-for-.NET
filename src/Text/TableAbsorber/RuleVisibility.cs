using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
    // Which painted paths are rules a reader can see. A table's grid is read from
    // the rules drawn on the page; two kinds of path look like rules to the path
    // walk and are not:
    //  * a FILLED path that holds a curve is a drawn shape - a title set as glyph
    //    outlines, a logo, a chart mark. The straight stems of its letters are not
    //    column rules;
    //  * a thin bar filled in the paper's colour over bare paper cannot be seen: a
    //    report generator rules every cell and fills the separators it wants
    //    hidden white. Inside a grid of visible rules such a bar is dropped.

    /// <summary>A colour component at or above this is the paper's white.</summary>
    private const double PaperWhiteLevel = 0.995;

    /// <summary>Follow the fill colour far enough to know whether it is
    /// the paper's white. Only the device spaces can say so; any other space reads
    /// as a visible colour.</summary>
    private static void TrackFillColour(TableLinesState tl, string op)
    {
        switch (op)
        {
            case "cs":
                tl.fillSpaceIsDevice = tl.operands.Count >= 1 && tl.operands[0] is PdfName
                {
                    Value: "DeviceGray" or "DeviceRGB" or "DeviceCMYK"
                };
                // A colour space starts at its initial colour, which is black in the device spaces.
                tl.fillIsPaper = false;
                break;
            case "sc" or "scn":
                tl.fillIsPaper = tl.fillSpaceIsDevice && OperandsArePaperWhite(tl.operands);
                break;
            default:
                tl.fillSpaceIsDevice = true;
                tl.fillIsPaper = OperandsArePaperWhite(tl.operands);
                break;
        }
    }

    /// <summary>Follow the stroke colour the same way, for lines stroked in the paper's white.</summary>
    private static void TrackStrokeColour(TableLinesState tl, string op)
    {
        switch (op)
        {
            case "CS":
                tl.strokeSpaceIsDevice = IsDeviceLikeSpace(tl);
                tl.strokeIsPaper = false;
                break;
            case "SC" or "SCN":
                tl.strokeIsPaper = tl.strokeSpaceIsDevice && OperandsArePaperWhite(tl.operands);
                break;
            default:
                tl.strokeSpaceIsDevice = true;
                tl.strokeIsPaper = OperandsArePaperWhite(tl.operands);
                break;
        }
    }

    /// <summary>Whether the colour space operand names a device space or an ICC-based one: either reads
    /// white from its gray, RGB or CMYK components.</summary>
    private static bool IsDeviceLikeSpace(TableLinesState tl)
    {
        if (tl.operands.Count < 1 || tl.operands[0] is not PdfName name) return false;
        if (name.Value is "DeviceGray" or "DeviceRGB" or "DeviceCMYK") return true;
        return tl.colourSpaces is not null && tl.reader is not null
            && tl.reader.Resolve(tl.colourSpaces.Get(name.Value)) is PdfArray { Count: >= 2 } space
            && space[0] is PdfName { Value: "ICCBased" };
    }

    /// <summary>The colour spaces of a page's resources (inherited from the page tree) or a form's own.</summary>
    private static PdfDictionary? ResolveColourSpaces(PdfDictionary dict, PdfReader reader)
    {
        const int MaxTreeDepth = 6;
        var current = dict;
        for (var depth = 0; current is not null && depth < MaxTreeDepth; depth++)
        {
            if (reader.ResolveDict(current.Get("Resources")) is { } resources)
                return reader.ResolveDict(resources.Get("ColorSpace"));
            current = reader.ResolveDict(current.Get("Parent"));
        }
        return null;
    }

    /// <summary>A line only stroked, in the paper's colour over bare paper: a rule nobody sees.</summary>
    private static bool IsPaperStroke(TableLinesState tl, PendingLine line, bool stroked, bool filled) =>
        stroked && !filled && tl.strokeIsPaper
        && !OnBackground(tl, (line.X1 + line.X2) / 2, (line.Y1 + line.Y2) / 2);

    /// <summary>One gray or three RGB components all at white, or four CMYK components all at no ink.</summary>
    private static bool OperandsArePaperWhite(List<PdfObject> operands)
    {
        const int Gray = 1, Rgb = 3, Cmyk = 4;
        if (operands.Count != Gray && operands.Count != Rgb && operands.Count != Cmyk) return false;
        foreach (var operand in operands)
        {
            if (operand is not (PdfInteger or PdfReal)) return false;
            var v = Num(operand);
            if (operands.Count == Cmyk ? v > 1 - PaperWhiteLevel : v < PaperWhiteLevel) return false;
        }
        return true;
    }

    /// <summary>Whether the buffered path can hold rules, asked at the paint operator
    /// that finalises it. A visibly filled box of cell size is remembered as a
    /// background: a paper-coloured bar drawn over one is a rule one can see.</summary>
    private static bool KeepVisibleRules(TableLinesState tl, bool stroked, bool filled)
    {
        // A filled shape with a curve in it is not made of rules.
        if (!stroked && tl.pathHasCurve) return false;

        if (filled && !tl.fillIsPaper && !tl.pathHasCurve) RememberBackgrounds(tl);
        return true;
    }

    private static void RememberBackgrounds(TableLinesState tl)
    {
        foreach (var rect in tl.pendingRects)
            if (rect.W >= MinCellW && rect.H >= MinCellH) tl.backgrounds.Add(rect);
        if (tl.pendingLines.Count == 0) return;
        // A polygon's extent stands for the area it covers.
        double x1 = double.MaxValue, y1 = double.MaxValue, x2 = double.MinValue, y2 = double.MinValue;
        foreach (var line in tl.pendingLines)
        {
            x1 = Math.Min(x1, Math.Min(line.X1, line.X2)); x2 = Math.Max(x2, Math.Max(line.X1, line.X2));
            y1 = Math.Min(y1, Math.Min(line.Y1, line.Y2)); y2 = Math.Max(y2, Math.Max(line.Y1, line.Y2));
        }
        if (x2 - x1 >= MinCellW && y2 - y1 >= MinCellH) tl.backgrounds.Add(new PendingRect(x1, y1, x2 - x1, y2 - y1));
    }

    /// <summary>A thin bar filled in the paper's colour over bare paper: a rule nobody sees.</summary>
    private static bool IsHiddenBar(TableLinesState tl, PendingRect bar, bool stroked) =>
        !stroked && tl.fillIsPaper && !OnBackground(tl, bar.X + bar.W / 2, bar.Y + bar.H / 2);

    /// <summary>Drop the hidden rules of every lattice that also has rules one can
    /// see: there they are separators the drawing hides. A lattice ruled wholly in
    /// the paper's colour is a layout grid and keeps its rules.</summary>
    private static void DropHiddenSeparators(List<HEdge> hEdges, List<VEdge> vEdges)
    {
        DropPaperStrokesOutside(hEdges, vEdges);
        if (!hEdges.Any(e => e.Hidden) && !vEdges.Any(e => e.Hidden)) return;
        foreach (var (compH, compV) in BuildLatticeComponents(hEdges, vEdges))
        {
            if (!compH.Any(e => !e.Hidden) && !compV.Any(e => !e.Hidden)) continue;
            foreach (var e in compH.Where(e => e.Hidden)) hEdges.Remove(e);
            foreach (var e in compV.Where(e => e.Hidden)) vEdges.Remove(e);
        }
    }

    /// <summary>Lines stroked in the paper's colour often stand between the cells of a grid whose only
    /// visible rules run the other way, and there they are its columns. Beyond the visible rules that
    /// cross them - above the top one, below the bottom one, or past the outer ones - they bound no cell
    /// anyone sees and would add an empty row or column to the table.</summary>
    private static void DropPaperStrokesOutside(List<HEdge> hEdges, List<VEdge> vEdges)
    {
        if (!hEdges.Any(e => e.PaperStroke) && !vEdges.Any(e => e.PaperStroke)) return;
        var visibleH = hEdges.Where(e => !e.Hidden && !e.PaperStroke).ToList();
        var visibleV = vEdges.Where(e => !e.Hidden && !e.PaperStroke).ToList();
        vEdges.RemoveAll(e =>
        {
            if (!e.PaperStroke) return false;
            var across = visibleH.Where(h => e.X >= h.X1 - EdgeTol && e.X <= h.X2 + EdgeTol).ToList();
            return across.Count > 0
                && (e.Y1 >= across.Max(h => h.Y) - LineRectThreshold || e.Y2 <= across.Min(h => h.Y) + LineRectThreshold);
        });
        hEdges.RemoveAll(e =>
        {
            if (!e.PaperStroke) return false;
            var across = visibleV.Where(v => e.Y >= v.Y1 - EdgeTol && e.Y <= v.Y2 + EdgeTol).ToList();
            return across.Count > 0
                && (e.X1 >= across.Max(v => v.X) - LineRectThreshold || e.X2 <= across.Min(v => v.X) + LineRectThreshold);
        });
    }

    private static bool OnBackground(TableLinesState tl, double x, double y)
    {
        foreach (var b in tl.backgrounds)
            if (x >= b.X && x <= b.X + b.W && y >= b.Y && y <= b.Y + b.H) return true;
        return false;
    }
}
