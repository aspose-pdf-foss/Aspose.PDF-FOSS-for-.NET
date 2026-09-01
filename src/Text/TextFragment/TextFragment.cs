namespace Aspose.Pdf.Text;

/// <summary>
/// Represents a fragment of text on a page with position and style information.
/// </summary>
public partial class TextFragment : BaseParagraph
{
    private string _text;
    private readonly TextSegmentCollection _segments;

    /// <summary>
    /// Create a text fragment with tab stops. Text is set via Segments.
    /// </summary>
    /// <summary>Create an empty text fragment.</summary>
    public TextFragment() : this("") { }

    public TextFragment(TabStops tabStops) : this("")
    {
        TabStops = tabStops;
    }

    /// <summary>Create a text fragment with the given <paramref name="text"/>.
    /// Standalone single-arg ctor (kept for reflection-based callers).</summary>
    public TextFragment(string text) : this(text, rectangle: null, textState: null) { }

    /// <summary>Create a text fragment with the given <paramref name="text"/>
    /// and <paramref name="tabStops"/> resolution table.</summary>
    public TextFragment(string text, TabStops tabStops) : this(text, rectangle: null, textState: null)
    {
        TabStops = tabStops;
    }

    // DataWorks form-grid input boxes for this fragment's InlineInputChar
    // markers, in document order (W/H in points, the value typeset inside).
    internal System.Collections.Generic.List<(double W, double H, string Value, bool Mono, double Lift)>? InlineInputBoxes;

    /// <summary>Span-scoped colour runs inside this line (DataWorks control
    /// cells: the red validation star beside black captions).</summary>
    internal System.Collections.Generic.List<(int S, int L, Color C)>? HtmlColorRuns;
    // Redline cell decorations for this fragment's lines (kind per
    // HtmlToPdfConverter.Block.DecorRuns; C = the marker border's colour).
    internal System.Collections.Generic.List<(int Kind, Color? C)>? HtmlDecors;

    // Inline <a href> runs inside an HTML table cell: each entry is the anchor's
    // (collapsed) inner text and target URL. The table renderer resolves each run
    // against its laid-out line and emits a Link annotation over just that run.
    internal System.Collections.Generic.List<(string Text, string Url)>? HtmlAnchors;

    // Form-grid line whose bold state toggles mid-run ('Owner Team: <b>bv
    // Designers</b>'): the segments in order, each drawn in its own face variant
    // by the table renderer. Null for uniformly-styled lines.
    internal System.Collections.Generic.List<(string Text, bool Bold)>? FormGridRuns;

    private TextEditOptions? _textEditOptions;

    /// <summary>Edit options applied during text replacement / font substitution.</summary>
    public TextEditOptions TextEditOptions
    {
        get => _textEditOptions ??= new TextEditOptions(TextEditOptions.LanguageTransformation.Default);
        set => _textEditOptions = value;
    }

    /// <summary>Family the in-flight explicit-ReplaceFonts redress pins for the CID
    /// fallback (see <see cref="RedressAfterExplicitFontAssignment"/>); null outside it.</summary>
    private string? _pendingCidRedressFamily;

    public new VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Top;

    public TextFragment(string text, Rectangle? rectangle = null, TextState? textState = null)
    {
        _text = text;
        Rectangle = rectangle;
        if (rectangle is not null)
            AbsorbedRectangle = new Rectangle(rectangle.LLX, rectangle.LLY, rectangle.URX, rectangle.URY);
        TextState = new TextFragmentState(this);
        if (textState is not null) TextState.ApplyChangesFrom(textState);
        _segments = new TextSegmentCollection { Owner = this };
        var seg = new TextSegment(text);
        // Quiet copy: inheriting the fragment's size must not read as an
        // explicit caller size on the segment (FontSizeTouched stays false).
        seg.TextState.SetFontSizeQuiet(TextState.FontSize);
        seg.TextState.FontName = TextState.FontName;
        seg.TextState.CharacterSpacing = TextState.CharacterSpacing;
        seg.TextState.WordSpacing = TextState.WordSpacing;
        seg.TextState.HorizontalScaling = TextState.HorizontalScaling;
        seg.TextState.IsBold = TextState.IsBold;
        seg.TextState.IsItalic = TextState.IsItalic;
        seg.TextState.RenderingMode = TextState.RenderingMode;
        seg.TextState.Font = TextState.Font;
        seg.Owner = this;
        _segments.Add(seg);
    }

    /// <summary>
    /// The text content. Setting this property replaces the text in the PDF content stream
    /// when a source page reference is available (i.e. when the fragment was obtained from
    /// a TextFragmentAbsorber).
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            // Identical text is normally a no-op, but a post-replace reflow
            // (ReplaceOptions.Rectangle set) can still need to run — e.g.
            // ScaleToFill re-fits the SAME text to a resized rectangle.
            if (string.Equals(_text, value, StringComparison.Ordinal)
                && _replaceOptions?.Rectangle is null)
                return;

            // NoCharacterAction.ThrowException (opt-in): reject replacement text the
            // fragment's font can't represent, before mutating anything.
            ThrowIfFontLacksGlyph(value);

            // ★ What is being replaced in the CONTENT STREAM is the text as DRAWN, which
            // for an RTL fragment is not what Text reports: Text is the reading order, the
            // stream holds the glyph (visual) order. The segments are per-glyph-run, so
            // their join IS the drawn order — take it, or an RTL replacement searches the
            // stream for a string that was never written to it and silently does nothing.
            var oldText = DrawnOrderText;

            // Post-replace reflow: when a target rectangle is supplied via
            // ReplaceOptions.Rectangle, re-wrap the replacement text into that
            // rectangle (with optional font-size fit) instead of doing an
            // in-place operator swap. Only for page-level, non-CID fragments —
            // CID/Type0 (CJK/Arabic) reflow needs shaping the layout engine
            // doesn't model. TryReflowIntoRectangle sets _text/_segments/_rectangle.
            if (_replaceOptions?.Rectangle is { } reflowRect && SourcePage is not null
                && TextState.Font is not null && TextState.FontSize > 0
                && TryReflowIntoRectangle(oldText, value, reflowRect))
            {
                return;
            }

