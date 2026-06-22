using Aspose.Pdf;
using Aspose.Pdf.Content;
using Aspose.Pdf.Tagged;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

public class StructureTreeBuilderTests
{
    [Fact]
    public void CreateTaggedPdf_SetsMarkInfo()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);
        tree.CreateElement("Document");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.True(doc2.IsTagged);
    }

    [Fact]
    public void CreateTaggedPdf_HasStructTreeRoot()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);
        tree.CreateElement("Document");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.True(doc2.HasStructTree);
        Assert.NotNull(doc2.StructTreeRoot);
    }

    [Fact]
    public void CreateElement_SetsStructureType()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);
        var docElem = tree.CreateElement("Document");

        Assert.Equal("Document", docElem.StructureType);
    }

    [Fact]
    public void CreateChild_NestedStructure()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);
        var docElem = tree.CreateElement("Document");
        var h1 = docElem.CreateChild("H1");
        var p = docElem.CreateChild("P");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var root = doc2.StructTreeRoot!;
        Assert.NotEmpty(root.Children);
        var docChild = root.Children[0];
        Assert.Equal("Document", docChild.StructureType);
        Assert.Equal(2, docChild.Children.Count);
        Assert.Equal("H1", docChild.Children[0].StructureType);
        Assert.Equal("P", docChild.Children[1].StructureType);
    }

    [Fact]
    public void SetTitle_PersistsOnElement()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);
        var docElem = tree.CreateElement("Document");
        var h1 = docElem.CreateChild("H1").SetTitle("Chapter 1");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var h1Elem = doc2.StructTreeRoot!.Children[0].Children[0];
        Assert.Equal("H1", h1Elem.StructureType);
        Assert.Equal("Chapter 1", h1Elem.Title);
    }

    [Fact]
    public void SetLanguage_PersistsOnElement()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);
        var docElem = tree.CreateElement("Document").SetLanguage("en-US");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var elem = doc2.StructTreeRoot!.Children[0];
        Assert.Equal("en-US", elem.Language);
    }

    [Fact]
    public void SetAltText_PersistsOnElement()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);
        var docElem = tree.CreateElement("Document");
        docElem.CreateChild("Figure").SetAltText("A photo of a sunset");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var figElem = doc2.StructTreeRoot!.Children[0].Children[0];
        Assert.Equal("Figure", figElem.StructureType);
        Assert.Equal("A photo of a sunset", figElem.AltText);
    }

    [Fact]
    public void SetActualText_PersistsOnElement()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);
        var docElem = tree.CreateElement("Document");
        docElem.CreateChild("Span").SetActualText("ffi");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var spanElem = doc2.StructTreeRoot!.Children[0].Children[0];
        Assert.Equal("ffi", spanElem.ActualText);
    }

    [Fact]
    public void MarkedContent_ProducesBdcEmc()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);
        var docElem = tree.CreateElement("Document");
        var pElem = docElem.CreateChild("P");

        var mc = pElem.AddMarkedContent(page);

        Assert.Equal(0, mc.Mcid);
        Assert.Equal("P", mc.Tag);
        Assert.Contains("/P", mc.BeginMarkedContent());
        Assert.Contains("MCID 0", mc.BeginMarkedContent());
        Assert.Contains("BDC", mc.BeginMarkedContent());
        Assert.Equal("EMC\n", mc.EndMarkedContent());
    }

    [Fact]
    public void MarkedContent_McidIncrementsAutomatically()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);
        var docElem = tree.CreateElement("Document");

        var mc1 = docElem.CreateChild("P").AddMarkedContent(page);
        var mc2 = docElem.CreateChild("P").AddMarkedContent(page);

        Assert.Equal(0, mc1.Mcid);
        Assert.Equal(1, mc2.Mcid);
    }

    [Fact]
    public void ContentStreamBuilder_MarkedContent()
    {
        var builder = new ContentStreamBuilder();
        var content = builder
            .BeginMarkedContent("P", 0)
            .BeginText()
            .SetFont("F1", 12)
            .MoveTextPosition(72, 720)
            .ShowText("Hello World")
            .EndText()
            .EndMarkedContent()
            .ToString();

        Assert.Contains("/P <</MCID 0>> BDC", content);
        Assert.Contains("(Hello World) Tj", content);
        Assert.Contains("EMC", content);
    }

    [Fact]
    public void ContentStreamBuilder_SetExtGState()
    {
        var builder = new ContentStreamBuilder();
        var content = builder
            .SetExtGState("GS0")
            .ToString();

        Assert.Equal("/GS0 gs\n", content);
    }

    [Fact]
    public void FullTaggedDocument_RoundTrip()
    {
        // Create a tagged PDF with structure: Document > H1 + P
        using var doc = Document.Create();
        var page = doc.Pages.Add();

        var tree = new StructureTreeBuilder(doc);
        var docElem = tree.CreateElement("Document");
        var h1 = docElem.CreateChild("H1");
        var p = docElem.CreateChild("P");

        var mc1 = h1.AddMarkedContent(page);
        var mc2 = p.AddMarkedContent(page);

        // Build content stream with marked content
        var builder = new ContentStreamBuilder();
        builder
            .BeginMarkedContent(mc1)
            .BeginText()
            .SetFont("Helv", 18)
            .MoveTextPosition(72, 720)
            .ShowText("Title")
            .EndText()
            .EndMarkedContent()
            .BeginMarkedContent(mc2)
            .BeginText()
            .SetFont("Helv", 12)
            .MoveTextPosition(72, 690)
            .ShowText("Paragraph text here.")
            .EndText()
            .EndMarkedContent();

        page.SetContentStream(builder.Build());
        tree.BuildParentTree();

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        // Verify structure
        Assert.True(doc2.IsTagged);
        var root = doc2.StructTreeRoot!;
        Assert.NotEmpty(root.Children);

        var docChild = root.Children[0];
        Assert.Equal("Document", docChild.StructureType);
        Assert.Equal(2, docChild.Children.Count);
        Assert.Equal("H1", docChild.Children[0].StructureType);
        Assert.Equal("P", docChild.Children[1].StructureType);
    }

    [Fact]
    public void AddRoleMapping_RoundTrip()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);
        var docElem = tree.CreateElement("Document");

        tree.AddRoleMapping("MyHeading", "H1");
        tree.AddRoleMapping("MyPara", "P");

        // Verify builder state
        var mappings = tree.GetRoleMappings();
        Assert.Equal(2, mappings.Count);
        Assert.Equal("H1", mappings["MyHeading"]);
        Assert.Equal("P", mappings["MyPara"]);

        // Save and re-read
        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var root = doc2.StructTreeRoot!;
        var roleMap = root.RoleMap;
        Assert.Equal("H1", roleMap["MyHeading"]);
        Assert.Equal("P", roleMap["MyPara"]);
    }

    [Fact]
    public void AddRoleMapping_CustomRoleMapsToStandardType()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);
        tree.CreateElement("Document");

        // Map a custom type to a standard Figure type
        tree.AddRoleMapping("Illustration", "Figure");

        // Use the custom type in the structure
        var docElem = tree.CreateElement("Document");
        docElem.CreateChild("Illustration").SetAltText("A custom illustration");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var root = doc2.StructTreeRoot!;
        var roleMap = root.RoleMap;
        Assert.Contains("Illustration", (IDictionary<string, string>)roleMap);
        Assert.Equal("Figure", roleMap["Illustration"]);
    }

    [Fact]
    public void AddRoleMapping_OverwritesExisting()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);
        tree.CreateElement("Document");

        tree.AddRoleMapping("Custom", "P");
        tree.AddRoleMapping("Custom", "H1"); // overwrite

        var mappings = tree.GetRoleMappings();
        Assert.Single(mappings);
        Assert.Equal("H1", mappings["Custom"]);
    }

    [Fact]
    public void DeeplyNestedStructure()
    {
        using var doc = Document.Create();
        doc.Pages.Add();
        var tree = new StructureTreeBuilder(doc);

        var docElem = tree.CreateElement("Document");
        var sect = docElem.CreateChild("Sect");
        var table = sect.CreateChild("Table");
        var tr = table.CreateChild("TR");
        var td1 = tr.CreateChild("TD");
        var td2 = tr.CreateChild("TD");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var root = doc2.StructTreeRoot!;
        var d = root.Children[0];
        Assert.Equal("Document", d.StructureType);
        var s = d.Children[0];
        Assert.Equal("Sect", s.StructureType);
        var t = s.Children[0];
        Assert.Equal("Table", t.StructureType);
        var row = t.Children[0];
        Assert.Equal("TR", row.StructureType);
        Assert.Equal(2, row.Children.Count);
        Assert.Equal("TD", row.Children[0].StructureType);
        Assert.Equal("TD", row.Children[1].StructureType);
    }
}
