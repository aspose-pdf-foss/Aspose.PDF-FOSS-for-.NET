# API Reference

Public classes organised by namespace. Helper types and content-stream
operator wrappers are summarised at the bottom.

## `Aspose.Pdf`

Top-level document model, page model, geometry, metadata, and the
PDF-format / conversion enums.

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `Document`                  | Main entry point — open, create, and save PDF documents      |
| `Document.OptimizationOptions` | Nested alias of `Optimization.OptimizationOptions`        |
| `Page`                      | A single page; carries content, annotations, resources       |
| `PageCollection`            | Page list (1-based indexer)                                  |
| `PageInfo`                  | Default page dimensions and margins                          |
| `PageLabel`                 | Page-label numbering entry                                   |
| `PageLabelCollection`       | Per-document page-label entries                              |
| `PageLabelBuilder`          | Builder for page labels                                      |
| `PageSize`                  | Standard page sizes (A4, Letter, ...)                        |
| `PageTransition`            | Page transition effect                                       |
| `Rectangle`                 | PDF rectangle (LLX, LLY, URX, URY)                           |
| `Point` / `Point3D`         | 2D / 3D point                                                |
| `Matrix` / `Matrix3D`       | 2D / 3D affine transformation matrix                         |
| `MarginInfo`                | Margins (left, bottom, right, top)                           |
| `DocumentInfo`              | Document metadata (title, author, subject, ...)              |
| `Metadata`                  | XMP metadata dictionary                                      |
| `XmpMetadata`               | Legacy XMP metadata accessor                                 |
| `XmpField` / `XmpValue`     | Individual XMP entries                                       |
| `XmpPdfAExtensionSchema` / `XmpPdfAExtensionField` / `XmpPdfAExtensionProperty` / `XmpPdfAExtensionValueType` / `XmpPdfAExtensionSchemaDescription` | PDF/A XMP extension nodes |
| `FileSpecification`         | Embedded-file specification                                  |
| `FileParams`                | File-spec parameter dictionary                               |
| `EmbeddedFileCollection`    | Document-level embedded files                                |
| `CollectionItem` / `EncryptedPayload` | Portable-collection items                          |
| `FloatingBox`               | Floating content container                                   |
| `HeaderFooter`              | Page header / footer content                                 |
| `HtmlFragment`              | HTML rendered into a page                                    |
| `HtmlLoadOptions`           | HTML-to-PDF load options                                     |
| `HtmlSaveOptions`           | PDF-to-HTML save options                                     |
| `ImageStamp`                | Image stamp                                                  |
| `ImageCollection` / `XImageCollection` / `ImageXObject` / `XImage` | Image resources         |
| `BorderInfo`                | Border configuration for tables / cells                      |
| `GraphInfo`                 | Fill / stroke / dash / opacity for shapes                    |
| `Color`                     | RGB / grayscale colour with named presets                    |
| `ColorSpace`                | Colour-space wrapper                                         |
| `CompositingParameters`     | Blend-mode / opacity parameters                              |
| `NamedDestination`          | Named destination in a document                              |
| `OutlineItem` / `Outlines`  | Bookmark / outline entries                                   |
| `OptionalContent` / `OptionalContentBuilder` | OCG (layer) data                            |
| `OutputIntent` / `OutputIntents` | PDF/X output intents                                    |
| `Stamp`                     | Abstract base for stamps                                     |
| `Table` / `Row` / `Cell` / `Rows` / `Cells` | Table model for page content                 |
| `TocInfo` / `Heading` / `LevelFormat` | Table-of-contents configuration                    |
| `ValidationIssue`           | Document-validation issue                                    |
| `ViewerPreferences`         | PDF viewer preferences                                       |
| `Hyperlink`                 | Inline hyperlink reference                                   |
| `RenderingOptions`          | Top-level rendering options                                  |
| `Operator`                  | Base content-stream operator                                 |
| `Artifact` / `BackgroundArtifact` / `WatermarkArtifact` / `ArtifactCollection` | Page artifacts |
| `PdfFormatConversionOptions` | PDF/A or PDF/X conversion configuration                     |
| `HeadingLevels` / `AutoTaggingSettings` / `FontEmbeddingOptions` / `PdfANonSpecificationFlags` / `PdfASymbolicFontEncodingStrategy` / `ToUnicodeProcessingRules` | Conversion-tuning types |
| `RgbToDeviceGrayConversionStrategy` | RGB->DeviceGray reduction strategy                   |
| `Note`                      | Footnote / endnote                                           |
| `BaseParagraph`             | Base for queued page paragraphs                              |
| `Paragraphs`                | Page-paragraph collection                                    |

