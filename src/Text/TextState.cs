namespace Aspose.Pdf.Text;

/// <summary>
/// Represents a position on a page.
/// </summary>
/// <summary>Font style flags matching the public API.</summary>
[Flags]
public enum FontStyles
{
    Regular = 0,
    Bold = 1,
    Italic = 2,
}


/// <summary>
/// Text formatting state.
/// </summary>
public partial class TextState
{
    /// <summary>Default tab-stop width in PDF points (56 pt ≈ 0.78 in,
    /// matches Adobe's default tab spacing). Declared as an instance
    /// field so reflection-based callers see a non-static field.</summary>
    public float TabstopDefaultValue = 56f;

    /// <summary>Creates a text state with the default settings: Helvetica at 10 points.</summary>
    public TextState() { }

    /// <summary>Creates a text state with the given font size, in points.</summary>
    public TextState(double fontSize) { FontSize = (float)fontSize; }

    /// <summary>Creates a text state that uses the named font family.</summary>
    public TextState(string fontFamily) { FontName = fontFamily; }

    /// <summary>Creates a text state that uses the named font family at the given size, in points.</summary>
    public TextState(string fontFamily, double fontSize)
    {
        FontName = fontFamily;
        FontSize = (float)fontSize;
    }

    /// <summary>Creates a text state that uses the named font family with the given bold and italic flags; the styled face (e.g. Times-Bold) is resolved when the font is applied.</summary>
    public TextState(string fontFamily, bool bold, bool italic)
    {
        // Keep the family name clean and carry the requested style as flags. The styled
        // base-font name (e.g. "Times" + Bold → Times-Bold, "Courier" + Italic →
        // Courier-Oblique) is resolved from FontName + FontStyle at the point the font is
        // applied, so the standard-14 oblique/italic spelling differences are handled in
        // one place instead of being baked into the name here.
        FontName = fontFamily;
        IsBold = bold;
        IsItalic = italic;
    }

    /// <summary>Creates a text state with the given text (foreground) colour.</summary>
    public TextState(System.Drawing.Color foregroundColor)
    {
        ForegroundColor = Color.FromRgb(foregroundColor);
    }

    /// <summary>Creates a text state with the given text (foreground) colour and font size, in points.</summary>
    public TextState(System.Drawing.Color foregroundColor, double fontSize)
    {
        ForegroundColor = Color.FromRgb(foregroundColor);
        FontSize = (float)fontSize;
    }

    /// <summary>Colours, style, face and size in one call. The style REPLACES the face's
    /// own: a regular face with <see cref="FontStyles.Italic"/> resolves to the family's
    /// italic sibling, and an italic face with <see cref="FontStyles.Bold"/> to the
    /// family's bold (not bold-italic) — the face contributes its family, the caller the
    /// style. A family without the styled sibling keeps the face it was given.</summary>
    public TextState(System.Drawing.Color foregroundColor, System.Drawing.Color backgroundColor,
        FontStyles fontStyle, Font font, double fontSize)
    {
        ForegroundColor = Color.FromRgb(foregroundColor);
        BackgroundColor = Color.FromRgb(backgroundColor);
        FontSize = (float)fontSize;
        IsBold = (fontStyle & FontStyles.Bold) != 0;
        IsItalic = (fontStyle & FontStyles.Italic) != 0;
        if (font is null) return;
        var family = FaceFamily(font);
        Font = FontRepository.TryFindFont(family, fontStyle, ignoreCase: true) ?? font;
    }

    /// <summary>The family a face belongs to: its base name with the style suffix removed,
    /// whether the face spells it "Times New Roman-Italic" (a suffix) or "Times New Roman
    /// Italic" (a trailing word, the system face names' spelling).</summary>
    private static string FaceFamily(Font font)
    {
        var family = FontRepository.FamilyOf(font.BaseFont ?? font.FontName);
        foreach (var style in new[] { " Bold Italic", " Bold", " Italic", " Regular" })
        {
            if (family.Length > style.Length
                && family.EndsWith(style, StringComparison.OrdinalIgnoreCase))
                return family.Substring(0, family.Length - style.Length);
        }
        return family;
    }

    /// <summary>Gets or sets the name of the font family used for the text; <c>null</c> when not set.</summary>
    public string? FontName { get; set; }

    /// <summary>Emit a /FontDescriptor (line-box Ascent/Descent) on the Standard-14
    /// font dict this fragment resolves to. Opt-in for writers that want the
    /// extraction rect to carry real ascent/descent (the hOCR overlay) without
    /// changing the descriptor-less dicts every other generator path emits.</summary>
    internal bool EmitStandard14Descriptor { get; set; }

    /// <summary>When set, the writer uses this base-font name for the Standard-14
    /// resource instead of the alias-mapped one (e.g. "Arial" written as itself,
    /// with its own face metrics, rather than collapsed to Helvetica).</summary>
    internal string? Std14FaceOverride { get; set; }

    /// <summary>Advance widths (1000ths of an em, unrounded, by character code) to
    /// write as the font's own /Widths, so the extent read back off the page is the
    /// one the text was laid out with. Null keeps the core face's built-in table.</summary>
    internal double[]? Std14Widths { get; set; }

