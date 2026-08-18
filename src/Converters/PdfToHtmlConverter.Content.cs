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

    private static void RenderContentToHtml(byte[] streamBytes,
        Dictionary<string, FontInfo> fonts,
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
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();

        // Applied text spacing beyond the font's natural advances — this is exactly what
        // becomes CSS letter-spacing: Tc per character, plus the TJ kerning residual
        // (positive TJ numbers move left, so subtract). Accumulated per group.
        double charSpacing = 0;
        double wordSpacing = 0; // Tw — extra advance on single-byte code 32
        double pendingTjNum = 0; // TJ number sum since the last shown run
        double groupTjNum = 0;
        int groupChars = 0;

        // Text state. The text matrix (Tm) and text line matrix (Tlm) are tracked in
        // full: a PDF commonly carries the visible glyph scale in Tm (e.g. `Tf /F 1`
        // then `Tm [12 0 0 12 …]`), so reading the size from Tf alone yields 1pt text
        // and mis-placed runs. `tx`/`ty` mirror the text position for the `v` path op.
        double tx = 0, ty = 0;
        var tlm = new CtmState();
        var tm = new CtmState();
        double leading = 0; bool hasLeading = false; // TL — inter-line spacing for T*/'
        double fontSize = 12;
        double rise = 0; // Ts — raised (superscript) / lowered (subscript) baseline
        // The current face is a Type3 (glyph procedures, no servable program).
        var fontIsType3 = false;
        string fontFamily = "sans-serif";
        string fontCssFamily = "sans-serif";
        string fontWeight = "normal";
        // Text rendering mode (Tr). Fill-then-stroke (2) and its clipping variant (6)
        // are the faux-bold idiom: the same face is painted with an outline stroke to
        // thicken it, so a run drawn that way reads as bold even though the font
        // carries no weight of its own. Only THAT weight rides the span inline —
        // a face that declares its own weight is described by the emitted font class.
        int textRenderMode = 0;
        bool FauxBold() => textRenderMode is 2 or 6;
        // Modes 3 (invisible) and 7 (clip-only) paint no glyphs: this is the OCR text
        // layer a scanner lays under the page raster. It is not part of the visible
        // page, so it reaches the markup only when the save asks for it — and then it
        // is stated as what it is, a run in a fully transparent colour.
        bool Invisible() => textRenderMode is 3 or 7;
        string fontStyle = "normal";
        // A sheared text matrix (b = 0, significant c/d) is the faux-ITALIC idiom:
        // the upright face is slanted by the matrix rather than by a designed italic
        // (the classic shear is tan ≈ 1/3). A rotation carries b ≠ 0, so requiring a
        // zero b keeps rotated runs out; the threshold keeps rounding noise out.
        bool FauxItalic() => Math.Abs(tm.B) < 1e-6 && Math.Abs(tm.D) > 1e-9
            && Math.Abs(tm.C / tm.D) > 0.1;
        // The slant a faux-bold run DECLARES inline: the face's own italic when it
        // has one, else the matrix shear — the painted appearance either way.
        string DeclStyle() => fontStyle != "normal" ? fontStyle
            : FauxItalic() ? "italic" : "normal";
        double fontAscent = 1.0;
        double fontLineHeight = 0.0;
        double r = 0, g = 0, b = 0;
        string? currentFontKey = null;

        // CTM state (a Form XObject invocation seeds the child call with the
        // composed matrix of its call site; cloned so the caller's isn't mutated).
        var ctm = initialCtm?.Clone() ?? new CtmState();
        var ctmStack = new Stack<CtmState>();
        var colorStack = new Stack<(double r, double g, double b,
            double fr, double fg, double fb, double sr, double sg, double sb)>();
        var textSpacingStack = new Stack<(double cs, double ws)>();

        // Path state
        var pathState = new PathState();
        var svgPaths = new StringBuilder();
        // Operator ordinal, and the index of the operator that opened the path
        // currently under construction (-1 = no open path).
        var opCounter = 0;
        var pathOpenIndex = -1;
        // Named colour spaces (cs/CS): a Separation/DeviceN space maps its tint
        // operand through the tint transform into the alternate space; null while a
        // plain component space is selected (component-count mapping applies).
        Func<double, (double r, double g, double b)>? fillTintMap = null, strokeTintMap = null;
        double cpx = 0, cpy = 0;   // path current point, device space
        // The clip a `sh` paints inside: `W`/`W*` mark the path just built as the new
        // clip, the following path-painting op commits it, and q/Q save and restore it
        // with the rest of the graphics state.
        string? clipD = null, pendingClipD = null;
        var clipStack = new Stack<string?>();
        var shadingSeq = 0;
        // An open SVG soft-mask group: everything painted between the gs that set a
        // luminosity /SMask and the gs (or Q) that clears it renders inside a
        // <g mask="…">. The depth records the q-nesting at open time so a Q that
        // pops past it closes the group too.
        var maskGroupOpen = false;
        var maskOpenClipDepth = 0;
        (double x, double y) Dp(double x, double y) =>
            (x * ctm.A + y * ctm.C + ctm.E, x * ctm.B + y * ctm.D + ctm.F);

        // TrySaveTextUnderliningAndStrikeoutingInCss: horizontal hairlines (stroked
        // lines / thin filled rects) are collected in DEVICE space as decoration
        // candidates; FlushGroup matches them against each text run (same colour,
        // under/through the baseline, horizontally covering the run start).
        // (Y = rule centre line, Thick in device units.)
        List<(double Y, double X0, double X1, double Thick, double R, double G, double B)>? rules =
            cssTextDecorations ? new() : null;
        var pathSegs = new List<(double X0, double Y0, double X1, double Y1)>();
        double curDevX = 0, curDevY = 0;
        (double X, double Y) Dev(double x, double y) =>
            (ctm.A * x + ctm.C * y + ctm.E, ctm.B * x + ctm.D * y + ctm.F);
        double DevScale() => Math.Sqrt(Math.Abs(ctm.A * ctm.D - ctm.B * ctm.C));
        var pendingRects = new List<(double X0, double Y0, double X1, double Y1)>();
        void CollectRuleCandidates(bool stroked, bool filled)
        {
            if (rules is null) return;
            if (stroked)
                foreach (var (x0, y0, x1, y1) in pathSegs)
                {
                    if (Math.Abs(y1 - y0) > 0.35 || Math.Abs(x1 - x0) < 0.5) continue;
                    rules.Add(((y0 + y1) / 2, Math.Min(x0, x1), Math.Max(x0, x1),
                        pathState.LineWidth * DevScale(),
                        pathState.StrokeR, pathState.StrokeG, pathState.StrokeB));
                }
            if (filled)
                foreach (var (x0, y0, x1, y1) in pendingRects)
                {
                    var h = Math.Abs(y1 - y0);
                    var w = Math.Abs(x1 - x0);
                    if (w < 0.5 || h >= w) continue;
                    rules.Add(((y0 + y1) / 2, Math.Min(x0, x1), Math.Max(x0, x1), h,
                        pathState.FillR, pathState.FillG, pathState.FillB));
                }
            pathSegs.Clear();
            pendingRects.Clear();
        }

        // Line-grouping buffer (text-only overlay): consecutive text shows on the
        // same baseline are accumulated into a single span — separately-positioned
        // words (one Tj each) would otherwise become one span per word, so a phrase
        // spanning several shows could never be matched as contiguous text. Matches
        // the expected glyph/word grouping.
        //
        // Each show is kept as its own (x, text) segment and the group is assembled
        // in LEFT-TO-RIGHT positional order at flush time: an RTL writer emits the
        // rightmost word first, so concatenating in content-stream order would
        // reverse the visual line (Hebrew/Arabic text and their embedded LTR digit
        // clusters). For LTR content the stream order is already x-ascending, so
        // the sort is the identity there.
        var groupSegs = new List<(double X, StringBuilder Text, double PenEnd, double GlyphEnd)>();
        // Per-line glyph records feeding the stl_ line solver; lineOk drops to
        // false (legacy emission) when a show lacks aligned per-char advances.
        var lineGlyphs = styleReg is not null ? new List<StlLineGlyph>() : null;
        var lineStyles = styleReg is not null ? new List<StlRunStyle>() : null;
        var lineOk = true;
        var lineStyleIdx = -1;
        bool groupActive = false;
        // Extent pinning: the group's device-space right edge accumulated from each
        // run's PDF advances (per /Widths). False when any run's advance is unknown.
        bool groupPinned = true;
        double groupEndX = 0;
        // groupPenX tracks the group's accumulated pen edge (Tc/Tw included) for
        // backward-draw detection in the overlay dialect. groupTextPenX is the
        // pen edge after the last NON-whitespace show — the datum the
        // forward-gap div-split measures from (whitespace shows are transparent).
        double groupPenX = 0;
        double groupTextPenX = 0;
        double groupX = 0, groupY = 0, groupFontSize = 12, groupRise = 0;
        double groupAngle = 0; // CSS rotation (deg) shared by the group's runs
        // The UNSCALED text rise (Ts) of the group. RiseThreshold is defined in text-space
        // units (see its doc), so the sup/sub decision must test the raw rise, not the
        // device-scaled groupRise used for positioning — otherwise a down-scaled text
        // matrix (scale < 1) shrinks the rise below the threshold and drops the tag.
        double groupRawRise = 0;
        var groupIsType3 = false;
        string groupFamily = "sans-serif", groupCssFamily = "sans-serif",
            groupWeight = "normal", groupStyle = "normal";
        // Whether the group's runs are drawn in the faux-bold rendering mode.
        var groupFauxBold = false;
        // The slant the group's faux-bold declaration states (face italic or shear).
        var groupDeclStyle = "normal";
        double groupR = 0, groupG = 0, groupB = 0;
        // The group was opened by a run drawn in an invisible rendering mode.
        bool groupTransparent = false;
        // Ascent fraction of the group's font (usWinAscent/upm) — the fixed-layout
        // top subtracts ascent×size, not a full em. groupZ is the paint-order
        // z-counter value of the group's last non-whitespace glyph (UseZOrder).
        double groupAscent = 1.0;
        double groupLineHeight = 0.0;
        var groupZ = 0;
        // Marked-content sequence: advanced at the boundaries of /MCID-carrying
        // BDC…EMC items. groupMcSeq tracks the item of the group's LAST merged
        // show, so line continuation across structure-item boundaries demands a
        // near-identical baseline.
        var mcSeq = 0;
        var groupMcSeq = 0;
        var mcStack = new Stack<bool>();
        // How many layer boxes each open marked-content region opened, so EMC closes
        // exactly its own.
        var ocDepth = new Stack<int>();

        // Text of the line's last merged show, for the overstrike drop. Line state:
        // parked and resumed with the rest.
        var groupLastShowText = "";

        // Lines the producer has moved away from but may come back to, in FIRST-USE
        // order — that order is what they are emitted in, so a page whose lines are
        // drawn one after another (every ordinary page) emits exactly as before.
        var parkedLines = new List<StlLinePark>();
        // The slot the line currently being built came from, if it was resumed: a
        // line writes back to its OWN slot, never to whatever else shares its
        // baseline, so a second line started at the same height cannot displace it.
        StlLinePark? activePark = null;
        // Baselines within this much of each other are the same line, matching the
        // grouping tolerance used for a continuing show.
        const double ParkBaselineTolPt = 0.5;
        // Sub-point slack for pen-position comparisons: a producer restates a
        // continuation's x from its own accumulator, which differs from ours by
        // rounding (observed up to ~0.1 pt); 0.5 pt covers that while staying far
        // under the narrowest meaningful gap (a word space, 2.5+ pt at body sizes).
        // The same slack the pre-existing backward-draw and off-page rules use.
        const double PenSlackPt = 0.5;

        StlLinePark? FindParkedLine(double y)
        {
            for (var i = parkedLines.Count - 1; i >= 0; i--)
                if (!parkedLines[i].Closed
                    && Math.Abs(parkedLines[i].Y - y) <= ParkBaselineTolPt) return parkedLines[i];
            return null;
        }

        StlLinePark? ParkCurrentLine()
        {
            if (!groupActive) return null;
            var p = activePark;
            if (p is null) { p = new StlLinePark(); parkedLines.Add(p); }
            activePark = null;
            p.Segs = groupSegs; p.Glyphs = lineGlyphs; p.Styles = lineStyles;
            p.Ok = lineOk; p.StyleIdx = lineStyleIdx;
            p.Pinned = groupPinned; p.EndX = groupEndX; p.PenX = groupPenX;
            p.TextPenX = groupTextPenX;
            p.X = groupX; p.Y = groupY; p.FontSize = groupFontSize; p.Rise = groupRise;
            p.Angle = groupAngle; p.RawRise = groupRawRise; p.IsType3 = groupIsType3;
            p.Family = groupFamily; p.CssFamily = groupCssFamily; p.Weight = groupWeight;
            p.Style = groupStyle; p.DeclStyle = groupDeclStyle; p.FauxBold = groupFauxBold;
            p.R = groupR; p.G = groupG; p.B = groupB; p.Transparent = groupTransparent;
            p.Ascent = groupAscent; p.LineHeight = groupLineHeight;
            p.Z = groupZ; p.McSeq = groupMcSeq; p.TjNum = groupTjNum; p.Chars = groupChars;
            p.LastShowText = groupLastShowText;
            // The parked line owns its collections; the active slot takes fresh ones,
            // and EVERY piece of live line state resets exactly as FlushGroup's tail
            // resets it — a parked line's pen surviving into the next line suppressed
            // the column split page-wide (every fresh line compared its gaps against
            // the stale 469 pt pen of a line long left).
            groupSegs = new List<(double X, StringBuilder Text, double PenEnd, double GlyphEnd)>();
            lineGlyphs = styleReg is not null ? new List<StlLineGlyph>() : null;
            lineStyles = styleReg is not null ? new List<StlRunStyle>() : null;
            lineOk = true;
            lineStyleIdx = -1;
            groupActive = false;
            groupLastShowText = "";
            groupTjNum = 0;
            groupChars = 0;
            groupPinned = true;
            groupEndX = 0;
            groupPenX = 0;
            groupTextPenX = 0;
            return p;
        }

        void ResumeParkedLine(StlLinePark p)
        {
            groupSegs = p.Segs; lineGlyphs = p.Glyphs; lineStyles = p.Styles;
            lineOk = p.Ok; lineStyleIdx = p.StyleIdx;
            groupPinned = p.Pinned; groupEndX = p.EndX; groupPenX = p.PenX;
            groupTextPenX = p.TextPenX;
            groupX = p.X; groupY = p.Y; groupFontSize = p.FontSize; groupRise = p.Rise;
            groupAngle = p.Angle; groupRawRise = p.RawRise; groupIsType3 = p.IsType3;
            groupFamily = p.Family; groupCssFamily = p.CssFamily; groupWeight = p.Weight;
            groupStyle = p.Style; groupDeclStyle = p.DeclStyle; groupFauxBold = p.FauxBold;
            groupR = p.R; groupG = p.G; groupB = p.B; groupTransparent = p.Transparent;
            groupAscent = p.Ascent; groupLineHeight = p.LineHeight;
            groupZ = p.Z; groupMcSeq = p.McSeq; groupTjNum = p.TjNum; groupChars = p.Chars;
            groupLastShowText = p.LastShowText;
            groupActive = true;
            activePark = p;
        }

        // Assemble the group's segments in visual (x-ascending) order. The sort is
        // stable, so consecutive shows reported at the same x (no repositioning
        // between them) keep their stream order. The text-only overlay joins
        // adjacent word segments with a space (as before, just between positional
        // neighbours now); the normal path concatenates directly — justified lines
        // are split into abutting segments that already carry their own spacing.
        string JoinGroupSegments()
        {
            var ordered = new List<(double X, StringBuilder Text, double PenEnd, double GlyphEnd)>(groupSegs);
            ordered.Sort((a, b) => a.X.CompareTo(b.X));
            var joined = new StringBuilder();
            foreach (var (_, seg, _, _) in ordered)
            {
                if (seg.Length == 0) continue;
                if (textOnly && joined.Length > 0
                    && !char.IsWhiteSpace(joined[joined.Length - 1]) && !char.IsWhiteSpace(seg[0]))
                    joined.Append(' ');
                joined.Append(seg);
            }
            if (ordered.Count > 0) groupX = ordered[0].X;
            return joined.ToString();
        }

        bool TrySolveStlLine()
        {
            if (styleReg is null || !lineOk || lineGlyphs is not { Count: > 0 }) return false;
            if (rules is not null) return false;   // css text-decoration path keeps legacy emission
            if (groupAngle != 0) return false;
            if (Math.Abs(groupRawRise) > RiseThreshold) return false;
            var yTop = double.IsNaN(yTopRef) ? pageHeight : yTopRef;
            var divCls = classNamer.Cls("01");
            var zStyle = zCounter is not null && groupZ > 0 ? $"z-index:{groupZ};" : "";
            // A Type3 face is a set of content-stream procedures, not a program a
            // browser can be handed, so text transcribed from it is a best-effort
            // fallback and must not claim the annotation's hyperlink — the link keeps
            // its own click surface instead.
            var link = groupIsType3 ? null : FindLinkTarget(linkTargets, groupX, groupPenX, groupY);
            var popup = link?.PopupItems;
            // Wrapped is what suppresses the overlay, so the solver sets it per SPAN,
            // as it opens each anchor — a line whose spans all fall outside a rect (or
            // that the solver emitted empty) leaves that link its click surface.
            var solved = EmitStlSolvedDiv(sb, lineGlyphs, lineStyles!, styleReg, classNamer,
                divCls, zStyle, pageLLX, yTop, groupY,
                groupIsType3 ? null : (x0, x1) => FindLinkTarget(linkTargets, x0, x1, groupY),
                popup,
                pageTurnedOver ? pageWidth / 12.0 : 0, pageTurnedOver ? pageHeight / 12.0 : 0,
                emGrid: emCompensation);
            return solved;
        }

        void FlushGroup()
        {
            // A line closed for real gives up its park slot, so it cannot be
            // emitted a second time when the page's remaining lines are closed.
            if (groupActive && activePark is { } closing) parkedLines.Remove(closing);
            activePark = null;
            var groupText = new StringBuilder(JoinGroupSegments());
            if (groupActive && groupText.Length > 0 && TrySolveStlLine())
            {
                // Solved and emitted by the stl_ line solver.
            }
            else if (groupActive && groupText.Length > 0)
            {
                if (styleReg is not null && string.IsNullOrWhiteSpace(groupText.ToString()))
                {
                    // Whitespace-only group (re-drawn word-gap space glyphs over an
                    // already-shown line, a positioned space show between columns, or
                    // a stray trailing space glyph past the text): dropped in BOTH
                    // stl_ dialects — a lone space div
                    // must not grow the re-imported page width, and its font class
                    // must not burn a class number.
                }
                else if (styleReg is not null)
                {
                    // stl_ shape: a positioned stl_01 div wrapping the group's text.
                    // Both stl_ dialects emit one span per word-anchored SEGMENT,
                    // each letter/word-spacing-pinned so the measured boxes reach
                    // their device anchors (external SVG-text saves
                    // pin exactly like the PNG-background overlay); a group whose
                    // face cannot resolve keeps the single whole-line span with the
                    // plain Tc/TJ letter-spacing and the face's natural metric flow.
                    // Channel bytes TRUNCATE (0.994118 -> 253 #FD) when forming
                    // the emitted class colors.
                    var color = groupTransparent
                        ? TransparentTextColor
                        : $"#{(int)(Math.Clamp(groupR, 0, 1) * 255):X2}{(int)(Math.Clamp(groupG, 0, 1) * 255):X2}{(int)(Math.Clamp(groupB, 0, 1) * 255):X2}";
                    var fontNum = styleReg.Font(groupCssFamily, groupFontSize / 12.0, color, null);
                    // Line-height is the font's hhea (asc+|desc|)/upm when a program
                    // is available (1.117188 for Arial), the
                    // generic 1.2 fallback otherwise.
                    var lhNum = styleReg.LineHeight(groupLineHeight > 0 ? Math.Round(groupLineHeight, 6) : 1.2);
                    var fs = Math.Max(0.01, groupFontSize);
                    var textAll = groupText.ToString();
                    var face = groupPinned && groupEndX > groupX + 0.01
                        && !string.IsNullOrWhiteSpace(textAll)
                        ? HtmlToPdfConverter.ResolveStlFace(groupFamily) : null;

                    // Fixed-layout geometry: x is measured from the MediaBox left
                    // edge, the page top reference is LLY + floor(height), and the
                    // run's visual top sits ascent×size above the baseline (the
                    // font's usWinAscent fraction, not a full em).
                    var yTop = double.IsNaN(yTopRef) ? pageHeight : yTopRef;
                    var left = (groupX - pageLLX) / 12.0;
                    var top = (yTop - groupY - groupAscent * groupFontSize) / 12.0;
                    if (pageTurnedOver) { left -= pageWidth / 12.0; top -= pageHeight / 12.0; }
                    // A rotated run carries a document-wide rotation class next to
                    // stl_01 (vendor-prefixed transform block in the stylesheet).
                    var divCls = groupAngle != 0
                        ? $"{classNamer.Cls("01")} {classNamer.Cls(styleReg.Rotation(Math.Round(groupAngle, 2)))}"
                        : classNamer.Cls("01");
                    var zStyle = zCounter is not null && groupZ > 0 ? $"z-index:{groupZ};" : "";
                    sb.Append($"<div class=\"{divCls}\" style=\"left:{Em4T(left)}em;top:{Em4T(top)}em;{zStyle}\">");
                    // A text run inside a link annotation's rect renders as an anchor
                    // wrapping the span(s) (div > a > span), carrying the link with
                    // the text itself rather than only as an invisible overlay.
                    // Containment is judged by OVERLAP, not the group origin: a rect
                    // is fitted to the link's visible text with a little padding, so
                    // the NEXT run's leading space can start inside the rect's right
                    // padding without being the link's text.
                    var link = groupIsType3
                        ? null : FindLinkTarget(linkTargets, groupX, groupPenX, groupY);
                    var popupItems = link?.PopupItems;
                    var linkOpen = link is null || popupItems is not null
                        ? null
                        : $"<a href=\"{EscapeHtml(link.Uri)}\"" +
                            (link.Uri.StartsWith('#') ? ">" : " target=\"_blank\">");
                    // The single-span path opens the anchor around its one span here;
                    // the pinned multi-segment path wraps EACH span in its own anchor.
                    // Wrapped is set only where an anchor is really written: it is what
                    // suppresses the click-surface overlay.
                    if (linkOpen is not null && face is null)
                    {
                        sb.Append(linkOpen);
                        if (link is not null) link.Wrapped = true;
                    }

                    // A page-menu widget wraps the caption span in a relative
                    // hover box; its drop-up list class allocates after the
                    // caption's own classes.
                    var popupBoxNum = 0;
                    if (popupItems is not null)
                    {
                        popupBoxNum = styleReg.PopupBox();
                        sb.Append($"<div class=\"{classNamer.Cls(popupBoxNum)}\">");
                    }

                    if (face is not null)
                    {
                        // Overlay segments in visual order; re-drawn whitespace-only
                        // overlap segments are dropped.
                        var ordered = new List<(double X, StringBuilder Text, double PenEnd, double GlyphEnd)>(groupSegs);
                        ordered.Sort((a, b) => a.X.CompareTo(b.X));
                        var emit = new List<(string text, double startX, double glyphEnd)>();
                        double coveredTo = double.MinValue;
                        foreach (var seg in ordered)
                        {
                            var st = seg.Text.ToString();
                            if (st.Length == 0) continue;
                            if (string.IsNullOrWhiteSpace(st) && seg.X < coveredTo - 0.5) continue;
                            emit.Add((st, seg.X, seg.GlyphEnd));
                            coveredTo = Math.Max(coveredTo, seg.GlyphEnd);
                        }
                        // Re-cut segment boundaries at word boundaries: a span is
                        // never split inside a word, but a justified line's
                        // raw shows often break mid-word ("laborat" + "ory"). A
                        // segment's leading word fragment moves into the previous
                        // span, and the following span starts at the fragment's
                        // measured end past its old device anchor.
                        var spaceAdv = HtmlToPdfConverter.MeasureStlExactText(face, " ", fs);
                        for (var si = 0; si + 1 < emit.Count; si++)
                        {
                            var cur = emit[si];
                            var nxt = emit[si + 1];
                            if (cur.text.Length == 0 || nxt.text.Length == 0) continue;
                            if (char.IsWhiteSpace(cur.text[^1]) || char.IsWhiteSpace(nxt.text[0])) continue;
                            // Only ABUTTING segments are a split word: a device gap
                            // approaching a space width at the boundary is a word
                            // gap (separately positioned words carry no space
                            // glyph), and those segments stay separate so the
                            // emission below writes the gap as a real space.
                            if (nxt.startX - cur.glyphEnd > 0.5 * spaceAdv) continue;
                            var cut = 0;
                            while (cut < nxt.text.Length && !char.IsWhiteSpace(nxt.text[cut])) cut++;
                            var headText = nxt.text[..cut];
                            if (cut == nxt.text.Length)
                            {
                                // The whole next segment is the word's tail: absorb it
                                // and re-examine the merged span's new right boundary.
                                emit[si] = (cur.text + headText, cur.startX, nxt.glyphEnd);
                                emit.RemoveAt(si + 1);
                                si--;
                            }
                            else
                            {
                                emit[si] = (cur.text + headText, cur.startX, cur.glyphEnd);
                                emit[si + 1] = (nxt.text[cut..],
                                    nxt.startX + HtmlToPdfConverter.MeasureStlExactText(face, headText, fs),
                                    nxt.glyphEnd);
                            }
                        }
                        var lastLsNum = 0;
                        for (var si = 0; si < emit.Count; si++)
                        {
                            var (segText, segX, segGlyphEnd) = emit[si];
                            // Interior segments pin the span box to the NEXT
                            // segment's device anchor so every word lands at its PDF
                            // position; the LAST segment pins to its width-only glyph
                            // edge - the line-width budget - and the
                            // sentinel &nbsp; dangles beyond it.
                            // A segment born from REPOSITIONING (each word its own Tj)
                            // carries no space glyph before the next word; the word
                            // gap is still written as a real space
                            // inside the span - its ws slot absorbs the pin residual -
                            // so the extracted text keeps its word boundaries.
                            if (si + 1 < emit.Count && !char.IsWhiteSpace(segText[^1])
                                && !char.IsWhiteSpace(emit[si + 1].text[0]))
                                segText += " ";
                            var target = (si + 1 < emit.Count ? emit[si + 1].startX : segGlyphEnd) - segX;
                            var natural = HtmlToPdfConverter.MeasureStlExactText(face, segText, fs);
                            var lsEm = Math.Round((target - natural) / (segText.Length * fs), 4);
                            var resid = target - natural - lsEm * segText.Length * fs;
                            var spaces = 0;
                            foreach (var ch in segText) if (ch == ' ') spaces++;
                            var wsEm = spaces > 0 ? Math.Round(resid / (spaces * fs), 4) : 0;
                            var lsNum = styleReg.LetterSpacing(lsEm);
                            // A ten-thousandth-scale residue is letter-spacing
                            // rounding noise, not a word gap worth
                            // bridging — no inline style for it.
                            // A bold/italic face carries its weight inline: the emitted
                            // font class names the FAMILY only, so a viewer that falls
                            // back to a system face would otherwise render the run regular.
                            var weightCss = StlWeightStyleCss(groupFauxBold, groupDeclStyle);
                            var wsCss = Math.Abs(wsEm) >= 0.001
                                ? $"word-spacing:{wsEm.ToString("0.####", CultureInfo.InvariantCulture)}em;"
                                : "";
                            var wsAttr = weightCss.Length + wsCss.Length > 0
                                ? $" style=\"{weightCss}{wsCss}\""
                                : "";
                            lastLsNum = lsNum;
                            // Each segment resolves its own target from its own extent
                            // (see the solver): a row of per-word hotspots gives each
                            // word its own href.
                            var segLink = popupItems is null && !groupIsType3
                                ? FindLinkTarget(linkTargets, segX, segGlyphEnd, groupY)
                                : null;
                            var segOpen = segLink is null
                                ? null
                                : $"<a href=\"{EscapeHtml(segLink.Uri)}\"" +
                                    (segLink.Uri.StartsWith('#') ? ">" : " target=\"_blank\">");
                            if (segOpen is not null)
                            {
                                sb.Append(segOpen);
                                segLink!.Wrapped = true;
                            }
                            sb.Append($"<span class=\"{classNamer.Attr(fontNum, lhNum, lsNum)}\"{wsAttr}>");
                            var stlSeg = EscapeHtml(segText);
                            if (groupRawRise > RiseThreshold) stlSeg = $"<sup>{stlSeg}</sup>";
                            else if (groupRawRise < -RiseThreshold) stlSeg = $"<sub>{stlSeg}</sub>";
                            sb.Append(stlSeg);
                            // The line-end sentinel is a SPACE then the nbsp, as in the
                            // solved line path: the space belongs to the line, the nbsp
                            // hangs past it and stays outside the width budget.
                            if (si == emit.Count - 1 && popupItems is null) sb.Append(" &nbsp;");
                            sb.Append("</span>");
                            if (segOpen is not null) sb.Append("</a>");
                        }
                        if (popupItems is not null)
                        {
                            var listNum = styleReg.PopupList(popupBoxNum);
                            sb.Append($"<div class=\"{classNamer.Cls(listNum)}\">");
                            foreach (var (label, href) in popupItems)
                                sb.Append($"<a href=\"{href}\" class=\"{classNamer.Cls(fontNum)} " +
                                    $"{classNamer.Cls(lhNum)}  {classNamer.Cls(lastLsNum)}\">{EscapeHtml(label)}</a>");
                            sb.Append("</div></div>");
                        }
                        sb.Append("</div>\n");
                    }
                    else
                    {
                        var lsEm = charSpacing / Math.Max(0.01, groupFontSize)
                            - groupTjNum / (1000.0 * Math.Max(1, groupChars));
                        var lsNum = styleReg.LetterSpacing(System.Math.Round(lsEm, 4));
                        // A same-colour hairline under (or through) the baseline that
                        // covers the run start becomes CSS text-decoration; a
                        // decorated span carries the inline style and drops the
                        // trailing &nbsp;.
                        var decoration = FindDecoration(rules, groupX, groupY, groupFontSize,
                            groupR, groupG, groupB);
                        sb.Append($"<span class=\"{classNamer.Attr(fontNum, lhNum, lsNum)}\"");
                        var groupWeightCss = StlWeightStyleCss(groupFauxBold, groupDeclStyle);
                        if (decoration is not null)
                            sb.Append($" style=\"{groupWeightCss}text-decoration:{decoration};word-spacing:0em;\"");
                        else if (groupWeightCss.Length > 0)
                            sb.Append($" style=\"{groupWeightCss}\"");
                        sb.Append('>');
                        // A non-trivial text rise marks a superscript/subscript run:
                        // wrap it in <sup>/<sub> so the markup carries the semantics
                        // (the .stl_ sup/sub CSS rules already position them). The
                        // rise is baked into `top`, so the tags are purely semantic -
                        // see EmitSpan for the non-stl_ counterpart.
                        var stlInner = EscapeHtml(groupText.ToString());
                        if (groupRawRise > RiseThreshold) stlInner = $"<sup>{stlInner}</sup>";
                        else if (groupRawRise < -RiseThreshold) stlInner = $"<sub>{stlInner}</sub>";
                        sb.Append(stlInner)
                            .Append(decoration is not null || popupItems is not null ? "</span>" : " &nbsp;</span>");
                        if (popupItems is not null)
                        {
                            var listNum = styleReg.PopupList(popupBoxNum);
                            sb.Append($"<div class=\"{classNamer.Cls(listNum)}\">");
                            foreach (var (label, href) in popupItems)
                                sb.Append($"<a href=\"{href}\" class=\"{classNamer.Cls(fontNum)} " +
                                    $"{classNamer.Cls(lhNum)}  {classNamer.Cls(lsNum)}\">{EscapeHtml(label)}</a>");
                            sb.Append("</div></div>");
                        }
                        sb.Append(linkOpen is not null ? "</a></div>\n" : "</div>\n");
                    }
                }
                else
                {
                    EmitSpan(sb, groupText.ToString(), groupX, groupY, groupFontSize,
                        groupFamily, groupWeight, groupStyle, groupR, groupG, groupB, pageHeight,
                        groupRise, transparentText: textOnly,
                        rotationClass: groupAngle != 0 && rotReg is not null
                            ? rotReg.Class(groupAngle) : null);
                }
            }
            groupSegs.Clear();
            groupActive = false;
            groupTjNum = 0;
            groupChars = 0;
            groupPinned = true;
            groupEndX = 0;
            groupPenX = 0;
            groupTextPenX = 0;
            lineGlyphs?.Clear();
            lineOk = true;
            lineStyleIdx = -1;
            groupLastShowText = "";
        }

        /// Close every line this page still holds open, in first-use order — the
        /// order legacy emission produces when lines are drawn one after another.
        /// Anything that is not more text (an image entering the text layer, a
        /// layer box, a form, the end of the stream) ends the parking window, so
        /// text never crosses such content.
        void FlushParkedLines()
        {
            if (parkedLines.Count == 0) { FlushGroup(); return; }
            ParkCurrentLine();
            var order = parkedLines;
            parkedLines = new List<StlLinePark>();
            foreach (var p in order) { ResumeParkedLine(p); FlushGroup(); }
        }

        // Show one decoded run. The visible position and glyph size come from the
        // text matrix composed with the CTM (text space → device space), so a scale
        // carried in Tm/CTM rather than Tf is honoured. Consecutive shows on the same
        // baseline with the same appearance are merged into one span: a justified line
        // is split into abutting segments — often mid-word ("laborat" + "ory") — that
        // already carry their own spacing, so in normal mode they are concatenated
        // directly; the PNG-overlay (textOnly) mode joins separately-positioned words
        // with a space.
        void ShowRun(string text, double advTextSpace = double.NaN, double extTextSpace = double.NaN,
            List<(double pen, double glyph)>? perChar = null, List<int>? perCode = null)
        {
            if (string.IsNullOrEmpty(text)) return;

            // An invisible run is dropped whole when the save does not ask for it. The
            // caller advances the text matrix after this returns, so a visible run
            // later on the same line still seats where its own pen puts it.
            if (Invisible() && !saveTransparentTexts)
            {
                pendingTjNum = 0;
                return;
            }

            var dev = tm.Times(ctm);
            var scale = Math.Sqrt(dev.C * dev.C + dev.D * dev.D);
            if (scale <= 0) scale = 1;
            var effSize = fontSize * scale;
            var effRise = rise * scale;
            var posX = dev.E;
            var posY = dev.F;

            // Baseline direction in device space. PDF angles are counter-clockwise
            // with y up; CSS rotation is clockwise with y down, so the CSS angle is
            // the negation (e.g. Tm [0 s -s 0 …] — text running upward — is CSS
            // rotate(-90deg)).
            var cssAngle = -Math.Atan2(dev.B, dev.A) * (180.0 / Math.PI);
            if (Math.Abs(cssAngle) < 0.05) cssAngle = 0;

            // A same-baseline show that lands well PAST where the previous show's
            // pen ended is a COLUMN, not a continuation: keep it as its own group so
            // every column keeps its own x, instead of one concatenated run whose
            // tail drifts by re-measured advances (an invoice's label/value columns
            // fused into single runs). Gaps up to one em of the font still merge —
            // a TOC number→title gap of ~0.9 font-em is bridged with a
            // stretched word space but ~1.06 splits — and so do BACKTRACKS: a
            // zero-leading ' wraps back to the line start at the same y, and
            // those halves join into one flowing line. The text-only
            // overlay's grouping is left as-is.
            // The gap is measured from the last TEXT pen edge: whitespace-only
            // shows are transparent to the split decision (they bridge into the
            // run when text resumes nearby, and never force a split themselves).
            // The stl_ dialects split at 87.5×fs milli-em of pen gap
            // (0.0875·fs² pt); the plain dialect keeps its one-em rule.
            var divGapPt = styleReg is not null ? 0.0875 * effSize * effSize : 1.0 * effSize;
            // The column split concerns a show on the SAME baseline: a show on a
            // different baseline no longer closes the line here — it parks it (below),
            // because the producer may come back to it. The baseline test is the same
            // one the sameLine decision makes, so a same-baseline column still cuts
            // exactly where it always did.
            var lineYTol = styleReg is not null && mcSeq != groupMcSeq
                ? 0.2
                : Math.Max(0.5, Math.Max(effSize, groupFontSize) * 0.3);
            if (groupActive && !textOnly && groupSegs.Count > 0
                && !string.IsNullOrWhiteSpace(text)
                && Math.Abs(posY - groupY) <= lineYTol
                && posX - Math.Max(groupTextPenX, groupX) > divGapPt)
            {
                // The severed line must not EMIT here: it keeps its place in the
                // first-use order and simply stops accepting shows — emitting at
                // the split point let it jump ahead of every line still parked,
                // scrambling the document-wide class numbering, which derives
                // from first use.
                if (styleReg is not null && Math.Abs(cssAngle) < 0.05)
                {
                    var severed = ParkCurrentLine();
                    if (severed is not null) severed.Closed = true;
                }
                else FlushGroup();
            }

            // A show that STARTS past the page's right edge is invisible (an
            // off-page TouchUp leftover, clipped by the page rect) — it must not
            // join a line and stretch the flow toward its phantom position.
            if (styleReg is not null && cssAngle == 0 && posX > pageWidth - 0.5)
            {
                pendingTjNum = 0;
                return;
            }

            // A shadowed run is the same text drawn AGAIN a hair off the original
            // (fill pass over the shadow pass): a show restarting at the GROUP'S
            // OWN ORIGIN that repeats a substantial run of text the group already
            // carries is the duplicate pass, dropped whole. The guards keep every
            // legitimate backtrack: an RTL line's next word lands left of the pen
            // with fresh text; a wrap continuation carries new text; a repeated
            // word later in the line starts at the pen, not the origin.
            if (styleReg is not null && groupActive && groupSegs.Count > 0
                && !string.IsNullOrWhiteSpace(text)
                && posX < groupTextPenX - 0.5
                && Math.Abs(posX - groupX) <= 2 * effSize
                && Math.Abs(posY - groupY) <= 0.5
                && text.Trim().Length >= 6
                && !HasRtlCodepoint(text))
            {
                var joined = new StringBuilder();
                foreach (var (_, seg, _, _) in groupSegs) joined.Append(seg);
                if (joined.ToString().Contains(text.Trim(), StringComparison.Ordinal))
                {
                    pendingTjNum = 0;
                    return;
                }
            }

            // An OVERSTRIKE: some producers thicken text by re-stroking the glyph
            // they just drew a fraction of a point away — the same character(s), a
            // hair left of the pen, ending exactly ON the pen. Counted, it doubles
            // the line's characters ("fun" "d" then "d" again reading as "fundd").
            // The suffix match plus the end-on-pen test keeps everything
            // legitimate: an RTL line's next word ends at the previous word's
            // START, not at the pen; a repeated word starts AT the pen or later;
            // a justified continuation carries fresh text. The line consulted is
            // whichever this show belongs to — the current group, or the parked
            // line on this baseline.
            if (styleReg is not null && cssAngle == 0
                && !string.IsNullOrWhiteSpace(text) && !double.IsNaN(advTextSpace))
            {
                string? lastTxt = null; double lastPen = 0, lastX0 = 0;
                if (groupActive && Math.Abs(posY - groupY) <= ParkBaselineTolPt)
                { lastTxt = groupLastShowText; lastPen = groupTextPenX; lastX0 = groupX; }
                else if (!groupActive || Math.Abs(posY - groupY) > ParkBaselineTolPt)
                {
                    if (FindParkedLine(posY) is { } c)
                    { lastTxt = c.LastShowText; lastPen = c.TextPenX; lastX0 = c.X; }
                }
                // Observed second strokes end within 0.06-0.24 pt of the pen; 1 pt
                // holds margin for coarser producers while staying well under half
                // a word space, so a genuinely new word can never qualify.
                const double OverstrikeEndTolPt = 1.0;
                if (!string.IsNullOrEmpty(lastTxt)
                    && lastTxt.EndsWith(text, StringComparison.Ordinal)
                    && posX >= lastX0 - PenSlackPt
                    && posX < lastPen - PenSlackPt
                    && Math.Abs(posX + advTextSpace * scale - lastPen) <= OverstrikeEndTolPt)
                {
                    pendingTjNum = 0;
                    return;
                }
            }

            // A whitespace-only show continues the line regardless of its own
            // font/colour — a word gap drawn with a different font (a larger
            // space glyph between runs) is coerced to the group's font as its
            // own segment instead of breaking the div chain. stl_ dialects only;
            // the plain span dialect keeps strict font grouping.
            var wsOnlyShow = styleReg is not null && string.IsNullOrWhiteSpace(text);
            // In the stl_ dialects a font/size/colour switch cuts a SPAN, not the
            // line: shows keep merging while the line stays solver-eligible.
            // The stl_ dialects keep the loose 0.3-em baseline merge only for
            // shows WITHIN one marked-content item (or in untagged content):
            // across a BDC/EMC boundary two runs continue one line only on a
            // (near-)identical baseline — a tagged CV's date span and its
            // right-hand subtitle span sat 0.24pt apart and stayed two divs,
            // while an untagged report's footer runs 1–2pt apart still merge.
            bool sameLine = groupActive &&
                Math.Abs(effRise - groupRise) <= 0.01 &&
                Math.Abs(cssAngle - groupAngle) <= 0.1 &&
                Math.Abs(posY - groupY) <= lineYTol &&
                (wsOnlyShow || (styleReg is not null && lineOk) ||
                 (fontFamily == groupFamily && fontCssFamily == groupCssFamily &&
                  fontWeight == groupWeight && fontStyle == groupStyle &&
                  r == groupR && g == groupG && b == groupB &&
                  Invisible() == groupTransparent));
                        // A run that starts LEFT of the accumulated pen (overlapping/backward
            // draw - e.g. word-gap space glyphs re-drawn over an already-shown line)
            // cannot continue the inline span flow; it opens its own positioned div.
            // (Overlay mode only - the SVG-text dialect keeps the legacy grouping.)
            // The em-compensation dialect tolerates a SQUEEZED inter-span word
            // space: a body span drawn 0.73 pt behind the title's pen (its
            // separator space compressed by justification) still continues the
            // line — the squeeze is solved as negative word-spacing
            // in ONE div. A genuine re-draw starts at least a word further back.
            var backTolPt = emCompensation ? 1.5 : 0.5;
            if (textOnly && sameLine && groupSegs.Count > 0 && posX < groupPenX - backTolPt)
                sameLine = false;
            if (!sameLine)
            {
                // A line the producer merely moved AWAY from is parked, not closed:
                // a show landing back on a parked baseline CONTINUES that line iff
                // it would have continued it had no interleave happened — at or
                // near the parked pen (the column split keeps its distance rule),
                // or behind it under the dialect's own backtrack semantics.
                var canPark = styleReg is not null && Math.Abs(cssAngle) < 0.05;
                StlLinePark? resumed = null;
                if (canPark)
                {
                    ParkCurrentLine();
                    var cand = FindParkedLine(posY);
                    if (cand is not null && cand.Segs.Count > 0
                        && Math.Abs(cand.Angle) < 0.05
                        && Math.Abs(effRise - cand.Rise) <= 0.01)
                    {
                        var candPen = Math.Max(cand.TextPenX, cand.X);
                        // Only a genuine CONTINUATION resumes: the show picks up at
                        // (or within a word gap of) the parked pen. A show landing
                        // BEHIND the pen of an interrupted line is not one of its
                        // fragments — a subscript pass, a wrap-back, an annotation
                        // overlay — and legacy gave those their own div once the
                        // line had been left; that stands. (A same-line backtrack
                        // with the group still open never reaches here.)
                        var continues = posX >= cand.X - PenSlackPt
                            && (wsOnlyShow
                                ? posX >= candPen - PenSlackPt
                                : posX >= candPen - PenSlackPt && posX <= candPen + divGapPt);
                        // The em-compensation dialect also PREFIX-joins: a fragment
                        // drawn after its line whose pen END lands on the line's
                        // START (a title drawn second is assembled into
                        // ONE div with its body). The end must abut the start
                        // within a squeezed word space — a number-column head ends
                        // a full quad short and keeps its own div.
                        const double PrefixJoinTolPt = 2.5;
                        var prefixJoins = emCompensation && !wsOnlyShow
                            && !double.IsNaN(advTextSpace)
                            && posX < cand.X - PenSlackPt
                            && posX + advTextSpace * scale >= cand.X - PenSlackPt
                            && posX + advTextSpace * scale <= cand.X + PrefixJoinTolPt;
                        if (continues || prefixJoins) resumed = cand;
                    }
                }
                else FlushGroup();

                if (resumed is not null)
                {
                    ResumeParkedLine(resumed);
                }
                else
                {
                groupActive = true;
                groupX = posX; groupY = posY; groupFontSize = effSize; groupRise = effRise;
                groupRawRise = rise;
                groupAngle = cssAngle;
                groupFamily = fontFamily; groupCssFamily = fontCssFamily;
                groupWeight = fontWeight; groupStyle = fontStyle;
                groupFauxBold = FauxBold();
                groupDeclStyle = DeclStyle();
                groupR = r; groupG = g; groupB = b;
                groupTransparent = Invisible();
                groupAscent = fontAscent;
                groupLineHeight = fontLineHeight;
                groupIsType3 = fontIsType3;
                groupZ = 0;
                groupLastShowText = "";
                activePark = null;      // a fresh line owns no park slot yet
                }
            }
            groupMcSeq = mcSeq;
            groupPenX = Math.Max(groupPenX, posX);
            if (!wsOnlyShow) groupLastShowText = text;

            // Append the run to the segment chain (one segment per repositioned
            // run, as before). With aligned per-char advances the OVERLAY run is
            // additionally CUT at word boundaries (space-to-nonspace edges), one
            // segment per word, each pinned separately at flush. Anchors accumulate
            // in Tc/Tw-FREE width space (glyph advances only) - the
            // same budget the per-segment letter-spacings solve against - while the
            // pen (Tc/Tw included) is kept for backward-draw detection only.
            var aligned = perChar is not null && perChar.Count == text.Length;

            // Record the show's glyphs for the stl_ line solver. Any show the
            // solver cannot model (no aligned advances, rotation) drops the whole
            // line back to the legacy emission.
            if (lineGlyphs is not null && lineOk)
            {
                if (!aligned || Math.Abs(cssAngle) > 0.05)
                {
                    lineOk = false;
                }
                else
                {
                    if (lineStyleIdx < 0
                        || lineStyles![lineStyleIdx].Family != fontFamily
                        || lineStyles[lineStyleIdx].CssFamily != fontCssFamily
                        || lineStyles[lineStyleIdx].FontSize != effSize
                        || lineStyles[lineStyleIdx].R != r
                        || lineStyles[lineStyleIdx].G != g
                        || lineStyles[lineStyleIdx].B != b
                        || lineStyles[lineStyleIdx].Transparent != Invisible()
                        || lineStyles[lineStyleIdx].UseFallbackMetrics)
                    {
                        var faceName = HtmlToPdfConverter.ResolveStlFace(fontFamily);
                        var fiNow = currentFontKey is not null
                            && fonts.TryGetValue(currentFontKey, out var fNow) ? fNow : null;
                        var subsetSpace = fiNow?.AdvanceOf is null || fiNow.AdvanceOf(32) > 0;
                        lineStyles!.Add(new StlRunStyle
                        {
                            Family = fontFamily,
                            CssFamily = fontCssFamily,
                            FaceName = faceName,
                            FontSize = effSize,
                            FauxBold = FauxBold(), FontStyle = DeclStyle(),
                            R = r, G = g, B = b, Transparent = Invisible(),
                            Ascent = fontAscent,
                            LineHeightEm = fontLineHeight,
                            SubsetHasSpace = subsetSpace,
                            SubsetHas = fiNow?.SubsetHas,
                            HasEmbeddedMetrics = fiNow?.EmbeddedAdvMilli is not null,
                            SubstituteFace = fiNow?.SubstituteFace ?? false,
                            ProgramCharMilli = fiNow?.ProgramCharAdvMilli,
                            SpaceAdvMilli = subsetSpace && faceName is not null
                                ? HtmlToPdfConverter.StlCharAdvanceMilli(faceName, ' ')
                                // No installed face to measure: the served program's
                                // own space advance beats the generic 250.
                                : fiNow?.EmbeddedAdvMilli?.Invoke(32)
                                    ?? HtmlToPdfConverter.StlFallbackAdvanceMilli(' '),
                        });
                        lineStyleIdx = lineStyles.Count - 1;
                    }
                    // A glyph shown through a GID the embedded program's cmap
                    // cannot address renders in the CSS fallback face: it takes a
                    // sibling style whose metrics and font class carry the
                    // fallback (spaces stay on the base).
                    FontInfo? fRec = null;
                    if (currentFontKey is not null) fonts.TryGetValue(currentFontKey, out fRec);
                    var glyphMapped = fRec?.GlyphMapped;
                    var baseIdx = lineStyleIdx;
                    var fbIdx = -1;
                    var sxRec = posX;
                    for (var ci = 0; ci < text.Length; ci++)
                    {
                        var chRec = text[ci];
                        var idxRec = baseIdx;
                        var codeRec = perCode is not null && ci < perCode.Count ? perCode[ci] : -1;
                        if (chRec != ' ' && codeRec >= 0 && glyphMapped is not null && !glyphMapped(codeRec))
                        {
                            if (fbIdx < 0)
                            {
                                var b0 = lineStyles[baseIdx];
                                lineStyles.Add(new StlRunStyle
                                {
                                    Family = b0.Family, CssFamily = b0.CssFamily,
                                    FaceName = b0.FaceName, FontSize = b0.FontSize,
                                    FauxBold = b0.FauxBold, FontStyle = b0.FontStyle,
                                    R = b0.R, G = b0.G, B = b0.B, Transparent = b0.Transparent,
                                    Ascent = b0.Ascent, LineHeightEm = b0.LineHeightEm,
                                    SubsetHasSpace = b0.SubsetHasSpace,
                                    SubsetHas = b0.SubsetHas,
                                    SpaceAdvMilli = b0.SpaceAdvMilli,
                                    HasEmbeddedMetrics = b0.HasEmbeddedMetrics,
                                    SubstituteFace = b0.SubstituteFace,
                                    ProgramCharMilli = b0.ProgramCharMilli,
                                    UseFallbackMetrics = true,
                                });
                                fbIdx = lineStyles.Count - 1;
                            }
                            idxRec = fbIdx;
                        }
                        double? embAdv = null;
                        if (codeRec >= 0)
                        {
                            // The em-compensation dialect measures by the embedded
                            // program's own advances (that program is re-served
                            // via @font-face, so the solve and the browser agree
                            // glyph by glyph); other dialects keep the face-metric model.
                            if (emCompensation && fRec?.ProgramAdvMilli is { } pa)
                                embAdv = pa(codeRec);
                            embAdv ??= fRec?.EmbeddedAdvMilli?.Invoke(codeRec);
                        }
                        // A code expanding to several chars shares its embedded
                        // advance with the FIRST char; the rest ride at (near-)zero —
                        // a TJ kern between the code and its neighbour can leak a
                        // sub-point residue onto the tail char, which is still no
                        // advance of its own.
                        // A multi-char code expansion fuses (head + tails solve as
                        // one item) when the font serves EMBEDDED advances — the
                        // code's whole advance rides the head and the tails add
                        // zero, so the pair's error stays the small ligature-vs-
                        // components difference instead of two large opposite
                        // errors that atomize the span. Face-metric fonts keep the
                        // unfused per-char model.
                        // Tail detection is relative to the EFFECTIVE size: the
                        // tail's own advance is at most kern residue (a few
                        // milli-em), never a real glyph advance — a Tf 1 font
                        // scaled up by Tm must not read every repeated character
                        // ("00") as an expansion.
                        // A REAL second character never has a negative advance — a
                        // tail whose residue is a big NEGATIVE kern (a line-final
                        // ligature pulled back by the kern before its trailing
                        // space) is still a tail.
                        var expansionTail = embAdv is not null && ci > 0 && perCode is not null
                            && ci < perCode.Count && perCode[ci - 1] == codeRec
                            && (emCompensation
                                ? perChar![ci].glyph * scale < 0.1 * effSize
                                : Math.Abs(perChar![ci].glyph) * scale < 0.1 * effSize);
                        if (expansionTail) embAdv = 0;
                        lineGlyphs.Add(new StlLineGlyph
                        {
                            Ch = chRec,
                            Style = idxRec,
                            StartX = sxRec,
                            WidthsAdv = perChar![ci].glyph * scale,
                            TtfMilli = embAdv ?? lineStyles![idxRec].TtfMilli(chRec),
                            ExpansionTail = expansionTail,
                            FuseByFace = expansionTail && glyphMapped is not null,
                            SynthSpace = chRec == ' ' && perCode is not null
                                && ci < perCode.Count && perCode[ci] < 0,
                        });
                        sxRec += perChar[ci].pen * scale;
                    }
                }
            }

            if (groupSegs.Count == 0 || groupSegs[groupSegs.Count - 1].X != posX)
                groupSegs.Add((posX, new StringBuilder(), posX, posX));
            var segIdx = groupSegs.Count - 1;
            var s0 = groupSegs[segIdx];
            var penX = Math.Max(s0.PenEnd, posX);
            var glyphX = Math.Max(s0.GlyphEnd, posX);
            for (var ci = 0; ci < text.Length; ci++)
            {
                var ch = text[ci];
                if (textOnly && aligned && groupSegs[segIdx].Text.Length > 0
                    && !char.IsWhiteSpace(ch)
                    && char.IsWhiteSpace(groupSegs[segIdx].Text[groupSegs[segIdx].Text.Length - 1]))
                {
                    // close the current word segment and anchor the next at the
                    // running width-only edge
                    groupSegs[segIdx] = (groupSegs[segIdx].X, groupSegs[segIdx].Text, penX, glyphX);
                    groupSegs.Add((glyphX, new StringBuilder(), penX, glyphX));
                    segIdx++;
                }
                groupSegs[segIdx].Text.Append(ch);
                if (aligned)
                {
                    penX += perChar![ci].pen * scale;
                    glyphX += perChar[ci].glyph * scale;
                }
            }
            if (!aligned)
            {
                penX = double.IsNaN(advTextSpace) ? penX : posX + advTextSpace * scale;
                glyphX = double.IsNaN(extTextSpace) ? glyphX : posX + extTextSpace * scale;
            }
            var cl = groupSegs[segIdx];
            groupSegs[segIdx] = (cl.X, cl.Text,
                Math.Max(cl.PenEnd, penX), Math.Max(cl.GlyphEnd, glyphX));
            groupPenX = Math.Max(groupPenX, penX);
            if (!string.IsNullOrWhiteSpace(text))
                groupTextPenX = Math.Max(groupTextPenX, penX);

            // Extent tracking: the run's device right edge from its width-only PDF
            // advances (the line-box budget ignores Tc/Tw).
            if (double.IsNaN(extTextSpace)) groupPinned = false;
            else groupEndX = Math.Max(groupEndX, posX + extTextSpace * scale);
            groupTjNum += pendingTjNum;
            pendingTjNum = 0;
            groupChars += text.Length;
            // UseZOrder: every shown non-whitespace glyph advances the paint
            // counter; the div's z-index is the value at its last such glyph.
            if (zCounter is not null)
            {
                var nws = 0;
                foreach (var ch in text) if (!char.IsWhiteSpace(ch)) nws++;
                if (nws > 0) { zCounter.V += nws; groupZ = zCounter.V; }
            }
        }

        // One shown string's PDF advances in text-space units. `pen` is the full pen
        // movement (per-code widths × font size, plus Tc per code and Tw per
        // single-byte space) used to update Tm; `glyphs` is the width-only sum
        // line EXTENTS are measured with — the line-box budget ignores Tc/Tw
        // (a Tc-condensed line still pins to the uncondensed glyph sum).
        // Both NaN when the current font carries no width data (pinning turns off).
        // When `perChar` is requested, the per-code advance pairs come back too so a
        // long run can be cut into word-anchored segments; null when there is no
        // width data (callers align them to the DECODED text by count).
        (double pen, double glyphs) StringAdvance(PdfString ps,
            List<(double pen, double glyph)>? perChar = null,
            List<int>? perCode = null)
        {
            if (currentFontKey is null || !fonts.TryGetValue(currentFontKey, out var fi)
                || fi.AdvanceOf is null) return (double.NaN, double.NaN);
            var bytes = ps.Value;
            double pen = 0, glyphs = 0;
            if (fi.IsCidFont)
            {
                for (var i = 0; i + 1 < bytes.Length; i += 2)
                {
                    var code = (bytes[i] << 8) | bytes[i + 1];
                    var a = fi.AdvanceOf(code) * fontSize;
                    glyphs += a;
                    pen += a + charSpacing;
                    perChar?.Add((a + charSpacing, a));
                    perCode?.Add(code);
                }
            }
            else
            {
                foreach (var b8 in bytes)
                {
                    var a = fi.AdvanceOf(b8) * fontSize;
                    glyphs += a;
                    var p1 = a + charSpacing + (b8 == 32 ? wordSpacing : 0);
                    pen += p1;
                    perChar?.Add((p1, a));
                    perCode?.Add(b8);
                }
            }
            return (pen, glyphs);
        }

        // Per-code aligned decode for shows whose whole-string decode EXPANDS
        // (ligature glyphs decoding to several chars): each code's decoded chars
        // share its advance (first char carries it), keeping the per-char lists
        // the line solver needs aligned with the text. Returns null unless the
        // concatenated per-code decode matches the whole-string decode.
        (List<(double pen, double glyph)> perChar, List<int> perCode)? DecodeAligned(
            PdfString ps, string wholeText)
        {
            if (currentFontKey is null || !fonts.TryGetValue(currentFontKey, out var fi)
                || fi.AdvanceOf is null) return null;
            var bytes = ps.Value;
            var step = fi.IsCidFont ? 2 : 1;
            var sbT = new StringBuilder();
            var pcs = new List<(double pen, double glyph)>();
            var codes = new List<int>();
            for (var i = 0; i + step - 1 < bytes.Length; i += step)
            {
                var code = step == 2 ? (bytes[i] << 8) | bytes[i + 1] : bytes[i];
                var seg = step == 2 ? new[] { bytes[i], bytes[i + 1] } : new[] { bytes[i] };
                var dec = DecodeString(new PdfString(seg), currentFontKey, fonts);
                if (dec.Length == 0) continue;
                var a = fi.AdvanceOf(code) * fontSize;
                var p1 = a + charSpacing + (step == 1 && code == 32 ? wordSpacing : 0);
                for (var k = 0; k < dec.Length; k++)
                {
                    sbT.Append(dec[k]);
                    pcs.Add(k == 0 ? (p1, a) : (0.0, 0.0));
                    codes.Add(code);
                }
            }
            return sbT.ToString() == wholeText ? (pcs, codes) : null;
        }

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer: operands.Add(new PdfInteger(token.IntValue)); break;
                case TokenKind.Real: operands.Add(new PdfReal(token.RealValue)); break;
                case TokenKind.LiteralString: operands.Add(new PdfString(token.BytesValue!)); break;
                case TokenKind.HexString: operands.Add(new PdfString(token.BytesValue!, isHex: true)); break;
                case TokenKind.Name: operands.Add(new PdfName(token.StringValue!)); break;
                case TokenKind.ArrayStart:
                    operands.Add(ParseArray(lexer));
                    break;
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    // Operator ordinal within this content stream. A path element's
                    // id is the 0-based index of the operator that opened its
                    // construction, so the emitted SVG identifies each path by
                    // where it is authored rather than by a dense running count.
                    var opIndex0 = opCounter++;
                    if (pathState.Data.Length == 0 && pathOpenIndex < 0
                        && op is "m" or "re" or "l" or "c" or "v" or "y")
                        pathOpenIndex = opIndex0;
                    // UseZOrder paint counter: each path paint op and image Do is
                    // one atomic object (whatever the subpath count or clip
                    // outcome); an ExtGState carrying a soft mask adds the mask
                    // form's own object count at EVERY gs that loads it. Glyphs
                    // count in ShowRun; forms count through their contents.
                    if (zCounter is not null)
                    {
                        switch (op)
                        {
                            case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                                zCounter.V++;
                                break;
                            case "BI":
                                zCounter.V++;
                                break;
                            case "Do":
                                if (operands.Count >= 1 && operands[0] is PdfName zxn
                                    && IsImageXObject(zxn.Value, imageXObjects, resources, reader))
                                    zCounter.V++;
                                break;
                            case "gs":
                                if (resources is not null && operands.Count >= 1 && operands[0] is PdfName zgs)
                                {
                                    var egs = reader.ResolveDict(
                                        reader.ResolveDict(resources.Get("ExtGState"))?.Get(zgs.Value));
                                    var smDict = egs is not null ? reader.ResolveDict(egs.Get("SMask")) : null;
                                    var maskForm = smDict is not null ? reader.ResolveStream(smDict.Get("G")) : null;
                                    if (maskForm is not null)
                                        zCounter.V += CountMaskPaintOps(maskForm, reader, zCounter.MaskMemo, depth: 0);
                                }
                                break;
                        }
                    }
                    switch (op)
                    {
                        // ── Graphics state stack ──
                        case "q":
                            clipStack.Push(clipD);
                            ctmStack.Push(ctm.Clone());
                            // Fill/stroke color are graphics state (PDF 32000 §8.4.2):
                            // a color set inside q…Q must not leak to later text.
                            colorStack.Push((r, g, b,
                                pathState.FillR, pathState.FillG, pathState.FillB,
                                pathState.StrokeR, pathState.StrokeG, pathState.StrokeB));
                            // Character/word spacing are text state, which is part
                            // of the graphics state too: a Tc set inside q…Q (and
                            // never reset by the generator) must not leak into the
                            // blocks that follow the Q.
                            textSpacingStack.Push((charSpacing, wordSpacing));
                            break;
                        case "Q":
                            if (clipStack.Count > 0) clipD = clipStack.Pop();
                            if (ctmStack.Count > 0)
                                ctm = ctmStack.Pop();
                            if (colorStack.Count > 0)
                            {
                                var c9 = colorStack.Pop();
                                r = c9.r; g = c9.g; b = c9.b;
                                pathState.FillR = c9.fr; pathState.FillG = c9.fg; pathState.FillB = c9.fb;
                                pathState.StrokeR = c9.sr; pathState.StrokeG = c9.sg; pathState.StrokeB = c9.sb;
                            }
                            if (textSpacingStack.Count > 0)
                                (charSpacing, wordSpacing) = textSpacingStack.Pop();
                            if (maskGroupOpen && clipStack.Count < maskOpenClipDepth)
                            {
                                svgPaths.Append("</g>");
                                maskGroupOpen = false;
                            }
                            break;

                        // ── ExtGState: a luminosity /SMask masks what follows ──
                        // The mask group is rasterised to a grayscale sidecar
                        // (shaped as the group's BBox at 200 dpi) and
                        // applied as an SVG mask around the painted content until a
                        // gs clears the soft mask or the q-scope pops.
                        case "gs":
                            if (!textOnly && imageSink is not null && resources is not null
                                && operands.Count >= 1 && operands[0] is PdfName gsOpName)
                            {
                                var egsDict = reader.ResolveDict(
                                    reader.ResolveDict(resources.Get("ExtGState"))?.Get(gsOpName.Value));
                                var smaskDict = egsDict is not null ? reader.ResolveDict(egsDict.Get("SMask")) : null;
                                if (egsDict is not null && maskGroupOpen)
                                {
                                    // Any gs that carries an /SMask entry replaces the
                                    // soft mask — /None (a name, not a dict) clears it.
                                    if (egsDict.Get("SMask") is not null && smaskDict is null)
                                    {
                                        svgPaths.Append("</g>");
                                        maskGroupOpen = false;
                                    }
                                }
                                var maskG = smaskDict is not null
                                    && (smaskDict.GetName("S") ?? "Luminosity") == "Luminosity"
                                    ? reader.ResolveStream(smaskDict.Get("G"))
                                    : null;
                                if (maskG is not null && !maskGroupOpen
                                    && RenderLuminosityMaskPng(reader, smaskDict!, maskG, ctm) is { } mr)
                                {
                                    var maskUrl = imageSink.AddRawPng(mr.Png);
                                    var mid = $"svgmask{++imageSink.MaskSeq}";
                                    var msx = (mr.X1 - mr.X0) / mr.PxW;
                                    var msy = (mr.Y1 - mr.Y0) / mr.PxH;
                                    svgPaths.Append(
                                        $"<mask id=\"{mid}\" maskUnits=\"userSpaceOnUse\" " +
                                        $"x=\"{F(mr.X0)}\" y=\"{F(mr.Y0)}\" " +
                                        $"width=\"{F(mr.X1 - mr.X0)}\" height=\"{F(mr.Y1 - mr.Y0)}\">" +
                                        $"<image x=\"0\" y=\"0\" width=\"{mr.PxW}\" height=\"{mr.PxH}\" " +
                                        $"transform=\"matrix({F(msx)} 0 0 {F(-msy)} {F(mr.X0)} {F(mr.Y1)})\" " +
                                        $"xlink:href=\"{maskUrl}\" /></mask>" +
                                        $"<g mask=\"url(#{mid})\">");
                                    maskGroupOpen = true;
                                    maskOpenClipDepth = clipStack.Count;
                                }
                            }
                            break;

                        // ── CTM ──
                        case "cm":
                            if (operands.Count >= 6)
                            {
                                ctm.Concat(
                                    Num(operands[0]), Num(operands[1]),
                                    Num(operands[2]), Num(operands[3]),
                                    Num(operands[4]), Num(operands[5]));
                            }
                            break;

                        // ── Marked content ──
                        // Line grouping distinguishes shows WITHIN one structure
                        // content item from shows across items (see sameLine), so
                        // only /MCID-carrying BDC marks advance the sequence —
                        // ActualText spans and artifacts are not line boundaries.
                        case "BDC":
                            {
                                var hasMcid = operands.Count >= 2
                                    && operands[1] is PdfDictionary mcDict
                                    && mcDict.Get("MCID") is not null;
                                mcStack.Push(hasMcid);
                                if (hasMcid) mcSeq++;
                                // An /OC region becomes a layer box when the caller asked
                                // for marked content as layers: the div carries the
                                // optional-content group's own name, and a group that
                                // sits under a titled /Order entry nests a second div
                                // for the title. Content-stream nesting is mirrored
                                // as-is, so a region marked inside another produces
                                // nested boxes.
                                var opened = 0;
                                if (ocLayers is not null && operands.Count >= 2
                                    && operands[0] is PdfName { Value: "OC" }
                                    && operands[1] is PdfName ocName
                                    && ocLayers.TryGetValue(ocName.Value, out var layer))
                                {
                                    FlushParkedLines();
                                    sb.Append($"<div class=\"{classNamer.Cls("layer")}\" " +
                                        $"data-pdflayer=\"{EscapeHtml(layer.Name)}\">");
                                    opened++;
                                    if (layer.GroupTitle is { Length: > 0 } title)
                                    {
                                        sb.Append($"<div class=\"{classNamer.Cls("layer")}\" " +
                                            $"data-pdflayer=\"{EscapeHtml(title)}\">");
                                        opened++;
                                    }
                                }
                                ocDepth.Push(opened);
                            }
                            break;
                        case "BMC":
                            mcStack.Push(false);
                            ocDepth.Push(0);
                            break;
                        case "EMC":
                            if (mcStack.Count > 0 && mcStack.Pop()) mcSeq++;
                            if (ocDepth.Count > 0 && ocDepth.Pop() is var closing and > 0)
                            {
                                FlushParkedLines();
                                for (var c = 0; c < closing; c++) sb.Append("</div>");
                            }
                            break;

                        // ── Text state ──
                        case "BT":
                            tlm.Set(1, 0, 0, 1, 0, 0);
                            tm.Set(1, 0, 0, 1, 0, 0);
                            tx = 0; ty = 0;
                            break;
                        case "ET":
                            break;
                        case "Tf":
                            if (operands.Count >= 2)
                            {
                                currentFontKey = (operands[0] as PdfName)?.Value;
                                fontSize = Num(operands[1]);
                                if (currentFontKey is not null && fonts.TryGetValue(currentFontKey, out var fi))
                                {
                                    fontFamily = fi.Family;
                                    fontCssFamily = fi.CssFamily;
                                    fontWeight = fi.Weight;
                                    fontStyle = fi.Style;
                                    fontAscent = fi.AscentFactor;
                                    fontLineHeight = fi.LineHeightEm;
                                    fontIsType3 = fi.IsType3;
                                }
                            }
                            break;
                        case "Tr":
                            if (operands.Count >= 1) textRenderMode = (int)Num(operands[0]);
                            break;
                        case "TL":
                            if (operands.Count >= 1)
                            { leading = Num(operands[0]); hasLeading = true; }
                            break;
                        case "Td" or "TD":
                            if (operands.Count >= 2)
                            {
                                if (op == "TD") { leading = -Num(operands[1]); hasLeading = true; }
                                // Text-line matrix is translated in text space, then the
                                // text matrix is reset to it.
                                tlm.Concat(1, 0, 0, 1, Num(operands[0]), Num(operands[1]));
                                tm.CopyFrom(tlm);
                                tx = tm.E; ty = tm.F;
                            }
                            break;
                        case "Tm":
                            if (operands.Count >= 6)
                            {
                                tlm.Set(Num(operands[0]), Num(operands[1]), Num(operands[2]),
                                    Num(operands[3]), Num(operands[4]), Num(operands[5]));
                                tm.CopyFrom(tlm);
                                tx = tm.E; ty = tm.F;
                            }
                            break;
                        case "T*":
                            tlm.Concat(1, 0, 0, 1, 0, -(hasLeading ? leading : fontSize * 1.2));
                            tm.CopyFrom(tlm);
                            tx = tm.E; ty = tm.F;
                            break;
                        case "Ts":
                            if (operands.Count >= 1)
                                rise = Num(operands[0]);
                            break;
                        case "Tc": // character spacing
                            if (operands.Count >= 1)
                                charSpacing = Num(operands[0]);
                            break;
                        case "Tw": // word spacing (single-byte code 32)
                            if (operands.Count >= 1)
                                wordSpacing = Num(operands[0]);
                            break;

                        // ── Color ──
                        case "rg":
                            if (operands.Count >= 3)
                            {
                                r = Num(operands[0]); g = Num(operands[1]); b = Num(operands[2]);
                                pathState.FillR = r; pathState.FillG = g; pathState.FillB = b;
                            }
                            break;
                        case "RG":
                            if (operands.Count >= 3)
                            {
                                pathState.StrokeR = Num(operands[0]);
                                pathState.StrokeG = Num(operands[1]);
                                pathState.StrokeB = Num(operands[2]);
                            }
                            break;
                        case "g":
                            if (operands.Count >= 1)
                            {
                                var gray = Num(operands[0]);
                                r = g = b = gray;
                                pathState.FillR = pathState.FillG = pathState.FillB = gray;
                            }
                            break;
                        case "G":
                            if (operands.Count >= 1)
                            {
                                var gray = Num(operands[0]);
                                pathState.StrokeR = pathState.StrokeG = pathState.StrokeB = gray;
                            }
                            break;
                        case "k":
                            if (operands.Count >= 4)
                            {
                                // Text colours go through the same colour-managed
                                // CMYK profile the render devices use, so the
                                // emitted classes match the rasterized ink.
                                var (lr, lg, lb) = Devices.CmykToRgbLut.Convert(
                                    Num(operands[0]), Num(operands[1]),
                                    Num(operands[2]), Num(operands[3]));
                                r = lr / 255.0; g = lg / 255.0; b = lb / 255.0;
                                pathState.FillR = r; pathState.FillG = g; pathState.FillB = b;
                            }
                            break;
                        case "K":
                            if (operands.Count >= 4)
                            {
                                var (kr, kg, kb) = CmykToRgb(Num(operands[0]), Num(operands[1]),
                                    Num(operands[2]), Num(operands[3]));
                                pathState.StrokeR = kr; pathState.StrokeG = kg; pathState.StrokeB = kb;
                            }
                            break;
                        // Colour space selection: a Separation/DeviceN space carries a
                        // tint transform for its scn operand; other spaces keep the
                        // component-count mapping.
                        case "cs":
                            fillTintMap = operands.Count >= 1 && operands[0] is PdfName fcs
                                ? TryBuildTintMap(resources, fcs.Value, reader) : null;
                            break;
                        case "CS":
                            strokeTintMap = operands.Count >= 1 && operands[0] is PdfName scs
                                ? TryBuildTintMap(resources, scs.Value, reader) : null;
                            break;

                        // Colour in the current colour space (sc/scn): numeric
                        // components map like gray/rgb/cmyk by count; a trailing
                        // pattern NAME operand leaves the colour untouched.
                        case "sc" or "scn":
                            if (fillTintMap is not null && operands.Count == 1
                                && operands[0] is PdfInteger or PdfReal)
                            {
                                var (tr, tg, tb) = fillTintMap(Num(operands[0]));
                                r = tr; g = tg; b = tb;
                                pathState.FillR = tr; pathState.FillG = tg; pathState.FillB = tb;
                            }
                            else if (TryColorComponents(operands, out var fr, out var fg, out var fb))
                            {
                                r = fr; g = fg; b = fb;
                                pathState.FillR = fr; pathState.FillG = fg; pathState.FillB = fb;
                            }
                            break;
                        case "SC" or "SCN":
                            if (strokeTintMap is not null && operands.Count == 1
                                && operands[0] is PdfInteger or PdfReal)
                            {
                                var (tr, tg, tb) = strokeTintMap(Num(operands[0]));
                                pathState.StrokeR = tr; pathState.StrokeG = tg; pathState.StrokeB = tb;
                            }
                            else if (TryColorComponents(operands, out var sr, out var sg, out var sbb))
                            {
                                pathState.StrokeR = sr; pathState.StrokeG = sg; pathState.StrokeB = sbb;
                            }
                            break;

                        // ── Line width ──
                        case "w":
                            // Stroke width lives in user space: fold in the CTM scale.
                            if (operands.Count >= 1)
                                pathState.LineWidth = Num(operands[0]) * ctm.Scale;
                            break;

                        // ── Text showing ──
                        case "Tj":
                            if (operands.Count >= 1 && operands[0] is PdfString s)
                            {
                                var text = DecodeString(s, currentFontKey, fonts);
                                var pc = new List<(double pen, double glyph)>();
                                var pcodes = new List<int>();
                                var (adv, ext) = StringAdvance(s, pc, pcodes);
                                var ok = pc.Count == text.Length;
                                if (!ok && pc.Count > 0 && DecodeAligned(s, text) is { } al)
                                { pc = al.perChar; pcodes = al.perCode; ok = pc.Count == text.Length; }
                                ShowRun(text, adv, ext, ok ? pc : null, ok ? pcodes : null);
                                if (!double.IsNaN(adv))
                                { tm.Concat(1, 0, 0, 1, adv, 0); tx = tm.E; ty = tm.F; }
                            }
                            break;
                        case "TJ":
                            if (operands.Count >= 1 && operands[0] is PdfArray arr)
                            {
                                var tjText = new StringBuilder();
                                double tjAdv = 0, tjExt = 0;
                                var tjChars = new List<(double pen, double glyph)>();
                                var tjCodes = new List<int>();
                                void TjKern(double num)
                                {
                                    pendingTjNum += num;
                                    var d = -num / 1000.0 * fontSize;
                                    if (!double.IsNaN(tjAdv)) tjAdv += d;
                                    if (!double.IsNaN(tjExt)) tjExt += d;
                                    if (num < -100)
                                    {
                                        // a deep kern is a synthesized word space
                                        // whose advance IS the kern gap
                                        tjText.Append(' ');
                                        tjChars.Add((d, d));
                                        tjCodes.Add(-1);
                                    }
                                    else if (tjChars.Count > 0)
                                    {
                                        // a small kern tightens the preceding advance
                                        var lastC = tjChars[^1];
                                        tjChars[^1] = (lastC.pen + d, lastC.glyph + d);
                                    }
                                }
                                foreach (var item in arr)
                                {
                                    if (item is PdfString ts)
                                    {
                                        var t0 = tjText.Length;
                                        var itemText = DecodeString(ts, currentFontKey, fonts);
                                        tjText.Append(itemText);
                                        var itemChars = new List<(double pen, double glyph)>();
                                        var itemCodes = new List<int>();
                                        var (a, e) = StringAdvance(ts, itemChars, itemCodes);
                                        // keep tjChars aligned with tjText even when a
                                        // decode expands/contracts the char count
                                        if (itemChars.Count != itemText.Length && itemChars.Count > 0
                                            && DecodeAligned(ts, itemText) is { } alItem)
                                        {
                                            itemChars = alItem.perChar;
                                            itemCodes = alItem.perCode;
                                        }
                                        if (itemChars.Count == tjText.Length - t0)
                                        {
                                            tjChars.AddRange(itemChars);
                                            tjCodes.AddRange(itemCodes);
                                        }
                                        else
                                        {
                                            for (var fill = t0; fill < tjText.Length; fill++)
                                            {
                                                tjChars.Add((double.NaN, double.NaN));
                                                tjCodes.Add(-1);
                                            }
                                        }
                                        tjAdv = double.IsNaN(tjAdv) || double.IsNaN(a)
                                            ? double.NaN : tjAdv + a;
                                        tjExt = double.IsNaN(tjExt) || double.IsNaN(e)
                                            ? double.NaN : tjExt + e;
                                    }
                                    else if (item is PdfInteger ti)
                                        TjKern(ti.Value);
                                    else if (item is PdfReal tr)
                                        TjKern(tr.Value);
                                }
                                if (tjText.Length > 0)
                                {
                                    var pcOk = !double.IsNaN(tjAdv) && tjChars.Count == tjText.Length;
                                    if (pcOk)
                                        foreach (var e2 in tjChars)
                                            if (double.IsNaN(e2.pen)) { pcOk = false; break; }
                                    ShowRun(tjText.ToString(), tjAdv, tjExt,
                                        pcOk ? tjChars : null, pcOk ? tjCodes : null);
                                }
                                if (!double.IsNaN(tjAdv))
                                { tm.Concat(1, 0, 0, 1, tjAdv, 0); tx = tm.E; ty = tm.F; }
                            }
                            break;
                        case "'":
                            // Move to next line and show string
                            tlm.Concat(1, 0, 0, 1, 0, -(hasLeading ? leading : fontSize * 1.2));
                            tm.CopyFrom(tlm);
                            tx = tm.E; ty = tm.F;
                            if (operands.Count >= 1 && operands[0] is PdfString qs)
                            {
                                var text = DecodeString(qs, currentFontKey, fonts);
                                var qc = new List<(double pen, double glyph)>();
                                var qcodes = new List<int>();
                                var (adv, ext) = StringAdvance(qs, qc, qcodes);
                                var qok = qc.Count == text.Length;
                                if (!qok && qc.Count > 0 && DecodeAligned(qs, text) is { } alq)
                                { qc = alq.perChar; qcodes = alq.perCode; qok = qc.Count == text.Length; }
                                ShowRun(text, adv, ext, qok ? qc : null, qok ? qcodes : null);
                                if (!double.IsNaN(adv))
                                { tm.Concat(1, 0, 0, 1, adv, 0); tx = tm.E; ty = tm.F; }
                            }
                            break;

                        // ── XObject (Do operator): images drawn, forms recursed ──
                        case "Do":
                            // An image XObject paints in graphics modes; anything else
                            // falls through to the FORM branch. Form XObjects carry
                            // their own content (annotation overlays commonly nest
                            // text several forms deep — e.g. rotated note text): both
                            // the text-only overlay and the SVG-text dialect recurse
                            // so that text is not silently dropped. The visited set
                            // guards self-referencing forms (an inner /Fm0 resolving
                            // to its own stream) and is released after the call so a
                            // form invoked at several call sites still renders at each.
                            if (!textOnly && operands.Count >= 1 && operands[0] is PdfName xobjName
                                && imageXObjects.TryGetValue(xobjName.Value, out var img))
                            {
                                // An image ends the parking window only when it lands in the
                                // TEXT LAYER: under the SVG-referenced and data-URI shapes it
                                // goes to the page SVG instead and the layer never sees it, so
                                // lines the producer is still adding to must not close on its
                                // account.
                                var imageEntersTextLayer = imageSink is null
                                    || !(imageSink.SvgImageRefs || imageSink.EmbedDataUris);
                                if (imageEntersTextLayer) FlushParkedLines();
                                if (imageSink is not null) imageSink.Emit(sb, svgPaths, img, ctm, pageHeight);
                                else EmitImage(sb, img, ctm, pageHeight);
                            }
                            // Form XObjects carry their own content (annotation overlays
                            // commonly nest text several forms deep — e.g. rotated note
                            // text, or a datasheet whose whole body table lives in one
                            // form); both the text-only overlay and the full export
                            // recurse so that content is not silently dropped. The
                            // visited set guards self-referencing forms (an inner /Fm0
                            // resolving to its own stream) and is released after the
                            // call so a form invoked at several call sites still
                            // renders at each.
                            else if (resources is not null
                                && operands.Count >= 1 && operands[0] is PdfName formName)
                            {
                                var xoDict = reader.ResolveDict(resources.Get("XObject"));
                                var formStream = xoDict is not null ? reader.ResolveStream(xoDict.Get(formName.Value)) : null;
                                if (formStream is not null && formStream.Dict.GetName("Subtype") == "Form"
                                    && (visitedForms ??= new HashSet<PdfStream>()).Add(formStream))
                                {
                                    byte[]? formBytes = null;
                                    try { formBytes = reader.DecodeStream(formStream); } catch { /* skip undecodable */ }
                                    if (formBytes is not null)
                                    {
                                        FlushParkedLines();
                                        var formRes = reader.ResolveDict(formStream.Dict.Get("Resources")) ?? resources;
                                        var formFonts = ResolveFontsFromResources(formRes, reader, preferFontCmap, substitutors, defaultFontName);
                                        if (formFonts.Count == 0) formFonts = fonts;
                                        // The form's own /XObject images shadow the page's.
                                        var formImages = imageXObjects;
                                        var formXo = reader.ResolveDict(formRes.Get("XObject"));
                                        if (formXo is not null)
                                        {
                                            Dictionary<string, ImageXObject>? own = null;
                                            foreach (var k in formXo.Keys)
                                            {
                                                var imgStream = reader.ResolveStream(formXo.Get(k));
                                                if (imgStream is not null && imgStream.Dict.GetName("Subtype") == "Image")
                                                    (own ??= new Dictionary<string, ImageXObject>(imageXObjects, StringComparer.Ordinal))[k]
                                                        = new ImageXObject(k, imgStream, reader);
                                            }
                                            if (own is not null) formImages = own;
                                        }
                                        var childCtm = ctm.Clone();
                                        if (reader.Resolve(formStream.Dict.Get("Matrix")) is PdfArray fm && fm.Count >= 6)
                                            childCtm.Concat(
                                                Num(reader.Resolve(fm[0])!), Num(reader.Resolve(fm[1])!),
                                                Num(reader.Resolve(fm[2])!), Num(reader.Resolve(fm[3])!),
                                                Num(reader.Resolve(fm[4])!), Num(reader.Resolve(fm[5])!));
                                        RenderContentToHtml(formBytes, formFonts, formImages, reader, sb,
                                            pageHeight, pageWidth, saveTransparentTexts,
                                            emCompensation: emCompensation,
                                            textOnly: textOnly,
                                            externalSvgPaths: textOnly ? null : svgPaths,
                                            imageSink: textOnly ? null : imageSink,
                                            styleReg: styleReg, classNamer: classNamer,
                                            linkTargets: linkTargets,
                                            resources: formRes, preferFontCmap: preferFontCmap,
                                            substitutors: substitutors, initialCtm: childCtm,
                                            visitedForms: visitedForms, rotReg: rotReg,
                                            pageLLX: pageLLX, yTopRef: yTopRef, zCounter: zCounter,
                                            defaultFontName: defaultFontName,
                                            authoredPathShape: authoredPathShape);
                                    }
                                    visitedForms.Remove(formStream);
                                }
                            }
                            break;

                        // ── Path construction ──
                        // Coordinates are user-space: the CTM maps them to the page
                        // space the SVG's outer matrix expects (content drawn under a
                        // scaling cm — e.g. q 0.12 0 0 0.12 0 0 cm — landed kilopoints
                        // off-canvas when emitted raw).
                        case "m": // moveto
                            if (operands.Count >= 2)
                            {
                                var (px, py) = Dp(Num(operands[0]), Num(operands[1]));
                                pathState.Data.Append($"M{F(px)} {F(py)}");
                                cpx = px; cpy = py;
                                (curDevX, curDevY) = Dev(Num(operands[0]), Num(operands[1]));
                            }
                            break;
                        case "l": // lineto
                            if (operands.Count >= 2)
                            {
                                var (px, py) = Dp(Num(operands[0]), Num(operands[1]));
                                pathState.Data.Append($"L{F(px)} {F(py)}");
                                cpx = px; cpy = py;
                                var (lx, ly) = Dev(Num(operands[0]), Num(operands[1]));
                                pathSegs.Add((curDevX, curDevY, lx, ly));
                                (curDevX, curDevY) = (lx, ly);
                            }
                            break;
                        case "c": // curveto
                            if (operands.Count >= 6)
                            {
                                var (x1, y1) = Dp(Num(operands[0]), Num(operands[1]));
                                var (x2, y2) = Dp(Num(operands[2]), Num(operands[3]));
                                var (x3, y3) = Dp(Num(operands[4]), Num(operands[5]));
                                pathState.Data.Append($"C{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x3)} {F(y3)}");
                                cpx = x3; cpy = y3;
                            }
                            break;
                        case "v": // curveto (initial point replicated)
                            if (operands.Count >= 4)
                            {
                                var (x2, y2) = Dp(Num(operands[0]), Num(operands[1]));
                                var (x3, y3) = Dp(Num(operands[2]), Num(operands[3]));
                                pathState.Data.Append($"C{F(cpx)} {F(cpy)} {F(x2)} {F(y2)} {F(x3)} {F(y3)}");
                                cpx = x3; cpy = y3;
                            }
                            break;
                        case "y": // curveto (final point replicated)
                            if (operands.Count >= 4)
                            {
                                var (x1, y1) = Dp(Num(operands[0]), Num(operands[1]));
                                var (x3, y3) = Dp(Num(operands[2]), Num(operands[3]));
                                pathState.Data.Append($"C{F(x1)} {F(y1)} {F(x3)} {F(y3)} {F(x3)} {F(y3)}");
                                cpx = x3; cpy = y3;
                            }
                            break;
                        case "h": // closepath
                            pathState.Data.Append("Z");
                            break;
                        case "re": // rectangle
                            if (operands.Count >= 4)
                            {
                                var rx = Num(operands[0]); var ry = Num(operands[1]);
                                var rw = Num(operands[2]); var rh = Num(operands[3]);
                                var (ax, ay) = Dp(rx, ry);
                                var (bx, by) = Dp(rx + rw, ry);
                                var (cx2, cy2) = Dp(rx + rw, ry + rh);
                                var (dx, dy) = Dp(rx, ry + rh);
                                pathState.Data.Append($"M{F(ax)} {F(ay)}L{F(bx)} {F(by)}L{F(cx2)} {F(cy2)}L{F(dx)} {F(dy)}Z");
                                cpx = ax; cpy = ay;
                                var (dx0, dy0) = Dev(rx, ry);
                                var (dx1, dy1) = Dev(rx + rw, ry + rh);
                                pendingRects.Add((dx0, dy0, dx1, dy1));
                            }
                            break;

                        // ── Path painting ──
                        case "S": // stroke
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: true, fill: false, pageHeight: pageHeight, pathId: pathOpenIndex, authoredShape: authoredPathShape);
                                pathState.Clear(); pathOpenIndex = -1;
                            }
                            CollectRuleCandidates(stroked: true, filled: false);
                            break;
                        case "s": // close and stroke
                            pathState.Data.Append("Z");
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: true, fill: false, pageHeight: pageHeight, pathId: pathOpenIndex, authoredShape: authoredPathShape);
                                pathState.Clear(); pathOpenIndex = -1;
                            }
                            CollectRuleCandidates(stroked: true, filled: false);
                            break;
                        case "f" or "F": // fill (nonzero)
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: false, fill: true, pageHeight: pageHeight, pathId: pathOpenIndex, authoredShape: authoredPathShape);
                                pathState.Clear(); pathOpenIndex = -1;
                            }
                            CollectRuleCandidates(stroked: false, filled: true);
                            break;
                        case "f*": // fill (even-odd)
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: false, fill: true, evenOdd: true, pageHeight: pageHeight, pathId: pathOpenIndex, authoredShape: authoredPathShape);
                                pathState.Clear(); pathOpenIndex = -1;
                            }
                            CollectRuleCandidates(stroked: false, filled: true);
                            break;
                        case "B": // fill and stroke (nonzero)
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: true, fill: true, pageHeight: pageHeight, pathId: pathOpenIndex, authoredShape: authoredPathShape);
                                pathState.Clear(); pathOpenIndex = -1;
                            }
                            CollectRuleCandidates(stroked: true, filled: true);
                            break;
                        case "B*": // fill and stroke (even-odd)
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: true, fill: true, evenOdd: true, pageHeight: pageHeight, pathId: pathOpenIndex, authoredShape: authoredPathShape);
                                pathState.Clear(); pathOpenIndex = -1;
                            }
                            CollectRuleCandidates(stroked: true, filled: true);
                            break;
                        case "b": // close, fill and stroke (nonzero)
                            pathState.Data.Append("Z");
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: true, fill: true, pageHeight: pageHeight, pathId: pathOpenIndex, authoredShape: authoredPathShape);
                                pathState.Clear(); pathOpenIndex = -1;
                            }
                            break;
                        case "b*": // close, fill and stroke (even-odd)
                            pathState.Data.Append("Z");
                            if (pathState.Data.Length > 0)
                            {
                                EmitSvgPath(svgPaths, pathState, stroke: true, fill: true, evenOdd: true, pageHeight: pageHeight, pathId: pathOpenIndex, authoredShape: authoredPathShape);
                                pathState.Clear(); pathOpenIndex = -1;
                            }
                            break;
                        case "W":
                        case "W*":
                            // The clip takes the path as it stands; the painting op that
                            // ends the path commits it.
                            pendingClipD = pathState.Data.ToString().Trim();
                            break;
                        case "n": // end path (no paint)
                            if (pendingClipD is { Length: > 0 }) { clipD = pendingClipD; pendingClipD = null; }
                            pathState.Clear(); pathOpenIndex = -1;
                            pathSegs.Clear();
                            pendingRects.Clear();
                            break;
                        case "sh":
                            // A shading painted straight onto the page fills the current
                            // clip with the gradient the shading dictionary describes.
                            if (!textOnly && resources is not null && operands.Count >= 1
                                && operands[0] is PdfName shName
                                && reader.ResolveDict(resources.Get("Shading")) is { } shDict)
                            {
                                var shading = Aspose.Pdf.Shading.ShadingBase.Parse(shDict.Get(shName.Value), reader);
                                EmitSvgShading(svgPaths, shading, clipD, Dp, ++shadingSeq, pageHeight);
                            }
                            break;
                        case "BI":
                            SkipInlineImage(lexer);
                            operands.Clear();
                            continue;
                    }
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }

        // Flush any trailing grouped line.
        FlushParkedLines();

        // Emit collected SVG paths. When an external collector is supplied the raw
        // A mask group still open at the end of this stream (a form whose content
        // ended inside the masked scope) must not leak an unbalanced element.
        if (maskGroupOpen) svgPaths.Append("</g>");
        // path elements are handed to the caller (which writes them to a sidecar
        // <base>_files/img_NN.svg file); otherwise they are emitted inline.
        if (!textOnly && svgPaths.Length > 0)
        {
            if (externalSvgPaths is not null)
            {
                externalSvgPaths.Append(svgPaths);
            }
            else
            {
                sb.AppendLine($"<svg class=\"pdf-svg\" style=\"position:absolute;top:0;left:0;\" width=\"{F(pageWidth)}pt\" height=\"{F(pageHeight)}pt\" " +
                    $"viewBox=\"0 0 {F(pageWidth)} {F(pageHeight)}\" xmlns=\"http://www.w3.org/2000/svg\">");
                // Flip Y: PDF origin is bottom-left, SVG is top-left
                sb.AppendLine($"<g transform=\"translate(0,{F(pageHeight)}) scale(1,-1)\">");
                sb.Append(svgPaths);
                sb.AppendLine("</g>");
                sb.AppendLine("</svg>");
            }
        }
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
            : $"#{(int)Math.Round(Math.Clamp(R, 0, 1) * 255):X2}" +
              $"{(int)Math.Round(Math.Clamp(G, 0, 1) * 255):X2}" +
              $"{(int)Math.Round(Math.Clamp(B, 0, 1) * 255):X2}";

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

    /// <summary>Solve one line div's glyphs into spans and emit them.
    /// Returns false when the data cannot be solved (caller falls back).</summary>
    private static bool EmitStlSolvedDiv(StringBuilder sb, List<StlLineGlyph> glyphs,
        List<StlRunStyle> styles, StyleRegistry styleReg, ClassNamer classNamer,
        string divCls, string zStyle, double pageLLX, double yTop, double baselineY,
        Func<double, double, LinkTarget?>? linkFor, List<(string Label, string Href)>? popupItems,
        double turnedOverShiftLeftEm = 0, double turnedOverShiftTopEm = 0,
        bool emGrid = false)
    {
        // A drawn NO-BREAK SPACE is a word gap to the line solver, exactly like a
        // drawn space: it is emitted as a plain word space inside one
        // span, and a line made of nothing else is dropped (a scanner's trailing nbsp
        // runs produce whole such rows). Treated as a character instead, it cut a
        // span on both sides - it usually arrives in its own font - atomizing the
        // line into one span per word.
        static bool IsSpaceGlyph(char c) => c is ' ' or ' ';

        // A prefix-joined line arrives with its fragments in DRAW order (body
        // first, title behind it): the em-compensation solve reads the line
        // left-to-right, so a real inversion re-orders by pen position. Kern
        // jitter under a point is left alone; other dialects never prefix-join.
        if (emGrid)
            for (var q = 1; q < glyphs.Count; q++)
                if (glyphs[q].StartX < glyphs[q - 1].StartX - 1.0)
                {
                    glyphs = glyphs.OrderBy(x => x.StartX).ToList();
                    break;
                }

        // 1. Trim line-edge space glyphs.
        int lo = 0, hi = glyphs.Count - 1;
        while (lo <= hi && IsSpaceGlyph(glyphs[lo].Ch)) lo++;
        while (hi >= lo && IsSpaceGlyph(glyphs[hi].Ch)) hi--;
        if (hi < lo) return true;   // whitespace-only line: nothing rendered

        // Every style needs a real browser-model advance: either it resolves to an
        // installed face, or its font serves the embedded program's own metrics.
        // The em-compensation dialect keeps the line in the solved path on the
        // fallback advance model instead — bailing to the legacy group emission
        // flattened a masthead's mixed font/size runs into ONE span (no per-font
        // spans, no column splits, no synthesized gaps) whenever a single piece
        // used a font that neither embeds nor installs.
        foreach (var st in styles)
        {
            if (st.FaceName is null && !st.HasEmbeddedMetrics)
            {
                if (!emGrid) return false;
                st.UseFallbackMetrics = true;
            }
            // A SUBSTITUTE face (SimSun standing in for a font that neither
            // embeds nor installs) measures approximately: the default
            // four-decimal dialect's outlier atomization would cut spans at
            // every drawn-vs-substitute divergence, so those lines keep the
            // legacy group emission there. The em-compensation dialect never
            // atomizes and solves against the substitute basis.
            if (!emGrid && st.SubstituteFace) return false;
        }

        // 2. Build the item stream: chars with advance errors, and space slots
        //    (kept, dropped or synthesized around 0.6×m).
        var items = new List<StlItem>();
        // Facts the number-column split below turns on: whether any REAL space
        // glyph is drawn on the line (a synthesized gap-space advances the pen
        // but carries no drawn advance of its own), and the raw gap behind a
        // single leading character (captured when the first slot lands at
        // items[1]).
        var lineHasSpaceGlyph = false;
        for (var t0 = lo; t0 <= hi; t0++)
            if (IsSpaceGlyph(glyphs[t0].Ch) && !glyphs[t0].SynthSpace) { lineHasSpaceGlyph = true; break; }
        // A uniformly letter-spread line (every inter-char pen gap carries the
        // same tracking) is LETTER-SPACING, not word gaps: such a heading is
        // emitted as plain words ("Journal of Xiangfan University"), not
        // atomized per gap into 'J o u r n a l …'. The em-compensation
        // synthesis therefore measures each gap against the line's TYPICAL
        // inter-char gap (median over positive gaps, 4+ samples) instead of
        // against zero — word boundaries still exceed it by a space width.
        var lineSpreadPt = 0.0;
        if (emGrid)
        {
            var gaps = new List<double>();
            for (var t0 = lo; t0 < hi; t0++)
            {
                if (IsSpaceGlyph(glyphs[t0].Ch) || IsSpaceGlyph(glyphs[t0 + 1].Ch)) continue;
                var rg = glyphs[t0 + 1].StartX - glyphs[t0].StartX - glyphs[t0].WidthsAdv;
                if (rg > 0.02) gaps.Add(rg);
            }
            const int SpreadMinSamples = 4;
            if (gaps.Count >= SpreadMinSamples)
            {
                gaps.Sort();
                lineSpreadPt = gaps[gaps.Count / 2];
            }
        }
        double headGapPt = 0, headFs = 0;
        var i = lo;
        while (i <= hi)
        {
            var g = glyphs[i];
            if (IsSpaceGlyph(g.Ch)) { i++; continue; }   // consumed by gap handling below
            var st = styles[g.Style];
            var fs = Math.Max(0.01, st.FontSize);
            var fsEff = Math.Floor(fs * 1000.0) / 1000.0;

            // A ligature code expanded to several chars renders as its COMPONENT
            // glyphs in the browser model, so the head and its expansion tails
            // fuse into ONE item whose natural width is the face's component
            // advances — the pair's whole advance error is the (small) ligature-
            // vs-components width difference, not two large opposite errors that
            // would atomize the span.
            var itemEnd = i;
            var wSum = g.WidthsAdv;
            double tailW = 0;
            string? itemText = null;
            var fuseByFace = false;
            while (itemEnd + 1 <= hi && glyphs[itemEnd + 1].ExpansionTail
                && glyphs[itemEnd + 1].Style == g.Style)
            {
                itemEnd++;
                wSum += glyphs[itemEnd].WidthsAdv;
                tailW += glyphs[itemEnd].WidthsAdv;
                itemText = (itemText ?? g.Ch.ToString()) + glyphs[itemEnd].Ch;
                fuseByFace |= glyphs[itemEnd].FuseByFace;
            }
            var ttfMilliItem = g.TtfMilli;
            // The components-vs-lig face delta charged to the ws numerator but
            // NOT to the ls mean (LsE): the ls classes ignore it.
            var lsAdjMilli = 0.0;
            if (itemText is not null)
            {
                if (fuseByFace && !emGrid && st.FaceName is not null)
                {
                    double faceSum = 0;
                    foreach (var chF in itemText) faceSum += st.TtfMilli(chF);
                    ttfMilliItem = faceSum;
                }
                else if (emGrid && st.ProgramCharMilli is not null)
                {
                    // The em-compensation FACE basis for a ligature is its
                    // COMPONENT advances from the embedded program's own metrics
                    // (an 'ft' ligature's components-vs-lig delta comes to
                    // +27.34 exactly; 'ffl' +39.55).
                    // Unresolvable components keep the LIG advance.
                    double compSum = 0;
                    var okComp = true;
                    foreach (var chF in itemText)
                    {
                        if (st.ProgramCharMilli(chF) is { } aC) compSum += aC;
                        else { okComp = false; break; }
                    }
                    if (okComp && compSum > 0)
                    {
                        lsAdjMilli = compSum - ttfMilliItem;
                        ttfMilliItem = compSum;
                    }
                    else
                        for (var t = i + 1; t <= itemEnd; t++) ttfMilliItem += glyphs[t].TtfMilli;
                }
                else
                {
                    for (var t = i + 1; t <= itemEnd; t++) ttfMilliItem += glyphs[t].TtfMilli;
                }
            }
            var ttfPt = ttfMilliItem / 1000.0 * fsEff;
            // The em-compensation PEN basis drops the /W-vs-program rounding
            // residue: such a /W is authored as round(program float),
            // so δ = round(float) − float per item — the solve sees each char's
            // error as exactly the kern/gap residue. Other dialects keep the
            // physical /W pen. (The drawn advance wSum itself carries the TJ
            // kern, which must stay.)
            var penPt = wSum;
            if (emGrid && st.HasEmbeddedMetrics)
                penPt -= (Math.Round(g.TtfMilli) - g.TtfMilli) / 1000.0 * fsEff;

            // Locate the next rendered char and whether real space glyphs sit between.
            var j = itemEnd + 1;
            var sawSpace = false;
            var spaceStyle = g.Style;
            double spaceGlyphMilli = 0;
            // The width the drawn space actually contributes, taken from the source's
            // own /Widths rather than from a face measurement: when the run's face is
            // not installed, an unmappable space measures as the half-em guess, which
            // is nearly twice a real space and silently swallows the word break.
            double spaceDrawnPt = 0;
            while (j <= hi && IsSpaceGlyph(glyphs[j].Ch))
            {
                sawSpace = true;
                spaceStyle = glyphs[j].Style;
                spaceGlyphMilli = glyphs[j].TtfMilli;
                spaceDrawnPt += glyphs[j].WidthsAdv;
                j++;
            }

            if (j > hi)
            {
                // Line-final char: error is advance-only and never enters ls. In
                // the em-compensation region an expansion tail's advance residue
                // is the kern ADJACENT TO THE TRAILING SPACE — excluded
                // (a ±288 kern before the trailing space is invisible).
                var eFin = (penPt - (emGrid ? tailW : 0) - ttfPt) / fs * 1000.0;
                items.Add(new StlItem { Ch = g.Ch, Text = itemText, Style = g.Style, StartX = g.StartX,
                    E = eFin, LsE = eFin + (wSum - penPt) / fs * 1000.0 + lsAdjMilli * fsEff / fs, LsEligible = false,
                    FaceMilli = ttfPt / fs * 1000.0 });
                break;
            }

            // The slot metric m: a space glyph of the LINE's own font measures by
            // the font's space advance; a foreign-font word gap (and a
            // synthesized slot) measures by the line font's space advance at the
            // line font's size.
            var mMilliSlot = sawSpace && spaceStyle == g.Style
                ? styles[spaceStyle].SpaceAdvMilli
                : st.SpaceAdvMilli;
            var mPt = mMilliSlot / 1000.0 * fs;
            var gapPt = glyphs[j].StartX - g.StartX - wSum;   // pen end → next char
            // Slot decision. For a synthesized/kern gap (no space glyph drawn) the gap
            // must reach 0.6 of the line font's nominal space advance. When a space glyph
            // WAS drawn, measure against the width that glyph actually contributes: a real
            // word space leaves a gap close to its drawn advance, whereas a space drawn for
            // justification/letter-spacing is pulled back by a following negative kern, so
            // its gap falls well short of the drawn width and must not open a word break.
            // (Against the nominal advance the two are indistinguishable — both ~0.45·m.)
            bool slotFires;
            if (sawSpace)
            {
                var drawnPt = spaceDrawnPt > 0.01
                    ? spaceDrawnPt
                    : spaceGlyphMilli / 1000.0 * styles[spaceStyle].FontSize;
                slotFires = drawnPt > 0.01 ? gapPt >= 0.6 * drawnPt : gapPt >= 0.6 * mPt;
            }
            else
            {
                // The line's uniform tracking (see lineSpreadPt above) is not a
                // word gap; only the excess over it opens a slot.
                slotFires = gapPt - lineSpreadPt >= 0.6 * mPt;
            }

            if (!slotFires)
            {
                // Plain gap (dropped space or kern): folds into this char's error.
                var eFold = (penPt + gapPt - ttfPt) / fs * 1000.0;
                items.Add(new StlItem { Ch = g.Ch, Text = itemText, Style = g.Style, StartX = g.StartX,
                    E = eFold, LsE = eFold + (wSum - penPt) / fs * 1000.0 + lsAdjMilli * fsEff / fs, LsEligible = true,
                    FaceMilli = ttfPt / fs * 1000.0 });
            }
            else
            {
                // Word-final char, then the space slot. The slot rides on the
                // LINE's style (a word-gap space drawn with its own font is
                // coerced — it burns no font class of its own).
                var eWf = (penPt - ttfPt) / fs * 1000.0;
                items.Add(new StlItem { Ch = g.Ch, Text = itemText, Style = g.Style, StartX = g.StartX,
                    E = eWf, LsE = eWf + (wSum - penPt) / fs * 1000.0 + lsAdjMilli * fsEff / fs, LsEligible = false,
                    FaceMilli = ttfPt / fs * 1000.0 });
                items.Add(new StlItem { IsSlot = true, Ch = ' ', Style = g.Style,
                    StartX = g.StartX + wSum,
                    E = (gapPt - mPt) / Math.Max(0.01, styles[spaceStyle].FontSize) * 1000.0,
                    GapPt = gapPt,
                    Synth = !sawSpace,
                    FaceMilli = mPt / Math.Max(0.01, styles[spaceStyle].FontSize) * 1000.0 });
                if (items.Count == 2) { headGapPt = gapPt; headFs = fs; }
            }
            i = j;
        }
        if (items.Count == 0) return true;

        // A single character standing a full quad ahead of the rest of a line
        // that draws NO space glyphs is a NUMBER COLUMN, and a fresh
        // positioned div starts at the text after the gap. The head must be
        // exactly one rendered char (a two-char head or
        // a trailing dot folds), the gap must exceed 0.95 of the font size
        // (0.95 folds, 0.955 splits; no upper bound), the line's font must be
        // under 11.5 pt (11.4 splits, 11.5 folds — headings escape), and one
        // real space glyph anywhere on the line disables the whole rule.
        // Colour, link annotations, font identity and line position play
        // no part.
        if (popupItems is null && !lineHasSpaceGlyph && items.Count >= 3
            && !items[0].IsSlot && items[1].IsSlot && !items[2].IsSlot
            && headGapPt > 0.95 * headFs && headFs < 11.5)
        {
            EmitStlPart(new List<StlItem> { items[0] });
            EmitStlPart(items.GetRange(2, items.Count - 2));
            return true;
        }
        // A TAB — a gap a quad or more past the pen on a line that draws real
        // space glyphs — starts a fresh positioned div per segment: a
        // single-char head splits past 1.05 of the font
        // size (1.00 folds, 1.10 splits), a longer head past 1.50 (1.45 folds,
        // 1.55 splits); smaller stretches stay in the line as word-spacing. A
        // spaceless line keeps the lone-char rule above instead.
        if (popupItems is null)
        {
            const double TabSplitLoneHeadEm = 1.05;
            const double TabSplitWordHeadEm = 1.50;
            // The em-compensation dialect splits a lone head (a list bullet) at a
            // smaller stretch: the bullet MERGES at a 0.8394 em gap and
            // SPLITS at 0.8758 — the default
            // dialect keeps its own 1.05.
            const double TabSplitLoneHeadEmGrid = 0.86;
            // Column-gap split for a SPACELESS em-compensation line: a
            // char-spaced masthead splits into per-name divs at ~1.46 em gaps
            // while a 0.79 em byline gap and a 0.66 em pre-tail gap stay in
            // the line — the spaced-line lone-head threshold sits between those.
            const double TabSplitNoSpaceEmGrid = 1.05;
            List<List<StlItem>>? tabParts = null;
            int partStart = 0, headChars = 0;
            for (var k = 0; k < items.Count; k++)
            {
                if (!items[k].IsSlot) { headChars++; continue; }
                var fsSlot = Math.Max(0.01, styles[items[k].Style].FontSize);
                var loneHeadEm = emGrid ? TabSplitLoneHeadEmGrid : TabSplitLoneHeadEm;
                var thr = (headChars <= 1 ? loneHeadEm : TabSplitWordHeadEm) * fsSlot;
                // A spaceless CJK line still splits at a COLUMN-sized gap in the
                // em-compensation dialect: a masthead's pieces sit 5+ em apart
                // (its word gaps stay under ~0.8 em) and each piece is emitted
                // as its own div rather than a giant word-spacing.
                var canSplit = lineHasSpaceGlyph
                    || (emGrid && items[k].GapPt > TabSplitNoSpaceEmGrid * fsSlot);
                if (canSplit && items[k].GapPt > thr && k + 1 < items.Count)
                {
                    tabParts ??= new List<List<StlItem>>();
                    tabParts.Add(items.GetRange(partStart, k - partStart));
                    partStart = k + 1;
                    headChars = 0;
                }
            }
            if (tabParts is not null)
            {
                tabParts.Add(items.GetRange(partStart, items.Count - partStart));
                foreach (var part in tabParts)
                    if (part.Count > 0) EmitStlPart(part);
                return true;
            }
        }
        EmitStlPart(items);
        return true;

        void EmitStlPart(List<StlItem> items)
        {

        // 3. Span boundaries: style changes and gap atomization.
        var cut = new bool[items.Count];   // cut[k] = span boundary BEFORE item k
        // A slot rides the style of the char it follows, so it stays inside the
        // span it trails; the boundary lands on the next RENDERED char whose
        // style differs from the last rendered one — a word gap between two
        // differently-sized runs must still cut.
        var lastRendered = -1;
        for (var k = 0; k < items.Count; k++)
        {
            if (items[k].IsSlot) continue;
            if (lastRendered >= 0
                && !styles[items[k].Style].SameSpan(styles[items[lastRendered].Style]))
                cut[k] = true;
            lastRendered = k;
        }

        // Atomization inside runs bounded by slots/style cuts. The EXPLICIT
        // em-compensation mode never atomizes: it emits ONE span per
        // style run and absorbs per-char outliers into the quantized line spacing.
        // (The trigger is the OPTION being set - the enum's em member is its
        // first value, but the field's DEFAULT is the pixel mode; a save that
        // never touches it solves at four decimals.)
        const double TAtom = 1000.0 / 11.0;
        var runStart = 0;
        if (!emGrid)
        for (var k = 1; k <= items.Count; k++)
        {
            if (k < items.Count && !items[k].IsSlot && !cut[k] && !items[k - 1].IsSlot) continue;
            // run = items[runStart..k)
            var internals = new List<int>();
            for (var t = runStart; t < k; t++)
                if (!items[t].IsSlot && items[t].LsEligible) internals.Add(t);
            if (internals.Count >= 2)
            {
                for (var t = 0; t < internals.Count; t++)
                {
                    double sum = 0;
                    foreach (var u in internals) if (u != internals[t]) sum += items[u].E;
                    var meanOther = sum / (internals.Count - 1);
                    if (Math.Abs(items[internals[t]].E - meanOther) > TAtom)
                    {
                        var carrier = internals[t];
                        if (carrier > runStart) cut[carrier] = true;                 // [prefix][carrier
                        if (carrier + 1 < items.Count) cut[carrier + 1] = true;      // carrier][first-after
                        if (carrier + 2 < items.Count && !items[carrier + 1].IsSlot
                            && !items[carrier + 2].IsSlot) cut[carrier + 2] = true;  // first-after][rest
                        break;   // one atomization per run
                    }
                }
            }
            runStart = k;
        }

        // 4. Assemble spans left-to-right, folding/externalizing slots.
        var spans = new List<(List<StlItem> Items, int Style, bool IsNbsp, double? InheritWs)>();
        List<StlItem>? cur = null;
        var curStyle = 0;
        var foldedSlots = new List<double>();
        double? pendingInheritWs = null;
        void Close()
        {
            if (cur is { Count: > 0 })
                spans.Add((cur, curStyle, false, pendingInheritWs));
            cur = null;
            foldedSlots.Clear();
            pendingInheritWs = null;
        }
        for (var k = 0; k < items.Count; k++)
        {
            var it = items[k];
            if (it.IsSlot)
            {
                if (cur is null || cur.Count == 0)
                {
                    // Slot with no open span (should not happen mid-line): externalize.
                    spans.Add((new List<StlItem> { it }, it.Style, true, null));
                    pendingInheritWs = it.E / 1000.0;
                    continue;
                }
                var mean = 0.0;
                if (foldedSlots.Count > 0)
                {
                    foreach (var v in foldedSlots) mean += v;
                    mean /= foldedSlots.Count;
                }
                var mMilli = styles[it.Style].SpaceAdvMilli;
                // The em-compensation dialect folds every DRAWN-space slot (the
                // solved ws absorbs their spread); a SYNTHESIZED slot keeps the
                // outlier rule — a char-spaced line's one wide gap becomes its
                // own nbsp span, not a ws inflation. A slot at
                // a CROSS-FONT boundary (next char changes family or size) is
                // charged to the boundary, never to the preceding span's ws —
                // dense-CJK spans carry NO ws; the connector
                // span after them takes the gap. Same-font boundaries keep the
                // fold (the anchored title solve depends on its inter-span
                // slot staying in the title span).
                var crossFont = k + 1 < items.Count && !items[k + 1].IsSlot
                    && (styles[items[k + 1].Style].CssFamily != styles[it.Style].CssFamily
                        || Math.Abs(styles[items[k + 1].Style].FontSize
                                    - styles[it.Style].FontSize) > 0.01);
                if (emGrid && crossFont)
                {
                    Close();
                    spans.Add((new List<StlItem> { it }, it.Style, true, null));
                    pendingInheritWs = it.E / 1000.0;
                    continue;
                }
                if ((emGrid && !it.Synth && !crossFont)
                    || foldedSlots.Count == 0 || Math.Abs(it.E - mean) <= 0.6 * mMilli)
                {
                    foldedSlots.Add(it.E);
                    cur.Add(it);
                }
                else
                {
                    Close();
                    spans.Add((new List<StlItem> { it }, it.Style, true, null));
                    pendingInheritWs = it.E / 1000.0;
                }
                continue;
            }
            if (cur is not null && (cut[k] || !styles[it.Style].SameSpan(styles[curStyle])))
            {
                var inherit = pendingInheritWs;
                Close();
                // A style-matching span straight after an externalized slot inherits
                // its ws; a font-change span does not.
                pendingInheritWs = styles[it.Style].SameSpan(styles[curStyle]) ? inherit : null;
            }
            if (cur is null) { cur = new List<StlItem>(); curStyle = it.Style; }
            cur.Add(it);
        }
        Close();
        if (spans.Count == 0) return;

        // 5. Emit. Div geometry: left from the first rendered item, top from the
        //    first span's font ascent.
        var first = spans[0];
        var st0 = styles[first.Style];
        var left = (first.Items[0].StartX - pageLLX) / 12.0 - turnedOverShiftLeftEm;
        var top = (yTop - baselineY - st0.Ascent * st0.FontSize) / 12.0 - turnedOverShiftTopEm;
        sb.Append($"<div class=\"{divCls}\" style=\"left:{Em4T(left)}em;top:{Em4T(top)}em;{zStyle}\">");

        var popupBoxNum = 0;
        if (popupItems is not null)
        {
            popupBoxNum = styleReg.PopupBox();
            sb.Append($"<div class=\"{classNamer.Cls(popupBoxNum)}\">");
        }

        var renderedChars = 0;
        foreach (var sp in spans)
            if (!sp.IsNbsp) renderedChars += sp.Items.Count(x => !x.IsSlot);

        int lastFontNum = 0, lastLhNum = 0, lastLsNum = 0;
        for (var s = 0; s < spans.Count; s++)
        {
            var (its, styleIdx, isNbsp, inheritWs) = spans[s];
            var st = styles[styleIdx];
            var fs = Math.Max(0.01, st.FontSize);
            // The em-compensation dialect emits the css font-size ROUNDED to the
            // 0.01-em grid (drawn 11 → 0.92em, 15 → 1.25em, 40 → 3.33em); the ws
            // solve above uses the TRUNCATED size — the dialect's own
            // deliberate inconsistency, not to be reconciled.
            var fontNum = styleReg.Font(st.CssFamily,
                emGrid
                    ? Math.Round(st.FontSize / 12.0, 2, MidpointRounding.AwayFromZero)
                    : st.FontSize / 12.0,
                st.CssColor, null,
                st.UseFallbackMetrics ? "Times New Roman" : null);
            var lhNum = styleReg.LineHeight(st.LineHeightEm > 0 ? Math.Round(st.LineHeightEm, 6) : 1.2);

            double lsMilli = 0;
            string text;
            double? wsEm = null;
            if (isNbsp)
            {
                text = "&nbsp;";
                wsEm = Math.Round(its[0].E / 1000.0, 4, MidpointRounding.AwayFromZero);
            }
            else
            {
                var eligible = its.Where(x => !x.IsSlot && x.LsEligible).ToList();
                // A span-final char whose word continues into the next span stays
                // ls-eligible; the builder marked word-finals ineligible already.
                // The mean reads the LIG-basis error (LsE): the components-vs-lig
                // face delta stays out of the ls classes.
                if (eligible.Count > 0)
                    lsMilli = eligible.Average(x => emGrid ? x.LsE : x.E);
                // The em-compensation mode keeps its spacing on a 0.01 em grid: the
                // letter-spacing FLOORS to the grid first (a floor, NOT
                // round-half-away) and the word-spacing
                // then solves against the floored value, absorbing the residue.
                if (emGrid)
                    lsMilli = Math.Floor(lsMilli / 10.0) * 10.0;
                var slots = its.Count(x => x.IsSlot);
                if (slots > 0 && !emGrid)
                {
                    double sumE = 0;
                    for (var t = 0; t < its.Count; t++)
                    {
                        // The four-decimal dialect excludes the line-final char's
                        // own advance residue.
                        var isLineFinal = s == spans.Count - 1 && t == its.Count - 1;
                        if (!isLineFinal) sumE += its[t].E;
                    }
                    // CSS letter-spacing lands after every character - the space
                    // slots included - except a span-final one, whose advance the
                    // next box absorbs; the solve counts terms the same way.
                    var lsTerms = its.Count - (its[^1].IsSlot ? 0 : 1);
                    wsEm = Math.Round(
                        (sumE - lsTerms * lsMilli) / slots / 1000.0,
                        4, MidpointRounding.AwayFromZero);
                }
                else if (slots > 0)
                {
                    // THE EM-COMPENSATION SOLVE:
                    //   ws = R2( (S·ΣE − S·(S−1)·Σface − n·lsFloor) / (D·1000) )
                    // · The solve runs at the css size TRUNCATED to the
                    //   0.01-em grid (0.91em·12 = 10.92 pt for a drawn 11) while
                    //   the markup emits the ROUNDED size (0.92em); S is the
                    //   drawn/solve ratio, and the face side scales by S again
                    //   (the ws deliberately lands short of its own ink).
                    // · n = every region item (interior slots included; the
                    //   trailing inter-span slot of a title span included — that
                    //   slot extends the region to the next span's start, which
                    //   is what makes the solve track the following span rather
                    //   than the title's own ink).
                    // · D counts only KERN-CARRYING slots: a bare drawn space
                    //   (pen gap = its own advance) contributes no divisor. The
                    //   membership floor lies in the (0.1, 48.8) milli-em
                    //   bracket; 20 sits mid-bracket.
                    const double EmGridSlotKernFloorMilli = 20.0;
                    var fsEm = st.FontSize / 12.0;
                    var cssEm = Math.Floor(fsEm * 100.0) / 100.0;
                    var scale = cssEm > 0 ? fsEm / cssEm : 1.0;
                    double sumE = 0, faceSum = 0;
                    foreach (var x in its) { sumE += x.E; faceSum += x.FaceMilli; }
                    var dKern = its.Count(x => x.IsSlot
                        && Math.Abs(x.E) >= EmGridSlotKernFloorMilli);
                    if (dKern == 0) dKern = slots;   // all-bare span: every slot carries
                    var lsTerms = its.Count;
                    wsEm = Math.Round(
                        (scale * sumE - scale * (scale - 1.0) * faceSum - lsTerms * lsMilli)
                        / dKern / 1000.0,
                        2, MidpointRounding.AwayFromZero);
                    var fitEnv = Environment.GetEnvironmentVariable("ASPOSE_PH2_FIT");
                    if (fitEnv is "1" or "2")
                    {
                        var head = new StringBuilder();
                        foreach (var x in its)
                        {
                            if (head.Length >= 28) break;
                            head.Append(x.IsSlot ? ' ' : x.Ch);
                        }
                        Console.Error.WriteLine(
                            $"[fit] n={its.Count} D={dKern}/{slots} S={scale:F5} " +
                            $"sumE={sumE:F2} face={faceSum:F1} ls={lsMilli:F1} " +
                            $"ws={wsEm:F2} |{head}|");
                        if (fitEnv == "2")
                            foreach (var x in its)
                                Console.Error.WriteLine(
                                    $"  [it] {(x.IsSlot ? "SLOT" : (x.Text ?? x.Ch.ToString())),-4} " +
                                    $"E={x.E:F2} face={x.FaceMilli:F1} x={x.StartX:F2}");
                    }
                }
                else if (inheritWs is { } iw && !emGrid)
                {
                    // The em-compensation dialect never inherits a filler's ws:
                    // a slotless span there carries NO word-spacing (the import
                    // charges ws at every adjacent-ideograph boundary, so an
                    // inherited filler rate would re-stretch the whole span).
                    wsEm = Math.Round(iw, 4, MidpointRounding.AwayFromZero);
                }
                var t2 = new StringBuilder();
                foreach (var x in its)
                {
                    if (x.IsSlot) t2.Append(' ');
                    else if (x.Text is not null) t2.Append(x.Text);
                    else t2.Append(x.Ch);
                }
                text = EscapeHtml(t2.ToString());
            }

            var emVal = Math.Round(lsMilli / 1000.0, 4, MidpointRounding.AwayFromZero);
            var pxVal = Math.Round(lsMilli * fs * 4.0 / 3.0 / 1000.0, 4, MidpointRounding.AwayFromZero);
            var lsNum = styleReg.LetterSpacingExact(emVal, pxVal);
            lastFontNum = fontNum; lastLhNum = lhNum; lastLsNum = lsNum;

            // A bold/italic run carries its weight inline: the emitted font class
            // names the FAMILY only, so a viewer falling back to a system face
            // would otherwise render the run regular.
            var weightCss = StlWeightStyleCss(st.FauxBold, st.FontStyle);
            var wsCss = wsEm is { } w
                ? $"word-spacing:{w.ToString("0.####", CultureInfo.InvariantCulture)}em;"
                : "";
            var wsAttr = weightCss.Length + wsCss.Length > 0
                ? $" style=\"{weightCss}{wsCss}\""
                : "";
            // A link annotation covers a RECTANGLE, not a line: each span resolves
            // its OWN target from its glyph extent, so a row of per-word hotspots
            // gives each word its own href instead of putting the whole line inside
            // the first rect's anchor. A span inside a line-wide rect still binds to
            // that rect (it is the first match), which is how a per-word hotspot
            // nested in a row-spanning link ends up with no anchor of its own.
            var spanLink = popupItems is null && linkFor is not null
                ? linkFor(its[0].StartX, its[^1].StartX)
                : null;
            if (spanLink is not null)
            {
                sb.Append($"<a href=\"{EscapeHtml(spanLink.Uri)}\"" +
                    (spanLink.Uri.StartsWith('#') ? ">" : " target=\"_blank\">"));
                spanLink.Wrapped = true;
            }
            sb.Append($"<span class=\"{classNamer.Attr(fontNum, lhNum, lsNum)}\"{wsAttr}>");
            sb.Append(text);
            if (s == spans.Count - 1 && renderedChars > 1 && popupItems is null)
                sb.Append(" &nbsp;");
            sb.Append("</span>");
            if (spanLink is not null) sb.Append("</a>");
        }

        if (popupItems is not null)
        {
            var listNum = styleReg.PopupList(popupBoxNum);
            sb.Append($"<div class=\"{classNamer.Cls(listNum)}\">");
            foreach (var (label, href) in popupItems)
                sb.Append($"<a href=\"{href}\" class=\"{classNamer.Cls(lastFontNum)} " +
                    $"{classNamer.Cls(lastLhNum)}  {classNamer.Cls(lastLsNum)}\">{EscapeHtml(label)}</a>");
            sb.Append("</div></div>");
        }
        sb.Append("</div>\n");
        }
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

    private static IEnumerable<string> OrderStlRun(List<(double L, double T, string Html)> run)
    {
        if (run.Count <= 1)
        {
            foreach (var d in run) yield return d.Html;
            yield break;
        }
        const double RowTol = 0.05;   // divs within this top distance share a row
        const double LaneTol = 0.1;   // lefts within this distance share a column
        var rows = new List<List<(double L, double T, string Html)>>();
        foreach (var d in run.OrderBy(x => x.T).ToList())
        {
            if (rows.Count > 0 && Math.Abs(rows[^1][0].T - d.T) <= RowTol) rows[^1].Add(d);
            else rows.Add(new List<(double L, double T, string Html)> { d });
        }
        static bool Near(double a, double b) => Math.Abs(a - b) <= LaneTol;
        var regions = new List<List<List<(double L, double T, string Html)>>>();
        List<double>? regionLanes = null;
        foreach (var row in rows)
        {
            var rowLanes = new List<double>();
            foreach (var d in row)
                if (!rowLanes.Exists(x => Near(x, d.L))) rowLanes.Add(d.L);
            var chains = regionLanes is not null
                && (rowLanes.TrueForAll(l => regionLanes.Exists(r => Near(l, r)))
                    || regionLanes.TrueForAll(r => rowLanes.Exists(l => Near(l, r))));
            if (!chains)
            {
                regions.Add(new List<List<(double L, double T, string Html)>>());
                regionLanes = new List<double>();
            }
            regions[^1].Add(row);
            foreach (var l in rowLanes)
                if (!regionLanes!.Exists(r => Near(r, l))) regionLanes.Add(l);
        }
        // A LEADER region: every row is a label column plus a title cell whose
        // text runs out in a dot leader. Consecutive leader regions form a
        // CHAIN, and a chain emits as: the first region's label
        // cells plus the HEAD row's label of a deeper second region, then every
        // title cell in row order, then the remaining label cells in row order.
        // Anything else emits region-major: lanes left-to-right, columns
        // top-down.
        static bool IsLeaderRegion(List<List<(double L, double T, string Html)>> region)
        {
            foreach (var row in region)
            {
                if (row.Count < 2) return false;
                var rightmost = row[0];
                foreach (var d in row) if (d.L > rightmost.L) rightmost = d;
                if (!System.Text.RegularExpressions.Regex.IsMatch(rightmost.Html, @"\.{8,}"))
                    return false;
            }
            return true;
        }
        var ri = 0;
        while (ri < regions.Count)
        {
            var chainLen = 0;
            while (ri + chainLen < regions.Count && IsLeaderRegion(regions[ri + chainLen])) chainLen++;
            if (chainLen >= 2)
            {
                var chain = regions.GetRange(ri, chainLen);
                double LabelLeft(List<List<(double L, double T, string Html)>> region)
                {
                    var min = double.MaxValue;
                    foreach (var row in region) foreach (var d in row) min = Math.Min(min, d.L);
                    return min;
                }
                var deeperSecond = LabelLeft(chain[1]) > LabelLeft(chain[0]) + LaneTol;
                var labelsFirst = new List<string>();
                var titles = new List<string>();
                var labelsRest = new List<string>();
                for (var ci = 0; ci < chain.Count; ci++)
                {
                    for (var rowIdx = 0; rowIdx < chain[ci].Count; rowIdx++)
                    {
                        var row = chain[ci][rowIdx];
                        var rightmost = row[0];
                        foreach (var d in row) if (d.L > rightmost.L) rightmost = d;
                        var leading = ci == 0 || (ci == 1 && rowIdx == 0 && deeperSecond);
                        foreach (var d in row)
                        {
                            if (ReferenceEquals(d.Html, rightmost.Html)) titles.Add(d.Html);
                            else if (leading) labelsFirst.Add(d.Html);
                            else labelsRest.Add(d.Html);
                        }
                    }
                }
                foreach (var h in labelsFirst) yield return h;
                foreach (var h in titles) yield return h;
                foreach (var h in labelsRest) yield return h;
                ri += chainLen;
                continue;
            }
            var region = regions[ri];
            var lanes = new List<double>();
            foreach (var row in region)
                foreach (var d in row)
                    if (!lanes.Exists(x => Near(x, d.L))) lanes.Add(d.L);
            lanes.Sort();
            foreach (var lane in lanes)
                foreach (var row in region)
                    foreach (var d in row)
                        if (Near(d.L, lane)) yield return d.Html;
            ri++;
        }
    }
}
