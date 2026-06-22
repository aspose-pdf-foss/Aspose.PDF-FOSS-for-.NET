namespace Aspose.Pdf.Devices;

/// <summary>Abstract base for whole-document rendering devices. Concrete
/// subclasses override <see cref="Process(Document, Stream)"/> to render
/// every page; the page-range overload defaults to a sub-document
/// extracted via <see cref="Document.ImportPages(Document, int[])"/>.</summary>
public abstract class DocumentDevice
{
    /// <summary>Render every page of <paramref name="document"/> to
    /// <paramref name="output"/>. Required override for concrete devices.</summary>
    public abstract void Process(Document document, Stream output);

    /// <summary>Render every page of <paramref name="document"/> to
    /// <paramref name="outputFileName"/>.</summary>
    public virtual void Process(Document document, string outputFileName)
    {
        using var fs = File.Create(outputFileName);
        Process(document, fs);
    }

    /// <summary>Render <paramref name="fromPage"/>..<paramref name="toPage"/>
    /// (1-based inclusive) of <paramref name="document"/> to
    /// <paramref name="output"/>. Default implementation builds a fresh
    /// document containing only the selected range and delegates to the
    /// full-document <see cref="Process(Document, Stream)"/>.</summary>
    public virtual void Process(Document document, int fromPage, int toPage, Stream output)
    {
        using var slice = ExtractRange(document, fromPage, toPage);
        Process(slice, output);
    }

    /// <summary>Render <paramref name="fromPage"/>..<paramref name="toPage"/>
    /// (1-based inclusive) of <paramref name="document"/> to
    /// <paramref name="outputFileName"/>.</summary>
    public virtual void Process(Document document, int fromPage, int toPage, string outputFileName)
    {
        using var fs = File.Create(outputFileName);
        Process(document, fromPage, toPage, fs);
    }

    private static Document ExtractRange(Document document, int from, int to)
    {
        if (from < 1) from = 1;
        if (to > document.PageCount) to = document.PageCount;
        var indices = new System.Collections.Generic.List<int>();
        for (var i = from; i <= to; i++) indices.Add(i);
        var slice = Document.Create();
        slice.ImportPages(document, indices.ToArray());
        return slice;
    }
}

/// <summary>Default <see cref="DocumentDevice"/> implementation that saves
/// the document as a PDF (Document.Save round-trip). Useful when callers
/// want to drive a doc through the SendTo pipeline without any rendering
/// transform.</summary>
public sealed class PdfDocumentDevice : DocumentDevice
{
    public override void Process(Document document, Stream output)
    {
        if (document is null) return;
        document.Save(output);
    }
}
