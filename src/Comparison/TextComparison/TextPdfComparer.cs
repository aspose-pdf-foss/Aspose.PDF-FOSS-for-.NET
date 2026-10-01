using System;
using System.Collections.Generic;
using Aspose.Pdf.Comparison.Diff;

namespace Aspose.Pdf.Comparison
{
    /// <summary>Compares the text of two documents. Each page's text is extracted in reading
    /// order - limited to the extraction area and less the excluded areas and, when asked,
    /// the tables - and the two texts are diffed. The result is an edit list per page, or
    /// one list over the flattened documents, which any output generator can render.</summary>
    public class TextPdfComparer
    {
        /// <summary>Creates a comparer. All comparison methods are static, so an instance is not needed to call them.</summary>
        public TextPdfComparer() { }

        /// <summary>Compare page by page: page n of the first document against page n of the
        /// second, a missing page reading as empty text.</summary>
        public static List<List<DiffOperation>> CompareDocumentsPageByPage(
            Document document1, Document document2, ComparisonOptions options)
        {
            var result = new List<List<DiffOperation>>();
            var pages = Math.Max(PageCount(document1), PageCount(document2));
            for (var i = 1; i <= pages; i++)
                result.Add(ComparePages(PageAt(document1, i), PageAt(document2, i), options));
            return result;
        }

        /// <summary>Compare page by page and also render the result to
        /// <paramref name="resultPdfDocumentPath"/>.</summary>
        public static List<List<DiffOperation>> CompareDocumentsPageByPage(
            Document document1, Document document2, ComparisonOptions options, string resultPdfDocumentPath)
        {
            var diffs = CompareDocumentsPageByPage(document1, document2, options);
            new PdfOutputGenerator().GenerateOutput(diffs, resultPdfDocumentPath);
            return diffs;
        }

        /// <summary>Compare the two documents as one text each - every page's text joined in
        /// order - so a change that moves across a page boundary reads as one edit.</summary>
        public static List<DiffOperation> CompareFlatDocuments(
            Document document1, Document document2, ComparisonOptions options)
        {
            var text1 = FlatText(document1, options, first: true);
            var text2 = FlatText(document2, options, first: false);
            return new DiffSolver(options).FindDiff(text1, text2);
        }

        /// <summary>Compare flat and also render the result to
        /// <paramref name="resultPdfDocumentPath"/>.</summary>
        public static List<DiffOperation> CompareFlatDocuments(
            Document document1, Document document2, ComparisonOptions options, string resultPdfDocumentPath)
        {
            var diffs = CompareFlatDocuments(document1, document2, options);
            new PdfOutputGenerator().GenerateOutput(diffs, resultPdfDocumentPath);
            return diffs;
        }

        /// <summary>Compare the text of two pages. A null page reads as empty text.</summary>
        public static List<DiffOperation> ComparePages(Page? page1, Page? page2, ComparisonOptions options)
        {
            var text1 = PageText(page1, options, first: true);
            var text2 = PageText(page2, options, first: false);
            return new DiffSolver(options).FindDiff(text1, text2);
        }

        /// <summary>Counts over one edit list.</summary>
        public static TextItemComparisonStatistics CreateComparisonStatistics(List<DiffOperation> diffs)
        {
            var stats = new TextItemComparisonStatistics();
            if (diffs is not null)
                foreach (var d in diffs)
                    if (d is not null) stats.Count(d);
            return stats;
        }

        /// <summary>Counts over a per-page edit list: each page's own counts, and the
        /// document total across them.</summary>
        public static DocumentComparisonStatistics CreateComparisonStatistics(List<List<DiffOperation>> diffs)
        {
            var stats = new DocumentComparisonStatistics();
            if (diffs is not null)
            {
                foreach (var page in diffs)
                {
                    var pageStats = CreateComparisonStatistics(page);
                    stats.PagesStatistics.Add(pageStats);
                    stats.Add(pageStats);
                }
            }
            return stats;
        }

        /// <summary>Rebuild the first text from an edit list (equal and deleted runs).</summary>
        public static string AssemblySourcePageText(List<DiffOperation> diffs)
            => DiffUtils.AssemblySourceText(diffs);

        /// <summary>Rebuild the second text from an edit list (equal and inserted runs).</summary>
        public static string AssemblyDestinationPageText(List<DiffOperation> diffs)
            => DiffUtils.AssemblyDestinationText(diffs);

        // ---- text extraction ------------------------------------------------------------

        /// <summary>A page's comparable text, built from its text fragments in reading order.
        /// The reference has two shapes, both reproduced here: with nothing excluded the text
        /// keeps its lines (fragments on one line a space apart, lines a line break apart), so
        /// a changed wrap reads as an edit; with tables or areas excluded every fragment is a
        /// space apart and the lines flow, so a wrap is not an edit at all. An extraction area
        /// keeps only the glyphs inside it - a word on the edge is cut mid-word.</summary>
        private static string PageText(Page? page, ComparisonOptions options, bool first)
        {
            if (page is null) return string.Empty;
            var areas = first ? options?.ExcludeAreas1 : options?.ExcludeAreas2;
            if (options?.ExcludeTables == true)
                areas = ComparisonUtils.AddTablesToExcludeAreas(areas, page);
            areas ??= Array.Empty<Rectangle>();
            var flowing = options?.ExcludeTables == true || areas.Length > 0;

            var kept = KeptWords(page, options?.ExtractionArea, areas);
            kept.Sort(ByReadingOrder);
            return Join(kept, flowing ? " " : "\r\n");
        }

