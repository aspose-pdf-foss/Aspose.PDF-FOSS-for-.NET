using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Text;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Printing;

/// <summary>
/// A page sent to a printer as drawing commands, or as an image of itself when its filled paths
/// are too many for a driver to take one by one; and the saving of the printer surface's state at
/// every <c>q</c>, which must round-trip the transform and the clip.
/// </summary>
[SupportedOSPlatform("windows")]
public class DensePagePrintTests
{
    /// <summary>A Letter page whose content fills <paramref name="fills"/> small shapes, one path each.</summary>
    private static byte[] PageOfFills(int fills)
    {
        var content = new StringBuilder();
        for (var i = 0; i < fills; i++)
        {
            var x = 40 + (i % 50) * 10;
            var y = 60 + (i / 50) * 12;
            content.Append($"{x} {y} m {x + 4} {y + 6} l {x + 8} {y} l h f\n");
        }
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        };
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new int[objects.Length];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i] = pdf.Length;
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) pdf.Append($"{o:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    /// <summary>Draws the page the way a print job does, onto a metafile standing in for the printer
    /// surface: whether it went as commands, and how many path fills the metafile recorded.</summary>
    private static (bool AsCommands, int FillRecords) PrintToMetafile(byte[] pdf)
    {
        using var document = new Document(new MemoryStream(pdf));
        using var reference = new Bitmap(1, 1);
        using var referenceGraphics = Graphics.FromImage(reference);
        var hdc = referenceGraphics.GetHdc();
        Metafile metafile;
        bool asCommands;
        try
        {
            metafile = new Metafile(hdc, new RectangleF(0, 0, 850, 1100), MetafileFrameUnit.Pixel, EmfType.EmfPlusOnly);
            using var target = Graphics.FromImage(metafile);
            var renderer = new GdiPlusPageRenderer();
            asCommands = renderer.TryRenderPageToGraphics(document.Pages[1], target, new RectangleF(0, 0, 850, 1100), 600);
        }
        finally
        {
            referenceGraphics.ReleaseHdc(hdc);
        }
        using (metafile)
        {
            var fills = 0;
            using var counter = Graphics.FromImage(reference);
            counter.EnumerateMetafile(metafile, new System.Drawing.Point(0, 0), (type, _, _, _, _) =>
            {
                if (type == EmfPlusRecordType.FillPath) fills++;
                return true;
            });
            return (asCommands, fills);
        }
    }

    [WindowsOnlyFact]
    public void APageOfFewFillsIsSentToThePrinterAsCommands()
    {
        var (asCommands, fillRecords) = PrintToMetafile(PageOfFills(300));
        Assert.True(asCommands);
        Assert.Equal(300, fillRecords);
    }

    [WindowsOnlyFact]
    public void APageDenseWithFillsIsSentToThePrinterAsAnImage()
    {
        // Over the limit a driver would take the page one path at a time, many times slower than one
        // image of it, so the renderer draws nothing and the job prints the page as an image.
        var (asCommands, fillRecords) = PrintToMetafile(PageOfFills(1500));
        Assert.False(asCommands);
        Assert.Equal(0, fillRecords);
    }

    [WindowsOnlyFact]
    public void TheNativeSaveRestoresTheTransformAndTheClip()
    {
        using var bitmap = new Bitmap(200, 200);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.TranslateTransform(10, 20);
        graphics.SetClip(new System.Drawing.Rectangle(0, 0, 50, 60));
        var transform = graphics.Transform.Elements;
        var clip = graphics.ClipBounds;

        Assert.True(NativeGraphicsState.TrySave(graphics, out var state));
        graphics.ScaleTransform(3, 3);
        graphics.SetClip(new System.Drawing.Rectangle(5, 5, 10, 10), CombineMode.Intersect);
        Assert.NotEqual(clip, graphics.ClipBounds);

        NativeGraphicsState.Restore(graphics, state);
        Assert.Equal(transform, graphics.Transform.Elements);
        Assert.Equal(clip, graphics.ClipBounds);
    }
}
