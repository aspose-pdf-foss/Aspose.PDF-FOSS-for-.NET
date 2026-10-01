using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
// The head-part placer's mapping from a shifted x back to the original pen.
    private static double OrigX(ReflowState rf, double px, double shiftedX) => px + (shiftedX - rf.runStartX);
}
