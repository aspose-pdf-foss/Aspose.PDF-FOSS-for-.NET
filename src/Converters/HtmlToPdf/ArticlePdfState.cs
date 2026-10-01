using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ArticlePdfState
{
    // the sheet's own pixel geometry
    public double articlePadTop;
    public double titleMaxW;
    public double titleMarB;
    public double wrapperPadX;
    public double wrapperPadB;
    public double colLeftShare;
    public double colLeftPadR;
    public double colLeftPadT;
    public double datePadTop;
    public double datePadBottom;
    public double disclaimerPad;
    // ── parse the article ──
    public string titleText = null!;
    public string logoAlt = null!;
    public string descText = null!;
    public string dateText = null!;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.ApBlock> content = null!;
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.ApBlock> footer = null!;
    public System.Text.RegularExpressions.Match logoM = null!;
    // The logo image itself, when its file resolves — through the shared
    // resolver, whose parent-directory fallback recovers the expected file
    // (a base pointing INTO the img folder doubles the folder on the naive
    // join; the parent-dir retry finds the file).
    public byte[]? logoBytes;
    public int logoNatW;
    public System.Text.RegularExpressions.Match logoSrcM = null!;
    // the left column runs to the right column's opening div
    public System.Text.RegularExpressions.Match leftM = null!;
    public System.Text.RegularExpressions.Match rightM = null!;
    public System.Text.RegularExpressions.Match footM = null!;
    // ── layout ──
    public Document doc = null!;
    public Page page = null!;
    public System.Globalization.CultureInfo invc = null!;
    public double limit;
    public double contentDrop;
    // title band: article padding-top below the margin, the band the title's
    // padded box at 65% max-width, the text wrapped inside its padding
    public double bandTop;
    public double titlePadL;
    public double titlePadR;
    public double titlePadY;
    public string[] titleLines = null!;
    public double bandH;
    public double titleDrop;
    // the absolute logo holder: the image at the holder's 220px width ×
    // natural aspect (width:100%; height:auto), right-inset by the sheet's
    // 40px, one 40px pad below the page top — or its alt text when the
    // image file does not resolve
    public double holderW;
    public double holderPadT;
    // the description column beside the first page of content — the
    // stylesheet's serif bold italic in its declared colour, wrapped on the
    // face that draws it
    public double colsTop;
    public double colLeftX;
    public double innerW;
    public double colLeftW;
    public string[] descLines = null!;
    public double descDrop;
    public int descIdx;
    public Page page1 = null!;
    // The content column: beside the description on page 1; after a page
    // break it moves to the wrapper's left edge but KEEPS the column width
    // (measured: continuation lines end at x≈399, the same 360 pt measure).
    public double col1X;
    public double col1W;
    public double fullW;
    public double yTd;
    public bool firstPage;
    public string html = null!;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = null!;
    public double marginLeft = 0;
    public double marginRight = 0;
    public double marginTop = 0;
    public double marginBottom = 0;
    public double pageWidth = 0;
    public double pageHeight = 0;
    public string? basePath = null;
    public Color descColor = null!;
    public (double asc, double sum) fm;
    public int logoNatH;
}
}
