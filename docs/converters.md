# Converters

The library supports converting PDFs to and from a handful of formats. All
converters are pure-managed and work in memory.

Supported formats:

| Direction | Formats                                                      |
|-----------|--------------------------------------------------------------|
| Output    | PDF, HTML, Markdown, SVG, XML (tagged structure), plain text |
| Input     | PDF, HTML, Markdown, SVG, XML, plain text, PostScript / EPS  |

PDF output may target the PDF/A profile via `Document.Convert(...)` (see
[Optimization](optimization.md)).

`Document.Save(path, SaveFormat)` dispatches `Pdf`, `Html`, `Markdown`, `Svg`
and `Xml` (the tagged-structure export); `Save(stream, SaveFormat)` the same
minus `Svg`, and minus `Html` — an HTML stream target needs the
`Save(stream, HtmlSaveOptions)` overload. Any other `SaveFormat` value throws
`NotSupportedException`. Plain text loads through `Document(path,
TxtLoadOptions)`; XML loads through `Document.BindXml(...)`.

The static `Document.Convert(source, loadOptions, destination, saveOptions)`
(file paths or streams on either side) loads and saves in one call:
`HtmlSaveOptions` writes HTML, `MarkdownSaveOptions` Markdown, and any other
save options PDF.

## PDF to HTML

### Whole document

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Converters;

using var doc = new Document("input.pdf");

var converter = new PdfToHtmlConverter();
string html = converter.SaveAsHtml(doc);

File.WriteAllText("output.html", html);
```

`SaveAsHtml(doc, Stream)` writes the same document straight to a stream.

### Single page

`SavePageAsHtml` returns a `<div>` fragment for the requested 1-based page,
not a full HTML document. `RenderPageAsDocument(doc, pageNumber, bodyOnly:
false)` wraps that fragment in a complete document envelope.

```csharp
var converter = new PdfToHtmlConverter();
string pageHtml = converter.SavePageAsHtml(doc, pageNumber: 1);
```

### All pages as separate fragments

```csharp
var converter = new PdfToHtmlConverter();
string[] pages = converter.SaveAllPagesAsHtml(doc);

for (int i = 0; i < pages.Length; i++)
    File.WriteAllText($"page_{i + 1}.html", pages[i]);
```

### Via `Document.Save(..., HtmlSaveOptions)`

```csharp
using Aspose.Pdf;

using var doc = new Document("input.pdf");
doc.Save("output.html", new HtmlSaveOptions());
```

A plain file save writes the HTML plus a `<stem>_files` sidecar folder holding
each page's vector graphics (SVG), raster images and the stylesheet.
`SplitIntoPages = true` writes one HTML file per page instead. To get a single
self-contained file, combine `RasterImagesSavingMode =
AsEmbeddedPartsOfPngPageBackground` with `PartsEmbeddingMode =
EmbedAllIntoHtml` — each page's graphics are flattened to one PNG behind the
selectable text layer and everything is inlined as `data:` URIs.

### `HtmlSaveOptions`

The HTML writer emits absolute-positioned text and keeps link annotations as
anchor tags. The string-returning converter methods apply all styling inline
on the elements and inline images as base64 `data:` URIs; the file save
writes a stylesheet and externalises images as described above. Of the
configuration fields, the writer consults `ExplicitListOfSavedPages`,
`SplitIntoPages`, `SplitCssIntoPages`, `DocumentType`, `Title`,
`RasterImagesSavingMode`, `PartsEmbeddingMode`, `ImageResolution` (the DPI of
page-background rasters), `FontSavingMode`, `DefaultFontName`,
`ExcludeFontNameList`, `FontEncodingStrategy`, `CssClassNamesPrefix`,
`SpecialFolderForAllImages`, `LettersPositioningMethod`,
`HtmlMarkupGenerationMode`, `SaveTransparentTexts`,
`TrySaveTextUnderliningAndStrikeoutingInCss`, `UseZOrder`,
`ConvertMarkedContentToLayers` (optional-content layers as nested boxes) and
the callbacks `CustomResourceSavingStrategy`, `CustomHtmlSavingStrategy`,
`CustomCssSavingStrategy`, `CustomStrategyOfCssUrlCreation` and
`CustomProgressHandler`. The remaining fields (`FixedLayout`,
`RenderTextAsImage`, `SaveFullFont`, `PageMarginIfAny`, etc.) are accepted for
API compatibility but are not consulted.

```csharp
var options = new HtmlSaveOptions
{
    // 1-based; restrict the page set to convert
    ExplicitListOfSavedPages = new[] { 1, 3, 5 },
};

