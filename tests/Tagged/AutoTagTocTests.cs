using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of a table of contents: its entries - a number, a title, leader dots and the page they lead to,
/// the pages ending at one edge - are a TOC of TOCIs, each read whole, never taken as a table, a column of numbers or notes.</summary>
public class AutoTagTocTests
{
    /// <summary>A Letter page drawing <paramref name="content"/> with F1 Helvetica, F2 Helvetica-Bold and F3 Times-Italic.</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [5 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 6 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 7 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Italic /Encoding /WinAnsiEncoding >>",
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

    private static string Text(string font, double size, double x, double y, string text)
        => System.FormattableString.Invariant($"BT /{font} {size} Tf {x} {y} Td ({text}) Tj ET\n");

    // One entry: its number where its level sets it, its title further in, leader dots and its page ending at 540 pt
    // (a digit of 10 pt Helvetica is 5.56 pt wide).
    private static string Entry(double y, string font, double numberX, string number, double titleX, string title, string page)
        => Text(font, 10, numberX, y, number) + Text(font, 10, titleX, y, title)
           + Text("F1", 10, 300, y, new string('.', 60)) + Text("F1", 10, 540 - 5.56 * page.Length, y, page);

    /// <summary>A table of contents: level-one entries in bold, their numbers at the margin; level-two entries set in.</summary>
    internal static string Contents()
        => Text("F1", 14, 72, 707, "Table of Contents")
           + Entry(688, "F2", 72, "1", 96, "PURPOSE", "1") + Entry(671, "F2", 72, "2", 96, "AUTHORITY", "1")
           + Entry(654, "F2", 72, "3", 96, "DEFINITIONS AND SYMBOLS", "2")
           + Entry(637, "F1", 84, "3.1", 120, "Definitions", "2") + Entry(625, "F1", 84, "3.2", 120, "Symbols", "5")
           + Entry(608, "F2", 72, "4", 96, "ELEMENTS", "7") + Entry(591, "F1", 84, "4.1", 120, "Block Cipher", "7")
           + Entry(579, "F1", 96, "4.1.1", 132, "Operations and Functions", "8")
           + Text("F1", 12, 72, 500, "The body of the page goes on under the contents, a paragraph of its own.");

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    private static string TextOf(StructureElement el) => string.Concat(el.GetMarkedContent(true).Select(i => i.Text)).Trim();

    [Fact]
    public void EntriesEndingInLeaderDotsAndAPageAreATocOfTociReadWhole()
    {
        using var doc = Tag(Build(Contents()));
        var all = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).ToList();
        var toc = Assert.Single(all, e => e.S.Name == "TOC");
        var entries = toc.FindElements<StructureElement>(false).Where(e => e.S.Name == "TOCI").ToList();
        Assert.Equal(8, entries.Count);
        // Each entry holds its number, its title and its page, in that order.
        Assert.StartsWith("1", TextOf(entries[0]));
        Assert.Contains("PURPOSE", TextOf(entries[0]));
        Assert.StartsWith("3.2", TextOf(entries[4]));
        Assert.EndsWith("5", TextOf(entries[4]));
        // No table, no notes: the numbers stand in their entries.
        Assert.DoesNotContain(all, e => e.S.Name is "Table" or "Note");
        // The body under the contents is no entry.
        Assert.DoesNotContain("The body", TextOf(toc));
    }

    [Fact]
    public void AnEntryIsSetFromItsStartNeverFlushRight()
    {
        using var doc = Tag(Build(Contents()));
        var entries = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).Where(e => e.S.Name == "TOCI").ToList();
        Assert.NotEmpty(entries);
        foreach (var entry in entries)
            Assert.Null(entry.Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.TextAlign));
        // A level-two entry states how far in its level sets it.
        var set = entries.First(e => TextOf(e).StartsWith("3.1")).Attributes.GetAttributes(AttributeOwnerStandard.Layout).GetAttribute(AttributeKey.StartIndent);
        Assert.NotNull(set);
        Assert.InRange(set!.GetNumberValue() ?? 0, 10, 14);
    }

    [Fact]
    public void NumberedHeadingsOneUnderAnotherAreNoTable()
    {
        // A section's heading and its first subsection's right under it, each a number set apart from its title, in bold.
        var page = Text("F1", 12, 72, 740, "The designers of the mode submitted it, and they discuss its security in detail.")
                   + Text("F2", 12, 72, 700, "4") + Text("F2", 12, 90, 700, "Definitions, Abbreviations, and Symbols")
                   + Text("F2", 12, 72, 680, "4.1") + Text("F2", 12, 108, 680, "Definitions and Abbreviations")
                   + Text("F1", 12, 72, 650, "The terms used in this document are defined as follows, in their order.");
        using var doc = Tag(Build(page));
        var all = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).ToList();
        Assert.DoesNotContain(all, e => e.S.Name == "Table");
        Assert.Contains(all, e => (e.S.Name == "P" || e.S.Name.StartsWith("H")) && TextOf(e).Contains("4.1 Definitions and Abbreviations"));
    }

    [Fact]
    public void NumberedHeadingsInItalicsOneUnderAnotherAreNoTable()
    {
        // A subsection's heading and its first part's under it, a blank line between, each a number set apart from its
        // title, in italics.
        var page = Text("F3", 12, 72, 700, "5.2.1") + Text("F3", 12, 97.5, 700, " ") + Text("F3", 12, 108, 700, "Authenticated Encryption Function")
                   + Text("F1", 12, 72, 686.2, " ") + Text("F3", 12, 72, 672.4, "5.2.1.1") + Text("F3", 12, 108.5, 672.4, " ") + Text("F3", 12, 115.2, 672.4, "Input Data")
                   + Text("F1", 12, 72, 658.6, " ") + Text("F1", 12, 72, 644.8, "Given the selection of an approved block cipher and key, there are three inputs.");
        using var doc = Tag(Build(page));
        var all = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).ToList();
        Assert.DoesNotContain(all, e => e.S.Name == "Table");
        Assert.Contains(all, e => (e.S.Name == "P" || e.S.Name.StartsWith("H")) && TextOf(e).Contains("5.2.1.1 Input Data"));
    }
}
