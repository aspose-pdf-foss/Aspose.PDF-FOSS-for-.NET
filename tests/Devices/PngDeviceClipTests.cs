using System.IO;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Devices;

/// <summary>
/// Exercises the W / W* clipping operators end-to-end through PngDevice. Each test
/// builds a minimal PDF whose content stream installs a clip and then paints a fill
/// that would cover the whole page; the rendered RGBA is sampled to prove the fill
/// only landed inside the clip stencil and that q/Q restores the outer clip.
/// </summary>
public class PngDeviceClipTests
{
    [Fact]
    public void W_ClipsSubsequentFill_NonZeroWinding()
    {
        // Page 612x792 at 72 DPI → pixel buffer is 612x792. User y goes up from the
        // bottom, so rect (x=200, y=700, w=50, h=50) maps to pixel rows [42..92] and
        // columns [200..250] (pixel_y = 792 - user_y).
        var content = Encoding.ASCII.GetBytes(
            "200 700 50 50 re W n\n" +       // build rect, W installs it as clip, n no-op paints
            "0 0 1 rg\n" +                    // blue fill color
            "0 0 612 792 re f");              // fill whole page — only the clip rect should show blue
        var pdf = PdfBuilder.BuildWithTextContent(content);

        var rgba = RenderToRgba(pdf);

        // Inside clip — (225, 67) must be blue.
        AssertPixel(rgba, 612, x: 225, y: 67, r: 0, g: 0, b: 255);
        // Outside clip on all four sides — must stay white.
        AssertPixel(rgba, 612, x: 100, y: 67, r: 255, g: 255, b: 255);
        AssertPixel(rgba, 612, x: 300, y: 67, r: 255, g: 255, b: 255);
        AssertPixel(rgba, 612, x: 225, y: 30, r: 255, g: 255, b: 255);
        AssertPixel(rgba, 612, x: 225, y: 100, r: 255, g: 255, b: 255);
    }

    [Fact]
    public void W_AppliesAfterThePathHasBeenPainted_NotBefore()
    {
        // The clip intersection happens *after* the painting operator per §8.5.4.2,
        // so the red fill that IS the clip path gets drawn at full coverage despite
        // a later blue fill being clipped away. Confirms the parser's "fire clip
        // event after paint event" ordering.
        var content = Encoding.ASCII.GetBytes(
            "1 0 0 rg\n" +                    // red
            "200 700 50 50 re W f\n" +       // fill red, then set clip to same rect
            "0 0 1 rg\n" +
            "0 0 612 792 re f");              // blue full-page fill, clipped to the small rect
        var pdf = PdfBuilder.BuildWithTextContent(content);

        var rgba = RenderToRgba(pdf);

        // Inside the small rect — the later blue fill overwrites the red.
        AssertPixel(rgba, 612, x: 225, y: 67, r: 0, g: 0, b: 255);
        // Outside the rect — white (blue was clipped out, and red never got there).
        AssertPixel(rgba, 612, x: 100, y: 67, r: 255, g: 255, b: 255);
    }

    [Fact]
    public void WStar_EvenOddRuleIsHonoured()
    {
        // Concentric rectangles with the even-odd rule produce a hollow ring: the
        // outer rect is "inside", the inner rect flips back to "outside". With W*,
        // the clip retains that donut shape and blocks the fill in the inner hole.
        var content = Encoding.ASCII.GetBytes(
            "100 200 400 400 re\n" +         // outer rect
            "200 300 200 200 re\n" +         // inner rect inside the outer
            "W* n\n" +                        // clip to even-odd interior (donut)
            "0 0 1 rg\n" +
            "0 0 612 792 re f");              // blue fill everywhere

        var pdf = PdfBuilder.BuildWithTextContent(content);
        var rgba = RenderToRgba(pdf);

        // Inside the ring (between outer and inner rect) — should be blue.
        AssertPixel(rgba, 612, x: 150, y: 792 - 250, r: 0, g: 0, b: 255);
        // Inside the inner hole — must stay white (even-odd said "outside").
        AssertPixel(rgba, 612, x: 300, y: 792 - 400, r: 255, g: 255, b: 255);
        // Outside the outer rect — must stay white.
        AssertPixel(rgba, 612, x: 50, y: 792 - 250, r: 255, g: 255, b: 255);
    }

    [Fact]
    public void Q_RestoresClipPathToEnclosingScope()
    {
        // q pushes state, W tightens clip, Q pops — the fill after Q must not be
        // constrained by the tightened clip anymore.
        var content = Encoding.ASCII.GetBytes(
            "q\n" +
            "200 700 50 50 re W n\n" +       // tight clip
            "0 0 1 rg 0 0 612 792 re f\n" + // blue fill constrained to tight clip
            "Q\n" +                            // restore — clip back to none
            "1 0 0 rg\n" +
            "0 0 612 792 re f");              // red fill, no clip, covers everything

        var pdf = PdfBuilder.BuildWithTextContent(content);
        var rgba = RenderToRgba(pdf);

        // A pixel well outside the former clip — red (post-Q fill reached it).
        AssertPixel(rgba, 612, x: 100, y: 200, r: 255, g: 0, b: 0);
        // The inner clipped area is also red (red fill painted *over* the blue).
        AssertPixel(rgba, 612, x: 225, y: 67, r: 255, g: 0, b: 0);
    }

    private static byte[] RenderToRgba(byte[] pdfBytes)
    {
        using var doc = Document.Open(pdfBytes);
        var renderer = new SoftwarePageRenderer();
        var buf = renderer.RenderPage(pdfBytes, 1, 72);
        return buf.Data;
    }

    private static void AssertPixel(byte[] rgba, int pixelW, int x, int y, byte r, byte g, byte b)
    {
        var idx = (y * pixelW + x) * 4;
        Assert.Equal(r, rgba[idx]);
        Assert.Equal(g, rgba[idx + 1]);
        Assert.Equal(b, rgba[idx + 2]);
    }
}
