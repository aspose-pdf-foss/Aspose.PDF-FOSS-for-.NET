using Xunit;

namespace Aspose.Pdf.Tests;

public class OptionalContentBuilderTests
{
    [Fact]
    public void AddLayer_SingleVisible_RoundTrip()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.AddLayer("Foreground");
        builder.Build();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.True(doc2.HasLayers);
        var oc = doc2.OptionalContent!;
        Assert.Equal(1, oc.Count);
        Assert.Equal("Foreground", oc[0].Name);
        Assert.True(oc[0].IsVisible);
    }

    [Fact]
    public void AddLayer_MultipleLayers_PreservesOrder()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.AddLayer("Layer1");
        builder.AddLayer("Layer2");
        builder.AddLayer("Layer3");
        builder.Build();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var oc = doc2.OptionalContent!;
        Assert.Equal(3, oc.Count);
        Assert.Equal("Layer1", oc[0].Name);
        Assert.Equal("Layer2", oc[1].Name);
        Assert.Equal("Layer3", oc[2].Name);
    }

    [Fact]
    public void AddLayer_HiddenLayer_AppearsInOffArray()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.AddLayer("Visible");
        builder.AddLayer("Hidden", visible: false);
        builder.Build();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var oc = doc2.OptionalContent!;
        Assert.True(oc.FindByName("Visible")!.IsVisible);
        Assert.False(oc.FindByName("Hidden")!.IsVisible);
    }

    [Fact]
    public void AddLayer_AllHidden_AllInOffArray()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.AddLayer("A", visible: false);
        builder.AddLayer("B", visible: false);
        builder.Build();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var oc = doc2.OptionalContent!;
        Assert.False(oc[0].IsVisible);
        Assert.False(oc[1].IsVisible);
    }

    [Fact]
    public void AddLayer_AllVisible_NoOffArray()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.AddLayer("X");
        builder.AddLayer("Y");
        builder.Build();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var oc = doc2.OptionalContent!;
        Assert.True(oc[0].IsVisible);
        Assert.True(oc[1].IsVisible);
    }

    [Fact]
    public void Build_NoLayers_DoesNotAddOCProperties()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.Build();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.False(doc2.HasLayers);
        Assert.Null(doc2.OptionalContent);
    }

    [Fact]
    public void AddLayer_ReturnsLayerEntry_WithCorrectProperties()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);

        var entry1 = builder.AddLayer("First", visible: true);
        var entry2 = builder.AddLayer("Second", visible: false);

        Assert.Equal("First", entry1.Name);
        Assert.True(entry1.Visible);
        Assert.Equal("Second", entry2.Name);
        Assert.False(entry2.Visible);
    }

    [Fact]
    public void Build_SetsObjectNumbersOnLayerEntries()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        var entry = builder.AddLayer("Test");

        Assert.Equal(0, entry.ObjectNumber);
        builder.Build();
        Assert.NotEqual(0, entry.ObjectNumber);
    }

    [Fact]
    public void Build_DisplayOrder_MatchesAddOrder()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.AddLayer("C");
        builder.AddLayer("A");
        builder.AddLayer("B");
        builder.Build();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var order = doc2.OptionalContent!.GetDisplayOrder();
        Assert.NotNull(order);
        Assert.Equal(3, order!.Count);
        Assert.Equal("C", order[0]);
        Assert.Equal("A", order[1]);
        Assert.Equal("B", order[2]);
    }

    [Fact]
    public void Build_FindByName_WorksAfterRoundTrip()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.AddLayer("Alpha");
        builder.AddLayer("Beta", visible: false);
        builder.AddLayer("Gamma");
        builder.Build();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var oc = doc2.OptionalContent!;

        Assert.NotNull(oc.FindByName("Alpha"));
        Assert.NotNull(oc.FindByName("Beta"));
        Assert.NotNull(oc.FindByName("Gamma"));
        Assert.Null(oc.FindByName("Delta"));
    }

    [Fact]
    public void Build_Names_MatchAddedLayers()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.AddLayer("One");
        builder.AddLayer("Two");
        builder.Build();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var names = doc2.OptionalContent!.Names;
        Assert.Equal(new[] { "One", "Two" }, names);
    }

    [Fact]
    public void BeginLayer_ReturnsCorrectBdcOperator()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        var entry = builder.AddLayer("Test");
        builder.Build();

        var bdc = OptionalContentBuilder.BeginLayer(entry);
        Assert.Contains("BDC", bdc);
        Assert.Contains("/OC", bdc);
        Assert.Contains($"/MC{entry.ObjectNumber}", bdc);
    }

    [Fact]
    public void EndLayer_ReturnsEmcOperator()
    {
        var emc = OptionalContentBuilder.EndLayer();
        Assert.Contains("EMC", emc);
    }

    [Fact]
    public void Build_MixedVisibility_CorrectRoundTrip()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.AddLayer("Visible1", visible: true);
        builder.AddLayer("Hidden1", visible: false);
        builder.AddLayer("Visible2", visible: true);
        builder.AddLayer("Hidden2", visible: false);
        builder.AddLayer("Visible3", visible: true);
        builder.Build();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var oc = doc2.OptionalContent!;

        Assert.Equal(5, oc.Count);
        Assert.True(oc.FindByName("Visible1")!.IsVisible);
        Assert.False(oc.FindByName("Hidden1")!.IsVisible);
        Assert.True(oc.FindByName("Visible2")!.IsVisible);
        Assert.False(oc.FindByName("Hidden2")!.IsVisible);
        Assert.True(oc.FindByName("Visible3")!.IsVisible);
    }

    [Fact]
    public void Build_ThenSetVisibility_Persists()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.AddLayer("Layer1", visible: true);
        builder.AddLayer("Layer2", visible: true);
        builder.Build();

        var saved1 = doc.ToArray();
        using var doc2 = Document.Open(saved1);
        var oc = doc2.OptionalContent!;

        // Toggle visibility after building
        Assert.True(oc.SetVisibility("Layer1", false));

        var saved2 = doc2.ToArray();
        using var doc3 = Document.Open(saved2);
        var oc2 = doc3.OptionalContent!;

        Assert.False(oc2.FindByName("Layer1")!.IsVisible);
        Assert.True(oc2.FindByName("Layer2")!.IsVisible);
    }

    [Fact]
    public void Build_LayerWithSpecialCharactersInName()
    {
        using var doc = Document.Create();
        var builder = new OptionalContentBuilder(doc);
        builder.AddLayer("Layer (1)");
        builder.AddLayer("Layer-2/3");
        builder.Build();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var oc = doc2.OptionalContent!;

        Assert.Equal(2, oc.Count);
        Assert.Equal("Layer (1)", oc[0].Name);
        Assert.Equal("Layer-2/3", oc[1].Name);
    }
}
