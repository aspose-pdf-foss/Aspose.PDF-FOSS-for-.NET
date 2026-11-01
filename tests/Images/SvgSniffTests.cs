using System.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Images;

/// <summary>Whether image bytes are taken for an SVG document is a byte-for-byte test of their head, the
/// same on every runtime and in every culture: a NUL is a character of the head like any other.</summary>
public class SvgSniffTests
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"/>";

    public static TheoryData<byte[], bool> Heads => new()
    {
        { Encoding.UTF8.GetBytes(Svg), true },
        { Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\n" + Svg), true },
        // UTF-16 without a byte-order mark: a NUL after every ASCII letter.
        { Encoding.Unicode.GetBytes(Svg), false },
        { Encoding.Unicode.GetBytes("<?xml version=\"1.0\"?>" + Svg), false },
        // A head padded with NULs before the root.
        { Encoding.ASCII.GetBytes("\0\0\0\0" + Svg), false },
    };

    [Theory]
    [MemberData(nameof(Heads))]
    public void AnImageCollectionTakesForSvgOnlyAHeadThatSpellsTheRoot(byte[] data, bool svg)
        => Assert.Equal(svg, XImageCollection.IsSvg(data));

    [Theory]
    [MemberData(nameof(Heads))]
    public void ATableCellPictureIsTakenForSvgOnlyByAHeadThatSpellsTheRoot(byte[] data, bool svg)
        => Assert.Equal(svg, Table.IsSvg(new Image(), data));
}
