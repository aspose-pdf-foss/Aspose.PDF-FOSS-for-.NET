# Working with Pages

`Document.Pages` is a **1-based** `PageCollection`. This page covers adding,
inserting, deleting, reordering, rotating, and resizing pages, plus merging and
splitting documents at the DOM level. For the high-level facade equivalents see
[`PdfFileEditor`](facades.md#pdffileeditor).

## Accessing pages

```csharp
using Aspose.Pdf;

using var doc = new Document("input.pdf");

Page first = doc.Pages[1];                 // 1-based
Page last  = doc.Pages[doc.Pages.Count];
Console.WriteLine($"Page count: {doc.PageCount}");

foreach (Page page in doc.Pages)
    Console.WriteLine(page.Rect);          // page rectangle, in points
```

## Adding pages

```csharp
using Aspose.Pdf;

using var doc = new Document();

Page blank = doc.Pages.Add();                       // default size
Page a4    = doc.Pages.Add(PageSize.A4.Width, PageSize.A4.Height);
Page letter = doc.Pages.Add(612, 792);              // explicit points
```

`PageSize` provides the common presets: `A0`–`A6`, `B5`, `Letter`, `Legal`,
`P11x17`. Each exposes `Width` / `Height` (points) and an `IsLandscape` toggle.

## Inserting and deleting

```csharp
// Insert a blank page so it becomes page 2 (1-based)
Page inserted = doc.Pages.Insert(2);
Page sized    = doc.Pages.Insert(2, PageSize.A4.Width, PageSize.A4.Height);

// Delete one page, several, or all
doc.Pages.Delete(3);             // delete page 3
doc.Pages.Delete(1, 4, 5);       // delete pages 1, 4, 5
doc.Pages.Delete();              // delete every page
```

## Reordering / moving a page

There's no dedicated move method — reinsert the page object, then delete the
original slot:

```csharp
// Move page 5 to the front
var page = doc.Pages[5];
doc.Pages.Insert(1, page);   // now appears at position 1
doc.Pages.Delete(6);         // original slot shifted to 6 after the insert
```

## Rotating pages

```csharp
// Rotation enum: None, on90, on180, on270
doc.Pages[1].Rotate = Rotation.on90;

// Or by degrees
doc.Pages[1].RotateDegrees = 180;
doc.Pages[2].SetRotation(270);
```

## Resizing pages

```csharp
// Set a page's size (points)
doc.Pages[1].SetPageSize(PageSize.A4.Width, PageSize.A4.Height);

// Read / set the boxes directly
Rectangle media = doc.Pages[1].MediaBox;
Rectangle crop  = doc.Pages[1].CropBox;
doc.Pages[1].CropBox = new Rectangle(0, 0, 595.276, 841.890);
```

## Copying pages between documents

`Pages.Add(Page)` deep-copies a page (and its resources) from any document:

```csharp
using var src  = new Document("source.pdf");
using var dest = new Document();

dest.Pages.Add(src.Pages[1]);   // copy the first page of source into dest
dest.Save("copied.pdf");
```

## Merging documents

Append every page of one document onto another with `Pages.Add(PageCollection)`:

```csharp
using Aspose.Pdf;

using var target = new Document("first.pdf");
using (var second = new Document("second.pdf"))
{
    target.Pages.Add(second.Pages);   // append all pages of second
}
target.Save("merged.pdf");
```

To merge many files:

```csharp
using var merged = new Document();
foreach (var path in new[] { "a.pdf", "b.pdf", "c.pdf" })
{
    using var part = new Document(path);
    merged.Pages.Add(part.Pages);
}
merged.Save("all.pdf");
```

## Splitting a document

Copy the desired pages into fresh documents:

```csharp
using Aspose.Pdf;

using var src = new Document("input.pdf");

// One file per page
for (int i = 1; i <= src.Pages.Count; i++)
{
    using var part = new Document();
    part.Pages.Add(src.Pages[i]);
    part.Save($"page_{i}.pdf");
}

// A range (pages 2–4) into one file
using var range = new Document();
for (int i = 2; i <= 4; i++)
    range.Pages.Add(src.Pages[i]);
range.Save("pages_2-4.pdf");
```

## What's next

- [Working with Text](working-with-text.md) — add text to the pages you create
- [Rendering](rendering.md) — render pages to images
- [Facades](facades.md#pdffileeditor) — `PdfFileEditor` for stream-based merge/split/extract
