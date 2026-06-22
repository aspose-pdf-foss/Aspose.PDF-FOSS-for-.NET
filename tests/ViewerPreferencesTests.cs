using Aspose.Pdf;
using Xunit;

namespace Aspose.Pdf.Tests;

public sealed class ViewerPreferencesTests
{
    [Fact]
    public void DefaultPreferences_AllFalse()
    {
        var prefs = new ViewerPreferences();
        Assert.False(prefs.HideMenubar);
        Assert.False(prefs.HideToolbar);
        Assert.False(prefs.HideWindowUI);
        Assert.False(prefs.FitWindow);
        Assert.False(prefs.CenterWindow);
        Assert.False(prefs.DisplayDocTitle);
        Assert.Equal("L2R", prefs.Direction);
    }

    [Fact]
    public void SetProperties_RoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var prefs = doc.GetOrCreateViewerPreferences();
        prefs.HideMenubar = true;
        prefs.CenterWindow = true;
        prefs.DisplayDocTitle = true;
        prefs.Direction = "R2L";

        var saved = doc.ToArray();

        using var reopened = Document.Open(saved);
        var readPrefs = reopened.ViewerPreferences;
        Assert.NotNull(readPrefs);
        Assert.True(readPrefs.HideMenubar);
        Assert.True(readPrefs.CenterWindow);
        Assert.True(readPrefs.DisplayDocTitle);
        Assert.False(readPrefs.HideToolbar);
        Assert.Equal("R2L", readPrefs.Direction);
    }

    [Fact]
    public void NoViewerPreferences_ReturnsNull()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        Assert.Null(doc.ViewerPreferences);
    }

    [Fact]
    public void GetOrCreate_CreatesWhenMissing()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        var prefs = doc.GetOrCreateViewerPreferences();
        Assert.NotNull(prefs);
    }

    [Fact]
    public void PageLayout_SetAndGet()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        doc.PageLayout = PageLayout.TwoColumnLeft;

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);
        Assert.Equal(PageLayout.TwoColumnLeft, reopened.PageLayout);
        Assert.Equal("TwoColumnLeft", reopened.PageLayoutName);
    }

    [Fact]
    public void PageMode_SetAndGet()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        doc.PageMode = PageMode.UseOutlines;

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);
        Assert.Equal(PageMode.UseOutlines, reopened.PageMode);
        Assert.Equal("UseOutlines", reopened.PageModeName);
    }

    [Fact]
    public void PrintScaling_None()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var prefs = doc.GetOrCreateViewerPreferences();
        prefs.PrintScaling = "None";

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);
        Assert.Equal("None", reopened.ViewerPreferences!.PrintScaling);
    }
}
