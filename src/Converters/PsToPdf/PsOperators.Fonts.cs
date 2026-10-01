using System;
using System.Collections.Generic;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Font and text operators. A base-14 name becomes a non-embedded Type 1 font
/// referenced by name; any other name is resolved through the font library, and a
/// name nothing can satisfy paints no text at all — the behaviour the reference
/// converter shows for the corpus's unavailable faces.
/// </summary>
internal sealed partial class PsInterpreter
{
    /// <summary>The em a font's widths are expressed in.</summary>
    private const double FontUnitsPerEm = 1000.0;

    private readonly Dictionary<string, string?> _fontResourceNames = new(StringComparer.Ordinal);

    /// <summary>The dictionary each font resource name was written as, so a page that
    /// reuses the name can declare it again.</summary>
    private readonly Dictionary<string, PdfDictionary> _fontResources = new(StringComparer.Ordinal);

    /// <summary>The advance the glyph procedure now running declared, in glyph space.</summary>
    public double GlyphAdvance { get; set; }

    /// <summary>How many glyph procedures are running, so a font that shows text from
    /// inside its own glyph cannot recurse without end.</summary>
    public int GlyphDepth { get; set; }

    private void RegisterFontOperators()
    {
        Def("findfont", i => i.Push(i.FindFontByName(i.PopName())));
        Def("scalefont", ScaleFontOperator);
        Def("makefont", MakeFontOperator);
        Def("setfont", i => i.Graphics.State.Font = i.InstanceOf(i.Pop()));
        Def("currentfont", i => i.Push(CurrentFontValue(i)));
        Def("rootfont", i => i.Push(RootFontValue(i)));
        Def("selectfont", SelectFontOperator);
        Def("definefont", DefineFontOperator);
        Def("undefinefont", i => i.FontDirectory.Remove(PsValue.LiteralName(i.PopName())));
        Def("findencoding", i => i.Push(PsValue.Arr(
            i.PopName() == "ISOLatin1Encoding" ? PsEncodings.IsoLatin1Array() : PsEncodings.StandardArray())));
        Def("composefont", i =>
        {
            i.Pop();
            i.Pop();
            i.Push(PsValue.Dict(new PsDictionary()));
        });
        RegisterShowOperators();
        RegisterGlyphMetricOperators();
    }

    private void RegisterShowOperators()
    {
        Def("show", i => ShowString(i, i.PopString()));
        Def("ashow", AShowOperator);
        Def("widthshow", WidthShowOperator);
        Def("awidthshow", AWidthShowOperator);
        Def("kshow", KShowOperator);
        Def("cshow", i =>
        {
            var s = i.PopString();
            i.PopProc();
            ShowString(i, s);
        });
        Def("xshow", i => ShowWithDisplacements(i, readX: true, readY: false));
        Def("yshow", i => ShowWithDisplacements(i, readX: false, readY: true));
        Def("xyshow", i => ShowWithDisplacements(i, readX: true, readY: true));
        Def("glyphshow", GlyphShowOperator);
    }

    private void RegisterGlyphMetricOperators()
    {
        Def("stringwidth", StringWidthOperator);
        Def("charpath", CharPathOperator);
        Def("eocharpath", CharPathOperator);
        // A glyph procedure declares its own advance; the show loop needs it to know
        // where the next character starts.
        Def("setcachedevice", i =>
        {
            for (var k = 0; k < CacheDeviceOperands - 2; k++) i.PopNumber();
            i.PopNumber();
            i.GlyphAdvance = i.PopNumber();
        });
        Def("setcachedevice2", i =>
        {
            for (var k = 0; k < CacheDevice2Operands - 2; k++) i.PopNumber();
            i.PopNumber();
            i.GlyphAdvance = i.PopNumber();
        });
        Def("setcharwidth", i =>
        {
            i.PopNumber();
            i.GlyphAdvance = i.PopNumber();
        });
    }

    /// <summary>How many numbers <c>setcachedevice</c> takes.</summary>
    private const int CacheDeviceOperands = 6;

    /// <summary>How many numbers <c>setcachedevice2</c> takes.</summary>
    private const int CacheDevice2Operands = 10;

