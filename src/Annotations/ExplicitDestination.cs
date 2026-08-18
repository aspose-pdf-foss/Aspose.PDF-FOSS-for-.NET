using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Represents an explicit destination in a PDF document (e.g. [page /Fit], [page /XYZ left top zoom]).
/// </summary>
public class ExplicitDestination : IAppointment
{
    /// <summary>Target page number (1-based).</summary>
    public int PageNumber { get; internal set; }

    /// <summary>The destination type name (Fit, XYZ, FitH, etc).</summary>
    public string Type { get; }

    /// <summary>The target page (API parity). Populated when resolvable.</summary>
    public Page? Page { get; internal set; }

    internal ExplicitDestination(int pageNumber, string type)
    {
        PageNumber = pageNumber;
        Type = type;
    }

    /// <summary>Try to parse a destination from a PDF array.</summary>
    internal static ExplicitDestination? FromArray(PdfArray arr, PdfReader? reader)
    {
        if (arr.Count < 2) return null;

        // Preserve the indirect-ref form so we can look up the page's 1-based
        // index from its object number (PDF writers often store the page as an
        // indirect reference to the page dict rather than an integer index).
        int? pageObjNum = arr[0] is PdfIndirectRef iref ? iref.ObjectNumber : null;
        var pageObj = reader is null ? arr[0] : reader.Resolve(arr[0]);
        int pageNum = 0;

        if (pageObj is PdfInteger pi)
        {
            pageNum = (int)pi.Value + 1;
        }
        else if (pageObj is PdfDictionary pageDict && reader is not null && pageObjNum.HasValue)
        {
            // Walk the page tree to find the 1-based index of this page dict.
            pageNum = ResolvePageIndex(reader, pageObjNum.Value);
        }

        var typeObj = arr[1];
        string typeName = typeObj is PdfName name ? name.Value : "Unknown";

        // PDF 32000 §12.3.2.2 Explicit Destinations: return the concrete
        // subclass for fit types that have them so tests can cast to e.g.
        // FitExplicitDestination.
        var dest = typeName switch
        {
            "Fit" => new FitExplicitDestination(pageNum),
            "FitB" => new FitBExplicitDestination(pageNum),
            "FitH" => new FitHExplicitDestination(pageNum, ReadReal(arr, 2)),
            "FitV" => new FitVExplicitDestination(pageNum, ReadReal(arr, 2)),
            "FitBH" => new FitBHExplicitDestination(pageNum, ReadReal(arr, 2)),
            "FitBV" => new FitBVExplicitDestination(pageNum, ReadReal(arr, 2)),
            "XYZ" => new XYZExplicitDestination(pageNum,
                ReadReal(arr, 2) ?? 0, ReadReal(arr, 3) ?? 0, ReadReal(arr, 4) ?? 0),
            "FitR" => new FitRExplicitDestination(pageNum,
                ReadReal(arr, 2) ?? 0, ReadReal(arr, 3) ?? 0,
                ReadReal(arr, 4) ?? 0, ReadReal(arr, 5) ?? 0),
            _ => new ExplicitDestination(pageNum, typeName),
        };
        // Materialise the target Page from the owner document's page list so
        // the destination tracks the page OBJECT: callers that re-anchor the
        // page (e.g. inserting a TOC page ahead of it) see its current number,
        // and identity look-ups against Pages succeed. Matched by page-dict
        // identity — the raw-tree index and the live collection index diverge
        // once pages have been inserted.
        if (reader?.OwnerDocument is { } ownerDoc && pageObj is PdfDictionary destPageDict)
        {
            foreach (var p in ownerDoc.Pages)
                if (ReferenceEquals(p.Dict, destPageDict))
                {
                    dest.Page = p;
                    break;
                }
            if (dest.Page is null && pageNum >= 1 && pageNum <= ownerDoc.Pages.Count)
                dest.Page = ownerDoc.Pages[pageNum];
        }
        return dest;
    }

    private static double? ReadReal(PdfArray arr, int index)
    {
        if (index >= arr.Count) return null;
        return arr[index] switch
        {
            PdfReal r => r.Value,
            PdfInteger i => i.Value,
            _ => null,
        };
    }

