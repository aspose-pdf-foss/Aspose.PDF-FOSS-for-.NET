namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>The text state of the content render as q saves it and Q restores it: the font
    /// set by Tf with what the render derived from it, the spacings, leading, rise and rendering
    /// mode. All of it belongs to the graphics state, so a block drawn after a Q draws in the font
    /// set before the matching q, not in the last font some inner block set.</summary>
    private readonly struct SavedTextState
    {
        private readonly string? _fontKey;
        private readonly double _fontSize;
        private readonly string _fontFamily;
        private readonly string _fontCssFamily;
        private readonly string _fontWeight;
        private readonly string _fontStyle;
        private readonly double _fontAscent;
        private readonly double _fontLineHeight;
        private readonly bool _fontIsType3;
        private readonly double _charSpacing;
        private readonly double _horizScale;
        private readonly double _wordSpacing;
        private readonly double _leading;
        private readonly bool _hasLeading;
        private readonly double _rise;
        private readonly int _textRenderMode;

        private SavedTextState(ContentRenderState ct)
        {
            _fontKey = ct.currentFontKey;
            _fontSize = ct.fontSize;
            _fontFamily = ct.fontFamily;
            _fontCssFamily = ct.fontCssFamily;
            _fontWeight = ct.fontWeight;
            _fontStyle = ct.fontStyle;
            _fontAscent = ct.fontAscent;
            _fontLineHeight = ct.fontLineHeight;
            _fontIsType3 = ct.fontIsType3;
            _charSpacing = ct.charSpacing;
            _horizScale = ct.horizScale;
            _wordSpacing = ct.wordSpacing;
            _leading = ct.leading;
            _hasLeading = ct.hasLeading;
            _rise = ct.rise;
            _textRenderMode = ct.textRenderMode;
        }

        public static SavedTextState Of(ContentRenderState ct) => new(ct);

        public void RestoreTo(ContentRenderState ct)
        {
            ct.currentFontKey = _fontKey;
            ct.fontSize = _fontSize;
            ct.fontFamily = _fontFamily;
            ct.fontCssFamily = _fontCssFamily;
            ct.fontWeight = _fontWeight;
            ct.fontStyle = _fontStyle;
            ct.fontAscent = _fontAscent;
            ct.fontLineHeight = _fontLineHeight;
            ct.fontIsType3 = _fontIsType3;
            ct.charSpacing = _charSpacing;
            ct.horizScale = _horizScale;
            ct.wordSpacing = _wordSpacing;
            ct.leading = _leading;
            ct.hasLeading = _hasLeading;
            ct.rise = _rise;
            ct.textRenderMode = _textRenderMode;
        }
    }
}
