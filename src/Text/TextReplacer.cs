using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Finds and replaces text in PDF content streams.
/// Only handles simple WinAnsiEncoding / Latin1 text operators (Tj, TJ, ', ").
/// For CIDFont / Type0 fonts with identity encoding, replacement is limited to
/// characters already present in the font.
/// </summary>
public sealed partial class TextReplacer
{
    private int _replacementCount;

    private bool _isRegex;

    private Regex? _regexPattern;

    private bool _allowCrossOperator;

    /// <summary>When true, <see cref="Replace(Page,string,string,bool)"/> also matches search
    /// text that spans multiple text operators (a word drawn glyph-by-glyph). Off by default;
    /// the rectangle/fragment replace path turns it on and relies on <see cref="TargetY"/>/
    /// <see cref="TargetX"/> to keep the cross-operator replacement inside the target region.</summary>
    internal bool AllowCrossOperator { get => _allowCrossOperator; set => _allowCrossOperator = value; }

    /// <summary>Number of replacements made in the last Replace call.</summary>
    public int ReplacementCount => _replacementCount;

    /// <summary>When true, a cross-operator replacement re-flows the rest of the LINE:
    /// the kept tail after the match is re-anchored at match-start + replacement width,
    /// and following same-line absolute-Tm runs shift left/right by the width delta —
    /// giving a same-line reflow when replacing with shorter/longer
    /// text. Off by default: page/document-wide Replace keeps every surviving glyph at its
    /// original position. Set by the <see cref="TextFragment.Text"/> setter.</summary>
    internal bool ReflowLineOnReplace { get; set; }

    /// <summary>
    /// When true, stop after the first match. Set by callers to honour
    /// <c>ReplaceTextStrategy.Scope.ReplaceFirst</c>. Default is replace-all.
    /// </summary>
    public bool ReplaceFirstOnly { get; set; }

    /// <summary>
    /// Redaction mode: when a match is fully deleted (replacement is empty),
    /// emit a TJ advance equal to the removed text's width instead of dropping
    /// the show operator, so following text on the same line keeps its position
    /// (no reflow). The glyphs are still gone from the content — extraction can
    /// no longer find them — but the layout is preserved. Used by
    /// <c>RedactionAnnotation.Redact()</c>.
    /// </summary>
    internal bool PreserveAdvanceOnDelete { get; set; }

    /// <summary>
    /// When true, a full deletion (replacement is empty) keeps an empty
    /// <c>() Tj</c> show operator instead of dropping it, so the emptied
    /// fragment is still re-extractable as a zero-length fragment. Set by the
    /// <see cref="Replace(XForm, string, string)"/> path (clearing a Form
    /// XObject's text) so an emptied form field remains a zero-length fragment
    /// in place rather than disappearing. Off by default so page-level
    /// deletions keep dropping the operator (no spurious empty fragments).
    /// </summary>
    internal bool KeepEmptyShowOperator { get; set; }

    /// <summary>
    /// When set, a replacement whose glyphs are absent from the source embedded subset
    /// font is font-switched to a fallback (source family if installed, else Times) so the
    /// missing glyphs render via whole-run substitution. Enabled only
    /// on the facade <c>PdfContentEditor.ReplaceText</c> path (which owns the whole
    /// replacement); the <c>TextFragment.Text</c> setter leaves it off, since that caller
    /// manages the font itself (e.g. sets <c>TextState.Font</c> after the text) and an
    /// auto-switch would change the run width and shift following text.
    /// </summary>
    internal bool AllowSubsetGlyphFallback { get; set; }

    // When a subset-glyph fallback substitutes the whole run in a different face
    // (source family / Times), record that family so the caller can report it on
    // the fragment's TextState.Font (matching the default no-character behaviour
    // that surfaces the substituted font). Thread-static because the embed helpers
    // that resolve the family are static; reset per replacement via ResetSwitchedFont.
    [ThreadStatic] private static string? s_switchedFontFamily;

    internal string? SwitchedFontFamily => s_switchedFontFamily;

    internal static void ResetSwitchedFont() => s_switchedFontFamily = null;

    internal static void RecordSwitchedFont(string family) => s_switchedFontFamily = family;

