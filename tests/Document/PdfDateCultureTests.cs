using Aspose.Pdf.Annotations;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Tests.Security;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>A date the library writes into a PDF or an XMP packet is a Gregorian date with ':' between
/// its hours, minutes and seconds, whatever the calendar and time separator of the machine's culture.</summary>
public class PdfDateCultureTests
{
    private static readonly DateTime When = new(2026, 10, 19, 12, 30, 45, DateTimeKind.Local);

    /// <summary>Cultures whose default calendar is not the Gregorian one: th-TH counts Buddhist years,
    /// ar-SA Um Al-Qura (Hijri) ones, the synthetic culture Buddhist ones on every runtime.</summary>
    public static TheoryData<string> Calendars => new() { "th-TH", "ar-SA", CultureScope.SyntheticName };

    /// <summary>As <see cref="Calendars"/>, and fi-FI, whose time separator is '.', as the synthetic culture's is.</summary>
    public static TheoryData<string> CalendarsAndTimeSeparators => new() { "th-TH", "ar-SA", "fi-FI", CultureScope.SyntheticName };

    private static void AssertThisYear(DateTime written)
        => Assert.InRange(written.Year, DateTime.Now.Year - 1, DateTime.Now.Year + 1);

    [Theory]
    [MemberData(nameof(Calendars))]
    public void AnAnnotationsModifiedDateIsWrittenInGregorianYears(string culture)
    {
        using var doc = new Document();
        var annot = new TextAnnotation(doc.Pages.Add(), new Rectangle(100, 100, 120, 120));

        using (new CultureScope(culture))
            annot.Modified = When;

        // The clock fields in Gregorian years; a local time carries its offset after them.
        Assert.StartsWith("D:20261019123045", annot.ModifiedDate);
        Assert.Equal(When, annot.Modified);
    }

    [Theory]
    [MemberData(nameof(Calendars))]
    public void ARedactionsCreationDateIsWrittenInGregorianYears(string culture)
    {
        using var doc = new Document();
        var annot = new RedactionAnnotation(doc.Pages.Add(), new Rectangle(100, 100, 120, 120));

        using (new CultureScope(culture))
            annot.CreationDate = When;

        Assert.Equal(When, annot.CreationDate);
    }

    [Theory]
    [MemberData(nameof(Calendars))]
    public void AMarkupAnnotationsCreationDateIsWrittenInGregorianYears(string culture)
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        TextAnnotation made, set;

        using (new CultureScope(culture))
        {
            made = new TextAnnotation(page, new Rectangle(100, 100, 120, 120));
            set = new TextAnnotation(page, new Rectangle(200, 100, 220, 120)) { CreationDate = When };
        }

        AssertThisYear(made.CreationDate);
        Assert.Equal(When, set.CreationDate);
    }

    [Theory]
    [MemberData(nameof(Calendars))]
    public void AnAttachmentsCreationAndModificationDatesAreWrittenInGregorianYears(string culture)
    {
        var spec = new FileSpecification(new MemoryStream(new byte[] { 1, 2, 3 }), "data.bin");
        var parameters = new FileParams(spec);

        using (new CultureScope(culture))
        {
            parameters.CreationDate = When;
            parameters.ModDate = When;
        }

        Assert.Equal(When, parameters.CreationDate);
        Assert.Equal(When, parameters.ModDate);
    }

    [Theory]
    [MemberData(nameof(Calendars))]
    public void ARubberStampsCreationDateIsWrittenInGregorianYears(string culture)
    {
        var editor = new PdfContentEditor();
        editor.BindPdf(PdfBuilder.BuildMinimal());

        using (new CultureScope(culture))
            editor.CreateRubberStamp(1, new System.Drawing.Rectangle(100, 100, 80, 40), "Approved", "Checked", System.Drawing.Color.Red);

        using var saved = new MemoryStream();
        editor.Save(saved);
        using var doc = Document.Open(saved.ToArray());
        var stamp = Assert.IsAssignableFrom<MarkupAnnotation>(doc.Pages[1].Annotations[1]);
        AssertThisYear(stamp.CreationDate);
    }

    [Theory]
    [MemberData(nameof(Calendars))]
    public void ASignaturesSigningTimeIsWrittenInGregorianYears(string culture)
    {
        var options = new SignatureOptions { SigningDate = new DateTime(2026, 10, 19, 12, 30, 45, DateTimeKind.Utc) };

        byte[] signed;
        using (new CultureScope(culture))
            signed = PdfSigner.Sign(PdfBuilder.BuildMinimal(), PdfSignerTests.CreateTestCertificate(), options);

        using var doc = Document.Open(signed);
        Assert.Equal(new DateTime(2026, 10, 19, 12, 30, 45), Signature.EnumerateSignatures(doc).Single().Date);
    }

    [Theory]
    [MemberData(nameof(CalendarsAndTimeSeparators))]
    public void TheXmpDatesADocumentWithoutAPacketReportsFromItsInfoAreIsoDates(string culture)
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        doc.Info.CreationDate = When;
        doc.Info.ModDate = When;

        string? created, modified;
        using (new CultureScope(culture))
        {
            created = doc.Metadata["xmp:CreateDate"]?.ToString();
            modified = doc.Metadata["xmp:ModifyDate"]?.ToString();
        }

        Assert.Equal("2026-10-19T12:30:45", created);
        Assert.Equal("2026-10-19T12:30:45", modified);
    }

    [Theory]
    [MemberData(nameof(CalendarsAndTimeSeparators))]
    public void TheXmpDatesAFileInfoSaveWritesAreIsoDates(string culture)
    {
        var info = new PdfFileInfo(PdfBuilder.BuildMinimal())
        {
            CreationDateValue = When,
            ModDateValue = When,
        };

        byte[] saved;
        using (new CultureScope(culture))
            saved = info.SaveNewInfoWithXmp();

        using var doc = Document.Open(saved);
        Assert.StartsWith("2026-10-19T12:30:45", doc.Metadata["xmp:CreateDate"]?.ToString());
        Assert.StartsWith("2026-10-19T12:30:45", doc.Metadata["xmp:ModifyDate"]?.ToString());
    }
}
