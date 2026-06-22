using System.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

public class OptionalContentTests
{
    [Fact]
    public void NoLayers_ReturnsFalse()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        Assert.False(doc.HasLayers);
        Assert.Null(doc.OptionalContent);
    }

    [Fact]
    public void HasLayers_ReturnsTrue()
    {
        var pdf = BuildPdfWithLayers("Layer1", "Layer2");
        using var doc = Document.Open(pdf);
        Assert.True(doc.HasLayers);
        Assert.NotNull(doc.OptionalContent);
    }

    [Fact]
    public void Layers_CountAndNames()
    {
        var pdf = BuildPdfWithLayers("Background", "Text", "Images");
        using var doc = Document.Open(pdf);
        var oc = doc.OptionalContent!;
        Assert.Equal(3, oc.Count);
        Assert.Equal("Background", oc[0].Name);
        Assert.Equal("Text", oc[1].Name);
        Assert.Equal("Images", oc[2].Name);
    }

    [Fact]
    public void Layers_DefaultVisible()
    {
        var pdf = BuildPdfWithLayers("Visible", "Hidden");
        using var doc = Document.Open(pdf);
        var oc = doc.OptionalContent!;
        // By default both should be visible (Hidden is in OFF array in our builder)
        Assert.True(oc[0].IsVisible);
        Assert.False(oc[1].IsVisible);
    }

    [Fact]
    public void Layers_FindByName()
    {
        var pdf = BuildPdfWithLayers("Alpha", "Beta");
        using var doc = Document.Open(pdf);
        var oc = doc.OptionalContent!;
        var beta = oc.FindByName("Beta");
        Assert.NotNull(beta);
        Assert.Equal("Beta", beta!.Name);
    }

    [Fact]
    public void Layers_FindByName_NotFound()
    {
        var pdf = BuildPdfWithLayers("Alpha");
        using var doc = Document.Open(pdf);
        Assert.Null(doc.OptionalContent!.FindByName("Gamma"));
    }

    [Fact]
    public void Layers_Names()
    {
        var pdf = BuildPdfWithLayers("A", "B", "C");
        using var doc = Document.Open(pdf);
        var names = doc.OptionalContent!.Names;
        Assert.Equal(new[] { "A", "B", "C" }, names);
    }

    [Fact]
    public void Layers_Intent()
    {
        var pdf = BuildPdfWithIntent("Design");
        using var doc = Document.Open(pdf);
        var oc = doc.OptionalContent!;
        Assert.Equal("Design", oc[0].Intent);
    }

    [Fact]
    public void Layers_NoIntent_ReturnsNull()
    {
        var pdf = BuildPdfWithLayers("NoIntent");
        using var doc = Document.Open(pdf);
        Assert.Null(doc.OptionalContent![0].Intent);
    }

    [Fact]
    public void Layers_DisplayOrder()
    {
        var pdf = BuildPdfWithOrder("C", "B", "A");
        using var doc = Document.Open(pdf);
        var order = doc.OptionalContent!.GetDisplayOrder();
        Assert.NotNull(order);
        Assert.Equal(3, order!.Count);
        Assert.Equal("C", order[0]);
        Assert.Equal("B", order[1]);
        Assert.Equal("A", order[2]);
    }

    [Fact]
    public void Layers_NoOrder_ReturnsNull()
    {
        var pdf = BuildPdfWithLayers("X");
        using var doc = Document.Open(pdf);
        Assert.Null(doc.OptionalContent!.GetDisplayOrder());
    }

    /// <summary>Build a PDF with an OCG that has /Intent.</summary>
    private static byte[] BuildPdfWithIntent(string intent)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.5\n");

        var ocgOffset = ms.Position;
        Write($"4 0 obj\n<< /Type /OCG /Name (Layer1) /Intent /{intent} >>\nendobj\n");

        var ocPropsOffset = ms.Position;
        Write("5 0 obj\n<< /OCGs [4 0 R] /D << /ON [4 0 R] >> >>\nendobj\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /OCProperties 5 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{ocgOffset:D10} 00000 n \n");
        Write($"{ocPropsOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>Build a PDF with OCGs and a /D/Order array.</summary>
    private static byte[] BuildPdfWithOrder(params string[] layerNames)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.5\n");

        var ocgStart = 4;
        var ocgOffsets = new long[layerNames.Length];
        for (var i = 0; i < layerNames.Length; i++)
        {
            ocgOffsets[i] = ms.Position;
            Write($"{ocgStart + i} 0 obj\n<< /Type /OCG /Name ({layerNames[i]}) >>\nendobj\n");
        }

        var ocgsRefs = string.Join(" ", Enumerable.Range(ocgStart, layerNames.Length).Select(n => $"{n} 0 R"));
        var orderRefs = string.Join(" ", Enumerable.Range(ocgStart, layerNames.Length).Select(n => $"{n} 0 R"));

        var ocPropsObjNum = ocgStart + layerNames.Length;
        var ocPropsOffset = ms.Position;
        Write($"{ocPropsObjNum} 0 obj\n<< /OCGs [{ocgsRefs}] /D << /ON [{ocgsRefs}] /Order [{orderRefs}] >> >>\nendobj\n");

        var catalogOffset = ms.Position;
        Write($"1 0 obj\n<< /Type /Catalog /Pages 2 0 R /OCProperties {ocPropsObjNum} 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        var totalObjs = ocPropsObjNum + 1;
        var xrefOffset = ms.Position;
        Write($"xref\n0 {totalObjs}\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        for (var i = 0; i < layerNames.Length; i++)
            Write($"{ocgOffsets[i]:D10} 00000 n \n");
        Write($"{ocPropsOffset:D10} 00000 n \n");

        Write($"trailer\n<< /Size {totalObjs} /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with OCG layers. The second layer (if present) is OFF by default.
    /// </summary>
    private static byte[] BuildPdfWithLayers(params string[] layerNames)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.5\n");

        // OCG objects start at obj 4
        var ocgStart = 4;
        var ocgOffsets = new long[layerNames.Length];
        for (var i = 0; i < layerNames.Length; i++)
        {
            ocgOffsets[i] = ms.Position;
            Write($"{ocgStart + i} 0 obj\n<< /Type /OCG /Name ({layerNames[i]}) >>\nendobj\n");
        }

        // Build OCGs array ref string
        var ocgsRefs = string.Join(" ", Enumerable.Range(ocgStart, layerNames.Length).Select(n => $"{n} 0 R"));

        // OFF array: second layer if exists
        var offRefs = layerNames.Length > 1 ? $"/OFF [{ocgStart + 1} 0 R]" : "";

        var ocPropsObjNum = ocgStart + layerNames.Length;
        var ocPropsOffset = ms.Position;
        Write($"{ocPropsObjNum} 0 obj\n<< /OCGs [{ocgsRefs}] /D << /ON [{ocgsRefs}] {offRefs} >> >>\nendobj\n");

        var catalogOffset = ms.Position;
        Write($"1 0 obj\n<< /Type /Catalog /Pages 2 0 R /OCProperties {ocPropsObjNum} 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        var totalObjs = ocPropsObjNum + 1;
        var xrefOffset = ms.Position;
        Write($"xref\n0 {totalObjs}\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        for (var i = 0; i < layerNames.Length; i++)
            Write($"{ocgOffsets[i]:D10} 00000 n \n");
        Write($"{ocPropsOffset:D10} 00000 n \n");

        Write($"trailer\n<< /Size {totalObjs} /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }
}

