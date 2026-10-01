# Comparison

Three ways to compare PDFs, plus the text-diff model they are built on. Everything
lives in `Aspose.Pdf.Comparison` — including the `Operation` and
`EditOperationsOrder` enums — except the diff primitives `DiffOperation` and
`DiffUtils` in `Aspose.Pdf.Comparison.Diff`:

| API | Compares | Produces |
|-----|----------|----------|
| `SideBySidePdfComparer` | extracted **text** of two pages or two documents | a result PDF showing both versions next to each other, changes highlighted |
| `TextPdfComparer` | extracted **text** of two pages or two documents | edit lists you can count (`CreateComparisonStatistics`) and render as PDF, HTML, Markdown or JSON |
| `GraphicalPdfComparer` | rendered **pixels** of two pages | a pixel difference, written as an image or a PDF report |
| `Aspose.Pdf.Comparison.Diff` | two strings | a normalized list of equal / delete / insert operations |

## Side-by-side comparison

`SideBySidePdfComparer.Compare` lays the two versions out on facing halves of each
result page and marks what changed: **deletions on the left**, **insertions on the
right**. It writes the result document for you — to a path or to a stream.

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Comparison;

using var v1 = new Document("contract-v1.pdf");
using var v2 = new Document("contract-v2.pdf");

var result = SideBySidePdfComparer.Compare(v1, v2, "comparison.pdf",
    new SideBySideComparisonOptions());

if (result.HasChanges)
    Console.WriteLine($"{result.FullChanges.Count} page pair(s) compared");
```

Comparing a single page pair instead of whole documents:

```csharp
var result = SideBySidePdfComparer.Compare(v1.Pages[1], v2.Pages[1], "page1-diff.pdf",
    new SideBySideComparisonOptions());
```

Both overloads also accept a `Stream` in place of the output path.

### Reading the result

A document comparison returns `SideBySideDocsComparisonResult`, a page comparison
returns `SideBySidePagesComparisonResult`. The document-level lists are **one entry
per page**; the page-level ones are flat:

| Member | Document result | Page result |
|--------|-----------------|-------------|
| `HasChanges` | `bool` — any page pair differs | `bool` — the pages differ |
| first side's highlights | `FirstDocChanges` — `List<List<EditContainer>>` | `FirstPageChanges` — `List<EditContainer>` |
| second side's highlights | `SecondDocChanges` | `SecondPageChanges` |
| full edit sequence | `FullChanges` — `List<List<DiffOperation>>` | `FullChanges` — `List<DiffOperation>` |

Each `EditContainer` carries an `Id`, its `Operation` (the `DiffOperation` it came
from) and `Rects` — the rectangles on that page the change covers, which is what you
need to drive your own highlighting instead of the generated document.

```csharp
foreach (var pageChanges in result.FirstDocChanges)
    foreach (var edit in pageChanges)
        Console.WriteLine($"#{edit.Id} {edit.Operation.Operation}: \"{edit.Operation.Text}\" "
                          + $"over {edit.Rects.Count} rect(s)");
```

### Options

`SideBySideComparisonOptions` controls whitespace handling, what area participates,
and the marker colours:

```csharp
var options = new SideBySideComparisonOptions
{
    ComparisonMode = ComparisonMode.ParseSpaces,
    AdditionalChangeMarks = true,
    ExcludeTables = true,
    ComparisonArea1 = new Rectangle(0, 100, 612, 700),   // only compare this box
    ComparisonArea2 = new Rectangle(0, 100, 612, 700),
    ExcludeAreas1 = new[] { new Rectangle(0, 0, 612, 60) },  // skip a running header
    DeleteColor = Color.Red,
    InsertColor = Color.Green,
};
```

`ComparisonMode` decides how whitespace is treated — the setting that most often
changes the answer:

| Mode | Behaviour |
|------|-----------|
| `Normal` | compare the extracted text runs as-is |
| `IgnoreSpaces` | ignore all whitespace; only non-space characters are compared |
| `ParseSpaces` | reconstruct inter-word spaces and line breaks from glyph geometry, then compare |

Use `IgnoreSpaces` when re-flowed layout would otherwise report every line as
changed, and `ParseSpaces` when spacing itself is meaningful.

## Graphical comparison

`GraphicalPdfComparer` renders both pages and diffs the pixels, which catches what a
text comparison cannot — moved images, changed vector art, colour edits.

> **Cross-platform.** Rendering, the pixel diff and the image and PDF outputs
> (`ComparePagesToImage`, `CompareDocumentsToImages`, `ComparePagesToPdf`,
> `CompareDocumentsToPdf`) are managed code and run on every platform. Only the
> `System.Drawing.Bitmap` views of an `ImagesDifference` — `SourceImage`,
> `GetDestinationImage()` and `DifferenceToImage(...)` — are
> `[SupportedOSPlatform("windows")]`.

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Comparison;
using Aspose.Pdf.Devices;   // Resolution

using var v1 = new Document("v1.pdf");
using var v2 = new Document("v2.pdf");

var comparer = new GraphicalPdfComparer
{
    Resolution = new Resolution(150),
    Color = Color.Red,      // colour the differing pixels are marked in
    Threshold = 5,          // per-channel tolerance in percent (0..100); 0 = exact
};

using var difference = comparer.GetDifference(v1.Pages[1], v2.Pages[1]);

// Windows only: a System.Drawing.Bitmap mask of the differing pixels
using var image = difference.DifferenceToImage(Color.Red, Color.White);
image.Save("diff.png");
```

