using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the DNN wrapper walk: the row flush and one element step.
    private static void FlushDnnRow(DnnWalkState dw)
    {
        if (dw.row is { Pairs.Count: > 0 }) dw.blocks.Add(dw.row);
        dw.row = null;
        dw.runX = 0;
    }

    /// <summary></summary>
    private static bool DnnWalkStep(DnnWalkState dw)
    {
        dw.m = Regex.Match(dw.inner[dw.pos..], @"<(div|table|ol|hr|br)\b[^>]*/?>", RegexOptions.IgnoreCase);
        if (!dw.m.Success) return false;
        dw.openIdx = dw.pos + dw.m.Index;
        dw.open = dw.m.Value;
        dw.tag = dw.m.Groups[1].Value.ToLowerInvariant();
        dw.cls = DtpAttr(dw.open, "class") ?? "";
        dw.st = DtpAttr(dw.open, "style") ?? "";
        if (dw.tag != "br") dw.brRun = false;
        if (!dw.cls.Contains("RowPad", StringComparison.OrdinalIgnoreCase)) dw.lastRowPad = false;

        if (dw.cls.Contains("PrintHidden", StringComparison.OrdinalIgnoreCase)
            || dw.cls.Contains("aspNetHidden", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(dw.st, @"display\s*:\s*none", RegexOptions.IgnoreCase))
        {
            var hEnd = dw.tag == "hr" ? -2 : DnnTagEnd(dw.inner, dw.openIdx, dw.tag);
            dw.pos = hEnd < 0 ? dw.openIdx + dw.open.Length : dw.inner.IndexOf('>', hEnd) + 1;
            return true;
        }

        if (dw.tag == "br")
        {
            // section spacer <br/>s between wrappers: the first of a run
            // clears the float line (half a row pad), each further one is
            // a full 23px row (both measured against the band ladder)
            FlushDnnRow(dw);
            dw.blocks.Add(new DnnGap { H = (dw.brRun ? 18 : 5) * 0.75 });
            dw.brRun = true;
            dw.pos = dw.openIdx + dw.open.Length;
            return true;
        }

        if (dw.tag == "hr")
        {
            FlushDnnRow(dw);
            dw.blocks.Add(new DnnGap { H = 10 * 0.75 });
            dw.pos = dw.openIdx + dw.open.Length;
            return true;
        }

        if (dw.tag == "ol"
            || dw.tag == "table" && dw.cls.Contains("RadioButtonList", StringComparison.OrdinalIgnoreCase))
        {
            // the agreement list arrives in a later increment; radio
            // tables are the hidden editable twin of a shown value. Plain
            // tables are skin chrome — fall through and descend.
            FlushDnnRow(dw);
            var tEnd = DnnTagEnd(dw.inner, dw.openIdx, dw.tag);
            if (tEnd < 0) { dw.pos = dw.openIdx + dw.open.Length; return true; }
            dw.pos = dw.inner.IndexOf('>', tEnd) + 1;
            return true;
        }

        if (dw.tag == "table" && dw.cls.Contains("GridContainer", StringComparison.OrdinalIgnoreCase))
        {
            FlushDnnRow(dw);
            var tEnd = DnnTagEnd(dw.inner, dw.openIdx, "table");
            if (tEnd < 0) { dw.pos = dw.openIdx + dw.open.Length; return true; }
            DnnParseGrid(dw.inner[(dw.inner.IndexOf('>', dw.openIdx) + 1)..tEnd], dw.indentPx, dw.blocks);
            dw.pos = dw.inner.IndexOf('>', tEnd) + 1;
            return true;
        }

        if (dw.cls.Contains("ModuleHeaderContainer", StringComparison.OrdinalIgnoreCase)
            || dw.cls.Contains("ModuleSubHeaderContainer", StringComparison.OrdinalIgnoreCase))
        {
            DnnWalkModuleHeader(dw);
            return true;
        }

        if (dw.cls.Contains("ModuleBodyContainer", StringComparison.OrdinalIgnoreCase))
        {
            DnnWalkModuleBody(dw);
            return true;
        }

        if (dw.cls.Contains("ContainerFieldLabelHoriz", StringComparison.OrdinalIgnoreCase))
        {
            DnnWalkFieldLabel(dw);
            return true;
        }

        if (dw.cls.Contains("ContainerFieldControlHoriz", StringComparison.OrdinalIgnoreCase))
        {
            DnnWalkFieldControl(dw);
            return true;
        }

        if (dw.cls.Contains("RowPad", StringComparison.OrdinalIgnoreCase))
        {
            DnnWalkRowPad(dw);
            return true;
        }

        // any other wrapper: descend
        dw.pos = dw.openIdx + dw.open.Length;
        return true;
    }
}
