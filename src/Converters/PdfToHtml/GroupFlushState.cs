using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class GroupFlushState
{
    public System.Text.StringBuilder groupText = null!;
    public ContentRenderState ct = null!;
    public StringBuilder sb = null!;
    public double pageHeight = 0;
    public double pageWidth = 0;
    public bool textOnly = false;
    public StyleRegistry? styleReg = null;
    public ClassNamer classNamer;
    public List<LinkTarget>? linkTargets = null;
    public RotationRegistry? rotReg = null;
    public double pageLLX = 0;
    public double yTopRef = 0;
    public ZCounter? zCounter = null;
    public bool pageTurnedOver = false;
    public bool emCompensation = false;
}
}
