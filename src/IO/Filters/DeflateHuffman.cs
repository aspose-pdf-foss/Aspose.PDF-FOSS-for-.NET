namespace Aspose.Pdf.IO.Filters;

/// <summary>Code lengths from symbol frequencies, and canonical codes from lengths (RFC 1951 §3.2.2).</summary>
internal static class DeflateHuffman
{
    /// <summary>
    /// Prefix-code lengths for the symbols with non-zero frequency, none longer than
    /// <paramref name="maxBits"/>. When the plain Huffman tree is too deep the frequencies
    /// are halved (never below one) and the tree rebuilt, which flattens it a step at a time
    /// and always ends within the limit. Symbol order breaks every tie, so the result is the
    /// same on every host.
    /// </summary>
    public static byte[] BuildLengths(int[] freq, int maxBits)
    {
        var used = 0;
        var single = -1;
        for (var i = 0; i < freq.Length; i++)
        {
            if (freq[i] == 0) continue;
            used++;
            single = i;
        }
        var lengths = new byte[freq.Length];
        if (used == 0) return lengths;
        if (used == 1)
        {
            lengths[single] = 1;
            return lengths;
        }
        var work = (int[])freq.Clone();
        while (true)
        {
            lengths = TreeLengths(work);
            var deepest = 0;
            foreach (var l in lengths) deepest = Math.Max(deepest, l);
            if (deepest <= maxBits) return lengths;
            for (var i = 0; i < work.Length; i++)
                if (work[i] > 0) work[i] = (work[i] + 1) / 2;
        }
    }

    /// <summary>Unbounded Huffman code lengths: repeatedly join the two lightest trees,
    /// the lower symbol (or the older node) first on equal weight.</summary>
    private static byte[] TreeLengths(int[] freq)
    {
        var symbols = freq.Length;
        var nodeCount = symbols * 2;
        var weight = new long[nodeCount];
        var parent = new int[nodeCount];
        var active = new List<int>();
        for (var i = 0; i < symbols; i++)
        {
            if (freq[i] == 0) continue;
            weight[i] = freq[i];
            active.Add(i);
        }
        var next = symbols;
        while (active.Count > 1)
        {
            var first = TakeLightest(active, weight);
            var second = TakeLightest(active, weight);
            weight[next] = weight[first] + weight[second];
            parent[first] = next;
            parent[second] = next;
            active.Add(next);
            next++;
        }
        var root = active[0];
        var depth = new byte[nodeCount];
        for (var node = next - 1; node >= symbols; node--)
            if (node != root) depth[node] = (byte)(depth[parent[node]] + 1);
        var lengths = new byte[symbols];
        for (var i = 0; i < symbols; i++)
            if (freq[i] != 0) lengths[i] = (byte)(depth[parent[i]] + 1);
        return lengths;
    }

    private static int TakeLightest(List<int> active, long[] weight)
    {
        var best = 0;
        for (var i = 1; i < active.Count; i++)
        {
            var candidate = active[i];
            var chosen = active[best];
            if (weight[candidate] < weight[chosen] || (weight[candidate] == weight[chosen] && candidate < chosen))
                best = i;
        }
        var result = active[best];
        active.RemoveAt(best);
        return result;
    }

    /// <summary>Canonical codes: within a length, codes ascend with the symbol.</summary>
    public static ushort[] AssignCodes(byte[] lengths)
    {
        var countPerLength = new int[DeflateTables.MaxCodeBits + 1];
        foreach (var l in lengths) countPerLength[l]++;
        countPerLength[0] = 0;
        var nextCode = new int[DeflateTables.MaxCodeBits + 2];
        var code = 0;
        for (var bits = 1; bits <= DeflateTables.MaxCodeBits; bits++)
        {
            code = (code + countPerLength[bits - 1]) << 1;
            nextCode[bits] = code;
        }
        var codes = new ushort[lengths.Length];
        for (var i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] == 0) continue;
            codes[i] = (ushort)nextCode[lengths[i]];
            nextCode[lengths[i]]++;
        }
        return codes;
    }
}
