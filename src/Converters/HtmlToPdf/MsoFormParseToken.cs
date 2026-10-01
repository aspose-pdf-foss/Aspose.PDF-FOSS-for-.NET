using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal partial class HtmlToPdfConverter
{
    /// <summary>Consumes one token of the MSO form table: text feeds the open cell (or the selected option's box), a close tag flushes and pops its state, an open tag starts a row, cell, run style, input or select.</summary>
    private static bool ParseMsoFormToken(MsoFormParseState mp, Token tok)
    {
        if (tok.Kind == TokenKind.Text)
        {
            if (mp.inSelect)
            {
                // only the SELECTED option's text shows, inside the box
                if (mp.inSelectedOption && mp.cell is not null && mp.cell.Runs.Count > 0
                    && mp.cell.Runs[^1].Input is { Select: true } selBox)
                    selBox.Value = (selBox.Value == "?" ? "" : selBox.Value)
                                   + CollapseWs(DecodeEntities(tok.Value)).Trim();
                return true;
            }
            if (mp.cell is not null) mp.text.Append(DecodeEntities(tok.Value));
            return true;
        }
        var tag = tok.Tag!.ToLowerInvariant();
        if (tok.IsClose)
        {
            HandleMsoCloseTag(mp, tag);
            return true;
        }
        switch (tag)
        {
            case "table":
                // the roster's nested table: its host cell closes as a label
                // row; the nested rows follow tagged Nested
                if (mp.cell is not null)
                {
                    mp.cell.NestedHost = true;
                    CloseCell(mp);
                    CloseRow(mp);
                    mp.nestedDepth++;
                }
                break;
            case "tr":
                HandleMsoRowOpen(mp, tok);
                break;
            case "td":
                HandleMsoCellOpen(mp, tok);
                break;
            case "p":
                HandleMsoParagraphOpen(mp, tok);
                break;
            case "br":
                FlushText(mp);
                // a bare <br> still occupies its line box (the contact-info
                // row's <br><br><br> rhythm paces the row)
                if (mp.cell is not null && mp.pendingLine)
                    mp.cell.Runs.Add(new MsoRun { Text = "", NewLine = true, Fs = mp.fs, Face = mp.face });
                mp.pendingLine = true;
                mp.pendingBr = true;
                break;
            case "b": case "strong": FlushText(mp); mp.bold = true; break;
            case "i": case "em": FlushText(mp); mp.ital = true; break;
            case "span":
                HandleMsoSpanOpen(mp, tok);
                break;
            case "input":
            {
                HandleMsoInputOpen(mp, tok);
                break;
            }
            case "select":
            {
                FlushText(mp);
                if (mp.cell is null) break;
                // a dropdown draws as a small input-height box with the
                // selected option's text (measured: the Other box 47.8 wide)
                mp.cell.Runs.Add(new MsoRun
                {
                    Input = new MsoInputBox { Select = true, WPt = 47.8 },
                    NewLine = mp.pendingLine, Center = mp.center,
                });
                mp.pendingLine = false;
                mp.inSelect = true;
                break;
            }
            case "option":
                mp.inSelectedOption = (tok.Attributes is { } oa && oa.ContainsKey("selected"))
                    || (mp.cell is not null && mp.cell.Runs.Count > 0
                        && mp.cell.Runs[^1].Input is { Select: true, Value: "" });
                break;
        }
        return true;
    }
}
