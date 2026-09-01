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
| `XmpPdfAExtensionSchema` / `XmpPdfAExtensionField` / `XmpPdfAExtensionProperty` / `XmpPdfAExtensionValueType` / `XmpPdfAExtensionSchemaDescription` / `XmpPdfAExtensionObject` | PDF/A XMP extension nodes |
| `XmpPacketContainer` / `XmpWorkingPacket` | Raw XMP packet accessors                           |
| `FileSpecification`         | Embedded-file specification                                  |
| `FileParams`                | File-spec parameter dictionary                               |
| `EmbeddedFileCollection`    | Document-level embedded files                                |
| `Collection` / `CollectionSchema` / `CollectionField` / `CollectionItem` / `EncryptedPayload` | Portable-collection schema and items |
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
| `CompositingParameters`     | Blend-mode / opacity parameters                              |
| `NamedDestination` / `NamedDestinationCollection` / `DestinationCollection` / `DestinationArray` | Named destinations |
| `OutlineItem` / `Outlines` / `OutlineCollection` / `OutlineItemCollection` / `OutlineBuilder` / `OutlineItemBuilder` | Bookmark / outline entries and builders |
| `OptionalContentBuilder` / `OptionalContentGroup` / `OptionalContentProperties` / `LayerEntry` / `Layer` / `LayerCollection` | Optional-content (layer) data |
| `OutputIntent` / `OutputIntents` | PDF/X output intents                                    |
| `Stamp` / `TextStamp` / `PageNumberStamp` / `PdfPageStamp` | Stamp base plus the text, page-number and PDF-page stamps applied to pages |
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
| `Image`                     | Image paragraph for generator content                        |
| `ColumnInfo`                | Column layout settings                                       |
| `WebHyperlink` / `LocalHyperlink` / `FileHyperlink` | Hyperlink targets for generator content |
| `PaginationArtifact` / `BatesNArtifact` / `PageCollectionExtensions` | Bates numbering and pagination artifacts (`AddBatesNumbering`, `AddPagination`, `UpdatePagination`, `DeleteBatesNumbering` on `PageCollection`) |
| `ImagePlacement` / `ImagePlacementCollection` / `ImagePlacementAbsorber` | Placed-image extraction |
| `FontUtilities`             | Font utility methods                                         |
| `ExportFieldsToJsonOptions` / `FieldSerializationResult` / `FieldExportingData` / `AcroFormData` / `AppearanceEntry` / `AppearanceImageData` / `DefaultResourcesData` | Form-field JSON export model (`AppearanceImageData` carries a widget appearance's decoded image) |
| `SaveOptions` / `PdfSaveOptions` / `UnifiedSaveOptions` / `SvgSaveOptions` / `LoadOptions` | Save / load option bases |
| `MdLoadOptions` / `SvgLoadOptions` / `TxtLoadOptions` / `PageSizeInfo` | Markdown / SVG / text import options |
| `Document.MergeOptions` / `Document.RepairOptions` | Merge and repair settings (nested in `Document`) |
| `DocumentCollection`        | Set of documents                                             |
| `PageResources` / `Resources` / `XForm` / `XFormCollection` / `ExtGStateValue` / `Opi` | Page resource dictionaries and form XObjects |
| `JavaScriptCollection`      | Document-level JavaScript entries                            |
| `PageActionCollection`      | Page open / close actions                                    |
| `Watermark` / `Group`       | Page watermark and transparency-group dictionaries           |
| `Id`                        | The two byte strings of the trailer `/ID` array              |
| `InterruptMonitor`          | Cooperative cancellation for long operations                 |
| `BuildVersionInfo`          | Library build and version information                        |
| `IWarningCallback` / `WarningInfo` | Warning reporting during load / save                  |
| `OcspSettings` / `TimestampSettings` | Signature OCSP and timestamp settings               |
| `OperatorCollection` / `BaseOperatorCollection` / `ContentsAppender` / `RawOperator` / `OperatorSelector` / `IOperatorSelector` | Content-stream operator access |
| `OptimizedMemoryStream` / `ZDeflaterOutputStream` / `ZInflaterInputStream` | Stream helpers |
| `BoundsCheckableList` / `PolygonsHelper` | Bounds-checked list and polygon geometry helpers     |
| `PdfException` / `InvalidPasswordException` / `InvalidPdfFileFormatException` / `FontNotFoundException` / `IncorrectFontUsageException` / `UnsupportedFontTypeException` / `PdfTextDecodingException` / `InvalidFormTypeOperationException` / `DeprecatedFeatureException` / `BoundsOutOfRangeException` / `CrashReportOptions` / `PdfExceptionMessages` | Exception hierarchy |
| `EmptyValueException`       | Thrown when a required value is left empty (e.g. `DateField.Init` on a field without a `PartialName`) |
| `FontEmbeddingException`    | Raised when a font's licence forbids embedding it into the document |
| `MissingOptionalDependencyException` | Raised when an optional package the operation needs (System.Drawing.Common for printing) is not referenced |

Enumerations: `BorderSide`, `BorderCornerStyle`, `BlendMode`, `ColorSpace`,
`ColorType`, `ColumnAdjustment`, `ConvertErrorAction`,
`ConvertTransparencyAction`, `ConvertSoftMaskAction`, `CryptoAlgorithm`,
`DigestHashAlgorithm`, `Permissions`, `Direction`, `DefaultState`,
`ExtendedBoolean`, `FieldValueType`, `FieldSerializationStatus`, `Fixup`,
`FontSubsetStrategy`, `HeadingRecognitionStrategy`, `HorizontalAlignment`,
`ImageDeleteAction`, `ImageFileType`, `ImageFilterType`, `LaunchActionOperation`,
`LoadFormat`, `NumberingStyle`, `PageLayout`, `PageLayoutMode`, `PageMode`,
`PageModeValue`, `PageCoordinateType`, `ParagraphPositioningMode`,
`PasswordType`, `PdfFormat`, `PdfAStandardVersion`, `PdfVersion`, `PrintDuplex`,
`PrintScaling`, `Rotation`, `SaveFormat`, `Subset`, `TabOrder`, `TableBroken`,
`VerticalAlignment`, `WarningType`, `ProgressEventType`, `ReturnAction`,
`ExtractTextMode`, `ExtractImageMode`, `ArtifactType`, `ArtifactSubtype`,
`HtmlDocumentType`, `HtmlMediaType`, `HtmlPageLayoutOption`,
`ConversionEngines`, `PuaProcessingStrategy`, `RemoveFontsStrategy`,
`SegmentAlignStrategy`, `XmpPdfAExtensionCategoryType`, `XmpFieldType`,
`AFRelationship`, `FileEncoding`.

`SaveFormat` and `LoadFormat` also carry members for formats this library does
not implement (see [Not included](#not-included)); `Document.Save` throws
`NotSupportedException` for a `SaveFormat` other than `Pdf`, `Html`, `Markdown`,
`Xml` or `Svg`.

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
| `FontSource` / `FileFontSource` / `FolderFontSource` / `MemoryFontSource` / `SystemFontSource` | Font discovery sources |
| `FontSourceCollection`      | Registered font sources                                      |
| `FontSubstitution` / `SimpleFontSubstitution` / `CustomFontSubstitutionBase` / `OriginalFontSpecification` / `FontSubstitutionCollection` | Font substitution rules |
| `FontData` / `PdfFontView` / `IFontOptions` | Font program data and engine-font views              |
| `ExternalFontCache`         | Folders searched for external (non-embedded) fonts           |
| `PhysicalTextSegment`       | Page-space projection of an absorbed `TextSegment`           |
| `TextOptions`               | Base for the text edit / search option classes               |
| `OneBasedList`              | Read-only list with a 1-based indexer                        |
| `RegexManager`              | Global regex settings for text search (match timeout, non-backtracking engine) |

Enumerations: `FontStyles`, `FontType` / `FontTypes`, `CoordinateOrigin`,
`ClippingPathsProcessingMode`, `FontReplace`, `FontSizeAdjustment`,
`LanguageTransformation`, `LineSpacingMode`, `NoCharacterAction`,
`ReplaceAdjustment`, `Scope`, `TabAlignmentType`, `TabLeaderType`,
`TextFormattingMode`, `TextRenderingMode`, `WordWrapMode`.
`ImagePlacement*`, `FontUtilities` and `FontSubsetStrategy` live in the
top-level `Aspose.Pdf` namespace.

## `Aspose.Pdf.Forms`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `Form`                      | Interactive AcroForm with field collection                   |
| `Field`                     | Base class for every form field                              |
| `TextBoxField` / `RichTextBoxField` | Text inputs                                          |
| `DateField`                 | Text field with a date format and a popup JavaScript calendar (`Init(page)` wires the script) |
| `BarcodeField`              | Barcode-bearing text field (`Symbology`)                     |
| `CheckboxField`             | Checkbox                                                     |
| `RadioButtonField`          | Radio button group                                           |
| `RadioButtonGroup` / `RadioButtonOption` / `RadioButtonOptionField` | Radio modelling     |
| `ChoiceField` / `ComboBoxField` / `ListBoxField` | Dropdown / list                         |
| `Option` / `OptionCollection` | Option entries on a choice field                           |
| `ButtonField`               | Push button                                                  |
| `SignatureField`            | Digital-signature field                                      |
| `FormFieldBuilder`          | Create form fields on a page                                 |
| `XFA` / `XfaAccessor` / `XfaField` | XFA data accessors and template fields by SOM path    |
| `Form.FlattenSettings`      | Flattening settings (nested in `Form`)                       |
| `Signature` / `PKCS1` / `PKCS7` / `PKCS7Detached` / `ExternalSignature` | Signature value wrappers (attached, detached, externally computed) |
| `SignHash` (delegate)       | Callback that signs a digest for `ExternalSignature`        |
| `SignatureCustomAppearance` | Custom signature appearance                                  |
| `DocMDPSignature`           | Document-MDP certifying signature                            |
| `IconFit`                   | Icon-fit settings for button fields                          |

Enumerations: `FieldType`, `BoxStyle`, `BoxShape`, `FormType`,
`IconCaptionPosition`, `ScalingMode`, `ScalingReason`,
`DocMDPAccessPermissions`, `SignDependentElementsRenderingModes`,
`SubjectNameElements`, `Symbology`. The JSON export types
(`ExportFieldsToJsonOptions`, `FieldSerializationResult`,
`FieldSerializationStatus`) live in the top-level `Aspose.Pdf` namespace.

## `Aspose.Pdf.Annotations`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `Annotation`                | Base annotation class                                        |
| `AnnotationCollection`      | Page annotations with `Add*` helpers                         |
| `AnnotationSelector`        | Visitor base for typed annotation dispatch                   |
| `LinkAnnotation`            | Hyperlink                                                    |
| `TextAnnotation`            | Sticky note                                                  |
| `FreeTextAnnotation`        | Text rendered directly on a page                             |
| `MarkupAnnotation` / `TextMarkupAnnotation` / `CommonFigureAnnotation` / `PolyAnnotation` | Bases for markup, text-markup, shape and polygon annotations |
| `GenericAnnotation`         | Annotation of a subtype without a dedicated class            |
| `PrinterMarkAnnotation`     | Base for the pre-press marks                                 |
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
| `Rendition` / `MediaRendition` / `SelectorRendition` / `MediaClip` / `MediaClipData` / `MediaClipSection` | Rendition objects played by screen annotations |
| `Characteristics`           | Annotation rotation / border / background                    |
| `DefaultAppearance`         | Default appearance (DA) string wrapper                       |
| `Border` / `Dash`           | Annotation border configuration                              |
| `ExplicitDestination` / `XYZExplicitDestination` / `FitExplicitDestination` / `FitBExplicitDestination` / `FitHExplicitDestination` / `FitVExplicitDestination` / `FitBHExplicitDestination` / `FitBVExplicitDestination` / `FitRExplicitDestination` | Destinations |
| `Measure` / `NumberFormat` / `NumberFormatList` | Measure dictionary and its number formats           |
| `IAppointment`              | Marker for objects an outline item can point at (actions, destinations) |
| `RichTextToFlatStructureTransformer` | Flattens free-text rich text to plain runs           |
| `JavascriptExtensions.FieldDateTimeFormatter` / `FieldNumberCurrencyFormatter` / `FieldNumberPercentFormatter` | Acrobat-style field formatting (`Aspose.Pdf.Annotations.JavascriptExtensions`) |
| `PdfActionCollection`       | Annotation-level action collection                           |
| `AnnotationActionCollection`| Widget event-action collection                               |
| `AppearanceDictionary`      | Appearance-stream collection (`AP /N /D /R`)                 |
| `DocumentActionCollection`  | Document-level open / close / save actions                   |
| `FixedPrint`                | FixedPrint dictionary                                        |
| `TextStyle`                 | Free-text rich-text style                                    |
| `SoundData` / `SoundSampleData` | Sound annotation payload                                 |
| `PDF3DAnnotation` / `PDF3DContent` / `PDF3DStream` / `PDF3DLightingScheme` / `PDF3DRenderMode` / `PDF3DCuttingPlaneOrientation` / `PDF3DCrossSection` / `PDF3DCrossSectionArray` / `PDF3DView` / `PDF3DViewArray` / `PDF3DArtwork` | 3D-content model read from the `/3DD` stream; `Content` assigned to an annotation read from a document is written back to that stream |
| `BleedMarkAnnotation` / `ColorBarAnnotation` / `PageInformationAnnotation` / `RegistrationMarkAnnotation` / `TrimMarkAnnotation` | Pre-press marks |

Enumerations: `AnnotationType`, `AnnotationFlags`, `AnnotationState`,
`AnnotationStateModel`, `ReplyType`, `BorderStyle`, `BorderEffect`,
`CapStyle`, `CaptionPosition`, `ColorsOfCMYK`, `FileIcon`, `HighlightingMode`,
`LineEnding`, `LineIntent`, `PolyIntent`, `StampIcon`, `TextIcon`,
`PredefinedAction`, `PrinterMarksKind`, `PrinterMarkCornerPosition`,
`PrinterMarkSidePosition`, `TextAlignment`, `Justification`, `FreeTextIntent`,
`RichTextFontStyles`, `ExplicitDestinationType`, `LightingSchemeType`,
`RenderModeType`, `SoundEncoding`, `SoundIcon`,
`SoundSampleDataEncodingFormat`, `PDF3DActivation`, `ActionType`.

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
| `VerificationResult`        | Outcome of `PdfFileSignature.TryVerifySignature`             |
| `SignatureAlgorithmInfo` / `TimestampAlgorithmInfo` | Algorithms used by a signature / its timestamp |
| `EncryptionInfo`            | Encryption details of an opened document                     |
| `SignatureLengthMismatchException` | Thrown when a produced signature does not fit the reserved `/Contents` space |
| `Sha3_256` / `Sha3_384` / `Sha3_512` / `HashAlgorithmFactory` | SHA-3 implementations and digest factory |
| `MathExtensions`            | Numeric helpers used by the crypto code                      |
| `HiddenDataSanitization.HiddenDataSanitizer` / `HiddenDataSanitizationOptions` | Strip hidden data (metadata, annotations, scripts, attachments, layers) or rasterise pages (`Aspose.Pdf.Security.HiddenDataSanitization`) |

Enumerations: `SignatureAlgorithmType`, `CryptographicStandard`,
`ValidationMethod`, `ValidationMode`, `ValidationStatus`, `VerificationState`.
`CryptoAlgorithm`, `DigestHashAlgorithm` and `Permissions` live in the
top-level `Aspose.Pdf` namespace.

## `Aspose.Pdf.Converters`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `PdfToHtmlConverter`        | PDF -> HTML                                                  |
| `PdfToMarkdownConverter`    | PDF -> Markdown                                              |
| `PdfToSvgConverter`         | PDF -> SVG                                                   |
| `PdfToTextConverter`        | PDF -> plain text                                            |
| `MarkdownConverterOptions`  | Heading thresholds, table support                            |
| `PdfToMarkdown.MarkdownSaveOptions` | Markdown save options (`Aspose.Pdf.PdfToMarkdown`)   |

The import load options (`MdLoadOptions`, `SvgLoadOptions`, `TxtLoadOptions`,
`PageSizeInfo`) live in the top-level `Aspose.Pdf` namespace.

## `Aspose.Pdf.Devices`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `ImageDevice`               | Abstract base for image rendering devices                    |
| `PngDevice`                 | Render pages to PNG                                          |
| `JpegDevice`                | Render pages to JPEG                                         |
| `BmpDevice`                 | Render pages to BMP                                          |
| `TiffDevice`                | Render pages to TIFF (single / multi-page)                   |
| `GifDevice`                 | Render pages to GIF                                          |
| `ThumbnailDevice`           | Render pages to thumbnail PNGs                               |
| `SvgDevice`                 | Render pages to SVG (vector)                                 |
| `TextDevice`                | Extract text via the device API                              |
| `DocumentDevice` / `PdfDocumentDevice` | Document-level device base / PDF round-trip device |
| `PageDevice` / `ImagePageDevice` | Page-level device bases                                 |
| `IPageRenderer`             | Pluggable rendering backend                                  |
| `SoftwarePageRenderer`      | Built-in pure-managed renderer (all platforms)               |
| `GdiPlusPageRenderer`       | GDI+ renderer — `[SupportedOSPlatform("windows")]`           |
| `RgbaBuffer`                | Raw RGBA pixel buffer                                        |
| `Resolution`                | DPI resolution settings                                      |
| `TiffSettings` / `Margins`  | TIFF encoder configuration                                   |
| `IndexBitmapConverter`      | Base for index-bitmap quantisation helpers (`IIndexBitmapConverter` is in `Aspose.Pdf`) |
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
| `PositionSettings`          | Position adjustment passed to `StructureElement.AdjustPosition` |
| `TaggedException`           | Thrown on tagged-content errors                              |

## `Aspose.Pdf.LogicalStructure`

The typed logical-structure element hierarchy reached when walking an
existing `/StructTreeRoot` tree, and returned by the `ITaggedContent`
factories when authoring one.

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `Element` / `StructureElement` | Element base / tagged structure-element base              |
| `StructTreeRootElement`     | Root of the logical-structure tree                           |
| `SpanElement` / `ParagraphElement` / `HeaderElement` / `FigureElement` / `NoteElement` / `AnnotElement` / `ArtElement` / `SectElement` / `PartElement` / `DivElement` / `LinkElement` / `FormElement` | Typed structure elements |
| `DocumentElement` / `BlockQuoteElement` / `CaptionElement` / `TOCElement` / `TOCIElement` / `IndexElement` / `NonStructElement` / `PrivateElement` | Grouping elements |
| `QuoteElement` / `CodeElement` / `ReferenceElement` / `BibEntryElement` / `FormulaElement` / `IllustrationElement` | Inline and illustration elements |
| `RubyElement` / `RubyChildElement` / `RubyRBElement` / `RubyRTElement` / `RubyRPElement` | Ruby annotation and its RB / RT / RP children |
| `WarichuElement` / `WarichuChildElement` / `WarichuWTElement` / `WarichuWPElement` | Warichu and its WT / WP children |
| `MCRElement` / `OBJRElement` | Marked-content and object-reference leaves                  |
| `ListElement` / `ListLIElement` / `ListLBodyElement` / `ListLblElement` | List structure elements |
| `TableElement` / `TableTRElement` / `TableTDElement` / `TableTHElement` / `TableTHeadElement` / `TableTBodyElement` / `TableTFootElement` | Table structure elements |
| `StructureTypeStandard` / `StructureTypeCategory` | The standard structure types (`P`, `H1`, `Table`, `Ruby`, ...) with their category (grouping, block-level, inline-level, illustration) |
| `StructureType`             | Raw `/S` role tag of an element                              |
| `StructureAttributes` / `StructureElementAttributes` / `StructureAttribute` / `AttributeName` | Structure-element attribute dictionaries |
| `StructureTextState`        | Text state applied to authored structure content             |
| `ElementList` / `ITextElement` | Child list and text-bearing element contract              |
| `HeaderElementTextConflictException` / `TOCpageHasNoTitleException` | Tagged table-of-contents errors |

Enumerations: `AttributeKey`, `AttributeOwnerStandard`.

## `Aspose.Pdf.Facades`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `PdfFileEditor`             | Merge, split, extract, insert, delete pages; tagged inputs stay tagged |
| `PdfFileEditor.ContentsResizeParameters` / `ContentsResizeValue` / `PageBreak` / `CorruptedItem` | Resize parameters, page breaks and corrupted-input records (nested in `PdfFileEditor`) |
| `PdfFileSanitization`       | Byte-level repair of damaged files (trim header / trailer waste, rebuild xref and trailer) |
| `PdfFileSecurity`           | Encrypt, decrypt, change passwords                           |
| `PdfFileSignature`          | Sign, verify, inspect signatures                             |
| `FormEditor`                | Fill, flatten, create, remove form fields                    |
| `Form` / `FormImportResult` | Facade-level form access and import outcome                  |
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
| `PdfConverter`              | Page-to-image conversion cursor (`DoConvert` / `GetNextImage` / `SaveAsTIFF`) |
| `PdfExtractor`              | Extract text, images, and attachments                        |
| `PdfXmpMetadata`            | XMP metadata accessor                                        |
| `PdfViewer`                 | Page decoding and printing surface: `DecodePage` (Windows only), print-to-PDF-file via `PrintDocumentWithSettings`; spooler printing throws `PlatformNotSupportedException` |
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
| `LineInfo`                  | Line parameters for `PdfContentEditor.DrawCurve` / polygons  |
| `IFacade` / `ISaveableFacade` | Facade contracts (`BindPdf`, `Save`)                       |
| `PdfQueryPageSettingsEventHandler` (delegate) | `PdfViewer` page-settings event               |

Enumerations: `FieldType` (facade variant), `KeySize`, `Algorithm`,
`SubmitFormFlag`, `PropertyFlag`, `ImageMergeMode`, `BlendingColorSpace`,
`StampType`, `EncodingType`, `FontStyle`, `WordWrapMode`, `PositioningMode`,
`PdfConverterImageFormat`, `DataType`, `DefaultMetadataProperties`,
`AutoRotateMode`, `ImportStatus`, `Scope`, `NoCharacterAction`,
`PdfFileEditor.ConcatenateCorruptedFileAction`. The printing support types
(`PrinterSettings`, `PageSettings`, `PrintingOptionalDependencyGuard`, the print
event args) live in `Aspose.Pdf.Printing`.

## `Aspose.Pdf.Drawing`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `Graph`                     | Container for drawable shapes on a page                      |
| `Shape`                     | Abstract base for shapes                                     |
| `Line` / `DrawingRectangle` / `Circle` / `Ellipse` / `Arc` / `Polygon` / `Curve` / `DrawingPath` / `Rectangle` | Concrete shapes |
| `Path`                      | Composite shape: its child shapes' outlines form one path painted with the `Path`'s own `GraphInfo` |
| `Color`                     | Drawing colour (RGB) with named presets                      |
| `Point`                     | Drawing-space point                                          |
| `GradientAxialShading`      | Axial-gradient fill                                          |
| `PatternColorSpace`         | Pattern colour space                                         |

Enumerations: `ImageFormat`.

## `Aspose.Pdf.Actions`

The action classes are declared in the `Aspose.Pdf.Annotations` namespace
(there is no separate `Aspose.Pdf.Actions` namespace); they are listed here as
a group.

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `PdfAction`                 | Base action class                                            |
| `GoToAction`                | Navigate to an in-document destination                       |
| `UriAction` / `GoToURIAction` | Open a URI                                                 |
| `GoToRemoteAction`          | Navigate to another file                                     |
| `LaunchAction`              | Launch external content                                      |
| `NamedAction`               | Built-in named action (NextPage, PrevPage, ...)              |
| `HideAction`                | Set the hidden flag of named fields / annotations            |
| `ImportDataAction`          | Import form data from an FDF file                            |
| `RenditionAction`           | Controls playback of multimedia (rendition) content          |
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
| `WatermarkStamp`            | Watermark stamp                                              |

`PageNumberStamp`, `PdfPageStamp` and `ImageStamp` (plus a second `Stamp` /
`TextStamp` pair) live in the top-level `Aspose.Pdf` namespace; `StampInfo` /
`StampType` live in `Aspose.Pdf.Facades`.

## `Aspose.Pdf.Content`

| Class                       | Description                                                  |
|-----------------------------|--------------------------------------------------------------|
| `ContentStreamBuilder`      | Build PDF content streams                                    |
| `ExtGState`                 | Graphics-state parameter dictionary                          |
| `GraphicsState`             | Live graphics-state snapshot                                 |
| `PathExtractor` / `PathSegment` / `ExtractedPath` | Vector-path extraction                |
| `PathCommand`               | One path-construction command with its coordinates           |

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
| `GraphicsAbsorber`          | Extracts a page's painted vector sub-paths                   |
| `SubPath`                   | One painted sub-path with its paint and transform            |
| `XFormPlacement`            | A form-XObject invocation found in a content stream          |

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
| `Operation` | `Equal`, `Delete`, `Insert` — the kind of a `DiffOperation` |
| `EditOperationsOrder` | `DeleteFirst`, `InsertFirst` — order in which merged delete / insert pairs are emitted |
| `SideBySideDocsComparisonResult` | `HasChanges`, per-page `FirstDocChanges` / `SecondDocChanges`, per-page `FullChanges` |
| `SideBySidePagesComparisonResult` | `HasChanges`, `FirstPageChanges` / `SecondPageChanges`, `FullChanges` |
| `EditContainer` | One highlighted change: `Id`, its `DiffOperation`, and the `Rects` it covers |
| `GraphicalPdfComparer` | Pixel comparison of two pages (`Resolution`, `Color`, `Threshold`) — **Windows only** |
| `ImagesDifference` | `Difference` / `Stride` / `Height`, `SourceImage`, `GetDestinationImage()`, `DifferenceToImage()` — **Windows only** |

## `Aspose.Pdf.Comparison.Diff`

| Type | Purpose |
|------|---------|
| `DiffOperation` | One edit: an `Operation` (from `Aspose.Pdf.Comparison`) plus its `Text` |
| `DiffUtils` | `FindCommonStartParts`, `FindCommonEndParts`, `AssemblySourceText`, `AssemblyDestinationText` |
| `DiffOptimization.IDiffOptimizationOperation` / `OperationsMerger` / `MergingOptimizer` / `OperationsSlideMerger` | Edit-sequence normalisers (`Aspose.Pdf.Comparison.Diff.DiffOptimization`) |

See [Comparison](comparison.md) for worked examples.

## Not included

The following surface areas are intentionally not part of this library:

- `Aspose.Pdf.AI`, `Aspose.Pdf.LowCode`, `Aspose.Pdf.Plugins`
- DOCX / EPUB / XPS / PCL / LaTeX / DJVU / OFD / PostScript converters — the
  `SaveFormat` / `LoadFormat` members exist, but `Document.Save` throws
  `NotSupportedException` for them and no import path reads them
- 3D-content rendering — `PDF3D*` annotations are read (artwork, views,
  cross-sections) and a `Content` assigned to an annotation read from a document is
  written back to its `/3DD` stream, but a newly created `PDF3DAnnotation` writes
  no 3D stream and the model is not displayed
- Native (spooler) printing — `PdfViewer.PrintDocument`, `PrintDocumentWithSetup`,
  `PrintDocuments` and `PrintLargePdf` throw `PlatformNotSupportedException`;
  `PrintDocumentWithSettings` only handles `PrinterSettings.PrintToFile` with a
  `.pdf` target. `Aspose.Pdf.Printing.PrintingOptionalDependencyGuard` reports a
  missing System.Drawing.Common package as `MissingOptionalDependencyException`