    /// <summary>Build the font object <c>findfont</c> hands out. A name the program
    /// has already defined wins; otherwise a dictionary is synthesised so that a
    /// program which reads <c>/FontMatrix</c> or <c>/Encoding</c> off it still works.</summary>
    public PsValue FindFontByName(string? name)
    {
        if (FontDirectory.TryGet(name ?? string.Empty, out var defined)) return defined;
        var dict = new PsDictionary();
        dict.Put("FontName", PsValue.LiteralName(name ?? string.Empty));
        dict.Put("FontType", PsValue.Int(1));
        dict.Put("FontMatrix", PsValue.Arr(MatrixToArray(null,
            PsMatrix.Scaling(1 / FontUnitsPerEm, 1 / FontUnitsPerEm))));
        dict.Put("Encoding", PsValue.Arr(PsEncodings.StandardArray()));
        dict.Put("PsToPdfResolved", PsValue.Bool(Fonts?.Knows(name) ?? PsFontLibrary.IsBase14(name)));
        dict.Put("CharStrings", PsValue.Dict(CharStringsOf(dict)));
        dict.Put("FontBBox", PsValue.Arr(BoxOf(name)));
        // The face this dictionary came from. A program routinely derives a font by
        // copying a found one and renaming it — a re-encoded slice of Helvetica is
        // still drawn with Helvetica's letters — and the copy carries this key along,
        // so the derived name does not have to resolve to a font file of its own.
        dict.Put(FaceKey, PsValue.LiteralName(name ?? string.Empty));
        var font = PsValue.Font(dict);
        FontDirectory.Put(name ?? string.Empty, font);
        return font;
    }

    /// <summary>The key a font dictionary records the face it was found under.</summary>
    public const string FaceKey = "PsToPdfFace";

    /// <summary>The box every glyph of a face fits inside, which a program copying a
    /// font carries over to the copy. A face the standard tables know reports its own;
    /// anything else reports nothing enclosed at all, which is what a font with no
    /// glyphs of its own would say.</summary>
    private static PsArray BoxOf(string? name)
    {
        var box = Aspose.Pdf.Text.Standard14Fonts.GetFontBBox(PsFontLibrary.NearestBase14(name));
        var array = new PsArray(BoxOperandCount);
        for (var k = 0; k < BoxOperandCount; k++)
            array[k] = PsValue.Int(box != null && k < box.Length ? box[k] : 0);
        return array;
    }

    /// <summary>The glyphs a face carries, keyed by name. A program sizes a dictionary
    /// of its own from how many there are before walking the encoding to measure them,
    /// so the entries a face's vector names are what this reports.</summary>
    private static PsDictionary CharStringsOf(PsDictionary font)
    {
        var strings = new PsDictionary(PsEncodings.CodeCount);
        if (!font.TryGet("Encoding", out var encoding)) return strings;
        for (var code = 0; code < PsEncodings.CodeCount; code++)
        {
            var name = PsEncodings.Lookup(encoding.AsArray, code);
            if (name == PsEncodings.NotDefined) continue;
            strings.Put(name, PsValue.LiteralName(name));
        }

        return strings;
    }

    /// <summary>Read a font operand back as an instance, keeping the scale a previous
    /// <c>scalefont</c> stored on the dictionary.</summary>
    private PsFontInstance InstanceOf(PsValue value)
    {
        var dict = value.AsDictionary;
        if (dict is null) throw new PsErrorSignal("typecheck");
        var name = dict.TryGet("FontName", out var n) ? n.Text : null;
        var face = dict.TryGet(FaceKey, out var f) ? f.Text : null;
        var matrix = dict.TryGet("PsToPdfScale", out var m) && m.AsArray != null
            ? MatrixFromArray(m.AsArray)
            : PsMatrix.Identity;
        var encoding = dict.TryGet("Encoding", out var e) ? e.AsArray : null;
        return new PsFontInstance(name, dict, matrix, encoding, face);
    }

    private static PsValue CurrentFontValue(PsInterpreter i)
    {
        var font = i.Graphics.State.Font;
        return font is null ? PsValue.Dict(new PsDictionary()) : PsValue.Font(font.Dict);
    }

    /// <summary>The font at the top of a composite's chain. A descendant's own glyph
    /// procedure commonly reaches back to the root for the drawing it shares with its
    /// siblings, so while one is running this must name the root and not the
    /// descendant that happens to be current.</summary>
    private static PsValue RootFontValue(PsInterpreter i) =>
        i.RootFont != null ? PsValue.Font(i.RootFont) : CurrentFontValue(i);

