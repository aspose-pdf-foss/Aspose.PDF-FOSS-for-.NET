using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.IO;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Content;

public class ExtGStateWriteTests
{
    [Fact]
    public void ToPdfDictionary_FillAlpha()
    {
        var gs = new ExtGState { FillAlpha = 0.5 };
        var dict = gs.ToPdfDictionary();

        var ca = dict.Get("ca");
        Assert.IsType<PdfReal>(ca);
        Assert.Equal(0.5, ((PdfReal)ca).Value);
    }

    [Fact]
    public void ToPdfDictionary_StrokeAlpha()
    {
        var gs = new ExtGState { StrokeAlpha = 0.3 };
        var dict = gs.ToPdfDictionary();

        var ca = dict.Get("CA");
        Assert.IsType<PdfReal>(ca);
        Assert.Equal(0.3, ((PdfReal)ca).Value);
    }

    [Fact]
    public void ToPdfDictionary_BlendMode()
    {
        var gs = new ExtGState { BlendMode = "Multiply" };
        var dict = gs.ToPdfDictionary();

        var bm = dict.Get("BM");
        Assert.IsType<PdfName>(bm);
        Assert.Equal("Multiply", ((PdfName)bm).Value);
    }

    [Fact]
    public void ToPdfDictionary_DefaultValues_OnlyType()
    {
        var gs = new ExtGState(); // all defaults
        var dict = gs.ToPdfDictionary();

        // Should have Type but not ca/CA/BM (they are defaults)
        Assert.NotNull(dict.Get("Type"));
        Assert.Null(dict.Get("ca"));
        Assert.Null(dict.Get("CA"));
        Assert.Null(dict.Get("BM"));
    }

    [Fact]
    public void ToPdfDictionary_Overprint()
    {
        var gs = new ExtGState { OverprintStroke = true, OverprintFill = true };
        var dict = gs.ToPdfDictionary();

        var op = dict.Get("OP") as PdfBoolean;
        Assert.NotNull(op);
        Assert.True(op!.Value);
        var opFill = dict.Get("op") as PdfBoolean;
        Assert.NotNull(opFill);
        Assert.True(opFill!.Value);
    }

    [Fact]
    public void ToPdfDictionary_LineProperties()
    {
        var gs = new ExtGState { LineWidth = 2.5, LineCap = 1, LineJoin = 2 };
        var dict = gs.ToPdfDictionary();

        var lw = dict.Get("LW") as PdfReal;
        Assert.NotNull(lw);
        Assert.Equal(2.5, lw!.Value);
        var lc = dict.Get("LC") as PdfInteger;
        Assert.NotNull(lc);
        Assert.Equal(1, lc!.Value);
        var lj = dict.Get("LJ") as PdfInteger;
        Assert.NotNull(lj);
        Assert.Equal(2, lj!.Value);
    }

    [Fact]
    public void Page_AddExtGState_ReturnsUniqueName()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        var name1 = page.AddExtGState(new ExtGState { FillAlpha = 0.5 });
        var name2 = page.AddExtGState(new ExtGState { FillAlpha = 0.3 });

