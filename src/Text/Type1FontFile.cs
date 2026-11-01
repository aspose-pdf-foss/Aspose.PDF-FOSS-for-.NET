using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

/// <summary>
/// A Type 1 font program read from a file - PFB (segmented) or PFA (hexadecimal) - with
/// the metrics of its AFM companion when there is one. A page declares it as a simple
/// /Type1 font (ISO 32000-1 9.6.2) with the program embedded as /FontFile, its three
/// parts measured as /Length1, /Length2 and /Length3 (9.9). A face whose AFM names the
/// standard Latin glyphs is written in WinAnsiEncoding; a FontSpecific face keeps its
/// built-in encoding and its text is written as the codes it gives.
/// </summary>
internal sealed class Type1FontFile
{
    private const byte PfbMarker = 0x80;
    private const byte PfbAscii = 1;
    private const byte PfbBinary = 2;
    private const int PfbHeaderLength = 6;
    private const int CodeCount = 256;
    private const double GlyphSpace = 1000;
    private const string Eexec = "eexec";
    private const string ClearToMark = "cleartomark";

    // Font descriptor flags (ISO 32000-1 9.8.2, Table 123).
    private const int FixedPitchFlag = 1;
    private const int SymbolicFlag = 4;
    private const int NonsymbolicFlag = 32;
    private const int ItalicFlag = 64;

    private readonly int[] _widths = new int[CodeCount];
    private readonly double[] _bbox = new double[4];
    private PdfStream? _program;

    /// <summary>The face's PostScript name, the /BaseFont it is declared by.</summary>
    public string FontName { get; private set; } = "Type1";

    /// <summary>Whether the face keeps its own encoding: its text is written as the codes
    /// that encoding gives rather than in WinAnsi.</summary>
    public bool FontSpecific { get; private set; }

    private byte[] _data = Array.Empty<byte>();
    private int _length1, _length2, _length3;
    private double _italicAngle, _ascent, _descent, _capHeight, _xHeight, _stemV;
    private bool _fixedPitch;

    private Type1FontFile() { }

    /// <summary>Read <paramref name="program"/> and, when given, the AFM metrics; null when
    /// the program is neither a PFB nor a PFA.</summary>
    public static Type1FontFile? TryRead(byte[] program, string? afm)
    {
        var file = new Type1FontFile();
        if (!file.ReadProgram(program)) return null;
        file.ReadName();
        if (afm is not null) file.ReadAfm(afm);
        else file.ReadOwnWidths();
        return file;
    }

    /// <summary>The program as a PDF stream, made once and shared by every page that
    /// declares the face, so the file carries it once.</summary>
    public PdfStream Program()
    {
        if (_program is not null) return _program;
        _program = new PdfStream(new PdfDictionary(), _data);
        _program.Dict.Set("Length1", new PdfInteger(_length1));
        _program.Dict.Set("Length2", new PdfInteger(_length2));
        _program.Dict.Set("Length3", new PdfInteger(_length3));
        return _program;
    }

    /// <summary>The code a character is written as.</summary>
    public char Code(char c) => FontSpecific ? c : Content.ContentStreamBuilder.ToWinAnsi(c);

    /// <summary>The advance of <paramref name="text"/> at <paramref name="fontSize"/>.</summary>
    public double Measure(string text, double fontSize)
    {
        double total = 0;
        foreach (var c in text)
        {
            var code = Code(c);
            if (code < CodeCount) total += _widths[code];
        }
        return total * fontSize / GlyphSpace;
    }

