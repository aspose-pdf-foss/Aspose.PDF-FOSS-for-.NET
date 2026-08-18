using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Devices;

public class SvgDeviceTests
{
    [Fact]
    public void Process_Page_ReturnsSvg()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (SVG test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.StartsWith("<?xml", svg);
        Assert.Contains("<svg", svg);
        Assert.Contains("version=\"1.1\"", svg);
        Assert.Contains("</svg>", svg);
    }

    [Fact]
    public void Process_Page_ContainsTextElement()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (SVG text content) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("<text", svg);
        Assert.Contains("SVG text content", svg);
    }

    [Fact]
    public void Process_Page_CorrectViewBox()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        // width/height are CSS px (pt / 0.75); the viewBox stays in points.
        Assert.Contains("width=\"816\"", svg);
        Assert.Contains("height=\"1056\"", svg);
        Assert.Contains("viewBox=\"0 0 612 792\"", svg);
    }

    [Fact]
    public void Process_Page_ContainsRectanglePath()
    {
        var content = Encoding.ASCII.GetBytes("100 200 300 400 re S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("<path", svg);
        Assert.Contains("stroke=", svg);
    }

    [Fact]
    public void Process_ToStream_WritesBytes()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (stream) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        using var ms = new MemoryStream();
        device.Process(doc.Pages[1], ms);

        var result = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("<svg", result);
    }

    [Fact]
    public void Process_EmptyPage_ReturnsValidSvg()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("<svg", svg);
        Assert.Contains("</svg>", svg);
    }

    [Fact]
    public void Process_TJ_RendersText()
    {
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf [(Hello) -200 (World)] TJ ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("Hello", svg);
        Assert.Contains("World", svg);
    }

    [Fact]
    public void Process_LinePath_RendersPath()
    {
        var content = Encoding.ASCII.GetBytes("100 200 m 300 400 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("<path", svg);
        Assert.Contains("M100", svg);
        Assert.Contains("L300", svg);
    }

    [Fact]
    public void Process_FilledRect_HasFill()
    {
        var content = Encoding.ASCII.GetBytes("1 0 0 rg 100 200 50 50 re f");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("<path", svg);
        Assert.Contains("fill=\"#ff0000\"", svg);
    }

    [Fact]
    public void Process_CurvePath_RendersBezier()
    {
        var content = Encoding.ASCII.GetBytes("100 200 m 150 300 200 350 250 250 c S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("<path", svg);
        Assert.Contains("C", svg);
    }

    // --- New tests for enhanced features ---

    [Fact]
    public void Process_GraphicsStateStack_RestoresState()
    {
        // q saves state, change fill to red, Q restores to black
        var content = Encoding.ASCII.GetBytes(
            "q 1 0 0 rg 100 200 50 50 re f Q 0 0 0 rg 200 200 50 50 re f");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("fill=\"#ff0000\"", svg);
        Assert.Contains("fill=\"#000000\"", svg);
    }

    [Fact]
    public void Process_StrokeColor_RG_AppliedToStroke()
    {
        var content = Encoding.ASCII.GetBytes("0 0 1 RG 100 200 m 300 400 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("stroke=\"#0000ff\"", svg);
    }

    [Fact]
    public void Process_StrokeAndFillColors_Separate()
    {
        // Red fill, blue stroke
        var content = Encoding.ASCII.GetBytes("1 0 0 rg 0 0 1 RG 100 200 50 50 re B");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("fill=\"#ff0000\"", svg);
        Assert.Contains("stroke=\"#0000ff\"", svg);
    }

    [Fact]
    public void Process_LineWidth_AppliedToStroke()
    {
        var content = Encoding.ASCII.GetBytes("3 w 100 200 m 300 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("stroke-width=\"3\"", svg);
    }

    [Fact]
    public void Process_LineCap_Round()
    {
        var content = Encoding.ASCII.GetBytes("1 J 100 200 m 300 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("stroke-linecap=\"round\"", svg);
    }

    [Fact]
    public void Process_LineCap_Square()
    {
        var content = Encoding.ASCII.GetBytes("2 J 100 200 m 300 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("stroke-linecap=\"square\"", svg);
    }

    [Fact]
    public void Process_LineJoin_Round()
    {
        var content = Encoding.ASCII.GetBytes("1 j 100 200 m 200 300 l 300 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("stroke-linejoin=\"round\"", svg);
    }

    [Fact]
    public void Process_LineJoin_Bevel()
    {
        var content = Encoding.ASCII.GetBytes("2 j 100 200 m 200 300 l 300 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("stroke-linejoin=\"bevel\"", svg);
    }

    [Fact]
    public void Process_DashPattern_Applied()
    {
        var content = Encoding.ASCII.GetBytes("[5 3] 0 d 100 200 m 300 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("stroke-dasharray=\"5,3\"", svg);
    }

    [Fact]
    public void Process_DashPattern_WithPhase()
    {
        var content = Encoding.ASCII.GetBytes("[4 2] 1 d 100 200 m 300 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("stroke-dasharray=\"4,2\"", svg);
        Assert.Contains("stroke-dashoffset=\"1\"", svg);
    }

    [Fact]
    public void Process_FillRule_EvenOdd_WithFStar()
    {
        var content = Encoding.ASCII.GetBytes("100 200 50 50 re f*");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("fill-rule=\"evenodd\"", svg);
    }

    [Fact]
    public void Process_FillRule_Nonzero_WithF()
    {
        var content = Encoding.ASCII.GetBytes("100 200 50 50 re f");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        // Nonzero is the default, so fill-rule="evenodd" should NOT be present
        Assert.DoesNotContain("fill-rule=\"evenodd\"", svg);
    }

    [Fact]
    public void Process_FillAndStroke_BStar_HasEvenOdd()
    {
        var content = Encoding.ASCII.GetBytes("1 0 0 rg 0 1 0 RG 100 200 50 50 re B*");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("fill-rule=\"evenodd\"", svg);
        Assert.Contains("fill=\"#ff0000\"", svg);
        Assert.Contains("stroke=\"#00ff00\"", svg);
    }

    [Fact]
    public void Process_CmykFill_k_Operator()
    {
        // Cyan = (1,0,0,0) -> RGB (0,255,255)
        var content = Encoding.ASCII.GetBytes("1 0 0 0 k 100 200 50 50 re f");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("fill=\"#00ffff\"", svg);
    }

    [Fact]
    public void Process_CmykStroke_K_Operator()
    {
        // Magenta = (0,1,0,0) -> RGB (255,0,255)
        var content = Encoding.ASCII.GetBytes("0 1 0 0 K 100 200 m 300 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("stroke=\"#ff00ff\"", svg);
    }

    [Fact]
    public void Process_CmykFill_WithKey()
    {
        // Black = (0,0,0,1) -> RGB (0,0,0)
        var content = Encoding.ASCII.GetBytes("0 0 0 1 k 100 200 50 50 re f");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("fill=\"#000000\"", svg);
    }

    [Fact]
    public void Process_GrayscaleStroke_G_Operator()
    {
        var content = Encoding.ASCII.GetBytes("0.5 G 100 200 m 300 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("stroke=\"#808080\"", svg);
    }

    [Fact]
    public void Process_GrayscaleFill_g_Operator()
    {
        var content = Encoding.ASCII.GetBytes("0.5 g 100 200 50 50 re f");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("fill=\"#808080\"", svg);
    }

    [Fact]
    public void Process_TStar_MovesToNextLine()
    {
        // Set TL=14, then T* moves down. Two text lines.
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 700 Td 14 TL (Line1) Tj T* (Line2) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("Line1", svg);
        Assert.Contains("Line2", svg);
        // T* moves the line down by the leading (700 - 14 = 686). Runs are emitted
        // in top-down page coordinates with per-glyph x positions, so Line2 lands
        // at y = 792 - 686 = 106 with its x list starting at 100.
        Assert.Contains("y=\"106.0\"", svg);
        Assert.Contains("x=\"100.0", svg);
    }

    [Fact]
    public void Process_SingleQuote_ShowsTextOnNextLine()
    {
        // ' is equivalent to T* followed by Tj
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 700 Td 14 TL (Line1) Tj (Line2) ' ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("Line1", svg);
        Assert.Contains("Line2", svg);
    }

    [Fact]
    public void Process_CmTransform_BakedIntoCoordinates()
    {
        var content = Encoding.ASCII.GetBytes("q 2 0 0 2 100 200 cm 50 50 20 20 re f Q");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        // Coordinates are flattened: (50,50) under [2 0 0 2 100 200] is (200,300)
        // in PDF space, i.e. y = 792 - 300 = 492 top-down. No <g> groups emitted.
        Assert.Contains("M200 492", svg);
        Assert.DoesNotContain("<g ", svg);
    }

    [Fact]
    public void Process_CmTransform_RestoredByQ()
    {
        var content = Encoding.ASCII.GetBytes("q 1 0 0 1 50 50 cm 10 10 20 20 re f Q 30 30 10 10 re f");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        // First rect translated by (50,50): (60,60) → y' = 792-60 = 732.
        // After Q the translation is gone: (30,30) → y' = 762.
        Assert.Contains("M60 732", svg);
        Assert.Contains("M30 762", svg);
    }

    [Fact]
    public void Process_QSaveRestore_PreservesLineWidth()
    {
        // Set line width to 5, save, change to 2, stroke, restore, stroke with 5
        var content = Encoding.ASCII.GetBytes(
            "5 w q 2 w 100 100 m 200 100 l S Q 100 200 m 200 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("stroke-width=\"2\"", svg);
        Assert.Contains("stroke-width=\"5\"", svg);
    }

    [Fact]
    public void Process_QSaveRestore_PreservesFillColor()
    {
        var content = Encoding.ASCII.GetBytes(
            "q 1 0 0 rg 10 10 50 50 re f Q 10 10 50 50 re f");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        // First fill red, after Q restore fill is back to black (default)
        Assert.Contains("fill=\"#ff0000\"", svg);
        Assert.Contains("fill=\"#000000\"", svg);
    }

    [Fact]
    public void Process_QSaveRestore_PreservesDashPattern()
    {
        var content = Encoding.ASCII.GetBytes(
            "q [5 3] 0 d 100 100 m 200 100 l S Q 100 200 m 200 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("stroke-dasharray=\"5,3\"", svg);
        // After Q, the second stroke should NOT have dasharray
        // Count occurrences of stroke-dasharray — should be exactly 1
        var count = svg.Split("stroke-dasharray").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public void Process_B_FillAndStroke_UsesSeparateColors()
    {
        // fill=green, stroke=red
        var content = Encoding.ASCII.GetBytes("0 1 0 rg 1 0 0 RG 100 200 50 50 re B");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("fill=\"#00ff00\"", svg);
        Assert.Contains("stroke=\"#ff0000\"", svg);
    }

    [Fact]
    public void Process_b_ClosesFillAndStroke()
    {
        // b = close path, fill, and stroke
        var content = Encoding.ASCII.GetBytes("0 1 0 rg 1 0 0 RG 100 200 m 200 300 l 300 200 l b");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("<path", svg);
        Assert.Contains("Z", svg); // path should be closed
        Assert.Contains("fill=\"#00ff00\"", svg);
        Assert.Contains("stroke=\"#ff0000\"", svg);
    }

    [Fact]
    public void Process_bStar_ClosesFillEvenOddAndStroke()
    {
        var content = Encoding.ASCII.GetBytes("1 0 0 rg 0 0 1 RG 100 200 m 200 300 l 300 200 l b*");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("fill-rule=\"evenodd\"", svg);
        Assert.Contains("Z", svg);
    }

    [Fact]
    public void Process_TD_SetsLeading()
    {
        // TD sets leading to -ty, then T* uses that
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 700 Td 0 -14 TD (Line1) Tj T* (Line2) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.Contains("Line1", svg);
        Assert.Contains("Line2", svg);
    }

    [Fact]
    public void Process_DefaultLineWidth_NoAttribute()
    {
        // Default line width is 1.0 — should NOT emit stroke-width attribute
        var content = Encoding.ASCII.GetBytes("100 200 m 300 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.DoesNotContain("stroke-width", svg);
    }

    [Fact]
    public void Process_DefaultLineCap_NoAttribute()
    {
        var content = Encoding.ASCII.GetBytes("100 200 m 300 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.DoesNotContain("stroke-linecap", svg);
    }

    [Fact]
    public void Process_DefaultLineJoin_NoAttribute()
    {
        var content = Encoding.ASCII.GetBytes("100 200 m 200 300 l 300 200 l S");
        var data = PdfBuilder.BuildWithTextContent(content);
        using var doc = Document.Open(data);

        var device = new SvgDevice();
        var svg = device.Process(doc.Pages[1]);
        Assert.DoesNotContain("stroke-linejoin", svg);
    }
}
