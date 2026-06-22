using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Xunit;

namespace Aspose.Pdf.Tests.Content;

public class ContentStreamParserAdvancedTests
{
    private static ContentStreamParser CreateParser()
    {
        // ContentStreamParser needs a PdfReader; create a minimal one
        var minimalPdf = Helpers.PdfBuilder.BuildMinimal();
        var reader = PdfReader.FromBytes(minimalPdf);
        return new ContentStreamParser(reader);
    }

    [Fact]
    public void MarkedContent_BMC_FiresEvent()
    {
        var parser = CreateParser();
        string? capturedTag = null;
        PdfDictionary? capturedProps = null;

        parser.OnMarkedContentBegin += (tag, props) =>
        {
            capturedTag = tag;
            capturedProps = props;
        };

        var content = Encoding.ASCII.GetBytes("/Span BMC\nEMC");
        parser.Parse(content);

        Assert.Equal("Span", capturedTag);
        Assert.Null(capturedProps);
    }

    [Fact]
    public void MarkedContent_BDC_WithProperties_FiresEvent()
    {
        var parser = CreateParser();
        string? capturedTag = null;
        PdfDictionary? capturedProps = null;

        parser.OnMarkedContentBegin += (tag, props) =>
        {
            capturedTag = tag;
            capturedProps = props;
        };

        var content = Encoding.ASCII.GetBytes("/Span << /MCID 0 >> BDC\nEMC");
        parser.Parse(content);

        Assert.Equal("Span", capturedTag);
        Assert.NotNull(capturedProps);
        Assert.Equal(0, capturedProps!.GetInt("MCID"));
    }

    [Fact]
    public void MarkedContent_EMC_FiresEndEvent()
    {
        var parser = CreateParser();
        var endCount = 0;

        parser.OnMarkedContentEnd += () => endCount++;

        var content = Encoding.ASCII.GetBytes("/P BMC\nEMC");
        parser.Parse(content);

        Assert.Equal(1, endCount);
    }

    [Fact]
    public void MarkedContent_BDC_SetsStateTag()
    {
        var parser = CreateParser();
        string? tagDuringBdc = null;
        string? tagAfterEmc = null;

        parser.OnMarkedContentBegin += (tag, _) => tagDuringBdc = parser.State.MarkedContentTag;
        parser.OnMarkedContentEnd += () => tagAfterEmc = parser.State.MarkedContentTag;

        var content = Encoding.ASCII.GetBytes("/P << /MCID 0 >> BDC\nEMC");
        parser.Parse(content);

        Assert.Equal("P", tagDuringBdc);
        Assert.Null(tagAfterEmc);
    }

    [Fact]
    public void MarkedContent_BDC_WithActualText()
    {
        var parser = CreateParser();
        string? actualText = null;

        parser.OnMarkedContentBegin += (_, _) => actualText = parser.State.ActualText;

        var content = Encoding.ASCII.GetBytes("/Span << /ActualText (Hello) >> BDC\nEMC");
        parser.Parse(content);

        Assert.Equal("Hello", actualText);
    }

    [Fact]
    public void InlineImage_Parsed()
    {
        var parser = CreateParser();
        PdfDictionary? capturedDict = null;
        byte[]? capturedData = null;

        parser.OnInlineImage += (dict, data) =>
        {
            capturedDict = dict;
            capturedData = data;
        };

        // Build a simple inline image: 2x2 grayscale
        var pixels = new byte[] { 0, 64, 128, 255 };
        var sb = new StringBuilder();
        sb.Append("BI\n/W 2 /H 2 /BPC 8 /CS /G\nID ");
        var header = Encoding.ASCII.GetBytes(sb.ToString());
        var footer = Encoding.ASCII.GetBytes(" EI");

        var stream = new byte[header.Length + pixels.Length + footer.Length];
        Array.Copy(header, 0, stream, 0, header.Length);
        Array.Copy(pixels, 0, stream, header.Length, pixels.Length);
        Array.Copy(footer, 0, stream, header.Length + pixels.Length, footer.Length);

        parser.Parse(stream);

        Assert.NotNull(capturedDict);
        Assert.Equal(2, (int)capturedDict!.GetInt("Width"));
        Assert.Equal(2, (int)capturedDict!.GetInt("Height"));
        Assert.Equal(8, (int)capturedDict!.GetInt("BitsPerComponent"));
        Assert.Equal("DeviceGray", capturedDict!.GetName("ColorSpace"));
        Assert.NotNull(capturedData);
        Assert.Equal(pixels, capturedData);
    }

