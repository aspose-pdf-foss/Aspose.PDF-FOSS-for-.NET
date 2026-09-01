using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>
    /// Split of a TJ/Tj text run around a matched span: the untouched head (original
    /// elements/kerns, possibly ending with a partial string element), the trailing
    /// run to re-anchor (possibly starting with a partial string element), and the
    /// text-space pen advance from the op's origin to the suffix start AS ORIGINALLY
    /// DRAWN (glyph widths + kerns + per-glyph Tc + per-space Tw), so the suffix can
    /// be re-anchored at its exact pre-replacement position.
    /// </summary>
    private sealed class TjSplitPlan
    {
        public PdfArray Head = new();
        public PdfArray Suffix = new();
        public double SuffixAdvX;
        public bool IsHex;
        /// <summary>Pen displacement of the kern elements that separated the
        /// matched run from the suffix (folded into <see cref="SuffixAdvX"/>).</summary>
        public double LeadingGap;
    }

    /// <summary>
    /// Analyze a TJ array (a plain Tj string is a one-element array) for an anchored
    /// split around <paramref name="search"/>. Handles matches that start or end
    /// mid-element by splitting that element's bytes, provided its byte→char mapping
    /// is unambiguous (1 byte/char simple encoding or 2 bytes/char CID). Returns null
    /// when the match isn't found or the boundaries can't be mapped to bytes.
    /// </summary>
    private TjSplitPlan? ComputeTjSplit(PdfArray arr, string search,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, double fontSize,
        double tc, double tw, PdfReader reader)
    {
        if (fontDict is null || fontSize <= 0 || string.IsNullOrEmpty(search)) return null;
        FontMetrics? metrics;
        try { metrics = FontMetrics.FromFontDict(fontDict, reader); } catch { return null; }
        if (metrics is null) return null;
        bool isCid = metrics.IsCid;

        // Advance of a byte run as originally drawn: glyph widths plus per-glyph Tc
        // and, for single-byte encodings, per-space (byte 0x20) Tw — the PDF text
        // state contributions FontMetrics doesn't know about.
        double AdvOf(byte[] bytes)
        {
            if (bytes.Length == 0) return 0;
            double w = metrics!.MeasureString(bytes, fontSize);
            int glyphs = isCid ? bytes.Length / 2 : bytes.Length;
            w += glyphs * tc;
            if (!isCid && tw != 0)
                foreach (var b in bytes)
                    if (b == 0x20) w += tw;
            return w;
        }

        // Per-element char-start in the concatenated text (mirroring ConcatenateTJText's
        // synthetic-space rule) and the pen advance before each element (kern-aware).
        var charStart = new int[arr.Count];
        var localXBefore = new double[arr.Count];
        var decoded = new string?[arr.Count];
        var sb = new StringBuilder();
        var tjRule = TjBreakRuleOf(arr, toUnicode, fontDict, reader);
        double localX = 0;
        for (int i = 0; i < arr.Count; i++)
        {
            charStart[i] = sb.Length; localXBefore[i] = localX;
            if (arr[i] is PdfString s)
            {
                var dec = DecodeString(s.Value, toUnicode, fontDict, reader);
                decoded[i] = dec;
                sb.Append(dec);
                try { localX += AdvOf(s.Value); } catch { return null; }
            }
            else
            {
                double v = arr[i] is PdfInteger ai ? ai.Value : arr[i] is PdfReal ar2 ? ar2.Value : 0;
                if (tjRule.Breaks(v) && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                localX += -v * fontSize / 1000.0;
            }
        }
        var concat = sb.ToString();
        int matchStart = concat.IndexOf(search, StringComparison.Ordinal);
        int matchEnd;
        if (matchStart >= 0)
            matchEnd = matchStart + search.Length;
        else
        {
            // Normalized fallback (Arabic presentation forms): offsets aren't
            // byte-mappable in general, so only the match-at-start shape is kept
            // (pre-existing behaviour).
            var nn = NormalizeForSearch(concat);
            if (nn.IndexOf(NormalizeForSearch(search), StringComparison.Ordinal) != 0) return null;
            matchStart = 0;
            matchEnd = Math.Min(search.Length, concat.Length);
        }

        // Byte offset of a char offset within element i; -1 when the mapping is
        // ambiguous (decoded length doesn't line up with the byte count).
        int ByteOff(int i, int charOff)
        {
            var dec = decoded[i]!;
            var bytes = ((PdfString)arr[i]).Value;
            if (charOff == 0) return 0;
            if (charOff == dec.Length) return bytes.Length;
            if (bytes.Length == dec.Length) return charOff;          // 1 byte/char
            if (bytes.Length == dec.Length * 2) return charOff * 2;  // 2-byte CID
            return -1;
        }

        // Locate the elements containing the match start/end. A boundary that
        // falls exactly between elements belongs to the LATER element for the
        // start (offset 0) and the EARLIER one for the end (offset = length),
        // so partial slices stay minimal.
        int startEl = -1, endEl = -1, startOff = 0, endOff = 0;
        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not PdfString || decoded[i] is null) continue;
            int len = decoded[i]!.Length;
            if (startEl < 0 && matchStart >= charStart[i] && matchStart < charStart[i] + len)
            { startEl = i; startOff = matchStart - charStart[i]; }
            if (matchEnd > charStart[i] && matchEnd <= charStart[i] + len)
            { endEl = i; endOff = matchEnd - charStart[i]; }
        }
        if (startEl < 0) return null;
        if (endEl < 0)
        {
            // Match ends at/after the last text — trailing run is empty only if
            // it really ends past every string element.
            for (int i = arr.Count - 1; i >= 0; i--)
                if (arr[i] is PdfString && decoded[i] is not null)
                {
                    if (matchEnd < charStart[i] + decoded[i]!.Length) return null;
                    endEl = i; endOff = decoded[i]!.Length;
                    break;
                }
            if (endEl < 0) return null;
        }

        int startByte = ByteOff(startEl, startOff);
        int endByte = ByteOff(endEl, endOff);
        if (startByte < 0 || endByte < 0) return null;

        var plan = new TjSplitPlan();
        foreach (var el in arr)
            if (el is PdfString ps0) { plan.IsHex = ps0.IsHex; break; }

        // Head: whole elements before the match plus the pre-match slice.
        for (int i = 0; i < startEl; i++) plan.Head.Add(arr[i]);
        if (startByte > 0)
            plan.Head.Add(new PdfString(((PdfString)arr[startEl]).Value[..startByte], plan.IsHex));

        // Suffix: the post-match slice plus the whole elements after it.
        var endBytes = ((PdfString)arr[endEl]).Value;
        if (endByte < endBytes.Length)
            plan.Suffix.Add(new PdfString(endBytes[endByte..], plan.IsHex));
        for (int i = endEl + 1; i < arr.Count; i++) plan.Suffix.Add(arr[i]);

        plan.SuffixAdvX = localXBefore[endEl] + AdvOf(endBytes[..endByte]);

        // Fold the suffix's LEADING kerns into the anchor advance: the re-anchor Tm
        // must sit at the first trailing GLYPH's position. A kern left at the array
        // head would displace the pen after the Tm, and consumers that take a
        // fragment's origin from the operation start would report the pre-kern
        // position instead of where the trailing text actually is.
        while (plan.Suffix.Count > 0 && plan.Suffix[0] is not PdfString)
        {
            double kv = plan.Suffix[0] is PdfInteger ki2 ? ki2.Value
                : plan.Suffix[0] is PdfReal kr2 ? kr2.Value : 0;
            plan.LeadingGap += -kv * fontSize / 1000.0;
            plan.Suffix.RemoveAt(0);
        }
        plan.SuffixAdvX += plan.LeadingGap;
        return plan;
    }

    /// <summary>Emit the suffix run re-anchored at its original absolute position:
    /// Tm translated along the text matrix's X axis by the original advance, the
    /// suffix TJ, then a Tlm restore when relative positioning follows.</summary>
    private static void WriteReanchoredSuffix(MemoryStream result, TjSplitPlan plan,
        double tmA, double tmB, double tmC, double tmD, double tmTx, double tmTy,
        bool restoreTlm)
    {
        string N(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);
        // The pen advances along the text matrix's X axis: origin' = Tm·(advX, 0).
        // Adding advX to tmTx alone breaks rotated matrices (0 b -c 0), where the
        // advance lands in the Y component through tmB. Leading space: the bytes
        // copied before this op can end in a keyword ("… Tm") with no trailing
        // delimiter, and "Tm0 0.99 …" would lex as an unknown operator.
        double advX = plan.SuffixAdvX;
        result.Write(Encoding.ASCII.GetBytes(
            $" {N(tmA)} {N(tmB)} {N(tmC)} {N(tmD)} {N(tmTx + tmA * advX)} {N(tmTy + tmB * advX)} Tm "));
        WriteTJArray(result, plan.Suffix);
        result.Write(" TJ"u8);
        // Restore the line matrix: the suffix's absolute Tm also moved Tlm, but any
        // following RELATIVE positioning (Td/TD/T*/'/") computes from the Tlm that
        // was live at this op. Without the restore, the next Td-positioned line
        // inherits the suffix X and every later line shifts by the re-anchor delta.
        if (restoreTlm)
            result.Write(Encoding.ASCII.GetBytes(
                $" {N(tmA)} {N(tmB)} {N(tmC)} {N(tmD)} {N(tmTx)} {N(tmTy)} Tm"));
    }

    private bool WriteAnchoredTJSplit(MemoryStream result, PdfArray arr, string search, string replacement,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, double fontSize,
        double tmA, double tmB, double tmC, double tmD, double tmTx, double tmTy,
        double tc, double tw, PdfReader reader, bool restoreTlm)
    {
        var plan = ComputeTjSplit(arr, search, toUnicode, fontDict, fontSize, tc, tw, reader);
        if (plan is null) return false;

        if (replacement.Length > 0
            && NeedsFontSwitch(replacement, toUnicode, fontDict, reader, allowGlyphFallback: false))
            return false;

        // Head: untouched leading run plus the re-encoded replacement, one TJ.
        var headArr = new PdfArray();
        foreach (var el in plan.Head) headArr.Add(el);
        if (replacement.Length > 0)
            headArr.Add(new PdfString(EncodeString(replacement, toUnicode, fontDict), plan.IsHex));
        if (headArr.Count > 0)
        {
            WriteTJArray(result, headArr);
            result.Write(" TJ "u8);
        }

        if (plan.Suffix.Count > 0)
        {
            // A pure deletion re-anchors the suffix at the pen position right
            // after the deleted glyphs' widths: a SMALL kern separating the
            // match from the trailing run is typography deleted with the match,
            // not kept as a gap. A wide kern is layout (a tab-stop / column
            // separator, same idea as the column-kern rule in the font-switch
            // path) — the suffix keeps its original position. (A replacement
            // always keeps the gap — the new text fills the matched span.)
            if (replacement.Length == 0 && plan.LeadingGap < 0.5 * fontSize)
                plan.SuffixAdvX -= plan.LeadingGap;
            WriteReanchoredSuffix(result, plan, tmA, tmB, tmC, tmD, tmTx, tmTy, restoreTlm);
        }
        return true;
    }

    private bool WriteFontSwitchedTJSplit(MemoryStream result, PdfArray arr, string search, string replacement,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, string? fontName, double fontSize,
        double tmA, double tmB, double tmC, double tmD, double tmTx, double tmTy,
        double tc, double tw, PdfReader reader, PdfDictionary pageDict, bool restoreTlm,
        bool anchored)
    {
        if (string.IsNullOrEmpty(fontName)) return false;
        var plan = ComputeTjSplit(arr, search, toUnicode, fontDict, fontSize, tc, tw, reader);
        // No trailing text → nothing to re-anchor → let the caller flatten.
        if (plan is null || plan.Suffix.Count == 0) return false;

        // Under a reflowing mode the run normally flattens (trailing text closes up
        // behind the replacement). But a trailing run separated by a COLUMN-width
        // kern is an independently placed block (a tab-stop / form-column layout),
        // not line flow — it keeps its own position, so split and re-anchor it.
        if (!anchored && plan.LeadingGap < 2 * fontSize) return false;

        // Resolve the switched font BEFORE any output so a failed embed leaves the
        // result stream untouched (the caller then flattens).
        var cid = EmbedTimesCidForRun(pageDict, reader, replacement, fontDict);
        if (cid is not { } c) return false;

        // Untouched leading run replays first (original font still selected), putting
        // the pen exactly at the match start.
        if (plan.Head.Count > 0)
        {
            WriteTJArray(result, plan.Head);
            result.Write(" TJ "u8);
        }

        // Font-switched replacement for the matched run (drawn at the current pen).
        var fs = fontSize.ToString("0.####", CultureInfo.InvariantCulture);
        result.Write(Encoding.ASCII.GetBytes($"/{c.resName} {fs} Tf <"));
        result.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(c.hexIds)));
        result.Write(Encoding.ASCII.GetBytes("> Tj "));

        // Trailing run back in the original font, re-anchored at its original
        // absolute position independent of the replacement width.
        result.Write(Encoding.ASCII.GetBytes($"/{fontName} {fs} Tf"));
        WriteReanchoredSuffix(result, plan, tmA, tmB, tmC, tmD, tmTx, tmTy, restoreTlm);
        return true;
    }

    /// <summary>
    /// Write a font-switched replacement show operator. For non-Latin1 text
    /// (Cyrillic/CJK) embed a Type0 CID fallback so the run renders + is
    /// searchable; otherwise fall back to the Standard-14 Helvetica + Latin1 path
    /// (unchanged behaviour for Latin replacements). Restores the original font
    /// afterwards. <paramref name="showOp"/> is "Tj" or "'".
    /// </summary>
    private static void WriteFontSwitchedReplacement(MemoryStream result, string newText,
        PdfDictionary? currentFontDict, string? currentFontName, double currentFontSize,
        PdfDictionary pageDict, PdfReader reader, string showOp, bool allowGlyphFallback = false,
        string? forcedCidFamily = null)
    {
        var fs = currentFontSize.ToString("F1", CultureInfo.InvariantCulture);
        // A CID source font that cannot encode the replacement switches to the CID
        // fallback family for Latin-1 text as well (the same face a non-Latin run gets),
        // so both replaced runs of one document land in one substitute font.
        var cidSource = currentFontDict?.GetName("Subtype") == "Type0";
        if (newText.Any(c => c > 0xFF) || cidSource)
        {
            var cid = TryEmbedCidFallback(pageDict, reader, newText, currentFontDict, forcedCidFamily);
            if (cid is { } c)
            {
                result.Write(Encoding.ASCII.GetBytes($"/{c.resName} {fs} Tf <"));
                result.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(c.hexIds)));
                result.Write(Encoding.ASCII.GetBytes($"> {showOp} /{currentFontName} {fs} Tf"));
                return;
            }
        }
        // Latin replacement whose glyphs are absent from the source subset font: substitute
        // the whole run in a Times New Roman Type0/CID subset, so the
        // missing glyphs render AND the run stays searchable via the embedder's /ToUnicode.
        else if (allowGlyphFallback && SimpleFontMissingGlyphChars(currentFontDict, reader, newText).Length > 0)
        {
            var times = EmbedTimesCidForRun(pageDict, reader, newText, currentFontDict);
            if (times is { } t)
            {
                result.Write(Encoding.ASCII.GetBytes($"/{t.resName} {fs} Tf <"));
                result.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(t.hexIds)));
                result.Write(Encoding.ASCII.GetBytes($"> {showOp} /{currentFontName} {fs} Tf"));
                return;
            }
        }
        // Standard-font substitution for a run the source subset can't faithfully show (its
        // glyph is present by width but absent from the font's ToUnicode, so the encoding
        // can't be confirmed). Record the family the fragment should REPORT for the default
        // no-character behaviour (source family if installed, else Times New Roman). This is
        // a REPORT ONLY — the glyphs stay on this cheap Standard-14 path (no font embedded,
        // file size unaffected), and only the TextFragment.Text setter reads the record; the
        // facade ReplaceText path never surfaces it, so its output is byte-for-byte unchanged.
        if (allowGlyphFallback && IsEmbeddedSimpleFont(currentFontDict, reader))
            RecordSwitchedFont(ResolveReportedFallbackFamily(currentFontDict));
        var fallbackFont = EnsureStandardFont(pageDict, reader);
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_REPLDEBUG") == "1")
            Console.Error.WriteLine($"[fallback-emit] newText='{newText}' font={currentFontName} fs={fs}");
        result.Write(Encoding.ASCII.GetBytes($"/{fallbackFont} {fs} Tf "));
        var latin = Encoding.Latin1.GetBytes(newText);
        WriteStringOperand(result, latin, false);
        result.Write(Encoding.ASCII.GetBytes($" {showOp} /{currentFontName} {fs} Tf"));
    }
}
