using System.Text;

namespace Aspose.Pdf.Text;

public sealed partial class ParagraphAbsorber
{
    /// <summary>Decides whether line i opens a paragraph: indent, outdent, numeral, font jump, bullet, short-line space, shift or leading mark against the current paragraph, then closes or extends it.</summary>
    private static void GroupParagraphLine(ParagraphGroupState gp, int i)
    {
        gp.prev = gp.lines[i - 1];
        gp.curr = gp.lines[i];
        gp.f = gp.curr.AvgFontSize > 0 ? gp.curr.AvgFontSize : 12;
        gp.prevF = gp.prev.AvgFontSize > 0 ? gp.prev.AvgFontSize : 12;
        gp.text = LineText(gp.curr);
        gp.prevRaw = LineText(gp.prev);
        gp.prevText = gp.prevRaw.TrimEnd();
        var capIdx = LeadsWithCapital(gp.text) ?? -1;
        gp.capital = capIdx >= 0;

        gp.leftScatter = 0;
        foreach (var pl in gp.currentLines)
            gp.leftScatter = Math.Max(gp.leftScatter, pl.MinX - gp.paraLeft);
        gp.suppressIndent = gp.leftScatter > 4 * gp.f;
        gp.paraBullet = BulletLead(LineText(gp.currentLines[0]));
        gp.currBullet = BulletLead(gp.text);
        gp.tIndent = gp.capital && !gp.suppressIndent && !gp.paraBullet && gp.curr.MinX > gp.paraLeft + 0.55 * gp.f;
        gp.tOutdent = false;
        if (gp.capital && gp.currentLines.Count >= 2)
        {
            var contLeft = double.MaxValue;
            for (var li = 1; li < gp.currentLines.Count; li++)
                contLeft = Math.Min(contLeft, gp.currentLines[li].MinX);
            gp.tOutdent = gp.curr.MinX < contLeft - 0.55 * gp.f;
        }
        gp.tNumeric = gp.prevText.EndsWith(".") && NumericLead(gp.text);
        gp.currLeadF = gp.curr.Fragments.Count > 0 && gp.curr.Fragments[0].FontSize > 0
            ? (double)gp.curr.Fragments[0].FontSize : gp.f;
        gp.prevTailF = gp.prev.Fragments.Count > 0 && gp.prev.Fragments[^1].FontSize > 0
            ? (double)gp.prev.Fragments[^1].FontSize : gp.prevF;
        gp.tFont = gp.capital && Math.Max(gp.currLeadF, gp.prevTailF) > 1.25 * Math.Min(gp.currLeadF, gp.prevTailF);
        gp.tBullet = gp.currBullet && !gp.paraBullet
                      || gp.paraBullet && gp.currBullet && gp.curr.MinX < gp.paraLeft + 0.55 * gp.f
                      || gp.paraBullet && !gp.currBullet && gp.capital && gp.curr.MinX < gp.paraLeft + 0.55 * gp.f;
        gp.trailingSpaces = gp.prevRaw.Length - gp.prevText.Length;
        gp.prevInkRight = gp.prev.MaxX - gp.trailingSpaces * 0.25 * gp.prevF;
        var tSpace = gp.capital && capIdx > 0 && gp.text.Length > 0 && char.IsWhiteSpace(gp.text[0])
                     && (gp.bodyRight - gp.prevInkRight) > 1.5 * gp.prevF;
        gp.prevShort = (gp.sectionRight - gp.prevInkRight) > ShortLineGapEm * gp.prevF;
        gp.shift = Math.Abs(gp.curr.MinX - gp.prev.MinX);
        gp.tShift = gp.prevShort
                     && (gp.capital ? gp.shift >= Math.Max(MinShiftPt, MinShiftEm * gp.f)
                                 : !LeadsWithLowercase(gp.text) && gp.shift >= LargeShiftEm * gp.f);

        gp.tMark = gp.capital && capIdx > 0 && !gp.currBullet && LeadsWithMark(gp.text, capIdx);

        if (GridDebug)
            Console.Error.WriteLine($"[para] minX={gp.curr.MinX:F1} paraLeft={gp.paraLeft:F1} f={gp.f:F1} cap={gp.capital} scat={gp.leftScatter:F0} supp={gp.suppressIndent} tI={gp.tIndent} tN={gp.tNumeric} tS={tSpace} tO={gp.tOutdent} tF={gp.tFont} tB={gp.tBullet} tM={gp.tMark} tSh={gp.tShift} '{gp.text[..Math.Min(24, gp.text.Length)]}'");
        if (gp.tIndent || gp.tNumeric || tSpace || gp.tOutdent || gp.tFont || gp.tBullet || gp.tMark || gp.tShift)
        {
            gp.paragraphs.Add(BuildParagraph(gp.currentLines));
            gp.currentLines = [gp.curr];
            gp.paraLeft = gp.curr.MinX;
        }
        else
        {
            gp.currentLines.Add(gp.curr);
            gp.paraLeft = Math.Min(gp.paraLeft, gp.curr.MinX);
        }
    }
}