    /// <summary>A fresh /Type1 font dictionary for one page's resources.</summary>
    public PdfDictionary FontDictionary()
    {
        var widths = new PdfArray();
        foreach (var w in _widths) widths.Add(new PdfInteger(w));

        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(FontName));
        font.Set("FirstChar", new PdfInteger(0));
        font.Set("LastChar", new PdfInteger(CodeCount - 1));
        font.Set("Widths", widths);
        if (!FontSpecific) font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        font.Set("FontDescriptor", Descriptor());
        return font;
    }

    private PdfDictionary Descriptor()
    {
        var flags = FontSpecific ? SymbolicFlag : NonsymbolicFlag;
        if (_fixedPitch) flags |= FixedPitchFlag;
        if (_italicAngle != 0) flags |= ItalicFlag;

        var bbox = new PdfArray();
        foreach (var v in _bbox) bbox.Add(new PdfReal(v));
        var descriptor = new PdfDictionary();
        descriptor.Set("Type", new PdfName("FontDescriptor"));
        descriptor.Set("FontName", new PdfName(FontName));
        descriptor.Set("Flags", new PdfInteger(flags));
        descriptor.Set("FontBBox", bbox);
        descriptor.Set("ItalicAngle", new PdfReal(_italicAngle));
        descriptor.Set("Ascent", new PdfReal(_ascent));
        descriptor.Set("Descent", new PdfReal(_descent));
        descriptor.Set("CapHeight", new PdfReal(_capHeight));
        if (_xHeight != 0) descriptor.Set("XHeight", new PdfReal(_xHeight));
        descriptor.Set("StemV", new PdfReal(_stemV));
        descriptor.Set("FontFile", Program());
        return descriptor;
    }

    /// <summary>The program's clear-text, encrypted and trailing parts, the encrypted part
    /// as binary whichever form the file holds it in.</summary>
    private bool ReadProgram(byte[] program)
    {
        if (program.Length > PfbHeaderLength && program[0] == PfbMarker) return ReadPfb(program);
        var text = Compat.Latin1.GetString(program);
        return text.StartsWith("%!", StringComparison.Ordinal) && ReadPfa(text);
    }

    private bool ReadPfb(byte[] program)
    {
        var clear = new List<byte>();
        var binary = new List<byte>();
        var trailer = new List<byte>();
        var at = 0;
        while (at + PfbHeaderLength <= program.Length && program[at] == PfbMarker)
        {
            var kind = program[at + 1];
            if (kind != PfbAscii && kind != PfbBinary) break;
            var length = BitConverter.ToInt32(program, at + 2);
            at += PfbHeaderLength;
            if (length < 0 || at + length > program.Length) return false;
            var part = new ArraySegment<byte>(program, at, length);
            if (kind == PfbBinary) binary.AddRange(part);
            else if (binary.Count == 0) clear.AddRange(part);
            else trailer.AddRange(part);
            at += length;
        }
        return Assemble(clear.ToArray(), binary.ToArray(), trailer.ToArray());
    }

    private bool ReadPfa(string text)
    {
        var eexec = text.IndexOf(Eexec, StringComparison.Ordinal);
        var mark = text.LastIndexOf(ClearToMark, StringComparison.Ordinal);
        if (eexec < 0 || mark < eexec) return false;
        var clearEnd = eexec + Eexec.Length;
        while (clearEnd < text.Length && text[clearEnd] is '\r' or '\n') clearEnd++;
        // The encrypted part ends where the trailer's zeros begin.
        var trailerStart = mark;
        while (trailerStart > clearEnd && (text[trailerStart - 1] == '0' || char.IsWhiteSpace(text[trailerStart - 1])))
            trailerStart--;

        var binary = new List<byte>();
        var high = -1;
        for (var i = clearEnd; i < trailerStart; i++)
        {
            var nibble = Uri.IsHexDigit(text[i]) ? Convert.ToInt32(text[i].ToString(), 16) : -1;
            if (nibble < 0) continue;
            if (high < 0) high = nibble;
            else { binary.Add((byte)((high << 4) | nibble)); high = -1; }
        }
        return Assemble(Compat.Latin1.GetBytes(text[..clearEnd]), binary.ToArray(),
            Compat.Latin1.GetBytes(text[trailerStart..]));
    }

    private bool Assemble(byte[] clear, byte[] binary, byte[] trailer)
    {
        if (clear.Length == 0 || binary.Length == 0) return false;
        _data = new byte[clear.Length + binary.Length + trailer.Length];
        clear.CopyTo(_data, 0);
        binary.CopyTo(_data, clear.Length);
        trailer.CopyTo(_data, clear.Length + binary.Length);
        (_length1, _length2, _length3) = (clear.Length, binary.Length, trailer.Length);
        return true;
    }

    /// <summary>The name and box the program's clear text states, which an AFM overrides.</summary>
    private void ReadName()
    {
        var clear = Compat.Latin1.GetString(_data, 0, _length1);
        var name = SafeRegex.Match(clear, @"/FontName\s*/([^\s/\[\]{}()<>]+)");
        if (name.Success) FontName = name.Groups[1].Value;
        var box = SafeRegex.Match(clear, @"/FontBBox\s*[\[{]\s*([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)");
        if (box.Success)
            for (var i = 0; i < _bbox.Length; i++) _bbox[i] = Number(box.Groups[i + 1].Value);
        _ascent = _bbox[3];
        _descent = _bbox[1];
        _capHeight = _bbox[3];
    }

    /// <summary>Header keys and character metrics of an AFM file (Adobe Technical Note 5004).</summary>
    private void ReadAfm(string afm)
    {
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        var byCode = new Dictionary<int, int>();
        foreach (var raw in afm.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("C ", StringComparison.Ordinal) || line.StartsWith("CH ", StringComparison.Ordinal))
            {
                ReadCharMetric(line, byName, byCode);
                continue;
            }
            var space = line.IndexOf(' ');
            if (space < 0) continue;
            ReadHeader(line[..space], line[(space + 1)..].Trim());
        }

        for (var code = 0; code < CodeCount; code++)
        {
            if (FontSpecific) _widths[code] = byCode.GetValueOrDefault(code);
            else if (PdfEncodings.WinAnsiName(code) is { } glyph) _widths[code] = byName.GetValueOrDefault(glyph);
        }
    }

    private void ReadHeader(string key, string value)
    {
        switch (key)
        {
            case "FontName": FontName = value; break;
            case "EncodingScheme": FontSpecific = value == "FontSpecific"; break;
            case "ItalicAngle": _italicAngle = Number(value); break;
            case "IsFixedPitch": _fixedPitch = value == "true"; break;
            case "Ascender": _ascent = Number(value); break;
            case "Descender": _descent = Number(value); break;
            case "CapHeight": _capHeight = Number(value); break;
            case "XHeight": _xHeight = Number(value); break;
            case "StdVW": _stemV = Number(value); break;
            case "FontBBox":
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < _bbox.Length && i < parts.Length; i++) _bbox[i] = Number(parts[i]);
                break;
        }
    }

    /// <summary>One <c>C code ; WX width ; N name ; ...</c> line.</summary>
    private static void ReadCharMetric(string line, Dictionary<string, int> byName, Dictionary<int, int> byCode)
    {
        int? code = null, width = null;
        string? name = null;
        foreach (var field in line.Split(';'))
        {
            var parts = field.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            switch (parts[0])
            {
                case "C": code = (int)Number(parts[1]); break;
                case "CH": code = Convert.ToInt32(parts[1].Trim('<', '>'), 16); break;
                case "WX" or "W0X": width = (int)Math.Round(Number(parts[1])); break;
                case "N": name = parts[1]; break;
            }
        }
        if (width is not { } w) return;
        if (name is not null) byName[name] = w;
        if (code is >= 0 and < CodeCount) byCode[code.Value] = w;
    }

    /// <summary>Without an AFM the widths come from the charstrings themselves; a face
    /// whose own encoding is not the standard one keeps it.</summary>
    private void ReadOwnWidths()
    {
        var source = Type1GlyphSource.TryLoad(_data, _length1, _length2);
        if (source is null) return;
        var scale = GlyphSpace / source.UnitsPerEm;
        FontSpecific = source.EncodingByteToGid is not null && !UsesStandardNames(source);
        for (var code = 0; code < CodeCount; code++)
        {
            var gid = GlyphFor(source, code);
            if (gid > 0) _widths[code] = (int)Math.Round(source.GetAdvanceWidth(gid) * scale);
        }
    }

    /// <summary>The glyph a code is set with: by the face's own encoding when it keeps one, else by its WinAnsi name.</summary>
    private int GlyphFor(Type1GlyphSource source, int code)
    {
        if (FontSpecific) return source.EncodingByteToGid![code];
        return PdfEncodings.WinAnsiName(code) is { } glyph ? source.GidForName(glyph) : 0;
    }

    private static bool UsesStandardNames(Type1GlyphSource source) =>
        source.NameToGid.ContainsKey("a") || source.NameToGid.ContainsKey("A") || source.NameToGid.ContainsKey("zero");

    private static double Number(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