    /// <summary>Walk the page tree to find the 1-based index of the page with this object number.</summary>
    private static int ResolvePageIndex(PdfReader reader, int targetObjNum)
    {
        var pagesDict = reader.ResolveDict(reader.Catalog.Get("Pages"));
        if (pagesDict is null) return 0;
        int counter = 0;
        return FindPage(reader, pagesDict, targetObjNum, ref counter) ? counter : 0;
    }

    private static bool FindPage(PdfReader reader, PdfDictionary node, int targetObjNum, ref int counter)
    {
        var kids = reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is null) return false;
        foreach (var kid in kids)
        {
            if (kid is PdfIndirectRef kidRef)
            {
                var kidDict = reader.ResolveDict(kid);
                if (kidDict is null) continue;
                var type = kidDict.GetName("Type");
                if (type == "Page")
                {
                    counter++;
                    if (kidRef.ObjectNumber == targetObjNum) return true;
                }
                else if (type == "Pages")
                {
                    if (FindPage(reader, kidDict, targetObjNum, ref counter)) return true;
                }
            }
        }
        return false;
    }

    /// <summary>Serialize this destination to a PDF array for writing.</summary>
    internal virtual PdfArray ToPdfArray()
    {
        var arr = new PdfArray();
        arr.Add(new PdfInteger(PageNumber - 1)); // 0-based page index
        arr.Add(new PdfName(Type));
        return arr;
    }

    internal PdfArray ToPdfArrayPublic() => ToPdfArray();

    /// <inheritdoc />
    public override string ToString() => $"{PageNumber} {Type}";

    /// <summary>Factory: create the concrete destination subclass for <paramref name="type"/> on <paramref name="pageNumber"/>.</summary>
    public static ExplicitDestination CreateDestination(int pageNumber, ExplicitDestinationType type, double[] values)
        => Build(pageNumber, type, values);

    /// <summary>Factory: create the destination on <paramref name="page"/>.</summary>
    public static ExplicitDestination CreateDestination(Page page, ExplicitDestinationType type, double[] values)
        => Build(page?.Number ?? 1, type, values);

    /// <summary>Factory: create the destination on <paramref name="pageNumber"/> within <paramref name="doc"/>.</summary>
    public static ExplicitDestination CreateDestination(Document doc, int pageNumber, ExplicitDestinationType type, double[] values)
        => Build(pageNumber, type, values);

    private static ExplicitDestination Build(int pageNumber, ExplicitDestinationType type, double[] values)
    {
        values ??= Array.Empty<double>();
        double V(int i) => i < values.Length ? values[i] : 0;
        return type switch
        {
            ExplicitDestinationType.Fit  => new FitExplicitDestination(pageNumber),
            ExplicitDestinationType.FitB => new FitBExplicitDestination(pageNumber),
            ExplicitDestinationType.FitH => new FitHExplicitDestination(pageNumber, V(0)),
            ExplicitDestinationType.FitV => new FitVExplicitDestination(pageNumber, V(0)),
            ExplicitDestinationType.FitBH => new FitBHExplicitDestination(pageNumber, V(0)),
            ExplicitDestinationType.FitBV => new FitBVExplicitDestination(pageNumber, V(0)),
            ExplicitDestinationType.FitR => new FitRExplicitDestination(pageNumber, V(0), V(1), V(2), V(3)),
            ExplicitDestinationType.XYZ => new XYZExplicitDestination(pageNumber, V(0), V(1), V(2)),
            _ => new ExplicitDestination(pageNumber, type.ToString()),
        };
    }
}

/// <summary>Explicit destination type, mirroring PDF 32000 §12.3.2.2 names.</summary>
public enum ExplicitDestinationType
{
    Fit = 0,
    FitB = 1,
    FitH = 2,
    FitV = 3,
    FitBH = 4,
    FitBV = 5,
    FitR = 6,
    XYZ = 7,
}

/// <summary>
/// XYZ explicit destination: display the page at position (left, top) with zoom factor.
/// A value of 0 for any parameter means "retain the current viewer value".
/// </summary>
public class XYZExplicitDestination : ExplicitDestination
{
    /// <summary>Left coordinate (0 = unchanged).</summary>
    public double Left { get; }

    /// <summary>Top coordinate (0 = unchanged).</summary>
    public double Top { get; }

    /// <summary>Zoom factor (0 = unchanged).</summary>
    public double Zoom { get; }

    private readonly Page? _page;

