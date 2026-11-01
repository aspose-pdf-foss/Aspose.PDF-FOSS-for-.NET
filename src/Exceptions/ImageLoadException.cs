namespace Aspose.Pdf;

/// <summary>Thrown when an image's bytes are no image the library can read: no decoder recognises them.</summary>
public class ImageLoadException : PdfException
{
    /// <summary>Initializes a new instance of the <see cref="ImageLoadException"/> class.</summary>
    public ImageLoadException() : base("Image loading failed.") { }

    /// <summary>Initializes a new instance of the <see cref="ImageLoadException"/> class with a message.</summary>
    public ImageLoadException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="ImageLoadException"/> class with a message and inner exception.</summary>
    public ImageLoadException(string message, Exception innerException) : base(message, innerException) { }
}
