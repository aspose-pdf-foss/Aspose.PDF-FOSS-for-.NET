using Aspose.Pdf.Security;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

/// <summary>The ASN.1 writer encodes an object identifier's arcs as the numbers they are whatever the culture of the
/// machine: an arc written with a '+' is the arc an en-US machine read it as.</summary>
public class Asn1WriterCultureTests
{
    private static byte[] EncodedOid(string oid, string culture)
    {
        using var scope = new CultureScope(culture);
        var writer = new Asn1Writer();
        writer.WriteOid(oid);
        return writer.ToArray();
    }

    [Theory]
    [InlineData("ar-SA")] // a letter mark before the sign, which a culture parse will not take for '+'
    [InlineData("he-IL")] // a left-to-right mark before the sign
    [InlineData(CultureScope.SyntheticName)] // both, on every runtime
    public void AnArcWrittenWithAPlusSignIsEncodedAsUnderTheInvariantCulture(string culture)
    {
        var expected = EncodedOid("1.2.840.113549", string.Empty);
        Assert.Equal(expected, EncodedOid("1.2.+840.+113549", string.Empty)); // an arc's '+' is no part of its value

        Assert.Equal(expected, EncodedOid("1.2.+840.+113549", culture));
    }
}
