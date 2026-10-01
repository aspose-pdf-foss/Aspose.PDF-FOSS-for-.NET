using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ListingCardState
{
    public Dictionary<string, Dictionary<string, string>> css = null!;
    public double borderW;
    public System.Text.RegularExpressions.Match bm = null!;
    public Color borderCol = null!;
    public double headerFs;
    public Color headerBg = null!;
    public Color evenBg = null!;
    public Color oddBg = null!;
    // ── parse: the header text and the item rows (icon svg + text) ──
    public System.Text.RegularExpressions.Match headM = null!;
    public string headHtml = null!;
    public System.Text.RegularExpressions.Match folderM = null!;
    public string folderText = null!;
    public string headText = null!;
    public List<(string SvgXml, string Text)> items = null!;
    // ── layout ──
    public Document doc = null!;
    public Page page = null!;
    public System.Globalization.CultureInfo invc = null!;
    public double contentW;
    public double cardW;
    public double contentX;
    // the header's line box paces on the face's hhea line (34px at 22pt)
    public double headLineSum;
    public double headLineH;
    public double headerH;
    public double cardH;
    // rounded card border: four edges joined by quarter-turn beziers, the
    // stroke centred half a border inside the outer box
    public double k;
    public double x0;
    public double y0;
    public double x1;
    public double y1;
    public System.Text.StringBuilder sbr = null!;
    // header band over the content box, its text at the header padding
    public double headTop;
    public double headDrop;
    public double headBase;
    public double headW;
    // item rows: alternating fills, the floated icon, the row-centred text
    public double rowTop;
    // the row's line box centres the text on the 48px line-height
    public double itemDrop;
    public string html = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
    // the stylesheet geometry the frame, header and item stages share
    public double radius;
    public double pad;
    public double itemFs;
    public double headerPad;
    public double folderFs;
    public double itemH;
    public double contPct;
    public (double asc, double sum) fm;
}
}
