# Optimization

Reduce PDF file size by removing unused objects, deduplicating streams, and
subsetting fonts. Optimization is applied through
`Document.OptimizeResources(...)`.

## Quick optimization

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Optimization;

using var doc = new Document("large.pdf");

doc.OptimizeResources();   // default strategy
doc.Save("optimized.pdf");
```

## Optimization options

`OptimizationOptions` controls which optimizations run. Construct it directly,
or use the nested `Document.OptimizationOptions` alias.

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Optimization;

using var doc = new Document("large.pdf");

var opt = new OptimizationOptions
{
    // Object-graph cleanup
    RemoveUnusedObjects   = true,
    RemoveUnusedStreams   = true,
    LinkDuplicateStreams  = true,
    AllowReusePageContent = false,
    RemovePrivateInfo     = false,

    // Font handling
    UnembedFonts = false,
    SubsetFonts  = true,

    // Image stream re-encoding
    ImageEncoding = ImageEncoding.Unchanged,
};

// Image-compression sub-options
opt.ImageCompressionOptions.CompressImages = true;
opt.ImageCompressionOptions.ImageQuality   = 75;     // 1..100
opt.ImageCompressionOptions.MaxResolution  = 150;    // downsample above this DPI

doc.OptimizeResources(opt);
doc.Save("optimized.pdf");
```

### `OptimizationOptions.All()`

`All()` returns a strategy with every non-destructive option turned on:

```csharp
var opt = OptimizationOptions.All();
doc.OptimizeResources(opt);
```

### Nested `Document.OptimizationOptions`

`Document.OptimizationOptions` is a thin shadow of the optimization type — use
either name interchangeably:

```csharp
var opt = new Document.OptimizationOptions
{
    RemoveUnusedObjects = true,
    SubsetFonts         = true,
};
doc.OptimizeResources(opt);
```

## Image compression

Compress embedded images with a quality target:

```csharp
var opt = new OptimizationOptions();
opt.ImageCompressionOptions.CompressImages = true;
opt.ImageCompressionOptions.ImageQuality   = 60;   // lower = smaller, lossier

doc.OptimizeResources(opt);
```

## Image downsampling

Limit the effective DPI for embedded images:

```csharp
var opt = new OptimizationOptions();
opt.ImageCompressionOptions.CompressImages = true;
opt.ImageCompressionOptions.MaxResolution  = 150;

doc.OptimizeResources(opt);
```

## Font subsetting

Keep only the glyphs referenced from page content:

```csharp
var opt = new OptimizationOptions
{
    SubsetFonts = true,
};

doc.OptimizeResources(opt);
```

## PDF/A validation

`Document.Validate(...)` checks the document against a PDF/A or PDF/X profile
and writes a brief log to the supplied stream / file path:

```csharp
using Aspose.Pdf;

using var doc = new Document("document.pdf");

// Validate without writing a log
bool isPdfA = doc.Validate(Stream.Null, PdfFormat.PDF_A_1B);

// Validate and emit a log file
bool ok = doc.Validate("validation.log", PdfFormat.PDF_A_2B);
```

`Document.GetPdfACompliance()` returns the PDF/A flavour declared in the XMP
metadata, or `null` if the document does not advertise PDF/A conformance:

```csharp
PdfFormat? declared = doc.GetPdfACompliance();
```

### Supported formats

```csharp
PdfFormat.PDF_A_1A   // PDF/A-1a (full accessibility)
PdfFormat.PDF_A_1B   // PDF/A-1b (basic archival)
PdfFormat.PDF_A_2A   // PDF/A-2a
PdfFormat.PDF_A_2B   // PDF/A-2b
PdfFormat.PDF_A_2U   // PDF/A-2u (Unicode)
PdfFormat.PDF_A_3A   // PDF/A-3a
PdfFormat.PDF_A_3B   // PDF/A-3b
PdfFormat.PDF_A_3U   // PDF/A-3u
PdfFormat.PDF_A_4    // PDF/A-4
PdfFormat.PDF_X_1A   // PDF/X-1a
PdfFormat.PDF_X_3    // PDF/X-3
```

## Converting to PDF/A

`Document.Convert(...)` rewrites the document to conform to a target format.
The error action controls what happens to non-conforming elements:

```csharp
using Aspose.Pdf;

using var doc = new Document("input.pdf");

doc.Convert("conversion.log", PdfFormat.PDF_A_2B, ConvertErrorAction.Delete);
doc.Save("pdfa.pdf");
```

`PdfFormatConversionOptions` exposes a richer configuration:

```csharp
using Aspose.Pdf;

var conv = new PdfFormatConversionOptions(PdfFormat.PDF_A_2B)
{
    ErrorAction        = ConvertErrorAction.Delete,
    TransparencyAction = ConvertTransparencyAction.Default,
    LogFileName        = "conversion.log",
};

doc.Convert(conv);
```

## Document repair

`IsRepairNeeded` reports structural issues; `Repair()` re-serializes the
document, rebuilding the cross-reference table.

```csharp
using Aspose.Pdf;

using var doc = new Document("corrupt.pdf");

if (doc.IsRepairNeeded(out var repairOptions))
{
    Console.WriteLine($"Validation issues: {repairOptions.HasValidationIssues}");
    Console.WriteLine($"XRef issues:       {repairOptions.HasXRefIssues}");
    Console.WriteLine($"Issue count:       {repairOptions.IssueCount}");

    doc.Repair();
    doc.Save("repaired.pdf");
}
```

## General document validation

`Document.Validate()` (no arguments) returns the per-document issue list
gathered by the built-in validator:

```csharp
var issues = doc.Validate();

foreach (var issue in issues)
    Console.WriteLine($"[{issue.Severity}] {issue.Code}: {issue.Message} ({issue.Location})");
```
