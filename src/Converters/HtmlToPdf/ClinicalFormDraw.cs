using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Clinical form helpers: text flattening, measurement, page breaks, runs, lines, fills, box and radio inks, cell widths and the item layout.
    private static string Flat(ClinicalFormState cf, string frag) => Regex.Replace(
        DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();

    private static double M(ClinicalFormState cf, string s, int style, double fs)
    {
        if (s.Length == 0) return 0;
        var (ttf, face) = style switch
        {
            1 => (cf.segoeBold!, "SegoeUIBold"),
            2 => (cf.segoeItalic!, "SegoeUIItalic"),
            _ => (cf.segoe!, "SegoeUI"),
        };
        return Text.Type0FontEmbedder.MeasureText(cf.measureDict, ttf, face, s, fs,
            stripSpacesInBaseFont: true);
    }

    private static void NewPage(ClinicalFormState cf) { cf.pageShapes.Add(new StringBuilder()); cf.pageRuns.Add(new()); cf.pageFlowBot.Add(CfContentBottom); }

    private static void Run(ClinicalFormState cf, int style, double fs, double x, double baseTd, string text, string col = CfText)
    { if (text.Length > 0) cf.pageRuns[^1].Add((style, fs, x, baseTd, text, col)); }

    private static void Line(ClinicalFormState cf, double x0, double y0, double x1, double y1, double w, string rgb)
        => cf.pageShapes[^1].AppendLine(Compat.Format(cf.inv,
            $"q {rgb} RG {w:0.###} w {x0:F3} {CfPageH - y0:F3} m {x1:F3} {CfPageH - y1:F3} l S Q"));

    private static void Fill(ClinicalFormState cf, double x, double yTop, double w, double h, string rgb)
        => cf.pageShapes[^1].AppendLine(Compat.Format(cf.inv,
            $"q {rgb} rg {x:F3} {CfPageH - yTop - h:F3} {w:F3} {h:F3} re f Q"));

    // the era widget border: a 1 pt black box half a point inside the rect
    private static void BoxInk(ClinicalFormState cf, double x, double yTop, double w, double h)
        => cf.pageShapes[^1].AppendLine(Compat.Format(cf.inv,
            $"q {CfBlack} RG 1 w {x + 0.5:F3} {CfPageH - yTop - h + 0.5:F3} {w - 1:F3} {h - 1:F3} re S Q"));

    // the radio widget circle: two beziers, radius 4.375, 1 pt stroke
    private static void RadioInk(ClinicalFormState cf, double x, double baseTd)
    {
        var cy = CfPageH - baseTd + CfRadio / 2;   // centre, PDF coords
        var x0 = x + 0.5; var x1 = x + CfRadio - 0.5;
        var yc = cy; var yo = 5.8333333;
        cf.pageShapes[^1].AppendLine(Compat.Format(cf.inv,
            $"q 1 w {CfBlack} RG {x0:F3} {yc:F3} m {x0:F3} {yc + yo:F3} {x1:F3} {yc + yo:F3} {x1:F3} {yc:F3} c "
            + $"{x1:F3} {yc - yo:F3} {x0:F3} {yc - yo:F3} {x0:F3} {yc:F3} c s Q"));
    }

    private static void BreakPage(ClinicalFormState cf)
    {
        cf.pageFlowBot[^1] = cf.bot;    // the container borders stop at the flow cut
        NewPage(cf);
        cf.bot = CfContentTop;
        cf.prevWasBox = true;      // a continued box tops at 72 − 0.75
    }

    private static double CellW(ClinicalFormState cf, int c) => M(cf, cf.boldParts[c], 1, CfFs)
        + (cf.restParts[c].Length > 0 ? M(cf, " " + cf.restParts[c], 0, CfFs) : M(cf, " ", 0, CfFs));

    // one li: inline atoms flow into wrapped lines; blocks interleave
    private static void LayoutCfItem(ClinicalFormState cf, List<CfAtom> atoms, int itemNo)
    {
        cf.markerPending = true;
        cf.lineAtoms = new List<(CfAtom Atom, double X)>();
        cf.pen = CfItemX;
        cf.lineHasInput = false;
        cf.pendingSpace = false;

        foreach (var a in atoms)
        {
            switch (a.Kind)
            {
                case "text":
                {
                    // words are the wrap units of bare text
                    var words = a.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var lead = a.Text.StartsWith(' ');
                    for (var w = 0; w < words.Length; w++)
                    {
                        if (w > 0 || lead) cf.pendingSpace = cf.pendingSpace || cf.lineAtoms.Count > 0;
                        PlaceInline(cf, itemNo, new CfAtom { Kind = "text", Text = words[w], Style = a.Style });
                    }
                    if (a.Text.EndsWith(' ')) cf.pendingSpace = true;
                    break;
                }
                case "radio" or "iinput" or "label":
                    PlaceInline(cf, itemNo, a);
                    break;
                case "br":
                    FlushLine(cf, itemNo);
                    // the forced break makes one empty line box
                    cf.bot += 0;
                    var t = cf.bot - (cf.prevWasBox ? CfJunction : 0);
                    cf.bot = t + CfLineH;
                    cf.prevWasBox = false;
                    break;
                case "binput" or "tarea":
                {
                    FlushLine(cf, itemNo);
                    var h = a.Kind == "tarea" ? CfTextAreaH : CfControlH;
                    var top = cf.bot - CfJunction;
                    if (top + h > CfContentBottom) { BreakPage(cf); top = cf.bot - CfJunction; }
                    BoxInk(cf, CfItemX, top, CfInnerX1 - CfItemX, h);
                    cf.bot = top + h;
                    cf.prevWasBox = true;
                    break;
                }
                case "check":
                {
                    FlushLine(cf, itemNo);
                    var top = cf.bot - (cf.prevWasBox ? CfJunction : 0);
                    if (top + CfLineH > CfContentBottom) { BreakPage(cf); top = cf.bot; }
                    var baseline = top + CfBaseOff;
                    if (cf.markerPending)
                    {
                        var num = itemNo + ".";
                        Run(cf, 0, CfFs, CfMarkerOneX + M(cf, "1.", 0, CfFs) - M(cf, num, 0, CfFs), baseline, num);
                        cf.markerPending = false;
                    }
                    Run(cf, 0, CfFs, CfItemX + CfCheckIndent, baseline, a.Text);
                    cf.bot = baseline + CfLineDesc + CfCheckMb;
                    cf.prevWasBox = false;
                    break;
                }
                case "table":
                {
                    FlushLine(cf, itemNo);
                    if (a.Rows is not { Count: > 0 }) break;   // JS-filled shells render nothing
                    foreach (var row in a.Rows)
                    {
                        var top = cf.bot - (cf.prevWasBox ? CfJunction : 0);
                        if (top + CfCellH > CfContentBottom) { BreakPage(cf); top = cf.bot; }
                        var colW = (CfInnerX1 - CfItemX) / Math.Max(1, row.Count);
                        for (var c = 0; c < row.Count; c++)
                            Run(cf, 0, CfFs, CfItemX + c * colW + CfCellPad, top + CfCellBaseOff, row[c]);
                        cf.bot = top + CfCellH;
                        cf.prevWasBox = true;
                    }
                    break;
                }
            }
        }
        FlushLine(cf, itemNo);
    }
}
