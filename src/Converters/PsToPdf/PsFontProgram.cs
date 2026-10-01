using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// A font program loaded from the search folders, served by glyph NAME. PostScript
/// selects glyphs by name through an encoding vector, so a name lookup is what the
/// converter needs; a program that carries no names is reached through the Adobe
/// glyph list and its own character map instead.
/// </summary>
internal sealed class PsFontProgram
{
    /// <summary>The em the PDF font matrix is expressed in.</summary>
    public const double PdfUnitsPerEm = 1000.0;

    private readonly IGlyphOutlineSource _outlines;
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> _byUnicode;
    private readonly int[] _widths;
    private readonly Dictionary<string, GlyphOutline?> _cache = new(StringComparer.Ordinal);

    private PsFontProgram(IGlyphOutlineSource outlines, Dictionary<int, string> names,
        Dictionary<int, int> byUnicode, int[]? widths)
    {
        _outlines = outlines;
        _byUnicode = byUnicode;
        _widths = widths ?? new int[0];
        foreach (var pair in names)
            if (!string.IsNullOrEmpty(pair.Value) && !_byName.ContainsKey(pair.Value))
                _byName[pair.Value] = pair.Key;
    }

    /// <summary>The font's design units per em.</summary>
    public int UnitsPerEm => _outlines.UnitsPerEm <= 0 ? 1 : _outlines.UnitsPerEm;

    /// <summary>How far above the current point a <c>charpath</c> outline of this program
    /// seats, in ems: the face's bounding-box top (<c>head.yMax</c>) over its Windows ascent
    /// (<c>OS/2.usWinAscent</c>). Null for a program without those tables.</summary>
    public double? CharPathSeatEm { get; private set; }

