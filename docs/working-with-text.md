# Working with Text

## Extracting text

### From a single page

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

using var doc = new Document("input.pdf");
var absorber = new TextAbsorber();
absorber.Visit(doc.Pages[1]);  // pages are 1-based

Console.WriteLine(absorber.Text);
```

### From the entire document

```csharp
var absorber = new TextAbsorber();
absorber.Visit(doc);

Console.WriteLine(absorber.Text);
```

### Extraction modes

`TextExtractionOptions.TextFormattingMode`:

```csharp
using Aspose.Pdf.Text;

// Pure mode (default) — line-sort by Y, fixed-width column padding
var pure = new TextAbsorber(new TextExtractionOptions(
    TextExtractionOptions.TextFormattingMode.Pure));

// Raw mode — text in content-stream order, no line sorting
var raw = new TextAbsorber(new TextExtractionOptions(
    TextExtractionOptions.TextFormattingMode.Raw));
```

### Using TextDevice

`TextDevice` is an alternative that returns per-page text into a stream:

```csharp
using Aspose.Pdf.Devices;

// TextDevice writes using its Encoding property, which defaults to
// Encoding.Unicode (UTF-16). Decode with the same encoding, or set
// device.Encoding = System.Text.Encoding.UTF8 before calling Process.
var device = new TextDevice();
using var ms = new MemoryStream();
device.Process(doc.Pages[1], ms);
string pageText = device.Encoding.GetString(ms.ToArray());

// Or take the string directly (per page or for the whole document)
string direct = device.Process(doc.Pages[1]);
string all    = device.Process(doc);
```

`TextDevice` also has a `Process(Page, string outputFileName)` overload and
accepts a `TextExtractionOptions` in its constructor, so the Pure / Raw modes
above apply to it as well.

## Searching text

### By literal phrase

```csharp
var absorber = new TextFragmentAbsorber("invoice");
absorber.Visit(doc.Pages[1]);

foreach (var fragment in absorber.TextFragments)
{
    Console.WriteLine($"Found '{fragment.Text}' at {fragment.Rectangle}");
    Console.WriteLine($"  Font: {fragment.TextState.FontName}, Size: {fragment.TextState.FontSize}");
}
```

### By regex

```csharp
// Pass `isRegex: true` on the literal-overload, or construct from System.Text.RegularExpressions.Regex
var byString = new TextFragmentAbsorber(@"\d{3}-\d{2}-\d{4}", isRegex: true);

var regex = new System.Text.RegularExpressions.Regex(@"\d{3}-\d{2}-\d{4}");
var byRegex = new TextFragmentAbsorber(regex);

byString.Visit(doc);
foreach (var f in byString.TextFragments)
    Console.WriteLine($"SSN-like match: {f.Text}");
```

### Search options

```csharp
var options = new TextSearchOptions
{
    IsRegularExpression = true,
    CaseSensitive       = false,
    WholeWord           = true,
    Rectangle           = new Rectangle(0, 0, 300, 500),  // limit to this area
};

var absorber = new TextFragmentAbsorber(@"\btotal\b", options);
absorber.Visit(doc.Pages[1]);
```

`TextSearchOptions` also exposes `ExcludeRectangles` (areas to skip),
`LimitToPageBounds`, `SearchInAnnotations` (include annotation appearance
text), `IgnoreShadowText`, and `IgnoreResourceFontErrors`. `CaseSensitive`
defaults to `true`.

### Across all pages

```csharp
var absorber = new TextFragmentAbsorber("confidential");
absorber.Visit(doc);

Console.WriteLine($"Hits: {absorber.TextFragments.Count}");
```

## Replacing text

### Simple replacement

```csharp
var absorber = new TextFragmentAbsorber("old company name");
absorber.Visit(doc);

foreach (var fragment in absorber.TextFragments)
    fragment.Text = "new company name";

doc.Save("updated.pdf");
```

### Replace with style changes

`TextState.ForegroundColor` is `Aspose.Pdf.Color`. `Color.FromRgb(r, g, b)`
takes **0..1 doubles** (values above 1 are clamped); `Color.FromArgb(r, g, b)`
takes 0..255 integers, and `Color.FromRgb(System.Drawing.Color)` converts a GDI
colour.

```csharp
var absorber = new TextFragmentAbsorber("DRAFT");
absorber.Visit(doc);