    /// <summary>Create an XYZ destination to a specific page with coordinates.</summary>
    public XYZExplicitDestination(Page page, double left, double top, double zoom)
        : base(page?.Number ?? 1, "XYZ")
    {
        _page = page;
        Left = left;
        Top = top;
        Zoom = zoom;
    }

    /// <summary>Create an XYZ destination by page number.</summary>
    public XYZExplicitDestination(int pageNumber, double left, double top, double zoom)
        : base(pageNumber, "XYZ")
    {
        Left = left;
        Top = top;
        Zoom = zoom;
    }

    /// <summary>Create an XYZ destination for a page number in the given document (API parity).</summary>
    public XYZExplicitDestination(Document document, int pageNumber, double left, double top, double zoom)
        : base(pageNumber, "XYZ")
    {
        Left = left;
        Top = top;
        Zoom = zoom;
    }

    internal override PdfArray ToPdfArray()
    {
        var arr = new PdfArray();
        arr.Add(new PdfInteger(PageNumber - 1)); // 0-based
        arr.Add(new PdfName("XYZ"));
        // A value of 0 in the public API means "retain current viewer value", which
        // the PDF spec encodes as a null literal in the destination array.
        arr.Add(Left != 0 ? (PdfObject)new PdfReal(Left) : PdfNull.Instance);
        arr.Add(Top != 0 ? (PdfObject)new PdfReal(Top) : PdfNull.Instance);
        arr.Add(Zoom != 0 ? (PdfObject)new PdfReal(Zoom) : PdfNull.Instance);
        return arr;
    }

    public override string ToString() => $"{PageNumber} XYZ {Left} {Top} {Zoom}";

    /// <summary>Build an XYZ destination pointing at the upper-left corner of <paramref name="page"/>.</summary>
    public static XYZExplicitDestination CreateDestinationToUpperLeftCorner(Page page)
        => new(page, 0, page?.MediaBox?.URY ?? 0, 0);

    /// <summary>Upper-left destination with an explicit zoom factor.</summary>
    public static XYZExplicitDestination CreateDestinationToUpperLeftCorner(Page page, double zoom)
        => new(page, 0, page?.MediaBox?.URY ?? 0, zoom);

    /// <summary>Build an XYZ destination. <paramref name="considerRotation"/> swaps Left/Top when the page is rotated 90° or 270°.</summary>
    public static XYZExplicitDestination CreateDestination(Page page, double left, double top, double zoom, bool considerRotation)
    {
        if (considerRotation && page is not null)
        {
            var rot = ((page.RotateDegrees % 360) + 360) % 360;
            if (rot == 90 || rot == 270) (left, top) = (top, left);
        }
        return page is null
            ? new XYZExplicitDestination(1, left, top, zoom)
            : new XYZExplicitDestination(page, left, top, zoom);
    }
}

/// <summary>Fit-the-page destination (/Fit).</summary>
public class FitExplicitDestination : ExplicitDestination
{
    public FitExplicitDestination(Page page) : base(page?.Number ?? 1, "Fit") { Page = page; }
    public FitExplicitDestination(int pageNumber) : base(pageNumber, "Fit") { }
    public FitExplicitDestination(Document document, int pageNumber) : base(pageNumber, "Fit") { }
    public override string ToString() => $"{PageNumber} Fit";
}

/// <summary>Fit-bounding-box destination (/FitB).</summary>
public class FitBExplicitDestination : ExplicitDestination
{
    public FitBExplicitDestination(Page page) : base(page?.Number ?? 1, "FitB") { Page = page; }
    public FitBExplicitDestination(int pageNumber) : base(pageNumber, "FitB") { }
    public FitBExplicitDestination(Document document, int pageNumber) : base(pageNumber, "FitB") { }
    public override string ToString() => $"{PageNumber} FitB";
}

/// <summary>Fit horizontally at /Top (/FitH).</summary>
public class FitHExplicitDestination : ExplicitDestination
{
    public double Top { get; }
    public FitHExplicitDestination(Page page, double top) : base(page?.Number ?? 1, "FitH") { Page = page; Top = top; }
    public FitHExplicitDestination(int pageNumber, double top) : base(pageNumber, "FitH") { Top = top; }
    public FitHExplicitDestination(Document document, int pageNumber, double top)
        : base(pageNumber, "FitH") { Top = top; }
    internal FitHExplicitDestination(int pageNumber, double? top) : base(pageNumber, "FitH") { Top = top ?? 0; }
    internal override PdfArray ToPdfArray()
    {
        var arr = new PdfArray();
        arr.Add(new PdfInteger(PageNumber - 1));
        arr.Add(new PdfName("FitH"));
        arr.Add(new PdfReal(Top));
        return arr;
    }
    public override string ToString() => $"{PageNumber} FitH {Top}";
}

