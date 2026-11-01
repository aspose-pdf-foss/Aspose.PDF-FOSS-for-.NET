using System.IO;
using System.IO.Compression;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Tests.Redaction;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>Text extraction across Flate-compressed inline images: the image data is skipped to its
/// own EI - a stray "EI" inside the compressed bytes is not the end - and the text after every image
/// is still read, however many images the content stream holds.</summary>
public sealed class InlineFlateImageTextTests
{
    private const int Side = 4;

    [Fact]
    public void TextAfterAnImageWhoseDataHoldsAStrayEiIsRead()
    {
        var text = Extracted(Page(images: 1));
        Assert.Contains("before", text);
        Assert.Contains("after", text);
    }

    [Fact]
    public void TextAfterManyImagesInOneContentStreamIsRead()
    {
        var text = Extracted(Page(images: 2000));
        Assert.Contains("before", text);
        Assert.Contains("after", text);
    }

    /// <summary>Text, then <paramref name="images"/> inline images, then text again.</summary>
    private static byte[] Page(int images)
    {
        using var content = new MemoryStream();
        void Write(string s) => content.Write(Encoding.ASCII.GetBytes(s));
        Write("BT /F1 12 Tf 72 700 Td (before) Tj ET\n");
        var data = StoredDeflate(Samples());
        for (var i = 0; i < images; i++)
        {
            Write($"q 10 0 0 10 {72 + i % 50 * 10} {600 - i / 50 % 50 * 10} cm " +
                  $"BI /W {Side} /H {Side} /CS /G /BPC 8 /F /Fl ID ");
            content.Write(data);
            Write("\nEI Q\n");
        }
        Write("BT /F1 12 Tf 72 100 Td (after) Tj ET\n");
        return RawPage.Build(content.ToArray(), $"<< /Font << /F1 {RawPage.Helvetica} >> >>",
            string.Empty, string.Empty);
    }

    /// <summary>The image's samples, spelling "EI" and a blank among them, then an operator and an
    /// unclosed string: a reader that took that "EI" for the end would read the rest of the content
    /// stream as one string and lose the text after the image.</summary>
    private static byte[] Samples()
    {
        var samples = new byte[Side * Side];
        Encoding.ASCII.GetBytes("EI\nQ (").CopyTo(samples, 3);
        return samples;
    }

    /// <summary>zlib data in stored (uncompressed) blocks, so the samples - the stray "EI" with
    /// them - appear in it byte for byte.</summary>
    private static byte[] StoredDeflate(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.NoCompression, leaveOpen: true))
            z.Write(raw);
        var bytes = ms.ToArray();
        Assert.Contains("EI\n", Encoding.ASCII.GetString(bytes));
        return bytes;
    }

    private static string Extracted(byte[] pdf)
    {
        using var document = new Document(new MemoryStream(pdf));
        var absorber = new TextAbsorber();
        document.Pages.Accept(absorber);
        return absorber.Text;
    }
}
