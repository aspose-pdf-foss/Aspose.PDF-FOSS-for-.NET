using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging reads a rule by the colour it is painted in: white on the white page (a text box's outline) rules
/// nothing, white on a grey ground does, and an ink named by a colour space (a /Separation's "1 SCN") is the colour it prints.</summary>
public class AutoTagPaintColourTests
{
    /// <summary>A Letter page drawing <paramref name="content"/> with F1 Times-Roman and CS0, a black ink (a /Separation
    /// printing tint 1 as CMYK black).</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding /WinAnsiEncoding >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /F1 3 0 R >> "
                + "/ColorSpace << /CS0 [/Separation /Ink /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 0 0 1] /N 1 >>] >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        };
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Compat.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static string Text(double x, double y, string text, double size = 12)
        => $"BT /F1 {size} Tf {x} {y} Td ({text}) Tj ET\n";

    // Three lines of a paragraph, the first standing 5 pt under the line the paint draws at 684.
    private static readonly string Lines = Text(80, 670, "Special Publication 800-38D") + Text(80, 656, "the second line of the block")
        + Text(80, 642, "and the third line of the block") + Text(80, 400, "A paragraph further down the page, far from the rule.");

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    /// <summary>The rules stated over the paragraph starting "Special": its /BorderThickness, null for none.</summary>
    private static double?[]? RulesOverTheBlock(Document doc)
    {
        var block = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true)
            .First(e => (e.S.Name == "P" || e.S.Name.StartsWith("H"))
                        && string.Concat(e.GetMarkedContent(true).Select(i => i.Text)).TrimStart().StartsWith("Special"));
        return block.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BorderThickness)?.GetArrayNumberValue();
    }

    [Fact]
    public void AWhiteOutlineOnThePageRulesNothing()
    {
        // A text box's outline and ground, both white, round the block.
        using var doc = Tag(Build("1 g 72 525 202 159 re f 1 G 0.75 w 72 525 202 159 re S\n" + Lines));
        Assert.Null(RulesOverTheBlock(doc));
    }

    [Fact]
    public void AWhiteLineOnAGreyGroundRulesTheBlockUnderIt()
    {
        // A grey band over the block, a white line across it.
        using var doc = Tag(Build("0.6 g 60 684 240 40 re f 1 G 0.75 w 72 690 m 274 690 l S\n" + Lines));
        Assert.NotNull(RulesOverTheBlock(doc));
    }

    [Fact]
    public void ATextBoxsWhiteGroundDrawsNoPartOfTheSealInsideIt()
    {
        // A text box - white ground, white outline - holding an address and, under it, a seal of many small shapes.
        var seal = new System.Text.StringBuilder("0 g\n");
        for (var k = 0; k < 40; k++)
        {
            var a = 2 * System.Math.PI * k / 40;
            seal.Append(System.FormattableString.Invariant($"{316 + 30 * System.Math.Cos(a):0.##} {240 + 30 * System.Math.Sin(a):0.##} 3 3 re f\n"));
        }
        var address = Text(281, 407, "Computer Security Division", 11) + Text(281, 394, "Information Technology Laboratory", 11)
                      + Text(281, 382, "Gaithersburg, MD 20899-8930", 11) + Text(281, 141, "U.S. Department of Commerce", 9);
        using var doc = Tag(Build("1 g 274 62 280 387 re f 1 G 0.75 w 274 62 280 387 re S\n" + seal + address));
        // The address is text of the page: no figure takes it in as its labels.
        var address0 = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true)
            .First(e => string.Concat(e.GetMarkedContent(true).Select(i => i.Text)).Contains("Computer Security Division") && e.S.Name != "Document"
                        && e.S.Name != "Part" && e.S.Name != "Sect");
        Assert.NotEqual("Figure", address0.S.Name);
        Assert.DoesNotContain(doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true),
            e => e.S.Name == "Figure" && string.Concat(e.GetMarkedContent(true).Select(i => i.Text)).Contains("Computer"));
    }

    [Fact]
    public void ALineInAnInkNamedByAColourSpaceRulesTheBlockUnderIt()
    {
        using var doc = Tag(Build("/CS0 CS 1 SCN 0.75 w 72 684 m 274 684 l S\n" + Lines));
        Assert.NotNull(RulesOverTheBlock(doc));
    }
}
