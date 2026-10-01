using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Metrics homepage: one metric grid drawn on its page.</summary>
    private static bool DrawMetricsGrid(MetricsHomepageState mh, int ti)
    {
        if (ti == 1) { FlushPage(mh); mh.page = mh.doc.Pages.Add(MhPageW, MhPageH); EnsureFonts(mh.page); WhiteGround(mh); }
        var top = ti == 0 ? MhTable1Top : MhTable2Top;
        var y = top;
        foreach (var row in mh.tables[ti])
        {
            if (row.Count == 1 && row[0].IsHeading)
            {
                Fill(mh, MhX0, y, MhTableW, MhBandH, MhPurple);
                Run(mh, mh.segoeSemi, "SegoeUISemibold", MhHeadFs, MhX0 + MhCellPad,
                    y + MhBandBaseOff, row[0].Text, "1 1 1");
                y += MhBandH;
                HLine(mh, MhX0, MhX1, y + 0.375, 0.75, MhRule);
                y += 0.75;                       // the rule occupies one 1px row gap
                continue;
            }
            // wrap each cell in its content width, take the tallest
            var cellX = MhX0;
            var drawList = new List<(MhCell Cell, double X, double W, List<string> Lines)>();
            var maxH = MhLabelPitch;
            foreach (var cell in row)
            {
                var cw = cell.Flex * MhTableW + MhRowPad;   // border box: basis + padding
                var fs = cell.IsLabel ? MhLabelFs : cell.IsLink && cell.Flex > 0.09 ? MhLabelFs : MhLinkFs;
                var budget = cw - MhRowPad;
                var lines = cell.Text.Length > 0
                    ? WrapCfWords(cell.Text, s => M(mh, mh.segoe, "SegoeUI", s, fs) * MhWrapFactor, budget)
                    : new List<string>();
                drawList.Add((cell, cellX, cw, lines));
                var pitch = cell.IsLabel ? MhLabelPitch : MhLinkPitch;
                if (lines.Count > 0 && lines[0].Length > 0)
                    maxH = Math.Max(maxH, lines.Count * pitch);
                cellX += cw;
            }
            var rowH = maxH + MhRowPad;
            foreach (var (cell, cx, cw, lines) in drawList)
            {
                if (cell.IsLabel) Fill(mh, cx, y, cw, rowH, MhCyan);
                if (lines.Count == 0 || lines[0].Length == 0) continue;
                var isViewAll = !cell.IsLabel && cell.Flex > 0.09;   // the centred 10% cell
                var fs = cell.IsLabel || isViewAll ? MhLabelFs : MhLinkFs;
                var baseOff = cell.IsLabel || isViewAll ? MhLabelBaseOff : MhLinkBaseOff;
                var pitch = cell.IsLabel || isViewAll ? MhLabelPitch : MhLinkPitch;
                var col = cell.IsLink ? MhBlue : MhInk;
                for (var li = 0; li < lines.Count; li++)
                {
                    var lw2 = M(mh, mh.segoe, "SegoeUI", lines[li], fs);
                    var lx = isViewAll ? cx + (cw - lw2) / 2 : cx + MhCellPad;
                    var baseline = y + baseOff + li * pitch;
                    Run(mh, mh.segoe, "SegoeUI", fs, lx, baseline, lines[li], col);
                    if (cell.IsLink)
                        HLine(mh, lx, lx + lw2, baseline + (fs >= 15 ? 1.5 : 1.35),
                            fs >= 15 ? 1.5 : 1.35, MhBlue);
                }
            }
            y += rowH;
            HLine(mh, MhX0, MhX1, y + 0.375, 0.75, MhRule);
            y += 0.75;
        }
        return true;
    }
}
