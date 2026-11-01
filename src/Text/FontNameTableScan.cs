namespace Aspose.Pdf.Text;

/// <summary>Face-name lookup over font FILES: reads each file's embedded `name` table
/// and matches the query against the full name and the family — the resolution filename
/// matching can't provide (comic.ttf carries "Comic Sans MS"; a query "ComicSansMS"
/// fuzzy-matches it with spaces/hyphens stripped) - and every other name the face carries,
/// in any language (<see cref="FontFaceNames"/>). Shared by <see cref="FolderFontSource"/>
/// and <see cref="SystemFontSource"/>. The names read per file are cached process-wide by
/// path so repeated misses don't re-read every font (a .ttc can be &gt;10 MB).
/// TrueType Collections carry one name PER FACE (SourceHanSerif-Regular.ttc =
/// "Source Han Serif TC" / "... SC" / …), so every face is scanned and a matching face is
/// rebuilt to a standalone sfnt so the name read and a later FontFile2 embed both see a
/// valid table directory.</summary>
internal static class FontNameTableScan
{
    // Process-wide font-file → the names of each face, read from its name table.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, FontFaceNames[]> _namesByPath = new();

    // Process-wide (file, face) → normalized sfnt bytes. Besides saving the re-read,
    // repeat lookups of the same face return the SAME array instance — save-time font
    // consolidation dedups embedded programs by reference, so two fragments assigned
    // FindFont("Times New Roman") twice must share one program, not embed two copies.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string path, int face), byte[]?> _sfntByFace = new();

    internal static FontData? FindByFaceName(IEnumerable<string> files, string name,
        StringComparison comparison, string normalizedName)
    {
        // A family-name query matches EVERY face of that family, so returning the first
        // one found lets the directory order pick the style: "CourierNew" resolved to
        // Courier New Italic wherever the file system handed back couri.ttf first.
        FontData? styledFallback = null;
        foreach (var file in files)
        {
            var faceNames = _namesByPath.GetOrAdd(file, static f =>
            {
                try { return ReadFaceNames(f); }
                catch { return new[] { FontFaceNames.Unknown }; }
            });
            for (int face = 0; face < faceNames.Length; face++)
            {
                var names = faceNames[face];
                var match = names.Match(name, comparison, normalizedName);
                if (match == FaceNameMatch.None) continue;
                var data = _sfntByFace.GetOrAdd((file, face), static key =>
                {
                    try { return CjkFallbackFont.NormalizeToSfnt(System.IO.File.ReadAllBytes(key.path), key.face); }
                    catch { return null; }
                });
                if (data is null) continue;
                var fd = new FontData(names.Full, FontType.TrueType, file);
                fd.SetTtfData(data);
                // The regular face carries no style in its full name, so it equals the
                // family. Take it, or a hit on a name of the face itself, at once; hold
                // anything styled back for families that ship no regular face at all.
                if (match == FaceNameMatch.Face || names.IsRegular)
                    return fd;
                styledFallback ??= fd;
            }
        }
        return styledFallback;
    }

    private const int OffsetTableSize = 12;
    private const int TableRecordSize = 16;
    private const int TtcHeaderSize = 12;
    private const int FaceOffsetSize = 4;
    private const int TagSize = 4;
    private static readonly byte[] TtcTag = "ttcf"u8.ToArray();
    private static readonly byte[] NameTag = "name"u8.ToArray();

    /// <summary>The names of every face in the font file at <paramref name="path"/>,
    /// read from each face's `name` table alone: a host carries hundreds of font files, several
    /// of them collections of tens of MB, and a miss walks them all - reading each whole and
    /// rebuilding every face into a standalone font just to take two strings off it was the
    /// cost of that walk.</summary>
    private static FontFaceNames[] ReadFaceNames(string path)
    {
        using var stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
        var head = ReadAt(stream, 0, TtcHeaderSize + FaceOffsetSize);
        long[] faceOffsets;
        if (head.AsSpan(0, TagSize).SequenceEqual(TtcTag))
        {
            var count = (int)BigEndian32(head, 8);
            faceOffsets = new long[count];
            var table = ReadAt(stream, TtcHeaderSize, count * FaceOffsetSize);
            for (var i = 0; i < count; i++) faceOffsets[i] = BigEndian32(table, i * FaceOffsetSize);
        }
        else faceOffsets = [0];

        var names = new FontFaceNames[faceOffsets.Length];
        for (var i = 0; i < names.Length; i++)
        {
            try
            {
                var sfnt = NameTableOnlySfnt(stream, faceOffsets[i]);
                names[i] = sfnt is null ? FontFaceNames.Unknown : FontFaceNames.Read(sfnt);
            }
            catch { names[i] = FontFaceNames.Unknown; }
        }
        return names;
    }

    /// <summary>A font holding only the `name` table of the face whose table directory starts
    /// at <paramref name="faceOffset"/> - what the name readers parse - or null when the face
    /// has none. A collection's table offsets count from the start of the file.</summary>
    private static byte[]? NameTableOnlySfnt(System.IO.Stream stream, long faceOffset)
    {
        var offsetTable = ReadAt(stream, faceOffset, OffsetTableSize);
        var numTables = (offsetTable[4] << 8) | offsetTable[5];
        var records = ReadAt(stream, faceOffset + OffsetTableSize, numTables * TableRecordSize);
        for (var i = 0; i < numTables; i++)
        {
            var at = i * TableRecordSize;
            if (!records.AsSpan(at, TagSize).SequenceEqual(NameTag)) continue;
            var length = (int)BigEndian32(records, at + 12);
            var table = ReadAt(stream, BigEndian32(records, at + 8), length);
            var sfnt = new byte[OffsetTableSize + TableRecordSize + length];
            Array.Copy(offsetTable, sfnt, OffsetTableSize);
            sfnt[4] = 0; sfnt[5] = 1; // one table
            Array.Copy(records, at, sfnt, OffsetTableSize, TableRecordSize);
            var dataOffset = (uint)(OffsetTableSize + TableRecordSize);
            sfnt[OffsetTableSize + 8] = (byte)(dataOffset >> 24);
            sfnt[OffsetTableSize + 9] = (byte)(dataOffset >> 16);
            sfnt[OffsetTableSize + 10] = (byte)(dataOffset >> 8);
            sfnt[OffsetTableSize + 11] = (byte)dataOffset;
            Array.Copy(table, 0, sfnt, (int)dataOffset, length);
            return sfnt;
        }
        return null;
    }

    private static byte[] ReadAt(System.IO.Stream stream, long offset, int count)
    {
        var buffer = new byte[count];
        stream.Position = offset;
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n <= 0) throw new System.IO.EndOfStreamException();
            read += n;
        }
        return buffer;
    }

    private static uint BigEndian32(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
}
