using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Tagged;

/// <summary>
/// Splits a text-showing operation that is only partly covered by a link annotation, so the
/// linked words become operations of their own that a Link element can own while the words
/// around them stay with their paragraph. The split falls between two character codes: the
/// pieces are shown one after the other, so the pen and every glyph land where they did
/// (a show advances by its codes' widths plus Tc and Tw, and TJ adjustments stay between
/// the codes they separated).
/// </summary>
internal static class LinkTextSplit
{
    // A code belongs to a link when its centre lies inside the link's rectangle, its baseline
    // allowed this share of the size below the rectangle (a rectangle drawn round the glyphs'
    // x-height and ascent starts above their descent).
    private const double BaselineSlackEms = 0.35;
    // A TJ adjustment moving the pen on by at least this many thousandths of an em parts two
    // words as a space does (a table row written as one string, its cells spaced by adjustments).
    private const double WordGapAdjustment = 1000;
    // A TJ adjustment moving the pen on by at least this many thousandths of an em - a space's width - parts two
    // words of a row written as one string (kerning moves it by far less); the cells of a table set so stand but a
    // space apart.
    private const double SpaceAdjustment = 200;
    // The pieces cut off as labels are numbered from here, apart from the links' pieces.
    private const int LeadLinks = 1 << 20;
    // The pieces cut apart on lines whose shows reach into one another are numbered from here.
    private const int ApartLinks = 1 << 21;
    // On such a line a TJ adjustment of at least this many thousandths of an em, either way, places a glyph apart (kerning
    // moves the pen less).
    private const double ApartAdjustment = 250;

    private sealed class ShownString
    {
        public byte[] Bytes = [];
        /// <summary>The decoded text, when it has one character per code (else empty).</summary>
        public string Text = string.Empty;
        /// <summary>Per code: the byte offset just past it, and the link it falls in (-1: none).</summary>
        public List<(int ByteEnd, int Link)> Codes = new();
        /// <summary>The TJ adjustment standing before the string (0: none), in thousandths of an em.</summary>
        public double AdjustmentBefore;
    }

    private sealed class TextOp
    {
        public int Start;
        public int End;
        public string Name = string.Empty;
        public List<PdfObject> Operands = new();
        public List<ShownString> Strings = new();
        // Cutting apart: the piece its codes have reached, and whether the last code was a blank.
        public int Piece;
        public bool Blank;
    }

    private sealed class Capture(List<TextOp> ops) : IInstructionHandler
    {
        public TextOp? Current;

        public bool Handled(string instruction, IReadOnlyList<PdfObject> operands, GraphicsState state)
        {
            if (instruction is "Tj" or "TJ" or "'" or "\"")
            {
                Current = new TextOp { Name = instruction, Operands = operands.ToList() };
                ops.Add(Current);
            }
            else
            {
                Current = null;
            }
            return false;
        }
    }

    /// <summary>Split the page's text shows at the edges of <paramref name="linkRects"/>;
    /// true when the page's content changed.</summary>
    public static bool Apply(Page page, IReadOnlyList<Rectangle> linkRects) => Apply(page, linkRects, []);

    /// <summary>Split the page's text shows at the edges of <paramref name="columns"/>, each word going whole to the
    /// column its middle stands in (<see cref="SnapWords"/>): an edge standing inside a figure cuts nothing.</summary>
    public static bool ApplyToColumns(Page page, IReadOnlyList<Rectangle> columns) => Apply(page, columns, [], [], snapWords: true);

    /// <summary>Split the page's text shows at the edges of <paramref name="linkRects"/>, and after the label each of
    /// <paramref name="leads"/> names: a show whose first code stands in the lead's rectangle, its text starting with
    /// the lead's label, is cut after the label. True when the page's content changed.</summary>
    public static bool Apply(Page page, IReadOnlyList<Rectangle> linkRects, IReadOnlyList<(Rectangle At, string Label)> leads)
        => Apply(page, linkRects, leads, []);

