using Aspose.Pdf.Security.HiddenDataSanitization;
using Xunit;

namespace Aspose.Pdf.Tests.Redaction;

/// <summary>What <see cref="HiddenDataSanitizer"/> takes out is gone from the saved file - not only
/// unlisted: metadata of every object, the document information, actions of every kind, private
/// application data, attachments.</summary>
public class SanitizeLeavesNothingTests
{
    private static byte[] Sanitized(byte[] pdf, HiddenDataSanitizationOptions options)
    {
        using var doc = Document.Open(pdf);
        new HiddenDataSanitizer(options).Sanitize(doc);
        return doc.ToArray();
    }

    private static byte[] RichDocument() => RawPage.Build(
        "q 10 0 0 10 100 100 cm /Im1 Do Q",
        "<< /XObject << /Im1 5 0 R >> >>",
        "/Metadata 6 0 R /PieceInfo << /App << /Private (CATALOG-PRIVATE) >> >> /Outlines 7 0 R " +
        "/OpenAction << /S /JavaScript /JS (OPEN-SCRIPT) >> " +
        "/Names << /EmbeddedFiles << /Names [(a.txt) 9 0 R] >> >>",
        "/PieceInfo << /App << /Private (PAGE-PRIVATE) >> >> /AA << /O << /S /JavaScript /JS (PAGE-SCRIPT) >> >>",
        new RawPage.Obj("<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray " +
                        "/BitsPerComponent 8 /Metadata 10 0 R >>", [0x80]),
        new RawPage.Obj("<< /Type /Metadata /Subtype /XML >>", System.Text.Encoding.ASCII.GetBytes("<x>DOC-XMP</x>")),
        new RawPage.Obj("<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>"),
        new RawPage.Obj("<< /Title (Go) /Parent 7 0 R /A << /S /JavaScript /JS (BOOKMARK-SCRIPT) >> >>"),
        new RawPage.Obj("<< /Type /Filespec /F (a.txt) /EF << /F 11 0 R >> >>"),
        new RawPage.Obj("<< /Type /Metadata /Subtype /XML >>", System.Text.Encoding.ASCII.GetBytes("<x>IMAGE-XMP</x>")),
        new RawPage.Obj("<< /Type /EmbeddedFile >>", System.Text.Encoding.ASCII.GetBytes("ATTACHED-SECRET")));

    [Fact]
    public void MetadataGoesFromEveryObjectAndTheDocumentInformationEmpties()
    {
        byte[] pdf;
        using (var doc = Document.Open(RichDocument()))
        {
            doc.Info.Author = "AUTHOR-SECRET";
            pdf = doc.ToArray();
        }

        var saved = Sanitized(pdf, new HiddenDataSanitizationOptions { RemoveMetadata = true });

        Assert.False(SavedFile.Holds(saved, "DOC-XMP"));
        Assert.False(SavedFile.Holds(saved, "IMAGE-XMP"));
        Assert.False(SavedFile.Holds(saved, "AUTHOR-SECRET"));
        Assert.False(SavedFile.Holds(saved, "/Metadata"));
    }

    [Fact]
    public void ActionsGoFromTheDocumentPagesAndBookmarks()
    {
        var saved = Sanitized(RichDocument(), new HiddenDataSanitizationOptions { RemoveJavaScriptsAndActions = true });

        Assert.False(SavedFile.Holds(saved, "OPEN-SCRIPT"));
        Assert.False(SavedFile.Holds(saved, "PAGE-SCRIPT"));
        Assert.False(SavedFile.Holds(saved, "BOOKMARK-SCRIPT"));
    }

    [Fact]
    public void PrivateApplicationDataGoesFromEveryObject()
    {
        var saved = Sanitized(RichDocument(), new HiddenDataSanitizationOptions { RemoveSearchIndexAndPrivateInfo = true });

        Assert.False(SavedFile.Holds(saved, "CATALOG-PRIVATE"));
        Assert.False(SavedFile.Holds(saved, "PAGE-PRIVATE"));
    }

    [Fact]
    public void DocumentRemoveMetadataRemovesTheSame()
    {
        byte[] saved;
        using (var doc = Document.Open(RichDocument()))
        {
            doc.Info.Author = "AUTHOR-SECRET";
            doc.RemoveMetadata();
            saved = doc.ToArray();
        }

        Assert.False(SavedFile.Holds(saved, "DOC-XMP"));
        Assert.False(SavedFile.Holds(saved, "IMAGE-XMP"));
        Assert.False(SavedFile.Holds(saved, "AUTHOR-SECRET"));
    }

    [Fact]
    public void SavingBackToTheSourceStreamKeepsNoEarlierRevision()
    {
        var pdf = RichDocument();
        var source = new MemoryStream();
        source.Write(pdf, 0, pdf.Length);
        source.Position = 0;

        using (var doc = new Document(source))
        {
            new HiddenDataSanitizer(new HiddenDataSanitizationOptions { RemoveAttachments = true }).Sanitize(doc);
            doc.Save();
        }

        Assert.False(SavedFile.Holds(source.ToArray(), "ATTACHED-SECRET"));
    }

    [Fact]
    public void AttachmentsAreGoneFromTheFileNotOnlyUnlisted()
    {
        var saved = Sanitized(RichDocument(), new HiddenDataSanitizationOptions { RemoveAttachments = true });

        Assert.False(SavedFile.Holds(saved, "ATTACHED-SECRET"));
    }
}
