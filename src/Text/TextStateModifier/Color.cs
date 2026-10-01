using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
    /// <summary>Whether the last <see cref="ModifyForegroundColor"/> call found a
    /// matching show operator and injected the colour (callers use it to retry
    /// with a wider positional scope).</summary>
    public bool LastForegroundColorApplied { get; private set; }

    /// <summary>
    /// Inject a `R G B rg` operator immediately before the first text-showing
    /// operator whose decoded string contains <paramref name="text"/>, so the
    /// rendered glyphs pick up the new fill colour. The rg is scoped by the
    /// containing graphics-state block (BT/ET sets text-rendering colour from
    /// the current fill colour at the BT call site, so subsequent text within
    /// the same BT block also picks up the new value — this is fine for
    /// per-fragment colour changes since each fragment's text-showing op is
    /// what we're targeting).
    /// </summary>
    /// <param name="page">The page whose content (and form XObjects) is rewritten.</param>
    /// <param name="text">The decoded text of the run to recolour.</param>
    /// <param name="color">The new fill colour.</param>
    /// <param name="targetY">The baseline y of the run to match, or null to match on text alone.</param>
    /// <param name="targetX">The pen x of the run to match, or null to match on text alone.</param>
    /// <param name="nearestX">Fallback mode. The strict pass anchors a recolour on a show
    /// operator whose pen sits at <paramref name="targetX"/>; when an EARLIER replacement on the
    /// same line has already moved the run, that anchor misses by the width delta. Rather than
    /// fall back to "the first operator on the line that contains the text" — which repaints the
    /// wrong occurrence whenever a line carries several — this mode picks the occurrence whose
    /// pen is NEAREST the recorded X.</param>
    /// <param name="renderingMode">A text rendering mode (Tr) to set with the colour, or null to leave it unchanged.</param>
    public void ModifyForegroundColor(Page page, string text, Color color, double? targetY = null,
        double? targetX = null, bool nearestX = false, int? renderingMode = null)
    {
        LastForegroundColorApplied = false;
        var reader = page.Reader;
        if (reader is null) return;

        if (ModifyForegroundColorInFormXObjects(page.Dict, reader, text, color, targetY, targetX,
                1, 0, 0, 1, 0, 0, nearestX, renderingMode))
        {
            LastForegroundColorApplied = true;
            return;
        }

        var contentStreams = GetContentStreams(page, reader);
        if (contentStreams.Count == 0) return;

        var combined = CombineStreams(contentStreams);
        var modified = ModifyForegroundColorInStream(combined, text, color, targetY, targetX,
            page.Dict, reader, 1, 0, 0, 1, 0, 0, nearestX, renderingMode);
        if (modified is not null)
        {
            // The whole rewritten page content is bracketed in a single q…Q pair
            // (idempotent — content already opening with q is left alone); the
            // recolor wraps the page once and keeps every
            // original operator in place.
            page.SetContentStream(TextReplacer.WrapInGraphicsState(modified));
            LastForegroundColorApplied = true;
        }
    }

    private bool ModifyForegroundColorInFormXObjects(PdfDictionary dict, PdfReader reader,
        string text, Color color, double? targetY, double? targetX,
        double ctmA, double ctmB, double ctmC, double ctmD, double ctmTx, double ctmTy,
        bool nearestX = false, int? renderingMode = null)
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
            var modified = ModifyForegroundColorInStream(streamData, text, color, targetY, targetX,
                xobjStream.Dict, reader, ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy, nearestX, renderingMode);
            if (modified is not null)
            {
                xobjStream.Dict.Remove("Filter");
                xobjStream.Dict.Remove("DecodeParms");
                xobjStream.Dict.Set("Length", new PdfInteger(modified.Length));
                xobjStream.ReplaceData(modified);
                return true;
            }

            if (ModifyForegroundColorInFormXObjects(xobjStream.Dict, reader, text, color, targetY,
                    targetX, ctmA, ctmB, ctmC, ctmD, ctmTx, ctmTy, nearestX, renderingMode))
                return true;
        }
        return false;
    }

    private byte[]? ModifyForegroundColorInStream(byte[] streamBytes, string text, Color color,
        double? targetY, double? targetX, PdfDictionary pageDict, PdfReader reader,
        double initCtmA, double initCtmB, double initCtmC, double initCtmD,
        double initCtmTx, double initCtmTy, bool nearestX = false, int? renderingMode = null)
    {
        var fc = new ForegroundColorState();
        InitForegroundColorState(fc, streamBytes, text, targetY, targetX, pageDict, reader, initCtmA, initCtmB, initCtmC, initCtmD, initCtmTx, initCtmTy, nearestX);

        while (true)
        {
            var startPos = (int)fc.lexer.Position;
            var token = fc.lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;
            var endPos = (int)fc.lexer.Position;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    fc.operands.Add((token.Kind, new PdfInteger(token.IntValue), startPos, endPos));
                    break;
                case TokenKind.Real:
                    fc.operands.Add((token.Kind, new PdfReal(token.RealValue), startPos, endPos));
                    break;
                case TokenKind.LiteralString:
                    fc.operands.Add((token.Kind, new PdfString(token.BytesValue!), startPos, endPos));
                    break;
                case TokenKind.HexString:
                    fc.operands.Add((token.Kind, new PdfString(token.BytesValue!, isHex: true), startPos, endPos));
                    break;
                case TokenKind.Name:
                    fc.operands.Add((token.Kind, new PdfName(token.StringValue!), startPos, endPos));
                    break;
                case TokenKind.ArrayStart:
                    if (!CollectTjArray(fc, startPos)) goto streamDone;
                    break;
                case TokenKind.Keyword:
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "BT": case "Td": case "TD": case "Tm": case "TL": case "T*": case "Tc": case "Tw": case "Tz":
                            TrackColorTextMatrix(fc, op);
                            break;
                        case "q": case "Q": case "cm": case "Tf": case "rg": case "g": case "k": case "Tr":
                            TrackColorGraphicsState(fc, endPos, streamBytes, reader, op);
                            break;
                        case "Do":
                            if (RecolorInFormXObject(fc, streamBytes, text, color, targetY, targetX, pageDict, reader, op) is { } recolorInFormXObjectResult) return recolorInFormXObjectResult;
                            break;
                        case "Tj": case "'": case "\"":
                            if (RecolorShownString(fc, endPos, streamBytes, text, color, targetX, nearestX, renderingMode, op) is { } recolorShownStringResult) return recolorShownStringResult;
                            break;
                        case "TJ":
                            if (RecolorShownArray(fc, endPos, streamBytes, text, color, targetX, nearestX, renderingMode, op) is { } recolorShownArrayResult) return recolorShownArrayResult;
                            break;
                    }
                    fc.operands.Clear();
                    break;
                default:
                    fc.operands.Clear();
                    break;
            }
        }
        streamDone:
        return fc.bestResult;
    }

    /// <summary>Collect the TJ array's text into a single PdfString operand so the
    /// TJ branch below can match it (mirrors ModifyFontSizeInStream /
    /// FindTfNameRange). Without this, `[ (hi world) -180 ... ] TJ` runs
    /// would never be seen by the colour matcher and no `rg` is injected.
    /// The raw components are kept for the pen-advance computation. Returns false at an unterminated array (end of stream).</summary>
    private static bool CollectTjArray(ForegroundColorState fc, int startPos)
    {
        var arrTexts = new StringBuilder();
        fc.tjItems = new List<object>();
        while (true)
        {
            var t = fc.lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return false;
            if (t.Kind == TokenKind.ArrayEnd) break;
            if (t.Kind == TokenKind.LiteralString || t.Kind == TokenKind.HexString)
            {
                var strBytes = t.BytesValue;
                if (strBytes is not null)
                {
                    arrTexts.Append(DecodeTextString(strBytes, fc.currentToUnicode));
                    fc.tjItems.Add(strBytes);
                }
            }
            else if (t.Kind == TokenKind.Integer) fc.tjItems.Add((double)t.IntValue);
            else if (t.Kind == TokenKind.Real) fc.tjItems.Add(t.RealValue);
        }
        fc.operands.Add((TokenKind.ArrayStart, new PdfString(
            Cp1252.GetBytes(arrTexts.ToString())), startPos, (int)fc.lexer.Position));
        return true;
    }

    /// <summary>The operator source text exactly as the producer wrote it, so a restore
    /// can re-emit that form rather than a normalised equivalent.</summary>
    private static string Verbatim(byte[] stream, int start, int end)
    {
        if (start < 0 || end > stream.Length || end <= start) return string.Empty;
        return Encoding.ASCII.GetString(stream, start, end - start).Trim();
    }

    private static double ToDouble(PdfObject obj) => obj switch
    {
        PdfInteger pi => pi.Value,
        PdfReal pr => pr.Value,
        _ => 0
    };

    /// <summary>
    /// Split a literal-string show operator so a matched substring is recoloured to
    /// <paramref name="color"/> while the prefix/suffix glyphs keep the active fill colour
    /// (<paramref name="activeColor"/>). Returns null — letting the caller fall back to a
    /// whole-run colour injection — when the match spans the whole run, the operand isn't a
    /// plain parenthesised literal, or it contains escapes/parentheses the simple byte-offset
    /// split can't safely handle.
    /// </summary>
    private static byte[]? SplitColorRun(byte[] original, int litStart, int litEnd,
        string text, Color color, (double r, double g, double b) activeColor,
        int occurrenceCharIndex = -1, int? renderingMode = null, int activeRenderingMode = 0)
    {
        // Operand must be a plain "(...)" literal.
        if (litEnd - litStart < 2
            || original[litStart] != (byte)'(' || original[litEnd - 1] != (byte)')')
            return null;

        int innerStart = litStart + 1;
        int innerLen = litEnd - 1 - innerStart;
        if (innerLen <= 0) return null;

        // Bail on any escape or nested parenthesis — the char↔byte offset mapping below
        // assumes a 1:1, single-byte literal (true for the WinAnsi runs produced by
        // text replacement).
        for (int i = innerStart; i < innerStart + innerLen; i++)
        {
            byte b = original[i];
            if (b == (byte)'\\' || b == (byte)'(' || b == (byte)')') return null;
        }

        var innerBytes = new byte[innerLen];
        Array.Copy(original, innerStart, innerBytes, 0, innerLen);
        var inner = Cp1252.GetString(innerBytes);
        // The caller identifies WHICH occurrence belongs to the segment being recoloured;
        // -1 means it had no positional anchor, so the first one is taken.
        int idx = occurrenceCharIndex >= 0 && occurrenceCharIndex + text.Length <= inner.Length
                  && string.CompareOrdinal(inner, occurrenceCharIndex, text, 0, text.Length) == 0
            ? occurrenceCharIndex
            : inner.IndexOf(text, StringComparison.Ordinal);
        if (idx < 0) return null;
        // Whole-run match → let the caller inject a single colour before the operator.
        if (idx == 0 && text.Length == inner.Length) return null;

        string prefix = inner.Substring(0, idx);
        string suffix = inner.Substring(idx + text.Length);

        string Rg(double r, double g, double b) => string.Format(CultureInfo.InvariantCulture,
            "{0:F3} {1:F3} {2:F3} rg", r, g, b);

        // Lead with a space so the first emitted token never abuts the preceding operator
        // (e.g. "Tm" running straight into "(prefix)" or "1.000", which the lexer mis-parses).
        var sb = new StringBuilder(" ");
        if (prefix.Length > 0) sb.Append('(').Append(prefix).Append(") Tj ");
        sb.Append(Rg(color.R / 255.0, color.G / 255.0, color.B / 255.0));
        // A requested rendering mode rides with the colour so the matched glyphs are
        // shown the way the caller's TextState asks (0 = fill, 3 = invisible).
        if (renderingMode is int rm) sb.Append(' ').Append(rm).Append(" Tr");
        sb.Append(" (").Append(text).Append(')');
        if (suffix.Length > 0)
        {
            // The original Tj keyword that follows this operand will show the suffix.
            sb.Append(" Tj");
            if (renderingMode is not null) sb.Append(' ').Append(activeRenderingMode).Append(" Tr");
            sb.Append(' ').Append(Rg(activeColor.r, activeColor.g, activeColor.b))
              .Append(" (").Append(suffix).Append(')');
        }

        var replacement = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[original.Length - (litEnd - litStart) + replacement.Length];
        Array.Copy(original, 0, result, 0, litStart);
        Array.Copy(replacement, 0, result, litStart, replacement.Length);
        Array.Copy(original, litEnd, result, litStart + replacement.Length, original.Length - litEnd);
        return result;
    }

    /// <summary>
    /// Split a TJ ARRAY so only the matched substring is shown under
    /// <c>beforeOps</c>, with <c>afterOps</c> restoring whatever state
    /// those changed for the glyphs that follow. The wrapping operators are the caller's: a
    /// fill colour for a recolour, a `Tf` for a font or size change.
    /// The array is cut into up to three arrays shown by their own TJ operators — consecutive
    /// show operators continue from the pen where the previous one left off, and each kern
    /// number stays in the group of the glyph it positions, so the split is
    /// positionally identical to the original. Returns null (caller falls back to applying the
    /// change to the whole operator) when the match spans the whole array, isn't found, or the
    /// array holds a string this simple 1:1 byte↔char split cannot address (escapes,
    /// multi-byte codes).
    /// </summary>
    /// <summary>`/Res size Tf`, spaced so it never abuts a neighbour.</summary>
    private static string TfOps(string res, double size) => string.Format(
        CultureInfo.InvariantCulture, " /{0} {1} Tf ", res,
        size.ToString("0.####", CultureInfo.InvariantCulture));

    /// <summary>`R G B rg` for <paramref name="color"/>, spaced so it never abuts a neighbour.</summary>
    private static string RgOps(Color color)
    {
        static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        return string.Format(CultureInfo.InvariantCulture, " {0} {1} {2} rg ",
            N(color.R / 255.0), N(color.G / 255.0), N(color.B / 255.0));
    }

    /// <summary>The operator that puts the fill colour back to what was active. Written as
    /// `v g` when the prior fill was a gray, matching how producers write a default-black
    /// reset, otherwise as `r g b rg`.</summary>
    private static string RestoreFillOps(double r, double g, double b)
    {
        static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        return r == g && g == b
            ? string.Format(CultureInfo.InvariantCulture, " {0} g ", N(r))
            : string.Format(CultureInfo.InvariantCulture, " {0} {1} {2} rg ", N(r), N(g), N(b));
    }

    private static byte[]? SplitShowRunTJ(byte[] original, int arrStart, int arrEnd,
        string text, string beforeOps, string afterOps,
        Dictionary<int, string>? toUnicode, int occurrenceCharIndex = -1)
    {
        if (arrEnd <= arrStart || arrEnd > original.Length) return null;

        // (isString, byteStart, byteEnd, charStart, charLen) in array order.
        var (items, chars) = ScanTjItems(original, arrStart, arrEnd, toUnicode);
        if (items is null) return null;

        var whole = new StringBuilder(chars);
        foreach (var it in items)
        {
            if (!it.isString) continue;
            var raw = new byte[it.end - it.start];
            Array.Copy(original, it.start, raw, 0, raw.Length);
            whole.Append(DecodeTextString(raw, toUnicode));
        }
        var all = whole.ToString();
        // The caller identifies WHICH occurrence belongs to the segment being recoloured;
        // -1 means it had no positional anchor, so the first one is taken.
        int idx = occurrenceCharIndex >= 0 && occurrenceCharIndex + text.Length <= all.Length
                  && string.CompareOrdinal(all, occurrenceCharIndex, text, 0, text.Length) == 0
            ? occurrenceCharIndex
            : all.IndexOf(text, StringComparison.Ordinal);
        if (idx < 0) return null;
        if (idx == 0 && text.Length == all.Length) return null; // whole run → caller wraps

        // Lead with a space so the first token never abuts the preceding operator.
        var sb = new StringBuilder(" ");
        if (idx > 0) { EmitGroup(sb, items, original, 0, idx); sb.Append(" TJ "); }
        sb.Append(beforeOps);
        EmitGroup(sb, items, original, idx, idx + text.Length);
        sb.Append(" TJ ").Append(afterOps);
        // The original TJ keyword that follows this operand shows the tail — which may be
        // empty, and an empty array is a valid (no-op) TJ operand.
        EmitGroup(sb, items, original, idx + text.Length, chars);

        var replacement = Compat.Latin1.GetBytes(sb.ToString());
        var result = new byte[original.Length - (arrEnd - arrStart) + replacement.Length];
        Array.Copy(original, 0, result, 0, arrStart);
        Array.Copy(replacement, 0, result, arrStart, replacement.Length);
        Array.Copy(original, arrEnd, result, arrStart + replacement.Length, original.Length - arrEnd);
        return result;
    }

    /// <summary>
    /// Wrap a text-showing operator with a fill-colour change and a matching restore:
    /// inject `R G B rg` immediately before the show operand (at <paramref name="rgInsertPos"/>)
    /// and restore the previously-active fill colour immediately after the show keyword (at
    /// <paramref name="restorePos"/>). Without the restore the recolour leaks onto every
    /// subsequent glyph in the same BT block. The restore is emitted as `v g` (SetGray) when
    /// the prior fill was a gray (r==g==b) — matching how producers write a default-black
    /// reset — otherwise as `r g b rg`.
    /// </summary>
    private static byte[] InjectColorAround(byte[] original, int rgInsertPos, int restorePos,
        Color color, (double r, double g, double b) restore, string? restoreOpText = null,
        int? renderingMode = null, int activeRenderingMode = 0)
    {
        // Leading space separates the injected rg from the preceding token
        // (e.g. "Tc" runs straight into "1.000" without it, which the lexer
        // mis-parses as one keyword "Tc1.000"); trailing space separates the
        // rg from the following PdfString '(' delimiter. Components are written
        // minimally ("1 0 0 rg", not "1.000 0.000 0.000 rg") — the exact
        // form asserted verbatim by operator-comparing consumers.
        static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var before = string.Format(CultureInfo.InvariantCulture, " {0} {1} {2} rg ",
            N(color.R / 255.0), N(color.G / 255.0), N(color.B / 255.0));
        // A requested rendering mode rides with the colour, so a replacement whose
        // TextState asks for visible text shows through an invisible (Tr 3) source run
        // and the surrounding glyphs keep the mode they had.
        if (renderingMode is int mode) before += mode.ToString(CultureInfo.InvariantCulture) + " Tr ";
        // Restore the producer's OWN colour operator verbatim when one was seen, so
        // `0 0 0 rg` comes back as `0 0 0 rg` instead of collapsing to `0 g`.
        string restoreColor = restoreOpText
            ?? ((restore.r == restore.g && restore.g == restore.b)
                ? string.Format(CultureInfo.InvariantCulture, "{0} g", N(restore.r))
                : string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} rg",
                    N(restore.r), N(restore.g), N(restore.b)));
        string after = renderingMode is null
            ? " " + restoreColor + " "
            : string.Format(CultureInfo.InvariantCulture, " {0} Tr {1} ",
                activeRenderingMode, restoreColor);
        var beforeBytes = Encoding.ASCII.GetBytes(before);
        var afterBytes = Encoding.ASCII.GetBytes(after);

        var result = new byte[original.Length + beforeBytes.Length + afterBytes.Length];
        int pos = 0;
        Array.Copy(original, 0, result, pos, rgInsertPos); pos += rgInsertPos;
        Array.Copy(beforeBytes, 0, result, pos, beforeBytes.Length); pos += beforeBytes.Length;
        Array.Copy(original, rgInsertPos, result, pos, restorePos - rgInsertPos); pos += restorePos - rgInsertPos;
        Array.Copy(afterBytes, 0, result, pos, afterBytes.Length); pos += afterBytes.Length;
        Array.Copy(original, restorePos, result, pos, original.Length - restorePos);
        return result;
    }
}
