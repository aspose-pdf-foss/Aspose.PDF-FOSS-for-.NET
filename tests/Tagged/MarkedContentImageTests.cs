using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>An image item written as it appears on the page as it is shown: turned and mirrored
/// as its placement and the page's /Rotate draw it.</summary>
public class MarkedContentImageTests
{
    // Two pixels side by side: red, then blue.
    private const string Image = "<< /Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 6 >>\nstream\nÿ\u0000\u0000\u0000\u0000ÿ\nendstream";

    private const string Prose =
        "BT /F1 12 Tf 72 700 Td (The meadow beyond the northern fjord lies quiet in the evening light and) Tj " +
        "0 -14 Td (willows grow along the valley floor there, with juniper on the slopes.) Tj ET\n";

    /// <summary>A Letter page turned by <paramref name="rotate"/> drawing two lines of prose and
    /// image Im1 through <paramref name="placement"/>.</summary>
    private static byte[] Build(string placement, int rotate = 0)
    {
        var content = Prose + $"q {placement} cm /Im1 Do Q\n";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [5 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            Image,
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Rotate {rotate} /Contents 6 0 R " +
            "/Resources << /Font << /F1 3 0 R >> /XObject << /Im1 4 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        };
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Compat.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    /// <summary>The image item of the tagged page, saved; its pixels as colour names, row by row.</summary>
    private static (int Width, int Height, string[] Pixels) Saved(byte[] pdf)
    {
        using var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        var image = doc.TaggedContent.StructTreeRootElement.GetMarkedContent()
            .Single(i => i.Kind == MarkedContentKind.Image);
        using var ms = new MemoryStream();
        image.SaveImage(ms);
        var (pixels, width, height, alpha) = Aspose.Pdf.Facades.PdfFileMend.DecodePng(ms.ToArray());
        var step = alpha ? 4 : 3;
        var names = Enumerable.Range(0, width * height)
            .Select(i => pixels[i * step] > 128 ? "red" : pixels[i * step + 2] > 128 ? "blue" : "other").ToArray();
        return (width, height, names);
    }

    [Fact]
    public void AnImageDrawnAsStored_IsWrittenAsStored()
    {
        var (width, height, pixels) = Saved(Build("100 0 0 50 72 400"));

        Assert.Equal((2, 1), (width, height));
        Assert.Equal(new[] { "red", "blue" }, pixels);
    }

    [Fact]
    public void AnImageDrawnMirrored_IsWrittenMirrored()
    {
        var (width, height, pixels) = Saved(Build("-100 0 0 50 172 400"));

        Assert.Equal((2, 1), (width, height));
        Assert.Equal(new[] { "blue", "red" }, pixels);
    }

    [Fact]
    public void AnImageOnATurnedPage_IsWrittenTurned()
    {
        // Shown a quarter turn clockwise, the image's left-to-right runs top to bottom.
        var (width, height, pixels) = Saved(Build("100 0 0 50 72 400", rotate: 90));

        Assert.Equal((1, 2), (width, height));
        Assert.Equal(new[] { "red", "blue" }, pixels);
    }

    [Fact]
    public void AnImageDrawnTurned_IsWrittenTurned()
    {
        // Drawn a quarter turn counter-clockwise: its left-to-right runs up the page, blue on top.
        var (width, height, pixels) = Saved(Build("0 100 -50 0 122 400"));

        Assert.Equal((1, 2), (width, height));
        Assert.Equal(new[] { "blue", "red" }, pixels);
    }
}
