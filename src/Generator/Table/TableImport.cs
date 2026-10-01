using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>Import a one-dimensional object array into the table, wrapping the
    /// values into rows by the table's column count. Filling starts at the 0-based
    /// (firstFilledRow, firstFilledColumn) offset; once a row is full the next row
    /// starts at column 0 when <paramref name="isLeftColumnsFilled"/> is set and at
    /// <paramref name="firstFilledColumn"/> again when it is not, so the columns to the
    /// left stay bare (probed: seven values from (2, 1) into three columns lay out
    /// [-,1,2] [3,4,5] [6,7,-] filled-left and [-,1,2] [-,3,4] [-,5,6] [-,7,-] otherwise).
    /// A bare cell holds no paragraph at all.</summary>
    public void ImportArray(object?[] importedArray, int firstFilledRow, int firstFilledColumn, bool isLeftColumnsFilled)
    {
        if (importedArray is null) return;

        // Column count drives the wrap. When the table declares no columns yet,
        // fall back to a single row (the whole array) so callers that rely on a
        // column-less table keep the prior one-row behaviour.
        var columnCount = ResolveImportColumnCount();
        if (columnCount <= 0) columnCount = importedArray.Length;
        if (columnCount <= 0) return;

        var row = Math.Max(0, firstFilledRow);
        var startCol = Math.Min(Math.Max(0, firstFilledColumn), columnCount - 1);
        var col = startCol;
        foreach (var value in importedArray)
        {
            EnsureRowsAndColumns(row + 1, columnCount);
            var r = Rows[row];
            EnsureCellCount(r, col + 1);
            r.Cells[col].Text = value?.ToString() ?? string.Empty;
            if (++col >= columnCount) { col = isLeftColumnsFilled ? 0 : startCol; row++; }
        }
    }

    /// <summary>The table's column count used when wrapping an imported array:
    /// the number of declared <see cref="ColumnWidths"/>, else the widest existing
    /// row, else zero (no columns declared yet).</summary>
    private int ResolveImportColumnCount()
    {
        if (!string.IsNullOrEmpty(ColumnWidths))
        {
            var n = ColumnWidths.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (n > 0) return n;
        }
        var max = 0;
        foreach (var r in Rows)
            if (r.Cells.Count > max) max = r.Cells.Count;
        return max;
    }

    /// <summary>Place a sequence of values into a single table row starting at the
    /// 0-based (firstFilledRow, firstFilledColumn) offset — the per-row fill used by
    /// the DataTable / DataView importers, which already iterate row by row and so
    /// must not wrap.</summary>
    private void FillSingleRow(object?[] values, int firstFilledRow, int firstFilledColumn, bool html = false)
    {
        // The offsets are 0-based: both reference renders that pass a non-zero column
        // leave the table's first column blank rather than starting in it.
        if (firstFilledRow < 0) firstFilledRow = 0;
        if (firstFilledColumn < 0) firstFilledColumn = 0;
        EnsureRowsAndColumns(firstFilledRow + 1, firstFilledColumn + values.Length);
        var row = Rows[firstFilledRow];
        for (int i = 0; i < values.Length; i++)
        {
            var cellIdx = firstFilledColumn + i;
            EnsureCellCount(row, cellIdx + 1);
            var text = values[i]?.ToString() ?? string.Empty;
            // With HTML support a value that carries markup is the cell's HTML fragment,
            // not its literal text.
            if (html && text.Contains('<'))
                row.Cells[cellIdx].Paragraphs.Add(new HtmlFragment(text));
            else
                row.Cells[cellIdx].Text = text;
        }
    }

    /// <summary>Import all rows of a <see cref="System.Data.DataTable"/>.</summary>
    public void ImportDataTable(System.Data.DataTable importedDataTable, bool isColumnNamesImported,
        int firstFilledRow, int firstFilledColumn)
    {
        if (importedDataTable is null) return;
        var startRow = firstFilledRow < 0 ? 0 : firstFilledRow;
        if (isColumnNamesImported)
        {
            var header = importedDataTable.Columns.Cast<System.Data.DataColumn>()
                .Select(c => (object)c.ColumnName).ToArray();
            FillSingleRow(header, startRow, firstFilledColumn);
            startRow++;
        }
        for (int r = 0; r < importedDataTable.Rows.Count; r++)
        {
            var values = importedDataTable.Rows[r].ItemArray;
            // Coerce DBNull to empty string so .ToString() doesn't surface "System.DBNull".
            for (int i = 0; i < values.Length; i++)
                if (values[i] is null || values[i] is System.DBNull) values[i] = string.Empty;
            FillSingleRow(values, startRow + r, firstFilledColumn);
        }
    }

    /// <summary>Import with explicit max-rows / max-columns and HTML support flag.
    /// The HTML flag is optional, so the six-argument form selects this overload.</summary>
    public void ImportDataTable(System.Data.DataTable importedDataTable, bool isColumnNamesShown,
        int firstFilledRow, byte firstFilledColumn, int maxRows, int maxColumns, bool isHtmlSupported = false)
    {
        if (importedDataTable is null) return;
        var startRow = firstFilledRow < 0 ? 0 : firstFilledRow;
        if (isColumnNamesShown)
        {
            var header = importedDataTable.Columns.Cast<System.Data.DataColumn>()
                .Take(maxColumns > 0 ? maxColumns : int.MaxValue)
                .Select(c => (object)c.ColumnName).ToArray();
            FillSingleRow(header, startRow, firstFilledColumn);
            startRow++;
        }
        var rowCap = maxRows > 0 ? Math.Min(maxRows, importedDataTable.Rows.Count) : importedDataTable.Rows.Count;
        for (int r = 0; r < rowCap; r++)
        {
            var values = importedDataTable.Rows[r].ItemArray;
            if (maxColumns > 0 && values.Length > maxColumns) values = values[..maxColumns];
            for (int i = 0; i < values.Length; i++)
                if (values[i] is null || values[i] is System.DBNull) values[i] = string.Empty;
            FillSingleRow(values, startRow + r, firstFilledColumn, isHtmlSupported);
        }
    }

    /// <summary>Import a subset of rows / columns selected by index lists.
    /// The HTML flag is optional, so the six-argument form selects this overload.</summary>
    public void ImportDataTable(System.Data.DataTable importedDataTable,
        int[] sourceRowList, int[] sourceColumnList,
        int firstFilledRow, int firstFilledColumn,
        bool showColumnNamesAsFirstRow, bool isHtmlSupported = false)
    {
        if (importedDataTable is null || sourceRowList is null || sourceColumnList is null) return;
        var startRow = firstFilledRow < 0 ? 0 : firstFilledRow;
        if (showColumnNamesAsFirstRow)
        {
            var header = sourceColumnList
                .Where(c => c >= 0 && c < importedDataTable.Columns.Count)
                .Select(c => (object)importedDataTable.Columns[c].ColumnName)
                .ToArray();
            FillSingleRow(PadToColumnCount(header, firstFilledColumn), startRow, firstFilledColumn);
            startRow++;
        }
        for (int r = 0; r < sourceRowList.Length; r++)
        {
            var rowIx = sourceRowList[r];
            if (rowIx < 0 || rowIx >= importedDataTable.Rows.Count) continue;
            var row = importedDataTable.Rows[rowIx];
            var values = sourceColumnList
                .Where(c => c >= 0 && c < importedDataTable.Columns.Count)
                .Select(c => (object)(row[c] is System.DBNull ? string.Empty : row[c]?.ToString() ?? string.Empty))
                .ToArray();
            FillSingleRow(PadToColumnCount(values, firstFilledColumn), startRow + r, firstFilledColumn, isHtmlSupported);
        }
    }

    /// <summary>A column-list import fills EVERY cell of its rows: the columns the list
    /// leaves out get an empty text (probed: two listed columns of a four-column grid
    /// give four cells, the last two holding an empty fragment), so a caller may style
    /// the paragraph of any cell in an imported row.</summary>
    private object?[] PadToColumnCount(object?[] values, int firstFilledColumn)
    {
        var columnCount = ResolveImportColumnCount();
        var want = columnCount - Math.Max(0, firstFilledColumn);
        if (want <= values.Length) return values;
        var padded = new object?[want];
        Array.Copy(values, padded, values.Length);
        for (var i = values.Length; i < want; i++) padded[i] = string.Empty;
        return padded;
    }

    /// <summary>Import a <see cref="System.Data.DataView"/>.</summary>
    public void ImportDataView(System.Data.DataView sourceDataView, bool isColumnNamesImported,
        int firstFilledRow, int firstFilledColumn, int maxRows, int maxColumns)
    {
        if (sourceDataView is null) return;
        var startRow = firstFilledRow < 0 ? 0 : firstFilledRow;
        var cols = sourceDataView.Table?.Columns.Cast<System.Data.DataColumn>().ToList()
                   ?? new System.Collections.Generic.List<System.Data.DataColumn>();
        if (maxColumns > 0 && maxColumns < cols.Count) cols = cols.GetRange(0, maxColumns);
        if (isColumnNamesImported)
        {
            var header = cols.Select(c => (object)c.ColumnName).ToArray();
            FillSingleRow(header, startRow, firstFilledColumn);
            startRow++;
        }
        var rowCap = maxRows > 0 ? Math.Min(maxRows, sourceDataView.Count) : sourceDataView.Count;
        for (int r = 0; r < rowCap; r++)
        {
            var values = cols.Select(c =>
            {
                var v = sourceDataView[r][c.ColumnName];
                return (object?)(v is System.DBNull ? string.Empty : v?.ToString() ?? string.Empty);
            }).ToArray();
            FillSingleRow(values, startRow + r, firstFilledColumn);
        }
    }
}
