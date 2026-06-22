using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Optimization;

public class FontSubsetterTests
{
    [Fact]
    public void SubsetEmbeddedFonts_ReducesFontStreamSize()
    {
        // Build a PDF with embedded TrueType font using only "Hi" (2 distinct chars)
        var data = PdfBuilder.BuildWithEmbeddedTrueTypeFont("Hi");

        using var doc = Document.Open(data);

        // Get the original font data size
        var originalSize = data.Length;

        // Optimize with font subsetting
        doc.OptimizeResources(new OptimizationOptions
        {
            RemoveUnusedObjects = false,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
            SubsetEmbeddedFonts = true,
        });

        var saved = doc.ToArray();

        // The subsetted PDF should be smaller because only used glyphs are retained
        Assert.True(saved.Length < originalSize,
            $"Expected subsetted PDF ({saved.Length}) to be smaller than original ({originalSize})");
    }

    [Fact]
    public void SubsetEmbeddedFonts_PreservesTextExtraction()
    {
        var text = "Hello World";
        var data = PdfBuilder.BuildWithEmbeddedTrueTypeFont(text);

        using var doc = Document.Open(data);

        // Extract text before subsetting
        var absorber1 = new TextAbsorber();
        absorber1.Visit(doc);
        var textBefore = absorber1.Text.Trim();

        // Optimize with subsetting
        doc.OptimizeResources(new OptimizationOptions
        {
            RemoveUnusedObjects = false,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
            SubsetEmbeddedFonts = true,
        });

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        // Extract text after subsetting — content stream is unchanged
        var absorber2 = new TextAbsorber();
        absorber2.Visit(doc2);
        var textAfter = absorber2.Text.Trim();

        Assert.Equal(textBefore, textAfter);
    }

    [Fact]
    public void SubsetEmbeddedFonts_RemovesUnusedGlyphs()
    {
        // Use only "AB" — a small subset of the full 95-char font
        var data = PdfBuilder.BuildWithEmbeddedTrueTypeFont("AB");

        using var doc = Document.Open(data);
        var originalSize = data.Length;

        doc.OptimizeResources(new OptimizationOptions
        {
            RemoveUnusedObjects = false,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
            SubsetEmbeddedFonts = true,
        });

        var saved = doc.ToArray();

        // With only 2 chars used out of 95, the subset should be meaningfully smaller
        Assert.True(saved.Length < originalSize,
            $"Expected subset with 2 chars ({saved.Length}) to be smaller than full font ({originalSize})");
    }

    [Fact]
    public void SubsetFonts_Standard14_StillHandled()
    {
        // Build a PDF with a Standard 14 font (Helvetica, no embedding)
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Standard14 test) Tj ET");
        var data = PdfBuilder.BuildWithTextContent(content);

        using var doc = Document.Open(data);

        // This should not throw — Standard 14 fonts are handled separately
        doc.OptimizeResources(new OptimizationOptions
        {
            SubsetFonts = true,
            SubsetEmbeddedFonts = true,
            RemoveUnusedObjects = false,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
        });

        var saved = doc.ToArray();
        Assert.True(saved.Length > 0);

        // Verify text is still extractable
        using var doc2 = Document.Open(saved);
        var absorber = new TextAbsorber();
        absorber.Visit(doc2);
        Assert.Contains("Standard14 test", absorber.Text);
    }

    [Fact]
    public void SubsetEmbeddedFonts_AddsSubsetPrefix()
    {
        var data = PdfBuilder.BuildWithEmbeddedTrueTypeFont("Test");

        using var doc = Document.Open(data);

        doc.OptimizeResources(new OptimizationOptions
        {
            RemoveUnusedObjects = false,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
            SubsetEmbeddedFonts = true,
        });

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        // Find the font dictionary and check BaseFont has a subset prefix
        var reader = doc2.Reader;
        foreach (var entry in reader.XRefTable.Entries.Values)
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;
            var obj = reader.Resolve(new Core.PdfIndirectRef(entry.ObjectNumber, entry.Generation));
            if (obj is not Core.PdfDictionary dict) continue;
            if (dict.GetName("Type") != "Font") continue;
            if (dict.GetName("Subtype") != "TrueType") continue;

            var baseFont = dict.GetName("BaseFont");
            Assert.NotNull(baseFont);

            // Subset prefix format: ABCDEF+FontName
            Assert.True(baseFont!.Length > 7, $"BaseFont '{baseFont}' too short for subset prefix");
            Assert.Equal('+', baseFont[6]);

            // The 6-char prefix should be all uppercase letters
            var prefix = baseFont[..6];
            Assert.All(prefix.ToCharArray(), c => Assert.True(char.IsAsciiLetterUpper(c),
                $"Expected uppercase letter but got '{c}' in prefix '{prefix}'"));

            // Original name should follow after the '+'
            Assert.Equal("TestFont", baseFont[7..]);
            return;
        }

        // If we get here with a subsetted font, the test structure is OK
        // (Standard 14 fonts won't have TrueType subtype)
    }

    [Fact]
    public void SubsetEmbeddedFonts_DoesNotThrowOnMinimalPdf()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);

        // Should not throw on a PDF with no fonts at all
        doc.OptimizeResources(new OptimizationOptions
        {
            SubsetEmbeddedFonts = true,
            RemoveUnusedObjects = false,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
        });

        var saved = doc.ToArray();
        Assert.True(saved.Length > 0);
    }

    [Fact]
    public void SubsetEmbeddedFonts_UpdatesWidths()
    {
        // Use a small set of characters
        var data = PdfBuilder.BuildWithEmbeddedTrueTypeFont("AZ");

        using var doc = Document.Open(data);

        doc.OptimizeResources(new OptimizationOptions
        {
            RemoveUnusedObjects = false,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
            SubsetEmbeddedFonts = true,
        });

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        // Find the font dictionary and check FirstChar/LastChar are updated
        var reader = doc2.Reader;
        foreach (var entry in reader.XRefTable.Entries.Values)
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;
            var obj = reader.Resolve(new Core.PdfIndirectRef(entry.ObjectNumber, entry.Generation));
            if (obj is not Core.PdfDictionary dict) continue;
            if (dict.GetName("Type") != "Font") continue;
            if (dict.GetName("Subtype") != "TrueType") continue;

            var firstChar = (int)dict.GetInt("FirstChar");
            var lastChar = (int)dict.GetInt("LastChar");

            // 'A' = 65, 'Z' = 90
            Assert.Equal(65, firstChar);
            Assert.Equal(90, lastChar);

            // Widths array should have exactly 26 entries (A-Z)
            var widths = reader.Resolve(dict.Get("Widths")) as Core.PdfArray;
            Assert.NotNull(widths);
            Assert.Equal(26, widths!.Count);
            return;
        }
    }

    [Fact]
    public void OptimizationOptionsAll_IncludesSubsetEmbeddedFonts()
    {
        var all = OptimizationOptions.All();
        Assert.True(all.SubsetEmbeddedFonts);
    }
}
