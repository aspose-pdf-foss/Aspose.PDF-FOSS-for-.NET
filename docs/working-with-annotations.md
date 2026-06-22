# Working with Annotations

## Reading annotations

### List annotations on a page

Pages are 1-based. `Page.Annotations` is an `AnnotationCollection` whose
`this[int]` indexer is also 1-based.

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Annotations;

using var doc = new Document("input.pdf");

foreach (var annot in doc.Pages[1].Annotations)
{
    Console.WriteLine($"Type: {annot.AnnotationType}");
    Console.WriteLine($"  Rect:     {annot.Rect}");
    Console.WriteLine($"  Contents: {annot.Contents}");
    Console.WriteLine($"  Title:    {annot.Title}");
}
```

### Pattern-match by concrete annotation type

```csharp
foreach (var annot in doc.Pages[1].Annotations)
{
    switch (annot)
    {
        case LinkAnnotation link:
            Console.WriteLine($"Link to: {link.Uri ?? $"page {link.TargetPageNumber}"}");
            break;
        case TextAnnotation text:
            Console.WriteLine($"Note (open={text.Open}): {text.Contents}");
            break;
        case FreeTextAnnotation ft:
            Console.WriteLine($"Free text: {ft.Contents}");
            break;
        case FileAttachmentAnnotation attach:
            Console.WriteLine($"Attachment: {attach.FileName}");
            break;
    }
}
```

## Adding annotations

`Page.Annotations` exposes `Add*` helpers that build the annotation dictionary,
append it to the page, and return the new `Annotation`. Color parameters take a
`double[]` (values in 0..1, e.g. `new double[] { 1, 0, 0 }` for red).

### Text annotation (sticky note)

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Annotations;

var page = doc.Pages[1];

page.Annotations.AddTextAnnotation(
    new Rectangle(100, 700, 130, 730),
    contents: "Review this section",
    title: "Reviewer",
    open: true);
```

### Free text annotation

```csharp
page.Annotations.AddFreeTextAnnotation(
    new Rectangle(100, 600, 400, 650),
    contents: "This comment is rendered directly on the page",
    fontName: "Helvetica",
    fontSize: 12,
    color: new double[] { 1, 0, 0 });   // red
```

### Link annotations

```csharp
// Link to a URL
page.Annotations.AddLinkAnnotation(
    new Rectangle(100, 550, 300, 570),
    uri: "https://github.com/aspose-pdf-foss");

// Link to another page (1-based page number, destination rectangle in points)
page.Annotations.AddLinkAnnotation(
    new Rectangle(100, 520, 200, 540),
    destinationPage: 3,
    destRect: new Rectangle(0, 0, 612, 792));
```

### Highlight, underline, strikeout, squiggly

```csharp
page.Annotations.AddHighlightAnnotation(
    new Rectangle(100, 480, 400, 500),
    color: new double[] { 1, 1, 0 });   // yellow

page.Annotations.AddUnderlineAnnotation(
    new Rectangle(100, 450, 400, 470),
    color: new double[] { 0, 0, 1 });

page.Annotations.AddStrikeOutAnnotation(
    new Rectangle(100, 420, 400, 440),
    color: new double[] { 1, 0, 0 });

page.Annotations.AddSquigglyAnnotation(
    new Rectangle(100, 390, 400, 410),
    color: new double[] { 1, 0, 0 });
```

### Shape annotations