        Assert.Equal("GS0", name1);
        Assert.Equal("GS1", name2);
        Assert.NotEqual(name1, name2);
    }

    [Fact]
    public void Page_AddExtGState_CreatesResourceDict()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        var name = page.AddExtGState(new ExtGState { FillAlpha = 0.7 });

        // The ExtGState should be in the page resources after save/reopen
        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var page2 = doc2.Pages[1];

        var states = ExtGState.FromPage(page2);
        Assert.Contains(name, (IDictionary<string, ExtGState>)states);
        Assert.Equal(0.7, states[name].FillAlpha, 2);
    }

    [Fact]
    public void ExtGState_RoundTrip_AllProperties()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        var gs = new ExtGState
        {
            FillAlpha = 0.4,
            StrokeAlpha = 0.6,
            BlendMode = "Screen",
        };
        var name = page.AddExtGState(gs);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var states = ExtGState.FromPage(doc2.Pages[1]);

        Assert.Contains(name, (IDictionary<string, ExtGState>)states);
        var loaded = states[name];
        Assert.Equal(0.4, loaded.FillAlpha, 2);
        Assert.Equal(0.6, loaded.StrokeAlpha, 2);
        Assert.Equal("Screen", loaded.BlendMode);
    }

    [Fact]
    public void WatermarkStamp_Opacity_WritesExtGState()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var stamp = new WatermarkStamp("DRAFT")
        {
            Opacity = 0.3,
        };
        doc.Pages[1].AddStamp(stamp);
        var saved = doc.ToArray();

        // Reopen and verify ExtGState was written
        using var doc2 = Document.Open(saved);
        var states = ExtGState.FromPage(doc2.Pages[1]);
        Assert.NotEmpty(states);

        // At least one state should have opacity ~0.3
        Assert.Contains(states.Values, s => Math.Abs(s.FillAlpha - 0.3) < 0.05);
    }

    [Fact]
    public void WatermarkStamp_Opacity_ContentUsesGsOperator()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var stamp = new WatermarkStamp("CONFIDENTIAL") { Opacity = 0.5 };
        doc.Pages[1].AddStamp(stamp);
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        var page = doc2.Pages[1];
        var reader = doc2.Reader;

        // The stamp is emitted as a Form XObject referenced from the page content;
        // the gs operator + ExtGState live inside that form.
        var contentsObj = reader.Resolve(page.Dict.Get("Contents"));
        Assert.IsType<PdfStream>(contentsObj);
        var pageContent = Encoding.ASCII.GetString(reader.DecodeStream((PdfStream)contentsObj));
        Assert.Contains("Do", pageContent); // page references the stamp form

        var resources = reader.Resolve(page.Dict.Get("Resources")) as PdfDictionary;
        Assert.NotNull(resources);
        var xobjects = reader.Resolve(resources!.Get("XObject")) as PdfDictionary;
        Assert.NotNull(xobjects);
        string? formContent = null;
        foreach (var key in xobjects!.Keys)
        {
            if (reader.Resolve(xobjects.Get(key)) is PdfStream form
                && form.Dict.GetName("Subtype") == "Form")
            {
                formContent = Encoding.ASCII.GetString(reader.DecodeStream(form));
                break;
            }
        }
        Assert.NotNull(formContent);
        Assert.Contains("gs", formContent!); // gs operator
        Assert.Contains("GS0", formContent!); // the ExtGState name
    }

    [Fact]
    public void WatermarkStamp_FullOpacity_NoExtGState()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var stamp = new WatermarkStamp("VISIBLE") { Opacity = 1.0 };
        doc.Pages[1].AddStamp(stamp);
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        var states = ExtGState.FromPage(doc2.Pages[1]);
        // No ExtGState should be created for fully opaque stamp
        Assert.Empty(states);
    }

    [Fact]
    public void TextStamp_Opacity_WritesExtGState()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var stamp = new TextStamp("Hello") { Opacity = 0.5 };
        doc.Pages[1].AddStamp(stamp);
        var saved = doc.ToArray();

        using var doc2 = Document.Open(saved);
        var states = ExtGState.FromPage(doc2.Pages[1]);
        Assert.NotEmpty(states);
        Assert.Contains(states.Values, s => Math.Abs(s.FillAlpha - 0.5) < 0.05);
    }

    [Fact]
    public void Graph_WithOpacity_WritesExtGState()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        var graph = new Graph(200, 100);
        graph.Add(new DrawingRectangle(10, 10, 80, 40)
        {
            GraphInfo =
            {
                FillColor = Color.Blue,
                FillOpacity = 0.5,
            }
        });

        var content = graph.Build(page);
        page.AddContentStream(content);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var states = ExtGState.FromPage(doc2.Pages[1]);
        Assert.NotEmpty(states);
        Assert.Contains(states.Values, s => Math.Abs(s.FillAlpha - 0.5) < 0.05);
    }

    [Fact]
    public void Graph_WithoutOpacity_NoExtGState()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        var graph = new Graph(200, 100);
        graph.Add(new DrawingRectangle(10, 10, 80, 40)
        {
            GraphInfo = { FillColor = Color.Red }
        });

        var content = graph.Build(page);
        page.AddContentStream(content);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var states = ExtGState.FromPage(doc2.Pages[1]);
        Assert.Empty(states);
    }

    [Fact]
    public void Graph_CircleWithOpacity_WritesExtGState()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        var graph = new Graph(200, 200);
        graph.Add(new Circle(100, 100, 50)
        {
            GraphInfo =
            {
                FillColor = Color.Green,
                FillOpacity = 0.7,
                StrokeColor = Color.Black,
                StrokeOpacity = 0.3,
            }
        });

        var content = graph.Build(page);
        page.AddContentStream(content);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var states = ExtGState.FromPage(doc2.Pages[1]);
        Assert.NotEmpty(states);
    }

    [Fact]
    public void ExtGState_Write_ParsedByContentStreamParser()
    {
        // Create a PDF with ExtGState, then verify content stream parser
        // picks up the opacity
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        var gs = new ExtGState { FillAlpha = 0.25 };
        var gsName = page.AddExtGState(gs);

        var builder = new ContentStreamBuilder();
        builder.SetExtGState(gsName);
        page.AddContentStream(builder.Build());

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var page2 = doc2.Pages[1];
        var reader = doc2.Reader;

        var parser = new ContentStreamParser(reader);
        var extGStates = ExtGState.ResolveRawFromPage(page2.Dict, reader);

        double? capturedAlpha = null;
        parser.OnOperator += (op, _, state) =>
        {
            if (op == "gs") capturedAlpha = state.FillAlpha;
        };

        var contentsObj = reader.Resolve(page2.Dict.Get("Contents"));
        if (contentsObj is PdfStream stream)
        {
            var contentBytes = reader.DecodeStream(stream);
            parser.Parse(contentBytes, extGStates: extGStates);
        }

        Assert.Equal(0.25, capturedAlpha);
    }

    [Fact]
    public void MultipleExtGStates_IndependentNames()
    {
        var input = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(input);
        var page = doc.Pages[1];

        var names = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            names.Add(page.AddExtGState(new ExtGState { FillAlpha = 0.1 * (i + 1) }));
        }

        // All names should be unique
        Assert.Equal(5, names.Distinct().Count());

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var states = ExtGState.FromPage(doc2.Pages[1]);
        Assert.Equal(5, states.Count);
    }

    [Fact]
    public void ExtGState_BlendMode_RoundTrips()
    {
        var blendModes = new[] { "Multiply", "Screen", "Overlay", "Darken", "Lighten",
            "ColorDodge", "ColorBurn", "HardLight", "SoftLight", "Difference", "Exclusion" };

        foreach (var bm in blendModes)
        {
            var input = PdfBuilder.BuildMinimal();
            using var doc = Document.Open(input);
            var page = doc.Pages[1];
            page.AddExtGState(new ExtGState { BlendMode = bm });

            var saved = doc.ToArray();
            using var doc2 = Document.Open(saved);
            var states = ExtGState.FromPage(doc2.Pages[1]);
            Assert.Single(states);
            Assert.Equal(bm, states.Values.First().BlendMode);
        }
    }
}
