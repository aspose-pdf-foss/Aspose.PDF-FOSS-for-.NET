using System;
using System.IO;
using Aspose.Pdf;
using Aspose.Pdf.Core;
using Xunit;

namespace Aspose.Pdf.Tests;

public class UsageRightsSaveTests
{
    /// <summary>A document whose catalog, written in an update of the file, holds a usage-rights signature
    /// made over the file (it has a /ByteRange) and a certification.</summary>
    private static byte[] WithUsageRights()
    {
        using var blank = new Document();
        blank.Pages.Add();
        using var doc = Document.Open(blank.ToArray());
        var signature = new PdfDictionary();
        signature.Set("Type", new PdfName("Sig"));
        var range = new PdfArray();
        foreach (var value in new[] { 0, 0, 0, 0 }) range.Add(new PdfInteger(value));
        signature.Set("ByteRange", range);
        var perms = new PdfDictionary();
        perms.Set("UR3", signature);
        perms.Set("DocMDP", new PdfDictionary());
        doc.Catalog.Set("Perms", perms);
        var root = (PdfIndirectRef)doc.Reader.Trailer.Get("Root")!;
        return doc.SaveIncremental((root.ObjectNumber, doc.Catalog));
    }

    private static PdfDictionary? Perms(Document doc) => doc.Reader.ResolveDict(doc.Catalog.Get("Perms"));

    [Fact]
    public void Save_OfADocumentOpenForUpdate_AppendsToTheSignedBytesAndKeepsUsageRights()
    {
        var source = WithUsageRights();
        using var held = new MemoryStream();
        held.Write(source, 0, source.Length);
        held.Position = 0;
        using var saved = new MemoryStream();
        using (var doc = new Document(held))
            doc.Save(saved);
        var bytes = saved.ToArray();
        Assert.True(bytes.AsSpan(0, source.Length).SequenceEqual(source));
        using var reopened = Document.Open(bytes);
        Assert.True(Perms(reopened)!.ContainsKey("UR3"));
    }

    [Fact]
    public void Save_ThatRewritesTheFile_DropsUsageRightsAndKeepsTheCertification()
    {
        using var doc = Document.Open(WithUsageRights());
        using var saved = new MemoryStream();
        doc.Save(saved);
        using var reopened = Document.Open(saved.ToArray());
        var perms = Perms(reopened);
        Assert.NotNull(perms);
        Assert.False(perms!.ContainsKey("UR3"));
        Assert.True(perms.ContainsKey("DocMDP"));
    }

    [Fact]
    public void Save_ThatRewritesItsOwnStreamWhole_DropsUsageRights()
    {
        var source = WithUsageRights();
        using var held = new MemoryStream();
        held.Write(source, 0, source.Length);
        held.Position = 0;
        using (var doc = new Document(held))
        {
            doc.MustRewriteWhole = true;
            doc.Save();
        }
        using var reopened = Document.Open(held.ToArray());
        Assert.False(Perms(reopened)?.ContainsKey("UR3") ?? false);
    }
}
