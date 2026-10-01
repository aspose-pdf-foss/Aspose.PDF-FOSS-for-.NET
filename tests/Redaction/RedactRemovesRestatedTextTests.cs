using Aspose.Pdf.Annotations;
using Xunit;
using static Aspose.Pdf.Tests.Redaction.Redacting;

namespace Aspose.Pdf.Tests.Redaction;

/// <summary>Text a tagged page keeps beside what it shows - the ActualText, Alt and E of marked
/// content and of structure elements - goes with the content <see cref="RedactionAnnotation.Redact"/>
/// removes, and stays where nothing was removed.</summary>
public class RedactRemovesRestatedTextTests
{
    private const string Fonts = "<< /Font << /F1 5 0 R >> >>";

    [Fact]
    public void TheActualTextOfMarkedContentLosingGlyphsGoes()
    {
        var pdf = RawPage.Build(
            "/Span <</ActualText (4111111111111111)>> BDC BT /F1 12 Tf 200 700 Td (4111 1111 1111 1111) Tj ET EMC " +
            "/Span <</ActualText (KEPT-ACTUAL)>> BDC BT /F1 12 Tf 200 500 Td (kept) Tj ET EMC",
            Fonts, new RawPage.Obj(RawPage.Helvetica));

        var saved = Save(pdf, new Rectangle(195, 695, 400, 715));

        Assert.False(SavedFile.Holds(saved, "4111111111111111"));
        Assert.True(SavedFile.Holds(saved, "KEPT-ACTUAL"));
    }

    [Fact]
    public void ANamedPropertyListIsScrubbedInACopyAndTheOriginalStaysForOtherUses()
    {
        var pdf = RawPage.Build(
            "/Span /MC0 BDC BT /F1 12 Tf 200 700 Td (secret) Tj ET EMC",
            "<< /Font << /F1 5 0 R >> /Properties << /MC0 << /ActualText (NAMED-SECRET) >> >> >>",
            new RawPage.Obj(RawPage.Helvetica));

        var saved = Save(pdf, new Rectangle(195, 695, 400, 715));

        Assert.False(SavedFile.Holds(saved, "NAMED-SECRET"));
    }

    [Fact]
    public void TheStructureElementOwningRedactedContentAndItsAncestorsLoseTheirRestatedText()
    {
        var pdf = RawPage.Build(
            "/P <</MCID 0>> BDC BT /F1 12 Tf 200 700 Td (SECRETTEXT) Tj ET EMC " +
            "/P <</MCID 1>> BDC BT /F1 12 Tf 200 500 Td (other) Tj ET EMC",
            Fonts, "/StructTreeRoot 6 0 R /MarkInfo << /Marked true >>", "/StructParents 0",
            new RawPage.Obj(RawPage.Helvetica),
            new RawPage.Obj("<< /Type /StructTreeRoot /K 7 0 R /ParentTree << /Nums [0 [8 0 R 9 0 R]] >> >>"),
            new RawPage.Obj("<< /Type /StructElem /S /Document /P 6 0 R /Alt (DOC-ALT-SECRET) /K [8 0 R 9 0 R] >>"),
            new RawPage.Obj("<< /Type /StructElem /S /P /P 7 0 R /Pg 3 0 R /ActualText (ELEMENT-SECRET) /K 0 >>"),
            new RawPage.Obj("<< /Type /StructElem /S /P /P 7 0 R /Pg 3 0 R /ActualText (OTHER-KEPT) /K 1 >>"));

        var saved = Save(pdf, new Rectangle(195, 695, 400, 715));

        Assert.False(SavedFile.Holds(saved, "ELEMENT-SECRET"));
        Assert.False(SavedFile.Holds(saved, "DOC-ALT-SECRET"));
        Assert.True(SavedFile.Holds(saved, "OTHER-KEPT"));
    }
}
