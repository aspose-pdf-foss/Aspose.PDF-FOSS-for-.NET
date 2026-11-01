using System.IO;
using System.Linq;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

/// <summary>An XFA template's occurrence counts and column spans are read as the numbers they are, whatever the
/// culture of the machine: max="-1" (no maximum) and colSpan="-1" (the rest of the row) keep their minus sign, and a
/// page area's max="+2" serves two pages.</summary>
public class XfaOccurrenceCultureTests
{
    private static readonly string[] Items = { "Alpha", "Bravo", "Charlie" };

    /// <summary>A dynamic XFA form: a row subform of two cells - an item field and a note drawn over the rest of
    /// the row - that may repeat without limit, and data holding three rows.</summary>
    private static byte[] BuildForm()
    {
        var template = "<template xmlns=\"http://www.xfa.org/schema/xfa-template/2.8/\"><subform name=\"form1\" layout=\"tb\">"
            + "<pageSet><pageArea name=\"Page\"><medium short=\"612pt\" long=\"792pt\"/>"
            + "<contentArea x=\"36pt\" y=\"36pt\" w=\"540pt\" h=\"720pt\"/></pageArea></pageSet>"
            + "<subform name=\"row\" layout=\"table\" columnWidths=\"100pt 100pt 100pt\"><occur min=\"0\" max=\"-1\"/>"
            + "<subform layout=\"row\">"
            + "<field name=\"item\" w=\"100pt\" h=\"20pt\"><ui><textEdit/></ui></field>"
            + "<draw name=\"note\" colSpan=\"-1\" h=\"20pt\"><para hAlign=\"center\"/><value><text>NOTE</text></value></draw>"
            + "</subform></subform></subform></template>";
        var data = "<xfa:datasets xmlns:xfa=\"http://www.xfa.org/schema/xfa-data/1.0/\"><xfa:data><form1>"
            + string.Concat(Items.Select(i => $"<row><item>{i}</item></row>"))
            + "</form1></xfa:data></xfa:datasets>";
        return BuildForm(template, data);
    }

    /// <summary>A dynamic XFA form of two page areas - a letter sheet that serves at most <paramref name="max"/>
    /// pages, then a smaller sheet for the rest - and a body of a hundred lines, which fills more pages than that.</summary>
    private static byte[] BuildPagedForm(string max)
    {
        var lines = string.Concat(Enumerable.Range(1, 100).Select(i =>
            $"<draw w=\"300pt\" h=\"20pt\"><font typeface=\"Helvetica\" size=\"10pt\"/><value><text>Line {i}</text></value></draw>"));
        var template = "<template xmlns=\"http://www.xfa.org/schema/xfa-template/2.8/\"><subform name=\"form1\" layout=\"tb\">"
            + "<pageSet relation=\"orderedOccurrence\">"
            + "<pageArea name=\"First\"><occur max=\"" + max + "\"/><medium short=\"612pt\" long=\"792pt\"/>"
            + "<contentArea x=\"36pt\" y=\"36pt\" w=\"540pt\" h=\"720pt\"/></pageArea>"
            + "<pageArea name=\"Rest\"><medium short=\"500pt\" long=\"700pt\"/>"
            + "<contentArea x=\"36pt\" y=\"36pt\" w=\"428pt\" h=\"628pt\"/></pageArea></pageSet>"
            + "<subform name=\"body\" layout=\"tb\" w=\"540pt\">" + lines + "</subform></subform></template>";
        return BuildForm(template, null);
    }

    /// <summary>A PDF that holds <paramref name="template"/> as a dynamic XFA form, with <paramref name="data"/> as its
    /// datasets where given.</summary>
    private static byte[] BuildForm(string template, string? data)
    {
        const string content = "BT /F1 12 Tf 72 700 Td (Please wait...) Tj ET";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /NeedsRendering true /AcroForm 6 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            data is null ? "<< /Fields [] /XFA [(template) 7 0 R] >>" : "<< /Fields [] /XFA [(template) 7 0 R (datasets) 8 0 R] >>",
            $"<< /Length {template.Length} >>\nstream\n{template}\nendstream",
        };
        if (data is not null) objects.Add($"<< /Length {data.Length} >>\nstream\n{data}\nendstream");
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Compat.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    /// <summary>The size of each page the paged form converts to under <paramref name="culture"/>, its first area's
    /// maximum written <paramref name="max"/>.</summary>
    private static string[] ConvertedPageSizes(string max, string culture)
    {
        using var scope = new CultureScope(culture);
        using var saved = new MemoryStream();
        using (var doc = new Document(new MemoryStream(BuildPagedForm(max))))
        {
            doc.Form.Type = FormType.Standard;
            doc.Save(saved);
        }
        saved.Position = 0;
        using var reopened = new Document(saved);
        return Enumerable.Range(1, reopened.Pages.Count).Select(p => reopened.Pages[p].MediaBox)
            .Select(box => FormattableString.Invariant($"{box.Width:0}x{box.Height:0}")).ToArray();
    }

    private static string ConvertedText(string culture)
    {
        using var scope = new CultureScope(culture);
        using var saved = new MemoryStream();
        using (var doc = new Document(new MemoryStream(BuildForm())))
        {
            doc.Form.Type = FormType.Standard;
            doc.Save(saved);
        }
        saved.Position = 0;
        using var reopened = new Document(saved);
        var fields = string.Join("\n", reopened.Form.Fields.Select(f => f.FullName + "=" + f.Value));
        // Where each note stands tells the width it was laid out in: centred over the rest of its row.
        var notes = new TextFragmentAbsorber("NOTE");
        reopened.Pages[1].Accept(notes);
        var noteSeats = string.Join(" ", notes.TextFragments.Select(f => FormattableString.Invariant($"{f.Rectangle?.LLX ?? double.NaN:0}")));
        return fields + "\n" + noteSeats;
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the minus sign, which a culture parse will not take for '-'
    [InlineData("he-IL")] // a left-to-right mark before the minus sign
    [InlineData(CultureScope.SyntheticName)] // all of these at once, on every runtime
    public void ARowWithoutAMaximumRepeatsForEveryDataRowAsUnderTheInvariantCulture(string culture)
    {
        var expected = ConvertedText(string.Empty);
        Assert.All(Items, i => Assert.Contains(i, expected, StringComparison.Ordinal));

        Assert.Equal(expected, ConvertedText(culture));
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the plus sign, which a culture parse will not take for '+'
    [InlineData("he-IL")] // a left-to-right mark before the plus sign
    [InlineData(CultureScope.SyntheticName)] // both, on every runtime
    public void APageAreaWhoseMaximumHasAPlusSignServesThatManyPagesAsUnderTheInvariantCulture(string culture)
    {
        var expected = ConvertedPageSizes("+2", string.Empty);
        // The letter sheet serves two pages, then the smaller sheet takes the rest of the body.
        Assert.Equal(new[] { "612x792", "612x792", "500x700" }, expected.Take(3));
        Assert.Equal(expected, ConvertedPageSizes("2", string.Empty)); // a maximum's '+' is no part of its value

        Assert.Equal(expected, ConvertedPageSizes("+2", culture));
    }

    [Fact]
    public void ACellSpanningTheRestOfItsRowIsCentredOverIt()
    {
        // The row's columns start at 36pt and are 100pt wide; the note spans the last two, 136pt to 336pt.
        var seats = ConvertedText(string.Empty).Split('\n').Last().Split(' ')
            .Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(Items.Length, seats.Count);
        Assert.All(seats, x => Assert.InRange(x, 200, 250));
    }
}
