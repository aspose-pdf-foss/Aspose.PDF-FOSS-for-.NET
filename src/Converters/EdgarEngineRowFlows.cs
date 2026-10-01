using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
namespace Aspose.Pdf.Converters;

internal static partial class EdgarHtmlRenderer
{
    sealed partial class Engine
    {
        /// <summary>The row's pending flow lines are emitted top-down; the shift the page break cost comes back.</summary>
        private double EmitPendingRowFlows(List<CellFlow> flows, double rowTop)
        {
            double pageShift = 0;
            var pending = flows.SelectMany(f => f.Lines.Select(l => (f, l)))
                .OrderBy(t => t.l.Top).ToList();
            foreach (var (f, ln) in pending)
            {
                var top = rowTop + ln.Top - pageShift;
                var bottom = top + ln.Asc + ln.Desc;
                if (bottom > BottomLimit + 0.01)
                {
                    BreakPage(false);
                    _atPageTop = false;
                    pageShift += top - _y;
                    top = rowTop + ln.Top - pageShift;
                }
                var baseline = top + ln.Asc;
                if (ln.BorderTopW > 0)
                    _pg.Rects.Add(new RectFill { X = f.X0, TopTd = top - ln.BorderTopW / 2, W = f.Width, H = 0, Color = ln.BorderTopColor, Stroke = true, LineW = ln.BorderTopW });
                double lineW = ln.Pieces.Sum(p => p.W);
                double x = f.X0 + ln.St.MarginLeft + (ln.FirstLine ? ln.St.TextIndent : 0);
                if (ln.St.Align == "center") x = f.X0 + (f.Width - lineW) / 2;
                else if (ln.St.Align == "right") x = f.X0 + f.Width - lineW;
                foreach (var piece in ln.Pieces)
                {
                    var r = piece.Run;
                    AddRun(new Run { Text = piece.Text, Face = r.Face, Size = r.Size, Color = r.Color, Sup = r.Sup, LinkId = r.LinkId, AnchorsBefore = r.AnchorsBefore }, x, baseline - (r.Sup ? 1.26 : 0));
                    r.AnchorsBefore = null;
                    if (r.LinkId >= 0 && RunHasInk(piece.Run) && piece.Text.Trim(' ', (char)0xA0).Length > 0)
                        AddLinkRect(r.LinkId, x, baseline, x + piece.W, r.Face, r.Size);
                    x += piece.W;
                }
                if (ln.BorderBottomW > 0)
                    _pg.Rects.Add(new RectFill { X = f.X0, TopTd = top + ln.Asc + ln.Desc + ln.BorderBottomW / 2, W = f.Width, H = 0, Color = ln.BorderBottomColor, Stroke = true, LineW = ln.BorderBottomW });
            }
            return pageShift;
        }
    }
}
