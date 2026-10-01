using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The binary-text converter helpers: the advance of a glyph, segment and paragraph closing, item collection, the run emitter and the line break.
    private static double Adv(BinaryTextState bt, char ch) => bt.gp.CMap.TryGetValue(ch, out var g)
        ? Math.Round(bt.gp.GetAdvanceWidth(g) * 1000.0 / bt.upm) * 12.0 / 1000.0
        : 6.0;

    private static void EndSegment(BinaryTextState bt)
    {
        if (bt.curSeg.Count > 0)
        {
            bt.curPara.Add((bt.curSegW, bt.curSeg));
            bt.curSeg = new List<(double, char?)>();
            bt.curSegW = 0;
        }
    }

    private static void EndParagraph(BinaryTextState bt)
    {
        EndSegment(bt);
        bt.paragraphs.Add(bt.curPara);
        bt.curPara = new List<(double, List<(double, char?)>)>();
        bt.pendingSpace = false;
    }

    private static void AddItem(BinaryTextState bt, double adv, char? ch)
    {
        if (bt.pendingSpace)
        {
            // The collapsed space run becomes ONE 3 pt inter-segment separator
            // (a space draws no ink, so it is an invisible advance).
            EndSegment(bt);
            bt.curPara.Add((3.0, new List<(double, char?)> { (3.0, null) }));
            bt.pendingSpace = false;
        }
        bt.curSeg.Add((adv, ch));
        bt.curSegW += adv;
    }

    private static void EmitRun(BinaryTextState bt, double x, double y, string run)
    {
        if (run.Length == 0) return;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(bt.fontDict, bt.ttf, "TimesNewRoman", run);
        bt.sb.Append("BT /").Append(rn).Append(" 12 Tf 1 0 0 1 ")
          .Append(x.ToString("F2", bt.invc)).Append(' ')
          .Append((bt.pageH - y).ToString("F2", bt.invc)).Append(" Tm <")
          .Append(Compat.ToHexString(hex)).Append("> Tj ET\n");
    }

    private static void NextLine(BinaryTextState bt)
    {
        bt.baseline += 13.5;
        if (bt.baseline > bt.pageH - 60)
        {
            bt.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
            bt.sb.Clear();
            bt.page = bt.doc.Pages.Add(bt.pageW, bt.pageH);
            EnsureFonts(bt.page, bt.fontDict);
            bt.baseline = 88.91;
        }
    }
}