`Threshold` is a percentage of the per-channel colour range: two pixels whose
channels differ by less than it count as identical. The default `0` flags any
difference.

`ImagesDifference` also exposes the raw data on every platform: `Difference` is
an `int[]` with one entry per pixel, row by row — `-1` where the pages agree,
otherwise the second page's colour as `0xRRGGBB` — and `Height` is the number of
rows (so the width is `Difference.Length / Height`); `Stride` is the byte length
of a rendered RGB row, padded to four bytes. `SourceImage` and
`GetDestinationImage()` are the Windows-only bitmap views of the two rendered
pages.

The comparer can also write its result directly, on any platform:

```csharp
comparer.ComparePagesToImage(v1.Pages[1], v2.Pages[1], "diff.png");
comparer.ComparePagesToPdf(v1.Pages[1], v2.Pages[1], "diff.pdf");
comparer.CompareDocumentsToPdf(v1, v2, "diff-all-pages.pdf");
comparer.CompareDocumentsToImages(v1, v2, "diff-images", "page",
    System.Drawing.Imaging.ImageFormat.Png);
```

`ComparePagesToPdf` also accepts a `Document` to append the result page to
instead of a path.

## The diff model

The text comparers sit on `Aspose.Pdf.Comparison.Diff`, which you can use directly on
text. A `DiffOperation` pairs an `Operation` (the `Aspose.Pdf.Comparison.Operation`
enum — `Equal`, `Delete`, or `Insert`) with the text run it applies to, and
`DiffUtils` provides the helpers around it (`FindCommonStartParts`,
`FindCommonEndParts`, `AssemblySourceText`, `AssemblyDestinationText`).

```csharp
using Aspose.Pdf.Comparison.Diff;

// reconstruct the original and the edited text from an edit sequence
string source = DiffUtils.AssemblySourceText(result.FullChanges[0]);
string target = DiffUtils.AssemblyDestinationText(result.FullChanges[0]);
```

The edit sequences the comparers return are normalized by the mergers in
`Aspose.Pdf.Comparison.Diff.DiffOptimization` (`OperationsMerger`,
`MergingOptimizer`, `OperationsSlideMerger`, all implementing
`IDiffOptimizationOperation`). Adjacent deletions and insertions are coalesced
and emitted in the order given by `EditOperationsOrder` — `DeleteFirst` or
`InsertFirst`; `SideBySidePdfComparer` uses `DeleteFirst`, so a replaced run
appears as its `Delete` followed by its `Insert`, and `TextPdfComparer` takes the
order from `ComparisonOptions.EditOperationsOrder` (default `DeleteFirst`).

`TextPdfComparer` hands you these edit lists directly. Its static methods take a
`ComparisonOptions` — `ExtractionArea` (the part of each page to read),
`ExcludeAreas1` / `ExcludeAreas2`, `ExcludeTables` and `EditOperationsOrder`:

- `ComparePages(page1, page2, options)` — one `List<DiffOperation>`;
- `CompareDocumentsPageByPage(doc1, doc2, options)` — one list per page (a missing
  page reads as empty text);
- `CompareFlatDocuments(doc1, doc2, options)` — one list over each document's
  whole text, so a change that crosses a page boundary reads as one edit.

The document methods have an overload with a result PDF path. Any edit list can be
rendered by an output generator — `PdfOutputGenerator`, `HtmlDiffOutputGenerator`
and `MarkdownDiffOutputGenerator` (these two also return a string through
`IStringOutputGenerator`) and `JsonDiffOutputGenerator` (file only), all
`IFileOutputGenerator`s. The PDF and HTML generators take an `OutputTextStyle`
(`InsertedStyle`, `DeletedStyle`, `EqualStyle` as `TextStyle` colours,
`StrikethroughDeleted`), and the PDF one a `PageInfo` for the page geometry.
`CreateComparisonStatistics` counts characters and operations: a
`TextItemComparisonStatistics` for one list (`TotalCharacters`,
`DeletedCharactersCount`, `InsertedCharactersCount`, `DeleteOperationsCount`,
`InsertOperationsCount`), or a `DocumentComparisonStatistics` with the document
totals plus `PagesStatistics` for a per-page result.

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Comparison;

using var v1 = new Document("contract-v1.pdf");
using var v2 = new Document("contract-v2.pdf");

var options = new ComparisonOptions { ExcludeTables = true };
var diffs = TextPdfComparer.CompareDocumentsPageByPage(v1, v2, options);

DocumentComparisonStatistics stats = TextPdfComparer.CreateComparisonStatistics(diffs);
Console.WriteLine($"{stats.DeleteOperationsCount} deletions, {stats.InsertOperationsCount} insertions");

new HtmlDiffOutputGenerator().GenerateOutput(diffs, "changes.html");
string markdown = new MarkdownDiffOutputGenerator().GenerateOutput(diffs);
```

## Notes

- Side-by-side and `TextPdfComparer` comparison are **text-based**: they compare
  extracted text, so a change that leaves the text identical (a recoloured heading,
  a moved image) does not register. Use the graphical comparer for those.
- `ExcludeTables` (on both option classes) drops table content from the comparison — useful when a data table
  is regenerated every run and would swamp the real prose changes.
