using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Bootstrap rows: one matched construct - heading, row or panel - emitted.</summary>
    private static bool EmitBootstrapConstruct(BootstrapRowsState bs)
    {
        var m = bs.construct.Match(bs.html, bs.pos);
        if (!m.Success || bs.yTd > bs.limit) return false;
        bs.pos = m.Index + m.Length;
        if (m.Groups["h3"].Success)
        {
            Advance(bs, BrH3MarginTop);
            var txt = Flat(bs, m.Groups["h3"].Value);
            var w = MeasureFaceText(bs.face, txt, BrH3FontPt);
            EmitRun(bs, "FA", BrH3FontPt, (bs.pageWidth - w) / 2, bs.yTd + bs.dropH3, txt, BrText);
            bs.yTd += BrH3LineH;
            bs.pendingMb = BrH3MarginBottom;
        }
        else if (m.Value.StartsWith("<hr", StringComparison.OrdinalIgnoreCase))
        {
            Advance(bs, BrHrMargin);
            bs.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(bs.invc,
                $"q {BrHrInk.R / 255.0:0.###} {BrHrInk.G / 255.0:0.###} {BrHrInk.B / 255.0:0.###} RG 0.75 w " +
                $"{bs.contentL:F2} {bs.pageHeight - bs.yTd:F2} m {bs.contentR:F2} {bs.pageHeight - bs.yTd:F2} l S Q\n")));
            bs.pendingMb = BrHrMargin;
        }
        else if (m.Groups["pc"].Success)
        {
            Advance(bs, 0);
            var txt = Flat(bs, m.Groups["pc"].Value);
            var w = MeasureFaceText(bs.face, txt, BrFontPt);
            EmitRun(bs, "FA", BrFontPt, (bs.pageWidth - w) / 2, bs.yTd + bs.drop, txt, BrText);
            bs.yTd += BrLineH;
            bs.pendingMb = BrPMb;
        }
        else if (m.Groups["cls"].Value.Equals("row", StringComparison.OrdinalIgnoreCase))
        {
            // a bare body-level row: consume to its balanced close
            var end = FindDivClose(bs.html, bs.pos);
            Advance(bs, 0);
            bs.yTd += RenderRow(bs, bs.html[bs.pos..end], bs.yTd, bs.contentL, bs.contentR);
            bs.pos = end;
        }
        else
        {
            EmitBootstrapPanel(bs);
        }
        return true;
    }
}
