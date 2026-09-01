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

The default strategy removes unused objects and streams, links duplicate
streams, and merges images whose decoded pixels (and soft mask) are identical
even when their compressed bytes differ.

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
    CompressObjects       = false,

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

`ResizeImages`, `ImageEncoding` and `MaxResoultion` (spelled that way) on
`OptimizationOptions` are shortcuts for `ImageCompressionOptions.ResizeImages`,
`.Encoding` and `.MaxResolution`. `ImageEncoding` values: `Unchanged`, `Jpeg`,
`Flate`, `Jpeg2000`. `ImageCompressionOptions.Version`
(`ImageCompressionVersion`) is stored for API compatibility; all values behave
the same.

### `OptimizationOptions.All()`

`All()` returns a strategy with every optimization turned on, including the
lossy ones: image recompression at quality 50 downsampled to 150 DPI and
converted to grayscale, duplicate-image merging, font unembedding and
subsetting, and metadata removal. Use it when size matters more than fidelity;
otherwise pick options individually.

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

`Validate(PdfFormatConversionOptions)` takes the same options object as
`Convert`. A document opened with user access only, whose permissions withhold
modification, refuses conformance validation and conversion; the log then
carries that single permission problem.

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
PdfFormat.PDF_A_4E   // PDF/A-4e (engineering)
PdfFormat.PDF_A_4F   // PDF/A-4f (embedded files)
PdfFormat.PDF_UA_1   // PDF/UA-1 (accessibility)
PdfFormat.PDF_E_1    // PDF/E-1 (engineering)
PdfFormat.PDF_X_1A   // PDF/X-1a
PdfFormat.PDF_X_1A_2001
PdfFormat.PDF_X_3    // PDF/X-3
PdfFormat.PDF_X_4    // PDF/X-4
PdfFormat.ZUGFeRD
PdfFormat.v_1_0 … PdfFormat.v_1_7, PdfFormat.v_2_0   // plain PDF versions
```

The plain-version members (`v_1_x`, `v_2_0`, `Pdf`) carry no conformance
requirements: validating against them succeeds for any well-formed file, and
converting to them restamps the header version (a `v_2_0` conversion also
applies the PDF 2.0 rules — see [Metadata & XMP](metadata-and-xmp.md#info-vs-xmp)
and [Security](security-and-encryption.md#pdf-20-deprecation-gates)).

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

Further options: `LogStream` (instead of a file), `IccProfileFileName` /
`OutputIntent`, `ConvertSoftMaskAction`, `ExcludeFontsStrategy`,
`FontEmbeddingOptions`, `AlignText`, `AutoTaggingSettings` (for the `A`
levels), `OptimizeFileSize` and `IsTransferInfo`.

Conversion is refused in three cases:

- **A signed document.** Conformance conversion rewrites the file and would
  break every signature's byte range, so `Convert` returns `false` without
  touching the document; the log carries a single "Can not convert signed
  file" entry. Remove the signatures first (`PdfFileSignature.RemoveSignatures`)
  if the conversion is wanted. Plain version targets are not affected.
- **A permission-restricted document** (see validation above).
- **PDF 2.0 with a pending RC4 encryptor**, which throws
  `DeprecatedFeatureException`.

Fonts are embedded as the target format requires. A font whose OS/2 `fsType`
forbids embedding normally raises `FontEmbeddingException` when the library is
asked to embed it; a conformance conversion embeds it regardless, because the
format's requirement outranks the face's licence flag for the duration of the
conversion. `Document.DisableFontLicenseVerifications = true` switches the
check off altogether.

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

Only structural errors count; advisory warnings (a missing title, say) do not
make `IsRepairNeeded` return `true`. `Repair(RepairOptions)` is accepted for
API compatibility only and performs no work — call `Repair()`.

For a file too damaged to open as a `Document` at all, the
`PdfFileSanitization` facade works at the byte level: `TrimTop()` removes
bytes before the `%PDF-` header, `TrimBottom()` removes bytes after the last
`%%EOF`, and `RebuildXrefAndTrailer()` re-scans the file for its indirect
objects and writes a fresh cross-reference table and trailer. `Recover()` runs
the steps selected by `UseTrimTop`, `UseTrimBottom` (both on by default) and
`UseRebuildXrefAndTrailer`; `Log` lists every action taken.

```csharp
using Aspose.Pdf.Facades;

using var san = new PdfFileSanitization();
san.BindPdf("damaged.pdf");
san.UseRebuildXrefAndTrailer = true;
san.Recover();
san.Save("recovered.pdf");
```

## General document validation

`Document.Validate()` (no arguments) returns the per-document issue list
gathered by the built-in validator:

```csharp
var issues = doc.Validate();

foreach (var issue in issues)
    Console.WriteLine($"[{issue.Severity}] {issue.Code}: {issue.Message} ({issue.Location})");
```

Each `ValidationIssue` carries a `Severity` and `Code` string, the `Message`,
and an optional `Location`.
