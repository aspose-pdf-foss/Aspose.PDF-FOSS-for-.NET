using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of display formulas: a short line set apart in a math face, with its
/// indices raised and lowered, is a Formula stating its box; prose with a symbol in it is not.</summary>
public class AutoTagFormulaTests
{
    /// <summary>A Letter page with F1 Helvetica and F2 a (non-embedded) CMMI10, TeX's math italic.</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [5 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /CMMI10 /Encoding /WinAnsiEncoding >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 6 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> >>",
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

    private static readonly string[] BodyLines =
    {
        "Amber meadows lie beyond the northern fjord where willow and juniper grow along",
        "the quiet valley floor and the slopes above it, and the river turns east under",
        "the ridge before it reaches the lake, whose shore the road follows for a mile.",
    };

    private static string Body(double top)
        => $"BT /F1 12 Tf 72 {top} Td " + string.Join(" 0 -14 Td ", BodyLines.Select(l => $"({l}) Tj")) + " ET\n";

    /// <summary>A centred formula in the math face with a raised index and a lowered one, numbered at the margin.</summary>
    private const string Formula =
        "BT /F2 12 Tf 240 620 Td (f) Tj /F2 8 Tf 3 5 Td (n) Tj -3 -5 Td /F2 12 Tf 10 0 Td (x = a) Tj "
        + "/F2 8 Tf 2 -4 Td (k) Tj /F2 12 Tf 10 4 Td (+ b) Tj /F1 12 Tf 200 0 Td ((1)) Tj ET\n";

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    private static string Text(StructureElement el) => string.Concat(el.GetMarkedContent(true).Select(i => i.Text));

    [Fact]
    public void ADisplayFormulaIsAFormulaStatingItsBox()
    {
        using var doc = Tag(Build(Body(700) + Formula + Body(580)));
        var root = doc.TaggedContent.StructTreeRootElement;
        var formula = Assert.Single(root.FindElements<FormulaElement>(true));
        Assert.Contains("x = a", Text(formula));
        var box = formula.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BBox)?.GetArrayNumberValue();
        Assert.NotNull(box);
        Assert.InRange(box![0]!.Value, 235, 245);
        Assert.InRange(box[1]!.Value, 610, 620);
        Assert.Equal(2, root.FindElements<ParagraphElement>(true).Count);
    }

    [Fact]
    public void ASumSetOutInLinesWithARuleUnderItsOperandIsOneFormula()
    {
        // Three short lines midway across the text: a quantity, its factor after the sign with a rule under both,
        // and the product under the rule.
        // (the widest line of the text runs from 72 to 511: the sum, from 244 to 338, stands midway across it)
        const string sum = "BT /F1 9 Tf 264 620 Td (line 5a  \\(column 1\\)) Tj ET\n"
                           + "BT /F1 9 Tf 244 608 Td (x      0.124) Tj ET\n0.5 w 244 606 m 294 606 l S\n"
                           + "BT /F1 9 Tf 264 596 Td (line 5a  \\(column 2\\)) Tj ET\n";
        using var doc = Tag(Build(Body(700) + sum + Body(560)));
        var root = doc.TaggedContent.StructTreeRootElement;
        var formula = Assert.Single(root.FindElements<FormulaElement>(true));
        var text = string.Join(" ", Text(formula).Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal("line 5a (column 1) x 0.124 line 5a (column 2)", text);
        var box = formula.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.BBox)?.GetArrayNumberValue();
        Assert.NotNull(box);
        Assert.InRange(box![0]!.Value, 242, 246);
        Assert.Equal("Center", formula.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.TextAlign)?.GetNameValue()?.ToString());
        Assert.InRange(box[1]!.Value, 590, 597);
        Assert.InRange(box[3]!.Value, 626, 632);
        Assert.Equal(2, root.FindElements<ParagraphElement>(true).Count);
        // The rule under the operand is the formula's content.
        var rule = Assert.Single(formula.GetMarkedContent(true), i => i.Kind == MarkedContentKind.Drawing);
        Assert.InRange(rule.Rectangle.LLX, 243, 245);
        Assert.InRange(rule.Rectangle.URX, 293, 295);
    }

    [Fact]
    public void ASumAtTheTopOfAColumnIsNoPartOfTheParagraphRunningOnIntoTheColumn()
    {
        // Two columns of 9 pt text: the left one's last line runs full and ends no sentence, the right one opens
        // with a sum - its first line in lower case - and goes on with text.
        static string Line(double x, double y, string text) => $"BT /F1 9 Tf {x} {y} Td ({text}) Tj ET\n";
        const string full = "the survey went on over the hills and along the river";
        var left = string.Concat(Enumerable.Range(0, 6).Select(i => Line(42, 700 - 12 * i, full)));
        var sum = Line(380, 700, "line 5d  \\(column 1\\)") + Line(360, 688, "x      0.009") + "0.5 w 360 686 m 410 686 l S\n"
                  + Line(380, 676, "line 5d  \\(column 2\\)");
        var right = string.Concat(Enumerable.Range(0, 5).Select(i => Line(306, 640 - 12 * i, "Then " + full.Substring(4, 44) + ".")));
        using var doc = Tag(Build(left + sum + right));
        var root = doc.TaggedContent.StructTreeRootElement;

        var formula = Assert.Single(root.FindElements<FormulaElement>(true));
        Assert.Equal("line 5d (column 1) x 0.009 line 5d (column 2)",
            string.Join(" ", Text(formula).Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries)));
        Assert.DoesNotContain(root.FindElements<ParagraphElement>(true).Cast<StructureElement>(), p => Text(p).Contains("line 5d"));
    }

    [Fact]
    public void ARuleUnderProseMakesNoFormula()
    {
        // A short rule under a word of a paragraph's first line (an underline), and no sign before a figure.
        using var doc = Tag(Build(Body(700) + "0.5 w 72 698 m 120 698 l S\n"));
        Assert.Empty(doc.TaggedContent.StructTreeRootElement.FindElements<FormulaElement>(true));
    }

    [Fact]
    public void ProseWithASymbolInItIsNoFormula()
    {
        var prose = "BT /F1 12 Tf 72 640 Td (The rate is ) Tj /F2 12 Tf (x) Tj /F1 12 Tf ( for every one of the harbour towns along the northern coast.) Tj ET\n";
        using var doc = Tag(Build(Body(700) + prose));
        Assert.Empty(doc.TaggedContent.StructTreeRootElement.FindElements<FormulaElement>(true));
    }
}
