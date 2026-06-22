using Aspose.Pdf;
using Aspose.Pdf.Core;
using Xunit;

namespace Aspose.Pdf.Tests;

public sealed class NamedDestinationTests
{
    [Fact]
    public void CreateFitDestination_RoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var dest = NamedDestination.CreateFitDestination(0);
        doc.AddNamedDestination("chapter1", dest);

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);

        var dests = reopened.NamedDestinations;
        Assert.NotNull(dests);
        Assert.Equal(1, dests.Count);

        var found = dests.FindByName("chapter1");
        Assert.NotNull(found);
        Assert.Equal("chapter1", found.Name);
        Assert.Equal(0, found.PageIndex);
        Assert.Equal("Fit", found.Type);
    }

    [Fact]
    public void CreateXYZDestination_WithPositionAndZoom()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var dest = NamedDestination.CreateXYZDestination(0, 100.0, 500.0, 1.5);
        doc.AddNamedDestination("section2", dest);

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);

        var found = reopened.NamedDestinations?.FindByName("section2");
        Assert.NotNull(found);
        Assert.Equal("XYZ", found.Type);
        Assert.Equal(0, found.PageIndex);
    }

    [Fact]
    public void CreateFitHDestination_RoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var dest = NamedDestination.CreateFitHDestination(0, 700.0);
        doc.AddNamedDestination("top", dest);

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);

        var found = reopened.NamedDestinations?.FindByName("top");
        Assert.NotNull(found);
        Assert.Equal("FitH", found.Type);
    }

    [Fact]
    public void CreateFitVDestination_RoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var dest = NamedDestination.CreateFitVDestination(0, 50.0);
        doc.AddNamedDestination("left", dest);

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);

        var found = reopened.NamedDestinations?.FindByName("left");
        Assert.NotNull(found);
        Assert.Equal("FitV", found.Type);
    }

    [Fact]
    public void CreateFitRDestination_RoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var dest = NamedDestination.CreateFitRDestination(0, 10, 20, 300, 400);
        doc.AddNamedDestination("rect", dest);

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);

        var found = reopened.NamedDestinations?.FindByName("rect");
        Assert.NotNull(found);
        Assert.Equal("FitR", found.Type);
    }

    [Fact]
    public void MultipleDestinations_AllPersist()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        doc.Pages.Add();

        doc.AddNamedDestination("first", NamedDestination.CreateFitDestination(0));
        doc.AddNamedDestination("second", NamedDestination.CreateXYZDestination(1, 0, 0, 2.0));
        doc.AddNamedDestination("third", NamedDestination.CreateFitHDestination(0, 300));

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);

        var dests = reopened.NamedDestinations;
        Assert.NotNull(dests);
        Assert.Equal(3, dests.Count);
        Assert.NotNull(dests.FindByName("first"));
        Assert.NotNull(dests.FindByName("second"));
        Assert.NotNull(dests.FindByName("third"));
    }

    [Fact]
    public void RemoveNamedDestination_RemovesCorrectEntry()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        doc.AddNamedDestination("keep", NamedDestination.CreateFitDestination(0));
        doc.AddNamedDestination("remove", NamedDestination.CreateFitDestination(0));
        doc.AddNamedDestination("alsokeep", NamedDestination.CreateFitHDestination(0, 100));

        // Verify all three exist before removal
        var saved1 = doc.ToArray();
        using var check = Document.Open(saved1);
        Assert.Equal(3, check.NamedDestinations!.Count);

        // Remove the middle one from the original doc
        var removed = doc.RemoveNamedDestination("remove");
        Assert.True(removed);

        var saved2 = doc.ToArray();
        using var reopened = Document.Open(saved2);

        var dests = reopened.NamedDestinations;
        Assert.NotNull(dests);
        Assert.Equal(2, dests.Count);
        Assert.NotNull(dests.FindByName("keep"));
        Assert.NotNull(dests.FindByName("alsokeep"));
        Assert.Null(dests.FindByName("remove"));
    }

    [Fact]
    public void RemoveNamedDestination_NonExistent_ReturnsFalse()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        doc.AddNamedDestination("exists", NamedDestination.CreateFitDestination(0));

        var result = doc.RemoveNamedDestination("doesnotexist");
        Assert.False(result);
    }

    [Fact]
    public void RemoveNamedDestination_NoNamesDict_ReturnsFalse()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var result = doc.RemoveNamedDestination("anything");
        Assert.False(result);
    }

    [Fact]
    public void ViewerPreferences_PageLayout_EnumRoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var prefs = doc.GetOrCreateViewerPreferences();
        prefs.PageLayout = PageLayoutMode.TwoColumnLeft;

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);

        var readPrefs = reopened.ViewerPreferences;
        Assert.NotNull(readPrefs);
        Assert.Equal(PageLayoutMode.TwoColumnLeft, readPrefs.PageLayout);

        // Also check the string-based property on Document
        Assert.Equal("TwoColumnLeft", reopened.PageLayoutName);
    }

    [Fact]
    public void ViewerPreferences_PageMode_EnumRoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var prefs = doc.GetOrCreateViewerPreferences();
        prefs.PageMode = PageModeValue.FullScreen;

        var saved = doc.ToArray();
        using var reopened = Document.Open(saved);

        var readPrefs = reopened.ViewerPreferences;
        Assert.NotNull(readPrefs);
        Assert.Equal(PageModeValue.FullScreen, readPrefs.PageMode);

        Assert.Equal("FullScreen", reopened.PageModeName);
    }

    [Fact]
    public void ViewerPreferences_PageLayout_AllValues()
    {
        var layouts = new[]
        {
            PageLayoutMode.SinglePage,
            PageLayoutMode.OneColumn,
            PageLayoutMode.TwoColumnLeft,
            PageLayoutMode.TwoColumnRight,
            PageLayoutMode.TwoPageLeft,
            PageLayoutMode.TwoPageRight,
        };

        foreach (var layout in layouts)
        {
            using var doc = Document.Create();
            doc.Pages.Add();

            var prefs = doc.GetOrCreateViewerPreferences();
            prefs.PageLayout = layout;

            var saved = doc.ToArray();
            using var reopened = Document.Open(saved);
            Assert.Equal(layout, reopened.ViewerPreferences?.PageLayout);
        }
    }

    [Fact]
    public void ViewerPreferences_PageMode_AllValues()
    {
        var modes = new[]
        {
            PageModeValue.UseNone,
            PageModeValue.UseOutlines,
            PageModeValue.UseThumbs,
            PageModeValue.FullScreen,
            PageModeValue.UseOC,
            PageModeValue.UseAttachments,
        };

        foreach (var mode in modes)
        {
            using var doc = Document.Create();
            doc.Pages.Add();

            var prefs = doc.GetOrCreateViewerPreferences();
            prefs.PageMode = mode;

            var saved = doc.ToArray();
            using var reopened = Document.Open(saved);
            Assert.Equal(mode, reopened.ViewerPreferences?.PageMode);
        }
    }

    [Fact]
    public void ViewerPreferences_PageLayout_Null_ReturnsNull()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var prefs = doc.GetOrCreateViewerPreferences();
        Assert.Null(prefs.PageLayout);
    }

    [Fact]
    public void ViewerPreferences_PageMode_Null_ReturnsNull()
    {
        using var doc = Document.Create();
        doc.Pages.Add();

        var prefs = doc.GetOrCreateViewerPreferences();
        Assert.Null(prefs.PageMode);
    }
}