    [Fact]
    public void InlineImage_AbbreviatedKeys_Expanded()
    {
        var parser = CreateParser();
        PdfDictionary? capturedDict = null;

        parser.OnInlineImage += (dict, _) => capturedDict = dict;

        // Use abbreviated keys: W, H, BPC, CS
        var pixels = new byte[] { 255 };
        var sb = new StringBuilder();
        sb.Append("BI\n/W 1 /H 1 /BPC 8 /CS /RGB\nID ");
        var header = Encoding.ASCII.GetBytes(sb.ToString());
        var footer = Encoding.ASCII.GetBytes(" EI");

        var stream = new byte[header.Length + pixels.Length + footer.Length];
        Array.Copy(header, 0, stream, 0, header.Length);
        Array.Copy(pixels, 0, stream, header.Length, pixels.Length);
        Array.Copy(footer, 0, stream, header.Length + pixels.Length, footer.Length);

        parser.Parse(stream);

        Assert.NotNull(capturedDict);
        Assert.Equal(1, (int)capturedDict!.GetInt("Width"));
        Assert.Equal("DeviceRGB", capturedDict!.GetName("ColorSpace"));
    }

    // ── Color space operators ───────────────────────────────────────

    [Fact]
    public void ColorSpace_cs_SetsFillColorSpace()
    {
        var parser = CreateParser();
        var content = Encoding.ASCII.GetBytes("/DeviceRGB cs");
        parser.Parse(content);

        Assert.Equal("DeviceRGB", parser.State.FillColorSpace);
    }

    [Fact]
    public void ColorSpace_CS_SetsStrokeColorSpace()
    {
        var parser = CreateParser();
        var content = Encoding.ASCII.GetBytes("/DeviceCMYK CS");
        parser.Parse(content);

        Assert.Equal("DeviceCMYK", parser.State.StrokeColorSpace);
    }

    [Fact]
    public void ColorSpace_sc_SetsFillColorRGB()
    {
        var parser = CreateParser();
        var content = Encoding.ASCII.GetBytes("0.2 0.4 0.6 sc");
        parser.Parse(content);

        Assert.Equal(0.2, parser.State.FillR, 3);
        Assert.Equal(0.4, parser.State.FillG, 3);
        Assert.Equal(0.6, parser.State.FillB, 3);
    }

    [Fact]
    public void ColorSpace_SC_SetsStrokeColorRGB()
    {
        var parser = CreateParser();
        var content = Encoding.ASCII.GetBytes("0.1 0.3 0.5 SC");
        parser.Parse(content);

        Assert.Equal(0.1, parser.State.StrokeR, 3);
        Assert.Equal(0.3, parser.State.StrokeG, 3);
        Assert.Equal(0.5, parser.State.StrokeB, 3);
    }

    [Fact]
    public void ColorSpace_scn_SetsFillColorGray()
    {
        var parser = CreateParser();
        var content = Encoding.ASCII.GetBytes("0.75 scn");
        parser.Parse(content);

        Assert.Equal(0.75, parser.State.FillR, 3);
        Assert.Equal(0.75, parser.State.FillG, 3);
        Assert.Equal(0.75, parser.State.FillB, 3);
    }

    [Fact]
    public void ColorSpace_SCN_SetsStrokeColorCMYK()
    {
        var parser = CreateParser();
        // CMYK: C=1, M=0, Y=0, K=0 => R=0, G=1, B=1
        var content = Encoding.ASCII.GetBytes("1 0 0 0 SCN");
        parser.Parse(content);

        Assert.Equal(0.0, parser.State.StrokeR, 3);
        Assert.Equal(1.0, parser.State.StrokeG, 3);
        Assert.Equal(1.0, parser.State.StrokeB, 3);
    }

    [Fact]
    public void ColorSpace_SavedAndRestored()
    {
        var parser = CreateParser();
        var content = Encoding.ASCII.GetBytes("/DeviceRGB cs /DeviceCMYK CS q /CalGray cs /CalRGB CS Q");
        parser.Parse(content);

        Assert.Equal("DeviceRGB", parser.State.FillColorSpace);
        Assert.Equal("DeviceCMYK", parser.State.StrokeColorSpace);
    }

    // ── Dash pattern ────────────────────────────────────────────────

    [Fact]
    public void DashPattern_d_SetsArrayAndPhase()
    {
        var parser = CreateParser();
        var content = Encoding.ASCII.GetBytes("[3 5] 6 d");
        parser.Parse(content);

        Assert.Equal(new double[] { 3, 5 }, parser.State.DashArray);
        Assert.Equal(6.0, parser.State.DashPhase);
    }

    [Fact]
    public void DashPattern_EmptyArray_SolidLine()
    {
        var parser = CreateParser();
        var content = Encoding.ASCII.GetBytes("[] 0 d");
        parser.Parse(content);

        Assert.Empty(parser.State.DashArray);
        Assert.Equal(0.0, parser.State.DashPhase);
    }

    [Fact]
    public void DashPattern_SavedAndRestored()
    {
        var parser = CreateParser();
        var content = Encoding.ASCII.GetBytes("[2 4] 1 d q [10] 0 d Q");
        parser.Parse(content);

        Assert.Equal(new double[] { 2, 4 }, parser.State.DashArray);
        Assert.Equal(1.0, parser.State.DashPhase);
    }

    // ── BT/ET text object ───────────────────────────────────────────

    [Fact]
    public void BT_SetsInTextObject()
    {
        var parser = CreateParser();
        bool? duringBt = null;

        parser.OnOperator += (op, _, _) =>
        {
            if (op == "BT") duringBt = parser.State.InTextObject;
        };

        var content = Encoding.ASCII.GetBytes("BT ET");
        parser.Parse(content);

        Assert.True(duringBt);
    }