    /// <summary>As <see cref="Apply(Page, IReadOnlyList{Rectangle}, IReadOnlyList{ValueTuple{Rectangle, string}})"/>, and a
    /// show whose codes stand in one of <paramref name="apart"/> is cut into its words, its blanks and the strings of a
    /// TJ array placed apart by adjustments - each then shown where its glyphs stand.</summary>
    public static bool Apply(Page page, IReadOnlyList<Rectangle> linkRects, IReadOnlyList<(Rectangle At, string Label)> leads,
        IReadOnlyList<Rectangle> apart, bool snapWords = false)
    {
        if (linkRects.Count == 0 && leads.Count == 0 && apart.Count == 0) return false;
        var reader = page.Reader;
        var bytes = page.GetContentStreamBytes();
        if (bytes is null || bytes.Length == 0) return false;

        PdfDictionary? resources = null;
        for (var node = page.Dict; node is not null && resources is null; node = reader.ResolveDict(node.Get("Parent")))
            resources = reader.ResolveDict(node.Get("Resources"));
        var fonts = new Dictionary<string, PdfDictionary>();
        if (resources is not null && reader.ResolveDict(resources.Get("Font")) is { } fontDict)
            foreach (var key in fontDict.Keys)
                if (reader.ResolveDict(fontDict.Get(key)) is { } fd)
                    fonts[key] = fd;

        var ops = new List<TextOp>();
        var capture = new Capture(ops);
        var parser = new ContentStreamParser(reader) { Handler = capture };
        var prevEnd = 0;
        ShownString? shown = null;
        var decoder = new ShownTextDecoder(reader, fonts);
        parser.OnTextShown += (text, shownBytes, state) =>
        {
            shown = new ShownString { Bytes = shownBytes, Text = decoder.Decode(state.FontName, shownBytes, text) };
            if (capture.Current is { } current)
            {
                shown.AdjustmentBefore = AdjustmentBefore(current, current.Strings.Count);
                if (Math.Abs(shown.AdjustmentBefore) >= ApartAdjustment) current.Piece++;
                current.Strings.Add(shown);
            }
        };
        parser.OnTextCodeAdvances += (ends, state) =>
        {
            if (shown is null) return;
            var m = GraphicsState.MultiplyMatrices(state.TextMatrix, state.Ctm);
            var size = state.FontSize * Math.Sqrt(m[2] * m[2] + m[3] * m[3]);
            var previous = 0.0;
            foreach (var (byteEnd, advance) in ends)
            {
                var centre = (previous + advance) / 2;
                previous = advance;
                var x = m[0] * centre + m[4];
                var y = m[1] * centre + m[5];
                var link = LinkAt(linkRects, x, y, size);
                if (link < 0 && capture.Current is { } op && LinkAt(apart, x, y, size) >= 0)
                {
                    var index = shown.Codes.Count;
                    var blank = index < shown.Text.Length && char.IsWhiteSpace(shown.Text[index]);
                    if (blank != op.Blank) (op.Piece, op.Blank) = (op.Piece + 1, blank);
                    // (glyphs spaced a quarter em or more apart by the character spacing: each placed apart)
                    else if (index > 0 && state.FontSize > 0 && state.CharSpacing / state.FontSize * 1000 >= ApartAdjustment) op.Piece++;
                    link = ApartLinks + op.Piece;
                }
                shown.Codes.Add((byteEnd, link));
            }
            if (shown.Text.Length != shown.Codes.Count) shown.Text = string.Empty;
            // A label opening the operation's first string: its codes are a piece of their own.
            if (shown.Text.Length == 0 || shown.Codes.Count == 0 || capture.Current is not { Strings.Count: 1 }) return;
            var spaces = shown.Text.Length - shown.Text.TrimStart().Length;
            var (fx, fy) = (m[0] * (ends[0].Advance / 2) + m[4], m[1] * (ends[0].Advance / 2) + m[5]);
            for (var k = 0; k < leads.Count; k++)
            {
                var (at, label) = leads[k];
                if (fx < at.LLX || fx > at.URX || fy < at.LLY - BaselineSlackEms * size || fy > at.URY) continue;
                if (!shown.Text.AsSpan(spaces).StartsWith(label.AsSpan(), StringComparison.Ordinal) || spaces + label.Length > shown.Codes.Count) continue;
                // (the blanks after the label are its: the text starts where its glyphs do)
                var through = spaces + label.Length;
                while (through < shown.Codes.Count && char.IsWhiteSpace(shown.Text[through])) through++;
                for (var c = 0; c < through; c++)
                    shown.Codes[c] = (shown.Codes[c].ByteEnd, LeadLinks + k);
                break;
            }
        };
        parser.OnInlineImageEnd += () => prevEnd = (int)Math.Min(parser.OperatorEnd, bytes.Length);
        parser.OnOperator += (_, _, _) =>
        {
            var end = (int)Math.Min(parser.OperatorEnd, bytes.Length);
            if (capture.Current is { } op)
            {
                op.Start = prevEnd;
                op.End = end;
            }
            capture.Current = null;
            shown = null;
            prevEnd = end;
        };
        try
        {
            parser.Parse(bytes, fonts, properties: resources is not null ? reader.ResolveDict(resources.Get("Properties")) : null);
        }
        catch
        {
            return false;
        }

        if (snapWords) foreach (var op in ops) SnapWords(op);
        var split = ops.Where(o => o.End > o.Start && o.Strings.SelectMany(s => s.Codes).Select(c => c.Link).Distinct().Count() > 1
                && CutsBetweenWords(o)).ToList();
        if (split.Count == 0) return false;

        using var output = new System.IO.MemoryStream();
        var at = 0;
        foreach (var op in split)
        {
            if (op.Start < at) continue;
            output.Write(bytes, at, op.Start - at);
            var replacement = Encoding.ASCII.GetBytes("\n" + Rewrite(op) + "\n");
            output.Write(replacement, 0, replacement.Length);
            at = op.End;
        }
        output.Write(bytes, at, bytes.Length - at);
        page.SetContentStream(output.ToArray());
        return true;
    }

