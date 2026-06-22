using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

public class StructureTests
{
    [Fact]
    public void Document_NoStructTree_ReturnsNull()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.False(doc.HasStructTree);
        Assert.Null(doc.StructTreeRoot);
    }
}
