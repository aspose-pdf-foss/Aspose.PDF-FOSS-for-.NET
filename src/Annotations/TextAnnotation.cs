using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class TextAnnotation : MarkupAnnotation
{
    internal TextAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Programmatic ctor — creates a /Text (sticky note) annotation
    /// at <paramref name="rect"/> on <paramref name="page"/>.</summary>
    public TextAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Text"));
    }

    public bool Open
    {
        get => Dict.Get("Open") is PdfBoolean b ? b.Value : Dict.GetInt("Open") != 0;
        set => Dict.Set("Open", (value ? PdfBoolean.True : PdfBoolean.False));
    }

    /// <summary>Review / marked state of this text annotation
    /// (/State entry per PDF 32000 §12.5.6.3). The base annotation
    /// exposes /State as a string via <see cref="AnnotationState"/>;
    /// this typed enum surface complements it.</summary>
    public AnnotationState State
    {
        get
        {
            return AnnotationState switch
            {
                "Marked" => Aspose.Pdf.Annotations.AnnotationState.Marked,
                "Unmarked" => Aspose.Pdf.Annotations.AnnotationState.Unmarked,
                "Accepted" => Aspose.Pdf.Annotations.AnnotationState.Accepted,
                "Rejected" => Aspose.Pdf.Annotations.AnnotationState.Rejected,
                "Cancelled" => Aspose.Pdf.Annotations.AnnotationState.Cancelled,
                "Completed" => Aspose.Pdf.Annotations.AnnotationState.Completed,
                "None" => Aspose.Pdf.Annotations.AnnotationState.None,
                _ => Aspose.Pdf.Annotations.AnnotationState.None,
            };
        }
        set
        {
            if (value == Aspose.Pdf.Annotations.AnnotationState.None) Dict.Remove("State");
            else Dict.Set("State", new PdfName(value.ToString()));
        }
    }

    /// <summary>Review-state model of this text annotation
    /// (/StateModel entry per PDF 32000 §12.5.6.3).</summary>
    public AnnotationStateModel StateModel
    {
        get
        {
            return AnnotationStateModel switch
            {
                "Marked" => Aspose.Pdf.Annotations.AnnotationStateModel.Marked,
                "Review" => Aspose.Pdf.Annotations.AnnotationStateModel.Review,
                _ => Aspose.Pdf.Annotations.AnnotationStateModel.Undefined,
            };
        }
        set
        {
            if (value == Aspose.Pdf.Annotations.AnnotationStateModel.Undefined) Dict.Remove("StateModel");
            else Dict.Set("StateModel", new PdfName(value.ToString()));
        }
    }

    /// <summary>The icon shown for the closed sticky note (/Name entry).</summary>
    public TextIcon Icon
    {
        get
        {
            var n = Dict.GetName("Name");
            return n switch
            {
                "Comment" => TextIcon.Comment,
                "Key" => TextIcon.Key,
                "Note" => TextIcon.Note,
                "Help" => TextIcon.Help,
                "NewParagraph" => TextIcon.NewParagraph,
                "Paragraph" => TextIcon.Paragraph,
                "Insert" => TextIcon.Insert,
                "Check" => TextIcon.Check,
                "Circle" => TextIcon.Circle,
                "Cross" => TextIcon.Cross,
                "Star" => TextIcon.Star,
                _ => TextIcon.Note,
            };
        }
        set => Dict.Set("Name", new PdfName(value.ToString()));
    }

    /// <summary>Generate the normal appearance (/AP /N) for the note icon
    /// (PDF 32000 §12.5.6.4) so the annotation renders and flattens. Uses the
    /// standard icon path for the current /Name; /C fills the icon body while
    /// outlines stay black.</summary>
    public override void UpdateAppearances()
    {
        var rect = Rect;
        if (rect is null) return;
        var c = Color;
        var content = TextAnnotationIcons.ContentFor(
            Dict.GetName("Name") ?? "Note",
            c is null ? null : (c.R / 255.0, c.G / 255.0, c.B / 255.0));

        // The note icon is a FIXED-size glyph: every icon stream is authored at
        // its natural ~18–20 pt in the box's lower-left corner, and the
        // reference draws the SAME ops into a 24 pt sticky note and a 100 pt
        // comment rect alike (probed op-for-op — its /AP BBox equals the RECT,
        // so placement never scales). A rect smaller than the natural icon
        // shrinks it to fit.
        var w = rect.URX - rect.LLX;
        var h = rect.URY - rect.LLY;
        const double iconPt = 20.0;   // the streams' natural extent
        string wrapped;
        if (w > 0 && h > 0 && Math.Min(w, h) < iconPt)
        {
            var s = Math.Min(w, h) / iconPt;
            wrapped = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "q\n{0:0.####} 0 0 {0:0.####} 0 0 cm\n{1}\nQ\n", s, content);
        }
        else
        {
            wrapped = content;
        }
        var data = System.Text.Encoding.ASCII.GetBytes(wrapped);
        SetNormalAppearance(data, new Rectangle(0, 0, w, h));
    }

    /// <summary>Document-bound ctor; rectangle defaults to empty.</summary>
    public TextAnnotation(Document document) : base(document)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Text"));
    }

    /// <summary>Always <see cref="AnnotationType.Text"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.Text;

    /// <summary>Apply <paramref name="transform"/> to the annotation's rect.</summary>
    public new void ChangeAfterResize(Matrix transform)
    {
        if (transform is null) return;
        var r = Rect;
        if (r is null) return;
        transform.Transform(r.LLX, r.LLY, out var x1, out var y1);
        transform.Transform(r.URX, r.URY, out var x2, out var y2);
        Rect = new Rectangle(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    /// <summary>The popup annotation associated with this sticky note (/Popup entry).</summary>
    public new PopupAnnotation? Popup
    {
        get
        {
            var p = InternalReader.ResolveDict(Dict.Get("Popup"));
            return p is null ? null : new PopupAnnotation(p, InternalReader);
        }
        set
        {
            if (value is null) Dict.Remove("Popup");
            else Dict.Set("Popup", value.Dict);
        }
    }
}