            // WholeWordsHyphenation: re-wrap the whole CONTAINING paragraph so text flows up
            // to close the gap the (usually shorter) replacement leaves, giving a
            // continuous re-flow. Done once per paragraph — the first fragment whose search
            // text is found reflows every occurrence in the paragraph; sibling fragments then
            // find nothing to replace and no-op.
            if (SourcePage is not null && TextState.Font is not null && TextState.FontSize > 0
                && _replaceOptions?.ReplaceAdjustmentAction == TextReplaceOptions.ReplaceAdjustment.WholeWordsHyphenation
                && TryReflowParagraph(SourcePage, oldText, value))
            {
                _text = value;
                return;
            }

            // IsFormFillingMode: an over-wide replacement word-wraps INTO the matched
            // fragment's own rectangle — left-aligned at the fragment's LLX, first
            // baseline kept, following lines stepping 1.2·fs down, font size unchanged.
            // Non-final wrapped lines keep their trailing separator space so
            // re-extraction reassembles the exact replacement string. Replacements
            // that fit on one line stay on the ordinary in-place path.
            if (SourcePage is not null && TextState.Font is not null && TextState.FontSize > 0
                && _replaceOptions?.ReplaceAdjustmentAction == TextReplaceOptions.ReplaceAdjustment.IsFormFillingMode
                && TryFormFillWrap(SourcePage, oldText, value))
            {
                _text = value;
                return;
            }

            _text = value;

            // Capture ClippingPathsProcessing.Expand BEFORE segments are reset below —
            // the option is set per segment by callers replacing text inside
            // cell-clipped table layouts.
            var expandClips =
                _textEditOptions?.ClippingPathsProcessing == TextEditOptions.ClippingPathsProcessingMode.Expand;
            if (!expandClips)
                foreach (var s0 in _segments)
                    if (s0.TextEditOptions.ClippingPathsProcessing
                        == TextEditOptions.ClippingPathsProcessingMode.Expand)
                    {
                        expandClips = true;
                        break;
                    }