    [Fact]
    public void ET_ClearsInTextObject()
    {
        var parser = CreateParser();
        bool? afterEt = null;

        parser.OnOperator += (op, _, _) =>
        {
            if (op == "ET") afterEt = parser.State.InTextObject;
        };

        var content = Encoding.ASCII.GetBytes("BT ET");
        parser.Parse(content);

        Assert.False(afterEt);
    }

    [Fact]
    public void BT_ResetsTextMatricesToIdentity()
    {
        var parser = CreateParser();
        var identity = new double[] { 1, 0, 0, 1, 0, 0 };

        // Set text matrix to something non-identity, then BT should reset
        var content = Encoding.ASCII.GetBytes("BT 2 0 0 2 10 20 Tm ET BT ET");
        parser.Parse(content);

        Assert.Equal(identity, parser.State.TextMatrix);
        Assert.Equal(identity, parser.State.TextLineMatrix);
    }

    // ── Path painting events ────────────────────────────────────────

    [Fact]
    public void PathPainted_S_FiresEvent()
    {
        var parser = CreateParser();
        string? capturedOp = null;

        parser.OnPathPainted += (op, _, _) => capturedOp = op;

        var content = Encoding.ASCII.GetBytes("100 200 m 300 400 l S");
        parser.Parse(content);

        Assert.Equal("S", capturedOp);
    }

    [Fact]
    public void PathPainted_f_FiresEvent()
    {
        var parser = CreateParser();
        string? capturedOp = null;

        parser.OnPathPainted += (op, _, _) => capturedOp = op;

        var content = Encoding.ASCII.GetBytes("0 0 100 100 re f");
        parser.Parse(content);

        Assert.Equal("f", capturedOp);
    }

    [Fact]
    public void PathPainted_B_FiresEvent()
    {
        var parser = CreateParser();
        string? capturedOp = null;

        parser.OnPathPainted += (op, _, _) => capturedOp = op;

        var content = Encoding.ASCII.GetBytes("0 0 m 100 0 l 100 100 l h B");
        parser.Parse(content);

        Assert.Equal("B", capturedOp);
    }

    [Fact]
    public void PathPainted_n_FiresEvent()
    {
        var parser = CreateParser();
        string? capturedOp = null;

        parser.OnPathPainted += (op, _, _) => capturedOp = op;

        var content = Encoding.ASCII.GetBytes("0 0 100 100 re W n");
        parser.Parse(content);

        Assert.Equal("n", capturedOp);
    }

    [Fact]
    public void PathPainted_AllPaintingOperators_Fire()
    {
        var parser = CreateParser();
        var ops = new List<string>();

        parser.OnPathPainted += (op, _, _) => ops.Add(op);

        var content = Encoding.ASCII.GetBytes(
            "0 0 m S 0 0 m s 0 0 m f 0 0 m F " +
            "0 0 m f* 0 0 m B 0 0 m B* " +
            "0 0 m b 0 0 m b* 0 0 m n");
        parser.Parse(content);

        Assert.Equal(new[] { "S", "s", "f", "F", "f*", "B", "B*", "b", "b*", "n" }, ops);
    }

    [Fact]
    public void PathPainted_IncludesGraphicsState()
    {
        var parser = CreateParser();
        double? lineWidth = null;

        parser.OnPathPainted += (_, state, __) => lineWidth = state.LineWidth;

        var content = Encoding.ASCII.GetBytes("3.5 w 0 0 m 100 100 l S");
        parser.Parse(content);

        Assert.Equal(3.5, lineWidth);
    }

    [Fact]
    public void PathOperators_TrackedViaOnOperator()
    {
        var parser = CreateParser();
        var ops = new List<string>();

        parser.OnOperator += (op, _, _) => ops.Add(op);

        var content = Encoding.ASCII.GetBytes("10 20 m 30 40 l 50 60 70 80 90 100 c h 0 0 100 50 re");
        parser.Parse(content);

        Assert.Contains("m", ops);
        Assert.Contains("l", ops);
        Assert.Contains("c", ops);
        Assert.Contains("h", ops);
        Assert.Contains("re", ops);
    }

    [Fact]
    public void CurveTo_v_y_TrackedViaOnOperator()
    {
        var parser = CreateParser();
        var ops = new List<string>();

        parser.OnOperator += (op, _, _) => ops.Add(op);

        var content = Encoding.ASCII.GetBytes("10 20 30 40 v 50 60 70 80 y");
        parser.Parse(content);

        Assert.Contains("v", ops);
        Assert.Contains("y", ops);
    }

    [Fact]
    public void ClippingOperators_TrackedViaOnOperator()
    {
        var parser = CreateParser();
        var ops = new List<string>();

        parser.OnOperator += (op, _, _) => ops.Add(op);

        var content = Encoding.ASCII.GetBytes("0 0 100 100 re W n");
        parser.Parse(content);

        Assert.Contains("W", ops);
    }
}
