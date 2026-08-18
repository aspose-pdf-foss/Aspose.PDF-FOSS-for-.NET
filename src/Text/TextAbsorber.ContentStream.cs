using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>
    /// Processes one content stream, appending extracted text. Returns whether a font is
    /// set in the graphics state at the end of the stream, so a page's multiple content
    /// streams (which share one graphics state) can thread the "font set" flag between them.
    /// </summary>
    private bool ExtractTextFromContentStream(byte[] streamBytes, PdfDictionary pageDict, PdfReader reader,
        int depth = 0, double[]? inheritedBounds = null, double cmTx = 0, double cmTy = 0,
        bool fontSetOnEntry = false, double cmD = 1,
        double cmLinA = 1, double cmLinB = 0, double cmLinC = 0, double cmLinD = 1,
        double cmLinE = 0, double cmLinF = 0)
    {
        if (depth > 10) return fontSetOnEntry; // prevent infinite recursion
        var fonts = ResolveFonts(pageDict, reader);
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        string? currentFontName = null;
        Dictionary<int, string>? currentToUnicode = null;
        // UseFontEngineEncoding: decode via the font program's encoding/cmap instead of /ToUnicode.
        bool useFontEngine = TextSearchOptions?.UseFontEngineEncoding ?? false;
        PdfDictionary? currentFontDict = null;
        string? actualText = null;
        var actualTextUsed = false;
        // Figma-style Type3 grid reconstruction: the active span's RAW ActualText
        // (no ligature collapse, \n kept) and the per-glyph consumption offset.
        // Each show op inside the span takes the next slice, sized by ITS OWN
        // raw ToUnicode decode length (the ActualText prefix is distributed
        // per glyph), and records it as a grid run — the page then
        // rebuilds on the character grid like an OCR overlay.
        string? atSpan = null;
        var atOffset = 0;
        // Consume the next ActualText slice for a show inside a Type3 span and
        // record it as a grid run at the given device position.
        void CollectType3SpanRun(int rawLen, double xDev, double yDev, double fsDev, double wDev)
        {
            if (atSpan is null || rawLen <= 0) return;
            var take = Math.Min(rawLen, atSpan.Length - atOffset);
            if (take <= 0) return;
            var slice = atSpan.Substring(atOffset, take).Replace('\t', ' ');
            atOffset += take;
            _ocrRuns.Add((slice, xDev, yDev, fsDev, wDev));
            _type3SpanRuns++;
        }
        bool Type3SpanActive() => _collectOcrRuns && atSpan is not null
            && currentToUnicode is not null
            && currentFontDict?.GetName("Subtype") == "Type3";
        // True when the span's /ActualText decoded to exactly ONE character
        // (pre-ligature-collapse). When a one-glyph show decodes to the SAME
        // LETTER differing only in case, that's the small-caps styling idiom —
        // the recorded character reflects the STYLED glyph, not the reading —
        // and the font's own decode wins. A single-char
        // ActualText that names a DIFFERENT character (a space over a tab
        // glyph, a reading char over a symbol) is an author correction and is
        // honored, like every multi-character ActualText span.
        var actualTextSingleChar = false;
        // The single character a show operand (string or TJ array) decodes to,
        // or null when it decodes to zero or 2+ characters.
        char? DecodedSingleShowChar(PdfObject showOperand)
        {
            string d = string.Empty;
            if (showOperand is PdfString ps1)
                d = NormalizeDecoded(DecodeString(ps1.Value, currentToUnicode, currentFontDict, reader, useFontEngine));
            else if (showOperand is PdfArray pa1)
                foreach (var it in pa1)
                {
                    if (it is not PdfString ps2) continue;
                    d += NormalizeDecoded(DecodeString(ps2.Value, currentToUnicode, currentFontDict, reader, useFontEngine));
                    if (d.Length > 1) break;
                }
            return d.Length == 1 ? d[0] : null;
        }
        // True when the pending single-char ActualText should yield to the
        // show's own decode (same letter, different case only).
        bool ActualTextYieldsToDecode(PdfObject showOperand)
            => DecodedSingleShowChar(showOperand) is char dc
               && dc != actualText![0]
               && char.ToUpperInvariant(dc) == char.ToUpperInvariant(actualText[0]);
        double fontSize = 12;
        double tmD = 1.0;
        // X-scale (a component) of the most recent Tm. Td/TD advances and glyph widths
        // are tracked in unscaled text-space units, so multiplying by tmA converts an X
        // delta to page space. Usually equal to tmD (uniform scale).
        double tmA = 1.0;
        double leading = 0.0;
        double tlmX = 0;
        // Page-space X origin set by the most recent Tm (operands[4]). Td/TD advance tlmX
        // in unscaled text-space units, so the true page-space pen X is
        // tmOriginX + (tx - tmOriginX) * tmA. Tracked so the search-rectangle filter can
        // clip glyphs in page space (see AppendClippedRun).
        double tmOriginX = 0;
        double tx = 0;
        double lastRunEndX = double.NaN;
        // Device-space end of the previous run for ROTATED text: per-Tm projection
        // scales (n2) make text-space tx values incomparable between runs whose Tm
        // differs, so sideways gap math runs in device coordinates.
        double lastRunEndDevX = double.NaN;
        // True page-space end of the previous run for UPRIGHT text
        // (tmE + (tx + w − tlmX)·tmAr). tx-space deltas are only meaningful while the
        // Tm is unchanged; a document that re-sets a SCALED Tm per glyph (fontSize 1,
        // size in the matrix) makes tx values Tm-origin page coords while widths stay
        // unscaled — the unscaled subtraction then reads each glyph's own width as an
        // inter-run gap. Page-space endpoints compare correctly across Tm changes and
        // reduce to the tx-space delta exactly when the Tm scale is 1.
        double lastRunEndPageX = double.NaN;
        // Page-space START of the previous run (upright): distinguishes an
        // out-of-order COLUMN backjump (pen jumps left of the previous run's own
        // start) from an overlapping re-draw at (near-)the-same spot — the
        // duplicate-stack shape the later-ink dedup collapses. Only consulted
        // while lastRunEndX is valid, so it needs no reset bookkeeping.
        double lastRunStartPageX = double.NaN;
        // A standalone whitespace run at the visual start of a line can be a mid-line word space
        // emitted out of stream order (scrambled RTL/LTR run order — the space is streamed before
        // the run it separates). Hold such a leading space and re-home it before the first RTL run
        // on the same line, so a Hebrew/Arabic word space no longer lands as a leading space.
        double pendingReorderSpaceY = double.NaN;
        // Raw mode reproduces the stream order without reconstructing visual rows,
        // and SUB/SUPERSCRIPT hops stay inline there: a Td that
        // dips less than ~0.42 em (a TeX subscript is ~0.16 em, a summation-bound
        // or fraction move ~0.6 em+) continues the current output line instead of
        // breaking it ("L" + subscript "DF" extracts as one line "𝐿𝐷𝐹 = …").
        var rawInlineScripts = ExtractionOptions?.FormattingMode == TextExtractionOptions.TextFormattingMode.Raw;
        int lastDecodedLength = 0;
        double lastRunEstWidth = 0;
        bool lastHadMetrics = false;
        double prevTmY = double.NaN;
        FontMetrics? currentMetrics = null;
        bool currentFontNonAgl = false;
        double horizScale = 1.0;
        // Character/word spacing (Tc/Tw): used for the leading-space anchor
        // adjustment (the drawn spaces' true advance includes them).
        double charSpacing = 0, wordSpacing = 0;
        double tmY = 0;
        // Rotated-text line tracking: for a rotated Tm (d ≈ 0, b ≠ 0 — e.g.
        // "0 1 -1 0 e f", 90° text) the visual line coordinate is the origin
        // projected on the Tm up-axis (c,d) — ±e — not f. tmN is |(c,d)|, the
        // per-unit line advance used to scale Td/T*/leading displacements.
        double tmN = 1.0;
        bool tmRotated = false;
        // Raw text-matrix components + page-space line origin (e,f), maintained
        // through Tm/Td/TD/T*. For sideways text the projected tmY/tlmX no longer
        // ARE page coordinates, so the bounds/rectangle filters need these.
        double tmAr = 1, tmBr = 0, tmCr = 0, tmDr = 1, tmE = 0, tmF = 0;
        int textRenderMode = 0; // Tr; 3 = invisible (hOCR overlay signature)
        // Track the Y at which the most recent Tj/TJ/'/" actually rendered so we can
        // distinguish "new logical line" (large Y delta) from "same row, repositioned
        // by Tm for a different column" (small Y delta). Used to suppress false
        // line-breaks from ' and " after an absolute-position Tm.
        double lastRenderedY = double.NaN;
        // Font size in effect when the last text-showing operator rendered — the
        // blank-line rule measures the vertical gap from the PREVIOUS line's bottom,
        // approximated as its baseline minus ~0.2·fs of descent.
        double lastRenderedFs = 0;
        // Later-ink duplicate dedup (duplicate-stack scope):
        // a run whose ink covers ≥55% of the IMMEDIATELY-PRECEDING run's glyph box
        // and draws the IDENTICAL text replaces that run in the output — only
        // the last copy of a stacked duplicate draw is reported (a
        // headline drawn gray-then-black 0.6 pt apart extracts once, not
        // interleaved). Box = the baseline-anchored −0.2 em … +0.7 em band the
        // fragment absorber's occlusion pass uses. Upright, unclipped, Pure only.
        var dedupPrevText = string.Empty;
        var dedupPrevOffset = -1;
        double dedupPrevLlx = 0, dedupPrevLly = 0, dedupPrevUrx = -1, dedupPrevUry = -1;
        bool ReplaceOccludedPrevRun(string runText, double startPageX, double pageWidth, double baselineY)
        {
            var effFs = Math.Abs(_currentLineEffFs) > 0.001 ? Math.Abs(_currentLineEffFs) : fontSize;
            var llx = Math.Min(startPageX, startPageX + pageWidth);
            var urx = Math.Max(startPageX, startPageX + pageWidth);
            var lly = baselineY - 0.2 * effFs;
            var ury = baselineY + 0.7 * effFs;
            var replaced = false;
            if (textRenderMode != 3 && textRenderMode != 7
                && runText == dedupPrevText && dedupPrevOffset >= 0
                && dedupPrevOffset + runText.Length <= _text.Length)
            {
                var area = (dedupPrevUrx - dedupPrevLlx) * (dedupPrevUry - dedupPrevLly);
                var ix = Math.Min(urx, dedupPrevUrx) - Math.Max(llx, dedupPrevLlx);
                var iy = Math.Min(ury, dedupPrevUry) - Math.Max(lly, dedupPrevLly);
                if (area > 0.01 && ix > 0 && iy > 0 && ix * iy > area * 0.55)
                {
                    // The victim must still be the output's tail (at most trailing
                    // spaces after it) — a line break or other run in between
                    // means the copies aren't an adjacent duplicate stack.
                    var tailIsVictim = true;
                    for (var t = 0; t < runText.Length && tailIsVictim; t++)
                        if (_text[dedupPrevOffset + t] != runText[t]) tailIsVictim = false;
                    for (var t = dedupPrevOffset + runText.Length; t < _text.Length && tailIsVictim; t++)
                        if (_text[t] != ' ') tailIsVictim = false;
                    if (tailIsVictim)
                    {
                        _text.Length = dedupPrevOffset;
                        while (_pageRunSpans.Count > 0 && _pageRunSpans[^1].Offset >= dedupPrevOffset)
                            _pageRunSpans.RemoveAt(_pageRunSpans.Count - 1);
                        replaced = true;
                    }
                }
            }
            dedupPrevText = runText;
            dedupPrevOffset = -1;               // the caller stamps the append offset
            dedupPrevLlx = llx; dedupPrevLly = lly; dedupPrevUrx = urx; dedupPrevUry = ury;
            return replaced;
        }
        bool pageBoundsActive = TextSearchOptions?.LimitToPageBounds == true;
        // Use inherited page bounds for Form XObjects (they don't have their own MediaBox)
        double[]? pageBounds = inheritedBounds ?? (pageBoundsActive ? GetPageMediaBox(pageDict, reader) : null);
        bool skipText = false;
        var searchRect = _effectiveSearchRect ?? TextSearchOptions?.Rectangle;
        // Glyph-clip rectangle: the search rectangle intersected with the page
        // bounds. LimitToPageBounds clips partially off-page runs GLYPH-wise —
        // the on-page tail of a left-overflowing word survives ("…er"), the
        // off-page overflow of a right-overflowing one is cut — using the same
        // machinery as the search rectangle.
        var clipRect = searchRect;
        // Page-bounds clipping BLANKS the off-page glyphs instead of dropping
        // them: the page keeps its full (uncropped) layout — grid columns,
        // indents, and gaps — with the clipped glyphs read as whitespace
        // ("Bestelbonnummer   /" crops to "…er   /", the columns intact). A
        // search rectangle instead RE-ANCHORS the window (glyphs removed).
        var blankClip = false;
        if (pageBounds is not null)
        {
            var pb = new Rectangle(pageBounds[0] - 1, pageBounds[1] - 1, pageBounds[2] + 1, pageBounds[3] + 1);
            if (clipRect is null) { clipRect = pb; blankClip = true; }
            else
                clipRect = new Rectangle(Math.Max(clipRect.LLX, pb.LLX), Math.Max(clipRect.LLY, pb.LLY),
                    Math.Min(clipRect.URX, pb.URX), Math.Min(clipRect.URY, pb.URY));
        }
        // CTM tracking for cm operator — accumulates with inherited CTM from parent.
        // localCmD is the composed vertical scale (d): a page whose content is drawn
        // under a flipped CTM ("1 0 0 -1 0 H cm", text-space Y growing downward) needs
        // it to recover the device Y for line ordering.
        double localCmTx = cmTx, localCmTy = cmTy, localCmD = cmD;
        var cmStack = new Stack<(double tx, double ty, double d)>();
        // Full CTM (linear part + true composed translation), tracked in parallel
        // with the scalar approximations above. A page that rotates its content via
        // `cm` (deskewed scans, landscape forms) has an IDENTITY Tm — the rotation
        // only shows in the composed Tm×CTM, so direction detection and page-space
        // positions for sideways text must come from here.
        double cmLa = cmLinA, cmLb = cmLinB, cmLc = cmLinC, cmLd = cmLinD, cmLe = cmLinE, cmLf = cmLinF;
        var cmFullStack = new Stack<(double a, double b, double c, double d, double e, double f)>();
        // Strict font-usage check: track whether a font is set in the current graphics
        // state (Tf sets it; q/Q save/restore it as spec text state). A text-showing
        // operator with no font set means the content stream is malformed (no preceding
        // Tf) — throw IncorrectFontUsageException unless IgnoreResourceFontErrors is set.
        bool fontSet = fontSetOnEntry;
        var fontSetStack = new Stack<bool>();

        // Line-level position filter (page bounds + search rectangle), evaluated at
        // every line reposition (Tm/Td/T*). Upright text filters on the baseline Y
        // (X is clipped per glyph); sideways text swaps the roles — the baseline's
        // page X is the line coordinate and the advance axis is clipped per glyph.
        bool LineFiltered(double upY)
        {
            // tmE/tmF are TRUE page coordinates (composed Tm×CTM) — no cm re-add.
            // The upright branch composes the CTM's linear scale too: content nested
            // in a scaled Form XObject (resized page content invoked via
            // "0.6 0 0 0.6 tx ty cm /Fm Do") reports text-space line coordinates,
            // and translation alone would test the wrong band of the page.
            var cmSy = System.Math.Abs(localCmD) > 1e-9 ? localCmD : 1.0;
            var cmSx = System.Math.Abs(cmLa) > 1e-9 ? cmLa : 1.0;
            var py = tmRotated ? tmF : upY * cmSy + localCmTy;
            var px = tmRotated ? tmE : tlmX * cmSx + localCmTx;
            var skip = false;
            // Page bounds filter the BASELINE axis only at line level — the
            // advance axis is clipped per glyph, so a line entering from
            // off-page keeps its on-page portion.
            if (pageBounds is not null)
                skip = tmRotated
                    ? px < pageBounds[0] - 1 || px > pageBounds[2] + 1
                    : py < pageBounds[1] - 1 || py > pageBounds[3] + 1;
            if (!skip && searchRect is not null)
                skip = tmRotated
                    ? px < searchRect.LLX || px > searchRect.URX
                    : py < searchRect.LLY || py > searchRect.URY;
            if (GridDebug && searchRect is not null)
                Console.Error.WriteLine($"[linefilt] upY={upY:F2} py={py:F2} px={px:F2} tmF={tmF:F2} tmE={tmE:F2} cmTy={localCmTy:F2} cmD={localCmD:F2} rot={tmRotated} skip={skip}");
            return skip;
        }

        // Sideways-text glyph clip: keep glyphs whose advance span (which runs along
        // the page Y axis for rotated text) lies inside the rectangle's Y band. The
        // X band was already enforced at line level by LineFiltered.
        void AppendClippedRunRot(StringBuilder sb, byte[] bytes, ref double penText)
        {
            const double eps = 0.05;
            var isCid = currentMetrics?.IsCid ?? false;
            var step = isCid ? 2 : 1;
            for (var i = 0; i + step - 1 < bytes.Length; i += step)
            {
                var code = isCid ? ((bytes[i] << 8) | bytes[i + 1]) : bytes[i];
                var seg = isCid ? new[] { bytes[i], bytes[i + 1] } : new[] { bytes[i] };
                var glyph = NormalizeDecoded(DecodeString(seg, currentToUnicode, currentFontDict, reader, useFontEngine), foldNbsp: false);
                var w = ((currentMetrics is not null
                    ? currentMetrics.GetWidth(code) * fontSize / 1000.0
                    : fontSize * 0.5 * Math.Max(1, glyph.Length))
                    + charSpacing + (!isCid && code == 32 ? wordSpacing : 0)) * horizScale;
                // Distance from the CURRENT line origin (tlmX), whose page position is
                // (tmE, tmF) — Td displacements are already baked into tmF, so measuring
                // from the Tm-time tmOriginX would double-count them.
                var d0 = penText - tlmX;
                var y0 = tmF + d0 * tmBr;
                var y1 = tmF + (d0 + w) * tmBr;
                var lo = Math.Min(y0, y1);
                var hi = Math.Max(y0, y1);
                if (lo >= clipRect!.LLY - eps && hi <= clipRect.URY + eps)
                    sb.Append(glyph);
                penText += w;
            }
        }

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
                case TokenKind.Boolean:
                    operands.Add(token.BoolValue ? PdfBoolean.True : PdfBoolean.False);
                    break;
                case TokenKind.ArrayStart:
                {
                    var array = ParseContentArray(lexer);
                    operands.Add(array);
                    break;
                }
                case TokenKind.DictStart:
                {
                    var dict = ParseContentDict(lexer);
                    operands.Add(dict);
                    break;
                }
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "BI": // Begin inline image — skip until EI
                            SkipInlineImage(lexer);
                            operands.Clear();
                            continue;
                        case "BDC" when operands.Count >= 2:
                        {
                            // Check for ActualText in marked content properties
                            if (operands[1] is PdfDictionary props)
                            {
                                var at = props.Get("ActualText");
                                if (at is PdfString ats)
                                {
                                    var atDecoded = DecodeTextString(ats.Value);
                                    actualText = CollapseTwoCharLigature(atDecoded);
                                    actualTextUsed = false;
                                    actualTextSingleChar = atDecoded.Length == 1;
                                    atSpan = atDecoded;
                                    atOffset = 0;
                                }
                            }
                            break;
                        }
                        case "BMC":
                            break;
                        case "EMC":
                        {
                            // Emit ActualText if it wasn't already emitted by text operators
                            if (actualText is not null && !actualTextUsed)
                                AppendShowText(actualText);
                            actualText = null;
                            actualTextUsed = false;
                            atSpan = null;
                            atOffset = 0;
                            break;
                        }
                        case "cm" when operands.Count >= 6:
                            localCmTx += GetNumber(operands[4]);
                            // Compose the Y transform (axis-aligned): with the CTM in effect
                            // y_dev = D·y + T, appending "a b c d e f cm" gives
                            // y_dev = (D·d)·y + (D·f + T).
                            localCmTy += localCmD * GetNumber(operands[5]);
                            localCmD *= GetNumber(operands[3]);
                            {
                                // Full composition CTM' = M_new × CTM (row-vector convention).
                                var na = GetNumber(operands[0]); var nb = GetNumber(operands[1]);
                                var nc = GetNumber(operands[2]); var nd = GetNumber(operands[3]);
                                var ne = GetNumber(operands[4]); var nf = GetNumber(operands[5]);
                                var a2 = na * cmLa + nb * cmLc; var b2 = na * cmLb + nb * cmLd;
                                var c2 = nc * cmLa + nd * cmLc; var d2 = nc * cmLb + nd * cmLd;
                                var e2 = ne * cmLa + nf * cmLc + cmLe; var f2 = ne * cmLb + nf * cmLd + cmLf;
                                cmLa = a2; cmLb = b2; cmLc = c2; cmLd = d2; cmLe = e2; cmLf = f2;
                            }
                            break;
                        case "q":
                            cmStack.Push((localCmTx, localCmTy, localCmD));
                            cmFullStack.Push((cmLa, cmLb, cmLc, cmLd, cmLe, cmLf));
                            fontSetStack.Push(fontSet);
                            break;
                        case "Q":
                            if (cmStack.Count > 0) (localCmTx, localCmTy, localCmD) = cmStack.Pop();
                            if (cmFullStack.Count > 0) (cmLa, cmLb, cmLc, cmLd, cmLe, cmLf) = cmFullStack.Pop();
                            if (fontSetStack.Count > 0) fontSet = fontSetStack.Pop();
                            break;
                        case "Do" when operands.Count >= 1 && operands[0] is PdfName doName:
                        {
                            var xobjs = ResolveXObjects(pageDict, reader);
                            if (xobjs is not null)
                            {
                                var xstr = reader.ResolveStream(xobjs.Get(doName.Value));
                                if (xstr is not null && reader.ResolveName(xstr.Dict, "Subtype") == "Form")
                                {
                                    var xbytes = reader.DecodeStream(xstr);
                                    // A form XObject inherits the graphics state (incl. font) at the Do.
                                    ExtractTextFromContentStream(xbytes, xstr.Dict, reader, depth + 1,
                                        pageBounds, localCmTx, localCmTy, fontSet, localCmD,
                                        cmLa, cmLb, cmLc, cmLd, cmLe, cmLf);
                                }
                            }
                            break;
                        }
                        case "Tr" when operands.Count >= 1:
                            textRenderMode = (int)GetNumber(operands[0]);
                            break;
                        case "Tf" when operands.Count >= 2:
                            fontSize = GetNumber(operands[1]);
                            fontSet = true;
                            if (operands[0] is PdfName tfFontName)
                            {
                                currentFontName = tfFontName.Value;
                                if (fonts.TryGetValue(currentFontName, out var tfFontDict))
                                {
                                    currentFontDict = tfFontDict;
                                    currentToUnicode = useFontEngine ? null : ParseToUnicode(tfFontDict, reader);
                                    currentMetrics = FontMetrics.FromFontDict(tfFontDict, reader);
                                    currentFontNonAgl = (TextSearchOptions?.LogTextExtractionErrors ?? false)
                                        && DifferencesNotAglCompliant(tfFontDict, reader);
                                }
                                else
                                {
                                    currentFontDict = null;
                                    currentToUnicode = null;
                                    currentMetrics = null;
                                    currentFontNonAgl = false;
                                }
                            }
                            break;
                        case "Tm":
                            // Track scale components to interpret Td/TD displacements correctly.
                            // Many PDFs use a tiny-scale Tm (e.g. d=0.015) and large Td values;
                            // the actual page displacement is d * ty (or a * tx), not ty (tx) alone.
                            if (operands.Count >= 6)
                            {
                                var newTmY = GetNumber(operands[5]);
                                tmD = Math.Abs(GetNumber(operands[3]));
                                tmA = Math.Abs(GetNumber(operands[0]));
                                var tmBraw = GetNumber(operands[1]);
                                var tmCraw = GetNumber(operands[2]);
                                var tmDraw = GetNumber(operands[3]);
                                var tmEraw = GetNumber(operands[4]);
                                var tmFraw = GetNumber(operands[5]);
                                // Effective text direction = Tm × CTM. A page that rotates
                                // content via `cm` (deskewed scan, landscape form) keeps an
                                // identity Tm; only the composed matrix shows it sideways.
                                var cEa = GetNumber(operands[0]) * cmLa + tmBraw * cmLc;
                                var cEb = GetNumber(operands[0]) * cmLb + tmBraw * cmLd;
                                var cEc = tmCraw * cmLa + tmDraw * cmLc;
                                var cEd = tmCraw * cmLb + tmDraw * cmLd;
                                var cEe = tmEraw * cmLa + tmFraw * cmLc + cmLe;
                                var cEf = tmEraw * cmLb + tmFraw * cmLd + cmLf;
                                tmAr = cEa; tmBr = cEb; tmCr = cEc; tmDr = cEd;
                                tmE = cEe; tmF = cEf;
                                tmN = Math.Sqrt(tmCraw * tmCraw + tmDraw * tmDraw);
                                if (tmN < 0.001) tmN = 1.0;
                                // Rotation test on the composed direction, tolerant of the
                                // slight skew a deskewed scan carries (|d| ≪ |b|).
                                tmRotated = Math.Abs(cEb) > 0.001 && Math.Abs(cEd) < 0.1 * Math.Abs(cEb);
                                if (tmRotated)
                                {
                                    // Line coordinate along the up-axis: successive visual
                                    // lines of sideways text differ along the composed (c,d)
                                    // direction. The sign (via c) keeps "later line" = smaller
                                    // coordinate, so the Y-descending sort yields reading order.
                                    tmN = Math.Sqrt(cEc * cEc + cEd * cEd);
                                    if (tmN < 0.001) tmN = 1.0;
                                    newTmY = (cEe * cEc + cEf * cEd) / tmN;
                                }

                                // Emit newline when Tm repositions to a different Y line.
                                // Compare against lastRenderedY (where the previous Tj/'/"
                                // actually PUT ink) rather than just prevTmY — the tracking Y
                                // can differ from the rendered Y by a full 'leading' when the
                                // previous BT/ET block used the '/(") operator to step down.
                                // Only do this for upright text (tmD > 0). Rotated text (tmD ≈ 0,
                                // e.g. 90° rotation [0 fs -fs 0 e f]) has meaningless f-value
                                // differences that would generate false line breaks.
                                var tmYThreshold = Math.Max(1.0, fontSize * 0.3 * (tmRotated ? tmN : 1.0));
                                // refY = where the last text landed. lastRenderedY / prevTmY are
                                // per-content-stream locals, so they reset to NaN when text
                                // continues inside a Form XObject drawn via Do (a common way to
                                // place a diagram/overlay). Fall back to the instance-level
                                // _currentLineY (which survives the recursion) so the XObject's
                                // first positioned run still line-breaks against the outer text
                                // instead of gluing onto it (e.g. floor-plan letters merging into
                                // the paragraph line above them).
                                var refY = !double.IsNaN(lastRenderedY) ? lastRenderedY
                                         : !double.IsNaN(prevTmY) ? prevTmY
                                         : _currentLineY;
                                // After a ' or " operator, the actual rendered Y is tmY - leading,
                                // but a subsequent Tm's newTmY is compared with the refY directly.
                                // For same-row column layouts the Tm targets Y ≈ previous Tm's Y
                                // (before its '), so the above refY==lastRenderedY path would
                                // fire a newline incorrectly. Fall back to prevTmY when the
                                // difference to lastRenderedY is exactly ~leading.
                                if (!double.IsNaN(prevTmY) && !double.IsNaN(lastRenderedY)
                                    && Math.Abs(Math.Abs(newTmY - lastRenderedY) - leading) < tmYThreshold)
                                {
                                    refY = prevTmY;
                                }
                                bool tmSameRow = (tmD > 0 || tmRotated) && !double.IsNaN(refY)
                                                 && Math.Abs(newTmY - refY) <= tmYThreshold;
                                if (GridDebug)
                                    Console.Error.WriteLine($"[tm] newY={newTmY:F1} refY={refY:F1} same={tmSameRow} rot={tmRotated} lastRendY={lastRenderedY:F1} prevTmY={prevTmY:F1}");
                                if ((tmD > 0 || tmRotated) && !double.IsNaN(refY) && !tmSameRow &&
                                    _text.Length > 0 && _text[^1] != '\n')
                                {
                                    RecordLineY();
                                    AppendStreamBreak();
                                }
                                // Track absolute page-space Y for line sorting: keep
                                // _currentLineY in text space, but snapshot the CTM Y offset
                                // in effect now so RecordLineY can emit page-space Y. Only inside
                                // a Form XObject (depth > 0) — page-content Y tracking is left
                                // byte-identical to avoid disturbing the common extraction path.
                                _currentLineY = newTmY;
                                _currentLineCmTy = tmRotated ? 0 : LineCmAdjust(depth, localCmD, localCmTy, _currentLineY);
                                prevTmY = newTmY;
                                tmY = newTmY;
                                if (tmRotated)
                                {
                                    // Advance axis for sideways text is the origin projected
                                    // on the composed direction vector (a,b) — so the
                                    // word-gap / column-grid logic sees real line offsets.
                                    var n2 = Math.Sqrt(cEa * cEa + cEb * cEb);
                                    if (n2 < 0.001) n2 = 1.0;
                                    tlmX = (cEe * cEa + cEf * cEb) / n2;
                                    // Reading-axis scale: |a| is ~0 for sideways text, which
                                    // would freeze runPageX at the block origin (every run in
                                    // the BT block reporting the same grid X). The advance
                                    // axis norm |(a,b)| is the true per-unit X scale.
                                    tmA = n2;
                                }
                                else
                                {
                                    tlmX = GetNumber(operands[4]);
                                }
                                tmOriginX = tlmX;
                                tx = tlmX;
                                // Line-level bounds + rectangle filter (rotation-aware).
                                skipText = LineFiltered(newTmY);
                                // Reset gap-detection only when the Tm actually moved to a new
                                // logical row. For same-row Tm (column reposition) keep
                                // lastRunEndX so the ' / " / Tj that follows can insert
                                // proportional spaces reflecting the visible column gap.
                                if (!tmSameRow) { lastRunEndX = double.NaN; lastRunEndPageX = double.NaN; } lastRunEndDevX = double.NaN;
                            }
                            break;
                        case "BT":
                            // PDF spec ISO 32000-1 §9.4.1: BT initializes only the text matrix
                            // and text line matrix to identity. All other text state (leading,
                            // char/word spacing, horizontal scaling, rendering mode, font size)
                            // persists across BT/ET per §9.3.  Earlier we zeroed leading here
                            // and wiped lastRunEndX, which caused the downstream
                            // Tm-vs-lastRenderedY heuristic to miss same-row column
                            // repositioning whenever a fresh BT block preceded the Tm (typical
                            // for column-per-BT PDF layouts). Keep lastRunEndX alive — the next
                            // Tm will decide whether to clear it based on row change.
                            tlmX = 0;
                            tmOriginX = 0;
                            tx = 0;
                            tmY = 0;
                            tmD = 1.0;
                            tmA = 1.0;
                            tmN = 1.0;
                            // BT sets Tm to identity, so the effective direction IS the CTM.
                            tmAr = cmLa; tmBr = cmLb; tmCr = cmLc; tmDr = cmLd; tmE = cmLe; tmF = cmLf;
                            tmRotated = Math.Abs(cmLb) > 0.001 && Math.Abs(cmLd) < 0.1 * Math.Abs(cmLb);
                            if (tmRotated)
                            {
                                tmN = Math.Sqrt(cmLc * cmLc + cmLd * cmLd);
                                if (tmN < 0.001) tmN = 1.0;
                                var n2bt = Math.Sqrt(cmLa * cmLa + cmLb * cmLb);
                                if (n2bt < 0.001) n2bt = 1.0;
                                tmA = n2bt;
                                tmY = (cmLe * cmLc + cmLf * cmLd) / tmN;
                                tlmX = (cmLe * cmLa + cmLf * cmLb) / n2bt;
                                tmOriginX = tlmX;
                                tx = tlmX;
                            }
                            lastRunEstWidth = 0;
                            horizScale = 1.0; // Tz resets to 100% at start of text object
                            break;
                        case "TL":
                            if (operands.Count >= 1)
                                leading = GetNumber(operands[0]);
                            break;
                        case "Tz":
                            if (operands.Count >= 1)
                                horizScale = GetNumber(operands[0]) / 100.0;
                            break;
                        case "Tc":
                            if (operands.Count >= 1)
                                charSpacing = GetNumber(operands[0]);
                            break;
                        case "Tw":
                            if (operands.Count >= 1)
                                wordSpacing = GetNumber(operands[0]);
                            break;
                        case "Td" or "TD":
                        {
                            if (operands.Count >= 2)
                            {
                                var rawTy = GetNumber(operands[1]);
                                if (op == "TD") leading = -rawTy; // TD sets TL = -ty
                                var rawTx = GetNumber(operands[0]);
                                // PDF spec: Td updates the text LINE matrix, then sets Tm = Tlm.
                                // After Td, the text cursor resets to the new line origin.
                                // Keep rawTx unscaled: both Td advances and MeasureString widths
                                // use the same coordinate system (text space via fontSize from Tf).
                                tlmX += rawTx;
                                tmE += rawTx * tmAr + rawTy * tmCr;
                                tmF += rawTx * tmBr + rawTy * tmDr;

                                tx = tlmX;
                                // Compute actual page-space y-displacement: ty * tmD
                                // (tmD is the y-scale component from the most recent Tm)
                                var pageDisp = Math.Abs(rawTy * (tmRotated ? tmN : tmD > 0 ? tmD : tmN));
                                tmY += rawTy * (tmRotated ? tmN : tmD > 0 ? tmD : tmN);
                                // Raw mode: sub/superscript hops stay inline. DOWNWARD moves
                                // break past ~0.42 em (a subscript dip is ~0.16 em; a fraction
                                // denominator / summation lower bound ~0.6 em+); UPWARD moves
                                // break only past ~1.5 em (superscripts, returns from a
                                // subscript, and raised summation bounds all continue the line).
                                var fsScaleTd = tmRotated ? tmN : tmD > 0 ? tmD : tmN;
                                var sDispTd = rawTy * fsScaleTd;
                                var tdBreakTol = !rawInlineScripts ? 0.5
                                    : sDispTd < 0 ? Math.Max(0.5, 0.42 * fontSize * Math.Abs(fsScaleTd))
                                    : Math.Max(0.5, 1.5 * fontSize * Math.Abs(fsScaleTd));
                                if (pageDisp > tdBreakTol)
                                {
                                    RecordLineY();
                                    AppendStreamBreak();
                                    // Mirror the absolute text-space baseline (tmY, already advanced
                                    // above). Assigning rather than incrementing keeps _currentLineY
                                    // correct across a BT that reset tmY to 0 — e.g. cell-per-BT pages
                                    // ("BT x y Td (cell) Tj ET" repeated), where incrementing by the
                                    // absolute Td turned line Ys into a runaway cumulative sum.
                                    _currentLineY = tmY;
                                    _currentLineCmTy = tmRotated ? 0 : LineCmAdjust(depth, localCmD, localCmTy, _currentLineY);
                                    lastRunEndX = double.NaN; lastRunEndDevX = double.NaN; lastRunEndPageX = double.NaN;
                                }
                                // Line-level bounds + rectangle filter (rotation-aware).
                                skipText = LineFiltered(tmY);
                            }
                            break;
                        }
                        case "T*":
                        {
                            // Equivalent to 0 -TL Td: move the text line matrix down by the
                            // current leading and reset the cursor to the line origin. Mirror
                            // the Td handler so tmY, the line-break detection and the
                            // page-bounds / search-rectangle filters all advance with the new
                            // baseline. (The earlier version left tmY stale, so a Tj after a
                            // run of T* operators was positioned and filtered against the Y of
                            // a line several rows above, dropping in-rectangle text.)
                            tx = tlmX;
                            var disp = leading * (tmRotated ? tmN : tmD > 0 ? tmD : tmN);
                            var pageDisp = Math.Abs(disp);
                            tmY -= disp;
                            tmE += -leading * tmCr;
                            tmF += -leading * tmDr;
                            // See the Td note: Raw mode keeps sub/superscript-scale hops
                            // inline (T* moves DOWN by the leading, so the downward tol applies).
                            var tstarBreakTol = rawInlineScripts
                                ? Math.Max(0.5, 0.42 * fontSize * Math.Abs(tmRotated ? tmN : tmD > 0 ? tmD : tmN))
                                : 0.5;
                            if (pageDisp > tstarBreakTol)
                            {
                                RecordLineY();
                                AppendStreamBreak();
                                // See the Td note: mirror the absolute baseline (survives a BT
                                // that zeroed tmY).
                                _currentLineY = tmY;
                                _currentLineCmTy = tmRotated ? 0 : LineCmAdjust(depth, localCmD, localCmTy, _currentLineY);
                                lastRunEndX = double.NaN; lastRunEndDevX = double.NaN; lastRunEndPageX = double.NaN;
                            }
                            // Re-evaluate the line-level filters at the new baseline.
                            skipText = LineFiltered(tmY);
                            break;
                        }
                        case "Tj":
                        {
                            _textShowingOpCount++;
                            EnsureFontSet(fontSet, op);
                            if (skipText) break;
                            _pageHasRotatedText |= tmRotated;
                            _currentLineEffFs = tmRotated
                                ? Math.Abs(fontSize * tmN)  // composed projection norm already carries the CTM; the scalar d is ~0 sideways
                                : Math.Abs(fontSize * (tmD > 0 ? tmD : tmN) * localCmD);
                            _currentLineDescent = currentMetrics is not null && currentMetrics.Descent < 0
                                ? -currentMetrics.Descent / 1000.0
                                : 0.2;
                            _currentLineIsRotated = tmRotated && !_pageRotDominant
                                && (_text.Length == 0 || _text[^1] == '\n' || _currentLineIsRotated);
                            if (_currentLineIsRotated && double.IsNaN(_currentLineDevY))
                            {
                                _currentLineDevY = tmF + (tx - tlmX) * tmBr / (Math.Abs(tmA) < 0.001 ? 1.0 : tmA);
                                if (GridDebug)
                                    Console.Error.WriteLine($"[roty] devY={_currentLineDevY:F1} tmF={tmF:F1} tx={tx:F1} tlmX={tlmX:F1} tmBr={tmBr:F2} tmA={tmA:F2} tmE={tmE:F1} op={op}");
                            }
                            // A page positioned by Td alone (no Tm) never seeds the line Y —
                            // without it RecordLineY skips every line and the Y-sort/merge
                            // pass gets nothing to work with. Seed from the tracked tmY.
                            if (double.IsNaN(_currentLineY))
                            {
                                _currentLineY = tmY;
                                _currentLineCmTy = tmRotated ? 0 : LineCmAdjust(depth, localCmD, localCmTy, _currentLineY);
                            }
                            if (operands.Count >= 1 && operands[0] is PdfString tjStr)
                            {
                                // Styled single glyph: one-char /ActualText over a one-glyph
                                // show falls back to the font's own decode (see the flag note).
                                if (actualText is not null && !actualTextUsed && actualTextSingleChar
                                    && ActualTextYieldsToDecode(tjStr))
                                    actualText = null;
                                if (actualText is not null)
                                {
                                    if (!actualTextUsed)
                                    {
                                        AppendShowText(actualText);
                                        actualTextUsed = true;
                                        // The replaced glyphs' advance differs from the ActualText's,
                                        // so the gap chain restarts at the next regular run.
                                        lastRunEndX = double.NaN; lastRunEndDevX = double.NaN; lastRunEndPageX = double.NaN;
                                    }
                                    // The span's glyphs still advance the pen even though their
                                    // decode is replaced — with a stale tx the NEXT run's Td
                                    // reads as a huge phantom word gap ("Is," grew a space).
                                    var atAdvW = (currentMetrics?.MeasureString(tjStr.Value, fontSize)
                                           ?? fontSize * 0.5 * tjStr.Value.Length) * horizScale;
                                    if (Type3SpanActive())
                                    {
                                        var t3Adv = Type3Advance(tjStr.Value, currentFontDict!, reader, fontSize);
                                        if (t3Adv >= 0) atAdvW = t3Adv * horizScale;
                                        CollectType3SpanRun(
                                            DecodeString(tjStr.Value, currentToUnicode, currentFontDict, reader, useFontEngine).Length,
                                            tmOriginX + (tx - tmOriginX) * tmA + localCmTx, tmY + localCmTy,
                                            fontSize * Math.Abs(tmA), atAdvW * Math.Abs(tmA));
                                    }
                                    tx += atAdvW;
                                }
                                else
                                {
                                    var fullDecoded = ApplyRtlIfPureRtl(NormalizeDecoded(DecodeString(tjStr.Value, currentToUnicode, currentFontDict, reader, useFontEngine), foldNbsp: searchRect is null));
                                    if (currentFontNonAgl)
                                        RecordAglError(currentFontName, fullDecoded,
                                            tmE + (tx - tlmX) * tmAr, tmY + localCmTy);
                                    // When a search rectangle is active, clip the run to the
                                    // glyphs whose advance box falls inside it (page space).
                                    // Sideways text clips along its advance axis (page Y).
                                    var clipRot = clipRect is not null && tmRotated && currentMetrics is not null;
                                    var clipping = clipRot || (clipRect is not null && tmD > 0 && currentMetrics is not null);
                                    var decoded = fullDecoded;
                                    // A left-clipped run starts, for layout purposes, at its first
                                    // surviving glyph — the off-page prefix neither indents the line
                                    // nor widens the gap to the previous run.
                                    var txClip = tx;
                                    if (clipping)
                                    {
                                        var clip = new StringBuilder();
                                        var pen = tx;
                                        if (clipRot)
                                            AppendClippedRunRot(clip, tjStr.Value, ref pen);
                                        else
                                        {
                                            AppendClippedRun(clip, tjStr.Value, currentToUnicode, currentFontDict,
                                                reader, useFontEngine, currentMetrics, fontSize, horizScale,
                                                clipRect!, tmOriginX, tmA, localCmTx, cmLa, ref pen, charSpacing, wordSpacing,
                                                out var keptStart, blankClip);
                                            if (!double.IsNaN(keptStart)) txClip = keptStart;
                                        }
                                        decoded = clip.ToString();
                                    }
                                    var measuredWidth = currentMetrics?.MeasureString(tjStr.Value, fontSize);
                                    var width = (measuredWidth ?? (fontSize * 0.5 * fullDecoded.Length)) * horizScale;
                                    if (!clipping || decoded.Length > 0)
                                    {
                                        // In Pure mode, capture the current run's page-space X and keep the
                                        // per-line grid origin up to date before computing spacing.
                                        double runPageX = 0;
                                        if (_pageCellWidth > 0)
                                        {
                                            // Upright: composed device X (identical to the raw
                                            // expression under an identity CTM/Tm, correct under
                                            // scaled ones). Rotated keeps its projection frame on a
                                            // rotated-dominant page; a minority rotated run on an
                                            // upright page grids at its DEVICE x — the horizontal
                                            // position of its vertical baseline.
                                            runPageX = tmRotated
                                                ? (_pageRotDominant
                                                    ? tmOriginX + (tx - tmOriginX) * tmA + localCmTx
                                                    : tmE)
                                                : tmE + (txClip - tlmX) * tmAr;
                                            TrackLineStart(runPageX, string.IsNullOrWhiteSpace(decoded));
                                        }
                                        TrackRowX(tmRotated
                                            ? (_pageRotDominant ? tmOriginX + (tx - tmOriginX) * tmA + localCmTx : tmE)
                                            : (tmOriginX + (txClip - tmOriginX) * tmA) * cmLa + cmLe);
                                        // Insert space for significant inter-word gap.
                                        // With proper text line matrix tracking, gap = tx - lastRunEndX
                                        // represents the actual visual gap between text runs (in user space).
                                        // A word space is typically ~fontSize * 0.25; we use a lower threshold
                                        // to catch narrow word spaces while avoiding false positives.
                                        // A trailing source space suppresses WORD-gap insertion
                                        // (no double spaces), but a genuine COLUMN jump still pads
                                        // to its grid column - the emitted chars (that space
                                        // included) already count toward the output column.
                                        var runDevX = tmRotated ? tmOriginX + (tx - tmOriginX) * tmA + localCmTx : 0;
                                        var useDev = tmRotated && !double.IsNaN(lastRunEndDevX);
                                        var usePage = !tmRotated && !double.IsNaN(lastRunEndPageX);
                                        var runStartPageX = tmE + (txClip - tlmX) * tmAr;
                                        var gapPre = double.IsNaN(lastRunEndX) ? 0
                                            : useDev ? runDevX - lastRunEndDevX
                                            : usePage ? runStartPageX - lastRunEndPageX
                                            : (txClip - lastRunEndX) * (tmRotated ? tmA : tmAr);
                                        // Duplicate-stack dedup: when this run re-draws the previous
                                        // run's text over its box, it inherits the victim's slot —
                                        // no gap spaces of its own (they were measured against the
                                        // victim's end, which the truncation just removed).
                                        var dedupReplaced = !tmRotated && searchRect is null && !rawInlineScripts
                                            && decoded.Trim().Length > 0
                                            && ReplaceOccludedPrevRun(decoded, runStartPageX, width * Math.Abs(tmAr), tmY);
                                        var synthesizedHoleSpace = false;
                                        if (!dedupReplaced
                                            && !double.IsNaN(lastRunEndX)
                                            && _text.Length > 0 && _text[^1] != '\n'
                                            && (_text[^1] != ' '
                                                || _prevShowHadTab
                                                || (_pageCellWidth > 0 && gapPre > _pageCellWidth)))
                                        {
                                            var gap = useDev ? runDevX - lastRunEndDevX
                                                : usePage ? runStartPageX - lastRunEndPageX
                                                : txClip - lastRunEndX;
                                            // See the TJ note: upright keeps the page-space Tm scale,
                                            // rotated runs use the projected line size.
                                            var gapFs = usePage ? fontSize * Math.Abs(tmAr)
                                                : _currentLineEffFs > 0 && !double.IsNaN(_currentLineEffFs)
                                                ? _currentLineEffFs
                                                : tmRotated ? Math.Abs(fontSize * tmN)
                                                : fontSize;
                                            // Use a threshold based on font size. Lower threshold for runs
                                            // with font metrics since tlmX tracking gives accurate gaps.
                                            // Cumulative font metric imprecision over long runs can narrow
                                            // the apparent gap, so use 0.09 * fontSize to catch narrow spaces
                                            // (6pt fine print squeezes a word space down to ~0.098 em).
                                            var threshold = (lastHadMetrics || currentMetrics != null)
                                                ? gapFs * 0.09
                                                : gapFs * 0.4;
                                            // A run pads to its own start column; leading drawn
                                            // space glyphs then land at their columns like any
                                            // character (nothing is discounted
                                            // for them — pad + drawn spaces total the gap).
                                            var spaces = _pageCellWidth > 0
                                                ? ColumnSpaces(gap, threshold, runPageX)
                                                : ComputeSpaceCount(gap, threshold, usePage ? gapFs : fontSize);
                                            // Sub-cell gaps keep their grid pad: the synthesized gap
                                            // space lands at ITS OWN grid column (padding
                                            // the cursor up to it) and the following word writes
                                            // contiguously after it — so target − output is the pad
                                            // even when the visual gap is narrower than one cell.
                                            var devGap = useDev || usePage || tmRotated ? gap : gap * tmAr;
                                            if (GridDebug)
                                                Console.Error.WriteLine($"[gap] gap={gap:F2} thr={threshold:F2} spaces={spaces} devGap={devGap:F2} cell={_pageCellWidth:F2} rot={tmRotated} tmA={tmA:F3} runPageX={runPageX:F1} lineStartX={_lineStartPageX:F1} fs={fontSize:F2} tx={tx:F2} lastEnd={lastRunEndX:F2} metrics={(lastHadMetrics || currentMetrics != null)} txt='{(decoded.Length > 24 ? decoded.Substring(0, 24) : decoded)}'");
                                            if (spaces > 0) _sawIntraLineGapSpaces = true;
                                            for (int si = 0; si < spaces; si++) _text.Append(' ');
                                            synthesizedHoleSpace = spaces > 0;
                                        }
                                        // Avoid double spaces: if a space was just emitted and the decoded text
                                        // starts with a space, skip the leading space — UNLESS the space was
                                        // just synthesized for THIS boundary's inter-run hole in the
                                        // layout-aware (Pure) mode (the hole and a drawn space
                                        // glyph count separately there; Raw/MemorySaving keep the
                                        // single-space collapse), and NOT on RTL lines: the document's
                                        // own space glyphs are kept there in ADDITION to the
                                        // synthesized gap space ("כתובת:    שפרעם" carries three glyphs +
                                        // one synthesized), and the RTL row rebuild needs the full count.
                                        if ((!synthesizedHoleSpace
                                                || ExtractionOptions?.FormattingMode
                                                    is TextExtractionOptions.TextFormattingMode.Raw
                                                    or TextExtractionOptions.TextFormattingMode.MemorySaving)
                                            && _text.Length > 0 && _text[^1] == ' ' && decoded.Length > 0 && decoded[0] == ' '
                                            && !RecentTextIsRtl())
                                            decoded = decoded.Substring(1);
                                        if (decoded.Length > 0)
                                        {
                                            var spanScale = horizScale * Math.Abs(tmRotated ? tmA : tmAr);
                                            _pageRunSpans.Add(new RunSpan(_text.Length, decoded.Length,
                                                tmRotated ? (_pageRotDominant ? tmOriginX + (tx - tmOriginX) * tmA + localCmTx : tmE)
                                                          : tmE + (txClip - tlmX) * tmAr,
                                                (currentMetrics?.MeasureString(tjStr.Value, fontSize)
                                                 ?? (fontSize * 0.5 * fullDecoded.Length)) * spanScale,
                                                !clipping && IsPureRtlRun(decoded),
                                                clipping ? null : BuildCharXs(tjStr.Value, currentMetrics, fontSize,
                                                    spanScale, decoded.Length)));
                                        }
                                        if (!tmRotated && searchRect is null && !rawInlineScripts
                                            && decoded.Trim().Length > 0)
                                            dedupPrevOffset = _text.Length;
                                        AppendShowText(decoded);
                                    }
                                    // Capture invisible (Tr 3) runs (with their rendered advance) for
                                    // hOCR-overlay reconstruction.
                                    if (_collectOcrRuns && textRenderMode == 3 && fullDecoded.Length > 0)
                                        _ocrRuns.Add((fullDecoded,
                                            tmOriginX + (tx - tmOriginX) * tmA + localCmTx, tmY, fontSize, width));
                                    lastRunEndDevX = tmRotated ? tmOriginX + (tx - tmOriginX) * tmA + localCmTx + width * tmA : double.NaN;
                                    lastRunEndPageX = tmRotated ? double.NaN : tmE + (tx + width - tlmX) * tmAr;
                                    lastRunStartPageX = tmRotated ? double.NaN : tmE + (tx - tlmX) * tmAr;
                                    lastRunEndX = tx + width * (tmRotated ? tmA : 1.0); // rotated: advance projects through the axis norm
                                    lastRunEstWidth = width;
                                    lastHadMetrics = measuredWidth.HasValue;
                                    lastDecodedLength = decoded.Length;
                                    tx += width;
                                    // Track rendered Y so subsequent '/"/'Tm' can distinguish
                                    // same-row column repositioning from real line advances.
                                    lastRenderedY = tmY; lastRenderedFs = fontSize * (tmRotated ? tmN : 1.0);
                                }
                            }
                            break;
                        }
                        case "TJ":
                        {
                            _textShowingOpCount++;
                            EnsureFontSet(fontSet, op);
                            if (skipText) break;
                            _pageHasRotatedText |= tmRotated;
                            _currentLineEffFs = tmRotated
                                ? Math.Abs(fontSize * tmN)  // composed projection norm already carries the CTM; the scalar d is ~0 sideways
                                : Math.Abs(fontSize * (tmD > 0 ? tmD : tmN) * localCmD);
                            _currentLineDescent = currentMetrics is not null && currentMetrics.Descent < 0
                                ? -currentMetrics.Descent / 1000.0
                                : 0.2;
                            _currentLineIsRotated = tmRotated && !_pageRotDominant
                                && (_text.Length == 0 || _text[^1] == '\n' || _currentLineIsRotated);
                            if (_currentLineIsRotated && double.IsNaN(_currentLineDevY))
                            {
                                _currentLineDevY = tmF + (tx - tlmX) * tmBr / (Math.Abs(tmA) < 0.001 ? 1.0 : tmA);
                                if (GridDebug)
                                    Console.Error.WriteLine($"[roty] devY={_currentLineDevY:F1} tmF={tmF:F1} tx={tx:F1} tlmX={tlmX:F1} tmBr={tmBr:F2} tmA={tmA:F2} tmE={tmE:F1} op={op}");
                            }
                            // See the Tj note: seed the line Y for Td-only pages.
                            if (double.IsNaN(_currentLineY))
                            {
                                _currentLineY = tmY;
                                _currentLineCmTy = tmRotated ? 0 : LineCmAdjust(depth, localCmD, localCmTy, _currentLineY);
                            }
                            if (operands.Count >= 1 && operands[0] is PdfArray tjArr)
                            {
                                // Styled single glyph: one-char /ActualText over a one-glyph
                                // show falls back to the font's own decode (see the flag note).
                                if (actualText is not null && !actualTextUsed && actualTextSingleChar
                                    && ActualTextYieldsToDecode(tjArr))
                                    actualText = null;
                                if (actualText is not null)
                                {
                                    if (!actualTextUsed)
                                    {
                                        AppendShowText(actualText);
                                        actualTextUsed = true;
                                        lastRunEndX = double.NaN; lastRunEndDevX = double.NaN; lastRunEndPageX = double.NaN;
                                    }
                                    var atStartTx = tx;
                                    var atRawLen = 0;
                                    // Advance the pen over the replaced glyphs (see the Tj note).
                                    foreach (var atItem in tjArr)
                                    {
                                        if (atItem is PdfString atS)
                                        {
                                            var atItemAdv = (currentMetrics?.MeasureString(atS.Value, fontSize)
                                                   ?? fontSize * 0.5 * atS.Value.Length) * horizScale;
                                            if (Type3SpanActive())
                                            {
                                                var t3 = Type3Advance(atS.Value, currentFontDict!, reader, fontSize);
                                                if (t3 >= 0) atItemAdv = t3 * horizScale;
                                                atRawLen += DecodeString(atS.Value, currentToUnicode, currentFontDict, reader, useFontEngine).Length;
                                            }
                                            tx += atItemAdv;
                                        }
                                        else
                                            tx += -GetNumber(atItem) * fontSize / 1000.0;
                                    }
                                    if (Type3SpanActive())
                                        CollectType3SpanRun(atRawLen,
                                            tmOriginX + (atStartTx - tmOriginX) * tmA + localCmTx, tmY + localCmTy,
                                            fontSize * Math.Abs(tmA), (tx - atStartTx) * Math.Abs(tmA));
                                }
                                else
                                {
                                    double tjWidth = 0;
                                    int tjDecodedLen = 0;
                                    // Buffer the TJ text so we can apply per-operator RTL reversal
                                    // after collecting all sub-strings (mirrors TypeScript applyRtl on TJ).
                                    var tjBuf = new StringBuilder();
                                    // When a search rectangle is active, clip each glyph to it in
                                    // page space; the pen advances over the whole array (strings and
                                    // numeric adjustments) regardless of visibility.
                                    // Sideways text clips along its advance axis (page Y).
                                    var clipRot = clipRect is not null && tmRotated && currentMetrics is not null;
                                    var clipping = clipRot || (clipRect is not null && tmD > 0 && currentMetrics is not null);
                                    var clipBuf = clipping ? new StringBuilder() : null;
                                    var clipPen = tx;
                                    var hadString = false;
                                    // Track this run for the Pure-mode grid (line-start X for
                                    // leading columns) — the TJ path must mirror the Tj path or
                                    // TJ-drawn documents get no grid anchoring at all.
                                    double tjRunPageX = 0;
                                    if (_pageCellWidth > 0)
                                    {
                                        // See the Tj-path note: device X for upright text;
                                        // minority rotated runs grid at their device X too.
                                        tjRunPageX = tmRotated
                                            ? (_pageRotDominant
                                                ? tmOriginX + (tx - tmOriginX) * tmA + localCmTx
                                                : tmE)
                                            : tmE + (tx - tlmX) * tmAr;
                                        // Whitespace-only detection from raw bytes (simple fonts:
                                        // every code 0x20; composite codes stay "visible").
                                        var tjAllSpaces = false;
                                        if (currentMetrics is not null && !currentMetrics.IsCid)
                                        {
                                            tjAllSpaces = true;
                                            foreach (var pre0 in tjArr)
                                            {
                                                if (pre0 is not PdfString ps0) continue;
                                                foreach (var b0 in ps0.Value)
                                                    if (b0 != 0x20) { tjAllSpaces = false; break; }
                                                if (!tjAllSpaces) break;
                                            }
                                        }
                                        TrackLineStart(tjRunPageX, tjAllSpaces);
                                    }
                                    TrackRowX(tmRotated
                                        ? (_pageRotDominant ? tmOriginX + (tx - tmOriginX) * tmA + localCmTx : tmE)
                                        : (tmOriginX + (tx - tmOriginX) * tmA) * cmLa + cmLe);
                                    // The inter-word space before the run depends only on pre-run state.
                                    var leadingSpaces = 0;
                                    var tjRunDevX = tmRotated ? tmOriginX + (tx - tmOriginX) * tmA + localCmTx : 0;
                                    var tjUseDev = tmRotated && !double.IsNaN(lastRunEndDevX);
                                    var tjUsePage = !tmRotated && !double.IsNaN(lastRunEndPageX);
                                    var tjStartPageX = tmE + (tx - tlmX) * tmAr;
                                    var tjGapPre = double.IsNaN(lastRunEndX) ? 0
                                        : tjUseDev ? tjRunDevX - lastRunEndDevX
                                        : tjUsePage ? tjStartPageX - lastRunEndPageX
                                        : (tx - lastRunEndX) * (tmRotated ? tmA : tmAr);
                                    if (!double.IsNaN(lastRunEndX)
                                        && _text.Length > 0 && _text[^1] != '\n'
                                        && (_text[^1] != ' '
                                            || _prevShowHadTab
                                            || (_pageCellWidth > 0 && tjGapPre > _pageCellWidth)))
                                    {
                                        var tjGap = tjUseDev ? tjRunDevX - lastRunEndDevX
                                            : tjUsePage ? tjStartPageX - lastRunEndPageX
                                            : tx - lastRunEndX;
                                        // Effective font size for the gap threshold. Upright pages
                                        // keep the page-space Tm scale (the calibrated rule); rotated
                                        // runs use the projected line size — their raw fontSize can be
                                        // Tm-scaled (fs 327 with tmA 0.027 is 8.85 pt on the page) and
                                        // an unprojected threshold swallows every real word gap.
                                        var tjGapFs = tjUsePage ? fontSize * Math.Abs(tmAr)
                                            : _currentLineEffFs > 0 && !double.IsNaN(_currentLineEffFs)
                                            ? _currentLineEffFs
                                            : tmRotated ? Math.Abs(fontSize * tmN)
                                            : fontSize;
                                        // A BACKWARD pen jump bigger than a grid cell means the
                                        // stream draws this row's columns out of X order: start a
                                        // new logical line and let the row merge re-order by column.
                                        // NOT for RTL text - Hebrew/Arabic legitimately pens
                                        // right-to-left and its runs assemble via the RTL row path.
                                        var recentRtl = false;
                                        for (var ri2 = _text.Length - 1; ri2 >= 0 && ri2 >= _text.Length - 8; ri2--)
                                            if (BidiReorderer.IsRtlChar(_text[ri2])) { recentRtl = true; break; }
                                        // An overlapping backjump — the pen lands within one cell of
                                        // the PREVIOUS run's own start, i.e. the stream re-draws over
                                        // the same spot (shadow/duplicate stack) — stays inline so the
                                        // later-ink dedup can collapse it; only a jump to an earlier
                                        // column (left of the previous run's start) breaks the line.
                                        var tjOverlapJump = !tmRotated && !double.IsNaN(lastRunStartPageX)
                                            && tjStartPageX >= lastRunStartPageX - _pageCellWidth;
                                        if (_pageCellWidth > 0 && tjGap < -_pageCellWidth && !recentRtl
                                            && !tjOverlapJump
                                            && _text.Length > 0 && _text[^1] != '\n')
                                        {
                                            RecordLineY();
                                            AppendStreamBreak();
                                            lastRunEndX = double.NaN; lastRunEndDevX = double.NaN; lastRunEndPageX = double.NaN;
                                        }
                                        else
                                        {
                                        if (GridDebug)
                                            Console.Error.WriteLine($"[tjgap] tx={tx:F1} lastEnd={lastRunEndX:F1} gap={tjGap:F1} rot={tmRotated} runPageX={tjRunPageX:F1} gapFs={tjGapFs:F2} effFs={_currentLineEffFs:F2} useDev={tjUseDev}");
                                        // Leading drawn spaces of the array's first piece fill
                                        // their own columns and count toward the grid target
                                        // (see the Tj-path note).
                                        // See the Tj-path note: a run pads to its own start
                                        // column; leading drawn spaces land at their columns.
                                        leadingSpaces = _pageCellWidth > 0
                                            ? ColumnSpaces(tjGap, tjGapFs * 0.15, tjRunPageX)
                                            : ComputeSpaceCount(tjGap, tjGapFs * 0.15, tjGapFs);
                                        // Sub-cell gaps keep their grid pad (see the Tj-path note:
                                        // the gap space grid-places like any word start).
                                        }
                                    }
                                    // Pen start offsets (text-space, one per tjBuf char) for the run
                                    // span's per-character X map; invalidated when the code↔char
                                    // mapping is not 1:1 for some sub-string.
                                    var tjRel = new List<double>();
                                    var tjRelValid = !clipping;
                                    // Synthetic-space eligibility (validated over a
                                    // 1231-run corpus; same rule as the fragment
                                    // absorber): one space per adjustment ≤ −130/1000 em iff the
                                    // array is "armed" — any ≥2-glyph piece, or any glyph that is
                                    // NOT an uppercase letter or punctuation (font type is
                                    // irrelevant; tracked caps-only display text collapses) — and
                                    // is not the letter-tracking shape (>10 pieces, ALL
                                    // single-glyph → collapse; word-piece prose arrays keep their
                                    // kern-encoded word gaps).
                                    var tjIsType0 = currentFontDict?.GetName("Subtype") == "Type0";
                                    var tjPieceCount = 0;
                                    var tjMultiGlyph = false;
                                    var tjAdjs = new List<double>();
                                    foreach (var pre in tjArr)
                                        if (pre is PdfString preS0)
                                        {
                                            tjPieceCount++;
                                            if (preS0.Value.Length >= (tjIsType0 ? 4 : 2)) tjMultiGlyph = true;
                                        }
                                        else
                                            tjAdjs.Add(GetNumber(pre));
                                    var tjSynthArmed = tjMultiGlyph;
                                    if (!tjSynthArmed)
                                        foreach (var pre in tjArr)
                                        {
                                            if (pre is not PdfString preS) continue;
                                            var preDec = NormalizeDecoded(DecodeString(preS.Value, currentToUnicode, currentFontDict, reader, useFontEngine));
                                            if (preDec.Length >= 2) { tjSynthArmed = true; tjMultiGlyph = true; break; }
                                            var preArm = false;
                                            foreach (var preC in preDec)
                                                if (!char.IsUpper(preC) && !char.IsPunctuation(preC))
                                                { preArm = true; break; }
                                            if (preArm) { tjSynthArmed = true; break; }
                                        }
                                    tjSynthArmed = tjSynthArmed && tjPieceCount >= 2
                                        && (tjPieceCount <= 10 || tjMultiGlyph);
                                    // Letter-tracked single-glyph arrays (the disarmed shape) can still
                                    // encode WORD gaps — as kern OUTLIERS against the array's uniform
                                    // tracking baseline, not as absolute-threshold kerns: a newspaper
                                    // headline tracks letters at +20..+58 and words at −135..−169
                                    // (never reaching the classic −190). Break where the adjustment
                                    // falls ≥130/1000 em BELOW the array's median; a uniformly tracked
                                    // display run (every kern ≈ the median) still collapses.
                                    var tjMedian = double.NaN;
                                    if (tjPieceCount >= 5 && tjAdjs.Count >= 4)
                                    {
                                        tjAdjs.Sort();
                                        tjMedian = tjAdjs[tjAdjs.Count / 2];
                                    }
                                    var tjLtrackMedian = !tjSynthArmed && !tjMultiGlyph ? tjMedian : double.NaN;
                                    // Per-glyph POSITIONING arrays: in an all-single-glyph array
                                    // where word-depth kerns are the NORM rather than the exception
                                    // (half or more of the adjustments reach −130), the kerns place
                                    // glyphs, they don't separate words — synthesizing a space at
                                    // each would shred the run into single-char confetti. "Page:1/1"
                                    // (4 of 7 kerns at −264…−284) collapses even though lowercase
                                    // letters arm it; "Date : 26/05/2022 03:53:42 PM" (3 word kerns
                                    // among 24 small tracking values) keeps its word gaps.
                                    var tjPositioningArray = false;
                                    var tjDeepMedian = double.NaN;
                                    if (!tjMultiGlyph && tjAdjs.Count >= 3)
                                    {
                                        var deepList = new List<double>();
                                        foreach (var a2 in tjAdjs)
                                            if (a2 <= -130) deepList.Add(a2);
                                        if (deepList.Count * 2 >= tjAdjs.Count && deepList.Count > 0)
                                        {
                                            tjPositioningArray = true;
                                            deepList.Sort();
                                            // The placement baseline is the word-depth cluster's own
                                            // median; only a kern well below IT separates words.
                                            tjDeepMedian = deepList[deepList.Count / 2];
                                        }
                                    }
                                    StringBuilder? tjDbg = GridDebug ? new StringBuilder() : null;
                                    foreach (var item in tjArr)
                                    {
                                        if (item is PdfString tjS)
                                        {
                                            hadString = true;
                                            var tjDecoded = NormalizeDecoded(DecodeString(tjS.Value, currentToUnicode, currentFontDict, reader, useFontEngine), foldNbsp: searchRect is null);
                                            tjDbg?.Append('\'').Append(tjDecoded).Append('\'');
                                            tjBuf.Append(tjDecoded);
                                            var tjItemW = (currentMetrics?.MeasureString(tjS.Value, fontSize)
                                                       ?? (fontSize * 0.5 * tjS.Value.Length)) * horizScale;
                                            if (tjRelValid)
                                            {
                                                var itemRel = BuildCharXs(tjS.Value, currentMetrics, fontSize,
                                                    horizScale, tjDecoded.Length);
                                                if (itemRel is not null)
                                                    foreach (var r in itemRel) tjRel.Add(tjWidth + r);
                                                else if (tjDecoded.Length > 0)
                                                {
                                                    // Uniform fallback for this sub-string only.
                                                    var step = tjItemW / tjDecoded.Length;
                                                    for (var ri = 0; ri < tjDecoded.Length; ri++)
                                                        tjRel.Add(tjWidth + step * ri);
                                                }
                                            }
                                            tjWidth += tjItemW;
                                            tjDecodedLen += tjDecoded.Length;
                                            if (clipRot)
                                                AppendClippedRunRot(clipBuf!, tjS.Value, ref clipPen);
                                            else if (clipping)
                                                AppendClippedRun(clipBuf!, tjS.Value, currentToUnicode, currentFontDict,
                                                    reader, useFontEngine, currentMetrics, fontSize, horizScale,
                                                    clipRect!, tmOriginX, tmA, localCmTx, cmLa, ref clipPen, charSpacing, wordSpacing, out _, blankClip);
                                        }
                                        else
                                        {
                                            var adj = GetNumber(item);
                                            tjDbg?.Append('(').Append(adj.ToString("F0")).Append(')');
                                            var advance = -adj * fontSize / 1000.0;
                                            tjWidth += advance;
                                            var kernGapStart = tjWidth - advance;
                                            // Any kern beyond the classic −190 word-break threshold
                                            // separates (incl. Pure-grid column jumps in letter-tracked
                                            // single-glyph arrays, e.g. an 11-piece row with a −9711
                                            // column hop); the −130 rule EXTENDS the reach for
                                            // armed shapes only.
                                            var tjKernBreaks = tjPositioningArray
                                                ? adj - tjDeepMedian <= -130
                                                : adj < -190 || (tjSynthArmed && adj <= -130)
                                                  || (!double.IsNaN(tjLtrackMedian) && adj - tjLtrackMedian <= -130
                                                      && (tjLtrackMedian >= 0 || adj <= -250));
                                            if (tjKernBreaks
                                                && (tjBuf.Length == 0 || tjBuf[^1] != ' '))
                                            {
                                                // Under the Pure grid a large intra-TJ kern is a column
                                                // gap like any other: pad to the grid column of the pen
                                                // position after the kern, not a single word space.
                                                var pad = 1;
                                                // Grid-pad only genuine column jumps (≥ ~1 em). A word-space
                                                // kern (0.2–0.6 em) stays a single space — proportional prose
                                                // output columns drift from ink columns, and padding to the
                                                // grid there sprays spaces mid-sentence.
                                                if (_pageCellWidth > 0 && !clipping && advance > fontSize)
                                                {
                                                    // Same absolute floor grid as ColumnSpaces — the kern pen
                                                    // sits at the target glyph's left edge; floor quantisation
                                                    // assigns boundary glyphs to the lower column by itself.
                                                    var penPageX = tmRotated
                                                                       ? tmOriginX + (tx + tjWidth - tmOriginX) * tmA + localCmTx
                                                                       : tmE + (tx + tjWidth - tlmX) * tmAr;
                                                    pad = ColumnSpaces(advance, 0, penPageX, leadingSpaces + tjBuf.Length);
                                                    if (GridDebug)
                                                        Console.Error.WriteLine($"[tjkern] pen={penPageX:F2} col={(penPageX - _pageMinX) / _pageCellWidth:F3} pad={pad} buf='{tjBuf}'");
                                                }
                                                for (var k = 0; k < pad; k++)
                                                {
                                                    tjBuf.Append(' ');
                                                    if (tjRelValid) tjRel.Add(kernGapStart);
                                                }
                                            }
                                            if (clipping)
                                            {
                                                // The synthesized word space sits at the current pen; emit it
                                                // only when that point is inside the rectangle (advance-axis
                                                // position: page X upright, page Y sideways).
                                                if (tjPositioningArray
                                                    ? adj - tjDeepMedian <= -130
                                                    : adj < -190 || (tjSynthArmed && adj <= -130)
                                                      || (!double.IsNaN(tjLtrackMedian) && adj - tjLtrackMedian <= -130
                                                          && (tjLtrackMedian >= 0 || adj <= -250)))
                                                {
                                                    // Under LimitToPageBounds with no caller rectangle, searchRect
                                                    // is null while clipping is driven by the page-bounds clipRect;
                                                    // fall back to it so the in-window test doesn't dereference null.
                                                    var win = searchRect ?? clipRect!;
                                                    var inWindow = clipRot
                                                        ? tmF + (clipPen - tlmX) * tmBr is var pY
                                                            && pY >= win.LLY && pY <= win.URY
                                                        : tmOriginX + (clipPen - tmOriginX) * tmA + localCmTx is var pX
                                                            && pX >= win.LLX && pX <= win.URX;
                                                    if (inWindow && (clipBuf!.Length == 0 || clipBuf[^1] != ' '))
                                                        clipBuf!.Append(' ');
                                                }
                                                clipPen += advance;
                                            }
                                        }
                                    }
                                    if (currentFontNonAgl && hadString)
                                        RecordAglError(currentFontName, tjBuf.ToString(),
                                            tmE + (tx - tlmX) * tmAr, tmY + localCmTy);
                                    // Apply per-operator RTL reversal: if all decoded TJ chars are RTL/neutral,
                                    // reverse to convert visual order to logical order (Hebrew, Arabic).
                                    var tjText = clipping ? clipBuf!.ToString() : ApplyRtlIfPureRtl(tjBuf.ToString());
                                    if (GridDebug)
                                    {
                                        Console.Error.WriteLine($"[tjrun] tx={tx:F2} w={tjWidth:F2} fs={fontSize:F2} tmA={tmA:F3} armed={tjSynthArmed} pieces={tjPieceCount} lead={leadingSpaces} txt='{(tjText.Length > 32 ? tjText.Substring(0, 32) : tjText)}'");
                                        var dbgS = tjDbg!.ToString();
                                        Console.Error.WriteLine($"[tjarr] {(dbgS.Length > 300 ? dbgS.Substring(0, 300) : dbgS)}");
                                    }
                                    // Duplicate-stack dedup (see the Tj path): the occluder inherits
                                    // the victim's slot, so its own gap spaces are dropped too.
                                    if (!tmRotated && searchRect is null && !rawInlineScripts
                                        && tjText.Trim().Length > 0
                                        && ReplaceOccludedPrevRun(tjText, tmE + (tx - tlmX) * tmAr,
                                            tjWidth * Math.Abs(tmAr), tmY))
                                        leadingSpaces = 0;
                                    if (clipping ? tjText.Length > 0 : hadString)
                                    {
                                        if (leadingSpaces > 0) _sawIntraLineGapSpaces = true;
                                        for (int si = 0; si < leadingSpaces; si++) _text.Append(' ');
                                    }
                                    // Avoid double spaces between previous run and this TJ block —
                                    // UNLESS the space was just synthesized for THIS boundary's
                                    // inter-run hole in the layout-aware (Pure) mode: a glyph-sized
                                    // hole and a drawn space glyph count separately
                                    // there (a run whose ':' was redacted reads back
                                    // "Date  13" — synth(gap) + the real space; Raw/MemorySaving
                                    // keep the single-space collapse). Also NOT on RTL lines (see
                                    // the Tj-path note): the document's own
                                    // space glyphs are kept beside the synthesized one.
                                    if ((leadingSpaces == 0
                                            || ExtractionOptions?.FormattingMode
                                                is TextExtractionOptions.TextFormattingMode.Raw
                                                or TextExtractionOptions.TextFormattingMode.MemorySaving)
                                        && _text.Length > 0 && _text[^1] == ' ' && tjText.Length > 0 && tjText[0] == ' '
                                        && !RecentTextIsRtl())
                                    {
                                        tjText = tjText.Substring(1);
                                        if (tjRelValid && tjRel.Count > 0) tjRel.RemoveAt(0);
                                    }
                                    // A minority-rotated run flattens left-to-right in logical
                                    // glyph order on one row: each INTERNAL
                                    // drawn-space group emits
                                    // max(n+1, floor(|cumAdvance|/cell) − chars)
                                    // spaces — the gap target is quantised from the advance
                                    // RELATIVE to the run start (not a difference of absolute
                                    // grid floors), and at least one synthesized pad joins
                                    // every drawn gap.
                                    if (tmRotated && !_pageRotDominant && _pageCellWidth > 0 && !clipping
                                        && tjRelValid && tjRel.Count == tjText.Length
                                        && tjText.IndexOf(' ') > 0)
                                    {
                                        var rsb = new StringBuilder(tjText.Length + 8);
                                        var ci2 = 0;
                                        while (ci2 < tjText.Length)
                                        {
                                            if (tjText[ci2] != ' ') { rsb.Append(tjText[ci2]); ci2++; continue; }
                                            var n2 = 0;
                                            while (ci2 + n2 < tjText.Length && tjText[ci2 + n2] == ' ') n2++;
                                            var after2 = ci2 + n2;
                                            if (after2 >= tjText.Length) { rsb.Append(' ', n2); break; }
                                            var target2 = (int)Math.Floor(Math.Abs(tjRel[after2] * tmA) / _pageCellWidth);
                                            rsb.Append(' ', Math.Min(200, Math.Max(n2 + 1, target2 - rsb.Length)));
                                            ci2 = after2;
                                        }
                                        if (rsb.Length != tjText.Length)
                                        {
                                            tjText = rsb.ToString();
                                            tjRelValid = false; // char↔pen map no longer 1:1
                                        }
                                    }
                                    bool tjIsLeadingPos = _text.Length == 0 || _text[^1] == '\n';
                                    bool tjAllSpace = tjText.Length > 0 && tjText.Trim().Length == 0;
                                    if (tjAllSpace && tjIsLeadingPos)
                                    {
                                        // A line-leading whitespace run is a grid citizen: emit it
                                        // (placeholder space glyphs form pure-pad lines in the
                                        // output). Remember its Y so a following RTL run
                                        // on the same line can still re-home the pad to its logical
                                        // end (the appended space also guards the re-home branch's
                                        // "text doesn't end with a space" check below).
                                        pendingReorderSpaceY = tmY;
                                        AppendShowText(tjText);
                                    }
                                    else
                                    {
                                        if (!double.IsNaN(pendingReorderSpaceY))
                                        {
                                            if (System.Math.Abs(tmY - pendingReorderSpaceY) > 1.0)
                                                pendingReorderSpaceY = double.NaN;              // different line: drop the orphan
                                            else if (tjText.Length > 0 && Aspose.Pdf.Text.BidiReorderer.IsRtlChar(tjText[0])
                                                     && _text.Length > 0 && _text[^1] != ' ')
                                            {
                                                _text.Append(' ');                             // re-home before the first RTL run
                                                pendingReorderSpaceY = double.NaN;
                                            }
                                        }
                                        if (tjText.Length > 0)
                                        {
                                            var tjPageScale = Math.Abs(tmRotated ? tmA : tmAr);
                                            double[]? tjCharXs = null;
                                            if (tjRelValid && tjRel.Count == tjText.Length)
                                            {
                                                tjCharXs = new double[tjRel.Count];
                                                for (var ri = 0; ri < tjRel.Count; ri++)
                                                    tjCharXs[ri] = tjRel[ri] * tjPageScale;
                                            }
                                            _pageRunSpans.Add(new RunSpan(_text.Length, tjText.Length,
                                                tmRotated ? (_pageRotDominant ? tmOriginX + (tx - tmOriginX) * tmA + localCmTx : tmE)
                                                          : tmE + (tx - tlmX) * tmAr,
                                                tjWidth * tjPageScale,
                                                !clipping && IsPureRtlRun(tjText),
                                                tjCharXs));
                                        }
                                        if (!tmRotated && searchRect is null && !rawInlineScripts
                                            && tjText.Trim().Length > 0)
                                            dedupPrevOffset = _text.Length;
                                        AppendShowText(tjText);
                                    }
                                    lastRunEndDevX = tmRotated ? tmOriginX + (tx - tmOriginX) * tmA + localCmTx + tjWidth * tmA : double.NaN;
                                    lastRunEndPageX = tmRotated ? double.NaN : tmE + (tx + tjWidth - tlmX) * tmAr;
                                    lastRunStartPageX = tmRotated ? double.NaN : tmE + (tx - tlmX) * tmAr;
                                    lastRunEndX = tx + tjWidth * (tmRotated ? tmA : 1.0); // rotated: advance projects through the axis norm
                                    lastRunEstWidth = tjWidth;
                                    lastDecodedLength = tjDecodedLen;
                                    tx += tjWidth;
                                    // Track rendered Y for subsequent line-break suppression logic
                                    lastRenderedY = tmY; lastRenderedFs = fontSize * (tmRotated ? tmN : 1.0);
                                }
                            }
                            break;
                        }
                        case "'":
                        case "\"":
                        {
                            _textShowingOpCount++;
                            EnsureFontSet(fontSet, op);
                            // PDF spec: ' is "move to next line and show string" — equivalent to T* then Tj.
                            //          " is "set word/char spacing, move to next line, show string" —
                            //          operands = aw, ac, string.
                            // The operator advances the text line matrix by -leading in y.
                            // Historically we unconditionally emitted \r\n, but when a preceding Tm
                            // has just repositioned to a different column's Y (same visual row),
                            // the post-' Y may still be on the SAME logical line. Compare with
                            // lastRenderedY to decide.
                            // Move text line matrix down by leading (pre-text position).
                            // This happens even while the current line is filtered out —
                            // ' is T* + Tj, and T* always advances the line matrix. Bailing
                            // out before the advance froze tmY at the paragraph's first
                            // line, so a paragraph starting above the search rectangle
                            // never re-entered it and its in-window lines were dropped.
                            var newY = tmY - leading * (tmRotated ? tmN : tmD > 0 ? tmD : tmN);
                            tmY = newY;
                            tmE += -leading * tmCr;
                            tmF += -leading * tmDr;
                            tx = tlmX;
                            // Re-evaluate the line-level filters at the new baseline.
                            skipText = LineFiltered(tmY);
                            if (skipText) { break; }
                            _pageHasRotatedText |= tmRotated;
                            _currentLineEffFs = tmRotated
                                ? Math.Abs(fontSize * tmN)  // composed projection norm already carries the CTM; the scalar d is ~0 sideways
                                : Math.Abs(fontSize * (tmD > 0 ? tmD : tmN) * localCmD);
                            _currentLineDescent = currentMetrics is not null && currentMetrics.Descent < 0
                                ? -currentMetrics.Descent / 1000.0
                                : 0.2;
                            _currentLineIsRotated = tmRotated && !_pageRotDominant
                                && (_text.Length == 0 || _text[^1] == '\n' || _currentLineIsRotated);
                            if (_currentLineIsRotated && double.IsNaN(_currentLineDevY))
                            {
                                _currentLineDevY = tmF + (tx - tlmX) * tmBr / (Math.Abs(tmA) < 0.001 ? 1.0 : tmA);
                                if (GridDebug)
                                    Console.Error.WriteLine($"[roty] devY={_currentLineDevY:F1} tmF={tmF:F1} tx={tx:F1} tlmX={tlmX:F1} tmBr={tmBr:F2} tmA={tmA:F2} tmE={tmE:F1} op={op}");
                            }
                            PdfString? qStr = null;
                            if (op == "'" && operands.Count >= 1) qStr = operands[0] as PdfString;
                            else if (op == "\"" && operands.Count >= 3) qStr = operands[2] as PdfString;

                            // Decide whether to emit a newline. If we have no prior rendered Y
                            // or the new Y is meaningfully below the last rendered Y, we are on
                            // a new logical line — emit \r\n. Otherwise (same Y ± ~fontSize*0.3)
                            // we are continuing the same row from a different column.
                            var yThreshold = Math.Max(1.0, fontSize * 0.3 * (tmRotated ? tmN : 1.0));
                            bool sameRow = !double.IsNaN(lastRenderedY)
                                           && Math.Abs(newY - lastRenderedY) <= yThreshold;
                            if (!sameRow)
                            {
                                if (_text.Length > 0 && _text[^1] != '\n')
                                {
                                    RecordLineY();
                                    AppendStreamBreak();
                                }
                                lastRunEndX = double.NaN; lastRunEndDevX = double.NaN; lastRunEndPageX = double.NaN; // new line, reset gap tracking
                            }

                            if (qStr is not null)
                            {
                                // Styled single glyph: one-char /ActualText over a one-glyph
                                // show falls back to the font's own decode (see the flag note).
                                if (actualText is not null && !actualTextUsed && actualTextSingleChar
                                    && ActualTextYieldsToDecode(qStr))
                                    actualText = null;
                                if (actualText is not null)
                                {
                                    if (!actualTextUsed)
                                    {
                                        AppendShowText(actualText);
                                        actualTextUsed = true;
                                    }
                                    // Advance the pen over the replaced glyphs (see the Tj note).
                                    tx += (currentMetrics?.MeasureString(qStr.Value, fontSize)
                                           ?? fontSize * 0.5 * qStr.Value.Length) * horizScale;
                                }
                                else
                                {
                                    var fullDecoded = ApplyRtlIfPureRtl(NormalizeDecoded(
                                        DecodeString(qStr.Value, currentToUnicode, currentFontDict, reader, useFontEngine), foldNbsp: searchRect is null));
                                    // When a search rectangle is active, clip the run to the
                                    // glyphs whose advance box falls inside it (page space).
                                    var clipping = clipRect is not null && tmD > 0 && currentMetrics is not null;
                                    var decoded = fullDecoded;
                                    if (clipping)
                                    {
                                        var clip = new StringBuilder();
                                        var pen = tx;
                                        AppendClippedRun(clip, qStr.Value, currentToUnicode, currentFontDict,
                                            reader, useFontEngine, currentMetrics, fontSize, horizScale,
                                            clipRect!, tmOriginX, tmA, localCmTx, cmLa, ref pen, charSpacing, wordSpacing, out _, blankClip);
                                        decoded = clip.ToString();
                                    }
                                    if (!clipping || decoded.Length > 0)
                                    {
                                        TrackRowX(tmRotated
                                            ? (_pageRotDominant ? tmOriginX + (tx - tmOriginX) * tmA + localCmTx : tmE)
                                            : (tmOriginX + (tx - tmOriginX) * tmA) * cmLa + cmLe);
                                        // Same-row continuation: insert proportional spaces for the
                                        // horizontal gap (Pure mode), mirrors Tj/TJ gap logic.
                                        if (sameRow && !double.IsNaN(lastRunEndX)
                                            && _text.Length > 0 && _text[^1] != ' ' && _text[^1] != '\n')
                                        {
                                            var gap = tx - lastRunEndX;
                                            var threshold = fontSize * 0.2;
                                            var spaces = ComputeSpaceCount(gap, threshold, fontSize);
                                            if (spaces > 0) _sawIntraLineGapSpaces = true;
                                            for (int si = 0; si < spaces; si++) _text.Append(' ');
                                        }
                                        AppendShowText(decoded);
                                    }
                                    var measuredWidth = currentMetrics?.MeasureString(qStr.Value, fontSize);
                                    var width = (measuredWidth ?? (fontSize * 0.5 * fullDecoded.Length)) * horizScale;
                                    lastRunEndDevX = tmRotated ? tmOriginX + (tx - tmOriginX) * tmA + localCmTx + width * tmA : double.NaN;
                                    lastRunEndPageX = tmRotated ? double.NaN : tmE + (tx + width - tlmX) * tmAr;
                                    lastRunStartPageX = tmRotated ? double.NaN : tmE + (tx - tlmX) * tmAr;
                                    lastRunEndX = tx + width * (tmRotated ? tmA : 1.0); // rotated: advance projects through the axis norm
                                    lastRunEstWidth = width;
                                    lastHadMetrics = measuredWidth.HasValue;
                                    lastDecodedLength = decoded.Length;
                                    tx += width;
                                    lastRenderedY = newY; lastRenderedFs = fontSize * (tmRotated ? tmN : 1.0);
                                }
                            }
                            _currentLineY = newY;
                            _currentLineCmTy = tmRotated ? 0 : LineCmAdjust(depth, localCmD, localCmTy, _currentLineY);
                            break;
                        }
                        default:
                            ProcessOperator(op, operands, fonts, reader, pageDict,
                                ref currentFontName, ref currentToUnicode, ref currentFontDict,
                                actualText, ref actualTextUsed, fontSize, depth, actualTextSingleChar);
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
        return fontSet;
    }

    /// <summary>
    /// Strict font-usage guard for a text-showing operator: if no font is set in the
    /// current graphics state the content stream is malformed (no preceding Tf), so throw
    /// <see cref="IncorrectFontUsageException"/> — unless the caller opted into tolerant
    /// extraction via <see cref="Text.TextSearchOptions.IgnoreResourceFontErrors"/>.
    /// </summary>
    private int _currentPageNumber;

    /// <summary>True when the font's /Encoding /Differences carries glyph names that
    /// aren't Adobe-Glyph-List-resolvable (custom "G12"-style or arbitrary tags) — the
    /// decoded text for such runs is best-effort, and with
    /// <see cref="TextSearchOptions.LogTextExtractionErrors"/> each affected show
    /// operator is reported through <see cref="Errors"/>.</summary>
    internal static bool DifferencesNotAglCompliant(PdfDictionary? fontDict, PdfReader reader)
    {
        if (fontDict is null) return false;
        var encodingObj = fontDict.Get("Encoding");
        var encodingDict = encodingObj as PdfDictionary ?? (encodingObj is null ? null : reader.ResolveDict(encodingObj));
        if (encodingDict is null) return false;
        var diffObj = reader.Resolve(encodingDict.Get("Differences"));
        if (diffObj is not PdfArray diffArray || diffArray.Count == 0) return false;
        foreach (var item in diffArray)
        {
            if (item is not PdfName nameVal) continue;
            var name = nameVal.Value;
            if (name.Length == 1) continue;
            if (GlyphNameToUnicode.ContainsKey(name)) continue;
            if (name.StartsWith("uni", StringComparison.Ordinal) && name.Length >= 7 && IsAllHex(name.Substring(3))) continue;
            if (name.Length >= 5 && name.Length <= 7 && name[0] == 'u' && IsAllHex(name.Substring(1))) continue;
            return true; // e.g. "G12" glyph-index names, producer-specific tags
        }
        return false;
    }

    private void RecordAglError(string? fontKey, string extracted, double x, double y)
    {
        var key = fontKey ?? "?";
        var summary = $"Font {key} contains glyphs notification that isn't compliant with Adobe Glyph List.";
        var description = "The font has Differences array. It is used for glyph to Unicode mapping. "
            + "But font's glyphs notification isn't compliant with Adobe Glyph List. "
            + string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "The text on position {{X={0:0.###},Y={1:0.###}}} may be extracted incorrectly.", x, y);
        Errors.Add(new TextExtractionError
        {
            PageIndex = _currentPageNumber,
            Message = summary,
            Summary = summary,
            Description = description,
            ExtractedText = extracted,
            FontKey = key,
            Location = new TextExtractionErrorLocation
            {
                PageNumber = _currentPageNumber,
                FontUsedKey = key,
                TextStartPoint = new Aspose.Pdf.Point(x, y),
            },
        });
    }

    private void EnsureFontSet(bool fontSet, string op)
    {
        if (fontSet) return;
        if (TextSearchOptions?.IgnoreResourceFontErrors ?? false) return;
        throw new IncorrectFontUsageException(
            $"Document error: {op} operator without preceding Tf - no font set for the text segment");
    }

    private void ProcessOperator(string op, List<PdfObject> operands,
        Dictionary<string, PdfDictionary> fonts, PdfReader reader, PdfDictionary pageDict,
        ref string? currentFontName, ref Dictionary<int, string>? currentToUnicode,
        ref PdfDictionary? currentFontDict,
        string? actualText, ref bool actualTextUsed, double fontSize, int depth,
        bool actualTextSingleChar = false)
    {
        // UseFontEngineEncoding: decode via the font program's encoding/cmap instead of
        // /ToUnicode (mirrors the local of the same name in the main extraction loop).
        bool useFontEngine = TextSearchOptions?.UseFontEngineEncoding ?? false;
        // Styled single glyph: a one-char /ActualText over a one-glyph show that
        // decodes to the SAME letter differing only in case falls back to the
        // font's own decode (see the main loop's ActualTextYieldsToDecode note).
        if (actualText is not null && !actualTextUsed && actualTextSingleChar
            && (op == "Tj" || op == "TJ") && operands.Count >= 1)
        {
            var d = string.Empty;
            if (operands[0] is PdfString sp)
                d = NormalizeDecoded(DecodeString(sp.Value, currentToUnicode, currentFontDict, reader, useFontEngine));
            else if (operands[0] is PdfArray ap)
                foreach (var it in ap)
                {
                    if (it is not PdfString s2) continue;
                    d += NormalizeDecoded(DecodeString(s2.Value, currentToUnicode, currentFontDict, reader, useFontEngine));
                    if (d.Length > 1) break;
                }
            if (d.Length == 1 && d[0] != actualText[0]
                && char.ToUpperInvariant(d[0]) == char.ToUpperInvariant(actualText[0]))
                actualText = null;
        }
        switch (op)
        {
            case "Tf": // Set font
                if (operands.Count >= 1 && operands[0] is PdfName fontName)
                {
                    currentFontName = fontName.Value;
                    if (fonts.TryGetValue(currentFontName, out var fontDict))
                    {
                        currentFontDict = fontDict;
                        currentToUnicode = useFontEngine ? null : ParseToUnicode(fontDict, reader);
                    }
                    else
                    {
                        currentFontDict = null;
                        currentToUnicode = null;
                    }
                }
                break;

            case "Tj": // Show string
                if (operands.Count >= 1 && operands[0] is PdfString str)
                {
                    if (actualText is not null)
                    {
                        if (!actualTextUsed)
                        {
                            AppendShowText(actualText);
                            actualTextUsed = true;
                        }
                    }
                    else
                    {
                        AppendShowText(NormalizeDecoded(DecodeString(str.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
                    }
                }
                break;

            case "TJ": // Show string array (with positioning)
                if (operands.Count >= 1 && operands[0] is PdfArray arr)
                {
                    if (actualText is not null)
                    {
                        if (!actualTextUsed)
                        {
                            AppendShowText(actualText);
                            actualTextUsed = true;
                        }
                    }
                    else
                    {
                        // Use font-size-relative threshold: 25% of font size in thousandths
                        var spaceThreshold = -(fontSize * 250 / fontSize); // -250 units (normalized)
                        // Simplified: -250 works well for most fonts at any size
                        foreach (var item in arr)
                        {
                            if (item is PdfString s)
                            {
                                AppendShowText(NormalizeDecoded(DecodeString(s.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
                            }
                            else if (item is PdfInteger adj && adj.Value < -200)
                            {
                                if (_text.Length == 0 || _text[^1] != ' ')
                                    _text.Append(' ');
                            }
                            else if (item is PdfReal adjR && adjR.Value < -200)
                            {
                                if (_text.Length == 0 || _text[^1] != ' ')
                                    _text.Append(' ');
                            }
                        }
                    }
                }
                break;

            case "'": // Move to next line and show string
                // Record the finished line's Y before breaking — an unrecorded break
                // desynchronizes the line↔Y pairing SortLinesByY relies on.
                RecordLineY();
                AppendStreamBreak();
                if (operands.Count >= 1 && operands[0] is PdfString str2)
                {
                    if (actualText is not null && !actualTextUsed)
                    {
                        AppendShowText(actualText);
                        actualTextUsed = true;
                    }
                    else if (actualText is null)
                    {
                        AppendShowText(NormalizeDecoded(DecodeString(str2.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
                    }
                }
                break;

            case "\"": // Set spacing, move to next line, show string
                RecordLineY(); // see the ' note — keep the line↔Y pairing aligned
                AppendStreamBreak();
                if (operands.Count >= 3 && operands[2] is PdfString str3)
                {
                    if (actualText is not null && !actualTextUsed)
                    {
                        AppendShowText(actualText);
                        actualTextUsed = true;
                    }
                    else if (actualText is null)
                    {
                        AppendShowText(NormalizeDecoded(DecodeString(str3.Value, currentToUnicode, currentFontDict, reader, useFontEngine)));
                    }
                }
                break;

            // Td, TD, Tm, T* are handled before ProcessOperator in the caller switch;
            // they should not reach here. Fall through without action if they do.
            case "Td" or "TD":
            case "Tm":
            case "T*":
                break;

            // cm, q, Q, Do are handled in the outer keyword switch (with CTM context)
            case "cm":
            case "q":
            case "Q":
                break;

            // Do is handled in the outer keyword switch (with CTM context)
            case "Do":
                break;
        }
    }

    /// <summary>
    /// Decode a byte string using the font's encoding. Used by both TextAbsorber and TextFragmentAbsorber.
    /// </summary>
    internal static string DecodeStringPublic(byte[] bytes, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader, bool useFontEngineEncoding = false,
        bool foldNbsp = true)
        => NormalizeDecoded(DecodeString(bytes, toUnicode, fontDict, reader, useFontEngineEncoding), foldNbsp);

    /// <summary>
    /// Normalize a decoded text string for extraction. Full-page plain extraction
    /// folds U+00A0 (non-breaking space) to a regular
    /// space, but RECT-LIMITED extraction preserves NBSP verbatim: the
    /// windowed output keeps the source's NBSP glyphs, and phrase
    /// asserts depend on it. Fragment extraction (TextFragmentAbsorber) always
    /// preserves it (foldNbsp=false).
    /// </summary>
    private static string NormalizeDecoded(string s, bool foldNbsp = true)
    {
        if (foldNbsp && s.IndexOf('\u00a0') >= 0) s = s.Replace('\u00a0', ' ');
        // Some PDFs ship a buggy ToUnicode CMap that maps a whitespace glyph to a
        // sequence containing CR/LF (e.g. the space glyph -> "\t\r  "). Those are
        // glyph text, not line structure (the absorber emits its own line breaks
        // from Td/T*/' positioning), so a stray CR/LF inside decoded glyph text
        // would corrupt extraction. Collapse any whitespace run that contains a
        // CR or LF into a single space; tab-only and normal spacing are untouched.
        if (s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0)
            s = CollapseControlWhitespace(s);
        return s;
    }

    private static string CollapseControlWhitespace(string s)
    {
        static bool IsWs(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\v';
        var sb = new System.Text.StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (IsWs(s[i]))
            {
                int j = i;
                bool hasBreak = false;
                while (j < s.Length && IsWs(s[j]))
                {
                    if (s[j] == '\r' || s[j] == '\n') hasBreak = true;
                    j++;
                }
                if (hasBreak) sb.Append(' ');          // whitespace run with CR/LF -> single space
                else sb.Append(s, i, j - i);            // no CR/LF -> leave untouched
                i = j;
            }
            else
            {
                sb.Append(s[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Append the glyphs of one show string that fall within the search rectangle, in
    /// page space, to <paramref name="sb"/>. The pen (<paramref name="penText"/>, in the
    /// unscaled text-space units the absorber tracks) advances over every glyph whether
    /// or not it is visible, so positioning of later runs is unaffected. X is clipped per
    /// glyph: a glyph contributes only when its whole advance box lies within
    /// [LLX, URX]. Y is filtered at the line level by the caller before this runs.
    /// </summary>
    /// <remarks>
    /// The absorber accumulates Td/TD advances unscaled but tracks the text-matrix X scale
    /// in <paramref name="tmScaleX"/> and the page-space line origin in <paramref name="tmOriginX"/>,
    /// so a text-space pen X maps to page space as
    /// tmOriginX + (penText - tmOriginX) * tmScaleX + localCmTx.
    /// </remarks>
    private static void AppendClippedRun(StringBuilder sb, byte[] bytes,
        Dictionary<int, string>? toUnicode, PdfDictionary? fontDict, PdfReader reader,
        bool useFontEngine, FontMetrics? metrics, double fontSize, double horizScale,
        Rectangle searchRect, double tmOriginX, double tmScaleX, double localCmTx,
        double cmScaleX, ref double penText, double charSpacing, double wordSpacing,
        out double keptStartPen, bool blankClipped = false)
    {
        // Text-space pen of the first SURVIVING glyph: a left-clipped run's
        // grid position starts there, not at the run's off-page origin.
        // (Not reported in blank mode — the run keeps its original position.)
        keptStartPen = double.NaN;
        const double eps = 0.05;
        var isCid = metrics?.IsCid ?? (fontDict?.GetName("Subtype") == "Type0");
        var step = isCid ? 2 : 1;
        for (var i = 0; i + step - 1 < bytes.Length; i += step)
        {
            var code = isCid ? ((bytes[i] << 8) | bytes[i + 1]) : bytes[i];
            var seg = isCid ? new[] { bytes[i], bytes[i + 1] } : new[] { bytes[i] };
            var glyph = NormalizeDecoded(DecodeString(seg, toUnicode, fontDict, reader, useFontEngine), foldNbsp: false);
            // Advance = glyph width + Tc (+ Tw on the space code), matching the
            // main extraction pen — clip positions drift without them.
            var w = ((metrics is not null
                ? metrics.GetWidth(code) * fontSize / 1000.0
                : fontSize * 0.5 * System.Math.Max(1, glyph.Length))
                + charSpacing + (!isCid && code == 32 ? wordSpacing : 0)) * horizScale;
            // Device X = CTM linear scale × (Tm-composed text X) + CTM translation.
            // Content nested in a scaled Form XObject (e.g. resized page content
            // invoked via "0.6 0 0 0.6 tx ty cm /Fm Do") must fold the cm scale in,
            // or every glyph past (URX − cmTx) of UNSCALED text space gets clipped.
            var scale = System.Math.Abs(cmScaleX) > 1e-9 ? cmScaleX : 1.0;
            var e1 = scale * (tmOriginX + (penText - tmOriginX) * tmScaleX) + localCmTx;
            var e2 = scale * (tmOriginX + (penText + w - tmOriginX) * tmScaleX) + localCmTx;
            var pageLeft = System.Math.Min(e1, e2);
            var pageRight = System.Math.Max(e1, e2);
            if (pageLeft >= searchRect.LLX - eps && pageRight <= searchRect.URX + eps)
            {
                if (!blankClipped && double.IsNaN(keptStartPen)) keptStartPen = penText;
                sb.Append(glyph);
            }
            else if (blankClipped)
            {
                sb.Append(' ', Math.Max(1, glyph.Length));
            }
            penText += w;
        }
    }

    private static string DecodeString(byte[] bytes, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader reader, bool useFontEngineEncoding = false)
    {
        // Resolve Differences encoding upfront (used as fallback below)
        Dictionary<int, string>? differences = null;
        string? baseEncodingName = null;

        var encodingObj = fontDict?.Get("Encoding");
        PdfDictionary? encodingDict = null;
        if (encodingObj is PdfDictionary ed)
            encodingDict = ed;
        else if (encodingObj is not null)
            encodingDict = reader.ResolveDict(encodingObj);

        if (encodingDict is not null)
        {
            differences = ParseDifferencesEncoding(encodingDict, reader);
            baseEncodingName = encodingDict.GetName("BaseEncoding");
        }

        // 1. ToUnicode CMap — highest priority; Differences used as fallback for unmapped codes
        if (toUnicode is not null)
        {
            return DecodeWithToUnicode(bytes, toUnicode, fontDict, reader, differences, baseEncodingName);
        }

        // Identity-H / Identity-V — 2-byte CID encoding
        // Also handle Uni*-UCS2-* / Uni*-UTF16-* predefined CMaps (2-byte big-endian → Unicode codepoint)
        if (fontDict?.GetName("Subtype") == "Type0")
        {
            var cidEncoding = fontDict.GetName("Encoding");
            if (cidEncoding is not null && (
                cidEncoding == "Identity-H" || cidEncoding == "Identity-V" ||
                cidEncoding.Contains("-UCS2-") || cidEncoding.Contains("-UTF16-")))
            {
                // A Uni*-UCS2-* / Uni*-UTF16-* CMap emits UNICODE, not Adobe CIDs: the
                // 2-byte code IS the codepoint, so neither the collection's CID table nor
                // a glyph-id inversion applies to it — both would substitute an unrelated
                // character for every code that happens to be a valid CID in the
                // ordering. Same distinction the renderers draw
                // (CidFontInfo.IsUnicodeEncoding); only Identity-H/V has code == CID.
                var isUnicodeCMap = cidEncoding.Contains("-UCS2-") || cidEncoding.Contains("-UTF16-");
                // Try to get Adobe CID collection ordering for predefined table lookup
                var cidOrdering = isUnicodeCMap ? null : GetCidOrdering(fontDict, reader);
                // A CID font without /ToUnicode: for a NON-embedded font, recover Unicode by
                // inverting the installed system face's cmap — the producer assigned glyph
                // ids from that same face, so these documents stay decodable. For an
                // EMBEDDED program the raw-code fallback is kept (the
                // "NoToUnicode_UseRawCode" behaviour), so cmap inversion there stays opt-in
                // via TextSearchOptions.UseFontEngineEncoding.
                var gidToUnicode = isUnicodeCMap
                    ? null
                    : GetGidToUnicode(fontDict, reader, allowEmbedded: useFontEngineEncoding);
                return DecodeCidString(bytes, toUnicode, cidOrdering, gidToUnicode);
            }

            // Predefined legacy national CMap (GBK-EUC-H, 90ms-RKSJ-H, KSC-EUC-H, …):
            // the show-string bytes are a national multi-byte charset (mixed 1-/2-byte
            // codes), NOT Adobe CIDs. Without this branch the bytes fell through to the
            // per-byte WinAnsi default and Chinese/Japanese/Korean text extracted as
            // Latin-1 mojibake ("由 扫描全能王" → "ÓÉ É¨Ãè…"). Decode through the same
            // codepage tables the renderer already uses (GbkTable/SjisTable/KscTable).
            if (cidEncoding is not null && GetLegacyCidInfo(fontDict, reader) is { } legacy)
            {
                var sb = new StringBuilder();
                var i = 0;
                while (i < bytes.Length)
                {
                    var step = legacy.LegacyByteLength(bytes[i]);
                    if (step == 2 && i + 1 >= bytes.Length) step = 1;
                    if (step == 1)
                    {
                        sb.Append((char)bytes[i]);
                    }
                    else
                    {
                        var code = (bytes[i] << 8) | bytes[i + 1];
                        if (legacy.LegacyToUnicode(code) is int u)
                            sb.Append(char.ConvertFromUtf32(u));
                        else
                            sb.Append('�');
                    }
                    i += step;
                }
                return sb.ToString();
            }
        }

        // 2. Differences from Encoding dict
        if (differences is not null)
        {
            var sb = new StringBuilder();
            foreach (var b in bytes)
            {
                if (differences.TryGetValue(b, out var mapped))
                    sb.Append(mapped);
                else
                    sb.Append(DecodeByteWithEncoding(b, baseEncodingName));
            }
            return sb.ToString();
        }

        // 3. BaseEncoding from Encoding dict (no Differences)
        if (baseEncodingName is not null)
            return DecodeWithNamedEncoding(bytes, baseEncodingName);

        // 4. Encoding is a name
        var encoding = fontDict?.GetName("Encoding");
        if (encoding is not null)
            return DecodeWithNamedEncoding(bytes, encoding);

        // 4b. No /ToUnicode and no /Encoding at all: a symbolic embedded subset font
        // (FirstChar 1, custom glyph order) whose only Unicode signal is its own program.
        // Recover code → Unicode from the embedded cmap + post glyph names (Adobe Glyph
        // List). Without this the bytes fall through to WinAnsi and Cyrillic/Greek subsets
        // decode as control-char mojibake.
        if (encodingObj is null && fontDict is not null
            && fontDict.GetName("Subtype") != "Type0")
        {
            var postMap = GetPostNameCodeToUnicode(fontDict, reader);
            if (postMap is not null)
            {
                var sb = new StringBuilder(bytes.Length);
                foreach (var b in bytes)
                    sb.Append(postMap.TryGetValue(b, out var u) ? u : DecodeByteWithEncoding(b, null).ToString());
                return sb.ToString();
            }

            // 4c. Not even post names (format-3 post, PUA-only cmap): zero Unicode
            // semantics anywhere. Fall back to recognising each
            // glyph's OUTLINE SHAPE, locked on for the font once a code below
            // 0x20 proves it is not character-coded (gate machine —
            // sequential-by-first-use Ghostscript subsets start at 0x01).
            if (reader is not null)
            {
                var shaped = GlyphShapeDecoder.TryDecode(bytes, fontDict, reader);
                if (shaped is not null) return shaped;
            }

            // 4d. A non-embedded Standard-14 Type 1 font with no /Encoding entry
            // uses the font program's BUILT-IN encoding — StandardEncoding, not
            // WinAnsi. The two agree on printable ASCII but diverge completely in
            // the high range (0xC1 is the grave ACCENT in Standard, Á in WinAnsi),
            // so only bytes in Standard's high range take this path; the ASCII
            // range keeps the established default below.
            if (IsStandardLatinType1(fontDict) && bytes.Any(b => b >= 0xA1))
            {
                var sb = new StringBuilder(bytes.Length);
                foreach (var b in bytes)
                {
                    string? uni = null;
                    if (b >= 0xA1 && Type1StandardEncoding.GetName(b) is { } gname
                        && GlyphNameToUnicode.TryGetValue(gname, out var mapped))
                        uni = mapped;
                    if (uni is not null) sb.Append(uni);
                    else sb.Append(DecodeByteWithEncoding(b, "WinAnsiEncoding"));
                }
                return sb.ToString();
            }
        }

        // 5. Check for Symbol or ZapfDingbats built-in font encoding
        var baseFont = fontDict?.GetName("BaseFont");
        if (baseFont is not null)
        {
            var cleanName = baseFont.Contains('+') ? baseFont.Substring(baseFont.IndexOf('+') + 1) : baseFont;
            if (cleanName == "Symbol")
            {
                var sb = new StringBuilder(bytes.Length);
                foreach (var b in bytes)
                    sb.Append(SymbolEncoding.TryGetValue(b, out var ch) ? ch : (char)b);
                return sb.ToString();
            }
            if (cleanName == "ZapfDingbats")
            {
                var sb = new StringBuilder(bytes.Length);
                foreach (var b in bytes)
                    sb.Append(ZapfDingbatsEncoding.TryGetValue(b, out var ch) ? ch : (char)b);
                return sb.ToString();
            }
        }

        // 6. Default: WinAnsiEncoding
        return DecodeWithNamedEncoding(bytes, null);
    }

    /// <summary>True for a non-embedded Latin Standard-14 Type 1 font (Helvetica /
    /// Times / Courier families) — the fonts whose built-in encoding is Adobe
    /// StandardEncoding. Symbol and ZapfDingbats have their own built-ins and are
    /// handled separately; an embedded program carries its own encoding.</summary>
    private static bool IsStandardLatinType1(PdfDictionary fontDict)
    {
        if (fontDict.GetName("Subtype") != "Type1") return false;
        var baseFont = fontDict.GetName("BaseFont");
        if (baseFont is null) return false;
        var clean = baseFont.Contains('+') ? baseFont[(baseFont.IndexOf('+') + 1)..] : baseFont;
        return clean is "Helvetica" or "Helvetica-Bold" or "Helvetica-Oblique" or "Helvetica-BoldOblique"
            or "Times-Roman" or "Times-Bold" or "Times-Italic" or "Times-BoldItalic"
            or "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique";
    }

    private static string DecodeWithNamedEncoding(byte[] bytes, string? encoding)
    {
        if (encoding == "MacRomanEncoding")
        {
            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
                sb.Append(DecodeByteWithEncoding(b, "MacRomanEncoding"));
            return sb.ToString();
        }

        // WinAnsiEncoding or null (default)
        if (encoding == "WinAnsiEncoding" || encoding is null)
        {
            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
                sb.Append(DecodeByteWithEncoding(b, "WinAnsiEncoding"));
            return sb.ToString();
        }

        // Identity-H / Identity-V and other 2-byte predefined CJK CMaps
        if (encoding == "Identity-H" || encoding == "Identity-V" ||
            encoding.Contains("-UCS2-") || encoding.Contains("-UTF16-"))
            return DecodeCidString(bytes, null);

        // Unknown encoding — treat as WinAnsi
        var sb2 = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
            sb2.Append(DecodeByteWithEncoding(b, "WinAnsiEncoding"));
        return sb2.ToString();
    }

    private static char DecodeByteWithEncoding(byte b, string? encoding)
    {
        if (encoding == "MacRomanEncoding")
        {
            if (b < 128)
                return (char)b;
            return MacRomanEncoding.TryGetValue(b, out var ch) ? ch : (char)b;
        }

        // WinAnsiEncoding (default)
        if (b < 128)
            return (char)b;
        return WinAnsiEncoding.TryGetValue(b, out var wch) ? wch : (char)b;
    }

    /// <summary>
    /// Parse the /Differences array from an encoding dictionary.
    /// Returns a map from byte code to Unicode string, or null if no Differences found.
    /// </summary>
    internal static Dictionary<int, string>? ParseDifferencesEncoding(PdfDictionary encodingDict, PdfReader reader)
    {
        var diffObj = encodingDict.Get("Differences");
        PdfArray? diffArray = null;

        if (diffObj is PdfArray arr)
            diffArray = arr;
        else if (diffObj is not null)
        {
            // Could be an indirect reference
            var resolved = reader.Resolve(diffObj);
            if (resolved is PdfArray resolvedArr)
                diffArray = resolvedArr;
        }

        if (diffArray is null || diffArray.Count == 0)
            return null;

        var map = new Dictionary<int, string>();
        var currentCode = 0;

        foreach (var item in diffArray)
        {
            if (item is PdfInteger intVal)
            {
                currentCode = (int)intVal.Value;
            }
            else if (item is PdfName nameVal)
            {
                var glyphName = nameVal.Value;
                var resolved = ResolveGlyphName(glyphName);
                if (resolved is not null)
                    map[currentCode] = resolved;
                else
                    map[currentCode] = ((char)currentCode).ToString(); // fallback to code point
                currentCode++;
            }
        }

        return map.Count > 0 ? map : null;
    }

    /// <summary>
    /// Resolve an Adobe glyph name to its Unicode string representation.
    /// Supports dictionary lookup, uni&lt;XXXX&gt; and u&lt;XXXX&gt; patterns.
    /// </summary>
    internal static string? ResolveGlyphName(string name)
    {
        // Single ASCII character — return as-is
        if (name.Length == 1) return name;

        // Dictionary lookup
        if (GlyphNameToUnicode.TryGetValue(name, out var unicode))
            return unicode;

        // uni<XXXX> form — explicit Unicode codepoint(s), groups of 4 hex digits
        if (name.Length >= 7 && name.StartsWith("uni", StringComparison.Ordinal))
        {
            var hex = name.Substring(3);
            if (hex.Length % 4 == 0 && IsAllHex(hex))
            {
                var sb = new StringBuilder();
                for (int i = 0; i < hex.Length; i += 4)
                    sb.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
                return sb.Length > 0 ? sb.ToString() : null;
            }
        }

        // u<XXXX> form — single codepoint, 4-6 hex digits
        if (name.Length >= 5 && name.Length <= 7 && name[0] == 'u' && IsAllHex(name.Substring(1)))
            return char.ConvertFromUtf32(Convert.ToInt32(name.Substring(1), 16));

        // AGL underscore ligatures — components joined with '_' (/f_i, /f_f_i):
        // the joined component NAME is looked up first so the standard ligature
        // codepoints apply ("f"+"i" → "fi" → U+FB01), else the components'
        // resolutions concatenate.
        if (name.IndexOf('_') > 0)
        {
            var parts = name.Split('_');
            var joinedName = string.Concat(parts);
            if (GlyphNameToUnicode.TryGetValue(joinedName, out var lig))
                return lig;
            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                var comp = ResolveGlyphName(part);
                if (comp is null) return null;
                sb.Append(comp);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        // G<number> form — glyph index used as character code (common in subset fonts)
        // e.g. /G65 → 'A', /G32 → ' ', /G147 → U+201C via WinAnsi
        if (name.Length >= 2 && name[0] == 'G')
        {
            var suffix = name.Substring(1);
            if (suffix.Length > 0 && suffix.All(char.IsAsciiDigit))
            {
                var code = int.Parse(suffix);
                if (code < 128)
                    return ((char)code).ToString();
                if (code < 256)
                {
                    // Map through WinAnsiEncoding for 128-255
                    if (WinAnsiEncoding.TryGetValue((byte)code, out var wch))
                        return wch.ToString();
                }
                return char.ConvertFromUtf32(code);
            }
        }

        return null;
    }

    private static bool IsAllHex(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                return false;
        }
        return s.Length > 0;
    }

    /// <summary>A ToUnicode destination of a lone noncharacter (U+FFFF/U+FFFE)
    /// marks "unicode unknown", not a mapping.</summary>
    internal static bool IsUnknownToUnicodeDst(string s)
        => s.Length == 1 && (s[0] == '￿' || s[0] == '￾');

    /// <summary>The characters that ARE ligatures: the Latin ij/IJ pair, the f-ligature
    /// block, and the ae/oe letters. A glyph the encoding names as one of these keeps it
    /// even when the CMap spells the ligature out letter by letter.</summary>
    private static bool IsLigatureChar(char c) =>
        c is '\u0132' or '\u0133'                       // IJ, ij
          or '\u00C6' or '\u00E6'                       // AE, ae
          or '\u0152' or '\u0153'                       // OE, oe
          or >= '\uFB00' and <= '\uFB06';               // ff, fi, fl, ffi, ffl, st
    private static string DecodeWithToUnicode(byte[] bytes, Dictionary<int, string> map,
        PdfDictionary? fontDict, PdfReader reader,
        Dictionary<int, string>? differences = null, string? baseEncodingName = null)
    {
        var isCid = fontDict?.GetName("Subtype") == "Type0";
        var sb = new StringBuilder();
        var i = 0;

        while (i < bytes.Length)
        {
            // Try 2-byte lookup first (handles CIDFonts and mixed encodings)
            if (i + 1 < bytes.Length)
            {
                var code2 = (bytes[i] << 8) | bytes[i + 1];
                // Bypass a U+FFFF "unicode unknown" destination only when there is a
                // /Differences glyph name to fall through to (pdfTeX ligature codes in a
                // simple font). A CID font has no Differences, so its U+FFFF separators
                // must be kept — they get stripped downstream and replaced with the
                // U+A880 placeholder; the raw-code fallback below would corrupt them.
                if (map.TryGetValue(code2, out var mapped2) &&
                    !(IsUnknownToUnicodeDst(mapped2) && differences is not null && differences.ContainsKey(code2)))
                {
                    sb.Append(mapped2);
                    i += 2;
                    continue;
                }
            }

            // Try 1-byte lookup. A U+FFFF/U+FFFE destination is the producer
            // saying "unicode unknown" (pdfTeX writes it for ligature glyphs),
            // not a real mapping — those codes fall through to the Differences
            // glyph names, which DO resolve (/f_i → U+FB01).
            var code1 = bytes[i];
            if (map.TryGetValue(code1, out var mapped1) &&
                !(IsUnknownToUnicodeDst(mapped1) && differences is not null && differences.ContainsKey(code1)))
            {
                // A LIGATURE keeps its own character. When the CMap spells the glyph out
                // as the letters it is made of (a producer writing "ij" so the text can be
                // copied as separate letters) but the encoding names the glyph as a real
                // ligature character, the named one wins — that single character is what
                // the page draws and reads as. The test is deliberately narrow: only a
                // NAMED ligature qualifies, so a code whose CMap value merely carries a
                // trailing line break, or whose subset name resolves through a numeric
                // convention, keeps every character the CMap gave it.
                if (!isCid && mapped1.Length > 1 && !char.IsSurrogate(mapped1[0])
                    && differences is not null && differences.TryGetValue(code1, out var ligature)
                    && ligature.Length == 1 && IsLigatureChar(ligature[0]))
                    sb.Append(ligature);
                else
                    sb.Append(mapped1);
                i++;
                continue;
            }

            // Try Differences encoding as fallback (single byte)
            if (differences is not null && differences.TryGetValue(code1, out var diffMapped))
            {
                sb.Append(diffMapped);
                i++;
                continue;
            }

            // Fallback for CID fonts: interpret 2-byte value as direct Unicode (UCS-2/UTF-16)
            if (isCid && i + 1 < bytes.Length)
            {
                var code = (bytes[i] << 8) | bytes[i + 1];
                if (code is > 0 and < 0xD800 or > 0xDFFF and <= 0xFFFF)
                    sb.Append((char)code);
                else
                    sb.Append('\uFFFD');
                i += 2;
            }
            else
            {
                sb.Append(DecodeByteWithEncoding(bytes[i], baseEncodingName));
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts the CIDSystemInfo /Ordering from the first DescendantFont of a Type0 font.
    /// Returns null if not available or if Registry is not "Adobe".
    /// </summary>
    private static string? GetCidOrdering(PdfDictionary type0FontDict, PdfReader reader)
    {
        if (reader is null) return null;
        var descObj = reader.Resolve(type0FontDict.Get("DescendantFonts"));
        if (descObj is not PdfArray descArr || descArr.Count == 0) return null;
        var cidFontDict = reader.ResolveDict(descArr[0]);
        if (cidFontDict is null) return null;
        var cidSystemInfo = reader.ResolveDict(cidFontDict.Get("CIDSystemInfo"));
        if (cidSystemInfo is null) return null;
        // Registry and Ordering are PDF strings (not names)
        var registryObj = cidSystemInfo.Get("Registry");
        var registry = registryObj is PdfString rs ? rs.ToText() : (registryObj is PdfName rn ? rn.Value : null);
        if (registry != "Adobe") return null;
        var orderingObj = cidSystemInfo.Get("Ordering");
        return orderingObj is PdfString os ? os.ToText() : (orderingObj is PdfName on2 ? on2.Value : null);
    }

    private static string DecodeCidString(byte[] bytes, Dictionary<int, string>? toUnicode,
        string? cidOrdering = null, Dictionary<int, int>? gidToUnicode = null)
    {
        if (toUnicode is not null)
            return DecodeWithToUnicode(bytes, toUnicode, null, null!);

        var sb = new StringBuilder();
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            var code = (bytes[i] << 8) | bytes[i + 1];
            // Try Adobe predefined CID collection lookup first
            if (cidOrdering is not null)
            {
                var unicode = AdobeCidTables.LookupCid(cidOrdering, code);
                if (unicode is not null)
                {
                    sb.Append(char.ConvertFromUtf32(unicode.Value));
                    continue;
                }
            }
            // Identity ordering with no ToUnicode: reverse-map the glyph id to Unicode
            // through the embedded font program's cmap (built once per font).
            if (gidToUnicode is not null && gidToUnicode.TryGetValue(code, out var u))
            {
                sb.Append(char.ConvertFromUtf32(u));
                continue;
            }
            sb.Append((char)code);
        }
        return sb.ToString();
    }

    /// <summary>Cache entry for a font's inverted gid → Unicode map, remembering whether the
    /// inversion source was the font's own embedded program (opt-in for decoding) or the
    /// installed system face (used by default for non-embedded fonts).</summary>
    private sealed class GidToUnicodeEntry
    {
        public Dictionary<int, int> Map = new();
        public bool FromEmbedded;
    }

    // Cache of glyph-id → Unicode maps built per Type0 font dictionary so a page's
    // repeated decode calls parse the font program once.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, GidToUnicodeEntry> _gidToUnicodeCache = new();

    // Per-font-dict cache of CidFontInfo for the legacy-CMap decode branch (an entry with
    // LegacyCodepage == 0 means "not a legacy national CMap" and is cached as null).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, CidFontInfo?> _legacyCidCache = new();

    private sealed class PostNameMapEntry { public Dictionary<int, string>? Map; }

    // Cache of byte-code → Unicode maps recovered from a simple font's embedded program
    // post-table glyph names (built once per font dict). Null Map = font has no usable
    // post names / no embedded program.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, PostNameMapEntry> _postNameCache = new();

    /// <summary>
    /// Recover a byte-code → Unicode map for a simple (non-Type0) embedded TrueType font
    /// that carries neither /ToUnicode nor an /Encoding: walk the embedded program's cmap
    /// (code → glyph id) then its version-2.0 post table (glyph id → PostScript name) and
    /// resolve each name to Unicode through the Adobe Glyph List. This is how
    /// Cyrillic/Greek subset fonts (FirstChar 1, symbolic flag) that would otherwise
    /// decode as WinAnsi mojibake are read. Returns null when the font lacks embedded post names.
    /// Cached per font dictionary.
    /// </summary>
    private static Dictionary<int, string>? GetPostNameCodeToUnicode(PdfDictionary fontDict, PdfReader reader)
    {
        if (_postNameCache.TryGetValue(fontDict, out var cached)) return cached.Map;
        var entry = new PostNameMapEntry();
        try
        {
            var fd = reader.ResolveDict(fontDict.Get("FontDescriptor"));
            var ff2 = fd?.Get("FontFile2");
            var stream = ff2 is null ? null : reader.ResolveStream(ff2);
            var data = stream is not null ? reader.DecodeStream(stream) : null;
            if (data is not null)
            {
                var parser = new TrueTypeParser(data);
                parser.Parse();
                if (parser.GlyphNames.Count > 0 && parser.CMap.Count > 0)
                {
                    var map = new Dictionary<int, string>(parser.CMap.Count);
                    foreach (var kv in parser.CMap)
                    {
                        if (kv.Key > 0xFF) continue; // simple fonts use single-byte codes
                        if (parser.GlyphNames.TryGetValue(kv.Value, out var gname)
                            && ResolveGlyphName(gname) is { Length: > 0 } u)
                            map[kv.Key] = u;
                    }
                    if (map.Count > 0) entry.Map = map;
                }
            }
        }
        catch { entry.Map = null; }
        _postNameCache.AddOrUpdate(fontDict, entry);
        return entry.Map;
    }

    /// <summary>CidFontInfo for a Type0 font whose /Encoding is a predefined legacy
    /// national CMap (LegacyCodepage != 0); null for every other font. Cached per dict.</summary>
    private static CidFontInfo? GetLegacyCidInfo(PdfDictionary fontDict, PdfReader reader)
    {
        if (_legacyCidCache.TryGetValue(fontDict, out var cached)) return cached;
        CidFontInfo? info = null;
        try
        {
            var built = CidFontInfo.TryBuild(fontDict, reader);
            if (built is { LegacyCodepage: not 0 }) info = built;
        }
        catch { info = null; }
        _legacyCidCache.Add(fontDict, info);
        return info;
    }

    /// <summary>
    /// Build a glyph-id → Unicode map for an Identity-encoded Type0 font that lacks a
    /// /ToUnicode CMap, by inverting a TrueType cmap (threading the CID→GID mapping when
    /// /CIDToGIDMap is a stream). The inversion source is the embedded program when present
    /// (returned only when <paramref name="allowEmbedded"/> — raw codes are kept
    /// for embedded programs unless font-engine decoding is requested), otherwise the
    /// installed system face named by /BaseFont. Cached per font dictionary.
    /// </summary>
    private static Dictionary<int, int>? GetGidToUnicode(PdfDictionary fontDict, PdfReader reader,
        bool allowEmbedded)
    {
        if (_gidToUnicodeCache.TryGetValue(fontDict, out var cached))
            return cached.Map.Count > 0 && (allowEmbedded || !cached.FromEmbedded) ? cached.Map : null;

        var entry = new GidToUnicodeEntry();
        var map = entry.Map;
        try
        {
            var descArr = reader.Resolve(fontDict.Get("DescendantFonts")) as PdfArray;
            var descendant = descArr is { Count: > 0 } ? reader.ResolveDict(descArr[0]) : null;
            var fd = descendant is null ? null : reader.ResolveDict(descendant.Get("FontDescriptor"));
            var ff2 = fd?.Get("FontFile2") ?? fd?.Get("FontFile3");
            var stream = ff2 is null ? null : reader.ResolveStream(ff2);
            byte[]? data = stream is not null ? reader.DecodeStream(stream) : null;
            entry.FromEmbedded = data is not null;
            // A NON-embedded Identity CID font draws with the glyph ids of the real face
            // it names, so the installed system font's cmap carries the same gid → Unicode
            // relation the producer used. Resolve it as the inversion source.
            if (data is null && fontDict.GetName("BaseFont") is { } nonEmbeddedBase)
                data = SystemFontResolver.Resolve(nonEmbeddedBase);
            if (data is not null)
            {
                var parser = new TrueTypeParser(data);
                parser.Parse();
                // parser.CMap is Unicode → glyph id; invert it. When several codepoints map
                // to the SAME glyph (e.g. a hyphen glyph reachable from both U+002D
                // hyphen-minus and U+00AD soft-hyphen), prefer the SMALLEST codepoint — the
                // canonical ASCII/base character — instead of letting iteration order decide.
                // EXCEPT Arabic: shaped fonts map a contextual glyph from both its base
                // letter and its presentation form(s), and font-engine extraction reports
                // the PRESENTATION FORM (Arabic FE-block first, then FB-block Farsi/ligature
                // forms, then the base letter) in font-engine extraction.
                var gidToUni = new Dictionary<int, int>(parser.CMap.Count);
                static int ArabicRank(int c) =>
                    c is >= 0xFE70 and <= 0xFEFF ? 3
                    : c is >= 0xFB50 and <= 0xFDFF ? 2
                    : c is >= 0x0600 and <= 0x06FF ? 1
                    : 0;
                foreach (var kv in parser.CMap)
                {
                    if (!gidToUni.TryGetValue(kv.Value, out var existing))
                    {
                        gidToUni[kv.Value] = kv.Key;
                        continue;
                    }
                    var ra = ArabicRank(existing);
                    var rb = ArabicRank(kv.Key);
                    if (rb > ra || (rb == ra && kv.Key < existing))
                        gidToUni[kv.Value] = kv.Key;
                }

                // CIDToGIDMap: Identity (default) means CID == GID, so the 2-byte code is
                // already the glyph id. A stream maps CID → GID as packed big-endian uint16s.
                var c2g = descendant!.Get("CIDToGIDMap");
                var c2gStream = c2g is not null ? reader.ResolveStream(c2g) : null;
                if (c2gStream is not null)
                {
                    var cg = reader.DecodeStream(c2gStream);
                    for (int cid = 0; cid * 2 + 1 < cg.Length; cid++)
                    {
                        int gid = (cg[cid * 2] << 8) | cg[cid * 2 + 1];
                        if (gid != 0 && gidToUni.TryGetValue(gid, out var u)) map[cid] = u;
                    }
                }
                else
                {
                    foreach (var kv in gidToUni) map[kv.Key] = kv.Value;
                }
            }
        }
        catch { /* best-effort: leave the map empty so the caller falls back */ }

        _gidToUnicodeCache.AddOrUpdate(fontDict, entry);
        return map.Count > 0 && (allowEmbedded || !entry.FromEmbedded) ? map : null;
    }

    /// <summary>
    /// Resolve XObject resources by walking up the page tree hierarchy.
    /// Returns the first XObject dict found (page-level takes priority over parent).
    /// </summary>
    internal static PdfDictionary? ResolveXObjects(PdfDictionary dict, PdfReader reader)
    {
        var current = dict;
        int depth = 0;
        while (current is not null && depth < 6)
        {
            var resources = reader.ResolveDict(current.Get("Resources"));
            if (resources is not null)
            {
                var xobjs = reader.ResolveDict(resources.Get("XObject"));
                if (xobjs is not null) return xobjs;
            }
            current = reader.ResolveDict(current.Get("Parent"));
            depth++;
        }
        return null;
    }

    internal static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        CollectFontsFromHierarchy(pageDict, reader, result, depth: 0);
        return result;
    }

    /// <summary>
    /// Collect fonts by walking up the page tree, allowing parent Resources to
    /// provide fonts not defined in the page's own Resources dict.
    /// Page-level fonts override parent fonts of the same name.
    /// </summary>
    private static void CollectFontsFromHierarchy(PdfDictionary dict, PdfReader reader,
        Dictionary<string, PdfDictionary> result, int depth)
    {
        if (depth > 6) return; // guard against infinite loops

        // Walk parent first (lower priority), then overlay with this node's fonts
        var parentRef = dict.Get("Parent");
        if (parentRef is not null)
        {
            var parentDict = reader.ResolveDict(parentRef);
            if (parentDict is not null)
                CollectFontsFromHierarchy(parentDict, reader, result, depth + 1);
        }

        var resources = reader.ResolveDict(dict.Get("Resources"));
        if (resources is null) return;

        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return;

        foreach (var key in fontDict.Keys)
        {
            var font = reader.ResolveDict(fontDict.Get(key));
            if (font is not null)
                result[key] = font; // page-level overrides parent
        }
    }

    /// <summary>Whether the /Resources/Font hierarchy CONTAINS an entry under
    /// <paramref name="key"/>, resolvable or not. A key that is present but whose
    /// target cannot be resolved in-memory (a just-registered replacement font) is
    /// NOT "absent from page Resources" — callers treat that differently from a
    /// genuinely missing key.</summary>
    internal static bool FontResourceKeyExists(PdfDictionary dict, PdfReader reader, string key, int depth = 0)
    {
        if (depth > 6) return false;
        var parentRef = dict.Get("Parent");
        if (parentRef is not null && reader.ResolveDict(parentRef) is { } parentDict
            && FontResourceKeyExists(parentDict, reader, key, depth + 1))
            return true;
        var resources = reader.ResolveDict(dict.Get("Resources"));
        var fontDict = resources is null ? null : reader.ResolveDict(resources.Get("Font"));
        return fontDict?.Get(key) is not null;
    }

    internal static Dictionary<int, string>? ParseToUnicodeFromDict(PdfDictionary fontDict, PdfReader reader) =>
        ParseToUnicode(fontDict, reader);

    /// <summary>For diagnostics only: expose ParseCMap publicly.</summary>
    internal static Dictionary<int, string> ParseCMapPublic(string cmapText) => ParseCMap(cmapText);

    private static Dictionary<int, string>? ParseToUnicode(PdfDictionary fontDict, PdfReader reader)
    {
        var toUnicodeObj = fontDict.Get("ToUnicode");
        if (toUnicodeObj is null) return null;

        var stream = reader.ResolveStream(toUnicodeObj);
        if (stream is null) return null;

        var decoded = reader.DecodeStream(stream);
        var text = Encoding.ASCII.GetString(decoded);

        return ParseCMap(text);
    }

    internal static Dictionary<int, string> ParseCMap(string cmapText)
    {
        var map = new Dictionary<int, string>();
        // Normalize: ensure section markers are on their own lines.
        // This handles CMaps where all content is on a single line (space-separated).
        cmapText = Regex.Replace(cmapText,
            @"(begin|end)(bfchar|bfrange)",
            "\n$1$2\n",
            RegexOptions.IgnoreCase);
        var lines = cmapText.Split('\n');

        var inBfChar = false;
        var inBfRange = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.Contains("beginbfchar", StringComparison.Ordinal))
            {
                inBfChar = true;
                continue;
            }
            if (line.Contains("endbfchar", StringComparison.Ordinal))
            {
                inBfChar = false;
                continue;
            }
            if (line.Contains("beginbfrange", StringComparison.Ordinal))
            {
                inBfRange = true;
                continue;
            }
            if (line.Contains("endbfrange", StringComparison.Ordinal))
            {
                inBfRange = false;
                continue;
            }

            if (inBfChar)
            {
                // A line may contain multiple pairs: <code> <unicode> <code> <unicode> .
                var tokens = ExtractHexTokens(line);
                for (var k = 0; k + 1 < tokens.Count; k += 2)
                {
                    var code = ParseHexInt(tokens[k]);
                    var unicode = HexToString(tokens[k + 1]);
                    map[code] = unicode;
                }
            }
            else if (inBfRange)
            {
                var tokens = ExtractHexTokens(line);
                if (tokens.Count >= 3)
                {
                    // Check if line contains array form: <start> <end> [<d0> <d1> .]
                    var arrayStart = line.IndexOf('[');
                    if (arrayStart >= 0)
                    {
                        var start = ParseHexInt(tokens[0]);
                        var end = ParseHexInt(tokens[1]);
                        // Array form: each code maps to successive array entries
                        var arrayTokens = tokens.Skip(2).ToList(); // tokens from inside array
                        for (var code = start; code <= end; code++)
                        {
                            var idx = code - start;
                            if (idx < arrayTokens.Count)
                                map[code] = HexToString(arrayTokens[idx]);
                        }
                    }
                    else
                    {
                        // Sequential form: start code maps to startUnicode, next codes
                        // increment. A one-line CMap packs EVERY range onto this line
                        // (<00> <00> <fffd><01> <01> <00ad>…), so consume triples, not
                        // just the first. The destination is UTF-16BE and may be a
                        // surrogate pair (plane-1 math alphanumerics, emoji):
                        // <16> <49> <D835DC34>. Decode it to codepoints first — parsing
                        // 8 hex digits as one integer lands above 0x10FFFF and would
                        // drop the whole range.
                        for (var k = 0; k + 2 < tokens.Count; k += 3)
                        {
                            var start = ParseHexInt(tokens[k]);
                            var end = ParseHexInt(tokens[k + 1]);
                            var destStr = HexToString(tokens[k + 2]);
                            if (destStr.Length == 0) continue;
                            // The LAST codepoint of the destination carries the increment;
                            // any preceding codepoints (multi-char ligature dest) are a
                            // constant prefix.
                            var lastCpStart = destStr.Length >= 2 && char.IsSurrogatePair(destStr[^2], destStr[^1])
                                ? destStr.Length - 2 : destStr.Length - 1;
                            // A malformed CMap can leave an UNPAIRED surrogate here (a
                            // 4-digit dest like <D835> survives via HexToString's raw
                            // fallback); ConvertToUtf32 would throw — drop the range like
                            // the pre-surrogate parser did.
                            if (char.IsSurrogate(destStr[lastCpStart])
                                && !(destStr.Length - lastCpStart == 2
                                     && char.IsSurrogatePair(destStr[lastCpStart], destStr[lastCpStart + 1])))
                                continue;
                            var prefix = destStr[..lastCpStart];
                            var lastCp = char.ConvertToUtf32(destStr, lastCpStart);
                            for (var code = start; code <= end; code++)
                            {
                                var cp = lastCp + (code - start);
                                if (cp is >= 0xD800 and <= 0xDFFF || cp > 0x10FFFF)
                                    continue; // skip invalid surrogate codepoints
                                map[code] = prefix.Length == 0 ? char.ConvertFromUtf32(cp) : prefix + char.ConvertFromUtf32(cp);
                            }
                        }
                    }
                }
            }
        }

        return map;
    }

    private static List<string> ExtractHexTokens(string line)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] == '<')
            {
                var end = line.IndexOf('>', i);
                if (end > i)
                {
                    tokens.Add(line[(i + 1)..end].Replace(" ", ""));
                    i = end + 1;
                    continue;
                }
            }
            i++;
        }
        return tokens;
    }

    private static int ParseHexInt(string hex)
    {
        if (long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var val))
            return val > int.MaxValue ? 0 : (int)val;
        return 0;
    }

    private static string HexToString(string hex)
    {
        var sb = new StringBuilder();
        for (var i = 0; i + 3 < hex.Length; i += 4)
        {
            var codePoint = ParseHexInt(hex[i..(i + 4)]);
            // UTF-16BE surrogate pair (emoji / CJK Ext-B): combine with the next unit.
            if (codePoint is >= 0xD800 and <= 0xDBFF && i + 7 < hex.Length)
            {
                var low = ParseHexInt(hex[(i + 4)..(i + 8)]);
                if (low is >= 0xDC00 and <= 0xDFFF)
                {
                    sb.Append(char.ConvertFromUtf32(char.ConvertToUtf32((char)codePoint, (char)low)));
                    i += 4;
                    continue;
                }
            }
            if (codePoint is >= 0xD800 and <= 0xDFFF || codePoint > 0x10FFFF)
                continue; // skip unpaired surrogate units
            sb.Append(char.ConvertFromUtf32(codePoint));
        }
        if (sb.Length == 0 && hex.Length >= 2)
        {
            // 2-digit hex = single byte
            sb.Append((char)ParseHexInt(hex));
        }
        return CollapseTwoCharLigature(sb.ToString());
    }

    /// <summary>A single glyph mapped (via ToUnicode or a marked-content
    /// /ActualText) to a TWO-letter ligature decomposition surfaces as the
    /// ligature codepoint — a "fi"-mapped glyph and an ActualText("fi") span both
    /// surface as U+FB01. A THREE-letter decomposition
    /// stays as its letters (e.g. "Effizent" stays searchable, not
    /// E+U+FB03+zent).</summary>
    private static string CollapseTwoCharLigature(string result) => result switch
    {
        "fi" => "ﬁ",
        "fl" => "ﬂ",
        "ff" => "ﬀ",
        _ => result,
    };

    /// <summary>
    /// Decode a PDF text string (handles BOM for UTF-16BE, otherwise Latin1).
    /// </summary>
    private static string DecodeTextString(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        return Encoding.Latin1.GetString(bytes);
    }

    private static PdfDictionary ParseContentDict(PdfLexer lexer)
    {
        var dict = new PdfDictionary();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.DictEnd || t.Kind == TokenKind.Eof) break;
            if (t.Kind != TokenKind.Name) continue;
            var key = t.StringValue!;
            var val = lexer.NextToken();
            if (val.Kind == TokenKind.DictEnd) break;
            PdfObject value = val.Kind switch
            {
                TokenKind.Integer => new PdfInteger(val.IntValue),
                TokenKind.Real => new PdfReal(val.RealValue),
                TokenKind.Name => new PdfName(val.StringValue!),
                TokenKind.LiteralString => new PdfString(val.BytesValue!),
                TokenKind.HexString => new PdfString(val.BytesValue!, isHex: true),
                TokenKind.Boolean => val.BoolValue ? PdfBoolean.True : PdfBoolean.False,
                _ => PdfNull.Instance,
            };
            dict.Set(key, value);
        }
        return dict;
    }

    private static PdfArray ParseContentArray(PdfLexer lexer)
    {
        var array = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind)
            {
                case TokenKind.Integer:
                    array.Add(new PdfInteger(t.IntValue));
                    break;
                case TokenKind.Real:
                    array.Add(new PdfReal(t.RealValue));
                    break;
                case TokenKind.LiteralString:
                    array.Add(new PdfString(t.BytesValue!));
                    break;
                case TokenKind.HexString:
                    array.Add(new PdfString(t.BytesValue!, isHex: true));
                    break;
                case TokenKind.Name:
                    array.Add(new PdfName(t.StringValue!));
                    break;
            }
        }
        return array;
    }

    // Compute number of spaces to emit for an inter-run gap.
    // Raw mode always emits at most 1 space (no visual formatting reconstruction).
    // Pure mode emits proportional spaces so column layout is preserved:
    //   count ≈ round(gap / spaceWidth), where spaceWidth is the typical space glyph width
    //   (~0.25 * fontSize for most Latin fonts). Clamped to avoid runaway widths.
    // Returns 0 when gap is below the threshold (no space should be inserted).
    /// <summary>
    /// Keep the Pure-mode grid origin current: find the start of the line being built
    /// (text after the last newline) and, when it changes, reset the grid so the first run
    /// of the new line anchors column 0. Called before spacing so <see cref="ColumnSpaces"/>
    /// measures from the correct line origin.
    /// </summary>
    private void TrackLineStart(double runPageX, bool whitespaceOnly = false)
    {
        int ls = _text.Length;
        while (ls > 0 && _text[ls - 1] != '\n') ls--;
        if (ls != _lineStartTextOffset) { _lineStartTextOffset = ls; _lineStartPageX = double.NaN; }
        // Whitespace-only runs are grid citizens like any other: they anchor
        // the line and their glyphs fill their own columns (the leading-space
        // extraChars rule keeps the column accounting consistent).
        // Pure-pad lines are emitted for them.
        if (double.IsNaN(_lineStartPageX))
        {
            _lineStartPageX = runPageX;
            // Remember every line's start offset + X so the page pass can pad
            // leading grid columns from the page-absolute origin (minX).
            _pageLineStarts.Add((ls, runPageX));
        }
        else if (runPageX < _lineStartPageX)
        {
            // The line's leading column reflects its LEFTMOST run: streams often
            // draw a row's trailing space fragment (far right) before the row
            // text, and anchoring on that first-seen X would pad wildly.
            _lineStartPageX = runPageX;
            for (var i = _pageLineStarts.Count - 1; i >= 0; i--)
            {
                if (_pageLineStarts[i].offset != ls) continue;
                _pageLineStarts[i] = (ls, runPageX);
                break;
            }
        }
    }

    // Per-page (offset, x) of each output line's first tracked run — the input to
    // the leading-column padding pass. Reset in Visit().
    private readonly List<(int offset, double x)> _pageLineStarts = new();

    // Per-page span of every appended show-run: [Offset, Offset+Len) in _text, the
    // run's page-space start X and rendered width, whether ApplyRtlIfPureRtl reversed
    // it at decode time, and (when the code↔char mapping is 1:1) each character's
    // page-space X offset from the run start, in CODE (visual) order. Input to the
    // RTL row re-assembly, which merges rows per character by X.
    private readonly record struct RunSpan(int Offset, int Len, double X, double Width, bool Reversed, double[]? CharXs);

    private readonly List<RunSpan> _pageRunSpans = new();

    /// <summary>Per-code start offsets (page units, relative to the run's X) for a show
    /// string, in code order. Null when no metrics are available or the decoded length
    /// differs from the code count (ligatures, multi-char mappings, clipped runs).</summary>
    private static double[]? BuildCharXs(byte[] bytes, FontMetrics? metrics, double fontSize,
        double scale, int decodedLen)
    {
        if (metrics is null) return null;
        var codeCount = metrics.IsCid ? bytes.Length / 2 : bytes.Length;
        if (codeCount != decodedLen || codeCount == 0) return null;
        var rel = new double[codeCount];
        double pen = 0;
        if (metrics.IsCid)
        {
            for (int i = 0, k = 0; i + 1 < bytes.Length; i += 2, k++)
            {
                rel[k] = pen * scale;
                pen += metrics.GetWidth((bytes[i] << 8) | bytes[i + 1]) * fontSize / 1000.0;
            }
        }
        else
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                rel[i] = pen * scale;
                pen += metrics.GetWidth(bytes[i]) * fontSize / 1000.0;
            }
        }
        return rel;
    }

    // Page grid origin (leftmost text X) from the pre-scan; NaN when unknown.
    private double _pageMinX = double.NaN;

    // TextSearchOptions.Rectangle mapped from viewer to media coordinates for the
    // page being visited (equal to the raw rectangle on an unrotated page).
    private Rectangle? _effectiveSearchRect;

    /// <summary>Map a viewer-space rectangle to media space by undoing the page's
    /// /Rotate. Viewer space for /Rotate 90 shows the media rotated clockwise, so
    /// (xv, yv) → (W − yv, xv) etc., with W/H the media box width/height.</summary>
    private static Rectangle? MapViewerRectToMedia(Rectangle? rect, Page page)
    {
        if (rect is null) return null;
        var rotate = ((page.RotateDegrees % 360) + 360) % 360;
        if (rotate == 0) return rect;
        var mb = page.MediaBox;
        var w = mb?.Width ?? 612;
        var h = mb?.Height ?? 792;
        double llx, lly, urx, ury;
        switch (rotate)
        {
            case 90:
                llx = w - rect.URY; lly = rect.LLX;
                urx = w - rect.LLY; ury = rect.URX;
                break;
            case 180:
                llx = w - rect.URX; lly = h - rect.URY;
                urx = w - rect.LLX; ury = h - rect.LLY;
                break;
            case 270:
                llx = rect.LLY; lly = h - rect.URX;
                urx = rect.URY; ury = h - rect.LLX;
                break;
            default:
                return rect;
        }
        return new Rectangle(llx, lly, urx, ury);
    }

    /// <summary>
    /// Pure-mode leading columns: each page lays out on a character
    /// grid anchored at the page's leftmost text X, so a line starting to the
    /// right of that origin gets round((x − minX) / cell) leading spaces.
    /// Runs after the page streams (before line sorting), inserting from the
    /// last line backwards so recorded offsets stay valid.
    /// </summary>
    /// <summary>Absolute grid column of a page X: the grid is anchored at
    /// the page's left edge (MediaBox LLX; x = 0 for ordinary pages) with
    /// lines every cell width, quantised by floor:
    /// pad = floor(X/cell) - floor(minX/cell); no phase, no dx division.</summary>
    // Grid column of a CONTENT position. Cells are LEFT-OPEN intervals
    // (k·cell, (k+1)·cell]: a CAD-snapped x sitting exactly on a boundary
    // belongs to the cell on its left. The epsilon eats
    // matrix-composition float dirt that pushes an exact multiple a hair above
    // the boundary; any real sub-cell offset dwarfs it. The TRIM variant
    // (GridColTrim) uses a plain floor so a line whose start sits exactly on a
    // boundary keeps that boundary as its zero (else the whole line shifts).
    private int GridCol(double x) => (int)Math.Floor((x - _pageGridOriginX) / _pageCellWidth - 1e-9);

    private int GridColTrim(double x) => (int)Math.Floor((x - _pageGridOriginX) / _pageCellWidth);

    // Grid column of a LINE START: text left of x = 0 (invisible zone markers,
    // shifted-MediaBox title blocks) reads as column −1 regardless of magnitude
    // (x = −3 and x = −300 both land one column left of the
    // grid). Such a line zeroes the trim at −1, shifting the whole page right
    // by one column.
    private int LineStartGridCol(double x) => x < 0 ? -1 : GridCol(x);

    private int LineStartGridColTrim(double x) => x < 0 ? -1 : GridColTrim(x);

    private void InsertLeadingGridSpaces(int pageTextStart)
    {
        if (_pageCellWidth <= 0 || _pageLineStarts.Count == 0) return;
        // The grid trim is the OBSERVED minimum over the page's emitted lines
        // (the grid is trimmed by the smallest leading-space count of
        // any produced line). The pre-scan minX can sit left of every emitted
        // line — clipped or invisible runs that never extract — and trimming
        // by it pads phantom leading columns onto the whole page.
        var minX = double.MaxValue;
        foreach (var (off, x) in _pageLineStarts)
            if (off >= pageTextStart && x < minX) minX = x;
        if (minX == double.MaxValue) return;
        // Keep the merge/target math on the same trim.
        _pageMinX = minX;


        if (GridDebug)
        {
            Console.Error.WriteLine($"[grid] cell={_pageCellWidth:R} minX={minX:F2} lines={_pageLineStarts.Count} rotDom={_pageRotDominant}");
            foreach (var (off, x) in _pageLineStarts)
            {
                var end = Math.Min(_text.Length, off + 30);
                var snippet = _text.ToString(off, Math.Max(0, end - off)).Replace("\r", "").Replace("\n", "");
                Console.Error.WriteLine($"[grid]   off={off} x={x:F2} n={GridCol(x) - GridCol(minX)} '{snippet}'");
            }
        }

        for (var i = _pageLineStarts.Count - 1; i >= 0; i--)
        {
            var (off, x) = _pageLineStarts[i];
            if (off < pageTextStart || off > _text.Length) continue;
            // Absolute grid: pad = floor(x/cell) − floor(minX/cell)
            // — boundaries at k·cell anchored at the page's left edge, floor
            // quantisation, no rounding phase (see the puremode-grid spec note).
            var n = LineStartGridCol(x) - LineStartGridColTrim(minX);
            if (n <= 0) continue;
            if (n > 5000) n = 5000; // grid bound (_maxCols)
            _text.Insert(off, new string(' ', n));
            // Keep the recorded offsets valid — SortLinesByY maps them back to
            // lines for the grid-aware same-row merge.
            for (var j = 0; j < _pageLineStarts.Count; j++)
                if (_pageLineStarts[j].offset > off)
                    _pageLineStarts[j] = (_pageLineStarts[j].offset + n, _pageLineStarts[j].x);
            // Run spans shift too; a span starting AT the insertion point moves right
            // (the pad is inserted before it).
            for (var j = 0; j < _pageRunSpans.Count; j++)
                if (_pageRunSpans[j].Offset >= off)
                    _pageRunSpans[j] = _pageRunSpans[j] with { Offset = _pageRunSpans[j].Offset + n };
        }
    }

    /// <summary>
    /// Number of spaces to pad before a run under the Pure-mode character grid: the run is
    /// placed at absolute column round((runPageX − lineStartX) / cellWidth), and we pad from
    /// the number of characters already emitted on the line. A real gap always yields at
    /// least one space; below the word-gap threshold the run is adjacent (no space).
    /// </summary>
    /// <summary>True when the current output line already carries fullwidth
    /// (CJK) glyphs — the one case where the emitted character count falls
    /// behind the device column (each glyph covers ~2 grid cells).</summary>
    /// <summary>True when the current output tail (last ~8 non-space chars)
    /// carries an RTL character — the same lookback the TJ backjump rule uses.</summary>
    private bool RecentTextIsRtl()
    {
        var seen = 0;
        for (var i = _text.Length - 1; i >= 0 && seen < 8; i--)
        {
            var c = _text[i];
            if (c == '\n' || c == '\r') return false;
            if (c == ' ') continue;
            if (BidiReorderer.IsRtlChar(c)) return true;
            seen++;
        }
        return false;
    }

    private bool LineHasWideGlyphs()
    {
        for (var i = Math.Max(0, _lineStartTextOffset); i < _text.Length; i++)
        {
            var c = _text[i];
            if (c >= 0x1100 && (c <= 0x115F
                || (c >= 0x2E80 && c <= 0xA4CF)
                || (c >= 0xAC00 && c <= 0xD7A3)
                || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0xFE30 && c <= 0xFE4F)
                || (c >= 0xFF00 && c <= 0xFF60)))
                return true;
        }
        return false;
    }

    private int ColumnSpaces(double gap, double threshold, double runPageX, int extraChars = 0)
    {
        if (gap <= threshold) return 0;
        int targetCol, outputCol;
        if (!double.IsNaN(_pageMinX))
        {
            // Page-absolute grid, quantised by floor against boundaries at k·cell
            // (col = floor(x/cell) − floor(minX/cell), no
            // rounding phase) — the same mapping the leading-pad insertion uses,
            // so target and output columns share one frame.
            targetCol = LineStartGridCol(runPageX) - LineStartGridColTrim(_pageMinX);
            var leadCols = LineStartGridCol(_lineStartPageX) - LineStartGridColTrim(_pageMinX);
            if (leadCols < 0) leadCols = 0;
            outputCol = leadCols + (_text.Length - _lineStartTextOffset) + extraChars;
        }
        else
        {
            targetCol = (int)Math.Round((runPageX - _lineStartPageX) / _pageCellWidth);
            outputCol = _text.Length - _lineStartTextOffset + extraChars;
        }
        if (GridDebug)
            Console.Error.WriteLine($"[cols] target={targetCol} output={outputCol} cell={_pageCellWidth:R} runPageX={runPageX:F1} lineStartX={_lineStartPageX:F1} minX={_pageMinX:F1}");
        int spaces = targetCol - outputCol;
        if (spaces < 1) spaces = 1;
        if (spaces > 5000) spaces = 5000; // grid bound (_maxCols)
        return spaces;
    }

    private int ComputeSpaceCount(double gap, double threshold, double fontSize)
    {
        // MemorySaving: ONE separator space per run boundary
        // whose pen jumped — forward column gap or BACKWARD overlap alike (a table
        // cell's unclipped text overruns its neighbour, and the next cell still
        // reads as a separate word).
        if (ExtractionOptions?.FormattingMode == TextExtractionOptions.TextFormattingMode.MemorySaving)
            return Math.Abs(gap) > threshold ? 1 : 0;
        if (gap <= threshold) return 0;
        if (ExtractionOptions?.FormattingMode != TextExtractionOptions.TextFormattingMode.Raw)
        {
            // Pure mode: one space per ~0.217 * fontSize of gap width
            // (the Pure-mode column-spacing rule).
            var spaceWidth = Math.Max(fontSize * 0.217, 0.5);
            var count = (int)Math.Round(gap / spaceWidth);
            if (count < 1) count = 1;
            if (count > 40) count = 40;
            return count;
        }
        return 1;
    }

    private static List<byte[]> GetContentStreams(Page page, PdfReader reader)
    {
        var result = new List<byte[]>();
        var contentsObj = reader.Resolve(page.Dict.Get("Contents"));

        if (contentsObj is PdfStream stream)
        {
            result.Add(reader.DecodeStream(stream));
        }
        else if (contentsObj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                    result.Add(reader.DecodeStream(s));
            }
        }

        return result;
    }

    private static PdfReader GetReader(Page page) => page.Reader;

    /// <summary>
    /// Skip inline image data (BI . ID &lt;data&gt; EI) per PDF spec §8.9.7.
    /// </summary>
    internal static void SkipInlineImage(PdfLexer lexer)
    {
        // Consume tokens until the ID keyword (image data start), capturing the
        // dictionary keys needed to size the data.
        int imgW = 0, imgH = 0, imgBpc = 8, imgColors = 1; bool imgFlate = false;
        string? key = null, firstFilter = null;
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.Keyword && t.StringValue == "ID") break;
            if (t.Kind == TokenKind.Name)
            {
                var n = t.StringValue!;
                if (key is "F" or "Filter" && firstFilter is null) firstFilter = n;
                switch (n)
                {
                    case "RGB": case "DeviceRGB": if (key is "CS" or "ColorSpace") imgColors = 3; break;
                    case "CMYK": case "DeviceCMYK": if (key is "CS" or "ColorSpace") imgColors = 4; break;
                    case "G": case "DeviceGray": if (key is "CS" or "ColorSpace") imgColors = 1; break;
                    case "Fl": case "FlateDecode": if (key is "F" or "Filter") imgFlate = true; break;
                }
                key = n;
            }
            else if (t.Kind == TokenKind.Integer)
            {
                int v = (int)t.IntValue;
                switch (key) { case "W": case "Width": imgW = v; break; case "H": case "Height": imgH = v; break;
                    case "BPC": case "BitsPerComponent": imgBpc = v; break; case "Colors": imgColors = v; break; }
                key = null;
            }
            // A filter array (/F [/A85 /Fl]) keeps the key alive so its first element
            // is still attributed to F/Filter.
            else if (t.Kind != TokenKind.ArrayStart) key = null;
        }

        long dataStart0 = lexer.Position + 1; // one whitespace byte after ID
        long lenAll = lexer.Length;

        // ASCII85/ASCIIHex data self-terminates with an explicit EOD marker ("~>" / ">").
        // Locate it directly: such data is printable text where 'E','I' are ordinary
        // digits and line breaks supply whitespace, so the "EI" byte scan below finds
        // false terminators inside the payload and desyncs the lexer into image bytes.
        if (firstFilter is "A85" or "ASCII85Decode" or "AHx" or "ASCIIHexDecode")
        {
            bool a85 = firstFilter is "A85" or "ASCII85Decode";
            byte eod = a85 ? (byte)'~' : (byte)'>';
            for (long p = dataStart0; p < lenAll; p++)
            {
                if (lexer.ByteAt(p) != eod) continue;
                if (a85 && (p + 1 >= lenAll || lexer.ByteAt(p + 1) != (byte)'>')) continue;
                long q = p + (a85 ? 2 : 1);
                while (q < lenAll && IsWhitespace(lexer.ByteAt(q))) q++;
                if (q + 1 < lenAll && lexer.ByteAt(q) == (byte)'E' && lexer.ByteAt(q + 1) == (byte)'I')
                    q += 2;
                lexer.Position = q;
                return;
            }
        }

        // Preferred for Flate-compressed data: probe each whitespace-delimited "EI"
        // candidate by inflating ID..candidate; the real EI is the earliest position
        // whose data inflates to the full raw image size. A stray "EI" byte pair inside
        // the compressed stream truncates the deflate stream → inflate fails, so it's
        // skipped. This stops the lexer desyncing and dropping every operator after the
        // image (nested-table grid lines were all lost after an inline image).
        if (imgFlate && imgW > 0 && imgH > 0)
        {
            int bytesPerRow = (imgW * imgColors * imgBpc + 7) / 8;
            int expected = imgH * bytesPerRow; // lower bound (a row predictor only adds bytes)
            int tailLen = (int)Math.Max(0, lenAll - dataStart0);
            var tail = new byte[tailLen];
            for (int i = 0; i < tailLen; i++) tail[i] = lexer.ByteAt(dataStart0 + i);
            for (int p = 1; p < tailLen - 1; p++)
            {
                if (tail[p] != (byte)'E' || tail[p + 1] != (byte)'I') continue;
                if (p + 2 < tailLen && !IsWhitespace(tail[p + 2])) continue;
                var slice = new byte[p];
                Array.Copy(tail, 0, slice, 0, p);
                try
                {
                    var inflated = Aspose.Pdf.IO.Filters.FlateDecodeFilter.Decode(slice, null);
                    if (inflated.Length >= expected) { lexer.Position = dataStart0 + p + 2; return; }
                }
                catch { /* truncated deflate at this candidate — keep scanning */ }
            }
        }

        // After ID, spec mandates one whitespace byte before raw data.
        // Scan raw bytes for 'E' 'I' followed by whitespace/EOF.
        // Many real-world PDFs don't have whitespace BEFORE "EI" (the image data
        // ends immediately before the E), so we check both patterns:
        //   1. Standard: whitespace + EI + whitespace (spec-compliant)
        //   2. Relaxed: any-byte + EI + whitespace (common in practice)
        var pos = lexer.Position + 1; // skip the whitespace byte after ID
        var len = lexer.Length;

        while (pos < len - 1)
        {
            if (lexer.ByteAt(pos) == (byte)'E' && lexer.ByteAt(pos + 1) == (byte)'I')
            {
                var after = pos + 2;
                if (after >= len || IsWhitespace(lexer.ByteAt(after)))
                {
                    // Verify this is the real EI by checking that what follows
                    // looks like valid PDF operators (not random image data).
                    // A valid operator context after EI would be: Q, BT, numbers, /, etc.
                    if (after < len)
                    {
                        // Skip whitespace after EI
                        var checkPos = after;
                        while (checkPos < len && IsWhitespace(lexer.ByteAt(checkPos)))
                            checkPos++;
                        if (checkPos < len)
                        {
                            var nextByte = lexer.ByteAt(checkPos);
                            // Valid PDF operator starts: letter, number, /, (, <, [
                            bool looksValid = (nextByte >= (byte)'A' && nextByte <= (byte)'Z')
                                || (nextByte >= (byte)'a' && nextByte <= (byte)'z')
                                || (nextByte >= (byte)'0' && nextByte <= (byte)'9')
                                || nextByte == (byte)'/' || nextByte == (byte)'('
                                || nextByte == (byte)'<' || nextByte == (byte)'['
                                || nextByte == (byte)'-' || nextByte == (byte)'.';
                            if (!looksValid) { pos++; continue; }
                        }
                    }
                    lexer.Position = after;
                    return;
                }
            }
            pos++;
        }
        lexer.Position = len; // consume everything if EI not found

        static bool IsWhitespace(byte b) =>
            b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;
    }

    private static double GetNumber(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };
}