    // Emit `[ kern ] TJ` — an advance with no glyphs — that moves the text
    // position right by the width of <paramref name="removedBytes"/>, so text
    // after a fully-deleted run stays put. No-op (writes nothing) when metrics
    // are unavailable or the width is negligible.
    private void WriteDeletionAdvance(MemoryStream result, byte[] removedBytes,
        PdfDictionary? fontDict, PdfReader reader, double fontSize)
    {
        if (!PreserveAdvanceOnDelete || fontDict is null || fontSize <= 0) return;
        double width;
        try
        {
            var metrics = FontMetrics.FromFontDict(fontDict, reader);
            if (metrics is null) return;
            width = metrics.MeasureString(removedBytes, fontSize);
        }
        catch { return; }
        WriteAdvance(result, width, fontSize);
    }

    // Width-preserving advance for a fully-deleted TJ array: total advance = sum of
    // the strings' widths minus the kerning numbers (scaled), so the whole operator
    // is replaced by a glyph-less advance of the same width.
    private void WriteDeletionAdvanceTJ(MemoryStream result, PdfArray arr,
        PdfDictionary? fontDict, PdfReader reader, double fontSize)
    {
        if (!PreserveAdvanceOnDelete || fontDict is null || fontSize <= 0) return;
        double width;
        try
        {
            var metrics = FontMetrics.FromFontDict(fontDict, reader);
            if (metrics is null) return;
            width = 0;
            foreach (var el in arr)
            {
                if (el is PdfString ps) width += metrics.MeasureString(ps.Value, fontSize);
                else if (el is PdfInteger pi) width += -pi.Value * fontSize / 1000.0;
                else if (el is PdfReal pr) width += -pr.Value * fontSize / 1000.0;
            }
        }
        catch { return; }
        WriteAdvance(result, width, fontSize);
    }

    private static void WriteAdvance(MemoryStream result, double width, double fontSize)
    {
        if (width <= 0.05) return;
        // PDF TJ: a number is subtracted from the advance (positive = shift left),
        // so a NEGATIVE number advances right by width.
        var kern = (int)Math.Round(-width * 1000.0 / fontSize);
        if (kern == 0) return;
        result.Write(Encoding.ASCII.GetBytes($"[{kern}] TJ"));
    }

    /// <summary>
    /// When set, only replace inside text-showing operators whose composed
    /// page-space Y (Tm.ty × CTM[3] + CTM[5]) is within
    /// <see cref="TargetYTolerance"/> of <see cref="TargetY"/>. Used by the
    /// per-fragment <c>TextFragment.Text</c> setter to scope the replacement
    /// to the operator that produced this fragment, instead of every matching
    /// occurrence on the page (otherwise iterating fragments[i].Text in a loop
    /// re-replaces the substring "X" inside "X-changed" each pass and the
    /// replacement string accumulates).
    /// </summary>
    internal double? TargetY { get; set; }

    /// <summary>
    /// Y-coordinate tolerance for <see cref="TargetY"/> matching, in PDF
    /// points. Default 6pt — wide enough to absorb the descent offset that
    /// <c>TextFragmentAbsorber.ComputeSegmentPosition</c> bakes into
    /// <c>Position.YIndent</c> (~2-3pt for 12pt body fonts), tight enough to
    /// distinguish text on adjacent lines (line height usually ≥ 12pt).
    /// </summary>
    internal double TargetYTolerance { get; set; } = 6.0;

    /// <summary>
    /// When set, only replace inside text-showing operators whose composed
    /// page-space X (Tm.tx × CTM[0] + Tm.ty × CTM[2] + CTM[4]) is within
    /// <see cref="TargetXTolerance"/> of <see cref="TargetX"/>. Used together
    /// with <see cref="TargetY"/> to scope a replacement to a single fragment's
    /// position — needed for region-scoped (rectangle) replacement where several
    /// matches share a baseline Y but only some fall inside the rectangle.
    /// </summary>
    internal double? TargetX { get; set; }

    /// <summary>X-coordinate tolerance for <see cref="TargetX"/>, in PDF points.
    /// Tight enough to distinguish neighbouring words on a line (typically spaced
    /// well beyond this), loose enough to absorb origin-vs-glyph-start rounding.</summary>
    internal double TargetXTolerance { get; set; } = 4.0;

