using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileEditor
{
    private static byte[] ResizePages(byte[] pdf, double width, double height)
    {
        using var doc = Document.Open(pdf);
        for (int i = 1; i <= doc.PageCount; i++)
            doc.Pages[i].SetPageSize(width, height);
        return doc.ToArray();
    }

    /// <summary>
    /// Resize the contents of all pages in a document by applying a scale/translate
    /// transform derived from the margin parameters.
    /// </summary>
    public void ResizeContents(Document source, ContentsResizeParameters parameters)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));

        for (int i = 1; i <= source.PageCount; i++)
            ResizePage(source.Pages.At(i), parameters);
    }

    /// <summary>
    /// Resize the contents of specific pages in a document by applying a scale/translate
    /// transform derived from the margin parameters.
    /// </summary>
    public void ResizeContents(Document source, int[] pages, ContentsResizeParameters parameters)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (pages is null) throw new ArgumentNullException(nameof(pages));
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));

        foreach (var pageNum in pages)
        {
            if (pageNum < 1 || pageNum > source.PageCount) continue;
            ResizePage(source.Pages.At(pageNum), parameters);
        }
    }

    private static void ResizePage(Page page, ContentsResizeParameters parameters)
    {
        var box = page.MediaBox;
        double w = box.Width;
        double h = box.Height;

        // Left margin, content width and right margin partition the page width
        // (their sum is the page width); top margin, content height and bottom
        // margin partition the page height. Any value left unspecified ("auto")
        // shares the space remaining after the fixed values equally.
        Partition(parameters.LeftMargin, parameters.ContentsWidth, parameters.RightMargin,
            w, out double left, out double contentW, out double right);
        Partition(parameters.TopMargin, parameters.ContentsHeight, parameters.BottomMargin,
            h, out double top, out double contentH, out double bottom);

        double sx = contentW / w;
        double sy = contentH / h;
        double tx = left;
        double ty = bottom;

        page.ApplyContentResizeAsForm(sx, sy, tx, ty);

        // Annotations live in /Annots (not the content stream), so the content-form
        // transform above doesn't reach them. Apply the same affine to their
        // geometry so ink strokes / rects / quadpoints track the resized content.
        TransformAnnotationGeometry(page, sx, sy, tx, ty);

        // Normalize degenerate shape-annotation appearances (part of
        // resize-with-normalization): a Square/Circle that ships a missing or empty /N
        // appearance stream gets a freshly regenerated /AP /N.
        NormalizeDegenerateShapeAppearances(page);

        // The margins plus content span the new page box. When that differs from
        // the current media box (e.g. fixed-zero margins shrinking the page to the
        // content size), resize the page boxes to match.
        double newW = left + contentW + right;
        double newH = top + contentH + bottom;
        if (Math.Abs(newW - w) > 1e-6 || Math.Abs(newH - h) > 1e-6)
            page.Rect = new Rectangle(box.LLX, box.LLY, box.LLX + newW, box.LLY + newH);
    }

    /// <summary>Split <paramref name="total"/> across the three slots (leading
    /// margin, content, trailing margin). Fixed (non-auto) slots resolve against
    /// <paramref name="total"/>; the remaining space is divided equally among the
    /// auto slots.</summary>
    private static void Partition(
        ContentsResizeValue? lead, ContentsResizeValue? content, ContentsResizeValue? trail,
        double total, out double leadOut, out double contentOut, out double trailOut)
    {
        bool leadAuto    = lead    is null || lead.IsAutoInternal;
        bool contentAuto = content is null || content.IsAutoInternal;
        bool trailAuto   = trail   is null || trail.IsAutoInternal;

        double fixedSum = 0;
        if (!leadAuto)    fixedSum += lead!.ResolveAgainst(total);
        if (!contentAuto) fixedSum += content!.ResolveAgainst(total);
        if (!trailAuto)   fixedSum += trail!.ResolveAgainst(total);

        int autoCount = (leadAuto ? 1 : 0) + (contentAuto ? 1 : 0) + (trailAuto ? 1 : 0);
        double autoShare = autoCount > 0 ? (total - fixedSum) / autoCount : 0;

        leadOut    = leadAuto    ? autoShare : lead!.ResolveAgainst(total);
        contentOut = contentAuto ? autoShare : content!.ResolveAgainst(total);
        trailOut   = trailAuto   ? autoShare : trail!.ResolveAgainst(total);
    }

    /// <summary>
    /// Represents a content-resize value. A value can be auto (let the engine derive it),
    /// a percentage of the corresponding page dimension, or an absolute number of points.
    /// </summary>
    public sealed class ContentsResizeValue
    {
        private double _value;
        private bool _isPercent;
        private bool _isAuto;

        private ContentsResizeValue(double value, bool isPercent, bool isAuto)
        {
            _value = value;
            _isPercent = isPercent;
            _isAuto = isAuto;
        }

        /// <summary>The numeric value (percentage or points, depending on <see cref="IsPercent"/>).</summary>
        public double Value => _value;

        /// <summary>True when <see cref="Value"/> is a percentage of the page dimension.</summary>
        public bool IsPercent => _isPercent;

        /// <summary>Set this value as a percentage of the page dimension.</summary>
        public double PercentValue
        {
            set { _value = value; _isPercent = true; _isAuto = false; }
        }

        /// <summary>Set this value as an absolute number of points.</summary>
        public double UnitValue
        {
            set { _value = value; _isPercent = false; _isAuto = false; }
        }

        /// <summary>Create an auto-sized value (engine derives it from the surrounding constraints).</summary>
        public static ContentsResizeValue Auto() => new(0, false, true);

        /// <summary>Create a value expressed as a percentage of the page dimension.</summary>
        public static ContentsResizeValue Percents(double value) => new(value, true, false);

        /// <summary>Create a value expressed as absolute points.</summary>
        public static ContentsResizeValue Units(double value) => new(value, false, false);

        internal bool IsAutoInternal => _isAuto;

        internal double ResolveAgainst(double pageDim)
            => _isPercent ? pageDim * _value / 100.0 : _value;
    }

    /// <summary>
    /// Parameters for <see cref="ResizeContents"/> describing margins, content size,
    /// and whether the page media box should change to match.
    /// </summary>
    public sealed class ContentsResizeParameters
    {
        /// <summary>Left margin, or <see cref="ContentsResizeValue.Auto"/> for engine-derived.</summary>
        public ContentsResizeValue? LeftMargin { get; set; }
        /// <summary>Content width, or <see cref="ContentsResizeValue.Auto"/> for engine-derived.</summary>
        public ContentsResizeValue? ContentsWidth { get; set; }
        /// <summary>Right margin, or <see cref="ContentsResizeValue.Auto"/> for engine-derived.</summary>
        public ContentsResizeValue? RightMargin { get; set; }
        /// <summary>Top margin, or <see cref="ContentsResizeValue.Auto"/> for engine-derived.</summary>
        public ContentsResizeValue? TopMargin { get; set; }
        /// <summary>Content height, or <see cref="ContentsResizeValue.Auto"/> for engine-derived.</summary>
        public ContentsResizeValue? ContentsHeight { get; set; }
        /// <summary>Bottom margin, or <see cref="ContentsResizeValue.Auto"/> for engine-derived.</summary>
        public ContentsResizeValue? BottomMargin { get; set; }

        /// <summary>Whether the page media box should be resized along with the content.</summary>
        public bool ChangeMediaBox { get; set; }

        /// <summary>Initialise an empty parameter set (all values auto).</summary>
        public ContentsResizeParameters() { }

        /// <summary>Initialise with explicit values for every margin / content dimension.</summary>
        public ContentsResizeParameters(
            ContentsResizeValue leftMargin,
            ContentsResizeValue contentsWidth,
            ContentsResizeValue rightMargin,
            ContentsResizeValue topMargin,
            ContentsResizeValue contentsHeight,
            ContentsResizeValue bottomMargin)
        {
            LeftMargin     = leftMargin;
            ContentsWidth  = contentsWidth;
            RightMargin    = rightMargin;
            TopMargin      = topMargin;
            ContentsHeight = contentsHeight;
            BottomMargin   = bottomMargin;
        }

        /// <summary>Resize parameters that fit page content into <paramref name="width"/>×<paramref name="height"/> points with zero margins.</summary>
        public static ContentsResizeParameters PageResize(double width, double height) =>
            new(
                ContentsResizeValue.Units(0),
                ContentsResizeValue.Units(width),
                ContentsResizeValue.Units(0),
                ContentsResizeValue.Units(0),
                ContentsResizeValue.Units(height),
                ContentsResizeValue.Units(0));

        /// <summary>Resize parameters that scale page content by <paramref name="widthPct"/>%×<paramref name="heightPct"/>% with zero margins.</summary>
        public static ContentsResizeParameters PageResizePct(double widthPct, double heightPct) =>
            new(
                ContentsResizeValue.Units(0),
                ContentsResizeValue.Percents(widthPct),
                ContentsResizeValue.Units(0),
                ContentsResizeValue.Units(0),
                ContentsResizeValue.Percents(heightPct),
                ContentsResizeValue.Units(0));

        /// <summary>Resize parameters with explicit content size in points; margins auto.</summary>
        public static ContentsResizeParameters ContentSize(double width, double height) =>
            new()
            {
                ContentsWidth  = ContentsResizeValue.Units(width),
                ContentsHeight = ContentsResizeValue.Units(height),
                LeftMargin     = ContentsResizeValue.Auto(),
                RightMargin    = ContentsResizeValue.Auto(),
                TopMargin      = ContentsResizeValue.Auto(),
                BottomMargin   = ContentsResizeValue.Auto(),
            };

        /// <summary>Resize parameters with explicit content size in percent of page; margins auto.</summary>
        public static ContentsResizeParameters ContentSizePercent(double width, double height) =>
            new()
            {
                ContentsWidth  = ContentsResizeValue.Percents(width),
                ContentsHeight = ContentsResizeValue.Percents(height),
                LeftMargin     = ContentsResizeValue.Auto(),
                RightMargin    = ContentsResizeValue.Auto(),
                TopMargin      = ContentsResizeValue.Auto(),
                BottomMargin   = ContentsResizeValue.Auto(),
            };

        /// <summary>Resize parameters with explicit margins in points; content size auto.</summary>
        public static ContentsResizeParameters Margins(double left, double right, double top, double bottom) =>
            new()
            {
                LeftMargin     = ContentsResizeValue.Units(left),
                RightMargin    = ContentsResizeValue.Units(right),
                TopMargin      = ContentsResizeValue.Units(top),
                BottomMargin   = ContentsResizeValue.Units(bottom),
                ContentsWidth  = ContentsResizeValue.Auto(),
                ContentsHeight = ContentsResizeValue.Auto(),
            };

        /// <summary>Resize parameters with explicit margins as percent of page; content size auto.</summary>
        public static ContentsResizeParameters MarginsPercent(double left, double right, double top, double bottom) =>
            new()
            {
                LeftMargin     = ContentsResizeValue.Percents(left),
                RightMargin    = ContentsResizeValue.Percents(right),
                TopMargin      = ContentsResizeValue.Percents(top),
                BottomMargin   = ContentsResizeValue.Percents(bottom),
                ContentsWidth  = ContentsResizeValue.Auto(),
                ContentsHeight = ContentsResizeValue.Auto(),
            };
    }
}
