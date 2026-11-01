using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Aspose.Pdf.IO;

internal static partial class PdfLinearizer
{
    /// <summary>A reference in an object's bytes: the span of its "N G R" text, the object it
    /// names, and the top-level dictionary key it sits under (-1 when none).</summary>
    private struct RefSpan
    {
        public int Start, End, Target, Key;
    }

    /// <summary>A top-level dictionary key: where its name starts and the span of its value,
    /// which runs to the next key or to the closing delimiter of the dictionary.</summary>
    private struct KeySpan
    {
        public string Name;
        public int NameStart, ValueStart, ValueEnd;
    }

    /// <summary>
    /// One object of the plainly-saved file as the linearized write re-emits it: its bytes
    /// (a span of the saved file, or an edited copy), the references they hold, and - for the
    /// catalog and the page tree - its top-level keys. The write renumbers the object, so its
    /// header and every reference are rewritten while everything else, stream data included,
    /// is copied as it is.
    /// </summary>
    private sealed class RawObject
    {
        public int Num;
        public byte[] Buf = null!;
        public int Start, BodyStart, End;
        /// <summary>Just past the "&lt;&lt;" that opens the object's own dictionary, or -1.</summary>
        public int DictOpen = -1;
        public bool TypeIsPage;
        /// <summary>Whether a stream's data follows the dictionary.</summary>
        public bool IsStream;
        /// <summary>The linearization dictionary or hint stream of an earlier linearized save.</summary>
        public bool IsLinearizationInfra;
        public List<RefSpan> Refs = new();
        public List<KeySpan>? Keys;
        public int NewNum;
        /// <summary>The object stream that holds the object, or null when the object is written
        /// at a file offset of its own; and its index there.</summary>
        public RawObject? Container;
        public int PackedIndex;
        /// <summary>For an object stream the linearized write adds: the objects it holds.</summary>
        public List<RawObject>? Members;
        /// <summary>Emitted size, from the header through "endobj" and the line feed after it.</summary>
        public int Length;
        public long Offset;
    }

    private const string ObjKeyword = "obj";
    private const string EndObjKeyword = "endobj";
    private const string RefSuffix = " 0 R";
    private const string ObjSuffix = " 0 obj";
    private static readonly byte[] NullBytes = Encoding.ASCII.GetBytes("null");
    private static readonly byte[] PageTypeName = Encoding.ASCII.GetBytes("Page");
    private static readonly byte[] TypeKeyName = Encoding.ASCII.GetBytes("Type");

    private static bool IsWhite(byte c) => c is 0 or 9 or 10 or 12 or 13 or 32;

    private static bool IsDelimiter(byte c) =>
        c is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
            or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    /// <summary>Where the object's body starts: just past the "obj" keyword of its "N G obj"
    /// header, or -1 when the bytes do not open with one.</summary>
    private static int BodyStartOf(byte[] b, int start, int end)
    {
        var p = start;
        for (var field = 0; field < 2; field++)
        {
            while (p < end && IsWhite(b[p])) p++;
            var digits = p;
            while (p < end && b[p] >= '0' && b[p] <= '9') p++;
            if (p == digits) return -1;
        }
        while (p < end && IsWhite(b[p])) p++;
        if (p + ObjKeyword.Length > end || b[p] != 'o' || b[p + 1] != 'b' || b[p + 2] != 'j') return -1;
        return p + ObjKeyword.Length;
    }

    /// <summary>Lexing state of one object scan: the nesting depth, the last two integer tokens
    /// (a reference is two integers and an R) and the top-level key being read.</summary>
    private struct ScanCursor
    {
        public int Depth, IntCount, LastIntStart, PrevIntStart, CurrentKey;
        public long LastInt, PrevInt;
        public bool TopIsDict, ExpectKey, TypeKeyPending;
    }

