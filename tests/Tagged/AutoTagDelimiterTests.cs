using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of a formula whose tall brace is drawn in pieces - its top on one row, its bottom on the row
/// under it, its middle between with the label the cases define beside it: the lines the brace spans are one Formula.</summary>
public class AutoTagDelimiterTests
{
    // The brace pieces of the Symbol face as the characters they are, and its space and equals sign.
    private const string BraceMap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CMapName /Brace def "
        + "1 begincodespacerange <00> <FF> endcodespacerange 5 beginbfchar <20> <0020> <3D> <003D> <EC> <23A7> <ED> <23A8> <EE> <23A9> endbfchar "
        + "endcmap CMapName currentdict /CMap defineresource pop end end";

    /// <summary>A Letter page drawing <paramref name="content"/> with F1 Courier and F4 Symbol.</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [5 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Symbol /ToUnicode 7 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 6 0 R /Resources << /Font << /F1 3 0 R /F4 4 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            $"<< /Length {BraceMap.Length} >>\nstream\n{BraceMap}\nendstream",
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

    private static string Show(string font, double size, double x, double y, string text)
        => System.FormattableString.Invariant($"BT /{font} {size} Tf {x} {y} Td ({text}) Tj ET\n");

    // The cases' rows 18 pt apart, the label and the brace's middle midway, a paragraph before and after.
    private static string Cases()
        => Show("F1", 12, 72, 760, "For i = 0 to 127, calculate the blocks Z and V as follows, in turn,")
           + Show("F1", 12, 72, 746, "each from the one before it, until the last of them is reached.")
           + Show("F4", 12, 229.6, 710, "\\354") + Show("F1", 12, 239, 709, "Z") + Show("F1", 7, 246.2, 706, "i") + Show("F1", 12, 324, 709, "if x = 0;")
           + Show("F1", 12, 200, 700, "Z") + Show("F1", 7, 207.2, 697, "i+1") + Show("F4", 12, 216.7, 699.9, " = \\355")
           + Show("F4", 12, 226.6, 688.6, " \\356") + Show("F1", 12, 236.1, 691, "Z") + Show("F1", 7, 243.3, 688, "i")
           + Show("F1", 12, 252, 691, "+ V") + Show("F1", 12, 324, 691, "if x = 1.")
           + Show("F1", 12, 72, 664, "The operation on the blocks is the multiplication of a binary field,")
           + Show("F1", 12, 72, 650, "and the reduction is by the polynomial the field is defined with.");

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    private static string TextOf(StructureElement el) => string.Concat(el.GetMarkedContent(true).Select(i => i.Text));

    [Fact]
    public void TheLinesAPiecedBraceSpansAreOneFormula()
    {
        using var doc = Tag(Build(Cases()));
        var formulas = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).Where(e => e.S.Name == "Formula").ToList();
        var formula = Assert.Single(formulas, f => TextOf(f).Contains('⎧'));
        // Its rows and what stands between them: the brace's three pieces, the label, both cases.
        var text = TextOf(formula);
        foreach (var piece in new[] { "⎧", "⎨", "⎩", "if x = 0;", "if x = 1.", "i+1" })
            Assert.Contains(piece, text);
    }
}
