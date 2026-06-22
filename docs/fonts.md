# Fonts

The library ships pure-managed parsers and writers for the **Standard 14**
fonts plus embedded **TrueType / OpenType**, **Type 1 (PostScript)**, and
**Type 3** fonts. You can set fonts on new text, embed and subset them, register
custom font folders, substitute missing fonts, and inspect the fonts already in
a document.

The font APIs live in `Aspose.Pdf.Text` (`Font`, `FontRepository`, `FontStyles`,
`FontTypes`).

## Standard 14 fonts

The 14 fonts every PDF viewer guarantees are referenced **by name** — no
embedding needed. Set `TextState.FontName` to one of:

```
Helvetica, Helvetica-Bold, Helvetica-Oblique, Helvetica-BoldOblique,
Times-Roman, Times-Bold, Times-Italic, Times-BoldItalic,
Courier, Courier-Bold, Courier-Oblique, Courier-BoldOblique,
Symbol, ZapfDingbats
```

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

var fragment = new TextFragment("Standard 14 — no embedding required");
fragment.TextState.FontName = "Times-Roman";
fragment.TextState.FontSize = 14f;   // FontSize is float
```

`IsBold` / `IsItalic` pick the matching style of the current family:

```csharp
fragment.TextState.IsBold = true;
fragment.TextState.IsItalic = true;
```

## Finding and embedding fonts

`FontRepository.FindFont` resolves a font by name (searching the Standard 14 set
and any registered [font sources](#registering-font-folders)). Assigning the
resulting `Font` to `TextState.Font` embeds it in the document:

```csharp
using Aspose.Pdf.Text;

var font = FontRepository.FindFont("Arial");
var fragment = new TextFragment("Embedded Arial")
{
    TextState = { Font = font, FontSize = 12f }
};
```

Resolve a specific style:

```csharp
var bold = FontRepository.FindFont("Arial", FontStyles.Bold);
var boldItalic = FontRepository.FindFont("Arial", FontStyles.Bold | FontStyles.Italic);
```

> Setting `TextState.FontName` (a string) references a font by name; setting
> `TextState.Font` (a `Font` object) embeds the actual font program.

## Loading a font from a file or stream

`FontRepository.OpenFont` loads a font that isn't installed on the machine:

```csharp
using Aspose.Pdf.Text;

// From a file
Font font = FontRepository.OpenFont(@"C:\fonts\MyFont.ttf");

// From a stream (FontTypes.TTF or FontTypes.OTF)
using var fs = File.OpenRead("MyFont.otf");
Font otf = FontRepository.OpenFont(fs, FontTypes.OTF);

var fragment = new TextFragment("Custom font") { TextState = { Font = font } };
```

## Registering font folders

Add directories or files to `FontRepository.Sources` so `FindFont` can resolve
fonts that live outside the system font path:

```csharp
using Aspose.Pdf.Text;

FontRepository.Sources.Add(new FolderFontSource(@"C:\app\fonts"));
FontRepository.Sources.Add(new FileFontSource(@"C:\app\fonts\Brand.ttf"));

var brand = FontRepository.FindFont("Brand");
```

## Substituting missing fonts

When a document references a font that can't be resolved, register a
substitution so a fallback is used instead:

```csharp
using Aspose.Pdf.Text;

FontRepository.Substitutions.Add(
    new SimpleFontSubstitution("MissingFont", "Helvetica"));
```

## Inspecting the fonts in a document

`Document.FontUtilities.GetAllFonts()` returns every font referenced by any
page; each `Font` exposes its name and embedding state:

```csharp
using Aspose.Pdf;

using var doc = new Document("input.pdf");

foreach (var font in doc.FontUtilities.GetAllFonts())
{
    Console.WriteLine($"{font.FontName} (base: {font.BaseFont}, {font.Subtype})");
    Console.WriteLine($"  embedded: {font.IsEmbedded}, subset: {font.IsSubset}, CID: {font.IsCid}");
}
```

The fonts used by a single page are on `Page.Fonts`:

```csharp
foreach (var font in doc.Pages[1].Fonts)
    Console.WriteLine(font.FontName);
```

`Font.Save(stream)` writes the raw font program to a stream, but only for a
`Font` that was loaded with embeddable data via `FontRepository.OpenFont`
(or `FindFont`). Fonts returned by `GetAllFonts()` are document-dictionary
views and do not carry that data, so `Save` writes nothing for them:

```csharp
var font = FontRepository.OpenFont(@"C:onts\MyFont.ttf");

using var outFs = File.Create("copy.ttf");
font.Save(outFs);   // writes nothing when no embeddable data is available
```

## Subsetting embedded fonts

Subsetting drops unused glyphs from embedded fonts, shrinking the file. Run it
before saving:

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

using var doc = new Document("input.pdf");
doc.FontUtilities.SubsetFonts(FontSubsetStrategy.SubsetAllFonts);
doc.Save("subset.pdf");
```

`FontSubsetStrategy.SubsetEmbeddedFontsOnly` restricts subsetting to fonts that
are already embedded. See [Optimization](optimization.md) for the broader
size-reduction pipeline.

## What's not included

- Font **rasterisation hints** and platform font enumeration beyond the
  registered sources.
- See [What's not included](../README.md#whats-not-included-vs-asposepdf-for-net)
  in the README for the full list of Aspose.PDF for .NET-only features.
