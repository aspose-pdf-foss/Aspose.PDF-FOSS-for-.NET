using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// A PostScript string: a mutable byte buffer that <c>getinterval</c> can view a
/// slice of without copying, which is how <c>cvs</c> and the <c>readstring</c>
/// family are expected to behave.
/// </summary>
internal sealed class PsString
{
    private readonly byte[] _data;
    private readonly int _start;

    /// <summary>A fresh zero-filled string of the given length.</summary>
    public PsString(int length) : this(new byte[length], 0, length)
    {
    }

    /// <summary>A string over the given bytes.</summary>
    public PsString(byte[] data) : this(data, 0, data.Length)
    {
    }

    private PsString(byte[] data, int start, int length)
    {
        _data = data;
        _start = start;
        Length = length;
    }

    /// <summary>The number of bytes in this string.</summary>
    public int Length { get; }

    /// <summary>Read or write one byte.</summary>
    public byte this[int index]
    {
        get => _data[_start + index];
        set => _data[_start + index] = value;
    }

    /// <summary>A slice sharing this string's storage.</summary>
    public PsString Interval(int index, int count) =>
        new(_data, _start + index, count);

    /// <summary>Copy <paramref name="source"/> into this string at
    /// <paramref name="index"/>.</summary>
    public void Put(int index, PsString source)
    {
        for (var i = 0; i < source.Length; i++) this[index + i] = source[i];
    }

    /// <summary>The bytes of this string as a new array.</summary>
    public byte[] ToArray()
    {
        var copy = new byte[Length];
        Array.Copy(_data, _start, copy, 0, Length);
        return copy;
    }

    /// <summary>Build a string from Latin-1 text.</summary>
    public static PsString FromText(string text)
    {
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++) bytes[i] = (byte)text[i];
        return new PsString(bytes);
    }

    /// <summary>This string's bytes read as Latin-1 text.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder(Length);
        for (var i = 0; i < Length; i++) sb.Append((char)this[i]);
        return sb.ToString();
    }
}

/// <summary>
/// A PostScript array: a mutable slot list that <c>getinterval</c> can view a slice
/// of without copying. Procedures are arrays too — only the executable flag differs.
/// </summary>
internal sealed class PsArray
{
    private readonly List<PsValue> _slots;
    private readonly int _start;

    /// <summary>A fresh array of nulls.</summary>
    public PsArray(int length) : this(NullSlots(length), 0, length)
    {
    }

    /// <summary>An array over the given values.</summary>
    public PsArray(IEnumerable<PsValue> values) : this(new List<PsValue>(values), 0, -1)
    {
        Length = _slots.Count;
    }

    private PsArray(List<PsValue> slots, int start, int length)
    {
        _slots = slots;
        _start = start;
        Length = length;
    }

    private static List<PsValue> NullSlots(int length)
    {
        var list = new List<PsValue>(length);
        for (var i = 0; i < length; i++) list.Add(PsValue.Null);
        return list;
    }

    /// <summary>The number of slots in this array.</summary>
    public int Length { get; private set; }

    /// <summary>Read or write one slot.</summary>
    public PsValue this[int index]
    {
        get => _slots[_start + index];
        set => _slots[_start + index] = value;
    }

    /// <summary>A slice sharing this array's storage.</summary>
    public PsArray Interval(int index, int count) =>
        new(_slots, _start + index, count);

    /// <summary>This array's slots as a new list.</summary>
    public List<PsValue> ToList()
    {
        var copy = new List<PsValue>(Length);
        for (var i = 0; i < Length; i++) copy.Add(this[i]);
        return copy;
    }
}

/// <summary>
/// A PostScript dictionary. Keys are PostScript objects, though in practice almost
/// always names, so lookups stay on the fast path of a plain hash map.
/// </summary>
internal sealed class PsDictionary
{
    private readonly Dictionary<PsValue, PsValue> _entries;

    /// <summary>A dictionary with room for <paramref name="capacity"/> entries.
    /// PostScript's capacity is advisory in a level-2 interpreter, so it is recorded
    /// for <c>maxlength</c> and never enforced.</summary>
    public PsDictionary(int capacity = 0)
    {
        _entries = new Dictionary<PsValue, PsValue>(Math.Max(capacity, 1));
        Capacity = capacity;
    }

    /// <summary>The capacity this dictionary was created with.</summary>
    public int Capacity { get; private set; }

    /// <summary>The number of entries.</summary>
    public int Count => _entries.Count;

    /// <summary>Whether writes to this dictionary are rejected.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>The entries, in insertion order where the runtime preserves it.</summary>
    public IEnumerable<KeyValuePair<PsValue, PsValue>> Entries => _entries;

