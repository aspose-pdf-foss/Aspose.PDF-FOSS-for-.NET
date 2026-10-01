using System.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Optimization;

internal static partial class PdfAValidator
{
    /// <summary>The structure types that group a table's rows.</summary>
    private static readonly HashSet<string> UaTableRowGroupTypes =
        new(StringComparer.Ordinal) { "THead", "TBody", "TFoot" };

    /// <summary>
    /// PDF/UA-1 clause 7.5: every row of a table spans the same number of columns. A cell
    /// states the columns it covers with /ColSpan and the rows below it reaches into with
    /// /RowSpan, so a row is as wide as the columns its own cells cover together with the
    /// columns that cells above still occupy. Rows of different widths leave no grid a reader
    /// can move through by row and column.
    /// </summary>
    /// <remarks>A row that departs from the width most of the table's rows share is the
    /// irregular one. It is reported against the page it is stated to be on - its own /Pg or
    /// an ancestor's, none when no ancestor states one; a cell's page says nothing about
    /// the row - and against no object. Irregular rows that share that page share one
    /// report.</remarks>
    private static void CheckUaTables(Document document, StructureTreeView tree, UaReport report)
    {
        var reader = document.Reader;
        var structRoot = reader.ResolveDict(document.Catalog.Get("StructTreeRoot"));
        var roleMap = reader.ResolveDict(structRoot?.Get("RoleMap"));
        var pageNumbers = new Dictionary<PdfDictionary, int>();
        foreach (var page in document.Pages) pageNumbers[page.Dict] = page.Number;
        var elementPages = new Dictionary<PdfDictionary, PdfDictionary?>(ReferenceEqualityComparer.Instance);
        foreach (var view in tree.Elements) elementPages[view.Element] = view.Page;

        var reportedPages = new HashSet<int>();
        foreach (var view in tree.Elements)
        {
            if (view.Type != "Table") continue;
            var rows = TableRowWidths(reader, roleMap, view.Element);
            if (rows.Count == 0) continue;
            var usual = rows.GroupBy(row => row.Width).OrderByDescending(g => g.Count()).First().Key;
            foreach (var (row, width) in rows)
            {
                if (width == usual) continue;
                var page = elementPages.TryGetValue(row, out var stated) ? stated : view.Page;
                var pageNumber = page is not null && pageNumbers.TryGetValue(page, out var n)
                    ? n
                    : (int?)null;
                if (!reportedPages.Add(pageNumber ?? 0)) continue;
                report.Add(UaProblems.IrregularTableRow, "UaTableRows", "Irregular table row", pageNumber);
            }
        }
    }

    /// <summary>Each row of a table with the number of columns it spans, in row order.</summary>
    private static List<(PdfDictionary Row, int Width)> TableRowWidths(PdfReader reader,
        PdfDictionary? roleMap, PdfDictionary table)
    {
        var widths = new List<(PdfDictionary Row, int Width)>();
        // Per column: how many rows, the current one included, the cell that last covered it
        // reaches down to. A column with rows left is taken before this row's own cells are
        // placed, and every column loses one row when the row ends.
        var reach = new List<int>();
        foreach (var row in TableRows(reader, roleMap, table))
        {
            var column = 0;
            foreach (var cell in StructureChildren(reader, row))
            {
                if (StandardStructureType(roleMap, cell.GetName("S")) is not ("TD" or "TH")) continue;
                while (column < reach.Count && reach[column] > 0) column++;
                var (colSpan, rowSpan) = ReadCellSpans(reader, cell);
                for (var k = 0; k < colSpan; k++, column++)
                {
                    while (reach.Count <= column) reach.Add(0);
                    reach[column] = rowSpan;
                }
            }
            var width = column;
            for (var c = column; c < reach.Count; c++)
                if (reach[c] > 0) width = c + 1;
            widths.Add((row, width));
            for (var c = 0; c < reach.Count; c++)
                if (reach[c] > 0) reach[c]--;
        }
        return widths;
    }

    /// <summary>The rows of a table in order: its /TR children, held directly or through a
    /// row group.</summary>
    private static IEnumerable<PdfDictionary> TableRows(PdfReader reader, PdfDictionary? roleMap,
        PdfDictionary table)
    {
        foreach (var child in StructureChildren(reader, table))
        {
            var type = StandardStructureType(roleMap, child.GetName("S"));
            if (type == "TR")
            {
                yield return child;
                continue;
            }
            if (type is null || !UaTableRowGroupTypes.Contains(type)) continue;
            foreach (var row in StructureChildren(reader, child))
                if (StandardStructureType(roleMap, row.GetName("S")) == "TR") yield return row;
        }
    }

    /// <summary>The structure elements among an element's kids; a marked-content or object
    /// reference is content, not an element.</summary>
    private static IEnumerable<PdfDictionary> StructureChildren(PdfReader reader, PdfDictionary element)
    {
        var kids = element.Get("K");
        foreach (var kid in reader.Resolve(kids) is PdfArray array ? array : kids is null ? [] : [kids])
            if (reader.ResolveDict(kid) is { } dict && dict.Get("S") is not null)
                yield return dict;
    }

    /// <summary>The column and row spans a cell states in its attributes; one each when it
    /// states none.</summary>
    private static (int ColSpan, int RowSpan) ReadCellSpans(PdfReader reader, PdfDictionary cell)
    {
        var colSpan = 1;
        var rowSpan = 1;
        var attributes = cell.Get("A");
        foreach (var entry in reader.Resolve(attributes) is PdfArray array ? array : attributes is null ? [] : [attributes])
        {
            if (reader.ResolveDict(entry) is not { } attribute) continue;
            if (SpanValue(reader.Resolve(attribute.Get("ColSpan"))) is { } stated) colSpan = stated;
            if (SpanValue(reader.Resolve(attribute.Get("RowSpan"))) is { } statedRows) rowSpan = statedRows;
        }
        return (colSpan, rowSpan);
    }

    /// <summary>A span as an attribute states it; null unless it is a number of at least one.</summary>
    private static int? SpanValue(PdfObject? value) => value switch
    {
        PdfInteger i when i.Value >= 1 => (int)i.Value,
        PdfReal r when r.Value >= 1 => (int)r.Value,
        _ => null,
    };
}
