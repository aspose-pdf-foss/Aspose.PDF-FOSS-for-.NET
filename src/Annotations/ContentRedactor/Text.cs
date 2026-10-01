using System.Globalization;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Annotations;

internal sealed partial class ContentRedactor
{
    // A font that states no ascent or descent is taken to reach this far above and below the
    // baseline, in ems.
    private const double DefaultAscent = 0.75;
    private const double DefaultDescent = -0.25;
    // Font descriptors state their ascent and descent in thousandths of an em.
    private const double GlyphUnits = 1000;

    /// <summary>A text-showing operation and the strings it shows, in order.</summary>
    private sealed class TextShow(Operation operation)
    {
        public readonly Operation Operation = operation;
        public readonly List<ShownString> Strings = new();
        public bool Removes => Strings.Any(s => s.Codes.Any(c => c.Removed));
    }

    private sealed class ShownString
    {
        public byte[] Bytes = [];
        /// <summary>Per code: the byte offset just past it, its advance in text space, and
        /// whether it lies in the area.</summary>
        public readonly List<(int ByteEnd, double Advance, bool Removed)> Codes = new();
        public double FontSize;
        public double Scaling = 1;
        public bool Vertical;
    }

    /// <summary>A font's reach above and below the baseline, in ems, and whether it writes
    /// down the page.</summary>
    private readonly record struct FontShape(double Ascent, double Descent, bool Vertical);

    private readonly Dictionary<PdfDictionary, FontShape> _fontShapes = new();

    private void MarkCodes(Walk walk, IReadOnlyList<(int ByteEnd, double Advance)> ends, GraphicsState state,
        Dictionary<string, PdfDictionary> fonts)
    {
        if (walk.Text is not { Strings.Count: > 0 } show) return;
        var shown = show.Strings[show.Strings.Count - 1];
        var font = state.FontName is { } name && fonts.TryGetValue(name, out var fd) ? ShapeOf(fd) : Unknown;
        shown.FontSize = state.FontSize;
        shown.Scaling = state.HorizontalScaling / 100.0;
        shown.Vertical = font.Vertical;

        var m = GraphicsState.MultiplyMatrices(state.TextMatrix, state.Ctm);
        // The area in text space, where each glyph's box is upright; none when the text collapses.
        var area = AreaIn(m);
        var size = state.FontSize;
        var low = state.Rise + font.Descent * size;
        var high = state.Rise + font.Ascent * size;
        var previous = 0.0;
        foreach (var (byteEnd, advance) in ends)
        {
            // A glyph goes when the middle of its box lies in the area: one mostly outside it stays
            // readable beside the box anyway, and a font's ascent reaches well above its letters, so
            // a box grazing the line above or below must not take it.
            var middle = font.Vertical ? (0.0, -(previous + advance) / 2) : ((previous + advance) / 2, (low + high) / 2);
            var removed = area is not null && area.Contains(middle.Item1, middle.Item2);
            shown.Codes.Add((byteEnd, advance - previous, removed));
            previous = advance;
        }
    }

    private static readonly FontShape Unknown = new(DefaultAscent, DefaultDescent, false);

    private FontShape ShapeOf(PdfDictionary font)
    {
        if (_fontShapes.TryGetValue(font, out var known)) return known;
        var descriptor = _reader.ResolveDict(font.Get("FontDescriptor"));
        if (descriptor is null && _reader.Resolve(font.Get("DescendantFonts")) is PdfArray { Count: > 0 } descendants)
            descriptor = _reader.ResolveDict(_reader.ResolveDict(descendants[0])?.Get("FontDescriptor"));
        var ascent = Metric(descriptor, "Ascent") is > 0 and var a ? a / GlyphUnits : DefaultAscent;
        var descent = Metric(descriptor, "Descent") is < 0 and var d ? d / GlyphUnits : DefaultDescent;
        var vertical = font.GetName("Subtype") == "Type0" && Text.CidFontInfo.TryBuild(font, _reader) is { IsVertical: true };
        return _fontShapes[font] = new FontShape(ascent, descent, vertical);
    }

    private double Metric(PdfDictionary? descriptor, string key) => _reader.Resolve(descriptor?.Get(key)) switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    /// <summary>One piece of a rewritten show: kept codes (hex), or a pen move in TJ thousandths -
    /// an adjustment the show had, or the advance of removed codes (a gap).</summary>
    private readonly record struct Piece(string? Codes, double Move, bool Gap);