doc.Save("output.html", options);
```

## HTML to PDF

```csharp
using Aspose.Pdf;

using var doc = new Document("page.html", new HtmlLoadOptions());
doc.Save("from-html.pdf");
```

From bytes:

```csharp
byte[] html = File.ReadAllBytes("page.html");
using var doc = new Document(new MemoryStream(html), new HtmlLoadOptions());
doc.Save("from-html.pdf");
```

Relative resources (images, stylesheets, fonts) resolve against
`HtmlLoadOptions.BasePath`. When you load from a file path the base path is
derived from the file's directory; when you load from a stream, pass it
explicitly with `new HtmlLoadOptions(basePath)`. `http:` / `https:` image
references are fetched (an unreachable URL falls back to the `alt` text), and
`file:` URIs are accepted. A page authored on Windows that references its assets
with backslash separators (`Images\logo.png`) is resolved correctly on Linux
and macOS as well. Further options: `PageInfo` (page size and margins),
`IsRenderToSinglePage`, `IsEmbedFonts`, `InputEncoding`, `HtmlMediaType`
(`Print` by default, or `Screen`), `IsPriorityCssPageRule`,
`PageLayoutOption` and `CustomLoaderOfExternalResources`.
`CreateLogicalStructure = true` also builds a structure tree that mirrors the
HTML elements (headings, paragraphs, lists, figures, links).
`ExternalResourcesCredentials` is accepted but not consulted.

## PDF to Markdown

### Whole document

```csharp
using Aspose.Pdf.Converters;

var converter = new PdfToMarkdownConverter();
string markdown = converter.SaveAsMarkdown(doc);

File.WriteAllText("output.md", markdown);
```

### Single page

```csharp
var converter = new PdfToMarkdownConverter();
string pageMd = converter.SavePageAsMarkdown(doc, pageNumber: 1);
```

`SaveAllPagesAsMarkdown(doc)` returns one string per page.

### `MarkdownConverterOptions`

```csharp
var options = new MarkdownConverterOptions
{
    H1Threshold   = 24,        // font size >= 24 becomes # heading
    H2Threshold   = 18,
    H3Threshold   = 14,
    IncludeTables = true,      // tables detected from drawn grid lines
    PageBreak     = "\n---\n\n",
    ImageOutputDirectory = "md_images",   // null (default) skips images
};

var converter = new PdfToMarkdownConverter(options);
string md = converter.SaveAsMarkdown(doc);
```

`Document.Save(path, new MarkdownSaveOptions())` (`Aspose.Pdf.PdfToMarkdown`)
is the alternative entry point; its `ResourcesDirectoryName` names the image
folder next to the output, `UseImageHtmlTag` switches image references to
`<img>` tags, `AreaToExtract` limits the conversion to a page rectangle and
`ExtractVectorGraphics` also writes vector drawings as image resources.
`RecognizeBullets` and `RelativeHorizontalProximity` are accepted but not
consulted.

## Markdown to PDF

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Converters;

using var doc = Document.Open("readme.md", new MdLoadOptions());
doc.Save("readme.pdf");
```

### `MdLoadOptions`

`PageInfo` controls the generated page size and margins. A `<style>` block in
the Markdown source is never rendered as text; when `IsPriorityCssPageRule` is
set, its `@page` size / margin rule overrides `PageInfo`. `CssStyles` is
accepted for API compatibility but is not consulted by the converter.

