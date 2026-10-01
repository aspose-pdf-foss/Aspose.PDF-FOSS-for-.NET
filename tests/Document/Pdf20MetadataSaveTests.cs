using System.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

// A PDF 2.0 save moves the documentary /Info into the XMP packet: every entry becomes xmp:<key> under its own name
// and leaves /Info, except the two dates, which stay and are mirrored. The library's producer and default creator
// go into the packet too. A 1.x save keeps /Info as it is. Metadata keys are literal property names: "xmp:Title"
// reads an xmp:Title property only, and a key the packet does not carry reads as null.
public class Pdf20MetadataSaveTests
{
    [Fact]
    public void Save_AsPdf20_MovesTheInfoEntriesIntoTheXmpPacket()
    {
        var saved = SavedWithInfo("2.0");

        using var reopened = new Document(new MemoryStream(saved));
        Assert.All(reopened.Info.Keys, key => Assert.Contains(key, new[] { "CreationDate", "ModDate" }));
        Assert.Contains("ModDate", reopened.Info.Keys);
        Assert.Null(reopened.Info.Title);
        var metadata = reopened.Metadata;
        Assert.Equal("My Title", metadata["xmp:Title"].ToString());
        Assert.Equal("My Author", metadata["xmp:Author"].ToString());
        Assert.Equal("My Subject", metadata["xmp:Subject"].ToString());
        Assert.Equal("keyword1, keyword2, keyword3", metadata["xmp:Keywords"].ToString());
        Assert.Equal("My Creator", metadata["xmp:Creator"].ToString());
        Assert.Equal("My Producer", metadata["xmp:Producer"].ToString());
        Assert.Equal("CustomValue", metadata["xmp:CustomKey"].ToString());
        Assert.True(metadata.ContainsKey("xmp:ModDate"));
        Assert.StartsWith("%PDF-2.0", Encoding.ASCII.GetString(saved, 0, 8));
    }

    [Fact]
    public void Save_AsPdf17_KeepsTheInfoEntries()
    {
        var saved = SavedWithInfo(null);

        using var reopened = new Document(new MemoryStream(saved));
        Assert.Equal("My Title", reopened.Info.Title);
        Assert.Equal("My Producer", reopened.Info.Producer);
        Assert.Null(reopened.Metadata["xmp:Title"]);
    }

    [Fact]
    public void Save_AsPdf20_PutsTheLibrarysIdentityInThePacket()
    {
        using var document = new Document();
        document.Pages.Add();
        document.SetVersion("2.0");
        using var output = new MemoryStream();
        document.Save(output);

        using var reopened = new Document(new MemoryStream(output.ToArray()));
        Assert.DoesNotContain("Producer", reopened.Info.Keys);
        Assert.DoesNotContain("Creator", reopened.Info.Keys);
        Assert.False(string.IsNullOrEmpty(reopened.Metadata["xmp:Producer"]?.ToString()));
        Assert.False(string.IsNullOrEmpty(reopened.Metadata["xmp:Creator"]?.ToString()));
    }

    [Fact]
    public void Indexer_AnswersNullForAKeyThePacketDoesNotCarry()
    {
        using var document = new Document();
        document.Pages.Add();

        Assert.Null(document.Metadata["xmp:CustomKey"]);
        Assert.False(document.Metadata.ContainsKey("xmp:CustomKey"));
    }

    private static byte[] SavedWithInfo(string? version)
    {
        using var document = new Document();
        document.Pages.Add();
        if (version is not null) document.SetVersion(version);
        var info = document.Info;
        info.Producer = "My Producer";
        info.Creator = "My Creator";
        info.Title = "My Title";
        info.Author = "My Author";
        info.Subject = "My Subject";
        info.Keywords = "keyword1, keyword2, keyword3";
        info.Add("CustomKey", "CustomValue");
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }
}
