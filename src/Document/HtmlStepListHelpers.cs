using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
// Line-box metrics, UA tag styles, glyph measures, the kerned greedy wrap and the run emitter behind the HTML step-list renderer.
    // CSS "normal" line box: the hhea line height rounds to whole CSS pixels; the
    // baseline sits winAscent + half the surplus leading below the box top.
    private static double Pitch(HtmlStepListState hs, double s) => 0.75 * System.Math.Floor(hs.hheaSum * (s * 96.0 / 72.0) / hs.upm + 0.5);

    private static double Asc(HtmlStepListState hs, double s) => hs.winAsc * s / hs.upm + (Pitch(hs, s) - (hs.winAsc + hs.winDesc) * s / hs.upm) / 2;

    // UA defaults: heading sizes and margins in em of the base size. The margin
    // resolves from the exact CSS size while Tf/metrics carry the 3-decimal-truncated
    // value (a float-formatting quirk — visually inert, kept for fidelity).
    private static (double size, double margin, bool bold) TagStyle(HtmlStepListState hs, string tag)
    {
        var (factor, marginEm) = tag switch
        {
            "h1" => (2.0, 0.67),
            "h2" => (1.5, 0.75),
            "h3" => (1.17, 0.83),
            _ => (1.0, 0.0),
        };
        var css = factor * hs.em;
        return (System.Math.Floor(css * 1000.0) / 1000.0, marginEm * css, tag is "h1" or "h2" or "h3");
    }

    private static int Gid(HtmlStepListState hs, char c, bool bold)
    {
        var gp = bold ? hs.gpBold : hs.gpReg;
        return gp.CMap.TryGetValue(c, out var g) ? g : 0;
    }

    private static double GlyphW(HtmlStepListState hs, int gid, bool bold, double s)
    {
        var gp = bold ? hs.gpBold : hs.gpReg;
        var u = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0;
        return gp.GetAdvanceWidth(gid) * s / u;
    }

    private static double KernW(HtmlStepListState hs, int prev, int cur, bool bold, double s)
    {
        var gp = bold ? hs.gpBold : hs.gpReg;
        var u = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0;
        return gp.GetKernAdjustment(prev, cur) * s / u;
    }

    // Greedy space-break wrap with pair kerning (IsBreakWords=false semantics: a word
    // never splits; an overlong word overflows). The space a line breaks at is dropped;
    // all other spaces stay in-string, so a run boundary keeps its inter-word space.
    private static List<List<(char c, bool bold)>> Wrap(HtmlStepListState hs, List<(char c, bool bold)> stream, double size, double maxW)
    {
        var lines = new List<List<(char c, bool bold)>>();
        var line = new List<(char c, bool bold)>();
        var w = 0.0;
        var prevGid = -1;
        var prevBold = false;

        (double w, int endGid, bool endBold) Measure(int from, int to, int pg, bool pb)
        {
            var mw = 0.0;
            for (var k = from; k < to; k++)
            {
                var (c, bl) = stream[k];
                var gid = Gid(hs, c, bl);
                if (pg >= 0 && pb == bl) mw += KernW(hs, pg, gid, bl, size);
                mw += GlyphW(hs, gid, bl, size);
                pg = gid;
                pb = bl;
            }
            return (mw, pg, pb);
        }

        var i = 0;
        while (i < stream.Count)
        {
            // One wrap token: the pending space (if any) plus the following word.
            var j = i + (stream[i].c == ' ' ? 1 : 0);
            while (j < stream.Count && stream[j].c != ' ') j++;
            var (extW, endGid, endBold) = Measure(i, j, prevGid, prevBold);
            if (line.Count > 0 && w + extW > maxW + 1e-9)
            {
                lines.Add(line);
                line = new List<(char c, bool bold)>();
                (w, prevGid, prevBold) = (0, -1, false);
                var from = stream[i].c == ' ' ? i + 1 : i;
                if (from < j)
                {
                    (w, prevGid, prevBold) = Measure(from, j, -1, false);
                    for (var k = from; k < j; k++) line.Add(stream[k]);
                }
            }
            else
            {
                for (var k = i; k < j; k++) line.Add(stream[k]);
                w += extW;
                prevGid = endGid;
                prevBold = endBold;
            }
            i = j;
        }
        if (line.Count > 0) lines.Add(line);
        return lines;
    }

    private static void EmitRun(HtmlStepListState hs, string text, bool bold, double size, double x, double yAbs)
    {
        var (res, hex) = Text.Type0FontEmbedder.Embed(hs.fontDict,
            bold ? hs.boldTtf : hs.regTtf,
            bold ? "Times New Roman Bold" : "Times New Roman",
            text, stripSpacesInBaseFont: true, resNameHint: hs.resNameHint);
        hs.csb.BeginText();
        hs.csb.SetFont(res, size);
        hs.csb.MoveTextPosition(x, yAbs);
        if (StepKernAdjustments(text, bold ? hs.gpBold : hs.gpReg) is { } adj)
            hs.csb.ShowTextHexKerned(hex, adj);
        else
            hs.csb.ShowTextHex(hex);
        hs.csb.EndText();
    }
}
