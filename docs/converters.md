# Converters

The library supports converting PDFs to and from a handful of formats. All
converters are pure-managed and work in memory.

Supported formats:

| Direction | Formats                              |
|-----------|--------------------------------------|
| Output    | PDF, HTML, Markdown, SVG, plain text |
| Input     | PDF, HTML, Markdown, SVG, XML        |

PDF output may target the PDF/A profile via `Document.Convert(...)` (see
[Optimization](optimization.md)).

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

### Single page

`SavePageAsHtml` returns a `<div>` fragment for the requested 1-based page,
not a full HTML document.

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

### `HtmlSaveOptions`

The HTML writer emits absolute-positioned text, inlines images as base64 data
URIs, and writes link annotations as anchor tags. Of the configuration fields,
`ExplicitListOfSavedPages` is honored at save time; the remaining fields
(`PartsEmbeddingMode`, `FixedLayout`, `SplitIntoPages`, `FontSavingMode`,
`SaveTransparentTexts`, etc.) are accepted for API compatibility but are not
yet consulted by the writer.

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

### `MarkdownConverterOptions`

```csharp
var options = new MarkdownConverterOptions
{
    H1Threshold   = 24,        // font size >= 24 becomes # heading
    H2Threshold   = 18,
    H3Threshold   = 14,
    IncludeTables = true,
    PageBreak     = "\n---\n\n",
};

var converter = new PdfToMarkdownConverter(options);
string md = converter.SaveAsMarkdown(doc);
```

## Markdown to PDF

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Converters;

using var doc = Document.Open("readme.md", new MdLoadOptions());
doc.Save("readme.pdf");
```

### `MdLoadOptions`

`PageInfo` controls the generated page size and margins. `CssStyles` is
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

## SVG to PDF

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Converters;

using var doc = Document.Open("drawing.svg", new SvgLoadOptions());
doc.Save("drawing.pdf");
```

### `SvgLoadOptions`

The SVG converter always sizes the PDF page to the SVG `viewBox` (or its
`width`/`height`). `AdjustPageSize` and `ConversionEngine` are accepted for
API compatibility but are not currently consulted.

```csharp
var options = new SvgLoadOptions();

using var doc = Document.Open("drawing.svg", options);
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
```
