using Aspose.Pdf.IO;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class XRefTableTests
{
    [Fact]
    public void FindStartXref_MinimalPdf()
    {
        var data = PdfBuilder.BuildMinimal();
        var offset = XRefTable.FindStartXref(data);
        Assert.True(offset > 0);
    }

    [Fact]
    public void Read_MinimalPdf_ParsesEntries()
    {
        var data = PdfBuilder.BuildMinimal();
        var xref = XRefTable.Read(data);

        // Should have entries for objects 0-3
        Assert.NotNull(xref.GetEntry(0));
        Assert.NotNull(xref.GetEntry(1));
        Assert.NotNull(xref.GetEntry(2));
        Assert.NotNull(xref.GetEntry(3));

        // Object 0 is free
        Assert.False(xref.GetEntry(0)!.Value.InUse);

        // Objects 1-3 are in use
        Assert.True(xref.GetEntry(1)!.Value.InUse);
        Assert.True(xref.GetEntry(2)!.Value.InUse);
        Assert.True(xref.GetEntry(3)!.Value.InUse);
    }

    [Fact]
    public void Read_MinimalPdf_TrailerHasRoot()
    {
        var data = PdfBuilder.BuildMinimal();
        var xref = XRefTable.Read(data);

        var trailer = xref.Trailer;
        Assert.NotNull(trailer.Get("Root"));
        Assert.Equal(4, trailer.GetInt("Size"));
    }

    [Fact]
    public void Read_MinimalPdf_OffsetsAreCorrect()
    {
        var data = PdfBuilder.BuildMinimal();
        var xref = XRefTable.Read(data);

        // Verify that entries point to valid locations
        foreach (var entry in xref.Entries.Values)
        {
            if (entry.InUse && entry.Offset > 0)
            {
                Assert.True(entry.Offset < data.Length);
            }
        }
    }

    [Fact]
    public void GetEntry_NonExistentObject_ReturnsNull()
    {
        var data = PdfBuilder.BuildMinimal();
        var xref = XRefTable.Read(data);
        Assert.Null(xref.GetEntry(999));
    }
}