    /// <summary>The show written again as TJ operations, each run of removed codes replaced by a
    /// TJ of its own that shows nothing and moves the pen as far as they did - so every kept glyph
    /// stays where it was, and the gap reads as one when the text is extracted ("a:b" with the colon
    /// removed reads "a b", not "ab"). The show's own adjustments beside a gap join it: no kept
    /// piece starts with an adjustment. A ' or " keeps its line move (and the spacings a " sets).
    /// A gap the show ends with comes back apart (<c>TrailingGap</c>): it moves nothing the page shows
    /// when the next text is placed anew, and then it goes.</summary>
    private static (string Kept, string? TrailingGap) RewriteShow(TextShow show)
    {
        var op = show.Operation;
        var sb = new StringBuilder();
        if (op.Name == "\"" && op.Operands.Count >= 3)
            sb.Append(Operand(op.Operands[0])).Append(" Tw ").Append(Operand(op.Operands[1])).Append(" Tc ");
        if (op.Name is "'" or "\"") sb.Append("T* ");

        var elements = op.Name switch
        {
            "TJ" when op.Operands.Count > 0 && op.Operands[0] is PdfArray array => array.ToList(),
            "\"" when op.Operands.Count >= 3 => [op.Operands[2]],
            _ when op.Operands.Count > 0 => [op.Operands[op.Operands.Count - 1]],
            _ => new List<PdfObject>(),
        };
        // A show whose strings the parse did not report one by one is dropped whole.
        if (elements.Count(e => e is PdfString) != show.Strings.Count) return (sb.ToString().TrimEnd(), null);

        var pieces = new List<Piece>();
        var next = 0;
        foreach (var element in elements)
        {
            if (element is PdfString) AddKept(pieces, show.Strings[next++]);
            else if (element is PdfInteger or PdfReal) pieces.Add(new Piece(null, Value(element), Gap: false));
        }
        var merged = MergeIntoGaps(pieces);
        string? trailing = null;
        if (merged.Count > 0 && merged[merged.Count - 1] is { Gap: true } last)
        {
            merged.RemoveAt(merged.Count - 1);
            if (last.Move != 0) trailing = "[" + Number(last.Move) + "] TJ";
        }
        AppendPieces(sb, merged);
        return (sb.ToString().Trim(), trailing);
    }

    /// <summary>A string's codes as pieces: runs of kept codes, and a gap for each run of removed ones.</summary>
    private static void AddKept(List<Piece> pieces, ShownString s)
    {
        var from = 0;
        var removedAdvance = 0.0;
        var codeStart = 0;
        foreach (var (byteEnd, advance, removed) in s.Codes)
        {
            if (removed)
            {
                if (codeStart > from) pieces.Add(new Piece(Hex(s.Bytes, from, codeStart), 0, Gap: false));
                removedAdvance += advance;
                from = byteEnd;
            }
            else if (removedAdvance != 0)
            {
                pieces.Add(new Piece(null, Adjustment(s, removedAdvance), Gap: true));
                removedAdvance = 0;
            }
            codeStart = byteEnd;
        }
        if (s.Bytes.Length > from) pieces.Add(new Piece(Hex(s.Bytes, from, s.Bytes.Length), 0, Gap: false));
        if (removedAdvance != 0) pieces.Add(new Piece(null, Adjustment(s, removedAdvance), Gap: true));
    }

    /// <summary>Adjustments touching a gap, and gaps touching each other, become one gap.</summary>
    private static List<Piece> MergeIntoGaps(List<Piece> pieces)
    {
        var merged = new List<Piece>();
        foreach (var piece in pieces)
        {
            var last = merged.Count > 0 ? merged[merged.Count - 1] : (Piece?)null;
            var isMove = piece.Codes is null;
            if (isMove && last is { Codes: null } previous && (piece.Gap || previous.Gap))
                merged[merged.Count - 1] = new Piece(null, previous.Move + piece.Move, Gap: true);
            else
                merged.Add(piece);
        }
        return merged;
    }

    /// <summary>Kept codes and their adjustments as one TJ per run between gaps; each gap as a TJ of
    /// its own.</summary>
    private static void AppendPieces(StringBuilder sb, List<Piece> pieces)
    {
        var open = false;
        foreach (var piece in pieces)
        {
            if (piece.Gap)
            {
                if (open) sb.Append("] TJ\n");
                open = false;
                if (piece.Move != 0) sb.Append('[').Append(Number(piece.Move)).Append("] TJ\n");
                continue;
            }
            if (!open) sb.Append('[');
            open = true;
            sb.Append(piece.Codes is { } codes ? "<" + codes + ">" : Number(piece.Move)).Append(' ');
        }
        if (open) sb.Append("] TJ");
    }

    /// <summary>The TJ number moving the pen on by <paramref name="advance"/> (text space): along
    /// the line it is scaled by the size and Tz and counts backwards; down a vertical line, by the
    /// size alone and forwards. Zero when the size is zero (nothing can move then).</summary>
    private static double Adjustment(ShownString s, double advance)
    {
        var unit = s.Vertical ? s.FontSize : s.FontSize * s.Scaling;
        if (Math.Abs(unit) < Singular) return 0;
        var thousandths = advance * GlyphUnits / unit;
        return s.Vertical ? thousandths : -thousandths;
    }

    private static double Value(PdfObject value) => value switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    private static string Operand(PdfObject value) => value switch
    {
        PdfInteger i => i.Value.ToString(CultureInfo.InvariantCulture),
        PdfReal r => Number(r.Value),
        _ => "0",
    };

    private static string Hex(byte[] bytes, int from, int to)
    {
        var sb = new StringBuilder((to - from) * 2);
        for (var i = from; i < to; i++) sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
