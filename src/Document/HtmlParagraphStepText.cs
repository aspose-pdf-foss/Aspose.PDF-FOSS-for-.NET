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
// The procedure-step text measurers and wrappers, lifted out of LayoutProcedureStepRows.
    private static double PsMeasure(string txt, bool bold, double fs)
    {
        var f = bold ? "Helvetica-Bold" : "Helvetica";
        try
        {
            return Text.FontRepository.TryFindFont(f)?.MeasureString(txt, fs)
                   ?? txt.Length * fs * 0.5;
        }
        catch { return txt.Length * fs * 0.5; }
    }

    private static List<string> PsWrap(string txt, double fs, bool bold, double maxW)
    {
        var res = new List<string>();
        var cur = "";
        foreach (var word in txt.Split(' '))
        {
            var cand = cur.Length == 0 ? word : cur + " " + word;
            if (PsMeasure(cand, bold, fs) <= maxW || cur.Length == 0 && word.Length == 0)
            {
                cur = cand;
                continue;
            }
            if (cur.Length > 0) { res.Add(cur); cur = ""; }
            var piece = "";
            foreach (var ch in word)
            {
                if (piece.Length > 0 && PsMeasure(piece + ch, bold, fs) > maxW)
                {
                    res.Add(piece);
                    piece = "";
                }
                piece += ch;
            }
            cur = piece;
        }
        if (cur.Length > 0) res.Add(cur);
        if (res.Count == 0) res.Add("");
        return res;
    }

    private static int PsFitPrefix(string s, double fs, bool bold, double maxW)
    {
        if (PsMeasure(s.TrimEnd(), bold, fs) <= maxW) return s.Length;
        var lastGood = 0;
        for (var k = 1; k < s.Length; k++)
        {
            if (s[k] != ' ') continue;
            if (PsMeasure(s[..k].TrimEnd(), bold, fs) <= maxW) lastGood = k + 1;
            else break;
        }
        if (lastGood > 0) return lastGood;
        var n = 1;
        while (n < s.Length && s[n] != ' '
               && PsMeasure(s[..(n + 1)], bold, fs) <= maxW) n++;
        return n;
    }

    private static int PsFirstWordEnd(string s)
    {
        var k = 0;
        while (k < s.Length && s[k] == ' ') k++;
        while (k < s.Length && s[k] != ' ') k++;
        return k;
    }

    private static double PsGlyph(Content.ContentStreamBuilder pb, bool checkbox, double gx, double gBase)
    {
        if (checkbox)
        {
            pb.SetLineWidth(0.9).Rectangle(gx + 0.4, gBase, 7.7, 7.7).Stroke();
            return gx + 8.5 + 3.0;
        }
        const double rr = 4.1, rk = 0.5523;
        var ccx = gx + rr + 0.4;
        var ccy = gBase + rr;
        pb.SetLineWidth(0.9)
          .MoveTo(ccx + rr, ccy)
          .CurveTo(ccx + rr, ccy + rk * rr, ccx + rk * rr, ccy + rr, ccx, ccy + rr)
          .CurveTo(ccx - rk * rr, ccy + rr, ccx - rr, ccy + rk * rr, ccx - rr, ccy)
          .CurveTo(ccx - rr, ccy - rk * rr, ccx - rk * rr, ccy - rr, ccx, ccy - rr)
          .CurveTo(ccx + rk * rr, ccy - rr, ccx + rr, ccy - rk * rr, ccx + rr, ccy)
          .Stroke();
        return gx + 2 * rr + 0.8 + 3.75;
    }
}
