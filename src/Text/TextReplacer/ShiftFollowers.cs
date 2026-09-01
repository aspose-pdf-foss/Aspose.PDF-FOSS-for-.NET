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
    /// A page whose runs each carry their OWN absolute text matrix
    /// (<c>BT … 1 0 0 1 x y Tm … Tj … ET</c>, one block per word) cannot reflow from
    /// inside a show operator: the follower's position is not expressed relative to the
    /// run that changed width, it is stated outright. Widening a replacement therefore
    /// draws it straight over the next word unless every later block on that baseline is
    /// moved by the same amount.
    ///
    /// Only the horizontal placement of an ABSOLUTE <c>Tm</c> is touched, and only for
    /// blocks seated on <paramref name="baselineY"/> at or beyond
    /// <paramref name="fromX"/> — text on other lines, and text to the left of the
    /// replacement, keep their positions exactly.</summary>
    internal static byte[] ShiftFollowingBlocks(byte[] streamBytes, double baselineY,
        double fromX, double delta)
    {
        if (delta == 0 || streamBytes.Length == 0) return streamBytes;
        var result = new MemoryStream(streamBytes.Length + 64);
        var lastWritePos = 0;
        foreach (var (txStart, txEnd, tx, ty) in AbsoluteTmPlacements(streamBytes))
        {
            if (Math.Abs(ty - baselineY) > BaselineTolerance) continue;
            if (tx < fromX - BaselineTolerance) continue;
            result.Write(streamBytes, lastWritePos, txStart - lastWritePos);
            var shifted = (tx + delta).ToString("0.####", CultureInfo.InvariantCulture);
            result.Write(Encoding.ASCII.GetBytes(shifted));
            lastWritePos = txEnd;
        }
        if (lastWritePos == 0) return streamBytes;
        result.Write(streamBytes, lastWritePos, streamBytes.Length - lastWritePos);
        return result.ToArray();
    }

    /// <summary>Two baselines count as the same line within this many points. A shared
    /// baseline is written from one layout pass, so the values agree to far better than
    /// this; the tolerance only absorbs the rounding of a decimal operand.</summary>
    private const double BaselineTolerance = 0.05;

    /// <summary>Every <c>Tm</c> in the stream that is a plain translation
    /// (<c>1 0 0 1 tx ty Tm</c>), reported as the byte span of its <c>tx</c> operand plus
    /// the placement it states. A rotated or scaled matrix is skipped: its horizontal
    /// operand is not a page-space X, so adding points to it would move the run
    /// somewhere the caller did not ask for.</summary>
    private static IEnumerable<(int txStart, int txEnd, double tx, double ty)>
        AbsoluteTmPlacements(byte[] bytes)
    {
        var tokens = new List<(int start, int end)>(8);
        var i = 0;
        while (i < bytes.Length)
        {
            var c = bytes[i];
            if (c is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or 0) { i++; continue; }
            // A string or dictionary operand can hold anything, including the letters
            // "Tm" — step over it rather than tokenising its contents.
            if (c is (byte)'(' or (byte)'<' or (byte)'[' or (byte)'/')
            {
                i = SkipOperand(bytes, i);
                tokens.Clear();
                continue;
            }
            var start = i;
            while (i < bytes.Length && !IsDelimiter(bytes[i])) i++;
            if (i == start) { i++; continue; }
            var len = i - start;
            if (len == 2 && bytes[start] == (byte)'T' && bytes[start + 1] == (byte)'m')
            {
                if (tokens.Count >= 6)
                {
                    var six = tokens.GetRange(tokens.Count - 6, 6);
                    if (TryNum(bytes, six[0], out var a) && TryNum(bytes, six[1], out var b)
                        && TryNum(bytes, six[2], out var cc) && TryNum(bytes, six[3], out var dd)
                        && TryNum(bytes, six[4], out var tx) && TryNum(bytes, six[5], out var ty)
                        && a == 1 && b == 0 && cc == 0 && dd == 1)
                    {
                        yield return (six[4].start, six[4].end, tx, ty);
                    }
                }
                tokens.Clear();
                continue;
            }
            tokens.Add((start, i));
            if (tokens.Count > 8) tokens.RemoveAt(0);
        }
    }

    private static bool IsDelimiter(byte b) =>
        b is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or 0
          or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>'
          or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    private static bool TryNum(byte[] bytes, (int start, int end) tok, out double value)
    {
        value = 0;
        var span = Encoding.ASCII.GetString(bytes, tok.start, tok.end - tok.start);
        return double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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
                    if (b == 0x5C) { i++; continue; }
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
