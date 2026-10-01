using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Quote schedule: one block of the fragment rendered.</summary>
    private void RenderQuoteBlock(QuoteScheduleState qs, QsBlock b)
    {
        switch (b.Kind)
        {
            case "pagebreak":
                Break(qs);
                break;

            case "img":
            {
                if (b.Image is null) break;
                qs.flow.MoveCursorTo(qs.y);
                qs.flow.PlaceImageBlock(b.Image, b.ImgW, b.ImgH);
                // the image sits ON the line's baseline; the line keeps the
                // ambient strut's descent below it
                qs.y = qs.flow.CurrentY
                    - (QsLineBox(QsDefaultFontPx) - QsBaselineInLine(QsDefaultFontPx));
                break;
            }

            case "line":
                qs.y -= QsLineBox(b.FontPx);
                break;

            case "para":
            {
                foreach (var ln in Wrap(qs, b.Text, b.FontPx, qs.bodyW, b.Bold, b.Italic))
                {
                    var box = QsLineBox(b.FontPx);
                    if (qs.y - box < qs.marginBottom) Break(qs);
                    var w = Measure(qs, ln, b.FontPx, b.Bold, b.Italic);
                    var x = b.Centre ? qs.left + (qs.bodyW - w) / 2 : qs.left;
                    var baseY = qs.y - QsBaselineInLine(b.FontPx);
                    // WriteAbsoluteText seats a fragment by its BOX BOTTOM, which
                    // sits one descent under the baseline.
                    qs.flow.WriteAbsoluteText(x, baseY - QsDescentEm * b.FontPx * QsPxToPt,
                        ln, b.FontPx * QsPxToPt, Face(qs, b.Bold, b.Italic));
                    if (b.Underline && w > 0)
                        Rule(qs, x, x + w, baseY - b.FontPx * QsPxToPt * QsUnderlineDrop,
                            b.FontPx * QsPxToPt * QsUnderlineW);
                    qs.y -= box;
                }
                break;
            }

            case "table":
            {
                RenderQuoteTable(qs, b);
                break;
            }
        }
    }
}