    /// <summary>
    /// When true, a text-showing operator matches only if its entire shown text
    /// equals the search string (not merely contains it). Used by fragment
    /// deletion so removing a short fragment such as "$" does not strip the same
    /// substring out of a longer operator such as "$ 200.00" on the same row.
    /// </summary>
    internal bool MatchWholeOperator { get; set; }

    /// <summary>
    /// When set, only text-showing operators drawn under this text render mode
    /// (Tr) match. Used by invisible-fragment deletion: an OCR/searchable overlay
    /// draws each phrase twice — once visible (Tr 0) and once invisible (Tr 3) at
    /// nearly the same position — so a delete scoped only by Y/X would strip the
    /// wrong (visible) copy. Restricting to Tr 3 targets exactly the invisible one.
    /// </summary>
    internal int? RequiredRenderMode { get; set; }

    /// <summary>
    /// When true, EVERY text-showing operator whose start position passes the
    /// <see cref="TargetY"/>/<see cref="TargetX"/> scoping is treated as a whole-operator
    /// match and replaced with the replacement text (typically empty = the operator is
    /// deleted), regardless of what it shows. Used to clear a page region at operator
    /// granularity — producers that draw one word (or one space) per operator defeat
    /// text-keyed deletion because no single operator's decode equals the coalesced
    /// segment text. Only deletion (empty replacement) is supported.
    /// </summary>
    internal bool MatchAnyOperator { get; set; }

    /// <summary>
    /// When true (TextFragment.Text setter under ReplaceAdjustment.None), a TJ
    /// rewrite with trailing text re-anchors the tail at its ORIGINAL absolute Tm so
    /// surrounding text keeps its exact position regardless of the replacement's
    /// width. Default false = the tail flows with the width delta (the
    /// default replace behaviour, also what redaction and the facades expect).
    /// </summary>
    internal bool AnchorTrailingOnReplace { get; set; }

    /// <summary>
    /// Replace all occurrences of <paramref name="search"/> with <paramref name="replacement"/>
    /// in the given page's content stream(s).
    /// </summary>
    public void Replace(Page page, string search, string replacement)
    {
        Replace(page, search, replacement, false);
    }

    /// <summary>
    /// Replace with cross-operator support enabled.
    /// Used when the caller (e.g., TextFragment.Text setter) knows the text exists
    /// as a cross-operator fragment.
    /// </summary>
    public void ReplaceWithCrossOperator(Page page, string search, string replacement)
    {
        _allowCrossOperator = true;
        Replace(page, search, replacement, false);
        _allowCrossOperator = false;
    }

    /// <summary>
    /// Replace occurrences of <paramref name="search"/> with <paramref name="replacement"/>
    /// in the given page's content stream(s). When <paramref name="isRegex"/> is <c>true</c>,
    /// <paramref name="search"/> is treated as a regular expression pattern.
    /// </summary>
    public void Replace(Page page, string search, string replacement, bool isRegex)
    {
        _replacementCount = 0;
        if (string.IsNullOrEmpty(search) && !MatchAnyOperator) return;
        _isRegex = isRegex;
        _regexPattern = isRegex ? new Regex(search) : null;
        var reader = page.Reader;
        var processedXObjects = new HashSet<int>();

        // Walk the page's content stream first — Form XObjects invoked via /Do
        // are processed recursively from inside the walk so the parent's CTM at
        // each Do site flows into the XObject's text-matrix math (TargetY
        // scoping needs that composition; otherwise positions computed by the
        // absorber after the parent's cm don't line up with the XObject's
        // local Tm.ty values).
        var contentStreams = GetContentStreams(page, reader);
        if (contentStreams.Count > 0)
        {
            var combined = CombineStreams(contentStreams);
            var (rA, rB, rC, rD, rTx, rTy) = PageRotationSeed(page);
            var replaced = ReplaceInContentStream(combined, search, replacement,
                page.Dict, reader, processedXObjects, rA, rB, rC, rD, rTx, rTy);
            if (_replacementCount > 0)
            {
                page.SetContentStream(WrapInGraphicsState(replaced));
            }
        }

        // Catch-all: any Form XObject in the page's resources that wasn't
        // reached via /Do (e.g. unreferenced legacy entries) still gets a pass
        // with identity CTM, mirroring the prior behaviour.
        ReplaceInFormXObjects(page.Dict, reader, search, replacement, processedXObjects);

        _isRegex = false;
        _regexPattern = null;
    }