/// <summary>Fit vertically at /Left (/FitV).</summary>
public class FitVExplicitDestination : ExplicitDestination
{
    public double Left { get; }
    public FitVExplicitDestination(Page page, double left) : base(page?.Number ?? 1, "FitV") { Page = page; Left = left; }
    public FitVExplicitDestination(int pageNumber, double left) : base(pageNumber, "FitV") { Left = left; }
    public FitVExplicitDestination(Document document, int pageNumber, double left)
        : base(pageNumber, "FitV") { Left = left; }
    internal FitVExplicitDestination(int pageNumber, double? left) : base(pageNumber, "FitV") { Left = left ?? 0; }
    internal override PdfArray ToPdfArray()
    {
        var arr = new PdfArray();
        arr.Add(new PdfInteger(PageNumber - 1));
        arr.Add(new PdfName("FitV"));
        arr.Add(new PdfReal(Left));
        return arr;
    }
    public override string ToString() => $"{PageNumber} FitV {Left}";
}

/// <summary>FitBH destination (/FitBH) — fit bounding box horizontally at Top.</summary>
public class FitBHExplicitDestination : ExplicitDestination
{
    public double Top { get; }
    public FitBHExplicitDestination(Page page, double top) : base(page?.Number ?? 1, "FitBH") { Page = page; Top = top; }
    public FitBHExplicitDestination(int pageNumber, double top) : base(pageNumber, "FitBH") { Top = top; }
    public FitBHExplicitDestination(Document document, int pageNumber, double top)
        : base(pageNumber, "FitBH") { Top = top; }
    internal FitBHExplicitDestination(int pageNumber, double? top) : base(pageNumber, "FitBH") { Top = top ?? 0; }
    public override string ToString() => $"{PageNumber} FitBH {Top}";
}

/// <summary>FitBV destination (/FitBV) — fit bounding box vertically at Left.</summary>
public class FitBVExplicitDestination : ExplicitDestination
{
    public double Left { get; }
    public FitBVExplicitDestination(Page page, double left) : base(page?.Number ?? 1, "FitBV") { Page = page; Left = left; }
    public FitBVExplicitDestination(int pageNumber, double left) : base(pageNumber, "FitBV") { Left = left; }
    public FitBVExplicitDestination(Document document, int pageNumber, double left)
        : base(pageNumber, "FitBV") { Left = left; }
    internal FitBVExplicitDestination(int pageNumber, double? left) : base(pageNumber, "FitBV") { Left = left ?? 0; }
    public override string ToString() => $"{PageNumber} FitBV {Left}";
}

/// <summary>FitR rectangle destination (/FitR).</summary>
public class FitRExplicitDestination : ExplicitDestination
{
    public double Left { get; }
    public double Bottom { get; }
    public double Right { get; }
    public double Top { get; }
    public FitRExplicitDestination(Page page, double left, double bottom, double right, double top)
        : base(page?.Number ?? 1, "FitR")
    {
        Page = page; Left = left; Bottom = bottom; Right = right; Top = top;
    }
    public FitRExplicitDestination(int pageNumber, double left, double bottom, double right, double top)
        : base(pageNumber, "FitR")
    {
        Left = left; Bottom = bottom; Right = right; Top = top;
    }
    public FitRExplicitDestination(Document document, int pageNumber, double left, double bottom, double right, double top)
        : base(pageNumber, "FitR")
    {
        Left = left; Bottom = bottom; Right = right; Top = top;
    }
    internal override PdfArray ToPdfArray()
    {
        var arr = new PdfArray();
        arr.Add(new PdfInteger(PageNumber - 1));
        arr.Add(new PdfName("FitR"));
        arr.Add(new PdfReal(Left));
        arr.Add(new PdfReal(Bottom));
        arr.Add(new PdfReal(Right));
        arr.Add(new PdfReal(Top));
        return arr;
    }
    public override string ToString() => $"{PageNumber} FitR {Left} {Bottom} {Right} {Top}";
}
