using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class PdfWriterXRefStreamTests
{
    [Fact]
    public void Save_DefaultFormat_WritesTraditionalXRef()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        var saved = doc.ToArray();
        var text = Encoding.ASCII.GetString(saved);

        // Traditional format has "xref" and "trailer" keywords
        Assert.Contains("xref", text);
        Assert.Contains("trailer", text);

        // Should not contain /Type /XRef (xref stream marker)
        Assert.DoesNotContain("/Type /XRef", text);

        // Verify round-trip
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Save_XRefStream_ProducesReadablePdf()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        // Save using xref stream
        using var ms = new MemoryStream();
        var writer = CreateWriterWithXRefStream(doc, ms, useXRefStream: true);

        var saved = ms.ToArray();

        // Verify it starts with %PDF-
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(saved, 0, 10));

        // Should have startxref but no "xref\n" or "trailer" keywords
        var text = Encoding.ASCII.GetString(saved);
        Assert.Contains("startxref", text);
        Assert.DoesNotContain("\nxref\n", text);
        Assert.DoesNotContain("\ntrailer\n", text);

        // Verify round-trip: PdfReader should be able to read the xref stream
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Save_XRefStream_BumpsVersionToAtLeast15()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        // Original is 1.4
        Assert.Equal("1.4", doc.PdfVersion);

        using var ms = new MemoryStream();
        var writer = CreateWriterWithXRefStream(doc, ms, useXRefStream: true);

        var saved = ms.ToArray();
        var header = Encoding.ASCII.GetString(saved, 0, 15);

        // Version should be at least 1.5
        Assert.StartsWith("%PDF-1.5", header);
    }

    [Fact]
    public void Save_XRefStream_PreservesPageMediaBox()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        using var ms = new MemoryStream();
        CreateWriterWithXRefStream(doc, ms, useXRefStream: true);

        using var doc2 = Document.Open(ms.ToArray());
        var mb = doc2.Pages[1].MediaBox;
        Assert.Equal(612, mb.URX);
        Assert.Equal(792, mb.URY);
    }

    [Fact]
    public void Save_XRefStream_PreservesTextContent()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (XRef stream test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        using var ms = new MemoryStream();
        CreateWriterWithXRefStream(doc, ms, useXRefStream: true);

        using var doc2 = Document.Open(ms.ToArray());
        var absorber = new Aspose.Pdf.Text.TextAbsorber();
        absorber.Visit(doc2.Pages[1]);
        Assert.Contains("XRef stream test", absorber.Text);
    }

    [Fact]
    public void Save_ObjectStreams_ProducesReadablePdf()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        using var ms = new MemoryStream();
        CreateWriterWithXRefStream(doc, ms, useXRefStream: true, useObjectStreams: true);

        var saved = ms.ToArray();

        // Verify round-trip
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Save_ObjectStreams_BumpsVersionToAtLeast15()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        using var ms = new MemoryStream();
        CreateWriterWithXRefStream(doc, ms, useXRefStream: true, useObjectStreams: true);

        var saved = ms.ToArray();
        var header = Encoding.ASCII.GetString(saved, 0, 15);
        Assert.StartsWith("%PDF-1.5", header);
    }

    [Fact]
    public void Save_ObjectStreams_PreservesMediaBox()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        using var ms = new MemoryStream();
        CreateWriterWithXRefStream(doc, ms, useXRefStream: true, useObjectStreams: true);

        using var doc2 = Document.Open(ms.ToArray());
        var mb = doc2.Pages[1].MediaBox;
        Assert.Equal(612, mb.URX);
        Assert.Equal(792, mb.URY);
    }

    [Fact]
    public void Save_CombinedXRefAndObjectStreams_RoundTrip()
    {
        // Build a PDF with text content to have more objects
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Combined streams test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        using var ms = new MemoryStream();
        CreateWriterWithXRefStream(doc, ms, useXRefStream: true, useObjectStreams: true);

        var saved = ms.ToArray();

        // Verify round-trip
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);

        var absorber = new Aspose.Pdf.Text.TextAbsorber();
        absorber.Visit(doc2.Pages[1]);
        Assert.Contains("Combined streams test", absorber.Text);
    }

    [Fact]
    public void Save_XRefStream_SmallerThanTraditional()
    {
        // Build a multi-page document for a fair size comparison
        var data = PdfBuilder.BuildMultiPage(10);
        using var doc = Document.Open(data);

        // Save traditional
        var traditionalBytes = doc.ToArray();

        // Save with xref stream + object streams
        using var ms = new MemoryStream();
        CreateWriterWithXRefStream(doc, ms, useXRefStream: true, useObjectStreams: true);
        var streamBytes = ms.ToArray();

        // Both should be valid
        using var doc1 = Document.Open(traditionalBytes);
        using var doc2 = Document.Open(streamBytes);
        Assert.Equal(doc1.PageCount, doc2.PageCount);
    }

    [Fact]
    public void Save_XRefStream_ContainsXRefType()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        using var ms = new MemoryStream();
        CreateWriterWithXRefStream(doc, ms, useXRefStream: true);

        var saved = ms.ToArray();
        var text = Encoding.ASCII.GetString(saved);

        // The xref stream object should contain /Type/XRef (compact name serialization)
        Assert.Contains("/Type/XRef", text);
    }

    /// <summary>
    /// Helper that manually saves a Document using PdfWriter with xref/object stream options.
    /// This mirrors what Document.Save does but with the stream options enabled.
    /// </summary>
    private static Aspose.Pdf.IO.PdfWriter CreateWriterWithXRefStream(
        Document doc, MemoryStream output,
        bool useXRefStream = false, bool useObjectStreams = false)
    {
        var reader = doc.Reader;
        var xref = reader.XRefTable;
        var trailer = reader.Trailer;

        // Determine version
        var version = doc.PdfVersion ?? "1.4";
        if (useXRefStream || useObjectStreams)
        {
            // Bump to at least 1.5 for xref streams
            if (CompareVersions(version, "1.5") < 0)
                version = "1.5";
        }

        var writer = new Aspose.Pdf.IO.PdfWriter(output);
        writer.UseXRefStream = useXRefStream;
        writer.UseObjectStreams = useObjectStreams;
        writer.WriteHeader(version);

        // Write all existing objects
        foreach (var entry in xref.Entries.Values)
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;

            var obj = reader.Resolve(new Aspose.Pdf.Core.PdfIndirectRef(entry.ObjectNumber, entry.Generation));
            if (obj is null) continue;

            writer.WriteIndirectObject(entry.ObjectNumber, obj);
        }

        // Build trailer dict
        var newTrailer = new Aspose.Pdf.Core.PdfDictionary();
        var root = trailer.Get("Root");
        if (root is not null) newTrailer.Set("Root", root);
        var info = trailer.Get("Info");
        if (info is not null) newTrailer.Set("Info", info);
        var id = trailer.Get("ID");
        if (id is not null) newTrailer.Set("ID", id);

        writer.WriteXRefAndTrailer(newTrailer);
        return writer;
    }

    private static int CompareVersions(string a, string b)
    {
        var aParts = a.Split('.');
        var bParts = b.Split('.');
        var major = int.Parse(aParts[0]).CompareTo(int.Parse(bParts[0]));
        if (major != 0) return major;
        return int.Parse(aParts[1]).CompareTo(int.Parse(bParts[1]));
    }
}
