using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Decision letter drawing helpers: fills, rules, frames, runs, label/value pairs and box titles.
    private static double Drop(DecisionLetterState dl, double fs, double box) => (box - fs * dl.wm.sum) / 2 + fs * dl.wm.asc;

    private static void SetFill(DecisionLetterState dl, Color col) => dl.sb.AppendLine(Compat.Format(dl.inv,
        $"{col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg"));

    private static void Fill(DecisionLetterState dl, double x, double yTop, double w, double h, Color col)
    {
        SetFill(dl, col);
        dl.sb.AppendLine(Compat.Format(dl.inv,
            $"{x:F2} {dl.pageHeight - yTop - h:F2} {w:F2} {h:F2} re f"));
    }

    private static void HLine(DecisionLetterState dl, double x0, double x1, double yTop, Color col, double w = 0.75)
        => dl.sb.AppendLine(Compat.Format(dl.inv,
            $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} RG {w:0.##} w " +
            $"{x0:F2} {dl.pageHeight - yTop:F2} m {x1:F2} {dl.pageHeight - yTop:F2} l S Q"));

    private static void VLine(DecisionLetterState dl, double x, double y0, double y1, Color col, double w = 0.75)
        => dl.sb.AppendLine(Compat.Format(dl.inv,
            $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} RG {w:0.##} w " +
            $"{x:F2} {dl.pageHeight - y0:F2} m {x:F2} {dl.pageHeight - y1:F2} l S Q"));

    private static void FrameRect(DecisionLetterState dl, double x, double yTop, double w, double h, Color col, double lw)
        => dl.sb.AppendLine(Compat.Format(dl.inv,
            $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} RG {lw:0.##} w " +
            $"{x:F2} {dl.pageHeight - yTop - h:F2} {w:F2} {h:F2} re S Q"));

    private static double Measure(DecisionLetterState dl, string t, bool bold, double fs)
        => MeasureFaceText(dl.face + (bold ? " Bold" : ""), t, fs);

    private static void EmitRun(DecisionLetterState dl, string text, double fs, bool bold, double x, double baseline, Color? col)
    {
        if (text.Length == 0) return;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(
            (dl.page.Dict.Get("Resources") as Core.PdfDictionary)!.Get("Font") as Core.PdfDictionary
                ?? throw new InvalidOperationException(),
            PosFace(dl.face + (bold ? " Bold" : "")).ttf ?? PosFace(dl.face!).ttf!,
            dl.face!.Replace(" ", "") + (bold ? "Bold" : ""), text, stripSpacesInBaseFont: true);
        var cc = col ?? dl.ink;
        dl.sb.AppendLine(Compat.Format(dl.inv,
            $"q {cc.R / 255.0:0.###} {cc.G / 255.0:0.###} {cc.B / 255.0:0.###} rg " +
            $"BT /{rn} {fs:0.##} Tf 1 0 0 1 {x:F2} {dl.pageHeight - baseline:F2} Tm " +
            $"<{Compat.ToHexString(hex)}> Tj ET Q"));
    }

    // a label span + its value on ONE line: bold prefix, plain remainder
    private static void EmitLabelValue(DecisionLetterState dl, string label, string value, double fs, double x, double baseline)
    {
        EmitRun(dl, label, fs, true, x, baseline, null);
        EmitRun(dl, value, fs, false, x + Measure(dl, label, true, fs), baseline, null);
    }

    private static string Inner(DecisionLetterState dl, string tagged) => CollapseWs(DecodeEntities(
        Regex.Replace(tagged, "<[^>]+>", " "))).Trim();

    private static void BrokenFrame(DecisionLetterState dl, double x, double top)
    {
        (var phName, dl.iconRef) = RegisterPlaceholderIcon(dl.doc, dl.page, dl.iconRef, masked: true);
        dl.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
            $"q 32 0 0 32 {x + 1:0.##} {dl.pageHeight - top - 33:0.##} cm /{phName} Do Q\n")));
        var dk = ParseCssColor("#555555");
        var lt = ParseCssColor("#AAAAAA");
        DrawBox(dl.page, x, dl.pageHeight - top, 34, 1, null, 0, dk);
        DrawBox(dl.page, x, dl.pageHeight - top - 33, 34, 1, null, 0, lt);
        DrawBox(dl.page, x, dl.pageHeight - top - 33, 1, 34, null, 0, dk);
        DrawBox(dl.page, x + 33, dl.pageHeight - top - 33, 1, 34, null, 0, lt);
    }

    // ── boxSection machinery ──
    private static double BoxTitle(DecisionLetterState dl, string title, double x, double sectionTop)
    {
        var kx = x + 18.0;                        // title left: 24 px
        var kTop = sectionTop + DnTitleKnockoutDropPt;
        Fill(dl, kx, kTop, Measure(dl, title, true, dl.bodyFs) + 12.0, 18.0,
            Color.FromArgb(255, 255, 255));
        EmitRun(dl, title, dl.bodyFs, true, kx + 6.0, kTop + Drop(dl, dl.bodyFs, 18.0), null);
        return sectionTop + DnFrameDropPt;        // the 2 px frame's top
    }
}