foreach (var fragment in absorber.TextFragments)
{
    fragment.Text = "FINAL";
    fragment.TextState.ForegroundColor = Color.FromRgb(0, 0.5, 0);   // green
    fragment.TextState.IsBold = true;
}
```

## Adding text

### `TextBuilder`

```csharp
var builder = new TextBuilder(doc.Pages[1]);

var fragment = new TextFragment("Added text");
fragment.Position = new Position(100, 700);
fragment.TextState.FontSize = 14f;       // FontSize is float
fragment.TextState.FontName = "Helvetica";

builder.AppendText(fragment);
```

### Multi-line via `TextParagraph`

```csharp
var builder   = new TextBuilder(doc.Pages[1]);
var paragraph = new TextParagraph
{
    Position  = new Position(72, 700),
    Rectangle = new Rectangle(72, 500, 540, 700),
};

paragraph.AppendLine("First line of text");
paragraph.AppendLine("Second line with custom style", new TextState
{
    FontSize = 16f,
    IsBold   = true,
});
paragraph.AppendLine("Third line");

builder.AppendParagraph(paragraph);
```

### Text styling

```csharp
var fragment = new TextFragment("Styled text");
var s = fragment.TextState;
s.FontSize         = 18f;
s.FontName         = "Times-Roman";
s.IsBold           = true;
s.IsItalic         = true;
s.IsUnderline      = true;
s.ForegroundColor  = Color.FromRgb(1, 0, 0);      // red
s.BackgroundColor  = Color.FromRgb(1, 1, 0);      // yellow highlight
s.CharacterSpacing = 1.5f;
s.WordSpacing      = 3f;
```

`TextFragment.TextState` is a `TextFragmentState` (a `TextState` subclass with extras like `TabStops`, `DrawTextRectangleBorder`, and `Font` typed as `Aspose.Pdf.Text.Font`). For most authoring you can treat it like a `TextState`.

## Paragraph extraction

```csharp
var absorber = new ParagraphAbsorber();
absorber.Visit(doc);

foreach (var markup in absorber.PageMarkups)
{
    Console.WriteLine($"Page {markup.Number}: {markup.Sections.Count} section(s)");
    foreach (var section in markup.Sections)
        foreach (var paragraph in section.Paragraphs)
            Console.WriteLine($"  {paragraph.Text}");
}
```

## Tab stops

```csharp
var tabs = new TabStops();
tabs.Add(200f);
var rightTab = tabs.Add(400f);
rightTab.AlignmentType = TabAlignmentType.Right;
rightTab.LeaderType    = TabLeaderType.Dot;

var fragment = new TextFragment(tabs);
fragment.Segments.Add(new TextSegment("Item"));
fragment.Segments.Add(new TextSegment("\t"));
fragment.Segments.Add(new TextSegment("$100.00"));

new TextBuilder(doc.Pages[1]).AppendText(fragment);
```

## Notes on the absorbers

- `TextAbsorber` produces line-sorted plain text per page (or across the document). Its `Text` property has trailing line breaks removed; Raw mode also strips leading whitespace, while Pure mode keeps a first line's leading column padding.
- `TextFragmentAbsorber` returns a `TextFragmentCollection` (`Count`, 1-based indexer) of `TextFragment` instances, each carrying `Text`, `Rectangle`, `Position`, `TextState`, `Page`, `Segments`. Best for find / replace flows. A `TextEditOptions` passed to the constructor controls what happens when the replacement text contains glyphs the original font lacks (`NoCharacterAction.ReplaceFonts` substitutes a covering face from the registered font sources).
- `ParagraphAbsorber` groups text into sections and paragraphs based on layout.
- `TableAbsorber` produces `AbsorbedTable` rows / cells from tabular content — see [Working with Tables](working-with-tables.md).
- `ImagePlacementAbsorber` extracts placed images with their page-space rectangles.
