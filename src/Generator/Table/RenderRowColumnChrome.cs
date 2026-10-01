using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>The chrome stage of a column render: the generator-cell finish, borders and the cursor advance, verbatim.</summary>
    private void RenderRowColumnChrome(RowColumnState rc)
    {
        EmitGeneratorCellClip(rc, rc.cellX);

        EmitCellImages(rc, rc.cellX);

        BuildCellInnerTables(rc, rc.cellX);

        EmitCellInlineItems(rc, rc.cellX);
        rc.cellX += rc.cellWidth + CellSpacingH;
    }
}