    /// <summary>Finds the object's references, the "&lt;&lt;" of its dictionary and whether it is
    /// typed /Page; with <paramref name="wantKeys"/> it also records the top-level keys. The scan
    /// stops at the stream keyword: stream data is never read.</summary>
    private static void ScanObject(RawObject o, bool wantKeys)
    {
        o.Refs.Clear();
        o.Keys = wantKeys ? new List<KeySpan>() : null;
        o.TypeIsPage = false;
        o.IsStream = false;
        o.IsLinearizationInfra = false;
        o.DictOpen = -1;
        var b = o.Buf;
        var end = o.End - EndObjKeyword.Length;
        var c = new ScanCursor { CurrentKey = -1 };
        var p = o.BodyStart;
        while (p < end)
        {
            var ch = b[p];
            if (IsWhite(ch)) { p++; continue; }
            if (ch == '%') { while (p < end && b[p] != '\n' && b[p] != '\r') p++; continue; }
            var tokenStart = p;
            if (ch == '(') { p = SkipLiteralString(b, p, end); ValueToken(ref c); continue; }
            if (ch == '<' && p + 1 < end && b[p + 1] == '<')
            {
                p += 2;
                OpenContainer(o, ref c, isDict: true, p);
                continue;
            }
            if (ch == '<') { p = SkipHexString(b, p, end); ValueToken(ref c); continue; }
            if (ch == '>' && p + 1 < end && b[p + 1] == '>') { p += 2; CloseContainer(o, ref c, tokenStart); continue; }
            if (ch == '[') { p++; OpenContainer(o, ref c, isDict: false, p); continue; }
            if (ch == ']') { p++; CloseContainer(o, ref c, tokenStart); continue; }
            if (ch == '/') { p = ScanName(o, ref c, p, end); continue; }
            if (IsDelimiter(ch)) { p++; continue; }
            while (p < end && !IsWhite(b[p]) && !IsDelimiter(b[p])) p++;
            if (RegularToken(o, ref c, tokenStart, p)) { o.IsStream = true; return; }
        }
    }

    private static void OpenContainer(RawObject o, ref ScanCursor c, bool isDict, int afterOpen)
    {
        if (c.Depth == 0 && isDict && o.DictOpen < 0)
        {
            o.DictOpen = afterOpen;
            c.TopIsDict = true;
            c.ExpectKey = true;
        }
        c.Depth++;
        c.IntCount = 0;
        c.TypeKeyPending = false;
    }

    private static void CloseContainer(RawObject o, ref ScanCursor c, int closeStart)
    {
        if (c.Depth > 0) c.Depth--;
        c.IntCount = 0;
        if (c.Depth == 1) c.ExpectKey = true;
        if (c.Depth == 0 && c.TopIsDict) EndKeyValue(o, ref c, closeStart);
    }

    /// <summary>Any token that completes a value: a following name at the top level is a key.</summary>
    private static void ValueToken(ref ScanCursor c)
    {
        c.IntCount = 0;
        c.TypeKeyPending = false;
        if (c.Depth == 1) c.ExpectKey = true;
    }

    private static void EndKeyValue(RawObject o, ref ScanCursor c, int at)
    {
        if (o.Keys is null || c.CurrentKey < 0) return;
        var k = o.Keys[c.CurrentKey];
        if (k.ValueEnd == 0) { k.ValueEnd = at; o.Keys[c.CurrentKey] = k; }
    }

    private static int ScanName(RawObject o, ref ScanCursor c, int p, int end)
    {
        var b = o.Buf;
        var start = p++;
        while (p < end && !IsWhite(b[p]) && !IsDelimiter(b[p])) p++;
        c.IntCount = 0;
        if (c.Depth == 1 && c.TopIsDict && c.ExpectKey)
        {
            EndKeyValue(o, ref c, start);
            c.CurrentKey = o.Keys?.Count ?? c.CurrentKey + 1;
            o.Keys?.Add(new KeySpan
            {
                Name = Encoding.ASCII.GetString(b, start + 1, p - start - 1),
                NameStart = start,
                ValueStart = p,
            });
            c.ExpectKey = false;
            c.TypeKeyPending = NameEquals(b, start, p, TypeKeyName);
            if (NameEquals(b, start, p, LinearizedKeyName) || NameEquals(b, start, p, HintMarkerName))
                o.IsLinearizationInfra = true;
            return p;
        }
        if (c.Depth == 1 && c.TypeKeyPending && NameEquals(b, start, p, PageTypeName)) o.TypeIsPage = true;
        ValueToken(ref c);
        return p;
    }

    private static bool NameEquals(byte[] b, int slash, int end, byte[] name)
    {
        if (end - slash - 1 != name.Length) return false;
        for (var i = 0; i < name.Length; i++)
            if (b[slash + 1 + i] != name[i]) return false;
        return true;
    }