            string? switchedFontFamily = null;
            // An attached (TextBuilder-appended) fragment is written again from its
            // state at save time; editing the page here would only leave the old
            // run's replacement behind for the rewrite to overwrite.
            if (SourcePage is not null && AttachedSegment is null)
            {
                // Font-switch a replacement whose glyphs are absent from the source
                // embedded subset to a fallback (source family / Times) so it renders,
                // splitting the run and re-anchoring following text at its original
                // absolute Tm so downstream positions are preserved.
                TextReplacer.ResetSwitchedFont();
                // NoCharacterAction.ReplaceAnyway means "force the bytes into the ORIGINAL
                // font, don't substitute" — so the subset-glyph fallback (and its font
                // report) is disabled for that mode; the default mode substitutes and
                // reports the fallback face.
                bool replaceAnyway = _textEditOptions is { NoCharacterBehaviorExplicit: true } teo0
                    && teo0.NoCharacterBehavior == TextEditOptions.NoCharacterAction.ReplaceAnyway;
                bool allowFallback = !replaceAnyway;
                // Fragment-level replace re-flows the rest of the line (following
                // same-line text shifts by the width delta when the
                // replacement is shorter/longer than the match) — EXCEPT under
                // ReplaceAdjustment.None, whose contract is that surrounding text
                // keeps its exact position regardless of the width change, and
                // ReplaceAdjustment.AdjustSpaceWidth, whose contract is that the
                // inter-word gaps absorb the width change so the line keeps its
                // total length — for both, trailing text stays anchored.
                var adjustAction = _replaceOptions?.ReplaceAdjustmentAction
                    ?? TextReplaceOptions.ReplaceAdjustment.None;
                bool reflowLine = adjustAction is not (TextReplaceOptions.ReplaceAdjustment.None
                    or TextReplaceOptions.ReplaceAdjustment.AdjustSpaceWidth);
                // An invisible (Tr 3) fragment is the OCR/searchable twin of a
                // visible copy drawn at nearly the same spot; scope every edit to
                // the invisible render mode so emptying it (fragment.Text = "")
                // doesn't strip the visible copy (see DeleteFromContent).
                int? reqRenderMode = TextState is { RenderingMode: TextRenderingMode.Invisible } ? 3 : null;
                var replacer = new TextReplacer { ForcedCidFallbackFamily = _pendingCidRedressFamily, AllowSubsetGlyphFallback = allowFallback, ReflowLineOnReplace = reflowLine, AnchorTrailingOnReplace = !reflowLine, RequiredRenderMode = reqRenderMode,
                    // AdjustSpaceWidth re-seats the rest of the line by the change in
                    // ADVANCE. A page that states each glyph off a relative Td chain
                    // carries no absolute placement for the pass below to patch, so the
                    // replacer does that line itself while it holds the operator spans.
                    ShiftFollowersByAdvance = adjustAction == TextReplaceOptions.ReplaceAdjustment.AdjustSpaceWidth };
                // Scope to this fragment's page-space Y so iterating
                // fragments[i].Text in a loop replaces only the operator that
                // produced this fragment, not every matching occurrence on the
                // page. Without this, the replacement string accumulates across
                // iterations: setting fragment 1 first replaces all occurrences,
                // then fragment 2's setter re-matches inside the just-replaced
                // text and appends another copy of the replacement, etc.
                //
                // The replacer compares against the Tm-origin (baseline) Y.
                // Position.YIndent tracks the baseline only loosely — depending on the
                // absorber path it is the rect bottom (descent below baseline) or the
                // baseline itself, and BaselinePosition's descent correction can itself
                // be wrong when FontSize carries an unscaled Tf value (page scale in the
                // CTM). So try BOTH candidate Ys, baseline-corrected first.
                // Geometry of the run BEFORE the edit, for the follower shift below.
                var preRect = _rectangle;
                double? preBaselineY = _position is { } prePos
                    ? (BaselinePosition ?? prePos).YIndent : null;
                var targetYs = new List<double?>();
                if (_position is { } pos)
                {
                    var baseY = (BaselinePosition ?? pos).YIndent;
                    targetYs.Add(baseY);
                    if (Math.Abs(pos.YIndent - baseY) > 0.01)
                        targetYs.Add(pos.YIndent);
                }
                else
                    targetYs.Add(null);
                // Absorber fragments map to whole show-operators, so per candidate Y
                // first look for an operator whose ENTIRE text equals the fragment.
                // This pins the edit to the fragment's own operator when the same words
                // also occur INSIDE a longer operator on the same line (emptying a
                // "Lorem Ipsum" heading must not eat the mid-sentence "Lorem Ipsum" of
                // a sibling fragment, and a whitespace-only fragment must not match the
                // spaces inside every neighbouring operator). Substring matching runs
                // as a second sweep — except for whitespace-only text, where it would
                // only cause the space-eating collapse the whole-op pass prevents.
                // The whole-op pass is additionally X-scoped: a table row repeats the
                // same short cell text ("(", "58", …) as one operator per cell at the
                // SAME baseline Y, and only the fragment's own X tells them apart.
                // Run the X-scoped sweep first, then relax X (a fragment whose X
                // drifted from the op's Tm origin — CTM scaling — must still match).
                foreach (var tx in new double?[] { _position?.XIndent, null })
                {
                    foreach (var ty in targetYs)
                    {
                        replacer.TargetX = tx;
                        replacer.TargetY = ty;
                        replacer.MatchWholeOperator = true;
                        replacer.Replace(SourcePage, oldText, value);
                        if (replacer.ReplacementCount > 0) break;
                    }
                    if (replacer.ReplacementCount > 0 || _position is null) break;
                }
                replacer.TargetX = null;
                if (replacer.ReplacementCount == 0 && oldText.Trim().Length > 0)
                {
                    foreach (var ty in targetYs)
                    {
                        replacer.TargetY = ty;
                        replacer.MatchWholeOperator = false;
                        replacer.Replace(SourcePage, oldText, value);
                        if (replacer.ReplacementCount > 0) break;
                    }
                }

                // Fallback: if simple replace found nothing, try cross-operator replacement
                // (handles text split across TJ/Tj operators). Not gated on segment count:
                // the absorber COALESCES a one-character-per-operator producer into a single
                // segment, so a single-segment fragment can still span many operators.
                if (replacer.ReplacementCount == 0 && (_segments.Count > 1 || oldText.Length > 1))
                {
                    var crossReplacer = new TextReplacer { ForcedCidFallbackFamily = _pendingCidRedressFamily, AllowSubsetGlyphFallback = allowFallback, ReflowLineOnReplace = reflowLine, AnchorTrailingOnReplace = !reflowLine, RequiredRenderMode = reqRenderMode,
                        ShiftFollowersByAdvance = adjustAction == TextReplaceOptions.ReplaceAdjustment.AdjustSpaceWidth };
                    foreach (var ty in targetYs)
                    {
                        crossReplacer.TargetY = ty;
                        crossReplacer.ReplaceWithCrossOperator(SourcePage, oldText, value);
                        if (crossReplacer.ReplacementCount > 0) break;
                    }
                    if (crossReplacer.ReplacementCount > 0)
                        replacer = crossReplacer; // use cross result
                }

                // Fallback: if the combined text wasn't found AND the fragment uses a CID
                // font (common for Arabic/CJK where text spans multiple content stream
                // operators), replace segment-by-segment. The first non-trivial segment
                // receives the new text; remaining segments are cleared.
                // A DELETION takes this path for any multi-segment fragment: a match
                // assembled X-ordered from out-of-stream-order runs (back-jump splice)
                // has no contiguous operator text to find, but each segment maps to a
                // concrete run and wipes cleanly on its own.
                if (replacer.ReplacementCount == 0 && _segments.Count > 1
                    && (IsCidFontFragment() || value.Length == 0))
                {
                    var firstSeg = true;
                    foreach (var s in _segments)
                    {
                        if (string.IsNullOrWhiteSpace(s.Text)) continue;
                        // Scope each segment wipe to the segment's own position — an
                        // UNSCOPED single-char segment ('M' from a per-glyph vertical
                        // run) deletes its letter from every operator on the page,
                        // leaving fallback-font remnants no later delete can match.
                        // Whole-op scoped first (a segment maps to a concrete run),
                        // then scoped substring, then Y-only (X drifts under CTM
                        // scaling) — never page-wide.
                        var segY = (s.BaselinePosition ?? s.Position)?.YIndent;
                        var segX = s.Position?.XIndent;
                        var found = false;
                        foreach (var (mx, mo) in new (double?, bool)[]
                                 { (segX, true), (segX, false), (null, false) })
                        {
                            var segReplacer = new TextReplacer
                            { TargetY = segY, TargetX = mx, MatchWholeOperator = mo, RequiredRenderMode = reqRenderMode };
                            segReplacer.Replace(SourcePage, s.Text, firstSeg ? value : "");
                            if (segReplacer.ReplacementCount > 0) { found = true; break; }
                            if (segY is null) break; // no geometry at all: single unscoped try
                        }
                        if (found) firstSeg = false;
                    }
                }

                // Deletion-only last resort: a fragment with EDGE whitespace (" .") can
                // join a space operator and a glyph operator that no single-op match —
                // whole or substring — covers, and a sibling fragment's earlier delete
                // may already have consumed the glyph op. Drop each whitespace/non-ws
                // token as its own whole operator so the space op doesn't survive as a
                // non-empty invisible remnant.
                if (replacer.ReplacementCount == 0 && value.Length == 0
                    && oldText.Length > 0 && oldText.Trim() != oldText)
                {
                    var ti = 0;
                    while (ti < oldText.Length)
                    {
                        var ws = char.IsWhiteSpace(oldText[ti]);
                        var tj = ti;
                        while (tj < oldText.Length && char.IsWhiteSpace(oldText[tj]) == ws) tj++;
                        var tokReplacer = new TextReplacer { MatchWholeOperator = true, RequiredRenderMode = reqRenderMode };
                        foreach (var ty in targetYs)
                        {
                            tokReplacer.TargetY = ty;
                            tokReplacer.Replace(SourcePage, oldText.Substring(ti, tj - ti), "");
                            if (tokReplacer.ReplacementCount > 0) break;
                        }
                        ti = tj;
                    }
                }

                // If the replacement's glyphs were absent from the source subset and the
                // run was substituted in a fallback face, surface that font on the
                // fragment (default no-character behaviour reports the substituted font).
                switchedFontFamily = replacer.SwitchedFontFamily;

                // AdjustSpaceWidth keeps the LINE's length by letting the gaps take up the
                // slack, but the words after the replacement still sit behind its new
                // advance. On a page that states every run's position outright — one
                // `BT .. Tm .. Tj .. ET` block per word — nothing inside the edited operator
                // can move them, so slide them here. The advance is taken from the page as
                // WRITTEN rather than measured: a replacement whose glyphs the source subset
                // lacks is re-dressed in another face, and the source font's own width table
                // then describes a run that was never drawn.
                if (replacer.ReplacementCount > 0 && value.Length > 0
                    && !replacer.ShiftedFollowers
                    && adjustAction == TextReplaceOptions.ReplaceAdjustment.AdjustSpaceWidth
                    && preRect is { } shiftPre && preBaselineY is { } shiftBaseY)
                {
                    var writtenAdvance = MeasureWrittenAdvance(SourcePage, value, shiftPre.LLX, shiftBaseY);
                    if (writtenAdvance is { } newAdvance)
                    {
                        var shiftDelta = newAdvance - shiftPre.Width;
                        if (Math.Abs(shiftDelta) > 0.01)
                        {
                            var content = SourcePage.GetContentStreamBytes();
                            if (content is { Length: > 0 })
                            {
                                var shifted = TextReplacer.ShiftFollowingBlocks(
                                    content, shiftBaseY, shiftPre.URX, shiftDelta);
                                if (!ReferenceEquals(shifted, content))
                                    SourcePage.SetContentStream(shifted);
                            }
                        }
                    }
                }

                // TextEditOptions.ClippingPathsProcessing.Expand: a cell-tight clip
                // (re W/W* n) around the replaced operator would clip a wider
                // replacement to the OLD text's box; widen any such clip so the new
                // text paints fully.
                if (expandClips && _position is { } cpos
                    && TextState.Font is { } clipFont && TextState.FontSize > 0)
                {
                    double neededW;
                    try { neededW = clipFont.MeasureString(value, TextState.FontSize); }
                    catch { neededW = -1; }
                    if (neededW > 0)
                        ExpandTightClipsAround(SourcePage, cpos.XIndent, cpos.YIndent, neededW);
                }
            }
            else if (Form is not null)
            {
                // The fragment was extracted via TextFragmentAbsorber.Visit(XForm), so
                // its producing operator lives in the Form XObject's content stream
                // (SourcePage is null). Edit that stream rather than no-op.
                new TextReplacer().Replace(Form, oldText, value);
            }