```csharp
var options = new MdLoadOptions
{
    PageInfo = new PageSizeInfo
    {
        Width  = 612,                              // US Letter, in points
        Height = 792,
        Margin = new MarginInfo(72, 72, 72, 72),   // 1-inch margins
    },
};

using var doc = Document.Open("readme.md", options);
doc.Save("readme.pdf");
```

## PDF to SVG

### Single page

```csharp
using Aspose.Pdf.Converters;

var converter = new PdfToSvgConverter();
string svg = converter.SavePageAsSvg(doc, pageNumber: 1);

File.WriteAllText("page1.svg", svg);
```

### All pages

```csharp
var converter = new PdfToSvgConverter();
string[] pages = converter.SaveAllPagesAsSvg(doc);
```

### Direct to file(s)

```csharp
var converter = new PdfToSvgConverter();

converter.SavePageToFile(doc, pageNumber: 1, "page1.svg");

// Output:   svg_out/page_1.svg, svg_out/page_2.svg, ...
converter.SaveAllPagesToFiles(doc, directory: "svg_out", prefix: "page");
```

`doc.Save("out.svg", new SvgSaveOptions())` writes the first page to the
given path and each further page N beside it as `out_N.svg`;
`CompressOutputToZipArchive = true` packs them into one ZIP file instead.
Images are embedded as `data:` URIs unless `CustomStrategyOfEmbeddedImagesSaving`
is set — the callback receives each image and returns the `href` to write.

## SVG to PDF

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Converters;

using var doc = Document.Open("drawing.svg", new SvgLoadOptions());
doc.Save("drawing.pdf");
```

### `SvgLoadOptions`

By default the PDF page is sized to the SVG `viewBox` (or its
`width`/`height`). `PageInfo` overrides that: an explicit `Width` / `Height`
replaces the content-derived dimension, and margins grow the page around the
artwork, which is never scaled (values are CSS pixels, converted at 0.75 pt
per px). `AdjustPageSize` and `ConversionEngine` are accepted for API
compatibility but are not currently consulted.

```csharp
var options = new SvgLoadOptions();

using var doc = Document.Open("drawing.svg", options);
doc.Save("drawing.pdf");
```

## PostScript to PDF

PostScript and Encapsulated PostScript files are read by an interpreter that runs
the program and records what it paints:

```csharp
using Aspose.Pdf;

using var doc = new Document("figure.eps", new PsLoadOptions());
doc.Save("figure.pdf");
```

### `PsLoadOptions`

`FontsFolders` names the folders searched for the font programs the source asks
for: a face among the Standard 14 is referenced by name, any other face is
outlined from the program found in those folders, and a face that cannot be
found paints no text. A PostScript error ends the program, and the page keeps
whatever it had painted up to that point. `SuppressErrors` and
`ConvertFontsToTTF` are accepted for API compatibility but are not currently
consulted.

The interpreter covers the language core, paths, clipping, transformations,
colour and tint transforms, sampled images, user paths, forms, tiling
patterns, character paths, Type 3 fonts, composite fonts (escape and 8/8
mapping), and text in both the standard faces and any face supplied through
`FontsFolders`. Smooth shading through `shfill` paints nothing, and the
raster-device controls (halftones, screens, transfer functions) are accepted
and ignored.

```csharp
var options = new PsLoadOptions
{
    FontsFolders = new[] { @"C:\Fonts\Type1" },
};

using var doc = new Document("drawing.ps", options);
doc.Save("drawing.pdf");
```

## PDF to plain text

### Whole document

```csharp
using Aspose.Pdf.Converters;

var converter = new PdfToTextConverter();
string text = converter.SaveAsText(doc);
```

### Per page

```csharp
var converter = new PdfToTextConverter();

string page1   = converter.SavePageAsText(doc, pageNumber: 1);
string[] pages = converter.SaveAllPagesAsText(doc);
string part    = converter.SavePageRangeAsText(doc, fromPage: 2, toPage: 4);
```
