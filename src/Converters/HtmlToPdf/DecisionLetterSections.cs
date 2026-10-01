using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The footer paragraph and the contact table with its 30/20/20/30 columns and vertically centred rows, opening the second page.</summary>
    private static void DrawFooterAndContacts(DecisionLetterState dl)
    {
        var ftM = Regex.Match(dl.c, @"class\s*=\s*[""']notifications-footer[""']",
            RegexOptions.IgnoreCase);
        var ftText = "";
        if (ftM.Success)
        {
            var pM = Regex.Match(dl.c[ftM.Index..], @"<p\b[^>]*>(.*?)</p>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (pM.Success) ftText = Inner(dl, pM.Groups[1].Value);
        }
        var yF = dl.y;   // the footer's 15 px top margin COLLAPSES with the
                      // disclosure boxSection's bottom margin (max-wise)
        foreach (var ln in MeasuredWordWrap(ftText, dl.cw, dl.face, dl.bodyFs))
        {
            EmitRun(dl, ln, dl.bodyFs, false, dl.cx, yF + Drop(dl, dl.bodyFs, dl.lineH), null);
            yF += dl.lineH;
        }
        yF += 7.5;                                // footer-information 10 px margin

        var fRows = dl.infoTables.Count >= 3 ? dl.infoTables[2] : null;
        if (fRows is not null)
        {
            // declared percent columns off the table markup
            var pcts = new List<double>();
            var ftTblM = Regex.Matches(dl.c,
                @"<table\b[^>]*class\s*=\s*[""']information-table[""'][^>]*>",
                RegexOptions.IgnoreCase);
            if (ftTblM.Count >= 3
                && BalancedInner(dl.c, ftTblM[2].Index + ftTblM[2].Length, "table") is { } fInner)
                foreach (Match tdM in Regex.Matches(fInner, @"<td\b[^>]*width\s*:\s*([\d.]+)%",
                    RegexOptions.IgnoreCase))
                    pcts.Add(double.Parse(tdM.Groups[1].Value, dl.inv) / 100.0);
            while (pcts.Count < fRows.Count) pcts.Add(1.0 / Math.Max(1, fRows.Count));

            // wrap each cell at its column minus the 25 px right pad
            var colX = new double[fRows.Count];
            var xAcc = dl.cx;
            for (var i = 0; i < fRows.Count; i++) { colX[i] = xAcc; xAcc += pcts[i] * dl.cw; }
            var cellLines = new List<(string lbl, string val)[]>();
            var maxLines = 1;
            for (var i = 0; i < fRows.Count; i++)
            {
                var (lbl, val) = fRows[i].Count > 0 ? fRows[i][0] : ("", "");
                var avail = pcts[i] * dl.cw - 18.75;
                var lblW = Measure(dl, lbl, true, dl.bodyFs);
                var wrapped = MeasuredWordWrap(val, avail - lblW, dl.face, dl.bodyFs);
                var lines = new (string, string)[Math.Max(1, wrapped.Length)];
                for (var k = 0; k < lines.Length; k++)
                    lines[k] = (k == 0 ? lbl : "",
                        // the value keeps its leading space after the label
                        k < wrapped.Length
                            ? (k == 0 && !wrapped[k].StartsWith(' ')
                                ? " " + wrapped[k] : wrapped[k])
                            : "");
                cellLines.Add(lines);
                maxLines = Math.Max(maxLines, lines.Length);
            }
            var rowH = maxLines * dl.lineH;
            if (yF + rowH > dl.pageHeight - DnRightMarginPt)
            {
                dl.page = dl.doc.Pages.Add(dl.pageWidth, dl.pageHeight);
                EnsureFonts(dl.page, dl.docFontDict);
                dl.sb = new StringBuilder();
                dl.pageStreams.Add((dl.page, dl.sb));
                yF = DnPage2TopPt;
            }
            for (var i = 0; i < cellLines.Count; i++)
            {
                var lines = cellLines[i];
                var yCell = yF + (rowH - lines.Length * dl.lineH) / 2;
                foreach (var (lbl, val) in lines)
                {
                    if (lbl.Length > 0)
                        EmitLabelValue(dl, lbl, val, dl.bodyFs, colX[i], yCell + Drop(dl, dl.bodyFs, dl.lineH));
                    else
                        EmitRun(dl, val, dl.bodyFs, false, colX[i], yCell + Drop(dl, dl.bodyFs, dl.lineH), null);
                    yCell += dl.lineH;
                }
            }
        }
    }

    /// <summary>The disclaimer block below the two panels.</summary>
    private static void DrawDisclaimer(DecisionLetterState dl)
    {
        var frameTop = BoxTitle(dl, "Notification Disclosure", dl.cx, dl.discTop);
        var dM = Regex.Match(dl.c, @"class\s*=\s*[""']notifications-disclosure[""']",
            RegexOptions.IgnoreCase);
        var dText = "";
        if (dM.Success)
        {
            var pM = Regex.Match(dl.c[dM.Index..], @"<p\b[^>]*>(.*?)</p>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (pM.Success) dText = Inner(dl, pM.Groups[1].Value);
        }
        var yD = frameTop + 2 + 11.25;
        foreach (var ln in MeasuredWordWrap(dText, dl.cw - 4 - 15, dl.face, dl.bodyFs))
        {
            EmitRun(dl, ln, dl.bodyFs, false, dl.cx + 2 + 7.5, yD + Drop(dl, dl.bodyFs, dl.lineH), null);
            yD += dl.lineH;
        }
        var dBottom = yD + 7.5 + 2;
        FrameRect(dl, dl.cx, frameTop, dl.cw, dBottom - frameTop, dl.frameGray, 1.5);
        dl.y = dBottom + 11.25;
    }

    /// <summary>The left and right half panels: their tables and frames at the declared split.</summary>
    private static void DrawSidePanels(DecisionLetterState dl)
    {
        var frameTop = BoxTitle(dl, "Stipulations", dl.rightX, dl.midTop);
        var padSide = 7.5;
        var stX = dl.rightX + 2 + padSide;
        var stW = dl.halfW - 4 - 2 * padSide;
        var stipM = Regex.Match(dl.c, @"class\s*=\s*[""']stipulations-text[""'][^>]*>",
            RegexOptions.IgnoreCase);
        var yRow = frameTop + 2 + 11.25;
        if (stipM.Success && BalancedInner(dl.c, stipM.Index + stipM.Length, "div") is { } stInner)
        {
            foreach (Match pM in Regex.Matches(stInner, @"<p\b[^>]*>(.*?)</p>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var odd = pM.Value.Contains("odd", StringComparison.OrdinalIgnoreCase);
                if (odd) HLine(dl, stX, stX + stW, yRow, dl.bandBlue);
                EmitRun(dl, Inner(dl, pM.Groups[1].Value), dl.bodyFs, false,
                    stX + 11.25, yRow + Drop(dl, dl.bodyFs, 15.0), null);
                if (odd) HLine(dl, stX, stX + stW, yRow + 15.0, dl.bandBlue);
                yRow += 15.0;
            }
        }
        var stipBottom = yRow + 7.5 + 2;
        FrameRect(dl, dl.rightX, frameTop, dl.halfW, stipBottom - frameTop, dl.frameGray, 1.5);

        // Comments box under it
        var cmTop = stipBottom + 11.25;
        var cmFrameTop = BoxTitle(dl, "Comments", dl.rightX, cmTop);
        var cmM = Regex.Match(dl.c, @"class\s*=\s*[""']approved-comments[^""']*[""']",
            RegexOptions.IgnoreCase);
        var cmText = "";
        var cmLineH = 12.75;                      // p line-height: 17px
        if (cmM.Success)
        {
            var pM = Regex.Match(dl.c[cmM.Index..], @"<p\b[^>]*>(.*?)</p>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (pM.Success) cmText = Inner(dl, pM.Groups[1].Value);
        }
        var yCm = cmFrameTop + 2 + 11.25;
        foreach (var ln in MeasuredWordWrap(cmText, stW, dl.face, dl.bodyFs))
        {
            EmitRun(dl, ln, dl.bodyFs, false, dl.rightX + 2 + padSide, yCm + Drop(dl, dl.bodyFs, cmLineH), null);
            yCm += cmLineH;
        }
        dl.rightBottom = yCm + 7.5 + 2;
        FrameRect(dl, dl.rightX, cmFrameTop, dl.halfW, dl.rightBottom - cmFrameTop, dl.frameGray, 1.5);
    }

    /// <summary>The half-width split and the dealer table with its framed rows.</summary>
    private static void DrawDealerTable(DecisionLetterState dl)
    {
        dl.halfM = Regex.Match(dl.c, @"class\s*=\s*[""'][^""']*\bleft[""'][^>]*style\s*=\s*[""'][^""']*width\s*:\s*([\d.]+)%",
            RegexOptions.IgnoreCase);
        dl.halfPct = dl.halfM.Success ? double.Parse(dl.halfM.Groups[1].Value, dl.inv) / 100.0 : 0.485;
        dl.halfW = dl.halfPct * dl.cw;
        dl.rightX = dl.cx + dl.cw - dl.halfW;

        dl.leftBottom = dl.midTop;
        dl.dealerM = Regex.Match(dl.c,
            @"<table\b[^>]*class\s*=\s*[""']dealer-structure-approved-table[""'][^>]*>",
            RegexOptions.IgnoreCase);
        if (dl.dealerM.Success && BalancedInner(dl.c, dl.dealerM.Index + dl.dealerM.Length, "table") is { } dInner)
        {
            var frameTop = BoxTitle(dl, "Dealer Structure", dl.cx, dl.midTop);
            var padSide = 11.25;                      // !important 15 px pads
            var tX = dl.cx + 2 + padSide;
            var tW = dl.halfW - 4 - 2 * padSide;
            var rowTop = frameTop + 2 + 11.25;
            var rowH = 15.0;                          // 2px pads + line + border
            var rows = new List<(string lbl, string val)>();
            foreach (Match trM in Regex.Matches(dInner, @"<tr\b[^>]*>(.*?)</tr>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var tds = Regex.Matches(trM.Groups[1].Value, @"<td\b[^>]*>(.*?)</td>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (tds.Count >= 2)
                    rows.Add((Inner(dl, tds[0].Groups[1].Value), Inner(dl, tds[1].Groups[1].Value)));
            }
            var yRow = rowTop;
            foreach (var (lbl, val) in rows)
            {
                HLine(dl, tX, tX + tW, yRow, dl.bandBlue);
                EmitRun(dl, lbl, dl.bodyFs, true, tX + 11.25, yRow + Drop(dl, dl.bodyFs, rowH), null);
                EmitRun(dl, val, dl.bodyFs, false, tX + tW / 2 + 11.25, yRow + Drop(dl, dl.bodyFs, rowH), null);
                yRow += rowH;
            }
            HLine(dl, tX, tX + tW, yRow, dl.bandBlue);
            dl.leftBottom = yRow + 15.0 + 2;             // 20 px !important bottom pad
            FrameRect(dl, dl.cx, frameTop, dl.halfW, dl.leftBottom - frameTop, dl.frameGray, 1.5);
        }
    }

    /// <summary>The column table: its rows of label/value cells with band fills and rules.</summary>
    private static void DrawColumnTable(DecisionLetterState dl)
    {
        dl.colM = Regex.Match(dl.c,
            @"<table\b[^>]*class\s*=\s*[""']application-approved-table[""'][^>]*>",
            RegexOptions.IgnoreCase);
        dl.colBottom = dl.sectionTop0;
        if (dl.colM.Success && BalancedInner(dl.c, dl.colM.Index + dl.colM.Length, "table") is { } colInner)
        {
            var ths = new List<string>();
            foreach (Match thM in Regex.Matches(colInner, @"<th\b[^>]*>(.*?)</th>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
                ths.Add(Inner(dl, thM.Groups[1].Value));
            var tds = new List<string>();
            foreach (Match tdM in Regex.Matches(colInner, @"<td\b[^>]*>(.*?)</td>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
                tds.Add(Inner(dl, tdM.Groups[1].Value));

            var frameTop = BoxTitle(dl, "Collateral", dl.cx, dl.sectionTop0);
            var padSide = 7.5;                        // box-content 10 px sides
            var tX = dl.cx + 2 + padSide;
            var tW = dl.cw - 4 - 2 * padSide;
            var thTop = frameTop + 2 + 11.25;         // 15 px content top pad
            var thH = 19.5;                           // 26 px line-height rows
            var n = Math.Max(1, ths.Count);
            // the auto solve: each column floors at max(declared 98 px, its
            // content + the 10px/4px pads), the leftover splits EQUALLY
            var thDeclared = 73.5;
            {
                var thDeclM = Regex.Match(colInner, @"<th\b[^>]*width\s*:\s*([\d.]+)px",
                    RegexOptions.IgnoreCase);
                if (thDeclM.Success)
                    thDeclared = double.Parse(thDeclM.Groups[1].Value, dl.inv) * 0.75;
            }
            var colWs = new double[n];
            double colSum = 0;
            for (var i = 0; i < n; i++)
            {
                var contentW = Math.Max(
                    i < ths.Count ? Measure(dl, ths[i], true, dl.bodyFs) : 0,
                    i < tds.Count ? Measure(dl, tds[i], false, dl.bodyFs) : 0) + 10.5;
                colWs[i] = Math.Max(thDeclared, contentW);
                colSum += colWs[i];
            }
            var share = Math.Max(0, tW - colSum) / n;
            for (var i = 0; i < n; i++) colWs[i] += share;
            var xTh = tX;
            for (var i = 0; i < ths.Count; i++)
            {
                Fill(dl, xTh, thTop, colWs[i], thH + 0.75, dl.bandBlue);
                FrameRect(dl, xTh, thTop, colWs[i], thH + 0.75, dl.borderLite, 0.75);
                EmitRun(dl, ths[i], dl.bodyFs, true,
                    xTh + (colWs[i] - Measure(dl, ths[i], true, dl.bodyFs)) / 2,
                    thTop + Drop(dl, dl.bodyFs, thH), null);
                xTh += colWs[i];
            }
            var tdTop = thTop + thH + 0.75;
            var xTd = tX;
            for (var i = 0; i < tds.Count && i < n; i++)
            {
                HLine(dl, xTd, xTd + colWs[i], tdTop + thH + 0.75, dl.bandBlue);
                VLine(dl, xTd, tdTop, tdTop + thH + 0.75, dl.borderLite);
                EmitRun(dl, tds[i], dl.bodyFs, false, xTd + 7.5,
                    tdTop + Drop(dl, dl.bodyFs, thH), null);
                xTd += colWs[i];
            }
            dl.colBottom = tdTop + thH + 0.75 + 7.5 + 2;   // pad-bottom + frame
            FrameRect(dl, dl.cx, frameTop, dl.cw, dl.colBottom - frameTop, dl.frameGray, 1.5);
        }

        dl.midTop = dl.colBottom + 11.25;
    }

    /// <summary>The info bands: the first row's blue band and columns, the second row's cells, and the frame around them.</summary>
    private static void DrawInfoBands(DecisionLetterState dl)
    {

        dl.infoTop = DnInfoTopPt;
        dl.r1 = dl.infoTables[0];
        dl.r1RowH = 9.0 + 2 * dl.lineH + 9.0;
        Fill(dl, dl.cx + 0.75, dl.infoTop + 0.4, dl.cw - 1.5, dl.r1RowH, dl.bandBlue);
        dl.nCols1 = Math.Max(1, dl.r1.Count);
        dl.colW1 = dl.cw / dl.nCols1;
        for (var i = 0; i <= dl.nCols1; i++)
            VLine(dl, dl.cx + i * dl.colW1, dl.infoTop + 0.4, dl.infoTop + 0.4 + dl.r1RowH, dl.borderLite);
        HLine(dl, dl.cx + 0.75, dl.cx + dl.cw - 0.75, dl.infoTop + 0.75, dl.bandBlue);
        HLine(dl, dl.cx + 0.75, dl.cx + dl.cw - 0.75, dl.infoTop + 0.4 + dl.r1RowH, dl.bandBlue);
        for (var i = 0; i < dl.r1.Count; i++)
        {
            var xCell = dl.cx + i * dl.colW1 + 0.75 + 11.25;   // border + 15 px pad
            var yLine = dl.infoTop + 0.4 + 9.0 + Drop(dl, dl.bodyFs, dl.lineH);
            foreach (var (lbl, val) in dl.r1[i])
            {
                EmitLabelValue(dl, lbl, val, dl.bodyFs, xCell, yLine);
                yLine += dl.lineH;
            }
        }
        dl.r2 = dl.infoTables[1];
        dl.r2Top = dl.infoTop + 0.4 + dl.r1RowH;
        dl.r2RowH = 3.75 + dl.lineH + 3.75;
        {
            var maxc = new double[dl.r2.Count];
            double sum = 0;
            for (var i = 0; i < dl.r2.Count; i++)
            {
                foreach (var (lbl, val) in dl.r2[i])
                    maxc[i] = Math.Max(maxc[i],
                        Measure(dl, lbl, true, dl.bodyFs) + Measure(dl, val, false, dl.bodyFs));
                sum += maxc[i] + 22.5;
            }
            var surplus = Math.Max(0, dl.cw - sum);
            double xCell = dl.cx + 0.75 + 11.25;
            var yLine = dl.r2Top + 3.75 + Drop(dl, dl.bodyFs, dl.lineH);
            double contentSum = 0;
            foreach (var mcW in maxc) contentSum += mcW;
            for (var i = 0; i < dl.r2.Count; i++)
            {
                if (dl.r2[i].Count > 0)
                    EmitLabelValue(dl, dl.r2[i][0].label, dl.r2[i][0].val, dl.bodyFs, xCell, yLine);
                xCell += maxc[i] + 22.5
                    + (contentSum > 0 ? surplus * maxc[i] / contentSum : 0);
            }
        }
        dl.infoBottom = dl.r2Top + dl.r2RowH;
        FrameRect(dl, dl.cx, dl.infoTop, dl.cw, dl.infoBottom - dl.infoTop + 0.4, dl.frameGray, 0.75);
    }

    /// <summary>Parse the info tables' label/value rows; a letter without two of them is not this dialect.</summary>
    private static bool TryParseInfoTables(DecisionLetterState dl)
    {
        dl.infoTables = new List<List<List<(string label, string val)>>>();
        foreach (Match tM in Regex.Matches(dl.c,
            @"<table\b[^>]*class\s*=\s*[""']information-table[""'][^>]*>", RegexOptions.IgnoreCase))
        {
            var innerT = BalancedInner(dl.c, tM.Index + tM.Length, "table");
            if (innerT is null) continue;
            var cells = new List<List<(string, string)>>();
            foreach (Match tdM in Regex.Matches(innerT, @"<td\b[^>]*>(.*?)</td>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var lines = new List<(string, string)>();
                foreach (var part in Regex.Split(tdM.Groups[1].Value, @"<br\s*/?>",
                    RegexOptions.IgnoreCase))
                {
                    var spM = Regex.Match(part, @"<span\b[^>]*>(.*?)</span>(.*)$",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (spM.Success)
                        lines.Add((Inner(dl, spM.Groups[1].Value),
                            " " + Inner(dl, spM.Groups[2].Value)));
                    else if (Inner(dl, part).Length > 0)
                        lines.Add(("", Inner(dl, part)));
                }
                cells.Add(lines);
            }
            dl.infoTables.Add(cells);
        }
        if (dl.infoTables.Count < 2) return false;
        return true;
    }

    /// <summary>The header: the broken-image frames, the header image, the h3 and h1 titles.</summary>
    private static void DrawLetterHeader(DecisionLetterState dl)
    {
        BrokenFrame(dl, dl.cx, DnHeaderImgTopPt);
        dl.qrPadM = Regex.Match(dl.c, @"class\s*=\s*[""']qr-code[^""']*[""'][^>]*style\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase);
        dl.qrPad = dl.qrPadM.Success
            ? CssEmLen(dl.qrPadM.Groups[1].Value.Replace("padding: 0 ", "padding-left: "),
                "padding-left", dl.bodyFs) ?? 7.5 : 7.5;
        BrokenFrame(dl, dl.cx + dl.cw - dl.qrPad - 34, DnHeaderImgTopPt);

        dl.hdrImgW = 0.0;
        dl.hdrImgM = Regex.Match(dl.c,
            @"class\s*=\s*[""']header-image[""'][^>]*style\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase);
        if (dl.hdrImgM.Success)
            dl.hdrImgW = CssEmLen(dl.hdrImgM.Groups[1].Value, "width", dl.bodyFs) ?? 0;
        dl.titlePadLeft = 7.5;
        dl.titleX = dl.cx + dl.hdrImgW + dl.titlePadLeft;

        dl.h3M = Regex.Match(dl.c, @"<h3\b[^>]*>(.*?)</h3>", RegexOptions.IgnoreCase);
        dl.h1M = Regex.Match(dl.c, @"<h1\b[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase);
        dl.y = DnH3BaselinePt;
        if (dl.h3M.Success)
        {
            var h3Style = Regex.Match(dl.h3M.Value, @"style\s*=\s*[""']([^""']*)[""']").Groups[1].Value;
            var h3Fs = CssEmLen(h3Style, "font-size", dl.bodyFs) ?? 11.25;
            var h3Text = Inner(dl, dl.h3M.Groups[1].Value);
            if (h3Style.Contains("uppercase", StringComparison.OrdinalIgnoreCase))
                h3Text = h3Text.ToUpperInvariant();
            EmitRun(dl, h3Text, h3Fs, true, dl.titleX, dl.y,
                ParseCssColor(Regex.Match(h3Style, @"color\s*:\s*([^;]+)").Groups[1].Value.Trim()));
        }
        dl.y += DnH1AdvancePt;
        if (dl.h1M.Success)
        {
            var h1Style = Regex.Match(dl.h1M.Value, @"style\s*=\s*[""']([^""']*)[""']").Groups[1].Value;
            var h1Fs = CssEmLen(h1Style, "font-size", dl.bodyFs) ?? 21.75;
            EmitRun(dl, Inner(dl, dl.h1M.Groups[1].Value), h1Fs, true, dl.titleX, dl.y,
                ParseCssColor(Regex.Match(h1Style, @"color\s*:\s*([^;]+)").Groups[1].Value.Trim()));
        }
    }

    /// <summary>Parse the body style, face, size and the basis box, and resolve the letter's colours; a letter that lacks them is not this dialect.</summary>
    private static bool TryParseLetterHead(DecisionLetterState dl, string html)
    {
        dl.c = Regex.Replace(html, @"\s+", " ");
        dl.inv = System.Globalization.CultureInfo.InvariantCulture;
        dl.bodyM = Regex.Match(dl.c, @"<body\b[^>]*style\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase);
        if (!dl.bodyM.Success) return false;
        dl.faceM = Regex.Match(dl.bodyM.Groups[1].Value, @"font-family\s*:\s*([^;]+)",
            RegexOptions.IgnoreCase);
        dl.face = (dl.faceM.Success ? FirstFontFamily(dl.faceM.Groups[1].Value) : null)!;
        if (dl.face is null || WinMetricsFor(dl.face) is not { } wm) return false;
        dl.wm = wm;
        dl.bodyFs = CssEmLen(dl.bodyM.Groups[1].Value, "font-size", 12) ?? 9.75;

        dl.basisM = Regex.Match(dl.c,
            @"<div\b[^>]*class\s*=\s*[""']basis[""'][^>]*style\s*=\s*[""']([^""']*)[""']",
            RegexOptions.IgnoreCase);
        if (!dl.basisM.Success) return false;
        dl.basisW = 0;
        foreach (Match wMatch in Regex.Matches(dl.basisM.Groups[1].Value,
            @"(?<![-\w])width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase))
            dl.basisW = double.Parse(wMatch.Groups[1].Value, dl.inv) * 0.75;   // LAST wins
        if (dl.basisW <= 0) return false;
        dl.padM = Regex.Match(dl.basisM.Groups[1].Value,
            @"padding\s*:\s*[\d.]+\w*\s+([\d.]+)px", RegexOptions.IgnoreCase);
        dl.sidePad = dl.padM.Success ? double.Parse(dl.padM.Groups[1].Value, dl.inv) * 0.75 : 15.0;

        dl.pageWidth = DnLeftMarginPt + dl.sidePad + dl.basisW + DnRightMarginPt;
        dl.cx = DnLeftMarginPt + dl.sidePad;
        dl.cw = dl.basisW;
        dl.lineH = MetricLineHeight(dl.bodyFs, wm.sum <= 1.0 ? 1.2 : wm.sum);

        dl.ink = ParseCssColor("#5c5c5c") ?? Color.FromArgb(92, 92, 92);
        dl.bandBlue = ParseCssColor("#d3e0ec") ?? Color.FromArgb(211, 224, 236);
        dl.borderLite = ParseCssColor("#f3f3f3") ?? Color.FromArgb(243, 243, 243);
        dl.frameGray = ParseCssColor("#e8e8e8") ?? Color.FromArgb(232, 232, 232);
        return true;
    }
}
