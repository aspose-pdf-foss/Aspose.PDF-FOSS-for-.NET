using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SvgDevice
{
    // The document's fonts as the pages draw them, shipped with each page as web fonts.
    private readonly SvgFontFaces _faces = new();

    /// <summary>
    /// The embedded fonts of a conversion and the faces each page ships. A font's glyph program and
    /// its code-to-glyph mapping are resolved once per conversion, the way the page renderer resolves
    /// them, so the SVG shows the glyph a render shows. Per page, every glyph a run draws is given a
    /// character the page's face maps to it: the character the text decodes to when that is free, a
    /// substitute one otherwise (no Unicode mapping, or a character another glyph already holds). A font
    /// with no program of its own is served by the installed face that stands in for it, subset the same way.
    /// </summary>
    private sealed class SvgFontFaces
    {
        private readonly Dictionary<PdfDictionary, FaceProgram?> _programs = new();
        private readonly Dictionary<PdfDictionary, SvgFontFace> _page = new();
        private readonly HashSet<string> _families = new(StringComparer.Ordinal);
        private readonly List<SvgFontFace> _order = new();

        public void BeginPage()
        {
            _page.Clear();
            _families.Clear();
            _order.Clear();
        }

        /// <summary>The page's face for a font, or null when the font embeds no usable program.</summary>
        public SvgFontFace? For(PdfDictionary font, PdfReader reader)
        {
            if (_page.TryGetValue(font, out var face)) return face;
            if (!_programs.TryGetValue(font, out var program))
            {
                program = FaceProgram.TryLoad(font, reader);
                _programs[font] = program;
            }
            if (program is null) return null;
            face = new SvgFontFace(program, UniqueFamily(program.Family));
            _page[font] = face;
            _order.Add(face);
            return face;
        }

        private string UniqueFamily(string family)
        {
            var name = family;
            for (var n = 2; !_families.Add(name); n++) name = $"{family}-{n}";
            return name;
        }

        /// <summary>The @font-face rules of the faces the page drew glyphs from, or an empty string.</summary>
        public string Css()
        {
            var css = new StringBuilder();
            foreach (var face in _order)
            {
                var ttf = face.BuildFont();
                if (ttf is null) continue;
                css.Append("@font-face {font-family:'").Append(face.Family).AppendLine("';");
                css.Append("src: url(\"data:font/truetype;charset=utf-8;base64,")
                    .Append(Convert.ToBase64String(ttf)).AppendLine("\");");
                css.AppendLine("}");
            }
            return css.ToString();
        }
    }

    /// <summary>A font's program - embedded, or the installed face standing in for a simple font that
    /// embeds none - and how a code of the font reaches its glyph.</summary>
    private sealed class FaceProgram
    {
        public required IGlyphOutlineSource Source { get; init; }
        public required string Family { get; init; }
        public CidFontInfo? Cid { get; init; }
        public int[]? EncodingGids { get; init; }
        public bool[]? UndefinedCodes { get; init; }
        // An installed face standing in for the font: its codes are known through the font's
        // encoding alone, and its family carries the weight and slant the run must ask for.
        public bool HostFace { get; init; }
        public bool Bold { get; init; }
        public bool Italic { get; init; }

        // The resource name the renderer's single-font helpers are asked under.
        private const string ResourceName = "F";

        public static FaceProgram? TryLoad(PdfDictionary font, PdfReader reader)
        {
            try
            {
                var subtype = font.GetName("Subtype");
                // A Type 3 font draws content streams, not outlines.
                if (subtype == "Type3") return null;
                var fonts = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal) { [ResourceName] = font };
                var embedded = SoftwarePageRenderer.HasEmbeddedProgram(fonts, reader, ResourceName);
                // A composite font without a program has no face to stand in for it here: its codes
                // are CIDs, which only the renderer's own fallback knows how to read.
                if (!embedded && subtype == "Type0") return null;
                var cache = new Dictionary<string, (IGlyphOutlineSource? parser, double hScale)>(StringComparer.Ordinal);
                var (source, _) = SoftwarePageRenderer.GetGlyphParser(fonts, reader, cache, ResourceName);
                if (source is null) return null;
                CidFontInfo? cid = null;
                if (subtype == "Type0")
                {
                    cid = CidFontInfo.TryBuild(font, reader);
                    // The device steps a composite font's string two bytes a code.
                    if (cid is null || !cid.IsTwoByteEncoding || cid.LegacyCodepage != 0) return null;
                }
                var baseFont = font.GetName("BaseFont") ?? "Font";
                var family = CssFamily(baseFont);
                var (bold, italic) = (false, false);
                if (!embedded)
                {
                    // The stand-in ships as a subset under a tag of its own, named for the installed face
                    // and its style, the way a page names a face it substitutes.
                    var face = SystemFontResolver.SubstituteFaceName(baseFont);
                    (bold, italic) = (face.Bold, face.Italic);
                    family = SubsetTag(baseFont) + "+" + CssFamily(face.Family)
                        + (bold ? " Bold" : "") + (italic ? " Italic" : "");
                }
                return new FaceProgram
                {
                    Source = source,
                    Family = family,
                    Cid = cid,
                    EncodingGids = cid is null ? SoftwarePageRenderer.BuildEncodingGidMap(fonts, reader, ResourceName, source) : null,
                    UndefinedCodes = cid is null ? SoftwarePageRenderer.BuildUndefinedCodes(font, reader) : null,
                    HostFace = !embedded,
                    Bold = bold,
                    Italic = italic,
                };
            }
            catch
            {
                return null; // a program that does not load keeps the generic family
            }
        }

        /// <summary>The glyph a code draws, 0 for none - the renderer's resolution for the font kind.</summary>
        public int GlyphFor(int code, string decoded)
        {
            if (Cid is not null)
            {
                var cid = Cid.CodeToCid(code);
                if (Source is CffGlyphSource { IsCidKeyed: true } cff)
                {
                    var gid = cff.CidToGid(cid);
                    // An out-of-charset CID over a small identity charset draws its low byte's glyph.
                    return gid == 0 && cid > byte.MaxValue ? cff.CidToGid(cid & byte.MaxValue) : gid;
                }
                return Cid.ResolveGid(cid);
            }
            if (UndefinedCodes is not null && code < UndefinedCodes.Length && UndefinedCodes[code]) return 0;
            if (EncodingGids is not null && code < EncodingGids.Length && EncodingGids[code] != 0)
                return Math.Max(EncodingGids[code], 0);
            // Only a program of the font's own keys its table by the font's codes; a stand-in knows them
            // through the encoding and the character alone.
            if (!HostFace && Source.CMap.TryGetValue(code, out var byCode) && byCode > 0) return byCode;
            return decoded.Length == 1 && Source.CMap.TryGetValue(decoded[0], out var byChar) ? Math.Max(byChar, 0) : 0;
        }

        /// <summary>A six-letter tag for a substitute face, derived from the font's name so the same font
        /// ships under the same family in every page and every save.</summary>
        private static string SubsetTag(string baseFont)
        {
            const uint FnvOffset = 2166136261;
            const uint FnvPrime = 16777619;
            const int TagLength = 6;
            const int Letters = 26;
            var hash = FnvOffset;
            foreach (var ch in baseFont) hash = (hash ^ ch) * FnvPrime;
            var tag = new char[TagLength];
            for (var i = 0; i < TagLength; i++)
            {
                tag[i] = (char)('A' + hash % Letters);
                hash /= Letters;
            }
            return new string(tag);
        }

        /// <summary>True when the glyph has no outline to paint.</summary>
        public bool IsBlank(int gid)
        {
            try { return Source.GetOutline(gid) is not { Contours.Length: > 0 }; }
            catch { return true; }
        }

        /// <summary>A family name safe inside a quoted CSS string in an XML attribute.</summary>
        private static string CssFamily(string baseFont)
        {
            var sb = new StringBuilder(baseFont.Length);
            foreach (var ch in baseFont)
                sb.Append(char.IsLetterOrDigit(ch) || ch is '+' or '-' or '_' or '.' or ' ' ? ch : '_');
            return sb.ToString();
        }
    }

    /// <summary>One font's face on one page: the characters written for its glyphs.</summary>
    private sealed class SvgFontFace
    {
        private readonly FaceProgram _program;
        private readonly Dictionary<int, int> _charToGlyph = new();
        private readonly Dictionary<int, string> _codeText = new();
        private int _nextSubstitute = SubstituteFirst;

        // A glyph with no character of its own is written as the next character after the blank-slot
        // placeholder, in order of first use - the block a reader of these documents expects such
        // glyphs in - and from the Basic Multilingual Plane's private use area once that block is spent.
        private const int SubstituteFirst = BlankPlaceholder + 1;
        private const int SubstituteLast = 0xA8FF;
        private const int PrivateUseFirst = 0xE000;
        private const int PrivateUseLast = 0xF8FF;

        // The character the device writes for a glyph slot that has no text (see ShowText).
        private const int BlankPlaceholder = 'ꢀ';

        public SvgFontFace(FaceProgram program, string family)
        {
            _program = program;
            Family = family;
        }

        public string Family { get; }

        /// <summary>True when the face is an installed stand-in of that weight or slant, which the run
        /// states beside the family.</summary>
        public bool Bold => _program.HostFace && _program.Bold;

        public bool Italic => _program.HostFace && _program.Italic;

        /// <summary>The text to write for one code of the font: the decoded text when its single
        /// character can name the code's glyph in this face, else a private-use character that does.
        /// A code drawing no glyph keeps its decoded text.</summary>
        public string TextFor(int code, string decoded)
        {
            if (_codeText.TryGetValue(code, out var known)) return known;
            var gid = _program.GlyphFor(code, decoded);
            var text = decoded;
            if (gid > 0)
            {
                // A glyph that paints is never written as whitespace: a producer mapping every letter
                // to a space would otherwise hand the space to a letter's glyph.
                if (decoded.Length == 1 && Writable(decoded[0])
                    && (!char.IsWhiteSpace(decoded[0]) || _program.IsBlank(gid))
                    && (!_charToGlyph.TryGetValue(decoded[0], out var held) || held == gid))
                    _charToGlyph[decoded[0]] = gid;
                else if (decoded.Length != 1 && decoded.Length != 0 && !IsOneSupplementaryChar(decoded))
                    return text; // several characters (a ligature): the text stays searchable
                else if (_program.IsBlank(gid))
                {
                    // A glyph with no outline paints nothing: its slot is the device's blank
                    // placeholder, which the face maps to that empty glyph so no other font draws it.
                    text = "";
                    _charToGlyph.TryAdd(BlankPlaceholder, gid);
                }
                else if (NextSubstitute() is { } substitute)
                {
                    _charToGlyph[substitute] = gid;
                    text = ((char)substitute).ToString();
                }
            }
            _codeText[code] = text;
            return text;
        }

        private static bool IsOneSupplementaryChar(string s) => s.Length == 2 && char.IsSurrogatePair(s[0], s[1]);

        // A character the face's format 4 table can carry and the XML text can hold, and that a
        // browser draws as a glyph of its own: a combining mark or a format character joins the
        // glyph before it and loses the position the run gives it.
        private static bool Writable(char ch) =>
            ch >= ' ' && !char.IsSurrogate(ch) && ch is not ('￾' or '￿' or 'ꢀ')
            && char.GetUnicodeCategory(ch) is not (System.Globalization.UnicodeCategory.NonSpacingMark
                or System.Globalization.UnicodeCategory.SpacingCombiningMark
                or System.Globalization.UnicodeCategory.EnclosingMark
                or System.Globalization.UnicodeCategory.Format);

        private int? NextSubstitute()
        {
            while (true)
            {
                if (_nextSubstitute > SubstituteLast && _nextSubstitute < PrivateUseFirst) _nextSubstitute = PrivateUseFirst;
                if (_nextSubstitute > PrivateUseLast) return null;
                if (!_charToGlyph.ContainsKey(_nextSubstitute)) return _nextSubstitute++;
                _nextSubstitute++;
            }
        }

        /// <summary>A TrueType subset holding the glyphs written, under exactly the written characters.</summary>
        public byte[]? BuildFont()
        {
            if (_charToGlyph.Count == 0) return null;
            try
            {
                var ttf = CffToTrueType.BuildSubset(_program.Source, _charToGlyph);
                return ttf is null ? null : CffToTrueType.MakeShippable(ttf);
            }
            catch
            {
                return null; // the run falls back to the generic family
            }
        }
    }
}
