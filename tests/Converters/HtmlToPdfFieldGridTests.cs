using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>A stylesheet form's control table laid out as a grid: each control a form field in its cell.</summary>
public class HtmlToPdfFieldGridTests
{
    // A layout rule on a wrapper takes the page off the plain UA flow without naming any installed face.
    private const string Sheet = "<style>.wrap { position: relative; top: 0 }</style>";

    /// <summary>Two boxes whose vertical middles are closer than this stand on one row.</summary>
    private const double SameRowPt = 8;

    private static Document Load(string html)
    {
        using var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(html)), new HtmlLoadOptions());
        using var saved = new MemoryStream();
        doc.Save(saved);
        saved.Position = 0;
        return new Document(saved);
    }

    private static List<(string Text, Rectangle Box)> Fragments(Page page)
    {
        var absorber = new TextFragmentAbsorber();
        page.Accept(absorber);
        return absorber.TextFragments.Cast<TextFragment>().Where(f => f.Text.Trim().Length > 0 && f.Rectangle is not null)
            .Select(f => (f.Text.Trim(), f.Rectangle!)).ToList();
    }

    private static List<Rectangle> Widgets(Page page)
        => page.Annotations.OfType<WidgetAnnotation>().Where(w => w.Rect is not null).Select(w => w.Rect!).ToList();

    private static string PageContent(Page page)
    {
        var reader = page.Reader;
        var contents = reader.Resolve(page.Dict.Get("Contents"));
        var text = new StringBuilder();
        if (contents is Core.PdfStream s) text.Append(Compat.Latin1.GetString(reader.DecodeStream(s)));
        else if (contents is Core.PdfArray parts)
            foreach (var part in parts)
                if (reader.ResolveStream(part) is { } ps) text.Append(Compat.Latin1.GetString(reader.DecodeStream(ps))).Append('\n');
        return text.ToString();
    }

    private static double MidY(Rectangle r) => (r.LLY + r.URY) / 2;

    private const string LabelGrid = "<table><tr><td>Name</td><td><input type=\"text\" name=\"n1\"></td></tr>"
        + "<tr><td>City</td><td><input type=\"text\" name=\"n2\"></td></tr></table>";

    private const string Toolbar = "<table><tr><td><input type=\"button\" value=\"Print\"></td>"
        + "<td><input type=\"button\" value=\"Close\"></td></tr></table>";

    private static string LabelRows(int rows)
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= rows; i++) sb.Append("<tr><td>Name ").Append(i).Append("</td><td><input type=\"text\" name=\"n").Append(i).Append("\"></td></tr>");
        return sb.ToString();
    }

    [Fact]
    public void TheFieldsOfRowsThatSpillOntoTheNextPageStandOnThatPage()
    {
        using var doc = Load("<html><head>" + Sheet + "</head><body><div class=\"wrap\"><table>" + LabelRows(60)
            + "</table></div><p>After table</p></body></html>");
        Assert.True(doc.Pages.Count >= 2, "the table runs onto a second page");
        Assert.Equal(60, doc.Form.Count);
        for (var p = 1; p <= doc.Pages.Count; p++)
        {
            var labels = Fragments(doc.Pages[p]).Where(f => f.Text.StartsWith("Name ")).ToList();
            var widgets = Widgets(doc.Pages[p]);
            Assert.Equal(labels.Count, widgets.Count);
            foreach (var label in labels)
                Assert.Contains(widgets, w => w.LLX > label.Box.URX && System.Math.Abs(MidY(w) - MidY(label.Box)) < SameRowPt);
        }
        // …and each field names the page its widget stands on
        foreach (var field in doc.Form.Cast<Field>())
            Assert.Contains(Widgets(doc.Pages[field.PageIndex]), w => System.Math.Abs(w.LLY - field.Rect!.LLY) < 0.01);
    }

    // A th-only first row: the header the grid draws again at the top of every page it runs onto.
    private const string HeaderRow = "<tr><th>All</th><th><input type=\"radio\" name=\"g\" value=\"all\"></th>"
        + "<th><input type=\"text\" name=\"h\"></th><th><input type=\"checkbox\" name=\"c\"></th></tr>";

    /// <summary>The widgets a page lists above its first body row: the header row's (whose label it draws).</summary>
    private static int HeaderWidgets(Page page)
    {
        var frags = Fragments(page);
        Assert.Contains(frags, f => f.Text == "All");
        var top = frags.Where(f => f.Text.StartsWith("Name")).Max(f => f.Box.URY);
        return Widgets(page).Count(w => MidY(w) > top);
    }

    [Fact]
    public void TheControlsOfAHeaderRowRepeatedOnEveryPageStandOnTheFirstPage()
    {
        // A widget stands on one page: the header's radio button, text box and check box stay on the page their
        // row was first built for, and the pages that draw the header again list only their own rows' fields.
        using var doc = Load("<html><head>" + Sheet + "</head><body><p>Intro</p><div class=\"wrap\"><table>" + HeaderRow
            + LabelRows(130) + "</table></div><p>After table</p></body></html>");
        Assert.True(doc.Pages.Count >= 3, "the table runs onto a third page");
        for (var p = 1; p <= doc.Pages.Count; p++)
        {
            var labels = Fragments(doc.Pages[p]).Count(f => f.Text.StartsWith("Name "));
            var header = p == 1 ? 3 : 0;
            Assert.Equal(header, HeaderWidgets(doc.Pages[p]));
            Assert.Equal(labels + header, Widgets(doc.Pages[p]).Count);
        }
        foreach (var field in doc.Form.Cast<Field>().Where(f => f is not RadioButtonField))
            Assert.Contains(Widgets(doc.Pages[field.PageIndex]), w => System.Math.Abs(w.LLY - field.Rect!.LLY) < 0.01);
    }

    [Fact]
    public void AGridClippedToAFloatColumnKeepsTheControlsOfItsHeaderRow()
    {
        // The rows the column clips leave the form; the header row the first page draws keeps its controls.
        using var doc = Load("<html><head>" + Sheet + "</head><body><div class=\"wrap\"><div style=\"float:left; width:50%\"><table>"
            + HeaderRow + LabelRows(60) + "</table></div><div style=\"float:left; width:50%\"><p>Right column</p></div></div><p>After</p></body></html>");
        Assert.Equal(3, HeaderWidgets(doc.Pages[1]));
        Assert.Contains(doc.Form.Cast<Field>(), f => f.PartialName == "h");
        Assert.Single(doc.Form.Fields.OfType<RadioButtonOptionField>());
    }

    private static string RadioRows(int rows)
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= rows; i++)
            sb.Append("<tr><td>Name ").Append(i).Append("</td><td><input type=\"text\" name=\"n").Append(i)
                .Append("\"></td><td><input type=\"radio\" name=\"pick\" value=\"v").Append(i).Append("\"></td></tr>");
        return sb.ToString();
    }

    [Fact]
    public void TheRadioButtonsOfRowsThatSpillOntoTheNextPageStandOnThatPage()
    {
        using var doc = Load("<html><head>" + Sheet + "</head><body><div class=\"wrap\"><table>" + RadioRows(60)
            + "</table></div><p>After table</p></body></html>");
        Assert.True(doc.Pages.Count >= 2, "the table runs onto a second page");
        for (var p = 1; p <= doc.Pages.Count; p++)
        {
            var labels = Fragments(doc.Pages[p]).Where(f => f.Text.StartsWith("Name ")).ToList();
            var widgets = Widgets(doc.Pages[p]);
            Assert.Equal(2 * labels.Count, widgets.Count);
            foreach (var label in labels)
                Assert.Equal(2, widgets.Count(w => w.LLX > label.Box.URX && System.Math.Abs(MidY(w) - MidY(label.Box)) < SameRowPt));
        }
    }

    [Fact]
    public void AGridClippedToAFloatColumnKeepsOnlyTheRadioButtonsOfTheRowsItDraws()
    {
        using var doc = Load("<html><head>" + Sheet + "</head><body><div class=\"wrap\"><div style=\"float:left; width:50%\"><table>"
            + RadioRows(60) + "</table></div><div style=\"float:left; width:50%\"><p>Right column</p></div></div><p>After</p></body></html>");
        var drawn = 0;
        for (var p = 1; p <= doc.Pages.Count; p++)
        {
            var labels = Fragments(doc.Pages[p]).Count(f => f.Text.StartsWith("Name "));
            Assert.Equal(2 * labels, Widgets(doc.Pages[p]).Count);
            drawn += labels;
        }
        Assert.True(drawn < 60, "the column clips the grid at the page foot");
        Assert.Equal(drawn, doc.Form.Fields.OfType<RadioButtonOptionField>().Count());
    }

    [Fact]
    public void AGridClippedToAFloatColumnKeepsOnlyTheFieldsOfTheRowsItDraws()
    {
        using var doc = Load("<html><head>" + Sheet + "</head><body><div class=\"wrap\"><div style=\"float:left; width:50%\"><table>"
            + LabelRows(60) + "</table></div><div style=\"float:left; width:50%\"><p>Right column</p></div></div><p>After</p></body></html>");
        var drawn = 0;
        for (var p = 1; p <= doc.Pages.Count; p++)
        {
            var labels = Fragments(doc.Pages[p]).Count(f => f.Text.StartsWith("Name "));
            Assert.Equal(labels, Widgets(doc.Pages[p]).Count);
            drawn += labels;
        }
        Assert.True(drawn < 60, "the column clips the grid at the page foot");
        Assert.Equal(drawn, doc.Form.Count);
    }

    [Fact]
    public void AGridRebuiltOnItsDeclaredBoxKeepsItsFieldsInTheirCells()
    {
        // A sectioned page: its grid declaring an absolute width wider than the flow is laid out again on that
        // width, on the sheet grown to hold it.
        var rows = new StringBuilder();
        for (var i = 1; i <= 4; i++)
            rows.Append("<tr><td>Label ").Append(i).Append("</td><td><input type=\"text\" name=\"t").Append(i).Append("\" value=\"v").Append(i)
                .Append("\"></td><td>Other ").Append(i).Append("</td><td><select name=\"s").Append(i).Append("\"><option>Yes</option><option>No</option></select></td><td><input type=\"checkbox\" name=\"c").Append(i).Append("\"></td></tr>");
        using var doc = Load("<html><head>" + Sheet + "</head><body><div class=\"wrap\"><table style=\"width:650px\">" + rows
            + "</table></div><p>After</p><hr style=\"page-break-after:always\"><p>Second section</p></body></html>");
        var page = doc.Pages[1];
        Assert.Equal(12, doc.Form.Count);
        var frags = Fragments(page);
        Assert.DoesNotContain(frags, f => f.Text.Contains("YesNo"));
        Assert.DoesNotContain(frags, f => f.Text == "v1");
        var widgets = Widgets(page);
        Assert.Equal(12, widgets.Count);
        foreach (var label in frags.Where(f => f.Text.StartsWith("Label ")))
            Assert.Equal(3, widgets.Count(w => w.LLX > label.Box.URX && System.Math.Abs(MidY(w) - MidY(label.Box)) < SameRowPt));
    }

    [Fact]
    public void AToolbarOfPushButtonsIsNoFieldGrid()
    {
        // On its own and on a page whose label table holds text boxes alike, its buttons are drawn round their
        // captions, not made push-button fields.
        foreach (var before in new[] { "", LabelGrid + "<p>Middle</p>" })
        {
            using var doc = Load("<html><head>" + Sheet + "</head><body><div class=\"wrap\">" + before + Toolbar
                + "<p>Below the toolbar</p></div></body></html>");
            Assert.DoesNotContain(doc.Form.Cast<Field>(), f => f is ButtonField);
            var texts = Fragments(doc.Pages[1]).Select(f => f.Text).ToList();
            Assert.Contains("Print", texts);
            Assert.Contains("Close", texts);
        }
    }

    [Fact]
    public void ADataTableBesideAFieldGridKeepsItsFrame()
    {
        // `border="3"` frames the table with a 3px rule round 1px cell rules, beside a text-box grid as anywhere.
        using var doc = Load("<html><head><style>.wrap { position: relative; top: 0 } .t { border-collapse: collapse }</style></head>"
            + "<body><div class=\"wrap\">" + LabelGrid + "<p>Middle</p><table class=\"t\" border=\"3\"><tr><td>Alpha</td><td>Beta</td></tr></table>"
            + "<p>After</p></div></body></html>");
        var content = PageContent(doc.Pages[1]);
        Assert.Contains("2.25 w", content);
        Assert.Contains("0.75 w", content);
    }

    [Fact]
    public void AFieldGridOfACollapsingClassKeepsItsFrame()
    {
        // The collapsed chrome of the UA check box grid is that grid's own: a text-box grid whose class collapses
        // its borders keeps the 3px frame of its border attribute round 1px cell rules, as a data table does.
        using var doc = Load("<html><head><style>.wrap { position: relative; top: 0 } .t { border-collapse: collapse }</style></head>"
            + "<body><div class=\"wrap\"><table class=\"t\" border=\"3\"><tr><td>Name</td><td><input type=\"text\" name=\"n1\"></td></tr>"
            + "<tr><td>City</td><td><input type=\"text\" name=\"n2\"></td></tr></table><p>After</p></div></body></html>");
        Assert.Equal(2, doc.Form.Count);
        var content = PageContent(doc.Pages[1]);
        Assert.Contains("2.25 w", content);
        Assert.Contains("0.75 w", content);
    }

    [Fact]
    public void TextUnderADataTableOfAFormStandsWhereItStandsOnAPageWithoutTheForm()
    {
        // A sheet with a chain rule seats a paragraph under a table by the box flow; the line-box drop a field
        // grid leaves is that grid's alone.
        const string sheet = "<style>.wrap { position: relative; top: 0 } #main p { color: #333333 }</style>";
        const string data = "<table><tr><td>Alpha</td><td>Beta</td></tr><tr><td>Gamma</td><td>Delta</td></tr></table>";
        double GapUnderData(string before)
        {
            using var doc = Load("<html><head>" + sheet + "</head><body><div id=\"main\" class=\"wrap\">" + before + "<p>Middle</p>" + data
                + "<p>Under the data</p></div></body></html>");
            var frags = Fragments(doc.Pages[1]);
            return frags.Single(f => f.Text == "Delta").Box.LLY - frags.Single(f => f.Text == "Under the data").Box.LLY;
        }
        Assert.Equal(GapUnderData("<p>Name City</p>"), GapUnderData(LabelGrid), 2);
    }

    [Fact]
    public void APushButtonSizedAsAShareOfItsCellStaysInItsColumn()
    {
        // The long last column squeezes the button's column to its buttons' own boxes: the 100% button fills it
        // and stops where the next column starts; the disabled one is read-only.
        using var doc = Load("<html><head>" + Sheet + "</head><body><div class=\"wrap\"><table>"
            + "<tr><td>Name</td><td><input type=\"text\" name=\"n1\"></td><td><input type=\"submit\" name=\"go\" value=\"Go\" style=\"width:100%\"></td>"
            + "<td>Edge words that run on long enough to fill the line of the page with text, and more text after them</td></tr>"
            + "<tr><td>City</td><td><input type=\"text\" name=\"n2\"></td><td><input type=\"button\" name=\"lock\" value=\"Locked\" disabled></td>"
            + "<td>More</td></tr></table><p>After</p></div></body></html>");
        var go = doc.Form.Cast<Field>().Single(f => f.PartialName == "go");
        var locked = doc.Form.Cast<Field>().Single(f => f.PartialName == "lock");
        var edge = Fragments(doc.Pages[1]).First(f => f.Text.StartsWith("Edge")).Box;
        Assert.True(go.Rect!.URX <= edge.LLX + 0.5, $"the button ends at {go.Rect.URX:0.#}, past the next column at {edge.LLX:0.#}");
        Assert.True(go.Rect.Width >= locked.Rect!.Width, "…and fills its own column");
        Assert.True(locked.ReadOnly);
    }

    [Fact]
    public void ATableWhoseControlsTheGridCannotSeatKeepsThemAll()
    {
        // A list with no attributes, and a text box standing between two cells: the grid's cell walk seats
        // neither, so such a table keeps the flow that draws every control.
        using var bare = Load("<html><head>" + Sheet + "</head><body><div class=\"wrap\"><table><tr><td>Name</td><td><input type=\"text\" name=\"n1\"></td></tr>"
            + "<tr><td>Agree</td><td><select><option>Yes</option><option>No</option></select></td></tr></table><p>After</p></div></body></html>");
        Assert.Contains(bare.Form.Cast<Field>(), f => f is ComboBoxField);
        Assert.DoesNotContain(Fragments(bare.Pages[1]), f => f.Text.Contains("YesNo"));
        using var stray = Load("<html><head>" + Sheet + "</head><body><div class=\"wrap\"><table><tr><td>Name</td><td><input type=\"text\" name=\"n1\"></td></tr>"
            + "<tr><td>City</td><input type=\"text\" name=\"stray\"><td><input type=\"text\" name=\"n2\"></td></tr></table><p>After</p></div></body></html>");
        Assert.Contains(stray.Form.Cast<Field>(), f => f.PartialName == "stray");
    }

    [Fact]
    public void TwoTextBoxesOfOneCellStandSideBySide()
    {
        // The grid seats one field per line of its cell, so a split zip code would stack its boxes: such a table
        // keeps the flow, which lines them up.
        using var doc = Load("<html><head>" + Sheet + "</head><body><div class=\"wrap\"><table><tr><td>Name</td><td><input type=\"text\" name=\"n1\"></td></tr>"
            + "<tr><td>Zip</td><td><input type=\"text\" name=\"zip\" size=\"5\"> - <input type=\"text\" name=\"zip4\" size=\"4\"></td></tr></table><p>After</p></div></body></html>");
        var zip = doc.Form.Cast<Field>().Single(f => f.PartialName == "zip").Rect!;
        var zip4 = doc.Form.Cast<Field>().Single(f => f.PartialName == "zip4").Rect!;
        Assert.True(zip4.LLX > zip.URX && System.Math.Abs(MidY(zip4) - MidY(zip)) < SameRowPt,
            $"the second box stands beside the first: {zip4.LLX:0.#},{MidY(zip4):0.#} against {zip.URX:0.#},{MidY(zip):0.#}");
    }

    [Fact]
    public void AControlAfterATableNestedInItsCellKeepsTheGrid()
    {
        // The nested table closes before the control, which stands in the outer cell the grid seats: a text box,
        // or a check box with no text beside it.
        foreach (var cell in new[] { "<table><tr><td>Hint</td></tr></table><input type=\"text\" name=\"n2\">",
                     "<table><tr><td>&nbsp;</td></tr></table><input type=\"checkbox\" name=\"c2\">" })
        {
            using var doc = Load("<html><head>" + Sheet + "</head><body><div class=\"wrap\"><table><tr><td>Name</td><td><input type=\"text\" name=\"n1\"></td></tr>"
                + "<tr><td>City</td><td>" + cell + "</td></tr></table><p>After</p></div></body></html>");
            var name = Fragments(doc.Pages[1]).Single(f => f.Text == "Name").Box;
            var n1 = doc.Form.Cast<Field>().Single(f => f.PartialName == "n1").Rect!;
            Assert.True(n1.LLX > name.URX && System.Math.Abs(MidY(n1) - MidY(name)) < SameRowPt,
                $"the first text box stands beside its label: box {n1.LLX:0.#},{MidY(n1):0.#}, label ends {name.URX:0.#} at {MidY(name):0.#}");
        }
    }
}
