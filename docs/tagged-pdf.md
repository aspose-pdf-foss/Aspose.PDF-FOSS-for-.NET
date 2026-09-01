# Tagged PDF

Tagged PDF adds a logical-structure tree on top of page content so screen
readers and other assistive tooling can present the document semantically.

## Reading tagged content

### Check whether a document is tagged

```csharp
using Aspose.Pdf;

using var doc = new Document("tagged.pdf");

Console.WriteLine($"Tagged: {doc.IsTagged}");
```

### Traverse the structure tree

`Document.StructTreeRoot` exposes the parsed structure tree:

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Tagged;

using var doc = new Document("tagged.pdf");

var root = doc.StructTreeRoot;
if (root is not null)
{
    foreach (var element in root.Children)
        PrintElement(element, indent: 0);
}

static void PrintElement(StructTreeElement element, int indent)
{
    var prefix = new string(' ', indent * 2);
    Console.WriteLine($"{prefix}<{element.StructureType}> " +
                      $"title='{element.Title}' alt='{element.AltText}'");

    foreach (var child in element.Children)
        PrintElement(child, indent + 1);
}
```

### Inspect role mappings

```csharp
var root = doc.StructTreeRoot;
if (root is not null)
{
    foreach (var (custom, standard) in root.RoleMap)
        Console.WriteLine($"  {custom} -> {standard}");
}
```

### Available read-only members

Each `StructTreeElement` exposes the element's role and accessibility metadata:

```csharp
StructTreeElement element = doc.StructTreeRoot!.Children[0];

string? type    = element.StructureType;     // /S role, e.g. "P", "Table"
string? title   = element.Title;             // /T
string? lang    = element.Language;           // /Lang
string? alt     = element.AltText;            // /Alt
string? actual  = element.ActualText;         // /ActualText
IReadOnlyDictionary<string, string>? attrs = element.Attributes;     // /A
IReadOnlyList<int> mcids = element.MarkedContentIds;                  // /K MCIDs
IReadOnlyList<StructTreeElement> children = element.Children;
```

### Find elements by type

`FindElements<T>` is available on the authoring tree exposed through
`Document.TaggedContent`, using the `Aspose.Pdf.LogicalStructure` element types:

```csharp
using Aspose.Pdf.LogicalStructure;

var rootElement = doc.TaggedContent.StructTreeRootElement;

var paragraphs = rootElement.FindElements<ParagraphElement>(recursive: true);
var figures    = rootElement.FindElements<FigureElement>(recursive: true);

foreach (var fig in figures)
{
    if (string.IsNullOrEmpty(fig.AlternativeText))
        Console.WriteLine("Warning: figure missing alt text");
}
```

## Creating tagged content

`Document.TaggedContent` returns an `ITaggedContent` that ensures the document
is marked as tagged and exposes factories for typed structure elements. The
factories return detached elements: attach each one with `AppendChild`, either
to `RootElement` (the `/Document` element, created on first access) or to
another element. On save the authored tree is written to `/StructTreeRoot`. For
a document built from scratch this way (no page content of its own), the text
set through `SetText` is also laid out onto pages on save, wrapped in marked
content that the structure elements point to; a document that already has page
content keeps it and only gains the structure tree.

```csharp
using Aspose.Pdf;

using var doc = new Document();
var tagged = doc.TaggedContent;

tagged.SetTitle("Accessible Report");
tagged.SetLanguage("en-US");

var root = tagged.RootElement;

var h1 = tagged.CreateHeaderElement(1);
h1.SetText("Annual Report 2024");
root.AppendChild(h1);

var para = tagged.CreateParagraphElement();
para.SetText("This report covers the fiscal year 2024.");
root.AppendChild(para);

var h2 = tagged.CreateHeaderElement(2);
h2.SetText("Financial Summary");
root.AppendChild(h2);

doc.Save("tagged-report.pdf");
```

### Available element factories

```csharp
// Headings (levels 1-6)
var h1 = tagged.CreateHeaderElement(1);
var h2 = tagged.CreateHeaderElement(2);

// Block text
var para = tagged.CreateParagraphElement();
var span = tagged.CreateSpanElement();

// Tables
var table  = tagged.CreateTableElement();
var thead  = tagged.CreateTableTHeadElement();
var tbody  = tagged.CreateTableTBodyElement();
var tfoot  = tagged.CreateTableTFootElement();
var tr     = tagged.CreateTableTRElement();
var th     = tagged.CreateTableTHElement();
var td     = tagged.CreateTableTDElement();

// Lists
var list  = tagged.CreateListElement();
var li    = tagged.CreateListLIElement();
var lbl   = tagged.CreateListLblElement();
var lbody = tagged.CreateListLBodyElement();

// Grouping
var part  = tagged.CreatePartElement();
var sect  = tagged.CreateSectElement();
var div   = tagged.CreateDivElement();
var bq    = tagged.CreateBlockQuoteElement();
var cap   = tagged.CreateCaptionElement();
var toc   = tagged.CreateTOCElement();
var toci  = tagged.CreateTOCIElement();
var index = tagged.CreateIndexElement();
var ns    = tagged.CreateNonStructElement();
var priv  = tagged.CreatePrivateElement();