            // Reset segments to a single segment with the new text
            _segments.Clear();
            var seg = new TextSegment(value);
            // Quiet copy: inheriting the fragment's size must not read as an
            // explicit caller size on the segment (FontSizeTouched stays false),
            // so a size set on the fragment afterwards still governs the segment.
            seg.TextState.SetFontSizeQuiet(TextState.FontSize);
            seg.TextState.FontName = TextState.FontName;
            seg.TextState.Font = TextState.Font;
            seg.Owner = this;
            // Inherit position from the fragment so segment-level
            // BackgroundColor / per-segment effects can compute rect bounds.
            // Use PositionOrNull, not the Position getter: the getter now
            // auto-materialises a (0,0) stand-in for an unpositioned fragment,
            // and copying that into the segment would make the flow paginator
            // treat the fragment as explicitly positioned (per-segment Position
            // set) and decline to flow it.
            seg.Position = PositionOrNull;
            // Wire OwnerSegment so subsequent TextState.ForegroundColor /
            // BackgroundColor / FontSize setters propagate to the page's
            // content stream (TextStateModifier looks up via OwnerSegment.Owner.SourcePage).
            seg.TextState.OwnerSegment = seg;
            _segments.Add(seg);

            // Under ReplaceAdjustment.None and ShiftRestOfLine the visible width of
            // the fragment tracks the replacement's advance — whether the
            // surrounding glyphs keep their positions (None: TJ compensation kerns) or
            // close up behind it (ShiftRestOfLine), the fragment spans the
            // new text, so its rectangle must reflect the width change. The wholesale
            // re-wrap modes (WholeWordsHyphenation etc.) recompute geometry themselves.
            // Adjust by the old→new advance DELTA, not to the absolute new advance:
            // for ordinary runs the old rect width IS the old advance so both agree,
            // but a Tc/kern-spread table row is far wider than its natural advance
            // and swapping one word must keep that spread (the stream keeps its kerns).
            // Whether the rectangle below already took the replacement's width delta:
            // the decoration block further down must not apply it a SECOND time, which
            // subtracts the old advance twice and collapses the rectangle to zero width
            // (a shortened replacement then draws a zero-width underline).
            var rectTookAdvanceDelta = false;
            if (_rectangle is not null
                && _replaceOptions?.ReplaceAdjustmentAction is TextReplaceOptions.ReplaceAdjustment.None
                    or TextReplaceOptions.ReplaceAdjustment.ShiftRestOfLine
                && TextState.Font is { } font && TextState.FontSize > 0)
            {
                double newWidth;
                try
                {
                    var oldWidth = font.MeasureString(oldText, TextState.FontSize);
                    newWidth = font.MeasureString(value, TextState.FontSize);
                    // Old text wider than its own rect can't happen for real layouts;
                    // spread detection: rect wider than the old natural advance by
                    // more than a point means positioning ops pad the run — keep them.
                    if (_rectangle.Width > oldWidth + 1.0)
                        newWidth = Math.Max(0, _rectangle.Width + newWidth - oldWidth);
                }
                catch { newWidth = -1; }
                if (newWidth >= 0)
                {
                    _rectangle = new Rectangle(_rectangle.LLX, _rectangle.LLY,
                        _rectangle.LLX + newWidth, _rectangle.URY);
                    rectTookAdvanceDelta = true;
                }
            }

