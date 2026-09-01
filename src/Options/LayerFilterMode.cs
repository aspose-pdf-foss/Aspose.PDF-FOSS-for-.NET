using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>Which reference layer operation the content filter reproduces.</summary>
internal enum LayerFilterMode
{
    /// <summary>Layer.Save — target verbatim, everything else reduced to its
    /// structure/state skeleton, tail cut after the target's last block.</summary>
    Save,
    /// <summary>Layer.Flatten — everything verbatim; only the target's marked-content
    /// markers (and markers nested inside its blocks) drop.</summary>
    Flatten,
    /// <summary>Layer.Delete — target blocks reduce to the skeleton (markers dropped);
    /// the target's XObject draws drop; everything else, markers included, stays.</summary>
    Delete,
}
