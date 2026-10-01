using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The cell's own declared borders draw over the row band, side by side.</summary>
    private static void RenderMetricCellBorders(MetricRowsState mr, int c, double boxW)
    {
        if (c < mr.r.Count && (mr.r[c].BorderLeftW > 0 || mr.r[c].BorderRightW > 0
            || mr.r[c].BorderBottomW > 0 || mr.r[c].BorderTopW > 0))
        {
            var bc2 = mr.r[c];
            var rowTopY = mr.cursor.y - mr.s;
            var rowBotY = mr.cursor.y - mr.s - mr.rowBoxH;
            var bsb = new StringBuilder("q 0 0 0 RG ");
            void SideLine(double w2, double sx0, double sy0, double sx1, double sy1, bool dash, Color col)
                => bsb.Append(Compat.Format(mr.invc,
                    $"{col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} RG " +
                    $"{w2:0.##} w {(dash ? "[2.25 2.25] 0 d " : "")}" +
                    $"{sx0:F2} {sy0:F2} m {sx1:F2} {sy1:F2} l S "));
            if (bc2.BorderLeftW > 0)
                SideLine(bc2.BorderLeftW, mr.colX, rowTopY, mr.colX, rowBotY, false, bc2.BorderLeftCol);
            if (bc2.BorderRightW > 0)
                SideLine(bc2.BorderRightW, mr.colX + boxW, rowTopY, mr.colX + boxW, rowBotY, false, bc2.BorderRightCol);
            if (bc2.BorderBottomW > 0 && bc2.BorderBottomDouble)
            {
                // a `double` rule: the pair of thin lines the sum rows
                // close with, spanning the declared width
                SideLine(0.7, mr.colX, rowBotY + bc2.BorderBottomW - 0.35,
                    mr.colX + boxW, rowBotY + bc2.BorderBottomW - 0.35, false, bc2.BorderBottomCol);
                SideLine(0.7, mr.colX, rowBotY + 0.35, mr.colX + boxW, rowBotY + 0.35, false, bc2.BorderBottomCol);
            }
            else if (bc2.BorderBottomW > 0)
                SideLine(bc2.BorderBottomW, mr.colX, rowBotY, mr.colX + boxW, rowBotY, false, bc2.BorderBottomCol);
            if (bc2.BorderTopW > 0)
                SideLine(bc2.BorderTopW, mr.colX, rowTopY, mr.colX + boxW, rowTopY,
                    bc2.BorderTopDashed, bc2.BorderTopCol);
            bsb.Append("Q\n");
            mr.cursor.page.AddContentStream(Encoding.ASCII.GetBytes(bsb.ToString()));
        }
    }
}
