using Aspose.Pdf.Annotations;
using Xunit;
using static Aspose.Pdf.Tests.Redaction.Redacting;

namespace Aspose.Pdf.Tests.Redaction;

/// <summary>The annotations <see cref="RedactionAnnotation.Redact"/> removes: every one touching the
/// rectangle, with its popup and replies - what they hold is not drawn, so a sliver of overlap is
/// enough - and none elsewhere.</summary>
public class RedactRemovesAnnotationsTests
{
    private static byte[] AnnotatedPage() => RawPage.Build(
        "BT /F1 12 Tf 200 700 Td (text) Tj ET",
        "<< /Font << /F1 5 0 R >> >>", string.Empty, "/Annots [6 0 R 7 0 R 8 0 R 9 0 R 10 0 R]",
        new RawPage.Obj(RawPage.Helvetica),
        new RawPage.Obj("<< /Type /Annot /Subtype /Text /Rect [210 700 230 720] /Contents (NOTE-SECRET) /Popup 7 0 R >>"),
        new RawPage.Obj("<< /Type /Annot /Subtype /Popup /Rect [300 600 400 700] /Parent 6 0 R >>"),
        new RawPage.Obj("<< /Type /Annot /Subtype /Text /Rect [500 100 520 120] /IRT 6 0 R /Contents (REPLY-SECRET) >>"),
        new RawPage.Obj("<< /Type /Annot /Subtype /Link /Rect [100 690 205 712] " +
                        "/A << /S /URI /URI (mailto:LINK-SECRET@example.com) >> >>"),
        new RawPage.Obj("<< /Type /Annot /Subtype /Text /Rect [50 50 70 70] /Contents (FAR-KEPT) >>"));

    [Fact]
    public void AnAnnotationTouchingTheRectangleGoesWithItsPopupAndReplies()
    {
        var saved = Save(AnnotatedPage(), new Rectangle(195, 695, 300, 715));

        Assert.False(SavedFile.Holds(saved, "NOTE-SECRET"));
        Assert.False(SavedFile.Holds(saved, "REPLY-SECRET"));
        Assert.False(SavedFile.Holds(saved, "LINK-SECRET"));
        Assert.True(SavedFile.Holds(saved, "FAR-KEPT"));
        using var doc = Document.Open(saved);
        var types = doc.Pages[1].Annotations.Select(a => a.AnnotationType).ToList();
        Assert.Equal(2, types.Count);
        Assert.Contains(AnnotationType.Redact, types);
        Assert.Contains(AnnotationType.Text, types);
    }

    [Fact]
    public void RedactAreaRemovesTheSameAnnotations()
    {
        using var editor = new Aspose.Pdf.Facades.PdfAnnotationEditor();
        editor.BindPdf(new MemoryStream(AnnotatedPage()));
        editor.RedactArea(1, new Rectangle(195, 695, 300, 715), System.Drawing.Color.Black);
        using var output = new MemoryStream();
        editor.Save(output);

        var saved = output.ToArray();
        Assert.False(SavedFile.Holds(saved, "NOTE-SECRET"));
        Assert.False(SavedFile.Holds(saved, "LINK-SECRET"));
        Assert.True(SavedFile.Holds(saved, "FAR-KEPT"));
    }
}
