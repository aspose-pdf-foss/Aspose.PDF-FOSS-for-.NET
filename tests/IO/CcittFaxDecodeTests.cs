using Aspose.Pdf.Core;
using Aspose.Pdf.IO.Filters;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

/// <summary>Group 3 fax rows (<see cref="CcittFaxDecodeFilter"/>) on a 61 by 23 picture libtiff
/// coded both ways: one-dimensionally, and mixed with each row's tag bit saying how it is coded.
/// The decoder follows the tag bits whatever K bounds, and can tell which rows were coded
/// one-dimensionally.</summary>
public sealed class CcittFaxDecodeTests
{
    private const int Width = 61;
    private const int Height = 23;
    private const string OneDimensional = "ABcCsAE14AoABNZODYACaycGwAE1k4MHBAATWTg8BICAAmsnDuC7igAmsnCjhoHGACaxcY4NxyABNZPOY4NxyABNQyoQ4FxzABNQzYBQOYAJqCjMdQGgcwATUFA4x5wYHIAE1BQOUIfgHHIAE1BQOY+PjofgIOMAE1BRbtDk5OKACagoHN0PwPggAJqCgcx8fHWgQAE1BQOdofHBIGACagcOYAJqBRGACagVuA==";
    private const string Mixed = "ABuBWACCPEAGaycGwAF4AM1k4MHBAAXBGAhgIADNZOHcF3FABdBBkcYAM1i4xwbjkACwRn+ADNQyoQ4FxzABQb4AM1BRmOoDQOYALI4wRnSgAzUFA5Qh+AccgAXQIKyOgjwlABmoKLdocnJxQAWCCLoJMMKADNQUDmPj460CAAvGlI4IYGADNQOHMAFDCgAzUCtw";
    // The picture as libtiff was given it, rows padded to a byte with 0.
    private const string Picture = "P/////////jH////////+OAAB//////44AAH//////jgAAf//AD/+OAAB//j/x/44AAH/5//5/jgAAf/f//7+OAAAP7///344AAHPv///fj////F///++P////j///74///+DR///vj///7+5//9+P///vxY//34///++2sf+/j///4eF+fn+P///vlj+B/4///++2gAH/j///74W//j+P////////z4/////////xj/////////4A==";

    [Fact]
    public void MixedRowsFollowTheirTagBitsWhateverKBounds()
    {
        var rows = new List<bool>();
        var decoded = CcittFaxDecodeFilter.Decode(Convert.FromBase64String(Mixed), Parms(1), false, rows);
        Assert.Equal(Expected(), decoded);
        Assert.Equal(Height, rows.Count);
        Assert.True(rows[0], "a Group 3 stream starts one-dimensionally");
        Assert.Contains(false, rows);
        // The same stream under a larger K decodes the same rows.
        Assert.Equal(Expected(), CcittFaxDecodeFilter.Decode(Convert.FromBase64String(Mixed), Parms(4), false, null));
    }

    [Fact]
    public void OneDimensionalRowsAreAllReportedSo()
    {
        var rows = new List<bool>();
        Assert.Equal(Expected(), CcittFaxDecodeFilter.Decode(Convert.FromBase64String(OneDimensional), Parms(0), false, rows));
        Assert.Equal(Height, rows.Count);
        Assert.All(rows, Assert.True);
    }

    private static PdfDictionary Parms(int k)
    {
        var parms = new PdfDictionary();
        parms.Set("K", new PdfInteger(k));
        parms.Set("Columns", new PdfInteger(Width));
        parms.Set("Rows", new PdfInteger(Height));
        parms.Set("BlackIs1", PdfBoolean.True);
        return parms;
    }

    /// <summary>The picture: libtiff codes its set bits as black, so it is the rows the decoder
    /// gives with black as 1, the row padding 0.</summary>
    private static byte[] Expected() => Convert.FromBase64String(Picture);
}
