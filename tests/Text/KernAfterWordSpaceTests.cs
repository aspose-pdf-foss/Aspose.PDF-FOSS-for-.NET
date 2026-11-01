using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.Tests.Redaction;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>On a page whose text runs in columns, a kern inside one array that follows the space
/// ending a word adds no column padding, however wide it is: the drawn space is the gap. A kern
/// with no drawn space before it, and a move between two show operators, still pad to the column.</summary>
public sealed class KernAfterWordSpaceTests
{
    // A column line below the tested one gives the page its grid: words at 70, 130 and 250.
    private const string Grid = "BT /F1 10 Tf 1 0 0 1 70 680 Tm (A) Tj 1 0 0 1 130 680 Tm (B) Tj 1 0 0 1 250 680 Tm (C) Tj ET\n";

    private const string Fonts = "<< /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >> >> >>";

    /// <summary>Minimum run of spaces that reads as a column jump rather than a word space.</summary>
    private const int ColumnPad = 10;

    [Theory]
    [InlineData("[(Put)( ) -15000 (=)( 3,94)] TJ", "Put = 3,94")]
    [InlineData("[(Put ) -15000 (=)( 3,94)] TJ", "Put = 3,94")]
    [InlineData("[(Put)(  ) -15000 (=)( 3,94)] TJ", "Put  = 3,94")]
    [InlineData("[(Put)( ) -3000 (=)( 3,94)] TJ", "Put = 3,94")]
    [InlineData("[(Put)( ) -1000 (=)( 3,94)] TJ", "Put = 3,94")]
    [InlineData("[(Put)( ) -10 -15000 (=)( 3,94)] TJ", "Put = 3,94")]
    [InlineData("10 Tw [(Put )-15000 (=)] TJ", "Put =")]
    public void AKernAfterAWordsDrawnSpaceAddsNoColumnPadding(string shows, string expected)
    {
        Assert.Equal(expected, Line(shows));
    }

    [Theory]
    [InlineData("[(Put) -15000 (=)( 3,94)] TJ")]
    [InlineData("[(Put X) -15000 (=)( 3,94)] TJ")]
    [InlineData("(Put ) Tj 150 0 Td (= 3,94) Tj")]
    [InlineData("(Put) Tj 150 0 Td (= 3,94) Tj")]
    public void AKernWithNoDrawnSpaceBeforeItOrAMoveBetweenShowsStillPadsToTheColumn(string shows)
    {
        var line = Line(shows);
        Assert.Matches($"^Put( X)? {{{ColumnPad},}}= 3,94$", line);
    }

    /// <summary>A space drawn alone between two column gaps is a blank cell, not the end of a word: the
    /// next column still lands where it does after a drawn mark in that cell.</summary>
    [Fact]
    public void ASpaceStandingAloneBetweenTwoColumnGapsStillPadsToTheColumn()
    {
        var blank = Line("[(Put) -900 ( ) -1130 (NAME)] TJ");
        var marked = Line("[(Put) -900 (*) -1130 (NAME)] TJ");
        Assert.Equal(marked.IndexOf("NAME"), blank.IndexOf("NAME"));
    }

    private static string Line(string shows)
    {
        var pdf = RawPage.Build($"BT /F1 10 Tf 1 0 0 1 70 700 Tm {shows} ET\n" + Grid, Fonts);
        using var document = new Document(new MemoryStream(pdf));
        var absorber = new TextAbsorber();
        document.Pages.Accept(absorber);
        return absorber.Text.Split('\n').Select(l => l.TrimEnd('\r', ' ')).Single(l => l.StartsWith("Put"));
    }
}
