using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class FreeTextAnnotation
{
    /// <summary>Free text appearance: the built stream stored as the annotation's normal appearance.</summary>
    private void StoreFreeTextAppearanceStream(FreeTextAppearanceState ft)
    {
        ft.apStream = new PdfStream(new PdfDictionary(), Compat.Latin1.GetBytes(ft.sb.ToString()));
        ft.apStream.Dict.Set("Type", new PdfName("XObject"));
        ft.apStream.Dict.Set("Subtype", new PdfName("Form"));
        ft.bbox = new PdfArray();
        ft.bbox.Add(new PdfReal(0)); ft.bbox.Add(new PdfReal(0));
        ft.bbox.Add(new PdfReal(ft.bboxW)); ft.bbox.Add(new PdfReal(ft.bboxH));
        ft.apStream.Dict.Set("BBox", ft.bbox);
        ft.fonts = new PdfDictionary();
        ft.fonts.Set(ResName(ft.fontName), ft.fontDict);
        ft.res = new PdfDictionary();
        ft.res.Set("Font", ft.fonts);
        ft.apStream.Dict.Set("Resources", ft.res);

        ft.ap = new PdfDictionary();
        ft.ap.Set("N", ft.apStream);
        Dict.Set("AP", ft.ap);
    }

    /// <summary>Free text appearance: the text lines emitted at the annotation's justification.</summary>
    private void EmitFreeTextLines(FreeTextAppearanceState ft)
    {
        ft.sb.Append('/').Append(ResName(ft.fontName)).Append(' ').Append(FtFmt(ft, ft.fontSize)).Append(" Tf\n");
        ft.sb.Append(FtFmt(ft, ft.color.R / 255.0)).Append(' ').Append(FtFmt(ft, ft.color.G / 255.0)).Append(' ')
          .Append(FtFmt(ft, ft.color.B / 255.0)).Append(" rg\n");
        ft.sb.Append(FtFmt(ft, ft.leading)).Append(" TL\n");
        ft.align = Justification switch
        {
            Justification.Right => Aspose.Pdf.HorizontalAlignment.Right,
            Justification.Center => Aspose.Pdf.HorizontalAlignment.Center,
            _ => ft.ts?.HorizontalAlignment ?? Aspose.Pdf.HorizontalAlignment.Left,
        };
        if (ft.rotated || ft.align == Aspose.Pdf.HorizontalAlignment.Left)
        {
            // A quarter turn needs no Td — its frame above already sits at the first baseline.
            if (ft.rotated && !ft.quarterTurn)
                ft.sb.Append("2 ").Append(FtFmt(ft, -ft.fontSize)).Append(" Td\n");
            else if (!ft.rotated)
                ft.sb.Append(FtFmt(ft, ft.inset)).Append(' ').Append(FtFmt(ft, ft.h - ft.fontSize)).Append(" Td\n");
            for (int i = 0; i < ft.lines.Count; i++)
            {
                if (i > 0) ft.sb.Append("T*\n");
                ft.sb.Append('(').Append(EscapePdfString(ft.lines[i])).Append(") Tj\n");
            }
        }
        else
        {
            // Center/Right alignment: each line is offset by its own measured width,
            // so emit a per-line Td (relative to the previous line's position).
            double prevX = 0;
            for (int i = 0; i < ft.lines.Count; i++)
            {
                var lineW = ft.metrics.MeasureString(ft.lines[i], ft.fontSize);
                double lineX = ft.align == Aspose.Pdf.HorizontalAlignment.Right
                    ? ft.w - lineW
                    : (ft.w - lineW) / 2;
                double dy = i == 0 ? ft.h - ft.fontSize : -ft.leading;
                ft.sb.Append(FtFmt(ft, lineX - prevX)).Append(' ').Append(FtFmt(ft, dy)).Append(" Td\n");
                ft.sb.Append('(').Append(EscapePdfString(ft.lines[i])).Append(") Tj\n");
                prevX = lineX;
            }
        }
    }

    /// <summary>Free text appearance: the text block opened, rotated onto the annotation's axis.</summary>
    private void OpenFreeTextTextBlock(FreeTextAppearanceState ft)
    {
        ft.sb.Append("/Tx BMC\nq\n");
        if (ft.quarterTurn)
        {
            // Turn the TEXT inside the (already-rotated) box. The frame's advance
            // direction runs along the box's new long axis and its "up" points at the
            // box edge that became the top; the first baseline is seated exactly as the
            // unrotated path seats it — `inset` in from the leading edge and one
            // fontSize down from the top inset — just measured along those axes.
            string Fmt6(double v) => v.ToString("0.######", ft.ci);
            var turn = (((int)System.Math.Round(ft.rotateDeg) % 360) + 360) % 360;
            // (advance, up) per turn, then the frame origin that seats the first baseline.
            var (fa, fb, fc, fd, ox, oy) = turn switch
            {
                90 => (0.0, 1.0, -1.0, 0.0, ft.fontSize, ft.inset),
                180 => (-1.0, 0.0, 0.0, -1.0, ft.w - ft.inset, ft.h - ft.inset - ft.fontSize),
                _ => (0.0, -1.0, 1.0, 0.0, ft.w - ft.inset - ft.fontSize, ft.h - ft.inset),
            };
            ft.sb.Append(Fmt6(fa)).Append(' ').Append(Fmt6(fb)).Append(' ')
              .Append(Fmt6(fc)).Append(' ').Append(Fmt6(fd)).Append(' ')
              .Append(Fmt6(ox)).Append(' ').Append(Fmt6(oy)).Append(" cm\n");
        }
        else if (ft.rotated)
        {
            // Rotate the text about the expanded box centre. The frame origin is the
            // box left edge (inset) at the vertical centre, so the rotated text stays
            // anchored to the rectangle's leading edge.
            string Fmt6(double v) => v.ToString("0.######", ft.ci);
            double e = ft.ehw - (ft.ehw - ft.inset) * ft.rcos;
            double f = ft.ehh - (ft.ehw - ft.inset) * ft.rsin;
            ft.sb.Append(Fmt6(ft.rcos)).Append(' ').Append(Fmt6(ft.rsin)).Append(' ')
              .Append(Fmt6(-ft.rsin)).Append(' ').Append(Fmt6(ft.rcos)).Append(' ')
              .Append(Fmt6(e)).Append(' ').Append(Fmt6(f)).Append(" cm\n");
        }
    }

    /// <summary>Free text appearance: the annotation's border and background drawn.</summary>
    private void DrawFreeTextBorder(FreeTextAppearanceState ft)
    {
        ft.bsDict = InternalReader.ResolveDict(Dict.Get("BS"));
        if (ft.borderWidth > 0)
        {
            bool dashed = ft.bsDict?.Get("S") is PdfName sn && sn.Value == "D";
            ft.sb.Append("q\n");
            ft.sb.Append(FtFmt(ft, ft.color.R / 255.0)).Append(' ').Append(FtFmt(ft, ft.color.G / 255.0)).Append(' ')
              .Append(FtFmt(ft, ft.color.B / 255.0)).Append(" RG\n");
            ft.sb.Append(FtFmt(ft, ft.borderWidth / 2)).Append(' ').Append(FtFmt(ft, ft.borderWidth / 2)).Append(' ')
              .Append(FtFmt(ft, ft.w - ft.borderWidth)).Append(' ').Append(FtFmt(ft, ft.h - ft.borderWidth)).Append(" re\n");
            ft.sb.Append(FtFmt(ft, ft.borderWidth)).Append(" w\n");
            if (dashed && InternalReader.Resolve(ft.bsDict!.Get("D")) is PdfArray dArr && dArr.Count > 0)
            {
                ft.sb.Append('[');
                for (int k = 0; k < dArr.Count; k++)
                {
                    if (k > 0) ft.sb.Append(' ');
                    ft.sb.Append(FtFmt(ft, PdfArrayHelper.GetDouble(dArr, k)));
                }
                ft.sb.Append("] 0 d\n");
            }
            ft.sb.Append("s\nQ\n");
        }
    }
}
