using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class PopupAnnotation : Annotation
{
    internal PopupAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Document-bound popup ctor; rectangle defaults to empty.</summary>
    public PopupAnnotation(Document document) : base(document, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Popup"));
    }

    /// <summary>Always <see cref="AnnotationType.Popup"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.Popup;

    /// <summary>Programmatic ctor — creates a /Popup annotation at
    /// <paramref name="rect"/> on <paramref name="page"/>.</summary>
    public PopupAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Popup"));
    }

    public bool Open
    {
        get => Dict.Get("Open") is PdfBoolean b ? b.Value : Dict.GetInt("Open") != 0;
        set => Dict.Set("Open", (value ? PdfBoolean.True : PdfBoolean.False));
    }

    /// <summary>The parent markup annotation this popup is attached to,
    /// or null if the popup has no /Parent entry.</summary>
    public Annotation? Parent
    {
        get
        {
            var parentDict = InternalReader.ResolveDict(Dict.Get("Parent"));
            return parentDict is null ? null : Annotation.Create(parentDict, InternalReader, -1);
        }
        set
        {
            if (value is null) Dict.Remove("Parent");
            else Dict.Set("Parent", value.Dict);
        }
    }
}