Enumerations: `BorderSide`, `BlendMode`, `ColorType`, `ConvertErrorAction`,
`ConvertTransparencyAction`, `ConvertSoftMaskAction`, `Direction`,
`ExtendedBoolean`, `FieldValueType`, `Fixup`, `HeadingRecognitionStrategy`,
`HorizontalAlignment`, `ImageDeleteAction`, `LoadFormat`, `NumberingStyle`,
`PageLayout`, `PageLayoutMode`, `PageMode`, `PageModeValue`,
`PageCoordinateType`, `ParagraphPositioningMode`, `PdfFormat`, `PdfVersion`,
`PrintDuplex`, `Rotation`, `SaveFormat`, `VerticalAlignment`,
`XmpPdfAExtensionCategoryType`, `XmpFieldType`, `AFRelationship`,
`FileEncoding`.

## `Aspose.Pdf.Text`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `TextAbsorber`              | Extract all text from a page or document                     |
| `TextFragmentAbsorber`      | Search for text fragments by phrase or regex                 |
| `ParagraphAbsorber` / `ParagraphAbsorberOptions` | Extract paragraphs / sections           |
| `TableAbsorber`             | Detect and extract tables from pages                         |
| `AbsorbedTable` / `AbsorbedRow` / `AbsorbedCell` | Extracted tabular data                  |
| `PageMarkup` / `MarkupSection` / `MarkupParagraph` | Paragraph absorber output             |
| `TextFragment`              | A piece of text with position, font, and style               |
| `TextFragmentCollection`    | Collection of found fragments (1-based)                      |
| `TextSegment` / `TextSegmentCollection` | Segments within a fragment                       |
| `CharInfo` / `CharInfoCollection` | Per-character metadata                                 |
| `TextState`                 | Font name, size, colour, bold, italic, underline             |
| `TextFragmentState`         | `TextState` subclass with extra authoring knobs              |
| `Position`                  | X / Y position on a page                                     |
| `TextBuilder`               | Append fragments / paragraphs to a page                      |
| `TextParagraph`             | Multi-line paragraph with formatting                         |
| `TextReplacer`              | Find-and-replace text across pages                           |
| `TextSearchOptions`         | Search configuration (regex, case, area)                     |
| `TextExtractionOptions`     | Extraction-mode configuration                                |
| `TextEditOptions`           | Text-edit configuration                                      |
| `TextReplaceOptions`        | Replacement adjustment options                               |
| `TextFormattingOptions`     | Word wrap and formatting options                             |
| `TextExtractionError` / `TextExtractionErrorLocation` | Extraction-diagnostic info         |
| `TabStops` / `TabStop`      | Tab-stop configuration                                       |
| `Font` / `FontInfo`         | Font metadata                                                |
| `FontCollection`            | Document fonts                                               |
| `FontRepository`            | Font lookup and resolution                                   |
| `FontAbsorber`              | Collect font usage from a document                           |
| `FontEmbedder`              | Embed fonts into a document                                  |
| `FontUtilities`             | Font utility methods                                         |
| `FontSource` / `FileFontSource` / `FolderFontSource` / `MemoryFontSource` / `SystemFontSource` | Font discovery sources |
| `FontSourceCollection`      | Registered font sources                                      |
| `FontSubstitution` / `SimpleFontSubstitution` / `FontSubstitutionCollection` | Font substitution rules |
| `ImagePlacement` / `ImagePlacementCollection` / `ImagePlacementAbsorber` | Placed-image extraction |

Enumerations: `FontStyles`, `FontType` / `FontTypes`, `FontSubsetStrategy`,
`TabAlignmentType`, `TabLeaderType`, `TextRenderingMode`.

