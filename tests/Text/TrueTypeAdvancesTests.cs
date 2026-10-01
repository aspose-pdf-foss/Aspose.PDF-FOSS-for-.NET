using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

public class TrueTypeAdvancesTests
{
    [Fact]
    public void Patch_SetsTheGivenAdvancesAndLeavesTheRest()
    {
        var program = PdfBuilder.BuildMinimalTrueTypeFont();
        var before = new TrueTypeParser(program);
        before.Parse();

        var patched = TrueTypeAdvances.Patch(program, new Dictionary<int, int> { [5] = 777 });
        var after = new TrueTypeParser(patched);
        after.Parse();

        Assert.Equal(777, after.GlyphWidths[5]);
        Assert.Equal(before.GlyphWidths[6], after.GlyphWidths[6]);
        var original = new TrueTypeParser(program);
        original.Parse();
        Assert.Equal(before.GlyphWidths[5], original.GlyphWidths[5]); // the input is left as it was
    }

    [Fact]
    public void CidWidths_ReadsBothForms()
    {
        var w = new PdfArray();
        w.Add(new PdfInteger(1));
        var run = new PdfArray();
        run.Add(new PdfInteger(500));
        run.Add(new PdfReal(600.5));
        w.Add(run);
        w.Add(new PdfInteger(10));
        w.Add(new PdfInteger(12));
        w.Add(new PdfInteger(250));

        var widths = TrueTypeAdvances.CidWidths(w, PdfReader.Empty);

        Assert.Equal(500, widths[1]);
        Assert.Equal(600.5, widths[2]);
        Assert.Equal(250, widths[10]);
        Assert.Equal(250, widths[12]);
        Assert.False(widths.ContainsKey(3));
    }

    [Fact]
    public void SubsetWithAdvances_CarriesThemIntoTheSubset()
    {
        var program = PdfBuilder.BuildMinimalTrueTypeFont();
        var parser = new TrueTypeParser(program);
        parser.Parse();
        var gid = parser.CMap['A'];

        var (subset, glyphMap) = new TrueTypeSubsetter(program, parser)
            .Subset(new[] { (int)'A' }, new Dictionary<int, int> { [gid] = 1234 });
        var sub = new TrueTypeParser(subset);
        sub.Parse();

        Assert.Equal(1234, sub.GlyphWidths[glyphMap[gid]]);
    }

    [Fact]
    public void CmapSubtables_ListsThePlatformAndEncodingPairs()
    {
        var subtables = TrueTypeAdvances.CmapSubtables(PdfBuilder.BuildMinimalTrueTypeFont());
        Assert.NotEmpty(subtables);
        Assert.Empty(TrueTypeAdvances.CmapSubtables(new byte[] { 1, 2, 3 }));
    }
}
