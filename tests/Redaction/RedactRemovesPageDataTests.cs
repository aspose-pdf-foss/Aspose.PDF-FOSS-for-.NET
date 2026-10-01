using Aspose.Pdf.Annotations;
using Xunit;
using static Aspose.Pdf.Tests.Redaction.Redacting;

namespace Aspose.Pdf.Tests.Redaction;

/// <summary>What a page keeps beside its content that <see cref="RedactionAnnotation.Redact"/> drops
/// with it: a thumbnail pictures the page as it was, private application data may copy it.</summary>
public class RedactRemovesPageDataTests
{
    [Fact]
    public void ThePageLosesItsThumbnailAndPrivateApplicationData()
    {
        var pdf = RawPage.Build(
            "BT /F1 12 Tf 200 700 Td (text) Tj ET",
            "<< /Font << /F1 5 0 R >> >>", string.Empty,
            "/Thumb 6 0 R /PieceInfo << /Illustrator << /Private (PIECE-SECRET) >> >>",
            new RawPage.Obj(RawPage.Helvetica),
            new RawPage.Obj("<< /Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 >>",
                System.Text.Encoding.ASCII.GetBytes("TH")));

        var saved = Save(pdf, new Rectangle(195, 695, 300, 715));

        Assert.False(SavedFile.Holds(saved, "PIECE-SECRET"));
        using var doc = Document.Open(saved);
        Assert.Null(doc.Pages[1].Dict.Get("Thumb"));
    }
}