## `Aspose.Pdf.Forms`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `Form`                      | Interactive AcroForm with field collection                   |
| `Field`                     | Base class for every form field                              |
| `TextBoxField` / `RichTextBoxField` | Text inputs                                          |
| `CheckboxField`             | Checkbox                                                     |
| `RadioButtonField`          | Radio button group                                           |
| `RadioButtonGroup` / `RadioButtonOption` / `RadioButtonOptionField` | Radio modelling     |
| `ChoiceField` / `ComboBoxField` / `ListBoxField` | Dropdown / list                         |
| `Option` / `OptionCollection` | Option entries on a choice field                           |
| `ButtonField`               | Push button                                                  |
| `SignatureField`            | Digital-signature field                                      |
| `FormFieldBuilder`          | Create form fields on a page                                 |
| `XFA`                       | XFA data accessor                                            |
| `Signature` / `PKCS1` / `PKCS7` | Signature value wrappers                                 |
| `SignatureCustomAppearance` | Custom signature appearance                                  |
| `DocMDPSignature`           | Document-MDP certifying signature                            |
| `ExportFieldsToJsonOptions` | JSON export configuration                                    |
| `FieldSerializationResult`  | Result of a JSON export                                      |
| `IconFit`                   | Icon-fit settings for button fields                          |

Enumerations: `FieldType`, `BoxStyle`, `FormType`, `IconCaptionPosition`,
`ScalingMode`, `ScalingReason`, `DocMDPAccessPermissions`,
`FieldSerializationStatus`, `SubjectNameElements`.

## `Aspose.Pdf.Annotations`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `Annotation`                | Base annotation class                                        |
| `AnnotationCollection`      | Page annotations with `Add*` helpers                         |
| `AnnotationFactory`         | Factory for raw annotation dictionaries                      |
| `AnnotationSelector`        | Visitor base for typed annotation dispatch                   |
| `LinkAnnotation`            | Hyperlink                                                    |
| `TextAnnotation`            | Sticky note                                                  |
| `FreeTextAnnotation`        | Text rendered directly on a page                             |
| `MarkupAnnotation`          | Base for highlight / underline / strikeout / squiggly        |
| `HighlightAnnotation` / `UnderlineAnnotation` / `StrikeOutAnnotation` / `SquigglyAnnotation` | Markup variants |
| `SquareAnnotation` / `CircleAnnotation` | Shape annotations                                |
| `LineAnnotation` / `PolygonAnnotation` / `PolylineAnnotation` | Line / polygon                |
| `InkAnnotation`             | Freehand ink                                                 |
| `StampAnnotation`           | Rubber stamp                                                 |
| `CaretAnnotation`           | Caret insertion point                                        |
| `PopupAnnotation`           | Popup body for markup annotations                            |
| `WidgetAnnotation`          | Form-widget annotation                                       |
| `FileAttachmentAnnotation`  | File attachment                                              |
| `RedactionAnnotation` / `RedactAnnotation` | Redaction                                     |
| `WatermarkAnnotation`       | Watermark overlay                                            |
| `MovieAnnotation` / `ScreenAnnotation` / `SoundAnnotation` / `RichMediaAnnotation` | Media   |
| `Characteristics`           | Annotation rotation / border / background                    |
| `DefaultAppearance`         | Default appearance (DA) string wrapper                       |
| `Border` / `Dash`           | Annotation border configuration                              |
| `ExplicitDestination` / `XYZExplicitDestination` / `FitExplicitDestination` / `FitBExplicitDestination` / `FitHExplicitDestination` / `FitVExplicitDestination` / `FitBHExplicitDestination` / `FitBVExplicitDestination` / `FitRExplicitDestination` | Destinations |
| `Measure`                   | Line-annotation measure                                      |
| `PdfActionCollection`       | Annotation-level action collection                           |
| `AnnotationActionCollection`| Widget event-action collection                               |
| `AppearanceDictionary`      | Appearance-stream collection (`AP /N /D /R`)                 |
| `DocumentActionCollection`  | Document-level open / close / save actions                   |
| `FixedPrint`                | FixedPrint dictionary                                        |
| `TextStyle`                 | Free-text rich-text style                                    |
| `SoundData` / `SoundSampleData` | Sound annotation payload                                 |
| `PDF3DAnnotation` / `PDF3DContent` / `PDF3DStream` / `PDF3DLightingScheme` / `PDF3DRenderMode` / `PDF3DCuttingPlaneOrientation` / `PDF3DCrossSection` / `PDF3DCrossSectionArray` / `PDF3DView` / `PDF3DViewArray` / `PDF3DArtwork` | 3D-content scaffolding (no 3D content is emitted on save) |
| `BleedMarkAnnotation` / `ColorBarAnnotation` / `PageInformationAnnotation` / `RegistrationMarkAnnotation` / `TrimMarkAnnotation` | Pre-press marks |

