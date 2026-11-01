using System.Globalization;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>A PDF date the library reads is read as under the invariant culture whatever the culture of the machine:
/// a date cut short, the sign of its timezone standing where the next field would be, keeps its fields on a machine
/// whose culture writes its signs behind a letter or direction mark (ar-SA, he-IL and fa-IR on ICU).</summary>
public class PdfDateReadCultureTests
{
    /// <summary>Dates cut short after the year, month, day, hour and minute, a timezone's sign in the next field's place.</summary>
    private static readonly string[] CutShort = { "D:2016+5", "D:201605+2", "D:20160525+10", "D:2016052514+5", "D:201605251430+5" };

    public static TheoryData<string> SignsBehindAMark => new()
    {
        "ar-SA", // a letter mark before the sign, which a culture parse will not take for '+'
        "he-IL", // a left-to-right mark before the sign
        CultureScope.SyntheticName, // both, on every runtime
    };

    /// <summary>The creation date a document's info dictionary reports for each of <see cref="CutShort"/>, read under
    /// <paramref name="culture"/>.</summary>
    private static string[] CreationDates(string culture)
    {
        using var scope = new CultureScope(culture);
        return CutShort.Select(date =>
        {
            using var doc = Document.Open(PdfBuilder.BuildWithDocumentInfo(creationDate: date));
            return doc.Info.CreationDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }).ToArray();
    }

    /// <summary>The content of the box an open popup shows for a note whose modification date is
    /// <paramref name="date"/>, built under <paramref name="culture"/>.</summary>
    private static string PopupContent(string date, string culture)
    {
        using var scope = new CultureScope(culture);
        using var doc = new Document();
        var note = new PdfDictionary();
        note.Set("T", new PdfString(Compat.Latin1.GetBytes("Reviewer")));
        note.Set("M", new PdfString(Compat.Latin1.GetBytes(date)));
        var rect = new PdfArray();
        foreach (var side in new[] { 100, 100, 300, 200 }) rect.Add(new PdfInteger(side));
        var popup = new PdfDictionary();
        popup.Set("Open", PdfBoolean.True);
        popup.Set("Rect", rect);
        popup.Set("Parent", note);
        var form = PopupAppearance.BuildOpenPopupForm(popup, doc.Reader);
        return Compat.Latin1.GetString(Assert.IsType<PdfStream>(form).RawData);
    }

    [Theory]
    [MemberData(nameof(SignsBehindAMark))]
    public void AnInfoDateCutShortBeforeItsTimezoneReadsAsUnderTheInvariantCulture(string culture)
    {
        var expected = CreationDates(string.Empty);
        Assert.All(expected, date => Assert.StartsWith("2016-", date, StringComparison.Ordinal)); // no date is lost

        Assert.Equal(expected, CreationDates(culture));
    }

    [Theory]
    [MemberData(nameof(SignsBehindAMark))]
    public void AnOpenPopupShowsANotesDateCutShortBeforeItsTimezoneAsUnderTheInvariantCulture(string culture)
    {
        var expected = PopupContent("D:20160525+10", string.Empty);
        Assert.Contains("(5/25/2016 ", expected, StringComparison.Ordinal); // the date reaches the box

        Assert.Equal(expected, PopupContent("D:20160525+10", culture));
    }
}
