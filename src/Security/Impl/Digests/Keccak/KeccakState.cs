namespace Aspose.Pdf.Security.Impl.Digests.Keccak;

/// <summary>
/// The Keccak state array A of FIPS 202 section 3.1.2: a 5x5 grid of w-bit lanes laid
/// over a b-bit string S, with A[x, y, z] = S[w * (5y + x) + z].
/// </summary>
internal sealed class KeccakState
{
    /// <summary>Lanes along x.</summary>
    internal const int RowSize = 5;

    /// <summary>Lanes along y.</summary>
    internal const int ColSize = 5;

    /// <summary>Lane width w, in bits.</summary>
    internal readonly int W;

    /// <summary>l = log2(w), the exponent the round count and the iota constants are cut to.</summary>
    internal readonly int L;

    /// <summary>State width b = 25w, in bits.</summary>
    internal readonly int B;

    /// <summary>Lanes along x, named as the standard names the dimension.</summary>
    internal readonly int StateMatrixXDimensionSize;

    /// <summary>Lanes along y.</summary>
    internal readonly int StateMatrixYDimensionSize;

    /// <summary>Bits along z, which is the lane width.</summary>
    internal readonly int StateMatrixZDimensionSize;

    // Lane (x, y) lives at _lanes[RowSize * y + x] - the same 5y + x order the standard
    // slices S by - and bit z of that lane sits at value bit z.
    private readonly ulong[] _lanes = new ulong[RowSize * ColSize];

    /// <summary>Read a b-bit string into the state.</summary>
    internal KeccakState(BitString s) : this(s.Length)
    {
        for (var i = 0; i < B; i++)
            SetBit(i, s[i]);
    }

    /// <summary>A zeroed state of the width SHA-3 permutes.</summary>
    internal KeccakState() : this(KeccakHelper.WidthOfPermutation) { }

    /// <summary>A zeroed state of width <paramref name="b"/>.</summary>
    internal KeccakState(int b)
    {
        B = b;
        W = GetW(b);
        L = GetL(W);
        StateMatrixXDimensionSize = RowSize;
        StateMatrixYDimensionSize = ColSize;
        StateMatrixZDimensionSize = W;
    }

    /// <summary>l = log2(<paramref name="w"/>).</summary>
    internal static int GetL(int w)
    {
        var l = 0;
        while (1 << l < w) l++;
        return l;
    }

    /// <summary>w = b / 25.</summary>
    internal static int GetW(int b) => b / (RowSize * ColSize);

    /// <summary>Zero every lane.</summary>
    internal void Clear() => Array.Clear(_lanes, 0, _lanes.Length);

    /// <summary>Write the state back out as the b-bit string it was read from.</summary>
    internal BitString ToBitString()
    {
        var s = new BitString(B);
        for (var i = 0; i < B; i++)
            s[i] = GetBit(i);
        return s;
    }

    /// <summary>Read bit <paramref name="index"/> of the underlying b-bit string.</summary>
    internal bool GetBit(int index) => (_lanes[index / W] >> (index % W) & 1UL) == 1UL;

    /// <summary>Write bit <paramref name="index"/> of the underlying b-bit string.</summary>
    internal void SetBit(int index, bool value)
    {
        var mask = 1UL << (index % W);
        if (value) _lanes[index / W] |= mask;
        else _lanes[index / W] &= ~mask;
    }

    /// <summary>Fold one absorbed bit into bit <paramref name="index"/> of the state.</summary>
    internal void XorBit(int index, bool value)
    {
        if (value) _lanes[index / W] ^= 1UL << (index % W);
    }

    /// <summary>A[x, y, z].</summary>
    internal bool this[int x, int y, int z]
    {
        get => (_lanes[RowSize * y + x] >> z & 1UL) == 1UL;
        set
        {
            var mask = 1UL << z;
            if (value) _lanes[RowSize * y + x] |= mask;
            else _lanes[RowSize * y + x] &= ~mask;
        }
    }

    /// <summary>The whole lane A[x, y], bit z at value bit z.</summary>
    internal ulong this[int x, int y]
    {
        get => _lanes[RowSize * y + x];
        set => _lanes[RowSize * y + x] = value;
    }
}
