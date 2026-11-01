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
        Assert.Contains("fill=\"#FF0000\"", svg);
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
        Assert.Contains("fill=\"#FF0000\"", svg);
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
        Assert.Contains("stroke=\"#0000FF\"", svg);
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
        Assert.Contains("fill=\"#FF0000\"", svg);
        Assert.Contains("stroke=\"#0000FF\"", svg);
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
        Assert.Contains("fill=\"#FF0000\"", svg);
        Assert.Contains("stroke=\"#00FF00\"", svg);
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
        Assert.Contains("fill=\"#00FFFF\"", svg);
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
        Assert.Contains("stroke=\"#FF00FF\"", svg);
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
        Assert.Contains("fill=\"#FF0000\"", svg);
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
        var count = svg.Split(new[] { "stroke-dasharray" }, StringSplitOptions.None).Length - 1;
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
        Assert.Contains("fill=\"#00FF00\"", svg);
        Assert.Contains("stroke=\"#FF0000\"", svg);
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
        Assert.Contains("fill=\"#00FF00\"", svg);
        Assert.Contains("stroke=\"#FF0000\"", svg);
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

    private const string RedToBlueAxial =
        "<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 0] /Extend [true true] " +
        "/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> >>";

    private const string RedToBlueAxialUnextended =
        "<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 0] /Extend [false false] " +
        "/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> >>";

    // The band of the unit-square axial shading placed by "200 0 0 100 100 500 cm": its axis runs from
    // (100,292) to (300,292) on the flipped page, and the band reaches 1404 (the page's width plus
    // height) either side of it, to be cut by the clip.
    private const string AxisBand = "d=\"M100 1696 L100 -1112 L300 -1112 L300 1696 Z\" fill=\"url(#sh1)\" stroke=\"none\"";

    [Fact]
    public void Process_ShOperator_PaintsTheAxisBandWithAGradientInShadingSpace()
    {
        // The unit-square shading is placed by the CTM and clipped to a rectangle.
        var data = BuildWithResources("q 100 500 200 100 re W n 200 0 0 100 100 500 cm /Sh1 sh Q",
            $"/Shading << /Sh1 {RedToBlueAxialUnextended} >>");
        using var doc = Document.Open(data);

        var svg = new SvgDevice().Process(doc.Pages[1]);
        Assert.Contains("<linearGradient id=\"sh1\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\" " +
            "gradientTransform=\"matrix(200 0 0 -100 100 292)\">", svg);
        Assert.Contains("<stop offset=\"0%\" stop-color=\"#ff0000\" />", svg);
        Assert.Contains("<stop offset=\"100%\" stop-color=\"#0000ff\" />", svg);
        Assert.Contains("clip-path=\"url(#clip1)\"  clip-rule=\"evenodd\" transform=\"matrix(1 0 0 1 0 0)\" " + AxisBand, svg);
        // Nothing beyond the band: no solid end colours.
        Assert.DoesNotContain("fill=\"#ff0000\"", svg);
        Assert.DoesNotContain("fill=\"#0000ff\"", svg);
    }

    [Fact]
    public void Process_ShOperator_ExtendedEndsAddTheirSolidColoursBeyondTheBand()
    {
        var data = BuildWithResources("q 100 500 200 100 re W n 200 0 0 100 100 500 cm /Sh1 sh Q",
            $"/Shading << /Sh1 {RedToBlueAxial} >>");
        using var doc = Document.Open(data);

        var svg = new SvgDevice().Process(doc.Pages[1]);
        Assert.Contains(AxisBand, svg);
        // The start colour fills from the band's first edge away from the axis, the end colour from its last.
        Assert.Contains("clip-path=\"url(#clip1)\"  clip-rule=\"evenodd\" transform=\"matrix(1 0 0 1 0 0)\" " +
            "d=\"M100 1696 L100 -1112 L-1304 -1112 L-1304 1696 Z\" fill=\"#ff0000\" stroke=\"none\"", svg);
        Assert.Contains("clip-path=\"url(#clip1)\"  clip-rule=\"evenodd\" transform=\"matrix(1 0 0 1 0 0)\" " +
            "d=\"M300 1696 L300 -1112 L1704 -1112 L1704 1696 Z\" fill=\"#0000ff\" stroke=\"none\"", svg);
    }

    [Fact]
    public void Process_ShadingPatternFill_PaintsTheGradientPlacedByThePatternMatrix()
    {
        // The pattern matrix maps the shading into page space; a later rg returns to a flat fill.
        var data = BuildWithResources("/Pattern cs /P1 scn 100 500 200 100 re f 0 0 1 rg 100 100 50 50 re f",
            $"/Pattern << /P1 << /PatternType 2 /Matrix [200 0 0 200 100 500] /Shading {RedToBlueAxial} >> >>");
        using var doc = Document.Open(data);

        var svg = new SvgDevice().Process(doc.Pages[1]);
        Assert.Contains("gradientTransform=\"matrix(200 0 0 -200 100 292)\"", svg);
        Assert.Contains("fill=\"url(#sh1)\"", svg);
        Assert.Contains("fill=\"#0000FF\"", svg);
    }

    [Fact]
    public void Process_TilingPatternFill_KeepsTheFlatColour()
    {
        var data = BuildWithResources("1 0 0 rg /Pattern cs /P1 scn 100 500 200 100 re f",
            "/Pattern << /P1 << /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10 /Resources << >> /Length 0 >> >>");
        using var doc = Document.Open(data);

        var svg = new SvgDevice().Process(doc.Pages[1]);
        Assert.DoesNotContain("url(#", svg);
        Assert.Contains("fill=\"#FF0000\"", svg);
    }

    [Fact]
    public void Process_NonEmbeddedHelvetica_ShipsATaggedArialSubset()
    {
        var data = PdfBuilder.BuildWithTextContent(Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello) Tj ET"));
        using var doc = Document.Open(data);

        var svg = new SvgDevice().Process(doc.Pages[1]);
        var face = System.Text.RegularExpressions.Regex.Match(svg, "@font-face \\{font-family:'(?<f>[A-Z]{6}\\+Arial)';");
        Assert.True(face.Success, "no tagged Arial face shipped");
        Assert.Contains($"font-family:'{face.Groups["f"].Value}',Helvetica, Arial, sans-serif;font-size:", svg);
        Assert.DoesNotContain("font-weight", svg);
        Assert.DoesNotContain("font-style", svg);
    }

    [Fact]
    public void Process_NonEmbeddedTimesBoldItalic_ShipsTheStyledSubstituteAndAsksForItsStyle()
    {
        var data = BuildWithResources("BT /F1 12 Tf (Hello) Tj ET",
            "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Times-BoldItalic /Encoding /WinAnsiEncoding >> >>");
        using var doc = Document.Open(data);

        var svg = new SvgDevice().Process(doc.Pages[1]);
        var face = System.Text.RegularExpressions.Regex.Match(svg, "@font-face \\{font-family:'(?<f>[A-Z]{6}\\+Times New Roman Bold Italic)';");
        Assert.True(face.Success, "no tagged Times New Roman Bold Italic face shipped");
        Assert.Contains($"font-family:'{face.Groups["f"].Value}',Times New Roman, serif;font-style:italic;font-weight:bold;font-size:", svg);
    }

    [Fact]
    public void Process_AxialShWithABoundingBox_PaintsTheBoxWhateverItsExtend()
    {
        // The axis (150..250) is shorter than the box (100..300 x 100..200) and neither end is extended:
        // the box is painted, the gradient padding the bands beyond the axis ends with the end colours.
        var data = BuildWithResources("q 50 50 300 300 re W n /Sh0 sh Q",
            "/Shading << /Sh0 << /ShadingType 2 /ColorSpace /DeviceRGB /BBox [100 100 300 200] /Coords [150 150 250 150] " +
            "/Extend [false false] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> >> >>");
        using var doc = Document.Open(data);

        var svg = new SvgDevice().Process(doc.Pages[1]);
        Assert.Contains("x1=\"150\" y1=\"150\" x2=\"250\" y2=\"150\"", svg);
        Assert.DoesNotContain("spreadMethod", svg);
        Assert.Contains("d=\"M100 692 L300 692 L300 592 L100 592 Z\" fill=\"url(#sh1)\"", svg);
        Assert.DoesNotContain("fill=\"#ff0000\"", svg);
        Assert.DoesNotContain("fill=\"#0000ff\"", svg);
    }

    /// <summary>Red at the inner circle (centre 200,200, radius r0) to blue at the outer one (radius 100).</summary>
    private static string RedToBlueRadial(int r0, string extend) =>
        $"<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [200 200 {r0} 200 200 100] /Extend [{extend}] " +
        "/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> >>";

    private static string RadialSvg(int r0, string extend)
    {
        var data = BuildWithResources("q 20 20 360 360 re W n /Sh0 sh Q", $"/Shading << /Sh0 {RedToBlueRadial(r0, extend)} >>");
        using var doc = Document.Open(data);
        return new SvgDevice().Process(doc.Pages[1]);
    }

    // The outer circle (radius 100 about 200,200 on the flipped page: centre 200,592) and the inner one of radius 30.
    private const string OuterCircle = "d=\"M300 592 C";
    private const string InnerCircle = " M230 592 C";
    private const string WholePage = "d=\"M0 0 L612 0 L612 792 L0 792 Z\"";

    [Fact]
    public void Process_RadialSh_UnextendedPaintsOnlyTheDiscOfItsOuterCircle()
    {
        var svg = RadialSvg(0, "false false");
        Assert.Contains("<radialGradient id=\"sh1\"", svg);
        Assert.Contains("<stop offset=\"0%\" stop-color=\"#ff0000\" />", svg);
        Assert.Contains(OuterCircle, svg);
        Assert.DoesNotContain(WholePage, svg);
    }

    [Fact]
    public void Process_RadialSh_ExtendedOuterEndPaintsBeyondItsCircle()
    {
        var svg = RadialSvg(0, "false true");
        Assert.Contains(WholePage + " fill=\"url(#sh1)\" fill-rule=\"evenodd\"", svg);
    }

    [Fact]
    public void Process_RadialSh_InnerRadiusStartsTheRampAndLeavesItsDiscUnpaintedUnlessExtended()
    {
        // The ramp runs from the inner circle: its first stop sits at 30% of the outer radius.
        var hole = RadialSvg(30, "false false");
        Assert.Contains("<stop offset=\"30%\" stop-color=\"#ff0000\" />", hole);
        Assert.Contains("<stop offset=\"100%\" stop-color=\"#0000ff\" />", hole);
        Assert.Contains(OuterCircle, hole);
        Assert.Contains(InnerCircle, hole);
        Assert.Contains("fill-rule=\"evenodd\"", hole);

        // Extending the inner end fills its disc - the gradient's padding gives it the first stop's red.
        var disc = RadialSvg(30, "true false");
        Assert.Contains(OuterCircle, disc);
        Assert.DoesNotContain(InnerCircle, disc);

        var both = RadialSvg(30, "true true");
        Assert.Contains(WholePage, both);
        Assert.DoesNotContain(InnerCircle, both);
    }

    /// <summary>A one-page letter-size PDF with the given content and page resources.</summary>
    private static byte[] BuildWithResources(string content, string resources)
    {
        var bytes = Encoding.ASCII.GetBytes(content);
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));
        Write("%PDF-1.4\n");
        var offsets = new long[4];
        offsets[0] = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[1] = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        offsets[2] = ms.Position;
        Write($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << {resources} >> >>\nendobj\n");
        offsets[3] = ms.Position;
        Write($"4 0 obj\n<< /Length {bytes.Length} >>\nstream\n");
        ms.Write(bytes);
        Write("\nendstream\nendobj\n");
        var xref = ms.Position;
        Write("xref\n0 5\n0000000000 65535 f \n");
        foreach (var offset in offsets) Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}
