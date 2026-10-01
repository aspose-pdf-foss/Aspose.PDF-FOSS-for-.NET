using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests;

public class TextStateFlowTests
{
    [Fact]
    public void ExplicitViews_AreNullUntilTheCallerSets()
    {
        var state = new TextState();
        Assert.Null(state.ExplicitFontSize);
        Assert.Null(state.ExplicitFont);
        Assert.Null(state.ExplicitFontStyle);
        Assert.Null(state.ExplicitForegroundColor);
        Assert.Null(state.ExplicitBackgroundColor);
        Assert.Null(state.ExplicitStrokingColor);
        Assert.Null(state.ExplicitCharacterSpacing);
        Assert.Null(state.ExplicitWordSpacing);
        Assert.Null(state.ExplicitHorizontalScaling);
        Assert.Null(state.ExplicitLineSpacing);
        Assert.Null(state.ExplicitUnderline);
        Assert.Null(state.ExplicitStrikeOut);

        state.FontSize = 14;
        state.FontStyle = FontStyles.Bold;
        state.CharacterSpacing = 1.5f;
        state.Underline = true;
        Assert.Equal(14f, state.ExplicitFontSize);
        Assert.Equal(FontStyles.Bold, state.ExplicitFontStyle);
        Assert.Equal(1.5f, state.ExplicitCharacterSpacing);
        Assert.True(state.ExplicitUnderline);
        Assert.Null(state.ExplicitWordSpacing);
    }

    [Fact]
    public void FragmentState_ReportsWhatEverySegmentShares()
    {
        var fragment = new TextFragment("text1");
        fragment.Segments.Add(new TextSegment("segment2"));
        var third = new TextSegment("segment3") { TextState = { FontSize = 25 } };
        fragment.Segments.Add(third);

        // Segments disagree: the fragment keeps its own size.
        Assert.Equal(10f, fragment.TextState.FontSize);
        Assert.Equal(25f, fragment.Segments[3].TextState.FontSize);

        // Every segment set to the same size: the fragment reports it.
        foreach (var segment in fragment.Segments) segment.TextState.FontSize = 7;
        Assert.Equal(7f, fragment.TextState.FontSize);

        // Every segment moved to the same face: the fragment reports that face.
        foreach (var segment in fragment.Segments) segment.TextState.Font = FontRepository.FindFont("Courier");
        Assert.Equal("Courier", fragment.TextState.Font?.FontName);
        Assert.Equal("Courier", fragment.Segments[1].TextState.Font?.FontName);
    }

    [Fact]
    public void CoreFaceAssignedAfterATrueTypeFace_IsWhatTheSaveWrites()
    {
        var fragment = new TextFragment("segment4");
        using var doc = Document.Open(Helpers.PdfBuilder.BuildMinimal());
        new TextBuilder(doc.Pages[1]).AppendText(fragment);
        fragment.TextState.Font = FontRepository.FindFont("Arial");
        using (var first = new MemoryStream()) doc.Save(first);

        fragment.TextState.Font = FontRepository.FindFont("Courier");
        byte[] saved;
        using (var ms = new MemoryStream()) { doc.Save(ms); saved = ms.ToArray(); }

        using var reloaded = Document.Open(saved);
        var absorber = new TextFragmentAbsorber("segment4");
        reloaded.Pages[1].Accept(absorber);
        Assert.Equal("Courier", absorber.TextFragments[1].TextState.Font?.FontName);
    }
}