    /// <summary>Horizontal scale of the text matrix the source run was drawn under.
    /// Every glyph advance in that run — and so the extent the fragment reports —
    /// is scaled by it, and a replacement written into the run's place has to carry
    /// the same scale to occupy the same width. 1 for ordinary unscaled text.</summary>
    internal double SourceTmScale { get; set; } = 1.0;

    /// <summary>On-page start X (points) of the text this state belongs to.
    /// Populated for states surfaced through <see cref="TextSegment.PhysicalSegment"/>.</summary>
    public float TextXIndent { get; internal set; }

    /// <summary>Text height in points: (Ascent + |Descent|) · FontSize / 1000 from
    /// the font's descriptor metrics. Falls back to the bare font size when no
    /// descriptor metrics are available.</summary>
    public float TextHeight
    {
        get
        {
            var m = Font?.GetMetrics();
            if (m is not null && m.Ascent > 0 && m.Descent != 0)
                return (float)((m.Ascent + Math.Abs(m.Descent)) * FontSize / 1000.0);
            return FontSize;
        }
    }

    private double _fontSize = 10;

    /// <summary>Gets or sets the font size, in points; the default is 10. Throws for NaN or infinity. On a fragment found on a page, a change rewrites the page content.</summary>
    public float FontSize
    {
        get => (float)_fontSize;
        set
        {
            // A non-finite size is rejected outright. Zero and negative stay
            // legal: real documents carry Tf 0 (hidden OCR text) and negative
            // sizes (vertically mirrored text), and the absorbers surface
            // those parsed values through this same setter.
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("Incorrect font size value");
            FontSizeTouched = true;
            if (Math.Abs(_fontSize - value) < 0.0001) return;
            var oldSize = _fontSize;
            _fontSize = value;
            // If this state belongs to a fragment from a page, update the content stream
            ApplyFontSizeChange(oldSize, value);
        }
    }

    /// <summary>True once the public FontSize setter ran — distinguishes an
    /// explicit caller size from the ctor's 10pt placeholder.</summary>
    internal bool FontSizeTouched { get; private set; }

    // One flag per property a caller can set: a state starts with every property at its
    // default, and a consumer that inherits values (a segment from its fragment, a
    // fragment from its page) must tell a chosen value from a default one.
    internal bool FontTouched { get; private set; }
    /// <summary>True once the caller assigned a face to a run already on a page: the
    /// replacement that follows re-encodes that run and carries the state with it.</summary>
    internal bool FaceReassigned { get; private set; }
    internal bool FontStyleTouched { get; private set; }
    internal bool ForegroundColorTouched { get; private set; }
    internal bool BackgroundColorTouched { get; private set; }
    internal bool StrokingColorTouched { get; private set; }
    internal bool CharacterSpacingTouched { get; private set; }
    internal bool WordSpacingTouched { get; private set; }
    internal bool HorizontalScalingTouched { get; private set; }
    internal bool LineSpacingTouched { get; private set; }
    internal bool UnderlineTouched { get; private set; }
    internal bool StrikeOutTouched { get; private set; }

    /// <summary>The font size the caller set, or null while it is still the default.</summary>
    internal float? ExplicitFontSize => FontSizeTouched ? FontSize : null;
    /// <summary>The font the caller set, or null while it is still the default.</summary>
    internal Font? ExplicitFont => FontTouched ? Font : null;
    /// <summary>The style the caller set, or null while it is still the default.</summary>
    internal FontStyles? ExplicitFontStyle => FontStyleTouched ? FontStyle : null;
    /// <summary>The fill colour the caller set, or null while it is still the default.</summary>
    internal Color? ExplicitForegroundColor => ForegroundColorTouched ? ForegroundColor : null;
    /// <summary>The background colour the caller set, or null while it is still the default.</summary>
    internal Color? ExplicitBackgroundColor => BackgroundColorTouched ? BackgroundColor : null;
    /// <summary>The stroking colour the caller set, or null while it is still the default.</summary>
    internal Color? ExplicitStrokingColor => StrokingColorTouched ? StrokingColor : null;
    /// <summary>The character spacing the caller set, or null while it is still the default.</summary>
    internal float? ExplicitCharacterSpacing => CharacterSpacingTouched ? CharacterSpacing : null;
    /// <summary>The word spacing the caller set, or null while it is still the default.</summary>
    internal float? ExplicitWordSpacing => WordSpacingTouched ? WordSpacing : null;
    /// <summary>The horizontal scaling the caller set, or null while it is still the default.</summary>
    internal float? ExplicitHorizontalScaling => HorizontalScalingTouched ? HorizontalScaling : null;
    /// <summary>The line spacing the caller set, or null while it is still the default.</summary>
    internal float? ExplicitLineSpacing => LineSpacingTouched ? LineSpacing : null;
    /// <summary>The underline choice the caller made, or null while it is still the default.</summary>
    internal bool? ExplicitUnderline => UnderlineTouched ? Underline : null;
    /// <summary>The strike-out choice the caller made, or null while it is still the default.</summary>
    internal bool? ExplicitStrikeOut => StrikeOutTouched ? StrikeOut : null;

    /// <summary>True when LineSpacing was assigned by an internal layout path
    /// (e.g. the HTML block renderer's 1.2× pitch) rather than the caller. A
    /// CALLER-set LineSpacing adds its leading above the FIRST line too (a
    /// 10 pt fragment with LineSpacing 13 starts one 23 pt pitch below the
    /// cursor); synthetic leading keeps the legacy
    /// first-line drop of one font size.</summary>
    internal bool LineSpacingSynthetic { get; set; }

