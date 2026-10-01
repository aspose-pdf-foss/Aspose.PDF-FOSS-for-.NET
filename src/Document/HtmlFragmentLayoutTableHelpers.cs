using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
// The mixed-content fragment's Verdana-grid flow-text helper.
    // The break's own line box, by ITS position: the wrapper
    // font's UNROUNDED Verdana-12 line inside the div, the
    // serif default's rounded 13.5 outside it.
    private static double VgBrBoxPt(HtmlFragmentLayoutState hl, int vgWrapClose, int at) => at < vgWrapClose
        ? Converters.HtmlToPdfConverter.FormGridBasePt
            * Converters.HtmlToPdfConverter.VerdanaWinLineRatio
        : Converters.HtmlToPdfConverter.PxLinePt(
            Converters.HtmlToPdfConverter.FormGridBasePt,
            Converters.HtmlToPdfConverter.SerifWinLineRatio);

    // A structure-only chunk holding one bare <div>text</div>
    // (plus breaks and closing tags): the text is a top-level
    // flow line in the serif default — Times-12 on its 18px
    // box, Arabic shaped, seated at half-leading + ascent
    // (bbox 505.21..518.49 inside the 505.08 line).
    private static bool TryVgFlowText(HtmlFragmentLayoutState hl, int vgWrapClose, string chunkV, int at)
    {
        var dm = System.Text.RegularExpressions.Regex.Match(
            chunkV, @"<div[^>]*>([^<]+)</div>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!dm.Success || dm.Groups[1].Value.Trim().Length == 0) return false;
        var restV = chunkV.Remove(dm.Index, dm.Length);
        if (System.Text.RegularExpressions.Regex.Replace(restV,
                @"<br\s*/?>|</\s*(?:font|div)\s*>|\s+", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Length != 0)
            return false;
        var serifFont = Text.FontRepository.TryFindFont("Times New Roman");
        var serifTtf = serifFont?.SourceFontData?.TtfData;
        if (serifTtf is null) return false;
        foreach (System.Text.RegularExpressions.Match brm in
            System.Text.RegularExpressions.Regex.Matches(chunkV, @"<br\s*/?>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            if (brm.Index < dm.Index) hl.flow.AdvanceY(VgBrBoxPt(hl, vgWrapClose, at + brm.Index));
        var vgfPt = Converters.HtmlToPdfConverter.FormGridBasePt;
        var vgfBox = Converters.HtmlToPdfConverter.PxLinePt(vgfPt,
            Converters.HtmlToPdfConverter.SerifWinLineRatio);
        var vgfText = System.Text.RegularExpressions.Regex.Replace(
            dm.Groups[1].Value, @"\s+", " ").Trim();
        var vgfShaped = Text.ArabicTextShaper.ContainsArabic(vgfText)
            ? Text.ArabicTextShaper.Shape(vgfText) : vgfText;
        var vgfDict = Table.ResolvePageFontDict(hl.flow.CurrentPage);
        var (vgfRes, vgfHex) = Text.Type0FontEmbedder.Embed(
            vgfDict, serifTtf, serifFont!.FontName, vgfShaped,
            stripSpacesInBaseFont: true);
        var vgfAsc = vgfPt * Converters.HtmlToPdfConverter.SerifWinAscent;
        var vgfDesc = vgfPt * Converters.HtmlToPdfConverter.SerifWinDescent;
        var vgfBase = hl.flow.CurrentY - (vgfBox - vgfAsc - vgfDesc) / 2 - vgfAsc;
        var vgfNb = new Content.ContentStreamBuilder();
        vgfNb.SaveState();
        vgfNb.BeginText().SetFont(vgfRes, vgfPt)
            .MoveTextPosition(hl.marginLeft, vgfBase);
        vgfNb.ShowTextHex(vgfHex);
        vgfNb.EndText();
        vgfNb.RestoreState();
        hl.flow.InjectContentAtCursor(vgfNb.Build());
        hl.flow.AdvanceY(vgfBox);
        foreach (System.Text.RegularExpressions.Match brm in
            System.Text.RegularExpressions.Regex.Matches(chunkV, @"<br\s*/?>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            if (brm.Index > dm.Index) hl.flow.AdvanceY(VgBrBoxPt(hl, vgWrapClose, at + brm.Index));
        return true;
    }

    /// <summary>The fragment's style sheet as the document sheet of one plain table chunk:
    /// null when the fragment declares no rules; a rule addressing the chunk's table by id
    /// (<c>table#meta td.var</c>) is also filed under the element keys the cell grid reads
    /// (<c>table td.var</c>, <c>td.var</c>, <c>.var</c>).</summary>
    private static IReadOnlyDictionary<string, Dictionary<string, string>>? TableSheetForChunk(string fragmentHtml, string chunk)
    {
        var sheet = Converters.HtmlToPdfConverter.ParseStyleSheet(fragmentHtml);
        if (sheet.Count == 0) return null;
        var id = System.Text.RegularExpressions.Regex.Match(chunk, @"<table\b[^>]*\bid\s*=\s*[""']?([\w-]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
        if (id.Length == 0) return sheet;
        foreach (var key in sheet.Keys.ToList())
        {
            var m = System.Text.RegularExpressions.Regex.Match(key, @"^table#" + System.Text.RegularExpressions.Regex.Escape(id) + @"(?:\s+(.+))?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            foreach (var alias in TableRuleAliases(m.Groups[1].Value.Trim()))
            {
                if (!sheet.TryGetValue(alias, out var target)) sheet[alias] = target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in sheet[key]) target[kv.Key] = kv.Value;
            }
        }
        return sheet;
    }

    /// <summary>The element keys a table-id rule's descendant part files under.</summary>
    private static IEnumerable<string> TableRuleAliases(string rest)
    {
        if (rest.Length == 0) { yield return "table"; yield break; }
        yield return "table " + rest;
        yield return rest;
        var cm = System.Text.RegularExpressions.Regex.Match(rest, @"^t[dh]\.([\w-]+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (cm.Success) yield return "." + cm.Groups[1].Value;
    }
}
