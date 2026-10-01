using Aspose.Pdf.Core;

namespace Aspose.Pdf.Optimization;

internal static partial class PdfAValidator
{
    /// <summary>The stages of the transparency check: one extended graphics state at a time.</summary>
    private static void CheckExtGStateTransparency(TransparencyCheckState tc, string gsName)
    {
        var gs = tc.document.Reader.ResolveDict(tc.extGStateDict!.Get(gsName));
        if (gs is null) return;

        var hasTransparency = false;
        string? detail = null;

        // Check /SMask
        var smask = gs.Get("SMask");
        if (smask is not null && smask is not PdfName { Value: "None" })
        {
            hasTransparency = true;
            detail = "has soft mask (SMask)";
        }

        // Check /ca (non-stroking alpha) < 1
        if (!hasTransparency)
        {
            var caObj = gs.Get("ca");
            if (caObj is PdfReal caReal && caReal.Value < 1.0)
            {
                hasTransparency = true;
                detail = $"has non-stroking alpha ca={caReal.Value}";
            }
            else if (caObj is PdfInteger caInt && caInt.Value < 1)
            {
                hasTransparency = true;
                detail = $"has non-stroking alpha ca={caInt.Value}";
            }
        }

        // Check /CA (stroking alpha) < 1
        if (!hasTransparency)
        {
            var bigCaObj = gs.Get("CA");
            if (bigCaObj is PdfReal bigCaReal && bigCaReal.Value < 1.0)
            {
                hasTransparency = true;
                detail = $"has stroking alpha CA={bigCaReal.Value}";
            }
            else if (bigCaObj is PdfInteger bigCaInt && bigCaInt.Value < 1)
            {
                hasTransparency = true;
                detail = $"has stroking alpha CA={bigCaInt.Value}";
            }
        }

        // Check /BM (blend mode) not Normal
        if (!hasTransparency)
        {
            var bm = gs.GetName("BM");
            if (bm is not null && bm != "Normal" && bm != "Compatible")
            {
                hasTransparency = true;
                detail = $"has blend mode BM={bm}";
            }
        }

        if (hasTransparency && tc.isPdfA1)
        {
            var msg = $"Page {tc.page.Number} uses transparency (not allowed in PDF/A-1)";
            if (!tc.issues.Contains(msg))
            {
                tc.issues.Add(msg);
                tc.violations.Add(new PdfAViolation
                {
                    Rule = "Transparency",
                    Description = $"Page {tc.page.Number} ExtGState '{gsName}' {detail}",
                    PageNumber = tc.page.Number,
                });
            }
        }
    }
}