    /// <summary>The composite font whose descendant is drawing, or null when no
    /// composite is being shown.</summary>
    public PsDictionary? RootFont { get; set; }

    /// <summary><c>scalefont</c> and <c>makefont</c> both produce a NEW font whose
    /// scale is recorded on a copy of the dictionary, so the unscaled original stays
    /// usable — programs routinely scale one found font to several sizes.</summary>
    private static void ScaleFontOperator(PsInterpreter i)
    {
        var size = i.PopNumber();
        i.Push(ScaledFont(i, i.Pop(), PsMatrix.Scaling(size, size)));
    }

    private static void MakeFontOperator(PsInterpreter i)
    {
        var matrix = MatrixFromArray(i.PopArray());
        i.Push(ScaledFont(i, i.Pop(), matrix));
    }

    private static PsValue ScaledFont(PsInterpreter i, PsValue font, PsMatrix by)
    {
        var source = font.AsDictionary;
        if (source is null) throw new PsErrorSignal("typecheck");
        var copy = new PsDictionary(source.Count);
        foreach (var pair in source.Entries) copy.Put(pair.Key, pair.Value);
        var current = source.TryGet("PsToPdfScale", out var m) && m.AsArray != null
            ? MatrixFromArray(m.AsArray)
            : PsMatrix.Identity;
        copy.Put("PsToPdfScale", PsValue.Arr(MatrixToArray(null, by.Concat(current))));
        return PsValue.Font(copy);
    }

    private static void SelectFontOperator(PsInterpreter i)
    {
        var scale = i.Pop();
        var name = i.PopName();
        var font = i.FindFontByName(name);
        var matrix = scale.Type == PsType.Array
            ? MatrixFromArray(scale.AsArray)
            : PsMatrix.Scaling(scale.Number, scale.Number);
        i.Graphics.State.Font = i.InstanceOf(ScaledFont(i, font, matrix));
    }

    /// <summary><c>definefont</c> registers a dictionary under a name and hands back
    /// the font object, which is how a program installs a re-encoded face.</summary>
    private static void DefineFontOperator(PsInterpreter i)
    {
        var dict = i.PopDict();
        var key = i.Pop();
        if (!dict.TryGet("FontName", out _)) dict.Put("FontName", key);
        var font = PsValue.Font(dict);
        i.FontDirectory.Put(key, font);
        i.Push(font);
    }

    /// <summary>Paint a string at the current point and advance the point by the
    /// string's width, which is what every member of the show family does.</summary>
    private static void ShowString(PsInterpreter i, PsString text)
    {
        var g = i.Graphics;
        var font = g.State.Font;
        if (!g.TryGetCurrentPoint(out var x, out var y)) throw new PsErrorSignal("nocurrentpoint");
        if (font is null) throw new PsErrorSignal("invalidfont");
        if (PsCompositeFonts.Applies(font))
        {
            g.MoveTo(x + PsCompositeFonts.Show(i, font, text, x, y), y);
            return;
        }

        if (PsBuiltGlyphs.Applies(font))
        {
            g.MoveTo(x + PsBuiltGlyphs.Show(i, font, text, x, y), y);
            return;
        }

        g.MoveTo(x + PaintString(i, font, text, x, y), y);
    }

    /// <summary><c>kshow</c> shows a string one character at a time and runs a
    /// procedure BETWEEN each pair, handing it the two character codes. The procedure
    /// is free to change the state as it goes — the sample that fades a word in does
    /// exactly that, darkening the grey one letter at a time.</summary>
    private static void KShowOperator(PsInterpreter i)
    {
        var text = i.PopString();
        var proc = i.PopProc();
        for (var k = 0; k < text.Length; k++)
        {
            ShowString(i, new PsString(new[] { text[k] }));
            if (k + 1 >= text.Length) continue;
            i.Push(text[k]);
            i.Push(text[k + 1]);
            i.ExecuteProcedure(proc);
        }
    }

