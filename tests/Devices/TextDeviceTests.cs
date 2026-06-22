using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Devices;

public class TextDeviceTests
{
    [Fact]
    public void Process_Page_ExtractsText()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello from TextDevice) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new TextDevice();
        var text = device.Process(doc.Pages[1]);
        Assert.Contains("Hello from TextDevice", text);
    }

    [Fact]
    public void Process_Document_ExtractsText()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Document text) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new TextDevice();
        var text = device.Process(doc);
        Assert.Contains("Document text", text);
    }

    [Fact]
    public void Process_ToStream_WritesBytes()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Stream output) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new TextDevice();
        using var ms = new MemoryStream();
        device.Process(doc.Pages[1], ms);

        var result = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Stream output", result);
    }

    [Fact]
    public void Process_EmptyPage_ReturnsEmptyString()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var device = new TextDevice();
        var text = device.Process(doc.Pages[1]);
        Assert.Empty(text);
    }
}
