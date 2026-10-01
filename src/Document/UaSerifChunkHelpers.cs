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
// The UA serif chunk's text measure.
    private static double UaMeasure(string t, bool bold, double fsM)
    {
        try
        {
            return Text.FontRepository.TryFindFont(bold ? "Times-Bold" : "Times-Roman")
                ?.MeasureString(t, fsM) ?? t.Length * fsM * 0.5;
        }
        catch { return t.Length * fsM * 0.5; }
    }
}
