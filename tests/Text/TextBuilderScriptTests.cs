using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>A sub- or superscript fragment a TextBuilder writes draws smaller, on a raised or
/// lowered baseline, upright or turned.</summary>
public class TextBuilderScriptTests
{
    private const double BaseSize = 11;

    private static TextFragment Fragment(string text, double rotation, bool subscript, bool superscript)
    {
        var fragment = new TextFragment(text);
        fragment.TextState.FontSize = (float)BaseSize;
        fragment.Position = new Position(300, 500);
        fragment.TextState.Subscript = subscript;
        fragment.TextState.Superscript = superscript;
        fragment.TextState.Rotation = rotation;
        return fragment;
    }

    private static TextFragment[] WriteAndReadBack(double rotation)
    {
        var doc = new Document();
        var page = doc.Pages.Add();
        var builder = new TextBuilder(page);
        builder.AppendText(Fragment("Base", rotation, false, false));
        builder.AppendText(Fragment("Sub", rotation, true, false));
        builder.AppendText(Fragment("Sup", rotation, false, true));
        using var ms = new MemoryStream();
        doc.Save(ms);
        using var saved = new Document(new MemoryStream(ms.ToArray()));
        var absorber = new TextFragmentAbsorber();
        saved.Pages[1].Accept(absorber);
        return absorber.TextFragments.Cast<TextFragment>().ToArray();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(90.0)]
    public void ScriptFragmentsDrawSmallerOnAShiftedBaseline(double rotation)
    {
        var fragments = WriteAndReadBack(rotation);
        var baseRun = Assert.Single(fragments, f => f.Text == "Base");
        var sub = Assert.Single(fragments, f => f.Text == "Sub");
        var sup = Assert.Single(fragments, f => f.Text == "Sup");

        Assert.Equal(BaseSize, baseRun.TextState.FontSize, 2);
        Assert.Equal(0, baseRun.TextState.TextRise, 3);
        // The generator's metrics: 0.583 of the base size, raised 0.421 em or lowered 0.245 em.
        Assert.Equal(BaseSize * 0.583, sub.TextState.FontSize, 2);
        Assert.Equal(BaseSize * 0.583, sup.TextState.FontSize, 2);
        Assert.Equal(-BaseSize * 0.245, sub.TextState.TextRise, 2);
        Assert.Equal(BaseSize * 0.421, sup.TextState.TextRise, 2);
        Assert.True(sub.TextState.Subscript);
        Assert.True(sup.TextState.Superscript);
    }
}
