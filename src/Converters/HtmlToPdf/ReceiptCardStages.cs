using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Writes the body rows: each label and value at its pen, the rule under every row, the total row bold.</summary>
    private static void EmitReceiptRows(ReceiptCardState rk)
    {
        for (var i = 0; i < rk.bodyRows.Count; i++)
        {
            var (label, value, isAmount, isTotal, isFees) = rk.bodyRows[i];
            if (i > 0) rk.by += isTotal ? RcTotalPitch : rk.afterFees ? RcPostFeesPitch : RcBodyPitch;
            rk.afterFees = false;
            var lfs = isTotal ? RcTotalFs : RcBodyFs;
            Emit(rk, isTotal ? "F2" : "F1", lfs, RcBodyLabelX, rk.by, label);
            if (isFees)
            {
                // the nested fees grid: name left, amount right, a touch lower
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var amt = parts.Length > 1 ? parts[^1] : "";
                var name = amt.Length > 0 ? value[..value.LastIndexOf(amt, StringComparison.Ordinal)].Trim() : value;
                Emit(rk, "F2", RcBodyFs, RcFeesValueX, rk.by + RcFeesDrop, name);
                if (amt.Length > 0)
                    Emit(rk, "F2", RcBodyFs, RcFeesAmtRight - W(rk, amt, RcBodyFs, true), rk.by + RcFeesDrop, amt);
                rk.afterFees = true;
            }
            else if (isAmount && value.Length > 0)
                Emit(rk, "F2", lfs,
                    (isTotal ? RcTotalAmtRight : RcAmountRight) - W(rk, value, lfs, true), rk.by, value);
            else if (value.Length > 0)
                Emit(rk, "F2", RcBodyFs, RcBodyValueX, rk.by, value);
        }
    }

    /// <summary>Writes the first two sections' heading and lines above the body.</summary>
    private static void EmitReceiptSections(ReceiptCardState rk)
    {
        for (var s = 0; s < rk.sections.Count && s < 2; s++)
        {
            var labelX = rk.hdrLeft + (s == 0 ? RcS1LabelOff : RcS2LabelOff);
            var valueX = rk.hdrLeft + (s == 0 ? RcS1ValueOff : RcS2ValueOff);
            var y = RcSumBase - (s == 1 ? RcSumLift : 0);
            foreach (var (label, value) in rk.sections[s])
            {
                Emit(rk, "F1", RcSumFs, labelX, y, label);
                if (value.Length > 0) Emit(rk, "F2", RcSumFs, valueX, y, value);
                y += RcSumPitch;
            }
        }
    }

    /// <summary>Collects the body rows from the table markup: label and value cells flattened, the total row flagged.</summary>
    private static void CollectReceiptRows(ReceiptCardState rk)
    {
        foreach (Match tr in Regex.Matches(rk.bodyM.Groups[1].Value,
            @"<tr>((?:(?!</tr>)[\s\S])*)</tr>", RegexOptions.IgnoreCase))
        {
            var row = tr.Groups[1].Value;
            var tds = Regex.Matches(row, @"<td\b([^>]*)>((?:(?!</td>|<td\b)[\s\S])*)</td>",
                RegexOptions.IgnoreCase);
            if (tds.Count < 2) continue;
            var label = Flat(rk, tds[0].Groups[2].Value);
            if (label.Length == 0) continue;
            var attrs0 = tds[0].Groups[1].Value;
            var isTotal = attrs0.Contains("totalLabel", StringComparison.OrdinalIgnoreCase);
            var isFees = row.Contains("<table", StringComparison.OrdinalIgnoreCase);
            var isAmount = tds[1].Groups[1].Value.Contains("detailValueAmt", StringComparison.OrdinalIgnoreCase)
                || isTotal;
            var value = Flat(rk, isFees
                ? Regex.Replace(tds[1].Groups[2].Value, @"</?t[a-z]+\b[^>]*>", " ")
                : tds[1].Groups[2].Value);
            rk.bodyRows.Add((label, value, isAmount, isTotal, isFees));
        }
    }

    /// <summary>Collects the sections from the markup: each heading with its flattened lines.</summary>
    private static void CollectReceiptSections(ReceiptCardState rk)
    {
        foreach (Match sm in Regex.Matches(rk.html,
            @"class=[""']summarySection[""'][\s\S]*?<table>([\s\S]*?)</table>", RegexOptions.IgnoreCase))
        {
            var rows = new List<(string, string)>();
            foreach (Match tr in Regex.Matches(sm.Groups[1].Value, @"<tr>([\s\S]*?)</tr>",
                RegexOptions.IgnoreCase))
            {
                var tds = Regex.Matches(tr.Groups[1].Value, @"<td[^>]*>([\s\S]*?)</td>",
                    RegexOptions.IgnoreCase);
                if (tds.Count >= 2)
                    rows.Add((Flat(rk, tds[0].Groups[1].Value), Flat(rk, tds[1].Groups[1].Value)));
            }
            rk.sections.Add(rows);
        }
    }

    /// <summary>Splits the address block into its lines.</summary>
    private static void ParseReceiptAddress(ReceiptCardState rk)
    {
        if (rk.addrM.Success)
            foreach (Match li in Regex.Matches(rk.addrM.Groups[1].Value, @"<li[^>]*>([\s\S]*?)</li>",
                RegexOptions.IgnoreCase))
            {
                var t = Flat(rk, li.Groups[1].Value);
                if (t.Length > 0) rk.addrItems.Add(t);
            }
    }
}
