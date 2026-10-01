using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CaseLetterState
{
    // The dialect's signature: the class rule declares all four side longhands.
    public System.Text.RegularExpressions.Match clsRule = null!;
    public System.Globalization.CultureInfo inv = null!;
    // ---- parse -------------------------------------------------------------
    public System.Text.RegularExpressions.Match headM = null!;
    public string headBold = null!;
    public string headRest = null!;
    // The address table: first padded td's <br>-split lines, second td's date.
    public System.Text.RegularExpressions.Match addrM = null!;
    public List<string> addrLines = null!;
    public string dateText = null!;
    // Prose/bullets and the case tables, in document order.
    public System.Text.RegularExpressions.Match reqM = null!;
    public string requestText = null!;
    public System.Text.RegularExpressions.Match paraM = null!;
    public string paraText = null!;
    public System.Text.RegularExpressions.Match pleaseM = null!;
    public string pleaseText = null!;
    // Level-1 items with their optional nested list.
    public List<(string Text, List<string> Nested)> bullets = null!;
    public System.Text.RegularExpressions.Match ulM = null!;
    // Case tables: every outer 630px table holding a nested .blackBorder table.
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.ClCaseTable> caseTables = null!;
    public List<int> breakPositions = null!;
    // The closing paragraph after the last case table (last page).
    public System.Text.RegularExpressions.Match closeM = null!;
    public string closeText = null!;
    // ---- layout + draw -----------------------------------------------------
    public Document doc = null!;
    public Page page = null!;
    public Dictionary<string, string> resByFace = null!;
    public System.Text.StringBuilder strokes = null!;
    public double contentX;
    public double proseBox;
    public double y;
    // Case tables.
    public double caseTop;
    public double innerBox;
    public bool firstOnPage;
    // Resolve columns: declared shares, min-content overrides, slack shrink.
    public int nCols;
    public double[] shares = null!;
    public double[] mins = null!;
    public double need;
    public double slack;
    public double[] widths = null!;
    // Wrap the cells and take the row heights.
    public double[] rowHs = null!;
    public double tableH;
    // Grid + cells.
    public double rowTop;
}
}
