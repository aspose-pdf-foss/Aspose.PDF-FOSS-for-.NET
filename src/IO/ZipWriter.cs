using System.Text;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.IO;

/// <summary>
/// Writes a ZIP archive (PKWARE APPNOTE 4.4.x): local headers, deflated entries, a central
/// directory and the end record. Entries are compressed by the library's own deflater, so
/// the archive's bytes do not depend on the runtime the library happens to be running on.
/// </summary>
internal sealed class ZipWriter : IDisposable
{
    private const uint LocalHeaderSignature = 0x04034B50;
    private const uint CentralHeaderSignature = 0x02014B50;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const ushort VersionNeeded = 20;          // 2.0: deflate
    private const ushort VersionMadeBy = 20;
    private const ushort FlagUtf8Names = 0x0800;
    private const ushort MethodDeflate = 8;
    private const int DosEpochYear = 1980;

    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private readonly List<EntryRecord> _entries = new();
    private readonly ushort _dosTime;
    private readonly ushort _dosDate;
    private long _position;
    private bool _closed;

    private sealed record EntryRecord(byte[] Name, uint Crc, int CompressedSize, int Size, long HeaderOffset);

    public ZipWriter(Stream output, bool leaveOpen = false) : this(output, DateTime.Now, leaveOpen) { }

    public ZipWriter(Stream output, DateTime timestamp, bool leaveOpen = false)
    {
        _output = output;
        _leaveOpen = leaveOpen;
        (_dosTime, _dosDate) = ToDosDateTime(timestamp);
    }

    /// <summary>Adds one file, deflated.</summary>
    public void AddEntry(string name, byte[] content)
    {
        if (_closed) throw new InvalidOperationException("The archive has been closed.");
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var compressed = ManagedDeflater.DeflateRaw(content, 0, content.Length);
        var crc = Crc32.Compute(content);
        var record = new EntryRecord(nameBytes, crc, compressed.Length, content.Length, _position);
        _entries.Add(record);
        WriteUInt32(LocalHeaderSignature);
        WriteUInt16(VersionNeeded);
        WriteUInt16(FlagUtf8Names);
        WriteUInt16(MethodDeflate);
        WriteUInt16(_dosTime);
        WriteUInt16(_dosDate);
        WriteUInt32(crc);
        WriteUInt32((uint)compressed.Length);
        WriteUInt32((uint)content.Length);
        WriteUInt16((ushort)nameBytes.Length);
        WriteUInt16(0);   // extra field length
        Write(nameBytes);
        Write(compressed);
    }

    /// <summary>Writes the central directory and the end record; idempotent.</summary>
    public void Close()
    {
        if (_closed) return;
        _closed = true;
        var directoryStart = _position;
        foreach (var entry in _entries)
        {
            WriteUInt32(CentralHeaderSignature);
            WriteUInt16(VersionMadeBy);
            WriteUInt16(VersionNeeded);
            WriteUInt16(FlagUtf8Names);
            WriteUInt16(MethodDeflate);
            WriteUInt16(_dosTime);
            WriteUInt16(_dosDate);
            WriteUInt32(entry.Crc);
            WriteUInt32((uint)entry.CompressedSize);
            WriteUInt32((uint)entry.Size);
            WriteUInt16((ushort)entry.Name.Length);
            WriteUInt16(0);   // extra field length
            WriteUInt16(0);   // comment length
            WriteUInt16(0);   // disk number start
            WriteUInt16(0);   // internal attributes
            WriteUInt32(0);   // external attributes
            WriteUInt32((uint)entry.HeaderOffset);
            Write(entry.Name);
        }
        var directorySize = _position - directoryStart;
        WriteUInt32(EndOfCentralDirectorySignature);
        WriteUInt16(0);   // this disk
        WriteUInt16(0);   // directory disk
        WriteUInt16((ushort)_entries.Count);
        WriteUInt16((ushort)_entries.Count);
        WriteUInt32((uint)directorySize);
        WriteUInt32((uint)directoryStart);
        WriteUInt16(0);   // comment length
        _output.Flush();
    }

    public void Dispose()
    {
        Close();
        if (!_leaveOpen) _output.Dispose();
    }

    private static (ushort Time, ushort Date) ToDosDateTime(DateTime value)
    {
        if (value.Year < DosEpochYear) value = new DateTime(DosEpochYear, 1, 1);
        var time = (ushort)((value.Hour << 11) | (value.Minute << 5) | (value.Second / 2));
        var date = (ushort)(((value.Year - DosEpochYear) << 9) | (value.Month << 5) | value.Day);
        return (time, date);
    }

    private void Write(byte[] bytes)
    {
        _output.Write(bytes, 0, bytes.Length);
        _position += bytes.Length;
    }

    private void WriteUInt16(ushort value) => Write(new[] { (byte)value, (byte)(value >> 8) });

    private void WriteUInt32(uint value)
        => Write(new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) });
}