    /// <summary>A number or keyword. Returns true at the stream keyword, where the scan ends.</summary>
    private static bool RegularToken(RawObject o, ref ScanCursor c, int start, int end)
    {
        var b = o.Buf;
        if (end - start == 1 && b[start] == 'R' && c.IntCount >= 2)
        {
            if (c.PrevInt > 0 && c.PrevInt <= int.MaxValue)
                o.Refs.Add(new RefSpan
                {
                    Start = c.PrevIntStart, End = end, Target = (int)c.PrevInt,
                    Key = c.TopIsDict && c.Depth >= 1 ? c.CurrentKey : -1,
                });
            ValueToken(ref c);
            return false;
        }
        if (c.Depth == 0 && end - start == 6 && b[start] == 's' && b[start + 1] == 't' && b[start + 2] == 'r'
            && b[start + 3] == 'e' && b[start + 4] == 'a' && b[start + 5] == 'm')
            return true;
        long value = 0;
        var isInt = end - start <= 10;
        for (var i = start; i < end && isInt; i++)
        {
            if (b[i] < '0' || b[i] > '9') isInt = false;
            else value = value * 10 + (b[i] - '0');
        }
        if (!isInt) { ValueToken(ref c); return false; }
        c.PrevInt = c.LastInt; c.PrevIntStart = c.LastIntStart;
        c.LastInt = value; c.LastIntStart = start;
        c.IntCount++;
        c.TypeKeyPending = false;
        if (c.Depth == 1) c.ExpectKey = true;
        return false;
    }

    private static int SkipLiteralString(byte[] b, int p, int end)
    {
        var nesting = 0;
        for (; p < end; p++)
        {
            if (b[p] == '\\') { p++; continue; }
            if (b[p] == '(') nesting++;
            else if (b[p] == ')' && --nesting == 0) return p + 1;
        }
        return end;
    }

    private static int SkipHexString(byte[] b, int p, int end)
    {
        while (p < end && b[p] != '>') p++;
        return Math.Min(p + 1, end);
    }

    private static int DecimalDigits(int n)
    {
        var digits = 1;
        while (n >= 10) { n /= 10; digits++; }
        return digits;
    }

    /// <summary>The object number a reference is rewritten to, or 0 when it names no object of
    /// the file - such a reference is written as null, which is what a reader resolves it to.</summary>
    private static int NewNumberOf(LinearizeState lz, int oldNum) =>
        lz.objs.TryGetValue(oldNum, out var target) ? target.NewNum : 0;

    /// <summary>The emitted size of a renumbered object, computed without writing it.</summary>
    private static int EmittedLength(LinearizeState lz, RawObject o)
    {
        var length = DecimalDigits(o.NewNum) + ObjSuffix.Length + (o.End - o.BodyStart) + 1;
        foreach (var r in o.Refs)
        {
            var target = NewNumberOf(lz, r.Target);
            var replacement = target > 0 ? DecimalDigits(target) + RefSuffix.Length : NullBytes.Length;
            length += replacement - (r.End - r.Start);
        }
        return length;
    }

    /// <summary>Writes the object under its new number with every reference renumbered, followed
    /// by a line feed.</summary>
    private static void EmitObject(LinearizeState lz, RawObject o)
    {
        lz.W(o.NewNum.ToString(CultureInfo.InvariantCulture) + ObjSuffix);
        WriteRenumbered(lz, o, lz.ms, o.BodyStart, o.End);
        lz.ms.WriteByte((byte)'\n');
    }

    /// <summary>Copies the object's bytes in [from, to) with every reference in them renumbered.</summary>
    private static void WriteRenumbered(LinearizeState lz, RawObject o, Stream s, int from, int to)
    {
        var p = from;
        foreach (var r in o.Refs)
        {
            if (r.Start < from || r.End > to) continue;
            s.Write(o.Buf, p, r.Start - p);
            var target = NewNumberOf(lz, r.Target);
            var text = target > 0 ? Encoding.ASCII.GetBytes(target.ToString(CultureInfo.InvariantCulture) + RefSuffix) : NullBytes;
            s.Write(text, 0, text.Length);
            p = r.End;
        }
        s.Write(o.Buf, p, to - p);
    }
}