// Inline
var quote = tagged.CreateQuoteElement();
var code  = tagged.CreateCodeElement();
var link  = tagged.CreateLinkElement();
var refe  = tagged.CreateReferenceElement();
var bib   = tagged.CreateBibEntryElement();
var ruby  = tagged.CreateRubyElement();
var wari  = tagged.CreateWarichuElement();

// Other
var figure  = tagged.CreateFigureElement();
var formula = tagged.CreateFormulaElement();
var form    = tagged.CreateFormElement();
var note    = tagged.CreateNoteElement();
var annot   = tagged.CreateAnnotElement();
var art     = tagged.CreateArtElement();
```

Every element is an `Aspose.Pdf.LogicalStructure.StructureElement`. Its
`StructureType` property is a `StructureTypeStandard` — the standard role the
element maps to (`StructureTypeStandard.P`, `.H1`, `.Table`, `.Ruby`, ...),
with a `Tag` string and a `Category` (`StructureTypeCategory.GroupingElements`,
`.BLSEs`, `.ILSEs`, `.IllustrationElements`). A custom role applied with
`SetTag` still reports the standard type it is role-mapped to; `S` returns the
raw `/S` tag.

Ruby and Warichu containers are created with `CreateRubyElement()` and
`CreateWarichuElement()`. Their content children — `RubyRBElement`,
`RubyRTElement`, `RubyRPElement` (base `RubyChildElement`) and
`WarichuWTElement`, `WarichuWPElement` (base `WarichuChildElement`) — are typed
when an existing tree is read, and are authored by tag through
`StructureTreeBuilder` (`ruby.CreateChild("RB")`, see below); `ITaggedContent`
has no factory for them.

### Figures with alt text

```csharp
var figure = tagged.CreateFigureElement();
figure.AlternativeText = "Bar chart showing quarterly revenue growth";
```

## Using `StructureTreeBuilder`

`StructureTreeBuilder` gives a fluent, builder-style construction for the
parent / marked-content side of the structure tree. Elements are created by
role tag, so it also covers roles without an `ITaggedContent` factory (the
Ruby `RB` / `RT` / `RP` and Warichu `WT` / `WP` children, for example).

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Tagged;

using var doc = new Document();
var page = doc.Pages.Add();
var builder = new StructureTreeBuilder(doc);

var docElement = builder.CreateElement("Document");
docElement.SetTitle("My Document");

var heading = docElement.CreateChild("H1");
heading.SetTitle("Introduction");
heading.SetAltText("Document introduction heading");

var paragraph = docElement.CreateChild("P");
paragraph.SetAltText("Introductory paragraph");

// Wire marked content on the page to the structure element
var mcInfo = paragraph.AddMarkedContent(page);
string beginMark = mcInfo.BeginMarkedContent();
string endMark   = mcInfo.EndMarkedContent();

// Register a custom-to-standard role mapping
builder.AddRoleMapping("CustomTag", "P");

// Flush the parent tree to the document
builder.BuildParentTree();

doc.Save("structured.pdf");
```

### Element-builder properties

```csharp
var section = builder.CreateElement("Section");
section.SetTitle("Chapter 1");
section.SetLanguage("en-US");
section.SetAltText("First chapter of the document");
section.SetActualText("Chapter 1: Getting Started");
```

### Nesting elements

```csharp
var section = builder.CreateElement("Sect");
var heading = section.CreateChild("H1");
heading.SetTitle("Section Title");

var para1 = section.CreateChild("P");
var para2 = section.CreateChild("P");

var subsection = section.CreateChild("Sect");
var subHeading = subsection.CreateChild("H2");
```

## Manipulating the structure tree

These operations work on the `Aspose.Pdf.LogicalStructure` authoring tree
reachable from `Document.TaggedContent` (the read-only `StructTreeElement`
view returned by `Document.StructTreeRoot` does not expose mutators).

### Reparent an element

```csharp
using Aspose.Pdf.LogicalStructure;

var rootElement = doc.TaggedContent.StructTreeRootElement;
StructureElement element   = rootElement.ChildElements[0];
StructureElement newParent = rootElement.ChildElements[1];

element.ChangeParentElement(newParent, validate: true);
```

### Remove an element

```csharp
// Detach the element and re-parent its children under its former parent
element.RemoveAndMoveItsChildObjectsToItsParent(validate: true);
```

### Adjusting element positioning

`AdjustPosition` accepts an `Aspose.Pdf.Tagged.PositionSettings` instance. Of
its members, `Margin`, `IsInNewPage` and `IsInLineParagraph` are read when the
authored tree is laid out on save (left/right margins indent the block,
top/bottom add space around it, `IsInNewPage` starts a new page,
`IsInLineParagraph` flows the element inline). `HorizontalAlignment`,
`VerticalAlignment`, `IsFirstParagraphInColumn` and `IsKeptWithNext` are stored
on the settings object but not applied.

```csharp
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tagged;

element.AdjustPosition(new PositionSettings
{
    Margin      = new MarginInfo(10, 10, 10, 10),
    IsInNewPage = true,
});
```
