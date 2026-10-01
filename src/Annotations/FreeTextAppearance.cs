using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class FreeTextAnnotation
{
    /// <summary>Free text appearance: a number in the appearance stream's format.</summary>
    private static string FtFmt(FreeTextAppearanceState ft, double v) => v.ToString("0.###", ft.ci);

    /// <summary>The line pitch of a generated free-text appearance in em (probed: the
    /// reference regenerates a 12 pt Helvetica box with 13.788 pt between baselines, and
    /// seats the first baseline one font size under the top edge, 2 pt in from the left).</summary>
    private const double FreeTextLeadingEm = 1.149;

    internal void GenerateAppearance()
    {
        if (InternalReader.ResolveDict(Dict.Get("AP")) is not null) return;
        var ft = new FreeTextAppearanceState();
        ft.text = Contents;
        if (string.IsNullOrEmpty(ft.text)) ft.text = PlainTextFromRichText(RichText);
        if (string.IsNullOrEmpty(ft.text)) return;
        ft.rect = Rect;
        if (ft.rect is null || ft.rect.Width <= 0 || ft.rect.Height <= 0) return;

        ft.da = DefaultAppearanceObject;
        ft.fontName = string.IsNullOrWhiteSpace(ft.da.FontName) ? "Helvetica" : ft.da.FontName!;
        ft.fontSize = ft.da.FontSize > 0 ? ft.da.FontSize : 12.0;
        ft.color = ft.da.TextColor;

        ft.ts = TextStyle;
        if (ft.ts is not null)
        {
            if (ft.ts.FontSize > 0 && System.Math.Abs(ft.ts.FontSize - 12.0) > 1e-6) ft.fontSize = ft.ts.FontSize;
            if (ft.ts.Color.ToArgb() != System.Drawing.Color.Black.ToArgb()) ft.color = ft.ts.Color;
            if (!string.IsNullOrWhiteSpace(ft.ts.FontName) && ft.ts.FontName != "Helvetica") ft.fontName = ft.ts.FontName;
        }

        ft.w = ft.rect.Width;
        ft.h = ft.rect.Height;
        ft.borderWidth = ReadBorderWidth();
        ft.inset = 2.0 * ft.borderWidth;
        ft.avail = System.Math.Max(1.0, ft.w - 2 * ft.inset);

        ft.rotateDeg = InternalReader.Resolve(Dict.Get("Rotate")) switch
        {
            PdfReal rrv => rrv.Value,
            PdfInteger riv => riv.Value,
            _ => 0,
        };
        ft.rotated = System.Math.Abs(ft.rotateDeg % 360.0) > 1e-6;
        ft.quarterTurn = ft.rotated && System.Math.Abs(ft.rotateDeg % 90.0) < 1e-6;
        ft.bboxW = ft.w;
        ft.bboxH = ft.h;
        ft.rcos = 1;
        ft.rsin = 0;
        ft.ehw = ft.w / 2;
        ft.ehh = ft.h / 2;
        if (ft.rotated && !ft.quarterTurn)
        {
            double th = ft.rotateDeg * System.Math.PI / 180.0;
            ft.rcos = System.Math.Cos(th); ft.rsin = System.Math.Sin(th);
            ft.ehw = System.Math.Abs(ft.w / 2 * ft.rcos) + System.Math.Abs(ft.h / 2 * ft.rsin);
            ft.ehh = System.Math.Abs(ft.w / 2 * ft.rsin) + System.Math.Abs(ft.h / 2 * ft.rcos);
            ft.bboxW = 2 * ft.ehw; ft.bboxH = 2 * ft.ehh;
            double cx = (ft.rect.LLX + ft.rect.URX) / 2, cy = (ft.rect.LLY + ft.rect.URY) / 2;
            var exp = new PdfArray();
            exp.Add(new PdfReal(cx - ft.ehw)); exp.Add(new PdfReal(cy - ft.ehh));
            exp.Add(new PdfReal(cx + ft.ehw)); exp.Add(new PdfReal(cy + ft.ehh));
            Dict.Set("Rect", exp);
        }

        // A quarter turn runs the text along the box's OTHER axis, so that is the
        // measure it wraps against — wrapping to the box's width would break a line
        // that comfortably fits the length it is actually drawn along.
        if (ft.quarterTurn && (System.Math.Abs(ft.rotateDeg % 180.0) > 1e-6))
            ft.avail = System.Math.Max(1.0, ft.h - 2 * ft.inset);

        ft.fontDict = MakeFreeTextFontDict(ft.fontName);
        ft.metrics = Aspose.Pdf.Text.FontMetrics.FromFontDict(ft.fontDict, InternalReader);
        ft.lines = WrapText(ft.text, ft.metrics, ft.fontSize, ft.avail);

        ft.ci = System.Globalization.CultureInfo.InvariantCulture;

        ft.leading = ft.fontSize * FreeTextLeadingEm;
        ft.sb = new System.Text.StringBuilder();

        // A FreeText annotation's /C entry is its background colour: fill the
        // rectangle with it (behind border and text) when
        // present. Only the unrotated rect is filled —
        // BBox == rect there; rotated FreeText backgrounds are rare and skipped.
        if ((!ft.rotated || ft.quarterTurn) && InternalReader.Resolve(Dict.Get("C")) is PdfArray bgArr && bgArr.Count >= 3)
        {
            var bg = Color;
            ft.sb.Append("q\n");
            ft.sb.Append(FtFmt(ft, bg.R / 255.0)).Append(' ').Append(FtFmt(ft, bg.G / 255.0)).Append(' ')
              .Append(FtFmt(ft, bg.B / 255.0)).Append(" rg\n");
            ft.sb.Append("0 0 ").Append(FtFmt(ft, ft.w)).Append(' ').Append(FtFmt(ft, ft.h)).Append(" re\nf\nQ\n");
        }

        DrawFreeTextBorder(ft);

        OpenFreeTextTextBlock(ft);
        ft.sb.Append("BT\n");
        EmitFreeTextLines(ft);
        ft.sb.Append("ET\nQ\nEMC\n");

        StoreFreeTextAppearanceStream(ft);
    }
}
