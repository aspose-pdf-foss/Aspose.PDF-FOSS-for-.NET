using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The stages of the styled-run emit: one run of the line at a time.</summary>
    private static void EmitStyledRun(StyledRunsState er)
    {
        var url = er.i < er.urls.Count ? er.urls[er.i] : null;
        // A run: same link coverage, same face, and same raised state for every
        // codepoint. A char that carries an extra pen advance (a word-spacing
        // stretched space) ends its run so the next one repositions.
        int j = er.i;
        var faceName = PosFaceNameFor(char.IsHighSurrogate(er.lineText[er.i]) && er.i + 1 < er.lineText.Length
            ? char.ConvertToUtf32(er.lineText[er.i], er.lineText[er.i + 1]) : er.lineText[er.i]);
        var isSupRun = er.supFlags.Count > er.i && er.supFlags.ElementAt(er.i);
        var runFs = isSupRun ? er.supFontSize : er.fontSize;
        var runY = er.y + (isSupRun ? er.supRise : 0);
        double runW = 0;
        var runSb = new StringBuilder();
        while (j < er.lineText.Length)
        {
            var u2 = j < er.urls.Count ? er.urls[j] : null;
            if (!string.Equals(u2, url, StringComparison.Ordinal)) break;
            if ((er.supFlags.Count > j && er.supFlags.ElementAt(j)) != isSupRun) break;
            int cp = er.lineText[j];
            int cpLen = 1;
            if (char.IsHighSurrogate(er.lineText[j]) && j + 1 < er.lineText.Length && char.IsLowSurrogate(er.lineText[j + 1]))
            {
                cp = char.ConvertToUtf32(er.lineText[j], er.lineText[j + 1]);
                cpLen = 2;
            }
            var fn = PosFaceNameFor(cp);
            // A space glued between two same-face chars stays in the run.
            if (!fn.Equals(faceName, StringComparison.OrdinalIgnoreCase) && er.lineText[j] != ' ') break;
            int k = j;
            (var adv, k) = MeasureSerifChar(er.lineText, k, runFs);
            runW += adv;
            runSb.Append(er.lineText, j, cpLen);
            var ext = er.extraAdv.Count > j ? er.extraAdv.ElementAt(j) : 0;
            j += cpLen;
            if (ext != 0) { runW += ext; break; }
        }

        var runText = runSb.ToString();
        var isLink = !string.IsNullOrEmpty(url);
        er.sb.Append(isLink ? "0 0 1 rg " : "0 0 0 rg ");

        var allAnsi = true;
        foreach (var ch in runText)
            if (ch > 0x7F && Text.Cp1252.TryGetByte(ch) is null) { allAnsi = false; break; }

        var face = PosFace(faceName);
        if (allAnsi && faceName.Equals("Times New Roman", StringComparison.OrdinalIgnoreCase) && face.ttf is not null)
        {
            er.sb.Append($"/FS1 {runFs.ToString("F1", er.inv)} Tf ");
            er.sb.Append($"1 0 0 1 {er.runX.ToString("F2", er.inv)} {runY.ToString("F2", er.inv)} Tm ");
            er.sb.Append($"({EscapePdfString(runText)}) Tj ");
        }
        else if (face.ttf is not null
            && er.page.Dict.Get("Resources") as Core.PdfDictionary is { } res
            && (res.Get("Font") as Core.PdfDictionary ?? er.docFontDict) is { } fontDict)
        {
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, face.ttf,
                faceName, runText, stripSpacesInBaseFont: true);
            er.sb.Append($"/{rn} {runFs.ToString("F1", er.inv)} Tf ");
            er.sb.Append($"1 0 0 1 {er.runX.ToString("F2", er.inv)} {runY.ToString("F2", er.inv)} Tm ");
            er.sb.Append('<').Append(Compat.ToHexString(hex)).Append("> Tj ");
        }
        else
        {
            er.sb.Append($"/F1 {runFs.ToString("F1", er.inv)} Tf ");
            er.sb.Append($"1 0 0 1 {er.runX.ToString("F2", er.inv)} {runY.ToString("F2", er.inv)} Tm ");
            er.sb.Append($"({EscapePdfString(runText)}) Tj ");
        }

        if (isLink)
        {
            er.underlines.Add((er.runX, runW, url!));
            er.pendingLinks.Add((er.page, new Aspose.Pdf.Rectangle(er.runX, runY - 2, er.runX + runW, runY + runFs), url!));
        }
        er.runX += runW;
        er.i = j;
    }
}
