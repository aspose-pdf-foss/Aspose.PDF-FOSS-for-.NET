using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Represents a file attachment annotation.
/// </summary>
public partial class FileAttachmentAnnotation : MarkupAnnotation
{
    internal FileAttachmentAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>
    /// Create a new file-attachment annotation on <paramref name="page"/>
    /// at <paramref name="rect"/> referencing <paramref name="fileSpec"/>.
    /// The annotation gets a default "Paperclip" icon name; callers can
    /// override via <see cref="IconName"/>'s setter (when added).
    /// </summary>
    public FileAttachmentAnnotation(Page page, Rectangle rect, FileSpecification fileSpec)
        : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("FileAttachment"));
        Dict.Set("Name", new PdfName("Paperclip"));
        if (fileSpec is not null)
        {
            // Same as the File setter: write the pending bytes into /EF so the
            // attachment's content (not just its name) survives save → reload.
            fileSpec.MaterializeEmbeddedStream();
            Dict.Set("FS", fileSpec.Dict);
        }
    }

    /// <summary>The icon name (/Name entry), e.g. "Paperclip", "Tag".</summary>
    public string? IconName => Dict.GetName("Name");

    /// <summary>The attached file name from /FS dictionary.</summary>
    public string? FileName
    {
        get
        {
            var fs = InternalReader.ResolveDict(Dict.Get("FS"));
            if (fs is null) return null;
            var obj = InternalReader.Resolve(fs.Get("F"));
            return obj is PdfString s ? s.ToText() : null;
        }
    }

    /// <summary>The attached file specification.</summary>
    public FileSpecification? File
    {
        get
        {
            var fs = InternalReader.ResolveDict(Dict.Get("FS"));
            return fs is not null ? new FileSpecification(fs, InternalReader) : null;
        }
        set
        {
            if (value is null) Dict.Remove("FS");
            else
            {
                value.MaterializeEmbeddedStream();
                Dict.Set("FS", value.Dict);
            }
        }
    }

    /// <summary>Always <see cref="AnnotationType.FileAttachment"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.FileAttachment;

    /// <summary>Named icon style for the attachment marker.</summary>
    public FileIcon Icon
    {
        get => Dict.GetName("Name") switch
        {
            "Graph" => FileIcon.Graph,
            "Paperclip" => FileIcon.Paperclip,
            "Tag" => FileIcon.Tag,
            _ => FileIcon.PushPin,
        };
        set => Dict.Set("Name", new PdfName(value.ToString()));
    }

    /// <summary>Annotation opacity (/CA entry; 0..1).</summary>
    public new double Opacity
    {
        get => (InternalReader.Resolve(Dict.Get("CA")) is PdfReal r) ? r.Value
              : (InternalReader.Resolve(Dict.Get("CA")) is PdfInteger i) ? i.Value
              : 1.0;
        set => Dict.Set("CA", new PdfReal(value));
    }
}
