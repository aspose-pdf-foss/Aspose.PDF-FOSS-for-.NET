using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfContentEditor
{
    public void AddDocumentAttachment(string fileAttachmentPath, string description)
    {
        var bytes = File.ReadAllBytes(fileAttachmentPath);
        AddAttachmentEntry(Path.GetFileName(fileAttachmentPath), bytes, description);
    }

    public void AddDocumentAttachment(Stream fileAttachmentStream, string fileAttachmentName, string description)
    {
        using var ms = new MemoryStream();
        fileAttachmentStream.CopyTo(ms);
        AddAttachmentEntry(fileAttachmentName, ms.ToArray(), description);
    }

    private void AddAttachmentEntry(string name, byte[] data, string description)
    {
        var doc = EnsureBound();
        var catalog = doc.Reader.Catalog;
        var namesObj = doc.Reader.Resolve(catalog.Get("Names"));
        var names = namesObj as PdfDictionary;
        if (names is null) { names = new PdfDictionary(); catalog.Set("Names", names); }
        var efObj = doc.Reader.Resolve(names.Get("EmbeddedFiles"));
        var ef = efObj as PdfDictionary;
        if (ef is null) { ef = new PdfDictionary(); names.Set("EmbeddedFiles", ef); }
        var arrObj = doc.Reader.Resolve(ef.Get("Names"));
        var arr = arrObj as PdfArray ?? new PdfArray();
        if (arrObj is null) ef.Set("Names", arr);
        var fs = BuildFileSpec(name, data);
        if (!string.IsNullOrEmpty(description))
            fs.Set("Desc", Latin1(description));
        arr.Add(Latin1(name));
        arr.Add(fs);
    }

    public void DeleteAttachments()
    {
        var doc = EnsureBound();
        var catalog = doc.Reader.Catalog;
        var names = doc.Reader.ResolveDict(catalog.Get("Names"));
        if (names is null) return;
        names.Remove("EmbeddedFiles");
    }

    public void DeleteImage()
    {
        // Delete the "current" image — first image on the first page that has one.
        var doc = EnsureBound();
        for (int p = 1; p <= doc.PageCount; p++)
        {
            var page = doc.Pages.At(p);
            var resources = doc.Reader.ResolveDict(page.Dict.Get("Resources"));
            var xobjects = doc.Reader.ResolveDict(resources?.Get("XObject"));
            if (xobjects is null) continue;
            foreach (var key in xobjects.Keys)
            {
                var xobj = doc.Reader.Resolve(xobjects.Get(key)) as PdfStream;
                if (xobj?.Dict.GetName("Subtype") == "Image")
                {
                    xobjects.Remove(key);
                    return;
                }
            }
        }
    }

    public void DeleteImage(int pageNumber, int[] index)
    {
        var page = GetPage1Based(pageNumber);
        var doc = EnsureBound();
        var resources = doc.Reader.ResolveDict(page.Dict.Get("Resources"));
        var xobjects = doc.Reader.ResolveDict(resources?.Get("XObject"));
        if (xobjects is null) return;
        var imageKeys = xobjects.Keys
            .Where(k => doc.Reader.Resolve(xobjects.Get(k)) is PdfStream s && s.Dict.GetName("Subtype") == "Image")
            .ToList();
        var toRemove = new HashSet<int>(index ?? []);
        for (int i = 0; i < imageKeys.Count; i++)
        {
            if (toRemove.Contains(i + 1) || toRemove.Contains(i))
                xobjects.Remove(imageKeys[i]);
        }
    }

    public void ReplaceImage(int pageNumber, int index, string imageFile)
    {
        var page = GetPage1Based(pageNumber);
        var doc = EnsureBound();
        var resources = doc.Reader.ResolveDict(page.Dict.Get("Resources"));
        var xobjects = doc.Reader.ResolveDict(resources?.Get("XObject"));
        if (xobjects is null) return;
        var imageKeys = xobjects.Keys
            .Where(k => doc.Reader.Resolve(xobjects.Get(k)) is PdfStream s && s.Dict.GetName("Subtype") == "Image")
            .ToList();
        var target = index - 1;
        if (target < 0 || target >= imageKeys.Count) return;

        var raw = File.ReadAllBytes(imageFile);
        var streamDict = new PdfDictionary();
        streamDict.Set("Type", new PdfName("XObject"));
        streamDict.Set("Subtype", new PdfName("Image"));
        streamDict.Set("Filter", new PdfName("DCTDecode"));
        streamDict.Set("Length", new PdfInteger(raw.Length));
        // Width/Height/ColorSpace are required by spec — caller would need to set if known.
        var newStream = new PdfStream(streamDict, raw);
        xobjects.Set(imageKeys[target], newStream);
    }
}
