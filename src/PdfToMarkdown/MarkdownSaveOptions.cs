#nullable disable

namespace Aspose.Pdf.PdfToMarkdown
{
    /// <summary>
    /// Save options for the PDF → Markdown converter (<c>doc.Save(path, new MarkdownSaveOptions())</c>).
    /// </summary>
    public class MarkdownSaveOptions : SaveOptions
    {
        /// <summary>
        /// Name of the sub-directory (created next to the output file) that extracted
        /// image/vector resources are written into and referenced from.
        /// </summary>
        public string ResourcesDirectoryName { get; set; } = "resources";

        /// <summary>
        /// When <c>true</c>, extracted images are referenced with an HTML <c>&lt;img&gt;</c>
        /// tag instead of the <c>![](...)</c> Markdown image syntax.
        /// </summary>
        public bool UseImageHtmlTag { get; set; }

        /// <summary>
        /// Restrict extraction to the given page rectangle. When <c>null</c> the whole page is used.
        /// </summary>
        public Rectangle AreaToExtract { get; set; }

        /// <summary>
        /// When <c>true</c>, vector graphics are rasterised/serialised and emitted as image resources.
        /// </summary>
        public bool ExtractVectorGraphics { get; set; }

        /// <summary>Whether leading list markers are recognised and emitted as Markdown bullets.</summary>
        public bool RecognizeBullets { get; set; }

        /// <summary>
        /// Horizontal proximity (as a fraction of the space width) below which two runs on the
        /// same line are considered adjacent rather than separated by a space.
        /// </summary>
        public float RelativeHorizontalProximity { get; set; }

        /// <inheritdoc/>
        public override SaveFormat SaveFormat => SaveFormat.Markdown;
    }
}
