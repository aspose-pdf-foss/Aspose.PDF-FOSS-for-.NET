# Bookmarks & Navigation

Document navigation structures — **outlines** (bookmarks), **named
destinations**, and **page labels** — live on the `Document`:

| Feature | Member |
|---|---|
| Bookmarks (read) | `Document.Outlines` (`OutlineCollection`) |
| Bookmarks (create) | `OutlineBuilder` |
| Named destinations | `Document.NamedDestinations` |
| Page labels (read) | `Document.PageLabels` |
| Page labels (create) | `PageLabelBuilder` |

## Reading bookmarks

`Document.Outlines` enumerates the top-level bookmarks; each is an
`OutlineItemCollection` that enumerates its own children, so a recursive walk
prints the whole tree:

```csharp
using Aspose.Pdf;

using var doc = new Document("input.pdf");

void Print(OutlineItemCollection item, int depth)
{
    Console.WriteLine($"{new string(' ', depth * 2)}{item.Title}  ->  page {item.DestinationPageNumber}");
    foreach (var child in item)          // children of this bookmark
        Print(child, depth + 1);
}

foreach (OutlineItemCollection top in doc.Outlines)
    Print(top, 0);

Console.WriteLine($"Has bookmarks: {doc.HasOutlines}");
```

`DestinationPageNumber` is the 1-based target page (0 when the bookmark has no
page destination).

## Creating bookmarks

`OutlineBuilder` is the simplest way to author a bookmark tree. It registers
itself with the document and is materialised on save. **Page indices are
0-based** here (pass a `pageIndex`, or use the `Add(title, page)` overload to
target a `Page` object directly):

```csharp
using Aspose.Pdf;

using var doc = new Document("input.pdf");

var builder = new OutlineBuilder(doc);

var ch1 = builder.Add("Chapter 1", pageIndex: 0)
                 .SetBold(true)
                 .SetColor(0.2, 0.2, 0.8);   // RGB 0.0–1.0

ch1.AddChild("Section 1.1", pageIndex: 1);
ch1.AddChild("Section 1.2", pageIndex: 2).SetOpen(false);

builder.Add("Chapter 2", doc.Pages[4]);      // by Page object

doc.Save("bookmarked.pdf");
```

`OutlineItemBuilder` supports `AddChild`, `SetOpen`, `SetBold`, `SetItalic`, and
`SetColor`, each returning the builder for chaining.

## Named destinations

`Document.NamedDestinations` lists named jump targets and looks them up by name:

```csharp
using Aspose.Pdf;

using var doc = new Document("input.pdf");

foreach (var dest in doc.NamedDestinations)
    Console.WriteLine($"{dest.Name} -> page {dest.PageNumber}");

// Look up by name (returns an IAppointment, or null)
var intro = doc.NamedDestinations["Introduction"];
```

`NamedDestinationCollection.Add(string name, IAppointment appointment)` adds a
new entry; pass any destination object that implements `IAppointment`.

## Page labels

Page labels give pages display names (e.g. `i, ii, iii` for front-matter, then
`1, 2, 3`). Build them with `PageLabelBuilder` — ranges start at a **0-based**
page index:

```csharp
using Aspose.Pdf;

using var doc = new Document("input.pdf");

var labels = new PageLabelBuilder(doc);
labels.Add(0, NumberingStyle.LowerRoman);              // pages 1..  -> i, ii, iii
labels.Add(3, NumberingStyle.Decimal);                 // page 4..   -> 1, 2, 3
labels.Add(10, NumberingStyle.Decimal, prefix: "A-");  // page 11..  -> A-1, A-2

doc.Save("labeled.pdf");
```

`NumberingStyle` values: `None`, `Decimal`, `UpperRoman`, `LowerRoman`,
`UpperAlpha`, `LowerAlpha` (parity aliases like `NumeralsArabic` also exist).

`Document.PageLabels` is always a usable collection — never `null`, even when the
document declares no labels yet — so you can also add, replace, or remove labels
through it directly, and the changes are written to the `/PageLabels` tree on save:

```csharp
var label = new PageLabel { NumberingStyle = NumberingStyle.NumeralsArabic, StartingValue = 10 };
doc.PageLabels.UpdateLabel(0, label);   // add or replace the range starting at page 1
doc.PageLabels.RemoveLabel(3);          // remove the range starting at page 4
doc.Save("relabeled.pdf");
```

Test whether any labels are declared with `doc.HasPageLabels` (or
`doc.PageLabels.Count`) — `doc.PageLabels` itself is never `null`.

## What's next

- [Working with Pages](working-with-pages.md) — the pages bookmarks point at
- [Working with Annotations](working-with-annotations.md) — link annotations for in-page navigation
- [Facades](facades.md#pdfbookmarkeditor) — `PdfBookmarkEditor` for stream-based bookmark editing
