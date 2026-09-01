using Aspose.Pdf;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>Smoke tests for Page.SendTo(PageDevice), Page.OnBeforePageGenerate,
/// Page.AddStamp(Aspose.Pdf.Stamp), Page.AddGraphics/DeleteGraphics, Page.Layers.</summary>
public class PageFeatureTests
{
    [Fact]
    public void OnBeforePageGenerate_FiresFromSave()
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        var page = doc.Pages[1];
        var fired = 0;
        page.OnBeforePageGenerate += _ => fired++;
        _ = doc.ToArray();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void SendTo_PageDevice_RoutesThroughImagePageDevice()
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        var page = doc.Pages[1];
        using var output = new System.IO.MemoryStream();
        var pngDevice = new PngDevice(new Resolution(72));
        page.SendTo(new ImagePageDevice(pngDevice), output);
        output.Position = 0;
        Assert.True(output.Length > 0);
        // PNG magic header is 0x89 50 4E 47
        Assert.Equal(0x89, output.ReadByte());
        Assert.Equal(0x50, output.ReadByte());
        Assert.Equal(0x4E, output.ReadByte());
        Assert.Equal(0x47, output.ReadByte());
    }

    [Fact]
    public void AddStamp_TopLevelStamp_CallsPut()
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        var page = doc.Pages[1];
        var stamp = new MarkerStamp();
        page.AddStamp(stamp);
        Assert.True(stamp.PutCalled);
    }

    [Fact]
    public void AddGraphics_And_DeleteGraphics_AcceptEmptyCollections()
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        var page = doc.Pages[1];
        // Both are implemented; empty collections are no-ops.
        page.AddGraphics(new Vector.GraphicElementCollection(), new Rectangle(0, 0, 100, 100));
        page.DeleteGraphics(new Vector.GraphicElementCollection());
    }

    [Fact]
    public void Layers_ReturnsListOfLayer()
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        var page = doc.Pages[1];
        var layers = page.Layers;
        Assert.NotNull(layers);
        Assert.IsAssignableFrom<System.Collections.Generic.List<Layer>>(layers);
    }

    private sealed class MarkerStamp : Stamp
    {
        public bool PutCalled { get; private set; }
        public override void Put(Page page) { PutCalled = true; _ = page; }
    }
}
