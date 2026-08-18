# Aspose.PDF FOSS for .NET

[![NuGet version](https://img.shields.io/nuget/v/Aspose.PDF.FOSS.svg)](https://www.nuget.org/packages/Aspose.PDF.FOSS/) [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE) [![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](src/Aspose.Pdf.Foss.csproj)

[![Aspose.PDF FOSS for .NET](https://raw.githubusercontent.com/aspose-pdf-foss/Aspose.PDF-FOSS-for-.NET/main/docs/media/banner-readme.png)](https://products.aspose.org/pdf/net/)

Aspose.PDF FOSS for .NET is a free, open-source PDF library for .NET 8+ that reads, creates,
modifies, and converts PDF documents. It provides an Aspose.PDF-compatible API surface for common
PDF scenarios, so a large part of existing Aspose.PDF for .NET code can compile and run against it
unchanged.

## Navigation

- [At a Glance](#at-a-glance)
- [Key Capabilities](#key-capabilities)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Additional Examples](#additional-examples)
- [API Reference](#api-reference)
- [Documentation & Resources](#documentation--resources)
- [Scope and Limitations](#scope-and-limitations)
- [Development and Testing](#development-and-testing)
- [License](#license)

## At a Glance

![Aspose.PDF FOSS for .NET at a glance](https://raw.githubusercontent.com/aspose-pdf-foss/Aspose.PDF-FOSS-for-.NET/main/docs/media/at-a-glance.png)

## Key Capabilities

- Open an existing PDF or create one from scratch with `Document`, then add, delete, reorder,
  rotate, resize, or copy pages across documents through `Page` and `PageCollection`; full
  round-trip save and incremental save preserve bookmarks and outlines, page labels, named
  destinations, document info, XMP metadata, and optional content (OCG) layers.
- Standard 14 font handling, TrueType / OpenType embedding and subsetting, and Type 1 (PostScript)
  and Type 3 font parsing are handled through `FontRepository` and `FontEmbedder`.
- Extract text with `TextAbsorber`, `TextFragmentAbsorber`, `ParagraphAbsorber`, and
  `TableAbsorber`; find and replace across pages — including cross-operator and ligature-aware
  spans — and build new text with `TextBuilder`, `TextFragment`, `TextSegment`, `TextStamp`, and
  `FormattedText`, with Bidi (RTL) support and AGL Unicode mapping.
- Read, fill, and build AcroForm fields — text, checkbox, radio, choice, button, signature, and
  rich text — through `Form`, `Field`, `FormFieldBuilder`, `FormEditor`, and `FormDataConverter`,
  including field-level flatten and JSON/XFDF form-data import and export.
- Add and edit every standard annotation type — link, text, free-text, highlight,
  square/circle/line/polyline, ink, stamp, popup, file attachment, screen, redaction, watermark,
  pre-press marks, and rich media — with appearance generation and flatten.
- Encrypt and decrypt with RC4-40, RC4-128, AES-128, and AES-256, and sign documents with PKCS#7 /
  CMS detached signatures — including DocMDP certifying signatures — through `PdfFileSecurity`,
  `PdfFileSignature`, `PdfSigner`, and `PdfCertificate`; document permissions can be read and set
  directly on `Document`.
- Render pages to PNG, JPEG, BMP, TIFF, or SVG with `PngDevice`, `JpegDevice`, `BmpDevice`,
  `TiffDevice`, and `SvgDevice` through the pluggable `IPageRenderer` (default
  `SoftwarePageRenderer`), with mesh shading types 4-7 (free-form and lattice-form Gouraud,
  Coons, and tensor-product patch meshes), a JBIG2 decoder, and JPEG / JPEG2000 / CCITT-Fax
  filters.
- Convert PDF to and from HTML and SVG, load Markdown, XML, or plain-text (TXT) files, and export
  to Markdown, plain text, or PDF/A (1A through 4F), using `HtmlLoadOptions` / `HtmlSaveOptions`,
  `SvgLoadOptions`, `MdLoadOptions`, and `TxtLoadOptions`.
- High-level, task-oriented facades cover common workflows: `PdfFileEditor` for
  concatenate/split/resize/extract, `PdfBookmarkEditor` for outline CRUD with XML/HTML export,
  `PdfContentEditor` for annotations/stamps/replace-text, `PdfFileStamp` for header/footer/
  page-number stamping, plus `PdfFileInfo`, `PdfFileMend`, `PdfAnnotationEditor`, `PdfPageEditor`,
  `PdfConverter`, `PdfExtractor`, and `PdfViewer`.
- Read an existing `/StructTreeRoot` tree and walk its `Aspose.Pdf.LogicalStructure` element
  hierarchy, or build tagged content from scratch with `ITaggedContent`'s 37 typed structure
  elements, setting document language, title, alternate text, and marked content for
  accessibility.
- Compare two documents or two pages: `SideBySidePdfComparer` writes a result PDF that sets both
  versions side by side with deletions marked on the left and insertions on the right, with
  whitespace handling, comparison / exclusion areas, and marker colours set through
  `SideBySideComparisonOptions`; `GraphicalPdfComparer` diffs the rendered pixels of two pages
  (Windows only), and the `Aspose.Pdf.Comparison.Diff` model sits under both.
- Extract tabular data and build tables with `Table`, `Row`, `Cell`, and `TableAbsorber`; draw
  with `Graph`, `Rectangle`, `Circle`, `Ellipse`, `Arc`, `Line`, and `Curve`; and extract or place
  images through `ImagePlacementAbsorber` and `XImageCollection`.

## Installation

This library publishes prebuilt binaries to NuGet as
[`Aspose.PDF.FOSS`](https://www.nuget.org/packages/Aspose.PDF.FOSS/):

```bash
dotnet add package Aspose.PDF.FOSS --version 26.8.0
```

Or with a `<PackageReference>`:

```xml
<PackageReference Include="Aspose.PDF.FOSS" Version="26.8.0" />
```

The library targets **.NET 8.0** and pulls in one NuGet dependency, `System.Drawing.Common`
(8.0.0), used only by a few image-interop members (e.g. `ImageDevice.GetBitmap`,
`StampInfo.Image`). Those specific members are Windows-only on .NET 8 and throw
`PlatformNotSupportedException` on Linux/macOS — use the byte-array / `Stream` overloads instead,
or read raw pixel data directly from the renderer's `RgbaBuffer`. Everything else — text, forms,
parsing, encryption and signing, and page-to-image rendering via `PngDevice`/`JpegDevice` (backed
by the built-in managed `SoftwarePageRenderer`, used automatically off Windows) — is pure-managed
and runs on Windows, Linux, and macOS.

To build from source instead:

```bash
git clone https://github.com/aspose-pdf-foss/Aspose.PDF-FOSS-for-.NET.git
cd Aspose.PDF-FOSS-for-.NET
dotnet build src/Aspose.Pdf.Foss.csproj -c Release
```

Then reference `src/Aspose.Pdf.Foss.csproj` from your project (e.g. `dotnet add reference`), or
add the built `Aspose.Pdf.Foss.dll` as an assembly reference.

## Dependencies

### Required Package Dependencies

- `System.Drawing.Common` (8.0.0) — used only by a few image-interop members (e.g.
  `ImageDevice.GetBitmap`, `StampInfo.Image`). Those specific members are Windows-only on .NET 8
  and throw `PlatformNotSupportedException` on Linux/macOS; everything else in the library is
  pure-managed and cross-platform.

### Optional Dependencies

No optional third-party package dependencies.

### Native and System Requirements

- .NET 8.0 or later, running on Windows, Linux, or macOS.

### Development Dependencies

No development-only third-party package dependencies beyond the standard .NET SDK/test tooling
used to build and run the test suite (see [Development and Testing](#development-and-testing)).

## Quick Start

Create a new PDF, then open an existing one and extract its text:

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

// Create a new PDF document
using var doc = Document.Create();
var page = doc.Pages.Add();
var fragment = new TextFragment("Hello, World!");
fragment.TextState.FontSize = 24;
page.Paragraphs.Add(fragment);
doc.Save("output.pdf");

// Open an existing PDF and extract its text
using var existing = Document.Open("input.pdf");
var absorber = new TextAbsorber();
absorber.Visit(existing.Pages[1]);
Console.WriteLine(absorber.Text);
```

## Additional Examples

More real, runnable examples, adapted from the library's own test suite.

### Add a Link Annotation With a URI Action

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Annotations;

using var doc = Document.Open("input.pdf");
var page = doc.Pages[1];

var action = PdfAction.CreateUri("https://example.org");
page.Annotations.AddLinkAnnotation(new Rectangle(10, 10, 100, 30), action);

doc.Save("output.pdf");
```

<details>
<summary>View Additional Examples</summary>

### Find Matching Text

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

using var doc = Document.Open("input.pdf");
var absorber = new TextFragmentAbsorber("Hello");
absorber.Visit(doc.Pages[1]);
Console.WriteLine($"Found {absorber.TextFragments.Count} matches");
```

### Add a Stamp Annotation

```csharp
using Aspose.Pdf;

using var doc = Document.Open("input.pdf");
doc.Pages[1].Annotations.AddStampAnnotation(
    new Rectangle(100, 700, 200, 750), "Approved!", "Approved");
doc.Save("output.pdf");
```

### Inspect a Choice Field's Options

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Forms;

using var doc = Document.Open("form.pdf");
var field = (ChoiceField)doc.Form!.Fields[0];
var options = field.Options;
for (int i = 1; i <= options.Count; i++)
    Console.WriteLine($"{options[i].ExportValue}: {options[i].DisplayValue}");
```

### Render a Page to SVG

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Devices;

using var doc = Document.Open("input.pdf");
var device = new SvgDevice();
string svg = device.Process(doc.Pages[1]);
File.WriteAllText("page1.svg", svg);
```

### Find and Replace Text

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Text;

using var doc = Document.Open("input.pdf");
var absorber = new TextFragmentAbsorber("old text");
absorber.Visit(doc);

foreach (var fragment in absorber.TextFragments)
    fragment.Text = "new text";

doc.Save("output.pdf");
```

### List Form Field Values

```csharp
using Aspose.Pdf;

using var doc = Document.Open("form.pdf");
foreach (var field in doc.Form!.Fields)
    Console.WriteLine($"{field.FullName} = {field.Value}");
```

### Encrypt a PDF

```csharp
using Aspose.Pdf;

using var doc = Document.Open("input.pdf");
doc.Encrypt("user-pw", "owner-pw", Permissions.PrintDocument, CryptoAlgorithm.AESx256);
doc.Save("encrypted.pdf");
```

### Convert HTML to PDF

```csharp
using Aspose.Pdf;

using var doc = Document.Open("input.html", new HtmlLoadOptions());
doc.Save("output.pdf");
```

### Render a Page to PNG

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Devices;

using var doc = Document.Open("input.pdf");
var device = new PngDevice(new Resolution(300));
device.Process(doc.Pages[1], "page1.png");
```

### Sign a PDF

```csharp
using Aspose.Pdf.Facades;
using Aspose.Pdf.Forms;

using var signer = new PdfFileSignature();
signer.BindPdf("input.pdf");
var sig = new PKCS7("my.pfx", "pfx-password");
signer.Sign(1, "reason", "contact", "location", true,
    new System.Drawing.Rectangle(100, 100, 200, 60), sig);
signer.Save("signed.pdf");
```

</details>

## API Reference

The primary entry point is `Document`, which exposes its pages through `Document.Pages` (a
`PageCollection` of `Page` objects), its interactive form through `Document.Form`, and its
security surface through `PdfFileSecurity` and `PdfFileSignature`. The public API surface includes
854 public types across 36 modules, summarized in the module-grouped tables below.

<details>
<summary>View the Core API Surface</summary>

### Core API

| Class | Description |
|---|---|
| `BaseOperatorCollection` | Standalone operator-list class — surface mirrors OperatorCollection but is detached from any page. |
| `BuildVersionInfo` | Build and version information for the library. |
| `CollectionItem` | Per-file metadata entries declared by a portfolio /Collection's schema, exposed as a typed dictionary on CollectionItem. |
| `ContentsAppender` | Buffers operators to prepend to / append to a page's content stream. |
| `Document` | Represents a PDF document. |
| `DocumentCollection` | Enumerable collection of `Document` instances; `GetEnumerator()` throws `NotImplementedException` in this edition (see Scope and Limitations). |
| `DocumentInfo` | Represents the document information dictionary. |
| `EmbeddedFileCollection` | Collection of embedded files in a document. |
| `EncryptedPayload` | Wraps the /EP entry on an embedded-file file-spec — the encrypted-payload that signals a PDF 2.0 unencrypted-wrapper document. |
| `EngineDocFacade` | Wrapper providing the small slice of engine-level state callers may set via `doc._engineDoc.X`. |
| `ExtGStateValue` | One /ExtGState entry — name plus stroke/fill alpha factors. |
| `FileParams` | Wraps the /Params dict on an embedded-file stream (PDF §7.11.3 Table 46). |
| `FileSpecification` | Represents an embedded file specification (PDF §7.11.3). |
| `Group` | Group / blending colour space dictionary (PDF 32000 §11.6.5). |
| `Image` | Image compositing can be customized using the BlendMode enumeration, which includes common blend operations such as Multiply and Screen. |
| `ImagePlacement` | Represents an image placement found on a PDF page — the position, size, and resolution of an image XObject as it appears on the page (after CTM transformation). |
| `ImagePlacementAbsorber` | Absorbs image placement information from PDF pages. |
| `ImagePlacementCollection` | A collection of ImagePlacement items. |
| `ImageStamp` | Adds an image to a PDF page. |
| `InterruptMonitor` | Class with 1 method. |
| `JavaScriptCollection` | Document-level JavaScript scripts (PDF spec §12.6.4.16). |
| `LaunchActionOperationConverter` | LaunchActionOperationConverter converts a LaunchActionOperation value to its string representation via ToString, exposing the strings strOpen and strPrint. |
| `Layer` | A PDF layer (Optional Content Group) as exposed through Layers. |
| `MergeOptions` | Merge tuning knobs honored by Merge(MergeOptions, Document[]) and friends. |
| `OcspSettings` | OCSP (Online Certificate Status Protocol) settings for the signer's revocation-check / OCSP-stapling path. |
| `Operator-Pdf` | Top-level base for all PDF content-stream operators. |
| `OperatorCollection` | Collection of content stream operators for a page. |
| `Opi` | Open Prepress Interface (OPI) metadata wrapper for an XForm. |
| `OptimizationOptions-Pdf` | Document-scoped alias for OptimizationOptions. |
| `Page` | Represents a page in a PDF document. |
| `PageActionCollection` | Per-page additional actions (PDF 32000 §12.6.3 /AA entries). |
| `PageExtensions` | Extension methods over Page that manipulate the page content stream. |
| `PageInfo` | Container for page dimension and margin properties. |
| `PageResources` | Provides access to a page's resource collections (fonts, images). |
| `PageTransition` | Represents a page transition effect (PDF32000 §12.4.4). |
| `RawOperator` | An unparsed operator token from a content stream — used as a fallback for operators not covered by the typed Operators hierarchy. |
| `RepairOptions` | Options describing what repair is needed. |
| `Resolution` | Represents the resolution (DPI) for device rendering and image placement. |
| `Resources` | Type alias for PageResources, matching the Resources class name. |
| `Stamp-Pdf` | Top-level Aspose.Pdf-shape abstract base for stamps applied to a Page via AddStamp(Stamp). |
| `TextStamp-Pdf` | Compat re-export so external tests that reference Aspose.Pdf.TextStamp (from before the type moved to Aspose.Pdf.Stamps) keep compiling. |
| `TimestampSettings` | RFC 3161 Time-Stamp Authority (TSA) settings consumed by the signer when embedding a timestamp token into the PKCS#7 envelope. |
| `Value` | One typed value pulled out of a CollectionItem dict by TryGet*Value. |
| `WarningInfo` | Class with 2 methods and 3 properties. |
| `Watermark` | Watermark applied to a page. |
| `XForm` | Represents a Form XObject (reusable content stream with its own resources). |
| `XFormCollection` | Collection of XForm (Form XObject) resources on a page. |
| `XFormResources` | Resources (fonts, xobjects) on an XForm's stream dict. |

#### Interfaces

| Interface | Description |
|---|---|
| `IDocumentFontUtilities` | Per-document font helper contract — implemented by FontUtilities. |
| `IOperatorSelector` | Visitor interface for Accept(IOperatorSelector). |
| `IWarningCallback` | Interface with 1 method. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `AFRelationship` | Relationship between an embedded file and the document content that references it (/AFRelationship, PDF 2.0 §7.11.3). |
| `FileEncoding` | Compression / encoding used for the embedded file stream's data (consumed by Encoding). |
| `Fixup` | Pre-defined PDF fixup operations accepted by Stream, bool, object[]). |
| `ImageDeleteAction` | Action taken by Delete(int, ImageDeleteAction) when the image being removed is still referenced from the page content. |
| `ImageFileType` | Recognised image-source file types reported by Image.FileType. |
| `ImageFilterType` | ImageFilterType enum defines the compression filters `Jpeg2000`, `Flate`, `Jpeg`, and `CCITTFax` that can be applied when re‑encoding images. |
| `LaunchActionOperation` | LaunchActionOperation enum defines the possible launch operations: None, Open, and Print. |
| `NoCharacterAction-Pdf` | Action taken when the configured font does not have a glyph for a character in the stamp text. |
| `PasswordType` | Identifies which password (if any) is in effect on an encrypted PDF. |
| `PdfAStandardVersion` | PdfAStandardVersion enum lists supported PDF/A conformance levels, including PDF_A_1A, PDF_A_2B, PDF_A_3U, and PDF_A_4. |
| `Permissions` | Permissions.PrintDocument, ModifyContent, ExtractContent, etc., are enum values that can be combined to set document security settings. |
| `PrintScaling` | Enum with 2 members. |
| `ReturnAction` | The ReturnAction enum is used by RichMediaAnnotation.Accept(visitor) to indicate whether the visitor should Continue processing or Abort. |
| `Rotation` | Enum with 5 members. |
| `TabOrder` | Tab order applied to widget annotations on a page (PDF 32000 /Tabs entry). |
| `WarningType` | Categories of warnings emitted during save/load. |

### Actions

| Class | Description |
|---|---|
| `ActionCollection` | Collection of actions associated with an annotation. |
| `GoToAction` | Class with 13 methods and 5 properties. |
| `GoToRemoteAction` | Go-to-remote action — jumps to a destination in a different PDF file . |
| `GoToURIAction` | Alias for UriAction, matching the public API name. |
| `ImportDataAction` | An import-data action (/S /ImportData): imports field data from the FDF/XFDF file identified by the action's /F file specification. |
| `JavascriptAction` | Class with 8 methods and 3 properties. |
| `LaunchAction` | Class with 9 methods and 4 properties. |
| `NamedAction` | NamedAction.CreateUri(uri) builds a URI action that can be linked to from annotations or form fields. |
| `PdfAction` | Base class for PDF actions. |
| `SubmitFormAction` | Submit-form action — sends form field data to a URL or remote file (PDF 32000-1:2008 §12.7.5.2). |
| `UriAction` | Class with 7 methods and 3 properties. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `ActionType` | The type of a PDF action. |

### Annotations

| Class | Description |
|---|---|
| `Annotation` | Represents a PDF annotation. |
| `AnnotationActionCollection` | Per-event PDF action slots for a widget annotation (matches the Aspose.Pdf /AA-tree entry shape: 14 named events plus the direct /A activation action). |
| `AnnotationCollection` | Collection of annotations on a page. |
| `AnnotationSelector` | Visitor that filters annotations across one or more pages. |
| `AppearanceDictionary` | Appearance-stream dictionary on an annotation (/AP entry): maps appearance-state name -> XForm. |
| `BleedMarkAnnotation` | BleedMarkAnnotation can be instantiated with a page and position, and provides methods to update its appearance, flatten it, and write XFDF data. |
| `Border` | Represents the border of an annotation or field widget. |
| `CaretAnnotation` | Class with 16 methods and 52 properties. |
| `Characteristics` | Annotation characteristics (border, rotation, etc.). |
| `CircleAnnotation` | Class with 16 methods and 51 properties. |
| `ColorBarAnnotation` | The ColorBarAnnotation constructor creates a color‑bar annotation on a page by specifying the page, rectangle and a CMYK color selector. |
| `CommonFigureAnnotation` | Common base for square and circle annotations — a figure drawn inside a rectangle, optionally inset by /RD (PDF 32000 §12.5.6.8). |
| `Dash` | Represents a dash pattern for borders. |
| `DefaultAppearance` | Represents the default appearance of a free text annotation. |
| `DocumentActionCollection` | Document-level /AA additional-action dictionary (PDF 32000-1 §12.6.3 Table 195). |
| `ExplicitDestination` | Represents an explicit destination in a PDF document (e.g. |
| `FieldDateTimeFormatter` | Formats a date/time value string according to an Acrobat-style date format. |
| `FieldNumberCurrencyFormatter` | Formats a numeric string as a currency value. |
| `FieldNumberPercentFormatter` | Formats a numeric string as a percentage (multiplies by 100 first). |
| `FileAttachmentAnnotation` | Represents a file attachment annotation. |
| `FitBExplicitDestination` | Fit-bounding-box destination (/FitB). |
| `FitBHExplicitDestination` | FitBH destination (/FitBH) — fit bounding box horizontally at Top. |
| `FitBVExplicitDestination` | FitBV destination (/FitBV) — fit bounding box vertically at Left. |
| `FitExplicitDestination` | Fit-the-page destination (/Fit). |
| `FitHExplicitDestination` | Fit horizontally at /Top (/FitH). |
| `FitRExplicitDestination` | FitR rectangle destination (/FitR). |
| `FitVExplicitDestination` | Fit vertically at /Left (/FitV). |
| `FixedPrint` | Class with 3 properties. |
| `FreeTextAnnotation` | FreeTextAnnotation constructors allow creating a free‑text annotation on a specific page rectangle with a defined appearance. |
| `GenericAnnotation` | Fallback annotation class for annotation subtypes that have no dedicated model (e.g. |
| `HighlightAnnotation` | Highlight text markup annotation. |
| `InkAnnotation` | Class with 17 methods and 52 properties. |
| `LineAnnotation` | Class with 17 methods and 62 properties. |
| `LinkAnnotation` | Class with 9 methods and 51 properties. |
| `MarkupAnnotation` | Class with 15 methods and 50 properties. |
| `Measure` | Measure-units metadata attached to a LineAnnotation (PDF 32000 §12.5.6.13 measure dictionaries). |
| `MediaClip` | A media clip object (PDF §13.2.4) — the actual media data or section thereof. |
| `MediaClipData` | A media clip data object (PDF §13.2.4.2) — full media data via a file specification. |
| `MediaClipSection` | A media clip section object (PDF §13.2.4.3) — a temporal section of another clip. |
| `MediaRendition` | A media rendition (PDF §13.2.3.2) — pairs a media clip with playback parameters. |
| `MovieAnnotation` | Class with 9 methods and 51 properties. |
| `NumberFormat` | One number-format entry — describes how a measurement value is rendered (precision, separators, before/after text). |
| `NumberFormatList` | Nested number-format list (Aspose.Pdf shape: Measure+NumberFormatList). |
| `PDF3DAnnotation` | Class with 14 methods and 53 properties. |
| `PDF3DArtwork` | A 3D artwork dictionary referenced by a PDF 3D annotation (Aspose.Pdf shape). |
| `PDF3DContent` | Embedded-stream content for a 3D artwork (U3D or PRC). |
| `PDF3DCrossSection` | Single cross-section through a PDF 3D model (Aspose.Pdf shape). |
| `PDF3DCrossSectionArray` | Ordered collection of PDF3DCrossSection entries applied to a PDF 3D view (Aspose.Pdf shape). |
| `PDF3DCuttingPlaneOrientation` | Orientation angles for the cutting plane of a 3D cross-section (Aspose.Pdf shape). |
| `PDF3DLightingScheme` | Lighting scheme applied to a PDF 3D artwork (Aspose.Pdf shape). |
| `PDF3DRenderMode` | Render mode applied to a 3D artwork view (Aspose.Pdf shape). |
| `PDF3DStream` | PDF /3DD stream wrapper — pairs a PDF3DArtwork with its document-owned content stream. |
| `PDF3DView` | Camera + render-state snapshot of a 3D artwork (Aspose.Pdf shape). |
| `PDF3DViewArray` | Indexed collection of PDF3DView snapshots (Aspose.Pdf shape). |
| `PageInformationAnnotation` | Class with 8 methods and 47 properties. |
| `PdfActionCollection` | Collection of PdfAction entries attached to an annotation (or any other action-bearing PDF object). |
| `PolyAnnotation` | Common base for polygon and polyline annotations — a chain of connected vertices (PDF 32000 §12.5.6.9). |
| `PolygonAnnotation` | Class with 16 methods and 52 properties. |
| `PolylineAnnotation` | Class with 15 methods and 52 properties. |
| `PopupAnnotation` | Class with 9 methods and 49 properties. |
| `PrinterMarkAnnotation` | Aggregate printer's-mark generator. |
| `RedactAnnotation` | Backward-compatible alias for RedactionAnnotation. |
| `RedactionAnnotation` | Represents a redaction annotation. |
| `RegistrationMarkAnnotation` | Class with 8 methods and 48 properties. |
| `Rendition` | A rendition object (PDF §13.2.3) — describes a media object to be played by a rendition action. |
| `RenditionAction` | A rendition action (PDF §12.6.4.14) — controls the playing of multimedia content. |
| `RichMediaAnnotation` | Class with 12 methods and 52 properties. |
| `RichTextToFlatStructureTransformer` | Transforms rich text annotation content (XHTML with arbitrarily nested spans) into a flat structure where every leaf text node becomes a single &lt;span style="."&gt; with the fully-merged CSS from all ancestor elements. |
| `ScreenAnnotation` | The ScreenAnnotation class lets you add a screen annotation to a page by providing the page, rectangle, and media file, and exposes AnnotationType, Action, and Title properties. |
| `SelectorRendition` | A selector rendition (PDF §13.2.3.3) — chooses among alternative renditions. |
| `SoundAnnotation` | Class with 16 methods and 52 properties. |
| `SoundData` | Class with 5 properties. |
| `SoundSampleData` | SoundSampleData constructors allow specifying samplingRate alone or together with numberOfSoundChannels, bitsPerChannel, and SoundSampleDataEncodingFormat. |
| `SquareAnnotation` | The SquareAnnotation.Accept(visitor) method implements the visitor pattern, allowing custom processing of annotation objects. |
| `SquigglyAnnotation` | Squiggly text markup annotation. |
| `StampAnnotation` | Class with 16 methods and 53 properties. |
| `StrikeOutAnnotation` | StrikeOut text markup annotation. |
| `TextAnnotation` | Class with 16 methods and 54 properties. |
| `TextMarkupAnnotation` | Base class for the text-markup annotations (Highlight, Underline, StrikeOut, Squiggly) — those whose geometry is a set of QuadPoints over page text. |
| `TextStyle` | Bundled font / colour / alignment style applied to a free-text annotation's rich text. |
| `TrimMarkAnnotation` | Class with 8 methods and 48 properties. |
| `UnderlineAnnotation` | Underline text markup annotation. |
| `WatermarkAnnotation` | Represents a watermark annotation that can be added to a PDF page. |
| `WidgetAnnotation` | Class with 13 methods and 59 properties. |
| `XYZExplicitDestination` | XYZ explicit destination: display the page at position (left, top) with zoom factor. |

#### Interfaces

| Interface | Description |
|---|---|
| `IAppointment` | Marker interface for any object that can be stored in an outline item's Destination, a link annotation's Destination, Document.OpenAction, or a named-destination collection. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `ActivationEvent` | Enum with 3 members. |
| `AnnotationFlags` | Annotation flags as defined in PDF spec Table 165. |
| `AnnotationState` | Review or marked-state of a markup annotation, as defined by PDF 32000 §12.5.6.3 (text annotations, /StateModel + /State entries). |
| `AnnotationStateModel` | Which state model a AnnotationState belongs to. |
| `AnnotationType` | Annotation subtype as defined in PDF spec Table 169. |
| `BorderEffect` | Border effect applied to annotations. |
| `BorderStyle` | Border style for annotations and fields. |
| `CapStyle` | Cap style used by ink strokes (free-hand drawings). |
| `CaptionPosition` | Caption-position within a LineAnnotation. |
| `CaretSymbol` | Caret-symbol style for CaretAnnotation. |
| `ColorsOfCMYK` | CMYK channel selector used by ColorBarAnnotation. |
| `ContentType` | Enum with 3 members. |
| `ExplicitDestinationType` | Explicit destination type, mirroring PDF 32000 §12.3.2.2 names. |
| `FileIcon` | Named-icon style for FileAttachmentAnnotation (/Name entry). |
| `FractionStyle` | How fractional values are rendered (decimal / fraction / round / truncate). |
| `FreeTextIntent` | Intent (sub-purpose) of a free-text annotation. |
| `HighlightingMode` | Highlighting mode for link annotations. |
| `Justification` | Justification of a free-text annotation's content. |
| `LightingSchemeType` | Lighting-scheme nominal type (PDF 32000-1 §13.6.4). |
| `LineEnding` | Line annotation ending styles (/LE entry elements). |
| `LineIntent` | Line annotation intents (/IT entry). |
| `PDF3DActivation` | How the embedded 3D model becomes active (PDF 32000-1 §13.6.2, AAS entry of the 3D activation dictionary). |
| `PolyIntent` | Intent of a polygon or polyline annotation (/IT entry). |
| `PredefinedAction` | Named viewer commands targeted by a NamedAction (PDF 32000 §12.6.4.11). |
| `PrinterMarkCornerPosition` | Corner a printer-mark annotation sits in (used by bleed-/trim-marks). |
| `PrinterMarkSidePosition` | Side a printer-mark annotation sits on. |
| `PrinterMarksKind` | The set of printer's-mark families AddPrinterMarks generates on a page (PDF 32000 pre-press marks). |
| `RenderModeType` | Render-mode nominal type (PDF 32000-1 §13.6.5). |
| `ReplyType` | Reply-relationship between a markup annotation and its InReplyTo target. |
| `RichTextFontStyles` | Rich-text run styles applied via Color). |
| `SoundEncoding` | Enum with 4 members. |
| `SoundIcon` | Enum with 2 members. |
| `SoundSampleDataEncodingFormat` | SoundSampleDataEncodingFormat enum defines the supported audio encodings: Raw, Signed, muLaw, and ALaw. |
| `StampIcon` | Named stamp-icon style for StampAnnotation (/Name entry, PDF 32000 §12.5.6.14). |
| `TextAlignment` | Horizontal text alignment for annotation text boxes. |
| `TextIcon` | Named-icon style for TextAnnotation (PDF 32000 §12.5.6.4 /Name entries). |

### Artifacts

| Class | Description |
|---|---|
| `Artifact` | Represents a PDF artifact — a marked content sequence tagged as /Artifact (PDF 32000 §14.8.2.2). |
| `ArtifactCollection` | Collection of artifacts on a page. |
| `BackgroundArtifact` | Represents a background artifact (a coloured block or image painted behind page content). |
| `HeaderArtifact` | Represents a header artifact — a page-level running head tagged Artifact /Subtype /Header (PDF 32000 §14.8.2.2). |
| `WatermarkArtifact` | Represents a watermark artifact that can be added to a PDF page. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `ArtifactSubtype` | Enumerates artifact subtypes. |
| `ArtifactType` | Enumerates artifact types as defined by PDF 32000 §14.8.2.2. |

### Collections

| Class | Description |
|---|---|
| `Collection` | PDF Portfolio collection. |
| `CollectionField` | One column in a PDF Portfolio's collection schema (PDF spec §7.11.5 Table 75). |
| `CollectionSchema` | Schema of a PDF Portfolio collection (PDF spec §7.11.5 Table 74). |
| `ImageCollection` | Collection of image XObjects on a page. |
| `ImageXObject` | Represents an image XObject found in a PDF page's resources. |
| `PageCollection` | Collection of pages in a PDF document. |
| `XImage` | Image XObject wrapper that mirrors the Aspose.Pdf XImage public surface. |
| `XImageCollection` | Collection of image XObjects on a page, with XImage-typed accessors and editing helpers. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `CollectionFieldSubtype` | Predefined collection field subtypes (PDF spec §7.11.5 Table 75). |

### Color

| Class | Description |
|---|---|
| `Color-Pdf` | Represents a color value used in PDF documents. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `ColorSpace` | Top-level device colour-space selector (Aspose.Pdf public surface). |
| `ColorType` | Represents the color type of a page. |

### Comparison

| Class | Description |
|---|---|
| `SideBySidePdfComparer` | Compares the text of two pages or two documents and writes a result PDF showing both versions side by side with the changes highlighted (deletions on the left page, insertions on the right). Static `Compare` overloads target a file path or a stream. |
| `SideBySideComparisonOptions` | Options for the side-by-side comparer: whitespace `ComparisonMode`, `AdditionalChangeMarks`, `ExcludeTables`, per-side comparison and exclusion areas, and the `DeleteColor` / `InsertColor` markers. |
| `SideBySideDocsComparisonResult` | Result of a document-level comparison: `HasChanges`, plus per-page `FirstDocChanges` / `SecondDocChanges` highlights and the per-page `FullChanges` edit sequences. |
| `SideBySidePagesComparisonResult` | Result of a page-level comparison: `HasChanges`, the `FirstPageChanges` / `SecondPageChanges` highlights, and the `FullChanges` edit list. |
| `EditContainer` | A single highlighted change in a side-by-side result: its `Id`, the `DiffOperation` it came from, and the `Rects` it covers on the page. |
| `GraphicalPdfComparer` | Renders two pages and compares them pixel by pixel, with configurable `Resolution`, mark `Color`, and `Threshold`. Windows only — it hands back `System.Drawing` bitmaps. |
| `ImagesDifference` | The pixel difference of two rendered pages: raw `Difference` / `Stride` / `Height`, the `SourceImage` and `GetDestinationImage()`, and `DifferenceToImage()`. Windows only. |
| `DiffOperation` | A single edit produced when diffing two texts: an Operation (Equal / Delete / Insert) together with the run of text it applies to. |
| `DiffUtils` | Text helpers used by the diff engine: common prefix/suffix extraction and re-assembly of the source / destination text from a list of DiffOperations. |
| `MergingOptimizer` | Semantic clean-up pass: eliminates equalities that are no larger than the edits surrounding them (folding those short common runs back into the adjacent delete/insert), then re-merges the result into canonical form. |
| `OperationsMerger` | Merges a diff into its canonical minimal form: coalesces adjacent runs of the same operation, and for a mixed delete/insert run factors any common prefix into the preceding equality and any common suffix into the following equality. |
| `OperationsSlideMerger` | Shifts a single edit that is surrounded on both sides by equalities sideways to eliminate one of those equalities, canonicalising the diff. |

#### Interfaces

| Interface | Description |
|---|---|
| `IDiffOptimizationOperation` | A post-processing pass that normalises a diff — a mutable list of DiffOperations — in place, preserving the source and destination texts while producing a cleaner or more canonical sequence of edits. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `ComparisonMode` | How the side-by-side comparer treats whitespace: `Normal` compares the extracted runs as-is, `IgnoreSpaces` compares only non-space characters, and `ParseSpaces` reconstructs inter-word spaces and line breaks from glyph geometry first. |
| `EditOperationsOrder` | When a delete and an insert are emitted for the same position, which one an optimizer places first in the resulting operation sequence. |
| `Operation` | The kind of edit a DiffOperation represents when diffing two texts: a run that is unchanged, deleted from the source, or inserted in the destination. |

### Content

| Class | Description |
|---|---|
| `BDC` | BDC — Begin marked content with properties. |
| `BI` | BI — Begin inline-image object. |
| `BMC` | BMC — Begin marked-content sequence (no properties). |
| `BT` | BT — Begin text object. |
| `BX` | BX — Begin compatibility section. |
| `BasicSetColorAndPatternOperator` | Common base for SCN/scn (basic color or pattern). |
| `BasicSetColorOperator` | Common base for the rg/RG/g/G/k/K family. |
| `BlockTextOperator` | Common base for the BT / ET delimiters. |
| `Clip` | W — Set clipping path (nonzero winding rule). |
| `ClosePath` | h — Close subpath. |
| `ClosePathEOFillStroke` | b* — Close, fill, and stroke path (even-odd rule). |
| `ClosePathFillStroke` | b — Close, fill, and stroke path (nonzero winding rule). |
| `ClosePathStroke` | s — Close and stroke path. |
| `ConcatenateMatrix` | cm — Concatenate matrix to CTM. |
| `ContentStreamBuilder` | Builds PDF content stream bytes using the operator syntax from PDF32000 §8-9. |
| `CurveTo` | c — Append cubic Bézier curve with three control points. |
| `CurveTo1` | v — Append cubic Bézier curve; first control point is current point. |
| `CurveTo2` | y — Append cubic Bézier curve; second control point coincides with final point. |
| `DP` | DP — Designate marked-content point with property list. |
| `Do` | Do — Invoke named XObject. |
| `EI` | EI — End inline-image object. |
| `EMC` | EMC — End marked content. |
| `EOClip` | W* — Set clipping path (even-odd rule). |
| `EOFill` | f* — Fill path (even-odd rule). |
| `EOFillStroke` | B* — Fill and stroke path (even-odd rule). |
| `ET` | ET — End text object. |
| `EX` | EX — End compatibility section. |
| `EndPath` | n — End path without filling or stroking. |
| `ExtGState` | Represents an Extended Graphics State dictionary (PDF32000 §8.4.5). |
| `ExtractedPath` | Represents a complete path with its painting mode and color. |
| `Fill` | f — Fill path (nonzero winding rule). |
| `FillStroke` | B — Fill and stroke path (nonzero winding rule). |
| `GRestore` | Q — Restore graphics state. |
| `GS` | gs — Set parameters from named ExtGState resource. |
| `GSave` | q — Save graphics state. |
| `GlyphPosition` | One element of a TJ-style glyph-position array: a string with an optional preceding/following position adjustment (in 1/1000 text units). |
| `GraphicsState` | Tracks the PDF graphics state during content stream parsing (PDF32000 §8.4). |
| `ID-Operators` | ID — Begin image data (after the inline-image dictionary). |
| `LineTo` | l — Append straight line segment to (X, Y). |
| `MP` | MP — Designate marked-content point (no properties). |
| `MoveTextPosition` | Td — Move text position: translate text origin by (X, Y). |
| `MoveTextPositionSetLeading` | TD — Move text position and set leading: translate by (X, Y) and set leading to -Y. |
| `MoveTo` | m — Begin new subpath at (X, Y). |
| `MoveToNextLine` | T* — Move to next line using current leading. |
| `MoveToNextLineShowText` | ' — Move to next line and show text. |
| `ObsoleteFill` | F — Fill path (deprecated; equivalent to f). |
| `Operator-Operators` | Back-compat alias for Operator kept so existing using Aspose.Pdf.Operators; code and the concrete operator subclasses (GSave, BT, ET, …) in this namespace continue to bind. |
| `OperatorSelector` | Visitor that filters content-stream operators by concrete type. |
| `PathExtractor` | Extracts vector paths from PDF page content streams. |
| `PathSegment` | Represents a segment of a path. |
| `Re` | re — Append rectangle (X, Y, Width, Height) as a complete subpath. |
| `SelectFont` | Tf — Select font and size. |
| `SetAdvancedColor` | scn — Set color (with optional pattern name) in current color space (non-stroking). |
| `SetAdvancedColorStroke` | SCN — Set color (with optional pattern name) in current color space (stroking). |
| `SetCMYKColor` | k — Set CMYK fill color. |
| `SetCMYKColorStroke` | K — Set CMYK stroke color. |
| `SetCharWidth` | d0 — Set glyph width in a Type 3 font. |
| `SetCharWidthBoundingBox` | d1 — Set glyph width and bounding box in a Type 3 font. |
| `SetCharacterSpacing` | Tc — Set character spacing. |
| `SetColor` | sc — Set color in current color space (non-stroking). |
| `SetColorOperator` | Common base for the SC/SCN/sc/scn family. |
| `SetColorRenderingIntent` | ri — Set color rendering intent. |
| `SetColorSpace` | cs — Set color space for non-stroking operations. |
| `SetColorSpaceStroke` | CS — Set color space for stroking operations. |
| `SetColorStroke` | SC — Set color in current color space (stroking). |
| `SetDash` | d — Set dash pattern. |
| `SetFlat` | i — Set flatness tolerance. |
| `SetGlyphsPositionShowText` | TJ — Show text with individual glyph positioning (array of strings and numeric adjustments). |
| `SetGray` | g — Set gray fill color. |
| `SetGrayStroke` | G — Set gray stroke color. |
| `SetHorizontalTextScaling` | Tz — Set horizontal text scaling. |
| `SetLineCap` | J — Set line cap style. |
| `SetLineJoin` | j — Set line join style. |
| `SetLineWidth` | w — Set line width. |
| `SetMiterLimit` | M — Set miter limit. |
| `SetRGBColor` | rg — Set RGB fill color. |
| `SetRGBColorStroke` | RG — Set RGB stroke color. |
| `SetSpacingMoveToNextLineShowText` | " — Set word/char spacing, move to next line, and show text. |
| `SetTextLeading` | TL — Set text leading. |
| `SetTextMatrix` | Tm — Set text matrix. |
| `SetTextRenderingMode` | Tr — Set text rendering mode. |
| `SetTextRise` | Ts — Set text rise. |
| `SetWordSpacing` | Tw — Set word spacing. |
| `ShFill` | sh — Paint shading specified by named resource. |
| `ShowText` | Tj — Show text string. |
| `Stroke` | S — Stroke path. |
| `TextOperator` | Common base for all text-related operators (BT/ET, Tx state, text-show, text-place). |
| `TextPlaceOperator` | Common base for text-positioning operators (Td, TD, T*, Tm). |
| `TextShowOperator` | Common base for text-showing operators (Tj, TJ, ', "). |
| `TextStateOperator` | Common base for text-state operators (Tc, Tw, Tz, TL, Tf, Tr, Ts). |

#### Structs

| Struct | Description |
|---|---|
| `PathCommand` | A single path construction command with coordinates. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `LineCap` | Line cap style. |
| `LineJoin` | Line join style. |
| `PathOp` | Path construction operator type. |
| `PathOperationType` | Represents a type of path operation. |
| `PathPaintMode` | Represents a single path painting operation. |

### Converters

| Class | Description |
|---|---|
| `MarkdownConverterOptions` | Options for PDF-to-Markdown conversion. |
| `MdLoadOptions` | Options for loading Markdown files as PDF documents. |
| `PageSizeInfo` | Page size configuration for document loading. |
| `PdfToHtmlConverter` | Converts PDF pages to HTML markup. |
| `PdfToMarkdownConverter` | Converts PDF pages to Markdown text. |
| `PdfToSvgConverter` | Converts PDF pages to SVG format. |
| `PdfToTextConverter` | Converts PDF pages to plain text. |
| `SvgLoadOptions` | Options for loading SVG files as PDF documents. |
| `TxtLoadOptions` | Options for loading a plain-text (.txt) file as a PDF document. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `ConversionEngines` | Available conversion engines. |

### Devices

| Class | Description |
|---|---|
| `BmpDevice` | Renders PDF document pages into BMP image format. |
| `DocumentDevice` | Abstract base for whole-document rendering devices. |
| `GdiPlusPageRenderer` | PDF page renderer that rasterizes through GDI+ (Graphics). |
| `GifDevice` | Renders PDF document pages into GIF image format. |
| `ImageDevice` | Abstract base class for image rendering devices (PNG, JPEG, BMP, TIFF). |
| `ImagePageDevice` | Default PageDevice that delegates to any ImageDevice — PngDevice / JpegDevice / BmpDevice / TiffDevice. |
| `IndexBitmapConverter` | Abstract base class for converting rendered images to indexed (1/4/8bpp) bitmaps. |
| `JpegDevice` | Renders PDF document pages into JPEG image format. |
| `Margins` | Page margins in points for TIFF rendering. |
| `PageDevice` | Abstract base for single-page rendering devices. |
| `PageSize-Devices` | Represents a physical page size in points. |
| `PdfDocumentDevice` | Default DocumentDevice implementation that saves the document as a PDF (Document.Save round-trip). |
| `PngDevice` | Renders PDF document pages into PNG image format. |
| `RgbaBuffer` | Raw RGBA pixel buffer returned by a IPageRenderer. |
| `SoftwarePageRenderer` | Built-in software PDF page renderer. |
| `SvgDevice` | Converts a PDF page to SVG markup. |
| `TextDevice` | Extracts text from PDF pages. |
| `TiffDevice` | Renders PDF document pages into TIFF image format. |
| `TiffSettings` | Settings for TIFF image generation (color depth, compression). |

#### Interfaces

| Interface | Description |
|---|---|
| `IPageRenderer` | Renders a PDF page to an RGBA pixel buffer. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `ColorDepth` | Color depth options for TIFF encoding. |
| `CompressionType` | TIFF compression algorithm options. |
| `FormPresentationMode` | How form fields are rendered (canonical Production / Editor split). |
| `PageCoordinateType` | Which page-box the rendered image extents come from. |
| `ShapeType` | Shape type for TIFF rendering. |

### Drawing

| Class | Description |
|---|---|
| `Arc` | An arc shape (portion of an ellipse). |
| `Circle` | A circle shape. |
| `Color-Drawing` | Color for drawing shapes. |
| `Curve` | A Bézier curve shape. |
| `DrawingPath` | A path shape composed of move, line, and curve segments. |
| `DrawingRectangle` | A rectangle shape. |
| `Ellipse` | An ellipse shape. |
| `GradientAxialShading` | Defines a linear (axial) gradient between two colors for use as a fill pattern. |
| `Graph` | A graph container that holds drawable shapes and renders them to a content stream. |
| `Line` | A line shape. |
| `PatternColorSpace` | Pattern colour-space marker (Aspose.Pdf public surface). |
| `Point-Drawing` | A 2D point (x, y). |
| `Polygon` | A polygon shape (closed path with arbitrary vertices). |
| `Rectangle-Drawing` | A rectangle shape in the Drawing namespace (distinct from Aspose.Pdf.Rectangle which is a page rectangle). |
| `Shape` | Base class for drawable shapes. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `ImageFormat` | Specifies the encoding format of an image. |

### Enums

| Class | Description |
|---|---|
| `Id` | Pair of byte strings that make up the /ID array in the PDF trailer. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `Direction` | Predominant text-flow direction recorded in the viewer-preferences dictionary. |
| `ExtendedBoolean` | A tri-state Boolean — True/False plus an Undefined sentinel used at deserialization time when a value isn't carried on the PDF dictionary entry. |
| `FieldValueType` | Logical value type carried by a CollectionField. |
| `HorizontalAlignment` | Describes horizontal alignment. |
| `PageLayout` | Page-layout entry written to the catalog (PDF32000 Table 28). |
| `PageMode` | Page-mode entry written to the catalog (PDF32000 Table 28). |
| `PdfVersion` | PDF specification version targeted by the document header. |
| `PrintDuplex` | Print-duplex value recorded in viewer preferences (PDF32000 Table 150). |
| `VerticalAlignment` | Describes vertical alignment. |

### Exceptions

| Class | Description |
|---|---|
| `CrashReportOptions` | Configures crash-report emission for GenerateCrashReport. |
| `DeprecatedFeatureException` | Thrown when a mechanism ISO 32000-2 retires is used on a PDF 2.0 document — RC4 under the 2.0 encryption flag, a legacy security handler, or the raw-RSA / enveloping PKCS#7 signature subfilters. |
| `FontNotFoundException` | Thrown when a requested font cannot be located (no system font with that name, no matching custom-font source, etc.). |
| `IncorrectFontUsageException` | Thrown during text extraction when the content stream issues a text-showing operator (Tj/TJ/'/") while no font is set in the current graphics state — i.e. |
| `InvalidFormTypeOperationException` | Thrown when an operation is attempted on the wrong form type (e.g. |
| `InvalidPasswordException` | Thrown when a password-protected operation is attempted without supplying a valid password (e.g. |
| `InvalidPdfFileFormatException` | Thrown when a stream cannot be opened as a PDF (bad header, truncated file, or otherwise unrecognisable as PDF). |
| `PdfException` | Represents errors that occur during PDF application execution. |
| `PdfExceptionMessages` | Centralised message strings surfaced by the library's exceptions and security diagnostics. |
| `PdfTextDecodingException` | Thrown when text content cannot be decoded — e.g. |
| `UnsupportedFontTypeException` | Thrown when a file cannot be opened as a font because its format is not a supported font program (e.g. |
| `ValidationIssue` | Represents a validation issue found in a PDF document. |

### Facades

| Class | Description |
|---|---|
| `AlignmentType` | Horizontal-alignment selector for legacy facade APIs (e.g. |
| `AutoFiller` | Repeatedly stamps a template PDF with rows from a DataTable: for each row, fills the form fields whose names match column names, flattens (except those listed in UnFlattenFields), and appends the resulting pages to the output. |
| `BDCProperties` | Properties for a BDC / DP marked-content operator (/MCID and /Lang). |
| `Bookmark` | A single bookmark extracted from a document's outline tree by ExtractBookmarks(). |
| `Bookmarks` | An ordered, indexable collection of Bookmark entries returned by ExtractBookmarks(). |
| `ContentsResizeParameters` | Parameters for ResizeContents describing margins, content size, and whether the page media box should change to match. |
| `ContentsResizeValue` | Represents a content-resize value. |
| `CorruptedItem` | One file the Concatenate path could not parse — recorded only when CorruptedFileAction is set to skip-and-continue. |
| `DocumentPrivilege` | Represents document access permissions (P value in encryption dictionary). |
| `FontColor` | Represents a font color using RGB components. |
| `Form-Facades` | Facade for form field manipulation including XML import/export. |
| `FormDataConverter` | Bridges PDF form-data formats (FDF / XFDF / XML) with DataTable and direct stream-to-stream transforms. |
| `FormEditor` | Facade for form operations: fill fields, flatten forms, import/export data. |
| `FormFieldFacade` | Represents the visual appearance attributes of a form field. |
| `FormImportResult` | Per-field result of a form-data import operation. |
| `FormattedText` | Represents formatted text used in stamp and mend operations. |
| `FormattedTextFont` | Represents a font reference returned by FormattedText.getFont(). |
| `LineInfo` | Line drawing parameters for PdfContentEditor.DrawCurve. |
| `PageBreak` | Describes a single horizontal cut on a source page: PageNumber (1-based) identifies the page in the source document, Position the PDF y-coordinate where the page is split. |
| `PdfAnnotationEditor` | Facade for annotation manipulation: import/export XFDF, delete, flatten, redact. |
| `PdfBookmarkEditor` | Facade for bookmark (outline) manipulation: create, extract, delete bookmarks. |
| `PdfContentEditor` | Facade for content-level editing: text replacement, link creation, image operations. |
| `PdfConverter` | Facade for converting PDF pages to images. |
| `PdfExtractor` | Facade for extracting text and images from a PDF document. |
| `PdfFileEditor` | Real-only additions to PdfFileEditor: exception-handling state, Try* wrappers around the existing working methods, and MemoryStream/file overloads for SplitToBulks/SplitToPages that wrap the real byte[] implementations already present in PdfFileEditor.cs. |
| `PdfFileInfo` | Facade for accessing PDF document metadata and properties. |
| `PdfFileMend` | Facade for adding text and images to existing PDF documents. |
| `PdfFileSecurity` | Facade for encrypting, decrypting, and changing passwords on PDF files. |
| `PdfFileSignature` | Facade for signing and reading digital signatures in PDF documents. |
| `PdfFileStamp` | Facade for adding stamps, page numbers, headers, footers, and watermarks. |
| `PdfJavaScriptStripper` | Removes all JavaScript from a PDF document. |
| `PdfPageEditor` | Facade for page-level editing: rotation, resizing, margin adjustment, and page box manipulation (CropBox, TrimBox, BleedBox, ArtBox). |
| `PdfPrintPageInfo` | Class with 1 property. |
| `PdfViewer` | Façade for viewing / printing a PDF document. |
| `PdfXmpMetadata` | Dictionary-style facade over a PDF document's XMP metadata stream. |
| `RenderingOptions-Facades` | Rendering options used by PdfConverter and other facade classes. |
| `ReplaceTextStrategy` | Configures how ReplaceText(string, string) matches and substitutes text. |
| `SignatureName` | Identifies a signature field by both its partial name (Name) and full hierarchical name (FullName), and reports whether the field currently carries a signature value via HasSignature. |
| `Stamp-Facades` | Represents a stamp to be applied via PdfFileStamp facade. |
| `StampInfo` | Contains information about a stamp on a page. |
| `TextProperties` | Text-display properties consumed by the *TextOperator family. |
| `VerticalAlignmentType` | Vertical-alignment selector for legacy facade APIs (e.g. |
| `ViewerPreference` | Bag of const int bit-flags describing viewer preferences (page mode, page layout, fullscreen behaviour, duplex, print scaling, reading direction). |

#### Interfaces

| Interface | Description |
|---|---|
| `IFacade` | Interface with 5 methods. |
| `ISaveableFacade` | Interface with 5 methods. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `Algorithm` | Encryption algorithm family used by PdfFileSecurity. |
| `AutoRotateMode` | Auto-rotation behaviour for the PDF viewer. |
| `BlendingColorSpace` | Blend-mode colour-space hint passed to a Stamp. |
| `ConcatenateCorruptedFileAction` | How the facade reacts to corrupted input during Concatenate. |
| `DataType` | Form-data source / destination kind consumed by FormDataConverter. |
| `DefaultMetadataProperties` | Well-known XMP-basic-schema property keys exposed by PdfXmpMetadata as a typed alternative to raw "xmp:&lt;name&gt;" strings. |
| `EncodingType` | Encoding type enumeration for FormattedText. |
| `ExtractImageMode` | Image extraction mode — matches docs.aspose.com Aspose.Pdf.ExtractImageMode. |
| `ExtractTextMode` | Text extraction mode — matches docs.aspose.com "Pure" (0) / "Raw" (1). |
| `FieldType-Facades` | The type of an AcroForm field, as used by AddField and related facade operations. |
| `FontStyle` | Font style enumeration for FormattedText. |
| `ImageMergeMode` | Layout strategy used by MergeImages when combining multiple input images into a single output image. |
| `ImportStatus` | Outcome of importing a single field's value. |
| `KeySize` | Key strength selector paired with Algorithm to pick the concrete RC4/AES variant used by PdfFileSecurity. |
| `NoCharacterAction-Facades` | What to do when the replacement font lacks a glyph for a character. |
| `PdfConverterImageFormat` | Image format for PdfConverter output. |
| `PositioningMode` | Text positioning mode for PdfFileMend. |
| `PropertyFlag` | Attribute flags applied to a form field via SetFieldAttribute. |
| `Scope-Facades` | How many matches to replace per call. |
| `StampType` | Stamp kind exposed by StampInfo. |
| `SubmitFormFlag` | Format of the data posted by a Submit form action — set per field via SetSubmitFlag. |
| `WordWrapMode-Facades` | Word wrap mode for PdfFileMend text operations. |

### Forms

| Class | Description |
|---|---|
| `AcroFormData` | Form-level AcroForm dictionary data surfaced by the form JSON export. |
| `AppearanceEntry` | One state of a widget appearance variant (the body of an /AP/N, AP/D, or /AP/R entry). |
| `ButtonField` | Push-button form field (FT=Btn with Pushbutton flag set). |
| `CheckboxField` | Class with 26 methods and 84 properties. |
| `ChoiceField` | Class with 25 methods and 89 properties. |
| `ComboBoxField` | Combo box (drop-down) form field — a ChoiceField with the Combo flag set. |
| `DefaultResourcesData` | Form-level default resources (/DR) surfaced by the form JSON export. |
| `DocMDPSignature` | Pairs a Signature (which carries the signing certificate) with the DocMDPAccessPermissions level a certifying signature will impose. |
| `ExportFieldsToJsonOptions` | Options passed to ExportToJson. |
| `Field` | Represents a form field. |
| `FieldExportingData` | Data-transfer object describing a single form field (or the form-level AcroForm dictionary) as produced by the form JSON export. |
| `FieldSerializationResult` | Per-field outcome of an ExportToJson or ImportFromJson call. |
| `FlattenSettings` | Settings for Flatten(FlattenSettings). |
| `Form-Forms` | Represents the interactive form (AcroForm) of a PDF document. |
| `FormFieldBuilder` | Creates new interactive form fields and adds them to a PDF document. |
| `IconFit` | PDF Annotation Handler "IF" entry — icon-fit dictionary for push-button widgets. |
| `ListBoxField` | List box form field — a ChoiceField without the Combo flag. |
| `Option` | Class represents option of choice field. |
| `OptionCollection` | Mutable collection of Option values backed by the owning ChoiceField's /Opt entry. |
| `PKCS1` | Class with 7 methods and 14 properties. |
| `PKCS7` | Class with 6 methods and 14 properties. |
| `PKCS7Detached` | A detached PKCS#7 (CMS) signature configuration. |
| `RadioButtonField` | Class with 27 methods and 95 properties. |
| `RadioButtonGroup` | A logical radio-button group — a set of mutually exclusive options sharing the same field name in the AcroForm hierarchy. |
| `RadioButtonOption` | A single option within a radio button group. |
| `RadioButtonOptionField` | One option of a RadioButtonField. |
| `RichTextBoxField` | A Tx form field that carries the rich-text flag (bit 26 of /Ff). |
| `Signature` | Represents a digital signature in a PDF document. |
| `SignatureCustomAppearance` | Visual-layout knobs for a signature's appearance stream — font, size, padding and which signer metadata strings are rendered inside the widget annotation. |
| `SignatureField` | SignatureField.Sign(signature) applies a digital signature to a form field, and Flatten() can make the signature appearance permanent in the document. |
| `TextBoxField` | The TextBoxField class lets developers create AcroForm text fields with multiple constructors and supports adding barcodes and images to the field. |
| `XFA` | XmlNode-based accessor for the document's XFA packets (template / datasets / config / form / xdp). |
| `XfaAccessor` | Provides indexer access to XFA field values by path. |
| `XfaField` | Represents a single field of an XFA form addressed by its template SOM path (e.g. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `BoxShape` | Outer shape of a checkbox / radio-button widget border. |
| `BoxStyle` | Visual style of the checkbox glyph (/MK /CA mapping). |
| `DocMDPAccessPermissions` | /DocMDP /TransformParams /P access-permission levels per PDF 32000-1 §12.8.2.2. |
| `FieldSerializationStatus` | Outcome of serializing a single form field through ExportToJson or ImportFromJson. |
| `FieldType-Forms` | The type of a form field. |
| `FormType` | Represents the type of a PDF form. |
| `IconCaptionPosition` | PDF Annotation Handler "TP" entry — caption / icon layout for push-button widgets. |
| `ScalingMode` | How a button icon is scaled to fit its rectangle (/MK /IF /SW). |
| `ScalingReason` | When to scale the icon to fit its rectangle (/MK /IF /S). |
| `SignDependentElementsRenderingModes` | How widgets dependent on signature appearance are rendered when the form is converted. |
| `SubjectNameElements` | X.500 distinguished-name components rendered inside the visible signature appearance when UseDigitalSubjectFormat is set. |

### Functions

| Class | Description |
|---|---|
| `ExponentialFunction` | Exponential interpolation function (§7.10.3). |
| `PdfFunction` | Abstract base for all PDF function types. |
| `PostScriptEvaluator` | PostScript calculator evaluator for Type 4 PDF functions (PDF32000 §7.10.5). |
| `PostScriptFunction` | PostScript calculator function (§7.10.5). |
| `SampledFunction` | Sampled function (§7.10.2): lookup table with linear interpolation. |
| `StitchingFunction` | Stitching function (§7.10.4): combines sub-functions over sub-domains. |

### Generator

| Class | Description |
|---|---|
| `BaseParagraph` | Base class for paragraph-level DOM content (text fragments, tables, images, header/footer fragments, floating boxes). |
| `BorderInfo` | Represents border information for table cells and rows. |
| `Cell` | Represents a cell in a table row. |
| `Cells` | A collection of cells in a row. |
| `ColumnInfo` | ColumnInfo stores table layout details, exposing ColumnWidths, ColumnSpacing, and ColumnCount properties for precise column sizing. |
| `FileHyperlink` | Hyperlink that launches an external file (e.g. |
| `FloatingBox` | Represents a floating box that can be positioned absolutely or flowed on a page. |
| `GraphInfo` | Stroke / fill settings for drawable shapes and table-cell borders. |
| `HeaderFooter` | Represents a header or footer that can be applied to PDF pages. |
| `Heading` | Represents a TOC heading entry that links to a destination page. |
| `Hyperlink` | Base type for hyperlinks attached to text fragments or annotations. |
| `LevelFormat` | Per-heading-level TOC formatting descriptor (line-dash style, margins, indent, text state). |
| `LocalHyperlink` | Hyperlink that jumps to another paragraph or page in the same document. |
| `MarginInfo` | Represents margin information for page elements. |
| `Note` | A footnote or endnote attached to a TextFragment. |
| `Paragraphs` | Container for paragraph-level content (text fragments, tables, images, header/footer fragments, etc.) attached to a Page, Cell, HeaderFooter or FloatingBox. |
| `Row` | Represents a row in a table. |
| `Rows` | A collection of rows in a table. |
| `Table` | Represents a table that can be added to a PDF page. |
| `TocInfo` | Table of contents information for a page. |
| `WebHyperlink` | Hyperlink that opens an external URL. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `BorderCornerStyle` | Corner-rounding style for a Table's border box. |
| `BorderSide` | Specifies which sides of a border to draw. |
| `ColumnAdjustment` | How a Table sizes its columns (API parity). |
| `ParagraphPositioningMode` | How Left / Top are interpreted. |
| `TableBroken` | TableBroken enum indicates how a table is split across pages, with values None, Vertical, VerticalInSamePage, and IsInNextPage. |

### Geometry

| Class | Description |
|---|---|
| `Matrix` | Represents a 3x3 transformation matrix [a b 0; c d 0; e f 1]. |
| `Matrix3D` | 4×3 affine 3D matrix (Aspose.Pdf shape for PDF 3D camera positioning). |
| `PageSize-Pdf` | Standard page size constants (dimensions in points, portrait orientation). |
| `Point-Pdf` | Represents a point in 2D space. |
| `Point3D` | Represents a point in 3D space (used by 3D annotations). |
| `Rectangle-Pdf` | Represents a rectangle defined by lower-left and upper-right coordinates (PDF user-space convention: Y increases upward). |

### Groupprocessor

| Struct | Description |
|---|---|
| `ObjectKey` | Identifies a PDF indirect object by its object number and generation number. |

### Helpers

| Class | Description |
|---|---|
| `BoundsCheckableList` | A list of T that optionally enforces container bounds on insertion. |
| `OptimizedMemoryStream` | A growable in-memory stream that can exceed the 2 GB single-array limit of MemoryStream by storing data in fixed-size chunks. |
| `PolygonsHelper` | Polygon and rectangle geometry utilities — point/polygon containment, segment hit-testing and rectangle/polygon classification. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `BoundsCheckMode` | How a BoundsCheckableList{T} reacts when an item lies outside its container. |
| `RectanglePosition` | Position of a rectangle relative to a polygon, as returned by GetRectanglePositionRelativePolygon. |

### HTML

| Class | Description |
|---|---|
| `CssSavingInfo` | Context passed to a CssSavingStrategy callback. |
| `CssUrlRequestInfo` | Context passed to a CssUrlMakingStrategy callback. |
| `HtmlFragment` | Represents an HTML content fragment that can be added to PDF pages via HeaderFooter.Paragraphs or Page.Paragraphs. |
| `HtmlImageSavingInfo` | Context passed to a per-image saving callback. |
| `HtmlLoadOptions` | Options for loading HTML content into PDF. |
| `HtmlPageMarkupSavingInfo` | Context passed to a HtmlPageMarkupSavingStrategy callback. |
| `HtmlSaveOptions` | Options for saving a PDF document as HTML. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `AntialiasingProcessingType` | Antialiasing pass applied to the resulting HTML. |
| `FontEncodingRules` | Backing-byte enum: the Aspose.Pdf reflection signature is byte-typed. |
| `FontSavingModes` | How fonts are emitted into the output HTML. |
| `HtmlDocumentType` | HTML document flavour produced by HtmlSaveOptions. |
| `HtmlImageType` | Image type produced for embedded raster content. |
| `HtmlMarkupGenerationModes` | Granularity of HTML markup produced. |
| `HtmlMediaType` | Specifies the media type for HTML rendering. |
| `HtmlPageLayoutOption` | How the generated PDF page layout reacts to wide HTML content. |
| `ImageParentTypes` | Container element that hosts an image. |
| `LettersPositioningMethods` | How letter positions are encoded in the output CSS. |
| `PartsEmbeddingModes` | Whether and how secondary parts (CSS / images / fonts) are embedded. |
| `RasterImagesSavingModes` | How raster images are saved alongside the HTML. |

### Interop

| Class | Description |
|---|---|
| `BitmapInfo` | Class with 2 methods and 4 properties. |

#### Interfaces

| Interface | Description |
|---|---|
| `IIndexBitmapConverter` | Interface with 3 methods. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `PixelFormat` | Enum with 6 members. |

### Io

| Class | Description |
|---|---|
| `ZDeflaterOutputStream` | zlib (RFC 1950) deflating output stream — a public helper matching the Aspose.Pdf surface. |
| `ZInflaterInputStream` | zlib (RFC 1950) inflating input stream — a public helper matching the Aspose.Pdf surface. |

### Logicalstructure

| Class | Description |
|---|---|
| `AnnotElement` | Class with 11 methods and 13 properties. |
| `ArtElement` | Class with 11 methods and 13 properties. |
| `AttributeName` | A typed value for a standard attribute name (the /Name-valued entries in an attribute object, e.g. |
| `BibEntryElement` | Class with 11 methods and 13 properties. |
| `BlockQuoteElement` | Class with 11 methods and 13 properties. |
| `CaptionElement` | Class with 11 methods and 13 properties. |
| `CodeElement` | Class with 11 methods and 13 properties. |
| `DivElement` | Class with 11 methods and 13 properties. |
| `DocumentElement` | The document root structure element (/S Document). |
| `Element-LogicalStructure` | Common base for nodes in the logical-structure tree. |
| `ElementList` | An ordered list of structure elements, as returned by ChildElements. |
| `FigureElement-LogicalStructure` | FigureElement also offers **AdjustPosition(settings)** to reposition the figure according to layout settings. |
| `FormElement` | Class with 11 methods and 13 properties. |
| `FormulaElement` | Class with 12 methods and 17 properties. |
| `HeaderElement` | Class with 11 methods and 13 properties. |
| `IllustrationElement` | Abstract base for illustration-kind structure elements (Figure, Formula). |
| `IndexElement` | Class with 11 methods and 13 properties. |
| `LinkElement` | Class with 11 methods and 15 properties. |
| `ListElement` | Class with 11 methods and 13 properties. |
| `ListLBodyElement` | Class with 11 methods and 13 properties. |
| `ListLIElement` | Class with 11 methods and 13 properties. |
| `ListLblElement` | Class with 11 methods and 13 properties. |
| `MCRElement` | A marked-content reference leaf (role "MCR") emitted by the auto-tagger to mark where a structure element's page content lives. |
| `NonStructElement` | NonStructElement.SetTag(tag) assigns a custom tag to a non‑structural element, influencing its identification in the PDF structure tree. |
| `NoteElement` | Class with 11 methods and 13 properties. |
| `OBJRElement` | An object reference (role "OBJR") — links a structure element to a PDF object on a page (typically an annotation, e.g. |
| `ParagraphElement` | ParagraphElement provides methods to modify its text, tag, ID, and hierarchical position within a PDF structure. |
| `PartElement` | Class with 11 methods and 13 properties. |
| `PrivateElement` | Class with 11 methods and 13 properties. |
| `QuoteElement` | Class with 11 methods and 13 properties. |
| `ReferenceElement` | Class with 11 methods and 13 properties. |
| `RubyElement` | Class with 11 methods and 13 properties. |
| `SectElement` | SectElement provides methods such as AppendChild, SetTag, SetText, and Remove to build and manipulate structural elements within a PDF document. |
| `SpanElement` | SpanElement methods such as AppendChild, SetTag, SetText, SetId, AdjustPosition, and ChangeParentElement let developers build and manipulate structured PDF content. |
| `StructTreeRootElement` | The /StructTreeRoot wrapper at the top of the logical- structure tree. |
| `StructureAttribute` | A single tagged-PDF structure attribute (key plus one typed value). |
| `StructureAttributes` | An owner-scoped set of StructureAttribute objects attached to a structure element (one PDF attribute dictionary with a fixed /O owner). |
| `StructureElement` | Base class for tagged-PDF logical-structure elements. |
| `StructureElementAttributes` | The attribute manager exposed by Attributes. |
| `StructureTextState` | Text-state snapshot used to format inline structure-element runs. |
| `StructureType` | A structure-type role (the /S entry value), exposed via S. |
| `TOCElement` | Class with 11 methods and 13 properties. |
| `TOCIElement` | Class with 11 methods and 13 properties. |
| `TableElement` | Class with 14 methods and 31 properties. |
| `TableTBodyElement` | Class with 12 methods and 13 properties. |
| `TableTDElement` | TableTDElement.ColSpan and RowSpan properties allow a cell to span multiple columns or rows within a Table. |
| `TableTFootElement` | Class with 12 methods and 13 properties. |
| `TableTHElement` | Class with 11 methods and 23 properties. |
| `TableTHeadElement` | Class with 12 methods and 13 properties. |
| `TableTRElement` | Class with 13 methods and 23 properties. |
| `WarichuElement` | Class with 11 methods and 13 properties. |

#### Interfaces

| Interface | Description |
|---|---|
| `ITextElement` | A structure element that can carry inline text content (written to the element's /ActualText entry). |

#### Enumerations

| Enumeration | Description |
|---|---|
| `AttributeKey` | Standard tagged-PDF attribute keys (ISO 32000-1 §14.8.5). |
| `AttributeOwnerStandard` | Standard owners of a tagged-PDF attribute set (the /O entry of an attribute object, ISO 32000-1 Table 348). |

### Navigation

| Class | Description |
|---|---|
| `DestinationArray` | Opaque wrapper around a PDF destination array, created by NamedDestination factory methods. |
| `DestinationCollection` | Named-destination collection exposed as IEnumerable&lt;KeyValuePair&lt;string, object&gt;&gt; for Aspose.Pdf parity. |
| `NamedDestination` | Represents a named destination in the document (PDF32000 §12.3.2.3). |
| `NamedDestinationCollection` | Collection of named destinations in the document. |
| `OutlineBuilder` | Builder for creating bookmarks (outlines) programmatically. |
| `OutlineCollection` | Class with 11 methods and 8 properties. |
| `OutlineItem` | Represents a bookmark (outline item) in the document outline hierarchy. |
| `OutlineItemBuilder` | Builder for a single outline item with fluent API. |
| `OutlineItemCollection` | The document outline (bookmark tree). |
| `Outlines` | Document-level bookmark (outline) collection. |
| `PageLabel` | Represents a page label range. |
| `PageLabelBuilder` | Builder for creating page labels on a document. |
| `PageLabelCollection` | Collection of page label ranges from the /PageLabels number tree. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `NumberingStyle` | Numbering style for page labels. |

### Optimization

| Class | Description |
|---|---|
| `AutoTaggingSettings` | Settings controlling automatic structure-tree generation during conversion. |
| `FontEmbeddingOptions` | Font-embedding behaviour during conversion. |
| `HeadingLevels` | Heading-level recognition tuning for AutoTaggingSettings. |
| `ImageCompressionOptions` | Image-compression sub-options for OptimizationOptions. |
| `OptimizationOptions-Optimization` | Class which describes document optimization algorithm. |
| `PdfANonSpecificationFlags` | PDF/A non-spec compliance toggles. |
| `PdfASymbolicFontEncodingStrategy` | Strategy for re-encoding symbolic fonts during PDF/A conversion. |
| `PdfFormatConversionOptions` | Options for converting a PDF document to a specific PDF/A conformance level. |
| `QueueItem` | One entry in the priority queue — names a single (platformID, platformSpecificID) cmap subtable. |
| `RgbToDeviceGrayConversionStrategy` | Converts all RGB color operators and image color spaces on a page to DeviceGray. |
| `ToUnicodeProcessingRules` | Rules applied when generating ToUnicode CMaps during conversion. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `CMapEncodingTableType` | OpenType cmap subtable selector. |
| `ConvertErrorAction` | Action to take when a conversion error is found. |
| `ConvertSoftMaskAction` | Soft-mask handling strategy during PDF/A conversion. |
| `ConvertTransparencyAction` | Action to take when transparency is encountered during PDF/A conversion. |
| `HeadingRecognitionStrategy` | Algorithm used to detect headings during auto-tagging. |
| `ImageCompressionVersion` | Strategy version for image compression. |
| `ImageEncoding` | How image streams are re-encoded during optimization. |
| `PdfFormat` | PDF format/conformance level for validation and conversion. |
| `PuaProcessingStrategy` | Strategy for processing Private-Use-Area Unicode characters. |
| `RemoveFontsStrategy` | Strategy for removing or excluding fonts during conversion. |
| `SegmentAlignStrategy` | Strategy for collapsing adjacent text segments during PDF/A conversion. |

### Options

| Class | Description |
|---|---|
| `BorderPartStyle` | Class with 1 method and 1 property. |
| `CompositingParameters` | Image-blending parameters consumed by Stream, int, float, float, float, float, CompositingParameters). |
| `LayerCollection` | Represents the collection of layers on a specific page. |
| `LayerEntry` | Represents a layer being built by OptionalContentBuilder. |
| `LoadOptions` | Class with 4 properties. |
| `MarginPartStyle` | One side of a MarginInfo: either a fixed PDF-point value or an auto-fit hint. |
| `OptionalContentBuilder` | Creates optional content groups (layers) in a PDF document. |
| `OptionalContentGroup` | Represents an Optional Content Group (layer) in a PDF document. |
| `OptionalContentProperties` | Collection of Optional Content Groups (layers) in a document. |
| `OutputIntent` | Represents a PDF OutputIntent entry (PDF32000 §14.11.5). |
| `OutputIntents` | Collection of OutputIntent entries on the document catalog's /OutputIntents array. |
| `PdfSaveOptions` | Class with 8 properties. |
| `ProgressEventHandlerInfo` | Class in the PDF .NET API. |
| `RenderingOptions-Pdf` | Rendering options used by the page-to-image converters (PngDevice, JpegDevice, PdfConverter, …). |
| `ResourceLoadingResult` | Class with 1 method and 1 property. |
| `ResourceSavingInfo` | Class with 1 property. |
| `SaveOptions` | The SaveOptions class exposes properties such as SaveFormat, CloseResponse, and WarningHandler to control PDF saving behavior. |
| `SvgImageSavingInfo` | Class with 2 properties. |
| `SvgSaveOptions` | SvgSaveOptions provides properties such as SaveFormat to control how a PDF is saved as SVG. |
| `UnifiedSaveOptions` | Class with 7 properties. |
| `ViewerPreferences` | Represents the document's viewer preferences (PDF32000 §12.2). |

#### Enumerations

| Enumeration | Description |
|---|---|
| `BlendMode` | PDF blend modes (Table 136 of PDF 32000-1:2008). |
| `DefaultState` | Default visibility state of an Optional Content Group (layer). |
| `HtmlBorderLineType` | Enum with 9 members. |
| `LoadFormat` | Source format selector used by LoadOptions. |
| `NodeLevelResourceType` | NodeLevelResourceType enum distinguishes between Font and Image resources when working with structural elements. |
| `PageLayoutMode` | Page layout mode for the document (PDF32000 §12.2, Table 28). |
| `PageModeValue` | Page mode specifying how the document should be displayed on opening (PDF32000 §12.2, Table 28). |
| `ProgressEventType` | Conversion-progress milestones reported via EventType. |
| `SaveFormat` | Save format enumeration. |
| `SvgExternalImageType` | Image format used by the SVG embedded-image saver. |

### Printing

| Class | Description |
|---|---|
| `CustomPrintEventArgs` | Event args raised by PdfViewer.CustomPrint. |
| `PageSettings` | Per-page print settings (paper size / orientation / margins). |
| `PaperSize` | The PaperSize class lets developers define custom page dimensions by specifying a name, width, and height. |
| `PdfQueryPageSettingsEventArgs` | Event args supplied to PdfViewer.PdfQueryPageSettings. |
| `PrinterSettings` | Printer-side settings (printer name, copies, range). |
| `StartEndPageEventArgs` | Event args raised by PdfViewer.StartPage / EndPage. |

### Security

| Class | Description |
|---|---|
| `BitString` | A variable-length bit string supporting bitwise operations, concatenation, substring extraction, and conversion to/from bytes and hex. |
| `CertificateEncryptionOptions` | Bundles the public certificate used to encrypt a document and the private-key store needed to decrypt it. |
| `EncryptionInfo` | Information about a document's encryption. |
| `EncryptionParameters` | Parameters parsed from the document's /Encrypt dictionary and passed to Initialize when a custom security handler takes over for an alternative /Filter. |
| `HashAlgorithmFactory` | Creates a HashAlgorithm for a DigestHashAlgorithm. |
| `MathExtensions` | Small integer math helpers shared by the security primitives. |
| `PdfCertificate` | Represents a certificate + private key for PDF signing. |
| `PdfSigner` | Signs PDF documents with X.509 certificates using PKCS#7/CMS detached signatures. |
| `Sha3_256` | SHA3-256 (FIPS 202) — own Keccak implementation. |
| `Sha3_384` | SHA3-384 (FIPS 202) — own Keccak implementation. |
| `Sha3_512` | SHA3-512 (FIPS 202) — own Keccak implementation. |
| `SignatureAlgorithmInfo` | Algorithm + digest + envelope-standard triple extracted from a PDF signature value. |
| `SignatureAppearance` | Defines the visual appearance for a digital signature annotation. |
| `SignatureOptions` | Options for signing a PDF document. |
| `ValidationOptions` | Tunes the verification policy used by Verify(SignatureName, ValidationOptions, out ValidationResult). |
| `ValidationResult` | Verification outcome produced by Verify(SignatureName, ValidationOptions, out ValidationResult). |

#### Interfaces

| Interface | Description |
|---|---|
| `ICustomSecurityHandler` | Pluggable custom security handler — implement this to handle non-Standard /Filter entries (e.g. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `CryptoAlgorithm` | PDF encryption algorithm. |
| `CryptographicStandard` | Cryptographic envelope standard reported by a PDF signature SubFilter entry. |
| `DigestHashAlgorithm` | Digest hash algorithm used by a PDF signature (PKCS#7 messageDigest attribute). |
| `SignatureAlgorithmType` | Signing-algorithm family extracted from a signature's PKCS#7 signatureAlgorithm OID. |
| `ValidationMethod` | Revocation-check protocol selector used by ValidationMethod. |
| `ValidationMode` | How aggressively VerifySignature(string, ValidationOptions, out ValidationResult) reports problems. |
| `ValidationStatus` | Outcome categories reported by Status. |

### Shading

| Class | Description |
|---|---|
| `AxialShading` | Axial (linear) shading (Type 2, §8.7.4.3). |
| `CoonsPatchShading` | Coons patch mesh (Type 6, §8.7.4.5.7). |
| `FreeFormGouraudShading` | Free-form Gouraud-shaded triangle mesh (Type 4, §8.7.4.5.5). |
| `FunctionBasedShading` | Function-based shading (Type 1, §8.7.4.2). |
| `LatticeFormGouraudShading` | Lattice-form Gouraud-shaded triangle mesh (Type 5, §8.7.4.5.6). |
| `Pattern` | Abstract base for all PDF pattern types. |
| `RadialShading` | Radial (circular) shading (Type 3, §8.7.4.4). |
| `ShadingBase` | Abstract base for all PDF shading types (§8.7.4.1). |
| `ShadingPattern` | Shading pattern (PatternType 2, §8.7.3.2): smooth gradient. |
| `TensorPatchShading` | Tensor-product patch mesh (Type 7, §8.7.4.5.8). |
| `TilingPattern` | Tiling pattern (PatternType 1, §8.7.3.1): repeating tile. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `ShadingType` | Shading type — /ShadingType entry. |

### Stamps

| Class | Description |
|---|---|
| `PageNumberStamp` | A stamp that adds page numbers to PDF pages. |
| `PdfPageStamp` | Stamps the content of one PDF page onto another page. |
| `Stamp-Stamps` | Base class for stamps that can be applied to PDF pages. |
| `TextStamp-Stamps` | A text stamp that can be applied to PDF pages. |
| `WatermarkStamp` | A watermark stamp that draws diagonal text across the page. |

### Structure

| Class | Description |
|---|---|
| `Element-Structure` | A node in a PDF's logical-structure tree (the /StructTreeRoot /K subtree, plus their /K descendants). |
| `ElementCollection` | A live, mutable collection of structure elements held under a parent's /K array. |
| `FigureElement-Structure` | A figure structure element (/S = "Figure") that wraps a raster or vector picture. |
| `RootElement` | Top-level wrapper for the PDF /StructTreeRoot dictionary. |
| `StructElement` | A generic structure element (anything other than the recognised typed subclasses). |
| `TextElement` | A text-bearing structure element (Span / P / Quote / Note / Reference / BibEntry). |

### Tagged

| Class | Description |
|---|---|
| `MarkedContentInfo` | Information about a marked content sequence, used to wrap content in BDC/EMC operators. |
| `PositionSettings` | Layout/position settings for a tagged structure element, passed to AdjustPosition. |
| `StructTreeElement` | Read-only view of a single structure element in a tagged document's structure tree: its role, accessibility text, attributes, marked-content references, and child elements. |
| `StructTreeRoot` | Read-only view of a tagged document's structure-tree root (/StructTreeRoot). |
| `StructureElementBuilder` | Builder for creating a structure element with children and marked content. |
| `StructureTreeBuilder` | Builds a logical structure tree for a tagged PDF document. |
| `TaggedContent` | Provides access to tagged PDF metadata (title, language) and the logical structure tree. |
| `TaggedException` | Exception thrown when a tagged PDF structure operation violates PDF spec constraints. |

#### Interfaces

| Interface | Description |
|---|---|
| `ITaggedContent` | The author-facing surface for a tagged PDF document. |

### Text

| Class | Description |
|---|---|
| `AbsorbedCell` | Represents a cell in an absorbed table. |
| `AbsorbedRow` | Represents a row in an absorbed table. |
| `AbsorbedTable` | Represents a table detected on a PDF page. |
| `CharInfo` | Per-character layout information (glyph rectangle + page position). |
| `CharInfoCollection` | Collection of CharInfo entries — supports the public surface used by TextSegment.Characters but stays empty by default. |
| `CustomFontSubstitutionBase` | Class with 1 method. |
| `ExternalFontCache` | Manages the set of folders searched for external (non-embedded) TrueType/OpenType faces during rendering and conversion. |
| `FileFontSource` | A font source backed by a single font file on disk. |
| `FolderFontSource` | A font source that searches fonts in a specific directory. |
| `Font` | Type alias for FontInfo, matching the Font class name. |
| `FontAbsorber` | Collects all fonts used in a PDF document or a single page. |
| `FontCollection` | Collection of fonts referenced by a page. |
| `FontData` | Represents a font with its name and type. |
| `FontEmbedder` | Embeds TrueType fonts into PDF documents for custom text rendering. |
| `FontInfo` | Information about a font used in a PDF. |
| `FontRepository` | Provides access to fonts available for use in PDF documents. |
| `FontSource` | Base class for font sources. |
| `FontSourceCollection` | A collection of font sources used by FontRepository. |
| `FontSubstitution` | Base type for font substitutions held in FontSubstitutionCollection. |
| `FontSubstitutionCollection` | Class with 9 methods and 3 properties. |
| `FontUtilities` | Provides font management utilities for a document. |
| `MarkupParagraph` | A paragraph within a markup section. |
| `MarkupSection` | A section of text on a page — a spatially coherent group of lines. |
| `MemoryFontSource` | A font source backed by an in-memory font byte buffer. |
| `OneBasedList` | Read-only list with 1-based indexer, matching the public API. |
| `OriginalFontSpecification` | Class with 1 method and 3 properties. |
| `PageMarkup` | Represents the text markup of a single page, organized into sections. |
| `ParagraphAbsorber` | Absorbs text from PDF pages and organizes it into sections and paragraphs. |
| `ParagraphAbsorberOptions` | Options for ParagraphAbsorber controlling section detection thresholds. |
| `PdfFontView` | Thin engine-font view used by Aspose.Pdf parity (Font.iPdfFont). |
| `PhysicalTextSegment` | Physical (page-space) projection of an absorbed TextSegment. |
| `Position` | The Position class can be used to specify X and Y indents for layout adjustments; its XIndent and YIndent properties are read‑only after construction. |
| `RegexManager` | Global configuration for regular-expression text search (for example via TextFragmentAbsorber). |
| `SimpleFontSubstitution` | Class with 3 methods and 3 properties. |
| `SystemFontSource` | A font source that searches the system's installed fonts. |
| `TabStop` | Represents a single tab stop position. |
| `TabStops` | A collection of tab stop positions for text layout. |
| `TableAbsorber` | Detects and extracts tables from PDF pages by analyzing text positions and line drawing operations. |
| `TextAbsorber` | Extracts text from PDF pages by parsing content streams. |
| `TextBuilder` | Appends text fragments to a PDF page by registering fonts in the page resources and writing content stream operators. |
| `TextEditOptions` | Options that control how text is edited (replaced, font-substituted, language-transformed). |
| `TextExtractionError` | Diagnostic produced by TextFragmentAbsorber when a page-level extraction fails or partially succeeds. |
| `TextExtractionErrorLocation` | Structured location of a TextExtractionError within a page's content stream. |
| `TextExtractionOptions` | Options for text extraction operations. |
| `TextFormattingOptions` | Represents text formatting options for a TextParagraph. |
| `TextFragment` | Represents a fragment of text on a page with position and style information. |
| `TextFragmentAbsorber` | Searches for text fragments on PDF pages, optionally matching a search phrase. |
| `TextFragmentCollection` | A 1-indexed collection of TextFragment objects, matching the public API. |
| `TextFragmentState` | Fragment-level text formatting state. |
| `TextOptions` | Base class for text-processing options. |
| `TextParagraph` | Represents a text paragraph that can be placed on a page via AppendParagraph. |
| `TextReplaceOptions` | Options for text replacement operations. |
| `TextReplacer` | Finds and replaces text in PDF content streams. |
| `TextSearchOptions` | Options for text search operations in PDF documents. |
| `TextSegment` | A single segment within a TextFragment. |
| `TextSegmentCollection` | A 1-indexed collection of TextSegment objects belonging to a TextFragment. |
| `TextState` | Text formatting state. |

#### Interfaces

| Interface | Description |
|---|---|
| `IFontOptions` | Per-font runtime options (currently only the font-embedding error toggle). |

#### Enumerations

| Enumeration | Description |
|---|---|
| `ClippingPathsProcessingMode` | How clipping paths are processed when text edits affect clipped regions. |
| `CoordinateOrigin` | How the lowest Y coordinate of a text fragment is interpreted in positioning APIs (CoordinateOrigin and per-segment SetPosition overloads). |
| `FontReplace` | Font-replacement strategy when the original font cannot encode replacement text. |
| `FontSizeAdjustment` | Font size adjustment strategy when replaced text has a different size. |
| `FontStyles` | Represents a position on a page. |
| `FontSubsetStrategy` | Strategy for font subsetting when saving a document. |
| `FontType` | Font type classification. |
| `FontTypes` | Stream-loadable font formats accepted by Stream, FontTypes). |
| `LanguageTransformation` | Language transformation behavior (RTL, ligatures, etc). |
| `LineSpacingMode` | How line spacing is interpreted (font-derived vs full glyph extent). |
| `NoCharacterAction-Text` | Action to perform if font does not contain required character. |
| `ReplaceAdjustment` | Specifies how text replacement adjustments are handled. |
| `Scope-Text` | Replace-scope selector (canonical screaming-snake naming). |
| `TabAlignmentType` | Tab alignment type for tab stops. |
| `TabLeaderType` | Tab leader character type. |
| `TextFormattingMode` | Specifies how extracted text is formatted. |
| `TextRenderingMode` | PDF text rendering mode (Tr operator). |
| `WordWrapMode-Text` | Word wrap mode that controls how text wraps within a paragraph rectangle. |

### Vector

| Class | Description |
|---|---|
| `GraphicElement` | Single vector-graphics element (path segment, image, text run) extracted from a PDF content stream. |
| `GraphicElementCollection` | Mutable collection of GraphicElement entries. |
| `GraphicsAbsorber` | Extracts the painted vector sub-paths of a page as SubPath elements, each carrying its page-space bounding Rectangle. |
| `SubPath` | A single painted sub-path extracted from a content stream: its construction operators (in their original user-space coordinates), the CTM in effect when it was drawn, and the painting operator that closed it. |
| `XFormPlacement` | Class in the PDF .NET API. |

### Xmp

| Class | Description |
|---|---|
| `Metadata` | XMP metadata accessor exposed by per-resource members such as Metadata. |
| `XmpField` | One named XMP field — a (prefix, local-name) pair plus a typed value. |
| `XmpMetadata` | Provides access to XMP metadata embedded in the PDF. |
| `XmpPacketContainer` | The XMP packet exposed by getData; its WorkingPacket yields the live RDF/XML. |
| `XmpPdfAExtensionField` | One field of a PDF/A XMP-extension structured value type. |
| `XmpPdfAExtensionObject` | One PDF/A XMP-extension property/value pair. |
| `XmpPdfAExtensionProperty` | One PDF/A XMP-extension property declaration (name, value, value type, category, description). |
| `XmpPdfAExtensionSchema` | One PDF/A XMP-extension schema, holding a description and its associated XmpPdfAExtensionObjects. |
| `XmpPdfAExtensionSchemaDescription` | Human-readable description of a PDF/A XMP extension schema. |
| `XmpPdfAExtensionValueType` | One PDF/A XMP-extension structured value type, holding its fields. |
| `XmpValue` | Represents a typed XMP metadata value. |
| `XmpWorkingPacket` | A working XMP packet; GetXml returns the packet's RDF/XML with rdf:RDF as the document element so callers can XPath into it. |

#### Enumerations

| Enumeration | Description |
|---|---|
| `XmpFieldType` | XMP field categories (matches the Aspose.Pdf reflection signature exactly — the underlying integer values are not load-bearing because callers compare against the named members). |
| `XmpPdfAExtensionCategoryType` | Whether a PDF/A XMP-extension property is for external (publicly visible) or internal (private) use. |

---

#### Detailed Member Reference

### Document and Pages

- `Document`
  - `Create() -> Document`
  - `Open(path) -> Document` / `Open(data) -> Document` / `Open(stream) -> Document`
  - `Pages -> PageCollection`
  - `Form -> Form`
  - `Save(path)` / `Save(stream, options)` / `ToArray() -> byte[]`
  - `Encrypt(userPassword, ownerPassword, permissions, algorithm)`
  - `Decrypt()`
- `PageCollection`
  - `Add() -> Page` / `Add(width, height) -> Page`
  - `At(oneBasedIndex) -> Page`
- `Page`
  - `Annotations -> AnnotationCollection`
  - `Resources -> PageResources`

### Text

- `TextAbsorber`
  - `Visit(page)` / `Visit(form)` / `Visit(pdf)`
  - `Text -> string`
- `TextFragmentAbsorber`
  - `Visit(page)`
  - `TextFragments -> TextFragmentCollection`

### Forms

- `Field`
  - `ExportToJson(stream)`
  - `Flatten()`
- `ChoiceField`
  - `Options -> OptionCollection`
  - `AddOption(optionName)` / `AddOption(export, name)`
  - `DeleteOption(optionName)`

### Annotations and Actions

- `AnnotationCollection`
  - `AddLinkAnnotation(rect, action)`
  - `AddStampAnnotation(rect, contents, stampName)`
  - `AddTextAnnotation(rect, contents, title, open)`
  - `AddFreeTextAnnotation(rect, contents, fontName, fontSize, color)`
- `PdfAction`
  - `CreateUri(uri)`
  - `CreateGoTo(pageIndex, fitType)`
  - `CreateJavaScript(script)`
  - `CreateNamed(name)`
  - `CreateLaunch(filePath)`

### Rendering

- `SvgDevice`
  - `Process(page) -> string`
  - `Process(page, output)` / `Process(page, outputFileName)`
- `PngDevice` / `JpegDevice` / `BmpDevice` / `TiffDevice`
  - `Process(page, output)` (inherited from `ImageDevice`)

</details>

## Documentation & Resources

- **[Getting started guide](https://docs.aspose.org/pdf/net/)** — installation, walkthroughs, and
  feature guides for this library.
- **[How-to guides & FAQ](https://kb.aspose.org/pdf/net/)** — task-focused answers for common
  PDF-processing questions.
- **[Full API reference](https://reference.aspose.org/pdf/net/)** — the complete, browsable
  reference for all 854 public types (the API Reference section above covers the essentials).
- Found a bug or have a feature request?
  [Open an issue](https://github.com/aspose-pdf-foss/Aspose.PDF-FOSS-for-.NET/issues) on GitHub.

<details>
<summary>View In-Repo Documentation Guides</summary>

| Guide | Description |
|-------|-------------|
| [Getting Started](docs/getting-started.md) | Installation, first program, core concepts (document lifecycle, 1-based page indexing, saving) |
| [Working with Text](docs/working-with-text.md) | Extract, search, and replace text; build new text with `TextBuilder` / `TextParagraph`; tab stops |
| [Working with Pages](docs/working-with-pages.md) | Add, delete, reorder, rotate, and resize pages; copy pages across documents; merge & split |
| [Fonts](docs/fonts.md) | Standard 14, embedding & subsetting, custom fonts (`FontRepository`), substitution, inspection |
| [Working with Forms](docs/working-with-forms.md) | Read, fill, and build AcroForm fields (text, checkbox, radio, choice, button, signature) |
| [Working with Annotations](docs/working-with-annotations.md) | Read, create, and modify PDF annotations; watermarks; flatten |
| [Bookmarks & Navigation](docs/bookmarks-and-navigation.md) | Outlines/bookmarks, named destinations, and page labels |
| [Security and Encryption](docs/security-and-encryption.md) | Encrypt / decrypt (RC4 / AES-128 / AES-256), document permissions, digital signatures (PKCS#7) |
| [Metadata & XMP](docs/metadata-and-xmp.md) | Document Info dictionary (Title/Author/…) and the XMP metadata packet |
| [Converters](docs/converters.md) | PDF ↔ HTML, PDF ↔ Markdown, PDF ↔ SVG, PDF → plain text |
| [Rendering](docs/rendering.md) | Render pages to PNG, JPEG, BMP, TIFF; SVG output; resolution + color depth control |
| [Optimization](docs/optimization.md) | Compress images, subset fonts, link duplicate streams, PDF/A validation + conversion |
| [Working with Tables](docs/working-with-tables.md) | Extract tabular data with `TableAbsorber`; build tables with `Table` / `Row` / `Cell` |
| [Tagged PDF](docs/tagged-pdf.md) | Read and build `/StructTreeRoot` trees; `ITaggedContent` authoring API |
| [Comparison](docs/comparison.md) | Side-by-side text comparison into a result PDF, graphical page diff, and the `Diff` edit model |
| [Facades](docs/facades.md) | High-level task-oriented APIs (`PdfFileEditor`, `PdfFileSecurity`, `PdfFileSignature`, `PdfBookmarkEditor`, `PdfContentEditor`, ...) |
| [API Reference](docs/api-reference.md) | Public classes organised by namespace, plus a "not included vs Aspose.PDF for .NET" list |

</details>

## Scope and Limitations

- AI / LowCode workflows (`Aspose.Pdf.AI.*`, `Aspose.Pdf.LowCode.*`) are out of scope by design —
  use Aspose.PDF for .NET for OpenAI / Llama-powered summarisation, OCR-aided extraction, and the
  LowCode façade APIs.
- Multithreading APIs (`Aspose.Pdf.Multithreading.*`) are not included; this edition is
  single-threaded for a single `Document` instance.
- Format converters are limited to PDF, HTML, SVG, Markdown, and XML — DOCX, EPUB, MHT, XPS, PCL,
  LaTeX, DJVU, OFD, PostScript, and CGM conversion are not included.
- `PdfViewer`'s `Print*` methods throw `PlatformNotSupportedException`; render to an image and
  pass it to your own printing stack instead.
- Basic digital signatures (sign / verify, PKCS#1, PKCS#7, DocMDP, certifying) work, but the
  validation-options surface — `ValidationOptions`, `ValidationResult`, OCSP, network
  timestamping, custom remote-sign delegates — is stored but not active.
- Advanced `PdfFileEditor` features (`MakeNUp` imposition, `MakeBooklet` from a `Stream` with
  non-trivial margins, `ResizeContents` with custom imposition matrices) are accepted as input,
  but the save path emits a simplified layout.
- Dynamic XFA forms are read, laid out from their own content — field heights, per-edge border
  chrome, captions, rich text — and can be flattened to real AcroForm pages; XFA datasets
  round-trip through the AcroForm `/XFA` entry, sync with AcroForm fields, and export / import via
  FDF, XFDF, and XML. Fine-grained authoring of individual XFA dataset fields is still limited.
- `PDF3DAnnotation` content (artwork, views, cross-sections, lighting) is read and round-trips,
  but the 3D content stream is not regenerated on save and the page renderer does not display the
  3D model.
- Side-by-side comparison is text-based: it compares extracted text runs, so a change that leaves
  the text identical — a recoloured heading, a moved image — does not register. Use
  `GraphicalPdfComparer` for those, bearing in mind it is Windows only.
- The structure tree is built and round-trips through `/StructTreeRoot`, but advanced PDF/UA-2
  layout-aware tagging features are partial.
- PDF/A level validation works, but certain PDF/A-2 / PDF/A-3 conversion fix-ups (transparency
  flattening, embedded-file constraints, ICC profile downgrade) are stored but not applied.
- Complex colour spaces (`DeviceN` / `Separation` with more than 4 channels, ICC-based CMYK group
  blending) render in approximate sRGB.
- Rendering output is pixel-close but not byte-identical to Aspose.PDF for .NET — sub-pixel glyph
  positioning, image dithering, anti-aliasing weight, and widget-appearance details may differ
  slightly.
- `TiffDevice.BinarizeBradley` and `DocumentCollection.GetEnumerator()` are not yet implemented
  and throw `NotImplementedException`.

These limitations don't apply to
[Aspose.PDF for .NET — Enterprise Edition](https://products.aspose.com/pdf/net/), which adds
broader format support and full feature coverage — AI / LowCode workflows, complete XFA authoring,
advanced digital-signature validation, multithreading, and conversion to many additional formats
(DOCX, EPUB, XPS, PCL, PostScript, and more).

## Development and Testing

```bash
dotnet test tests/Aspose.Pdf.Foss.Tests.csproj
```

The test project (`Aspose.Pdf.Foss.Tests`) is an xUnit suite; every runnable example in this
README traces back to a real test method in `tests/` — including
`tests/AnnotationCreationTests.cs`, `tests/Actions/PdfActionTests.cs`,
`tests/ChoiceFieldOptionsTests.cs`, and `tests/Content/ContentExtractionTests.cs`. CI runs the
same restore/build/test sequence on Windows via
[`.github/workflows/build.yml`](.github/workflows/build.yml).

## License

This project is licensed under the [MIT License](LICENSE). The MIT License permits use, copying,
modification, distribution, sublicensing, and commercial use, provided its copyright and
permission notice are retained. The software is provided without warranty.
