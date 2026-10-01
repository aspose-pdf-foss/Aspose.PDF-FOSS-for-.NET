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
private sealed class FieldEntryImportState
{
    public Aspose.Pdf.Core.PdfDictionary dict = null!;
    // A child's partial name is the last dotted segment; a top-level field uses its full name.
    public string partial = null!;
    public int pageIndex;
    public bool childApRoundTripped;
    // Restore the widget's /AP from captured stream bytes, when present.
    // /AP precedence over GenerateAppearance keeps Acrobat's pre-baked
    // appearance pixel-identical through the round-trip (the per-type
    // generator's first line short-circuits when /AP is populated).
    public bool apRoundTripped;
    public System.Text.Json.JsonElement entry = default!;
    public PdfReader reader = default!;
    public PdfDictionary? parent = null;
    public List<FieldSerializationResult> results = default!;
}
}
