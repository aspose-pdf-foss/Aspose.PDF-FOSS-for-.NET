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
private sealed class JsonAppearanceState
{
    public byte[] bytes = null!;
    public Aspose.Pdf.Core.PdfDictionary sd = null!;
    // Rebuild /Resources/Font as Standard-14 entries keyed by the original
    // aliases (Helv, HeBo, ZaDb, ...) so the content's Tf operator resolves.
    public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
    public Aspose.Pdf.Core.PdfDictionary resources = null!;
    public System.Text.Json.JsonElement stateEl = default!;
}
}
