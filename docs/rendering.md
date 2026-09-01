# Rendering

Render PDF pages to raster images (PNG, JPEG, BMP, GIF, TIFF) or to vector SVG.

## Quick start

`SoftwarePageRenderer` is the built-in pure-managed renderer. When no renderer
is passed to an image-device constructor, the device picks one for the host:
the GDI+-backed `GdiPlusPageRenderer` on Windows, `SoftwarePageRenderer`
everywhere else (setting the environment variable
`ASPOSE_PDF_FORCE_SOFTWARE_RENDERER=1` selects the software renderer on Windows
too). Pass `new SoftwarePageRenderer()` explicitly for output that is identical
on every platform.

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Devices;

using var doc = new Document("input.pdf");

var device = new PngDevice(new Resolution(300));
byte[] pngBytes = device.Process(doc.Pages[1]);  // pages are 1-based
File.WriteAllBytes("page1.png", pngBytes);
```

## Page renderer

`IPageRenderer` is the rendering extension point. Provide your own
implementation (e.g. backed by Skia or PDFium) to plug in an alternative
backend; otherwise the built-in renderer described above is used.

```csharp
public interface IPageRenderer
{
    RgbaBuffer RenderPage(byte[] pdfBytes, int pageNumber, int dpi);
}
```

`RgbaBuffer` carries the pixels as a flat RGBA `Data` array plus `Width` and
`Height`.

The software renderer covers the full graphics model: paths with dash
patterns (each dash element is widened to at least the line width, as the
PDF rasterisation convention requires); clipping; text in every font type
(Type 1, CFF, TrueType, Type 0 / CID, Type 3); images in every standard filter
(Flate, LZW, RunLength, CCITT, JPEG baseline and progressive, JPEG 2000,
JBIG2); stencil masks, soft masks (`/SMask`) and colour-key masks; ICC,
Indexed, Separation, DeviceN, CalRGB and Lab colour spaces; blend modes and
transparency groups; overprint; tiling and shading patterns with all seven
shading types (function-based, axial, radial, free-form and lattice-form
Gouraud, Coons and tensor patches); optional content; and annotation
appearance streams, which rotate with the page's `/Rotate` value.

`ImageDevice.RenderingOptions` tunes the renderer: `ConvertFontsToUnicodeTTF`
applies to both built-in renderers; `DefaultFontName` (the substitute for
fonts whose program cannot be resolved) and `BarcodeOptimization` (vector
fills without anti-aliasing) are consulted by the GDI+ renderer.
`CoordinateType` selects the `CropBox` (default) or `MediaBox` as the rendered
area.

## PNG

```csharp
using Aspose.Pdf.Devices;

using var doc = new Document("input.pdf");

// 150 DPI default, built-in renderer
var device = new PngDevice();
using var stream = File.Create("page1.png");
device.Process(doc.Pages[1], stream);
```

### Custom resolution

```csharp
var device = new PngDevice(new Resolution(300));
using var stream = File.Create("page1_hires.png");
device.Process(doc.Pages[1], stream);
```

### Explicit renderer

```csharp
var renderer = new SoftwarePageRenderer();
var device   = new PngDevice(renderer, new Resolution(300));

using var stream = File.Create("page1.png");
device.Process(doc.Pages[1], stream);
```

### As byte array

```csharp
byte[] pngBytes = device.Process(doc.Pages[1]);
File.WriteAllBytes("page1.png", pngBytes);
```

`Process(page, outputFileName)` writes straight to a path. Every image device
also has `(int width, int height)` and `(PageSize pageSize)` constructors that
pin the output pixel size (see [Resolution](#resolution)).

## JPEG

```csharp
var device = new JpegDevice(new Resolution(150));
using var stream = File.Create("page1.jpg");
device.Process(doc.Pages[1], stream);
```

### With quality

```csharp
var device = new JpegDevice(new Resolution(150), quality: 85);
using var stream = File.Create("page1.jpg");
device.Process(doc.Pages[1], stream);
```

### Quality only (default resolution)

```csharp
var device = new JpegDevice(quality: 90);
byte[] jpeg = device.Process(doc.Pages[1]);
```

`Quality` defaults to 100.

### Custom JPEG encoder

The built-in managed encoder writes baseline JFIF with the device resolution
recorded in the header; on Windows the platform (GDI+) codec is used instead.
Plug in a different encoder (SkiaSharp, ImageSharp, etc.) by registering a
callback, which takes precedence on every platform:

```csharp
JpegDevice.SetEncoder((rgba, width, height, quality) =>
{
    // Return JPEG bytes encoded from the RGBA pixel buffer.
    return MyJpegEncoder.Encode(rgba, width, height, quality);
});

