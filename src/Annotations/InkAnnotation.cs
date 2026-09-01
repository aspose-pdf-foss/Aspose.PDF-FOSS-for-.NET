using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class InkAnnotation : MarkupAnnotation
{
    internal InkAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Construct a page-bound ink annotation with the given stroke paths.</summary>
    public InkAnnotation(Page page, Rectangle rect, IList<Point[]> inkList)
        : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Ink"));
        WriteInkList(inkList);
    }

    /// <summary>Construct a document-bound ink annotation; rectangle is derived from the points.</summary>
    public InkAnnotation(Document document, IList<Point[]> inkList)
        : base(document, RectFromInkList(inkList))
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Ink"));
        WriteInkList(inkList);
    }

    /// <summary>Legacy non-generic overload accepting an <see cref="System.Collections.IList"/> of <see cref="Point"/>[].</summary>
    public InkAnnotation(Page page, Rectangle rect, System.Collections.IList inkList)
        : this(page, rect, ToGenericInkList(inkList))
    {
    }

    /// <summary>Always <see cref="AnnotationType.Ink"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.Ink;

    /// <summary>Stroke cap style; stored only.</summary>
    public CapStyle CapStyle { get; set; } = CapStyle.Rectangular;

    /// <summary>The /InkList entry: each inner array is one stroke path.</summary>
    public IList<Point[]> InkList
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("InkList")) as PdfArray;
            var result = new List<Point[]>();
            if (arr is null) return result;
            foreach (var item in arr)
            {
                if (InternalReader.Resolve(item) is not PdfArray pts) continue;
                var stroke = new Point[pts.Count / 2];
                for (int i = 0; i + 1 < pts.Count; i += 2)
                    stroke[i / 2] = new Point(GetN(pts[i]), GetN(pts[i + 1]));
                result.Add(stroke);
            }
            return result;
        }
        set => WriteInkList(value);
    }

    /// <summary>Transform every ink point and refresh the bounding rectangle.</summary>
    public new void ChangeAfterResize(Matrix transform)
    {
        if (transform is null) return;
        var strokes = InkList;
        var transformed = new List<Point[]>(strokes.Count);
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var stroke in strokes)
        {
            var newStroke = new Point[stroke.Length];
            for (int i = 0; i < stroke.Length; i++)
            {
                transform.Transform(stroke[i].X, stroke[i].Y, out var nx, out var ny);
                newStroke[i] = new Point(nx, ny);
                if (nx < minX) minX = nx;
                if (ny < minY) minY = ny;
                if (nx > maxX) maxX = nx;
                if (ny > maxY) maxY = ny;
            }
            transformed.Add(newStroke);
        }
        WriteInkList(transformed);
        if (transformed.Count > 0)
            Rect = new Rectangle(minX, minY, maxX, maxY);
    }

    /// <summary>Regenerate the normal appearance (/AP /N) by stroking every
    /// /InkList path with the annotation colour and border width. The
    /// appearance BBox (and /Rect) inflate by half the stroke width so a thick
    /// stroke and its round caps are not clipped at the path extremes.</summary>
    public override void UpdateAppearances()
    {
        var strokes = InkList;
        if (strokes.Count == 0) return;
        double lw = GetBorderWidthValue();
        if (lw <= 0) lw = 1;

        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(Color);
        b.SetLineWidth(lw);
        if (CapStyle == CapStyle.Rounded) { b.SetLineCap(1); b.SetLineJoin(1); }
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var stroke in strokes)
        {
            if (stroke.Length == 0) continue;
            b.MoveTo(stroke[0].X, stroke[0].Y);
            for (var i = 1; i < stroke.Length; i++) b.LineTo(stroke[i].X, stroke[i].Y);
            b.Stroke();
            foreach (var p in stroke)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
        }
        b.RestoreState();
        if (double.IsInfinity(minX)) return;

        var half = lw / 2.0;
        var bbox = new Rectangle(minX - half, minY - half, maxX + half, maxY + half);
        var rArr = new PdfArray();
        rArr.Add(new PdfReal(bbox.LLX)); rArr.Add(new PdfReal(bbox.LLY));
        rArr.Add(new PdfReal(bbox.URX)); rArr.Add(new PdfReal(bbox.URY));
        Dict.Set("Rect", rArr);
        SetNormalAppearance(b.Build(), bbox);
    }

    private void WriteInkList(IList<Point[]> inkList)
    {
        var outer = new PdfArray();
        if (inkList is not null)
        {
            foreach (var stroke in inkList)
            {
                if (stroke is null) continue;
                var inner = new PdfArray();
                foreach (var p in stroke)
                {
                    inner.Add(new PdfReal(p.X));
                    inner.Add(new PdfReal(p.Y));
                }
                outer.Add(inner);
            }
        }
        Dict.Set("InkList", outer);
    }

    private static Rectangle RectFromInkList(IList<Point[]> inkList)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        bool any = false;
        if (inkList is not null)
        {
            foreach (var stroke in inkList)
            {
                if (stroke is null) continue;
                foreach (var p in stroke)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                    any = true;
                }
            }
        }
        return any ? new Rectangle(minX, minY, maxX, maxY) : new Rectangle(0, 0, 0, 0);
    }

    private static IList<Point[]> ToGenericInkList(System.Collections.IList inkList)
    {
        var result = new List<Point[]>();
        if (inkList is null) return result;
        foreach (var item in inkList)
        {
            if (item is Point[] arr) result.Add(arr);
        }
        return result;
    }

    private static double GetN(PdfObject? obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };
}
