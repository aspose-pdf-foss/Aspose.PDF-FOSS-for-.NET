using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
    /// <summary>
    /// Change the font size of the Tf operator that immediately precedes the first
    /// occurrence of <paramref name="text"/> in the page's content stream(s).
    /// </summary>
    /// <param name="page">The page whose content (and form XObjects) is rewritten.</param>
    /// <param name="text">The decoded text of the run to resize.</param>
    /// <param name="oldSize">The run's current effective font size; only shows at this size match.</param>
    /// <param name="newSize">The desired effective font size.</param>
    /// <param name="allowCollateral">When false, a Tf that also governs shows outside the match
    /// is not resized; the match gets its own Tf instead.</param>
    /// <param name="reseat">Line geometry used to re-seat the rest of the line after the resize, or null.</param>
    /// <param name="splitSubRun">Whether a match that is PART of one show may split that show
    /// around itself. A fragment whose face was explicitly reassigned is re-encoded by the
    /// text replacement that follows, which carries the size with it and needs the show it
    /// absorbed left intact; such a caller passes false and the sub-run resize is left to it.</param>
    public void ModifyFontSize(Page page, string text, double oldSize, double newSize, bool allowCollateral = true,
        LineReseat? reseat = null, bool splitSubRun = true)
    {
        var reader = page.Reader;
        if (reader is null) return;

        // First try Form XObjects (text is often inside XObjects, not page content directly)
        if (ModifyInFormXObjects(page.Dict, reader, text, oldSize, newSize, allowCollateral, reseat, splitSubRun))
            return;

        // Then try the page's own content stream
        var contentStreams = GetContentStreams(page, reader);
        if (contentStreams.Count == 0) return;

        var combined = CombineStreams(contentStreams);
        var modified = ModifyFontSizeInStream(combined, text, oldSize, newSize, page.Dict, reader, allowCollateral, reseat, splitSubRun);
        if (modified is not null)
        {
            page.SetContentStream(modified);
        }
    }

    /// <summary>What a whole-show resize needs to re-seat the rest of its line (see
    /// <see cref="ReseatLine"/>): the page's right edge, the face's measure, and the LIVE
    /// page rectangle of every show in stream order (re-absorbed for each resize, because
    /// earlier resizes moved shows the caller's absorbed fragments still place where they were).</summary>
    internal readonly record struct LineReseat(double PageRight, Func<string, double, double> Measure,
        Func<IReadOnlyList<(double llx, double urx, double lly)>?> ShowRects);

    private bool ModifyInFormXObjects(PdfDictionary dict, PdfReader reader,
        string text, double oldSize, double newSize, bool allowCollateral, LineReseat? reseat = null,
        bool splitSubRun = true)
    {
        var resources = reader.ResolveDict(dict.Get("Resources"));
        if (resources is null) return false;
        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return false;

        foreach (var key in xobjects.Keys)
        {
            var xobjStream = reader.ResolveStream(xobjects.Get(key));
            if (xobjStream is null) continue;
            if (xobjStream.Dict.GetName("Subtype") != "Form") continue;

            var streamData = reader.DecodeStream(xobjStream);
            var modified = ModifyFontSizeInStream(streamData, text, oldSize, newSize, xobjStream.Dict, reader, allowCollateral, reseat, splitSubRun);
            if (modified is not null)
            {
                xobjStream.Dict.Remove("Filter");
                xobjStream.Dict.Remove("DecodeParms");
                xobjStream.Dict.Set("Length", new PdfInteger(modified.Length));
                xobjStream.ReplaceData(modified);
                return true;
            }

            // Recurse into nested Form XObjects
            if (ModifyInFormXObjects(xobjStream.Dict, reader, text, oldSize, newSize, allowCollateral, reseat, splitSubRun))
                return true;
        }
        return false;
    }

    /// <summary>Wrap the shows covering a match in their OWN <c>Tf</c>, leaving the Tf that
    /// governs the rest of the line alone: <c>/F newSize Tf</c> before the first covered show
    /// and <c>/F oldSize Tf</c> after the last. Returns null (leave the stream untouched) when
    /// the covered shows do not form one contiguous run wholly inside the match, when they do
    /// not share one Tf, or when the font resource name is unknown — in any of those cases a
    /// scope would reach text the caller did not name.</summary>
    private static byte[]? ScopedTfInsertion(byte[] streamBytes,
        List<(string decoded, int tfStart, int tfEnd, double effSize, int showStart, int showEnd, string? fontRes)> shows,
        (int start, int end)[] spans, int matchStart, int matchEnd, double oldSize, double newSize)
    {
        int first = -1, last = -1;
        for (var si = 0; si < shows.Count; si++)
        {
            if (spans[si].end <= matchStart || spans[si].start >= matchEnd) continue;
            if (spans[si].start < matchStart || spans[si].end > matchEnd) return null; // partial show
            if (Math.Abs(shows[si].effSize - oldSize) >= 0.5) return null;
            if (first < 0) first = si;
            last = si;
        }
        if (first < 0 || last < first) return null;
        for (var si = first; si <= last; si++)
        {
            if (spans[si].start < matchStart || spans[si].end > matchEnd) return null; // interleaved
            if (shows[si].tfStart != shows[first].tfStart) return null;                // mixed Tf
        }
        var res = shows[first].fontRes;
        if (string.IsNullOrEmpty(res)) return null;
        var rawOld = RawTfFor(streamBytes, shows[first]);
        var scale = shows[first].effSize / Math.Max(0.0001, rawOld);
        var rawNew = newSize / Math.Max(0.0001, scale);
        var open = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,
            " /{0} {1:0.####} Tf ", res, rawNew));
        var close = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,
            " /{0} {1:0.####} Tf ", res, rawOld));
        var at1 = shows[first].showStart;
        var at2 = shows[last].showEnd;
        if (at1 < 0 || at2 > streamBytes.Length || at2 < at1) return null;
        var result = new byte[streamBytes.Length + open.Length + close.Length];
        var w = 0;
        Array.Copy(streamBytes, 0, result, w, at1); w += at1;
        open.CopyTo(result, w); w += open.Length;
        Array.Copy(streamBytes, at1, result, w, at2 - at1); w += at2 - at1;
        close.CopyTo(result, w); w += close.Length;
        Array.Copy(streamBytes, at2, result, w, streamBytes.Length - at2);
        return result;
    }

    /// <summary>Parse the raw numeric Tf size at the recorded operand span.</summary>
    private static double RawTfFor(byte[] streamBytes,
        (string decoded, int tfStart, int tfEnd, double effSize, int showStart, int showEnd, string? fontRes) show)
    {
        var s = Encoding.ASCII.GetString(streamBytes, show.tfStart,
            Math.Max(0, show.tfEnd - show.tfStart)).Trim();
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v != 0 ? v : show.effSize;
    }

    private static byte[] PatchFontSize(byte[] original, int sizeStart, int sizeEnd, double newSize)
    {
        // Skip leading whitespace in the size range to preserve it
        while (sizeStart < sizeEnd && (original[sizeStart] == ' ' || original[sizeStart] == '\t'
            || original[sizeStart] == '\r' || original[sizeStart] == '\n'))
            sizeStart++;

        // Use enough precision so size * tmScale rounds back to the intended value
        string sizeStr;
        if (newSize == Math.Floor(newSize))
            sizeStr = ((int)newSize).ToString(CultureInfo.InvariantCulture);
        else
            sizeStr = newSize.ToString("R", CultureInfo.InvariantCulture); // round-trip format
        var sizeBytes = Encoding.ASCII.GetBytes(sizeStr);

        var result = new byte[original.Length - (sizeEnd - sizeStart) + sizeBytes.Length];
        Array.Copy(original, 0, result, 0, sizeStart);
        Array.Copy(sizeBytes, 0, result, sizeStart, sizeBytes.Length);
        Array.Copy(original, sizeEnd, result, sizeStart + sizeBytes.Length, original.Length - sizeEnd);
        return result;
    }

    /// <summary>Geometry for the overflow re-lay a fragment-level font assignment can
    /// request (see <see cref="TextState.Font"/>): the line's baseline drops one
    /// re-flow band and the tail runs re-seat at the match x plus one source-face
    /// space, each keeping its own face. Probed on a
    /// replaced run whose assigned face cannot fit the sheet (the overflow family: source
    /// baseline 364.27 → 346.71 at fs 15.96 = 1.10 em; the ': ' and CID tail runs
    /// both re-seat at 66.96 + 3.99, one source-face space right of the match).
    /// The match run's own seat anchors the edit — it is read off the run's
    /// preceding Tm, so no caller-side position is involved.</summary>
    internal readonly record struct OverflowRelay(double SourceSpaceW, double Drop);

    private static readonly System.Text.RegularExpressions.Regex SimpleTm =
        new(@"1 0 0 1 (-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) Tm");

    /// <summary>Re-seat the source line after a font assignment left its replaced run
    /// wider than the sheet: the matched run (seated at the anchor) keeps its x and
    /// drops by <see cref="OverflowRelay.Drop"/>; every later run on the same baseline
    /// re-seats one source-face space right of the match on the dropped baseline,
    /// keeping its own face. Only simple `1 0 0 1 x y Tm` seats are touched — a line
    /// positioned any other way is left exactly where it was.</summary>
    private static byte[] RelayOverflowLine(byte[] content, double matchX, double baselineY,
        OverflowRelay r)
    {
        var s = Compat.Latin1.GetString(content);
        var newY = baselineY - r.Drop;
        var tailX = matchX + r.SourceSpaceW;
        static string Fmt(double v) => v.ToString("0.0###", CultureInfo.InvariantCulture);
        var patched = SimpleTm.Replace(s, m =>
        {
            var x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var y = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (System.Math.Abs(y - baselineY) > 0.02) return m.Value;
            if (System.Math.Abs(x - matchX) <= 0.02)
                return "1 0 0 1 " + Fmt(x) + " " + Fmt(newY) + " Tm";
            if (x > matchX)
                return "1 0 0 1 " + Fmt(tailX) + " " + Fmt(newY) + " Tm";
            return m.Value;
        });
        return Compat.Latin1.GetBytes(patched);
    }

    /// <summary>Resize a match that covers PART of one show by splitting the show at the
    /// match's code boundaries inside its own BT: the codes before the match stay under the
    /// governing Tf, the match's codes are shown under their own <c>/F newSize Tf</c>, the
    /// codes after it under a restoring <c>/F oldSize Tf</c>. Every piece keeps its kern
    /// numbers, a kern belonging to the code that follows it, and the text cursor runs on
    /// through the pieces, so the remainder re-seats itself by the growth of the resized
    /// advance - the probed shape of a sub-run resize: the resized part starts at its own
    /// origin on the same baseline, the rest of the line moves by the advance growth, and
    /// nothing else on the baseline moves. Null when the match does not fall on code
    /// boundaries of exactly one show, or the operand cannot be re-lexed.</summary>
    private static byte[]? SplitShowScopedTf(FontSizeStreamState fz,
        (string decoded, int tfStart, int tfEnd, double effSize, int showStart, int showEnd, string? fontRes) show,
        int matchStart, int matchEnd, double oldSize, double newSize)
    {
        if (string.IsNullOrEmpty(show.fontRes) || Math.Abs(show.effSize - oldSize) >= 0.5) return null;
        Dictionary<int, string>? toUnicode = null;
        if (fz.fonts.TryGetValue(show.fontRes, out var fontDict))
            toUnicode = TextAbsorber.ParseToUnicodeFromDict(fontDict, fz.reader);
        var items = LexShowItems(fz.streamBytes, show.showStart, show.showEnd, toUnicode, out var opEnd);
        if (items is null || opEnd <= 0) return null;
        // Locate the code range [first, last] covering the match; the match must start and
        // end exactly on code boundaries, else the resize would reach text the caller did not name.
        int first = -1, last = -1, chars = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.Kern is not null) continue;
            var cs = chars;
            var ce = chars + it.Text!.Length;
            chars = ce;
            if (ce <= matchStart || cs >= matchEnd) continue;
            if (cs < matchStart || ce > matchEnd) return null;
            if (first < 0) first = i;
            last = i;
        }
        if (first < 0 || last < first || chars != show.decoded.Length) return null;
        var rawOld = RawTfFor(fz.streamBytes, show);
        var scale = show.effSize / Math.Max(0.0001, rawOld);
        var rawNew = newSize / Math.Max(0.0001, scale);
        // A kern ahead of the first matched code opens the matched group (it belongs to the
        // code it precedes); the kerns after the last matched code open the tail group.
        var preEnd = first;
        while (preEnd > 0 && items[preEnd - 1].Kern is not null) preEnd--;
        var postStart = last + 1;
        var sb = new StringBuilder();
        EmitShowGroup(sb, items, 0, preEnd);
        AppendTf(sb, show.fontRes!, rawNew);
        EmitShowGroup(sb, items, preEnd, postStart);
        AppendTf(sb, show.fontRes!, rawOld);
        EmitShowGroup(sb, items, postStart, items.Count);
        var piece = Compat.Latin1.GetBytes(sb.ToString());
        var result = new byte[fz.streamBytes.Length - (opEnd - show.showStart) + piece.Length];
        Array.Copy(fz.streamBytes, 0, result, 0, show.showStart);
        piece.CopyTo(result, show.showStart);
        Array.Copy(fz.streamBytes, opEnd, result, show.showStart + piece.Length, fz.streamBytes.Length - opEnd);
        return result;
    }

    private static void AppendTf(StringBuilder sb, string fontRes, double rawSize)
        => sb.Append(" /").Append(fontRes).Append(' ')
             .Append(rawSize.ToString("0.####", CultureInfo.InvariantCulture)).Append(" Tf ");

    /// <summary>One item of a show operand: a code (its bytes and decoded text) or a kern
    /// number (its raw spelling).</summary>
    private readonly record struct ShowItem(byte[]? Code, string? Text, string? Kern);

    /// <summary>Re-lex a Tj/TJ operand into codes and kern numbers. A string is cut into codes
    /// the way the show's text was decoded: two-byte codes when every pair maps through the
    /// ToUnicode CMap, else single bytes. <paramref name="opEnd"/> is the end of the show
    /// operator (Tj or TJ); null when the operand holds anything else.</summary>
    private static List<ShowItem>? LexShowItems(byte[] original, int start, int end,
        Dictionary<int, string>? toUnicode, out int opEnd)
    {
        opEnd = 0;
        var items = new List<ShowItem>();
        var lexer = new PdfLexer(original) { Position = start };
        var t = lexer.NextToken();
        if (t.Kind is TokenKind.LiteralString or TokenKind.HexString)
        {
            if (t.BytesValue is null) return null;
            AddCodes(items, t.BytesValue, toUnicode);
        }
        else if (t.Kind == TokenKind.ArrayStart)
        {
            while (true)
            {
                var itemStart = (int)lexer.Position;
                var e = lexer.NextToken();
                if (e.Kind == TokenKind.ArrayEnd) break;
                var itemEnd = (int)lexer.Position;
                if (itemEnd > end) return null;
                switch (e.Kind)
                {
                    case TokenKind.LiteralString:
                    case TokenKind.HexString:
                        if (e.BytesValue is null) return null;
                        AddCodes(items, e.BytesValue, toUnicode);
                        break;
                    case TokenKind.Integer:
                    case TokenKind.Real:
                        items.Add(new ShowItem(null, null,
                            Compat.Latin1.GetString(original, itemStart, itemEnd - itemStart).Trim()));
                        break;
                    default:
                        return null;
                }
            }
        }
        else return null;
        var op = lexer.NextToken();
        if (op.Kind != TokenKind.Keyword || op.StringValue is not ("Tj" or "TJ")) return null;
        opEnd = (int)lexer.Position;
        if (opEnd > end) return null;
        return items.Count == 0 ? null : items;
    }

    /// <summary>Cut a shown string into its codes, each with the text it decodes to.</summary>
    private static void AddCodes(List<ShowItem> items, byte[] bytes, Dictionary<int, string>? toUnicode)
    {
        if (bytes.Length == 0) return;
        var twoByte = toUnicode is { Count: > 0 } && bytes.Length % 2 == 0;
        if (twoByte)
            for (var i = 0; i < bytes.Length; i += 2)
                if (!toUnicode!.ContainsKey((bytes[i] << 8) | bytes[i + 1])) { twoByte = false; break; }
        var step = twoByte ? 2 : 1;
        for (var i = 0; i + step <= bytes.Length; i += step)
        {
            var code = bytes.AsSpan(i, step).ToArray();
            items.Add(new ShowItem(code, DecodeTextString(code, toUnicode), null));
        }
    }

    /// <summary>Write the items in [from, to) as one TJ, every code as a hex string.</summary>
    private static void EmitShowGroup(StringBuilder sb, List<ShowItem> items, int from, int to)
    {
        if (from >= to) return;
        sb.Append('[');
        for (var i = from; i < to; i++)
        {
            var it = items[i];
            if (it.Kern is not null) { sb.Append(it.Kern); continue; }
            sb.Append('<');
            foreach (var b in it.Code!) sb.Append(b.ToString("X2"));
            sb.Append('>');
        }
        sb.Append("] TJ");
    }
}
