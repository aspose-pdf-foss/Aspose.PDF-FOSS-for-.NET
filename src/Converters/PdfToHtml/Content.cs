using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    // ── CTM tracking ────────────────────────────────────────────────────

    private sealed class CtmState
    {
        public double A = 1, B, C, D = 1, E, F;

        public CtmState Clone() => new()
        {
            A = A, B = B, C = C, D = D, E = E, F = F
        };

        /// <summary>
        /// Multiply this CTM by a new matrix: this = new * this
        /// (PDF spec: the new matrix pre-multiplies the current CTM)
        /// </summary>
        public void Concat(double a, double b, double c, double d, double e, double f)
        {
            var na = a * A + b * C;
            var nb = a * B + b * D;
            var nc = c * A + d * C;
            var nd = c * B + d * D;
            var ne = e * A + f * C + E;
            var nf = e * B + f * D + F;
            A = na; B = nb; C = nc; D = nd; E = ne; F = nf;
        }

        /// <summary>Overwrite with the given matrix (used by the Tm operator, which
        /// sets — not concatenates — the text matrix).</summary>
        public void Set(double a, double b, double c, double d, double e, double f)
        {
            A = a; B = b; C = c; D = d; E = e; F = f;
        }

        public void CopyFrom(CtmState o) { A = o.A; B = o.B; C = o.C; D = o.D; E = o.E; F = o.F; }

        /// <summary>Isotropic scale factor (√|det|) — what a hairline's width scales by.</summary>
        public double Scale => Math.Sqrt(Math.Abs(A * D - B * C));

        /// <summary>Product <c>this · other</c> (this applied first, then other) — the
        /// composition used to map text space through the text matrix and then the CTM.</summary>
        public CtmState Times(CtmState o) => new()
        {
            A = A * o.A + B * o.C,
            B = A * o.B + B * o.D,
            C = C * o.A + D * o.C,
            D = C * o.B + D * o.D,
            E = E * o.A + F * o.C + o.E,
            F = E * o.B + F * o.D + o.F,
        };
    }

    // ── Path tracking ───────────────────────────────────────────────────

    private sealed class PathState
    {
        public readonly StringBuilder Data = new();
        public double StrokeR, StrokeG, StrokeB;
        public double FillR, FillG, FillB;
        public double LineWidth = 1.0;

        public void Clear() => Data.Clear();
    }

    // Baselines within this much of each other are the same line, matching the
    // grouping tolerance used for a continuing show.
    const double ParkBaselineTolPt = 0.5;

    // Sub-point slack for pen-position comparisons: a producer restates a
    // continuation's x from its own accumulator, which differs from ours by
    // rounding (observed up to ~0.1 pt); 0.5 pt covers that while staying far
    // under the narrowest meaningful gap (a word space, 2.5+ pt at body sizes).
    // The same slack the pre-existing backward-draw and off-page rules use.
    const double PenSlackPt = 0.5;

    private static void RenderContentToHtml(byte[] streamBytes,
        Dictionary<string, HtmlFontRecord> fonts,
        Dictionary<string, ImageXObject> imageXObjects,
        PdfReader reader,
        StringBuilder sb, double pageHeight, double pageWidth,
        bool saveTransparentTexts,
        bool emCompensation = false,
        bool textOnly = false, StringBuilder? externalSvgPaths = null,
        ExternalImageSink? imageSink = null,
        StyleRegistry? styleReg = null, ClassNamer classNamer = default,
        List<LinkTarget>? linkTargets = null,
        PdfDictionary? resources = null, bool preferFontCmap = false,
        Dictionary<int, LigatureSubstitutor>? substitutors = null,
        CtmState? initialCtm = null, HashSet<PdfStream>? visitedForms = null,
        RotationRegistry? rotReg = null, bool cssTextDecorations = false,
        double pageLLX = 0, double yTopRef = double.NaN, ZCounter? zCounter = null,
        string? defaultFontName = null, bool authoredPathShape = false,
        Dictionary<string, (string Name, string? GroupTitle)>? ocLayers = null,
        bool pageTurnedOver = false)
    {
        var ct = new ContentRenderState();
        ct.streamBytes = streamBytes;
        ct.fonts = fonts;
        ct.imageXObjects = imageXObjects;
        ct.reader = reader;
        ct.sb = sb;
        ct.pageHeight = pageHeight;
        ct.pageWidth = pageWidth;
        ct.saveTransparentTexts = saveTransparentTexts;
        ct.emCompensation = emCompensation;
        ct.textOnly = textOnly;
        ct.externalSvgPaths = externalSvgPaths;
        ct.imageSink = imageSink;
        ct.styleReg = styleReg;
        ct.classNamer = classNamer;
        ct.linkTargets = linkTargets;
        ct.resources = resources;
        ct.preferFontCmap = preferFontCmap;
        ct.substitutors = substitutors;
        ct.initialCtm = initialCtm;
        ct.visitedForms = visitedForms;
        ct.rotReg = rotReg;
        ct.cssTextDecorations = cssTextDecorations;
        ct.pageLLX = pageLLX;
        ct.yTopRef = yTopRef;
        ct.zCounter = zCounter;
        ct.defaultFontName = defaultFontName;
        ct.authoredPathShape = authoredPathShape;
        ct.ocLayers = ocLayers;
        ct.pageTurnedOver = pageTurnedOver;
        InitContentRenderState(ct);

        // Assemble the group's segments in visual (x-ascending) order. The sort is
        // stable, so consecutive shows reported at the same x (no repositioning
        // between them) keep their stream order. The text-only overlay joins
        // adjacent word segments with a space (as before, just between positional
        // neighbours now); the normal path concatenates directly — justified lines
        // are split into abutting segments that already carry their own spacing.
        // Close every line this page still holds open, in first-use order — the
        // order legacy emission produces when lines are drawn one after another.
        // Anything that is not more text (an image entering the text layer, a
        // layer box, a form, the end of the stream) ends the parking window, so
        // text never crosses such content.
        // Show one decoded run. The visible position and glyph size come from the
        // text matrix composed with the CTM (text space → device space), so a scale
        // carried in Tm/CTM rather than Tf is honoured. Consecutive shows on the same
        // baseline with the same appearance are merged into one span: a justified line
        // is split into abutting segments — often mid-word ("laborat" + "ory") — that
        // already carry their own spacing, so in normal mode they are concatenated
        // directly; the PNG-overlay (textOnly) mode joins separately-positioned words
        // with a space.
        // One shown string's PDF advances in text-space units. `pen` is the full pen
        // movement (per-code widths × font size, plus Tc per code and Tw per
        // single-byte space) used to update Tm; `glyphs` is the width-only sum
        // line EXTENTS are measured with — the line-box budget ignores Tc/Tw
        // (a Tc-condensed line still pins to the uncondensed glyph sum).
        // Both NaN when the current font carries no width data (pinning turns off).
        // When `perChar` is requested, the per-code advance pairs come back too so a
        // long run can be cut into word-anchored segments; null when there is no
        // width data (callers align them to the DECODED text by count).
        // Per-code aligned decode for shows whose whole-string decode EXPANDS
        // (ligature glyphs decoding to several chars): each code's decoded chars
        // share its advance (first char carries it), keeping the per-char lists
        // the line solver needs aligned with the text. Returns null unless the
        // concatenated per-code decode matches the whole-string decode.
        while (true)
            if (!RenderContentOp(ct)) break;

        // Flush any trailing grouped line.
        FlushParkedLines(ct, ct.styleReg, ct.sb, ct.pageHeight, ct.pageWidth, ct.textOnly, ct.classNamer, ct.linkTargets, ct.rotReg, ct.pageLLX, ct.yTopRef, ct.zCounter, ct.pageTurnedOver, ct.emCompensation);

        // Emit collected SVG paths. When an external collector is supplied the raw
        // A mask group still open at the end of this stream (a form whose content
        // ended inside the masked scope) must not leak an unbalanced element.
        EmitContentSvg(ct);
    }

    // ── Image rendering ─────────────────────────────────────────────────

    private static void EmitImage(StringBuilder sb, ImageXObject img,
        CtmState ctm, double pageHeight, string? externalSrc = null)
    {
        // The CTM after cm typically contains: [w 0 0 h x y]
        // where w=width, h=height, x=left, y=bottom
        var imgWidth = Math.Abs(ctm.A);
        var imgHeight = Math.Abs(ctm.D);
        var imgLeft = ctm.E;
        var imgBottom = ctm.F;

        // If width/height are 0 (no cm before Do), use image pixel dimensions
        if (imgWidth < 0.01) imgWidth = img.Width;
        if (imgHeight < 0.01) imgHeight = img.Height;

        // Convert PDF coords (bottom-left) to CSS (top-left)
        var cssTop = pageHeight - imgBottom - imgHeight;
        var cssLeft = imgLeft;

        // Reference a sidecar file when externalising, else inline as a data URI.
        string src;
        if (externalSrc is not null)
        {
            src = externalSrc;
        }
        else if (img.IsJpeg)
        {
            src = $"data:image/jpeg;base64,{Convert.ToBase64String(img.GetRawData())}";
        }
        else
        {
            src = $"data:image/png;base64,{Convert.ToBase64String(img.ToPng())}";
        }

        sb.Append($"<img class=\"pdf-image\" src=\"{src}\" ");
        sb.Append($"style=\"position:absolute;left:{F(cssLeft)}pt;top:{F(cssTop)}pt;");
        sb.Append($"width:{F(imgWidth)}pt;height:{F(imgHeight)}pt;\"");
        sb.AppendLine($" />");
    }

    // ── stl_ line solver ────────────────────────────────────────────────
    // The model for the fixed-layout span segmentation and letter/word
    // spacing solving:
    //  * pen model = /Widths advances + kerns/Tc as inter-char gaps;
    //    browser model = exact face advances (fallback face for chars the
    //    embedded subset lacks).
    //  * space slots collapse/drop/synthesize around 0.6×m (m = browser space
    //    advance); slots fold into a span while |e − running mean| ≤ 0.6×m,
    //    else the slot becomes its own &nbsp; span.
    //  * spans cut at font/size/colour changes and at outlier gaps
    //    (|e − mean of the others| > 1000/11 milli-em → [prefix][carrier]
    //    [first-after][rest]).
    //  * ls = mean advance error over word-internal chars; the inline
    //    word-spacing closes the span's total error budget.

    /// <summary>Appearance shared by a run of recorded line glyphs.</summary>
    private sealed class StlRunStyle
    {
        public string Family = "sans-serif";
        public string CssFamily = "sans-serif";
        public string? FaceName;
        public double FontSize = 12;
        /// <summary>True when the run is drawn in the faux-bold rendering mode (a
        /// fill-then-stroke thickening the same face).</summary>
        public bool FauxBold;
        /// <summary>The slant the face declares, carried so a faux-bold run can state
        /// its painted appearance in full.</summary>
        public string FontStyle = "normal";
        public double R, G, B;
        public double Ascent = 1.0;
        public double LineHeightEm;
        public double SpaceAdvMilli = 250.0;   // m — browser space advance
        public bool SubsetHasSpace = true;
        /// <summary>True when this run's chars are OUTSIDE the embedded subset:
        /// they render (and measure) in the CSS fallback face, and the font
        /// class lists the fallback family.</summary>
        public bool UseFallbackMetrics;
        /// <summary>Embedded-subset coverage probe (null = full coverage).</summary>
        public Func<int, bool>? SubsetHas;
        /// <summary>True when the font serves its own embedded advances, so the run
        /// is measurable even without a resolvable installed face.</summary>
        public bool HasEmbeddedMetrics;
        /// <summary>The style's font serves a SUBSTITUTE face's subset (SimSun
        /// standing in for a non-embedded, non-installed CJK font).</summary>
        public bool SubstituteFace;

        /// <summary>Embedded program advance by CHARACTER (via the font's own
        /// single-char ToUnicode), for measuring a ligature's components — the
        /// em-compensation face basis. Null when unavailable.</summary>
        public Func<int, double?>? ProgramCharMilli;

        /// <summary>True when the run was drawn in an invisible text rendering mode:
        /// it is saved only because the save asked for transparent texts, and its font
        /// class states the transparency instead of a fill colour.</summary>
        public bool Transparent;

        public string CssColor => Transparent
            ? TransparentTextColor
            : $"#{(int)Math.Round(Compat.Clamp(R, 0, 1) * 255):X2}" +
              $"{(int)Math.Round(Compat.Clamp(G, 0, 1) * 255):X2}" +
              $"{(int)Math.Round(Compat.Clamp(B, 0, 1) * 255):X2}";

        public bool SameSpan(StlRunStyle o) =>
            CssFamily == o.CssFamily && FontSize == o.FontSize
            && R == o.R && G == o.G && B == o.B
            && Transparent == o.Transparent
            && UseFallbackMetrics == o.UseFallbackMetrics;

        /// <summary>Browser-model advance of one char, milli-em. A face-less style
        /// measures like the fallback face — including the full-em ideograph rule,
        /// which the import's own measure applies to the same chars.</summary>
        public double TtfMilli(char ch) => UseFallbackMetrics || FaceName is null
            ? HtmlToPdfConverter.StlFallbackAdvanceMilli(ch)
            : HtmlToPdfConverter.StlCharAdvanceMilli(FaceName, ch);
    }

    /// <summary>One recorded glyph of the line being assembled: decoded char,
    /// style handle, device start x, its width-only pen advance, and the
    /// browser-model advance of the glyph that will render it (embedded
    /// program's own metric when the subset is the served face).</summary>
    private struct StlLineGlyph
    {
        public char Ch;
        public int Style;
        public double StartX;
        public double WidthsAdv;
        public double TtfMilli;
        /// <summary>A continuation char of a multi-char code expansion (ligature):
        /// it rides its head's glyph and advance, and the solver fuses the group
        /// into one item.</summary>
        public bool ExpansionTail;
        /// <summary>Fused-item natural width source: true (the variant-subset
        /// model) measures the face's component advances; false keeps the
        /// embedded advances the glyphs already carry.</summary>
        public bool FuseByFace;
        /// <summary>A space synthesized from a deep TJ kern (code −1): the pen
        /// really moved but the source drew no space glyph. The number-column
        /// split only fires on lines whose every space is of this kind.</summary>
        public bool SynthSpace;
    }

    /// <summary>A solved element: a rendered char or a space slot, with its
    /// advance error e (milli-em of the owning style's font size).</summary>
    private struct StlItem
    {
        public bool IsSlot;
        public char Ch;
        /// <summary>Multi-char text of a fused ligature-expansion item; null for
        /// the ordinary single-char item (render <see cref="Ch"/>).</summary>
        public string? Text;
        public int Style;
        public double E;
        /// <summary>The error the ls MEAN sees: for a fused ligature the LIG-basis
        /// residue (≈0) rather than the components-vs-lig face bias that the ws
        /// numerator charges — an ffl ligature's −39.55 delta leaves the ls
        /// class untouched while the ws budget carries it.</summary>
        public double LsE;
        public bool LsEligible;   // word-internal char (counts into ls)
        public double StartX;
        /// <summary>Slot only: the raw pen gap the slot spans.</summary>
        public double GapPt;
        /// <summary>Slot only: synthesized from a bare pen gap (no drawn space
        /// glyph consumed).</summary>
        public bool Synth;
        /// <summary>The face advance the item's error is measured against, in
        /// milli-em of the drawn size (char: the embedded program's advance;
        /// slot: the space metric m). The em-compensation solve re-scales the
        /// face side by the drawn/solve-size ratio.</summary>
        public double FaceMilli;
    }

    /// <summary>Reorder a page text layer's positioned line divs into the
    /// canonical emission order: consecutive rows chain into a
    /// REGION while their column lefts nest into the region's lanes (or the
    /// row's); a region emits its columns left-to-right, each column top-down —
    /// a tab-split table therefore stacks its label column before its text
    /// column. Content other than line divs (overlays, anchors, layer wrappers)
    /// splits the layer into independently reordered runs, so nothing crosses it.</summary>
    private static string ReorderStlLineDivs(string layer, string lineDivCls)
    {
        var divRx = new System.Text.RegularExpressions.Regex(
            "<div class=\"" + System.Text.RegularExpressions.Regex.Escape(lineDivCls)
            + "\" style=\"left:(?<l>-?[0-9.]+)em;top:(?<t>-?[0-9.]+)em;",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        var result = new StringBuilder(layer.Length);
        var run = new List<(double L, double T, string Html)>();
        void FlushRun()
        {
            foreach (var frag in OrderStlRun(run)) result.Append(frag);
            run.Clear();
        }
        var pos = 0;
        while (pos < layer.Length)
        {
            var m = divRx.Match(layer, pos);
            if (!m.Success)
            {
                FlushRun();
                result.Append(layer[pos..]);
                break;
            }
            var between = layer[pos..m.Index];
            if (between.Trim().Length > 0)
            {
                FlushRun();
                result.Append(between);
            }
            var end = BalancedDivEnd(layer, m.Index);
            if (end < layer.Length && layer[end] == '\n') end++;
            run.Add((
                double.Parse(m.Groups["l"].Value, System.Globalization.NumberStyles.Float,
                    CultureInfo.InvariantCulture),
                double.Parse(m.Groups["t"].Value, System.Globalization.NumberStyles.Float,
                    CultureInfo.InvariantCulture),
                layer[m.Index..end]));
            pos = end;
        }
        FlushRun();
        return result.ToString();
    }

    private static int BalancedDivEnd(string s, int start)
    {
        var depth = 0;
        var i = start;
        while (i < s.Length)
        {
            var open = s.IndexOf("<div", i, StringComparison.Ordinal);
            var close = s.IndexOf("</div>", i, StringComparison.Ordinal);
            if (close < 0) return s.Length;
            if (open >= 0 && open < close) { depth++; i = open + 4; }
            else
            {
                depth--;
                i = close + 6;
                if (depth <= 0) return i;
            }
        }
        return s.Length;
    }

}
