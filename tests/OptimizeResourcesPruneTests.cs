using System.Collections.Generic;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Optimization;
using Xunit;

namespace Aspose.Pdf.Tests;

public class OptimizeResourcesPruneTests
{
    // Build a single-page PDF whose /Resources declares two fonts (F1, F2) and one
    // image XObject (Im1), but whose content stream references only /F1. F2 and Im1
    // are unused and must be pruned by RemoveUnusedStreams.
    private static byte[] BuildPdfWithUnusedResources()
    {
        var objects = new List<string>
        {
            // 1: Catalog
            "<< /Type /Catalog /Pages 2 0 R >>",
            // 2: Pages
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            // 3: Page — Resources list F1, F2 (fonts) and Im1 (xobject); content uses F1 only
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] " +
                "/Resources << /Font << /F1 5 0 R /F2 6 0 R >> /XObject << /Im1 7 0 R >> >> " +
                "/Contents 4 0 R >>",
            // 4: content stream — references only /F1
            "<< /Length 36 >>\nstream\nBT /F1 24 Tf 20 100 Td (Hello) Tj ET\nendstream",
            // 5: F1 (used)
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            // 6: F2 (unused)
            "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman >>",
            // 7: Im1 (unused image XObject)
            "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray " +
                "/BitsPerComponent 8 /Length 1 >>\nstream\n \nendstream",
        };

        var sb = new StringBuilder();
        sb.Append("%PDF-1.5\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xrefPos = sb.Length;
        sb.Append($"xref\n0 {objects.Count + 1}\n");
        sb.Append("0000000000 65535 f \n");
        foreach (var off in offsets)
            sb.Append($"{off:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    [Fact]
    public void RemoveUnusedStreams_PrunesUnreferencedFontAndImage()
    {
        var data = BuildPdfWithUnusedResources();
        using var doc = Document.Open(data);

        // Sanity: before optimisation the page lists both fonts and the image.
        var fontsBefore = doc.Pages[1].Resources.Fonts;
        Assert.True(fontsBefore.Contains("F1"));
        Assert.True(fontsBefore.Contains("F2"));
        Assert.Equal(1, doc.Pages[1].Resources.Images.Count);

        doc.OptimizeResources(new OptimizationOptions
        {
            RemoveUnusedStreams = true,
            RemoveUnusedObjects = true,
            LinkDuplicateStreams = false,
        });
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        var fontsAfter = doc2.Pages[1].Resources.Fonts;
        // Used font kept, unused font and unused image pruned.
        Assert.True(fontsAfter.Contains("F1"));
        Assert.False(fontsAfter.Contains("F2"));
        Assert.Equal(0, doc2.Pages[1].Resources.Images.Count);
    }

    [Fact]
    public void RemoveUnusedStreams_KeepsAllReferencedResources()
    {
        // A doc where the single font is referenced — nothing should be pruned.
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 10 10 Td (Keep) Tj ET");
        var data = Helpers.PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);
        var before = doc.Pages[1].Resources.Fonts.Count;

        doc.OptimizeResources(new OptimizationOptions { RemoveUnusedStreams = true });
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        Assert.Equal(before, doc2.Pages[1].Resources.Fonts.Count);
        Assert.Equal(1, doc2.PageCount);
    }
}
