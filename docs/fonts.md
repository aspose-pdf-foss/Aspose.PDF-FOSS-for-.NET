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

`FontRepository.FindFont` resolves a font by name, searching the Standard 14
set, then the registered [font sources](#registering-font-folders) (which by
default include the installed system fonts), then the library's built-in faces.
It throws `FontNotFoundException` when nothing resolves; `FindFont(name,
ignoreCase: true)` relaxes the name match. Assigning the resulting `Font` to
`TextState.Font` embeds it in the document:

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

Embedding honours the face's own licence: a TrueType/OpenType font whose OS/2
`fsType` forbids embedding raises `FontEmbeddingException` at save time. Set
`Document.DisableFontLicenseVerifications = true` to embed it anyway, or clear
`font.FontOptions.NotifyAboutFontEmbeddingError` to let the save finish with
the face referenced by name only; the reason stays readable through
`font.GetLastFontEmbeddingError()`.

## Loading a font from a file or stream

`FontRepository.OpenFont` loads a font that isn't installed on the machine:

```csharp
using Aspose.Pdf.Text;

// From a file
Font font = FontRepository.OpenFont(@"C:\fonts\MyFont.ttf");

// From a stream (FontTypes.TTF or FontTypes.OTF)
using var fs = File.OpenRead("MyFont.otf");
Font otf = FontRepository.OpenFont(fs, FontTypes.OTF);

// Type 1: a .pfb program plus its .afm metrics
Font type1 = FontRepository.OpenFont(@"C:\fonts\MyFont.pfb", @"C:\fonts\MyFont.afm");

var fragment = new TextFragment("Custom font") { TextState = { Font = font } };
```

## Registering font folders

Add directories, files, or in-memory font programs to `FontRepository.Sources`
so `FindFont` can resolve fonts that live outside the system font path:

```csharp
using Aspose.Pdf.Text;

FontRepository.Sources.Add(new FolderFontSource(@"C:\app\fonts"));
FontRepository.Sources.Add(new FileFontSource(@"C:\app\fonts\Brand.ttf"));
FontRepository.Sources.Add(new MemoryFontSource(File.ReadAllBytes("Brand-Bold.ttf")));

var brand = FontRepository.FindFont("Brand");
```

The collection starts with a `SystemFontSource` (the platform's installed fonts
— Windows, `/usr/share/fonts`, and the macOS font folders) and the per-user
fonts folder. `FontRepository.ReloadFonts()` resets it to that default.

## Substituting missing fonts

`FontRepository.Substitutions` is a registry of `FontSubstitution` rules
(`Add`, `Remove`, `Delete`, `Clear`, `Count`). `SimpleFontSubstitution` maps one
font name to another; derive from `CustomFontSubstitutionBase` and override
`TrySubstitute` for custom logic:

```csharp
using Aspose.Pdf.Text;

var rule = new SimpleFontSubstitution("MissingFont", "Helvetica");
FontRepository.Substitutions.Add(rule);

var spec = new CustomFontSubstitutionBase.OriginalFontSpecification("MissingFont", isEmbedded: false);
if (rule.TrySubstitute(spec, out Font? fallback))
    Console.WriteLine($"Use {fallback!.FontName}");
```

The registry is not consulted automatically: `FindFont` and text extraction do
not read it, so apply a rule's `TrySubstitute` result yourself. The automatic
substitution the library does perform is glyph-coverage based — when a
replacement text contains characters the current font lacks and
`TextEditOptions.NoCharacterAction.ReplaceFonts` is in effect, a covering face
is chosen from the registered sources (caller-registered folders first, then the
system fonts).

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

`Font.Save(stream)` writes the raw font program to a stream. It uses, in order,
the data loaded via `FontRepository.OpenFont` / `FindFont`, the program embedded
in the source PDF (for a font returned by `GetAllFonts()` or `Page.Fonts`), or
the installed system face that resolves by name. When none of these is
available it writes nothing and `GetLastFontEmbeddingError()` reports why:

```csharp
var font = FontRepository.OpenFont(@"C:\fonts\MyFont.ttf");

using var outFs = File.Create("copy.ttf");
font.Save(outFs);
if (outFs.Length == 0)
    Console.WriteLine(font.GetLastFontEmbeddingError());
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

- Font **rasterisation hints**: glyphs are rendered from their unhinted
  outlines.
- Fonts whose licence forbids embedding are not embedded unless
  `Document.DisableFontLicenseVerifications` is set (see
  [Finding and embedding fonts](#finding-and-embedding-fonts)).
- See [Scope and Limitations](../README.md#scope-and-limitations) in the README
  for the library-wide list.
