using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Slide the text that follows a replacement along its own baseline.
    ///
    /// A run that changed width moves nothing but its own pen: the next run is seated
    /// either by an absolute <c>Tm</c> or by a <c>Td</c> off the line matrix, and neither
    /// knows the run before it grew. Widening a replacement therefore draws it straight
    /// over the next word unless every later seat on that baseline is moved by the
    /// same amount.
    ///
    /// Only seats on <paramref name="baselineY"/> at or beyond <paramref name="fromX"/>
    /// and written after <paramref name="fromOffset"/> move, and only inside the text
    /// object the run was rewritten in: the CHAIN of relative seats that continues the
    /// run. A <c>Td</c> is relative, so one patched <c>Td</c> carries the chain after it: a
    /// later <c>Td</c> that leaves the line takes the shift back out, and a
    /// <c>T*</c>/<c>'</c>/<c>"</c> gets the line matrix restored in front of it, so the lines
    /// below keep their positions exactly. An absolute <c>Tm</c> states its seat outright
    /// and is left where the producer put it (the reference keeps such runs in place).
    /// <paramref name="carried"/> says by how much the line matrix at
    /// <paramref name="fromOffset"/> already differs from what the stream states (a
    /// re-anchored suffix moved it); the walk takes that out at the first chain seat.
    /// <paramref name="absoluteSeats"/> is the older contract of the space-width adjustment:
    /// every plain <c>1 0 0 1 x y Tm</c> on the baseline anywhere in the stream moves and
    /// nothing else does.</summary>
    internal static byte[] ShiftFollowingBlocks(byte[] streamBytes, double baselineY,
        double fromX, double delta, int fromOffset = 0, double carried = 0, bool absoluteSeats = false)
    {
        if ((delta == 0 && carried == 0) || streamBytes.Length == 0) return streamBytes;
        var fs = new FollowerShiftState
        {
            bytes = streamBytes, baselineY = baselineY, fromX = fromX, delta = delta,
            fromOffset = fromOffset, carried = carried, absoluteSeats = absoluteSeats,
            result = new MemoryStream(streamBytes.Length + RewriteHeadroom),
        };
        var tokens = fs.tokens;
        var i = 0;
        while (i < streamBytes.Length && !fs.done)
        {
            var c = streamBytes[i];
            if (c is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or 0) { i++; continue; }
            if (fs.opStart < 0) fs.opStart = i;
            // A string or dictionary operand can hold anything, including the letters
            // "Tm" — step over it rather than tokenising its contents.
            if (c is (byte)'(' or (byte)'<' or (byte)'[' or (byte)'/')
            {
                i = SkipOperand(streamBytes, i);
                tokens.Clear();
                continue;
            }
            var start = i;
            while (i < streamBytes.Length && !IsDelimiter(streamBytes[i])) i++;
            if (i == start) { i++; continue; }
            // Inline image data is binary: step over it as a whole.
            if (i - start == 2 && streamBytes[start] == (byte)'I' && streamBytes[start + 1] == (byte)'D')
            {
                i = SkipInlineImageData(streamBytes, i);
                tokens.Clear();
                fs.opStart = -1;
                continue;
            }
            if (IsPositioningKeyword(streamBytes, start, i))
            {
                ApplyPositioningKeyword(fs, Encoding.ASCII.GetString(streamBytes, start, i - start), live: start >= fromOffset);
                tokens.Clear();
                fs.opStart = -1;
                continue;
            }
            // Any other keyword ends its operands; a number never starts with a letter.
            if (streamBytes[start] is >= (byte)'A' and <= (byte)'z') { tokens.Clear(); fs.opStart = -1; continue; }
            tokens.Add((start, i));
            if (tokens.Count > 8) tokens.RemoveAt(0);
        }
        if (!fs.changed) return streamBytes;
        fs.result.Write(streamBytes, fs.lastWritePos, streamBytes.Length - fs.lastWritePos);
        return fs.result.ToArray();
    }

    private sealed class FollowerShiftState
    {
        public byte[] bytes = null!;
        public double baselineY, fromX, delta, carried;
        public int fromOffset;
        public bool absoluteSeats;
        public MemoryStream result = null!;
        public int lastWritePos;
        public bool changed;
        /// <summary>The text object of the shift has ended: nothing after it moves.</summary>
        public bool done;
        public int opStart = -1;
        public List<(int start, int end)> tokens = new(8);
        public double tmA = 1, tmB, tmC, tmD = 1, tlmX, tlmY, leading;
    }

    private static bool IsPositioningKeyword(byte[] b, int start, int end)
    {
        var len = end - start;
        if (len == 1) return b[start] is (byte)'\'' or (byte)'"';
        if (len != 2) return false;
        if (b[start] is (byte)'B' or (byte)'E' && b[start + 1] == (byte)'T') return true;
        if (b[start] != (byte)'T') return false;
        return b[start + 1] is (byte)'m' or (byte)'d' or (byte)'D' or (byte)'L' or (byte)'*';
    }

    /// <summary>One positioning operator applied to the walk: the line matrix is tracked
    /// from the operands AS WRITTEN, and the seat is patched when it has to move. Before
    /// the shift's offset (<paramref name="live"/> false) only the matrix is tracked: the
    /// carried amount describes the line matrix AT that offset, whatever set it.</summary>
    private static void ApplyPositioningKeyword(FollowerShiftState fs, string op, bool live)
    {
        var t = fs.tokens;
        switch (op)
        {
            case "ET":
                if (live && !fs.absoluteSeats) fs.done = true;
                break;
            case "BT":
                fs.tmA = 1; fs.tmB = 0; fs.tmC = 0; fs.tmD = 1; fs.tlmX = 0; fs.tlmY = 0; fs.leading = 0;
                if (live && !fs.absoluteSeats) fs.done = true;
                break;
            case "TL":
                if (t.Count >= 1 && TryNum(fs.bytes, t[^1]) is { } tl) fs.leading = tl;
                break;
            case "Tm":
                if (t.Count < 6) break;
                if (TryNum(fs.bytes, t[^6]) is not { } a || TryNum(fs.bytes, t[^5]) is not { } b
                    || TryNum(fs.bytes, t[^4]) is not { } c || TryNum(fs.bytes, t[^3]) is not { } d
                    || TryNum(fs.bytes, t[^2]) is not { } tx || TryNum(fs.bytes, t[^1]) is not { } ty) break;
                fs.tmA = a; fs.tmB = b; fs.tmC = c; fs.tmD = d; fs.tlmX = tx; fs.tlmY = ty;
                if (!live) break;
                // An absolute seat starts a new chain, stated as the producer wrote it.
                // The older contract moves a plain translation; a rotated or scaled
                // matrix is skipped there too, since its operand is not a page X.
                fs.carried = 0;
#pragma warning disable S1244 // an identity matrix written by the producer, read back as literals - exact on purpose
                if (fs.absoluteSeats && a == 1 && b == 0 && c == 0 && d == 1 && OnLine(fs, tx, ty))
#pragma warning restore S1244
                    Patch(fs, t[^2], tx + fs.delta);
                break;
            case "Td":
            case "TD":
            {
                if (t.Count < 2 || TryNum(fs.bytes, t[^2]) is not { } dx || TryNum(fs.bytes, t[^1]) is not { } dy) break;
                fs.tlmX = dx * fs.tmA + dy * fs.tmC + fs.tlmX;
                fs.tlmY = dx * fs.tmB + dy * fs.tmD + fs.tlmY;
                if (op == "TD") fs.leading = -dy;
                if (!live || fs.absoluteSeats || fs.tmA == 0) break;
                var want = OnLine(fs, fs.tlmX, fs.tlmY) ? fs.delta : 0;
                if (Math.Abs(want - fs.carried) > SeatTolerance)
                {
                    Patch(fs, t[^2], dx + (want - fs.carried) / fs.tmA);
                    fs.carried = want;
                }
                break;
            }
            default: // T*, ', " : a new line off the line matrix, with no operand to patch
                if (live && !fs.absoluteSeats && fs.opStart >= 0 && Math.Abs(fs.carried) > SeatTolerance)
                {
                    var restore = $" {Num(fs.tmA)} {Num(fs.tmB)} {Num(fs.tmC)} {Num(fs.tmD)} {Num(fs.tlmX)} {Num(fs.tlmY)} Tm ";
                    fs.result.Write(fs.bytes, fs.lastWritePos, fs.opStart - fs.lastWritePos);
                    fs.result.Write(Encoding.ASCII.GetBytes(restore));
                    fs.lastWritePos = fs.opStart;
                    fs.changed = true;
                    fs.carried = 0;
                }
                fs.tlmX = -fs.leading * fs.tmC + fs.tlmX;
                fs.tlmY = -fs.leading * fs.tmD + fs.tlmY;
                break;
        }
    }

    private static bool OnLine(FollowerShiftState fs, double x, double y) =>
        Math.Abs(y - fs.baselineY) <= BaselineTolerance && x >= fs.fromX - BaselineTolerance;

    private static void Patch(FollowerShiftState fs, (int start, int end) tok, double value)
    {
        fs.result.Write(fs.bytes, fs.lastWritePos, tok.start - fs.lastWritePos);
        fs.result.Write(Encoding.ASCII.GetBytes(Num(value)));
        fs.lastWritePos = tok.end;
        fs.changed = true;
    }

    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Two baselines count as the same line within this many points. A shared
    /// baseline is written from one layout pass, so the values agree to far better than
    /// this; the tolerance only absorbs the rounding of a decimal operand.</summary>
    private const double BaselineTolerance = 0.05;

    /// <summary>A seat moved by less than this is left as written: the operand's own
    /// rounding is coarser.</summary>
    private const double SeatTolerance = 0.001;

    /// <summary>The index just after the <c>EI</c> that closes the inline image whose
    /// <c>ID</c> keyword ends at <paramref name="i"/>: the data starts after one
    /// whitespace byte and runs to an <c>EI</c> standing alone between whitespace.</summary>
    private static int SkipInlineImageData(byte[] bytes, int i)
    {
        i++;
        for (; i + 2 < bytes.Length; i++)
        {
            if (!IsWhitespace(bytes[i]) || bytes[i + 1] != (byte)'E' || bytes[i + 2] != (byte)'I') continue;
            if (i + 3 >= bytes.Length || IsWhitespace(bytes[i + 3])) return i + 3;
        }
        return bytes.Length;
    }

    private static bool IsWhitespace(byte b) => b is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or 0 or 0x0C;

    private static bool IsDelimiter(byte b) =>
        b is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or 0
          or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>'
          or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    private static double? TryNum(byte[] bytes, (int start, int end) tok)
    {
        var span = Encoding.ASCII.GetString(bytes, tok.start, tok.end - tok.start);
        return double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>Step past a string, hex string, array, dictionary or name operand,
    /// honouring escapes and nesting, and return the index just after it.</summary>
    private static int SkipOperand(byte[] bytes, int i)
    {
        switch (bytes[i])
        {
            case (byte)'(':
            {
                var depth = 0;
                for (; i < bytes.Length; i++)
                {
                    var b = bytes[i];
#pragma warning disable S127 // string-literal lexer - a backslash escape consumes the byte after it
                    if (b == 0x5C) { i++; continue; }
#pragma warning restore S127
                    if (b == (byte)'(') depth++;
                    else if (b == (byte)')') { depth--; if (depth == 0) return i + 1; }
                }
                return i;
            }
            case (byte)'/':
                i++;
                while (i < bytes.Length && !IsDelimiter(bytes[i])) i++;
                return i;
            default:
            {
                // '<' opens either a hex string or a dictionary, '[' an array; in every
                // case the matching close is what ends the operand.
                var open = bytes[i];
                var close = open == (byte)'[' ? (byte)']' : (byte)'>';
                var depth = 0;
                for (; i < bytes.Length; i++)
                {
                    if (bytes[i] == open) depth++;
                    else if (bytes[i] == close) { depth--; if (depth <= 0) return i + 1; }
                }
                return i;
            }
        }
    }
}
