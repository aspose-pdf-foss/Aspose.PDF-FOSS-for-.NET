using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.Core;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of content drawn through form XObjects - a page laid under a stamp, a
/// wrapped picture, a vector figure, a form drawn on every page - and of text set on its side
/// in a margin.</summary>
public class AutoTagFormTests
{
    private const string Image = "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Length 1 >>\nstream\n\u0080\nendstream";

    /// <summary>Letter pages drawing <paramref name="pages"/> with F1 Helvetica, an image Im1, a
    /// form Fx1 holding <paramref name="form"/> inside <paramref name="box"/> (drawing with the
    /// same font and image) and a form Fx2 that only draws Fx1.</summary>
    private static byte[] Build(string form, string box, params string[] pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(" ", pages.Select((_, i) => $"{7 + 2 * i} 0 R"))}] /Count {pages.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            Image,
            $"<< /Type /XObject /Subtype /Form /BBox [{box}] /Resources << /Font << /F1 3 0 R >> /XObject << /Im1 4 0 R >> >> /Length {form.Length} >>\nstream\n{form}\nendstream",
            "<< /Type /XObject /Subtype /Form /BBox [0 0 612 792] /Resources << /XObject << /Fx1 5 0 R >> >> /Length 8 >>\nstream\n/Fx1 Do \nendstream",
        };
        foreach (var content in pages)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {objects.Count + 2} 0 R " +
                        "/Resources << /Font << /F1 3 0 R >> /XObject << /Im1 4 0 R /Fx1 5 0 R /Fx2 6 0 R >> >> >>");
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

    private const string ProseText = "The meadow beyond the northern fjord lies quiet in the evening light and " +
                                     "willows grow along the valley floor there, with juniper on the slopes.";
    private const string Prose =
        "BT /F1 12 Tf 72 700 Td (The meadow beyond the northern fjord lies quiet in the evening light and) Tj " +
        "0 -14 Td (willows grow along the valley floor there, with juniper on the slopes.) Tj ET\n";
    private const string Picture = "q 200 0 0 150 72 400 cm /Im1 Do Q\n";
    private const string PageBox = "0 0 612 792";
    private const string DrawFx1 = "q 1 0 0 1 0 0 cm /Fx1 Do Q\n";

    /// <summary>Five body lines down from (72, 700) at a 14 pt pitch.</summary>
    private static string Body()
    {
        var lines = new[]
        {
            "Amber meadows lie beyond the northern fjord where willow and juniper grow along",
            "the quiet valley floor and the slopes above it, and the river turns east under",
            "the ridge before it reaches the lake, whose shore the road follows for a mile",
            "until the village, where the bridge crosses to the mill and the old orchard on",
            "the far bank, with its rows of apple trees and the beehives by the wall there.",
        };
        return "BT /F1 12 Tf 72 700 Td " + string.Join(" 0 -14 Td ", lines.Select(l => $"({l}) Tj")) + " ET\n";
    }

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

    private static string Text(StructureElement el) => string.Concat(el.GetMarkedContent().Select(i => i.Text));

    private static string Latin1(byte[]? bytes) => bytes is null ? string.Empty : Compat.Latin1.GetString(bytes);

    private static Document SaveAndReload(Document doc)
    {
        using var ms = new MemoryStream();
        doc.Save(ms);
        return new Document(new MemoryStream(ms.ToArray()));
    }

    /// <summary>A form XObject of the page's resources and its decoded content.</summary>
    private static (PdfStream Form, string Content) Form(Document doc, string name, int page = 1)
    {
        var reader = doc.Reader;
        var resources = reader.ResolveDict(doc.Pages[page].Dict.Get("Resources"))!;
        var form = reader.ResolveStream(reader.ResolveDict(resources.Get("XObject"))!.Get(name))!;
        return (form, Latin1(reader.DecodeStream(form)));
    }

    /// <summary>The first kid of an element's /K: its marked-content reference.</summary>
    private static PdfDictionary FirstKid(Document doc, StructureElement el)
    {
        var k = doc.Reader.Resolve(el._dict.Get("K"));
        return Assert.IsType<PdfDictionary>(k is PdfArray arr ? doc.Reader.Resolve(arr[0]) : k);
    }

    [Fact]
    public void ContentOfAFormDrawnOnceIsMarkedInsideTheForm()
    {
        using var doc = Tag(Build(Prose + Picture, PageBox, DrawFx1));
        var root = doc.TaggedContent.StructTreeRootElement;
        // The picture stands in a paragraph of its own: the prose is the other one.
        var paragraph = Assert.Single(root.FindElements<ParagraphElement>(true), p => Text(p).Length > 0);
        Assert.Equal(ProseText, Text(paragraph));
        var figure = Assert.Single(root.FindElements<FigureElement>(true));
        var image = Assert.Single(figure.GetMarkedContent());
        Assert.Equal(MarkedContentKind.Image, image.Kind);
        Assert.InRange(image.Rectangle.Width, 199.5, 200.5);
        // The page's own content carries no mark: the Do stands bare, the marks are the form's.
        var page = Latin1(doc.Pages[1].GetContentStreamBytes());
        Assert.DoesNotContain("BDC", page);
        Assert.DoesNotContain("BMC", page);

        using var saved = SaveAndReload(doc);
        var (form, content) = Form(saved, "Fx1");
        Assert.Contains("/P <</MCID 0>> BDC", content);
        Assert.Contains("/Figure <</MCID 1>> BDC", content);
        Assert.IsType<PdfInteger>(saved.Reader.Resolve(form.Dict.Get("StructParents")));
        var reloaded = Assert.Single(saved.TaggedContent.StructTreeRootElement.FindElements<ParagraphElement>(true), p => Text(p).Length > 0);
        Assert.Equal(ProseText, Text(reloaded));
        var mcr = FirstKid(saved, reloaded);
        Assert.Equal("MCR", mcr.GetName("Type"));
        Assert.Same(form, saved.Reader.Resolve(mcr.Get("Stm")));
    }

    [Fact]
    public void FormsNestAndTheInnermostHoldsTheMarks()
    {
        using var doc = Tag(Build(Prose + Picture, PageBox, "q /Fx2 Do Q\n"));
        var root = doc.TaggedContent.StructTreeRootElement;
        Assert.Equal(ProseText, Text(Assert.Single(root.FindElements<ParagraphElement>(true), p => Text(p).Length > 0)));
        var image = Assert.Single(Assert.Single(root.FindElements<FigureElement>(true)).GetMarkedContent());
        Assert.InRange(image.Rectangle.Width, 199.5, 200.5);

        using var saved = SaveAndReload(doc);
        Assert.Contains("/P <</MCID 0>> BDC", Form(saved, "Fx1").Content);
        var outer = Form(saved, "Fx2").Content;
        Assert.DoesNotContain("BDC", outer);
        Assert.DoesNotContain("Artifact", outer);
    }

    [Fact]
    public void AFormDrawnOnEveryPageStaysAnArtifact()
    {
        using var doc = Tag(Build(Prose, PageBox, DrawFx1, DrawFx1));
        var root = doc.TaggedContent.StructTreeRootElement;
        Assert.DoesNotContain(root.FindElements<ParagraphElement>(true), p => Text(p).Contains("meadow"));
        var page = Latin1(doc.Pages[1].GetContentStreamBytes());
        Assert.True(page.IndexOf("/Artifact BMC") is var at && at >= 0 && at < page.IndexOf("/Fx1 Do"), page);

        using var saved = SaveAndReload(doc);
        Assert.DoesNotContain("BDC", Form(saved, "Fx1").Content);
    }

    [Fact]
    public void ASymbolDrawnBeforeTheTextOfSeveralLinesIsAFigureOfEachLinesParagraph()
    {
        // A box to tick, a form of 9 pt drawn before "Yes." and before "No.", the answers a line apart from the prose.
        const string box = "0.5 w 0.5 0.5 8 8 re S\n";
        static string Answer(double y, string text) => $"q 1 0 0 1 72 {y - 1} cm /Fx1 Do Q\nBT /F1 9 Tf 86 {y} Td ({text}) Tj ET\n";
        var page = Body() + Answer(600, "Yes. Skip the next line of the form.") + Answer(576, "No. Go on to the next line.");
        using var doc = Tag(Build(box, "0 0 9 9", page));

        var root = doc.TaggedContent.StructTreeRootElement;
        var figures = root.FindElements<FigureElement>(true);
        Assert.Equal(2, figures.Count);
        foreach (var answer in new[] { "Yes.", "No." })
        {
            var paragraph = Assert.Single(root.FindElements<ParagraphElement>(true), p => Text(p).TrimStart().StartsWith(answer));
            var items = paragraph.GetMarkedContent(true);
            // The box comes before the answer it stands before.
            Assert.Equal(MarkedContentKind.Drawing, items[0].Kind);
            Assert.InRange(items[0].Rectangle.LLX, 71, 73);
            Assert.InRange(items[0].Rectangle.URX - items[0].Rectangle.LLX, 8, 10);
        }
    }

    [Fact]
    public void AFormDrawingMoreThanItSaysIsAFigureDrawnWhole()
    {
        var strokes = string.Concat(Enumerable.Range(0, 6).Select(i => $"{10 + i * 30} 20 m {30 + i * 30} 120 l S\n"));
        var figure = strokes + "BT /F1 8 Tf 60 5 Td (epochs) Tj ET\n";
        using var doc = Tag(Build(figure, "0 0 200 150", Body() + "q 1 0 0 1 300 400 cm /Fx1 Do Q\n"));
        var root = doc.TaggedContent.StructTreeRootElement;
        Assert.Single(root.FindElements<FigureElement>(true));
        Assert.DoesNotContain(root.FindElements<ParagraphElement>(true), p => Text(p).Contains("epochs"));
        var page = Latin1(doc.Pages[1].GetContentStreamBytes());
        var at = page.IndexOf("/Fx1 Do");
        Assert.True(page.LastIndexOf("/Figure <</MCID", at) > page.LastIndexOf("/Artifact", at), page);

        using var saved = SaveAndReload(doc);
        Assert.DoesNotContain("BDC", Form(saved, "Fx1").Content);
    }

    [Fact]
    public void AStampSetOnItsSideInTheMarginIsAnArtifact()
    {
        const string stamp = "BT /F1 20 Tf 0 1 -1 0 32 237 Tm (arXiv:1412.6980v9  [cs.LG]  30 Jan 2017) Tj ET\n";
        using var doc = Tag(Build(string.Empty, PageBox, Body() + stamp));
        var root = doc.TaggedContent.StructTreeRootElement;
        var paragraph = Assert.Single(root.FindElements<ParagraphElement>(true));
        Assert.StartsWith("Amber meadows lie beyond", Text(paragraph));
        Assert.EndsWith("by the wall there.", Text(paragraph));
        Assert.DoesNotContain("arXiv", Text(paragraph));
        var page = Latin1(doc.Pages[1].GetContentStreamBytes());
        var at = page.IndexOf("(arXiv");
        Assert.True(page.LastIndexOf("/Artifact BMC", at) > page.LastIndexOf("BDC", at), page);
    }

    [Fact]
    public void TextSetOnItsSideAmongTheBodyIsContent()
    {
        const string label = "BT /F1 10 Tf 0 1 -1 0 300 500 Tm (sideways among the lines) Tj ET\n";
        using var doc = Tag(Build(string.Empty, PageBox, Body() + label));
        var root = doc.TaggedContent.StructTreeRootElement;
        Assert.Contains(root.FindElements<ParagraphElement>(true), p => Text(p).Contains("sideways among the lines"));
    }
}
