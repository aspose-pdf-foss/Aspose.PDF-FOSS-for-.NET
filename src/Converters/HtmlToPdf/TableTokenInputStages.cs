using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A form control inside a grid cell: its box, its label text and the widget the dialect draws for it.</summary>
    private static void OpenInputControlInCell(TableStyleConfig cfg, TableParseState ps, Table table, Token tok)
    {
        tok.Attributes!.TryGetValue("type", out var inType);
        inType = inType?.Trim().ToLowerInvariant() ?? "text";
        // A radio in a form grid rides its text line INLINE as a marker
        // char (`◯ ◯Yes ◉ ◉No` sets on one line); the
        // factory-built option is drawn as the circle glyph and its
        // widget placed there by the table render pass.
        if (inType == "radio" && cfg.makeRadio is not null)
        {
            tok.Attributes.TryGetValue("name", out var rName);
            var rChecked = tok.Attributes.ContainsKey("checked");
            var rOpt = cfg.makeRadio(rName ?? "", rChecked);
            ps.line.Append(rChecked
                ? Table.InlineRadioCheckedChar : Table.InlineRadioChar);
            (ps.cellInlineOptions ??= new List<Aspose.Pdf.Forms.RadioButtonOptionField>())
                .Add(rOpt);
            ps.lineHadText = true;
        }
        // A push button in a form grid draws as its 3D chrome around
        // the caption (the Print/Close controls); the
        // caption rides the line between PUA markers so the column
        // measures it and the render pass draws the box.
        else if (inType is "button" or "submit" && cfg.makeRadio is not null
            && tok.Attributes.TryGetValue("value", out var btnVal)
            && !string.IsNullOrWhiteSpace(btnVal))
        {
            ps.line.Append(Table.InlineButtonChar).Append(btnVal.Trim())
                .Append(Table.InlineButtonEndChar);
            ps.lineHadText = true;
        }
        // A UA grid's checkbox draws nothing in the page: it is a form checkbox whose widget
        // appearance shows the check, seated in a 13px inline box the cell measures.
        else if (!cfg.dwFormCells && inType == "checkbox" && cfg.makeCheckbox is not null)
            OpenUaGridCheckbox(cfg, ps, tok);
        // DataWorks: a checked checkbox is a bare checkmark glyph;
        // a text-like control is its declared pixel box with the
        // value typeset inside.
        else if (cfg.dwFormCells && inType == "checkbox")
        {
            if (tok.Attributes.ContainsKey("checked"))
            {
                ps.line.Append(Table.InlineCheckChar);
                ps.rowMinHeightPt = Math.Max(ps.rowMinHeightPt, DwCheckboxRowHPt);
                ps.lineHadText = true;
            }
            else
            {
                // A borderless unchecked box still OCCUPIES its
                // widget width (the results row's text starts past
                // it) without contributing to the column's min.
                ps.line.Append(Table.InlineCheckboxGapChar);
                ps.cellImgWidthPt += Table.DwHiddenInlinePt;
                table.HtmlDwGapReservePt += Table.DwHiddenInlinePt;
                ps.rowMinHeightPt = Math.Max(ps.rowMinHeightPt, DwCheckboxRowHPt);
                ps.lineHadText = true;
            }
        }
        // …a FILE control is the browser chrome: its button and
        // the no-selection caption.
        else if (cfg.dwFormCells && inType == "file")
        {
            // The file control opens its OWN line (the Remove
            // button's div closes above it).
            if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
            ps.line.Append(Table.InlineButtonChar).Append(DwFileButtonCaption)
                .Append(Table.InlineButtonEndChar).Append(" No file chosen");
            ps.lineHadText = true;
        }
        else if (cfg.dwFormCells
            && inType is not ("hidden" or "submit" or "button" or "image" or "radio"))
        {
            var (diW, diH) = ParseInputSize(tok.Attributes.TryGetValue("style", out var diSt) ? diSt : null);
            tok.Attributes.TryGetValue("value", out var diVal);
            ps.line.Append(Table.InlineInputChar);
            (ps.cellInputBoxes ??= new()).Add((diW > 0 ? diW * 0.75 : DwSelectBoxWPt,
                diH > 0 ? diH * 0.75 : DwInputBoxHPt, diVal ?? "", false, 0));
            ps.lineHadText = true;
            ps.cellImgWidthPt = Math.Max(ps.cellImgWidthPt,
                (diW > 0 ? diW * 0.75 : DwSelectBoxWPt) + 4);
        }
        // A bare UA grid's text-like control is a box its column must hold whatever the
        // sheet asked of it (probed: the unsized 20-column inputs of the six-column
        // verification row seat 118.54 wide at A4 and the sheet grows to hold the last).
        else if (cfg.uaSerifMin && inType is not ("checkbox" or "radio" or "hidden" or "submit" or "button" or "image" or "file"))
        {
            ps.cellControlBoxPt = Math.Max(ps.cellControlBoxPt, UaTextControlAdvancePt(tok.Attributes));
            ps.lineHadText = true;
        }
        else if (inType is not ("checkbox" or "radio" or "hidden" or "submit" or "button" or "image")
            && tok.Attributes.TryGetValue("value", out var inVal)
            && !string.IsNullOrWhiteSpace(inVal))
        {
            if (ps.line.Length > 0 && !char.IsWhiteSpace(ps.line[^1])) ps.line.Append(' ');
            ps.line.Append(inVal);
            ps.lineHadText = true;
        }
    }
}
