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
    private sealed partial class FlowLayout
    {
        /// <summary>Each note group's lines: its marker, its wrapped body and the rule above it, measured onto the band the group will draw in.</summary>
        private void LayoutNoteGroups(List<List<BaseParagraph>> groups, List<BandLine> lines, NoteEntry e, BandGeometry g, BaseParagraph? noteHead)
        {
            for (var gi = 0; gi < groups.Count; gi++)
            {
                LayoutOneNoteGroup(groups, gi, lines, e, g, noteHead);
            }
        }
    }
}
