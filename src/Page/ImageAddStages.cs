using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>The stages of the image add: the general decode into a stamp.</summary>
    private void DecodeImageStamp(ImageAddState ia)
    {
        // Assume raw RGB pixel data — caller must ensure width/height are correct
        var w = (int)ia.rect.Width;
        var h = (int)ia.rect.Height;
        if (ia.imageData.Length == w * h * 3)
        {
            ia.stamp = ImageStamp.FromRgb(ia.imageData, w, h);
        }
        else if (((Compat.IsWindows() ? ImageStamp.TryFromGdiPlusDecoder(ia.imageData) : null)
                 ?? ImageStamp.TryFromManagedDecoder(ia.imageData)) is { } gdiStamp)
        {
            // GIF / TIFF / EMF / WMF / ICO and other GDI+-supported formats:
            // decode to raw RGB via System.Drawing where it exists, otherwise
            // through the library's own BMP/GIF/TIFF decoders. The dimensions are taken
            // from the image header, not the rect — the caller-supplied
            // rect controls the on-page display size below.
            ia.stamp = gdiStamp;
        }
        else
        {
            // EMF/WMF are out of scope off Windows; say that rather than letting the
            // PNG reader report the bytes as corrupt.
            ImageStamp.ThrowIfWindowsOnlyMetafile(ia.imageData);
            // Last resort: try treating as PNG anyway (some files lack proper header)
            try { ia.stamp = ImageStamp.FromPngData(ia.imageData); }
            catch { throw new ArgumentException(
                "Unsupported image format. Supported: JPEG, PNG, BMP, GIF, TIFF, or raw RGB data."); }
        }
    }
}
