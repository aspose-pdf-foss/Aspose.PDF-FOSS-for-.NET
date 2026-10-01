using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Positioned form: one section of the form rendered.</summary>
    private static bool RenderFormSection(PositionedFormState pf, Match secM)
    {
        var sn = new FormSectionState();
        sn.secH = double.Parse(secM.Groups[2].Value, pf.inv) * PxPt;
        sn.body = DivBodyAt(pf.html, secM.Index + secM.Length);

        foreach (Match fb in Regex.Matches(sn.body,
            @"<div\b[^>]*style\s*=\s*(['""])position:\s*absolute;left:\s*(-?\d+(?:\.\d+)?)px;top:\s*(-?\d+(?:\.\d+)?)px;?(?:width:\s*(\d+(?:\.\d+)?)px;?)?(?:height:\s*(\d+(?:\.\d+)?)px;?)?\s*\1[^>]*>",
            RegexOptions.IgnoreCase))
        {
            if (!RenderFormField(pf, sn, fb)) break;
        }

        pf.lastBottom = pf.secY + sn.secH + 0.75;            // the white border div bottom
        // the ignored page-break <br> = one 16px line between sections
        pf.secY = pf.lastBottom + PfSecGapPt + 0.75;
        return true;
    }
}
