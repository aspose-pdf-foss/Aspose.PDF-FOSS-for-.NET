using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Render the @media-print invoice sheet (see the caller's gate):
    /// label/value tables, the item table with dashed cell bottoms and totals
    /// rows, the trailer table, a dashed hr, and the QR bitmap — at the measured
    /// geometry. Null when the document does not fit the shape (the caller falls
    /// through to the ordinary flow).</summary>
    private static Document? TryRenderPrintInvoice(string html, HtmlLoadOptions? options)
    {
        var pv = new PrintInvoiceState();
        pv.html = html;
        pv.options = options;
        pv.bodyPctM = Regex.Match(pv.html,
            @"body\s*\{[^}]*?width\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase);
        if (!pv.bodyPctM.Success) return null;
        if (!double.TryParse(pv.bodyPctM.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var bodyPct)
            || bodyPct is <= 0 or > 100) return null;

        pv.css = ParseStyleSheet(pv.html);
        try
        {
            pv.dom = ParseDom(Regex.Replace(Regex.Replace(pv.html,
                @"<!--[\s\S]*?-->", m => new string(' ', m.Length)),
                @"<(script|style|head)[^>]*>[\s\S]*?</\1>",
                m => new string(' ', m.Length), RegexOptions.IgnoreCase));
        }
        catch { return null; }

        pv.tables = new List<List<(List<(string Text, bool Th, bool Italic)> Cells, bool AnyTh)>>();
        CollectInvoiceTables(pv);
        if (pv.tables.Count < 2) return null;
        pv.itemTableIdx = -1;
        for (var t = 0; t < pv.tables.Count; t++)
            foreach (var (cells, _) in pv.tables[t])
            {
                var filled = 0;
                foreach (var (txt, _, _) in cells) if (txt.Length > 0) filled++;
                if (filled >= 4) { pv.itemTableIdx = t; break; }
            }
        if (pv.itemTableIdx < 0) return null;

        pv.doc = new Document();
        pv.pageW = PrintLeftPt + PrintContainerPt + PrintRightBandPt;
        pv.page = pv.doc.Pages.Add(pv.pageW, 842.0);
        pv.fontDict = new Core.PdfDictionary();
        EnsureFonts(pv.page, pv.fontDict);

        pv.x0 = PrintLeftPt;
        pv.bodyW = PrintContainerPt * bodyPct / 100.0;
        pv.bodyR = pv.x0 + pv.bodyW;
        pv.fontPt = 9.0;
        pv.reg = PosFace("Calibri");
        pv.bold = PosFace("Calibri Bold");
        pv.ital = PosFace("Calibri Italic");
        pv.inv = System.Globalization.CultureInfo.InvariantCulture;

        pv.colEdges = new double[PrintItemColEdgeFrac.Length];
        for (var c = 0; c < pv.colEdges.Length; c++)
            pv.colEdges[c] = pv.x0 + PrintItemColEdgeFrac[c] * pv.bodyW;

        pv.yTop = PrintFirstTopPt;
        pv.trailerTop = -1;   // glyph top of the first trailer (ZOI) row
        RenderInvoiceTables(pv);

        // Trailing dashed hr and the QR bitmap, both anchored on the trailer top.
        if (pv.trailerTop < 0) pv.trailerTop = pv.yTop;
        Dash(pv, pv.x0, pv.bodyR, pv.trailerTop + PrintHrFromTrailerPt);
        pv.qrM = Regex.Match(pv.html, @"<img[^>]*src\s*=\s*[""'](data:image/[^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (pv.qrM.Success && LoadConverterImage(pv.qrM.Groups[1].Value, pv.options) is { } qrBytes)
        {
            var zTop = pv.trailerTop + PrintQrDropPt;
            var qx1 = pv.bodyR - PrintQrRightInsetPt;
            try
            {
                pv.page.AddImage(qrBytes, new Rectangle(
                    qx1 - PrintQrSizePt, 842.0 - zTop - PrintQrSizePt, qx1, 842.0 - zTop));
            }
            catch { }
        }
        return pv.doc;
    }
    /// <summary>Gap a left-floated box keeps between itself and the text beside it.</summary>
    private const double FloatGutterPt = 6;

    /// <summary>How far a line's BOX top stands above its baseline: the face's ascent plus
    /// the half-leading. A line is beside a float while its box TOP is above the float's
    /// bottom edge, so the count is taken from there, not from the baseline — measured on
    /// the certificate, whose paragraph keeps seven narrow lines and releases the eighth.
    /// A face without usable metrics keeps the flat line box.</summary>
    private static double FloatLineBoxRise(Block block, double fontSizePt, double lineHeightPt)
    {
        if (block.FontFamily is not { Length: > 0 } fam
            || WinMetricsFor(fam) is not { } fm)
            return lineHeightPt / 2;
        return fm.asc * fontSizePt + (lineHeightPt - fm.sum * fontSizePt) / 2;
    }

    /// <summary>The face the float flow measures a block's lines with — the family it
    /// resolved, in the style the block carries.</summary>
    private static string FloatFlowMeasureFace(Block block) =>
        block.FontRes == "F2" || block.EmBold ? block.FontFamily + " Bold"
        : block.FontRes == "F3" || block.EmItalic ? block.FontFamily + " Italic"
        : block.FontFamily!;

    // Print-invoice sheet constants — every value measured on the expected
    // render of the @media-print invoice fixture (page 931.25 × 842):
    // sheet = 96 + the print container + the fitted right band; the body is the
    // sheet's width% of the container; text runs on a 19px (14.25pt) pitch with
    // a fresh table opening one 21px (15.75pt) band below the previous row.
    private const double PrintContainerPt = 751.25;   // the engine's 1000px-class print viewport

    private const double PrintRightBandPt = 84.0;     // fitted right band (112px)

    private const double PrintLeftPt = 96.0;

    private const double PrintRowPitchPt = 14.25;     // 19px line band @ 9pt Calibri

    private const double PrintTableOpenPt = 15.75;    // 21px first-row band of a fresh table

    private const double PrintFirstTopPt = 87.9;      // page top → first row glyph top

    private const double PrintCellInsetPt = 2.25;     // cell chrome (border-spacing + padding)

    private const double PrintValueColFrac = 0.4;     // label tables: value col at 40% of the container

    private const double PrintZoiValueOffPt = 123.7;  // trailer table: value col offset

    private const double PrintColRightInsetPt = 0.7;  // right-aligned runs off the column edge

    private const double PrintDashDropPt = 10.3;      // values glyph top → dashed cell bottoms

    private const double PrintTrailerGapPt = 28.5;    // totals → trailer rows (two bands)

    private const double PrintHrFromTrailerPt = 90.2; // trailer top → dashed hr (347.9→438.1)

    private const double PrintQrDropPt = 26.5;        // trailer top → QR image top

    private const double PrintQrSizePt = 48.19;       // 1.7cm QR bitmap

    private const double PrintQrRightInsetPt = 10.77; // body right → QR right edge

    // The item table's column RIGHT edges as fractions of the body width and the
    // trailer rows' measured pitches (dl margins ride the last two).
    private static readonly double[] PrintItemColEdgeFrac = { 0.1701, 0.3467, 0.5439, 0.7695, 1.0 };

    private static readonly double[] PrintTrailerPitchPt = { 14.25, 17.1, 19.9 };

    // Calibri's ascender (typo ascent 750/1000 + the win gap) — seats a baseline
    // from a measured glyph top.
    private const double PrintCalibriAscEm = 0.952;

}