    /// <summary>Line pitch (points) the flow paginator reserved for a deferred
    /// multi-line chunk. The generator advances embedded-face lines by the font
    /// size alone (a 10 pt Arial Unicode paragraph pitches at 10, a 12 pt Arial
    /// one at 12), so the writer must not fall back to its 1.2 em default.</summary>
    internal double? FlowLinePitch { get; set; }

    /// <summary>True when the fragment's lines are CSS line BOXES of
    /// <c>FontSize + LineSpacing</c>: the first baseline on a page then seats at
    /// half the surplus leading plus the face's ascent below the content top
    /// (12 pt Arial on a 13.5 pt box: 0.05 + 10.86), instead of dropping by a
    /// whole font size. Set by the in-page HtmlFragment renderer when the CALLER
    /// declared the fragment's line spacing.</summary>
    internal bool LineBoxSeat { get; set; }

    /// <summary>True when the pitch is the CSS <c>line-height: normal</c> box the
    /// FACE itself defines (its pixel-quantised win metrics), rather than a caller's
    /// declared spacing: the first line's baseline seats half the box's surplus
    /// leading plus the face's ascent below the box top — 12 pt Arial in a 13.5 pt
    /// box sits 10.91 down — and the seat is expressed, like every other one the flow
    /// computes, as the text rect's BOTTOM (a fragment Position, the face's descent
    /// below that baseline). Set by the in-page HtmlFragment block renderer.</summary>
    internal bool CssLineBoxSeat { get; set; }

    /// <summary>Raw font size from the Tf operator (before text matrix scaling).</summary>
    internal float RawFontSize { get; set; }

    /// <summary>Text matrix D component (vertical scale) for height computation.</summary>
    internal double TmD { get; set; } = 1.0;

    /// <summary>Owner segment — needed to walk back to the source page for content stream updates.</summary>
    internal TextSegment? OwnerSegment { get; set; }

    /// <summary>Owner fragment — for fragment-level TextState, allows registration for save-time effects.</summary>
    internal TextFragment? OwnerFragment { get; set; }

    /// <summary>True when this state belongs to an ATTACHED fragment — one a
    /// TextBuilder append wrote into its own content-stream segment. Such a fragment is
    /// written again from its state at save, so a property change here must NOT also
    /// patch the operators: the two writers would fight over the same run.</summary>
    private bool OwnerWrittenByBuilder =>
        (OwnerSegment?.Owner ?? OwnerFragment) is { AttachedSegment: not null };

    private void ApplyFontSizeChange(double oldSize, double newSize)
    {
        // Segment-level state reaches its page via the owning fragment; a
        // fragment-level state (TextFragmentState) only has OwnerFragment —
        // fall back to it so absorbed fragments write the new size through
        // to the page content stream too.
        var page = OwnerSegment?.Owner?.SourcePage ?? OwnerFragment?.SourcePage;
        if (page is null || OwnerWrittenByBuilder) return;
        var text = OwnerSegment?.Text ?? OwnerFragment?.Text;
        if (string.IsNullOrEmpty(text)) return;
        var modifier = new TextStateModifier();
        // Segment-level resize keeps the historical semantics (patch the
        // covering Tf even when it also governs neighbouring shows — a
        // sub-run resize resizes its whole run). Fragment-level resize is
        // collateral-free: it only rewrites when the covering Tf runs are
        // wholly inside the fragment's text, else it leaves the stream alone.
        // The owner's absorbed rectangle, page edge and face measure let a whole-show
        // resize re-seat the rest of its line the way the reference does.
        TextStateModifier.LineReseat? reseat = null;
        Page livePage = page;
        if (Font is { } face)
            reseat = new TextStateModifier.LineReseat(livePage.Rect.URX,
                (t, size) => face.MeasureString(t, size),
                () =>
                {
                    var live = new TextFragmentAbsorber();
                    livePage.Accept(live);
                    var rects = new List<(double llx, double urx, double lly)>(live.TextFragments.Count);
                    foreach (TextFragment f in live.TextFragments)
                        rects.Add(f.Rectangle is { } r ? (r.LLX, r.URX, r.LLY) : (double.NaN, double.NaN, double.NaN));
                    return rects;
                });
        // A face assigned by the caller is applied by the re-encode of the replacement that
        // follows (the show it absorbed stays intact for it), so the size rides along there
        // and a sub-run is not split here.
        modifier.ModifyFontSize(page, text, oldSize, newSize,
            allowCollateral: OwnerSegment is not null, reseat, splitSubRun: !FaceReassigned);
        // Keep the fragment's segment states in sync without re-triggering
        // a second content-stream rewrite per segment.
        if (OwnerSegment is null && OwnerFragment is not null)
            foreach (var seg in OwnerFragment.Segments)
                seg.TextState.SetFontSizeQuiet(newSize);
    }

    /// <summary>Set the stored font size without the content-stream
    /// write-back side effect (used to sync segment states after a
    /// fragment-level change already rewrote the stream).</summary>
    internal void SetFontSizeQuiet(double value) => _fontSize = value;