    /// <summary>Paint a string with a font that carries its own glyphs, at a point
    /// rather than at the current one, and report the width it covers. A composite
    /// font's descendants are drawn through here too, one character at a time.</summary>
    public static double PaintString(PsInterpreter i, PsFontInstance font, PsString text,
        double x, double y)
    {
        var g = i.Graphics;
        var width = i.MeasureString(font, text);
        var resource = g.Marking ? i.ResourceNameFor(font) : null;
        if (resource != null)
        {
            var placement = font.Matrix.Concat(PsMatrix.Translation(x, y)).Concat(g.State.Ctm);
            g.Writer.ShowText(NormaliseTextMatrix(placement, font.Size), resource,
                font.Size, text.ToArray(), g.State);
        }

        return width;
    }

    /// <summary>The text is written at a fixed size, so the size must be divided back
    /// out of the matrix that places it — otherwise the scale would apply twice.</summary>
    private static PsMatrix NormaliseTextMatrix(PsMatrix placement, double size)
    {
        if (size <= 0) return placement;
        return new PsMatrix(placement.A / size, placement.B / size,
            placement.C / size, placement.D / size, placement.E, placement.F);
    }

    /// <summary><c>ashow</c>: every character's advance is widened by the pair given,
    /// which is how a program spreads or tightens a line by hand.</summary>
    private static void AShowOperator(PsInterpreter i)
    {
        var text = i.PopString();
        var ay = i.PopNumber();
        var ax = i.PopNumber();
        ShowAdjusted(i, text, ax, ay, NoSelectedCharacter, 0, 0);
    }

    /// <summary><c>widthshow</c>: one chosen character's advance is widened, which is
    /// how a program justifies a line on its spaces.</summary>
    private static void WidthShowOperator(PsInterpreter i)
    {
        var text = i.PopString();
        var selected = i.PopInt();
        var cy = i.PopNumber();
        var cx = i.PopNumber();
        ShowAdjusted(i, text, 0, 0, selected, cx, cy);
    }

    /// <summary><c>awidthshow</c>: both widenings at once.</summary>
    private static void AWidthShowOperator(PsInterpreter i)
    {
        var text = i.PopString();
        var ay = i.PopNumber();
        var ax = i.PopNumber();
        var selected = i.PopInt();
        var cy = i.PopNumber();
        var cx = i.PopNumber();
        ShowAdjusted(i, text, ax, ay, selected, cx, cy);
    }

    /// <summary>The code no character matches, for a show that widens every advance
    /// and singles none out.</summary>
    private const int NoSelectedCharacter = -1;

    /// <summary>Show a string one character at a time, moving the point on by the
    /// widening the program asked for after each.</summary>
    private static void ShowAdjusted(PsInterpreter i, PsString text, double ax, double ay,
        int selected, double cx, double cy)
    {
        for (var k = 0; k < text.Length; k++)
        {
            ShowString(i, new PsString(new[] { text[k] }));
            var dx = ax;
            var dy = ay;
            if (text[k] == selected)
            {
                dx += cx;
                dy += cy;
            }

            if (dx == 0 && dy == 0) continue;
            if (!i.Graphics.TryGetCurrentPoint(out var x, out var y)) continue;
            i.Graphics.MoveTo(x + dx, y + dy);
        }
    }

    /// <summary>The displacement-driven show operators place each character at an
    /// offset the program supplies rather than at the font's own advance.</summary>
    private static void ShowWithDisplacements(PsInterpreter i, bool readX, bool readY)
    {
        // The displacements sit ABOVE the string: `(text) [ … ] xyshow`.
        var displacements = i.PopArray();
        var text = i.PopString();
        var perChar = (readX ? 1 : 0) + (readY ? 1 : 0);
        var g = i.Graphics;
        var had = g.TryGetCurrentPoint(out var startX, out var startY);
        for (var k = 0; k < text.Length; k++)
        {
            var one = new PsString(new[] { text[k] });
            if (!g.TryGetCurrentPoint(out var x, out var y)) throw new PsErrorSignal("nocurrentpoint");
            ShowString(i, one);
            var dx = ReadDisplacement(displacements, k * perChar, readX);
            var dy = ReadDisplacement(displacements, k * perChar + (readX ? 1 : 0), readY);
            g.MoveTo(x + dx, y + dy);
        }

        // The point is left where the show FOUND it. The displacements walk the
        // characters along, but they do not carry the point with them: a second
        // displacement show with no moveto between the two starts over from the same
        // place, which is where the reference conversion puts the notes of a stave.
        if (had) g.MoveTo(startX, startY);
    }

