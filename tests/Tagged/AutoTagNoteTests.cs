using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of footnotes: small print at the foot of a column opening with a note
/// label is a Note, read after the page's text, and a paragraph the page breaks goes on past it.</summary>
public class AutoTagNoteTests
{
    /// <summary>Letter pages drawing <paramref name="pages"/> with F1 Helvetica.</summary>
    private static byte[] Build(params string[] pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(" ", pages.Select((_, i) => $"{4 + 2 * i} 0 R"))}] /Count {pages.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        foreach (var content in pages)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {objects.Count + 2} 0 R " +
                        "/Resources << /Font << /F1 3 0 R >> >> >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
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

    private static readonly string[] BodyLines =
    {
        "Amber meadows lie beyond the northern fjord where willow and juniper grow along",
        "the quiet valley floor and the slopes above it, and the river turns east under",
        "the ridge before it reaches the lake, whose shore the road follows for a mile",
        "until the village, where the bridge crosses to the mill and the old orchard on",
        "the far bank, with its rows of apple trees and the beehives by the wall, and",
    };

    /// <summary>Body lines of 12 pt from (72, <paramref name="top"/>) down at a 14 pt pitch.</summary>
    private static string Body(double top, params string[] lines)
        => $"BT /F1 12 Tf 72 {top} Td " + string.Join(" 0 -14 Td ", lines.Select(l => $"({l}) Tj")) + " ET\n";

    private const string Footnote = "BT /F1 8 Tf 72 100 Td (1 The code and the models are available at the project page.) Tj ET\n";
    private const string NextPage = "then the road climbs again toward the pass and the huts above the tree line.";

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        var options = new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        };
        doc.Convert(options);
        return doc;
    }

    private static string Text(StructureElement el) => string.Concat(el.GetMarkedContent(true).Select(i => i.Text));

    [Fact]
    public void AFooterOfTheOddPagesAndAnotherOfTheEvenOnesAreBothRunningLines()
    {
        // Five pages: the first with a footer of its own; on the others the title stands at the left of the odd
        // pages and at the right of the even ones, the page's number at the other side - each on two pages only.
        string Page(int n) => Body(700, BodyLines) + (n == 1 ? "BT /F1 9 Tf 250 30 Td (Survey office, the county) Tj ET\n"
            : n % 2 == 1 ? $"BT /F1 9 Tf 72 30 Td (Field notes of the season) Tj ET\nBT /F1 9 Tf 530 30 Td ({n}) Tj ET\n"
            : $"BT /F1 9 Tf 72 30 Td ({n}) Tj ET\nBT /F1 9 Tf 420 30 Td (Field notes of the season) Tj ET\n");
        using var doc = Tag(Build(Enumerable.Range(1, 5).Select(Page).ToArray()));

        var content = Text(doc.TaggedContent.StructTreeRootElement);
        Assert.DoesNotContain("Field notes", content);
        Assert.Contains("Amber meadows", content);
    }

    [Fact]
    public void AFootnoteIsANoteReadAfterTheTextThePageBreaks()
    {
        using var doc = Tag(Build(Body(700, BodyLines) + Footnote, Body(700, NextPage)));
        var root = doc.TaggedContent.StructTreeRootElement;
        var note = Assert.Single(root.FindElements<NoteElement>(true));
        Assert.Equal("1 The code and the models are available at the project page.", Text(note));
        var paragraph = Assert.Single(root.FindElements<ParagraphElement>(true));
        Assert.StartsWith("Amber meadows", Text(paragraph));
        Assert.EndsWith("by the wall, and then the road climbs again toward the pass and the huts above the tree line.", Text(paragraph));
        var blocks = root.FindElements<StructureElement>(true).Where(e => e is NoteElement or ParagraphElement).ToList();
        Assert.Equal(new[] { "P", "Note" }, blocks.Select(e => e.S.Name));
    }

    // Notes hanging their wrapped lines in spaces, all lines starting at one edge, and a legend set out left under them.
    private const string SpacedNotes = "BT /F1 8 Tf 72 140 Td"
        + " ( 1.  The code and the models are available at the project page, with the data the tables of this) Tj"
        + " 0 -9 Td (      paper were computed from and the scripts that computed them.) Tj"
        + " 0 -9 Td ( 2.  Measured on the second of the two machines described in the section on the setup.) Tj"
        + " 0 -9 Td ( 3.  The survey ran over two summers, the second of them after the station was moved to the) Tj"
        + " 0 -9 Td (      ridge above the lake, where the wind is steadier and the snow melts later.) Tj"
        + " -22 -9 Td (r=revised.  n.a.=not available.) Tj ET\n";

    [Fact]
    public void FootnotesHangingInSpacesAreANoteEachWithTheirLegendAfterThem()
    {
        using var doc = Tag(Build(Body(700, BodyLines.Concat(BodyLines).ToArray()) + SpacedNotes, Body(700, NextPage)));
        var root = doc.TaggedContent.StructTreeRootElement;
        var notes = root.FindElements<NoteElement>(true).Select(Text).ToList();
        Assert.Equal(3, notes.Count);
        Assert.StartsWith(" 1.", notes[0]);
        Assert.Contains("computed them.", notes[0]);
        Assert.StartsWith(" 2.", notes[1]);
        Assert.StartsWith(" 3.", notes[2]);
        Assert.EndsWith("melts later.", notes[2].TrimEnd());
        var blocks = root.FindElements<StructureElement>(true).Where(e => e is NoteElement or ParagraphElement).ToList();
        Assert.Equal(new[] { "P", "Note", "Note", "Note", "P" }, blocks.Select(e => e.S.Name));
        Assert.StartsWith("r=revised.", Text(blocks[^1]));
    }

    [Fact]
    public void SmallPrintWithBodyTextBelowItIsNoNote()
    {
        var note = "BT /F1 8 Tf 72 400 Td (1 A remark set small between the body's paragraphs.) Tj ET\n";
        using var doc = Tag(Build(Body(700, BodyLines) + note + Body(300, BodyLines)));
        var root = doc.TaggedContent.StructTreeRootElement;
        Assert.Empty(root.FindElements<NoteElement>(true));
        Assert.Contains(root.FindElements<ParagraphElement>(true), p => Text(p).StartsWith("1 A remark"));
    }
}