        /// <summary>The page's fragments - clipped to the extraction area glyph by glyph -
        /// less those whose centre falls in an excluded area.</summary>
        private static List<Word> KeptWords(Page page, Rectangle? extractionArea, Rectangle[] areas)
        {
            var absorber = new Aspose.Pdf.Text.TextFragmentAbsorber();
            absorber.Visit(page);

            var kept = new List<Word>();
            foreach (Aspose.Pdf.Text.TextFragment f in absorber.TextFragments)
            {
                if (f.Rectangle is not { } r || InAnyArea(r, areas)) continue;
                var word = extractionArea is null ? new Word(r, f.Text ?? string.Empty) : Clip(f, extractionArea);
                if (word is { } w && w.Text.Length > 0) kept.Add(w);
            }
            return kept;
        }

        /// <summary>The part of a fragment inside the extraction area: the glyphs whose whole
        /// width lies inside it and whose line the area covers at mid-height. The reference
        /// keeps "so" of a "some" that runs past the right edge and drops a line that starts
        /// above the top edge while keeping one whose ascenders merely graze it.</summary>
        private static Word? Clip(Aspose.Pdf.Text.TextFragment fragment, Rectangle area)
        {
            var sb = new System.Text.StringBuilder();
            Rectangle? box = null;
            foreach (Aspose.Pdf.Text.TextSegment segment in fragment.Segments)
            {
                var text = segment.Text ?? string.Empty;
                if (segment.Characters.Count != text.Length) continue;
                for (var i = 0; i < text.Length; i++)
                {
                    var g = segment.Characters[i + 1].Rectangle;
                    if (!GlyphInArea(g, area)) continue;
                    sb.Append(text[i]);
                    box = box is null ? g : Union(box, g);
                }
            }
            return box is null ? null : new Word(box, sb.ToString());
        }

        private static bool GlyphInArea(Rectangle g, Rectangle area)
        {
            var midY = (g.LLY + g.URY) / 2;
            return g.LLX >= area.LLX && g.URX <= area.URX && midY >= area.LLY && midY <= area.URY;
        }

        private static Rectangle Union(Rectangle a, Rectangle b) => new Rectangle(
            Math.Min(a.LLX, b.LLX), Math.Min(a.LLY, b.LLY), Math.Max(a.URX, b.URX), Math.Max(a.URY, b.URY));

        private static string Join(List<Word> words, string lineBreak)
        {
            var sb = new System.Text.StringBuilder();
            Word? previous = null;
            foreach (var word in words)
            {
                if (previous is { } p) sb.Append(Separator(p.Box, word.Box, lineBreak));
                sb.Append(word.Text);
                previous = word;
            }
            return sb.ToString();
        }

        /// <summary>What goes between two consecutive fragments: the line break when the second
        /// sits on another line, a space when it starts a new word, nothing when the gap is a
        /// kerning adjustment - a document that draws "W" and "e" as two text objects still
        /// reads "We", and the reference comparison shows it so (ours read "W e", "T exas").</summary>
        private static string Separator(Rectangle previous, Rectangle box, string lineBreak)
        {
            if (Math.Abs(box.LLY - previous.LLY) > box.Height / 2) return lineBreak;
            var gap = box.LLX - previous.URX;
            return gap >= WordGapFraction * box.Height ? " " : string.Empty;
        }

        /// <summary>A horizontal gap of at least this fraction of the line height is a word
        /// space; anything narrower is kerning.</summary>
        private const double WordGapFraction = 0.15;

        private static bool InAnyArea(Rectangle r, Rectangle[] areas)
        {
            var cx = (r.LLX + r.URX) / 2;
            var cy = (r.LLY + r.URY) / 2;
            foreach (var a in areas)
                if (a is not null && a.ContainsPoint(cx, cy)) return true;
            return false;
        }

        /// <summary>Top-to-bottom, then left-to-right; two fragments within half a line height
        /// of each other are on the same line.</summary>
        private static int ByReadingOrder(Word a, Word b)
        {
            var tol = Math.Min(a.Box.Height, b.Box.Height) / 2;
            if (Math.Abs(a.Box.LLY - b.Box.LLY) > tol) return b.Box.LLY.CompareTo(a.Box.LLY);
            return a.Box.LLX.CompareTo(b.Box.LLX);
        }

        /// <summary>A fragment's kept text and the box it occupies.</summary>
        private sealed record Word(Rectangle Box, string Text);

        private static string FlatText(Document document, ComparisonOptions options, bool first)
        {
            var sb = new System.Text.StringBuilder();
            var pages = PageCount(document);
            for (var i = 1; i <= pages; i++)
                sb.Append(PageText(PageAt(document, i), options, first));
            return sb.ToString();
        }

        private static int PageCount(Document? document) => document?.Pages?.Count ?? 0;

        private static Page? PageAt(Document? document, int oneBased)
            => document is not null && oneBased >= 1 && oneBased <= PageCount(document)
                ? document.Pages[oneBased]
                : null;
    }
}