    private static double ReadDisplacement(PsArray array, int index, bool present)
    {
        if (!present || array is null || index < 0 || index >= array.Length) return 0;
        return array[index].IsNumber ? array[index].Number : 0;
    }

    /// <summary><c>glyphshow</c> names a glyph rather than giving a code, so the code
    /// its encoding maps to is looked up and shown.</summary>
    private static void GlyphShowOperator(PsInterpreter i)
    {
        var glyph = i.PopName();
        var font = i.Graphics.State.Font;
        var code = CodeForGlyph(font, glyph);
        if (code >= 0)
        {
            ShowString(i, new PsString(new[] { (byte)code }));
            return;
        }

        if (font != null) ShowUnencodedGlyph(i, font, glyph);
    }

    /// <summary>Show a glyph the current vector does not name. The operator names the
    /// glyph itself rather than a code, so the face is shown through a vector that
    /// carries the glyph at a code nothing else uses — a face's ornaments and its
    /// musical signs are reached no other way.</summary>
    private static void ShowUnencodedGlyph(PsInterpreter i, PsFontInstance font, string glyph)
    {
        var vector = new PsArray(PsEncodings.CodeCount);
        var code = PsEncodings.CodeCount - 1;
        for (var k = 0; k < PsEncodings.CodeCount; k++)
        {
            var name = PsEncodings.Lookup(font.Encoding, k);
            vector[k] = PsValue.LiteralName(name);
            if (name == PsEncodings.NotDefined && code == PsEncodings.CodeCount - 1) code = k;
        }

        vector[code] = PsValue.LiteralName(glyph);
        var saved = i.Graphics.State.Font;
        i.Graphics.State.Font = new PsFontInstance(font.Name, font.Dict, font.Matrix, vector, font.Face);
        try
        {
            ShowString(i, new PsString(new[] { (byte)code }));
        }
        finally
        {
            i.Graphics.State.Font = saved;
        }
    }

    private static int CodeForGlyph(PsFontInstance? font, string glyph)
    {
        for (var code = 0; code < PsEncodings.CodeCount; code++)
            if (PsEncodings.Lookup(font?.Encoding, code) == glyph)
                return code;
        return -1;
    }

    /// <summary><c>stringwidth</c> reports the advance the string would take, in user
    /// space, without painting anything.</summary>
    private static void StringWidthOperator(PsInterpreter i)
    {
        var text = i.PopString();
        var font = i.Graphics.State.Font;
        if (font is null) throw new PsErrorSignal("invalidfont");
        i.Push(i.MeasureString(font, text));
        i.Push(0);
    }

    /// <summary><c>charpath</c> appends the outline of a string to the current path,
    /// so the program can stroke, clip or fill the letter shapes itself. A face whose
    /// program cannot be found contributes no outline, only its advance.</summary>
    private static void CharPathOperator(PsInterpreter i)
    {
        i.PopBool();
        var text = i.PopString();
        var font = i.Graphics.State.Font;
        if (font is null) throw new PsErrorSignal("invalidfont");
        if (!i.Graphics.TryGetCurrentPoint(out var x, out var y))
            throw new PsErrorSignal("nocurrentpoint");
        // Unlike showing, this needs the letter SHAPES, so a standard face is outlined
        // too. The search folders come first; a standard face they do not hold is
        // outlined from the face the library already substitutes for that name when it
        // renders, which is what the reference conversion draws.
        var program = i.OutlineProgramFor(font.Face);
        if (program != null) AppendGlyphOutlines(i, font, text, program, x, y);
        i.Graphics.MoveTo(x + i.MeasureString(font, text), y);
    }

