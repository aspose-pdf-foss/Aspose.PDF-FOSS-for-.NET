# Rendering

Render PDF pages to raster images (PNG, JPEG, BMP, TIFF) or to vector SVG.

## Quick start

`SoftwarePageRenderer` is the built-in pure-managed renderer. It is used
automatically when no renderer is passed to an image-device constructor.

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
backend; otherwise `SoftwarePageRenderer` is used.

```csharp
public interface IPageRenderer
{
    RgbaBuffer RenderPage(byte[] pdfBytes, int pageNumber, int dpi);
}
```

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

### Custom JPEG encoder

The built-in encoder writes baseline JPEG. Plug in a higher-fidelity encoder
(SkiaSharp, ImageSharp, etc.) by registering a callback:

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

## Text

`TextDevice` extracts plain text via the device API:

```csharp
using Aspose.Pdf.Devices;

var device = new TextDevice();

string singlePage = device.Process(doc.Pages[1]);
string allText    = device.Process(doc);
```

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
