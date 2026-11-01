using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Converters;

/// <summary>Form controls keep their names and their disabled state through HTML import; text after a table stands
/// below its last row; a hidden input leaves its table a grid.</summary>
public class HtmlToPdfControlsAndTableEdgeTests
{
    private static Document Convert(string html)
    {
        var saved = new MemoryStream();
        using (var doc = new Document(new MemoryStream(Encoding.UTF8.GetBytes(html)), new HtmlLoadOptions()))
            doc.Save(saved);
        saved.Position = 0;
        return new Document(saved);
    }

    private static TextFragment Fragment(Document doc, string text)
    {
        var absorber = new TextFragmentAbsorber(text);
        doc.Pages[1].Accept(absorber);
        return Assert.Single(absorber.TextFragments.Cast<TextFragment>());
    }

    [Fact]
    public void ACheckboxIsNamedAfterItsInput_AndADisabledOneIsReadOnly()
    {
        using var doc = Convert("<html><body><form><p>Agree <input type='checkbox' name='agreedToTerms' checked></p>"
            + "<p>Gap <input type='checkbox' id='gap' disabled></p></form></body></html>");

        var boxes = doc.Form.Fields.OfType<CheckboxField>().ToDictionary(f => f.PartialName ?? "");
        Assert.False(boxes["agreedToTerms"].ReadOnly);
        Assert.True(boxes["agreedToTerms"].Checked);
        Assert.True(boxes["gap"].ReadOnly);
    }

    [Fact]
    public void AGroupOfDisabledRadioButtonsIsReadOnly()
    {
        using var doc = Convert("<html><body><form><p><input type='radio' name='open' value='a'> <input type='radio' name='open' value='b'></p>"
            + "<p><input type='radio' name='locked' value='a' disabled> <input type='radio' name='locked' value='b' disabled></p></form></body></html>");

        Assert.False(((Field)doc.Form["open"]).ReadOnly);
        Assert.True(((Field)doc.Form["locked"]).ReadOnly);
    }

    [Fact]
    public void TheFirstLineAfterATableStandsBelowItsLastRow()
    {
        const string cell = "<p style=\"font-family:Arial; font-size:10pt; margin:0\">{0}</p>";
        var rows = string.Concat(Enumerable.Range(1, 4).Select(i =>
            $"<tr><td width=\"100\">{string.Format(cell, "LABEL" + i)}</td><td>{string.Format(cell, "VALUE" + i)}</td></tr>"));
        using var doc = Convert("<html><head><style>p.MsoNormal, li.MsoNormal, div.MsoNormal { margin: 0cm; font-size: 11pt; font-family: \"Calibri\", sans-serif; mso-fareast-language: EN-US; }</style></head>"
            + $"<body><table width=\"740\" border=\"0\">{rows}</table><div><p class=\"MsoNormal\">FIRSTLINE</p></div></body></html>");

        var lastRow = Fragment(doc, "LABEL4").Rectangle!;
        var first = Fragment(doc, "FIRSTLINE").Rectangle!;
        Assert.True(first.URY <= lastRow.LLY, $"the line after the table ({first}) reaches into its last row ({lastRow})");
    }

    [Fact]
    public void ABlankLineAfterATableKeepsItsPlace()
    {
        // The same mail table followed by a spacer paragraph, then a line: a spacer draws no glyphs into the
        // table's last row, so the line under it stands a line box higher than under a paragraph of text.
        const string cell = "<p style=\"font-family:Arial; font-size:10pt; margin:0\">{0}</p>";
        var rows = string.Concat(Enumerable.Range(1, 4).Select(i =>
            $"<tr><td width=\"100\">{string.Format(cell, "LABEL" + i)}</td><td>{string.Format(cell, "VALUE" + i)}</td></tr>"));
        double NextLineTop(string spacer)
        {
            using var doc = Convert("<html><head><style>p.MsoNormal, li.MsoNormal, div.MsoNormal { margin: 0cm; font-size: 11pt; font-family: \"Calibri\", sans-serif; mso-fareast-language: EN-US; }</style></head>"
                + $"<body><table width=\"740\" border=\"0\">{rows}</table><div><p class=\"MsoNormal\">{spacer}</p><p class=\"MsoNormal\">NEXTLINE</p></div></body></html>");
            return Fragment(doc, "NEXTLINE").Rectangle!.URY;
        }

        var underBlank = NextLineTop("&nbsp;");
        var underText = NextLineTop("SPACER");
        Assert.True(underBlank - underText > 5, $"under a blank line {underBlank:0.#}, under a line of text {underText:0.#}");
    }

    [Fact]
    public void AHiddenInputInACellLeavesTheTableAGrid()
    {
        using var doc = Convert("<html><body><table border=\"1\"><tr><td>Name<input type=\"hidden\" name=\"id\" value=\"7\"></td><td>Amount</td></tr>"
            + "<tr><td>Alpha</td><td>100</td></tr></table></body></html>");

        var name = Fragment(doc, "Name").Rectangle!;
        var amount = Fragment(doc, "Amount").Rectangle!;
        Assert.True(System.Math.Abs(name.LLY - amount.LLY) < 1, $"the cells of a row are not on one line: {name} / {amount}");
        Assert.True(amount.LLX > name.URX);
    }
}
