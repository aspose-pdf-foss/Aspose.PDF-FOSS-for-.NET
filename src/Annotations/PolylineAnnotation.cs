using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>A polyline annotation: an open line through a list of vertices.</summary>
public partial class PolylineAnnotation : PolyAnnotation
{
    internal PolylineAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Creates a polyline annotation on <c>page</c> inside <c>rect</c> with the given vertices (page coordinates in points).</summary>
    public PolylineAnnotation(Page page, Rectangle rect, Point[] vertices) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("PolyLine"));
        Vertices = vertices;
    }

    public new AnnotationType AnnotationType => AnnotationType.PolyLine;

    /// <summary>Border with width, style and dash pattern resolved from /BS.</summary>
    public new Border? Border
    {
        get
        {
            var border = new Border(this);
            var bs = InternalReader.ResolveDict(Dict.Get("BS"));
            if (bs is not null)
            {
                var w = InternalReader.Resolve(bs.Get("W"));
                if (w is PdfInteger wi) border.Width = (int)wi.Value;
                else if (w is PdfReal wr) border.Width = (int)wr.Value;
                border.Style = bs.GetName("S") switch
                {
                    "D" => BorderStyle.Dashed,
                    "B" => BorderStyle.Beveled,
                    "I" => BorderStyle.Inset,
                    "U" => BorderStyle.Underline,
                    _ => BorderStyle.Solid,
                };
                if (InternalReader.Resolve(bs.Get("D")) is PdfArray d && d.Count > 0)
                {
                    int on = d[0] switch { PdfInteger di => (int)di.Value, PdfReal dr => (int)dr.Value, _ => 0 };
                    int off;
                    if (d.Count > 1)
                    {
                        if (d[1] is PdfInteger oi) off = (int)oi.Value;
                        else if (d[1] is PdfReal orr) off = (int)orr.Value;
                        else off = on;
                    }
                    else off = on;
                    border.Dash = new Dash(on, off);
                }
            }
            return border;
        }
        set => base.Border = value;
    }
}
