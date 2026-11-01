using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The stages of the metric &lt;img> open: the in-cell image, its source read and its absolute placement.</summary>
    private static void OpenMetricImgInCell(MetricParseState mps, Token tok, StringBuilder text, IReadOnlyDictionary<string, Dictionary<string, string>> css, bool stdSerif, bool reportCells, HtmlLoadOptions? loadOptions)
    {
        var mi = new MetricImgOpenState();
        mi.mps = mps;
        mi.tok = tok;
        mi.text = text;
        mi.css = css;
        mi.stdSerif = stdSerif;
        mi.reportCells = reportCells;
        mi.loadOptions = loadOptions;
        mi.imgH = 0;
        if (mi.tok.Attributes is { } ia && ia.TryGetValue("height", out var ihv)
            && double.TryParse(ihv.Trim().TrimEnd('p', 'x'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var ihn))
            mi.imgH = ihn * PxPt;
        if (mi.imgH <= 0 && mi.mps.cell!.ClassNames is not null)
            foreach (var icn in mi.mps.cell.ClassNames)
                if (mi.css.TryGetValue("." + icn + " img", out var imgRule)
                    && imgRule.TryGetValue("height", out var irh)
                    && TryParseLength(irh.Trim()) is { } irhPt)
                    mi.imgH = Math.Max(mi.imgH, irhPt);
        mi.mps.cell!.ImgHPt = Math.Max(mi.mps.cell.ImgHPt, mi.imgH);
        if (mi.tok.Attributes is { } iaw && iaw.TryGetValue("width", out var iwv)
            && double.TryParse(iwv.Trim().TrimEnd('p', 'x'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var iwn))
            mi.mps.cell.ImgWPt = Math.Max(mi.mps.cell.ImgWPt, iwn * PxPt);
        // A PERCENT style width is a share of the cell's content box, known only once the
        // columns are solved: the picture's natural box is read below for its aspect and the
        // share is resolved onto it then (see ResolveMetricImgFractions).
        if (mi.mps.cell.ImgWPt <= 0 && mi.tok.Attributes is { } iaF && iaF.TryGetValue("style", out var ifSt)
            && SafeRegex.Match(ifSt, @"(?<![-\w])width\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase) is { Success: true } ifm
            && double.TryParse(ifm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var ifPct) && ifPct > 0)
            mi.mps.cell.ImgWFrac = ifPct / WholeWidthPercent;
        if (mi.tok.Attributes is { } iaD && iaD.TryGetValue("style", out var idSt) && idSt is not null
            && SafeRegex.IsMatch(idSt, @"(?<![-\w])display\s*:\s*block\b", RegexOptions.IgnoreCase))
            mi.mps.cell.ImgBlock = true;
        // A SIZED mislabelled data URI (GIF bytes shipped as
        // image/png — base64 opening "R0lGOD") draws at its
        // attribute box: without bytes the cell reserves the box
        // and paints nothing. Truthful data URIs keep the legacy
        // calibrated paths (their own arms below).
        if (mi.mps.cell.ImgBytes is null && (mi.mps.cell.ImgWPt > 0 || mi.mps.cell.ImgHPt > 0)
            && mi.tok.Attributes is { } iaS
            && iaS.TryGetValue("src", out var ssrc)
            && ssrc.Contains("base64,R0lGOD", StringComparison.Ordinal)
            && LoadConverterImage(DecodeEntities(ssrc), mi.loadOptions) is { Length: > 0 } sbytes)
            mi.mps.cell.ImgBytes = sbytes;
        // An <img> that declares NO box of its own takes its file's
        // OWN pixel size: the browser lays an unsized image out at
        // its intrinsic dimensions, 1 css px to 0.75 pt. Without
        // this the picture is never drawn and its column collapses
        // to nothing, which then mis-sizes the row and the sheet.
        // …MISLABELLED data URIs included: a GIF shipped as
        // image/png (base64 opening "R0lGOD") loads here — the
        // loader normalises it to PNG bytes, so the intrinsic-size
        // read sees a decodable picture. Truthful data URIs keep
        // the legacy calibrated paths (JPEG viewport clamp etc.).
        ReserveMetricImgNaturalBox(mi);
        // A data-URI JPEG draws at its INTRINSIC aspect, ignoring
        // the width/height attributes: an oversized photo clamps
        // to the engine's image viewport and overflows its column
        // (measured: a 1024×768 px photo lands 612×459 pt).
        ReadMetricImgJpegSource(mi);
        // A data-URI PNG inside an abs-positioned div (left:N%):
        // drawn at natural size at the offset, out of the flow.
        PlaceMetricImgAbsolute(mi);
        // (a picture that did not load has no box to share: the broken-image rules keep their own)
        if (mi.mps.cell.ImgBytes is null) mi.mps.cell.ImgWFrac = 0;
        // a REMOTE image the renderer cannot fetch shows its alt
        // text in the reserved box (the expected render's broken-
        // image behaviour — the header logo draws its name)
        // ...and so does any image whose source cannot be loaded at all (the boleto's
        // barcode temp file names its alt in the expected render)
        if (!mi.mps.cell.ImgPlaceholder && mi.tok.Attributes is { } iaAlt
            && iaAlt.TryGetValue("alt", out var altT)
            && !string.IsNullOrWhiteSpace(altT)
            && iaAlt.TryGetValue("src", out var altSrc)
            && (((mi.reportCells || mi.mps.uaFormCells) && altSrc.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase))
                || (mi.mps.cell.ImgBytes is null
                    && !altSrc.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    && !altSrc.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    && LoadConverterImage(DecodeEntities(altSrc), mi.loadOptions) is null)))
        {
            if (mi.mps.curSeg is null && CollapseWs(mi.text.ToString()).Trim(' ').Length == 0)
                mi.mps.cell.AltTextOnly = true;
            // (a UA form cell shows the remote image's alt in its own 1 px-bordered box)
            if (mi.mps.uaFormCells && altSrc.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase))
                mi.mps.cell.AltBoxed = true;
            (mi.mps.curSeg is not null ? mi.mps.divText : mi.text)
                .Append(' ').Append(DecodeEntities(altT)).Append(' ');
        }
    }
}
