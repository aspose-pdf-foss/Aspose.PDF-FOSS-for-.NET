using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of <c>BuildTableFromHtml</c>: the column
/// and width model the parse loop fills and the width solver consumes. One instance
/// per invocation; never shared.</summary>
private sealed class TableColumnModel
{
    // Content-based auto width: per column, the min-content width (widest single word — the
    // narrowest the column can be while still wrapping) and the max-content width (widest full
    // line — no wrapping). A browser uses max-content when the table fits and shrinks toward
    // min-content otherwise. Tracks the current column cursor per row.
    public List<double> colMinW = new List<double>();
    public List<double> colMaxW = new List<double>();
    public List<double> colHdrW = new List<double>();
    // Widest DECLARED cell width per column (pt, incl. cell padding): auto layout
    // treats a declared width as the column's floor, so an overflowing table's
    // min-content columns still honour it.
    public List<double> colDeclW = new List<double>();
    public List<double> colPctW = new List<double>();
    // Dash-aware min-content (a token breaks after each hyphen/en-dash): the floor the
    // declared-percent grid uses, so "B13-9876" can wrap to "B13-"/"9876" instead of
    // holding its column at the full unbroken token width.
    public List<double> colMinBrkW = new List<double>();
    // The widest HARD content floor any cell of the grid holds - a nested grid and its host
    // cell's pads - below which a declared box cannot cap the grid (it overflows instead).
    public double hardMinPt;
    // Per-column min-content with nested grids counted at THEIR min-content (a spanning cell's on its
    // first column): summed, the floor a declared box cannot cap the grid below.
    public List<double> colMinFloorW = new List<double>();
    // UA-serif grid floors: the same cells measured in the UA Times at the
    // row-cascaded size, content only - no padding and no legacy slack - the
    // true min-content of a percent-of-box or undeclared table, whose chrome
    // the sheet model adds per column edge (probed on the two-table report
    // and the returns grid). Kept as a SEPARATE set so every legacy consumer
    // of colMinW stays byte-calibrated.
    public List<double> colMinSerifW = new List<double>();
    // …and whether that floor is a text control's box (its ink ends 3 before its advance).
    public List<bool> colControlFloor = new List<bool>();
    public List<double>? colGroupPt = null;
    public List<double>? colWidthsPt = null;
    public int maxCols = 0;
    public int colCursor = 0;
    // Redline: a column declared at CONFLICTING percents across rows (the
    // review tables mix 86.58% single-cell rows into a 13.42/86.58 grid).
    public bool colPctConflict = false;
    // Column-spanning cells constrain the SUM of the columns they cross, not each one;
    // recorded here and resolved after all single-column widths are known.
    public List<(int start, int span, double min, double max, double hdr)> spanConstraints = new List<(int start, int span, double min, double max, double hdr)>();
    // The widest whole line a column-spanning NOWRAP cell keeps, measured in the cell's own face:
    // the grid can be no narrower than it whatever its columns declare (probed on the land-register
    // order: a 102-character Courier New 8 disclaimer spanning 14 declared-px columns sizes the sheet
    // 96 + 102 x 4.8007 + 90 = 675.67, and the columns then fit inside that box).
    public double spanNoWrapMinW = 0;
    // …and the column span that widest nowrap line covers (start, count): the line IS the grid's
    // min-content only when it runs across every column that holds content.
    public int spanNoWrapStart = 0;
    public int spanNoWrapCols = 0;
    // The per-cell slack the legacy model adds to every column minimum (a column at exactly that
    // slack holds nothing).
    public double cellExtraPt = 0;
    // The sheet collapses this grid's borders: its box takes half of the outer cell rules.
    public bool sheetCollapsed = false;
    // The widest ROW's bare demand (its cells' minima side by side): the grid's true min-content,
    // which the per-column maxima over-count when different rows floor different columns.
    public double rowNoWrapMinW = 0;

    // True only when the markup/CSS actually DECLARES a table width — the frac
    // itself defaults to a full box, so it cannot stand in for "declared".
    public bool tableWidthDeclared = false;
    // …and only an ABSOLUTE declaration ("9.75in") can PIN the natural width:
    // a percent width resolves against the box, and when the min-content floors
    // overflow that box the sheet grows to them (a
    // status report's 100%-wide milestone grid widens the page).
    public bool tableWidthDeclaredAbs = false;
    // The declared width itself, in points — the box the fixed columns squeeze
    // into (the frac above clamps to the caller's available width and loses it).
    public double tableWidthDeclAbsPt = 0;
    public double tableWidthFrac = 1.0;
    // The DOCUMENT sheet's `table { width: 650px }` element rule declares this
    // table's width the same way a segment-local rule would — the fragment map
    // is empty when the rules live in the page's own <style> block. Lifted
    // dialect only, like every other document-rule read in this build.
    public bool tableWidthFromDocRule = false;
    // A chain rule addressing the table itself ('.Managers { width: 100% }'
    // under '#ReportTable') declares its width like a flat rule would.
    // The flag says the declared width is a PERCENT of a box only known at draw:
    // such a grid emits PERCENT columns and never sizes the sheet, whichever
    // spelling declared it.
    public bool tableWidthPctOfBox = false;
    /// <summary>The sheet reaches this grid through its ancestors (a chain rule on its cells or an
    /// `X table` rule): the ancestor-styled dialect, whose declared box its auto columns fill.</summary>
    public bool ancestorScopedGrid = false;
    public double tblCellSpacingPt = 0.0;
    public double tblHeightPx = 0;
    // The table's declared cellspacing, in points; 0 when it declares none.
    public double cellSpacingPt = 0;
    public double preMaxLinePt = 0;
    // pt-styled fragment: widest per-cell border seen — the collapse model's
    // declared table width spans the OUTER BORDER CENTERS, one border width
    // short of the full border box (probed: 466.1 declared, 465.6 drawn).
    public double ptMaxCellBorderW = 0;
    // Column indices whose cells hold a nested grid (those columns stretch to
    // absorb the table's surplus — the grid fills them).
    public HashSet<int>? nestedTableCols = null;
    // Cells holding an <hr>: the pre-grown dialect draws each as a rule bar
    // across its spanned columns (the section separators of the case plan).
    public List<(Row Row, Cell Cell)>? hrCells = null;
    // The cells whose content came from a <pre> — they span the surplus
    // column the grown grid appends, so every OTHER cell (headers, bands)
    // keeps the declared column geometry.
    public List<(Row Row, Cell Cell)>? preCells = null;
}
}
