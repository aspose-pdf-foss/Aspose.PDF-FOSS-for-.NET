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
    /// <summary>The stages of the inline emphasis runs: tokenising the runs, wrapping the atoms and emitting one line.</summary>
    private bool EmitInlineEmphasisLine(InlineEmphasisRunsState ie, int li)
    {
        var baseline = ie.frameTop - ie.firstBaselinePt - li * ie.linePitchPt;
        // Merge adjacent same-style atoms into one show per run.
        var line = ie.lines2[li];
        var ri = 0;
        while (ri < line.Count)
        {
            var rj = ri;
            while (rj + 1 < line.Count
                   && line[rj + 1].Item2 == line[ri].Item2
                   && line[rj + 1].Item3 == line[ri].Item3) rj++;
            var textRun = string.Concat(line.GetRange(ri, rj - ri + 1)
                .ConvertAll(a => a.Item1));
            var xOff = line[ri].Item4;
            var runW = line[rj].Item4 + line[rj].Item5 - xOff;
            var bold2 = line[ri].Item2;
            var (res2, hex2) = Text.Type0FontEmbedder.Embed(ie.fontDict2,
                bold2 ? ie.boldTtf! : ie.regTtf!,
                bold2 ? ie.iface + " Bold" : ie.iface,
                textRun, stripSpacesInBaseFont: true);
            ie.b2.BeginText();
            ie.b2.SetFont(res2, ie.ipt);
            ie.b2.SetTextMatrix(1, 0, 0, 1, ie.marginLeft + xOff, baseline);
            ie.b2.ShowTextHex(hex2);
            ie.b2.EndText();
            if (line[ri].Item3)
            {
                // Stroked underline: a 0.1em-thick band whose top
                // edge sits 0.1em below the baseline, spanning the
                // run's advances.
                ie.b2.SaveState();
                ie.b2.SetStrokeGray(0);
                ie.b2.SetLineWidth(0.1 * ie.ipt);
                var uy = baseline - 0.15 * ie.ipt;
                ie.b2.MoveTo(ie.marginLeft + xOff, uy)
                  .LineTo(ie.marginLeft + xOff + runW, uy)
                  .Stroke();
                ie.b2.RestoreState();
            }
            ri = rj + 1;
        }
        return true;
    }

    /// <summary></summary>
    private void WrapInlineEmphasisAtoms(InlineEmphasisRunsState ie)
    {
        foreach (var at in ie.atoms)
        {
            var w = at.bold ? ie.mBold(at.text) : ie.mReg(at.text);
            if (!at.space && ie.curW + w > ie.contentW && ie.cur.Count > 0)
            {
                // Drop the trailing space atom the wrap breaks on.
                while (ie.cur.Count > 0 && ie.cur[^1].Item1.Trim().Length == 0)
                    ie.cur.RemoveAt(ie.cur.Count - 1);
                ie.lines2.Add(ie.cur);
                ie.cur = new List<(string, bool, bool, double, double)>();
                ie.curW = 0;
            }
            ie.cur.Add((at.text, at.bold, at.underline, ie.curW, w));
            ie.curW += w;
        }
    }

    /// <summary></summary>
    private void TokeniseInlineEmphasisRuns(InlineEmphasisRunsState ie)
    {
        foreach (var run in ie.iruns)
        {
            var t = run.text;
            var i0 = 0;
            while (i0 < t.Length)
            {
                var isSpace = t[i0] == ' ';
                var i1 = i0;
                while (i1 < t.Length && (t[i1] == ' ') == isSpace) i1++;
                ie.atoms.Add((t[i0..i1], run.bold, run.underline, isSpace));
                i0 = i1;
            }
        }
    }
}
