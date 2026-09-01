using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// A collection of rows in a table.
/// </summary>
public sealed class Rows : IEnumerable<Row>, IDisposable
{
    private readonly List<Row> _rows = new();
    private readonly Table? _table;
    private double _accumulatedHeight;
    // Default usable page height: Letter (792) minus 72pt top/bottom margins
    private const double DefaultPageContentHeight = 648;

    /// <summary>Construct a free-standing rows collection (no parent table).
    /// Used by callers that build a Rows instance and assign it to
    /// <see cref="Table.Rows"/> later.</summary>
    public Rows() { _table = null; }

    internal Rows(Table table) { _table = table; }

    /// <summary>Number of rows.</summary>
    public int Count => _rows.Count;

    /// <summary>Add a new empty row and return it.</summary>
    public Row Add()
    {
        var row = new Row();
        _rows.Add(row);
        UpdateIsInNewPage(row);
        return row;
    }

    /// <summary>Add an existing row.</summary>
    public void Add(Row row)
    {
        _rows.Add(row);
        UpdateIsInNewPage(row);
    }

    /// <summary>Get a row by index.</summary>
    public Row At(int index) => _rows[index];

    /// <summary>Indexer with get/set. Reading past
    /// the end auto-extends the collection with empty rows (the Table
    /// grows on demand, so callers may address a cell before filling it).</summary>
    public Row this[int index]
    {
        get
        {
            while (index >= 0 && _rows.Count <= index) _rows.Add(new Row());
            return _rows[index];
        }
        set => _rows[index] = value;
    }

    /// <summary>Index of <paramref name="row"/> in the collection, or -1.</summary>
    public int IndexOf(Row row) => _rows.IndexOf(row);

    /// <summary>Remove the first occurrence of <paramref name="row"/>.</summary>
    public void Remove(Row row) { _rows.Remove(row); }

    /// <summary>Remove the row at the given 0-based index.</summary>
    public void RemoveAt(int index) { _rows.RemoveAt(index); }

    /// <summary>Remove <paramref name="count"/> rows starting at <paramref name="index"/>.</summary>
    public void RemoveRange(int index, int count) { _rows.RemoveRange(index, count); }

    /// <summary>Releases any resources held by the collection. The FOSS
    /// implementation holds no unmanaged resources; the call clears the
    /// row list for API compatibility.</summary>
    public void Dispose() { _rows.Clear(); _accumulatedHeight = 0; }

    public IEnumerator<Row> GetEnumerator() => _rows.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void UpdateIsInNewPage(Row row)
    {
        // Estimate row height: fixed height, min height, or default (font size + padding)
        var textState = row.DefaultCellTextState ?? _table?.DefaultCellTextState;
        var fontSize = textState?.FontSize ?? 12;
        var padding = row.DefaultCellPadding ?? _table?.DefaultCellPadding;
        var padV = (padding?.Top ?? 2) + (padding?.Bottom ?? 2);
        var estimatedHeight = row.FixedRowHeight > 0
            ? row.FixedRowHeight
            : Math.Max(row.MinRowHeight, fontSize + padV);

        if (_accumulatedHeight + estimatedHeight > DefaultPageContentHeight && _rows.Count > 1)
        {
            row.ReportInNewPage(true);
            _accumulatedHeight = estimatedHeight; // reset for new page
        }
        else
        {
            row.ReportInNewPage(false);
            _accumulatedHeight += estimatedHeight;
        }
    }
}
