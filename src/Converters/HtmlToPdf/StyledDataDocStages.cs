using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the styled data-font document: the face set, the normal line height, one leaf's layout and the layout walk.
    private static (Text.GlyphOutlineParser gp, (double winAsc, double winDesc, double upm) fm)? SdFaces(StyledDataDocState sy, StyledNode p)
    {
        Text.GlyphOutlineParser? gp = default;
        (double winAsc, double winDesc, double upm) fm = default;
        gp = null!;
        fm = default;
        if (!sy.glyphParsers.TryGetValue(p.FontKey, out var g))
        {
            try
            {
                g = new Text.GlyphOutlineParser(p.Ttf!);
                var tp = new Text.TrueTypeParser(p.Ttf!);
                tp.Parse();
                if (tp.UnitsPerEm <= 0 || tp.UsWinAscent <= 0) return null;
                sy.faceMetrics[p.FontKey] = (tp.UsWinAscent, tp.UsWinDescent, tp.UnitsPerEm);
            }
            catch { return null; }
            sy.glyphParsers[p.FontKey] = g;
        }
        gp = g;
        fm = sy.faceMetrics[p.FontKey];
        return (gp, fm);
    }

    private static double SdNormalLh(double f)
    => 1.5 * Math.Floor(0.78125 * f + 0.5);

    private static bool SdLayoutLeaf(StyledDataDocState sy, StyledNode p)
    {
        if (SdFaces(sy, p) is not (var gp, var fm)) return false;
        sy.size = p.FontSizePt;
        sy.lh = SdNormalLh(sy.size);
        sy.asc = fm.winAsc * sy.size / fm.upm + (sy.lh - (fm.winAsc + fm.winDesc) * sy.size / fm.upm) / 2;
        sy.ls = p.Style.TryGetValue("letter-spacing", out var lsv) ? StyledLen(lsv) : 0.0;
        sy.lsF = (double)(float)Math.Round(sy.ls, 3);
        var upper = p.Style.TryGetValue("text-transform", out var tt)
            && tt.Trim().Equals("uppercase", StringComparison.OrdinalIgnoreCase);

        sy.x0 = sy.marginLeftLay + sy.bodyMarginLeft;
        sy.colWidth = sy.bodyWidth;
        for (var a = p.Parent; a is not null && a.Tag == "div"; a = a.Parent)
        {
            var aml = a.Style.TryGetValue("margin-left", out var v1) ? StyledLen(v1) : 0;
            var amr = a.Style.TryGetValue("margin-right", out var v2) ? StyledLen(v2) : 0;
            sy.x0 += aml;
            sy.colWidth -= aml + amr;
        }
        if (sy.colWidth <= 0) return false;

        sy.stream = new List<(char c, int run)>();
        for (var r = 0; r < p.Runs!.Count; r++)
        {
            var rt = upper ? p.Runs[r].ToUpperInvariant() : p.Runs[r];
            foreach (var ch in rt) sy.stream.Add((ch, r));
        }

        sy.lines = new List<List<(char c, int run)>>();
        {
            var line = new List<(char c, int run)>();
            var w = 0.0;
            var i = 0;
            while (i < sy.stream.Count)
            {
                var j = i + (sy.stream[i].c == ' ' ? 1 : 0);
                while (j < sy.stream.Count && sy.stream[j].c != ' ') j++;
                var segW = 0.0;
                for (var k = i; k < j; k++) segW += AdvW(sy, gp, fm, sy.stream[k].c) + sy.lsF;
                if (line.Count > 0 && w + segW > sy.colWidth + 1e-9)
                {
                    sy.lines.Add(line);
                    line = new List<(char c, int run)>();
                    w = 0;
                    var from = sy.stream[i].c == ' ' ? i + 1 : i;
                    for (var k = from; k < j; k++)
                    {
                        line.Add(sy.stream[k]);
                        w += AdvW(sy, gp, fm, sy.stream[k].c) + sy.lsF;
                    }
                }
                else
                {
                    for (var k = i; k < j; k++) line.Add(sy.stream[k]);
                    w += segW;
                }
                i = j;
            }
            if (line.Count > 0) sy.lines.Add(line);
        }

        sy.mt = p.Style.TryGetValue("margin-top", out var mtv) ? StyledLen(mtv, sy.size) : 0.0;
        sy.pendingMargins.Add(sy.mt);
        sy.y += sy.prevDesc + sy.pendingMargins.Max() + sy.asc;

        for (var li = 0; li < sy.lines.Count; li++)
        {
            LayoutStyledDataLine(sy, p, li, fm, gp);
        }

        sy.prevDesc = sy.lh - sy.asc;
        sy.pendingMargins.Clear();
        // UA default paragraph bottom margin is 1.12em when nothing is declared.
        sy.pendingMargins.Add(p.Style.TryGetValue("margin-bottom", out var mbv)
            ? StyledLen(mbv, sy.size) : 1.12 * sy.size);
        return true;
    }

    private static bool SdWalkLayout(StyledDataDocState sy, StyledNode n)
    {
        if (n.Tag == "p") return SdLayoutLeaf(sy, n);
        foreach (var c in n.Children)
        {
            if (c.Tag == "div")
            {
                sy.pendingMargins.Add(c.Style.TryGetValue("margin-top", out var v) ? StyledLen(v) : 0);
                if (!SdWalkLayout(sy, c)) return false;
                sy.pendingMargins.Add(c.Style.TryGetValue("margin-bottom", out var v2) ? StyledLen(v2) : 0);
            }
            else if (!SdWalkLayout(sy, c)) return false;
        }
        return true;
    }
}