    /// <summary>Bracket replace-rewritten page content in a single q…Q pair to
    /// isolate the rewritten stream. Content that already BEGINS with a q is returned
    /// unchanged (content opening with q keeps its operator indices after an edit,
    /// while content opening with a BDC gains exactly one wrapper), which keeps
    /// repeated absorber passes from nesting graphics-state operators.</summary>
    internal static byte[] WrapInGraphicsState(byte[] content)
    {
        var ops = ContentStreamOperatorParser.ParseOperators(content);
        if (ops.Count >= 1 && ops[0] == "q")
            return content;

        var prefix = System.Text.Encoding.ASCII.GetBytes("q\n");
        var suffix = System.Text.Encoding.ASCII.GetBytes("\nQ\n");
        var wrapped = new byte[prefix.Length + content.Length + suffix.Length];
        prefix.CopyTo(wrapped, 0);
        content.CopyTo(wrapped, prefix.Length);
        suffix.CopyTo(wrapped, prefix.Length + content.Length);
        return wrapped;
    }

    /// <summary>
    /// Replace text across all pages of a document, including Form XObjects.
    /// </summary>
    public void Replace(Document document, string search, string replacement)
        => Replace(document, search, replacement, false);

    /// <summary>
    /// Replace text across all pages of a document, including Form XObjects.
    /// When <paramref name="isRegex"/> is true, <paramref name="search"/> is a .NET regex.
    /// </summary>
    public void Replace(Document document, string search, string replacement, bool isRegex)
    {
        _replacementCount = 0;
        if (string.IsNullOrEmpty(search)) return;
        _isRegex = isRegex;
        _regexPattern = isRegex ? new Regex(search) : null;
        // Enable cross-operator replacement so matches that span separate
        // Tj/TJ operators (decoded text crossing positioning operators) are
        // not silently missed by the per-op matcher.
        var prevAllowCross = _allowCrossOperator;
        _allowCrossOperator = true;
        var processedXObjects = new HashSet<int>(); // track by obj number to avoid double processing

        try
        {
            foreach (var page in document.Pages)
            {
                if (ReplaceFirstOnly && _replacementCount > 0) break;
                var reader = page.Reader;

                // Walk page content first; XObjects are recursed via /Do.
                var contentStreams = GetContentStreams(page, reader);
                if (contentStreams.Count > 0)
                {
                    var combined = CombineStreams(contentStreams);
                    var count = _replacementCount;
                    var (rA, rB, rC, rD, rTx, rTy) = PageRotationSeed(page);
                    var replaced = ReplaceInContentStream(combined, search, replacement,
                        page.Dict, reader, processedXObjects, rA, rB, rC, rD, rTx, rTy);

                    if (_replacementCount > count)
                    {
                        page.SetContentStream(WrapInGraphicsState(replaced));
                    }
                }

                // Catch-all for XObjects not reached via /Do.
                ReplaceInFormXObjects(page.Dict, reader, search, replacement, processedXObjects);
            }
        }
        finally
        {
            _isRegex = false;
            _regexPattern = null;
            _allowCrossOperator = prevAllowCross;
        }
    }

    /// <summary>
    /// Replace text within a single Form XObject's own content stream (and any
    /// Form XObjects nested in its resources). Used by the TextFragment.Text setter
    /// for fragments extracted via TextFragmentAbsorber.Visit(XForm), which carry a
    /// null SourcePage — the producing operator lives in the form, not the page.
    /// </summary>
    public void Replace(XForm form, string search, string replacement)
    {
        _replacementCount = 0;
        if (form is null || string.IsNullOrEmpty(search)) return;
        var reader = form.Reader;
        var processed = new HashSet<int>();
        // Cross-operator on: form text frequently spans separate Tj/TJ operators.
        var prevAllowCross = _allowCrossOperator;
        _allowCrossOperator = true;
        // Clearing a form's text should leave an empty, re-extractable fragment
        // (an emptied Typewriter-form field stays in place), so retain the
        // empty show operator on full deletion only for this form path.
        var prevKeepEmpty = KeepEmptyShowOperator;
        KeepEmptyShowOperator = true;
        try
        {
            // Nested Form XObjects first (identity CTM catch-all).
            ReplaceInFormXObjects(form.StreamDict, reader, search, replacement, processed);
            var decoded = form.DecodedBytes;
            var replaced = ReplaceInContentStream(decoded, search, replacement,
                form.StreamDict, reader, processed);
            if (_replacementCount > 0)
                form.SetDecodedContent(replaced);
        }
        finally
        {
            _allowCrossOperator = prevAllowCross;
            KeepEmptyShowOperator = prevKeepEmpty;
        }
    }