    private Color? _foregroundColor;
    /// <summary>Gets or sets the text fill colour; <c>null</c> when not set. On a fragment found on a page, a change recolours that text in the page content.</summary>
    public Color? ForegroundColor
    {
        get => _foregroundColor;
        set
        {
            _foregroundColor = value;
            ForegroundColorTouched = true;
            if (value is null) return;
            // Mirror the FontSize/BackgroundColor side-effects: when this
            // TextState belongs to a segment from a page, propagate the new
            // fill colour to the content stream by injecting an `R G B rg`
            // before the segment's Tj/TJ operator. Pass the segment's X/Y so
            // the same text at multiple positions doesn't all get coloured by
            // a single setter call (an X+Y-scoped pass runs first; a Y-only
            // pass keeps the historical reach when the X anchor finds nothing).
            if (OwnerWrittenByBuilder) return;
            var page = OwnerSegment?.Owner?.SourcePage;
            var text = OwnerSegment?.Text;
            if (page is not null && !string.IsNullOrEmpty(text))
            {
                ModifySegmentColor(page, text!, value,
                    OwnerSegment!.Position?.YIndent ?? OwnerSegment.Owner?.PositionOrNull?.YIndent,
                    OwnerSegment.Position?.XIndent);
                return;
            }
            // Fragment-level state (the absorber's TextFragment.TextState): recolour
            // each segment's own show operator, mirroring ApplyFontSizeChange's
            // fragment fallback. Without this, fragment-level recolours were a no-op.
            if (OwnerFragment?.SourcePage is { } fragPage)
            {
                foreach (var seg in OwnerFragment.Segments)
                {
                    if (string.IsNullOrEmpty(seg.Text)) continue;
                    seg.TextState.SetCapturedForegroundColor(value);
                    ModifySegmentColor(fragPage, seg.Text, value,
                        (seg.BaselinePosition ?? seg.Position)?.YIndent,
                        seg.Position?.XIndent);
                }
            }

            static void ModifySegmentColor(Page pg, string segText, Color c, double? y, double? x)
            {
                var modifier = new TextStateModifier();
                modifier.ModifyForegroundColor(pg, segText, c, y, x);
                // X anchor missed — the segment starts mid-run, or an earlier replacement
                // on the same line has already shifted it by its width delta. Keep the X
                // anchor and take the NEAREST occurrence rather than dropping to a Y-only
                // scope, which repaints the first occurrence on the line every time and so
                // leaves the later ones (and only them) unrecoloured.
                if (x.HasValue && !modifier.LastForegroundColorApplied)
                    modifier.ModifyForegroundColor(pg, segText, c, y, x, nearestX: true);
            }
        }
    }

    /// <summary>Assigns the captured foreground color from absorber graphics-state
    /// tracking without triggering content-stream injection. Used by
    /// TextFragmentAbsorber when reading existing text colour during extraction.</summary>
    internal void SetCapturedForegroundColor(Color? color) => _foregroundColor = color;

    /// <summary>Stroking (outline) color of the text. Used together with
    /// a non-zero <see cref="RenderingMode"/> (1 = stroke, 2 = fill+stroke).</summary>
    public Color? StrokingColor
    {
        get => _strokingColor;
        set { _strokingColor = value; StrokingColorTouched = true; }
    }
    private Color? _strokingColor;

    /// <summary>Whether text positioning treats Y as the baseline or the descender.
    /// Default is <see cref="CoordinateOrigin.Descender"/>.</summary>
    public CoordinateOrigin CoordinateOrigin { get; set; } = CoordinateOrigin.Descender;

