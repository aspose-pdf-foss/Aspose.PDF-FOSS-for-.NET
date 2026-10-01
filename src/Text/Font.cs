using System.Collections;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// Type alias for FontInfo, matching the Font class name.
/// </summary>
public class Font : FontInfo
{
    internal Font(string resourceName, PdfDictionary fontDict, PdfReader reader)
        : base(resourceName, fontDict, reader) { }

    internal Font(string baseFont, string subtype) : base(baseFont, subtype) { }

    public new string BaseFont => base.BaseFont;
    public new string FontName => base.FontName;
    public new string DecodedFontName => base.DecodedFontName;
    public new bool IsEmbedded { get => base.IsEmbedded; set => base.IsEmbedded = value; }
    public new bool IsSubset { get => base.IsSubset; set => base.IsSubset = value; }
    public new bool IsAccessible => base.IsAccessible;

    public IFontOptions FontOptions => _fontOptions ??= new FontOptionsImpl(this);
    private IFontOptions? _fontOptions;

    /// <summary>Lower-level PDF-font view of this Font. The public API exposes
    /// the engine's IPdfFont through here; FOSS returns a thin wrapper
    /// that surfaces just <c>BaseFontNameOnly</c>.</summary>
    public PdfFontView iPdfFont => new PdfFontView(this);

    /// <summary>Last error encountered embedding this font in a PDF; empty when none.</summary>
    public string GetLastFontEmbeddingError() =>
        _lastEmbeddingError ?? SourceFontData?.LastEmbeddingError ?? string.Empty;
    private string? _lastEmbeddingError;

    /// <summary>Measure the rendered width of a string at the given size, in points.</summary>
    public double MeasureString(string str, float fontSize) =>
        MeasureString(str, (double)fontSize);

    /// <summary>Write the raw font file data to a stream: data loaded via
    /// FontRepository.OpenFont, the program embedded in the source PDF (an absorbed
    /// font), or the installed system face resolved by name — in that order.</summary>
    public void Save(System.IO.Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var data = SourceFontData?.TtfData ?? GetEmbeddedProgramBytes();
        if (data is null || data.Length == 0)
        {
            try { data = FontRepository.GetTtfData(FontName); }
            catch { data = null; }
        }
        if (data is null || data.Length == 0)
        {
            _lastEmbeddingError = "No embeddable font data is available for this Font.";
            return;
        }
        stream.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Implicit conversion from FontData (returned by FontRepository.FindFont)
    /// to Font, so tests can write <c>TextState.Font = FontRepository.FindFont("Arial")</c>
    /// without an explicit cast.
    /// </summary>
    public static implicit operator Font?(FontData? fontData)
    {
        if (fontData is null) return null;
        var font = new Font(fontData.FontName,
            fontData.Type == FontType.TrueType ? "TrueType" : "Type1");
        font.SourceFontData = fontData;
        return font;
    }

    /// <summary>The options ride on the face program (<c>SourceFontData</c>) when
    /// there is one, so the embedding writer — which is handed the program, not this
    /// wrapper — sees the caller's choice. A Font with no program of its own keeps the
    /// value locally.</summary>
    private sealed class FontOptionsImpl : IFontOptions
    {
        private readonly Font _owner;
        private bool _notify = true;
        public FontOptionsImpl(Font owner) { _owner = owner; }

        public bool NotifyAboutFontEmbeddingError
        {
            get => _owner.SourceFontData?.NotifyAboutEmbeddingError ?? _notify;
            set
            {
                _notify = value;
                if (_owner.SourceFontData is { } data) data.NotifyAboutEmbeddingError = value;
            }
        }
    }
}
