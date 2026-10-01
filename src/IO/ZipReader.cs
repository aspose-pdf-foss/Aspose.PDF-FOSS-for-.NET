using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.IO;

/// <summary>
/// Reads a ZIP archive (PKWARE APPNOTE 4.4.x) from its central directory: the end record found
/// from the back (ZIP64 records followed when the sizes overflow), then each entry's name, sizes,
/// method and header offset. Entries are stored or deflated, and inflated by the library's own
/// inflater, so reading does not depend on the runtime either.
/// </summary>
internal sealed class ZipReader
{
    private const uint LocalHeaderSignature = 0x04034B50;
    private const uint CentralHeaderSignature = 0x02014B50;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint Zip64EndSignature = 0x06064B50;
    private const uint Zip64LocatorSignature = 0x07064B50;
    private const int EndRecordSize = 22;
    private const int MaxCommentLength = 0xFFFF;
    private const ushort MethodStored = 0;
    private const ushort MethodDeflate = 8;
    private const ushort FlagEncrypted = 0x0001;
    private const ushort FlagUtf8Names = 0x0800;
    private const ushort Zip64ExtraId = 0x0001;
    private const uint Overflow32 = 0xFFFFFFFF;

    private readonly byte[] _data;
    private readonly List<Entry> _entries = new();

    /// <summary>One entry as the central directory describes it.</summary>
    internal sealed class Entry
    {
        public string Name = "";
        public ushort Method;
        public ushort Flags;
        public uint Crc;
        public long CompressedSize;
        public long Size;
        public long LocalHeaderOffset;
    }

    /// <summary>Reads the archive's directory; fails with InvalidDataException for bytes that are not one.</summary>
    internal ZipReader(byte[] archive)
    {
        _data = archive;
        ReadDirectory();
    }

    internal IReadOnlyList<Entry> Entries => _entries;

    /// <summary>The entry of that name, or null.</summary>
    internal Entry? Find(string name)
    {
        foreach (var entry in _entries)
        {
            if (entry.Name == name) return entry;
        }
        return null;
    }

    /// <summary>An entry's content, stored or inflated.</summary>
    internal byte[] Extract(Entry entry)
    {
        if ((entry.Flags & FlagEncrypted) != 0) throw new InvalidDataException("zip: encrypted entries are not supported");
        var at = entry.LocalHeaderOffset;
        if (U32(at) != LocalHeaderSignature) throw new InvalidDataException("zip: local header not found");
        var start = at + 30 + U16(at + 26) + U16(at + 28);
        if (start + entry.CompressedSize > _data.Length) throw new InvalidDataException("zip: entry data cut short");
        var packed = new byte[entry.CompressedSize];
        Array.Copy(_data, start, packed, 0, entry.CompressedSize);
        switch (entry.Method)
        {
            case MethodStored:
                return packed;
            case MethodDeflate:
                var inflated = ManagedInflater.InflateAll(packed, false, out var complete);
                if (!complete) throw new InvalidDataException("zip: entry data damaged");
                return inflated;
            default:
                throw new InvalidDataException("zip: compression method " + entry.Method + " is not supported");
        }
    }

    private void ReadDirectory()
    {
        var end = FindEndRecord();
        long count = U16(end + 10);
        long directorySize = U32(end + 12);
        long directoryStart = U32(end + 16);
        if (count == 0xFFFF || directorySize == Overflow32 || directoryStart == Overflow32)
        {
            var locator = end - 20;
            if (locator >= 0 && U32(locator) == Zip64LocatorSignature)
            {
                var zip64End = (long)U64(locator + 8);
                if (U32(zip64End) != Zip64EndSignature) throw new InvalidDataException("zip: ZIP64 end record not found");
                count = (long)U64(zip64End + 32);
                directoryStart = (long)U64(zip64End + 48);
            }
        }
        var at = directoryStart;
        for (long i = 0; i < count; i++)
        {
            if (U32(at) != CentralHeaderSignature) throw new InvalidDataException("zip: central directory entry not found");
            var entry = new Entry
            {
                Flags = U16(at + 8),
                Method = U16(at + 10),
                Crc = U32(at + 16),
                CompressedSize = U32(at + 20),
                Size = U32(at + 24),
                LocalHeaderOffset = U32(at + 42),
            };
            int nameLength = U16(at + 28), extraLength = U16(at + 30), commentLength = U16(at + 32);
            var encoding = (entry.Flags & FlagUtf8Names) != 0 ? Encoding.UTF8 : Compat.Latin1;
            entry.Name = encoding.GetString(_data, (int)(at + 46), nameLength);
            ReadZip64Extra(entry, at + 46 + nameLength, extraLength);
            _entries.Add(entry);
            at += 46 + nameLength + extraLength + commentLength;
        }
    }

    // Sizes and offset that overflowed 32 bits, in the order the extra field keeps them.
    private void ReadZip64Extra(Entry entry, long at, int length)
    {
        var end = at + length;
        while (at + 4 <= end)
        {
            var id = U16(at);
            var size = U16(at + 2);
            if (id == Zip64ExtraId)
            {
                var field = at + 4;
                if (entry.Size == Overflow32) { entry.Size = (long)U64(field); field += 8; }
                if (entry.CompressedSize == Overflow32) { entry.CompressedSize = (long)U64(field); field += 8; }
                if (entry.LocalHeaderOffset == Overflow32) entry.LocalHeaderOffset = (long)U64(field);
                return;
            }
            at += 4 + size;
        }
    }

    // The end record: the last signature within the final 22 + 65,535 bytes.
    private long FindEndRecord()
    {
        var earliest = Math.Max(0, _data.Length - EndRecordSize - MaxCommentLength);
        for (long at = _data.Length - EndRecordSize; at >= earliest; at--)
        {
            if (U32(at) == EndOfCentralDirectorySignature) return at;
        }
        throw new InvalidDataException("zip: end of central directory record not found");
    }

    private ushort U16(long at)
    {
        if (at < 0 || at + 2 > _data.Length) throw new InvalidDataException("zip: archive cut short");
        return (ushort)(_data[at] | (_data[at + 1] << 8));
    }

    private uint U32(long at)
    {
        if (at < 0 || at + 4 > _data.Length) throw new InvalidDataException("zip: archive cut short");
        return (uint)(_data[at] | (_data[at + 1] << 8) | (_data[at + 2] << 16) | (_data[at + 3] << 24));
    }

    private ulong U64(long at) => U32(at) | ((ulong)U32(at + 4) << 32);
}
