namespace Aspose.Pdf.Security.Impl.Digests.Keccak;

/// <summary>
/// The sponge construction of FIPS 202 section 4 over KECCAK-p: rate-bit blocks of the
/// padded message are folded into the state one permutation at a time, then the digest
/// is squeezed back out of it. The domain suffix that separates SHA-3 from a raw Keccak
/// is the caller's to append - the sponge itself only ever adds pad10*1.
/// </summary>
internal class Sponge
{
    private readonly int _digestSize;
    private readonly int _rate;
    private readonly KeccakState _state;
    private readonly KeccakP _permutation;

    // Bits already folded into the block being filled; a full block triggers a permutation.
    private int _blockBits;

    /// <summary>A sponge of the width SHA-3 permutes, holding back <paramref name="capacity"/> bits.</summary>
    internal Sponge(int digestSize, int capacity, int nr)
        : this(digestSize, KeccakHelper.WidthOfPermutation, capacity, nr) { }

    private Sponge(int digestSize, int b, int capacity, int nr)
    {
        _digestSize = digestSize;
        _rate = b - capacity;
        _state = new KeccakState(b);
        _permutation = new KeccakP(nr);
    }

    /// <summary>Absorb <paramref name="length"/> bytes of <paramref name="input"/>.</summary>
    internal void AddBlock(byte[] input, int index, int length)
    {
        for (var i = 0; i < length; i++)
            AddBits(input[index + i], KeccakHelper.ByteSize);
    }

    /// <summary>Absorb the <paramref name="count"/> low bits of <paramref name="bits"/>.</summary>
    internal void AddBits(int bits, int count)
    {
        for (var i = 0; i < count; i++)
            AddBit((bits >> i & 1) == 1);
    }

    /// <summary>Close the message with pad10*1 and squeeze the digest out.</summary>
    internal byte[] DoFinal()
    {
        Pad();
        return Squeeze().ToBytes();
    }

    /// <summary>
    /// The whole sponge in one call: KECCAK[capacity] over an already domain-separated
    /// <paramref name="message"/>, truncated to <paramref name="digestSize"/> bits.
    /// </summary>
    internal static BitString Execute(BitString message, int digestSize, int b, int capacity, int nr)
    {
        var sponge = new Sponge(digestSize, b, capacity, nr);
        for (var i = 0; i < message.Length; i++)
            sponge.AddBit(message[i]);
        sponge.Pad();
        return sponge.Squeeze();
    }

    private void AddBit(bool bit)
    {
        _state.XorBit(_blockBits, bit);
        _blockBits++;
        if (_blockBits < _rate) return;
        _permutation.Permute(_state);
        _blockBits = 0;
    }

    private void Pad()
    {
        var padSize = KeccakHelper.GetPadSize(_blockBits, _rate);
        AddBit(true);
        for (var i = 2; i < padSize; i++) AddBit(false);
        AddBit(true);
    }

    private BitString Squeeze()
    {
        var digest = new BitString(_digestSize);
        for (var produced = 0; produced < _digestSize;)
        {
            var take = Math.Min(_rate, _digestSize - produced);
            for (var i = 0; i < take; i++)
                digest[produced + i] = _state.GetBit(i);
            produced += take;
            if (produced < _digestSize) _permutation.Permute(_state);
        }
        return digest;
    }
}
