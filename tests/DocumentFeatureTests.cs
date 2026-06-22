using Aspose.Pdf;
using Aspose.Pdf.Devices;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

/// <summary>Smoke tests for Document.Merge(MergeOptions, …),
/// Document.SendTo(DocumentDevice, …) and Document.Convert(Fixup, …).</summary>
public class DocumentFeatureTests
{
    [Fact]
    public void Merge_RemoveSignatures_StripsVFromSignatureFields()
    {
        // Build a doc with two pages, the second carrying a signature field with /V.
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Pages.Add();
        var page = doc.Pages[doc.PageCount];
        var fields = doc.Form;
        var field = new Aspose.Pdf.Forms.SignatureField(doc, new Rectangle(0, 0, 100, 50));
        fields!.Add(field);
        field.Dict.Set("V", new Aspose.Pdf.Core.PdfDictionary());

        using var partner = Document.Open(PdfBuilder.BuildMinimal());
        doc.Merge(new Document.MergeOptions { RemoveSignatures = true }, partner);

        var formAfter = doc.Form!;
        foreach (var f in formAfter.Fields)
        {
            if (f.Type == Aspose.Pdf.Forms.FieldType.Signature)
                Assert.False(f.Dict.ContainsKey("V"));
        }
    }

    [Fact]
    public void Merge_KeepFieldsUnique_DisambiguatesCollidingNames()
    {
        // Build two single-page docs each carrying a TextBoxField named "Email".
        var doc1 = Document.Create();
        doc1.Pages.Add();
        var f1 = new Aspose.Pdf.Forms.TextBoxField(doc1, new Rectangle(0, 0, 80, 20));
        f1.SetPartialName("Email");
        doc1.Form!.Add(f1);
        var doc2 = Document.Create();
        doc2.Pages.Add();
        var f2 = new Aspose.Pdf.Forms.TextBoxField(doc2, new Rectangle(0, 0, 80, 20));
        f2.SetPartialName("Email");
        doc2.Form!.Add(f2);

        doc1.Merge(new Document.MergeOptions { KeepFieldsUnique = true }, doc2);
        var names = new System.Collections.Generic.HashSet<string>();
        foreach (var f in doc1.Form!.Fields)
            Assert.True(names.Add(f.FullName ?? ""));
    }

    [Fact]
    public void SendTo_PdfDocumentDevice_RoundTripsAsPdf()
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        using var output = new System.IO.MemoryStream();
        doc.SendTo(new PdfDocumentDevice(), output);
        output.Position = 0;
        Assert.True(output.Length > 0);
        using var roundTrip = Document.Open(output);
        Assert.Equal(doc.PageCount, roundTrip.PageCount);
    }

    [Fact]
    public void SendTo_PageRange_EmitsOnlySelectedPages()
    {
        var editor = new Aspose.Pdf.Facades.PdfFileEditor();
        var threePages = editor.Concatenate(
            PdfBuilder.BuildMinimal(),
            PdfBuilder.BuildMinimal(),
            PdfBuilder.BuildMinimal());
        using var doc = Document.Open(threePages);
        Assert.Equal(3, doc.PageCount);

        using var output = new System.IO.MemoryStream();
        doc.SendTo(new PdfDocumentDevice(), fromPage: 2, toPage: 3, output);
        output.Position = 0;
        using var slice = Document.Open(output);
        Assert.Equal(2, slice.PageCount);
    }

    [Fact]
    public void Convert_RotatePagesToLandscape_SetsRotateOnPortraitPages()
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        // Minimal page is 612 × 792 (portrait).
        var ok = doc.Convert(Fixup.RotatePagesToLandscape, System.IO.Stream.Null, onlyValidation: false);
        Assert.True(ok);
        var rotate = (int)(doc.Pages[1].Dict.Get("Rotate") is Aspose.Pdf.Core.PdfInteger n ? n.Value : 0);
        Assert.Equal(90, rotate);
    }

    [Fact]
    public void Convert_RotatePagesToPortrait_IsIdempotentOnPortrait()
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        var ok = doc.Convert(Fixup.RotatePagesToPortrait, System.IO.Stream.Null, onlyValidation: false);
        Assert.True(ok);
        // Page is already portrait — /Rotate must remain unset/0.
        Assert.False(doc.Pages[1].Dict.ContainsKey("Rotate"));
    }

    [Fact]
    public void Convert_EmbedMissingFonts_ThrowsNotSupported()
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        Assert.Throws<System.NotSupportedException>(
            () => doc.Convert(Fixup.EmbedMissingFonts, System.IO.Stream.Null, onlyValidation: false));
    }

    [Fact]
    public void Background_PrependsFillRectToPageContent()
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        doc.Background = Color.FromRgb(255, 0, 0);
        var saved = doc.ToArray();
        // Search the saved bytes for the fill-rect prologue (look for the
        // "re f Q" sequence emitted by EmitBackgroundOnPages).
        var asAscii = System.Text.Encoding.ASCII.GetString(saved);
        Assert.Contains("re f Q", asAscii);
        Assert.Contains(" rg ", asAscii);  // colour set
    }

    [Fact]
    public void Actions_WriteToCatalog_OnSave()
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        doc.Actions.BeforeClosing = new Aspose.Pdf.Annotations.NamedAction(Aspose.Pdf.Annotations.PredefinedAction.Print);
        var saved = doc.ToArray();
        using var roundTrip = Document.Open(saved);
        var catalog = roundTrip.Reader.Catalog;
        var aa = roundTrip.Reader.ResolveDict(catalog.Get("AA"));
        Assert.NotNull(aa);
        Assert.True(aa!.ContainsKey("WC"));
    }

    [Fact]
    public void Encrypt_WithCustomSecurityHandler_ThrowsNotSupported()
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        Assert.Throws<System.NotSupportedException>(
            () => doc.Encrypt("u", "o", Aspose.Pdf.Permissions.PrintDocument, (Aspose.Pdf.Security.ICustomSecurityHandler)null!));
    }

    [Fact]
    public void Encrypt_WithPublicCertificates_ThrowsNotSupported()
    {
        using var doc = Document.Open(PdfBuilder.BuildMinimal());
        Assert.Throws<System.NotSupportedException>(
            () => doc.Encrypt(
                Aspose.Pdf.Permissions.PrintDocument,
                Aspose.Pdf.CryptoAlgorithm.AESx128,
                new System.Collections.Generic.List<System.Security.Cryptography.X509Certificates.X509Certificate2>()));
    }
}
