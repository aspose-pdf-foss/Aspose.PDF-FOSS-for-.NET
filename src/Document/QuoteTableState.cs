using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf;

public sealed partial class Document
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class QuoteTableState
{
    // Solve the columns: every column takes its declared px share of
    // the table's own content budget. The widest row that declares
    // its cells owns the grid (a colspan band row does not).
    public List<double> declared = null!;
    public int n;
    public double declaredSum;
    public double scale;
    public double[] colX = null!;
    public double[] colW = null!;
    public double cx;
    // Measure every row, then place the table: one that does not fit
    // below the cursor moves to the next page whole.
    public List<List<List<string>>> wrapped = null!;
    public List<double> rowH = null!;
    public List<double> rowFont = null!;
    public double tableH;
    public double rowTop;
    public QuoteScheduleState qs = default!;
    public QsBlock b = default!;
}
}
