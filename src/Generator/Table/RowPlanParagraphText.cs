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
    /// <summary>The text stage of a row-plan paragraph: the fragment verdicts, the wrapped lines and their measures, and the generator-cell ledgers, verbatim. Returns true when the paragraph is done.</summary>
    private bool RowPlanParagraphText(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc, RowPlanState rp, int col, Row row, double[] colWidths, int[] cellMap, int[]? gridToCell, int[]? effRowSpan, double svgFillHeight)
    {
        pp.text = null;
        pp.fragFontSize = pc.defaultFontSize;
        pp.color = null;
        pp.fragLink = null;
        pp.fragAnchors = null;
        pp.fragEmbeddedTtf = null;
        pp.fragUnderline = false;
        pp.fragEmbeddedName = null;
        pp.fragCssAsc = 0;
        pp.fragCssDesc = 0;
        pp.fragKeepBlank = false;
        pp.fragCssForce = false;
        pp.lineAlign = pc.cellAlign;
        pp.htmlCssBoxPx = 0;
        pp.htmlBoxedDivLineH = 0;
        pp.fragBold = false;
        pp.fragItalic = false;
        pp.fragGridRuns = null;
        PlanTextFragmentSource(pp, paragraph, pc, row);
        if (PlanHtmlFragmentSource(pp, paragraph, pc, rp, row)) return true;
        if (pp.text is null) return true;
        pp.fragLineH = (paragraph as TextFragment)?.CssLineHeightPt ?? 0;
        pp.boxedFrag = (paragraph as TextFragment)?.InlineBoxes is { Count: > 0 };
        pp.thisLineHeight = !pp.boxedFrag && pp.fragLineH > 0 ? Math.Max(pp.fragFontSize, pp.fragLineH) : pp.fragFontSize;
        if (pp.htmlBoxedDivLineH > 0) pp.thisLineHeight = pp.htmlBoxedDivLineH;
        pp.fragLeading = XmlGeneratorModel
            ? XmlLineSpacing
            : CallerLineSpacing(paragraph);
        if (pp.fragLeading > 0) pp.thisLineHeight = pp.fragFontSize + pp.fragLeading;
        pp.runBoxH = CssRunBoxes && pp.fragLineH > 0 ? pp.fragLineH : 0.0;
        // With a CSS line box, a SINGLE-line cell also occupies the box
        // (tight = box height), so one-line rows pitch like wrapped ones.
        Consider(rp, pp.thisLineHeight, pp.thisLineHeight > pp.fragFontSize ? pp.thisLineHeight : pp.fragFontSize);

        // An empty TextFragment is a deliberate spacer in many cell
        // layouts (e.g. TextFragment with LineSpacing set and no
        // text). Emit it as one blank line so the row's height
        // budget includes the spacer — dropping it here would
        // collapse vertical padding that tests rely on.
        if (PlanEmptyText(pp, paragraph, pc, row)) return true;

        // Arabic/RTL cell text: the table draws cells with a single Standard-14 font in
        // single-byte encoding, which has no Arabic glyphs. Shape the text (contextual
        // presentation forms + visual bidi order) and emit it as one line flagged to be
        // drawn with an embedded Arabic-capable font (Type0/CID) by the render pass.
        if (PlanArabicText(pp, paragraph, pc, row)) return true;

        // Cell text carrying a fragment-level embedded font AND CJK content: draw each
        // newline-split line as Type0/CID with that font. Scoped to CJK so a fragment
        // that merely carries an embedded Latin font keeps the existing Standard-14 path.
        // Over-declared grid dialect: EVERY plain text line draws through
        // the coverage chain with the UA SERIF as its Latin primary — the
        // expected output sets ASCII runs (digits, parens, SPACES) in Times and
        // the CJK in SimSun on one line, and pricing ASCII at a CJK face's
        // half-em advances wraps lines that should stay whole (a SimSun
        // space is twice a Times space). It outranks the fragment's own
        // embedded face too. Control lines (inline radios, buttons) keep
        // their own arms below.
        if (PlanOverDeclaredHtml(pp, paragraph, pc)) return true;
        if (PlanEmbeddedTtfText(pp, pc)) return true;

        // CJK cell text whose fragment font can't cover it — either no font resolved or
        // the named face is unavailable (e.g. "Arial Unicode MS" isn't installed). The
        // Standard-14 path below has no CJK glyphs and would emit '?'. Substitute an
        // installed system CJK font (MS Gothic when available) and draw as
        // Type0/CID, mirroring the Arabic fallback above.
        if (PlanCjkText(pp, pc)) return true;

        // A fragment carrying an inline BUTTON marker renders as ONE control
        // line too — the render pass draws the button chrome around the
        // bracketed caption, so the line must not word-wrap through it.
        if (PlanInlineWidgets(pp, paragraph, pc)) return true;
        if (FinishParagraphTextPlan(pp, paragraph, pc, row)) return true;
        return false;
    }


    /// <summary>The plan's tail: pending anchors, the wrapped segments and the fragment's inline boxes.</summary>
    private bool FinishParagraphTextPlan(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc, Row row)
    {
        pp.pendingAnchors = pp.fragAnchors is { Count: > 0 }
            ? new List<(string Text, string Url)>(pp.fragAnchors) : null;
        // Fully-bold styled serif paragraph (e.g. <p style="font-family:georgia">
        // <strong>… in an HTML table cell): the HTML engine draws it in the
        // embedded bold serif face with kerned advances — wrap, align and
        // annotate with those metrics, not the Standard-14 estimate.
        if (PlanBoldCssText(pp, paragraph, pc, row)) return true;

        // Opted-in band tables (HonorCellFontFaces): a cell fragment that resolved
        // a serif font draws in the embedded serif face via the Type0 path, wrapped
        // with the same kerned metrics the column width was measured with. Without
        // this the text wraps and renders on the wider Standard-14 Helvetica
        // estimates, over-wrapping the serif-measured column and overgrowing rows.
        if (PlanCellFontFaceText(pp, paragraph, pc)) return true;

        // Form-document dialect (HonorCellTtfFaces): a cell fragment that resolved
        // any real installed face wraps and draws with that face's kerned hmtx
        // advances via the Type0 path. The Standard-14 Helvetica estimate is
        // narrower than faces like Verdana — it under-wraps the lines the
        // real face wraps and shortens every band below.
        if (PlanCellTtfFaceText(pp, paragraph, pc, row)) return true;

        // Generator cell in a RESOLVED installed face: the fragment's own font,
        // else the cell/row/table DefaultCellTextState's (a row whose default
        // state names Calibri Bold draws every cell in CalibriBold). The text
        // wraps on the face's real advances and draws through the Type0 path
        // in the face's own bold/italic variant (embedded as
        // "CalibriBold" / "Calibri,Italic" subsets; a Standard-14 face keeps
        // the plain path). Unkerned: generator shows are single hex strings.
        if (PlanGeneratorCellText(pp, paragraph, pc, row)) return true;

        pp.linesBeforeFrag = pc.lines.Count;
        pp.htmlMeas = HtmlLayoutWrap || CssRunBoxes || NestedTableRender
            ? s => MeasureFaceExact(s, pp.fragFontSize, pp.fragBold)
            : pp.fragEmbeddedTtf is { Length: > 12 } wrapTtf
                ? s => MeasureWidthWithFont(s, pp.fragFontSize, wrapTtf)
                : null;
        pp.ownBoxH = NestedTableRender && pp.fragLineH > 0
            && (paragraph as TextFragment)?.CssLineHeightFromCell == true
            ? pp.fragLineH : 0.0;
        // A paragraph's own vertical margin is a silent spacer box above its
        // first line (the converter hands the COLLAPSED value — max of this
        // top and the previous paragraph's bottom).
        if (NestedTableRender && ParagraphMargin(paragraph) is { Top: > 0 } pMargin)
            pc.lines.Add(new CellLine { Text = "", FontSize = pp.fragFontSize, BoxH = pMargin.Top, Align = pp.lineAlign });
        pp.genDefaultFace = GeneratorDialect && !XmlGeneratorModel && !pp.fragBold && !pp.fragItalic
            && (paragraph is not TextFragment mtf
                || ResolveGeneratorCellFace(mtf, pc.cell, row) is null);
        pp.genMeas = pp.htmlMeas ?? (pp.genDefaultFace
            ? new Func<string, double>(s => MeasureWidthExactAfm(s, pp.fragFontSize))
            : null);
        PlanTextSegments(pp, pc);
        // WrapLinesCount keeps only the fragment's first N wrapped lines.
        if (paragraph is TextFragment { WrapLinesCount: > 0 } capped
            && pc.lines.Count - pp.linesBeforeFrag > capped.WrapLinesCount)
            pc.lines.RemoveRange(pp.linesBeforeFrag + capped.WrapLinesCount,
                pc.lines.Count - pp.linesBeforeFrag - capped.WrapLinesCount);
        // The fragment's FootNote marker attaches after its last laid-out line.
        if (paragraph is TextFragment { FootNote: { } fragNote }
            && pc.lines.Count > pp.linesBeforeFrag)
            pc.lines[^1].FootNote = fragNote;
        // The fragment's own LEFT margin (an <li>'s hanging list indent from
        // the converter) indents every line it laid out.
        if (NestedTableRender && ParagraphMargin(paragraph) is { Left: > 0 } pIndent)
            for (var ii = pp.linesBeforeFrag; ii < pc.lines.Count; ii++)
                pc.lines[ii].LeftIndent = pIndent.Left;
        // Inline boxes (title plates, status pills) ride the fragment's first
        // laid-out line; the box line height (text pitch + pads) becomes the
        // line's own CSS box so the row reserves the pill's full height.
        PlanInlineBoxes(pp, paragraph, pc);
        return false;
    }

    /// <summary>Inline buttons, options, input boxes and check marks in a nested table render as widgets; true when the text was one.</summary>
    private bool PlanInlineWidgets(RowPlanParagraphState pp, BaseParagraph paragraph, RowPlanColumnState pc)
    {
        if (NestedTableRender && pp.text!.IndexOf(InlineButtonChar) >= 0)
        {
            pc.lines.Add(new CellLine
            {
                Text = pp.text, FontSize = pp.fragFontSize, ForegroundColor = pp.color,
                Align = pp.lineAlign,
                LeftIndent = ParagraphMargin(paragraph) is { Left: > 0 } btnInd
                    ? btnInd.Left : 0,
            });
            return true;
        }

        // A fragment carrying inline radio options renders as ONE control line:
        // the markers in its text draw as circle glyphs in the run (`◯ ◯Yes
        // ◉ ◉No` sits on a single line), so the line is
        // never word-wrapped — its glyph advances are the control boxes.
        if (paragraph is TextFragment { InlineOptions: { Count: > 0 } fragOpts })
        {
            pc.lines.Add(new CellLine
            {
                Text = pp.text!, FontSize = pp.fragFontSize, ForegroundColor = pp.color,
                Align = pp.lineAlign, InlineOptions = fragOpts,
                // The control line seats on its item's list indent like any
                // other line the fragment margin carries.
                LeftIndent = ParagraphMargin(paragraph) is { Left: > 0 } optInd
                    ? optInd.Left : 0,
            });
            return true;
        }

        // A fragment carrying input-box markers (DataWorks form grid)
        // is one control line as well; the render pass draws each box.
        if (paragraph is TextFragment { InlineInputBoxes: { Count: > 0 } dwFragBoxes })
        {
            pc.lines.Add(new CellLine
            {
                Text = pp.text!, FontSize = pp.fragFontSize, ForegroundColor = pp.color,
                Align = pp.lineAlign, InputBoxes = dwFragBoxes,
                LeftIndent = ParagraphMargin(paragraph) is { Left: > 0 } ibInd
                    ? ibInd.Left : 0,
            });
            return true;
        }
        // …and a bare checkmark line.
        if (NestedTableRender && pp.text!.IndexOf(InlineCheckChar) >= 0)
        {
            pc.lines.Add(new CellLine
            {
                Text = pp.text, FontSize = pp.fragFontSize, ForegroundColor = pp.color,
                Align = pp.lineAlign,
            });
            return true;
        }
        return false;
    }
}
