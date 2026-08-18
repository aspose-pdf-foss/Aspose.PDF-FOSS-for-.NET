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

public sealed class Position
{
    public Position(double xIndent, double yIndent)
    {
        // Assign the backing fields directly so construction does not set Touched —
        // only a later property write counts as the caller "setting" the position.
        _xIndent = xIndent;
        _yIndent = yIndent;
    }

    private double _xIndent;
    private double _yIndent;

    /// <summary>True once <see cref="XIndent"/>/<see cref="YIndent"/> has been
    /// written through a property setter (not via the constructor). Lets the owning
    /// <see cref="TextFragment"/> distinguish a position the caller explicitly set —
    /// e.g. <c>fragment.Position.XIndent = …</c> on a fresh fragment — from one that
    /// was merely auto-created when the (never-null) Position getter was read.</summary>
    internal bool Touched { get; private set; }

    public double XIndent { get => _xIndent; set { _xIndent = value; Touched = true; } }
    public double YIndent { get => _yIndent; set { _yIndent = value; Touched = true; } }

    public override bool Equals(object? obj)
        => obj is Position other
           && Math.Abs(XIndent - other.XIndent) < 0.001
           && Math.Abs(YIndent - other.YIndent) < 0.001;

    public override int GetHashCode()
        => HashCode.Combine(Math.Round(XIndent, 2), Math.Round(YIndent, 2));

    // Format: "( x, y )" with shortest-round-trip doubles ("( 25.92,
    // 661.138439991951 )") — tests log Position values and compare log LENGTHS.
    public override string ToString() => string.Format(
        System.Globalization.CultureInfo.InvariantCulture, "( {0}, {1} )", XIndent, YIndent);
}

/// <summary>
/// Text formatting state.
/// </summary>
public class TextState
{
    /// <summary>Default tab-stop width in PDF points (56 pt ≈ 0.78 in,
    /// matches Adobe's default tab spacing). Declared as an instance
    /// field so reflection-based callers see a non-static field.</summary>
    public float TabstopDefaultValue = 56f;

    public TextState() { }

    public TextState(double fontSize) { FontSize = (float)fontSize; }

    public TextState(string fontFamily) { FontName = fontFamily; }

    public TextState(string fontFamily, double fontSize)
    {
        FontName = fontFamily;
        FontSize = (float)fontSize;
    }

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

    public TextState(System.Drawing.Color foregroundColor)
    {
        ForegroundColor = Color.FromRgb(foregroundColor);
    }

    public TextState(System.Drawing.Color foregroundColor, double fontSize)
    {
        ForegroundColor = Color.FromRgb(foregroundColor);
        FontSize = (float)fontSize;
    }

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

    /// <summary>True when LineSpacing was assigned by an internal layout path
    /// (e.g. the HTML block renderer's 1.2× pitch) rather than the caller. A
    /// CALLER-set LineSpacing adds its leading above the FIRST line too (a
    /// 10 pt fragment with LineSpacing 13 starts one 23 pt pitch below the
    /// cursor); synthetic leading keeps the legacy
    /// first-line drop of one font size.</summary>
    internal bool LineSpacingSynthetic { get; set; }

    /// <summary>Raw font size from the Tf operator (before text matrix scaling).</summary>
    internal float RawFontSize { get; set; }

    /// <summary>Text matrix D component (vertical scale) for height computation.</summary>
    internal double TmD { get; set; } = 1.0;

    /// <summary>Owner segment — needed to walk back to the source page for content stream updates.</summary>
    internal TextSegment? OwnerSegment { get; set; }

    /// <summary>Owner fragment — for fragment-level TextState, allows registration for save-time effects.</summary>
    internal TextFragment? OwnerFragment { get; set; }

    private void ApplyFontSizeChange(double oldSize, double newSize)
    {
        // Segment-level state reaches its page via the owning fragment; a
        // fragment-level state (TextFragmentState) only has OwnerFragment —
        // fall back to it so absorbed fragments write the new size through
        // to the page content stream too.
        var page = OwnerSegment?.Owner?.SourcePage ?? OwnerFragment?.SourcePage;
        if (page is null) return;
        var text = OwnerSegment?.Text ?? OwnerFragment?.Text;
        if (string.IsNullOrEmpty(text)) return;
        var modifier = new TextStateModifier();
        // Segment-level resize keeps the historical semantics (patch the
        // covering Tf even when it also governs neighbouring shows — a
        // sub-run resize resizes its whole run). Fragment-level resize is
        // collateral-free: it only rewrites when the covering Tf runs are
        // wholly inside the fragment's text, else it leaves the stream alone.
        modifier.ModifyFontSize(page, text, oldSize, newSize,
            allowCollateral: OwnerSegment is not null);
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
    public Color? ForegroundColor
    {
        get => _foregroundColor;
        set
        {
            _foregroundColor = value;
            if (value is null) return;
            // Mirror the FontSize/BackgroundColor side-effects: when this
            // TextState belongs to a segment from a page, propagate the new
            // fill colour to the content stream by injecting an `R G B rg`
            // before the segment's Tj/TJ operator. Pass the segment's X/Y so
            // the same text at multiple positions doesn't all get coloured by
            // a single setter call (an X+Y-scoped pass runs first; a Y-only
            // pass keeps the historical reach when the X anchor finds nothing).
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
                // X anchor missed (segment starting mid-run shifts the operator
                // origin): fall back to the historical Y-only scope.
                if (x.HasValue && !modifier.LastForegroundColorApplied)
                    modifier.ModifyForegroundColor(pg, segText, c, y);
            }
        }
    }

