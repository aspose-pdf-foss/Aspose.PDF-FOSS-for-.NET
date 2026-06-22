#nullable disable
#pragma warning disable CS0067 // unused events

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Facades;

namespace Aspose.Pdf
{
    public class SaveOptions
    {
        public enum NodeLevelResourceType
        {
            Font = 0,
            Image = 1,
        }

        public class ResourceSavingInfo
        {
            /// <summary>Decoded resource content stream supplied by the saver.</summary>
            public System.IO.Stream ContentStream;
            /// <summary>Whether the caller decided to drop this resource.</summary>
            public bool CustomProcessingCancelled;
            /// <summary>Default file name proposed by the saver.</summary>
            public string SupposedFileName;
            /// <summary>Optional raw bytes (FOSS-only convenience over the stream).</summary>
            public byte[] ContentStreamData;
            /// <summary>What kind of resource this entry represents.</summary>
            public NodeLevelResourceType ResourceType { get; internal set; }
        }
        public class BorderInfo
        {
            public BorderInfo() { }
            public BorderInfo(BorderPartStyle commonStyle)
            {
                TopStyleIfAny = RightStyleIfAny = BottomStyleIfAny = LeftStyleIfAny = commonStyle;
            }
            public BorderPartStyle TopStyleIfAny;
            public BorderPartStyle RightStyleIfAny;
            public BorderPartStyle BottomStyleIfAny;
            public BorderPartStyle LeftStyleIfAny;
        }

        public class BorderPartStyle
        {
            public BorderPartStyle() { }
            public int WidthInPoints { get; set; }
            public System.Drawing.Color Color;
            public HtmlBorderLineType LineType;
        }

        public enum HtmlBorderLineType
        {
            None,
            Dotted,
            Dashed,
            Solid,
            Double,
            Groove,
            Ridge,
            Inset,
            Outset,
        }

        /// <summary>Per-side margin description used by save options that
        /// surface a page-margin field (e.g. <see cref="HtmlSaveOptions.PageMarginIfAny"/>).</summary>
        public class MarginInfo
        {
            public MarginInfo() { }
            public MarginInfo(MarginPartStyle commonMargin)
            {
                TopMarginIfAny = RightMarginIfAny = BottomMarginIfAny = LeftMarginIfAny = commonMargin;
            }
            public MarginPartStyle TopMarginIfAny;
            public MarginPartStyle RightMarginIfAny;
            public MarginPartStyle BottomMarginIfAny;
            public MarginPartStyle LeftMarginIfAny;
        }

        /// <summary>One side of a <see cref="MarginInfo"/>: either a fixed
        /// PDF-point value or an auto-fit hint.</summary>
        public class MarginPartStyle
        {
            public MarginPartStyle(bool isAuto) { IsAuto = isAuto; }
            public MarginPartStyle(int valueInPoints) { ValueInPoints = valueInPoints; }
            public bool IsAuto { get; set; }
            public int ValueInPoints { get; set; }
        }
        public bool CloseResponse { get; set; }
        public bool SaveFullPath { get; set; }

        /// <summary>Warning handler invoked during serialisation. The string-typed
        /// FOSS-extra <c>WarningHandlerName</c> sibling preserves the legacy text shape.</summary>
        public IWarningCallback WarningHandler { get; set; }

        /// <summary>Legacy string-typed warning-handler hook (FOSS-only extra).</summary>
        public string WarningHandlerName { get; set; }

        /// <summary>Pre-cache rasterised glyphs to speed up repeated text emission. Stored only.</summary>
        public bool CacheGlyphs { get; set; }

        /// <summary>The save format that this options instance configures.</summary>
        public virtual SaveFormat SaveFormat => SaveFormat.Pdf;
    }

    public class LoadOptions
    {
        /// <summary>Warning handler invoked while loading the source document.</summary>
        public IWarningCallback WarningHandler { get; set; }

        /// <summary>Legacy string-typed warning-handler hook (FOSS-only extra).</summary>
        public string WarningHandlerName { get; set; }

        /// <summary>Whether font license verifications are suppressed during load. Stored only.</summary>
        public bool DisableFontLicenseVerifications { get; set; }

        /// <summary>The source format this options instance configures.</summary>
        public virtual LoadFormat LoadFormat => LoadFormat.HTML;
        public class ResourceLoadingResult
        {
            public ResourceLoadingResult(byte[] data) { Data = data; }
            public byte[] Data { get; }

            /// <summary>Whether the caller asked the loader to drop this resource (Aspose.PDF for .NET-shape field).</summary>
            public bool LoadingCancelled;

            /// <summary>Text encoding declared by the resource, if any.</summary>
            public System.Text.Encoding EncodingIfKnown;

            /// <summary>Exception raised while fetching the resource, if any.</summary>
            public System.Exception ExceptionOfLoadingIfAny;

            /// <summary>MIME type declared by the resource, if any.</summary>
            public string MIMETypeIfKnown;
        }

        /// <summary>Callback invoked by the HTML loader to resolve an external resource by URI.</summary>
        public delegate ResourceLoadingResult ResourceLoadingStrategy(string resourceURI);
    }

    public class PdfSaveOptions : SaveOptions
    {
        /// <summary>Default font used when an embedded font cannot be resolved. Stored only.</summary>
        public string DefaultFontName { get; set; }

        /// <summary>Filesystem path used for temporary save artifacts. Stored only.</summary>
        public string TempPath { get; set; }
    }

    public class UnifiedSaveOptions : SaveOptions
    {
        public bool ExtractOcrSublayerOnly { get; set; }
        public bool IsMultiThreading;
        public bool TryMergeAdjacentSameBackgroundImages;

        public class ProgressEventHandlerInfo
        {
            public System.Guid DocumentId;
            public ProgressEventType EventType;
            public int MaxValue;
            public int Value;
        }

        public delegate void ConversionProgressEventHandler(ProgressEventHandlerInfo eventInfo);
    }

    /// <summary>
    /// Conversion-progress milestones reported via
    /// <see cref="UnifiedSaveOptions.ProgressEventHandlerInfo.EventType"/>.
    /// </summary>
    public enum ProgressEventType
    {
        TotalProgress,
        SourcePageAnalysed,
        ResultPageCreated,
        ResultPageSaved,
    }

    public class SvgSaveOptions : UnifiedSaveOptions
    {
        public bool CompressOutputToZipArchive;
        public bool TreatTargetFileNameAsDirectory;
        public bool ScaleToPixels;
        public EmbeddedImagesSavingStrategy CustomStrategyOfEmbeddedImagesSaving;
        public class SvgImageSavingInfo
        {
            public System.IO.Stream ImageStream { get; set; }
            public string ImageFileName { get; set; }
            /// <summary>Image format hint for the SVG writer.</summary>
            public SvgExternalImageType ImageType;
        }
        /// <summary>Callback returning the file name to use for an embedded image during SVG save.</summary>
        public delegate string EmbeddedImagesSavingStrategy(SvgImageSavingInfo imageSavingInfo);

        /// <summary>Image format used by the SVG embedded-image saver.</summary>
        public enum SvgExternalImageType
        {
            Jpeg = 0,
            Png = 1,
            Bmp = 2,
            Gif = 3,
            Tiff = 4,
            Unknown = 5,
        }
    }
}