Enumerations: `AnnotationType`, `AnnotationFlags`, `AnnotationState`,
`AnnotationStateModel`, `ReplyType`, `BorderStyle`, `BorderEffect`,
`TextAlignment`, `Justification`, `FreeTextIntent`, `RichTextFontStyles`,
`ExplicitDestinationType`, `LightingSchemeType`, `RenderModeType`,
`SoundEncoding`, `SoundIcon`, `SoundSampleDataEncodingFormat`,
`PDF3DActivation`.

## `Aspose.Pdf.Security`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `PdfCertificate`            | Digital certificate for signing                              |
| `PdfSigner`                 | Sign and verify PDF signatures                               |
| `SignatureOptions`          | Signing parameters (reason / location / field name)          |
| `SignatureAppearance`       | Visible-signature appearance                                 |
| `CertificateEncryptionOptions` | Public-key encryption options                             |
| `EncryptionParameters` / `ICustomSecurityHandler` | Custom security-handler extension      |
| `ValidationOptions` / `ValidationResult` | Signature-validation configuration              |
| `BitString`                 | ASN.1 bit-string used by certificate processing              |

Enumerations: `CryptoAlgorithm`, `ValidationMethod`, `ValidationMode`,
`ValidationStatus`.

## `Aspose.Pdf.Converters`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `PdfToHtmlConverter`        | PDF -> HTML                                                  |
| `PdfToMarkdownConverter`    | PDF -> Markdown                                              |
| `PdfToSvgConverter`         | PDF -> SVG                                                   |
| `PdfToTextConverter`        | PDF -> plain text                                            |
| `MdLoadOptions`             | Markdown-to-PDF load options                                 |
| `SvgLoadOptions`            | SVG-to-PDF load options                                      |
| `MarkdownConverterOptions`  | Heading thresholds, table support                            |
| `PageSizeInfo`              | Page dimensions used by import converters                    |

## `Aspose.Pdf.Devices`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `ImageDevice`               | Abstract base for image rendering devices                    |
| `PngDevice`                 | Render pages to PNG                                          |
| `JpegDevice`                | Render pages to JPEG                                         |
| `BmpDevice`                 | Render pages to BMP                                          |
| `TiffDevice`                | Render pages to TIFF (single / multi-page)                   |
| `SvgDevice`                 | Render pages to SVG (vector)                                 |
| `TextDevice`                | Extract text via the device API                              |
| `DocumentDevice` / `PdfDocumentDevice` | Document-level device base / PDF round-trip device |
| `PageDevice` / `ImagePageDevice` | Page-level device bases                                 |
| `IPageRenderer`             | Pluggable rendering backend                                  |
| `SoftwarePageRenderer`      | Built-in pure-managed renderer                               |
| `RgbaBuffer`                | Raw RGBA pixel buffer                                        |
| `Resolution`                | DPI resolution settings                                      |
| `TiffSettings` / `Margins`  | TIFF encoder configuration                                   |
| `IndexBitmapConverter`      | Base for index-bitmap quantisation helpers                   |
| `PageSize`                  | TIFF page-size hint                                          |
| `JpegEncoder` (delegate)    | Pluggable JPEG encoder callback                              |

Enumerations: `ColorDepth`, `CompressionType`, `FormPresentationMode`,
`ShapeType`.

