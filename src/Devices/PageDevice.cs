namespace Aspose.Pdf.Devices;

/// <summary>Abstract base for single-page rendering devices. Concrete
/// subclasses override <see cref="Process(Page, System.IO.Stream)"/> to
/// emit the page as image / text / etc.</summary>
public abstract class PageDevice
{
    /// <summary>Render <paramref name="page"/> to <paramref name="output"/>.
    /// Required override.</summary>
    public abstract void Process(Page page, Stream output);

    /// <summary>File-stream wrapper.</summary>
    public virtual void Process(Page page, string outputFileName)
    {
        using var fs = File.Create(outputFileName);
        Process(page, fs);
    }
}

/// <summary>Default <see cref="PageDevice"/> that delegates to any
/// <see cref="ImageDevice"/> — PngDevice / JpegDevice / BmpDevice /
/// TiffDevice. Lets callers drive the SendTo pipeline without subclassing
/// PageDevice manually.</summary>
public sealed class ImagePageDevice : PageDevice
{
    private readonly ImageDevice _imageDevice;

    public ImagePageDevice(ImageDevice imageDevice)
    {
        _imageDevice = imageDevice ?? throw new System.ArgumentNullException(nameof(imageDevice));
    }

    public override void Process(Page page, Stream output) => _imageDevice.Process(page, output);
}
