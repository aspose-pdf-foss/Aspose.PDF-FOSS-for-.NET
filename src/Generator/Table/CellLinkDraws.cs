namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>Per built page, the cell hyperlinks the build could not bind because that page
    /// does not exist yet (a spill page, or a start page the flow has already left), each with
    /// the rectangle its text landed on; the caller links them once the page materialises.</summary>
    internal IReadOnlyList<List<(Rectangle rect, Hyperlink link)>> LastLinkDraws => _pageLinks;
    private readonly List<List<(Rectangle rect, Hyperlink link)>> _pageLinks = new();

    /// <summary>The page the build draws its first rows for is an overflow page still to be
    /// materialised: its cell links and annotations are recorded like a spill page's
    /// instead of being bound to the build page, which is an earlier one.</summary>
    internal bool DefersStartPageAnnotations { get; set; }

    /// <summary>Link a cell's text rectangle on the page it landed on: a web address opens
    /// the URL (kept as /Contents too, so it reads back after a round trip), a page number
    /// jumps to that page, a file name launches that file. Null for a link kind cells do not resolve.</summary>
    internal static Aspose.Pdf.Annotations.Annotation? AddCellLink(Page page, Rectangle rect, Hyperlink link)
    {
        if (link is WebHyperlink wh && !string.IsNullOrEmpty(wh.Url))
        {
            var web = page.Annotations.AddLinkAnnotation(rect, wh.Url);
            web.Contents = wh.Url;
            return web;
        }
        if (link is LocalHyperlink lh && lh.TargetPageNumber > 0)
            return page.Annotations.AddLinkAnnotation(rect,
                new Aspose.Pdf.Annotations.GoToAction(
                    new Aspose.Pdf.Annotations.XYZExplicitDestination(lh.TargetPageNumber, 0, 0, 0)));
        if (link is FileHyperlink fh && !string.IsNullOrEmpty(fh.FileName))
            return page.Annotations.AddLinkAnnotation(rect,
                new Aspose.Pdf.Annotations.LaunchAction(fh.FileName) { NewWindow = fh.NewWindow });
        return null;
    }
}
