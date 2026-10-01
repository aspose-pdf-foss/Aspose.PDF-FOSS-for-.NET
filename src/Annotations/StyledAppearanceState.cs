using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class FreeTextAnnotation
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StyledAppearanceState
{
    public Rectangle? rect;
    public string? text;
    public Annotations.DefaultAppearance da = null!;
    public string baseFont = null!;
    public double size;
    public System.Drawing.Color color;
    public double border;
    public double inset;
    public double w;
    public double h;
    public double avail;
    public double leading;
    public Annotations.RichTextFontStyles[] styles = null!;
    // Per-variant font dict + metrics cache.
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary> fontDicts = null!;
    public Dictionary<string, Aspose.Pdf.Text.FontMetrics> metricsCache = null!;
    // Tokenise into words / spaces / newlines (carrying each char's style), then greedily
    // wrap into output lines no wider than `avail`.
    public List<List<(char ch, Aspose.Pdf.Annotations.RichTextFontStyles st)>> outLines = null!;
    public List<(char, Aspose.Pdf.Annotations.RichTextFontStyles)> line = null!;
    public double lineW;
    public List<(char ch, Aspose.Pdf.Annotations.RichTextFontStyles st)> word = null!;
    public double wordW;
    public System.Globalization.CultureInfo ci = null!;
    public System.Text.StringBuilder sb = null!;
    public List<(double x, double y, double len)> underlines = null!;
    public double y0;
    public Aspose.Pdf.Core.PdfStream apStream = null!;
    public Aspose.Pdf.Core.PdfArray bbox = null!;
    public Aspose.Pdf.Core.PdfDictionary fonts = null!;
    public Aspose.Pdf.Core.PdfDictionary res = null!;
    public Aspose.Pdf.Core.PdfDictionary ap = null!;
}
}