    /// <summary>Look a key up, returning false when it is absent.</summary>
    public bool TryGet(PsValue key, out PsValue value) => _entries.TryGetValue(key, out value);

    /// <summary>Look a name up, returning false when it is absent.</summary>
    public bool TryGet(string name, out PsValue value) =>
        _entries.TryGetValue(PsValue.LiteralName(name), out value);

    /// <summary>Store an entry, growing the recorded capacity when it overflows.</summary>
    public void Put(PsValue key, PsValue value)
    {
        _entries[Normalise(key)] = value;
        if (_entries.Count > Capacity) Capacity = _entries.Count;
    }

    /// <summary>Store an entry under a name.</summary>
    public void Put(string name, PsValue value) => Put(PsValue.LiteralName(name), value);

    /// <summary>Remove an entry.</summary>
    public void Remove(PsValue key) => _entries.Remove(Normalise(key));

    /// <summary>Replace every entry with the ones given, which is how a restore puts a
    /// dictionary back the way the matching save found it.</summary>
    public void Reset(IEnumerable<KeyValuePair<PsValue, PsValue>> entries)
    {
        _entries.Clear();
        foreach (var pair in entries) _entries[pair.Key] = pair.Value;
    }

    /// <summary>A key is stored in its literal form, so that a name looked up while
    /// executable finds the entry a literal name created.</summary>
    private static PsValue Normalise(PsValue key) =>
        key.Type == PsType.Name ? PsValue.LiteralName(key.Text ?? string.Empty) : key;
}

/// <summary>A built-in operator: a name and the action it performs.</summary>
internal sealed class PsOperator
{
    /// <summary>Create an operator.</summary>
    public PsOperator(string name, Action<PsInterpreter> action)
    {
        Name = name;
        Action = action;
    }

    /// <summary>The name this operator is bound to.</summary>
    public string Name { get; }

    /// <summary>What the operator does.</summary>
    public Action<PsInterpreter> Action { get; }
}

/// <summary>
/// A PostScript file object. Only the input side is meaningful here: programs read
/// image data and embedded font programs out of <c>currentfile</c>, and write to
/// streams the converter discards.
/// </summary>
internal sealed class PsFile
{
    private byte[] _data;
    private PsFile? _encoded;
    private string? _filter;

    /// <summary>A readable file over the given bytes.</summary>
    public PsFile(byte[] data, int position = 0)
    {
        _data = data ?? new byte[0];
        Position = position;
        IsReadable = true;
    }

    /// <summary>A readable file over what a filter decodes out of another file, taken
    /// when something first reads from it rather than when the filter was made: the
    /// data a program filters out of its own source begins after the operator that
    /// consumes it, not at the point where the filter was built.</summary>
    public PsFile(PsFile source, string filter)
    {
        _data = new byte[0];
        _encoded = source;
        _filter = filter;
        IsReadable = true;
    }

    /// <summary>Decode what the source holds, and leave the source just past the run
    /// the filter consumed so the program carries on after its own data.</summary>
    private void Materialise()
    {
        if (_encoded is null || _filter is null) return;
        var source = _encoded;
        var filter = _filter;
        _encoded = null;
        _filter = null;
        var available = source.Read(source.Available);
        var used = PsFilters.EncodedLength(filter, available);
        source.Position -= available.Length - used;
        var run = new byte[used];
        Array.Copy(available, run, used);
        _data = PsFilters.Decode(filter, run);
    }

    /// <summary>A writable file that discards what it is given.</summary>
    public PsFile(Stream? sink)
    {
        _data = new byte[0];
        Sink = sink;
    }

    /// <summary>Where the next read starts.</summary>
    public int Position { get; set; }

    /// <summary>Whether this file yields bytes.</summary>
    public bool IsReadable { get; }

    /// <summary>Whether the file has been closed.</summary>
    public bool Closed { get; set; }

    /// <summary>The sink a writable file discards into.</summary>
    public Stream? Sink { get; }

    /// <summary>Bytes left to read.</summary>
    public int Available
    {
        get
        {
            Materialise();
            return Math.Max(0, _data.Length - Position);
        }
    }

    /// <summary>Read one byte, or -1 at end of file.</summary>
    public int ReadByte()
    {
        Materialise();
        return Position < _data.Length ? _data[Position++] : -1;
    }

    /// <summary>Read up to <paramref name="count"/> bytes.</summary>
    public byte[] Read(int count)
    {
        var n = Math.Min(count, Available);
        var buffer = new byte[n];
        Array.Copy(_data, Position, buffer, 0, n);
        Position += n;
        return buffer;
    }
}
