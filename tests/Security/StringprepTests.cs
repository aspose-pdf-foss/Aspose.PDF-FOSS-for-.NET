using System.Text;
using Aspose.Pdf.Engine.Security.Impl.Sasl;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

/// <summary>
/// SASLprep (RFC 4013) profile tests for the <c>Stringprep</c> implementation.
/// Even-indexed cases feed the input through a UTF-8 stream
/// (Stringprep(Stream)); odd-indexed cases use Stringprep(string), so the two
/// constructors are exercised in strict alternation.
///
/// Inputs/outputs are built from explicit code points (encoding-proof). A null
/// expected value means the input must be rejected. Several vectors that
/// nominally target a "multibyte U+NNNN" character are deliberately kept as
/// per-char escapes, so those cases are really Latin-1/C1 code points whose
/// C1 controls are themselves prohibited — the reject outcome is identical.
/// </summary>
public class StringprepTests
{
    private sealed record Vector(string Name, int[] Input, int[]? Expected);

    private static readonly Vector[] Vectors =
    {
        new("Map to nothing",           new[] { 0x49, 0x00AD, 0x58 },        new[] { 0x49, 0x58 }), // I­X → IX
        new("No transformation",        new[] { 0x75, 0x73, 0x65, 0x72 },    new[] { 0x75, 0x73, 0x65, 0x72 }),
        new("Case preserved",           new[] { 0x55, 0x53, 0x45, 0x52 },    new[] { 0x55, 0x53, 0x45, 0x52 }),
        new("ASCII space U+0020",       new[] { 0x20 },                      new[] { 0x20 }),
        new("NFKC ª→a",                 new[] { 0x00AA },                    new[] { 0x61 }),
        new("NFKC Ⅸ→IX",                new[] { 0x2168 },                    new[] { 0x49, 0x58 }),
        new("C1 controls (U+009A…)",    new[] { 0x00E1, 0x009A, 0x0080 },    null),
        new("C1 control U+0085",        new[] { 0x00C2, 0x0085 },            null),
        new("C1 control U+008E",        new[] { 0x00E1, 0x00A0, 0x008E },    null),
        new("ASCII control U+0007",     new[] { 0x0007 },                    null),
        new("Bidi RandAL+digit",        new[] { 0x0627, 0x0031 },            null),
        new("Musical control U+1D175",  new[] { 0x1D175 },                   null),
        new("C1 control U+0084",        new[] { 0x00EF, 0x0084, 0x00A3 },    null),
        new("Private use plane 15",     new[] { 0xF1234 },                   null),
        new("Private use plane 16",     new[] { 0x10F234 },                  null),
        new("C1 control U+0082",        new[] { 0x00ED, 0x00BD, 0x0082 },    null),
        new("C1 control U+008E (2)",    new[] { 0x00E2, 0x0080, 0x008E },    null),
        new("C1 control U+0080",        new[] { 0x00E2, 0x0080, 0x00AA },    null),
        new("Tagging char U+E0042",     new[] { 0x61, 0x62, 0x63, 0xE0042 }, null),
    };

    [Fact]
    public void SaslPrep_MatchesReferenceVectors()
    {
        for (var i = 0; i < Vectors.Length; i++)
        {
            var v = Vectors[i];
            var input = FromCodePoints(v.Input);

            // Even index → stream ctor (UTF-8), odd index → string ctor.
            var stringprep = (i % 2 == 0)
                ? new Stringprep(ToUtf8Stream(input))
                : new Stringprep(input);

            if (v.Expected is null)
            {
                Assert.Throws<StringprepException>(() => stringprep.Process());
            }
            else
            {
                stringprep.Process();
                Assert.Equal(FromCodePoints(v.Expected), stringprep.Result);
            }
        }
    }

    [Fact]
    public void Aes256_UnicodePassword_DecryptsWithSaslPreppedEquivalent()
    {
        // "Ⅸ" (U+2168 ROMAN NUMERAL NINE) SASLprep-normalizes (NFKC) to "IX", so
        // AES-256 key derivation must treat the two forms as the same password.
        var pdf = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(pdf);
        doc.Encrypt("Ⅸ", "owner", algorithm: CryptoAlgorithm.AESx256);
        var saved = doc.ToArray();

        // Opens with the NFKC-normalized form...
        using var byNormalized = Document.Open(saved, "IX");
        Assert.True(byNormalized.IsEncrypted);
        Assert.Equal(1, byNormalized.PageCount);

        // ...and with the original Unicode form.
        using var byOriginal = Document.Open(saved, "Ⅸ");
        Assert.Equal(1, byOriginal.PageCount);
    }

    private static string FromCodePoints(int[] cps)
    {
        var sb = new StringBuilder(cps.Length);
        foreach (var cp in cps) sb.Append(char.ConvertFromUtf32(cp));
        return sb.ToString();
    }

    private static Stream ToUtf8Stream(string s)
    {
        var ms = new MemoryStream();
        using (var sw = new StreamWriter(ms, new UTF8Encoding(false), 1024, leaveOpen: true))
        {
            sw.Write(s);
            sw.Flush();
        }
        ms.Position = 0;
        return ms;
    }
}
