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
private sealed class PuaSymbolicConvertState
{
    public Aspose.Pdf.Core.PdfDictionary? resources;
    public Aspose.Pdf.Core.PdfDictionary? fonts;
    public byte[]? contentBytes;
    public string content = null!;
    public int converted;
    public Page page = default!;
    public Aspose.Pdf.Core.PdfDictionary? fontDict;
    public Aspose.Pdf.Core.PdfDictionary? descriptor;
    public Aspose.Pdf.Core.PdfStream? ff2;
    public long flags;
    // The program's cmap decides whether the used codes are PUA-routed.
    public Dictionary<int, int> cmap = null!;
    // Find this font's Tf selections and rewrite the shows that follow them.
    public System.Text.RegularExpressions.Regex tfPattern = null!;
    public int firstChar;
    public Aspose.Pdf.Core.PdfArray? widths;
    public SortedDictionary<int, double> usedGidWidths = null!;
    public bool anyPua;
    public string newKey = null!;
    public System.Text.StringBuilder rewritten = null!;
    public int pos;
    // Companion Type0 font: descendant shares the embedded program via the
    // SAME descriptor, CIDs are the program's glyph ids (CIDToGIDMap Identity).
    public Aspose.Pdf.Core.PdfDictionary cidSystemInfo = null!;
    public Aspose.Pdf.Core.PdfArray wArr = null!;
    public string baseFontName = null!;
    public Aspose.Pdf.Core.PdfDictionary cidFont = null!;
    public Aspose.Pdf.Core.PdfDictionary type0 = null!;
    public Aspose.Pdf.Core.PdfArray descendants = null!;
}
}
