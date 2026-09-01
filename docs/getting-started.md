# Getting Started

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Any OS: Windows, macOS, or Linux

The only runtime dependency is the **`System.Drawing.Common`** NuGet package, used by a handful of image-interop members that are Windows-only on .NET 8 (they throw `PlatformNotSupportedException` on Linux/macOS): the members that return a `System.Drawing` object (`ImageDevice.GetBitmap`, `FigureElement.Image`, `StampInfo.Image`; `XImage.Grayscaled` returns `null` instead), the hOCR overloads of `Document.Convert` that hand each page to the callback as a `System.Drawing.Image`, EMF output in `PdfConverter`, and EMF/WMF stamp images. The rest of the library — text, forms, parsing, encryption & signing, and page→image rendering via `PngDevice`/`JpegDevice`/`BmpDevice`/`GifDevice`/`TiffDevice` — runs on Windows, macOS, and Linux. On Windows the image devices rasterise through GDI+; elsewhere (or when the environment variable `ASPOSE_PDF_FORCE_SOFTWARE_RENDERER=1` is set) they use the library's own managed rasteriser.

Printing is not implemented: `PdfViewer.PrintDocument` throws `PlatformNotSupportedException` — render pages to images and hand them to your own printing stack. `Aspose.Pdf.Printing.PrintingOptionalDependencyGuard.EnsureDependenciesAvailable()` checks that `System.Drawing.Common` can be loaded and throws `MissingOptionalDependencyException` (naming the package to install) when it cannot.

## Installation

Build from source:

```bash
git clone https://github.com/aspose-pdf-foss/Aspose.PDF-FOSS-for-.NET.git
cd Aspose.PDF-FOSS-for-.NET
dotnet build src/Aspose.Pdf.Foss.csproj -c Release
```

Reference the built project directly:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Aspose.PDF-FOSS-for-.NET/src/Aspose.Pdf.Foss.csproj" />
</ItemGroup>
```

## Your first program

```bash
dotnet new console -n PdfDemo
cd PdfDemo
# (add the project reference shown above to PdfDemo.csproj)
```

### Open a PDF and extract text

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

using var doc = new Document("sample.pdf");

var absorber = new TextAbsorber();
absorber.Visit(doc.Pages[1]);  // pages are 1-based

Console.WriteLine($"Pages: {doc.Pages.Count}");
Console.WriteLine(absorber.Text);
```

### Create a PDF from scratch

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

using var doc = new Document();
var page = doc.Pages.Add();

var fragment = new TextFragment("Hello from Aspose.PDF FOSS!");
fragment.TextState.FontSize = 20f;
fragment.TextState.IsBold = true;
page.Paragraphs.Add(fragment);

doc.Save("hello.pdf");
```

> New documents are created at **PDF version 1.7** by default. Call
> `doc.SetVersion("2.0")` (or any `"1.x"`) before saving to change the header
> version, or construct the document with `new Document(PdfVersion.v_2_0)` to
> record the target version in the catalog; documents opened from a file keep
> their own version.

### Open an encrypted PDF

```csharp
using Aspose.Pdf;

// Pass the user or owner password as the second argument.
using var doc = new Document("encrypted.pdf", "mypassword");
Console.WriteLine($"Pages: {doc.Pages.Count}");
```

### Open from different sources

```csharp
using Aspose.Pdf;

// From file path
using var doc1 = new Document("input.pdf");

// From byte array
byte[] data = File.ReadAllBytes("input.pdf");
using var doc2 = new Document(new MemoryStream(data));

// From stream
using var stream = File.OpenRead("input.pdf");
using var doc3 = new Document(stream);

// From HTML
using var doc4 = new Document("page.html", new HtmlLoadOptions());

// From Markdown
using var doc5 = new Document("readme.md", new MdLoadOptions());

// From SVG
using var doc6 = Document.Open("drawing.svg", new SvgLoadOptions());

