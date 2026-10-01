using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ExternalDivState
{
    public Page page = null!;
    public IO.PdfReader reader = null!;
    public bool preferFontCmap;
    // DefaultFontName forces the emitted class family (and, with it, the embedded
    // @font-face) onto the requested face — but only when fonts are actually
    // saved. With FontSavingMode.DontSave nothing is embedded, so the class must
    // keep each source font's own (friendly) family name for the viewer to match;
    // the substitute is then not applied.
    public bool fontsNotSaved;
    public string? effectiveDefaultFont;
    public Dictionary<string, Aspose.Pdf.Converters.PdfToHtmlConverter.HtmlFontRecord> fonts = null!;
    public Dictionary<string, Aspose.Pdf.ImageXObject> imageXObjects = null!;
    public Aspose.Pdf.Core.PdfDictionary? pageResources;
    // A page whose /Rotate is 180 is presented upside down: the whole text
    // layer is turned over with a zero-sized rotation box and every line is
    // placed in the turned frame, one page box left and one page box up. The
    // quarter turns are left alone — they resize the page box instead, and
    // no rotated text is emitted for them at all.
    public bool pageTurnedOver;
    public System.Text.StringBuilder textBuf = null!;
    public System.Text.StringBuilder svgPaths = null!;
    public Converters.PdfToHtmlConverter.DestAnchorRegistry destAnchors = null!;
    public List<Aspose.Pdf.Converters.PdfToHtmlConverter.LinkTarget>? linkTargets;
    // Fixed-layout geometry references: x from the MediaBox left edge, page
    // top from LLY + floor(height). UseZOrder gets a fresh per-page counter.
    public Rectangle mb = null!;
    public Converters.PdfToHtmlConverter.ZCounter? zCounter;
    public byte[] content = null!;
    // The dynamic class numbering must be pinned BEFORE the text render issues
    // its first font class: a page with a backdrop wrapper numbers the text
    // layer 05/06 (dynamic from 07); a backdrop-less page numbers it 03/04
    // (fonts from 05). The wrapper's existence in the SVG-graphics mode is only
    // certain after rendering, so predict it from the content stream's paint
    // operators — over-predicting is harmless (it just keeps the 07 base).
    public bool pageHasPaint;
    // The background raster exists to carry what the text layer cannot — images,
    // fills, strokes, shadings. A page that paints nothing else needs no backdrop:
    // the self-contained save would embed a blank white raster (the text is
    // suppressed there), and the sidecar save would re-paint, as pixels, the very
    // text it also emits as selectable spans.
    public bool emitPngBackground;
    public bool hasBackdrop;
    // Text layer classes come from the document-wide counter: after a backdrop
    // wrapper (which took 03/04) the layer is 05/06 and dynamic classes start at
    // 07; with no backdrop the layer itself is 03/04 and fonts start at 05
    // (the backdrop-less numbering, pinned before the render).
    public string layerCls = null!;
    public Document doc = default!;
    public int i = 0;
    public StringBuilder sb = default!;
    public ClassNamer namer = default!;
    public StyleRegistry styleReg = default!;
    public ExternalImageSink imageSink = default!;
    public List<SidecarFile> sidecars = default!;
    public string imagesUrl = default!;
    public bool pngBackground = false;
    public int htmlPageNumber = 0;
    public HtmlSaveOptions? options = null;
    public bool dispatchPngBackground = false;
    public bool embedResources = false;
    public bool inlineSvg = false;
}
}
