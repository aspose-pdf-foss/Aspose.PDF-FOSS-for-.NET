using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    private static void ExtractRuns(byte[] streamBytes, PdfDictionary resourceDict,
        PdfReader reader, List<RawTextRun> result, int depth = 0,
        Matrix? inheritedCtm = null, List<RawFillRect>? fillRects = null,
        bool useFontEngineEncoding = false, bool keepAllFillRects = false,
        List<RawCoverRect>? coverRects = null,
        (double Llx, double Lly, double Urx, double Ury)? inheritedClip = null,
        bool strictFonts = false,
        HashSet<object>? seenForms = null,
        List<string>? missingFontKeys = null)
    {
        if (depth > 10) return; // prevent infinite recursion
        var fonts = ResolveFonts(resourceDict, reader);
        // A Type3 font carries no /BaseFont; the FontCollection surfaces a synthesised
        // "T3Font_<n>" handle indexed by /Font enumeration order. Mirror that here so a
        // fragment's TextState.Font.FontName reports the same handle (else it falls back
        // to the "Unknown" BaseFont). Keyed by resource name, same order as the collection.
        var t3Names = BuildType3SynthesizedNames(resourceDict, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        string? currentFontName = null;
        string? currentFontNameForGuard = null;
        // A page-level Tf named a font key the Resources hierarchy does not carry:
        // the text it governs cannot be decoded and produces no runs (the miss is
        // reported through missingFontKeys). Form resources are resolved from the
        // form dict alone, so the guard stays page-level.
        var currentFontMissing = false;
        // Strict font-usage guard (mirrors TextAbsorber.EnsureFontSet): a
        // text-showing operator before any Tf in the page content stream is a
        // malformed document — surface IncorrectFontUsageException instead of
        // best-effort output. Form XObjects (depth > 0) inherit the caller's
        // graphics state, so the guard applies to the page level only.
        void EnsureFontSet(string op)
        {
            if (strictFonts && depth == 0 && currentFontNameForGuard is null)
                throw new IncorrectFontUsageException(
                    $"Document error: {op} operator without preceding Tf - no font set for the text segment");
        }
        Dictionary<int, string>? toUnicode = null;
        PdfDictionary? fontDict = null;
        FontMetrics? metrics = null;
        Font? currentFontInfo = null;
        double fontSize = 12;
        double tx = 0, ty = 0;
        double txLine = 0, tyLine = 0; // Line matrix start (e,f of Tm; updated by Td/TD/T*)
        double leading = 0; // Text leading for T*, ', "
        double charSpacing = 0; // Tc operator
        double tmBaseTy = 0;    // F of the last Tm op (BT resets it) - the line frame origin
        double wordSpacing = 0; // Tw operator
        double hScaling = 1.0; // Tz operator (percentage / 100)
        double textRise = 0; // Ts operator (superscript/subscript offset)
        // Text matrix components (a, b, c, d) — updated by Tm.
        // Needed to correctly scale Td/TD/T* displacements (values are in unscaled text space).
        double tmA = 1.0, tmB = 0.0, tmC = 0.0, tmD = 1.0;

        // CTM stack for q/Q/cm operators; inherit from parent context if provided
        var ctm = inheritedCtm ?? Matrix.Identity;
        var ctmStack = new Stack<Matrix>();

        // Graphics state stack — save/restore text state across q/Q.
        // Per PDF spec, the graphics state includes text parameters. We save the simple
        // scalar parameters here (leading, spacing, scaling); font/font-size changes
        // within q/Q blocks are generally followed by a Tf that resets them, so we
        // don't try to restore font dict/metrics. Nonstroking color is part of the
        // graphics state and must be saved/restored alongside text params.
        var gsStack = new Stack<(double leading, double charSpacing, double wordSpacing,
            double hScaling, double textRise, int renderMode, Color fillColor, Color? strokeColor)>();

        // Nonstroking (fill) color tracking for SearchForTextRelatedGraphics.
        // Updated by g/rg/k/sc/scn; saved/restored on q/Q.
        var currentFillColor = Color.Black;

        // Stroking color tracking — captured onto each run's StrokingColor so a
        // round-tripped TextState.StrokingColor survives. Updated by G/RG/K/SC/SCN.
        Color? currentStrokeColor = null;

        // Pending path fragments since the last path-painting operator.
        // We only classify the path as a "rectangle fill" when it contains
        // at least one re and no other path-construction operator (m/l/c/v/y/h).
        // CTM is captured at the time of re so a subsequent cm doesn't shift the rect.
        var pendingPathRects = new List<(double x, double y, double w, double h, Matrix ctmAtRe)>();
        var currentPathHasNonRect = false;
        // Device-space bounding box of the CURRENT path (all construction ops since the
        // last paint op), including curve control points (a safe over-approximation).
        // Used for (a) occlusion covers from non-rect filled paths (rounded rects /
        // polygons drawn over text) and (b) the clip rect a `W … n` establishes.
        double pathMinX = double.PositiveInfinity, pathMinY = double.PositiveInfinity;
        double pathMaxX = double.NegativeInfinity, pathMaxY = double.NegativeInfinity;
        var pathSubpaths = 0; // count of m/re subpath starts — union-bbox covers only trust single-subpath fills (a multi-subpath even-odd fill is usually a hollow frame)
        void AddPathPoint(double px, double py, Matrix m)
        {
            var (dx, dy) = ApplyCtm(px, py, m);
            if (dx < pathMinX) pathMinX = dx;
            if (dy < pathMinY) pathMinY = dy;
            if (dx > pathMaxX) pathMaxX = dx;
            if (dy > pathMaxY) pathMaxY = dy;
        }
        void ResetPathBbox()
        {
            pathMinX = double.PositiveInfinity; pathMinY = double.PositiveInfinity;
            pathMaxX = double.NegativeInfinity; pathMaxY = double.NegativeInfinity;
            pathSubpaths = 0;
        }

        // Active clip rectangle (device space), tracked through q/Q. Non-rect clip
        // paths contribute their bounding box — an over-approximation of the clip
        // region, so "run outside the clip bbox" (or a degenerate sliver clip)
        // remains a safe invisibility signal. Runs record the clip in effect when
        // they were shown; a fully clipped-away run reads as hidden text.
        (double Llx, double Lly, double Urx, double Ury)? currentClip = inheritedClip;
        var clipStack = new Stack<(double Llx, double Lly, double Urx, double Ury)?>();
        var pendingClip = false; // a W/W* was seen for the current path
        void ApplyPendingClip()
        {
            if (!pendingClip) return;
            pendingClip = false;
            if (double.IsInfinity(pathMinX)) return; // empty path — nothing to intersect
            var c = (pathMinX, pathMinY, pathMaxX, pathMaxY);
            if (currentClip is { } prev)
                c = (Math.Max(prev.Llx, c.pathMinX), Math.Max(prev.Lly, c.pathMinY),
                     Math.Min(prev.Urx, c.pathMaxX), Math.Min(prev.Ury, c.pathMaxY));
            currentClip = c;
        }
        // Stroked-path points (from m/l) + current line width, so a horizontal stroked
        // line — the common way an underline/strikeout is drawn — is captured as a thin
        // decoration rect on the S/s operator.
        var strokePts = new List<(double x, double y, Matrix ctm)>();
        double currentLineWidth = 1.0;

        // Text rendering mode (0=fill, 3=invisible, etc.)
        int renderMode = 0;

        // Font style flags (resolved from font descriptor or BaseFont name)
        bool currentIsBold = false;
        bool currentIsItalic = false;
        // Font-intrinsic bold state (from descriptor/name), separate from Tr-based bold
        bool fontIsBold = false;

        // Track the Y position of the last actually-emitted text run.
        // Used by the Tm handler to avoid false "\n" sentinels when BT resets ty=0
        // but the next text block is on the same visual line as the previous one.
        double lastEmittedY = double.NaN;
        // The same position in PAGE space. Some producers draw every run in its own
        // q/cm/BT..ET/Q block with Tm y = 0 and the line position carried entirely by
        // the cm translation — there text-space Y never changes between lines and the
        // sentinel must compare page-space Y instead.
        double lastEmittedPageY = double.NaN;

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add(new PdfInteger(token.IntValue));
                    break;
                case TokenKind.Real:
                    operands.Add(new PdfReal(token.RealValue));
                    break;
                case TokenKind.LiteralString:
                    operands.Add(new PdfString(token.BytesValue!));
                    break;
                case TokenKind.HexString:
                    operands.Add(new PdfString(token.BytesValue!, isHex: true));
                    break;
                case TokenKind.Name:
                    operands.Add(new PdfName(token.StringValue!));
                    break;
                case TokenKind.ArrayStart:
                {
                    var arr = ParseArray(lexer);
                    operands.Add(arr);
                    break;
                }
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        // ── Graphics state: CTM tracking ──
                        case "q":
                            ctmStack.Push(ctm);
                            clipStack.Push(currentClip);
                            // Save text state parameters (part of the graphics state per PDF spec).
                            gsStack.Push((leading, charSpacing, wordSpacing, hScaling, textRise, renderMode, currentFillColor, currentStrokeColor));
                            break;
                        case "Q":
                            if (ctmStack.Count > 0)
                                ctm = ctmStack.Pop();
                            if (clipStack.Count > 0)
                                currentClip = clipStack.Pop();
                            if (gsStack.Count > 0)
                            {
                                var saved = gsStack.Pop();
                                leading = saved.leading;
                                charSpacing = saved.charSpacing;
                                wordSpacing = saved.wordSpacing;
                                hScaling = saved.hScaling;
                                textRise = saved.textRise;
                                renderMode = saved.renderMode;
                                currentFillColor = saved.fillColor;
                                currentStrokeColor = saved.strokeColor;
                            }
                            break;
                        case "cm":
                            if (operands.Count >= 6)
                            {
                                var m = new Matrix(
                                    GetNum(operands[0]), GetNum(operands[1]),
                                    GetNum(operands[2]), GetNum(operands[3]),
                                    GetNum(operands[4]), GetNum(operands[5]));
                                ctm = m.Multiply(ctm);
                            }
                            break;

                        // ── Nonstroking (fill) color operators ──
                        // Tracked unconditionally so each fragment's ForegroundColor
                        // reflects the glyph fill colour in effect when it was drawn,
                        // not only under SearchForTextRelatedGraphics.
                        case "g":
                            if (operands.Count >= 1)
                                currentFillColor = Color.FromGray(GetNum(operands[0]));
                            break;
                        case "rg":
                            if (operands.Count >= 3)
                                currentFillColor = Color.FromRgb(
                                    GetNum(operands[0]), GetNum(operands[1]), GetNum(operands[2]));
                            break;
                        case "k":
                            if (operands.Count >= 4)
                                currentFillColor = Color.FromCmyk(
                                    GetNum(operands[0]), GetNum(operands[1]),
                                    GetNum(operands[2]), GetNum(operands[3]));
                            break;
                        case "sc":
                        case "scn":
                            // Pick by operand count: 1=gray, 3=rgb, 4=cmyk.
                            // Pattern color spaces (where scn takes a /Name) fall through unchanged.
                            if (operands.Count == 1 && operands[0] is not PdfName)
                                currentFillColor = Color.FromGray(GetNum(operands[0]));
                            else if (operands.Count == 3)
                                currentFillColor = Color.FromRgb(
                                    GetNum(operands[0]), GetNum(operands[1]), GetNum(operands[2]));
                            else if (operands.Count == 4)
                                currentFillColor = Color.FromCmyk(
                                    GetNum(operands[0]), GetNum(operands[1]),
                                    GetNum(operands[2]), GetNum(operands[3]));
                            break;

                        // ── Stroking color operators ──
                        // Captured unconditionally onto each run's StrokingColor so a
                        // round-tripped TextState.StrokingColor (e.g. stroked text) survives.
                        case "G":
                            if (operands.Count >= 1)
                                currentStrokeColor = Color.FromGray(GetNum(operands[0]));
                            break;
                        case "RG":
                            if (operands.Count >= 3)
                                currentStrokeColor = Color.FromRgb(
                                    GetNum(operands[0]), GetNum(operands[1]), GetNum(operands[2]));
                            break;
                        case "K":
                            if (operands.Count >= 4)
                                currentStrokeColor = Color.FromCmyk(
                                    GetNum(operands[0]), GetNum(operands[1]),
                                    GetNum(operands[2]), GetNum(operands[3]));
                            break;
                        case "SC":
                        case "SCN":
                            if (operands.Count == 1 && operands[0] is not PdfName)
                                currentStrokeColor = Color.FromGray(GetNum(operands[0]));
                            else if (operands.Count == 3)
                                currentStrokeColor = Color.FromRgb(
                                    GetNum(operands[0]), GetNum(operands[1]), GetNum(operands[2]));
                            else if (operands.Count == 4)
                                currentStrokeColor = Color.FromCmyk(
                                    GetNum(operands[0]), GetNum(operands[1]),
                                    GetNum(operands[2]), GetNum(operands[3]));
                            break;

                        case "w":
                            if (fillRects is not null && operands.Count >= 1)
                                currentLineWidth = GetNum(operands[0]);
                            break;

                        // ── Path construction ──
                        case "re":
                            if (operands.Count >= 4)
                            {
                                var rx = GetNum(operands[0]);
                                var ry = GetNum(operands[1]);
                                var rw = GetNum(operands[2]);
                                var rh = GetNum(operands[3]);
                                if (fillRects is not null || coverRects is not null)
                                    pendingPathRects.Add((rx, ry, rw, rh, ctm));
                                pathSubpaths++;
                                AddPathPoint(rx, ry, ctm);
                                AddPathPoint(rx + rw, ry, ctm);
                                AddPathPoint(rx + rw, ry + rh, ctm);
                                AddPathPoint(rx, ry + rh, ctm);
                            }
                            break;
                        case "m":
                        case "l":
                            if (fillRects is not null || coverRects is not null)
                                currentPathHasNonRect = true;
                            if (operands.Count >= 2)
                            {
                                if (fillRects is not null || coverRects is not null)
                                    strokePts.Add((GetNum(operands[0]), GetNum(operands[1]), ctm));
                                if (op == "m") pathSubpaths++;
                                AddPathPoint(GetNum(operands[0]), GetNum(operands[1]), ctm);
                            }
                            break;
                        case "c":
                        case "v":
                        case "y":
                            if (fillRects is not null || coverRects is not null) currentPathHasNonRect = true;
                            // Control points over-approximate the curve's bbox — safe for
                            // both clip tracking and cover capture.
                            for (var pi = 0; pi + 1 < operands.Count; pi += 2)
                                AddPathPoint(GetNum(operands[pi]), GetNum(operands[pi + 1]), ctm);
                            break;
                        case "h":
                            if (fillRects is not null || coverRects is not null) currentPathHasNonRect = true;
                            break;

                        // ── Clipping ──
                        case "W":
                        case "W*":
                            // The clip takes effect at the next path-painting operator,
                            // intersecting the active clip with this path's bbox.
                            pendingClip = true;
                            break;

                        // ── Path painting: emit pending rects as fill rects. ──
                        // f/F/f*/B/b/B*/b* paint the current path (with fill); n/S/s do not fill.
                        case "f":
                        case "F":
                        case "f*":
                        case "B":
                        case "B*":
                        case "b":
                        case "b*":
                            if ((fillRects is not null || coverRects is not null)
                                && !currentPathHasNonRect && pendingPathRects.Count > 0)
                            {
                                // In default mode (no graphics/underline option) only thin rects —
                                // underline/strikeout candidates — are retained, so always-on
                                // collection stays cheap on graphics-heavy pages. When a consumer
                                // that needs full fill geometry (background capture) is enabled,
                                // keep every rect.
                                bool keepAll = keepAllFillRects;
                                foreach (var (x, y, w, h, ctmAtRe) in pendingPathRects)
                                {
                                    // Transform the four corners by the CTM at the time of re,
                                    // then take the axis-aligned bounding box (handles rotation).
                                    var (x1, y1) = ApplyCtm(x, y, ctmAtRe);
                                    var (x2, y2) = ApplyCtm(x + w, y, ctmAtRe);
                                    var (x3, y3) = ApplyCtm(x + w, y + h, ctmAtRe);
                                    var (x4, y4) = ApplyCtm(x, y + h, ctmAtRe);
                                    var llx = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
                                    var lly = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
                                    var urx = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
                                    var ury = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));
                                    // Occlusion candidates: a body-sized opaque fill (both
                                    // dimensions above glyph-bar size) that paints AFTER the
                                    // runs collected so far covers them (redaction-style
                                    // hidden text). RunsBefore = the run count at paint time.
                                    if (coverRects is not null && (ury - lly) >= 6.0 && (urx - llx) >= 6.0)
                                        AddCoverRect(coverRects, llx, lly, urx, ury, currentClip, result.Count);
                                    if (fillRects is null) continue;
                                    if (!keepAll && (ury - lly) >= 6.0) continue;
                                    fillRects.Add(new RawFillRect(llx, lly, urx, ury, currentFillColor, x, y, w, h));
                                }
                            }
                            // Non-rect fills also occlude: a rounded rect (m/c/…/h) or a
                            // closed polygon painted over text hides it just like a `re`
                            // fill. Trust the union bbox only for a SINGLE subpath — a
                            // multi-subpath even-odd fill is typically a hollow frame
                            // whose bbox interior stays visible.
                            else if (coverRects is not null && currentPathHasNonRect
                                && pathSubpaths == 1 && !double.IsInfinity(pathMinX)
                                && (pathMaxY - pathMinY) >= 6.0 && (pathMaxX - pathMinX) >= 6.0)
                            {
                                AddCoverRect(coverRects, pathMinX, pathMinY, pathMaxX, pathMaxY, currentClip, result.Count);
                            }
                            ApplyPendingClip();
                            ResetPathBbox();
                            pendingPathRects.Clear();
                            currentPathHasNonRect = false;
                            strokePts.Clear();
                            break;
                        case "S":
                        case "s":
                            // A horizontal stroked segment is an underline/strikeout rule:
                            // record it as a thin decoration rect (height = line width).
                            if (fillRects is not null && strokePts.Count == 2)
                            {
                                var (ax, ay, actm) = strokePts[0];
                                var (bx, by, _) = strokePts[1];
                                if (Math.Abs(ay - by) < 1e-3)
                                {
                                    var half = Math.Max(currentLineWidth, 0.1) / 2.0;
                                    var (lx, lyC) = ApplyCtm(Math.Min(ax, bx), ay, actm);
                                    var (rx, ryC) = ApplyCtm(Math.Max(ax, bx), ay, actm);
                                    var lineY = (lyC + ryC) / 2;
                                    var (_, hTop) = ApplyCtm(Math.Min(ax, bx), ay + half, actm);
                                    var thick = Math.Abs(hTop - lineY) * 2;
                                    if (thick < 0.2) thick = Math.Max(currentLineWidth, 0.5);
                                    var lly = lineY - thick / 2;
                                    var ury = lineY + thick / 2;
                                    if (keepAllFillRects || (ury - lly) < 6.0)
                                        fillRects.Add(new RawFillRect(
                                            Math.Min(lx, rx), lly, Math.Max(lx, rx), ury,
                                            currentStrokeColor ?? Color.Black, Math.Min(ax, bx), ay,
                                            Math.Abs(bx - ax), thick));
                                }
                            }
                            ApplyPendingClip();
                            ResetPathBbox();
                            pendingPathRects.Clear();
                            currentPathHasNonRect = false;
                            strokePts.Clear();
                            break;
                        case "n":
                            ApplyPendingClip();
                            ResetPathBbox();
                            pendingPathRects.Clear();
                            currentPathHasNonRect = false;
                            strokePts.Clear();
                            break;

                        // ── Text block delimiters ──
                        case "BT":
                            // PDF spec: BT resets the text matrix and text line matrix to identity.
                            // Reset text position and matrix components so subsequent Td/TD/Tm start fresh.
                            // Do NOT reset lastEmittedY — it tracks cross-BT-block Y position
                            // to prevent spurious newline sentinels between adjacent BT blocks.
                            tx = txLine = 0;
                            ty = tyLine = 0;
                            tmA = 1.0; tmB = 0.0; tmC = 0.0; tmD = 1.0;
                            tmBaseTy = 0;
                            break;

                        // ── Text state operators ──
                        case "Tf":
                            if (operands.Count >= 2 && operands[0] is PdfName fn)
                            {
                                currentFontName = fn.Value;
                                currentFontNameForGuard = fn.Value;
                                fontSize = GetNum(operands[1]);
                                currentIsBold = false;
                                fontIsBold = false;
                                currentIsItalic = false;
                                currentFontMissing = false;
                                if (fonts.TryGetValue(currentFontName, out var fd))
                                {
                                    fontDict = fd;
                                    // Strict validation: a TrueType subset addressed by raw glyph
                                    // indices (FirstChar < 32) with NEITHER /Encoding NOR /ToUnicode
                                    // NOR a decodable cmap in its embedded program (only a symbolic
                                    // (3,0) subtable) cannot be decoded — decoding throws
                                    // rather than emitting garbage (a (1,0) Mac subtable
                                    // keeps such a subset extractable). IgnoreResourceFontErrors
                                    // opts out.
                                    if (strictFonts && depth == 0
                                        && fd.GetName("Subtype") == "TrueType"
                                        && fd.Get("Encoding") is null
                                        && fd.Get("ToUnicode") is null
                                        && (int)fd.GetInt("FirstChar") is > 0 and < 32
                                        && !IsStandardSymbolFamily(fd.GetName("BaseFont"))
                                        && HasOnlySymbolCmap(fd, reader))
                                        throw new IncorrectFontUsageException(
                                            $"Font {fn.Value} cannot be used for text extraction: no encoding or Unicode mapping is available.");
                                    // Prefer BaseFont name (e.g. "ArialMT") over resource key (e.g. "TT2")
                                    var baseFontName = fd.GetName("BaseFont");
                                    if (baseFontName is not null)
                                        currentFontName = baseFontName;
                                    // UseFontEngineEncoding: ignore /ToUnicode and decode via
                                    // the font program's own encoding/cmap instead (recovers
                                    // text when the ToUnicode map is wrong or absent).
                                    toUnicode = useFontEngineEncoding
                                        ? null
                                        : TextAbsorber.ParseToUnicodeFromDict(fd, reader);
                                    metrics = FontMetrics.FromFontDict(fd, reader);

                                    // Create FontInfo from the resolved font dictionary
                                    currentFontInfo = new Font(fn.Value, fd, reader);
                                    // Nameless Type3 fonts report the collection's synthesised
                                    // "T3Font_<n>" handle rather than the "Unknown" BaseFont.
                                    if (currentFontInfo.IsNamelessType3
                                        && t3Names.TryGetValue(fn.Value, out var t3n))
                                        currentFontInfo.SynthesizedFontName = t3n;

                                    // Resolve bold/italic from font descriptor flags
                                    var descriptor = reader.ResolveDict(fd.Get("FontDescriptor"));
                                    if (descriptor is not null)
                                    {
                                        var flagsVal = (int)descriptor.GetInt("Flags");
                                        currentIsItalic = (flagsVal & 64) != 0;
                                        currentIsBold = (flagsVal & (1 << 18)) != 0;
                                    }
                                    // Also check BaseFont name for bold/italic hints
                                    if (baseFontName is not null)
                                    {
                                        var upper = baseFontName.ToUpperInvariant();
                                        if (!currentIsBold && (upper.Contains("BOLD") || upper.Contains(",BOLD")))
                                            currentIsBold = true;
                                        if (!currentIsItalic && (upper.Contains("ITALIC") || upper.Contains("OBLIQUE") || upper.Contains(",ITALIC")))
                                            currentIsItalic = true;
                                    }
                                    fontIsBold = currentIsBold;
                                    // Apply Tr-based bold if current render mode is fill+stroke
                                    if (renderMode == 2)
                                        currentIsBold = true;
                                }
                                else if (depth == 0
                                    && !TextAbsorber.FontResourceKeyExists(resourceDict, reader, fn.Value))
                                {
                                    // Only a key genuinely ABSENT from the Resources
                                    // hierarchy drops its text and gets reported. A key
                                    // that is present but unresolvable in-memory (a
                                    // just-registered replacement font awaiting save)
                                    // keeps the legacy carry-over decode.
                                    currentFontMissing = true;
                                    if (missingFontKeys is not null
                                        && !missingFontKeys.Contains(fn.Value))
                                        missingFontKeys.Add(fn.Value);
                                }
                            }
                            break;
                        case "TL":
                            if (operands.Count >= 1)
                                leading = GetNum(operands[0]);
                            break;
                        case "Tr":
                            if (operands.Count >= 1)
                            {
                                renderMode = (int)GetNum(operands[0]);
                                // Rendering mode 2 (fill+stroke) visually simulates bold text.
                                // Restore font-intrinsic bold when mode changes away from 2.
                                currentIsBold = renderMode == 2 || fontIsBold;
                            }
                            break;
                        case "Tc":
                            if (operands.Count >= 1)
                                charSpacing = GetNum(operands[0]);
                            break;
                        case "Tw":
                            if (operands.Count >= 1)
                                wordSpacing = GetNum(operands[0]);
                            break;
                        case "Tz":
                            if (operands.Count >= 1)
                                hScaling = GetNum(operands[0]) / 100.0;
                            break;
                        case "Ts":
                            if (operands.Count >= 1)
                                textRise = GetNum(operands[0]);
                            break;

                        // ── Text positioning operators ──
                        case "Td":
                            if (operands.Count >= 2)
                            {
                                var tdxVal = GetNum(operands[0]);
                                var tdyVal = GetNum(operands[1]);
                                // Td values are in unscaled text space; apply the text matrix to convert
                                // to content-stream space: new_line = Tm(a,b,c,d) × (tdx, tdy) + old_line.
                                txLine = tmA * tdxVal + tmC * tdyVal + txLine;
                                tyLine = tmB * tdxVal + tmD * tdyVal + tyLine;
                                tx = txLine;
                                ty = tyLine;
                                // Insert newline sentinel for significant vertical displacement.
                                var pageDisp = Math.Abs(tmB * tdxVal + tmD * tdyVal);
                                if (pageDisp > 0.5 && result.Count > 0 && result[^1].Text != "\r\n")
                                    result.Add(new RawTextRun("\r\n", tx, ty, fontSize, currentFontName, 0, ctm, metrics));
                            }
                            break;
                        case "TD":
                            if (operands.Count >= 2)
                            {
                                var tdxD = GetNum(operands[0]);
                                var tdyD = GetNum(operands[1]);
                                txLine = tmA * tdxD + tmC * tdyD + txLine;
                                tyLine = tmB * tdxD + tmD * tdyD + tyLine;
                                tx = txLine;
                                ty = tyLine;
                                leading = -tdyD; // TD sets TL = -ty (in unscaled text space)
                                var pageDispD = Math.Abs(tmB * tdxD + tmD * tdyD);
                                if (pageDispD > 0.5 && result.Count > 0 && result[^1].Text != "\r\n")
                                    result.Add(new RawTextRun("\r\n", tx, ty, fontSize, currentFontName, 0, ctm, metrics));
                            }
                            break;
                        case "Tm":
                            if (operands.Count >= 6)
                            {
                                var newTmTx = GetNum(operands[4]);
                                var newTmTy = GetNum(operands[5]);
                                // Track all Tm components so Td/TD/T* can scale displacements correctly.
                                tmA = GetNum(operands[0]);
                                tmB = GetNum(operands[1]);
                                tmC = GetNum(operands[2]);
                                tmD = GetNum(operands[3]); // raw value; use Math.Abs where needed for thresholds
                                // Emit newline sentinel when Tm repositions to a different Y line.
                                // Compare against lastEmittedY (not ty) so that BT resets (ty=0)
                                // don't cause false newlines when consecutive BT blocks are on the same line.
                                var tmRefY = !double.IsNaN(lastEmittedY) ? lastEmittedY : ty;
                                bool tmLineBreak = Math.Abs(newTmTy - tmRefY) > Math.Max(1.0, fontSize * 0.3);
                                // Producers that carry the line position in each block's cm
                                // (Tm y stays 0 across all lines) defeat the text-space test:
                                // compare page-space Y as well, thresholded by the EFFECTIVE
                                // page-space font size, so their line breaks are still seen.
                                // STRICTLY axis-aligned geometry only: both the Tm and the CTM
                                // must be rotation-free. Under a rotated CTM the page-Y of a Tm
                                // position varies with text-X, so stacked rotated labels (Tm
                                // identity, rotation in cm) would get a false sentinel per label;
                                // a rotated/curved Tm moves page-Y along the line the same way.
                                // With both rotation-free, page-Y depends on text-Y and the cm
                                // translation alone — exactly the per-block-cm producer shape.
                                if (!tmLineBreak && !double.IsNaN(lastEmittedPageY)
                                    && Math.Abs(tmB) <= 1e-4 * Math.Abs(tmA)
                                    && Math.Abs(ctm.B) <= 1e-4 * Math.Abs(ctm.A)
                                    && Math.Abs(ctm.C) <= 1e-4 * Math.Abs(ctm.D))
                                {
                                    var (_, newPageY) = ApplyCtm(newTmTx, newTmTy, ctm);
                                    var effTmFs = fontSize * Math.Max(Math.Abs(tmD), 0.001)
                                        * Math.Sqrt(Math.Abs(ctm.A * ctm.D - ctm.B * ctm.C));
                                    tmLineBreak = Math.Abs(newPageY - lastEmittedPageY)
                                        > Math.Max(1.0, effTmFs * 0.3);
                                }
                                if (tmLineBreak
                                    && result.Count > 0 && result[^1].Text != "\r\n")
                                    result.Add(new RawTextRun("\r\n", newTmTx, newTmTy, fontSize, currentFontName, 0, ctm, metrics));
                                tx = txLine = newTmTx;
                                ty = tyLine = newTmTy;
                                tmBaseTy = newTmTy;
                            }
                            break;
                        case "T*":
                            // T* = Td(0, -TL): move to the start of the next line.
                            // Apply the text matrix scale to the leading displacement.
                            txLine = tmA * 0 + tmC * (-leading) + txLine;
                            tyLine = tmB * 0 + tmD * (-leading) + tyLine;
                            tx = txLine;
                            ty = tyLine;
                            {
                                // Unlike Td/Tm (where a sentinel after a sentinel usually means the
                                // producer re-stated the same move), each T* is an explicit one-line
                                // advance, so consecutive T* = genuinely blank lines. Emit a sentinel
                                // per advance; sentinel consumers already skip runs of them.
                                var pageDispStar = Math.Abs(Math.Abs(tmD) * leading);
                                if (pageDispStar > 0.5 && result.Count > 0)
                                    result.Add(new RawTextRun("\r\n", tx, ty, fontSize, currentFontName, 0, ctm, metrics));
                            }
                            break;

                        // ── Text showing operators ──
                        case "Tj":
                            EnsureFontSet("Tj");
                            if (currentFontMissing) break;
                            if (operands.Count >= 1 && operands[0] is PdfString s)
                            {
                                var text = DecodeBytes(s.Value, toUnicode, fontDict, reader, useFontEngineEncoding);
                                var rawWidth = metrics?.MeasureStringExact(s.Value, fontSize) ?? 0;
                                var numChars = text.Length;
                                var numSpaces = text.Count(c => c == ' ');
                                var unscaledWidth = rawWidth + charSpacing * numChars + wordSpacing * numSpaces;
                                var scaledWidth = unscaledWidth * hScaling;
                                // Build per-character cumulative widths from byte-level
                                // metrics so segment positioning is consistent with how
                                // tx is advanced. Without this, MeasureString(string)
                                // may give different results than MeasureString(bytes)
                                // for fonts with custom encodings or differing glyph
                                // indices, causing segment X offsets to drift.
                                double[]? tjCharCumWidths = null;
                                if (metrics is not null && text.Length == s.Value.Length)
                                {
                                    // n+1 entries: cumWidths[i] = advance to start of char i;
                                    // cumWidths[n] = total advance past last char (incl. trailing Tc).
                                    var cumWidths = new double[text.Length + 1];
                                    double cumW = 0;
                                    for (var ci = 0; ci < s.Value.Length; ci++)
                                    {
                                        cumWidths[ci] = cumW;
                                        var charW = metrics.MeasureStringExact(
                                            s.Value[ci..(ci + 1)], fontSize);
                                        var isSpace = ci < text.Length && text[ci] == ' ';
                                        cumW += charW + charSpacing
                                            + (isSpace ? wordSpacing : 0);
                                    }
                                    cumWidths[text.Length] = cumW;
                                    tjCharCumWidths = cumWidths;
                                }
                                else if (metrics is not null && text.Length > 0
                                    && s.Value.Length == text.Length * 2)
                                {
                                    // CID font: 2 bytes per character
                                    var cumWidths = new double[text.Length + 1];
                                    double cumW = 0;
                                    for (var ci = 0; ci < text.Length; ci++)
                                    {
                                        cumWidths[ci] = cumW;
                                        var charW = metrics.MeasureStringExact(
                                            s.Value[(ci * 2)..(ci * 2 + 2)], fontSize);
                                        cumW += charW + charSpacing
                                            + (text[ci] == ' ' ? wordSpacing : 0);
                                    }
                                    cumWidths[text.Length] = cumW;
                                    tjCharCumWidths = cumWidths;
                                }
                                else if (metrics is not null && text.Length > 0
                                    && s.Value.Length != text.Length)
                                {
                                    // Other encoding mismatch: distribute proportionally
                                    // from byte-level measured width
                                    var cumWidths = new double[text.Length + 1];
                                    for (var ci = 0; ci <= text.Length; ci++)
                                        cumWidths[ci] = unscaledWidth * ci / text.Length;
                                    tjCharCumWidths = cumWidths;
                                }

                                NormalizeDegenerateCumWidths(tjCharCumWidths);
                                // RawTextRun.Width stores unscaled width (CTM handles visual scaling)
                                result.Add(new RawTextRun(text, tx, ty, fontSize, currentFontName, unscaledWidth, ctm, metrics,
                                    TmA: tmA, TmB: tmB, TmC: tmC, TmD: tmD,
                                    CharCumWidths: tjCharCumWidths,
                                    RenderingMode: renderMode,
                                    IsBold: currentIsBold, IsItalic: currentIsItalic, FontInfoObj: currentFontInfo,
                                    HScaling: hScaling,
                                    TextRise: textRise,
                                    FillColor: currentFillColor, StrokingColor: currentStrokeColor,
                                    ClipRect: currentClip, CharSpacing: charSpacing, WordSpacing: wordSpacing, TmBaseY: tmBaseTy));
                                lastEmittedY = ty;
                                (_, lastEmittedPageY) = ApplyCtm(tx, ty, ctm);
                                // Advance position uses scaled width
                                tx += tmA * scaledWidth;
                                ty += tmB * scaledWidth;
                            }
                            break;
                        case "TJ":
                            EnsureFontSet("TJ");
                            if (currentFontMissing) break;
                            if (operands.Count >= 1 && operands[0] is PdfArray arr)
                            {
                                var sb = new StringBuilder();
                                double tjWidth = 0;
                                double tjWidthUnscaled = 0; // same as tjWidth but without hScaling
                                // Segment origin + consumed advance: a huge intra-TJ kern
                                // (> ~1.5 em) SPLITS the array into separate runs at their
                                // drawn positions (the Flatten tokenization rule).
                                double segTx = tx, segTy = ty, consumedW = 0;
                                int lastStrLen = 0; // decoded length of last PdfString element
                                // Track per-character cumulative advance widths WITHOUT hScaling.
                                // Rectangle width should not include Tz scaling — CTM handles
                                // the visual scaling. This matches .NET behavior.
                                var charCumWidthsList = new List<double>();
                                // Parallel list: position just AFTER each character's own glyph
                                // advance, BEFORE any TJ kerning that follows.  Fragment-width
                                // computation uses this for the match's final character so that
                                // compensation kernings sitting between the matched region and
                                // subsequent runs don't inflate the fragment's rectangle.
                                var charEndPositionsList = new List<double>();

                                // Synthetic-space eligibility (validated over a
                                // 1231-run corpus with zero mismatches): a TJ
                                // run inserts ONE space per numeric adjustment ≤ −130/1000 em
                                // iff it is "armed" — any piece of ≥2 glyphs, or any glyph
                                // that is NOT an uppercase letter or punctuation (lowercase,
                                // digits, spaces and symbols arm; tracked caps-only display
                                // text like "(A)-417(R)-416(K)" collapses in EVERY font type) —
                                // AND it is not the letter-tracking shape: an array of MORE
                                // than 10 pieces that are ALL single-glyph collapses with no
                                // synthetic spaces; word-piece prose arrays of any length keep
                                // their kern-encoded word gaps.
                                var tjIsType0 = fontDict?.GetName("Subtype") == "Type0";
                                var tjPieceCount = 0;
                                var tjMultiGlyphPiece = false;
                                var tjAdjList = new List<double>();
                                foreach (var pre in arr)
                                    if (pre is PdfString preS0)
                                    {
                                        tjPieceCount++;
                                        if (preS0.Value.Length >= (tjIsType0 ? 4 : 2)) tjMultiGlyphPiece = true;
                                    }
                                    else
                                        tjAdjList.Add(GetNum(pre));
                                var tjArmed = tjMultiGlyphPiece;
                                if (!tjArmed)
                                    foreach (var pre in arr)
                                    {
                                        if (pre is not PdfString preS) continue;
                                        var preDec = DecodeBytes(preS.Value, toUnicode, fontDict, reader, useFontEngineEncoding);
                                        if (preDec.Length >= 2) { tjArmed = true; tjMultiGlyphPiece = true; break; }
                                        var preArm = false;
                                        foreach (var preC in preDec)
                                            if (!char.IsUpper(preC) && !char.IsPunctuation(preC))
                                            { preArm = true; break; }
                                        if (preArm) { tjArmed = true; break; }
                                    }
                                var tjSynthSpaces = tjArmed && tjPieceCount >= 2
                                    && (tjPieceCount <= 10 || tjMultiGlyphPiece);
                                // Letter-tracked single-glyph arrays (the disarmed shape) can still
                                // encode WORD gaps — as kern OUTLIERS against the array's uniform
                                // tracking baseline rather than absolute-threshold kerns (letters
                                // tracked at +20..+58, words at −135..−169). Break where the
                                // adjustment falls ≥130/1000 em BELOW the array's median; a
                                // uniformly tracked display run (every kern ≈ the median) still
                                // collapses. Mirrors the TextAbsorber rule.
                                var tjLtrackMedian = double.NaN;
                                if (!tjSynthSpaces && !tjMultiGlyphPiece && tjPieceCount >= 3 && tjAdjList.Count >= 2)
                                {
                                    tjAdjList.Sort();
                                    tjLtrackMedian = tjAdjList[tjAdjList.Count / 2];
                                }

                                for (int tjIdx = 0; tjIdx < arr.Count; tjIdx++)
                                {
                                    var item = arr[tjIdx];
                                    if (item is PdfString ps)
                                    {
                                        var decoded = DecodeBytes(ps.Value, toUnicode, fontDict, reader, useFontEngineEncoding);
                                        lastStrLen = decoded.Length;
                                        // Build per-character cumulative widths from byte-level metrics
                                        // so that TJ kerning before/between segments is correctly tracked.
                                        double segAdvance = 0;
                                        if (metrics is not null)
                                        {
                                            // Detect CID font: 2 bytes per character.
                                            int byteLen = (ps.Value.Length > 0 && decoded.Length > 0
                                                && ps.Value.Length == decoded.Length * 2) ? 2 : 1;
                                            for (var ci = 0; ci < ps.Value.Length; )
                                            {
                                                charCumWidthsList.Add(tjWidthUnscaled + segAdvance);
                                                var bl = Math.Min(byteLen, ps.Value.Length - ci);
                                                // float-rounded (glyph advances live in
                                                // float32; logged widths carry the float noise —
                                                // "26.79240010261536" — and tests compare log LENGTHS).
                                                var charW = (double)(float)metrics.MeasureStringExact(ps.Value[ci..(ci + bl)], fontSize);
                                                var charIdx = byteLen == 2 ? ci / 2 : ci;
                                                var isSpace = charIdx < decoded.Length && decoded[charIdx] == ' ';
                                                var advance = charW + charSpacing + (isSpace ? wordSpacing : 0);
                                                segAdvance += advance;
                                                charEndPositionsList.Add(tjWidthUnscaled + segAdvance);
                                                ci += bl;
                                            }
                                        }
                                        else
                                        {
                                            // No metrics: distribute total width proportionally
                                            for (var ci = 0; ci < decoded.Length; ci++)
                                            {
                                                charCumWidthsList.Add(tjWidthUnscaled + segAdvance);
                                                charEndPositionsList.Add(tjWidthUnscaled + segAdvance);
                                            }
                                        }
                                        sb.Append(decoded);
                                        var segW = (double)(float)(metrics?.MeasureStringExact(ps.Value, fontSize) ?? 0);
                                        var segSpaces = decoded.Count(c => c == ' ');
                                        var unscaledAdvance = segW + charSpacing * decoded.Length + wordSpacing * segSpaces;
                                        tjWidth += unscaledAdvance * hScaling;
                                        tjWidthUnscaled += unscaledAdvance;
                                    }
                                    else
                                    {
                                        // Kerning adjustment: value in thousandths of text space unit
                                        // Negative values move right, positive move left
                                        var adj = GetNum(item);
                                        var kernPt = -adj * fontSize / 1000.0;
                                        tjWidth += kernPt * hScaling;
                                        tjWidthUnscaled += kernPt;

                                        // Insert ONE synthetic space per adjustment ≤ −130 when the
                                        // run is eligible (see the prescan note above). The only
                                        // suppression is a space GLYPH immediately left of the gap
                                        // (a kern between a real space and the next word never
                                        // doubles); a real space FOLLOWING the gap does not
                                        // suppress — "T·−175·(sp)" extracts as "T␣␣".
                                        // A LARGE POSITIVE adjustment (≥1 em) is a backward pen jump —
                                        // a producer drawing same-row columns right-to-left inside one
                                        // TJ ('14.400'(+8691)'14.650') — UNLESS the pen lands just
                                        // right of an already-drawn CHAR's start (within ~1 em): a
                                        // draw-order zigzag continuing a visually contiguous token
                                        // ('1'(+13341)'1' landing one glyph right of the prior '1' in
                                        // a giant-advance font) stays glued. Char STARTS, not advance
                                        // ends — these producers carry column pitch in the advances.
                                        var backJumpBreaks = adj >= 1000;
                                        if (backJumpBreaks)
                                            foreach (var cs in charCumWidthsList)
                                            {
                                                var d = tjWidthUnscaled - cs;
                                                if (d > 0 && d <= 1.0 * fontSize) { backJumpBreaks = false; break; }
                                            }
                                        if (((tjSynthSpaces && adj <= -130)
                                             || (!double.IsNaN(tjLtrackMedian) && adj - tjLtrackMedian <= -130
                                                 && (tjLtrackMedian >= 0 || adj <= -250))
                                             || backJumpBreaks)
                                            && sb.Length > 0 && sb[^1] != ' ')
                                        {
                                            sb.Append(' ');
                                            charCumWidthsList.Add(tjWidthUnscaled); // space inserted at current position
                                            charEndPositionsList.Add(tjWidthUnscaled);
                                        }
                                    }
                                }
                                // Add n+1 entry (total width) for trailing Tc detection and clipping.
                                if (charCumWidthsList.Count == sb.Length)
                                    charCumWidthsList.Add(tjWidthUnscaled);
                                var charCumWidths = charCumWidthsList.Count == sb.Length + 1
                                    ? charCumWidthsList.ToArray() : null;
                                NormalizeDegenerateCumWidths(charCumWidths);
                                var charEndPositions = charEndPositionsList.Count == sb.Length
                                    ? charEndPositionsList.ToArray() : null;
                                // Use unscaled width for rectangle computation (CTM handles visual scaling)
                                result.Add(new RawTextRun(sb.ToString(), segTx, segTy, fontSize, currentFontName, tjWidthUnscaled, ctm, metrics,
                                    TmA: tmA, TmB: tmB, TmC: tmC, TmD: tmD, CharCumWidths: charCumWidths,
                                    CharEndPositions: charEndPositions, RenderingMode: renderMode,
                                    IsBold: currentIsBold, IsItalic: currentIsItalic, FontInfoObj: currentFontInfo,
                                    HScaling: hScaling, TextRise: textRise, FillColor: currentFillColor, StrokingColor: currentStrokeColor,
                                    ClipRect: currentClip, CharSpacing: charSpacing, WordSpacing: wordSpacing, TmBaseY: tmBaseTy));
                                lastEmittedY = ty;
                                (_, lastEmittedPageY) = ApplyCtm(tx, ty, ctm);
                                // Advance position through text matrix (for rotated text tmB≠0 advances Y)
                                tx += tmA * (consumedW + tjWidth);
                                ty += tmB * (consumedW + tjWidth);
                            }
                            break;
                        case "'":
                            // Move to next line (T* equivalent), then show text
                            txLine = tmA * 0 + tmC * (-leading) + txLine;
                            tyLine = tmB * 0 + tmD * (-leading) + tyLine;
                            tx = txLine; ty = tyLine;
                            if (result.Count > 0 && result[^1].Text != "\r\n")
                                result.Add(new RawTextRun("\r\n", tx, ty, fontSize, currentFontName, 0, ctm, metrics));
                            EnsureFontSet("'");
                            if (currentFontMissing) break;
                            if (operands.Count >= 1 && operands[0] is PdfString s2)
                            {
                                var text2 = DecodeBytes(s2.Value, toUnicode, fontDict, reader, useFontEngineEncoding);
                                var rawW2 = metrics?.MeasureString(s2.Value, fontSize) ?? 0;
                                var nSp2 = text2.Count(c => c == ' ');
                                var unscW2 = rawW2 + charSpacing * text2.Length + wordSpacing * nSp2;
                                var w2 = unscW2 * hScaling;
                                result.Add(new RawTextRun(text2, tx, ty, fontSize, currentFontName, unscW2, ctm, metrics,
                                    CharCumWidths: BuildCumWidthsForString(s2.Value, text2, metrics, fontSize, charSpacing, wordSpacing, unscW2),
                                    TmA: tmA, TmB: tmB, TmC: tmC, TmD: tmD, RenderingMode: renderMode,
                                    IsBold: currentIsBold, IsItalic: currentIsItalic, FontInfoObj: currentFontInfo,
                                    HScaling: hScaling, TextRise: textRise, FillColor: currentFillColor, StrokingColor: currentStrokeColor,
                                    ClipRect: currentClip, CharSpacing: charSpacing, WordSpacing: wordSpacing, TmBaseY: tmBaseTy));
                                tx += tmA * w2;
                                ty += tmB * w2;
                            }
                            break;
                        case "\"":
                            // Set word spacing, char spacing, move to next line, show text
                            if (operands.Count >= 3)
                            {
                                wordSpacing = GetNum(operands[0]);
                                charSpacing = GetNum(operands[1]);
                            }
                            txLine = tmA * 0 + tmC * (-leading) + txLine;
                            tyLine = tmB * 0 + tmD * (-leading) + tyLine;
                            tx = txLine; ty = tyLine;
                            if (result.Count > 0 && result[^1].Text != "\r\n")
                                result.Add(new RawTextRun("\r\n", tx, ty, fontSize, currentFontName, 0, ctm, metrics));
                            if (!currentFontMissing && operands.Count >= 3 && operands[2] is PdfString s3)
                            {
                                var text3 = DecodeBytes(s3.Value, toUnicode, fontDict, reader, useFontEngineEncoding);
                                var rawW3 = metrics?.MeasureString(s3.Value, fontSize) ?? 0;
                                var nSp3 = text3.Count(c => c == ' ');
                                var unscW3 = rawW3 + charSpacing * text3.Length + wordSpacing * nSp3;
                                var w3 = unscW3 * hScaling;
                                result.Add(new RawTextRun(text3, tx, ty, fontSize, currentFontName, unscW3, ctm, metrics,
                                    CharCumWidths: BuildCumWidthsForString(s3.Value, text3, metrics, fontSize, charSpacing, wordSpacing, unscW3),
                                    TmA: tmA, TmB: tmB, TmC: tmC, TmD: tmD, RenderingMode: renderMode,
                                    IsBold: currentIsBold, IsItalic: currentIsItalic, FontInfoObj: currentFontInfo,
                                    HScaling: hScaling, TextRise: textRise, FillColor: currentFillColor, StrokingColor: currentStrokeColor,
                                    ClipRect: currentClip, CharSpacing: charSpacing, WordSpacing: wordSpacing, TmBaseY: tmBaseTy));
                                tx += tmA * w3;
                                ty += tmB * w3;
                            }
                            break;

                        // ── Inline image — skip binary data ──
                        case "BI":
                            SkipInlineImage(lexer);
                            operands.Clear();
                            continue;

                        // ── XObject invocation ──
                        case "Do":
                            if (operands.Count >= 1 && operands[0] is PdfName xobjName)
                            {
                                var xobjects = TextAbsorber.ResolveXObjects(resourceDict, reader);
                                if (xobjects is not null)
                                {
                                    var xobjStream = reader.ResolveStream(xobjects.Get(xobjName.Value));
                                    if (xobjStream is not null &&
                                        reader.ResolveName(xobjStream.Dict, "Subtype") == "Form")
                                    {
                                        // Within one absorber run a Form XObject INDIRECT OBJECT is
                                        // absorbed at most once — the first Do wins, every later Do of
                                        // the same object (same page or a later page of a document
                                        // walk) contributes nothing. Keyed by object identity: two
                                        // distinct objects with identical bytes are both absorbed, a
                                        // different placement matrix does not defeat the dedup.
                                        if (seenForms is not null && !seenForms.Add(xobjStream))
                                            break;
                                        var xobjBytes = reader.DecodeStream(xobjStream);
                                        var xobjDict = xobjStream.Dict;

                                        // Compute the CTM for the XObject: current CTM × form's own /Matrix
                                        var xobjCtm = ctm;
                                        var matrixArr = reader.ResolveArray(xobjDict.Get("Matrix"));
                                        if (matrixArr is { Count: >= 6 })
                                        {
                                            var fm = new Matrix(
                                                GetNum(matrixArr[0]), GetNum(matrixArr[1]),
                                                GetNum(matrixArr[2]), GetNum(matrixArr[3]),
                                                GetNum(matrixArr[4]), GetNum(matrixArr[5]));
                                            xobjCtm = fm.Multiply(ctm);
                                        }

                                        var runCountBefore = result.Count;
                                        ExtractRuns(xobjBytes, xobjDict, reader, result, depth + 1, xobjCtm, fillRects, useFontEngineEncoding, keepAllFillRects, coverRects, currentClip, seenForms: seenForms, missingFontKeys: missingFontKeys);
                                        // Stamp the runs this form produced with their source
                                        // stream (innermost wins for nested forms — inner
                                        // recursion stamped its own runs first).
                                        for (var ri = runCountBefore; ri < result.Count; ri++)
                                            if (result[ri].SourceXObj is null)
                                                result[ri] = result[ri] with { SourceXObj = xobjStream };
                                    }
                                }
                            }
                            break;
                    }
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }
    }

    /// <summary>Per-character cumulative advances for a show string (n+1 entries),
    /// from byte-level metrics — the same construction the Tj path uses inline; the
    /// ' and \" show operators share it so substring matches inside their runs
    /// measure with real glyph widths instead of a uniform split.</summary>
    private static double[]? BuildCumWidthsForString(byte[] bytes, string text,
        FontMetrics? metrics, double fontSize, double charSpacing, double wordSpacing,
        double unscaledWidth)
    {
        if (metrics is null || text.Length == 0) return null;
        double[]? cum = null;
        if (text.Length == bytes.Length)
        {
            cum = new double[text.Length + 1];
            double w = 0;
            for (var ci = 0; ci < bytes.Length; ci++)
            {
                cum[ci] = w;
                w += metrics.MeasureStringExact(bytes[ci..(ci + 1)], fontSize)
                    + charSpacing + (text[ci] == ' ' ? wordSpacing : 0);
            }
            cum[text.Length] = w;
        }
        else if (bytes.Length == text.Length * 2)
        {
            cum = new double[text.Length + 1];
            double w = 0;
            for (var ci = 0; ci < text.Length; ci++)
            {
                cum[ci] = w;
                w += metrics.MeasureStringExact(bytes[(ci * 2)..(ci * 2 + 2)], fontSize)
                    + charSpacing + (text[ci] == ' ' ? wordSpacing : 0);
            }
            cum[text.Length] = w;
        }
        else
        {
            cum = new double[text.Length + 1];
            for (var ci = 0; ci <= text.Length; ci++)
                cum[ci] = unscaledWidth * ci / text.Length;
        }
        NormalizeDegenerateCumWidths(cum);
        return cum;
    }

    private static string DecodeBytes(byte[] bytes, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader, bool useFontEngineEncoding = false)
    {
        // Delegate to TextAbsorber for consistent encoding handling. Fragments keep
        // U+00A0 verbatim (it is folded only in plain-text extraction).
        return TextAbsorber.DecodeStringPublic(bytes, toUnicode, fontDict, reader, useFontEngineEncoding,
            foldNbsp: false);
    }

    private static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
        => TextAbsorber.ResolveFonts(pageDict, reader);

    private static List<byte[]> GetContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream)
            result.Add(reader.DecodeStream(stream));
        else if (obj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null) result.Add(reader.DecodeStream(s));
            }
        }
        return result;
    }

    /// <summary>Skip inline image data (BI . ID &lt;data&gt; EI) per PDF spec §8.9.7.</summary>
    private static void SkipInlineImage(PdfLexer lexer) => TextAbsorber.SkipInlineImage(lexer);

    private static PdfArray ParseArray(PdfLexer lexer)
    {
        var arr = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind)
            {
                case TokenKind.Integer: arr.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: arr.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: arr.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: arr.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: arr.Add(new PdfName(t.StringValue!)); break;
            }
        }
        return arr;
    }

    private static double GetNum(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    /// <param name="CharCumWidths">
    /// Per-character cumulative advance widths (text-space).
    /// CharCumWidths[i] = total advance from run start to the START of character i.
    /// Accounts for TJ kerning adjustments.  Null if not tracked (use proportional fallback).
    /// </param>
    /// <summary>
    /// A filled rectangle painted by the content stream — collected when
    /// <see cref="TextSearchOptions.SearchForTextRelatedGraphics"/> is enabled
    /// so that a fragment whose origin falls inside one of these rects can be
    /// reported with the rect's fill color as <c>TextState.BackgroundColor</c>.
    /// Coordinates are in the same absorber-space as <see cref="RawTextRun"/>
    /// (post-CTM, post-page-rotation).
    /// </summary>
    // RawX/Y/W/H carry the untransformed `re` operands so a save-time removal pass can
    // match and splice the exact rectangle operator out of the page content stream.
    private readonly record struct RawFillRect(double Llx, double Lly, double Urx, double Ury, Color FillColor,
        double RawX = 0, double RawY = 0, double RawW = 0, double RawH = 0);

    /// <summary>Spatial index over a page's fill rects keyed on each rect's vertical
    /// midpoint. The per-run background/underline/strikeout probes only care about rects
    /// near the run's baseline, so this lets them test a small Y-band slice instead of
    /// scanning the whole list — a vector-art page can carry hundreds of thousands of thin
    /// strokes, turning the old linear scans into an O(runs × rects) blow-up.
    ///
    /// Each probe originally reverse-scanned the list and returned the first hit, i.e. the
    /// matching rect with the greatest original index (top of the paint order). <see
    /// cref="FindTopMatch"/> reproduces that exactly by returning the max-index match among
    /// the candidates, so the result is order-independent and identical to the full scan as
    /// long as the candidate set covers every possible match. Rects taller than
    /// <see cref="TallCut"/> (whose midpoint can sit far from a baseline they still straddle)
    /// are always tested, so they are never missed by the Y-band slice.</summary>
    private sealed class FillRectIndex
    {
        private const double TallCut = 6.0;
        private readonly List<RawFillRect> _rects;
        private readonly int[] _order;   // rect indices sorted by cy ascending
        private readonly double[] _cy;   // cy of _rects[_order[k]] — parallel to _order, sorted
        private readonly List<int> _tall;

        public FillRectIndex(List<RawFillRect> rects)
        {
            _rects = rects;
            var n = rects.Count;
            _order = new int[n];
            var cyAll = new double[n];
            _tall = new List<int>();
            for (var i = 0; i < n; i++)
            {
                _order[i] = i;
                cyAll[i] = (rects[i].Lly + rects[i].Ury) / 2;
                if (rects[i].Ury - rects[i].Lly > TallCut) _tall.Add(i);
            }
            Array.Sort(_order, (a, b) => cyAll[a].CompareTo(cyAll[b]));
            _cy = new double[n];
            for (var k = 0; k < n; k++) _cy[k] = cyAll[_order[k]];
        }

        /// <summary>The extra Y margin a caller should add on each side of the exact
        /// predicate band so that a thin rect (height ≤ <see cref="TallCut"/>) whose
        /// midpoint drifts from the band edge is still in the slice.</summary>
        public const double Margin = TallCut;

        /// <summary>Return the candidate rect with the greatest original index whose
        /// midpoint lies in [yLo, yHi] (or that is a tall rect) and that satisfies
        /// <paramref name="match"/> — the same rect the old reverse full-scan returned.</summary>
        public RawFillRect? FindTopMatch(double yLo, double yHi, Func<RawFillRect, bool> match)
        {
            var best = -1;
            for (var k = LowerBound(yLo); k < _cy.Length && _cy[k] <= yHi; k++)
            {
                var idx = _order[k];
                if (idx > best && match(_rects[idx])) best = idx;
            }
            foreach (var idx in _tall)
                if (idx > best && match(_rects[idx])) best = idx;
            return best >= 0 ? _rects[best] : null;
        }

        private int LowerBound(double y)
        {
            int lo = 0, hi = _cy.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (_cy[mid] < y) lo = mid + 1; else hi = mid;
            }
            return lo;
        }
    }

    // A body-sized opaque fill rect (page space) that can OCCLUDE earlier text.
    // RunsBefore = result.Count when the rect painted: runs with a smaller index
    // were drawn before it and are covered if their box lies inside the rect.
    private readonly record struct RawCoverRect(double Llx, double Lly, double Urx, double Ury, int RunsBefore);

    // Record an occlusion cover, restricted to the active clip (paint can't cover
    // beyond the clip region). Skipped when the clipped remainder drops below
    // body size — a sliver can't hide a text line.
    private static void AddCoverRect(List<RawCoverRect> covers, double llx, double lly, double urx, double ury,
        (double Llx, double Lly, double Urx, double Ury)? clip, int runsBefore)
    {
        if (clip is { } c)
        {
            llx = Math.Max(llx, c.Llx); lly = Math.Max(lly, c.Lly);
            urx = Math.Min(urx, c.Urx); ury = Math.Min(ury, c.Ury);
            if ((ury - lly) < 6.0 || (urx - llx) < 6.0) return;
        }
        covers.Add(new RawCoverRect(llx, lly, urx, ury, runsBefore));
    }

    private readonly record struct RawTextRun(string Text, double X, double Y, double FontSize,
        string? FontName, double Width, Matrix Ctm, FontMetrics? Metrics = null,
        double TmA = 1.0, double TmB = 0.0, double TmC = 0.0, double TmD = 1.0,
        double[]? CharCumWidths = null, int RenderingMode = 0,
        bool IsBold = false, bool IsItalic = false,
        Font? FontInfoObj = null, double TextRise = 0.0,
        double HScaling = 1.0,
        // Parallel to CharCumWidths: the position just AFTER each character's
        // own glyph advance, BEFORE any subsequent TJ kerning. Used by the
        // fragment-width computation so compensation kernings emitted between
        // the match and post-match text don't inflate rectangle widths.
        double[]? CharEndPositions = null,
        // Fill colour in effect when this run was drawn (PDF default black).
        // Captured onto the fragment's TextState.ForegroundColor during assembly.
        Color? FillColor = null,
        // Stroking colour in effect when this run was drawn (null = default).
        // Captured onto the fragment's TextState.StrokingColor during assembly.
        Color? StrokingColor = null,
        // Active clip bbox (device space) when this run was shown; null = unclipped.
        // A run whose rect misses this box entirely was clipped away — hidden text.
        (double Llx, double Lly, double Urx, double Ury)? ClipRect = null,
        // True for the continuation pieces of a run the absorber split at a large
        // inter-glyph gap (Tc/kern column layout). The search text places exactly
        // one boundary space between split siblings, independent of the run-gap
        // heuristics.
        bool GapSplit = false,
        // Character/word spacing (Tc/Tw) in effect for this run — CharEndPositions
        // fold them into each char's advance, so the gap detector adds them back
        // to recover the INK gap between neighbouring glyphs.
        double CharSpacing = 0.0, double WordSpacing = 0.0,
        double TmBaseY = 0.0,
        // The Form XObject stream this run was extracted from (innermost, when the
        // page's content reached it through Do). Post-extraction edits that must
        // land in the producing stream — e.g. the BackgroundColor highlight —
        // target this stream instead of the page content.
        Core.PdfStream? SourceXObj = null);

    /// <summary>
    /// Detect superscript/subscript fragments heuristically by comparing each fragment
    /// with its neighbors. A fragment is super/subscript if its font size is significantly
    /// smaller than neighbors on the same visual line and its Y position is shifted.
    /// </summary>
    private static void DetectSuperSubscript(TextFragmentCollection fragments)
    {
        if (fragments.Count < 2) return;

        for (int i = 1; i <= fragments.Count; i++)
        {
            var frag = fragments[i];
            // Skip if already detected via Ts operator
            if (frag.TextState.TextRise != 0) continue;

            if (!frag.HasExplicitPosition) continue;
            var fs = frag.TextState.FontSize;
            var y = frag.Position!.YIndent;

            // Find the dominant (normal-sized) font size and Y from neighbors
            // that are horizontally adjacent (within the same visual line).
            double neighborFs = 0;
            double neighborY = double.NaN;

            // Check previous neighbor
            if (i > 1)
            {
                var prev = fragments[i - 1];
                if (prev.HasExplicitPosition && IsHorizontalNeighbor(frag, prev))
                {
                    neighborFs = prev.TextState.FontSize;
                    neighborY = prev.Position!.YIndent;
                }
            }
            // Check next neighbor if no previous or previous was also small
            if (neighborFs <= fs && i < fragments.Count)
            {
                var next = fragments[i + 1];
                if (next.HasExplicitPosition && IsHorizontalNeighbor(frag, next))
                {
                    neighborFs = next.TextState.FontSize;
                    neighborY = next.Position!.YIndent;
                }
            }

            if (neighborFs <= 0 || double.IsNaN(neighborY)) continue;

            // Super/subscript heuristic constraints:
            // 1. Fragment text must be short (≤5 chars) — real super/sub are brief
            // 2. Font must be significantly smaller (at most ~70% of neighbor size)
            if (frag.Text.Length > 5) continue;
            if (fs >= neighborFs * 0.7) continue;

            var yDiff = y - neighborY;
            var absYDiff = Math.Abs(yDiff);
            // Superscript: smaller font + Y is significantly higher (≥30% of neighbor font).
            // Subscript: smaller font + Y is at/near the same baseline (within 5% of neighbor font)
            // or below. In-between shifts (5-30%) are ambiguous — not marked.
            if (yDiff > neighborFs * 0.3)
            {
                frag.TextState.IsSuperscript = true;
            }
            else if (absYDiff < neighborFs * 0.05 || yDiff < -neighborFs * 0.05)
            {
                // Same baseline or below — subscript
                frag.TextState.IsSubscript = true;
            }
        }
    }

    /// <summary>Check if two fragments are on the same visual line (close Y, close X).
    /// Callers must ensure both Position values are non-null.</summary>
    private static bool IsHorizontalNeighbor(TextFragment a, TextFragment b)
    {
        // Y positions must be close (within the larger font size)
        var maxFs = Math.Max(a.TextState.FontSize, b.TextState.FontSize);
        var yDiff = Math.Abs(a.Position!.YIndent - b.Position!.YIndent);
        if (yDiff > maxFs) return false;

        // X positions should be reasonably close (fragments on the same line)
        var xDist = Math.Abs(a.Position.XIndent - b.Position.XIndent);
        return xDist < 500; // generous threshold for same-line proximity
    }

    /// <summary>
    /// Normalize Arabic Presentation Forms (U+FB50–U+FDFF, U+FE70–U+FEFF) to their
    /// base Unicode Arabic characters using NFKD decomposition.
    /// This allows text search to match regardless of whether the PDF uses
    /// presentation forms or standard Arabic codepoints.
    /// </summary>
    private static string NormalizeArabicPresentationForms(string text)
    {
        // Fast path: check if text contains any Arabic presentation form characters
        bool hasPresentationForms = false;
        foreach (var ch in text)
        {
            if ((ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF'))
            {
                hasPresentationForms = true;
                break;
            }
        }
        if (!hasPresentationForms) return text;

        // NFKD decomposition maps presentation forms to base characters
        return text.Normalize(System.Text.NormalizationForm.FormKD);
    }

    /// <summary>
    /// Index-preserving variant of <see cref="NormalizeArabicPresentationForms"/>:
    /// decomposes ONLY presentation-form chars (per char, NFKD) and reports, for
    /// every output char, the input index it came from (<paramref name="newToOld"/>,
    /// null when nothing changed). Decompositions change the string length, so any
    /// char-index map built on the input (charToRun / runStartChar) MUST be
    /// re-projected through this mapping before matching on the output.
    /// </summary>
    private static string NormalizeArabicPresentationFormsWithMap(string text, out int[]? newToOld)
    {
        newToOld = null;
        bool has = false;
        foreach (var ch in text)
            if ((ch >= 'ﭐ' && ch <= '﷿') || (ch >= 'ﹰ' && ch <= '﻿')) { has = true; break; }
        if (!has) return text;

        var sb = new StringBuilder(text.Length + 16);
        var map = new List<int>(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if ((c >= 'ﭐ' && c <= '﷿') || (c >= 'ﹰ' && c <= '﻿'))
            {
                var piece = c.ToString().Normalize(System.Text.NormalizationForm.FormKD);
                foreach (var pc in piece) { sb.Append(pc); map.Add(i); }
            }
            else
            {
                sb.Append(c); map.Add(i);
            }
        }
        newToOld = map.ToArray();
        return sb.ToString();
    }

    // ── API-surface additions ────────────────────────────────────

    /// <summary>Apply the supplied font to every absorbed fragment.</summary>
    public void ApplyForAllFragments(Font font)
    {
        if (font is null) return;
        foreach (var frag in _fragments)
        {
            if (frag.TextState is not null) frag.TextState.Font = font;
        }
    }

    /// <summary>Apply the supplied font + size to every absorbed fragment.</summary>
    public void ApplyForAllFragments(Font font, float fontSize)
    {
        if (font is null) return;
        foreach (var frag in _fragments)
        {
            if (frag.TextState is not null)
            {
                frag.TextState.Font = font;
                frag.TextState.FontSize = fontSize;
            }
        }
    }

    /// <summary>Apply the supplied font size to every absorbed fragment.</summary>
    public void ApplyForAllFragments(float fontSize)
    {
        foreach (var frag in _fragments)
        {
            if (frag.TextState is not null) frag.TextState.FontSize = fontSize;
        }
    }

    /// <summary>Replace every fragment's text with the empty string across every page in the document.</summary>
    public void RemoveAllText(Aspose.Pdf.Document document)
    {
        if (document is null) return;
        foreach (var page in document.Pages)
            RemoveAllText(page);
    }

    /// <summary>Replace every fragment's text with the empty string on the given page.</summary>
    public void RemoveAllText(Aspose.Pdf.Page page)
    {
        if (page is null) return;
        Visit(page);
        foreach (var frag in _fragments)
            frag.Text = string.Empty;
        _fragments.Clear();
    }

    /// <summary>Replace every fragment's text with the empty string on the given page, restricted to <paramref name="rect"/>.</summary>
    public void RemoveAllText(Aspose.Pdf.Page page, Aspose.Pdf.Rectangle rect)
    {
        if (page is null) return;
        var prevSearch = _textSearchOptions;
        try
        {
            _textSearchOptions = new TextSearchOptions(rect);
            RemoveAllText(page);
        }
        finally
        {
            _textSearchOptions = prevSearch;
        }
    }

    /// <summary>Clear absorbed fragments, errors, and per-regex results.</summary>
    public void Reset()
    {
        _fragments.Clear();
        Errors.Clear();
        RegexResults.Clear();
    }
}
