namespace Aspose.Pdf.Text;

/// <summary>
/// Represents a fragment of text on a page with position and style information.
/// </summary>
public class TextFragment : BaseParagraph
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

    /// <summary>Tab stops for this fragment (used with #$TAB markers in text).</summary>
    public TabStops? TabStops { get; set; }

    // CSS line-box font metrics (fractions of em) for the HTML→PDF table-cell path.
    // When set (> 0), a generator table cell holding lines of MIXED font sizes lays this
    // fragment's line out as a CSS line box: height = line-height × em, baseline at
    // ascent × em + half-leading below the box top. Zero = legacy uniform-row layout.
    internal double CssAscent;
    internal double CssDescent;
    // CSS line-height (pt) carried into table-cell layout: wrapped cell lines
    // pitch at this height instead of the bare font size when set.
    internal double CssLineHeightPt;
    // True when CssLineHeightPt came from the CELL'S OWN inline `line-height`
    // declaration: the lifted table dialect then makes it each line's BOX height
    // (the css-box stack advance), not just the row pitch. Document-level pitches
    // stay row-level — the calibrated dialects depend on that.
    internal bool CssLineHeightFromCell;
    // Inline boxes drawn behind this fragment's first laid-out cell line (HTML
    // inline-block plates/pills, pre-measured by the converter with the metrics
    // that lay the line out); consumed by the generator table's render pass.
    internal List<InlineBoxDecoration>? InlineBoxes;
    // Radio options riding this fragment's text INLINE (an HTML form grid's
    // `◯ ◯Yes ◉ ◉No` row): one entry per Table.InlineRadioChar /
    // InlineRadioCheckedChar in Text, in order. The table render pass draws each
    // as a circle glyph in the line's run and places the option's widget there.
    internal System.Collections.Generic.List<Aspose.Pdf.Forms.RadioButtonOptionField>? InlineOptions;
    // Deliberate blank line (an explicit <br> inside a styled paragraph): keeps its
    // line box in the flow as vertical space even though it renders no text.
    internal bool CssKeepBlank;

    /// <summary>Cell lines render as CSS line boxes (1.2 em pitch) even when every
    /// line shares one size — set for cells sized by a stylesheet `font:` shorthand
    /// (the form-document dialect), whose reference pitch is the CSS line box.</summary>
    internal bool CssLineBoxAlways;

    // Inline <a href> runs inside an HTML table cell: each entry is the anchor's
    // (collapsed) inner text and target URL. The table renderer resolves each run
    // against its laid-out line and emits a Link annotation over just that run.
    internal System.Collections.Generic.List<(string Text, string Url)>? HtmlAnchors;

    // Form-grid line whose bold state toggles mid-run ('Owner Team: <b>bv
    // Designers</b>'): the segments in order, each drawn in its own face variant
    // by the table renderer. Null for uniformly-styled lines.
    internal System.Collections.Generic.List<(string Text, bool Bold)>? FormGridRuns;

    // Form-grid baseline drop: the distance from this line's box top to its
    // baseline — max(strut drop, run drop), each half-leading + winAscent of
    // its box. Zero = legacy placement.
    internal double CssBaseDrop;

    private TextEditOptions? _textEditOptions;

    /// <summary>Edit options applied during text replacement / font substitution.</summary>
    public TextEditOptions TextEditOptions
    {
        get => _textEditOptions ??= new TextEditOptions(TextEditOptions.LanguageTransformation.Default);
        set => _textEditOptions = value;
    }

    /// <summary>True when the caller explicitly opted into
    /// <see cref="TextEditOptions.NoCharacterAction.ReplaceFonts"/> — the generator
    /// then substitutes a glyph-covering face at layout time. Reads the backing
    /// field so the check never instantiates default options.</summary>
    internal bool HasExplicitReplaceFonts =>
        _textEditOptions is { NoCharacterBehaviorExplicit: true,
            NoCharacterBehavior: TextEditOptions.NoCharacterAction.ReplaceFonts };

    /// <summary>
    /// When true, this text fragment renders on the same line as the previous
    /// in-line paragraph. Currently a state flag — layout wiring follows
    /// once Image / inline-flow rendering is implemented.
    /// </summary>
    public new bool IsInLineParagraph { get; set; }

    public new bool IsInNewPage { get; set; }

    public new VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Top;

    /// <summary>The bounding box the absorber computed at match time, snapshotted
    /// by value. Callers routinely mutate the live <see cref="Rectangle"/> instance
    /// (shift it, then hand it back as a replace target) — this copy preserves the
    /// pre-mutation geometry the shift is measured against.</summary>
    internal Rectangle? AbsorbedRectangle;

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

            var oldText = _text;

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
            if (SourcePage is not null)
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
                var replacer = new TextReplacer { AllowSubsetGlyphFallback = allowFallback, ReflowLineOnReplace = reflowLine, AnchorTrailingOnReplace = !reflowLine, RequiredRenderMode = reqRenderMode };
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
                    var crossReplacer = new TextReplacer { AllowSubsetGlyphFallback = allowFallback, ReflowLineOnReplace = reflowLine, AnchorTrailingOnReplace = !reflowLine, RequiredRenderMode = reqRenderMode };
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
            seg.TextState.FontSize = TextState.FontSize;
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
                if (_rectangle is not null && TextState.Font is { } decFont && TextState.FontSize > 0)
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

    /// <summary>
    /// Remove this fragment's text from the page content stream. Used when a
    /// fragment is removed from an absorber's result collection: the producing
    /// text-showing operator is dropped so the next save no longer renders it.
    /// Matches the operator whose entire shown text equals this fragment's text
    /// (scoped to the fragment's page-space Y) so deleting a short fragment such
    /// as "$" does not corrupt a longer one such as "$ 200.00" on the same row.
    /// </summary>
    internal void DeleteFromContent()
    {
        if (SourcePage is null || string.IsNullOrEmpty(_text))
            return;

        // The replacer scopes by the Tm-origin (baseline) Y; Position.YIndent is
        // often the rect bottom (a full descent below the baseline — exactly at
        // the scoping tolerance for a 24 pt font), so try the baseline-corrected
        // candidate first and the raw position second, same as the Text setter.
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

        foreach (var targetY in targetYs)
        {
            var replacer = new TextReplacer { MatchWholeOperator = true, TargetY = targetY };
            // An invisible (Tr 3) fragment is typically the OCR/searchable twin of a
            // visible copy drawn at nearly the same spot (a scanned-invoice text layer).
            // Deleting it must not strip the visible copy, which Y/X scoping alone would
            // hit first — restrict the match to the invisible render mode.
            if (TextState is { RenderingMode: TextRenderingMode.Invisible })
                replacer.RequiredRenderMode = 3;
            replacer.Replace(SourcePage, _text, string.Empty);
            if (replacer.ReplacementCount > 0) break;

            // Fall back to substring replacement for fragments whose text spans only
            // part of an operator (or multiple operators) and so was not removed by
            // the exact whole-operator pass. The rest of the operator's text survives
            // the deletion, so it must keep its exact position: anchor the trailing
            // run at its original absolute Tm instead of letting it slide left into
            // the deleted span.
            var fallback = new TextReplacer { AnchorTrailingOnReplace = true, TargetY = targetY };
            if (TextState is { RenderingMode: TextRenderingMode.Invisible })
                fallback.RequiredRenderMode = 3;
            fallback.Replace(SourcePage, _text, string.Empty);
            if (fallback.ReplacementCount > 0) break;
        }

        _text = string.Empty;
        _segments.Clear();
    }

    /// <summary>
    /// Remove this fragment's text from the page for redaction: like
    /// <see cref="DeleteFromContent"/> but width-preserving — a fully-deleted show
    /// operator leaves a glyph-less advance instead of being dropped, so text after
    /// it on the same line keeps its position (no reflow). Scoped to the fragment's
    /// page-space Y so only this occurrence is removed.
    /// </summary>
    internal void RedactFromContent()
    {
        if (SourcePage is null || string.IsNullOrEmpty(_text))
            return;

        // Scope to this occurrence: Y picks the line, X picks the operator —
        // a short run like " e" can appear several times on one line, and
        // deleting the copies outside the redaction box would eat text the
        // caller never asked to remove.
        var replacer = new TextReplacer { MatchWholeOperator = true, PreserveAdvanceOnDelete = true };
        if (_position is { } pos)
        {
            replacer.TargetY = pos.YIndent;
            replacer.TargetX = pos.XIndent;
        }
        replacer.Replace(SourcePage, _text, string.Empty);

        if (replacer.ReplacementCount == 0)
        {
            var fallback = new TextReplacer { PreserveAdvanceOnDelete = true };
            if (_position is { } pos2)
            {
                fallback.TargetY = pos2.YIndent;
                fallback.TargetX = pos2.XIndent;
            }
            fallback.Replace(SourcePage, _text, string.Empty);
        }

        _text = string.Empty;
        _segments.Clear();
    }

    /// <summary>
    /// Reflow <paramref name="newText"/> into <paramref name="rect"/>: word-wrap
    /// to the rectangle width, optionally fit the font size
    /// (<see cref="TextReplaceOptions.FontSizeAdjustment"/>), delete the original
    /// paragraph operators, and write the wrapped lines top-anchored inside the
    /// rectangle. Updates <see cref="_text"/>, <see cref="_segments"/> and
    /// <see cref="_rectangle"/> to the laid-out block. Returns false (leaving the
    /// caller to fall back to the in-place swap) when the geometry is unusable.
    /// </summary>
    private bool TryReflowIntoRectangle(string oldText, string newText, Rectangle rect)
    {
        var page = SourcePage!;
        var font = TextState.Font!;
        double baseFs = TextState.FontSize;
        if (rect.Width <= 1 || rect.Height <= 1 || string.IsNullOrEmpty(newText)) return false;

        // Left/RightAdjustment extend the wrap borders (a negative left
        // adjustment widens leftward, a positive right one rightward).
        var leftAdj = _replaceOptions?.LeftAdjustment ?? 0;
        var rightAdj = _replaceOptions?.RightAdjustment ?? 0;
        if (leftAdj != 0 || rightAdj != 0)
            rect = new Rectangle(rect.LLX + leftAdj, rect.LLY, rect.URX + rightAdj, rect.URY);
        // AdjustSpaceWidth justifies every wrapped line but the last to the
        // wrap width by widening the inter-word gaps.
        var justify = _replaceOptions is not null
            && (_replaceOptions.ReplaceAdjustmentAction
                & TextReplaceOptions.ReplaceAdjustment.AdjustSpaceWidth) != 0;

        // Derive the line pitch (baseline-to-baseline) from the fragment's current
        // multi-line layout so the reflowed block keeps the same leading —
        // averaged over the whole first-to-last span, so the per-segment
        // position quantization doesn't accumulate over the reflowed lines.
        double leadingRatio = 1.2;
        if (_segments.Count >= 2 && _segments[1].Position is { } b1
            && _segments[_segments.Count].Position is { } bLast)
        {
            var l = (b1.YIndent - bLast.YIndent) / (_segments.Count - 1) / baseFs;
            if (l > 0.5 && l < 3.0) leadingRatio = l;
            else if (_segments[2].Position is { } b2)
            {
                l = (b1.YIndent - b2.YIndent) / baseFs;
                if (l > 0.5 && l < 3.0) leadingRatio = l;
            }
        }

        double wrapWidth = rect.Width;
        // The source run's text matrix scales every advance it draws, so the width a
        // line occupies on the page is its advance sum times that scale. Measuring
        // and wrapping happen in the font's own space; only the page-space results
        // carry the factor.
        var sx = TextState.SourceTmScale is > 0.01 and < 100 ? TextState.SourceTmScale : 1.0;
        var wrapWidthT = wrapWidth / sx;
        var fit = _replaceOptions?.FontSizeAdjustmentAction ?? TextReplaceOptions.FontSizeAdjustment.None;

        // Same text into a same-size rectangle is a pure TRANSLATION: keep the
        // original line structure (strings, pitch, per-line widths) and move it.
        // Re-wrapping would put every measurement error of a metrics-less font
        // into the block's shape; translation cancels them all.
        // The matched text carries the source line breaks; the replacement has
        // plain spaces — compare whitespace-folded.
        static string FoldWs(string s) =>
            System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        if (fit is TextReplaceOptions.FontSizeAdjustment.None
            && string.Equals(FoldWs(oldText), FoldWs(newText), StringComparison.Ordinal)
            && _segments.Count > 0
            && _segments.All(s => s.Position is not null && s.Rectangle is not null
                && !string.IsNullOrEmpty(s.Text))
            && TryTranslateBlock(page, oldText, newText, rect, baseFs))
        {
            return true;
        }

        // A font resolved without its real width table measures every glyph near
        // an em wide; the original segments carry their TRUE drawn widths, so
        // calibrate the measure against them before wrapping.
        double measureScale = 1.0;
        {
            double measured = 0, actual = 0;
            foreach (var s in _segments)
            {
                if (s.Rectangle is not { } sr || string.IsNullOrEmpty(s.Text) || s.Text.Length < 4) continue;
                double m;
                try { m = font.MeasureString(s.Text, baseFs); } catch { continue; }
                if (m <= 0 || sr.Width <= 0) continue;
                measured += m; actual += sr.Width;
            }
            if (measured > 1 && actual > 1)
            {
                var sc = actual / measured;
                // The calibration is there to correct a face measuring at about an em
                // a glyph. A face that already reproduces its own drawn text measures
                // TRUE, and nudging it by the sample's couple of percent only moves
                // the wrap off the break the real metrics put it on.
                if (sc > 0.2 && sc < 5) measureScale = Math.Abs(sc - 1) <= 0.05 ? 1.0 : sc;
            }
        }

        // An embedded font program measures through its OWN advance table (hmtx at
        // its own units-per-em) — the width table the wrap measurement works from.
        // The PDF /Widths array carries the same advances rounded to integer
        // 1000ths of an em, and that rounding is enough to move a fitted size off
        // by a tenth of a point.
        Func<string, double, double>? hmtxMeasure = null;
        {
            // Only a program the document actually carries: a font that ships none
            // keeps the calibrated width-table measure, whose scale the surrounding
            // flow is already tuned against.
            var ttfData = (TextState.FontData ?? TextState.Font?.SourceFontData)?.TtfData
                ?? TextState.Font?.GetEmbeddedProgramBytes();
            if (ttfData is not null)
            {
                try
                {
                    var gp = new Aspose.Pdf.Text.GlyphOutlineParser(ttfData);
                    var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000;
                    hmtxMeasure = (str, size) =>
                    {
                        double w = 0;
                        foreach (var ch in str)
                            w += gp.GetAdvanceWidth(gp.CMap.TryGetValue(ch, out var g) ? g : 0);
                        return w * size / upm;
                    };
                }
                catch { /* unparseable program — fall back to the width tables */ }
            }
            // A subset program often carries only the glyphs it drew, so a cmap
            // lookup for the replacement's characters can come back empty and
            // measure everything at zero. Sanity-check against the width tables
            // and drop the program measure when it disagrees wildly.
            if (hmtxMeasure is not null)
            {
                var probe = newText.Replace(" ", "");
                double viaProgram = hmtxMeasure(probe, baseFs), viaTables = 0;
                try { viaTables = font.MeasureString(probe, baseFs); } catch { }
                if (viaProgram <= 0
                    || (viaTables > 0 && (viaProgram / viaTables is < 0.5 or > 2.0)))
                    hmtxMeasure = null;
            }
        }

        // A source font that cannot encode the replacement (a Japanese face asked to
        // show Latin, a subset carrying only its own glyphs) is SUBSTITUTED, and the
        // substitute is the serif default — the replacement measures and writes in
        // Times, not in the Helvetica the family map falls back to. Detected by the
        // width table itself: a face with no widths for these characters reports
        // about a full em each.
        var substFace = ResolveSubstituteFace(font, newText);
        // A stand-in has to be able to SHOW the replacement. The serif default carries
        // Latin, Greek, Cyrillic and the common symbols, but not a dingbat like '★', a
        // circled numeral, kana or han — and ONE character it has no glyph for re-dresses
        // the WHOLE run in a face that does. That is why a line of ordinary English can
        // come back measured at half-width advances: the covering face sets its Latin at
        // half an em, so a run led by one star fits a size well below the serif one.
        var coveringFace = substFace is null ? null : CoveringSubstituteFace(substFace, newText);
        var coveringGlyphs = coveringFace is null ? null : CjkFallbackFont.ResolveNamed(coveringFace);
        if (coveringGlyphs is null) coveringFace = null;

        // The stand-in follows the SHAPE of the font it stands in for. Replacing a CID
        // font's run produces a composite stand-in carrying the installed face's own
        // advances; standing in for a simple font keeps a simple one, whose widths are
        // the core table's. The extent the page reports back follows that choice, so
        // the measure has to make it too.
        var substWidths = substFace is null || font.Subtype != "Type0" || coveringFace is not null
            ? null
            : SystemFaceWidths(substFace);
        var substMeasure = substFace is null
            ? null
            : coveringFace is not null
            // The covering face is addressed by CHARACTER, not by byte code: the very
            // glyph that forced the switch lives outside any single-byte encoding.
            ? (str, size) =>
            {
                var upm = coveringGlyphs!.UnitsPerEm > 0 ? coveringGlyphs.UnitsPerEm : 1000;
                double w = 0;
                foreach (var ch in str)
                    w += coveringGlyphs.GetAdvanceWidth(
                        coveringGlyphs.CMap.TryGetValue(ch, out var g) ? g : 0) * size / upm;
                return w;
            }
            : substWidths is null
            ? Standard14Measurer(substFace)
            : (str, size) =>
            {
                double w = 0;
                foreach (var ch in str) w += substWidths[ch < 256 ? ch : '?'] * size / 1000.0;
                return w;
            };

        // Prefer measuring with the source resources' own width tables; the
        // calibrated host-face measure (wrapWidthM compensates its scale) is
        // the fallback.
        var srcMeasure = BuildSourceMeasurer(page);
        if (substMeasure is not null) srcMeasure = null;
        if (srcMeasure is not null && srcMeasure(newText.Replace(" ", ""), baseFs) < 0)
            srcMeasure = null; // source fonts can't encode the replacement
        // A width table that does not reproduce the widths of the text the page
        // actually drew with it is not the table the replacement should wrap by.
        if (srcMeasure is not null)
        {
            double viaSrc = 0, drawn = 0;
            foreach (var s in _segments)
            {
                if (s.Rectangle is not { } sr || string.IsNullOrEmpty(s.Text) || s.Text.Length < 4) continue;
                var m = srcMeasure(s.Text, baseFs);
                if (m <= 0 || sr.Width <= 0) continue;
                viaSrc += m; drawn += sr.Width;
            }
            if (viaSrc > 1 && drawn > 1 && Math.Abs(viaSrc / drawn - 1.0) > 0.05)
                srcMeasure = null;
        }

        // The fit/wrap measures run through the font; feed them the width the
        // MEASURE scale sees so the drawn result lands in the real rectangle.
        if (substMeasure is not null) hmtxMeasure = substMeasure;
        var wrapWidthM = hmtxMeasure is not null ? wrapWidth : wrapWidth / measureScale;
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
        {
            double mNew = 0; try { mNew = font.MeasureString(newText, baseFs); } catch { }
            double mSrc = srcMeasure is not null ? srcMeasure(newText, baseFs) : -1;
            Console.Error.WriteLine($"[fit] sx={sx:F6} baseFs={baseFs:F4} rect=({rect.LLX:F2},{rect.LLY:F2},{rect.URX:F2},{rect.URY:F2}) H={rect.Height:F3} wrapWidth={wrapWidth:F3} scale={measureScale:F4} wrapWidthM={wrapWidthM:F3} hmtx={(hmtxMeasure is not null)} measureFont={mNew:F3} measureSrc={mSrc:F3} fit={fit}");
        }
        double fs = baseFs;
        if (fit is TextReplaceOptions.FontSizeAdjustment.ShrinkToFit
                or TextReplaceOptions.FontSizeAdjustment.Decrease)
            fs = FitFontSize(newText, font, wrapWidthM / sx, rect.Height, 0.0, baseFs, leadingRatio, hmtxMeasure);
        else if (fit is TextReplaceOptions.FontSizeAdjustment.ScaleToFill
                or TextReplaceOptions.FontSizeAdjustment.Increase)
        {
            // ScaleToFill fills the rectangle in whichever direction it must: text
            // that already overruns the height at its own size scales DOWN to fit.
            // Increase only ever grows, so it keeps the one-sided window.
            var overruns = fit is TextReplaceOptions.FontSizeAdjustment.ScaleToFill
                && BlockHeight(newText, font, baseFs, wrapWidthM / sx, leadingRatio, hmtxMeasure) > rect.Height;
            fs = overruns
                ? FitFontSize(newText, font, wrapWidthM / sx, rect.Height, 0.0, baseFs, leadingRatio, hmtxMeasure)
                : FitFontSize(newText, font, wrapWidthM / sx, rect.Height, baseFs, 400.0, leadingRatio, hmtxMeasure);
        }

        // Wrapped lines keep the break's inter-word space (the last line ends at
        // its final word), so each re-absorbed line extent includes the trailing
        // space advance — the same extent the source lines report.
        var lines = hmtxMeasure is not null
            ? WrapToWidth(newText, font, fs, wrapWidthT, trailingSpace: true, measure: s => hmtxMeasure(s, fs))
            : srcMeasure is not null
            ? WrapToWidth(newText, font, fs, wrapWidthT, trailingSpace: true, measure: s => srcMeasure(s, fs))
            : WrapToWidth(newText, font, fs, wrapWidthM / sx, trailingSpace: true);
        if (lines.Count == 0) return false;
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
            Console.Error.WriteLine($"[fit2] fs={fs:F6} lines={lines.Count} subst={substFace ?? "none"} sysW={(substWidths is not null)}");

        double leading = leadingRatio * fs;
        // A block whose size the fitter actually CHANGED distributes its lines over
        // the FULL rectangle — in both directions: the baseline pitch becomes the
        // rect height over the line count. A size the fitter left alone (the text
        // already fits, so nothing was shrunk) keeps the glyph size's own leading.
        // A FILL mode always paces its lines over the whole rectangle, and so does any
        // fit that actually changed the size. A shrink that found the text already
        // fitting leaves both the size and the block's own leading alone.
        if (lines.Count > 1 && rect.Height > 0
            && (fit is TextReplaceOptions.FontSizeAdjustment.ScaleToFill
                    or TextReplaceOptions.FontSizeAdjustment.Increase
                || (fit is not TextReplaceOptions.FontSizeAdjustment.None
                    && Math.Abs(fs - baseFs) > 1e-6)))
            leading = rect.Height / lines.Count;
        // Anchor the block so its re-absorbed top matches rect.URY. The wrapped
        // lines are written through TextBuilder, which maps a non-embedded font
        // to a Standard-14 face; TextFragmentAbsorber then reconstructs that
        // run's box as URY = baseline + (1.1·fs + descentOff) and
        // LLY = baseline + descentOff (descentOff negative). Use the WRITTEN
        // font's descent (not the original run's ascent) so the anchor lines up.
        var writtenFontName = substFace ?? TextBuilder.MapToStandard14Public(TextState);
        // An Arial-family source is re-fonted with the system Arial face rather
        // than the Helvetica AFM face: the written resource carries Arial's own
        // vertical metrics (hhea descender -434 in a 2048 em, truncated to -211)
        // in a FontDescriptor, and the anchor math below runs on that descent.
        var arialFace = font.SourceFontData is null && writtenFontName == "Helvetica"
            && IsArialFamily(TextState.FontName);
        if (arialFace) writtenFontName = "Arial";
        double descentOff = font.SourceFontData is null
            ? Standard14Fonts.GetWrittenFaceDescent(writtenFontName) * fs / 1000.0
            : (font.GetMetrics()?.Descent ?? -212) * fs / 1000.0;
        double ascentH = fs * 1.1 + descentOff;
        double firstBaseline = rect.URY - ascentH;
        // Baseline grid for the SECOND and later lines. On the re-fonted path the
        // first line keeps the source baseline itself while the rest of the block
        // sits on the descent-corrected grid — the two differ by exactly the
        // source-vs-written descent delta, so the re-absorbed box top follows the
        // written face while the bottom stays on the source box model.
        double gridFirst = firstBaseline;
        // When the ORIGINAL baseline grid is recoverable, continue from it (plus
        // whatever vertical shift the target rectangle carries relative to the
        // absorbed box) — the reflowed text keeps the source's exact first
        // baseline and pitch instead of re-derived approximations of them.
        // Read both straight from the block's own positioning operators; fall
        // back to the segment-position reconstruction.
        if (fs == baseFs && AbsorbedRectangle is { } abr)
        {
            var shift = rect.URY - abr.URY;
            var dSrc = SourceDescentUnits(page) * baseFs / 1000.0;
            if (dSrc != 0 && TryGetSourceTopBaseline(page, abr, out var srcTop)
                && srcTop < abr.URY && srcTop > abr.URY - 3 * fs)
            {
                // Corrected by the descent difference between the source face
                // and the written face, the re-absorbed box edges land where
                // the SOURCE font's box model puts them. On the re-fonted path
                // only the GRID gets that correction; the first line stays on
                // the source baseline, so the box top follows the written face.
                gridFirst = srcTop + (dSrc - descentOff) + shift;
                firstBaseline = arialFace ? srcTop + shift : gridFirst;
            }
            else if (_segments.Count > 0 && _segments[1].Position is { } sp1)
            {
                var srcB1 = sp1.YIndent - dSrc;
                if (srcB1 < rect.URY && srcB1 > rect.URY - 3 * fs)
                {
                    firstBaseline = srcB1 + shift;
                    gridFirst = arialFace ? srcB1 + (dSrc - descentOff) + shift : firstBaseline;
                }
            }
        }

        // Remove the original paragraph: delete each source line operator at its
        // own baseline Y so a repeated substring elsewhere on the page is
        // untouched — then sweep the absorbed region for whatever a fragmented
        // multi-run line left behind (a paragraph drawn as dozens of kerned
        // sub-runs never deletes cleanly by text matching alone).
        DeleteReflowSource(page, oldText);
        // The region sweep is a FALLBACK for a paragraph drawn as dozens of kerned
        // sub-runs, which text matching cannot delete cleanly. Run it only when the
        // delete actually left the source behind, and even then take whole text
        // blocks only, so a block that also carries neighbouring text survives.
        {
            double sLLX = double.MaxValue, sLLY = double.MaxValue;
            double sURX = double.MinValue, sURY = double.MinValue;
            if (AbsorbedRectangle is { } ab0)
            {
                sLLX = ab0.LLX; sLLY = ab0.LLY; sURX = ab0.URX; sURY = ab0.URY;
            }
            foreach (var s in _segments)
            {
                if (s.Rectangle is not { } sr) continue;
                if (sr.LLX < sLLX) sLLX = sr.LLX;
                if (sr.LLY < sLLY) sLLY = sr.LLY;
                if (sr.URX > sURX) sURX = sr.URX;
                if (sr.URY > sURY) sURY = sr.URY;
            }
            if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
                Console.Error.WriteLine($"[sweep] abs={(AbsorbedRectangle is null ? "null" : AbsorbedRectangle.ToString())} segs={_segments.Count} rect=({sLLX:F2},{sLLY:F2},{sURX:F2},{sURY:F2})");
            if (sURX > sLLX && sURY > sLLY)
            {
                var sweep = new Rectangle(sLLX - 1, sLLY - 1, sURX + 1, sURY + 1);
                // The sweep strips whole BT…ET blocks, so it can take neighbouring
                // text with it. It exists for the paragraph drawn as dozens of
                // kerned sub-runs that text matching cannot delete cleanly — so run
                // it only when ink actually SURVIVED inside the region, and never
                // when a neighbour would be caught in the same block.
                // A single-segment source was one plain run: the text-matched delete
                // above removes it whole, so sweeping the region only risks taking a
                // line-mate drawn in the same text block. Keep the sweep for the
                // many-sub-run paragraph it exists for.
                // ★ And only when that delete actually LEFT SOMETHING BEHIND. It reports
                // how many runs it removed, so a paragraph whose every segment was
                // matched and deleted has nothing left to sweep, and sweeping it anyway
                // destroys every line-mate sharing its text block — a six-glyph line
                // took the whole five-line box around it with it.
                if (_segments.Count > 1 && _unresolvedSegments > 0)
                    TableAbsorber.RemoveTableContent(page, sweep, textOnly: true);

                // A rule the source drew UNDER this text belongs to it and goes with it —
                // left behind, it underscores whatever words now occupy that space, at the
                // old text's width. The band is the replaced text's OWN extent, and only a
                // path lying wholly inside it is claimed, so a page rule running beneath
                // the line (wider than the line) is not mistaken for its underline.
                var ulBand = new Rectangle(sLLX - 0.5, sLLY - 0.30 * baseFs, sURX + 0.5, sLLY + 0.06 * baseFs);
                TableAbsorber.RemoveTableContent(page, ulBand, decorationOnly: true);
            }
        }

        // Write the wrapped lines as positioned fragments.
        var tb = new TextBuilder(page);
        double maxW = 0;
        TextFragment MakeLine(string text, double x, double y)
        {
            var lf = new TextFragment(text);
            lf.TextState.Font = font;
            lf.TextState.FontName = TextState.FontName;
            lf.TextState.FontSize = (float)fs;
            lf.TextState.IsBold = TextState.IsBold;
            lf.TextState.IsItalic = TextState.IsItalic;
            // Weight the source got by STROKING its own outline travels with the
            // replacement: the stand-in is the regular face either way, so dropping the
            // mode is what turns a bold heading into a light one.
            lf.TextState.RenderingMode = TextState.RenderingMode;
            if (TextState.StrokingColor is { } strokeCol) lf.TextState.StrokingColor = strokeCol;
            if (TextState.ForegroundColor is { } fg) lf.TextState.ForegroundColor = fg;
            if (arialFace)
            {
                lf.TextState.Std14FaceOverride = writtenFontName;
                lf.TextState.EmitStandard14Descriptor = true;
                // Written in the system face, the resource carries that face's own
                // advances — the same choice already made for its vertical metrics.
                // Arial is drawn on a 2048-unit em, so its advances are not whole
                // 1000ths, and the core table's rounded ones make the extent read
                // back a twentieth of a point short over a paragraph.
                lf.TextState.Std14Widths = SystemFaceWidths(writtenFontName);
            }
            else if (substFace is not null)
            {
                lf.TextState.Std14FaceOverride = substFace;
                lf.TextState.Std14Widths = substWidths;
            }
            lf.TextState.SourceTmScale = sx;
            lf.Position = new Position(x, y);
            return lf;
        }
        double Measure(string s)
        {
            // The written face measures the result: a substituted run reports the
            // stand-in's advances, not the source font's.
            if (hmtxMeasure is not null) return hmtxMeasure(s, fs);
            if (srcMeasure is not null)
            {
                var v = srcMeasure(s, fs);
                if (v >= 0) return v;
            }
            try { return font.MeasureString(s, fs) * measureScale; }
            catch { return s.Length * fs * 0.5; }
        }
        for (var i = 0; i < lines.Count; i++)
        {
            var lineY = i == 0 ? firstBaseline : gridFirst - i * leading;
            var words = justify
                ? lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                : null;
            if (words is { Length: > 1 })
            {
                // Justified line: spread the words so the line's ink spans the
                // wrap width exactly — the widened inter-word gaps read back as
                // the stretched spaces of the justification. The last line keeps
                // its natural spacing. Each line ends with its own trailing
                // space run positioned at the line's ink end (like the source
                // lines it replaces), so the line's reported extent follows the
                // SOURCE advances, not the written face's.
                var wordW = new double[words.Length];
                double inkW = 0;
                for (var wi = 0; wi < words.Length; wi++) { wordW[wi] = Measure(words[wi]); inkW += wordW[wi]; }
                var lastLine = i == lines.Count - 1;
                var gap = lastLine
                    ? Measure(" ")
                    : (wrapWidthT - inkW) / (words.Length - 1);
                if (gap > 0)
                {
                    // Justification seats the last NON-SPACE character on the wrap
                    // edge; the break's own space then hangs past it at its natural
                    // advance. The last line is not justified and ends at its final
                    // word. The final run's reported right edge is its start plus
                    // the WRITTEN face's advance, so anchor it by that.
                    var lineEnd = lastLine
                        ? rect.LLX + (inkW + gap * (words.Length - 1)) * sx
                        : rect.LLX + wrapWidth + Measure(" ") * sx;
                    var written = TextBuilder.MapToStandard14Public(TextState);
                    double WrittenWidth(string s)
                    {
                        // The written run's bytes are WinAnsi — measure the same
                        // codes the re-absorber will (en dash lives at 0x96, not
                        // U+2013).
                        double w0 = 0;
                        foreach (var c in s)
                        {
                            var gw = Standard14Fonts.GetWidth(written,
                                Aspose.Pdf.Content.ContentStreamBuilder.ToWinAnsi(c));
                            if (gw > 0) w0 += gw * fs / 1000.0;
                        }
                        return w0;
                    }
                    // The final run must span at least an em: the absorber's
                    // line-break sentinel occupies a 1-em box at the last run's
                    // start, and a shorter final run would let it poke past the
                    // line's true end. Fold preceding words in until it doesn't.
                    var lastCount = 1;
                    string lastText;
                    double wWritten;
                    while (true)
                    {
                        var tail = string.Join(" ", words[(words.Length - lastCount)..]);
                        lastText = lastLine ? tail : tail + " ";
                        wWritten = WrittenWidth(lastText);
                        if (wWritten <= 0) { wWritten = wordW[^1] + Measure(" "); break; }
                        if (wWritten >= fs || lastCount >= words.Length) break;
                        lastCount++;
                    }

                    var x = 0.0;
                    for (var wi = 0; wi < words.Length - lastCount; wi++)
                    {
                        tb.AppendText(MakeLine(words[wi], rect.LLX + x * sx, lineY));
                        x += wordW[wi] + gap;
                    }
                    tb.AppendText(MakeLine(lastText, lineEnd - wWritten * sx, lineY));
                    if (lineEnd - rect.LLX > maxW) maxW = lineEnd - rect.LLX;
                    continue;
                }
            }
            tb.AppendText(MakeLine(lines[i], rect.LLX, lineY));
            var w = Measure(lines[i]) * sx;
            if (w > maxW) maxW = w;
        }

        // The delete (SetContentStream) + line writes (AddContentStream) edited the
        // raw /Contents; drop any materialised typed-operator view so a later
        // page.Contents use (and save) re-reads the reflowed content.
        page.ResetContentsCache();

        // Update this fragment to the laid-out block (matching the absorber's
        // baseline+descentOff floor and baseline+ascentH top).
        double lastBaseline = lines.Count == 1
            ? firstBaseline
            : gridFirst - (lines.Count - 1) * leading;
        // ★ The reported box is ONE LINE deep whatever the block wraps to: the extent a
        // replaced paragraph reports back is its top edge less a single line's height,
        // not the height of the lines it actually drew. A block that fills its rectangle
        // reports the same box as one that half-fills it, which is why every case here
        // wants h = leading·fs exactly. The DRAWN lines are untouched — this is what the
        // paragraph reports, not where its text sits.
        var reportedURY = arialFace ? firstBaseline + ascentH : rect.URY;
        // The reported line box is 1.1 em — the face-independent line height a replaced
        // paragraph reports back, NOT the flow's own leading (which is 1.2 here and is
        // what spaces the drawn lines apart).
        const double reportedLineHeightEm = 1.1;
        var singleLineLLY = reportedURY - reportedLineHeightEm * fs;
        // Keep the drawn-block floor when it is the SHALLOWER of the two: a one-line
        // block already reports its own line, and a source whose descent puts the floor
        // above that must not be pushed down by the nominal line height.
        var reportedLLY = lines.Count == 1
            ? Math.Max(lastBaseline + descentOff, singleLineLLY)
            : singleLineLLY;
        _rectangle = new Rectangle(rect.LLX, reportedLLY, rect.LLX + maxW, reportedURY);
        _text = newText;
        _segments.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            var seg = new TextSegment(lines[i]);
            seg.TextState.FontSize = (float)fs;
            seg.TextState.FontName = TextState.FontName;
            seg.TextState.Font = font;
            seg.Owner = this;
            seg.Position = new Position(rect.LLX, i == 0 ? firstBaseline : gridFirst - i * leading);
            seg.TextState.OwnerSegment = seg;
            _segments.Add(seg);
        }
        return true;
    }

    /// <summary>Regular Arial-family name (subset prefixes stripped): the reflow
    /// re-fonts such runs with the system Arial face and its own metrics.</summary>
    private static bool IsArialFamily(string? fontName)
    {
        if (string.IsNullOrEmpty(fontName)) return false;
        var stem = fontName!.ToLowerInvariant().Replace(" ", "");
        var plus = stem.IndexOf('+');
        if (plus >= 0 && plus + 1 < stem.Length) stem = stem[(plus + 1)..];
        return stem.StartsWith("arial", StringComparison.Ordinal)
            && !stem.Contains("bold") && !stem.Contains("italic");
    }

    /// <summary>Recover the source block's TOP baseline from its own positioning
    /// operators (full precision, unlike the quantized segment positions):
    /// identity text matrices set inside the absorbed region, and the absolute
    /// Td straight after a BT.</summary>
    private static bool TryGetSourceTopBaseline(Page page, Rectangle region, out double top)
    {
        top = double.MinValue;
        var afterBt = false;
        // A bare positioning op is not a line: empty marker runs park the pen
        // above the block's first baseline (and repeat it after the last line).
        // Only a baseline that text is actually SHOWN at counts.
        double? pending = null;
        page.Contents.EnsureMaterialized();
        foreach (var op in page.Contents)
        {
            switch (op)
            {
                case Aspose.Pdf.Operators.BT:
                    afterBt = true;
                    pending = null;
                    break;
                case Aspose.Pdf.Operators.SetTextMatrix tm:
                    pending = Math.Abs(tm.A - 1) < 1e-6 && Math.Abs(tm.B) < 1e-6
                        && Math.Abs(tm.C) < 1e-6 && Math.Abs(tm.D - 1) < 1e-6
                        && tm.E >= region.LLX - 2 && tm.E <= region.URX + 2
                        && tm.F >= region.LLY - 2 && tm.F <= region.URY + 2
                        ? tm.F : null;
                    afterBt = false;
                    break;
                case Aspose.Pdf.Operators.MoveTextPosition td:
                    // Absolute only straight after BT (line matrix = identity).
                    pending = afterBt
                        && td.X >= region.LLX - 2 && td.X <= region.URX + 2
                        && td.Y >= region.LLY - 2 && td.Y <= region.URY + 2
                        ? td.Y : null;
                    afterBt = false;
                    break;
                case Aspose.Pdf.Operators.TextShowOperator:
                    if (pending is { } y && y > top) top = y;
                    break;
                case Aspose.Pdf.Operators.TextPlaceOperator:
                    afterBt = false;
                    pending = null;
                    break;
            }
        }
        return top > double.MinValue;
    }

    /// <summary>The /Descent (1/1000 units, negative) of the first page font
    /// resource matching this fragment's family — the same value the absorber
    /// used when it anchored the original segments' positions. Zero when no
    /// matching resource carries one.</summary>
    private double SourceDescentUnits(Page page)
    {
        var reader = page.Reader;
        var fonts = Aspose.Pdf.Text.TextAbsorber.ResolveFonts(page.Dict, reader);
        var family = TextState.FontName ?? "";
        // A page can carry several same-family faces with different descents
        // (a subset "…+ArialMT" title next to the paragraph's "Arial"): the
        // fragment's own face — exact family equality — wins over a substring
        // relative; the loose match is only the no-exact-hit fallback.
        double loose = 0;
        foreach (var (_, fd) in fonts)
        {
            var bf = fd.GetName("BaseFont") ?? "";
            var plus = bf.IndexOf('+');
            if (plus >= 0 && plus + 1 < bf.Length) bf = bf[(plus + 1)..];
            var bfFamily = bf.Split('-')[0].Split(',')[0];
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(bfFamily)) continue;
            var exact = string.Equals(bfFamily, family, StringComparison.OrdinalIgnoreCase);
            if (!exact && !bfFamily.Contains(family, StringComparison.OrdinalIgnoreCase)
                && !family.Contains(bfFamily, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var fm = FontMetrics.FromFontDict(fd, reader);
                if (fm is not null && fm.Descent != 0)
                {
                    if (exact) return fm.Descent;
                    if (loose == 0) loose = fm.Descent;
                }
            }
            catch { }
        }
        return loose;
    }

    /// <summary>Measure text with the SOURCE font resources' own width tables
    /// (the tables re-absorption measures with), instead of a host-face
    /// approximation. Returns null when the page has no font of this family;
    /// the returned function yields a negative value for unencodable text.</summary>
    private Func<string, double, double>? BuildSourceMeasurer(Page page)
    {
        var reader = page.Reader;
        var fonts = Aspose.Pdf.Text.TextAbsorber.ResolveFonts(page.Dict, reader);
        var family = TextState.FontName ?? "";
        var cands = new List<(FontMetrics M, bool Cid, Dictionary<char, int>? Rev)>();
        foreach (var (_, fd) in fonts)
        {
            var bf = fd.GetName("BaseFont") ?? "";
            var plus = bf.IndexOf('+');
            if (plus >= 0 && plus + 1 < bf.Length) bf = bf[(plus + 1)..];
            var bfFamily = bf.Split('-')[0].Split(',')[0];
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(bfFamily)
                || (!bfFamily.Contains(family, StringComparison.OrdinalIgnoreCase)
                    && !family.Contains(bfFamily, StringComparison.OrdinalIgnoreCase)))
                continue;
            FontMetrics? fm;
            try { fm = FontMetrics.FromFontDict(fd, reader); } catch { continue; }
            if (fm is null) continue;
            var cid = fd.GetName("Subtype") == "Type0";
            Dictionary<char, int>? rev = null;
            if (cid)
            {
                var tu = Aspose.Pdf.Text.TextAbsorber.ParseToUnicodeFromDict(fd, reader);
                if (tu is null) continue;
                rev = new Dictionary<char, int>();
                foreach (var (code, str) in tu)
                    if (str.Length == 1 && !rev.ContainsKey(str[0])) rev[str[0]] = code;
            }
            cands.Add((fm, cid, rev));
        }
        if (cands.Count == 0) return null;
        return (s, sz) =>
        {
            foreach (var (fm, cid, rev) in cands)
            {
                double w = 0;
                var ok = true;
                foreach (var c in s)
                {
                    var code = cid ? (rev!.TryGetValue(c, out var cc) ? cc : -1) : c;
                    if (code < 0) { ok = false; break; }
                    var gw = fm.GetWidth(code);
                    if (gw <= 0 && c != ' ') { ok = false; break; }
                    w += gw;
                }
                if (ok) return w * sz / 1000.0;
            }
            return -1;
        };
    }

    /// <summary>Pure translation of an unchanged paragraph into a same-size
    /// rectangle: shift the block's own positioning operators (Tm, and a
    /// leading Td straight after BT) by the move delta, leaving the show
    /// strings, fonts, and kerning untouched — the re-absorbed block reproduces
    /// the original geometry exactly, moved by the shift.</summary>
    private bool TryTranslateBlock(Page page, string oldText, string newText, Rectangle rect, double baseFs)
    {
        // The caller may have handed the fragment's own Rectangle instance back
        // as the target (mutated in place) — recover the PRE-shift geometry from
        // the absorbed-box snapshot (exact), falling back to the segment union.
        double obLLX = double.MaxValue, obLLY = double.MaxValue;
        double obURX = double.MinValue, obURY = double.MinValue;
        if (AbsorbedRectangle is { } ab)
        {
            obLLX = ab.LLX; obLLY = ab.LLY; obURX = ab.URX; obURY = ab.URY;
        }
        else
        {
            foreach (var s in _segments)
            {
                var sr = s.Rectangle!;
                if (sr.LLX < obLLX) obLLX = sr.LLX;
                if (sr.LLY < obLLY) obLLY = sr.LLY;
                if (sr.URX > obURX) obURX = sr.URX;
                if (sr.URY > obURY) obURY = sr.URY;
            }
        }
        var dbg = Environment.GetEnvironmentVariable("ASPOSE_FOSS_GRIDDEBUG") == "1";
        if (Math.Abs(rect.Width - (obURX - obLLX)) >= 0.5
            || Math.Abs(rect.Height - (obURY - obLLY)) >= 0.5)
        {
            if (dbg) Console.Error.WriteLine($"[xlate] size mismatch rect={rect.Width:F2}x{rect.Height:F2} ob={(obURX - obLLX):F2}x{(obURY - obLLY):F2}");
            return false;
        }

        var dx = rect.LLX - obLLX;
        var dy = rect.URY - obURY;

        // Region gate for positioning ops: the block's own coordinates, padded
        // for the baseline-vs-rect-bottom offset. Anything else on the page
        // stays untouched.
        var padX = 2.0;
        var padY = Math.Max(4.0, baseFs * 0.5);
        bool InRegion(double x, double y) =>
            x >= obLLX - padX && x <= obURX + padX
            && y >= obLLY - padY && y <= obURY + padY;

        var shifted = 0;
        var afterBt = false;
        var toReplace = new List<Aspose.Pdf.Operators.SetTextMatrix>();
        // Materialize so the enumerated instances are the collection's own
        // (mutations persist and Index is stamped for the delete/insert swap).
        page.Contents.EnsureMaterialized();
        foreach (var op in page.Contents)
        {
            switch (op)
            {
                case Aspose.Pdf.Operators.BT:
                    afterBt = true;
                    break;
                case Aspose.Pdf.Operators.SetTextMatrix tm
                    when Math.Abs(tm.A - 1) < 1e-6 && Math.Abs(tm.B) < 1e-6
                        && Math.Abs(tm.C) < 1e-6 && Math.Abs(tm.D - 1) < 1e-6
                        && InRegion(tm.E, tm.F):
                    toReplace.Add(tm);
                    afterBt = false;
                    break;
                case Aspose.Pdf.Operators.MoveTextPosition td
                    when afterBt && InRegion(td.X, td.Y):
                    // Td straight after BT is absolute (line matrix = identity).
                    td.X += dx;
                    td.Y += dy;
                    shifted++;
                    afterBt = false;
                    break;
                case Aspose.Pdf.Operators.TextPlaceOperator:
                    afterBt = false;
                    break;
            }
        }
        foreach (var tm in toReplace)
        {
            var at = tm.Index;
            page.Contents.Delete(new Aspose.Pdf.Operator[] { tm });
            page.Contents.Insert(at, new Aspose.Pdf.Operators.SetTextMatrix(
                tm.A, tm.B, tm.C, tm.D, tm.E + dx, tm.F + dy));
            shifted++;
        }
        if (shifted == 0)
        {
            if (dbg) Console.Error.WriteLine("[xlate] no positioning ops found in region");
            return false;
        }
        page.Contents.FlushToPage();
        if (dbg) Console.Error.WriteLine($"[xlate] shifted {shifted} positioning ops by ({dx:F2},{dy:F2})");

        _rectangle = new Rectangle(obLLX + dx, obLLY + dy, obURX + dx, obURY + dy);
        _text = newText;
        foreach (var s in _segments)
        {
            s.Position = new Position(s.Position!.XIndent + dx, s.Position.YIndent + dy);
            s.Rectangle = new Rectangle(s.Rectangle!.LLX + dx, s.Rectangle.LLY + dy,
                s.Rectangle.URX + dx, s.Rectangle.URY + dy);
        }
        return true;
    }

    /// <summary>Delete the pre-reflow paragraph text. Removes each current
    /// segment's line at its own baseline Y (falls back to a page-wide delete of
    /// the joined text when segment positions are unavailable).</summary>
    /// <summary>The Standard-14 face a replacement must be written in when the
    /// source font cannot encode it — a subset carrying only its own glyphs, or a
    /// face whose width table answers every character with the same default. The
    /// serif default stands in for a family the system cannot resolve; a font that
    /// really does carry the glyphs returns null and keeps its own face.</summary>
    private string? ResolveSubstituteFace(FontInfo font, string newText)
    {
        var mapped = TextBuilder.MapToStandard14Public(TextState);
        var rawName = (TextState.FontName ?? "").Replace(" ", "");
        var familyKnown = !mapped.StartsWith("Helvetica", StringComparison.Ordinal)
            || rawName.StartsWith("Arial", StringComparison.OrdinalIgnoreCase)
            || rawName.StartsWith("Helvetica", StringComparison.OrdinalIgnoreCase);
        var probe = newText.Trim();
        if (familyKnown || probe.Length == 0) return null;
        // The trigger is real glyph coverage: one character the source font cannot
        // represent re-dresses the whole run. A width table that answers every
        // character with the same default is the same story told in widths.
        // An Identity-H CID subset addresses glyphs by the subset's own ids: text it
        // never carried has no id to write, so such a run always re-dresses.
        var lacksGlyph = font.Subtype == "Type0";
        try
        {
            if (!lacksGlyph)
                foreach (var ch in probe)
                    if (!char.IsWhiteSpace(ch) && !font.CanRepresent(ch)) { lacksGlyph = true; break; }
        }
        catch { return null; }
        if (!lacksGlyph)
        {
            double own = 0, wi = 0, wM = 0, wW = 0;
            try
            {
                own = font.MeasureString(probe, 1);
                wi = font.MeasureString("i", 1);
                wM = font.MeasureString("M", 1);
                wW = font.MeasureString("W", 1);
            }
            catch { return null; }
            var uniformWidths = wi > 0 && Math.Abs(wi - wM) < 1e-9 && Math.Abs(wM - wW) < 1e-9;
            if (!uniformWidths && own / probe.Length < 0.9) return null;
        }
        // Standing in for a COMPOSITE font, the stand-in is the default serif face
        // itself — that choice does not follow the source's weight or slope, so a
        // bold CID run is re-dressed in the regular face.
        if (font.Subtype == "Type0") return "Times-Roman";
        return TextState.IsBold
            ? (TextState.IsItalic ? "Times-BoldItalic" : "Times-Bold")
            : (TextState.IsItalic ? "Times-Italic" : "Times-Roman");
    }

    /// <summary>The face a stand-in hands off to when it has no glyph for some character
    /// of the replacement. One name covers the cases that arise — dingbats, circled
    /// numerals, kana and han — because the covering face is a full CJK font.</summary>
    private const string CjkStandInFace = "MS-Gothic";

    /// <summary>
    /// The face that must stand in for <paramref name="face"/> because
    /// <paramref name="text"/> contains a character it cannot show, or null when the
    /// stand-in already covers every character. Coverage is asked of the INSTALLED
    /// face's own cmap — the core width tables answer for characters the real font has
    /// no outline for, so they cannot be used to decide this.
    /// </summary>
    private static string? CoveringSubstituteFace(string face, string text)
    {
        var glyphs = SystemFaceGlyphs(face);
        if (glyphs is null) return null;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch)) continue;
            if (!glyphs.CMap.ContainsKey(ch)) return CjkStandInFace;
        }
        return null;
    }

    private static readonly Dictionary<string, GlyphOutlineParser?> _systemFaceGlyphs = new();

    /// <summary>The installed face behind a stand-in name, parsed once. Null when the
    /// face is not on this machine.</summary>
    private static GlyphOutlineParser? SystemFaceGlyphs(string face)
    {
        lock (_systemFaceGlyphs)
        {
            if (_systemFaceGlyphs.TryGetValue(face, out var cached)) return cached;
            GlyphOutlineParser? parser = null;
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                var file = SystemFaceFile(face);
                var path = file is null || dir.Length == 0 ? null : System.IO.Path.Combine(dir, file);
                if (path is not null && System.IO.File.Exists(path))
                    parser = new GlyphOutlineParser(System.IO.File.ReadAllBytes(path));
            }
            catch { parser = null; }
            _systemFaceGlyphs[face] = parser;
            return parser;
        }
    }

    private static string? SystemFaceFile(string face) => face switch
    {
        "Times-Roman" => "times.ttf",
        "Times-Bold" => "timesbd.ttf",
        "Times-Italic" => "timesi.ttf",
        "Times-BoldItalic" => "timesbi.ttf",
        "Arial" => "arial.ttf",
        _ => null,
    };

    private static readonly Dictionary<string, double[]?> _systemFaceWidths = new();

    /// <summary>Advance widths of the INSTALLED face behind a stand-in, in 1000ths of
    /// an em and left unrounded — the finer table a substituted replacement is
    /// measured through. Indexed by character code (the run is written WinAnsi, and
    /// the codes a substitution carries are Latin). Null when the face is not on this
    /// machine, which falls the caller back to the core width table.</summary>
    private static double[]? SystemFaceWidths(string face)
    {
        lock (_systemFaceWidths)
        {
            if (_systemFaceWidths.TryGetValue(face, out var cached)) return cached;
            double[]? table = null;
            var file = SystemFaceFile(face);
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                var path = file is null || dir.Length == 0 ? null : System.IO.Path.Combine(dir, file);
                if (path is not null && System.IO.File.Exists(path))
                {
                    var gp = new GlyphOutlineParser(System.IO.File.ReadAllBytes(path));
                    var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000;
                    var t = new double[256];
                    for (var c = 0; c < 256; c++)
                    {
                        var w = gp.CMap.TryGetValue((char)c, out var gid)
                            ? gp.GetAdvanceWidth(gid) * 1000.0 / upm
                            : -1;
                        // A code the face does not carry keeps the core table's answer,
                        // so a partial cmap cannot silently zero a glyph's advance.
                        if (w < 0) { var cw = Standard14Fonts.GetWidth(face, (char)c); w = cw >= 0 ? cw : 500; }
                        t[c] = w;
                    }
                    table = t;
                }
            }
            catch { table = null; }
            _systemFaceWidths[face] = table;
            return table;
        }
    }

    /// <summary>Measure through the stand-in's own width table — the same table the
    /// face is WRITTEN with, so what the flow measures is what the page reports back.</summary>
    private static Func<string, double, double> Standard14Measurer(string face) =>
        (str, size) =>
        {
            double w = 0;
            foreach (var ch in str)
            {
                var cw = Standard14Fonts.GetWidth(face, ch < 256 ? ch : '?');
                w += (cw >= 0 ? cw : 500) * size / 1000.0;
            }
            return w;
        };

    /// <summary>Segments whose text the delete could not find — the cue that the region
    /// sweep still has work to do.</summary>
    private int _unresolvedSegments;

    private int DeleteReflowSource(Page page, string oldText)
    {
        var deleted = 0;
        // Counted PER SEGMENT, not summed: one call can remove two runs when the segment's
        // text repeats on the line, and a sum then reads "all gone" while other segments
        // matched nothing at all. The caller only needs to know whether ANY segment was
        // left behind, so record the segments that resolved, not the runs removed.
        _unresolvedSegments = 0;
        foreach (var seg in _segments)
        {
            if (string.IsNullOrEmpty(seg.Text)) continue;
            var r = new TextReplacer();
            if (seg.Position is { } sp) r.TargetY = sp.YIndent;
            r.Replace(page, seg.Text, string.Empty);
            if (r.ReplacementCount == 0) _unresolvedSegments++;
            deleted += r.ReplacementCount;
        }
        if (deleted == 0 && !string.IsNullOrEmpty(oldText))
        {
            var r = new TextReplacer();
            if (_position is { } pos) r.TargetY = pos.YIndent;
            r.ReplaceWithCrossOperator(page, oldText, string.Empty);
            deleted += r.ReplacementCount;
        }
        return deleted;
    }

    /// <summary>
    /// IsFormFillingMode replace: word-wrap an over-wide replacement into the matched
    /// fragment's own rectangle. Lines break greedily at spaces
    /// against the ORIGINAL fragment's width, left-aligned at its LLX; the first line
    /// keeps the original baseline and each following line steps 1.2·fs down; the font
    /// size never changes; every non-final line keeps its trailing separator space (so
    /// re-extraction reassembles the exact replacement string). When the source
    /// (subset) font lacks replacement glyphs the lines are written in the system face
    /// of the same family, else Times New Roman. Returns false (leaving the ordinary
    /// in-place replace to run) when the replacement fits on one line or the page
    /// structure defeats it.
    /// </summary>
    private bool TryFormFillWrap(Page page, string oldText, string newText)
    {
        if (_rectangle is null || _position is null || string.IsNullOrEmpty(newText))
            return false;
        var font = TextState.Font;
        double fs = TextState.FontSize;
        if (font is null || fs <= 0) return false;
        double width = _rectangle.Width;
        if (width < 2) return false;

        // Pick the face the lines are WRITTEN in: the source font when it covers the
        // replacement, else the same family's system face, else Times New Roman.
        static bool Covers(Aspose.Pdf.Text.Font f, string s)
        {
            foreach (var ch in s)
                if (!char.IsWhiteSpace(ch) && !f.CanRepresent(ch)) return false;
            return true;
        }
        var writeFont = font;
        if (!Covers(writeFont, newText))
        {
            var fam = font.FontName ?? string.Empty;
            int plus = fam.IndexOf('+');
            if (plus >= 0 && plus + 1 < fam.Length) fam = fam[(plus + 1)..];
            int comma = fam.IndexOf(',');
            if (comma > 0) fam = fam[..comma];
            Aspose.Pdf.Text.Font? sys = null;
            if (fam.Length > 0)
                try { sys = FontRepository.FindFont(fam, ignoreCase: true); } catch { }
            if (sys is null || !Covers(sys, newText))
                try { sys = FontRepository.FindFont("Times New Roman", ignoreCase: true); } catch { }
            if (sys is null || !Covers(sys, newText)) return false;
            writeFont = sys;
        }

        // Greedy wrap at spaces against the fragment width, measuring each candidate
        // line WITH its trailing separator space (which non-final lines keep). A
        // FindFont-produced face has stub FontInfo metrics (no font dict); its REAL
        // advances live on the attached FontData's raw TTF widths.
        double Measure(string s)
        {
            try
            {
                if (writeFont.SourceFontData is { } fd) return fd.MeasureString(s, fs);
                return writeFont.MeasureString(s, fs);
            }
            catch { return s.Length * fs * 0.5; }
        }
        var wrapped = new System.Collections.Generic.List<string>();
        var cur = new System.Text.StringBuilder();
        foreach (var word in newText.Split(' '))
        {
            if (word.Length == 0) continue;
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length == 0 || Measure(trial + " ") <= width)
            {
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(word);
                continue;
            }
            wrapped.Add(cur.ToString() + " ");
            cur.Clear();
            cur.Append(word);
        }
        if (cur.Length > 0) wrapped.Add(cur.ToString());
        if (wrapped.Count <= 1) return false; // fits: ordinary in-place replace

        // Delete the source operator(s) by REGION — every text op starting inside the
        // fragment's own rect span at its baseline. (Trailing padding spaces in the
        // same operator go with it; nothing observable asserts them.)
        var del = new TextReplacer
        {
            MatchAnyOperator = true,
            TargetX = (_rectangle.LLX + _rectangle.URX) / 2,
            TargetXTolerance = width / 2 + 1.0,
        };
        var targetYs = new System.Collections.Generic.List<double>();
        var baseY = (BaselinePosition ?? _position).YIndent;
        targetYs.Add(baseY);
        if (System.Math.Abs(_position.YIndent - baseY) > 0.01) targetYs.Add(_position.YIndent);
        foreach (var ty in targetYs)
        {
            del.TargetY = ty;
            del.Replace(page, string.Empty, string.Empty);
            if (del.ReplacementCount > 0) break;
        }
        if (del.ReplacementCount == 0) return false;

        // Emit the wrapped lines.
        var tb = new TextBuilder(page);
        double x0 = _rectangle.LLX;
        double y0 = _position.YIndent;
        double step = 1.2 * fs;
        var laidOut = new System.Collections.Generic.List<(string text, double by, double w)>();
        double maxLineW = 0;
        for (int i = 0; i < wrapped.Count; i++)
        {
            double by = y0 - i * step;
            var frag = new TextFragment(wrapped[i]);
            frag.TextState.Font = writeFont;
            frag.TextState.FontSize = (float)fs;
            if (TextState.ForegroundColor is { } fg) frag.TextState.ForegroundColor = fg;
            frag.Position = new Position(x0, by);
            tb.AppendText(frag);
            double lw = Measure(wrapped[i]);
            laidOut.Add((wrapped[i], by, lw));
            if (lw > maxLineW) maxLineW = lw;
        }
        page.ResetContentsCache();

        // Re-point this fragment at the laid-out block (mirrors the whole-paragraph
        // reflow): box LLY = last baseline, URY = first baseline + 1.1·fs.
        _rectangle = new Rectangle(x0, laidOut[^1].by, x0 + maxLineW, laidOut[0].by + 1.1 * fs);
        _segments.Clear();
        foreach (var ln in laidOut)
        {
            var seg = new TextSegment(ln.text);
            seg.TextState.FontSize = (float)fs;
            seg.TextState.FontName = writeFont.FontName;
            seg.TextState.Font = writeFont;
            seg.Owner = this;
            seg.Position = new Position(x0, ln.by);
            seg.TextState.OwnerSegment = seg;
            _segments.Add(seg);
        }
        return true;
    }

    /// <summary>
    /// Re-wrap the whole paragraph that contains this fragment after a replacement, so
    /// following words flow up to close the gap a shorter replacement leaves — matching
    /// the WholeWordsHyphenation re-flow. Groups the contiguous, same-left-margin
    /// lines around this fragment into a paragraph, applies the replacement to EVERY
    /// occurrence in it, greedy-wraps the result to the paragraph's width, and re-emits the
    /// lines at the original baseline grid (in the paragraph's dominant font). Returns false
    /// when the search text isn't in the detected paragraph (so sibling fragments no-op).
    /// </summary>
    private bool TryReflowParagraph(Page page, string oldText, string newText)
    {
        if (_position is not { } myPos || TextState.Font is not { } myFont || TextState.FontSize <= 0)
            return false;
        double fs = TextState.FontSize;

        var abs = new TextFragmentAbsorber(".+", new TextSearchOptions(true));
        // The line fragments absorbed here are deleted in place (see below); pin
        // ReplaceAdjustment.None so the deletion never shifts other same-line
        // content, independent of the absorber's ShiftRestOfLine default.
        abs.TextReplaceOptions = new TextReplaceOptions(TextReplaceOptions.ReplaceAdjustment.None);
        page.Accept(abs);
        // Precompute geometry so Position (non-null after this filter) isn't re-dereferenced.
        var lines0 = new System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)>();
        // Full left span per line (min of the rect edge and the leftmost visible
        // segment) — a back-jump line's rect can START RIGHT of its own earlier-drawn
        // segments, and the match-containment test below must still accept a match
        // inside those segments.
        var spanLx = new System.Collections.Generic.Dictionary<TextFragment, double>();
        foreach (TextFragment f in abs.TextFragments)
        {
            var p = f.PositionOrNull;
            if (p is null) continue;
            if (string.IsNullOrWhiteSpace(f.Text)) continue;
            var rect = f.Rectangle;
            if (rect is null) continue;
            // The fragment rect's left edge lies about where the line's text starts in
            // BOTH directions: leading padding-space glyphs pull it LEFT of the visible
            // text (hanging indents padded from the wrap margin), and a back-jump line
            // (later-drawn run first in reading order) anchors it RIGHT of earlier-drawn
            // segments. The leftmost VISIBLE (non-blank) segment is the truth either way.
            double lx = rect.LLX;
            double vis = double.MaxValue;
            foreach (var sg in f.Segments)
                if (sg.Position is { } sp && !string.IsNullOrWhiteSpace(sg.Text) && sp.XIndent < vis)
                    vis = sp.XIndent;
            if (vis < double.MaxValue) lx = vis;
            spanLx[f] = System.Math.Min(rect.LLX, lx);
            lines0.Add((f, p.YIndent, lx, rect.URX));
        }
        if (lines0.Count == 0) return false;
        // Top-to-bottom (PDF Y grows upward, so higher YIndent = higher on page).
        lines0.Sort((a, b) => b.y.CompareTo(a.y));

        // Find the re-absorbed line that CONTAINS this fragment. The fragment's own
        // LLX is the X of the matched token, which may sit mid-line (e.g. "{{Name}}"
        // embedded in flowing text), so match by Y proximity plus X-within-[lx,rx]
        // rather than assuming the fragment starts at the line's left margin.
        double myLLX = _rectangle!.LLX;
        int myIdx = -1; double best = fs;
        for (int i = 0; i < lines0.Count; i++)
        {
            double dy = System.Math.Abs(lines0[i].y - myPos.YIndent);
            bool xin = myLLX >= spanLx[lines0[i].f] - 5 && myLLX <= lines0[i].rx + 5;
            if (dy <= best && xin && dy < fs) { best = dy; myIdx = i; }
        }
        if (myIdx < 0) return false;
        double leftX = lines0[myIdx].lx;

        // Grow the paragraph up/down over contiguous same-left-margin lines (one line pitch apart).
        // A line is only merged if it shares the left margin AND is close in font SIZE: a bigger
        // heading (e.g. a 24pt bold title above 12pt body, same left margin) is a SEPARATE
        // paragraph, so merging it would collapse it to body size on reflow. Same-size paragraphs
        // (the common case) are unaffected.
        const double xtol = 3.0;
        // IgnoreParagraphs = continuous-flow reflow: the replacement flows through the WHOLE text
        // block, ignoring paragraph boundaries. Grow across all contiguous same-size lines
        // regardless of left-margin changes so the entire block reflows as one unit and cascades
        // down naturally (no separate push-down of trailing paragraphs needed). Default mode keeps
        // the strict same-left-margin grow.
        bool ignorePara = _replaceOptions?.IgnoreParagraphs ?? false;
        double paraFs = lines0[myIdx].f.TextState.FontSize;
        if (paraFs <= 0) paraFs = fs;
        bool SizeCompatible(double lineFs) =>
            lineFs <= 0 || (lineFs <= paraFs * 1.35 && lineFs >= paraFs / 1.35);
        int lo = myIdx, hi = myIdx;
        // Hanging-indent lists (numbered/bulleted items): the item's FIRST line sits at a
        // dedented margin and its continuation lines share a deeper indent. The whole
        // item reflows as one paragraph, so grouping accepts one indent step
        // down from the match line (establishing the continuation indent) and, growing up
        // from a continuation line, the single dedented head line (then stops). An indent
        // step going UP is the tail of the PREVIOUS item — never merged. Any step must
        // keep the paragraph's OWN line pitch (≤1.35×): a dedented line a line-and-a-half
        // away (a salutation above an indented body, a heading) is a separate paragraph.
        const double maxHang = 40.0;
        const double stepPitchTol = 1.35;
        double downLx = lines0[myIdx].lx;
        bool hangStepped = false;
        while (hi + 1 < lines0.Count)
        {
            double gap = lines0[hi].y - lines0[hi + 1].y;
            if (!(gap > 0 && gap < 3 * fs) || !SizeCompatible(lines0[hi + 1].f.TextState.FontSize))
                break;
            double lx = lines0[hi + 1].lx;
            if (ignorePara || System.Math.Abs(lx - downLx) <= xtol) { hi++; continue; }
            if (!hangStepped && hi == myIdx && lx - downLx > xtol && lx - downLx <= maxHang)
            {
                double nextGap = hi + 2 < lines0.Count ? lines0[hi + 1].y - lines0[hi + 2].y : gap;
                if (nextGap > 0 && gap <= stepPitchTol * nextGap)
                {
                    hangStepped = true; downLx = lx; hi++; continue;
                }
            }
            break;
        }
        double upLx = lines0[myIdx].lx;
        while (lo - 1 >= 0)
        {
            double gap = lines0[lo - 1].y - lines0[lo].y;
            if (!(gap > 0 && gap < 3 * fs) || !SizeCompatible(lines0[lo - 1].f.TextState.FontSize))
                break;
            double lx = lines0[lo - 1].lx;
            if (ignorePara || System.Math.Abs(lx - upLx) <= xtol) { lo--; continue; }
            if (upLx - lx > xtol && upLx - lx <= maxHang)
            {
                double refGap = lo < hi ? lines0[lo].y - lines0[lo + 1].y : gap;
                if (refGap > 0 && gap <= stepPitchTol * refGap) { lo--; break; }
            }
            break;
        }

        var paraLines = lines0.GetRange(lo, hi - lo + 1);
        // Continuous flow anchors the re-emitted block at the flow's leftmost x.
        if (ignorePara)
            foreach (var l in paraLines) if (l.lx < leftX) leftX = l.lx;
        // Replace PER LINE (mirroring the per-fragment absorber), then reunite — an occurrence
        // split across a line break isn't a single-line match and is left intact (a
        // per-fragment replace also misses line-straddling occurrences).
        var origParts = new System.Collections.Generic.List<string>();
        var newParts = new System.Collections.Generic.List<string>();
        foreach (var l in paraLines)
        {
            var t = l.f.Text.Trim();
            origParts.Add(t);
            newParts.Add(t.Replace(oldText, newText, System.StringComparison.Ordinal));
        }
        var origText = string.Join(" ", origParts);
        var replaced = string.Join(" ", newParts);
        // Whole-paragraph replacement: when the matched fragment IS the entire paragraph
        // (oldText spans every line, e.g. a paragraph->paragraph+paragraph replace), no
        // single-line Replace fires, so replaced==origText. Detect that by comparing the
        // paragraph body to oldText ignoring all whitespace (robust to reconstruction
        // spacing differences) and re-wrap the replacement directly. Otherwise there is no
        // within-line occurrence in this paragraph and sibling fragments must no-op.
        bool wholePara = false;
        if (replaced == origText)
        {
            static string Squash(string s) =>
                System.Text.RegularExpressions.Regex.Replace(s, @"\s+", string.Empty);
            if (Squash(oldText) == Squash(origText)) { replaced = newText; wholePara = true; }
            else return false;
        }

        // Mid-token replacement (default flow): cascade from the MATCH
        // position — the paragraph lines above the match and the match line's prefix
        // stay untouched; text from the match onward re-packs onto the EXISTING baselines.
        // When the cascade can't handle the page's structure (CID font, cross-run match,
        // glyphs missing from the subset…) FALL THROUGH to the whole-paragraph re-wrap
        // below — bailing out entirely would leave the plain in-place replace to grow the
        // line past the page edge.
        // A LONE line has no following baselines to re-pack onto, so a replacement
        // that overflows it cannot cascade — it needs the free-space re-wrap below,
        // which takes its column from the page rather than from the line.
        // ...and only when the match IS that line: a token embedded in a longer line
        // still has its line-mates to re-pack against, so it cascades as before.
        var lonelyOverflow = paraLines.Count == 1
            && _rectangle is { } myRect
            && myRect.Width >= (paraLines[0].rx - paraLines[0].lx) * 0.9
            && MeasureOrEstimate(TextState.Font!, newText, fs, false) > (paraLines[0].rx - myLLX) * 1.05;
        if (!wholePara && !ignorePara && !lonelyOverflow
            && CascadeFromMatch(page, paraLines, myIdx - lo, myLLX, oldText, newText,
                out var cascadeBottom))
        {
            ExpandClipsToReflowBottom(page, paraLines, cascadeBottom);
            return true;
        }

        double rightX = 0;
        foreach (var l in paraLines) if (l.rx > rightX) rightX = l.rx;
        // Continuous-flow (IgnoreParagraphs): page-bound the wrap width. A previous longer
        // replacement can leave an over-wide unbreakable-token line, and re-absorbing that inflated
        // max-URX would compound the overflow. Cap the right border at the page's usable right edge
        // (mirror the left inset) so the flow wraps within the page instead of running off it.
        var pageRect = page.Rect;
        // A one-line "paragraph" carries no column width of its own: a lone token
        // sitting in free space would re-wrap to its own token width. Such a flow
        // takes the page as its column — the left inset mirrored on the right —
        // and stops short of the nearest text to its right on the same line, whose
        // own size sets the gap.
        if (paraLines.Count == 1 && pageRect is not null)
        {
            double lonelyInset = leftX - pageRect.LLX;
            double lonelyRight = pageRect.URX - (lonelyInset > 0 ? lonelyInset : 0);
            foreach (var l in lines0)
            {
                if (ReferenceEquals(l.f, paraLines[0].f)) continue;
                if (System.Math.Abs(l.y - myPos.YIndent) > fs) continue;
                if (l.lx <= myLLX) continue;
                var nfs = l.f.TextState.FontSize > 0 ? l.f.TextState.FontSize : fs;
                var clipped = l.lx - (nfs - 1);
                if (clipped < lonelyRight) lonelyRight = clipped;
            }
            if (lonelyRight > leftX + 10) rightX = lonelyRight;
        }
        if (ignorePara && pageRect is not null)
        {
            double leftInset = leftX - pageRect.LLX;
            double pageRight = pageRect.URX - (leftInset > 0 ? leftInset : 0);
            if (pageRight > leftX + 10 && rightX > pageRight) rightX = pageRight;
        }
        // RightAdjustment extends the wrap border to the right so a longer replacement
        // re-flows into more lines against the widened margin. It applies only to the
        // mid-line-token reflow; a whole-paragraph replace re-wraps to the paragraph's own
        // width and ignores RightAdjustment.
        double rightAdjust = wholePara ? 0 : (_replaceOptions?.RightAdjustment ?? 0);
        double width = (rightX - leftX) + rightAdjust;
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
            Console.Error.WriteLine($"[reflow] paraLines={paraLines.Count} leftX={leftX:F2} rightX={rightX:F2} width={width:F2} wholePara={wholePara} ignorePara={ignorePara} pageRect={(page.Rect is null ? "null" : page.Rect.ToString())}");
        if (width < 10) return false;

        // Re-flow in the paragraph's dominant font (the fragment carrying the most text),
        // so a lone bold word doesn't bold the whole paragraph and vice-versa.
        var domLine = paraLines[0].f;
        foreach (var l in paraLines) if (l.f.Text.Length > domLine.Text.Length) domLine = l.f;
        var domFont = domLine.TextState.Font ?? myFont;
        var domName = domLine.TextState.FontName ?? TextState.FontName;
        float domSize = domLine.TextState.FontSize > 0 ? domLine.TextState.FontSize : (float)fs;
        // A source font that cannot encode the replacement is substituted, and the
        // whole re-flow — wrap, widths and the written lines — runs in the stand-in.
        var reflowFace = ResolveSubstituteFace(domFont, replaced);
        var reflowMeasure = reflowFace is null ? null : Standard14Measurer(reflowFace);

        System.Collections.Generic.List<string> wrapped;
        if (wholePara)
        {
            // Shrink the font until the (larger) replacement fits the ORIGINAL rectangle,
            // HOLDING the line count measured at the original size, then re-wrap at the
            // fitted size. Compute the fit from the un-mutated original size (the fresh
            // re-absorb's, not THIS fragment's TextState which a caller may have already
            // shrunk via IsFitRectangle) and the original rectangle, so the result is
            // independent of the caller's font-size loop. Measure with a trailing space per
            // line: reserving one space width past each wrapped line breaks lines slightly
            // earlier and keeps the wrapped lines re-searchable across the line breaks.
            double origSize = domSize;
            double rectH = _rectangle!.Height;
            int nFit = WrapToWidth(replaced, domFont, origSize, width, trailingSpace: true, measure: reflowMeasure is null ? null : t => reflowMeasure(t, origSize)).Count;
            if (nFit < 1) nFit = 1;
            double fitFs = origSize;
            while (fitFs > 1.0 && nFit * 1.2 * fitFs > rectH) fitFs -= 0.5;
            domSize = (float)fitFs;
            wrapped = WrapToWidth(replaced, domFont, fitFs, width, trailingSpace: true, measure: reflowMeasure is null ? null : t => reflowMeasure(t, fitFs));
            // A LONGER whole-paragraph replacement flows into the ORIGINAL paragraph's line
            // grid (same baseline count). A greedy wrap at the full width under-fills (packs
            // one extra line's worth of text per line), so narrow the wrap width until the
            // line count reaches the original's. The exact per-line breaks produced by this
            // greedy wrapper still differ from an optimal/balanced line-breaker (so the flow,
            // and the block's URX, is a fidelity gap), but the line count — hence the segment
            // and baseline grid — matches.
            if (replaced.Length > origText.Length)
            {
                int targetLines = paraLines.Count;
                double renderW = width;
                int guard = 0;
                while (wrapped.Count < targetLines && renderW > 20 && guard++ < 800)
                {
                    renderW -= 0.5;
                    wrapped = WrapToWidth(replaced, domFont, fitFs, renderW, trailingSpace: true, measure: reflowMeasure is null ? null : t => reflowMeasure(t, fitFs));
                }
            }
        }
        else
        {
            wrapped = WrapToWidth(replaced, domFont, domSize, width, allowCharBreak: ignorePara, measure: reflowMeasure is null ? null : t => reflowMeasure(t, domSize));
        }
        if (wrapped.Count == 0) return false;

        var baselines = new System.Collections.Generic.List<double>();
        foreach (var l in paraLines)
        {
            // Line anchors are the source lines' own positions, which carry the
            // SOURCE font's descent. That cancels out when the re-flow writes the
            // same font — but a substituted face has its own descent, so anchor on
            // the run's true baseline and let the stand-in's descent apply.
            var anchor = l.y;
            if (reflowFace is not null && (l.f.BaselinePosition ?? l.f.PositionOrNull) is { } bp)
                anchor = bp.YIndent;
            baselines.Add(anchor);
        }
        double pitch = baselines.Count >= 2
            ? (baselines[0] - baselines[^1]) / (baselines.Count - 1)
            : 1.2 * domSize;
        if (pitch <= 0) pitch = 1.2 * domSize;

        foreach (var l in paraLines)
        {
            // The re-absorbed line fragments have ReplaceAdjustment.None (fresh absorber),
            // so this deletes in place via the normal replace machinery without recursing
            // back into paragraph reflow.
            try { l.f.Text = string.Empty; } catch { }
        }

        var tb = new TextBuilder(page);
        var laidOut = new System.Collections.Generic.List<(string text, double baseline, double width)>();
        double maxLineW = 0;
        for (int i = 0; i < wrapped.Count; i++)
        {
            double by = i < baselines.Count ? baselines[i] : baselines[^1] - (i - baselines.Count + 1) * pitch;
            var frag = new TextFragment(wrapped[i]);
            frag.TextState.Font = domFont;
            if (!string.IsNullOrEmpty(domName)) frag.TextState.FontName = domName;
            frag.TextState.FontSize = domSize;
            if (TextState.ForegroundColor is { } fg) frag.TextState.ForegroundColor = fg;
            if (reflowFace is not null)
            {
                frag.TextState.Std14FaceOverride = reflowFace;
                // Write the stand-in with its metrics, so the run reads back with
                // the stand-in's descent under its baseline.
                frag.TextState.EmitStandard14Descriptor = true;
            }
            frag.Position = new Position(leftX, by);
            tb.AppendText(frag);
            double lw;
            if (reflowMeasure is not null) lw = reflowMeasure(wrapped[i], domSize);
            else try { lw = domFont.MeasureString(wrapped[i], domSize); } catch { lw = wrapped[i].Length * domSize * 0.5; }
            laidOut.Add((wrapped[i], by, lw));
            if (lw > maxLineW) maxLineW = lw;
        }
        page.ResetContentsCache();
        if (wrapped.Count > baselines.Count)
            ExpandClipsToReflowBottom(page, paraLines,
                baselines[^1] - pitch * (wrapped.Count - baselines.Count));

        // A whole-paragraph replace re-points THIS fragment at the laid-out block so a caller
        // that reads fragment.Segments / fragment.Rectangle after the assignment (e.g. to add
        // a per-segment underline or a per-fragment highlight) sees the reflowed geometry. Box
        // mirrors the absorber: LLY = baseline, URY = baseline + 1.1*fs; URX = widest line.
        if (wholePara && laidOut.Count > 0)
        {
            double firstBaseline = laidOut[0].baseline;
            double lastBaseline = laidOut[^1].baseline;
            double ascentH = 1.1 * domSize;
            _rectangle = new Rectangle(leftX, lastBaseline, leftX + maxLineW, firstBaseline + ascentH);
            _text = newText;
            _segments.Clear();
            foreach (var ln in laidOut)
            {
                var seg = new TextSegment(ln.text);
                seg.TextState.FontSize = domSize;
                if (!string.IsNullOrEmpty(domName)) seg.TextState.FontName = domName;
                seg.TextState.Font = domFont;
                seg.Owner = this;
                seg.Position = new Position(leftX, ln.baseline);
                seg.TextState.OwnerSegment = seg;
                _segments.Add(seg);
            }
        }
        return true;
    }

    /// <summary>Hyphenation reflow for a RESTYLED replacement (the caller assigned a
    /// new font/size before setting the text). The reflow
    /// model: the match line's prefix keeps its exact position; the
    /// replacement and every following run drop onto a FRESH baseline one new-font-size
    /// step below the match baseline, flowing from the paragraph's left margin with
    /// greedy word-wrap against (page width − left inset). Retained source text keeps
    /// its own font/size; only replacement spans switch to the new style. Wrap units
    /// split at spaces and may span styles (a replacement glued to source text wraps
    /// as one unit).</summary>
    private bool StyledCascadeFromMatch(Page page,
        System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> paraLines,
        int matchLine, double myLLX, string oldText, string newText)
    {
        var newFont = TextState.Font;
        double newFs = TextState.FontSize;
        if (newFont is null || newFs <= 0 || string.IsNullOrEmpty(oldText)) return false;

        static string Family(string? n)
        {
            if (string.IsNullOrEmpty(n)) return string.Empty;
            var s = n;
            int plus = s.IndexOf('+');
            if (plus >= 0 && plus + 1 < s.Length) s = s[(plus + 1)..];
            int comma = s.IndexOf(',');
            if (comma > 0) s = s[..comma];
            int dash = s.IndexOf('-');
            if (dash > 0) s = s[..dash];
            return s.Replace(" ", string.Empty);
        }

        System.Collections.Generic.List<TextSegment> LineSegs(TextFragment f)
        {
            var list = new System.Collections.Generic.List<TextSegment>();
            foreach (var seg in f.Segments)
                if (seg.Position is not null && !string.IsNullOrEmpty(seg.Text)) list.Add(seg);
            list.Sort((a, b) => a.Position!.XIndent.CompareTo(b.Position!.XIndent));
            return list;
        }

        var headSegs = LineSegs(paraLines[matchLine].f);
        if (headSegs.Count == 0) return false;
        int headIdx = -1;
        for (int i = 0; i < headSegs.Count; i++)
            if (headSegs[i].Position!.XIndent <= myLLX + 0.5) headIdx = i; else break;
        if (headIdx < 0) return false;
        var headSeg = headSegs[headIdx];
        var headFont = headSeg.TextState.Font;
        double headFs = headSeg.TextState.FontSize > 0 ? headSeg.TextState.FontSize : newFs;

        // Only a genuine restyle takes this path; a same-style replacement stays on
        // the byte-level run mover (which positions that case exactly).
        bool restyled = System.Math.Abs(newFs - headFs) > 0.1
            || (Family(newFont.FontName).Length > 0 && Family(headFont?.FontName).Length > 0
                && !string.Equals(Family(newFont.FontName), Family(headFont?.FontName),
                    System.StringComparison.OrdinalIgnoreCase));
        if (!restyled) return false;

        double W(Aspose.Pdf.Text.Font? f, double fs, string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (f is not null) { try { return f.MeasureString(s, fs); } catch { } }
            return s.Length * fs * 0.5;
        }
        static double DescentOf(Aspose.Pdf.Text.Font? f, double fs)
        {
            double d = 0;
            try
            {
                if (f?.SourceFontData?.TtfData is { } ttf)
                {
                    // hhea descender — the SAME value the Type0 embed's descriptor
                    // carries, so emitted targets round-trip through the absorber.
                    d = TextBuilder.HheaDescentPerMille(ttf);
                    if (d == 0) (_, d, _, _) = FontRepository.ReadTtfMetrics(ttf);
                }
                if (d == 0) d = f?.GetMetrics()?.Descent ?? 0;
            }
            catch { }
            return d != 0 ? System.Math.Abs(d) / 1000.0 * fs : 0;
        }

        // Char index of the match inside the head run, located by measuring prefixes
        // against the match X (the match may be any occurrence within the run).
        double headX = headSeg.Position!.XIndent;
        int occ = -1; double bestD = double.MaxValue;
        for (int i = headSeg.Text.IndexOf(oldText, System.StringComparison.Ordinal); i >= 0;
             i = i + 1 <= headSeg.Text.Length - 1
                ? headSeg.Text.IndexOf(oldText, i + 1, System.StringComparison.Ordinal) : -1)
        {
            double d = System.Math.Abs(headX + W(headFont, headFs, headSeg.Text[..i]) - myLLX);
            if (d < bestD) { bestD = d; occ = i; }
        }
        if (occ < 0 || bestD > System.Math.Max(2.0, headFs)) return false;

        // Styled run stream: source text keeps its style; every oldText occurrence
        // becomes newText in the new style.
        var runs = new System.Collections.Generic.List<(string text, Aspose.Pdf.Text.Font? font, double fs, Color? fg)>();
        var newFg = TextState.ForegroundColor;
        void AddStyledSplit(string s, TextSegment src)
        {
            var f = src.TextState.Font ?? headFont;
            double fs = src.TextState.FontSize > 0 ? src.TextState.FontSize : headFs;
            var fg = src.TextState.ForegroundColor;
            int p = 0;
            while (true)
            {
                int q = s.IndexOf(oldText, p, System.StringComparison.Ordinal);
                if (q < 0) { if (p < s.Length) runs.Add((s[p..], f, fs, fg)); break; }
                if (q > p) runs.Add((s[p..q], f, fs, fg));
                runs.Add((newText, newFont, newFs, newFg));
                p = q + oldText.Length;
            }
        }
        AddStyledSplit(headSeg.Text[occ..], headSeg);
        for (int i = headIdx + 1; i < headSegs.Count; i++) AddStyledSplit(headSegs[i].Text, headSegs[i]);
        for (int li = matchLine + 1; li < paraLines.Count; li++)
        {
            if (runs.Count > 0 && !runs[^1].text.EndsWith(" ", System.StringComparison.Ordinal))
            {
                var last = runs[^1];
                runs.Add((" ", last.font, last.fs, last.fg));
            }
            foreach (var s in LineSegs(paraLines[li].f)) AddStyledSplit(s.Text, s);
        }
        if (runs.Count == 0) return false;

        // Wrap geometry: left = the match line's left margin; right mirrors the left
        // inset against the page width (never tighter than the paragraph's extent).
        // The line's leftmost RUN X (the fragment rect's LLX can degrade to 0).
        double pLeft = headSegs[0].Position!.XIndent;
        double maxRx = 0; foreach (var l in paraLines) if (l.rx > maxRx) maxRx = l.rx;
        double mediaW = page.MediaBox is { } mb ? mb.URX - mb.LLX : 0;
        double rightMargin = System.Math.Max(mediaW - pLeft, maxRx);
        if (rightMargin <= pLeft + 20) return false;

        // Tokenize into wrap units (split at spaces; units may span styles). The
        // style of the space BEFORE each unit is recorded for gap measurement.
        var units = new System.Collections.Generic.List<System.Collections.Generic.List<(string t, int r)>>();
        var unitGap = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<(string t, int r)>? cur = null;
        int pendingGap = -1;
        for (int r = 0; r < runs.Count; r++)
        {
            var parts = runs[r].text.Split(' ');
            for (int pi = 0; pi < parts.Length; pi++)
            {
                if (parts[pi].Length > 0)
                {
                    if (cur is null)
                    {
                        cur = new System.Collections.Generic.List<(string, int)>();
                        units.Add(cur);
                        unitGap.Add(pendingGap);
                    }
                    cur.Add((parts[pi], r));
                }
                if (pi < parts.Length - 1) { cur = null; pendingGap = r; }
            }
        }
        if (units.Count == 0) return false;

        // Greedy flow: first fresh baseline one new-size step below the match
        // baseline; every wrapped line steps by the new size.
        // The re-absorbed line Y is already the run's Tm baseline.
        double matchTm = paraLines[matchLine].y;
        double tmY = matchTm - newFs;
        double x = pLeft; bool lineHas = false;
        var pieces = new System.Collections.Generic.List<(string text, int r, double x, double tmY)>();
        for (int u = 0; u < units.Count; u++)
        {
            double unitW = 0;
            foreach (var (t, r) in units[u]) unitW += W(runs[r].font, runs[r].fs, t);
            int gapR = lineHas ? unitGap[u] : -1;
            double gapW = gapR >= 0 ? W(runs[gapR].font, runs[gapR].fs, " ") : 0;
            if (lineHas && x + gapW + unitW > rightMargin + 0.25)
            {
                tmY -= newFs; x = pLeft; lineHas = false; gapR = -1; gapW = 0;
            }
            if (gapR >= 0) { pieces.Add((" ", gapR, x, tmY)); x += gapW; }
            foreach (var (t, r) in units[u])
            {
                pieces.Add((t, r, x, tmY));
                x += W(runs[r].font, runs[r].fs, t);
            }
            lineHas = true;
        }

        // Merge same-style neighbours on a line into single show pieces.
        var merged = new System.Collections.Generic.List<(string text, int r, double x, double tmY)>();
        foreach (var p in pieces)
        {
            if (merged.Count > 0 && merged[^1].r == p.r && System.Math.Abs(merged[^1].tmY - p.tmY) < 0.01)
                merged[^1] = (merged[^1].text + p.text, p.r, merged[^1].x, p.tmY);
            else merged.Add(p);
        }

        // Delete the source runs: the whole match line (its prefix re-emits below at
        // its original coordinates) and every following paragraph line.
        for (int li = matchLine; li < paraLines.Count; li++)
        {
            var del = new TextReplacer
            {
                MatchAnyOperator = true,
                TargetY = paraLines[li].y,
                TargetX = (paraLines[li].lx + paraLines[li].rx) / 2,
                TargetXTolerance = (paraLines[li].rx - paraLines[li].lx) / 2 + 1.0,
            };
            del.Replace(page, string.Empty, string.Empty);
        }

        var tb = new TextBuilder(page);
        void Emit(string text, Aspose.Pdf.Text.Font? f, double fs, Color? fg, double px, double py)
        {
            if (string.IsNullOrEmpty(text) || f is null) return;
            var frag = new TextFragment(text);
            frag.TextState.Font = f;
            frag.TextState.FontSize = (float)fs;
            if (fg is not null) frag.TextState.ForegroundColor = fg;
            frag.Position = new Position(px, py);
            tb.AppendText(frag);
        }
        for (int i = 0; i < headIdx; i++)
        {
            var s = headSegs[i];
            Emit(s.Text, s.TextState.Font ?? headFont,
                s.TextState.FontSize > 0 ? s.TextState.FontSize : headFs,
                s.TextState.ForegroundColor, s.Position!.XIndent, s.Position.YIndent);
        }
        if (occ > 0)
            Emit(headSeg.Text[..occ], headFont, headFs, headSeg.TextState.ForegroundColor,
                headX, headSeg.Position.YIndent);
        foreach (var p in merged)
        {
            var st = runs[p.r];
            Emit(p.text, st.font, st.fs, st.fg, p.x, p.tmY - DescentOf(st.font, st.fs));
        }
        page.ResetContentsCache();
        return true;
    }

    /// <summary>Reflow for a mid-line token replacement
    /// (WholeWordsHyphenation): everything BEFORE the match stays untouched; the
    /// replacement plus all following paragraph text re-packs greedily from the match
    /// position onto the paragraph's EXISTING baselines (baselines never move; trailing
    /// lines that empty out stay empty). Works at run granularity — each source segment
    /// is one text-showing operator, deleted by whole-operator match at its page-space
    /// position and re-emitted as packed lines.</summary>
    private bool CascadeFromMatch(Page page,
        System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> paraLines,
        int matchLine, double myLLX, string oldText, string newText,
        out double appendedBottom)
    {
        // Lowest baseline (paragraph-line Y space) of any line the repack CREATED
        // below the paragraph's last existing baseline; NaN when everything fit.
        appendedBottom = double.NaN;
        if (matchLine < 0 || matchLine >= paraLines.Count) return false;

        // A replacement RESTYLED by the caller (font/size assigned before the text)
        // can't ride the byte-level run mover — the rewritten run must switch to the
        // new face. The restyled content drops onto a FRESH line below
        // the match (the prefix keeps its line) and flows at the new size.
        if (StyledCascadeFromMatch(page, paraLines, matchLine, myLLX, oldText, newText))
            return true;

        // Exact path first: MOVE the original runs (keeping their bytes,
        // fonts, kerning and per-run Tc) and rewrite only the matched operator,
        // re-encoded in its own font. Positions are then preserved to hundredths
        // of a point. Falls back to the coarser delete-and-re-emit below when the page
        // structure defeats it (CID font, replacement glyphs missing from the subset,
        // match not carried by a single run).
        {
            var rlines = new System.Collections.Generic.List<(double y, double lx, double rx)>();
            for (int li = matchLine; li < paraLines.Count; li++)
                rlines.Add((paraLines[li].y, paraLines[li].lx, paraLines[li].rx));
            double pLeft = double.MaxValue, maxRx = 0;
            foreach (var l in paraLines)
            {
                if (l.lx < pLeft) pLeft = l.lx;
                if (l.rx > maxRx) maxRx = l.rx;
            }
            double rPitch = rlines.Count >= 2 ? rlines[0].y - rlines[1].y : 0;
            if (rPitch <= 0) rPitch = 1.2 * (TextState.FontSize > 0 ? TextState.FontSize : 10);
            // Wrapping uses a right margin mirroring the paragraph's left inset
            // (MediaBox width − left X); never tighter than the paragraph's own extent.
            // RightAdjustment instead extends the border past the paragraph's own right
            // edge by the caller's amount (same base as the coarse re-wrap below).
            double mediaW = page.MediaBox is { } mbx ? mbx.URX - mbx.LLX : 0;
            double rightAdj = _replaceOptions?.RightAdjustment ?? 0;
            double rMargin = rightAdj > 0
                ? maxRx + rightAdj
                : System.Math.Max(mediaW - pLeft, maxRx);
            var mover = new TextReplacer();
            if (mover.ReflowFromMatch(page, oldText, newText, myLLX, rlines, pLeft, rMargin, rPitch,
                    _replaceOptions?.AdjustmentNewLineSpacing ?? 0))
            {
                if (mover.ReflowCreatedLines > 0)
                {
                    // Mirror the mover's created-line advance (mean pitch below the
                    // edited line) in paragraph-line Y space for the clip expansion.
                    double meanPitch = rlines.Count >= 2
                        ? (rlines[0].y - rlines[^1].y) / (rlines.Count - 1)
                        : rPitch;
                    appendedBottom = rlines[^1].y - meanPitch * mover.ReflowCreatedLines;
                }
                page.ResetContentsCache();
                return true;
            }
        }

        // Effective (page-space) font scale: producers that draw each run in its own
        // q/cm/BT..ET/Q block size text via Tm with the CTM shrinking it back; measuring
        // or re-emitting at the raw Tm size would be wrong by the CTM factor.
        double ctmScale = 1.0;
        if (ExtractionCtm is { } ectm)
        {
            var det = System.Math.Abs(ectm.A * ectm.D - ectm.B * ectm.C);
            if (det > 1e-9) ctmScale = System.Math.Sqrt(det);
        }

        // Collect the segments to re-flow, each one source run: on the match line those
        // at/after the match X, on the following paragraph lines all of them.
        var moved = new System.Collections.Generic.List<(TextSegment seg, double x, double y)>();
        for (int li = matchLine; li < paraLines.Count; li++)
        {
            foreach (var seg in paraLines[li].f.Segments)
            {
                if (seg.Position is not { } sp) continue;
                if (string.IsNullOrEmpty(seg.Text)) continue;
                if (li == matchLine && sp.XIndent < myLLX - 0.5) continue; // prefix stays
                moved.Add((seg, sp.XIndent, sp.YIndent));
            }
        }
        if (moved.Count == 0) return false;
        moved.Sort((a, b) => b.y != a.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));

        // The first moved segment must carry the matched token (a match hidden mid-run
        // with a prefix inside the same run is left to the in-place replace path).
        var head = moved[0].seg.Text;
        int occ = head.IndexOf(oldText, System.StringComparison.Ordinal);
        if (occ < 0) return false;

        // Combined text from the match onward. Same-line neighbours concatenate verbatim
        // (their spacing rides in the runs); a line break is a word boundary. NBSPs fold
        // to plain spaces — producers that pad word gaps with U+00A0 would otherwise glue
        // the NBSP onto the next word through the space-split below, and the re-emitted
        // line would never phrase-match a plain-space search.
        var sb = new System.Text.StringBuilder();
        sb.Append(head.Replace(oldText, newText, System.StringComparison.Ordinal));
        for (int i = 1; i < moved.Count; i++)
        {
            bool lineBreak = System.Math.Abs(moved[i].y - moved[i - 1].y) > 0.75;
            if (lineBreak && sb.Length > 0 && sb[^1] != ' ' && !moved[i].seg.Text.StartsWith(" "))
                sb.Append(' ');
            sb.Append(moved[i].seg.Text);
        }
        sb.Replace('\u00A0', ' ');

        // Measure/emit in the paragraph's dominant face at the effective size.
        var domSeg = moved[0].seg;
        foreach (var m in moved)
            if (m.seg.Text.Trim().Length > domSeg.Text.Trim().Length) domSeg = m.seg;
        var font = domSeg.TextState.Font ?? TextState.Font;
        double rawFs = domSeg.TextState.FontSize > 0 ? domSeg.TextState.FontSize : TextState.FontSize;
        double effFs = rawFs * ctmScale;
        if (font is null || effFs <= 0.5) return false;
        // Prefer the SYSTEM face of the same family for measuring and re-emission. The
        // source font is typically an embedded SUBSET whose width table is keyed by its
        // custom byte codes, so measuring Unicode text against it mis-indexes the widths;
        // the system face carries the true advances (the reflow is measured
        // with these), and embedding it makes the absorber read the same metrics back, so
        // the re-emitted words land at consistent positions.
        var faceName = font.FontName ?? string.Empty;
        int subsetPlus = faceName.IndexOf('+');
        if (subsetPlus >= 0 && subsetPlus + 1 < faceName.Length)
            faceName = faceName[(subsetPlus + 1)..];
        int styleComma = faceName.IndexOf(',');
        if (styleComma > 0) faceName = faceName[..styleComma];
        if (faceName.Length > 0
            && FontRepository.FindFont(faceName, ignoreCase: true) is { } sysFont)
            font = sysFont;

        double leftX = double.MaxValue, rightX = 0;
        for (int li = matchLine; li < paraLines.Count; li++)
        {
            if (paraLines[li].lx < leftX) leftX = paraLines[li].lx;
            if (paraLines[li].rx > rightX) rightX = paraLines[li].rx;
        }
        if (rightX - leftX < 10 || rightX <= myLLX + 5) return false;

        // Greedy pack: first line from the match X, continuation lines from their own
        // ORIGINAL left margin (hanging-indent items keep the continuation indent);
        // lines created beyond the paragraph continue at the last line's indent.
        double LxAt(int i2)
        {
            int li2 = matchLine + i2;
            return li2 < paraLines.Count ? paraLines[li2].lx : paraLines[^1].lx;
        }
        var words = sb.ToString().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return false;
        double SpaceW() { try { return font.MeasureString(" ", effFs); } catch { return effFs * 0.25; } }
        double WordW(string w) { try { return font.MeasureString(w, effFs); } catch { return w.Length * effFs * 0.5; } }
        var packed = new System.Collections.Generic.List<string>();
        var cur = new System.Text.StringBuilder();
        double curX = myLLX, curW = 0, spaceW = SpaceW();
        foreach (var w in words)
        {
            double ww = WordW(w);
            double trial = cur.Length == 0 ? ww : curW + spaceW + ww;
            if (curX + trial <= rightX + 0.5 || cur.Length == 0)
            {
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(w);
                curW = trial;
            }
            else
            {
                packed.Add(cur.ToString());
                cur.Clear(); cur.Append(w);
                curW = ww; curX = LxAt(packed.Count);
            }
        }
        if (cur.Length > 0) packed.Add(cur.ToString());

        // Existing baselines from the match line down; extend below by the pitch if the
        // packed text needs more lines than the paragraph had.
        var baselines = new System.Collections.Generic.List<double>();
        for (int li = matchLine; li < paraLines.Count; li++) baselines.Add(paraLines[li].y);
        double pitch = baselines.Count >= 2
            ? (baselines[0] - baselines[^1]) / (baselines.Count - 1)
            : 1.2 * effFs;
        if (pitch <= 0) pitch = 1.2 * effFs;
        if (packed.Count > baselines.Count)
            appendedBottom = baselines[^1] - pitch * (packed.Count - baselines.Count);

        // Delete the source runs by REGION, one line at a time, at operator granularity:
        // producers that draw one word (or one bare space) per operator defeat text-keyed
        // deletion — the absorber's coalesced segment text (with synthesized gap spaces)
        // never equals any single operator's decode. Every text operator starting inside
        // the line's X-span goes; the match line is cleared only from the match X on, so
        // its prefix stays put.
        for (int li = matchLine; li < paraLines.Count; li++)
        {
            double xmin = (li == matchLine ? myLLX : paraLines[li].lx) - 0.5;
            double xmax = paraLines[li].rx + 1.0;
            if (xmax <= xmin) continue;
            var del = new TextReplacer
            {
                MatchAnyOperator = true,
                TargetY = paraLines[li].y,
                TargetX = (xmin + xmax) / 2,
                TargetXTolerance = (xmax - xmin) / 2,
            };
            del.Replace(page, string.Empty, string.Empty);
        }

        // Re-emit the packed lines.
        var tb = new TextBuilder(page);
        for (int i = 0; i < packed.Count; i++)
        {
            double by = i < baselines.Count ? baselines[i] : baselines[^1] - (i - baselines.Count + 1) * pitch;
            var frag = new TextFragment(packed[i]);
            frag.TextState.Font = font;
            if (domSeg.TextState.FontName is { Length: > 0 } fn) frag.TextState.FontName = fn;
            frag.TextState.FontSize = (float)effFs;
            if (TextState.ForegroundColor is { } fg) frag.TextState.ForegroundColor = fg;
            frag.Position = new Position(i == 0 ? myLLX : LxAt(i), by);
            tb.AppendText(frag);
        }
        page.ResetContentsCache();
        return true;
    }

    /// <summary>Greedy word-wrap of <paramref name="text"/> to <paramref name="maxWidth"/>
    /// using the font's real advance metrics at <paramref name="fs"/>. When
    /// <paramref name="trailingSpace"/> is set, each candidate line is measured WITH a
    /// trailing space (reserving one space width past each line, so lines break slightly
    /// earlier) and every completed (non-final) line keeps that trailing space — this keeps
    /// the wrapped lines re-searchable across the breaks.</summary>
    private static double MeasureOrEstimate(FontInfo font, string s, double fs, bool trailingSpace)
    {
        var m = trailingSpace ? s + " " : s;
        try { return font.MeasureString(m, fs); } catch { return m.Length * fs * 0.5; }
    }

    private static System.Collections.Generic.List<string> WrapToWidth(string text, FontInfo font, double fs, double maxWidth, bool trailingSpace = false, bool allowCharBreak = false, Func<string, double>? measure = null)
    {
        double M(string s) => measure?.Invoke(s) ?? MeasureOrEstimate(font, s, fs, trailingSpace);
        var lines = new System.Collections.Generic.List<string>();
        var words = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Split(' ');
        var cur = new System.Text.StringBuilder();
        foreach (var word in words)
        {
            if (word.Length == 0) continue;
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (M(trial) <= maxWidth)
            {
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(word);
                continue;
            }
            // The word doesn't fit the current line. Flush the line, then place the word fresh.
            if (cur.Length > 0)
            {
                lines.Add(trailingSpace ? cur.ToString() + " " : cur.ToString());
                cur.Clear();
            }
            if (!allowCharBreak || M(word) <= maxWidth)
            {
                // Fits on its own line (or char-break disabled: keep the original behaviour of a
                // lone over-wide word occupying its own line).
                cur.Append(word);
            }
            else
            {
                // A single word wider than the line (an unbreakable long token, e.g. a no-space
                // replacement): character-break it so it stays within the page instead of running
                // off the right edge. Emit as many leading characters as fit per line.
                int start = 0;
                while (start < word.Length)
                {
                    int take = 1;
                    while (start + take < word.Length &&
                           M(word.Substring(start, take + 1)) <= maxWidth) take++;
                    string chunk = word.Substring(start, take);
                    start += take;
                    if (start < word.Length) lines.Add(trailingSpace ? chunk + " " : chunk);
                    else cur.Append(chunk); // last chunk continues the current line
                }
            }
        }
        // Splitting on spaces drops one the text itself ENDS with, and that space is
        // part of the replacement: the extent reported for the last line covers it, the
        // same way every earlier line covers the break's own trailing space.
        if (cur.Length > 0)
            lines.Add(trailingSpace && text.Length > 0 && text[^1] == ' '
                ? cur.ToString() + " "
                : cur.ToString());
        return lines;
    }

    /// <summary>Height the wrapped block occupies at <paramref name="fs"/>, counting
    /// one em of ascent for the last line — the lower edge of the fit window.</summary>
    private static double BlockHeight(string text, FontInfo font, double fs,
        double wrapWidth, double leadingRatio, Func<string, double, double>? measure)
    {
        var n = WrapToWidth(text, font, fs, wrapWidth,
            measure: measure is null ? null : s => measure(s, fs)).Count;
        return (leadingRatio * (n - 1) + 1.0) * fs;
    }

    /// <summary>Fit a font size to the rectangle by bisecting between
    /// <paramref name="lo"/> and <paramref name="hi"/>, testing the wrapped block
    /// against a two-sided height window: the block must FIT the rectangle counting
    /// one em of ascent for the last line, and must FILL it counting 1.1 em. The
    /// first midpoint inside that window is the answer — which is why fitted sizes
    /// come out as exact dyadic fractions of the starting size. When no size can
    /// satisfy the window (the line count flips before it is reached) the search
    /// converges instead on the wrap threshold, the largest size whose widest line
    /// still fits the width.</summary>
    private static double FitFontSize(string text, FontInfo font, double wrapWidth,
        double targetHeight, double lo, double hi, double leadingRatio,
        Func<string, double, double>? measure = null)
    {
        (double Min, double Max) Window(double fs)
        {
            var n = WrapToWidth(text, font, fs, wrapWidth,
                measure: measure is null ? null : s => measure(s, fs)).Count;
            var lead = leadingRatio * (n - 1);
            return ((lead + 1.0) * fs, (lead + 1.1) * fs);
        }
        for (var it = 0; it < 48; it++)
        {
            var mid = (lo + hi) / 2;
            var w = Window(mid);
            if (w.Min > targetHeight) hi = mid;
            else if (w.Max < targetHeight) lo = mid;
            else return mid;
        }
        return lo;
    }

    /// <summary>When the fragment's edit options explicitly select
    /// <see cref="TextEditOptions.NoCharacterAction.ThrowException"/>, throw
    /// <see cref="InvalidOperationException"/> if the new text contains a character
    /// the fragment's font cannot represent.</summary>
    private void ThrowIfFontLacksGlyph(string newText)
    {
        if (_textEditOptions is not { NoCharacterBehaviorExplicit: true } teo
            || teo.NoCharacterBehavior != TextEditOptions.NoCharacterAction.ThrowException
            || TextState.Font is not { } font
            || string.IsNullOrEmpty(newText))
            return;

        foreach (var ch in newText)
            if (!font.CanRepresent(ch))
                throw new InvalidOperationException(
                    $"Font '{font.FontName}' does not contain a glyph for character " +
                    $"'{ch}' (U+{(int)ch:X4}).");
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

    /// <summary>
    /// The page this fragment was extracted from. When set, modifying <see cref="Text"/>
    /// will update the PDF content stream.
    /// </summary>
    internal Page? SourcePage { get; set; }

    /// <summary>The page this fragment belongs to (public alias for SourcePage).</summary>
    public Page? Page => SourcePage;

    /// <summary>Raw <c>re</c> operands (X, Y, Width, Height) of underline rectangles found
    /// in the source content beneath this fragment, captured when the absorber runs with
    /// <see cref="TextEditOptions.ToAttemptGetUnderlineFromSource"/>. If the fragment's
    /// underline is later toggled off, these locate the exact rectangle operators to splice
    /// out of the page content stream at save time.</summary>
    internal System.Collections.Generic.List<(double X, double Y, double W, double H)>? CapturedUnderlineSources;

    /// <summary>Records a captured source underline rectangle and marks this fragment and all
    /// its segments as underlined (without registering save-time underline injection).</summary>
    internal void MarkCapturedUnderlineSource(double x, double y, double w, double h)
    {
        (CapturedUnderlineSources ??= new()).Add((x, y, w, h));
        TextState.SetCapturedUnderline(true);
        if (_segments is not null)
            foreach (var s in _segments)
                s.TextState?.SetCapturedUnderline(true);
    }

    /// <summary>Raw <c>re</c> operands of background (highlight) rectangles drawn in the
    /// source content behind this fragment, captured when the absorber runs with
    /// <see cref="TextEditOptions.ToAttemptGetUnderlineFromSource"/>. When the fragment's
    /// text is replaced, these locate the old highlight to splice out so a new one can be
    /// drawn at the replacement's advance.</summary>
    internal System.Collections.Generic.List<(double X, double Y, double W, double H)>? CapturedBackgroundSources;

    /// <summary>Fill colour of the captured source background, re-used when the highlight
    /// is re-drawn for replaced text.</summary>
    internal Color? CapturedBackgroundColor;

    /// <summary>Records a captured source background (highlight) rectangle without
    /// registering save-time background injection.</summary>
    internal void MarkCapturedBackgroundSource(double x, double y, double w, double h, Color? color)
    {
        (CapturedBackgroundSources ??= new()).Add((x, y, w, h));
        CapturedBackgroundColor ??= color;
    }

    /// <summary>Measure the replacement text's advance for decoration sizing. A subset
    /// font carries widths only for its own glyphs, so when any replacement character
    /// lacks an explicit width the embedded metrics would degrade to default (1 em)
    /// widths — fall back to the real system face of the same family/style, which is
    /// what the replaced text renders in after the subset-glyph font switch.</summary>
    private static double MeasureReplacementAdvance(FontInfo font, string text, double fontSize)
    {
        var covered = true;
        try
        {
            var m = font.Metrics;
            foreach (var ch in text)
            {
                var code = m.IsCid ? ch : (ch < 256 ? ch : '?');
                if (!m.HasExplicitWidth(code)) { covered = false; break; }
            }
        }
        catch { covered = true; }
        if (!covered && font.FontName is { Length: > 0 } name)
        {
            try
            {
                // Measure with the system face's raw TTF metrics (FontData.MeasureString);
                // Font.MeasureString would consult the synthetic font dict, which carries
                // no widths for a repository-resolved face.
                var real = FontRepository.FindFont(name, ignoreCase: true);
                if (real?.SourceFontData is { } fd)
                {
                    var w = fd.MeasureString(text, fontSize);
                    if (w > 0) return w;
                }
            }
            catch { }
        }
        try { return font.MeasureString(text, fontSize); }
        catch { return -1; }
    }

    /// <summary>The text as last written to the content stream by TextBuilder.</summary>
    internal string? LastWrittenText { get; set; }

    /// <summary>The XForm this fragment was extracted from, or null for page-level fragments.</summary>
    public XForm? Form { get; internal set; }

    /// <summary>The Form XObject stream the fragment's text was extracted from when
    /// the page content reached it through <c>Do</c> (null for direct page text).
    /// Post-extraction edits that must land in the producing stream — the
    /// BackgroundColor highlight — write here instead of the page content.</summary>
    internal Core.PdfStream? SourceXObjStream { get; set; }

    /// <summary>
    /// Optional footnote attached to this fragment. Stored only; the
    /// layout engine does not currently render footnote references or
    /// the page-bottom note text.
    /// </summary>
    public Note? FootNote { get; set; }

    /// <summary>
    /// Horizontal alignment used when this fragment is laid out as a
    /// paragraph (added to <c>page.Paragraphs</c>). Stored on the
    /// fragment so callers can set alignment without touching
    /// <see cref="TextState"/>; the layout engine reads this on save.
    /// </summary>
    public new HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>
    /// Check if this fragment uses a CID/Type0 font (Arabic, CJK, etc.).
    /// CID fonts store text in visual order with multi-byte character codes,
    /// requiring segment-by-segment replacement when the combined text isn't
    /// found in a single content stream operator.
    /// </summary>
    private bool IsCidFontFragment()
    {
        // Check font metadata first
        foreach (var seg in _segments)
        {
            if (seg.TextState?.Font?.IsCid == true) return true;
        }
        // Fallback: detect by Arabic/CJK presentation forms in the text
        foreach (var seg in _segments)
        {
            foreach (var ch in seg.Text)
            {
                if ((ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF') ||
                    (ch >= '\u3000' && ch <= '\u9FFF'))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Under <see cref="TextEditOptions.ClippingPathsProcessingMode.Expand"/>,
    /// after a paragraph reflow APPENDED overflow line(s) below the paragraph's last
    /// baseline: extend the bottom of every clip rectangle that contains the paragraph
    /// down to the appended line's baseline (the new clip is the
    /// union of the old clip and the reflowed extent — bottom lands exactly ON the
    /// appended baseline, top/left/right unchanged, and a clip never shrinks).</summary>
    private void ExpandClipsToReflowBottom(Page page,
        System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> paraLines,
        double newBottom)
    {
        if (double.IsNaN(newBottom) || paraLines.Count == 0) return;
        var expand = _textEditOptions?.ClippingPathsProcessing
            == TextEditOptions.ClippingPathsProcessingMode.Expand;
        if (!expand)
            foreach (var s in _segments)
                if (s.TextEditOptions.ClippingPathsProcessing
                    == TextEditOptions.ClippingPathsProcessingMode.Expand)
                {
                    expand = true;
                    break;
                }
        if (!expand) return;

        double paraLeft = double.MaxValue, paraRight = 0;
        foreach (var l in paraLines)
        {
            if (l.lx < paraLeft) paraLeft = l.lx;
            if (l.rx > paraRight) paraRight = l.rx;
        }
        double paraTop = paraLines[0].y, paraBottom = paraLines[^1].y;

        var ops = page.Contents;
        ops.EnsureMaterialized();
        Aspose.Pdf.Operator? prev = null;
        var changed = false;
        foreach (var op in ops)
        {
            if (op is Aspose.Pdf.Operators.Clip or Aspose.Pdf.Operators.EOClip
                && prev is Aspose.Pdf.Operators.Re re
                && re.X <= paraLeft + 5 && re.X + re.Width >= paraRight - 5
                && re.Y <= paraBottom + 2 && re.Y + re.Height >= paraTop - 2
                && re.Y > newBottom + 0.01)
            {
                re.Height += re.Y - newBottom;
                re.Y = newBottom;
                changed = true;
            }
            prev = op;
        }
        if (changed) ops.FlushToPage();
    }

    /// <summary>Widen every clip rectangle (<c>re</c> followed by <c>W</c>/<c>W*</c>)
    /// that contains the point (x, y) but is too narrow to fit <paramref name="neededWidth"/>
    /// of text starting at x. Used by <see cref="Text"/> under
    /// <see cref="TextEditOptions.ClippingPathsProcessingMode.Expand"/>.</summary>
    private static void ExpandTightClipsAround(Page page, double x, double y, double neededWidth)
    {
        var ops = page.Contents;
        // Materialize first: a plain enumeration yields throw-away parses whose
        // mutations never reach the stream.
        ops.EnsureMaterialized();
        Aspose.Pdf.Operator? prev = null;
        var changed = false;
        foreach (var op in ops)
        {
            if (op is Aspose.Pdf.Operators.Clip or Aspose.Pdf.Operators.EOClip
                && prev is Aspose.Pdf.Operators.Re re
                && re.X <= x + 0.5 && re.X + re.Width >= x - 0.5
                && re.Y <= y + 0.5 && re.Y + re.Height >= y - 0.5
                && re.X + re.Width < x + neededWidth - 0.25)
            {
                re.Width = x + neededWidth + 0.75 - re.X;
                changed = true;
            }
            prev = op;
        }
        if (changed) ops.FlushToPage();
    }

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
            // Compute descent from font metrics
            double descent = 0;
            var font = TextState.Font;
            var metrics = font?.GetMetrics();
            var fs = TextState.FontSize;
            if (metrics is not null && metrics.Descent != 0)
                descent = metrics.Descent * fs / 1000.0; // negative value
            return new Position(p.XIndent, p.YIndent - descent);
        }
        set
        {
            if (value is null) { _position = null; _positionExplicit = false; return; }
            // Reverse: add descent to get Position from BaselinePosition
            double descent = 0;
            var font = TextState.Font;
            var metrics = font?.GetMetrics();
            var fs = TextState.FontSize;
            if (metrics is not null && metrics.Descent != 0)
                descent = metrics.Descent * fs / 1000.0;
            _position = new Position(value.XIndent, value.YIndent + descent);
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

    /// <summary>The page index (0-based) this fragment was found on.</summary>
    public int PageIndex { get; internal set; }

    /// <summary>Tag this fragment's written content as marked content: the ops
    /// emitted for it are wrapped in <c>/name &lt;&lt;/MCID id&gt;&gt; BDC … EMC</c>.
    /// Consecutive fragments carrying the SAME tag and id share one block.</summary>
    internal void SetMarkedContentProperties(string name, int id)
    {
        TextState.MarkedContentTag = name;
        TextState.MarkedContentMcid = id;
    }

    /// <summary>
    /// Text direction in page space — the reading-direction unit vector
    /// transformed by both the text matrix and the CTM.
    /// For horizontal LTR text: (1, 0). For 90° rotated vertical text: (0, ±1).
    /// </summary>
    internal double TextDirX { get; set; } = 1;
    internal double TextDirY { get; set; }

    /// <summary>
    /// Trailing character spacing (Tc * HScaling * TmA) in page space, subtracted
    /// from Rectangle.Width to get the visual glyph-only width for bg rect rendering.
    /// </summary>
    internal double TrailingTcPageSpace { get; set; }

    /// <summary>
    /// The CTM that was active when this fragment was extracted.
    /// Used to transform page-space coordinates back to content-stream space
    /// when injecting background/underline rectangles.
    /// </summary>
    internal Matrix? ExtractionCtm { get; set; }

    /// <summary>The text-space Y of the fragment's first run (the line's Tm F
    /// plus any Td displacement) — with a FLIPPED text matrix (TmD &lt; 0) the
    /// background highlight replays the run's y-up frame, whose translation
    /// needs this value.</summary>
    internal double ExtractionTmTy { get; set; }

    /// <summary>Accumulated Position delta applied after absorption — geometry
    /// consumers (the background highlight) add it to the extraction rectangle.</summary>
    internal double PostAbsorbDx { get; set; }
    internal double PostAbsorbDy { get; set; }

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

    /// <summary>
    /// Isolate the character range <c>[startIndex, startIndex+length)</c>
    /// (<paramref name="startIndex"/> is a 0-based character offset into the
    /// fragment's text) into its own <see cref="TextSegment"/>(s): the
    /// covering segment is split into up to three pieces (before / isolated /
    /// after), each inheriting the original segment's <see cref="TextState"/>,
    /// and the fragment's <see cref="Segments"/> collection is rebuilt to
    /// reflect the split. Returns the isolated (middle) segments so a caller
    /// can restyle just that range, e.g. recolour "95" inside "Windows 95 ".
    /// </summary>
    public TextSegmentCollection IsolateTextSegments(int startIndex, int length)
    {
        var result = new TextSegmentCollection();
        if (length <= 0 || startIndex < 0) return result;

        var rangeStart = startIndex;
        var rangeEnd = startIndex + length;
        var rebuilt = new List<TextSegment>();
        var cursor = 0;
        foreach (var seg in _segments)
        {
            var text = seg.Text ?? string.Empty;
            var segStart = cursor;
            var segEnd = cursor + text.Length;
            cursor = segEnd;

            // No overlap with the isolation range — keep the segment intact.
            if (segEnd <= rangeStart || segStart >= rangeEnd)
            {
                rebuilt.Add(seg);
                continue;
            }

            // Overlap, expressed in this segment's local coordinates.
            var localStart = Math.Max(rangeStart, segStart) - segStart;
            var localEnd = Math.Min(rangeEnd, segEnd) - segStart;

            if (localStart > 0)
                rebuilt.Add(CloneSegmentText(seg, text.Substring(0, localStart)));

            var isolated = CloneSegmentText(seg, text.Substring(localStart, localEnd - localStart));
            rebuilt.Add(isolated);
            result.Add(isolated);

            if (localEnd < text.Length)
                rebuilt.Add(CloneSegmentText(seg, text.Substring(localEnd)));
        }

        _segments.Clear();
        foreach (var s in rebuilt) _segments.Add(s);
        RefreshTextFromSegments();
        return result;
    }

    /// <summary>New <see cref="TextSegment"/> carrying <paramref name="text"/>
    /// with a copy of <paramref name="src"/>'s text state.</summary>
    private static TextSegment CloneSegmentText(TextSegment src, string text)
    {
        var s = new TextSegment(text);
        s.TextState.ApplyChangesFrom(src.TextState);
        return s;
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

    /// <summary>Clone the fragment AND its segments. The cloned fragment
    /// has fresh segment instances that mirror the source's text+state.</summary>
    public object CloneWithSegments()
    {
        var copy = (TextFragment)Clone();
        copy._segments.Clear();
        foreach (var s in _segments)
        {
            var fresh = new TextSegment(s.Text);
            fresh.TextState.ApplyChangesFrom(s.TextState);
            copy._segments.Add(fresh);
        }
        copy.RefreshTextFromSegments();
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

    /// <summary>Internal read access to the hyperlink set via <see cref="Hyperlink"/>,
    /// used by the page layout pass to emit the corresponding link annotation.</summary>
    internal Hyperlink? HyperlinkValue => _hyperlink;

    /// <summary>Endnote attached to this fragment. Stored only.</summary>
    public Note? EndNote { get; set; }

    /// <summary>Number of wrapped lines computed during layout.
    /// 0 until layout runs.</summary>
    public int WrapLinesCount { get; set; }

    internal void RefreshTextFromSegments()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var seg in _segments)
            sb.Append(seg.Text);
        _text = sb.ToString();
    }

    /// <summary>Set the text as absorbed from the page — a match can contain
    /// line-break sentinels that belong to no segment, so the segment join
    /// can't reproduce it. Leaves segments untouched.</summary>
    internal void SetAbsorbedText(string text) => _text = text;
}
