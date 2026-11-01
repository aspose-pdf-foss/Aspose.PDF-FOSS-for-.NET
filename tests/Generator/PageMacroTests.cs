using System.IO;
using Aspose.Pdf;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Generator;

/// <summary>The page-number macros <c>$p</c> and <c>$P</c> in a page's paragraphs: replaced by
/// the page number and the page count by default, kept as literal text when the page's
/// <see cref="Page.ProcessSpecialSymbols"/> is false - in a text fragment and in a table cell
/// alike. Read back from the saved page.</summary>
public sealed class PageMacroTests
{
    [Fact]
    public void AFragmentsMacrosBecomeThePageNumberAndCount()
    {
        var text = Extracted(page => page.Paragraphs.Add(new TextFragment("page $p of $P")), process: true);
        Assert.Contains("page 1 of 1", text);
    }

    [Fact]
    public void AFragmentKeepsItsMacrosWhenThePageDoesNotProcessThem()
    {
        var text = Extracted(page => page.Paragraphs.Add(new TextFragment("page $p of $P")), process: false);
        Assert.Contains("page $p of $P", text);
    }

    [Fact]
    public void ACellsMacrosBecomeThePageNumberAndCount()
    {
        var text = Extracted(AddTable, process: true);
        Assert.Contains("page 1 of 1", text);
    }

    [Fact]
    public void ACellKeepsItsMacrosWhenThePageDoesNotProcessThem()
    {
        var text = Extracted(AddTable, process: false);
        Assert.Contains("page $p of $P", text);
    }

    private static void AddTable(Page page)
    {
        var table = new Table();
        table.Rows.Add().Cells.Add("page $p of $P");
        page.Paragraphs.Add(table);
    }

    private static string Extracted(System.Action<Page> fill, bool process)
    {
        using var ms = new MemoryStream();
        using (var document = new Document())
        {
            var page = document.Pages.Add();
            page.ProcessSpecialSymbols = process;
            fill(page);
            document.Save(ms);
        }
        ms.Position = 0;
        using var saved = new Document(ms);
        var absorber = new TextAbsorber();
        saved.Pages.Accept(absorber);
        return absorber.Text;
    }
}