    /// <summary>Assigns the captured foreground color from absorber graphics-state
    /// tracking without triggering content-stream injection. Used by
    /// TextFragmentAbsorber when reading existing text colour during extraction.</summary>
    internal void SetCapturedForegroundColor(Color? color) => _foregroundColor = color;

    /// <summary>Stroking (outline) color of the text. Used together with
    /// a non-zero <see cref="RenderingMode"/> (1 = stroke, 2 = fill+stroke).</summary>
    public Color? StrokingColor { get; set; }

    /// <summary>Whether text positioning treats Y as the baseline or the descender.
    /// Default is <see cref="CoordinateOrigin.Descender"/>.</summary>
    public CoordinateOrigin CoordinateOrigin { get; set; } = CoordinateOrigin.Descender;

    private Color? _backgroundColor;
    public Color? BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            _backgroundColor = value;
            // When BackgroundColor is set on a segment obtained via TextFragmentAbsorber,
            // register the owning fragment for rectangle injection during save.
            if (value is not null)
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
            IsBold = (value & FontStyles.Bold) != 0;
            IsItalic = (value & FontStyles.Italic) != 0;
        }
    }

    /// <summary>Whether the text is underlined (alias for <see cref="IsUnderline"/>).</summary>
    public bool Underline
    {
        get => _isUnderline;
        set
        {
            _isUnderline = value;
            _underlineRequested = value;
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
        set => IsStrikeOut = value;
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
    public float CharacterSpacing { get; set; }

    /// <summary>Word spacing in text space units.</summary>
    public float WordSpacing { get; set; }

    /// <summary>Horizontal scaling percentage (default 100).</summary>
    public float HorizontalScaling { get; set; } = 100;

    /// <summary>Line spacing (leading) in text space units.</summary>
    public float LineSpacing { get; set; }

    /// <summary>String token inserted into the rendered text in place of a
    /// tab character. Returns "\t" — the default tab-character placeholder.</summary>
    public string TabTag => "\t";

    /// <summary>Horizontal alignment of the text.</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>Text rendering mode (Tr operator). Controls fill / stroke /
    /// clipping behaviour of glyph rendering.</summary>
    public TextRenderingMode RenderingMode { get; set; }

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
    public Font? Font
    {
        get => _font;
        set
        {
            _font = value;
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
            }
            else
            {
                if (value.SourceFontData is null) return;
                value.SetEmbeddedDefault(true);
                value.SetSubsetDefault(true);
            }
            // OwnerSegment is wired for segment-level state; a fragment-level
            // TextFragmentState wires OwnerFragment instead.
            var page = OwnerSegment?.Owner?.SourcePage ?? OwnerFragment?.SourcePage;
            var text = OwnerSegment?.Text ?? OwnerFragment?.Text;
            if (page is null || string.IsNullOrEmpty(text)) return;
            try
            {
                new TextStateModifier().ModifyFont(page, text!, value,
                    OwnerSegment?.Position?.YIndent ?? OwnerFragment?.PositionOrNull?.YIndent,
                    segmentScoped: OwnerSegment is not null);
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
        try { f = FontRepository.FindFont(family); } catch { /* not installed */ }
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
    /// Copy every public formatting property from <paramref name="other"/> into
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
        HorizontalAlignment = other.HorizontalAlignment;
        RenderingMode = other.RenderingMode;
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

/// <summary>
/// A 1-indexed collection of <see cref="TextFragment"/> objects, matching the public API.
/// </summary>
public sealed class TextFragmentCollection : System.Collections.Generic.IEnumerable<TextFragment>
{
    private readonly System.Collections.Generic.List<TextFragment> _list = new();

    /// <summary>Number of fragments.</summary>
    public int Count => _list.Count;

    /// <summary>1-based indexer (index 1 returns the first fragment).</summary>
    public TextFragment this[int index]
    {
        get
        {
            if (index < 1 || index > _list.Count)
                throw new IndexOutOfRangeException($"Index {index} out of range [1, {_list.Count}].");
            return _list[index - 1];
        }
    }

    /// <summary>Backing list — internal so the absorber can reorder a
    /// just-added range into reading order.</summary>
    internal System.Collections.Generic.List<TextFragment> Inner => _list;

    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    public void Add(TextFragment fragment)
    {
        if (fragment is null) throw new ArgumentNullException(nameof(fragment));
        _list.Add(fragment);
    }

    /// <summary>Append every fragment from <paramref name="fragments"/> to this collection.</summary>
    public void AddRange(System.Collections.Generic.IEnumerable<TextFragment> fragments)
    {
        if (fragments is null) throw new ArgumentNullException(nameof(fragments));
        foreach (var fragment in fragments) Add(fragment);
    }

    public bool Contains(TextFragment item) => _list.Contains(item);

    public void CopyTo(TextFragment[] array, int index) => _list.CopyTo(array, index);

    public bool Remove(TextFragment item)
    {
        if (item is null) return false;
        bool removed = _list.Remove(item);
        // Deleting a fragment from an absorber's result collection deletes the
        // corresponding text from the page content stream, so the next save no
        // longer emits these glyphs.
        if (removed)
            item.DeleteFromContent();
        return removed;
    }

    /// <summary>Clear all fragments from the collection.</summary>
    public void Clear() => _list.Clear();

    /// <summary>Remove the element at the given 0-based internal index.</summary>
    internal void RemoveAt(int zeroBasedIndex) => _list.RemoveAt(zeroBasedIndex);

    /// <summary>0-based internal access for use within the library.</summary>
    internal TextFragment GetInternal(int zeroBasedIndex) => _list[zeroBasedIndex];

    public System.Collections.Generic.IEnumerator<TextFragment> GetEnumerator() => _list.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _list.GetEnumerator();
}

// Note class moved to top-level Aspose.Pdf namespace (src/Note.cs)
// where reflection-based callers expect to find it.
