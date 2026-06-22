using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Content;

public class ExtGStateTests
{
    [Fact]
    public void Parse_GsOperator_AppliesFillOpacity()
    {
        // Build a PDF with ExtGState that sets fill opacity
        var pdf = BuildPdfWithExtGState("GS1", "<< /Type /ExtGState /ca 0.5 >>");
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var reader = doc.Reader;
        var parser = new ContentStreamParser(reader);
        var extGStates = ExtGState.ResolveRawFromPage(page.Dict, reader);

        double? capturedFillAlpha = null;
        parser.OnOperator += (op, _, state) =>
        {
            if (op == "gs") capturedFillAlpha = state.FillAlpha;
        };

        var contentBytes = GetContentBytes(page, reader);
        parser.Parse(contentBytes, extGStates: extGStates);

        Assert.Equal(0.5, capturedFillAlpha);
    }

    [Fact]
    public void Parse_GsOperator_AppliesStrokeOpacity()
    {
        var pdf = BuildPdfWithExtGState("GS1", "<< /Type /ExtGState /CA 0.3 >>");
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var reader = doc.Reader;
        var parser = new ContentStreamParser(reader);
        var extGStates = ExtGState.ResolveRawFromPage(page.Dict, reader);

        double? capturedStrokeAlpha = null;
        parser.OnOperator += (op, _, state) =>
        {
            if (op == "gs") capturedStrokeAlpha = state.StrokeAlpha;
        };

        var contentBytes = GetContentBytes(page, reader);
        parser.Parse(contentBytes, extGStates: extGStates);

        Assert.Equal(0.3, capturedStrokeAlpha);
    }

    [Fact]
    public void Parse_GsOperator_AppliesBlendMode()
    {
        var pdf = BuildPdfWithExtGState("GS1", "<< /Type /ExtGState /BM /Multiply >>");
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var reader = doc.Reader;
        var parser = new ContentStreamParser(reader);
        var extGStates = ExtGState.ResolveRawFromPage(page.Dict, reader);

        string? capturedBlendMode = null;
        parser.OnOperator += (op, _, state) =>
        {
            if (op == "gs") capturedBlendMode = state.BlendMode;
        };

        var contentBytes = GetContentBytes(page, reader);
        parser.Parse(contentBytes, extGStates: extGStates);

        Assert.Equal("Multiply", capturedBlendMode);
    }

    [Fact]
    public void Parse_GsOperator_SaveRestore_Preserves()
    {
        // Content: q /GS1 gs Q — opacity should be restored after Q
        var pdf = BuildPdfWithExtGStateContent("GS1", "<< /Type /ExtGState /ca 0.2 >>",
            "q /GS1 gs Q");
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var reader = doc.Reader;
        var parser = new ContentStreamParser(reader);
        var extGStates = ExtGState.ResolveRawFromPage(page.Dict, reader);

        double lastFillAlpha = -1;
        parser.OnOperator += (op, _, state) =>
        {
            lastFillAlpha = state.FillAlpha;
        };

        var contentBytes = GetContentBytes(page, reader);
        parser.Parse(contentBytes, extGStates: extGStates);

        // After Q restore, fill alpha should be back to 1.0
        Assert.Equal(1.0, lastFillAlpha);
    }

    [Fact]
    public void ExtGState_FromPage_ParsesAll()
    {
        var pdf = BuildPdfWithExtGState("GS1", "<< /Type /ExtGState /ca 0.5 /CA 0.7 /BM /Screen >>");
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var states = ExtGState.FromPage(page);
        Assert.True(states.ContainsKey("GS1"));

        var gs = states["GS1"];
        Assert.Equal(0.5, gs.FillAlpha);
        Assert.Equal(0.7, gs.StrokeAlpha);
        Assert.Equal("Screen", gs.BlendMode);
    }

    [Fact]
    public void ExtGState_FromPage_MultipleStates()
    {
        var pdf = BuildPdfWithMultipleExtGStates();
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var states = ExtGState.FromPage(page);
        Assert.Equal(2, states.Count);
        Assert.Equal(0.5, states["GS1"].FillAlpha);
        Assert.Equal(0.8, states["GS2"].StrokeAlpha);
    }

