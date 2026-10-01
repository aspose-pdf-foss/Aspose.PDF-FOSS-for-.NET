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
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PdfAConvertState
{
    public PdfFormat format;
    // The standard PDF/A transformations (embed fonts, write the XMP pdfaid, add an
    // OutputIntent, normalise the version) are applied for every ErrorAction — that
    // applies structural fixes only (a None-conversion still embeds fonts and writes
    // metadata) and is the only way the output can validate structurally.
    public bool fix;
    // Removing prohibited CONTENT (catalog/AA actions, non-compliant annotations) is what
    // ConvertErrorAction governs: Delete strips it, None only logs the violation and leaves
    // the content in place. The structural fixes above are applied regardless.
    public bool strip;
    // Plain PDF version targets (1.0 – 1.6): retarget the header/catalog
    // version. No PDF/A conformance work is needed for these.
    public string? plainVersion;
    public bool isPdfX;
    // Determine PDF/A part and conformance from format
    public (string?, string?) partConformance;
    public string? part;
    public string? conformance;
    // 2. Fix the PDF version up to the floor its part requires. PDF/A-1 sits on
    // PDF 1.4; parts 2 and 3 only need 1.3 underneath them, and a 1.2 source
    // converted to either PDF/A-2 level comes out at 1.3 — NOT restamped at the
    // 1.7 its conformance level is written against.
    public string floor = null!;
    public string? version;
    // 3. Add/fix XMP metadata
    public XmpMetadata meta = null!;
    public bool needsPdfAId;
    // PDF/A-4 (part "4") carries no conformance level, so never treat its absence
    // as a violation; parts 1–3 still require pdfaid:conformance.
    public bool needsConformance;
    public bool needsTitle;
    public bool needsProducer;
    // 11. Verify all non-embedded non-Standard14 fonts can be resolved.
    // PDF/A requires every glyph-bearing font to be embedded; if a font is
    // unembedded AND FontRepository can't find it, conversion fails.
    public bool fontsResolved;
    // 11b. Auto-tagging: synthesise a logical-structure tree from the page content so the
    // output carries a /StructTreeRoot. This is mandatory for the accessible A-levels
    // (which require a tagged, titled document), and otherwise runs when the caller opts in
    // (AutoTaggingSettings.Default enables it) for tagged PDF/A / PDF/UA output.
    public bool autoTag;
    // An unconvertable violation (implementation limit baked into the content)
    // makes the conversion report failure even though every fixable repair above
    // was still applied.
    public bool hasUnconvertable;
    // The extraction inputs, captured from the method parameters.
    public PdfFormatConversionOptions options = null!;
}
}
