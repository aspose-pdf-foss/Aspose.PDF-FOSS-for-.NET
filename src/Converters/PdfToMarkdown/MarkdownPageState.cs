using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.PdfToMarkdown;

internal static partial class MarkdownRenderer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MarkdownPageState
{
    public Aspose.Pdf.Text.TextFragmentAbsorber absorber = null!;
    public List<Aspose.Pdf.Text.TextFragment> fragments = null!;
    public List<Aspose.Pdf.PdfToMarkdown.MarkdownRenderer.LinkInfo> links = null!;
    public List<Aspose.Pdf.PdfToMarkdown.MarkdownRenderer.MdBlock> tableBlocks = null!;
    // Group text into lines first — these provide the bands used to decide whether an
    // image sits inline within a line or forms its own image block.
    public List<Aspose.Pdf.PdfToMarkdown.MarkdownRenderer.Line> textLines = null!;
    // Classify each image inline vs block, group block images into rows, then number
    // every image by first appearance in reading order (rows top-down, within-row LLX
    // ascending; inline images at their host line) and save the unique PNGs.
    public List<(Aspose.Pdf.PdfToMarkdown.MarkdownRenderer.ImgPlace img, Aspose.Pdf.PdfToMarkdown.MarkdownRenderer.Line host)> inlineImgs = null!;
    public List<Aspose.Pdf.PdfToMarkdown.MarkdownRenderer.ImgPlace> blockImgs = null!;
    public List<Aspose.Pdf.PdfToMarkdown.MarkdownRenderer.ImgPlace> placements = null!;
    public List<List<Aspose.Pdf.PdfToMarkdown.MarkdownRenderer.ImgPlace>> rows = null!;
    public List<(Aspose.Pdf.PdfToMarkdown.MarkdownRenderer.ImgPlace ip, double repY, double llx)> order = null!;
    public List<Aspose.Pdf.PdfToMarkdown.MarkdownRenderer.Line> imageLines = null!;
    public List<Aspose.Pdf.PdfToMarkdown.MarkdownRenderer.MdBlock> local = null!;
    public List<Aspose.Pdf.PdfToMarkdown.MarkdownRenderer.Line> textOnly = null!;
    public double bodySize;
    public List<double> headingSizes = null!;
    // Build text blocks from text lines only (headings/paragraphs). A block image never
    // splits a paragraph, so image-row blocks are added separately and merged by top
    // edge; the block sort places a row before a paragraph when its top is higher.
    public List<Aspose.Pdf.PdfToMarkdown.MarkdownRenderer.Line> tls = null!;
    public Page page = default!;
    public int pageNumber = 0;
    public MarkdownSaveOptions options = default!;
    public List<HeadingDest> outlineDests = default!;
    public ImageNumberer images = default!;
    public List<MdBlock> blocks = default!;
    // the table regions the text and image passes exclude
    public List<Rectangle> tableRegions = null!;
}
}