    /// <summary>
    /// Process Form XObjects referenced from a page/XObject's Resources/XObject dict.
    /// Updates each XObject's content stream in-place (via the reader cache).
    /// </summary>
    private void ReplaceInFormXObjects(PdfDictionary dict, PdfReader reader,
        string search, string replacement, HashSet<int> processed)
    {
        var resources = reader.ResolveDict(dict.Get("Resources"));
        if (resources is null) return;
        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;

        foreach (var key in xobjects.Keys)
        {
            var xobjRef = xobjects.Get(key);

            // Only deduplicate indirect refs (we need object number)
            if (xobjRef is PdfIndirectRef indRef && !processed.Add(indRef.ObjectNumber))
                continue;

            var xobjStream = reader.ResolveStream(xobjRef);
            if (xobjStream is null || xobjStream.Dict.GetName("Subtype") != "Form") continue;

            // Recursively process nested XObjects within this Form XObject
            ReplaceInFormXObjects(xobjStream.Dict, reader, search, replacement, processed);

            // Process the Form XObject's own content
            var decoded = reader.DecodeStream(xobjStream);
            var countBefore = _replacementCount;
            var replaced = ReplaceInContentStream(decoded, search, replacement,
                xobjStream.Dict, reader, processed);

            if (_replacementCount > countBefore)
            {
                // Update the cached PdfStream with modified (uncompressed) content.
                // Remove the existing filter so PdfWriter will re-compress correctly.
                xobjStream.Dict.Remove("Filter");
                xobjStream.Dict.Remove("DecodeParms");
                xobjStream.ReplaceData(replaced);
                // Save re-emits an existing object only when it is registered
                // dirty; without this the edit silently reverts on save.
                if (xobjRef is PdfIndirectRef ir)
                    reader.OwnerDocument?.MarkDirty(ir.ObjectNumber, xobjStream);
            }
        }
    }

    /// <summary>Initial CTM for a page's operator walk: the page-rotation matrix
    /// (identity for Rotate 0). The absorber reports every fragment coordinate in
    /// VIEWER space (rotation applied), and <see cref="TargetY"/>/<see cref="TargetX"/>
    /// are set from those fragments — so the walk's position gating must live in the
    /// same space. Matrices mirror TextFragmentAbsorber.PageRotationCtm.</summary>
    private static (double a, double b, double c, double d, double tx, double ty) PageRotationSeed(Page page)
    {
        var rotate = ((page.RotateDegrees % 360) + 360) % 360;
        if (rotate == 0) return (1, 0, 0, 1, 0, 0);
        var mb = page.MediaBox;
        var w = mb.URX - mb.LLX;
        var h = mb.URY - mb.LLY;
        return rotate switch
        {
            90 => (0, -1, 1, 0, 0, w),
            180 => (-1, 0, 0, -1, w, h),
            270 => (0, 1, -1, 0, h, 0),
            _ => (1, 0, 0, 1, 0, 0),
        };
    }

    /// <summary>WholeWordsHyphenation reflow: keeps every original
    /// text-showing run VERBATIM (same bytes, kerns, font, per-run Tc) and only
    /// repositions runs by inserting an absolute Tm before (and a restoring Tm after)
    /// each moved run. Only the matched run itself is rewritten, re-encoded in its
    /// ORIGINAL font. Every run after the match shifts by the replacement's width
    /// delta; runs that would cross <paramref name="rightMargin"/> split at a space
    /// glyph and wrap onto the next original baseline; later lines pull up greedily
    /// with their original inter-run gaps preserved. Returns false when this page's
    /// structure can't be handled (CID font, missing glyphs, match not at a run
    /// start) so the caller can fall back to coarser strategies.</summary>
    /// <summary>Number of lines the last successful <see cref="ReflowFromMatch"/>
    /// CREATED beyond the paragraph's existing baselines (0 when everything fit).</summary>
    internal int ReflowCreatedLines { get; private set; }
}
