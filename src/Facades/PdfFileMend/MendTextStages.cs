using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileMend
{
    /// <summary>Writes the text object: fill colour, font and size, the first line at the seat, then every further line via TL and T* with its leading, each shown through the encoded operand.</summary>
    private void EmitMendText(MendTextState tx)
    {
        tx.fg = tx.ft.ForegroundColor;
        tx.sb.AppendFormat(CultureInfo.InvariantCulture,
            "{0:F3} {1:F3} {2:F3} rg\n",
            tx.fg.R / 255.0, tx.fg.G / 255.0, tx.fg.B / 255.0);

        tx.sb.Append("BT\n");
        tx.sb.AppendFormat(CultureInfo.InvariantCulture,
            "/{0} {1:G} Tf\n", tx.fontName, tx.ft.FontSize);

        // Position first line.
        tx.sb.AppendFormat(CultureInfo.InvariantCulture,
            "{0:F2} {1:F2} Td\n", tx.textX, tx.startY);

        // Emit first line
        tx.sb.AppendFormat("{0} Tj\n", ShowOperand(tx.ft.Lines[0].Text, tx.unicodeTtf, tx.unicodeFace, tx.unicodeFontDict));

        // Emit subsequent lines with TL (text leading) and T* (next line). A line's
        // /lineSpacing is extra leading applied AFTER it (before the next line), so the
        // baseline-to-baseline pitch into line i is the default line height plus the
        // PREVIOUS line's spacing — matching FormattedText.AddNewLineText semantics.
        for (var i = 1; i < tx.ft.Lines.Count; i++)
        {
            var line = tx.ft.Lines[i];
            var extra = tx.ft.Lines[i - 1].LineSpacing;
            var leading = tx.defaultLeading + (extra > 0 ? extra : 0);
            tx.sb.AppendFormat(CultureInfo.InvariantCulture,
                "{0:F2} TL\n", leading);
            tx.sb.Append("T*\n");
            tx.sb.AppendFormat("{0} Tj\n", ShowOperand(line.Text, tx.unicodeTtf, tx.unicodeFace, tx.unicodeFontDict));
        }

        tx.sb.Append("ET\n");
        tx.sb.Append("Q\n");
    }

    /// <summary>The background-colour bar behind each line, drawn before the glyphs, one per line stepping down by the leading.</summary>
    private void EmitMendBackgroundBars(MendTextState tx)
    {
        // The background-colour bar behind each line (drawn before the glyphs so it
        // renders underneath). This makes PdfFileMend honour FormattedText.BackgroundColor.
        if (!tx.ft.BackgroundColor.IsEmpty)
        {
            var bg = tx.ft.BackgroundColor;
            double bottom = tx.barBottom;
            for (var i = 0; i < tx.ft.Lines.Count; i++)
            {
                if (i > 0)
                {
                    var extra = tx.ft.Lines[i - 1].LineSpacing;
                    bottom -= tx.defaultLeading + (extra > 0 ? extra : 0);
                }
                tx.sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{0:F3} {1:F3} {2:F3} rg\n{3:F2} {4:F2} {5:F2} {6:F2} re\nf\n",
                    bg.R / 255.0, bg.G / 255.0, bg.B / 255.0,
                    tx.bgX, bottom, tx.bgW, tx.barH);
            }
        }
    }

    /// <summary>Seats the text and its bar: the probed geometry for a standard face, the legacy inset and bar for a named face, clamped to the page.</summary>
    private void PlaceMendTextBar(MendTextState tx)
    {
        if (!tx.namedFace)
        {
            tx.barBottom = tx.lly - tx.size + BarBottomRise;
            tx.barH = (tx.ascender + tx.descender) * tx.size + BarPadding;
            tx.startY = tx.barBottom + BaselineLift + tx.descender * tx.size;
            if (tx.rot != 0)
            {
                tx.barBottom -= tx.descender * tx.size;
                tx.barH += tx.descender * tx.size;
            }
            tx.textX = tx.llx + TextInsetX;
            tx.bgX = tx.llx;
            tx.bgW = tx.urx > tx.llx ? tx.urx - tx.llx : tx.displayW;
        }
        else
        {
            // Legacy placement (string-named / custom faces): the first baseline sits at
            // lly - size + 13 (slope -1 in size, e.g. lly=600: 15pt -> 598, 20pt -> 593), the
            // text is inset 0.2 * size from llx, and the background is a page-wide bar per
            // line from baseline + descent - 0.2 * size, 1.325 * size tall.
            tx.startY = tx.lly - tx.size + LegacyBaselineRise;
            tx.textX = tx.llx + tx.size * LegacyInsetFactor;
            double pad = tx.size * LegacyInsetFactor;
            tx.barBottom = tx.startY - tx.descender * tx.size - pad;
            tx.barH = tx.size * LegacyBarHeightFactor;
            tx.bgX = tx.pageMediaBox.LLX;
            tx.bgW = tx.displayW;
        }

        // Clamp to page bounds so off-page coordinates don't silently render nothing.
        if (tx.startY > tx.displayH - tx.size)
            tx.startY = tx.displayH - tx.size;
    }

    /// <summary>Resolves the size, leading and Standard-14 ascender and descender, and the page's display box and rotation matrix.</summary>
    private void ResolveMendTextMetrics(MendTextState tx)
    {
        tx.size = tx.ft.FontSize;
        tx.defaultLeading = tx.size + 2;

        tx.metricsFont = string.IsNullOrEmpty(tx.ft.FontName) ? "Helvetica" : tx.ft.FontName!;
        tx.std14 = Standard14Fonts.IsStandard14(tx.metricsFont);
        tx.ascender = (tx.std14 ? Standard14Fonts.GetAscent(tx.metricsFont) : 718) / 1000.0;
        tx.descender = (tx.std14 ? -Standard14Fonts.GetDescent(tx.metricsFont) : 207) / 1000.0;

        tx.pageMediaBox = tx.page.MediaBox;
        tx.rot = ((tx.page.RotateDegrees % 360) + 360) % 360;
        tx.swapAxes = tx.rot == 90 || tx.rot == 270;
        tx.mediaW = tx.pageMediaBox.URX - tx.pageMediaBox.LLX;
        tx.mediaH = tx.pageMediaBox.URY - tx.pageMediaBox.LLY;
        tx.displayW = tx.swapAxes ? tx.mediaH : tx.mediaW;
        tx.displayH = tx.swapAxes ? tx.mediaW : tx.mediaH;
        tx.rotCm = tx.rot switch
        {
            90 => string.Format(CultureInfo.InvariantCulture, "0 1 -1 0 {0:F2} 0 cm\n", tx.pageMediaBox.URX),
            180 => string.Format(CultureInfo.InvariantCulture, "-1 0 0 -1 {0:F2} {1:F2} cm\n", tx.pageMediaBox.URX, tx.pageMediaBox.URY),
            270 => string.Format(CultureInfo.InvariantCulture, "0 -1 1 0 0 {0:F2} cm\n", tx.pageMediaBox.URY),
            _ => string.Empty,
        };
        tx.sb.Append(tx.rotCm);
    }

    /// <summary>Picks the font resource: an embedded Unicode face for non-Latin text, a custom font file, a requested named face, else the FormattedText face.</summary>
    private void ResolveMendTextFont(MendTextState tx)
    {
        tx.namedFace = false;
        tx.needsUnicode = false;
        foreach (var probeLine in tx.ft.Lines)
        {
            foreach (var ch in probeLine.Text)
                if (ch > 255) { tx.needsUnicode = true; break; }
            if (tx.needsUnicode) break;
        }
        tx.unicodeTtf = null;
        tx.unicodeFace = "";
        tx.unicodeFontDict = null;
        if (tx.needsUnicode)
        {
            tx.unicodeFace = UnicodeFaceFor(tx.ft);
            tx.unicodeTtf = Aspose.Pdf.Text.FontRepository.GetTtfData(tx.unicodeFace);
            if (tx.unicodeTtf is not null) tx.unicodeFontDict = GetOrCreatePageFontDict(tx.page);
        }
        if (tx.unicodeTtf is not null)
        {
            var (resName, _) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                tx.unicodeFontDict!, tx.unicodeTtf, tx.unicodeFace, tx.ft.Lines[0].Text, stripSpacesInBaseFont: true);
            tx.fontName = resName;
        }
        else if (!string.IsNullOrEmpty(tx.ft.CustomFontFile) && File.Exists(tx.ft.CustomFontFile))
        {
            tx.namedFace = true;
            var resourceName = AllocateFontResourceName(tx.page);
            var embedder = FontEmbedder.EmbedFromFile(_document!, tx.ft.CustomFontFile, resourceName);
            embedder.AddToPage(tx.page);
            tx.fontName = resourceName;
        }
        else if (tx.ft.RequestedFontName is { } requested
            && requested.Replace(" ", "") is { Length: > 0 } strippedReq
            && !string.Equals(strippedReq, tx.ft.FontName, StringComparison.Ordinal))
        {
            // The caller asked for a named system font (e.g. "Times New Roman") via the
            // string-font FormattedText constructor, and its name differs from the
            // Standard-14 base font it folds to for glyph metrics ("Times-Roman"). Emit it
            // as a TrueType font whose /BaseFont is the requested name with spaces removed
            // ("TimesNewRoman"), so text extraction reports that name rather than the fold.
            tx.namedFace = true;
            tx.fontName = EnsureFont(tx.page, strippedReq, trueType: true);
        }
        else
        {
            tx.fontName = EnsureFont(tx.page, tx.ft.FontName);
        }
    }
}
