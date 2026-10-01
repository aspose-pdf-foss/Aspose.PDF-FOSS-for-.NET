using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of a list of variables set as a word processor sets one: each term, then its definition at 180 pt,
/// the spaces opening the definition's string stretched out to it. The definition's text stands where its glyphs do, not
/// where its stretched spaces start.</summary>
public class AutoTagGlossaryTests
{
    private static readonly string Widths = "/FirstChar 32 /LastChar 126 /Widths [" + string.Join(" ", Enumerable.Repeat("600", 95)) + "]";

    /// <summary>A Letter page drawing <paramref name="content"/> with F1 Courier and F2 Courier-Oblique, their widths stated.</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [5 0 R] /Count 1 >>",
            $"<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding {Widths} >>",
            $"<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Oblique /Encoding /WinAnsiEncoding {Widths} >>",
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

    // 12 pt Courier: 7.2 pt a character; entries 23 pt apart, a blank line between.
    private const double Char = 7.2;

    private static string Show(string font, double x, double y, string text)
        => System.FormattableString.Invariant($"BT /{font} 12 Tf {x} {y} Td ({text}) Tj ET\n");

    private static string Blank(double y) => Show("F1", 72, y - 20.7, " ");

    /// <summary>A one-line entry whose definition's string opens with three spaces stretched by word spacing out to 180 pt
    /// ("IV   Nonce.", as a word processor sets a list of variables).</summary>
    private static string Stretched(double y, string term, string definition)
    {
        var at = 72 + Char * term.Length + 3;
        return Show("F2", 72, y, term)
               + System.FormattableString.Invariant($"BT /F1 1 Tf {((180 - at) / 3 - Char) / 12:0.####} Tw 12 0 0 12 {at} {y} Tm (   {definition}) Tj 0 Tw ET\n")
               + Blank(y + 6.9);
    }

    private static string Variables()
        => Show("F1", 72, 760, "4.2.1 Variables") + Stretched(740, "H", "Subkey.") + Stretched(717, "IV", "Nonce.");

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    [Fact]
    public void TheSpacesStretchedOutToADefinitionAreNotItsText()
    {
        using var doc = Tag(Build(Variables()));
        foreach (var definition in new[] { "Subkey.", "Nonce." })
        {
            // The definition's item is its text, not the blanks stretched out from its term: it starts past the first of
            // them (each 30 pt wide), not right after the term.
            var item = doc.TaggedContent.StructTreeRootElement.GetMarkedContent(true).First(i => i.Text.Contains(definition, System.StringComparison.Ordinal));
            Assert.InRange(item.Rectangle.LLX, 115, 182);
        }
    }
}
