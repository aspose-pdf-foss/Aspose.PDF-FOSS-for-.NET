using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of <see cref="BuildTableFromHtml"/>: the column
/// and width model the parse loop fills and the width solver consumes. One instance
/// per invocation; never shared.</summary>
private sealed class TableStyleConfig
{
    // The <table> tag's own inline style="…" / attributes (font, width, border, cellpadding)
    // take precedence over stylesheet rules — CMS/report HTML commonly styles the table
    // inline rather than via a <style> block, so honour both.
    public Match tblTag = null!;
    public Dictionary<string, string> tblStyle = null!;
    // The document sheet's `a { color: … }` rule colours anchor text in cells
    // (the expected render applies it like any inline colour).
    public Color? docAnchorColor;
    public Dictionary<string, Dictionary<string, string>> css = null!;
    public double cellFontSize;
    // Cells sized by a `font:` SHORTHAND rule render as CSS line boxes (1.2 em
    // pitch) — the form-document dialect; longhand-sized cells keep the legacy
    // uniform grid their tests are calibrated to.
    public bool cellFontShorthand;
    // Inline-styled grid: the <table> tag's own style declares the face. When it
    // resolves to an installed face, the cells draw with that face's real
    // kerned advances (Type0) and the rows pitch at its CSS line box — the
    // reference typesets such a grid in the declared face, never Helvetica.
    public double inlineFaceRatio;
    // Document-level stylesheet base (a `body, table, td { font: … }` rule outside this
    // <table> snippet). Consulted ONLY by the styled-cell (mixed per-line font) path so
    // unstyled lines inherit the true CSS size/family; the legacy cellFontSize above —
    // and every measurement that feeds column widths — is deliberately left untouched.
    public double cssBasePt;
    public string? cssBaseFamily;
    // A grid whose type came from the DOCUMENT's own body rule and which carries no
    // run styling of its own is laid out on the browser's box model throughout: the
    // UA stylesheet supplies its line box, its `td { padding: 1px }` and its
    // `border-spacing: 2px`. A grid that DOES style its runs is already sized by that
    // dialect, whose padding and box model are calibrated separately.
    public bool uaDocGrid;
    // `* { word-break: break-word }` on the sheet makes every token breakable
    // ANYWHERE, and intrinsic min-content shrinks to the character — a
    // 200-character unbroken test string must not eat its neighbour's
    // declared percent share at the squeeze.
    public bool breakAnywhereDoc;
    public bool hasBorder;
    public double borderWidth;
    public Color borderColor = null!;
    public double pad;
    // The border came from an ELEMENT rule (`td { border: … }`) with no
    // border-collapse: the cells keep SEPARATE borders, and a CSS row height
    // is the row's content box — its own border rides on top, so the row
    // pitch is height + one border width (measured 20px + 1px = 15.75 pt).
    public bool elemRuleBorder;
    // The horizontal share of the cell padding, and the bottom one. Equal to `pad`
    // (the top) unless a `padding` shorthand declares the sides separately
    // ("7px 5px 6px" = top 7, sides 5, bottom 6).
    public double padSide;
    public double padBottom;
    // The chain-selector pass: rules addressed through the document tree
    // (`#ReportTable .Managers > tbody > tr > td`) reach this table and its
    // cells. The chain seats the table on the ancestors the caller threaded in
    // (nested builds inherit the outer cell's chain) and grows to its cells.
    public List<CssElem>? chainBase;
    // A chain table WITHOUT border-collapse keeps the UA's default 2px
    // border-spacing: its rows pitch half a spacing wider on each side and its
    // cell borders draw a spacing thicker (the visible white separation).
    public bool chainBorderSeparate;
    // A DECLARED border-spacing is a real gap band on all four sides of every
    // cell (`.5ex` on the risks pill = 2 pt: its three cells sit 2 pt apart and
    // 2 pt inside the grid's edges). The UA's implicit 2 px default keeps the
    // calibrated vertical-only band below — declaring the property is what turns
    // the horizontal half on.
    public double chainSpacingPt;
    // font-weight:normal on a chain-matched run CANCELS an enclosing bold
    // (`.SmallerTitle` under a bold title plate): the open stashes boldDepth,
    // the close restores it.
    public List<(string Tag, int PrevBoldDepth)> chainUnbold = null!;
    // <colgroup><col width="N"> declares the column grid up front. The layout engine gives
    // each column max(declared width, min-content) — the declared width stretches for an
    // unbreakable run but never squeezes below it — and IGNORES per-cell style widths when
    // a colgroup is present (spreadsheet exports carry a bogus full-table width on every
    // cell, which would otherwise blow each column up to the table's own width).
    // A table nested inside a cell is a table in its own right, not part of the
    // outer grid: lift each one out behind a placeholder BEFORE any structure
    // scan (a nested table's COLGROUP must not become this table's column
    // grid), then build it on its own when the placeholder reaches the cell
    // that held it.
    public List<string> nestedHtml = null!;
}
}
