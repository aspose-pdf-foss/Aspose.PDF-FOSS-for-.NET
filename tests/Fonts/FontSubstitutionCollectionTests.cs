using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Fonts;

public class FontSubstitutionCollectionTests
{
    [Fact]
    public void Add_Then_Delete_RestoresCount()
    {
        var prev = FontRepository.Substitutions.Count;
        var s = new SimpleFontSubstitution("Foo", "Helvetica");
        FontRepository.Substitutions.Add(s);
        Assert.Equal(prev + 1, FontRepository.Substitutions.Count);
        FontRepository.Substitutions.Delete(s);
        Assert.Equal(prev, FontRepository.Substitutions.Count);
    }

    [Fact]
    public void Add_Then_Remove_RestoresCount()
    {
        var prev = FontRepository.Substitutions.Count;
        var s = new SimpleFontSubstitution("Bar", "Times-Roman");
        FontRepository.Substitutions.Add(s);
        Assert.True(FontRepository.Substitutions.Remove(s));
        Assert.Equal(prev, FontRepository.Substitutions.Count);
    }

    [Fact]
    public void Clear_Empties()
    {
        FontRepository.Substitutions.Add(new SimpleFontSubstitution("A", "Courier"));
        FontRepository.Substitutions.Add(new SimpleFontSubstitution("B", "Courier"));
        FontRepository.Substitutions.Clear();
        Assert.Empty(FontRepository.Substitutions);
    }

    [Fact]
    public void Add_Custom_Subclass_Works()
    {
        FontRepository.Substitutions.Clear();
        var s = new MyCustom();
        FontRepository.Substitutions.Add(s);
        Assert.Single(FontRepository.Substitutions);
        FontRepository.Substitutions.Delete(s);
        Assert.Empty(FontRepository.Substitutions);
    }

    [Fact]
    public void SimpleFontSubstitution_Resolves_To_Standard14()
    {
        var s = new SimpleFontSubstitution("MyMissingFont", "Helvetica");
        var spec = new CustomFontSubstitutionBase.OriginalFontSpecification("MyMissingFont", false);
        Assert.True(s.TrySubstitute(spec, out var font));
        Assert.NotNull(font);
    }

    [Fact]
    public void SimpleFontSubstitution_DoesNotMatch_OtherName()
    {
        var s = new SimpleFontSubstitution("MyMissingFont", "Helvetica");
        var spec = new CustomFontSubstitutionBase.OriginalFontSpecification("OtherFont", false);
        Assert.False(s.TrySubstitute(spec, out var font));
        Assert.Null(font);
    }

    [Fact]
    public void Remove_NotPresent_ReturnsFalse()
    {
        var s = new SimpleFontSubstitution("Foo", "Helvetica");
        // s was never added
        Assert.False(FontRepository.Substitutions.Remove(s));
    }

    private sealed class MyCustom : CustomFontSubstitutionBase
    {
        public override bool TrySubstitute(OriginalFontSpecification spec, out Font? font)
        {
            font = null;
            return false;
        }
    }
}
