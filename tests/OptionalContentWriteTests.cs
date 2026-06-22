using System.Text;
using Aspose.Pdf;
using Xunit;

namespace Aspose.Pdf.Tests;

public class OptionalContentWriteTests
{
    [Fact]
    public void SetVisibility_HideLayer_RoundTrip()
    {
        var pdf = BuildPdfWithLayers("Layer1", "Layer2");
        using var doc = Document.Open(pdf);
        var oc = doc.OptionalContent!;

        // Both layers visible initially (Layer2 was OFF but let's test making Layer1 hidden)
        oc.SetVisibility("Layer1", false);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var oc2 = doc2.OptionalContent!;

        Assert.False(oc2.FindByName("Layer1")!.IsVisible);
    }

    [Fact]
    public void SetVisibility_ShowHiddenLayer()
    {
        var pdf = BuildPdfWithLayers("Visible", "Hidden");
        using var doc = Document.Open(pdf);
        var oc = doc.OptionalContent!;

        Assert.False(oc.FindByName("Hidden")!.IsVisible);
        oc.SetVisibility("Hidden", true);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var oc2 = doc2.OptionalContent!;

        Assert.True(oc2.FindByName("Hidden")!.IsVisible);
    }

    [Fact]
    public void SetVisibility_NonExistentLayer_ReturnsFalse()
    {
        var pdf = BuildPdfWithLayers("Layer1");
        using var doc = Document.Open(pdf);
        var oc = doc.OptionalContent!;

        Assert.False(oc.SetVisibility("NonExistent", false));
    }

    [Fact]
    public void Builder_CreateLayers_RoundTrip()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.AddLayer("Background", visible: true);
        builder.AddLayer("Watermark", visible: false);
        builder.Build();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.True(doc2.HasLayers);
        var oc = doc2.OptionalContent!;
        Assert.Equal(2, oc.Count);
        Assert.Equal("Background", oc[0].Name);
        Assert.True(oc[0].IsVisible);
        Assert.Equal("Watermark", oc[1].Name);
        Assert.False(oc[1].IsVisible);
    }

    [Fact]
    public void Builder_AllVisible_NoOffArray()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.AddLayer("Layer1");
        builder.AddLayer("Layer2");
        builder.Build();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var oc = doc2.OptionalContent!;
        Assert.True(oc[0].IsVisible);
        Assert.True(oc[1].IsVisible);
    }

    [Fact]
    public void Builder_EmptyLayers_NoCatalogEntry()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.Build(); // no layers added

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.False(doc2.HasLayers);
    }

    private static byte[] BuildPdfWithLayers(params string[] layerNames)
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