    [Fact]
    public void SvgDevice_WithOpacity_IncludesFillOpacity()
    {
        var pdf = BuildPdfWithExtGStateContent("GS1", "<< /Type /ExtGState /ca 0.5 >>",
            "/GS1 gs BT /F1 12 Tf 100 700 Td (Hello) Tj ET");
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var device = new Aspose.Pdf.Devices.SvgDevice();
        var svg = device.Process(page);

        Assert.Contains("fill-opacity=\"0.5\"", svg);
    }

    [Fact]
    public void SvgDevice_WithBlendMode_IncludesMixBlendMode()
    {
        var pdf = BuildPdfWithExtGStateContent("GS1", "<< /Type /ExtGState /BM /Multiply >>",
            "/GS1 gs BT /F1 12 Tf 100 700 Td (Hello) Tj ET");
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var device = new Aspose.Pdf.Devices.SvgDevice();
        var svg = device.Process(page);

        Assert.Contains("mix-blend-mode:multiply", svg);
    }

    [Fact]
    public void SvgDevice_FullOpacity_NoOpacityAttribute()
    {
        // Without ExtGState, no fill-opacity should appear
        var contentBytes = Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 700 Td (Hello) Tj ET");
        var pdf = PdfBuilder.BuildWithTextContent(contentBytes);
        using var doc = Document.Open(pdf);
        var page = doc.Pages[1];

        var device = new Aspose.Pdf.Devices.SvgDevice();
        var svg = device.Process(page);

        Assert.DoesNotContain("fill-opacity", svg);
        Assert.DoesNotContain("mix-blend-mode", svg);
    }

    [Fact]
    public void GraphInfo_Opacity_Defaults()
    {
        var info = new Aspose.Pdf.GraphInfo();
        Assert.Equal(1.0, info.FillOpacity);
        Assert.Equal(1.0, info.StrokeOpacity);
    }

    [Fact]
    public void GraphInfo_Opacity_CanBeSet()
    {
        var info = new Aspose.Pdf.GraphInfo
        {
            FillOpacity = 0.5,
            StrokeOpacity = 0.3,
        };
        Assert.Equal(0.5, info.FillOpacity);
        Assert.Equal(0.3, info.StrokeOpacity);
    }

    #region Helpers

    private static byte[] GetContentBytes(Page page, PdfReader reader)
    {
        var contentsObj = reader.Resolve(page.Dict.Get("Contents"));
        if (contentsObj is PdfStream stream)
            return reader.DecodeStream(stream);
        return [];
    }

    private static byte[] BuildPdfWithExtGState(string gsName, string gsDict)
    {
        return BuildPdfWithExtGStateContent(gsName, gsDict, $"/{gsName} gs");
    }

    private static byte[] BuildPdfWithExtGStateContent(string gsName, string gsDict, string content)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // ExtGState dict
        var gsObjOffset = ms.Position;
        Write($"5 0 obj\n{gsDict}\nendobj\n");

        // Font
        var fontOffset = ms.Position;
        Write("6 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        // Content stream
        var contentBytes = Encoding.ASCII.GetBytes(content);
        var contentOffset = ms.Position;
        Write($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        ms.Write(contentBytes);
        Write("\nendstream\nendobj\n");

        // Page with ExtGState resource
        var pageOffset = ms.Position;
        Write($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
              $"/Resources << /ExtGState << /{gsName} 5 0 R >> /Font << /F1 6 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 7\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{contentOffset:D10} 00000 n \n");
        Write($"{gsObjOffset:D10} 00000 n \n");
        Write($"{fontOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    private static byte[] BuildPdfWithMultipleExtGStates()
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var gs1Offset = ms.Position;
        Write("5 0 obj\n<< /Type /ExtGState /ca 0.5 >>\nendobj\n");

        var gs2Offset = ms.Position;
        Write("6 0 obj\n<< /Type /ExtGState /CA 0.8 >>\nendobj\n");

        var contentBytes = Encoding.ASCII.GetBytes("/GS1 gs /GS2 gs");
        var contentOffset = ms.Position;
        Write($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        ms.Write(contentBytes);
        Write("\nendstream\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
              "/Resources << /ExtGState << /GS1 5 0 R /GS2 6 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 7\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{contentOffset:D10} 00000 n \n");
        Write($"{gs1Offset:D10} 00000 n \n");
        Write($"{gs2Offset:D10} 00000 n \n");
        Write("trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    #endregion
}
