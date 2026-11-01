namespace Aspose.Pdf.IO;

/// <summary>
/// Every "N G obj" header of a file buffer, by object and generation number, in file order.
/// Built in one pass, so the header searches a damaged file makes for each object it cannot
/// find through its xref cost a lookup instead of a scan of the whole file apiece.
/// A header here starts a digit run (no digit before it), spells both numbers without leading
/// zeros and separates the parts by single spaces - the exact bytes of "{N} {G} obj".
/// </summary>
internal sealed class ObjectHeaderIndex
{
    // int.MaxValue has ten digits; a longer run is no object number.
    private const int MaxNumberDigits = 10;

    private static readonly IReadOnlyList<long> None = Array.Empty<long>();
    private readonly Dictionary<(int ObjNum, int Gen), List<long>> _offsets = new();

    internal ObjectHeaderIndex(byte[] data)
    {
        for (var pos = 0; pos < data.Length; pos++)
        {
            if (!IsDigit(data[pos]) || (pos > 0 && IsDigit(data[pos - 1]))) continue;
            if (!TryReadHeader(data, pos, out var objNum, out var gen)) continue;
            if (!_offsets.TryGetValue((objNum, gen), out var list))
                _offsets[(objNum, gen)] = list = new List<long>();
            list.Add(pos);
        }
    }

    /// <summary>The offsets of "objNum gen obj" headers, ascending; empty when there are none.</summary>
    internal IReadOnlyList<long> Find(int objNum, int gen) =>
        _offsets.TryGetValue((objNum, gen), out var list) ? list : None;

    private static bool TryReadHeader(byte[] data, int pos, out int objNum, out int gen)
    {
        gen = 0;
        if (!TryReadNumber(data, ref pos, out objNum) || !Expect(data, ref pos, (byte)' ')) return false;
        if (!TryReadNumber(data, ref pos, out gen) || !Expect(data, ref pos, (byte)' ')) return false;
        return Expect(data, ref pos, (byte)'o') && Expect(data, ref pos, (byte)'b') && Expect(data, ref pos, (byte)'j');
    }

    // A canonical decimal: "0" alone, or digits with no leading zero, within int range.
    private static bool TryReadNumber(byte[] data, ref int pos, out int value)
    {
        value = 0;
        var start = pos;
        long acc = 0;
        while (pos < data.Length && IsDigit(data[pos]) && pos - start < MaxNumberDigits)
            acc = acc * 10 + (data[pos++] - '0');
        var length = pos - start;
        if (length == 0 || (length > 1 && data[start] == '0') || acc > int.MaxValue) return false;
        if (pos < data.Length && IsDigit(data[pos])) return false;
        value = (int)acc;
        return true;
    }

    private static bool Expect(byte[] data, ref int pos, byte b)
    {
        if (pos >= data.Length || data[pos] != b) return false;
        pos++;
        return true;
    }

    private static bool IsDigit(byte b) => b >= '0' && b <= '9';
}
