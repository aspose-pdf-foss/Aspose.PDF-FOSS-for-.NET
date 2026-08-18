# Comparison

Two ways to compare PDFs, plus the text-diff model both are built on. Everything
lives in `Aspose.Pdf.Comparison`, except the diff primitives in
`Aspose.Pdf.Comparison.Diff`:

| API | Compares | Produces |
|-----|----------|----------|
| `SideBySidePdfComparer` | extracted **text** of two pages or two documents | a result PDF showing both versions next to each other, changes highlighted |
| `GraphicalPdfComparer` | rendered **pixels** of two pages | a pixel difference you can turn into an image (Windows only) |
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

> **Windows only.** `GraphicalPdfComparer` and `ImagesDifference` are marked
> `[SupportedOSPlatform("windows")]` because they expose `System.Drawing` bitmaps.
> On Linux/macOS, render both pages with `PngDevice` and diff the bytes yourself.

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
    Threshold = 0.01,       // per-pixel tolerance
};

using var difference = comparer.GetDifference(v1.Pages[1], v2.Pages[1]);
using var image = difference.DifferenceToImage(Color.Red, Color.White);
image.Save("diff.png");
```

`ImagesDifference` also exposes the raw data — `Difference` (an `int[]`), with
`Stride` and `Height` describing its shape — plus `SourceImage` and
`GetDestinationImage()` for the two rendered pages.

## The diff model

Both comparers sit on `Aspose.Pdf.Comparison.Diff`, which you can use directly on
text. A `DiffOperation` pairs an `Operation` — `Equal`, `Delete`, or `Insert` — with
the text run it applies to, and `DiffUtils` provides the helpers around it
(`FindCommonStartParts`, `FindCommonEndParts`, `AssemblySourceText`).

```csharp
using Aspose.Pdf.Comparison.Diff;

// reconstruct the original text from an edit sequence
string source = DiffUtils.AssemblySourceText(result.FullChanges[0]);
```

## Notes

- Side-by-side comparison is **text-based**: it compares extracted text runs, so a
  change that leaves the text identical (a recoloured heading, a moved image) does
  not register. Use the graphical comparer for those.
- `ExcludeTables` drops table content from the comparison — useful when a data table
  is regenerated every run and would swamp the real prose changes.
