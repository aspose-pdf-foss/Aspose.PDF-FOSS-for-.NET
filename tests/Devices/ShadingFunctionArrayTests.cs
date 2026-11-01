using System.Collections.Generic;
using System.Linq;
using System.Text;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Devices;

/// <summary>
/// A shading's /Function written as an array of 1-output functions, one per colour
/// component (§8.7.4.5; what Illustrator and InDesign write for CMYK
/// gradients), paints as the one n-output function it stands for. Each test renders
/// the same light gradient, as a type 1-4 shading, in a clip over a dark blue page twice - its function
/// once as a single n-output sampled function, once as n 1-output ones - and the
/// gradient's centre must be painted, and painted alike.
/// </summary>
public class ShadingFunctionArrayTests
{
    private const int CentreX = 100, CentreY = 50, PageW = 200;

    [Theory]
    [InlineData("DeviceCMYK", 1)]
    [InlineData("DeviceRGB", 1)]
    [InlineData("DeviceCMYK", 2)]
    [InlineData("DeviceRGB", 2)]
    [InlineData("DeviceCMYK", 3)]
    [InlineData("DeviceRGB", 3)]
    [InlineData("DeviceCMYK", 4)]
    [InlineData("DeviceRGB", 4)]
    public void AnArrayOfOneOutputFunctionsPaintsAsTheSingleFunction(string space, int shadingType)
        => AssertPaintedAlike(space, pdf => new SoftwarePageRenderer().RenderPage(pdf, 1, 72).Data, shadingType);

#pragma warning disable CA1416 // Windows-only renderer, guarded by the attribute
    [WindowsOnlyFact]
    public void AnArrayOfFourCmykFunctionsPaintsAsTheSingleFunction_OnTheNativeSurface()
        => AssertPaintedAlike("DeviceCMYK", pdf => new GdiPlusPageRenderer().RenderPage(pdf, 1, 72).Data);

    [WindowsOnlyFact]
    public void AnArrayOfThreeRgbFunctionsPaintsAsTheSingleFunction_OnTheNativeSurface()
        => AssertPaintedAlike("DeviceRGB", pdf => new GdiPlusPageRenderer().RenderPage(pdf, 1, 72).Data);
#pragma warning restore CA1416

    private static void AssertPaintedAlike(string space, System.Func<byte[], byte[]> render, int shadingType = 2)
    {
        var single = Centre(render(Build(space, shadingType, array: false)));
        var array = Centre(render(Build(space, shadingType, array: true)));

        Assert.NotEqual((0, 0, 153), array);
        Assert.True(array.r > 200 && array.g > 200 && array.b > 200, $"centre {array} is not the light gradient");
        Assert.Equal(single, array);
    }

