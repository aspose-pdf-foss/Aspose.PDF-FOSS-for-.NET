using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
// The margin-assigned fragment's face, line measure and page-break helpers.
    private static string MfFace(MarginFragmentState mf, HtmlFragmentLayoutState hl, Converters.HtmlToPdfConverter.FlowRun r)
        => Converters.HtmlToPdfConverter.Std14Face(
            r.Family ?? hl.html.TextState?.Font?.FontName, r.Bold, r.Italic);

    private static double MfWidthOf(MarginFragmentState mf, HtmlFragmentLayoutState hl, string t, string face)
    {
        if (t.Length == 0) return 0;
        try
        {
            return Text.FontRepository.TryFindFont(face)?.MeasureString(t, mf.mfSize)
                   ?? t.Length * mf.mfSize * 0.5;
        }
        catch { return t.Length * mf.mfSize * 0.5; }
    }

    private static void MfNewPage(MarginFragmentState mf, HtmlFragmentLayoutState hl)
    {
        mf.mfBuilder.RestoreState();
        if (mf.mfDrew) hl.flow.InjectContentAtCursor(mf.mfBuilder.Build());
        hl.flow.ForceNewPage();
        mf.mfBuilder = new Content.ContentStreamBuilder();
        mf.mfBuilder.SaveState();
        mf.mfDrew = false;
        mf.mfPageTop = mf.mfTopRest;
        mf.mfY = mf.mfTopRest;
    }
}
