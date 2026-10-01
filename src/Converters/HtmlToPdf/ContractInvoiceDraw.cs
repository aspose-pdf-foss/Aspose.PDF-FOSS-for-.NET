using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Contract invoice drawing helpers: drop, width, runs, fills, lines and the page break.
    private static double Drop(ContractInvoiceState cv, double fs, double box) => (box - fs * cv.wm.sum) / 2 + fs * cv.wm.asc;

    private static double W(ContractInvoiceState cv, string t, string face, double fs) => MeasureFaceText(face, t, fs);

    private static void EmitRun(ContractInvoiceState cv, string text, double fs, string face, double x, double baseline, Color col)
    {
        if (text.Length == 0) return;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(
            (cv.page.Dict.Get("Resources") as Core.PdfDictionary)!.Get("Font") as Core.PdfDictionary
                ?? throw new InvalidOperationException(),
            PosFace(face).ttf ?? PosFace(cv.lightFace).ttf!,
            face.Replace(" ", "").Replace("-", ""), text, stripSpacesInBaseFont: true);
        cv.sb.AppendLine(Compat.Format(cv.inv,
            $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
            $"BT /{rn} {fs:0.##} Tf 1 0 0 1 {x:F2} {cv.pageHeight - baseline:F2} Tm " +
            $"<{Compat.ToHexString(hex)}> Tj ET Q"));
    }

    private static void FillRect(ContractInvoiceState cv, double x, double yTop, double w, double h, Color col)
        => cv.sb.AppendLine(Compat.Format(cv.inv,
            $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
            $"{x:F2} {cv.pageHeight - yTop - h:F2} {w:F2} {h:F2} re f Q"));

    private static void Line(ContractInvoiceState cv, double x0, double y0, double x1, double y1, Color col, double lw, bool dotted)
    {
        var dash = dotted ? "[2.25 2.25] 0 d " : "";
        cv.sb.AppendLine(Compat.Format(cv.inv,
            $"q {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} RG {lw:0.##} w " +
            $"{dash}{x0:F2} {cv.pageHeight - y0:F2} m {x1:F2} {cv.pageHeight - y1:F2} l S Q"));
    }

    private static void NewPage(ContractInvoiceState cv)
    {
        cv.page = cv.doc.Pages.Add(CiPageWPt, cv.pageHeight);
        EnsureFonts(cv.page, cv.docFontDict);
        cv.sb = new StringBuilder();
        cv.streams.Add((cv.page, cv.sb));
    }
}