            // ToAttemptGetUnderlineFromSource: source decorations follow the replacement.
            // Splice the captured underline/highlight rectangles out of the content and
            // register standard injection so new ones are drawn at the replacement's
            // advance (the source rects are sized to the OLD text and would otherwise
            // keep rendering under/behind the new, differently-sized text). Runs BEFORE
            // the reported-font switch below so the advance is measured against the
            // ORIGINAL face (with its style), not the switched family name.
            if (SourcePage is not null &&
                (CapturedUnderlineSources is { Count: > 0 } || CapturedBackgroundSources is { Count: > 0 }))
            {
                SourcePage.RegisterUnderlineRemoval(this);

                // Re-size the fragment rectangle by the replacement's advance DELTA so
                // the injected decoration spans the new text, not the old. Delta, not
                // the absolute advance: a kern-spread run's rect is far wider than the
                // text's natural advance, and swapping one word must keep that spread.
                if (!rectTookAdvanceDelta && _rectangle is not null
                    && TextState.Font is { } decFont && TextState.FontSize > 0)
                {
                    var advance = MeasureReplacementAdvance(decFont, value, TextState.FontSize);
                    var oldAdvance = MeasureReplacementAdvance(decFont, oldText, TextState.FontSize);
                    if (advance >= 0 && oldAdvance >= 0)
                        _rectangle = new Rectangle(_rectangle.LLX, _rectangle.LLY,
                            _rectangle.LLX + Math.Max(0, _rectangle.Width + advance - oldAdvance),
                            _rectangle.URY);
                    else if (advance >= 0)
                        _rectangle = new Rectangle(_rectangle.LLX, _rectangle.LLY,
                            _rectangle.LLX + advance, _rectangle.URY);
                }

                if (CapturedUnderlineSources is { Count: > 0 })
                    SourcePage.RegisterUnderlineFragment(this);
                if (CapturedBackgroundSources is { Count: > 0 })
                {
                    if (TextState.BackgroundColor is null && CapturedBackgroundColor is { } cbc)
                        TextState.SetCapturedBackgroundColor(cbc);
                    SourcePage.RegisterBgColorFragment(this);
                }
            }

