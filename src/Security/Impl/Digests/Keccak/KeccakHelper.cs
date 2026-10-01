namespace Aspose.Pdf.Security.Impl.Digests.Keccak;

/// <summary>
/// Constants and bit-level helpers shared by the Keccak permutation and the sponge,
/// following FIPS 202 (SHA-3 Standard: Permutation-Based Hash and Extendable-Output
/// Functions). Bit positions are the standard's own positions - index 0 is the FIRST
/// bit of the string - which is how <see cref="BitString"/> indexes as well.
/// </summary>
internal static class KeccakHelper
{
    /// <summary>Bits in a byte.</summary>
    internal const int ByteSize = 8;

    /// <summary>Bits in the widest Keccak lane, which is also a machine word.</summary>
    internal const int LongSize = ByteSize * 8;

    /// <summary>The permutation width b that SHA-3 uses: KECCAK-p[1600, 24].</summary>
    internal const int WidthOfPermutation = 1600;

    /// <summary>That permutation's round count nr = 12 + 2l, with lane width 64 giving l = 6.</summary>
    internal const int RoundsNumber = 24;

    /// <summary>Digest size of SHA3-256, in bits.</summary>
    internal const int Sha3_256DigestSize = 256;

    /// <summary>Digest size of SHA3-384, in bits.</summary>
    internal const int Sha3_384DigestSize = 384;

    /// <summary>Digest size of SHA3-512, in bits.</summary>
    internal const int Sha3_512DigestSize = 512;

    // pad10*1 always contributes its leading and its trailing 1 on top of the zero run,
    // so the shortest padding it can produce is two bits.
    private const int PadDelimiterBits = 2;

    // rc() is an eight-bit LFSR whose feedback lands on positions 0, 4, 5 and 6
    // (FIPS 202, Algorithm 5); its output repeats with period 255.
    private const int RcRegisterBits = 8;
    private const int RcPeriod = 255;

    /// <summary>Bytes needed to carry <paramref name="bitCount"/> bits.</summary>
    internal static int GetByteCount(int bitCount) => (bitCount + ByteSize - 1) / ByteSize;

    /// <summary>Lane words needed to carry <paramref name="bitCount"/> bits.</summary>
    internal static int GetLongCount(int bitCount) => (bitCount + LongSize - 1) / LongSize;

    /// <summary>
    /// Floored modulo - the sense FIPS 202 gives "mod" for lane coordinates, where the
    /// result must land in [0, <paramref name="modulus"/>). Spelled as a plain static
    /// rather than an extension so that a Keccak call site is never ambiguous with
    /// <see cref="MathExtensions.Mod"/>.
    /// </summary>
    internal static int Mod(int value, int modulus) => MathExtensions.Mod(value, modulus);

    /// <summary>
    /// FIPS 202 Algorithm 5 - rc(t), the t-th output bit of the round-constant register.
    /// </summary>
    internal static bool Rc(int t)
    {
        var steps = Mod(t, RcPeriod);
        if (steps == 0) return true;
        // R starts as 10000000 with R[0] first. One slot past the register holds the bit
        // that "0 || R" pushes out, which is the feedback the step then folds back in.
        var r = new bool[RcRegisterBits + 1];
        r[0] = true;
        for (var i = 0; i < steps; i++)
        {
            for (var p = RcRegisterBits; p > 0; p--) r[p] = r[p - 1];
            r[0] = false;
            var feedback = r[RcRegisterBits];
            r[0] ^= feedback;
            r[4] ^= feedback;
            r[5] ^= feedback;
            r[6] ^= feedback;
        }
        return r[0];
    }

    /// <summary>
    /// FIPS 202 section 5.1 - how many pad10*1 bits carry a message of
    /// <paramref name="messageLength"/> bits up to a whole multiple of the sponge rate.
    /// </summary>
    internal static int GetPadSize(int messageLength, int rate) =>
        Mod(-messageLength - PadDelimiterBits, rate) + PadDelimiterBits;

    /// <summary>
    /// FIPS 202 section 5.1 - append pad10*1 to <paramref name="message"/> so the result
    /// is a whole number of <paramref name="rate"/>-bit blocks.
    /// </summary>
    internal static BitString PadMessage(BitString message, int rate)
    {
        // BitString counts from the end, so the padding room is prepended as zero bits
        // and then occupies the positions just past the message.
        var padded = new BitString(message, GetPadSize(message.Length, rate));
        padded[message.Length] = true;
        padded[padded.Length - 1] = true;
        return padded;
    }
}
