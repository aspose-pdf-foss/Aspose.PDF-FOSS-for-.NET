using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Xunit;

namespace Aspose.Pdf.Tests.Functions;

/// <summary>PDF functions (§7.10) parsed from objects that stand alone - no reader,
/// direct entries, a stream's bytes decoded by its own filters - and evaluated:
/// sampled functions of every sample width, a one-input sampled function of
/// order 3 interpolated by the natural cubic spline through its samples, the
/// exponential, stitching and PostScript calculator types.</summary>
public sealed class PdfFunctionTests
{
    private const double Tolerance = 1e-6;

    private static PdfArray Numbers(params double[] values)
    {
        var array = new PdfArray();
        foreach (var value in values) array.Add(new PdfReal(value));
        return array;
    }

    private static PdfArray Integers(params long[] values)
    {
        var array = new PdfArray();
        foreach (var value in values) array.Add(new PdfInteger(value));
        return array;
    }

    private static PdfStream Sampled(int[] size, int bitsPerSample, byte[] samples, double[] range, int? order = null)
    {
        var dict = new PdfDictionary();
        dict.Set("FunctionType", new PdfInteger(0));
        var domain = new double[size.Length * 2];
        for (int i = 0; i < size.Length; i++) domain[i * 2 + 1] = 1;
        dict.Set("Domain", Numbers(domain));
        dict.Set("Range", Numbers(range));
        var sizes = new long[size.Length];
        for (int i = 0; i < size.Length; i++) sizes[i] = size[i];
        dict.Set("Size", Integers(sizes));
        dict.Set("BitsPerSample", new PdfInteger(bitsPerSample));
        if (order is { } o) dict.Set("Order", new PdfInteger(o));
        return new PdfStream(dict, samples);
    }

    private static double One(PdfObject function, params double[] input) => PdfFunction.Parse(function, null)!.Evaluate(input)[0];

    [Fact]
    public void OrderThreePassesTheNaturalCubicSplineThroughTheSamples()
    {
        // Samples 0, 200, 50, 255 at knots 0..3; the natural spline through them,
        // second derivative zero at both ends, at 0.3, 1.5 and 2.7.
        var function = Sampled([4], 8, [0, 200, 50, 255], [0, 1], order: 3);
        Assert.Equal(0.360553, One(function, 0.1), Tolerance);
        Assert.Equal(0.488725, One(function, 0.5), Tolerance);
        Assert.Equal(0.632494, One(function, 0.9), Tolerance);
    }

    [Fact]
    public void OrderThreeMeetsEverySampleAndEachOutputHasItsOwnSpline()
    {
        var function = Sampled([3], 16, [0, 0, 0xFF, 0xFF, 0x40, 0x00], [0, 1], order: 3);
        Assert.Equal(0.0, One(function, 0), Tolerance);
        Assert.Equal(1.0, One(function, 0.5), Tolerance);
        Assert.Equal(0.5469996795605402, One(function, 0.2), Tolerance);
        Assert.Equal(0.9760004882887008, One(function, 0.6), Tolerance);
    }

    [Fact]
    public void OrderOneStaysMultilinear()
    {
        var function = Sampled([4], 8, [0, 200, 50, 255], [0, 1]);
        Assert.Equal(60.0 / 255, One(function, 0.1), Tolerance);
    }

    [Fact]
    public void SamplesWithTheTopBitSetAreLargeNotNegative()
    {
        var function = Sampled([2], 32, [0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF], [0, 1]);
        Assert.Equal(0.5, One(function, 0.5), Tolerance);
        Assert.Equal(1.0, One(function, 1), Tolerance);
    }

    [Fact]
    public void TwelveAndTwentyFourBitSamplesAreReadAcrossByteBoundaries()
    {
        // 0x000 then 0xFFF packed as 00 0F FF.
        var twelve = Sampled([2], 12, [0x00, 0x0F, 0xFF], [0, 1]);
        Assert.Equal(0.5, One(twelve, 0.5), Tolerance);
        Assert.Equal(1.0, One(twelve, 1), Tolerance);

        var twentyFour = Sampled([2], 24, [0x00, 0x00, 0x00, 0x80, 0x00, 0x00], [0, 1]);
        Assert.Equal(0x800000 / (double)0xFFFFFF, One(twentyFour, 1), Tolerance);
    }

    [Fact]
    public void AStandaloneExponentialAndStitchingFunctionEvaluate()
    {
        var exponential = new PdfDictionary();
        exponential.Set("FunctionType", new PdfInteger(2));
        exponential.Set("Domain", Numbers(0, 1));
        exponential.Set("C0", Numbers(1, 0, 0));
        exponential.Set("C1", Numbers(0, 0, 1));
        exponential.Set("N", new PdfInteger(2));
        Assert.Equal([0.75, 0, 0.25], PdfFunction.Parse(exponential, null)!.Evaluate([0.5]));

        var stitching = new PdfDictionary();
        stitching.Set("FunctionType", new PdfInteger(3));
        stitching.Set("Domain", Numbers(0, 1));
        var functions = new PdfArray();
        functions.Add(exponential);
        functions.Add(exponential);
        stitching.Set("Functions", functions);
        stitching.Set("Bounds", Numbers(0.5));
        stitching.Set("Encode", Numbers(0, 1, 1, 0));
        var result = PdfFunction.Parse(stitching, null)!.Evaluate([0.75]);
        Assert.Equal(0.75, result[0], Tolerance);
        Assert.Equal(0.25, result[2], Tolerance);
    }

    [Fact]
    public void AStandaloneCalculatorStreamIsDecodedByItsOwnFilters()
    {
        var dict = new PdfDictionary();
        dict.Set("FunctionType", new PdfInteger(4));
        dict.Set("Domain", Numbers(0, 1, 0, 1));
        dict.Set("Range", Numbers(0, 1));
        dict.Set("Filter", new PdfName("ASCIIHexDecode"));
        var program = System.Text.Encoding.ASCII.GetBytes("{ add 2 div }");
        var hex = System.Text.Encoding.ASCII.GetBytes(string.Concat(program.Select(b => b.ToString("X2"))) + ">");
        Assert.Equal(0.4, PdfFunction.Parse(new PdfStream(dict, hex), null)!.Evaluate([0.2, 0.6])[0], Tolerance);
    }
}