```csharp
// Rectangle (square)
page.Annotations.AddSquareAnnotation(
    new Rectangle(100, 300, 200, 350),
    borderColor: new double[] { 0, 0, 1 },
    fillColor:   new double[] { 0.9, 0.9, 1 },
    lineWidth:   2);

// Circle / ellipse
page.Annotations.AddCircleAnnotation(
    new Rectangle(250, 300, 350, 350),
    borderColor: new double[] { 1, 0, 0 },
    fillColor:   null,
    lineWidth:   1.5);

// Line
page.Annotations.AddLineAnnotation(
    new Rectangle(100, 250, 400, 280),
    x1: 100, y1: 265, x2: 400, y2: 265,
    color: new double[] { 0, 0, 0 },
    lineWidth: 1);

// Polygon (vertex list as x,y,x,y,...)
page.Annotations.AddPolygonAnnotation(
    new Rectangle(100, 150, 250, 230),
    vertices: new double[] { 175, 230, 100, 150, 250, 150 },
    borderColor: new double[] { 0, 0.5, 0 },
    fillColor:   new double[] { 0.8, 1, 0.8 },
    lineWidth:   1);

// Polyline
page.Annotations.AddPolyLineAnnotation(
    new Rectangle(300, 150, 500, 230),
    vertices: new double[] { 300, 200, 350, 230, 400, 180, 450, 220, 500, 150 },
    color:    new double[] { 0.5, 0, 0.5 },
    lineWidth: 2);
```

### Ink (freehand drawing)

```csharp
page.Annotations.AddInkAnnotation(
    new Rectangle(100, 100, 300, 150),
    inkPaths: new[]
    {
        new double[] { 110, 120, 150, 140, 200, 110, 280, 130 },
    },
    color: new double[] { 0, 0, 0 },
    lineWidth: 2);
```

### Stamp

```csharp
page.Annotations.AddStampAnnotation(
    new Rectangle(400, 700, 550, 750),
    contents: "DRAFT",
    stampName: "Draft");
```

### Caret

```csharp
page.Annotations.AddCaretAnnotation(
    new Rectangle(200, 500, 210, 520),
    contents: "Insert text here");
```

### File attachment

```csharp
byte[] fileData = File.ReadAllBytes("attachment.txt");

page.Annotations.AddFileAttachmentAnnotation(
    new Rectangle(100, 50, 130, 80),
    contents: "Supporting document",
    fileName: "attachment.txt",
    fileData: fileData);
```

### Redact

```csharp
page.Annotations.AddRedactAnnotation(
    new Rectangle(100, 400, 300, 420),
    color: new double[] { 0, 0, 0 },   // overlay fill
    overlayText: "REDACTED");
```

## Watermark annotations

Watermarks are constructed separately and added via
`page.Annotations.Add(watermark)`. The watermark text and font are set via
`SetTextAndState`.

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Text;

var watermark = new WatermarkAnnotation(page, new Rectangle(100, 300, 500, 500));

watermark.SetTextAndState(
    new[] { "CONFIDENTIAL" },
    new TextState
    {
        FontSize        = 48f,
        ForegroundColor = Color.FromRgb(255, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Center,
    });

watermark.Characteristics.Rotate = Rotation.on270;

page.Annotations.Add(watermark);
```

## Removing annotations

```csharp
// 1-based remove
page.Annotations.Delete(1);

// 0-based remove
page.Annotations.RemoveAt(0);

// Remove every annotation
page.Annotations.Clear();
```

## Flattening annotations

```csharp
// Flatten a single annotation in place
page.Annotations[1].Flatten();

// Flatten everything via the facade
using Aspose.Pdf.Facades;

var editor = new PdfAnnotationEditor();
editor.BindPdf("input.pdf");
editor.FlatteningAnnotations();
editor.Save("flat.pdf");
```

## Using `PdfAnnotationEditor`

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Facades;

var editor = new PdfAnnotationEditor();
editor.BindPdf("input.pdf");

// Remove all annotations
editor.DeleteAnnotations();

// Remove all annotations of a specific subtype
editor.DeleteAnnotations("Highlight");

// Remove by /NM name
editor.DeleteAnnotation("annot1");

// Redact a rectangle on page 1
editor.RedactArea(
    pageIndex: 1,
    new Rectangle(100, 400, 300, 420),
    color: new double[] { 0, 0, 0 });

editor.Save("edited.pdf");
```
