using System.Collections.Generic;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Table cell lines that print the page count, written while the flow was still making
    /// pages: the page each landed on, its text as authored and as written.
    ///
    /// A page table's rows are written before the flow has made the table's own spill pages, let
    /// alone the pages of what follows it, so the count a cell could read then is the count of that
    /// moment - "page 1 of 1" on every page of a two-page table in a four-page document. A band
    /// that prints the count waits until every page exists; a cell cannot wait, it is laid out with
    /// its row, so it is written with the count of the moment and rewritten in place once the
    /// sequence is final, in the segment that holds it, to the text it would have been written with.
    /// The same authored line written the same way on the same page is rewritten once.</summary>
    private readonly HashSet<(int PageNumber, string Original, string Written)> _deferredPageCountLines = new();

    /// <summary>Record a cell line written on <paramref name="pageNumber"/> as <paramref name="written"/>,
    /// to be rewritten from <paramref name="original"/> when the page count is final.</summary>
    internal void DeferPageCountLine(int pageNumber, string original, string written)
        => _deferredPageCountLines.Add((pageNumber, original, written));

    /// <summary>Rewrite the deferred lines to the count the finished page sequence has.</summary>
    private void ApplyDeferredPageCounts()
    {
        foreach (var (pageNumber, original, written) in _deferredPageCountLines)
        {
            if (pageNumber < 1 || pageNumber > Pages.Count) continue;
            var final = HeaderFooter.ApplyPageLabelMacros(original, this, pageNumber);
            if (final == written) continue;
            Pages[pageNumber].ReplaceInContentStreams(ShownText(written), ShownText(final));
        }
        _deferredPageCountLines.Clear();
    }

    /// <summary>The show operator of a cell line exactly as the table writes it.</summary>
    private static byte[] ShownText(string text) => new Content.ContentStreamBuilder().ShowText(text).Build();
}
