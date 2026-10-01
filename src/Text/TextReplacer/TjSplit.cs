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
    private TjSplitPlan? ComputeTjSplit(PdfArray arr, string search, Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, double fontSize, double tc, double tw, PdfReader reader)
    {
        var js = new TjSplitComputeState();
        js.arr = arr;
        js.search = search;
        js.toUnicode = toUnicode;
        js.fontDict = fontDict;
        js.fontSize = fontSize;
        js.tc = tc;
        js.tw = tw;
        js.reader = reader;
        if (js.fontDict is null || js.fontSize <= 0 || string.IsNullOrEmpty(js.search)) return null;
        try { js.metrics = FontMetrics.FromFontDict(js.fontDict, js.reader); } catch { return null; }
        if (js.metrics is null) return null;
        js.isCid = js.metrics.IsCid;

        js.charStart = new int[js.arr.Count];
        js.localXBefore = new double[js.arr.Count];
        js.decoded = new string?[js.arr.Count];
        js.sb = new StringBuilder();
        js.tjRule = TjBreakRuleOf(js.arr, js.toUnicode, js.fontDict, js.reader);
        js.localX = 0;
        for (int i = 0; i < js.arr.Count; i++)
        {
            js.charStart[i] = js.sb.Length; js.localXBefore[i] = js.localX;
            if (js.arr[i] is PdfString s)
            {
                var dec = DecodeString(s.Value, js.toUnicode, js.fontDict, js.reader);
                js.decoded[i] = dec;
                js.sb.Append(dec);
                try { js.localX += AdvOf(js, s.Value); } catch { return null; }
            }
            else
            {
                double v = js.arr[i] is PdfInteger ai ? ai.Value : js.arr[i] is PdfReal ar2 ? ar2.Value : 0;
                if (js.tjRule.Breaks(v) && js.sb.Length > 0 && js.sb[^1] != ' ') js.sb.Append(' ');
                js.localX += -v * js.fontSize / 1000.0;
            }
        }
        js.concat = js.sb.ToString();
        js.matchStart = js.concat.IndexOf(js.search, StringComparison.Ordinal);
        if (js.matchStart >= 0)
            js.matchEnd = js.matchStart + js.search.Length;
        else
        {
            // Normalized fallback (Arabic presentation forms): offsets aren't
            // byte-mappable in general, so only the match-at-start shape is kept
            // (pre-existing behaviour).
            var nn = NormalizeForSearch(js.concat);
            if (nn.IndexOf(NormalizeForSearch(js.search), StringComparison.Ordinal) != 0) return null;
            js.matchStart = 0;
            js.matchEnd = Math.Min(js.search.Length, js.concat.Length);
        }

        if (!MapTjMatchToElements(js)) return null;

        js.startByte = ByteOff(js, js.startEl, js.startOff);
        js.endByte = ByteOff(js, js.endEl, js.endOff);
        if (js.startByte < 0 || js.endByte < 0) return null;

        js.plan = new TjSplitPlan();
        foreach (var el in js.arr)
            if (el is PdfString ps0) { js.plan.IsHex = ps0.IsHex; break; }

        // Head: whole elements before the match plus the pre-match slice.
        for (int i = 0; i < js.startEl; i++) js.plan.Head.Add(js.arr[i]);
        if (js.startByte > 0)
            js.plan.Head.Add(new PdfString(((PdfString)js.arr[js.startEl]).Value[..js.startByte], js.plan.IsHex));

        js.endBytes = ((PdfString)js.arr[js.endEl]).Value;
        if (js.endByte < js.endBytes.Length)
            js.plan.Suffix.Add(new PdfString(js.endBytes[js.endByte..], js.plan.IsHex));
        for (int i = js.endEl + 1; i < js.arr.Count; i++) js.plan.Suffix.Add(js.arr[i]);

        js.plan.SuffixAdvX = js.localXBefore[js.endEl] + AdvOf(js, js.endBytes[..js.endByte]);

        // Fold the suffix's LEADING kerns into the anchor advance: the re-anchor Tm
        // must sit at the first trailing GLYPH's position. A kern left at the array
        // head would displace the pen after the Tm, and consumers that take a
        // fragment's origin from the operation start would report the pre-kern
        // position instead of where the trailing text actually is.
        while (js.plan.Suffix.Count > 0 && js.plan.Suffix[0] is not PdfString)
        {
            double kv = js.plan.Suffix[0] is PdfInteger ki2 ? ki2.Value
                : js.plan.Suffix[0] is PdfReal kr2 ? kr2.Value : 0;
            js.plan.LeadingGap += -kv * js.fontSize / 1000.0;
            js.plan.Suffix.RemoveAt(0);
        }
        js.plan.SuffixAdvX += js.plan.LeadingGap;
        return js.plan;
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
        result.Write(Encoding.ASCII.GetBytes(Compat.ToHexString(c.hexIds)));
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
                result.Write(Encoding.ASCII.GetBytes(Compat.ToHexString(c.hexIds)));
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
                result.Write(Encoding.ASCII.GetBytes(Compat.ToHexString(t.hexIds)));
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
        var latin = Compat.Latin1.GetBytes(newText);
        WriteStringOperand(result, latin, false);
        result.Write(Encoding.ASCII.GetBytes($" {showOp} /{currentFontName} {fs} Tf"));
    }
}
