using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class PolylineAnnotation : PolyAnnotation
{
    internal PolylineAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

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
                    int on = d[0] is PdfInteger di ? (int)di.Value : d[0] is PdfReal dr ? (int)dr.Value : 0;
                    int off = d.Count > 1 ? (d[1] is PdfInteger oi ? (int)oi.Value : d[1] is PdfReal orr ? (int)orr.Value : on) : on;
                    border.Dash = new Dash(on, off);
                }
            }
            return border;
        }
        set => base.Border = value;
    }
}
