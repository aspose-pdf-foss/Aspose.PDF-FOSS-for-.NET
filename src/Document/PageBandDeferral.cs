using System.Collections.Generic;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Pages whose band names its own page number, held back until every page exists,
    /// with the footer-table decision the layout pass made for each.
    ///
    /// A band is normally stamped while its page is laid out. That is too early for a band that
    /// prints the page number: content overflowing one page inserts another, and every page after
    /// the insertion is renumbered - but its band is already drawn, and drawn with the number the
    /// page had before. The document then counts 1, 2, 2, 3, … for the rest of its length.</summary>
    private readonly Dictionary<Page, bool> _deferredNumberBands = new();

    /// <summary>Whether a band prints the number of the page it lands on: the <c>#</c> of its own
    /// Text, or a <c>$p</c>/<c>$P</c> macro there or in any text paragraph it carries.</summary>
    private static bool BandNamesItsPageNumber(HeaderFooter? band)
    {
        if (band is null) return false;
        if (band.Text.Contains("#") || CarriesPageMacro(band.Text)) return true;
        foreach (var p in band.Paragraphs)
        {
            if (p is TextFragment tf && CarriesPageMacro(tf.Text ?? "")) return true;
            if (p is Table t && TableCarriesPageMacro(t)) return true;
        }
        return false;
    }

    private static bool CarriesPageMacro(string text)
        => text.Contains("$p") || text.Contains("$P");

    /// <summary>A band table states the macro in a cell, nested tables included.</summary>
    private static bool TableCarriesPageMacro(Table table)
    {
        foreach (var row in table.Rows)
            foreach (var cell in row.Cells)
                foreach (var p in cell.Paragraphs)
                {
                    if (p is TextFragment tf && CarriesPageMacro(tf.Text ?? "")) return true;
                    if (p is Table nested && TableCarriesPageMacro(nested)) return true;
                }
        return false;
    }
}
