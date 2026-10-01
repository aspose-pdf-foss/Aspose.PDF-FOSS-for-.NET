using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public partial class TextBoxField
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class TextBoxAppearanceState
{
    // Parse DA for font name and size. A field with no own /DA inherits it up the
    // /Parent chain and finally from the AcroForm /DA — only if none is found at all
    // do we fall back to a fixed Helvetica 12. (The inherited /DA commonly carries a
    // size of 0, i.e. auto-size, which the fixed default would otherwise mask.)
    public string da = null!;
    public string fontName = null!;
    public double fontSize;
    public string[] daParts = null!;
    // Get the widget rect for BBox
    public Aspose.Pdf.Core.PdfArray? rectArr;
    public double llx;
    public double lly;
    public double urx;
    public double ury;
    public double w;
    public double h;
    // /MK /R rotated appearance: the text lays out in the ROTATED box (width and
    // height swap for 90/270) and the form's /Matrix turns it into the rect.
    public int apRotation;
    // Resolve the composite / Unicode appearance face BEFORE any measurement so
    // the auto-size and alignment math below sees the same advances the shown
    // hex run will use (GetGlyphWidthEm consults _uniAppearanceTtf for chars
    // beyond WinAnsi).
    public bool needsComposite;
    public Aspose.Pdf.Core.PdfDictionary? drFontDict;
    public Dictionary<int, int>? compositeCmap;
    // No composite /DR face but the value still needs glyphs beyond WinAnsi
    // (Hebrew, Cyrillic, CJK …): embed the /DA font's system face — falling
    // back to Arial — as a Type0/Identity-H font in the appearance resources
    // and show the value as CID hex. RTL values are painted in visual order.
    public byte[]? uniTtf;
    public string uniFamily = null!;
    public PdfDictionary? uniFontDict;
    public string uniRes = null!;
    public string content = null!;
    public string textBody = null!;
    public byte[] contentBytes = null!;
    // Create the appearance stream
    public Aspose.Pdf.Core.PdfStream apStream = null!;
    public Aspose.Pdf.Core.PdfArray bboxArr = null!;
    // Carry font resources into the appearance so the value text renders.
    // Prefer reusing the resources from a previously-built /AP/N (preserves any
    // embedded font); otherwise declare the /DA font as standard-14 Helvetica —
    // the renderer resolves a font only from the appearance's own resources.
    public PdfDictionary? resolvedRes;
    public Aspose.Pdf.Core.PdfDictionary? existingAp;
    // Build a fresh /AP dict. Modifying the resolved (possibly-indirect)
    // /AP dict in place leaves the writer with no signal to re-emit it on
    // incremental save — the field's own dict is dirty-tracked but the
    // separate /AP indirect object isn't. Inlining a new direct dict means
    // the field's re-serialised body carries the updated /N reference, and
    // the new appearance stream gets promoted to its own indirect object
    // by PdfWriter.WriteDictionary.
    public Aspose.Pdf.Core.PdfDictionary newApDict = null!;
    public Aspose.Pdf.Core.PdfDictionary? oldApDict;
}
}
