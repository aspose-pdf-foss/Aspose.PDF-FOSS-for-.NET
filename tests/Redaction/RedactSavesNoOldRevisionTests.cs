using Aspose.Pdf.Annotations;
using Xunit;

namespace Aspose.Pdf.Tests.Redaction;

/// <summary>A redacted document saved back to the stream it was opened from is written whole: an
/// incremental update would keep the page as it was in the revision before it.</summary>
public class RedactSavesNoOldRevisionTests
{
    [Fact]
    public void SavingToTheSourceStreamAfterARedactionKeepsNoEarlierRevision()
    {
        var pdf = RawPage.Build("BT /F1 12 Tf 200 700 Td (4111111111111111) Tj ET",
            "<< /Font << /F1 5 0 R >> >>", new RawPage.Obj(RawPage.Helvetica));
        var source = new MemoryStream();
        source.Write(pdf, 0, pdf.Length);
        source.Position = 0;

        using (var doc = new Document(source))
        {
            var annotation = new RedactionAnnotation(doc.Pages[1], new Rectangle(195, 695, 400, 715));
            doc.Pages[1].Annotations.Add(annotation);
            annotation.Redact();
            doc.Save();
        }

        Assert.False(SavedFile.Holds(source.ToArray(), "4111111111111111"));
    }
}
