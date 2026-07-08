# Aspose.PDF FOSS for .NET

A free, open-source PDF library for .NET 8+ — read, create, modify, and convert PDF documents.

> **Note:** Some features are incomplete and the public API may change between releases — see [What's not included](#whats-not-included-vs-asposepdf-for-net) for current gaps.

> **Platform note:** Runs on .NET 8+ across Windows, Linux, and macOS. Core document work — parsing, editing, text, forms, encryption/signing — and page→image rendering (via the built-in managed `SoftwarePageRenderer`, used automatically off-Windows) are pure-managed and fully cross-platform. A few image-interop and font-measurement members, plus the optional GDI+ renderer, still depend on `System.Drawing.Common` and throw `PlatformNotSupportedException` on Linux/macOS. Removing the remaining Windows dependency is planned for a later release.

It provides an **Aspose.PDF-compatible** API surface for common PDF scenarios — a large part of existing [**Aspose.PDF for .NET**](https://products.aspose.com/pdf/net/) code can compile and run unchanged. See [What's included](#whats-included) and [What's not included](#whats-not-included-vs-asposepdf-for-net) below for the per-feature breakdown.

## Requirements

- .NET 8.0 or later — runs on Windows, macOS, and Linux.
- One NuGet dependency: **`System.Drawing.Common`**, used only by a few image-interop members
  (e.g. `ImageDevice.GetBitmap`, `StampInfo.Image`). It's Windows-only on .NET 8, so those specific
  members throw `PlatformNotSupportedException` on Linux/macOS; everything else — text, forms,
  parsing, encryption & signing, and page→image rendering via `PngDevice`/`JpegDevice` — is
  pure-managed and fully cross-platform.

## Installation

Build from source:

```bash
git clone https://github.com/aspose-pdf-foss/Aspose.PDF-FOSS-for-.NET.git
cd Aspose.PDF-FOSS-for-.NET
dotnet build src/Aspose.Pdf.Foss.csproj -c Release
```

Then reference `src/Aspose.Pdf.Foss.csproj` from your project (e.g. `dotnet add reference`),
or add the built `Aspose.Pdf.Foss.dll` as an assembly reference.

## What's included

### Document core
- Open and create PDFs; full round-trip save and incremental save
- Page manipulation: add, delete, reorder, rotate, resize, copy across documents
- Bookmarks / outlines, page labels, named destinations, document info & metadata (XMP)
- Read & write document permissions, optional content (OCG) layers
- Standard 14 font handling, TrueType / OpenType embedding & subsetting, Type 1 (PostScript) and Type 3 font parsing
- Pure-managed crypto — no `System.Security.Cryptography` runtime dependency

### Text
- Extraction via `TextAbsorber`, `TextFragmentAbsorber`, `ParagraphAbsorber`, `TableAbsorber`
- Find / replace across pages, including cross-operator and ligature-aware spans
- Build new text: `TextBuilder`, `TextFragment`, `TextSegment`, `TextStamp`, `FormattedText`
- Bidi (RTL) support; AGL Unicode mapping; CMap-based decoding

### Forms (AcroForm)
- Read, fill, and build form fields: text, checkbox, radio, choice (combo/list), button, signature, rich text
- Field-level flatten, JSON import / export, XFDF tags, form-data import / export
- `Form`, `Field`, `FormFieldBuilder`, `FormEditor`, `FormDataConverter`

### Annotations
- All standard annotation types: link, text, free-text, highlight, square / circle / line / polyline,
  ink, stamp, popup, file attachment, screen, redaction, watermark, pre-press marks, rich media
- Read, create, modify; appearance generation; flatten

### Security
- Encrypt / decrypt with RC4-40, RC4-128, AES-128, AES-256
- Read & set document permissions
- Sign with PKCS#7 / CMS detached signatures; verify signatures with embedded certificates
- DocMDP / certifying signatures; appearance stream + image preview embedding

### Rendering
- Page-to-image via `PngDevice`, `JpegDevice`, `BmpDevice`, `TiffDevice`, `SvgDevice`, `TextDevice`
- Pluggable `IPageRenderer` (default: `SoftwarePageRenderer`)
- Mesh shading types 4 – 7 (free-form / lattice Gouraud + Coons / tensor-product patch)
- JBIG2 decoder, JPEG / JPEG2000 / CCITT-Fax filters
- Resolution control, color depth, transparency, soft masks (incl. /TR transfer function)

### Converters
- **In:** PDF, HTML, XML, SVG, Markdown
- **Out:** PDF, PDF/A (1A, 1B, 2A, 2B, 2U, 3A, 3B, 3U, 4, 4E, 4F), HTML, Markdown, SVG, plain text, image formats above
- `HtmlSaveOptions`, `MdLoadOptions`, `SvgLoadOptions`, `HtmlLoadOptions`

### Facades
- `PdfFileEditor` — concatenate, split, resize, extract pages, edit content
- `PdfFileSecurity` — encrypt / decrypt / permission management
- `PdfFileSignature` — sign, verify, extract signature info, signature appearance
- `PdfBookmarkEditor` — outline / bookmark CRUD, XML / HTML export
- `PdfContentEditor` — add annotations, stamps, attachments, replace text
- `PdfFileStamp` — header / footer / page-number stamping
- `PdfFileInfo`, `PdfFileMend`, `PdfAnnotationEditor`, `PdfPageEditor`, `PdfConverter`, `PdfExtractor`, `PdfViewer`, `FormEditor`, `FormDataConverter`

### Tagged PDF & accessibility
- Read existing `/StructTreeRoot` trees; walk `Aspose.Pdf.LogicalStructure` element hierarchy
- Build tagged content via `ITaggedContent`: 37 typed structure elements (Document, Sect, Part, P, Span, H1–H6, L / LI / Lbl / LBody, Table / THead / TBody / TFoot / TR / TH / TD, Figure, Form, Annot, etc.)
- Set document language, title, alternate text, expansion text, marked content

### Other
- Tables (`Table`, `Row`, `Cell`, `TableAbsorber`)
- Drawing primitives (`Graph`, `Rectangle`, `Circle`, `Ellipse`, `Arc`, `Line`, `Curve`)
- XMP metadata + XmpPdfAExtension schema editing
- Image extraction & placement (`ImagePlacementAbsorber`, `XImageCollection`)
- Stamps (text / image / page-number / watermark / PDF-page)

## What's not included (vs. Aspose.PDF for .NET)

The following Aspose.PDF for .NET features are **not** in the FOSS edition. Code that depends on them will throw `NotImplementedException` / `PlatformNotSupportedException`, or — for some types — exist as a reflection-shape stub that doesn't perform the operation:

### Out of scope by design
- **AI / LowCode workflows** — `Aspose.Pdf.AI.*`, `Aspose.Pdf.LowCode.*`. Use Aspose.PDF for .NET for OpenAI / Llama-powered summarisation, OCR-aided extraction, and the LowCode façade APIs.
- **Multithreading APIs** — `Aspose.Pdf.Multithreading.*`. The FOSS edition is single-threaded for a single `Document` instance.
- **Format converters beyond PDF / HTML / SVG / Markdown / XML** — DOCX, EPUB, MHT, XPS, PCL, LaTeX, DJVU, OFD, PostScript, Cgm.
- **Printing pipeline** — `PdfViewer.Print*` methods throw `PlatformNotSupportedException`. Render to image and pass the image to your printing stack instead.

### Partial — basics work, advanced doesn't
- **Digital signatures** — basic Sign / Verify / PKCS#1 / PKCS#7 / DocMDP / certifying works. The validation-options surface (`ValidationOptions`, `ValidationResult`, OCSP, timestamping over the network, custom remote-sign delegates) is stored-but-not-active.
- **Advanced `PdfFileEditor` features** — `MakeNUp` imposition, `MakeBooklet` from `Stream` with non-trivial margins, `ResizeContents` with custom imposition matrices — accepted as input but the FOSS save path emits the simple layout.
- **XFA forms** — dynamic XFA is read and can be flattened to real, findable AcroForm pages; XFA datasets round-trip through the AcroForm `/XFA`, sync two-way with AcroForm fields, and export / import via FDF / XFDF / XML. Fine-grained authoring of individual XFA dataset fields is still limited.
- **3D annotations (`PDF3DAnnotation`)** — 3D annotations are read (artwork, views, cross-sections, lighting / render mode, view array) and round-trip, but the 3D content stream is not regenerated on save and the page renderer does not display the 3D model.
- **Document comparison** — the low-level `Aspose.Pdf.Comparison.Diff` edit-operation model (diff operations plus merge / slide optimizers) is available; a higher-level "compare two PDFs and produce a visual diff document" workflow is not.
- **Tagged PDF rendering fidelity** — the structure tree is built and round-trips through `/StructTreeRoot`, but advanced PDF/UA-2 layout-aware tagging features are partial.
- **Some PDF/A conversion semantics** — validation against PDF/A levels works; certain PDF/A-2 / PDF/A-3 conversion fix-ups (transparency flattening, embedded-file constraints, ICC profile downgrade) are stored-but-not-applied.
- **Advanced rendering of complex colour spaces** — `DeviceN` and `Separation` with N > 4 channels, ICC-based group blending in CMYK — render in approximate sRGB.

### Renderer fidelity
- The FOSS rendering output is pixel-close but not byte-identical to Aspose.PDF for .NET / GDI+ output. Sub-pixel glyph positioning, image dithering, anti-aliasing weight, and widget-appearance details may differ slightly. If you compare rendered PNGs against an Aspose.PDF for .NET reference, expect small per-pixel deltas.

### Platform-specific notes
- **`System.Drawing.Common` interop** — methods that return `System.Drawing.Image` / `Bitmap` are tagged `[SupportedOSPlatform("windows")]` and throw `PlatformNotSupportedException` on Linux / macOS. Use the byte-array / `Stream` overloads instead, or read pixel data directly from the renderer's `BitmapInfo`.

## Quick start

### Open a PDF and extract text

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

using var doc = new Document("input.pdf");
var absorber = new TextAbsorber();
absorber.Visit(doc.Pages[1]);
Console.WriteLine(absorber.Text);
```

### Create a new PDF

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

using var doc = new Document();
var page = doc.Pages.Add();

var fragment = new TextFragment("Hello, World!");
fragment.TextState.FontSize = 24;
page.Paragraphs.Add(fragment);

doc.Save("output.pdf");
```

### Find and replace text

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

using var doc = new Document("input.pdf");
var absorber = new TextFragmentAbsorber("old text");
absorber.Visit(doc);

foreach (var fragment in absorber.TextFragments)
    fragment.Text = "new text";

doc.Save("output.pdf");
```

### Fill form fields

```csharp
using Aspose.Pdf;

using var doc = new Document("form.pdf");
foreach (var field in doc.Form.Fields)
    Console.WriteLine($"{field.FullName} = {field.Value}");
```

### Encrypt a PDF

```csharp
using Aspose.Pdf;

using var doc = new Document("input.pdf");
doc.Encrypt("user-pw", "owner-pw", Permissions.PrintDocument, CryptoAlgorithm.AESx256);
doc.Save("encrypted.pdf");
```

### Convert HTML to PDF

```csharp
using Aspose.Pdf;

using var doc = new Document("input.html", new HtmlLoadOptions());
doc.Save("output.pdf");
```

### Render a page to PNG

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Devices;

using var doc = new Document("input.pdf");
var device = new PngDevice(new Resolution(300));
using var stream = File.Create("page1.png");
device.Process(doc.Pages[1], stream);
```

### Sign a PDF

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Forms;

using var signer = new PdfFileSignature();
signer.BindPdf("input.pdf");
var sig = new PKCS7("my.pfx", "pfx-password");
signer.Sign(1, "reason", "contact", "location", true,
    new System.Drawing.Rectangle(100, 100, 200, 60), sig);
signer.Save("signed.pdf");
```

## Documentation

| Guide | Description |
|-------|-------------|
| [Getting Started](docs/getting-started.md) | Installation, first program, core concepts (document lifecycle, 1-based page indexing, saving) |
| [Working with Text](docs/working-with-text.md) | Extract, search, and replace text; build new text with `TextBuilder` / `TextParagraph`; tab stops |
| [Working with Pages](docs/working-with-pages.md) | Add, delete, reorder, rotate, and resize pages; copy pages across documents; merge & split |
| [Fonts](docs/fonts.md) | Standard 14, embedding & subsetting, custom fonts (`FontRepository`), substitution, inspection |
| [Working with Forms](docs/working-with-forms.md) | Read, fill, and build AcroForm fields (text, checkbox, radio, choice, button, signature) |
| [Working with Annotations](docs/working-with-annotations.md) | Read, create, and modify PDF annotations; watermarks; flatten |
| [Bookmarks & Navigation](docs/bookmarks-and-navigation.md) | Outlines/bookmarks, named destinations, and page labels |
| [Security and Encryption](docs/security-and-encryption.md) | Encrypt / decrypt (RC4 / AES-128 / AES-256), document permissions, digital signatures (PKCS#7) |
| [Metadata & XMP](docs/metadata-and-xmp.md) | Document Info dictionary (Title/Author/…) and the XMP metadata packet |
| [Converters](docs/converters.md) | PDF ↔ HTML, PDF ↔ Markdown, PDF ↔ SVG, PDF → plain text |
| [Rendering](docs/rendering.md) | Render pages to PNG, JPEG, BMP, TIFF; SVG output; resolution + color depth control |
| [Optimization](docs/optimization.md) | Compress images, subset fonts, link duplicate streams, PDF/A validation + conversion |
| [Working with Tables](docs/working-with-tables.md) | Extract tabular data with `TableAbsorber`; build tables with `Table` / `Row` / `Cell` |
| [Tagged PDF](docs/tagged-pdf.md) | Read and build `/StructTreeRoot` trees; `ITaggedContent` authoring API |
| [Facades](docs/facades.md) | High-level task-oriented APIs (`PdfFileEditor`, `PdfFileSecurity`, `PdfFileSignature`, `PdfBookmarkEditor`, `PdfContentEditor`, ...) |
| [API Reference](docs/api-reference.md) | Public classes organised by namespace, plus a "not included vs Aspose.PDF for .NET" list |

## API overview

| Area | Key classes |
|------|-------------|
| Core | `Document`, `Page`, `PageCollection`, `Rectangle`, `Matrix` |
| Text | `TextAbsorber`, `TextFragmentAbsorber`, `TextFragment`, `TextBuilder`, `TextReplacer`, `TextState`, `TextFragmentState` |
| Forms | `Form`, `Field`, `FormFieldBuilder`, `FormEditor`, `Signature`, `SignatureField` |
| Annotations | `Annotation`, `AnnotationCollection`, `AnnotationSelector`, `WidgetAnnotation` |
| Security | `CryptoAlgorithm`, `DocumentPrivilege`, `PdfSigner`, `PdfCertificate`, `SignatureAppearance`, `PKCS1`, `PKCS7` |
| Facades | `PdfFileEditor`, `PdfFileSecurity`, `PdfFileSignature`, `PdfBookmarkEditor`, `PdfContentEditor`, `PdfAnnotationEditor`, `PdfFileStamp`, `PdfFileInfo` |
| Converters | `HtmlSaveOptions`, `MdLoadOptions`, `SvgLoadOptions`, `HtmlLoadOptions`, `PdfFormatConversionOptions` |
| Devices | `PngDevice`, `JpegDevice`, `BmpDevice`, `TiffDevice`, `SvgDevice`, `TextDevice` |
| Optimization | `OptimizationOptions`, `PdfFormatConversionOptions` (PDF/A) |
| Tagged PDF | `ITaggedContent`, `LogicalStructure.StructureElement`, `Structure.RootElement` |
| Tables | `Table`, `Row`, `Cell`, `TableAbsorber` |
| Drawing | `Graph`, `Rectangle`, `Circle`, `Ellipse`, `Arc`, `Line`, `Curve` |
| Fonts | `FontRepository`, `FontAbsorber`, `Font`, `FontInfo` |

## Running tests

```bash
dotnet test tests/Aspose.Pdf.Foss.Tests.csproj
```

## Related projects

[Aspose.PDF for .NET](https://products.aspose.com/pdf/net/) is the product from Aspose Pty Ltd that offers a broader feature set — including AI / LowCode workflows, XFA authoring, advanced digital-signature validation, multithreading, and conversion to many non-PDF formats (DOCX, EPUB, XPS, PCL, PostScript, ...). The FOSS edition shares the same public API shape, so code can usually migrate between the two with minimal changes.

## License

MIT — see [LICENSE](LICENSE).