    /// <summary>Append each character's outline at its own pen position, mapped from
    /// the font's em into user space and then through the CTM.</summary>
    private static void AppendGlyphOutlines(PsInterpreter i, PsFontInstance font,
        PsString text, PsFontProgram program, double startX, double startY)
    {
        var pen = startX;
        var size = font.Size;
        // A picture face the search folders do not hold is outlined from a SUBSTITUTE,
        // and a substitute knows nothing of the missing face's glyph names: it is asked
        // for the CHARACTER the code stands for, and draws its .notdef box for one it
        // has not got, which is what the reference conversion strokes for Greek pi.
        var byCharacter = PsEncodings.IsSymbolic(font.Face) && i.IsSubstitutedFace(font.Face);
        // The reference seats the outline ABOVE the current point by the face's bounding-box
        // top over its Windows ascent, plus one user-space unit: measured 2026-09-07 on the
        // reference converter for five faces (Times New Roman Bold Italic 289/2048 em, Times
        // New Roman 304/2048, Courier New Bold 796/2048, Helvetica Bold 167/1000, Helvetica
        // Regular about 0), at sizes 10..200 (the unit term is 1.0 at every size) and under
        // `0.5 0.5 scale`, `2 2 scale` and `30 rotate` (a user-space vector, not a device one).
        var seat = program.CharPathSeatEm is double seatEm ? seatEm * size + CharPathSeatUserUnits : 0.0;
        var baseline = startY + seat;
        for (var k = 0; k < text.Length; k++)
        {
            var glyph = PsEncodings.Lookup(font.Encoding, text[k]);
            var character = byCharacter ? PsEncodings.SymbolicCharacter(font.Face, text[k]) : 0;
            var outline = byCharacter ? program.OutlineAtCharacter(character) : program.OutlineFor(glyph);
            if (outline != null)
            {
                var originX = pen;
                var scale = program.ToPdfUnits * size / FontUnitsPerEm;
                PsGlyphOutline.AppendToPath(i.Graphics.Path, outline, (gx, gy) =>
                {
                    i.Graphics.ToDevice(originX + gx * scale, baseline + gy * scale,
                        out var dx, out var dy);
                    return (dx, dy);
                });
            }

            // The pen walks by what the string MEASURES, not by what the face supplying
            // the shapes happens to advance: a standard face keeps its standard metrics
            // however its outlines are found, and the point the operator leaves behind is
            // measured the same way.
            pen += i.MeasureCode(font, text[k]);
        }
    }

    /// <summary>The user-space unit a charpath outline seats above the current point on
    /// top of the face's bounding-box-over-ascent term (see AppendGlyphOutlines).</summary>
    private const double CharPathSeatUserUnits = 1.0;

    /// <summary>The advance one code takes in user space, measured exactly as a whole
    /// string is.</summary>
    public double MeasureCode(PsFontInstance? font, byte code) =>
        MeasureString(font, new PsString(new[] { code }));

    /// <summary>The width of a string in user space, from the metrics of the face the
    /// name resolves to.</summary>
    public double MeasureString(PsFontInstance? font, PsString? text)
    {
        if (font is null || text is null) return 0;
        var program = PsFontLibrary.IsBase14(font.Face) && !PsEncodings.IsSymbolic(font.Face)
            ? null
            : ProgramFor(font.Face);
        var metrics = PsFontMetrics.For(PsFontLibrary.NearestBase14(font.Face));
        var total = 0.0;
        for (var k = 0; k < text.Length; k++)
        {
            var glyph = PsEncodings.Lookup(font.Encoding, text[k]);
            // The real program's advance when it has the glyph; the nearest standard
            // face's metric only as a fallback, so an unavailable face still advances.
            var width = program?.WidthFor(glyph) ?? -1;
            total += width >= 0 ? width : metrics.Width(glyph);
        }

        return total / FontUnitsPerEm * font.Advance;
    }

    /// <summary>The page-resource name a font is written under, allocating one on
    /// first use. A face nothing can satisfy gets no resource, and so paints nothing.
    /// A base-14 name is referenced by name; anything else is outlined into a Type 3
    /// font, which is what the reference converter writes and what keeps a
    /// re-encoded face correct — the encoding names the char procs directly.</summary>
    public string? ResourceNameFor(PsFontInstance? font)
    {
        if (font?.Name is null) return null;
        var key = font.Face + "\0" + EncodingKey(font.Encoding);
        if (_fontResourceNames.TryGetValue(key, out var existing))
        {
            // A resource belongs to ONE page, so a face first used on an earlier page
            // has to be written again on this one: the name is remembered, the entry
            // is not.
            if (existing != null && _fontResources.TryGetValue(key, out var written))
                Graphics.Writer.AddResource("Font", existing, written);
            return existing;
        }
        var dict = BuildResourceDictionary(font);
        if (dict is null)
        {
            _fontResourceNames[key] = null;
            return null;
        }

        var name = "F" + (_fontResourceCount++ + 1);
        _fontResourceNames[key] = name;
        _fontResources[key] = dict;
        Graphics.Writer.AddResource("Font", name, dict);
        return name;
    }

