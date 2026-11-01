namespace Aspose.Pdf.Text;

/// <summary>How a font name query matched a face: not at all, by one of its family names, or by a
/// name of the face itself (its full or PostScript name).</summary>
internal enum FaceNameMatch
{
    None,
    Family,
    Face,
}

/// <summary>Every name one face answers to, read once from its `name` table: the English full name
/// and family the face reports, plus its PostScript name (id 6) and its family (id 1)
/// and full (id 4) names in the other languages the table carries. A face is asked for by any of them - "Humnst777 BT"
/// is the family of the face whose full name is "Humanist 777 BT", "MalgunGothic" the PostScript
/// name of Malgun Gothic, "標楷體" the Chinese family of DFKai-SB - so a lookup that compares the
/// English full name alone misses faces every other application finds.</summary>
internal sealed class FontFaceNames
{
    private const int FamilyId = 1;
    private const int FullNameId = 4;
    private const int PostScriptNameId = 6;
    // The typographic family (id 16) is left out: it groups faces whose own family names differ
    // ("Proxima Nova" over "Proxima Nova Rg" and "Proxima Nova Lt"), and a style sheet's family
    // name would take whichever face of the group a folder lists first.
    private const int NameRecordSize = 12;
    private const int NameTableHeaderSize = 6;
    private const int TableRecordSize = 16;
    private const int OffsetTableSize = 12;
    private const int TagSize = 4;
    private const int PlatformUnicode = 0;
    private const int PlatformMacintosh = 1;
    private const int PlatformWindows = 3;
    private const string UnknownName = "Unknown";

    internal static readonly FontFaceNames Unknown = new(UnknownName, UnknownName, [], []);

    // The names of the face itself and of its family, each beside its key: the name with spaces and
    // hyphens removed, made once here rather than on every comparison of a lookup that walks every
    // face of the host.
    private readonly string[] _faceNames;
    private readonly string[] _faceKeys;
    private readonly string[] _familyNames;
    private readonly string[] _familyKeys;

    private FontFaceNames(string full, string family, List<string> faceNames, List<string> familyNames)
    {
        Full = full;
        Family = family;
        faceNames.Insert(0, full);
        familyNames.Insert(0, family);
        _faceNames = Usable(faceNames);
        _faceKeys = _faceNames.Select(Key).ToArray();
        _familyNames = Usable(familyNames);
        _familyKeys = _familyNames.Select(Key).ToArray();
    }

    private static string[] Usable(List<string> names) =>
        names.Where(n => n.Length > 0 && n != UnknownName).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>A name as the lookups compare it loosely: without spaces and hyphens.</summary>
    internal static string Key(string name) => name.Replace(" ", "").Replace("-", "");

    /// <summary>The English full name: what a match reports as the font's name.</summary>
    internal string Full { get; }

    /// <summary>The English family name.</summary>
    internal string Family { get; }

    /// <summary>True when the face has no readable name.</summary>
    internal bool IsUnknown => Full == UnknownName;

    /// <summary>The regular face of its family: its full name is the family itself.</summary>
    internal bool IsRegular => string.Equals(Full, Family, StringComparison.OrdinalIgnoreCase);

    /// <summary>The names of the face whose sfnt (a whole font, or one holding only its name table)
    /// is <paramref name="sfnt"/>; <see cref="Unknown"/> when the name table cannot be read.</summary>
    internal static FontFaceNames Read(byte[] sfnt)
    {
        try
        {
            var full = FontRepository.ReadTtfFontName(sfnt);
            if (full == UnknownName) return Unknown;
            string family;
            try { family = FontRepository.ReadTtfFamilyName(sfnt); } catch { family = UnknownName; }
            var faceNames = new List<string>();
            var familyNames = new List<string>();
            CollectNames(sfnt, faceNames, familyNames);
            return new FontFaceNames(full, family, faceNames, familyNames);
        }
        catch { return Unknown; }
    }

