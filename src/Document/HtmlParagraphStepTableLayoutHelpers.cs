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
// The table cell layout's line break: the pending pieces close a line at its height.
    private static void NewLine(PsTableLayoutState pl)
    {
        pl.lhs.Add(pl.lineH + pl.clMargin);
        pl.clMargin = 0;
        pl.ccx = 0;
        pl.lineH = pl.baseLine;
        pl.lineDirty = false;
    }
}
