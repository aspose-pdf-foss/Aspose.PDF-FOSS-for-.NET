using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of a document carrying an outline: the lines its items name - on the pages they lead to - are
/// headings at the items' levels, whatever their size or weight; a table of contents naming them on its own page is no
/// heading, and a title wrapped over two lines is one heading.</summary>
public class AutoTagOutlineTests
{
    /// <summary>Letter pages drawing <paramref name="pages"/> (F1 Times-Roman, F2 Helvetica-Bold), with an outline of
    /// <paramref name="items"/>: each a title, its level (1 or 2: under the last level-1 item) and the page it leads to.</summary>
    private static byte[] Build(string[] pages, (string Title, int Level, int Page)[] items)
    {
        // 1 catalog, 2 pages, 3-4 fonts, 5 outlines, then the pages and their contents, then the items.
        var pageObj = Enumerable.Range(0, pages.Length).Select(i => 6 + 2 * i).ToList();
        var first = 6 + 2 * pages.Length;
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 5 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(" ", pageObj.Select(p => $"{p} 0 R"))}] /Count {pages.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
        };
        var tops = Enumerable.Range(0, items.Length).Where(k => items[k].Level == 1).ToList();
        objects.Add($"<< /Type /Outlines /First {first + tops[0]} 0 R /Last {first + tops[^1]} 0 R /Count {items.Length} >>");
        for (var i = 0; i < pages.Length; i++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {pageObj[i] + 1} 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> >>");
            objects.Add($"<< /Length {pages[i].Length} >>\nstream\n{pages[i]}\nendstream");
        }
        for (var k = 0; k < items.Length; k++)
        {
            var (title, level, page) = items[k];
            var siblings = Enumerable.Range(0, items.Length).Where(j => items[j].Level == level
                && (level == 1 || tops.Last(t => t < j) == tops.Last(t => t < k))).ToList();
            var at = siblings.IndexOf(k);
            var parent = level == 1 ? 5 : first + tops.Last(t => t < k);
            var kids = level == 1 ? Enumerable.Range(k + 1, items.Length - k - 1).TakeWhile(j => items[j].Level == 2).ToList() : [];
            objects.Add($"<< /Title ({title}) /Parent {parent} 0 R /Dest [{pageObj[page - 1]} 0 R /XYZ 0 792 0]"
                        + (at > 0 ? $" /Prev {first + siblings[at - 1]} 0 R" : "") + (at + 1 < siblings.Count ? $" /Next {first + siblings[at + 1]} 0 R" : "")
                        + (kids.Count > 0 ? $" /First {first + kids[0]} 0 R /Last {first + kids[^1]} 0 R /Count {kids.Count}" : "") + " >>");
        }
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

    private static string Line(double x, double y, string text, string font = "F1", double size = 12)
        => System.FormattableString.Invariant($"BT /{font} {size} Tf {x} {y} Td ({text}) Tj ET\n");

    /// <summary>Lines of 12 pt prose from (72, top) down, 14 pt apart.</summary>
    private static string Prose(double top, int lines, string tag)
        => string.Concat(Enumerable.Range(0, lines).Select(i =>
            Line(72, top - 14 * i, $"The survey of the ridge went on through the summer and the autumn months {tag}{i}")));

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true, HeadingRecognitionStrategy = HeadingRecognitionStrategy.Auto },
        });
        return doc;
    }

    private static string TextOf(StructureElement el) => string.Concat(el.GetMarkedContent(true).Select(i => i.Text)).Trim();

    private static List<StructureElement> Elements(Document doc) =>
        doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).ToList();

    // A contents page naming the sections, then the body: its chapter heading bold at the body's size, its sections'
    // headings bold and smaller than the body - their number standing apart from their title - the second wrapped.
    private static readonly string[] Pages =
    [
        Line(72, 720, "Contents", "F2", 12) + Line(72, 696, "1 Introduction") + Line(90, 682, "1.1 History of the Survey")
        + Line(90, 668, "1.2 Structure of This Report and the Ways Its Parts") + Line(108, 654, "Are Read"),
        Line(72, 720, "1 Introduction", "F2", 12) + Prose(696, 4, "A")
        + Line(72, 620, "1.1", "F2", 11) + Line(100.8, 620, "History of the Survey", "F2", 11) + Prose(596, 4, "B")
        + Line(72, 520, "1.2", "F2", 11) + Line(100.8, 520, "Structure of This Report and the Ways Its Parts", "F2", 11)
        + Line(100.8, 506, "Are Read", "F2", 11) + Prose(482, 4, "C"),
    ];

    private static readonly (string, int, int)[] Outline =
    [
        ("1 Introduction", 1, 2), ("1.1 History of the Survey", 2, 2), ("1.2 Structure of This Report and the Ways Its Parts Are Read", 2, 2),
    ];

    [Fact]
    public void LinesTheOutlineNamesAreHeadingsAtItsLevelsWhateverTheirSize()
    {
        using var doc = Tag(Build(Pages, Outline));
        var headings = Elements(doc).Where(e => e.S.Name is "H1" or "H2" or "H3" or "H4" or "H5" or "H6")
            .Select(e => (e.S.Name, Text: TextOf(e))).ToList();
        Assert.Contains(("H1", "1 Introduction"), headings);
        Assert.Contains(("H2", "1.1 History of the Survey"), headings);
    }

    [Fact]
    public void ATitleWrappedOverTwoLinesIsOneHeading()
    {
        using var doc = Tag(Build(Pages, Outline));
        var heading = Assert.Single(Elements(doc), e => e.S.Name == "H2" && TextOf(e).StartsWith("1.2", System.StringComparison.Ordinal));
        Assert.Equal("1.2 Structure of This Report and the Ways Its Parts Are Read", TextOf(heading));
    }

    [Fact]
    public void AContentsPageNamingTheTitlesOnAPageOfItsOwnIsNoHeading()
    {
        using var doc = Tag(Build(Pages, Outline));
        // The contents lines stand on page 1; the outline leads to page 2.
        Assert.DoesNotContain(Elements(doc), e => e.S.Name is "H1" or "H2" && e.GetMarkedContent(true).Any(i => i.Page?.Number == 1)
                                                  && TextOf(e).Contains("History", System.StringComparison.Ordinal));
    }
}
