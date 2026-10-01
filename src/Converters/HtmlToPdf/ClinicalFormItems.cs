using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Clinical form item layout helpers: the line flush, an atom's width and the inline placement.
    private static void FlushLine(ClinicalFormState cf, int itemNo)
    {
        if (cf.lineAtoms.Count == 0) { cf.pen = CfItemX; cf.lineHasInput = false; return; }
        var top = cf.bot - (cf.prevWasBox || cf.lineHasInput ? CfJunction : 0);
        var lineBot = cf.lineHasInput ? top + CfInlineInputH : top + CfLineH;
        if (lineBot > CfContentBottom)
        {
            BreakPage(cf);
            top = cf.bot - (cf.lineHasInput ? CfJunction : 0);
            lineBot = cf.lineHasInput ? top + CfInlineInputH : top + CfLineH;
        }
        var baseline = top + (cf.lineHasInput ? CfInlineBaseOff : CfBaseOff);
        if (cf.markerPending)
        {
            var num = itemNo + ".";
            Run(cf, 0, CfFs, CfMarkerOneX + M(cf, "1.", 0, CfFs) - M(cf, num, 0, CfFs), baseline, num);
            cf.markerPending = false;
        }
        foreach (var (a, x) in cf.lineAtoms)
            switch (a.Kind)
            {
                case "text": Run(cf, a.Style, a.Style == 3 ? CfSmallFs : CfFs, x, baseline, a.Text); break;
                case "radio": RadioInk(cf, x, baseline); break;
                case "iinput": BoxInk(cf, x, baseline - CfInlineBaseOff, CfInlineInputW, CfInlineInputH); break;
            }
        cf.bot = cf.lineHasInput ? lineBot : baseline + CfLineDesc;
        cf.prevWasBox = cf.lineHasInput;
        cf.lineAtoms.Clear();
        cf.pen = CfItemX;
        cf.lineHasInput = false;
    }

    private static double AtomW(ClinicalFormState cf, CfAtom a) => a.Kind switch
    {
        "text" => M(cf, a.Text, a.Style, a.Style == 3 ? CfSmallFs : CfFs),
        "radio" => CfRadio,
        "iinput" => CfInlineInputW + CfInlineMarginR,
        _ => 0,
    };

    private static void PlaceInline(ClinicalFormState cf, int itemNo, CfAtom a)
    {
        var group = a.Group is { } g ? g : new List<CfAtom> { a };
        var groupW = 0.0;
        foreach (var ga in group) groupW += AtomW(cf, ga);
        var spaceW = cf.pendingSpace ? M(cf, " ", 0, CfFs) : 0;
        if (cf.lineAtoms.Count > 0 && cf.pen + spaceW + groupW > CfInnerX1 && groupW <= CfInnerX1 - CfItemX)
        { FlushLine(cf, itemNo); spaceW = 0; }
        else if (cf.pendingSpace && cf.lineAtoms.Count > 0)
        {
            cf.lineAtoms.Add((new CfAtom { Kind = "text", Text = " " }, cf.pen));
            cf.pen += spaceW;
        }
        cf.pendingSpace = false;
        foreach (var ga in group)
        {
            if (ga.Kind == "iinput") cf.lineHasInput = true;
            cf.lineAtoms.Add((ga, cf.pen));
            cf.pen += AtomW(cf, ga);
        }
    }
}
