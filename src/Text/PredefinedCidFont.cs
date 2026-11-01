using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

/// <summary>
/// A predefined CJK face (<see cref="PredefinedCidFaces"/>) declared in a predefined CMap,
/// with no font program: a /Type0 font over one /CIDFontType0 descendant (ISO 32000-1 9.7.6)
/// whose glyphs the reader supplies from the face it carries for the character collection.
/// A face is found by its own name - declared in the Unicode CMap of its collection, its text
/// written as UCS-2 - or, for a Japanese face, by its name and "-90ms-RKSJ-H", its text
/// written in Shift-JIS. A run is measured by the /W widths of the CIDs its codes select.
/// </summary>
internal sealed class PredefinedCidFont
{
    private const double GlyphSpace = 1000;
    private const int DefaultWidth = 1000;
    private const string Japan1 = "Japan1";

    private readonly PredefinedCidFace _face;
    private readonly Dictionary<int, int> _widths;

    public PredefinedCMap CMap { get; }

    /// <summary>The face's name, the /BaseFont it is declared by.</summary>
    public string FaceName => _face.Name;

    /// <summary>The descent its descriptor declares, in thousandths of the size.</summary>
    public int Descent => _face.Descent;

    private PredefinedCidFont(PredefinedCidFace face, PredefinedCMap cmap)
    {
        _face = face;
        CMap = cmap;
        _widths = ReadWidths(face.Widths);
    }

    /// <summary>The face <paramref name="name"/> declares, or null when it names none.</summary>
    public static PredefinedCidFont? Find(string name, bool ignoreCase)
    {
        var comparison = ignoreCase ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal;
        foreach (var face in PredefinedCidFaces.All)
        {
            if (string.Equals(name, face.Name, comparison)
                || string.Equals(name, face.Name + "-" + face.UnicodeCMap, comparison))
                return new PredefinedCidFont(face, PredefinedCMap.Get(face.UnicodeCMap));
            if (face.Ordering == Japan1 && string.Equals(name, face.Name + "-" + PredefinedCMap.ShiftJis, comparison))
                return new PredefinedCidFont(face, PredefinedCMap.Get(PredefinedCMap.ShiftJis));
        }
        return null;
    }

    /// <summary>The bytes <paramref name="text"/> is shown as.</summary>
    public byte[] Encode(string text) => CMap.Bytes(CMap.Codes(text));

    /// <summary>The advance of <paramref name="text"/> at <paramref name="fontSize"/>.</summary>
    public double Measure(string text, double fontSize) =>
        CMap.Codes(text).Sum(code => Width(CMap.Cid(code))) * fontSize / GlyphSpace;

    private int Width(int cid) => _widths.TryGetValue(cid, out var width) ? width : DefaultWidth;

    /// <summary>The /Type0 font dictionary a page declares the face with.</summary>
    public PdfDictionary FontDictionary()
    {
        var systemInfo = new PdfDictionary();
        systemInfo.Set("Registry", new PdfString(System.Text.Encoding.ASCII.GetBytes("Adobe")));
        systemInfo.Set("Ordering", new PdfString(System.Text.Encoding.ASCII.GetBytes(_face.Ordering)));
        systemInfo.Set("Supplement", new PdfInteger(_face.Supplement));

        var descendant = new PdfDictionary();
        descendant.Set("Type", new PdfName("Font"));
        descendant.Set("Subtype", new PdfName("CIDFontType0"));
        descendant.Set("BaseFont", new PdfName(_face.Name));
        descendant.Set("CIDSystemInfo", systemInfo);
        descendant.Set("FontDescriptor", Descriptor());
        descendant.Set("DW", new PdfInteger(DefaultWidth));
        descendant.Set("W", WidthArray(_face.Widths));

        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type0"));
        font.Set("BaseFont", new PdfName(_face.Name));
        font.Set("Encoding", new PdfName(CMap.Name));
        font.Set("DescendantFonts", new PdfArray { descendant });
        return font;
    }

    private PdfDictionary Descriptor()
    {
        var bbox = new PdfArray();
        foreach (var v in _face.FontBBox) bbox.Add(new PdfInteger(v));
        var descriptor = new PdfDictionary();
        descriptor.Set("Type", new PdfName("FontDescriptor"));
        descriptor.Set("FontName", new PdfName(_face.Name));
        descriptor.Set("Flags", new PdfInteger(_face.Flags));
        descriptor.Set("FontBBox", bbox);
        descriptor.Set("ItalicAngle", new PdfInteger(0));
        descriptor.Set("Ascent", new PdfInteger(_face.Ascent));
        descriptor.Set("Descent", new PdfInteger(_face.Descent));
        descriptor.Set("CapHeight", new PdfInteger(_face.CapHeight));
        descriptor.Set("XHeight", new PdfInteger(_face.XHeight));
        descriptor.Set("StemV", new PdfInteger(_face.StemV));
        foreach (var (key, value) in _face.Extra) descriptor.Set(key, new PdfInteger(value));
        return descriptor;
    }

    /// <summary>The /W text as CID -> width.</summary>
    private static Dictionary<int, int> ReadWidths(string w)
    {
        var widths = new Dictionary<int, int>();
        var tokens = Tokens(w);
        var i = 0;
        while (i < tokens.Count)
        {
            var first = int.Parse(tokens[i], CultureInfo.InvariantCulture);
            if (tokens[i + 1] == "[")
            {
                i += 2;
                for (var cid = first; tokens[i] != "]"; cid++, i++)
                    widths[cid] = int.Parse(tokens[i], CultureInfo.InvariantCulture);
                i++;
            }
            else
            {
                var last = int.Parse(tokens[i + 1], CultureInfo.InvariantCulture);
                var width = int.Parse(tokens[i + 2], CultureInfo.InvariantCulture);
                for (var cid = first; cid <= last; cid++) widths[cid] = width;
                i += 3;
            }
        }
        return widths;
    }

    /// <summary>The /W text as the array a CIDFont carries.</summary>
    private static PdfArray WidthArray(string w)
    {
        var array = new PdfArray();
        PdfArray? list = null;
        foreach (var token in Tokens(w))
        {
            switch (token)
            {
                case "[":
                    list = new PdfArray();
                    break;
                case "]":
                    array.Add(list!);
                    list = null;
                    break;
                default:
                    (list ?? array).Add(new PdfInteger(int.Parse(token, CultureInfo.InvariantCulture)));
                    break;
            }
        }
        return array;
    }

    private static readonly char[] Space = [' '];

    private static List<string> Tokens(string w) =>
        w.Replace("[", " [ ").Replace("]", " ] ").Split(Space, System.StringSplitOptions.RemoveEmptyEntries).ToList();
}
