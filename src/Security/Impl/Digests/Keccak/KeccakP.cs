namespace Aspose.Pdf.Security.Impl.Digests.Keccak;

/// <summary>
/// KECCAK-p[b, nr] of FIPS 202 section 3.3: nr rounds of iota(chi(pi(rho(theta(A))))),
/// the last nr of the 12 + 2l rounds KECCAK-f would run.
/// </summary>
internal sealed class KeccakP
{
    // Rotation offsets of the rho step (FIPS 202, Table 2), by lane 5y + x.
    private static readonly int[] RhoOffsets =
    {
         0,  1, 62, 28, 27,
        36, 44,  6, 55, 20,
         3, 10, 43, 25, 39,
        41, 45, 15, 21,  8,
        18,  2, 61, 56, 14,
    };

    // KECCAK-f runs 12 + 2l rounds; a KECCAK-p of nr rounds starts that many from its end.
    private const int FullRoundBase = 12;

    // The iota constant for round ir takes rc(j + 7*ir) into lane bit 2^j - 1.
    private const int RcRoundStride = 7;

    // Round constants for the only lane width SHA-3 uses; other widths compute on demand.
    private static readonly int LaneL = KeccakState.GetL(KeccakHelper.LongSize);
    private static readonly ulong[] LaneRoundConstants = BuildRoundConstants(LaneL);

    private readonly int _rounds;

    /// <summary>A permutation of <paramref name="nr"/> rounds.</summary>
    internal KeccakP(int nr) => _rounds = nr;

    /// <summary>
    /// KECCAK-p[<paramref name="b"/>, <paramref name="nr"/>] applied to
    /// <paramref name="s"/>, which must be the b bits the permutation is defined over.
    /// </summary>
    internal BitString Execute(int b, int nr, BitString s)
    {
        if (s.Length != b)
            throw new ArgumentException($"KECCAK-p[{b}] needs {b} bits, got {s.Length}.", nameof(s));
        var state = new KeccakState(s);
        Permute(state, nr);
        return state.ToBitString();
    }

    /// <summary>Run this permutation's rounds over <paramref name="state"/> in place.</summary>
    internal void Permute(KeccakState state) => Permute(state, _rounds);

    private static void Permute(KeccakState state, int nr)
    {
        var last = FullRoundBase + 2 * state.L;
        for (var ir = last - nr; ir < last; ir++)
            Round(state, ir);
    }

    private static void Round(KeccakState state, int ir)
    {
        var w = state.W;
        var mask = w == KeccakHelper.LongSize ? ulong.MaxValue : (1UL << w) - 1;
        Theta(state, w, mask);
        var rotated = RhoAndPi(state, w, mask);
        Chi(state, rotated, mask);
        Iota(state, ir);
    }

    // theta: each lane takes the parity of the two neighbouring columns, one of them
    // turned by a single bit along z.
    private static void Theta(KeccakState state, int w, ulong mask)
    {
        var c = new ulong[KeccakState.RowSize];
        for (var x = 0; x < KeccakState.RowSize; x++)
            for (var y = 0; y < KeccakState.ColSize; y++)
                c[x] ^= state[x, y];
        for (var x = 0; x < KeccakState.RowSize; x++)
        {
            var d = c[KeccakHelper.Mod(x - 1, KeccakState.RowSize)] ^
                    Rotate(c[KeccakHelper.Mod(x + 1, KeccakState.RowSize)], 1, w, mask);
            for (var y = 0; y < KeccakState.ColSize; y++)
                state[x, y] ^= d;
        }
    }

    // rho turns each lane by its own offset; pi then moves lane (x + 3y, x) into (x, y).
    private static ulong[] RhoAndPi(KeccakState state, int w, ulong mask)
    {
        var moved = new ulong[KeccakState.RowSize * KeccakState.ColSize];
        for (var y = 0; y < KeccakState.ColSize; y++)
            for (var x = 0; x < KeccakState.RowSize; x++)
            {
                var sx = KeccakHelper.Mod(x + 3 * y, KeccakState.RowSize);
                var lane = KeccakState.RowSize * x + sx;
                moved[KeccakState.RowSize * y + x] = Rotate(state[sx, x], RhoOffsets[lane], w, mask);
            }
        return moved;
    }

    // chi is the only non-linear step: a lane is corrected by the two lanes to its right.
    private static void Chi(KeccakState state, ulong[] lanes, ulong mask)
    {
        for (var y = 0; y < KeccakState.ColSize; y++)
            for (var x = 0; x < KeccakState.RowSize; x++)
            {
                var self = lanes[KeccakState.RowSize * y + x];
                var next = lanes[KeccakState.RowSize * y + KeccakHelper.Mod(x + 1, KeccakState.RowSize)];
                var after = lanes[KeccakState.RowSize * y + KeccakHelper.Mod(x + 2, KeccakState.RowSize)];
                state[x, y] = (self ^ (~next & after)) & mask;
            }
    }

    // iota breaks the round symmetry by folding one constant into the origin lane.
    private static void Iota(KeccakState state, int ir) =>
        state[0, 0] ^= state.L == LaneL ? LaneRoundConstants[ir] : RoundConstant(ir, state.L);

    private static ulong[] BuildRoundConstants(int l)
    {
        var constants = new ulong[FullRoundBase + 2 * l];
        for (var ir = 0; ir < constants.Length; ir++)
            constants[ir] = RoundConstant(ir, l);
        return constants;
    }

    private static ulong RoundConstant(int ir, int l)
    {
        var rc = 0UL;
        for (var j = 0; j <= l; j++)
            if (KeccakHelper.Rc(j + RcRoundStride * ir))
                rc |= 1UL << ((1 << j) - 1);
        return rc;
    }

    private static ulong Rotate(ulong lane, int offset, int w, ulong mask)
    {
        var by = KeccakHelper.Mod(offset, w);
        if (by == 0) return lane & mask;
        return (lane << by | (lane & mask) >> (w - by)) & mask;
    }
}