    /// <summary>Each word of the operation - a run of codes with no blank among them and no word gap before the strings
    /// they stand in (a figure set glyph by glyph, each glyph a string with its kerning) - given the rectangle its middle
    /// code falls in: a column edge standing inside a figure (a stray rule's, or between two columns found) cuts no
    /// figure, which goes whole to the column most of it stands in.</summary>
    private static void SnapWords(TextOp op)
    {
        var word = new List<(ShownString String, int Code)>();
        void Snap()
        {
            if (word.Count > 1)
            {
                var (middle, at) = word[word.Count / 2];
                var link = middle.Codes[at].Link;
                foreach (var (s, i) in word) s.Codes[i] = (s.Codes[i].ByteEnd, link);
            }
            word.Clear();
        }
        foreach (var s in op.Strings)
        {
            if (Math.Abs(s.AdjustmentBefore) >= SpaceAdjustment) Snap();
            // (a string whose codes are not read as characters tells no word: each code keeps the rectangle it fell in)
            if (s.Text.Length != s.Codes.Count) { Snap(); continue; }
            for (var i = 0; i < s.Codes.Count; i++)
            {
                if (char.IsWhiteSpace(s.Text[i])) { Snap(); continue; }
                word.Add((s, i));
            }
        }
        Snap();
    }

    /// <summary>Whether every place the operation's link changes falls between words: a link
    /// rectangle that ends inside a word is placed wrongly, and cutting there would part the
    /// word's letters. Such an operation stays whole (its link then takes it or leaves it).</summary>
    private static bool CutsBetweenWords(TextOp op)
    {
        char? previousChar = null;
        int? previousLink = null;
        foreach (var s in op.Strings)
        {
            for (var i = 0; i < s.Codes.Count; i++)
            {
                char? c = s.Text.Length > 0 ? s.Text[i] : null;
                var link = s.Codes[i].Link;
                // (the pen moved on a word's gap, or placed a glyph apart on a line cut apart)
                var spaced = i == 0 && Math.Abs(s.AdjustmentBefore) >= (link >= ApartLinks ? ApartAdjustment : WordGapAdjustment);
                if (previousLink is { } before && before != link && !spaced
                    && (c is null || previousChar is null || (char.IsLetterOrDigit(c.Value) && char.IsLetterOrDigit(previousChar.Value))))
                    return false;
                previousChar = c;
                previousLink = link;
            }
        }
        return true;
    }

    /// <summary>The adjustment a TJ array holds right before its string number <paramref name="index"/>
    /// (0 for Tj and the other shows, and for a string with no number before it).</summary>
    private static double AdjustmentBefore(TextOp op, int index)
    {
        if (op.Name != "TJ" || op.Operands.Count == 0 || op.Operands[0] is not PdfArray array) return 0;
        var strings = -1;
        double pending = 0;
        foreach (var item in array)
        {
            if (item is PdfString)
            {
                if (++strings == index) return pending;
                pending = 0;
            }
            else if (item is PdfInteger n) pending += n.Value;
            else if (item is PdfReal r) pending += r.Value;
        }
        return 0;
    }

