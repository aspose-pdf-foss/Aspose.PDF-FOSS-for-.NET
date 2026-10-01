using System.Collections.Generic;
using System.IO;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A ligature glyph whose ToUnicode gives only its first letter, and text a TextBuilder
/// adds to a page after operators were added through <see cref="Page.Contents"/>.</summary>
public class TruncatedLigatureAndLateTextTests
{
    /// <summary>A one-page document showing <paramref name="content"/> in F1 (Helvetica whose code 2
    /// is the fi glyph) with a ToUnicode that maps code 2 to <paramref name="code2Unicode"/>.</summary>
    private static byte[] Build(string content, string code2Unicode)
    {
        var cmap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n" +
                   "/CMapName /Test def /CMapType 2 def\n1 begincodespacerange <00> <FF> endcodespacerange\n" +
                   $"3 beginbfchar\n<02> <{code2Unicode}>\n<64> <0064>\n<65> <0065>\nendbfchar\n" +
                   "endcmap CMapName currentdict /CMap defineresource pop end end";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding << /Differences [2 /fi] >> /ToUnicode 6 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 5 0 R /Resources << /Font << /F1 3 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            $"<< /Length {cmap.Length} >>\nstream\n{cmap}\nendstream",
        };
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s), 0, s.Length);
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

    private static string Extract(byte[] pdf)
    {
        using var doc = new Document(new MemoryStream(pdf));
        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages[1]);
        return absorber.Text;
    }

    [Fact]
    public void ALigatureGlyphMappedToItsFirstLetterOnlyReadsAsTheLigature()
    {
        // The producer's ToUnicode maps the fi glyph to "f": "de<fi>ned" read "defned".
        Assert.Contains("deﬁned", Extract(Build("BT /F1 12 Tf 72 700 Td (de\\002ned) Tj ET", "0066")));
    }

    [Fact]
    public void AGlyphMappedToAnotherSingleLetterKeepsTheMapping()
    {
        // "i" is not the first letter of fi: the ToUnicode mapping is taken as given.
        Assert.Contains("deined", Extract(Build("BT /F1 12 Tf 72 700 Td (de\\002ned) Tj ET", "0069")));
    }

    [Fact]
    public void TextBuiltAfterOperatorsAddedThroughContentsIsSavedAfterThem()
    {
        var doc = new Document();
        var page = doc.Pages.Add();
        page.Contents.Add(new GSave());
        page.Contents.Add(new Re(50, 50, 100, 100));
        page.Contents.Add(new Stroke());
        page.Contents.Add(new GRestore());
        new TextBuilder(page).AppendText(new TextFragment("Late text") { Position = new Position(100, 600) });
        page.Contents.Add(new Re(60, 60, 10, 10));
        using var ms = new MemoryStream();
        doc.Save(ms);

        using var saved = new Document(new MemoryStream(ms.ToArray()));
        var absorber = new TextAbsorber();
        saved.Pages[1].Accept(absorber);
        Assert.Contains("Late text", absorber.Text);
        // Drawn in the order they were added: the rectangle, the text, the second rectangle.
        var kinds = new List<string>();
        foreach (var op in saved.Pages[1].Contents)
            if (op is Re or ShowText) kinds.Add(op is Re ? "re" : "text");
        Assert.Equal(["re", "text", "re"], kinds);
    }
    [Fact]
    public void TextRecolouredAfterItsOperatorsWereEditedIsDrawnOnce()
    {
        // Operators edited through Contents, then a found phrase recoloured (which rewrites the
        // page's content whole): the text is drawn once, not once per copy of the content.
        var doc = new Document();
        var page = doc.Pages.Add();
        new TextBuilder(page).AppendText(new TextFragment("STUDY GUIDE") { Position = new Position(100, 600) });
        using (var first = new MemoryStream())
        {
            doc.Save(first);
            doc = new Document(new MemoryStream(first.ToArray()));
        }
        page = doc.Pages[1];
        var modes = page.Contents.OfType<SetTextRenderingMode>().ToList();
        page.Contents.Delete(modes.Cast<Operator>().ToList());
        page.Contents.Insert(1, new SetRGBColor(0, 0, 0));
        var absorber = new TextFragmentAbsorber("STUDY");
        page.Accept(absorber);
        foreach (TextFragment fragment in absorber.TextFragments)
            fragment.TextState.ForegroundColor = Color.Blue;
        using var ms = new MemoryStream();
        doc.Save(ms);

        using var saved = new Document(new MemoryStream(ms.ToArray()));
        var shows = saved.Pages[1].Contents.OfType<TextShowOperator>().Count();
        var reader = new TextAbsorber();
        saved.Pages[1].Accept(reader);
        Assert.Contains("STUDY", reader.Text);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(reader.Text, "GUIDE"));
        Assert.True(shows <= 2, $"text drawn {shows} times");
    }
}
