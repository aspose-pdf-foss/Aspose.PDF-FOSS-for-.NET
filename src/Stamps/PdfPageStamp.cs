using System.Collections.Generic;
using System.Globalization;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Stamps;

namespace Aspose.Pdf;

/// <summary>
/// Stamps the content of one PDF page onto another page.
/// The source page is drawn as a Form XObject at the specified position.
/// </summary>
public sealed class PdfPageStamp : Aspose.Pdf.Stamps.Stamp
{
    private Page _sourcePage;
    private PdfReader _sourceReader;

    /// <summary>Width of the stamp in points. Defaults to source page width.</summary>
    public double Width { get; set; }

    /// <summary>Height of the stamp in points. Defaults to source page height.</summary>
    public double Height { get; set; }

    /// <summary>The source page being stamped.</summary>
    public Page PdfPage
    {
        get => _sourcePage;
        set
        {
            _sourcePage = value;
            if (value is not null) _sourceReader = value.Reader;
        }
    }

    /// <summary>
    /// Create a PdfPageStamp from a page of another document.
    /// </summary>
    public PdfPageStamp(Page pdfPage)
    {
        _sourcePage = pdfPage;
        _sourceReader = pdfPage.Reader;
        Width = pdfPage.Width;
        Height = pdfPage.Height;
    }

    /// <summary>Alias for <see cref="ApplyTo"/> matching the Aspose.PDF for .NET public surface.</summary>
    public void Put(Page page) => ApplyTo(page);

    /// <summary>Create a PdfPageStamp from page <paramref name="pageIndex"/>
    /// (1-based) of the PDF at <paramref name="fileName"/>.</summary>
    public PdfPageStamp(string fileName, int pageIndex)
        : this(Document.Open(File.ReadAllBytes(fileName)).Pages.At(pageIndex)) { }

    /// <summary>Create a PdfPageStamp from page <paramref name="pageIndex"/>
    /// (1-based) of the PDF read from <paramref name="stream"/>.</summary>
    public PdfPageStamp(Stream stream, int pageIndex)
        : this(Document.Open(ReadStream(stream)).Pages.At(pageIndex)) { }

    private static byte[] ReadStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    internal override byte[] BuildContentStream(Page targetPage)
    {
        var sourcePage = _sourcePage;
        var sourceReader = _sourceReader;

        // Get source page content
        var sourceContent = GetPageContent(sourcePage.Dict, sourceReader);
        if (sourceContent.Length == 0) return [];

        // Create a Form XObject from the source page content
        var formDict = new PdfDictionary();
        formDict.Set("Type", new PdfName("XObject"));
        formDict.Set("Subtype", new PdfName("Form"));

        var mb = sourcePage.MediaBox;
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(mb.LLX));
        bbox.Add(new PdfReal(mb.LLY));
        bbox.Add(new PdfReal(mb.URX));
        bbox.Add(new PdfReal(mb.URY));
        formDict.Set("BBox", bbox);

        // Import the source page's resources into the TARGET document. The source
        // /Resources dictionary holds indirect references into the source document's
        // object table (fonts, ICC colour spaces, images, ExtGStates); copying them
        // verbatim would leave dangling references in the target. ImportDict resolves
        // the whole object graph against the source reader and re-registers it with
        // fresh object numbers in the target so the form is self-contained.
        var targetReader = targetPage.Reader;
        var targetDoc = targetReader.OwnerDocument;
        var srcResources = sourceReader.ResolveDict(sourcePage.Dict.Get("Resources"));
        if (srcResources is not null && targetDoc is not null)
            formDict.Set("Resources", targetDoc.ImportDict(srcResources, sourceReader, new Dictionary<int, int>()));
        else if (srcResources is not null)
            formDict.Set("Resources", srcResources);

        var formStream = new PdfStream(formDict, sourceContent);

        // Register the Form XObject in target page resources
        var targetResources = targetReader.ResolveDict(targetPage.Dict.Get("Resources"));
        if (targetResources is null)
        {
            targetResources = new PdfDictionary();
            targetPage.Dict.Set("Resources", targetResources);
        }

        var xobjectDict = targetReader.ResolveDict(targetResources.Get("XObject"));
        if (xobjectDict is null)
        {
            xobjectDict = new PdfDictionary();
            targetResources.Set("XObject", xobjectDict);
        }

        // Find unique name for the form XObject
        var xobjName = "Fm0";
        var counter = 0;
        while (xobjectDict.ContainsKey(xobjName))
            xobjName = $"Fm{++counter}";

        xobjectDict.Set(xobjName, formStream);

        // Build content stream to draw the form XObject
        var x = XIndent;
        var y = YIndent;
        var sx = Width / mb.Width * ZoomX;
        var sy = Height / mb.Height * ZoomY;

        var f = (double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        var content = $"q {f(sx)} 0 0 {f(sy)} {f(x)} {f(y)} cm /{xobjName} Do Q\n";
        return System.Text.Encoding.ASCII.GetBytes(content);
    }

    /// <summary>
    /// Apply this stamp to a page. Overrides base to support Background mode.
    /// </summary>
    public void ApplyTo(Page page)
    {
        var stampBytes = BuildContentStream(page);
        if (stampBytes.Length == 0) return;

        if (IsBackground)
        {
            // Prepend stamp content before existing page content. The page's
            // /Contents may be a single stream or an array of streams — decode
            // both so the underlying page content is preserved beneath the stamp.
            byte[] existingData = GetPageContent(page.Dict, page.Reader);

            var combined = new byte[stampBytes.Length + 1 + existingData.Length];
            stampBytes.CopyTo(combined, 0);
            combined[stampBytes.Length] = (byte)'\n';
            existingData.CopyTo(combined, stampBytes.Length + 1);

            page.SetContentStream(combined);
        }
        else
        {
            page.AddContentStream(stampBytes);
        }
    }

    private static byte[] GetPageContent(PdfDictionary pageDict, PdfReader reader)
    {
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream) return reader.DecodeStream(stream);
        if (obj is PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                {
                    var data = reader.DecodeStream(s);
                    ms.Write(data);
                    ms.WriteByte((byte)'\n');
                }
            }
            return ms.ToArray();
        }
        return [];
    }
}