// From plain text
using var doc7 = new Document("notes.txt", new TxtLoadOptions());
```

`HtmlLoadOptions`, `MdLoadOptions`, `SvgLoadOptions`, and `TxtLoadOptions` all
live in the `Aspose.Pdf` namespace. Each has constructor overloads
(`new Document(path | stream, options)`); HTML, Markdown, and SVG additionally
have the static factory (`Document.Open(path | byte[], options)`), so either
form can be used for those. `Document.Open(...)` also has plain PDF overloads
taking a path, a stream, or a byte array, each with an optional password.

A damaged PDF that fails to open can be repaired first with
`Aspose.Pdf.Facades.PdfFileSanitization`: `BindPdf` the file or stream, call
`Recover()` (trims junk before the header and after `%%EOF`; set
`UseRebuildXrefAndTrailer` to also rebuild the cross-reference table), then
`Save` the result and open that.

## Core concepts

### Document lifecycle

`Document` implements `IDisposable`. Use `using` so the underlying stream / file handle is released:

```csharp
using var doc = new Document("input.pdf");
// ... work with the document
doc.Save("output.pdf");
```

### Page indexing

Pages are **1-based**: `doc.Pages[1]` is the first page, `doc.Pages[doc.Pages.Count]` is the last. `doc.PageCount` is a shortcut for `doc.Pages.Count`.

```csharp
var firstPage = doc.Pages[1];           // 1-based
var lastPage  = doc.Pages[doc.Pages.Count];
Console.WriteLine($"Page count: {doc.PageCount}");
```

### Saving

```csharp
// Save to file
doc.Save("output.pdf");

// Save to stream
using var fs = File.Create("output.pdf");
doc.Save(fs);

// Save to byte array (call ToArray, NOT Save())
byte[] bytes = doc.ToArray();

// Save in place (back to the original file or writable stream)
doc.Save();
```

`doc.Save()` (no argument) writes back to the source: to the file path the document was opened from, or (as an incremental update) into the stream passed to `new Document(stream)` when that stream is writable and seekable. A document created from a byte array, from `Document.Open(stream)`, or from a read-only stream has no writable source; `doc.Save()` then only finalises the in-memory document (paragraph layout, stamp materialisation) and writes nothing — use `doc.Save(path)`, `doc.Save(stream)`, or `doc.ToArray()` to get the bytes.

### Saving as PDF/A

```csharp
using Aspose.Pdf;

using var doc = new Document("input.pdf");
doc.Convert("conversion-log.xml", PdfFormat.PDF_A_2B, ConvertErrorAction.Delete);
doc.Save("output_pdfa2b.pdf");
```

## What's next

- [Working with Text](working-with-text.md) — extract, search, replace, build text
- [Working with Pages](working-with-pages.md) — add, delete, reorder, rotate, resize; merge & split
- [Fonts](fonts.md) — embedding, subsetting, Standard 14, custom fonts, substitution
- [Working with Forms](working-with-forms.md) — read, fill, and build AcroForm fields
- [Working with Annotations](working-with-annotations.md) — add and modify annotations
- [Bookmarks & Navigation](bookmarks-and-navigation.md) — outlines, named destinations, page labels
- [Security and Encryption](security-and-encryption.md) — encrypt, decrypt, digital signatures
- [Metadata & XMP](metadata-and-xmp.md) — document info dictionary and XMP packet
- [Converters](converters.md) — PDF to / from HTML, Markdown, SVG, text
- [Rendering](rendering.md) — render pages to PNG, JPEG, BMP, TIFF
- [Optimization](optimization.md) — compress images, subset fonts, PDF/A
- [Working with Tables](working-with-tables.md) — extract and create tables
- [Tagged PDF](tagged-pdf.md) — accessible PDFs with structure trees
- [Facades](facades.md) — high-level API for common tasks
- [API Reference](api-reference.md) — complete class and method listing
