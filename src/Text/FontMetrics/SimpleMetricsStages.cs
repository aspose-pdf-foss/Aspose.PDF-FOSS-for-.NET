using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class FontMetrics
{
// The stages of the simple font metrics: the Type3 matrix and the /Widths array.

    /// <summary>ReadType3Metrics: one guarded read of the simple font metrics.</summary>
    private static void ReadType3Metrics(SimpleMetricsState sm)
    {
        if (sm.isType3)
        {
            double fontMatrix0 = 0.001;
            if (sm.reader?.Resolve(sm.fontDict.Get("FontMatrix")) is PdfArray fmArr && fmArr.Count >= 1)
            {
                fontMatrix0 = (sm.reader!.Resolve(fmArr[0]) ?? fmArr[0]) switch
                {
                    PdfInteger pi => pi.Value,
                    PdfReal pr => pr.Value,
                    _ => 0.001,
                };
            }
            sm.widthScale = fontMatrix0 * 1000.0;
        }
    }

    /// <summary>ReadWidthsArray: one guarded read of the simple font metrics.</summary>
    private static void ReadWidthsArray(SimpleMetricsState sm)
    {
        if (sm.widthsObj is PdfArray widthsArr && widthsArr.Count > 0)
        {
            sm.widths = new int[widthsArr.Count];
            for (var i = 0; i < widthsArr.Count; i++)
            {
                var w = sm.reader?.Resolve(widthsArr[i]) ?? widthsArr[i];
                if (!sm.isType3 && w is PdfReal exact && exact.Value != System.Math.Floor(exact.Value))
                    (sm.exactWidths ??= new Dictionary<int, double>())[sm.firstChar + i] = exact.Value;
                if (sm.isType3)
                {
                    double raw = w switch
                    {
                        PdfInteger pi => pi.Value,
                        PdfReal pr => pr.Value,
                        _ => 0,
                    };
                    sm.widths[i] = (int)System.Math.Round(raw * sm.widthScale);
                }
                else
                {
                    // Non-Type3 simple fonts: /Widths are already in 1/1000 text space.
                    // An integer entry casts exactly as it always did; a real one rounds
                    // to its nearest whole width rather than losing its fraction outright,
                    // and the exact value above is what the extraction measure reads.
                    sm.widths[i] = w switch
                    {
                        PdfInteger pi => (int)pi.Value,
                        PdfReal pr => (int)System.Math.Round(pr.Value),
                        _ => 0,
                    };
                }
            }
        }
    }
}