            // Report the substituted fallback face on the fragment's TextState.Font
            // (the byte-level replacer already switched the glyphs in the stream; this
            // only updates what the fragment REPORTS, side-effect-free — no re-embed
            // or content rewrite, so following text is not shifted).
            if (switchedFontFamily is not null)
                TextState.SetReportedFont(switchedFontFamily);
        }
    }

    private static readonly Dictionary<string, GlyphOutlineParser?> _systemFaceGlyphs = new();

    private static readonly Dictionary<string, double[]?> _systemFaceWidths = new();

    /// <summary>Segments whose text the delete could not find — the cue that the region
    /// sweep still has work to do.</summary>
    private int _unresolvedSegments;

    /// <summary>Reflow for a mid-line token replacement
    /// (WholeWordsHyphenation): everything BEFORE the match stays untouched; the
    /// replacement plus all following paragraph text re-packs greedily from the match
    /// position onto the paragraph's EXISTING baselines (baselines never move; trailing
    /// lines that empty out stay empty). Works at run granularity — each source segment
    /// is one text-showing operator, deleted by whole-operator match at its page-space
    /// position and re-emitted as packed lines.</summary>
    /// <summary>The page's absorbed fragments folded into real LINES: a producer that draws a
    /// line as many operators (one per word, or split around every positioning nudge) yields a
    /// fragment per run, all sharing one baseline. A paragraph grown over those fragments stops
    /// at the first same-baseline sibling — the gap between them is zero, not a line pitch — so
    /// the "paragraph" collapses to the matched run alone and the reflow reads a column that
    /// does not exist. Merging by baseline restores the line, and with it the paragraph's own
    /// right edge.</summary>
    private static System.Collections.Generic.List<(double y, double lx, double rx)> MergeBaselines(
        System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> lines,
        System.Collections.Generic.Dictionary<TextFragment, double> spanLx)
    {
        // Grouped by the fragments' own Y (their rectangle bottoms), which is the one figure
        // every run of a line agrees on whatever face or Tm scale it carries; each band then
        // reports the TRUE baseline of its widest run, the run whose metrics the line is set in.
        var ordered = new System.Collections.Generic.List<(double y, double baseY, double lx, double rx, double fs)>();
        foreach (var l in lines)
        {
            // The line's FULL left span, including leading blank glyphs. Grouping on the
            // first VISIBLE glyph instead splits a line that opens with padding spaces off
            // from its own column: an edited line reads 63.80 that way while every
            // other line of the same block reads 56.90, so the block never forms and the
            // reflow wraps against one line's extent rather than the column's.
            var lx0 = spanLx.TryGetValue(l.f, out var sp) ? System.Math.Min(sp, l.lx) : l.lx;
            ordered.Add((l.y, LineBaseline(l), lx0, l.rx,
                l.f.TextState.FontSize > 0 ? l.f.TextState.FontSize : 10));
        }
        ordered.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : a.lx.CompareTo(b.lx));

        var bands = new System.Collections.Generic.List<(double y, double lx, double rx)>();
        var widest = new System.Collections.Generic.List<double>();
        var baseOf = new System.Collections.Generic.List<double>();
        foreach (var o in ordered)
        {
            int hit = -1;
            for (int i = bands.Count - 1; i >= 0; i--)
            {
                if (System.Math.Abs(bands[i].y - o.y) > 0.75) continue;
                // Sharing a baseline is not enough: a two-column page puts one column's line
                // on the same baseline as the other's, and folding them together would report
                // a column that spans the whole page. Runs join a line only while they stay
                // horizontally contiguous — a gap wider than a couple of ems is a column break.
                double gapTol = System.Math.Max(6.0, 2.0 * o.fs);
                if (o.lx <= bands[i].rx + gapTol && o.rx >= bands[i].lx - gapTol) { hit = i; break; }
            }
            if (hit < 0)
            {
                bands.Add((o.y, o.lx, o.rx));
                widest.Add(o.rx - o.lx);
                baseOf.Add(o.baseY);
                continue;
            }
            var b = bands[hit];
            bands[hit] = (b.y, System.Math.Min(b.lx, o.lx), System.Math.Max(b.rx, o.rx));
            if (o.rx - o.lx > widest[hit]) { widest[hit] = o.rx - o.lx; baseOf[hit] = o.baseY; }
        }
        for (int i = 0; i < bands.Count; i++)
            bands[i] = (baseOf[i], bands[i].lx, bands[i].rx);
        bands.Sort((a, b) => b.y.CompareTo(a.y));
        return bands;
    }

    /// <summary>The contiguous run of merged lines around <paramref name="matchBaseline"/> that
    /// share a left margin and a line pitch — the paragraph, seen as lines. Returns the lines
    /// from the match line DOWN, which is the slice a cascade re-packs.</summary>
    private static System.Collections.Generic.List<(double y, double lx, double rx)> GrowBandParagraph(
        System.Collections.Generic.List<(double y, double lx, double rx)> bands,
        double matchBaseline, double matchX, double maxGap, double xtol)
    {
        int at = -1;
        double best = double.MaxValue;
        for (int i = 0; i < bands.Count; i++)
        {
            double d = System.Math.Abs(bands[i].y - matchBaseline);
            if (d < best && matchX >= bands[i].lx - 5 && matchX <= bands[i].rx + 5) { best = d; at = i; }
        }
        var result = new System.Collections.Generic.List<(double y, double lx, double rx)>();
        if (at < 0) return result;
        double refLx = bands[at].lx;
        // The paragraph's OWN pitch, measured from the lines that share its left margin. Taking
        // it from the match's font size fails on a producer that carries the size in the text
        // matrix and reports 1 pt: the pitch then comes out around a point and every real line
        // gap looks like a paragraph break.
        {
            var col = new System.Collections.Generic.List<int>();
            for (int i = 0; i < bands.Count; i++)
                if (System.Math.Abs(bands[i].lx - refLx) <= xtol) col.Add(i);
            int k = col.IndexOf(at);
            if (k >= 0 && col.Count >= 2)
            {
                var gaps = new System.Collections.Generic.List<double>();
                for (int j = System.Math.Max(1, k - 6); j < System.Math.Min(col.Count, k + 7); j++)
                {
                    double g = bands[col[j - 1]].y - bands[col[j]].y;
                    if (g > 0.5) gaps.Add(g);
                }
                if (gaps.Count > 0)
                {
                    gaps.Sort();
                    maxGap = gaps[gaps.Count / 2] * 1.35;
                }
            }
        }
        var taken = new System.Collections.Generic.List<int> { at };
        // A page with two columns interleaves their lines in Y order, so the paragraph's own
        // next line is not necessarily the next BAND: a band at another left margin is skipped,
        // not treated as the end of the paragraph. The vertical gap is measured from the last
        // line actually taken, so the paragraph still ends where its own pitch breaks.
        double lastY = bands[at].y;
        for (int i = at + 1; i < bands.Count; i++)
        {
            double gap = lastY - bands[i].y;
            if (gap > maxGap) break;
            if (gap <= 0 || System.Math.Abs(bands[i].lx - refLx) > xtol) continue;
            taken.Add(i);
            lastY = bands[i].y;
        }
        lastY = bands[at].y;
        for (int i = at - 1; i >= 0; i--)
        {
            double gap = bands[i].y - lastY;
            if (gap > maxGap) break;
            if (gap <= 0 || System.Math.Abs(bands[i].lx - refLx) > xtol) continue;
            taken.Add(i);
            lastY = bands[i].y;
        }
        taken.Sort();
        // The column is read from the WHOLE paragraph; the cascade re-packs from the match down.
        foreach (var i in taken) result.Add(bands[i]);
        return result;
    }

    /// <summary>
    /// Replace options inherited from the producing TextFragmentAbsorber. Drives
    /// the rect-recompute behavior in the <see cref="Text"/> setter — when set
    /// to <see cref="TextReplaceOptions.ReplaceAdjustment.None"/>, the
    /// fragment's <see cref="Rectangle"/> shrinks to the replacement text's
    /// advance width.
    /// </summary>
    private TextReplaceOptions? _replaceOptions;

    /// <summary>Text-replacement options used by TextFragmentAbsorber
    /// replace paths. Lazy-initialised on first access so callers can
    /// mutate properties without setting a fresh instance first.</summary>
    public TextReplaceOptions ReplaceOptions
    {
        get => _replaceOptions ??= new TextReplaceOptions(TextReplaceOptions.ReplaceAdjustment.None);
        internal set => _replaceOptions = value;
    }

    /// <summary>The page this fragment belongs to (public alias for SourcePage).</summary>
    public Page? Page => SourcePage;

    /// <summary>Raw <c>re</c> operands (X, Y, Width, Height) of underline rectangles found
    /// in the source content beneath this fragment, captured when the absorber runs with
    /// <see cref="TextEditOptions.ToAttemptGetUnderlineFromSource"/>. If the fragment's
    /// underline is later toggled off, these locate the exact rectangle operators to splice
    /// out of the page content stream at save time.</summary>
    internal System.Collections.Generic.List<(double X, double Y, double W, double H)>? CapturedUnderlineSources;

    /// <summary>The rules the match's LINE carries besides its own - one under each other
    /// underlined run on the same baseline, in page space with that run's own colour. A
    /// replacement re-lays the line's rules in one band, so these are spliced out and
    /// redrawn with the match's; leaving them where the source put them would strand them
    /// half a point off the band the re-laid ones sit in.</summary>
    internal System.Collections.Generic.List<(double X, double W, Color Colour)>? CompanionRules;

    /// <summary>Raw <c>re</c> operands of the companion rules, for the splice.</summary>
    internal System.Collections.Generic.List<(double X, double Y, double W, double H)>? CompanionRuleSources;

    /// <summary>The captured source underline in PAGE space (Llx, Lly, Urx, Ury). The raw
    /// operands above locate the operator to splice out; these say WHERE the rule actually
    /// is, which is what a redraw needs — a rule under a `cm` (the usual shape for a
    /// STROKED underline) has raw coordinates that are not page coordinates at all.</summary>
    internal (double Llx, double Lly, double Urx, double Ury)? CapturedUnderlinePageRect;

    /// <summary>Raw <c>re</c> operands of background (highlight) rectangles drawn in the
    /// source content behind this fragment, captured when the absorber runs with
    /// <see cref="TextEditOptions.ToAttemptGetUnderlineFromSource"/>. When the fragment's
    /// text is replaced, these locate the old highlight to splice out so a new one can be
    /// drawn at the replacement's advance.</summary>
    internal System.Collections.Generic.List<(double X, double Y, double W, double H)>? CapturedBackgroundSources;

    /// <summary>The XForm this fragment was extracted from, or null for page-level fragments.</summary>
    public XForm? Form { get; internal set; }

    /// <summary>
    /// Horizontal alignment used when this fragment is laid out as a
    /// paragraph (added to <c>page.Paragraphs</c>). Stored on the
    /// fragment so callers can set alignment without touching
    /// <see cref="TextState"/>; the layout engine reads this on save.
    /// </summary>
    public new HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>The bounding rectangle of this text fragment on the page.</summary>
    public Rectangle? Rectangle
    {
        get
        {
            if (_rectangle != null) return _rectangle;
            // Estimate rectangle for newly created fragments from font metrics.
            // Used before the fragment is placed on a page. An empty fragment
            // still yields a (zero-width) rectangle —
            // Rectangle is never null for a constructed fragment.
            {
                double fontSize = TextState.FontSize;
                double width = 0;
                if (Text != null && Text.Length > 0)
                {
                    var font = TextState.Font;
                    if (font != null)
                    {
                        try { width = font.MeasureString(Text, fontSize); }
                        catch { width = Text.Length * fontSize * 0.5; }
                    }
                    else
                        width = Text.Length * fontSize * 0.5;
                }

                double height = fontSize * 1.2; // approximate line height
                // A quarter turn puts the run's advance on the VERTICAL axis: the box a
                // caller measures is the box the text will occupy once turned, so the two
                // extents swap. (Callers size a table column by the turned run's Height,
                // which is the advance — the whole point of asking a rotated fragment for
                // its box.) A half turn leaves the extents where they are.
                var rot = ((TextState.Rotation % 360) + 360) % 360;
                if (Math.Abs(rot - 90) < 0.01 || Math.Abs(rot - 270) < 0.01)
                    (width, height) = (height, width);
                double x = PositionOrNull?.XIndent ?? 0;
                double y = PositionOrNull?.YIndent ?? 0;
                return new Rectangle(x, y - height * 0.2, x + width, y + height * 0.8);
            }
        }
        internal set => _rectangle = value;
    }
    private Rectangle? _rectangle;

    /// <summary>The text position.</summary>
    public Position? Position
    {
        // Never return null: hand back a lazily-created (0,0) Position so callers can
        // write `fragment.Position.XIndent = …` on a freshly-constructed fragment
        // without a NullReferenceException.
        // IMPORTANT: the auto-Position is kept in a SEPARATE field and is NOT written
        // into _position, so field-based readers (Rectangle, BaselinePosition) still
        // see "no position" for an unpositioned fragment. It also starts Touched==false,
        // so merely reading Position does not make the fragment count as explicitly
        // positioned — only writing XIndent/YIndent (or the setter) does. See
        // HasExplicitPosition.
        get => _position ?? (_autoPosition ??= new Position(0, 0));
        set
        {
            _positionExplicit = value is not null;
            var oldPos = _position;
            _position = value;
            // A move applied AFTER absorption (the caller relocating found text):
            // remember the accumulated delta so the background highlight (and any
            // other geometry derived from the extraction rectangle) follows.
            if (SourcePage is not null && oldPos is not null && value is not null)
            {
                PostAbsorbDx += value.XIndent - oldPos.XIndent;
                PostAbsorbDy += value.YIndent - oldPos.YIndent;
            }
            // Reposition all segments relative to the new position.
            // Preserve their relative offsets from the fragment's previous position.
            if (value is not null && _segments.Count > 0)
            {
                // Determine reference position: use old fragment position, or first segment's position
                var refPos = oldPos ?? _segments[1].Position;
                if (refPos is not null)
                {
                    var dx = value.XIndent - refPos.XIndent;
                    var dy = value.YIndent - refPos.YIndent;
                    foreach (var seg in _segments)
                    {
                        if (seg.Position is not null)
                            seg.Position = new Position(seg.Position.XIndent + dx, seg.Position.YIndent + dy);
                    }
                }
                else
                {
                    _segments[1].Position = value;
                }
            }
        }
    }
    private Position? _position;

    /// <summary>Replace the stored rectangle and position WITHOUT any of the
    /// setter side effects (no segment repositioning, no explicit-position mark,
    /// no post-absorb delta) and hand back the previous pair. The paragraph
    /// absorber uses it to run its layout model in a rotated frame and restore.</summary>
    internal (Rectangle? rect, Position? pos) SwapGeometry(Rectangle? rect, Position? pos)
    {
        var old = (_rectangle, _position);
        _rectangle = rect;
        _position = pos;
        return old;
    }
    // Lazily-created stand-in returned by the Position getter when no real position
    // is set; never stored in _position (so field-based readers stay null-correct).
    private Position? _autoPosition;
    private bool _positionExplicit;

    /// <summary>Whether this fragment has a caller-specified position — set via the
    /// <see cref="Position"/> setter (absorber / generator) or by writing
    /// <c>Position.XIndent</c>/<c>YIndent</c> on the auto-materialised Position.
    /// Distinct from "<c>Position != null</c>" because the getter now never returns
    /// null; consumers that previously branched on null use this instead so a fragment
    /// that merely had its Position read still flows / derives geometry as before.</summary>
    internal bool HasExplicitPosition
        => _positionExplicit || (_position?.Touched ?? false) || (_autoPosition?.Touched ?? false);

    /// <summary>The real position, or null when none was set — i.e. the pre-refactor
    /// semantics of the <see cref="Position"/> getter (which now never returns null).
    /// Cross-instance readers that fall back to Rectangle/owner when unpositioned use
    /// this so the auto-materialised (0,0) stand-in doesn't suppress the fallback.
    /// A touched auto-Position (the caller did <c>Position.XIndent = …</c> on a fresh
    /// fragment) counts as a real position and is surfaced here too.</summary>
    internal Position? PositionOrNull
        => _position ?? (_autoPosition is { Touched: true } ? _autoPosition : null);

    /// <summary>
    /// The text baseline position. Position includes descent offset (bottom of text rect);
    /// BaselinePosition is the actual text baseline (higher by |descent|).
    /// </summary>
    public Position? BaselinePosition
    {
        get
        {
            // No baseline for an unpositioned fragment. PositionOrNull is null unless a
            // real position was set (or the auto-Position was touched), so the auto
            // (0,0) stand-in doesn't fabricate a baseline.
            var p = PositionOrNull;
            if (p is null) return null;
            return new Position(p.XIndent, p.YIndent - SeatDescent());
        }
        set
        {
            if (value is null) { _position = null; _positionExplicit = false; return; }
            // Reverse: add descent to get Position from BaselinePosition
            _position = new Position(value.XIndent, value.YIndent + SeatDescent());
            _positionExplicit = true;
        }
    }

    /// <summary>Margin information for layout when used as a paragraph element.</summary>
    public new MarginInfo Margin { get; set; } = new();

    /// <summary>Text state (font, size, color). Fragment-typed wrapper
    /// around the underlying <see cref="Aspose.Pdf.Text.TextState"/> so
    /// callers can reach <see cref="TextFragmentState.Font"/> as
    /// <see cref="Font"/> and the fragment-only members
    /// (DrawTextRectangleBorder, TabStops, IsFitRectangle).</summary>
    public TextFragmentState TextState { get; }

    /// <summary>True when the LAYOUT ENGINE wrote this fragment: its segments are the
    /// pieces of one flowed line, so a rewrite chains them along that line instead of
    /// seating each at its own position (see <c>TextBuilder.AppendTextInline</c>).</summary>
    internal bool AttachedInline { get; set; }

    /// <summary>The page index (0-based) this fragment was found on.</summary>
    public int PageIndex { get; internal set; }

    /// <summary>Shortcut for TextState.FontSize.</summary>
    public double FontSize => TextState.FontSize;

    /// <summary>
    /// The collection of text segments that make up this fragment (1-indexed).
    /// A newly constructed fragment always contains at least one segment.
    /// Setter replaces every segment with the supplied collection's items.
    /// </summary>
    public TextSegmentCollection Segments
    {
        get => _segments;
        set
        {
            // Mutate-in-place rather than rebinding so the existing Owner
            // back-reference stays intact.
            _segments.Clear();
            if (value is null) return;
            foreach (var s in value) _segments.Add(s);
            RefreshTextFromSegments();
        }
    }

    /// <summary>Shallow copy of the fragment (text + state). Segments are
    /// regenerated from the cloned text.</summary>
    public override object Clone()
    {
        var copy = new TextFragment(_text, Rectangle, textState: null);
        copy.TextState.ApplyChangesFrom(TextState);
        copy.TabStops = TabStops;
        copy.IsInLineParagraph = IsInLineParagraph;
        copy.IsInNewPage = IsInNewPage;
        copy.VerticalAlignment = VerticalAlignment;
        copy.WrapLinesCount = WrapLinesCount;
        return copy;
    }

    /// <summary>Hyperlink applied to this fragment. Set-only on the
    /// public surface; the underlying hyperlink action is wired into
    /// the page's annotation stream at save time (stored only in this build).</summary>
    public new Hyperlink Hyperlink
    {
        set
        {
            _hyperlink = value;
            // Register for save-time link-annotation emission when set on a fragment
            // obtained via TextFragmentAbsorber (the generator path emits links during
            // layout, but absorber-edited fragments need an explicit save-time pass).
            if (value is not null) SourcePage?.RegisterHyperlinkFragment(this);
        }
    }
    private Hyperlink? _hyperlink;

    /// <summary>The fragment's text in the order it is DRAWN — the join of its
    /// per-glyph-run segments. Equal to <see cref="Text"/> for everything left-to-right;
    /// for an RTL run Text reports the reading order while the content stream holds this
    /// one, so anything that searches or rewrites the stream must use this.</summary>
    internal string DrawnOrderText
    {
        get
        {
            if (_segments is null || _segments.Count == 0) return _text;
            var sb = new System.Text.StringBuilder();
            foreach (var seg in _segments)
                sb.Append(seg.Text);
            return sb.Length == 0 ? _text : sb.ToString();
        }
    }

}