## `Aspose.Pdf.Optimization`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `OptimizationOptions`       | What to optimise (objects, fonts, images)                    |
| `ImageCompressionOptions`   | Image-compression sub-options                                |

Enumerations: `ImageCompressionVersion`, `ImageEncoding`.

PDF/A and PDF/X profile types (`PdfFormat`, `PdfFormatConversionOptions`,
`ConvertErrorAction`, etc.) live in the top-level `Aspose.Pdf` namespace.

## `Aspose.Pdf.Tagged`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `ITaggedContent`            | Author-facing tagged-content surface                         |
| `TaggedContent`             | `ITaggedContent` implementation                              |
| `StructTreeRoot`            | Document structure-tree root                                 |
| `StructTreeElement`         | Structure-tree element node                                  |
| `StructureTreeBuilder`      | Builder for the structure tree                               |
| `StructureElementBuilder`   | Fluent element builder                                       |
| `MarkedContentInfo`         | Marked-content sequence info                                 |
| `TaggedException`           | Thrown on tagged-content errors                              |

## `Aspose.Pdf.LogicalStructure`

The typed logical-structure element hierarchy reached when walking an
existing `/StructTreeRoot` tree.

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `Element` / `StructureElement` | Element base / tagged structure-element base              |
| `StructTreeRootElement`     | Root of the logical-structure tree                           |
| `SpanElement` / `ParagraphElement` / `HeaderElement` / `FigureElement` / `NoteElement` / `AnnotElement` / `ArtElement` / `SectElement` / `PartElement` / `DivElement` / `LinkElement` / `FormElement` | Typed structure elements |
| `ListElement` / `ListLIElement` / `ListLBodyElement` / `ListLblElement` | List structure elements |
| `TableElement` / `TableTRElement` / `TableTDElement` / `TableTHElement` / `TableTHeadElement` / `TableTBodyElement` / `TableTFootElement` | Table structure elements |
| `PositionSettings`          | Position adjustment for a structure element                  |

## `Aspose.Pdf.Facades`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `PdfFileEditor`             | Merge, split, extract, insert, delete pages                  |
| `PdfFileSecurity`           | Encrypt, decrypt, change passwords                           |
| `PdfFileSignature`          | Sign, verify, inspect signatures                             |
| `FormEditor`                | Fill, flatten, create, remove form fields                    |
| `Form`                      | Facade-level form access                                     |
| `FormDataConverter`         | Convert form data between FDF / XML / DataTable              |
| `FormFieldFacade`           | Field-appearance settings                                    |
| `FormattedText` / `FormattedTextFont` / `FontColor` | Rich text for facade APIs            |
| `PdfBookmarkEditor`         | Create and modify bookmarks                                  |
| `Bookmark` / `Bookmarks`    | Bookmark entries                                             |
| `PdfContentEditor`          | Edit content, stamps, annotations, links                     |
| `PdfAnnotationEditor`       | Delete, flatten, redact annotations                          |
| `PdfPageEditor`             | Page-level edits (rotate, resize, page sizes)                |
| `PdfFileInfo`               | Read / update document metadata                              |
| `PdfFileMend`               | Add text / images to existing pages                          |
| `PdfFileStamp`              | Add header / footer / page-number stamps                     |
| `PdfJavaScriptStripper`     | Remove JavaScript from a PDF                                 |
| `PdfConverter`              | Batch PDF/A normalisation wrapper                            |
| `PdfExtractor`              | Extract text, images, and attachments                        |
| `PdfXmpMetadata`            | XMP metadata accessor                                        |
| `PdfViewer`                 | Page-printing scaffolding (printing operations throw `PlatformNotSupportedException`) |
| `Stamp` / `StampInfo`       | Stamp object / extracted stamp info                          |
| `DocumentPrivilege`         | Document permission flags                                    |
| `ReplaceTextStrategy`       | Text-replace tuning knobs                                    |
| `RenderingOptions`          | Facade-level rendering options                               |
| `AlignmentType` / `VerticalAlignmentType` | Alignment constants                            |
| `AutoFiller`                | Auto-fill helper                                             |
| `ViewerPreference`          | Bit-flag viewer-preference constants                         |
| `BDCProperties`             | BDC properties dictionary                                    |
| `TextProperties`            | Text-properties container                                    |
| `SignatureName`             | Composite signature-name descriptor                          |

