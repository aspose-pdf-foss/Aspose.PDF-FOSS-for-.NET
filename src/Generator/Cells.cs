using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// A collection of cells in a row.
/// </summary>
public sealed class Cells : IEnumerable<Cell>
{
    private readonly List<Cell> _cells = new();

    /// <summary>Number of cells.</summary>
    public int Count => _cells.Count;

    /// <summary>Add a new empty cell and return it.</summary>
    public Cell Add()
    {
        var cell = new Cell();
        _cells.Add(cell);
        return cell;
    }

    /// <summary>Add a cell with the specified text content.</summary>
    public Cell Add(string text)
    {
        var cell = new Cell();
        cell.Paragraphs.Add(new TextFragment(text));
        _cells.Add(cell);
        return cell;
    }

    /// <summary>Add a cell with the specified text content and pre-applied text state.</summary>
    public Cell Add(string text, Text.TextState ts)
    {
        var cell = new Cell();
        var fragment = new TextFragment(text);
        if (ts is not null)
        {
            fragment.TextState.ApplyChangesFrom(ts);
            // Carry the text state's horizontal alignment onto the cell so the
            // renderer centres / right-aligns the content within the cell.
            cell.Alignment = ts.HorizontalAlignment;
        }
        cell.Paragraphs.Add(fragment);
        _cells.Add(cell);
        return cell;
    }

    /// <summary>Add a cell containing a TextFragment.</summary>
    public Cell Add(Text.TextFragment textFragment)
    {
        var cell = new Cell();
        cell.Paragraphs.Add(textFragment);
        _cells.Add(cell);
        return cell;
    }

    /// <summary>Add an existing cell.</summary>
    public void Add(Cell cell) => _cells.Add(cell);

    /// <summary>Indexer access to the cell at the given zero-based index. Reading
    /// past the end auto-extends the row with empty cells (the Row grows
    /// on demand, so a cell may be styled before the row is fully populated).</summary>
    public Cell this[int index]
    {
        get
        {
            while (index >= 0 && _cells.Count <= index) _cells.Add(new Cell());
            return _cells[index];
        }
        set => _cells[index] = value;
    }

    /// <summary>Insert <paramref name="cell"/> at the given zero-based <paramref name="index"/>.</summary>
    public void Insert(int index, Cell cell) => _cells.Insert(index, cell);

    /// <summary>Remove <paramref name="cell"/> if present.</summary>
    public void Remove(Cell cell) => _cells.Remove(cell);

    /// <summary>Remove <paramref name="obj"/> if it is a <see cref="Cell"/> that is present.</summary>
    public void Remove(object obj)
    {
        if (obj is Cell c) _cells.Remove(c);
    }

    /// <summary>Remove <paramref name="count"/> cells starting at <paramref name="index"/>.</summary>
    public void RemoveRange(int index, int count) => _cells.RemoveRange(index, count);

    /// <summary>Releases per-cell resources. Currently a no-op — cells hold no unmanaged buffers.</summary>
    public void Dispose() => _cells.Clear();

    /// <summary>Get a cell by index.</summary>
    public Cell At(int index) => _cells[index];

    public IEnumerator<Cell> GetEnumerator() => _cells.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