    private int _fontResourceCount;

    /// <summary>Two instances of one face may carry different encoding vectors, and
    /// each needs its own resource; the vector's glyph names identify it.</summary>
    private static string EncodingKey(PsArray? encoding)
    {
        if (encoding is null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        for (var code = 0; code < PsEncodings.CodeCount; code++)
            sb.Append(PsEncodings.Lookup(encoding, code)).Append('/');
        return sb.ToString();
    }

    /// <summary>The font dictionary for a face, or null when nothing can render it.</summary>
    private PdfDictionary? BuildResourceDictionary(PsFontInstance font)
    {
        // A picture face is outlined like any other: its codes stand for ornaments
        // that no substitute for the name would draw, so the letters have to come from
        // the program itself. Only when none can be found is the name referenced.
        var program = ProgramFor(font.Face);
        if (PsFontLibrary.IsBase14(font.Face) &&
            (program is null || !PsEncodings.IsSymbolic(font.Face)))
            return BuildFontDictionary(font);
        if (program is null) return null;
        return PsType3Font.Build(program, font.Encoding);
    }

    private readonly Dictionary<string, PsFontProgram?> _programs = new(StringComparer.Ordinal);

    /// <summary>The page-resource name each pattern was written under, or null when it
    /// could not be built.</summary>
    public Dictionary<PsDictionary, string?> PatternNames { get; } = new();

    /// <summary>The stream each pattern was written as, so a page that paints with a
    /// pattern first used on an earlier page can declare it again.</summary>
    public Dictionary<PsDictionary, PdfStream> PatternStreams { get; } = new();

    /// <summary>How many pattern cells are being built, so a cell that paints with
    /// itself cannot recurse without end.</summary>
    public int PatternDepth { get; set; }

    /// <summary>The program to take letter SHAPES from: the one the search folders
    /// hold, or, for a standard face they do not hold, the installed face the library
    /// substitutes for that name elsewhere. Only outlines use this — which faces are
    /// embedded, and what a string measures, stay decided by the inputs alone.</summary>
    public PsFontProgram? OutlineProgramFor(string? name)
    {
        var found = ProgramFor(name);
        if (found != null || string.IsNullOrEmpty(name)) return found;
        var standard = PsFontLibrary.NearestBase14(name);
        if (_substitutes.TryGetValue(standard, out var cached)) return cached;
        var data = Aspose.Pdf.Text.SystemFontResolver.Resolve(standard);
        var program = data is null ? null : PsFontProgram.FromBytes(data);
        _substitutes[standard] = program;
        return program;
    }

    private readonly Dictionary<string, PsFontProgram?> _substitutes = new(StringComparer.Ordinal);

    /// <summary>Whether a face's outlines can only come from a SUBSTITUTE — the search
    /// folders hold no program of that name.</summary>
    public bool IsSubstitutedFace(string? name) => ProgramFor(name) is null;

    /// <summary>The loaded program backing a face name, loaded once.</summary>
    public PsFontProgram? ProgramFor(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_programs.TryGetValue(name!, out var cached)) return cached;
        var path = Fonts?.FindProgram(name);
        var program = path is null ? null : PsFontProgram.Load(path);
        if (program != null) program.NamesArePositions = PsEncodings.IsSymbolic(name);
        _programs[name!] = program;
        return program;
    }

    /// <summary>A base-14 font is written by name with no descriptor and no embedded
    /// program, exactly as the reference converter writes it.</summary>
    private static PdfDictionary BuildFontDictionary(PsFontInstance font)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Font"));
        dict.Set("Subtype", new PdfName("Type1"));
        dict.Set("BaseFont", new PdfName(PsFontLibrary.NearestBase14(font.Face)));
        dict.Set("Encoding", BuildEncodingDictionary(font.Encoding));
        return dict;
    }

    /// <summary>The encoding written alongside a font: a Differences array naming
    /// every code the vector defines.</summary>
    private static PdfDictionary BuildEncodingDictionary(PsArray? encoding)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Encoding"));
        var differences = new List<PdfObject> { new PdfInteger(0) };
        for (var code = 0; code < PsEncodings.CodeCount; code++)
            differences.Add(new PdfName(PsEncodings.Lookup(encoding, code)));
        dict.Set("Differences", new PdfArray(differences));
        return dict;
    }
}
