using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FlattenPageState
{
    public IO.PdfReader reader = null!;
    public Aspose.Pdf.Core.PdfArray? annotsObj;
    public Aspose.Pdf.Core.PdfArray remaining = null!;
    public System.IO.MemoryStream appendContent = null!;
    // Flattened field appearances are registered as FRM{n} in /Annots order so a caller can
    // look each one up by position. The base index is path-dependent:
    // the document/form flatten numbers from FRM0, the facade FlattenAllFields from FRM1.
    public int frmCounter;
    public Page page = default!;
    public bool hideButtons = false;
    public int frmStartIndex = 0;
    public bool flattenNonWidgets = false;
    public bool skipInvisible = false;
    public System.Collections.Generic.HashSet<PdfDictionary>? fieldWidgets = null;
    public bool dropUnstamped = false;
}
}
