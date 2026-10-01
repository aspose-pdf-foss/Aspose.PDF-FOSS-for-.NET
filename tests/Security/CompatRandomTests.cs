using System.Linq;
using Xunit;

namespace Aspose.Pdf.Tests.Security;

/// <summary>
/// The random bytes the encryptor builds file ids, salts and keys from.
///
/// A platform without System.Security.Cryptography — the WebAssembly build has none — falls back to GUIDs, and
/// these check that the fallback hands out random bytes only: a version-4 GUID carries six bits of version and
/// variant in known places, and passing those on would put constants inside a key.
/// </summary>
public sealed class CompatRandomTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(100)]
    public void RandomBytes_HasTheLengthAsked(int count)
    {
        Assert.Equal(count, Compat.RandomBytes(count).Length);
        Assert.Equal(count, Compat.GuidRandomBytes(count).Length);
    }

    [Fact]
    public void RandomBytes_DifferBetweenCalls()
    {
        Assert.NotEqual(Compat.RandomBytes(32), Compat.RandomBytes(32));
        Assert.NotEqual(Compat.GuidRandomBytes(32), Compat.GuidRandomBytes(32));
    }

    /// <summary>
    /// No position may be a constant. The version nibble and the variant bits of a GUID are fixed, so a fallback
    /// that passed its bytes through unfiltered would show a position that never changes.
    /// </summary>
    [Fact]
    public void GuidRandomBytes_HasNoConstantPosition()
    {
        const int length = 48;
        const int draws = 200;
        var samples = Enumerable.Range(0, draws).Select(_ => Compat.GuidRandomBytes(length)).ToArray();

        for (var position = 0; position < length; position++)
        {
            var seen = samples.Select(sample => sample[position]).Distinct().Count();
            Assert.True(seen > draws / 10, $"byte {position} took only {seen} values over {draws} draws");
        }
    }

    /// <summary>The high nibble is where a GUID's version sits; over many draws every value must appear.</summary>
    [Fact]
    public void GuidRandomBytes_FillsTheHighNibble()
    {
        var nibbles = new bool[16];
        foreach (var sample in Enumerable.Range(0, 200).Select(_ => Compat.GuidRandomBytes(48)))
        {
            foreach (var value in sample)
            {
                nibbles[value >> 4] = true;
            }
        }
        Assert.DoesNotContain(false, nibbles);
    }

    [Fact]
    public void RandomInt32_StaysInRange()
    {
        for (var i = 0; i < 500; i++)
        {
            var value = Compat.RandomInt32(10, 20);
            Assert.InRange(value, 10, 19);
        }
    }
}
