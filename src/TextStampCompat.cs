#nullable disable

namespace Aspose.Pdf
{
    /// <summary>Compat re-export so external tests that reference
    /// <c>Aspose.Pdf.TextStamp</c> (from before the type moved to
    /// <c>Aspose.Pdf.Stamps</c>) keep compiling. Most behaviour comes
    /// from the real implementation in <see cref="Stamps.TextStamp"/>;
    /// Aspose.Pdf-shape additions are declared here so they surface in
    /// the reflection dump.</summary>
    public class TextStamp : Stamps.TextStamp
    {
        /// <summary>Action taken when the configured font does not have a glyph for a character in the stamp text.</summary>
        public enum NoCharacterAction
        {
            /// <summary>Use the configured font even when it lacks the glyph (renders a tofu box).</summary>
            UseStandardFont = 0,
            /// <summary>Substitute the missing glyph from the <see cref="ReplacementFont"/>.</summary>
            UseCustomReplacementFont = 1,
            /// <summary>Render the glyph in whatever font has it; no fallback.</summary>
            ReplaceAnyway = 2,
            /// <summary>Throw an exception when a glyph is missing.</summary>
            ThrowException = 3,
        }

        public TextStamp(string value) : base(value) { }

        /// <summary>Construct a text stamp from a FormattedText template — copies its text, font, size and colour.</summary>
        /// <remarks>Delegates to the base <see cref="Stamps.TextStamp(Aspose.Pdf.Facades.FormattedText)"/>
        /// ctor so every line added via <c>AddNewLineText</c> is preserved (joined with '\n');
        /// the earlier <c>base(formattedText.Text)</c> kept only the first line.</remarks>
        public TextStamp(Aspose.Pdf.Facades.FormattedText formattedText) : base(formattedText)
        {
            if (formattedText is null) return;
            if (!string.IsNullOrEmpty(formattedText.FontName)) FontName = formattedText.FontName;
            if (formattedText.ForegroundColor is not null) Color = formattedText.ForegroundColor;
        }

        /// <summary>Construct a text stamp from a value and a precomputed TextState.</summary>
        public TextStamp(string value, Aspose.Pdf.Text.TextState textState) : base(value)
        {
            if (textState is not null) base.TextState = textState;
        }

        /// <summary>Add this stamp to a page. Alias for <see cref="Page.AddStamp"/>.</summary>
        public void Put(Page page)
        {
            page?.AddStamp(this);
        }

        // ── Redeclared inherited properties so the reflection dump surfaces them ──

        public new double Height { get => base.Height; set => base.Height = value; }
        public new bool Scale { get => base.Scale; set => base.Scale = value; }
        public new Aspose.Pdf.HorizontalAlignment TextAlignment { get => base.TextAlignment; set => base.TextAlignment = value; }
        public new string Value { get => base.Value; set => base.Value = value; }
        public new double Width { get => base.Width; set => base.Width = value; }
        public new bool WordWrap { get => base.WordWrap; set => base.WordWrap = value; }
        public new Aspose.Pdf.Text.TextFormattingOptions.WordWrapMode WordWrapMode { get => base.WordWrapMode; set => base.WordWrapMode = value; }
        /// <summary>Text-state snapshot. Get-only on the derived type per the Aspose.Pdf public API.</summary>
        public new Aspose.Pdf.Text.TextState TextState => base.TextState;

        /// <summary>Font size in points. Get-only on the derived type per the Aspose.Pdf public API
        /// (the inherited setter remains accessible internally for the renderer + facades).</summary>
        public new float FontSize { get => base.FontSize; internal set => base.FontSize = value; }

        // ── Aspose.Pdf-shape additions ───────────────────────────

        /// <summary>When auto-adjusting font size to fit the stamp rectangle, the precision (in points). Stored only.</summary>
        public float AutoAdjustFontSizePrecision { get; set; } = 0.1f;

        /// <summary>When true, the renderer shrinks the font size until the text fits the stamp's Width/Height.</summary>
        public bool AutoAdjustFontSizeToFitStampRectangle { get; set; }

        /// <summary>Drive the base auto-fit off the Aspose.Pdf-shape properties.</summary>
        protected override bool AutoFitToBox => AutoAdjustFontSizeToFitStampRectangle;

        /// <summary>Bisection stop interval for the auto-fit search.</summary>
        protected override double AutoFitPrecision => AutoAdjustFontSizePrecision > 0 ? AutoAdjustFontSizePrecision : 0.1;

        /// <summary>When false, the stamp records intent but skips drawing. Stored only.</summary>
        public bool Draw { get; set; } = true;

        /// <summary>When true, the stamp's text is full-justified within the stamp width. Stored only.</summary>
        public bool Justify { get; set; }

        /// <summary>Maximum row width before wrapping. 0 means use the stamp <see cref="Width"/>.</summary>
        public double MaxRowWidth { get; set; }

        /// <summary>Prefer <see cref="MaxRowWidth"/> as the wrap width, falling back to <see cref="Width"/>.</summary>
        protected override double WrapWidth => MaxRowWidth > 0 ? MaxRowWidth : Width;

        /// <summary>Strategy used when a character has no glyph in the configured font.</summary>
        public NoCharacterAction NoCharacterBehavior { get; set; } = NoCharacterAction.UseStandardFont;

        /// <summary>Fallback font used when the main font lacks a required glyph; consulted only when <see cref="NoCharacterBehavior"/> is <see cref="NoCharacterAction.UseReplacementFont"/>.</summary>
        public Aspose.Pdf.Text.Font ReplacementFont { get; set; }

        /// <summary>Expose the configured <see cref="ReplacementFont"/>'s embedded TrueType
        /// program to the base stamp builder so it can fall back to a Type0 font for glyphs
        /// the primary font lacks. Null when no usable replacement program is available.</summary>
        protected override (byte[] ttf, string name)? ReplacementFontProgram =>
            ReplacementFont?.SourceFontData?.TtfData is { } ttf
                ? (ttf, ReplacementFont.FontName)
                : null;

        /// <summary>When true, the stamp's Y-indent is treated as the text baseline rather than the bounding-box top. Stored only.</summary>
        public bool TreatYIndentAsBaseLine { get; set; }
    }
}
