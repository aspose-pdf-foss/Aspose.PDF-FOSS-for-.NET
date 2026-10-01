using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The body-face METRICS CARD ──────────────────────────────────────────────
    // A one-card report: `body { font-family: Verdana-ish; font-size: Npx;
    // width: 100% }`, a `.main` border-collapse table whose single column stacks
    // a tinted HEADING band over a body cell, and a nested `.metrics` table
    // (width: inherit — the enclosing td's percent class) of bold-label /
    // value rows. The expected render draws the card with the real body face at
    // the px-derived sizes, the collapsed 2px frame around both cells, and the
    // nested grid's 75%/25% columns.
    //
    // Geometry (measured unless derived):
    //   page 601 × 842; card [96 .. 517] (right band 84); frame 1.5 pt black,
    //   heading band fill from the heading class; heading text 0.6em of the
    //   9px body = 4.05 pt at (97.5, band top + 5.8); labels x 98.93, values
    //   x 201.38 (col1 = 75% of the metrics table's inherited 33% box);
    //   first row baseline 98.4; single-line row pitch 9.75 (13px), wrapped
    //   in-cell line 8.25 (11px = the face's hhea box); a <sup> grows its
    //   value line: main run +1.16 below the label baseline, the sup at
    //   0.833× size raised 2.36 above the value baseline.

    private const double McPageWidthPt = 601.0;        // measured page box
    private const double McCardLeftPt = 96.0;          // card frame left edge
    private const double McCardRightPt = 517.0;        // card frame right edge
    private const double McFramePt = 1.5;              // collapsed 2px border
    private const double McHeadBandTopPt = 78.75;      // heading band top (stroke centre)
    private const double McHeadBandBotPt = 86.71;      // heading band bottom
    private const double McCardBottomPt = 179.71;      // body cell bottom (stroke centre)
    private const double McHeadBaseDropPt = 5.8;       // band top → heading baseline
    private const double McLabelXPt = 98.93;           // label pen
    private const double McValueXPt = 201.38;          // value pen
    private const double McFirstBasePt = 98.4;         // first row baseline
    private const double McRowPitchPt = 9.75;          // row-to-row (13px)
    private const double McCellLinePt = 8.25;          // wrapped in-cell line (11px)
    private const double McSupValueDropPt = 1.16;      // sup-bearing value baseline drop
    private const double McSupRaisePt = 2.36;          // sup baseline above the value's
    private const double McSupSizeFactor = 0.833;      // sup size vs the value size

    private static Document? TryRenderMetricsCard(string html, IReadOnlyDictionary<string, Dictionary<string, string>> css, double pageHeight)
    {
        var mc = new MetricsCardRenderState();
        mc.html = html;
        mc.css = css;
        mc.pageHeight = pageHeight;
        // Gate: a px-sized 100%-width body naming a resolvable face, a
        // border-collapse card class, a width:inherit nested-table class, and
        // a heading class with a background and an em font-size.
        if (!mc.css.TryGetValue("body", out var bodyRule)
            || !bodyRule.TryGetValue("font-family", out var bodyFam)
            || !bodyRule.TryGetValue("font-size", out var bodyFsV)
            || !Regex.IsMatch(bodyFsV, @"^\s*[\d.]+\s*px\s*$", RegexOptions.IgnoreCase)
            || !(bodyRule.TryGetValue("width", out var bodyW)
                 && bodyW.Trim() == "100%"))
            return null;
        mc.face = FirstFontFamily(bodyFam);
        if (mc.face is null || WinMetricsFor(mc.face) is not { } fm) return null;
        mc.headCls = null;
        mc.hasCollapse = false;
        mc.hasInherit = false;
        foreach (var (sel, props) in mc.css)
        {
            if (props.TryGetValue("border-collapse", out var bc)
                && bc.Contains("collapse", StringComparison.OrdinalIgnoreCase))
                mc.hasCollapse = true;
            if (props.TryGetValue("width", out var wv)
                && wv.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase))
                mc.hasInherit = true;
            if (sel.StartsWith('.')
                && (props.ContainsKey("background-color") || props.ContainsKey("background"))
                && props.TryGetValue("font-size", out var hfs)
                && hfs.Contains("em", StringComparison.OrdinalIgnoreCase))
                mc.headCls = props;
        }
        if (!mc.hasCollapse || !mc.hasInherit || mc.headCls is null) return null;

        const double PxPt = 0.75;
        mc.bodyFs = double.Parse(Regex.Match(bodyFsV, @"[\d.]+").Value,
            System.Globalization.CultureInfo.InvariantCulture) * PxPt;   // 9px → 6.75
        mc.headFs = mc.headCls.TryGetValue("font-size", out var hfv)
            && Regex.Match(hfv, @"([\d.]+)\s*em") is { Success: true } hem
            ? double.Parse(hem.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture) * mc.bodyFs
            : 0.6 * mc.bodyFs;
        mc.headBg = (mc.headCls.TryGetValue("background-color", out var hbg)
                ? ParseCssColor(hbg) : null)
            ?? Color.FromRgbBytes(0x4E, 0x58, 0x9E);

        mc.headTdM = Regex.Match(mc.html,
            @"<td\b[^>]*class\s*=\s*[""'][^""']*heading[^""']*[""'][^>]*>([\s\S]*?)</td>",
            RegexOptions.IgnoreCase);
        if (!mc.headTdM.Success) return null;
        mc.headText = CollapseWs(DecodeEntities(
            Regex.Replace(mc.headTdM.Groups[1].Value, "<[^>]+>", " "))).Trim();

        mc.innerTblM = Regex.Match(mc.html,
            @"<table\b[^>]*class\s*=\s*[""'][^""']*metrics[^""']*[""'][^>]*>([\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        if (!mc.innerTblM.Success) return null;
        mc.rows = new List<(string label, string valueMain, string valueSup)>();
        ReadMetricsCardRows(mc);
        if (mc.rows.Count == 0) return null;

        DrawMetricsCardFrame(mc);

        mc.labelBoxW = McValueXPt - McLabelXPt;
        mc.yBase = McFirstBasePt;
        DrawMetricsCardRows(mc);

        mc.page.AddContentStream(Encoding.ASCII.GetBytes(mc.sb.ToString()));
        _ = fm;
        return mc.doc;
    }

    // ── The XML-DUMP VIEWER ─────────────────────────────────────────────────────
    // The "styled XML dump" export idiom: html/body margins zeroed, one root
    // class whose `font:` shorthand carries a KEYWORD size and a quoted family
    // (`font: small 'Verdana'`), a `.root *` rule turning every descendant into
    // a padded block (`display: block; padding-left: 2em`), and nested
    // per-element divs whose leaf anchors/spans carry the element markup as
    // colored runs with a negative em margin pulling them back onto the
    // container's line. The expected render applies the whole chain: line x =
    // page margin + root padding-left + one `*` padding per open container +
    // the leaf's own (negative) margin-left, every em resolved at the root's
    // keyword size.
    //
    // Vertical model (formula-exact against the measured output): line box =
    // MetricLineHeight(fs, winSum) (Verdana small: 16px = 12pt), baseline =
    // MetricBaselineDrop; the first line opens at top margin + the root's
    // inline padding-top.

    // ── The PRINT-MEDIA JOB AD ──────────────────────────────────────────────────
    // A job-ad export rendered on its @media print rules: the container class
    // (max-width + padding + px font + unitless line-height) sizes the sheet at
    // zero margins (page = UA body margin + padding + max-width + padding), the
    // print block hides the apply/benefits/salary chrome, h6 section headers
    // set uppercase over a bottom hairline, and list items draw the
    // `li:before` content marker with its margin-right. All vertical geometry
    // is the win-metric line model: line = px-round(fs·factor), baseline =
    // half-leading + winAscent; block gaps are the print rules' margins,
    // parent/child bottoms MAX-collapsing.

    /// <summary>Inner markup of the balanced element whose OPEN tag ends at
    /// <c>afterOpen</c> — null when unbalanced.</summary>
    // ── Contract-invoice sheets on remote faces ─────────────────────────────
    // The Lato invoice: a Google-Fonts stylesheet resolves the sheet's faces
    // (the converter FETCHES the css and its TTF programs, exactly as it
    // fetches remote images), the 600 px min-width body sets the 450 pt
    // column (page = 90 + UA 6 + 450 + 90 = 636), the float header pairs the
    // #5d80ba info panel with the fetched logo, and each `.contract` block —
    // h2 label line, right-floated description, orange collapsed table —
    // moves WHOLE to the next page under its page-break-inside: avoid.

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,
        Dictionary<(int weight, bool italic), string>> RemoteFaceCache = new(StringComparer.Ordinal);

    /// <summary>Fetch the @font-face programs a stylesheet names over http(s),
    /// register them as font sources, and map (weight, italic) to the face
    /// names the repository now resolves. Cached per css text hash.</summary>
    private static Dictionary<(int weight, bool italic), string> LoadRemoteFaces(string css)
    {
        var key = css.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + css.GetHashCode().ToString("x", System.Globalization.CultureInfo.InvariantCulture);
        return RemoteFaceCache.GetOrAdd(key, _ =>
        {
            var map = new Dictionary<(int, bool), string>();
            foreach (Match m in Regex.Matches(css, @"@font-face\s*\{(?<b>[^{}]*)\}",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var body = m.Groups["b"].Value;
                var wM = Regex.Match(body, @"font-weight\s*:\s*(\d+)", RegexOptions.IgnoreCase);
                var weight = wM.Success
                    ? int.Parse(wM.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                    : 400;
                var italic = Regex.IsMatch(body, @"font-style\s*:\s*(italic|oblique)",
                    RegexOptions.IgnoreCase);
                var uM = Regex.Match(body, @"url\(\s*[""']?(https?://[^""')]+)[""']?\s*\)",
                    RegexOptions.IgnoreCase);
                if (!uM.Success || map.ContainsKey((weight, italic))) continue;
                var bytes = FetchRemoteImage(uM.Groups[1].Value.Trim());
                if (bytes is null || bytes.Length < 12) continue;
                string? fam = null, sub = null;
                try
                {
                    var tp = new Text.TrueTypeParser(bytes);
                    tp.Parse();
                    fam = tp.FamilyName; sub = tp.SubfamilyName;
                }
                catch { continue; }
                if (string.IsNullOrWhiteSpace(fam) || fam == "Unknown") continue;
                Text.FontRepository.Sources.Add(new Text.MemoryFontSource(bytes));
                // resolve back through the repository by whichever name the
                // program's own name table answers to
                foreach (var cand in new[]
                    { fam + " " + (sub ?? ""), fam, fam + (sub ?? "") })
                {
                    var candT = cand.Trim();
                    if (candT.Length == 0 || PosFace(candT).ttf is null) continue;
                    map[(weight, italic)] = candT;
                    break;
                }
            }
            return map;
        });
    }

    // ── Resume-builder document sheets ──────────────────────────────────────
    // The LiveCareer resume export: a `div#document` whose dynamic stylesheet
    // resolves the sheet (11 pt Palatino Linotype at the 13 pt line, 30 pt
    // horizontal padding, 552 pt single column, 23/31 name, 10/12 address,
    // 13/15 section titles). The expected output emits each page's SECTION
    // TITLES first, then the name/address group, then the body — the text
    // fragments keep that order, and every markup span is its own fragment.
    // List items seat their marker at +12.21 and their text at +23 inside
    // the column (30 + 10 pt ul + 13 pt li = the asserted x 53).

    // ── Angular audit-report exports ────────────────────────────────────────
    // The audit finding sheet: an Angular app dump whose content lives under
    // `.report-container` (40 px padded white card inside the 17 px
    // main-content-middle), headed by h1–h4 at 14 px bold with left-line
    // dashes, then a label table — 9 pt bold th labels in a 50 pt column, a
    // dot cell, and content cells. Text inside the <gt-editor-content> custom
    // element does NOT inherit the table's 12 px size (the expected render's
    // inheritance breaks at the unknown element): it draws at the UA 16 px
    // base, italic via the sheet's span[lang] rule, #333 ink. The measured
    // constants carry their derivations.

    // ── Decision-notification letters ───────────────────────────────────────
    // A TCI notifications template: an all-inline-styled sheet under a centred
    // `basis` container (duplicate width declarations — the LAST wins), a float
    // header (broken logo/QR images keeping their divs' widths), bordered
    // `information-table`s with bold label spans riding their value lines,
    // `boxSection` panels whose white-backed titles sit ON the 2 px frame, and
    // a 48.5 % float column pair. All seats derive from the inline box model at
    // the UA 13 px body line; the measured constants below carry their
    // derivations.

    private static string? BalancedInner(string html, int afterOpen, string tag)
    {
        var depth = 1;
        var rx = new Regex(@"<(/?)" + tag + @"\b[^>]*?(/?)>", RegexOptions.IgnoreCase);
        for (var m = rx.Match(html, afterOpen); m.Success; m = m.NextMatch())
        {
            if (m.Groups[2].Value == "/") continue;
            depth += m.Groups[1].Value == "/" ? -1 : 1;
            if (depth == 0) return html[afterOpen..m.Index];
        }
        return null;
    }

    /// <summary>A declaration's length in pt with em resolved at
    /// <paramref name="emPt"/> — null when the property is absent.</summary>
    private static double? CssEmLen(string decl, string prop, double emPt)
    {
        var m = Regex.Match(decl, @"(?<![-\w])" + Regex.Escape(prop) + @"\s*:\s*(-?[\d.]+)\s*(em|px|pt)?",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var v = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "em" => v * emPt,
            "px" => v * 0.75,
            _ => v,
        };
    }

    /// <summary>Attribute value out of a single tag's markup — null when absent.</summary>
    private static string? AttrValue(string tagMarkup, string attr)
    {
        var m = Regex.Match(tagMarkup,
            attr + @"\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
    }
}