Enumerations: `FieldType` (facade variant), `KeySize`, `Algorithm`,
`SubmitFormFlag`, `PropertyFlag`, `ImageMergeMode`, `BlendingColorSpace`,
`StampType`, `EncodingType`, `FontStyle`, `WordWrapMode`, `PositioningMode`,
`PdfConverterImageFormat`, `DataType`, `DefaultMetadataProperties`.

## `Aspose.Pdf.Drawing`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `Graph`                     | Container for drawable shapes on a page                      |
| `Shape`                     | Abstract base for shapes                                     |
| `Line` / `DrawingRectangle` / `Circle` / `Ellipse` / `Arc` / `Polygon` / `Curve` / `DrawingPath` / `Rectangle` | Concrete shapes |
| `Color`                     | Drawing colour (RGB) with named presets                      |
| `Point`                     | Drawing-space point                                          |
| `GradientAxialShading`      | Axial-gradient fill                                          |
| `PatternColorSpace`         | Pattern colour space                                         |

Enumerations: `ImageFormat`.

## `Aspose.Pdf.Actions`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `PdfAction`                 | Base action class                                            |
| `GoToAction`                | Navigate to an in-document destination                       |
| `UriAction` / `GoToURIAction` | Open a URI                                                 |
| `GoToRemoteAction`          | Navigate to another file                                     |
| `LaunchAction`              | Launch external content                                      |
| `NamedAction`               | Built-in named action (NextPage, PrevPage, ...)              |
| `JavascriptAction`          | Execute JavaScript                                           |
| `SubmitFormAction`          | Submit AcroForm data                                         |
| `ActionCollection`          | Annotation action list                                       |

Enumerations: `ActionType`.

## `Aspose.Pdf.Shading`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `ShadingBase`               | Abstract base for shading dictionaries                       |
| `FunctionBasedShading`      | Type-1 function-based shading                                |
| `AxialShading`              | Type-2 axial shading                                         |
| `RadialShading`             | Type-3 radial shading                                        |
| `FreeFormGouraudShading`    | Type-4 free-form Gouraud-shaded triangle mesh                |
| `LatticeFormGouraudShading` | Type-5 lattice-form Gouraud mesh                             |
| `CoonsPatchShading`         | Type-6 Coons patch mesh                                      |
| `TensorPatchShading`        | Type-7 tensor-product patch mesh                             |
| `Pattern` / `TilingPattern` / `ShadingPattern` | Pattern types                             |

Enumerations: `ShadingType`.

## `Aspose.Pdf.Functions`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `PdfFunction`               | Abstract PDF-function base                                   |
| `ExponentialFunction`       | Type-2 exponential function                                  |
| `StitchingFunction`         | Type-3 stitching function                                    |
| `SampledFunction`           | Type-0 sampled function                                      |
| `PostScriptFunction`        | Type-4 PostScript function                                   |
| `PostScriptEvaluator`       | Type-4 evaluator helper                                      |

## `Aspose.Pdf.Stamps`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `Stamp`                     | Abstract base for stamps                                     |
| `TextStamp`                 | Text stamp                                                   |
| `PageNumberStamp`           | Page-number stamp                                            |
| `WatermarkStamp`            | Watermark stamp                                              |
| `PdfPageStamp`              | Stamp sourced from another PDF page                          |
| `StampInfo` / `StampType`   | Stamp metadata and type enum                                 |

## `Aspose.Pdf.Content`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `ContentStreamBuilder`      | Build PDF content streams                                    |
| `ExtGState`                 | Graphics-state parameter dictionary                          |
| `GraphicsState`             | Live graphics-state snapshot                                 |
| `PathExtractor` / `PathSegment` / `ExtractedPath` | Vector-path extraction                |

Enumerations: `PathOp`, `PathOperationType`, `PathPaintMode`.

## `Aspose.Pdf.Operators`

Typed wrappers around every PDF content-stream operator. `Operator` (in
`Aspose.Pdf`) is the base type; the typed subclasses live here.

