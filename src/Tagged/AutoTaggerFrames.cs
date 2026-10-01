using System;
using System.Collections.Generic;
using System.Linq;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    /// <summary>A box the page's rules draw round content that is no table - a picture, a few lines of text: the
    /// blocks inside it are grouped under a Div stating the box (/BBox) and the rules drawn round it
    /// (/BorderStyle, /BorderThickness before, after, start, end).</summary>
    private sealed class Frame
    {
        public Aspose.Pdf.Rectangle Box = new(0, 0, 0, 0);
        public double[] Borders = new double[4];
        /// <summary>The colour fills shade the box with; null when none does.</summary>
        public double[]? Shade;
    }

    // Rules this near (points) the edge of a box draw it.
    private const double FrameRuleReach = 3.0;

    /// <summary>The frames the page's rules draw: each band of a ruled grid of one column that is no table, holding
    /// text or a picture, is a box of its own. An edge two boxes share is drawn once, as the lower box's top.</summary>
    private static List<Frame> FindFrames(PageWork pw, List<(Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY)> ruled,
        List<Line> content)
    {
        var frames = new List<Frame>();
        foreach (var t in ruled.Where(t => t.cols == 1 && t.rowY.Count == t.rows + 1
                                           && !pw.Tables.Any(x => Overlap(x.region, t.region) > 0)))
        {
            // rowY runs up the page: band k lies between rowY[k] and rowY[k + 1].
            var framed = new bool[t.rows];
            for (var k = 0; k < t.rows; k++)
            {
                var box = new Aspose.Pdf.Rectangle(t.region.LLX, t.rowY[k], t.region.URX, t.rowY[k + 1]);
                framed[k] = content.Any(l => l.Frags.Count > 0 && InsideBox(l, box.LLX, box.LLY, box.URX, box.URY))
                            || pw.Figures.Any(f => InsideFrame(f, box));
                if (!framed[k]) continue;
                frames.Add(new Frame
                {
                    Box = box,
                    Shade = ShadeOf(pw, box, box),
                    Borders =
                    [
                        Drawn(pw.Rules, true, box.URY, box.LLX, box.URX),
                        k > 0 && framed[k - 1] ? 0 : Drawn(pw.Rules, true, box.LLY, box.LLX, box.URX),
                        Drawn(pw.Rules, false, box.LLX, box.LLY, box.URY),
                        Drawn(pw.Rules, false, box.URX, box.LLY, box.URY),
                    ],
                });
            }
        }
        return frames;
    }

    // A cell of a box set beside others holds at least this many lines.
    private const int MinBoxLines = 4;

    /// <summary>Whether a ruled grid is boxes set side by side: one row, two or more columns, each cell holding lines of
    /// running text - a heading, prose, a list - as a page's column does (a table's row holds a few lines in a cell).</summary>
    private static bool BoxesSideBySide((Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<Line> content)
        => t.rows == 1 && t.cols >= 2 && t.colX.Count == t.cols + 1
           && Enumerable.Range(0, t.cols).All(c => content.Count(l => l.Y >= t.region.LLY && l.Y <= t.region.URY
                  && l.Frags.Any(f => f.X >= t.colX[c] && f.R <= t.colX[c + 1] && f.Text.Trim().Length > 0)) >= MinBoxLines);

    /// <summary>The boxes of a grid set side by side: one per cell, its edges ruled as the page draws them.</summary>
    private static IEnumerable<Frame> CellFrames(PageWork pw, (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t)
        => Enumerable.Range(0, t.cols).Select(c =>
        {
            var box = new Aspose.Pdf.Rectangle(t.colX[c], t.region.LLY, t.colX[c + 1], t.region.URY);
            return new Frame
            {
                Box = box,
                Shade = ShadeOf(pw, box, box),
                Borders =
                [
                    Drawn(pw.Rules, true, box.URY, box.LLX, box.URX), Drawn(pw.Rules, true, box.LLY, box.LLX, box.URX),
                    Drawn(pw.Rules, false, box.LLX, box.LLY, box.URY), Drawn(pw.Rules, false, box.URX, box.LLY, box.URY),
                ],
            };
        });

    /// <summary>Whether a line stands in the page's frames: every piece of it inside one (a line across boxes set side by
    /// side, not yet parted at their edge, stands in them too).</summary>
    private static bool InFrames(PageWork pw, Line line)
        => pw.Frames.Count > 0 && line.Frags.Any(f => f.Text.Trim().Length > 0)
           && line.Frags.Where(f => f.Text.Trim().Length > 0).All(f => pw.Frames.Any(b => f.X >= b.Box.LLX - FrameRuleReach
               && f.R <= b.Box.URX + FrameRuleReach && line.Y >= b.Box.LLY && line.Y <= b.Box.URY));

    /// <summary>How thick the rules along a box's edge are drawn: the thickest of those standing on it within its span.</summary>
    private static double Drawn(List<PageContentScan.Rule> rules, bool horizontal, double at, double from, double to)
    {
        var on = rules.Where(r => r.Horizontal == horizontal && Math.Abs(r.At - at) <= FrameRuleReach && r.From < to && r.To > from).ToList();
        return on.Count == 0 ? 0 : Math.Round(on.Max(r => r.Width > 0 ? r.Width : LoneRuleThickness), 2);
    }

    private static bool InsideFrame((double y, double x, double w, double h, int op) f, Aspose.Pdf.Rectangle box)
        => f.x >= box.LLX - FrameRuleReach && f.x + f.w <= box.URX + FrameRuleReach
           && f.y >= box.LLY - FrameRuleReach && f.y + f.h <= box.URY + FrameRuleReach;

    /// <summary>Whether a rule draws an edge of a frame: it is no rule of a block's.</summary>
    private static bool OnFrame(PageWork pw, PageContentScan.Rule rule)
        => pw.Frames.Any(f => rule.Horizontal
            ? (Math.Abs(rule.At - f.Box.LLY) <= FrameRuleReach || Math.Abs(rule.At - f.Box.URY) <= FrameRuleReach) && rule.From < f.Box.URX && rule.To > f.Box.LLX
            : (Math.Abs(rule.At - f.Box.LLX) <= FrameRuleReach || Math.Abs(rule.At - f.Box.URX) <= FrameRuleReach) && rule.From < f.Box.URY && rule.To > f.Box.LLY);

    /// <summary>The frames as regions the column finder keeps whole, as it keeps a table's.</summary>
    private static IEnumerable<(Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY)> FrameRegions(PageWork pw)
        => pw.Frames.Select(f => (f.Box, 1, 1, new List<double> { f.Box.LLX, f.Box.URX }, new List<double> { f.Box.LLY, f.Box.URY }));

    /// <summary>Each paragraph, heading, list or figure standing inside a frame is that frame's.</summary>
    private static void MarkFramedBlocks(PageWork pw, List<Block> blocks)
    {
        if (pw.Frames.Count == 0) return;
        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            var frame = b.Kind switch
            {
                BlockKind.Paragraph or BlockKind.Heading when b.Lines is { Count: > 0 } lines
                    => pw.Frames.FirstOrDefault(f => lines.All(l => l.Frags.Count == 0 || InsideBox(l, f.Box.LLX, f.Box.LLY, f.Box.URX, f.Box.URY))),
                // A heading stands where its text starts, from its top to its last line's foot (its descent may reach as far
                // over the box's edge as a rule of the box stands from it: a band set as tight as its line).
                BlockKind.Heading => pw.Frames.FirstOrDefault(f => b.TextMinX >= f.Box.LLX && b.TextMinX <= f.Box.URX
                                                                   && b.Y >= f.Box.LLY - FrameRuleReach && b.SortTop <= f.Box.URY + FrameRuleReach),
                BlockKind.List when b.Items is { Count: > 0 } items
                    => pw.Frames.FirstOrDefault(f => items.SelectMany(it => it.Lines).All(l => l.Frags.Count == 0 || InsideBox(l, f.Box.LLX, f.Box.LLY, f.Box.URX, f.Box.URY))),
                BlockKind.Figure when b.Figs is { Count: > 0 } figs
                    => pw.Frames.FirstOrDefault(f => figs.All(op => pw.Figures.Any(x => x.op == op && InsideFrame(x, f.Box)))),
                _ => null,
            };
            if (frame is null) continue;
            b.Frame = frame;
            blocks[i] = b;
        }
    }

    /// <summary>The Div a frame's blocks are grouped under: its box and the rules drawn round it.</summary>
    private static LS.StructureElement FrameDiv(ITaggedContent tc, Frame frame)
    {
        var div = tc.CreateDivElement();
        var layout = div.Attributes.GetAttributes(LS.AttributeOwnerStandard.Layout);
        var style = new LS.StructureAttribute(LS.AttributeKey.BorderStyle);
        style.SetNameValue(LS.AttributeName.BorderStyle_Solid);
        layout.SetAttribute(style);
        var thickness = new LS.StructureAttribute(LS.AttributeKey.BorderThickness);
        thickness.SetArrayNumberValue(frame.Borders.Select(v => (double?)v).ToArray());
        layout.SetAttribute(thickness);
        var bbox = new LS.StructureAttribute(LS.AttributeKey.BBox);
        bbox.SetArrayNumberValue([Math.Round(frame.Box.LLX, 1), Math.Round(frame.Box.LLY, 1), Math.Round(frame.Box.URX, 1), Math.Round(frame.Box.URY, 1)]);
        layout.SetAttribute(bbox);
        if (frame.Shade is { } fill) StateBackground(div, fill);
        return div;
    }
}