    private static (int r, int g, int b) Centre(byte[] rgba)
    {
        var i = (CentreY * PageW + CentreX) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2]);
    }

    /// <summary>A 200x100 page: dark blue, then a function-based (type 1: 2-in calculator
    /// functions of x over the unit square), axial (type 2), radial (type 3) or
    /// free-form triangle mesh (type 4: one triangle over the clip, each vertex a parametric t
    /// the function maps to a colour) shading in a 160x40 clip whose colour runs from white to 20% black (CMYK) or to 80% grey (RGB)
    /// over 35 8-bit samples.</summary>
    private static byte[] Build(string space, int shadingType, bool array)
    {
        const int samples = 35;
        // Per component, the sample at each step.
        var components = space == "DeviceCMYK"
            ? new[] { Ramp(0, 0), Ramp(0, 0), Ramp(0, 0), Ramp(0, 51) }
            : new[] { Ramp(255, 204), Ramp(255, 204), Ramp(255, 204) };
        var n = components.Length;

        string Sampled(int outputs)
        {
            var pairs = string.Join(" ", Enumerable.Repeat("0 1", outputs));
            return $"/FunctionType 0 /Domain [0 1] /Range [{pairs}] /Size [{samples}] /BitsPerSample 8 "
                 + $"/Order 1 /Encode [0 {samples - 1}] /Decode [{pairs}]";
        }

        var functions = new List<byte[]>();
        string entry;
        if (shadingType == 1)
        {
            // Of (x, y), x scaled to the ramp's end: 0.2x for black, 1 - 0.2x for each RGB component.
            string Calculator(int outputs)
                => $"/FunctionType 4 /Domain [0 1 0 1] /Range [{string.Join(" ", Enumerable.Repeat("0 1", outputs))}]";
            var ramp = space == "DeviceCMYK" ? "{ pop 0.2 mul }" : "{ pop 0.2 mul 1 exch sub }";
            if (array)
            {
                for (var c = 0; c < n; c++)
                    functions.Add(Stream(Calculator(1), Ascii(space == "DeviceCMYK" && c < 3 ? "{ pop pop 0 }" : ramp)));
                entry = "[" + string.Join(" ", Enumerable.Range(6, n).Select(o => $"{o} 0 R")) + "]";
            }
            else
            {
                var whole = space == "DeviceCMYK" ? "{ pop 0.2 mul 0 0 0 4 -1 roll }" : "{ pop 0.2 mul 1 exch sub dup dup }";
                functions.Add(Stream(Calculator(n), Ascii(whole)));
                entry = "6 0 R";
            }
        }
        else if (array)
        {
            foreach (var c in components) functions.Add(Stream(Sampled(1), c));
            entry = "[" + string.Join(" ", Enumerable.Range(6, n).Select(o => $"{o} 0 R")) + "]";
        }
        else
        {
            var interleaved = new byte[samples * n];
            for (var s = 0; s < samples; s++)
                for (var c = 0; c < n; c++) interleaved[s * n + c] = components[c][s];
            functions.Add(Stream(Sampled(n), interleaved));
            entry = "6 0 R";
        }

        var coords = shadingType == 2 ? "0 0 1 0" : "0.5 0.5 0 0.5 0.5 1";
        var content = Encoding.ASCII.GetBytes(
            "0 0 0.6 rg 0 0 200 100 re f\nq 20 30 160 40 re W n\n160 0 0 40 20 30 cm\n/Sh0 sh\nQ");
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Contents 4 0 R "
                + "/Resources << /Shading << /Sh0 5 0 R >> >> >>"),
            Stream("", content),
            shadingType == 1
                ? Ascii($"<< /ShadingType 1 /ColorSpace /{space} /Function {entry} >>")
                : shadingType == 4
                ? Stream($"/ShadingType 4 /ColorSpace /{space} /BitsPerCoordinate 8 /BitsPerComponent 8 "
                       + $"/BitsPerFlag 8 /Decode [-1 3 -1 3 0 1] /Function {entry}",
                         [0, 0, 0, 0, 0, 255, 0, 255, 0, 0, 255, 0])
                : Ascii($"<< /ShadingType {shadingType} /ColorSpace /{space} /Coords [{coords}] /Function {entry} /Extend [true true] >>"),
        };
        objects.AddRange(functions);

        var pdf = new List<byte>(Ascii("%PDF-1.7\n"));
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Count);
            pdf.AddRange(Ascii($"{i + 1} 0 obj\n"));
            pdf.AddRange(objects[i]);
            pdf.AddRange(Ascii("\nendobj\n"));
        }
        var xref = pdf.Count;
        var table = new StringBuilder($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) table.Append($"{o:D10} 00000 n \n");
        table.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        pdf.AddRange(Ascii(table.ToString()));
        return pdf.ToArray();

        static byte[] Ramp(int a, int b)
            => Enumerable.Range(0, samples).Select(i => (byte)System.Math.Round(a + (b - a) * i / (double)(samples - 1))).ToArray();
    }

    private static byte[] Stream(string dict, byte[] data)
        => Ascii($"<< {dict} /Length {data.Length} >>\nstream\n").Concat(data).Concat(Ascii("\nendstream")).ToArray();

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
}
