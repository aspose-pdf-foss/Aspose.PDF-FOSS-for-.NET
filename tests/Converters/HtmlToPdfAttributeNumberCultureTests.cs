using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>A number in a markup attribute or a style is read the same whatever the culture of the machine: a font
/// size, a list's start, a counter, a cell's span, a spacer's height and a font weight keep their signs and a percent
/// width its decimal point.</summary>
public class HtmlToPdfAttributeNumberCultureTests
{
    /// <summary>The text of every page.</summary>
    private static string Text(string html, string culture)
    {
        using var scope = new CultureScope(culture);
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(html)), new HtmlLoadOptions());
        var absorber = new TextAbsorber();
        doc.Pages.Accept(absorber);
        return absorber.Text;
    }

    /// <summary>A filing of three pages, each opening with the rule and contents link that make the markup one,
    /// then <paramref name="page"/>'s markup for its number.</summary>
    private static string Filing(Func<int, string> page)
    {
        var html = new StringBuilder("<html><body style=\"font-family:ARIAL;font-size:10pt\">");
        for (var n = 1; n <= 3; n++)
            html.Append("<p style=\"page-break-before:always\"></p><hr size=\"3\" noshade><h5><a href=\"#toc\">Table of Contents</a></h5>")
                .Append(page(n));
        return html.Append("</body></html>").ToString();
    }

    /// <summary>Each fragment of <paramref name="phrases"/> on every page, with its size and seat.</summary>
    private static string Fragments(string html, string culture, params string[] phrases)
    {
        using var scope = new CultureScope(culture);
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(html)), new HtmlLoadOptions());
        var seen = new StringBuilder();
        foreach (var phrase in phrases)
        {
            var absorber = new TextFragmentAbsorber(phrase);
            doc.Pages.Accept(absorber);
            foreach (TextFragment f in absorber.TextFragments)
                seen.Append(FormattableString.Invariant(
                    $"{phrase}: {f.TextState.FontSize:0.##} pt on {f.Page?.Number} at {f.Rectangle?.LLX:0.#},{f.Rectangle?.LLY:0.#}\n"));
        }
        return seen.ToString();
    }

    /// <summary>Each fragment of <paramref name="phrases"/> with its size and seat, on a page holding
    /// <paramref name="html"/> as an HTML fragment.</summary>
    private static string OnAPage(string html, string culture, params string[] phrases)
    {
        using var scope = new CultureScope(culture);
        using var saved = new MemoryStream();
        using (var doc = new Document())
        {
            doc.Pages.Add().Paragraphs.Add(new HtmlFragment(html));
            doc.Save(saved);
        }
        using var reopened = Document.Open(saved.ToArray());
        var seen = new List<string>();
        foreach (var phrase in phrases)
        {
            var absorber = new TextFragmentAbsorber(phrase);
            reopened.Pages[1].Accept(absorber);
            seen.AddRange(absorber.TextFragments.Select(f => FormattableString.Invariant(
                $"{f.Text}: {f.TextState.FontSize:0.##} pt at {f.Rectangle?.LLX:0.#},{f.Rectangle?.LLY:0.#}")));
        }
        return string.Join(" | ", seen);
    }

    /// <summary>The size of each form field widget on the converted markup's first page.</summary>
    private static string Widgets(string html, string culture)
    {
        using var scope = new CultureScope(culture);
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(html)), new HtmlLoadOptions());
        return string.Join(" | ", doc.Pages[1].Annotations.OfType<WidgetAnnotation>().Where(w => w.Rect is not null)
            .Select(w => FormattableString.Invariant($"{w.Rect!.Width:0.#} x {w.Rect.Height:0.#}")));
    }

    /// <summary>The face the first fragment of each of <paramref name="phrases"/> is set in.</summary>
    private static string?[] Faces(string html, string culture, params string[] phrases)
    {
        using var scope = new CultureScope(culture);
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(html)), new HtmlLoadOptions());
        return phrases.Select(phrase =>
        {
            var absorber = new TextFragmentAbsorber(phrase);
            doc.Pages.Accept(absorber);
            return absorber.TextFragments.FirstOrDefault()?.TextState.Font?.FontName;
        }).ToArray();
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the minus sign, which a culture parse will not take for '-'
    [InlineData("he-IL")] // a left-to-right mark before the minus sign
    [InlineData(CultureScope.SyntheticName)] // all of these at once, on every runtime
    public void AFontSizeOfMinusOneInPreformattedTextSetsTheSizeAsUnderTheInvariantCulture(string culture)
    {
        const string html = "<html><head><style>p { margin: 0; }</style></head>"
            + "<body><pre><font size=\"-1\">smaller\n</font>plain</pre></body></html>";

        var expected = Fragments(html, string.Empty, "smaller", "plain");
        Assert.Contains("smaller", expected, StringComparison.Ordinal);
        Assert.Equal(expected, Fragments(html, culture, "smaller", "plain"));
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void AFontSizeOfMinusOneInASplitPanelSetsTheSizeAsUnderTheInvariantCulture(string culture)
    {
        // A two-panel page whose side cell stands in a row of its own after the heading's table closes.
        const string html = "<html><body><table width=\"100%\"><tr><td>Heading</td></tr></table>"
            + "<td rowspan=\"2\"><table width=\"100%\"><tr><td width=\"16%\">Side</td>"
            + "<td width=\"84%\"><p>Main <font size=\"-1\">smaller</font> text</p></td></tr></table></td></body></html>";

        var expected = Fragments(html, string.Empty, "smaller", "Main");
        Assert.Contains("smaller", expected, StringComparison.Ordinal);
        Assert.Equal(expected, Fragments(html, culture, "smaller", "Main"));
    }

    [Theory]
    [InlineData("uk-UA")] // decimal comma
    [InlineData("tr-TR")] // decimal comma, '.' between thousands: 33.5 would read as 335
    [InlineData(CultureScope.SyntheticName)]
    public void AFractionalPercentWidthInAFilingTableSetsTheColumnAsUnderTheInvariantCulture(string culture)
    {
        var html = Filing(n => "<table width=\"100%\"><tr><td width=\"33.5%\" style=\"font-family:ARIAL\">Left " + n
            + "</td><td style=\"font-family:ARIAL\">Right " + n + "</td></tr></table>");

        var expected = Fragments(html, string.Empty, "Right");
        Assert.Contains("Right", expected, StringComparison.Ordinal);
        Assert.Equal(expected, Fragments(html, culture, "Right"));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData(CultureScope.SyntheticName)] // ',' as its decimal separator, '.' between thousands
    public void APercentWidthInAFilingTableSkipsAGroupSeparatorAsOnAnEnglishMachine(string culture)
    {
        // A ',' between the digits of a width groups thousands, as an en-US machine has always read it.
        string Table(string tableWidth, string cellWidth) => Filing(n => "<table width=\"" + tableWidth + "\"><tr><td width=\""
            + cellWidth + "\" style=\"font-family:ARIAL\">Left " + n + "</td><td style=\"font-family:ARIAL\">Right " + n + "</td></tr></table>");

        var expected = Fragments(Table("50%", "335%"), culture, "Right");
        Assert.Contains("Right", expected, StringComparison.Ordinal);
        Assert.Equal(expected, Fragments(Table("5,0%", "33,5%"), culture, "Right"));
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the minus sign
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void AnOrderedListCountingFromBelowZeroNumbersItsItemsAsUnderTheInvariantCulture(string culture)
    {
        const string html = "<html><body><ol start=\"-2\"><li>Alpha</li><li>Bravo</li></ol>"
            + "<ol style=\"counter-reset: item -5\"><li>Charlie</li></ol></body></html>";

        var expected = Text(html, string.Empty);
        Assert.Contains("Charlie", expected, StringComparison.Ordinal);
        Assert.Equal(expected, Text(html, culture));
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the minus sign
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void AnHtmlFragmentInOneFontSizedAStepDownSetsTheSizeAsUnderTheInvariantCulture(string culture)
    {
        const string html = "<font face=\"Times New Roman\" size=\"-1\">Small <u>under</u> tail</font>";

        var expected = OnAPage(html, string.Empty, "Small");
        Assert.Contains("Small", expected, StringComparison.Ordinal);
        Assert.Equal(expected, OnAPage(html, culture, "Small"));
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the sign, which a culture parse will not take for '-' or '+'
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void AnHtmlFragmentsFontSizesAStepDownAndUpSetTheSizesAsUnderTheInvariantCulture(string culture)
    {
        // A class attribute keeps the fragment off the one-font layout of the test above: the general
        // layout reads a font tag's size where it reads its face.
        const string html = "<p class=\"note\"><font face=\"Arial\" size=\"-1\">Alpha words</font></p>"
            + "<p class=\"note\"><font face=\"Arial\" size=\"+1\">Bravo words</font></p>";

        var expected = OnAPage(html, string.Empty, "Alpha", "Bravo");
        Assert.Contains("Alpha: 10 pt", expected, StringComparison.Ordinal); // size 2 of the legacy scale
        Assert.Contains("Bravo: 13.5 pt", expected, StringComparison.Ordinal); // size 4
        Assert.Equal(expected, OnAPage(html, culture, "Alpha", "Bravo"));
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void AFontSizeAStepDownAndUpInAFormGridCellSetsTheSizeAsUnderTheInvariantCulture(string culture)
    {
        // A width-percent wrapper over cells in Verdana spans lays the fragment out as a form grid, whose
        // table reader takes a font tag's size in a cell itself.
        const string html = "<div style='width: 92%'><font face='Verdana'><table border='1' cellspacing='0' width='100%'>"
            + "<tr><td><span style='font-family: Verdana; font-size: 8pt'>Name of the applicant</span></td>"
            + "<td><span style='font-family: Verdana; font-size: 8pt'>Alpha</span></td></tr>"
            + "<tr><td><font size='-1'>Small</font></td><td><span style='font-family: Verdana; font-size: 8pt'>Bravo</span></td></tr>"
            + "<tr><td><font size='+1'>Large</font></td><td><span style='font-family: Verdana; font-size: 8pt'>Charlie</span></td></tr>"
            + "</table></font></div>";

        var expected = OnAPage(html, string.Empty, "Small", "Large", "Charlie");
        Assert.Contains("Small: 10 pt", expected, StringComparison.Ordinal);
        Assert.Contains("Large: 13.5 pt", expected, StringComparison.Ordinal);
        Assert.Equal(expected, OnAPage(html, culture, "Small", "Large", "Charlie"));
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void AFontSizeOfPlusOneInAFilingSetsTheSizeAsUnderTheInvariantCulture(string culture)
    {
        var html = Filing(n => "<p>Plain <font size=\"+1\">sized</font> text on page " + n + "</p>");

        var expected = Fragments(html, string.Empty, "sized", "Plain");
        Assert.Contains("sized", expected, StringComparison.Ordinal);
        Assert.Equal(expected, Fragments(html, culture, "sized", "Plain"));
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the sign, which a culture parse will not take for '+'
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void ATableCellsSpansWithAPlusSignSpanAsUnderTheInvariantCulture(string culture)
    {
        // A browser reads the number after a span's '+', as an en-US machine always did.
        static string Table(string span) => "<html><body><table border=\"1\"><tr><td colspan=\"" + span + "\">Wide heading cell</td>"
            + "<td rowspan=\"" + span + "\">Tall</td></tr><tr><td>Left</td><td>Right</td></tr></table></body></html>";

        var expected = Fragments(Table("2"), string.Empty, "Tall", "Right");
        Assert.Equal(expected, Fragments(Table("+2"), string.Empty, "Tall", "Right"));
        Assert.Equal(expected, Fragments(Table("+2"), culture, "Tall", "Right"));
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void AFontWeightWithAPlusSignIsBoldAsUnderTheInvariantCulture(string culture)
    {
        const string html = "<html><head><style>.heavy { font-weight: +700 }</style></head><body>"
            + "<p style=\"font-weight:+700\">Inline words</p><p class=\"heavy\">Sheet words</p><p>Plain words</p></body></html>";
        string[] phrases = { "Inline", "Sheet", "Plain" };

        var expected = Faces(html, string.Empty, phrases);
        Assert.NotEqual(expected[2], expected[0]); // the bold cut, as a browser draws weight 700
        Assert.NotEqual(expected[2], expected[1]);
        Assert.Equal(expected, Faces(html, culture, phrases));
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void AFilingTablesSpanAndSpacerHeightWithAPlusSignAreReadAsUnderTheInvariantCulture(string culture)
    {
        string Table(string sign) => Filing(n => "<table width=\"100%\"><tr><td colspan=\"" + sign + "2\" style=\"font-family:ARIAL\">Wide " + n
            + "</td></tr><tr><td style=\"font-family:ARIAL\">Left " + n + "</td><td style=\"font-family:ARIAL\">Right " + n + "</td></tr>"
            + "<tr><td height=\"" + sign + "40\"></td></tr><tr><td style=\"font-family:ARIAL\">Below " + n + "</td></tr></table>");

        var expected = Fragments(Table(string.Empty), string.Empty, "Right", "Below");
        Assert.Equal(expected, Fragments(Table("+"), string.Empty, "Right", "Below"));
        Assert.Equal(expected, Fragments(Table("+"), culture, "Right", "Below"));
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void AFormControlsSizeColumnsAndRowsWithAPlusSignSizeItAsUnderTheInvariantCulture(string culture)
    {
        // A positioned wrapper takes the page off the plain flow onto the control grid, which sizes a control
        // from its size, cols and rows.
        static string Form(string sign) => "<html><head><style>.wrap { position: relative; top: 0 }</style></head><body>"
            + "<div class=\"wrap\"><table><tr><td>Name</td><td><input type=\"text\" name=\"n\" size=\"" + sign + "40\"></td></tr>"
            + "<tr><td>Notes</td><td><textarea name=\"t\" cols=\"" + sign + "30\" rows=\"" + sign + "4\"></textarea></td></tr>"
            + "</table></div></body></html>";

        var expected = Widgets(Form(string.Empty), string.Empty);
        Assert.Equal(expected, Widgets(Form("+"), string.Empty));
        Assert.Equal(expected, Widgets(Form("+"), culture));
    }

    [Theory]
    [InlineData("ar-SA")]
    [InlineData("he-IL")]
    [InlineData(CultureScope.SyntheticName)]
    public void AStyleSheetsColumnCountWithAPlusSignFlowsTheColumnsAsUnderTheInvariantCulture(string culture)
    {
        static string Columns(string count) => "<html><head><style>.cols { columns: " + count + " }</style></head><body>"
            + "<div class=\"cols\">" + string.Concat(Enumerable.Range(1, 6).Select(n => "<p>Paragraph " + n
                + " of the column flow, long enough to take a line or two of the page's width.</p>")) + "</div></body></html>";

        var expected = Fragments(Columns("2"), string.Empty, "Paragraph 6");
        Assert.Equal(expected, Fragments(Columns("+2"), string.Empty, "Paragraph 6"));
        Assert.Equal(expected, Fragments(Columns("+2"), culture, "Paragraph 6"));
    }
}