var device = new JpegDevice(new Resolution(150), quality: 90);
byte[] jpeg = device.Process(doc.Pages[1]);

// Clear the custom encoder when done.
JpegDevice.ClearEncoder();
```

## BMP

```csharp
var device = new BmpDevice(new Resolution(150));
using var stream = File.Create("page1.bmp");
device.Process(doc.Pages[1], stream);
```

The BMP writer is managed and produces a 24-bit file on every platform.
`GifDevice` has the same constructors and writes GIF.

## TIFF

### Single page

```csharp
var device = new TiffDevice(new Resolution(300));
using var stream = File.Create("page1.tiff");
device.Process(doc.Pages[1], stream);
```

### Multi-page TIFF

`ProcessRange` produces a single multi-page TIFF; page numbers are 1-based and
inclusive. Pass `endPage = 0` (default) to include every remaining page.

```csharp
var device = new TiffDevice(new Resolution(300));

// All pages from page 1 onwards
byte[] tiff = device.ProcessRange(doc, startPage: 1);

// Pages 2..5
byte[] range = device.ProcessRange(doc, startPage: 2, endPage: 5);

File.WriteAllBytes("document.tiff", tiff);
```

`ProcessRange(doc, startPage, endPage, Stream)` and the `Process(Document, …)`
overloads (whole document, or `fromPage` / `toPage`, to a stream or a file
name) write the same multi-page output directly.

### `TiffSettings`

```csharp
using Aspose.Pdf.Devices;

var settings = new TiffSettings
{
    Compression = CompressionType.LZW,
    Depth       = ColorDepth.Default,
    // Other compression / margin / shape options...
};

var device = new TiffDevice(settings);
```

The TIFF writer is managed. `CompressionType` offers `None`, `LZW` (default),
`CCITT3`, `CCITT4`, `RLE` and `Packbits`; the CCITT fax encodings are bilevel
and therefore imply a 1-bit image regardless of `Depth`. `ColorDepth` offers
`Default` (24-bit), `Format1bpp`, `Format4bpp`, `Format8bpp` and
`Format24bpp`; `Brightness` (0–1, default 0.5) is the threshold for the 1-bit
conversion. `SkipBlankPages` drops empty pages and `CoordinateType` selects
the page box. `Shape` and `Margins` are stored for API compatibility only: each
page keeps its native aspect ratio and crop-box extents.

## SVG (vector output)

SVG export converts PDF vector content directly and does not require an
image renderer:

```csharp
using Aspose.Pdf.Devices;

var device = new SvgDevice();

string svg = device.Process(doc.Pages[1]);
File.WriteAllText("page1.svg", svg);

using var stream = File.Create("page1.svg");
device.Process(doc.Pages[1], stream);
```

`Process(page, outputFileName)` writes to a path.

## Text

`TextDevice` extracts plain text via the device API:

```csharp
using Aspose.Pdf.Devices;

var device = new TextDevice();

string singlePage = device.Process(doc.Pages[1]);
string allText    = device.Process(doc);
```

Constructor overloads take a `TextExtractionOptions` and/or the output
`Encoding` (default UTF-16) used by the stream and file `Process` overloads.

## Rendering every page

```csharp
var device = new PngDevice(new Resolution(150));

for (int i = 1; i <= doc.PageCount; i++)
{
    using var stream = File.Create($"page_{i}.png");
    device.Process(doc.Pages[i], stream);
}
```

## Resolution

`Resolution` controls the output DPI for raster devices:

```csharp
var res1 = new Resolution(300);        // Uniform 300 DPI
var res2 = new Resolution(300, 600);   // 300 x DPI, 600 y DPI
```

The default is 150 DPI. The page is rendered at exactly the requested
resolution — the output is `round(points × dpi / 72)` pixels on each axis
(A4 at 150 DPI = 1240 × 1754) — and the DPI is recorded in the JPEG and TIFF
headers. One guard applies: when the page at the requested DPI would
exceed 40 million pixels, the resolution is halved until it fits.

The `(width, height)` device constructors pin the output size instead: the page
is drawn straight onto that pixel grid when its aspect ratio matches, and
otherwise rendered at the given resolution and resampled bilinearly to fit.