    private Color? _backgroundColor;
    /// <summary>Gets or sets the colour of a rectangle painted behind the text; <c>null</c> (the default) paints none.</summary>
    public Color? BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            _backgroundColor = value;
            BackgroundColorTouched = true;
            // When BackgroundColor is set on a segment obtained via TextFragmentAbsorber,
            // register the owning fragment for rectangle injection during save.
            if (value is not null && !OwnerWrittenByBuilder)
            {
                // Segment-level: register via segment's owner fragment
                if (OwnerSegment?.Owner?.SourcePage is not null)
                    OwnerSegment.Owner.SourcePage.RegisterBgColorFragment(OwnerSegment.Owner);
                // Fragment-level TextState: register the fragment directly
                else if (OwnerFragment?.SourcePage is not null)
                    OwnerFragment.SourcePage.RegisterBgColorFragment(OwnerFragment);
            }
        }
    }

    /// <summary>Assigns the captured background color from absorber graphics-state
    /// tracking without triggering rect-injection registration. Used by
    /// TextFragmentAbsorber when SearchForTextRelatedGraphics is enabled.</summary>
    internal void SetCapturedBackgroundColor(Color? color) => _backgroundColor = color;

    /// <summary>Whether the font is bold.</summary>
    public bool IsBold { get; set; }

    /// <summary>Whether the font is italic.</summary>
    public bool IsItalic { get; set; }

    /// <summary>Font style flags (Bold, Italic, etc.).</summary>
    public FontStyles FontStyle
    {
        get
        {
            var s = FontStyles.Regular;
            if (IsBold) s |= FontStyles.Bold;
            if (IsItalic) s |= FontStyles.Italic;
            return s;
        }
        set
        {
            FontStyleTouched = true;
            var wasBold = IsBold;
            var wasItalic = IsItalic;
            IsBold = (value & FontStyles.Bold) != 0;
            IsItalic = (value & FontStyles.Italic) != 0;
            if (IsBold != wasBold || IsItalic != wasItalic)
                ApplyFontStyleToSource();
        }
    }

    /// <summary>Re-show an ABSORBED run in the styled sibling of its own face, so setting
    /// FontStyle on a fragment the absorber produced actually renders bold/italic. Storing
    /// the flags alone left the property a silent no-op: the run kept being drawn with the
    /// face already selected in the content stream. Routed through the same font-swap the
    /// <see cref="Font"/> setter uses, so the change scopes to this run's own glyphs.
    /// <para>A state that belongs to no page — a fragment being composed for the generator
    /// or a TextBuilder append — keeps storing the flags only: those writers read the state
    /// when they lay the text out, and patching operators here would fight them.</para>
    /// </summary>
    private void ApplyFontStyleToSource()
    {
        var page = OwnerSegment?.Owner?.SourcePage ?? OwnerFragment?.SourcePage;
        var text = OwnerSegment?.Text ?? OwnerFragment?.Text;
        if (page is null || string.IsNullOrEmpty(text) || OwnerWrittenByBuilder) return;
        // The face the run is drawn in now, as a family name the repository can style:
        // its /BaseFont with the subset tag and any existing style suffix removed.
        var current = _font ?? OwnerSegment?.Owner?.TextState._font;
        var family = FontRepository.FamilyOf(current?.FontName ?? current?.BaseFont ?? FontName);
        if (string.IsNullOrEmpty(family)) return;
        var wanted = FontStyles.Regular;
        if (IsBold) wanted |= FontStyles.Bold;
        if (IsItalic) wanted |= FontStyles.Italic;
        var styled = FontRepository.TryFindFont(family!, wanted, ignoreCase: true);
        // Only a face that carries a program can be embedded and shown; a repository miss
        // (or a resolve back to the same unstyled file) leaves the run as it was.
        if (styled?.SourceFontData is null) return;
        try
        {
            new TextStateModifier().ModifyFont(page, text!, styled,
                OwnerSegment?.Position?.YIndent ?? OwnerFragment?.PositionOrNull?.YIndent,
                segmentScoped: OwnerSegment is not null);
        }
        catch { /* best-effort: leave the content unchanged if the rewrite fails */ }
    }

    /// <summary>Whether the text is underlined (alias for <see cref="IsUnderline"/>).</summary>
    public bool Underline
    {
        get => _isUnderline;
        set
        {
            _isUnderline = value;
            _underlineRequested = value;
            UnderlineTouched = true;
            // Register the owning fragment for underline-rect injection during save.
            // Try segment ownership first (segment-level TextState), then fragment ownership.
            var frag = OwnerSegment?.Owner ?? OwnerFragment;
            if (value)
            {
                frag?.SourcePage?.RegisterUnderlineFragment(frag);
            }
            // Turning underline off on a fragment whose source underline was captured
            // (ToAttemptGetUnderlineFromSource): register it so the source rectangle is
            // spliced out of the content stream at save time.
            else if (frag?.CapturedUnderlineSources is { Count: > 0 })
            {
                frag.SourcePage?.RegisterUnderlineRemoval(frag);
            }
        }
    }

    private bool _isUnderline;

    /// <summary>Whether the text is underlined.</summary>
    public bool IsUnderline
    {
        get => _isUnderline;
        set => Underline = value; // delegate to the registering setter
    }

    /// <summary>Assigns the captured underline state from absorber graphics-state
    /// tracking without triggering save-time rect-injection. Used by
    /// TextFragmentAbsorber when SearchForTextRelatedGraphics is enabled.</summary>
    internal void SetCapturedUnderline(bool value) => _isUnderline = value;

    /// <summary>True only when an underline was ASKED for through the public setter, as
    /// opposed to merely OBSERVED under the source text. A writer must draw a rule for
    /// the first and not the second: text sitting just above a page rule captures that
    /// rule as its underline, and re-emitting it lays a second, thinner copy over the
    /// original.</summary>
    internal bool UnderlineRequested => _underlineRequested;
    private bool _underlineRequested;

    /// <summary>Assigns the captured strikeout state from absorber graphics-state
    /// tracking without triggering the save-time strikeout-fragment registration
    /// that the public <see cref="IsStrikeOut"/> setter performs.</summary>
    internal void SetCapturedStrikeOut(bool value) => _isStrikeOut = value;

    private bool _isStrikeOut;

    /// <summary>Whether the text has strikethrough.</summary>
    public bool IsStrikeOut
    {
        get => _isStrikeOut;
        set
        {
            _isStrikeOut = value;
            if (value)
            {
                var frag = OwnerSegment?.Owner ?? OwnerFragment;
                frag?.SourcePage?.RegisterStrikeOutFragment(frag);
            }
        }
    }

    /// <summary>Alias for <see cref="IsStrikeOut"/>.</summary>
    public bool StrikeOut
    {
        get => IsStrikeOut;
        set { IsStrikeOut = value; StrikeOutTouched = true; }
    }

    /// <summary>Whether the text is superscript.</summary>
    public bool IsSuperscript { get; set; }

    /// <summary>Alias for <see cref="IsSuperscript"/>.</summary>
    public bool Superscript
    {
        get => IsSuperscript;
        set => IsSuperscript = value;
    }

    /// <summary>Whether the text is subscript.</summary>
    public bool IsSubscript { get; set; }

    /// <summary>Alias for <see cref="IsSubscript"/>.</summary>
    public bool Subscript
    {
        get => IsSubscript;
        set => IsSubscript = value;
    }

    /// <summary>Character spacing in text space units.</summary>
    public float CharacterSpacing
    {
        get => _characterSpacing;
        set { _characterSpacing = value; CharacterSpacingTouched = true; }
    }
    private float _characterSpacing;

    /// <summary>Word spacing in text space units.</summary>
    public float WordSpacing
    {
        get => _wordSpacing;
        set { _wordSpacing = value; WordSpacingTouched = true; }
    }
    private float _wordSpacing;

    /// <summary>Horizontal scaling percentage (default 100).</summary>
    public float HorizontalScaling
    {
        get => _horizontalScaling;
        set { _horizontalScaling = value; HorizontalScalingTouched = true; }
    }
    private float _horizontalScaling = 100;

    /// <summary>Line spacing (leading) in text space units.</summary>
    /// <summary>The line-box ascent this caller declares, in em (1 = the font
    /// size), or null to use the face's own. Only read under
    /// <see cref="TextFormattingOptions.LineSpacingMode.LineBox"/>.
    ///
    /// A face carries several defensible ascent/descent pairs (AFM, hhea, OS/2
    /// typo, win, the bounding box) and a typographic system picks one -- some
    /// scale the pair they pick. A caller that must match such a system declares
    /// the pair here instead of distorting the leading to compensate, which would
    /// move the pitch as well as the seat.</summary>
    public double? LineBoxAscentEm { get; set; }

    /// <summary>The line-box descent this caller declares, in em, as a POSITIVE
    /// distance below the baseline. Null uses the face's own. See
    /// <see cref="LineBoxAscentEm"/>.</summary>
    public double? LineBoxDescentEm { get; set; }

    /// <summary>Gets or sets the extra space, in points, added above each line of the text; the default is 0.</summary>
    public float LineSpacing
    {
        get => _lineSpacing;
        set { _lineSpacing = value; LineSpacingTouched = true; }
    }
    private float _lineSpacing;

    /// <summary>String token inserted into the rendered text in place of a
    /// tab character. Returns "\t" — the default tab-character placeholder.</summary>
    public string TabTag => "\t";

    private HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Left;

    /// <summary>Horizontal alignment of the text.</summary>
    public HorizontalAlignment HorizontalAlignment
    {
        get => _horizontalAlignment;
        set { _horizontalAlignment = value; HorizontalAlignmentTouched = true; }
    }

    /// <summary>True once <see cref="HorizontalAlignment"/> has been written through
    /// its setter. The alignment defaults to Left, so without this flag a state the
    /// caller never touched is indistinguishable from one they aligned Left on
    /// purpose — and an explicit cell-level Left could not override a row-level
    /// Center. Mirrors <see cref="FontSizeTouched"/>.</summary>
    internal bool HorizontalAlignmentTouched { get; private set; }

    /// <summary>Copy an alignment in without recording it as caller-set, so an
    /// untouched source does not make the target look explicitly aligned.</summary>
    internal void SetHorizontalAlignmentQuiet(HorizontalAlignment value) => _horizontalAlignment = value;

    /// <summary>Text rendering mode (Tr operator). Controls fill / stroke /
    /// clipping behaviour of glyph rendering.</summary>
    public TextRenderingMode RenderingMode { get; set; }

    /// <summary>Stroke line width (the <c>w</c> operator) in effect for the run:
    /// the pen a stroking <see cref="RenderingMode"/> outlines the glyphs with. An
    /// absorbed run reports the width that was set when it was shown; a run written
    /// by the generator carries the pen it was stroked with (a synthesised bold
    /// weight strokes with a size-proportional pen). 1.0 is the PDF default.</summary>
    public double LineWidth { get; set; } = 1.0;

    /// <summary>
    /// Whether this text fragment is invisible: rendering mode 3, or text that a
    /// LATER opaque filled rectangle fully covers (hidden-by-occlusion:
    /// redaction-style covered text reports Invisible while its
    /// RenderingMode stays FillText).
    /// Setting to true sets RenderingMode=Invisible; setting to false sets RenderingMode=FillText.
    /// </summary>
    public bool Invisible
    {
        get => RenderingMode == TextRenderingMode.Invisible || _occluded;
        set => RenderingMode = value ? TextRenderingMode.Invisible : TextRenderingMode.FillText;
    }

    private bool _occluded;

    /// <summary>Absorber-side capture: the run is fully covered by a later opaque
    /// fill rect (drawn over it), so it reads as invisible despite FillText mode.</summary>
    internal void SetCapturedOccluded(bool value) => _occluded = value;

    /// <summary>Text rise (superscript/subscript offset).</summary>
    public double TextRise { get; set; }

    /// <summary>Text rotation angle in degrees.</summary>
    public double Rotation { get; set; }

    /// <summary>
    /// The font used for this text. May be null if font info was not resolved.
    /// Set by TextAbsorber/TextFragmentAbsorber during extraction.
    /// </summary>
    private Font? _font = FontInfo.DefaultHelvetica;
    /// <summary>The band a font assignment drops an overflowing replaced line by, in
    /// em of the fragment size — the same 1.10 em re-flow band the whole-paragraph
    /// re-flow uses (probed: baseline 364.27 → 346.71 at
    /// fs 15.96, an exact 1.10 × 15.96 = 17.556 pt drop).</summary>
    private const double OverflowRelayPitchEm = 1.10;

    /// <summary>Gets or sets the font used for the text; the default is Helvetica. Setting it also sets <c>FontName</c>, and on a fragment found on a page a real font with a program is embedded and the text is rewritten with it.</summary>
    public Font? Font
    {
        get => _font;
        set
        {
            var prevFont = _font;
            _font = value;
            FontTouched = true;
            if (value is null) return;
            // Mirror the assigned font's name into FontName so downstream code that
            // keys on FontName (TextParagraph.RenderAbsolute → ensureFont(fontName))
            // sees the requested font instead of falling back to Helvetica.
            FontName = value.FontName;

            // Reassigning an absorbed fragment's font to a real, embeddable font
            // (one that carries a font program — e.g. FontRepository.FindFont(...))
            // embeds it by default and rewrites the page content so the run is
            // shown with it. Fonts read back from a PDF dictionary during
            // absorption carry no SourceFontData, so they no-op here.
            // SetEmbeddedDefault (not the IsEmbedded setter) respects an explicit
            // caller IsEmbedded=false — the save/layout pipeline re-assigns the font
            // into fresh text states, which would otherwise re-embed it and clobber
            // the caller's choice (font embedded incorrectly became true after save).
            // Standard-14 fonts are referenced by name and are never embedded/subset;
            // any other real font (one carrying a program) embeds and subsets by
            // default. IsCoreName matches only the genuine Core-14 names, so an
            // aliased TrueType such as "Courier New" still embeds.
            var isCore = Standard14Fonts.IsCoreName(value.BaseFont)
                || Standard14Fonts.IsCoreName(value.FontName);
            if (isCore)
            {
                value.SetEmbeddedDefault(false);
                value.SetSubsetDefault(false);
                // A core face carries no program: the cached one belonged to the face this
                // state had before, and a writer that re-emits the run from the state would
                // otherwise keep drawing with it.
                FontData = null;
            }
            else
            {
                if (value.SourceFontData is null) return;
                value.SetEmbeddedDefault(true);
                value.SetSubsetDefault(true);
                // The cached program follows the face: assigning a second font
                // (Times, then Courier New) must not leave the writer measuring and
                // embedding the first one.
                FontData = value.SourceFontData;
            }
            // OwnerSegment is wired for segment-level state; a fragment-level
            // TextFragmentState wires OwnerFragment instead.
            var page = OwnerSegment?.Owner?.SourcePage ?? OwnerFragment?.SourcePage;
            var text = OwnerSegment?.Text ?? OwnerFragment?.Text;
            if (page is null || string.IsNullOrEmpty(text) || OwnerWrittenByBuilder) return;
            // Assigning a face to a run that is ALREADY on a page rewrites that page
            // there and then, so this assignment is the embedding: a face whose licence
            // forbids it is refused here rather than at the save that never happens
            // (a fragment the absorber produced throws on the assignment, a
            // fragment not yet added to a page throws only on save).
            if (value.SourceFontData is { } assigned)
                FontData.RefuseEmbedding(assigned, page.Reader?.OwnerDocument);
            // A fragment-level assignment that leaves the (already replaced) run wider
            // than the sheet re-LAYS the source line instead of merely re-facing it:
            // the line drops one re-flow band and the tail runs re-seat at the match x
            // plus one source-face space, each keeping its own face (probed against the
            // reference on the overflow family — see TextStateModifier.OverflowRelay).
            // Gated on the ABSORBED rectangle having fit the sheet, so a line that
            // always overflowed is never moved.
            TextStateModifier.OverflowRelay? relay = null;
            if (OwnerSegment is null && OwnerFragment is not null
                && OwnerFragment.PositionOrNull is { } fragPos && FontSize > 0
                && page.MediaBox is { URX: > 0 } media
                && OwnerFragment.AbsorbedRectangle is { } srcRect && srcRect.URX <= media.URX)
            {
                double newW = 0;
                try { newW = MeasureString(text!); } catch { }
                if (newW > 0 && fragPos.XIndent + newW > media.URX)
                {
                    double spaceW;
                    try { spaceW = prevFont?.MeasureString(" ", FontSize) ?? 0.25 * FontSize; }
                    catch { spaceW = 0.25 * FontSize; }
                    relay = new TextStateModifier.OverflowRelay(
                        spaceW, OverflowRelayPitchEm * FontSize);
                }
            }
            FaceReassigned = true;
            try
            {
                new TextStateModifier().ModifyFont(page, text!, value,
                    OwnerSegment?.Position?.YIndent ?? OwnerFragment?.PositionOrNull?.YIndent,
                    segmentScoped: OwnerSegment is not null, relay: relay);
            }
            catch { /* best-effort: leave content unchanged if the rewrite fails */ }

            // When the fragment's absorber requested RemoveUnusedFonts, flag the page so
            // the save pipeline drops /Font resources the replacement left unreferenced.
            var frag = OwnerSegment?.Owner ?? OwnerFragment;
            if (frag?.TextEditOptions?.FontReplaceBehavior
                == TextEditOptions.FontReplace.RemoveUnusedFonts)
                page.PruneUnusedFontsOnSave = true;
        }
    }

    /// <summary>
    /// Report a substituted font on this state WITHOUT the embedding/content-rewrite
    /// side effects of the <see cref="Font"/> setter. The glyphs were already switched
    /// in the content stream by the byte-level replacer; this only updates what the
    /// fragment reports (e.g. after a default no-character font fallback).
    /// </summary>
    internal void SetReportedFont(string family)
    {
        Font? f = null;
        try { f = FontRepository.TryFindFont(family); } catch { /* not installed */ }
        if (f is not null) _font = f;
        FontName = f?.FontName ?? family;
    }

    /// <summary>Rough text-width estimate at the current font/size. Uses the
    /// configured <see cref="Font"/>'s glyph widths when available; falls
    /// back to a half-em approximation per character.</summary>
    public double MeasureString(string str)
    {
        if (string.IsNullOrEmpty(str)) return 0;

        // Arabic is cursive: the simple-font metric path measures each base codepoint as a
        // missing glyph (it isn't in a Latin font's WinAnsi range). Shape the run to its
        // contextual presentation forms and measure those against an Arabic-capable face so
        // the width reflects the joined glyphs actually drawn.
        if (ArabicShaper.ContainsArabic(str))
        {
            var arabic = ArabicMeasurer.Measure(str, FontSize);
            if (arabic > 0) return arabic;
        }

        var font = Font;
        if (font is not null)
        {
            try { return font.MeasureString(str, FontSize); }
            catch { /* fall through to estimate */ }
        }
        return str.Length * FontSize * 0.5;
    }

    /// <summary>Height of <paramref name="character"/> at the current font / size, in
    /// points — the glyph's own bounding-box height (yMax − yMin) mapped to text space as
    /// <c>height × FontSize / 1000</c>. Returns 0 when the font carries no glyph for the
    /// character (e.g. a subset that never used it) or its outline is unavailable.</summary>
    public double MeasureHeight(char character)
    {
        if (Font is { } font)
        {
            var units = font.GlyphHeightUnits(character);
            if (units > 0) return units * FontSize / 1000.0;
        }
        return 0;
    }

    /// <summary>
    /// External font data for embedding (set via FontRepository.OpenFont).
    /// When set, TextBuilder will embed this font in the PDF instead of using Standard 14.
    /// </summary>
    public FontData? FontData { get; set; }

    /// <summary>Text formatting options (WordWrapMode, LineSpacingMode, etc.).
    /// Auto-initialized so callers can set
    /// <c>state.FormattingOptions.WrapMode = ...</c> on a fresh instance.</summary>
    public TextFormattingOptions FormattingOptions { get; set; } = new TextFormattingOptions();

    /// <summary>Marked-content wrapping for generator output: when set, the runs
    /// written for this state are enclosed in a <c>/Tag &lt;&lt;/MCID id&gt;&gt; BDC … EMC</c>
    /// block. Consecutive runs carrying the same tag+id merge into ONE block —
    /// two paragraphs tagged ("P", 0) produce a single BDC/EMC pair.</summary>
    internal string? MarkedContentTag { get; set; }

    /// <summary>MCID paired with <see cref="MarkedContentTag"/>.</summary>
    internal int MarkedContentMcid { get; set; }

    /// <summary>
    /// Copy every public formatting property from <c>other</c> into
    /// this state (leaving owner linkage intact).
    /// </summary>
    public void ApplyChangesFrom(TextState textState)
    {
        var other = textState;
        if (other is null) return;
        FontName = other.FontName;
        // Mirror the SOURCE's touched-ness: copying a state must not turn the
        // ctor placeholder size into an "explicit" one.
        if (other.FontSizeTouched) FontSize = other.FontSize;
        else SetFontSizeQuiet(other.FontSize);
        ForegroundColor = other.ForegroundColor;
        BackgroundColor = other.BackgroundColor;
        IsBold = other.IsBold;
        IsItalic = other.IsItalic;
        Underline = other.Underline;
        IsStrikeOut = other.IsStrikeOut;
        IsSuperscript = other.IsSuperscript;
        IsSubscript = other.IsSubscript;
        CharacterSpacing = other.CharacterSpacing;
        WordSpacing = other.WordSpacing;
        HorizontalScaling = other.HorizontalScaling;
        LineSpacing = other.LineSpacing;
        if (other.HorizontalAlignmentTouched) HorizontalAlignment = other.HorizontalAlignment;
        else SetHorizontalAlignmentQuiet(other.HorizontalAlignment);
        RenderingMode = other.RenderingMode;
        LineWidth = other.LineWidth;
        _occluded = other._occluded; // hidden-by-occlusion capture (field: no setter side effects)
        StrokingColor = other.StrokingColor;
        TextRise = other.TextRise;
        Rotation = other.Rotation;
        EmitStandard14Descriptor = other.EmitStandard14Descriptor;
        Std14FaceOverride = other.Std14FaceOverride;
        Std14Widths = other.Std14Widths;
        SourceTmScale = other.SourceTmScale;
        if (other.Font is not null) Font = other.Font;
        if (other.FontData is not null) FontData = other.FontData;
        if (other.FormattingOptions is not null) FormattingOptions = other.FormattingOptions;
    }
}


// Note class moved to top-level Aspose.Pdf namespace (src/Note.cs)
// where reflection-based callers expect to find it.