    /// <summary>How <paramref name="name"/> names this face: compared as given, and with spaces and
    /// hyphens removed (<paramref name="normalizedName"/>) without regard to case, as the folder
    /// and system sources have always compared.</summary>
    internal FaceNameMatch Match(string name, StringComparison comparison, string normalizedName)
    {
        if (Matches(_faceNames, _faceKeys, name, comparison, normalizedName)) return FaceNameMatch.Face;
        return Matches(_familyNames, _familyKeys, name, comparison, normalizedName) ? FaceNameMatch.Family : FaceNameMatch.None;
    }

    private static bool Matches(string[] names, string[] keys, string name, StringComparison comparison, string normalizedName)
    {
        for (var i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], name, comparison)
                || string.Equals(keys[i], normalizedName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Adds every full and PostScript name of the face to <paramref name="faceNames"/> and
    /// every family name to <paramref name="familyNames"/>, in every language.</summary>
    private static void CollectNames(byte[] data, List<string> faceNames, List<string> familyNames)
    {
        var tableOffset = NameTableOffset(data);
        if (tableOffset < 0 || tableOffset + NameTableHeaderSize > data.Length) return;
        var count = ReadUInt16BE(data, tableOffset + 2);
        var storage = tableOffset + ReadUInt16BE(data, tableOffset + 4);
        for (var r = 0; r < count; r++)
        {
            var rec = tableOffset + NameTableHeaderSize + r * NameRecordSize;
            if (rec + NameRecordSize > data.Length) break;
            var platform = ReadUInt16BE(data, rec);
            var nameId = ReadUInt16BE(data, rec + 6);
            var target = nameId switch
            {
                FullNameId or PostScriptNameId => faceNames,
                FamilyId => familyNames,
                _ => null,
            };
            if (target is null) continue;
            // English family and full names beyond the ones the face reports are other platforms'
            // spellings of its group ("Proxima Nova" on the Macintosh record of "Proxima Nova Rg"):
            // only the PostScript name and the names in other languages are added to those.
            if (nameId != PostScriptNameId && IsEnglish(platform, ReadUInt16BE(data, rec + 4))) continue;
            var length = ReadUInt16BE(data, rec + 8);
            var start = storage + ReadUInt16BE(data, rec + 10);
            if (length == 0 || start + length > data.Length) continue;
            var text = platform switch
            {
                PlatformWindows or PlatformUnicode => System.Text.Encoding.BigEndianUnicode.GetString(data, start, length),
                // Macintosh records in other languages use legacy script encodings: only the
                // Roman (English) ones read as Latin-1.
                PlatformMacintosh when ReadUInt16BE(data, rec + 4) == 0 => Compat.Latin1.GetString(data, start, length),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(text)) target.Add(text!.Trim('\0', ' '));
        }
    }

    private const int EnglishLanguageMask = 0x3FF;
    private const int WindowsEnglish = 0x009;
    private const int MacintoshEnglish = 0;

    /// <summary>A record in English: any English Windows locale, the Macintosh English one, or a
    /// Unicode-platform record, which carries no language.</summary>
    private static bool IsEnglish(int platform, int language) => platform switch
    {
        PlatformWindows => (language & EnglishLanguageMask) == WindowsEnglish,
        PlatformMacintosh => language == MacintoshEnglish,
        _ => true,
    };

    private static int NameTableOffset(byte[] data)
    {
        if (data.Length < OffsetTableSize) return -1;
        var numTables = ReadUInt16BE(data, 4);
        for (var i = 0; i < numTables; i++)
        {
            var at = OffsetTableSize + i * TableRecordSize;
            if (at + TableRecordSize > data.Length) break;
            if (data[at] == 'n' && data[at + 1] == 'a' && data[at + 2] == 'm' && data[at + TagSize - 1] == 'e')
                return (int)(((uint)data[at + 8] << 24) | ((uint)data[at + 9] << 16) | ((uint)data[at + 10] << 8) | data[at + 11]);
        }
        return -1;
    }

    private static int ReadUInt16BE(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];
}
