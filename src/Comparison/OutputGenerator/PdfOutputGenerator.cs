using System.Collections.Generic;
using System.IO;
using System.Text;
using Aspose.Pdf.Comparison.Diff;

namespace Aspose.Pdf.Comparison
{
    /// <summary>Writes a comparison result as a PDF. The reference render of this output is
    /// the HTML output laid out by the HTML converter, so that is exactly what this does:
    /// the same marked-up text, converted with the page geometry the caller supplied.
    /// A per-page result puts each compared page on its own output page; a result that does
    /// not fit its page is laid out again on a page grown by whole page heights, so one edit
    /// list is always one page.</summary>
    public class PdfOutputGenerator : IFileOutputGenerator
    {
        private readonly HtmlDiffOutputGenerator _html;
        private readonly PageInfo? _pageInfo;

        /// <summary>Create a generator with the default colours on the default page.</summary>
        public PdfOutputGenerator() : this(null, null) { }

        /// <summary>Create a generator with the default colours on the given page.</summary>
        public PdfOutputGenerator(PageInfo pageInfo) : this(null, pageInfo) { }

        /// <summary>Create a generator drawing each kind of edit in the given style.</summary>
        public PdfOutputGenerator(OutputTextStyle textStyle) : this(textStyle, null) { }

        /// <summary>Create a generator with the given style on the given page.</summary>
        public PdfOutputGenerator(OutputTextStyle? textStyle, PageInfo? pageInfo)
        {
            _html = textStyle is null ? new HtmlDiffOutputGenerator() : new HtmlDiffOutputGenerator(textStyle);
            _pageInfo = pageInfo;
        }

        /// <inheritdoc/>
        public void GenerateOutput(List<DiffOperation> differences, string targetFilePath)
        {
            using var document = LayOut(differences);
            Save(document, targetFilePath);
        }

        /// <inheritdoc/>
        public void GenerateOutput(List<List<DiffOperation>> differences, string targetFilePath)
        {
            var groups = differences ?? new List<List<DiffOperation>>();
            if (groups.Count == 0)
            {
                GenerateOutput(new List<DiffOperation>(), targetFilePath);
                return;
            }
            using var document = LayOut(groups[0]);
            for (var i = 1; i < groups.Count; i++)
            {
                // Round-tripped through its own bytes so the merge imports a saved page,
                // not a page whose content the converter still holds.
                using var group = Reload(LayOut(groups[i]));
                document.Pages.Add(group.Pages);
            }
            Save(document, targetFilePath);
        }

        /// <summary>One edit list laid out as one page: converted on the output page and, when
        /// the text spills, converted again on a page as many times taller as the spill took
        /// pages. The reference flat comparison of a three-page document is one 1396 pt page -
        /// exactly two default heights - not a content-fitted one.</summary>
        private Document LayOut(List<DiffOperation> differences)
        {
            var html = _html.GenerateOutput(differences);
            var page = OutputPage(_pageInfo);
            var document = Convert(html, page);
            var spill = document.Pages.Count;
            if (spill <= 1) return document;

            document.Dispose();
            page.Height *= spill;
            return Convert(html, page);
        }

        private static Document Convert(string html, PageInfo page)
        {
            var options = new HtmlLoadOptions { PageInfo = page };
            using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(html));
            return new Document(stream, options);
        }

        private static Document Reload(Document document)
        {
            using (document)
            {
                var bytes = new MemoryStream();
                document.Save(bytes);
                bytes.Position = 0;
                return new Document(bytes);
            }
        }

        private static void Save(Document document, string targetFilePath)
        {
            var dir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            document.Save(targetFilePath);
        }

        /// <summary>The page the output is laid out on. Its height is the descriptor's height
        /// LESS the top and bottom margins - the reference renders pin this: the default page
        /// is A4 wide but 698 pt tall (842 - 72 - 72), while a caller's zero-margin landscape
        /// A4 comes out the full 842 x 595. The width is never reduced.</summary>
        private static PageInfo OutputPage(PageInfo? requested)
        {
            var source = requested ?? new PageInfo();
            var margin = source.Margin;
            var page = new PageInfo
            {
                Width = source.Width,
                Height = source.Height - (margin?.Top ?? 0) - (margin?.Bottom ?? 0),
            };
            if (margin is not null) page.Margin = margin;
            return page;
        }
    }
}