    /// <summary>Load a program from a file, or null when it cannot be read.</summary>
    public static PsFontProgram? Load(string path)
    {
        try
        {
            return FromBytes(File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Load a program from its bytes. An OpenType container with charstring
    /// outlines is marked by the tag <c>OTTO</c> and needs the charstring reader;
    /// anything else is read as TrueType.</summary>
    public static PsFontProgram? FromBytes(byte[] data)
    {
        if (data is null || data.Length < HeaderBytes) return null;
        var program = IsOpenTypeCharstrings(data) ? FromCharstrings(data) : FromTrueType(data);
        if (program != null) program.CharPathSeatEm = SfntCharPathSeatEm(data);
        return program;
    }

    private const int SfntDirectoryOffset = 12;
    private const int SfntDirectoryEntryBytes = 16;
    private const int HeadUnitsPerEmOffset = 18;
    private const int HeadYMaxOffset = 42;
    private const int HeadBytesNeeded = 54;
    private const int Os2WinAscentOffset = 74;
    private const int Os2BytesNeeded = 78;

    /// <summary>Read <c>head.yMax - OS/2.usWinAscent</c> in ems straight from the sfnt
    /// table directory, which both a TrueType and an OpenType charstring container carry.</summary>
    private static double? SfntCharPathSeatEm(byte[] data)
    {
        var numTables = (data[4] << 8) | data[5];
        int headOffset = -1, os2Offset = -1;
        for (var k = 0; k < numTables; k++)
        {
            var entry = SfntDirectoryOffset + k * SfntDirectoryEntryBytes;
            if (entry + SfntDirectoryEntryBytes > data.Length) return null;
            var tag = Encoding.ASCII.GetString(data, entry, 4);
            var offset = (int)ReadUInt32(data, entry + 8);
            if (tag == "head") headOffset = offset;
            else if (tag == "OS/2") os2Offset = offset;
        }
        if (headOffset < 0 || os2Offset < 0) return null;
        if (headOffset + HeadBytesNeeded > data.Length || os2Offset + Os2BytesNeeded > data.Length) return null;
        var unitsPerEm = ReadUInt16(data, headOffset + HeadUnitsPerEmOffset);
        if (unitsPerEm == 0) return null;
        var yMax = (short)ReadUInt16(data, headOffset + HeadYMaxOffset);
        var winAscent = ReadUInt16(data, os2Offset + Os2WinAscentOffset);
        return (yMax - winAscent) / (double)unitsPerEm;
    }

    private static int ReadUInt16(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];

    private static uint ReadUInt32(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

    /// <summary>Bytes needed before a program's kind can be told.</summary>
    private const int HeaderBytes = 12;

    private static bool IsOpenTypeCharstrings(byte[] data) =>
        data[0] == (byte)'O' && data[1] == (byte)'T' && data[2] == (byte)'T' && data[3] == (byte)'O';

    private static PsFontProgram? FromCharstrings(byte[] data)
    {
        var cff = CffGlyphSource.TryLoad(data);
        if (cff is null) return null;
        var names = new Dictionary<int, string>();
        foreach (var pair in cff.NameToGid)
            if (!names.ContainsKey(pair.Value))
                names[pair.Value] = pair.Key;
        return new PsFontProgram(cff, names, new Dictionary<int, int>(cff.CMap), null);
    }

    private static PsFontProgram? FromTrueType(byte[] data)
    {
        var parser = new TrueTypeParser(data);
        try
        {
            parser.Parse();
        }
        catch (Exception)
        {
            return null;
        }

        var glyphs = new GlyphOutlineParser(data);
        return new PsFontProgram(glyphs, parser.GlyphNames,
            new Dictionary<int, int>(parser.CMap), parser.GlyphWidths);
    }

    /// <summary>Whether the program holds pictures rather than letters. Such a face
    /// names each glyph after the letter whose code it occupies, so a name is read as
    /// that code and looked up in the program's own character map rather than as the
    /// character the name usually stands for.</summary>
    public bool NamesArePositions { get; set; }

    /// <summary>The glyph id a name resolves to, or -1 when the program has no such
    /// glyph. A name absent from the program's own name table is tried through the
    /// Adobe glyph list and the program's character map.</summary>
    public int GlyphFor(string name)
    {
        if (string.IsNullOrEmpty(name) || name == PsEncodings.NotDefined) return -1;
        // The position wins over the name on a picture face: such a program names its
        // ornaments after the letters whose codes they sit at, so the letter's name
        // stands for the code and not for the letter.
        if (NamesArePositions)
        {
            var position = PsEncodings.StandardCodeOf(name);
            if (position >= 0 && TryMap(position, out var byPosition)) return byPosition;
        }

        if (_byName.TryGetValue(name, out var gid)) return gid;
        var code = UnicodeFor(name);
        return code >= 0 && TryMap(code, out var mapped) ? mapped : -1;
    }

    /// <summary>A character map lookup, allowing for the private-use block a symbol
    /// character map keeps a face's own codes in.</summary>
    private bool TryMap(int code, out int gid)
    {
        if (_byUnicode.TryGetValue(code, out gid)) return true;
        return code <= ByteMask && _byUnicode.TryGetValue(SymbolArea + code, out gid);
    }

    /// <summary>Where a symbol character map puts a face's own codes.</summary>
    private const int SymbolArea = 0xF000;

    /// <summary>The largest code such a map keys on.</summary>
    private const int ByteMask = 0xFF;

    /// <summary>The code point a glyph name stands for, or -1. Handles the
    /// <c>uniXXXX</c> form as well as the standard list.</summary>
    private static int UnicodeFor(string name)
    {
        if (TextAbsorber.GlyphNameToUnicode.TryGetValue(name, out var text) && text.Length > 0)
            return char.ConvertToUtf32(text, 0);
        const string UniPrefix = "uni";
        if (name.Length == UniPrefix.Length + 4 && name.StartsWith(UniPrefix, StringComparison.Ordinal) &&
            int.TryParse(name.Substring(UniPrefix.Length),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var code))
            return code;
        return -1;
    }

    /// <summary>The glyph a CHARACTER resolves to through the program's own character
    /// map. A character the program has not got resolves to glyph zero, the box a face
    /// draws in place of what it cannot show.</summary>
    public int GlyphAtCharacter(int character) =>
        character > 0 && TryMap(character, out var gid) ? gid : NotDefinedGlyph;

    /// <summary>The glyph a face draws in place of what it cannot show.</summary>
    private const int NotDefinedGlyph = 0;

    /// <summary>The outline of the glyph a character resolves to.</summary>
    public GlyphOutline? OutlineAtCharacter(int character) =>
        _outlines.GetOutline(GlyphAtCharacter(character));

    /// <summary>The advance of a named glyph, in units of one thousandth of an em, or
    /// -1 when the program has no such glyph.</summary>
    public double WidthFor(string name)
    {
        var gid = GlyphFor(name);
        if (gid < 0) return -1;
        var units = gid < _widths.Length ? _widths[gid] : _outlines.GetAdvanceWidth(gid);
        return units * PdfUnitsPerEm / UnitsPerEm;
    }

    /// <summary>The outline of a named glyph, in units of one thousandth of an em with
    /// y increasing upwards, or null when the program has no such glyph.</summary>
    public GlyphOutline? OutlineFor(string name)
    {
        if (_cache.TryGetValue(name, out var cached)) return cached;
        var gid = GlyphFor(name);
        var outline = gid < 0 ? null : _outlines.GetOutline(gid);
        _cache[name] = outline;
        return outline;
    }

    /// <summary>Scale from the program's design units to the PDF em.</summary>
    public double ToPdfUnits => PdfUnitsPerEm / UnitsPerEm;
}
