using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The row's own style attribute sets its height, background and rules.</summary>
    private static void ApplyMetricRowStyleAttribute(MetricTableState mt, Token tok)
    {
        if (tok.Attributes is { } tra && tra.TryGetValue("style", out var trst))
        {
            // per-row inline styles (the official-letter dialect
            // sizes and paces every row this way)
            var fsm = Regex.Match(trst, @"font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (fsm.Success && TryParseCssFontSize(fsm.Groups[1].Value.Trim()) is { } trfs)
                mt.mps.rowFs = trfs;
            // …and its face and weight, which its cells inherit the same way its size does
            // (measured: a row declaring Arial 10 draws its cells in Arial 10 and pitches them
            // on that face's line box, where the flow default gave the serif at 13.5).
            var ffm = Regex.Match(trst, @"(?<![-\w])font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            // (a face the HTML engine does not resolve draws the UA serif in a UA row - not the
            // table's own face: the cheque's `MICR Plain` row draws Times inside its Arial table)
            if (ffm.Success && FirstFontFamily(ffm.Groups[1].Value) is { Length: > 0 } trFam)
                mt.mps.rowFace = !mt.stdSerif || SourceEngineFaces.Contains(trFam) ? CanonicalStandardFaceName(trFam) : UaSerifFaceName;
            if (Regex.IsMatch(trst, @"(?<![-\w])font-weight\s*:\s*bold", RegexOptions.IgnoreCase))
                mt.mps.rowBold = true;
            // A declared FACE is what re-pitches the row: it changes the line box the cells
            // measure on. A size alone rides the table's own base line, as it always has.
            // (…and in the UA grid a size alone re-pitches it too - measured: the 14 px returns
            // rows band 12 + 6 = 18, where the table's 12 pt strut gave 19.5)
            mt.mps.rowInlineTypo = ffm.Success || (mt.stdSerif && fsm.Success);
            var ham = Regex.Match(trst, @"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase);
            if (ham.Success)
                mt.mps.rowAlign = ham.Groups[1].Value.ToLowerInvariant() switch
                {
                    "right" => HorizontalAlignment.Right,
                    "center" => HorizontalAlignment.Center,
                    _ => HorizontalAlignment.Left,
                };
            var hm2 = Regex.Match(trst, @"height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (hm2.Success) mt.mps.pendingRowH = DtpNum(hm2.Groups[1].Value) * PxPt;
            // (…and a UA row's height in POINTS - the Words export's `height:112.5pt` - bands it the
            // same way: probed on the Words letter, the address grid's second row stands 112.5)
            else if (mt.stdSerif
                && Regex.Match(trst, @"(?<![-\w])height\s*:\s*([\d.]+\s*(?:pt|in|cm|mm))", RegexOptions.IgnoreCase) is { Success: true } hmPt
                && TryParseLength(hmPt.Groups[1].Value.Replace(" ", "")) is { } trPt && trPt > mt.mps.pendingRowH)
                mt.mps.pendingRowH = trPt;
            // …and a row's inline colour is its cells' ink, as its class's would be (the
            // worksheet's `color: rgb(46, 0, 0)` section rows).
            var trFore = Regex.Match(trst, @"(?<![-\w])color\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (trFore.Success && ParseCssColor(trFore.Groups[1].Value.Trim()) is { } trFc) mt.mps.rowFore = trFc;
            // …and a row's inline background tints it like its class's would.
            var bgm = Regex.Match(trst, @"background(?:-color)?\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (bgm.Success && ParseCssColor(bgm.Groups[1].Value.Trim()) is { } trBg) mt.mps.rowBg = trBg;
        }
    }
}
