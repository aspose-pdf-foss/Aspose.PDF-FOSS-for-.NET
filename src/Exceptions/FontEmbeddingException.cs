namespace Aspose.Pdf;

/// <summary>Raised when a font marked for embedding may not be written into the
/// document — the face's own licence permits printing and preview but not the
/// editable copy a PDF amounts to. Reporting is opt-out: clearing
/// <see cref="Text.IFontOptions.NotifyAboutFontEmbeddingError"/> lets the save finish
/// with the face referenced by name, and leaves the reason readable through
/// <see cref="Text.Font.GetLastFontEmbeddingError"/>.</summary>
public sealed class FontEmbeddingException : PdfException
{
    public FontEmbeddingException() { }

    public FontEmbeddingException(string message) : base(message) { }

    public FontEmbeddingException(string message, System.Exception innerException)
        : base(message, innerException) { }

    public FontEmbeddingException(System.Exception innerException)
        : base(string.Empty, innerException) { }
}
