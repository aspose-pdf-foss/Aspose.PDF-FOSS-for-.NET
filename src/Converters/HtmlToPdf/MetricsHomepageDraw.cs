using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// Metrics homepage: 
    private static double M(MetricsHomepageState mh, byte[] ttf, string face, string s, double fs)
        => s.Length == 0 ? 0 : Text.Type0FontEmbedder.MeasureText(
            mh.measureDict, ttf, face, s, fs, stripSpacesInBaseFont: true);

    private static void WhiteGround(MetricsHomepageState mh) => mh.page.AddContentStream(Encoding.ASCII.GetBytes(
        Compat.Format(mh.inv, $"q 1 1 1 rg 0 0 {MhPageW:F0} {MhPageH:F0} re f Q\n")));

    private static void FlushPage(MetricsHomepageState mh)
    {
        mh.page.AddContentStream(Encoding.ASCII.GetBytes(mh.shapes.ToString()));
        if (mh.page.Dict.Get("Resources") is Core.PdfDictionary res
            && res.Get("Font") is Core.PdfDictionary fd)
        {
            var sb = new StringBuilder();
            foreach (var (ttf, face, fs, x, baseTd, text, col) in mh.runs)
            {
                var (rn, hex) = Text.Type0FontEmbedder.Embed(fd, ttf, face, text,
                    stripSpacesInBaseFont: true);
                sb.AppendLine(Compat.Format(mh.inv,
                    $"BT {col} rg /{rn} {fs:0.##} Tf 1 0 0 1 {x:F3} {MhPageH - baseTd:F3} Tm ")
                    + "<" + Compat.ToHexString(hex) + "> Tj ET");
            }
            mh.page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        }
        mh.shapes = new StringBuilder();
        mh.runs = new List<(byte[], string, double, double, double, string, string)>();
    }

    private static void Fill(MetricsHomepageState mh, double x, double yTop, double w, double h, string rgb)
        => mh.shapes.AppendLine(Compat.Format(mh.inv,
            $"q {rgb} rg {x:F3} {MhPageH - yTop - h:F3} {w:F3} {h:F3} re f Q"));

    private static void HLine(MetricsHomepageState mh, double x0, double x1, double yTop, double w, string rgb)
        => mh.shapes.AppendLine(Compat.Format(mh.inv,
            $"q {rgb} RG {w:0.###} w {x0:F3} {MhPageH - yTop:F3} m {x1:F3} {MhPageH - yTop:F3} l S Q"));

    private static void Run(MetricsHomepageState mh, byte[] ttf, string face, double fs, double x, double baseTd, string text, string col)
    { if (text.Length > 0) mh.runs.Add((ttf, face, fs, x, baseTd, text, col)); }
}
