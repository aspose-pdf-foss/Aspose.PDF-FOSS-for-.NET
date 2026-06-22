namespace Aspose.Pdf.Devices;

/// <summary>
/// Raw RGBA pixel buffer returned by a <see cref="IPageRenderer"/>.
/// </summary>
public sealed class RgbaBuffer
{
    /// <summary>
    /// RGBA pixel data (4 bytes per pixel, row-major, top row first).
    /// Length must equal <c>Width * Height * 4</c>.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; }

    public RgbaBuffer(byte[] data, int width, int height)
    {
        Data = data;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// Renders a PDF page to an RGBA pixel buffer.
/// Implementations may use any rendering backend (SkiaSharp, System.Drawing, etc.).
/// </summary>
public interface IPageRenderer
{
    /// <summary>
    /// Render a page to RGBA pixels.
    /// </summary>
    /// <param name="pdfBytes">Complete PDF file bytes.</param>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="dpi">Target resolution in DPI.</param>
    /// <returns>RGBA pixel buffer.</returns>
    RgbaBuffer RenderPage(byte[] pdfBytes, int pageNumber, int dpi);
}

/// <summary>
/// A no-op renderer used when TiffDevice is constructed without a renderer.
/// Throws <see cref="InvalidOperationException"/> if rendering is actually attempted.
/// </summary>
internal sealed class NullRenderer : IPageRenderer
{
    public static readonly NullRenderer Instance = new();

    public RgbaBuffer RenderPage(byte[] pdfBytes, int pageNumber, int dpi)
        => throw new InvalidOperationException(
            "This TiffDevice was constructed without an IPageRenderer. " +
            "Provide a renderer to render pages.");
}
