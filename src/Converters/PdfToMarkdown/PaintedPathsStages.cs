using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.PdfToMarkdown;

internal static partial class MarkdownRenderer
{
    /// <summary>The stages of the painted-path collection: one content operator at a time.</summary>
    private static void CollectPaintedPath(PaintedPathsState pp, string raw)
    {
        var s = raw.Trim();
        var sp = s.LastIndexOf(' ');
        var name = sp < 0 ? s : s.Substring(sp + 1);
        switch (name)
        {
            case "q": pp.stack.Push((double[])pp.ctm.Clone()); break;
            case "Q": if (pp.stack.Count > 0) pp.ctm = pp.stack.Pop(); break;
            case "cm":
            {
                var p = Operands(s, 6);
                if (p == null) break;
                pp.ctm = new[]
                {
                    p[0] * pp.ctm[0] + p[1] * pp.ctm[2], p[0] * pp.ctm[1] + p[1] * pp.ctm[3],
                    p[2] * pp.ctm[0] + p[3] * pp.ctm[2], p[2] * pp.ctm[1] + p[3] * pp.ctm[3],
                    p[4] * pp.ctm[0] + p[5] * pp.ctm[2] + pp.ctm[4], p[4] * pp.ctm[1] + p[5] * pp.ctm[3] + pp.ctm[5],
                };
                break;
            }
            case "rg":
            {
                var p = Operands(s, 3);
                if (p != null)
                    pp.fill = $"#{(int)(p[0] * 255):x2}{(int)(p[1] * 255):x2}{(int)(p[2] * 255):x2}";
                break;
            }
            case "g":
            {
                var p = Operands(s, 1);
                if (p != null)
                    pp.fill = $"#{(int)(p[0] * 255):x2}{(int)(p[0] * 255):x2}{(int)(p[0] * 255):x2}";
                break;
            }
            case "m": case "l": case "c": case "h": case "re":
                AppendPathSegment(pp, name, s);
                break;
            case "S": case "s": case "B": case "b": case "B*": case "b*":
            case "f": case "F": case "f*":
            {
                if (pp.pts.Count > 0)
                {
                    var box = new Rectangle(pp.pts.Min(p => p.x), pp.pts.Min(p => p.y),
                        pp.pts.Max(p => p.x), pp.pts.Max(p => p.y));
                    var stroked = name is "S" or "s" or "B" or "b" or "B*" or "b*";
                    var filled = name is "f" or "F" or "f*" or "B" or "b" or "B*" or "b*";
                    var elem = $"<path d=\"{pp.d}\""
                        + $" fill=\"{(filled ? pp.fill : "none")}\""
                        + (stroked ? $" stroke=\"{pp.fill}\"" : "")
                        + $" transform=\"translate({F(-box.LLX)} {F(box.URY)}) scale(1 -1)\"/>";
                    pp.paints.Add((box, elem));
                }
                pp.pts.Clear();
                pp.d.Clear();
                break;
            }
            case "n":
                pp.pts.Clear();
                pp.d.Clear();
                break;
        }
    }

    /// <summary>The path-construction operators of <see cref="CollectPaintedPath"/>: m, l, c, h and re,
    /// each appended to the current path in transformed user space.</summary>
    private static void AppendPathSegment(PaintedPathsState pp, string name, string s)
    {
        switch (name)
        {
            case "m":
            {
                var p = Operands(s, 2);
                if (p == null) break;
                var (x, y) = Apply(pp, p[0], p[1]);
                pp.pts.Add((x, y));
                pp.d.Append(pp.d.Length > 0 ? " M " : "M ").Append(F(x)).Append(' ').Append(F(y));
                break;
            }
            case "l":
            {
                var p = Operands(s, 2);
                if (p == null) break;
                var (x, y) = Apply(pp, p[0], p[1]);
                pp.pts.Add((x, y));
                pp.d.Append(" L ").Append(F(x)).Append(' ').Append(F(y));
                break;
            }
            case "c":
            {
                var p = Operands(s, 6);
                if (p == null) break;
                var (x1, y1) = Apply(pp, p[0], p[1]);
                var (x2, y2) = Apply(pp, p[2], p[3]);
                var (x3, y3) = Apply(pp, p[4], p[5]);
                pp.pts.Add((x1, y1)); pp.pts.Add((x2, y2)); pp.pts.Add((x3, y3));
                pp.d.Append(" C ").Append(F(x1)).Append(' ').Append(F(y1)).Append(' ')
                    .Append(F(x2)).Append(' ').Append(F(y2)).Append(' ')
                    .Append(F(x3)).Append(' ').Append(F(y3));
                break;
            }
            case "h": pp.d.Append(" Z"); break;
            case "re":
            {
                var p = Operands(s, 4);
                if (p == null) break;
                var (x0, y0) = Apply(pp, p[0], p[1]);
                var (x1, y1) = Apply(pp, p[0] + p[2], p[1] + p[3]);
                pp.pts.Add((x0, y0)); pp.pts.Add((x1, y1));
                pp.d.Append(pp.d.Length > 0 ? " M " : "M ").Append(F(x0)).Append(' ').Append(F(y0))
                    .Append(" L ").Append(F(x1)).Append(' ').Append(F(y0))
                    .Append(" L ").Append(F(x1)).Append(' ').Append(F(y1))
                    .Append(" L ").Append(F(x0)).Append(' ').Append(F(y1)).Append(" Z");
                break;
            }
        }
    }
}