    private static int LinkAt(IReadOnlyList<Rectangle> rects, double x, double y, double size)
    {
        for (var i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            if (x >= r.LLX && x <= r.URX && y >= r.LLY - BaselineSlackEms * size && y <= r.URY) return i;
        }
        return -1;
    }

    /// <summary>The operation as shows of pieces that each lie in one link, or outside all.</summary>
    private static string Rewrite(TextOp op)
    {
        var sb = new StringBuilder();
        if (op.Name == "\"" && op.Operands.Count >= 3)
            sb.Append(Number(op.Operands[0])).Append(" Tw ").Append(Number(op.Operands[1])).Append(" Tc ");
        if (op.Name is "'" or "\"") sb.Append("T* ");

        if (op.Name != "TJ")
        {
            foreach (var piece in Pieces(op.Strings[0]))
                sb.Append('<').Append(Hex(piece)).Append("> Tj ");
            return sb.ToString().TrimEnd();
        }

        // TJ: a new array wherever the link changes; adjustments stay with the codes before them - but between glyphs cut
        // apart they move the pen in an array of their own (a piece holding a step back would run backwards: no text reads it).
        var array = new StringBuilder();
        var adjustments = new StringBuilder();
        int? link = null;
        var strings = 0;
        void Close()
        {
            if (array.Length > 0) sb.Append('[').Append(array.ToString().TrimEnd()).Append("] TJ ");
            array.Clear();
        }
        void Adjust()
        {
            array.Append(adjustments);
            adjustments.Clear();
        }
        var elements = op.Operands.Count > 0 && op.Operands[0] is PdfArray arr ? arr.ToList() : new List<PdfObject>();
        foreach (var operand in elements)
        {
            if (operand is not PdfString || strings >= op.Strings.Count)
            {
                if (operand is PdfInteger or PdfReal) adjustments.Append(Number(operand)).Append(' ');
                continue;
            }
            var s = op.Strings[strings++];
            var from = 0;
            var codeStart = 0;
            foreach (var (byteEnd, codeLink) in s.Codes)
            {
                if (link is { } current && codeLink != current)
                {
                    if (codeStart > from) array.Append('<').Append(Hex(s.Bytes, from, codeStart)).Append("> ");
                    if (codeLink < ApartLinks && current < ApartLinks) Adjust();
                    Close();
                    Adjust();
                    Close();
                    from = codeStart;
                }
                Adjust();
                link = codeLink;
                codeStart = byteEnd;
            }
            Adjust();
            array.Append('<').Append(Hex(s.Bytes, from, s.Bytes.Length)).Append("> ");
        }
        Adjust();
        Close();
        return sb.ToString().TrimEnd();
    }

    /// <summary>The string cut where the link its codes fall in changes.</summary>
    private static IEnumerable<byte[]> Pieces(ShownString s)
    {
        var from = 0;
        var previousEnd = 0;
        int? link = null;
        foreach (var (byteEnd, codeLink) in s.Codes)
        {
            if (link is { } current && codeLink != current)
            {
                yield return Slice(s.Bytes, from, previousEnd);
                from = previousEnd;
            }
            link = codeLink;
            previousEnd = byteEnd;
        }
        yield return Slice(s.Bytes, from, s.Bytes.Length);
    }

    private static byte[] Slice(byte[] bytes, int from, int to)
    {
        var result = new byte[Math.Max(to - from, 0)];
        Array.Copy(bytes, from, result, 0, result.Length);
        return result;
    }

    private static string Hex(byte[] bytes) => Hex(bytes, 0, bytes.Length);

    private static string Hex(byte[] bytes, int from, int to)
    {
        var sb = new StringBuilder((to - from) * 2);
        for (var i = from; i < to; i++) sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static string Number(PdfObject value) => value switch
    {
        PdfInteger i => i.Value.ToString(CultureInfo.InvariantCulture),
        PdfReal r => r.Value.ToString("0.######", CultureInfo.InvariantCulture),
        _ => "0",
    };
}