The full set includes path-construction operators (`MoveTo`, `LineTo`,
`CurveTo`, `Re`, `ClosePath`), painting operators (`Stroke`, `Fill`,
`EOFill`, `FillStroke`, `ClosePathFillStroke`, `EndPath`), state operators
(`GSave`, `GRestore`, `Clip`, `EOClip`, `SetLineWidth`, `SetLineCap`,
`SetLineJoin`, `SetMiterLimit`, `SetDash`, `SetFlat`, `GS`, `ConcatenateMatrix`),
text operators (`BT`, `ET`, `ShowText`, `MoveTextPosition`,
`MoveTextPositionSetLeading`, `MoveToNextLine`, `SetTextMatrix`,
`SetTextLeading`, `SetTextRenderingMode`, `SelectFont`,
`SetCharacterSpacing`, `SetWordSpacing`, `SetHorizontalTextScaling`,
`SetTextRise`), colour operators (`SetRGBColor`, `SetRGBColorStroke`,
`SetCMYKColor`, `SetCMYKColorStroke`, `SetGray`, `SetGrayStroke`,
`SetColor`, `SetColorStroke`, `SetAdvancedColor`, `SetAdvancedColorStroke`,
`SetColorSpace`, `SetColorSpaceStroke`, `SetColorRenderingIntent`), and
marked-content / inline-image operators (`BMC`, `BDC`, `EMC`, `MP`, `DP`,
`BX`, `EX`, `BI`, `ID`, `EI`, `Do`, `ShFill`).

Enumerations: `LineCap`, `LineJoin`.

## `Aspose.Pdf.Vector`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `GraphicElement`            | Vector page-content element                                  |
| `GraphicElementCollection`  | Vector-element list                                          |

## `Aspose.Pdf.Structure`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `Element`                   | Lightweight structure-element base                           |
| `ElementCollection`         | Element collection                                           |
| `RootElement` / `StructElement` / `TextElement` / `FigureElement` | Element variants    |

## `Aspose.Pdf.Comparison`

| Type | Purpose |
|------|---------|
| `SideBySidePdfComparer` | Static `Compare` overloads for two pages or two documents, writing a side-by-side result PDF to a path or stream |
| `SideBySideComparisonOptions` | Whitespace `ComparisonMode`, comparison / exclusion areas, `ExcludeTables`, `DeleteColor` / `InsertColor`, `AdditionalChangeMarks` |
| `ComparisonMode` | `Normal`, `IgnoreSpaces`, `ParseSpaces` |
| `SideBySideDocsComparisonResult` | `HasChanges`, per-page `FirstDocChanges` / `SecondDocChanges`, per-page `FullChanges` |
| `SideBySidePagesComparisonResult` | `HasChanges`, `FirstPageChanges` / `SecondPageChanges`, `FullChanges` |
| `EditContainer` | One highlighted change: `Id`, its `DiffOperation`, and the `Rects` it covers |
| `GraphicalPdfComparer` | Pixel comparison of two pages (`Resolution`, `Color`, `Threshold`) — **Windows only** |
| `ImagesDifference` | `Difference` / `Stride` / `Height`, `SourceImage`, `GetDestinationImage()`, `DifferenceToImage()` — **Windows only** |

## `Aspose.Pdf.Comparison.Diff`

| Type | Purpose |
|------|---------|
| `DiffOperation` | One edit: an `Operation` plus its `Text` |
| `Operation` | `Equal`, `Delete`, `Insert` |
| `DiffUtils` | `FindCommonStartParts`, `FindCommonEndParts`, `AssemblySourceText` |

See [Comparison](comparison.md) for worked examples.

## Not included

The following surface areas are intentionally not part of this library:

- `Aspose.Pdf.AI`, `Aspose.Pdf.LowCode`, `Aspose.Pdf.Plugins`
- DOCX / EPUB / XPS / PCL / LaTeX / DJVU / OFD / PostScript converters
- 3D-content rendering / save — `PDF3D*` annotations are read (artwork, views,
  cross-sections) but no 3D content is written on save and the model is not displayed
- Native printing — `PdfViewer.Print*` throws `PlatformNotSupportedException`
