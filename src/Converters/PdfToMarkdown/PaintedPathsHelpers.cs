using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.PdfToMarkdown;

internal static partial class MarkdownRenderer
{
// The local helpers of the painted-path collection.
    private static (double x, double y) Apply(PaintedPathsState pp, double x, double y)
        => (pp.ctm[0] * x + pp.ctm[2] * y + pp.ctm[4], pp.ctm[1] * x + pp.ctm[3] * y + pp.ctm[5]);
}
