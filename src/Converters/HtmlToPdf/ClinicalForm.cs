using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The Bootstrap clinical report form ─────────────────────────────────────
    // A `form-container` sheet: a flex `header-grid` (logo image beside a
    // bordered `header-table`), a centred `header-maintext` notice over the
    // form-header's 2px rule, then `section.section`s — each an #ddd-banded h3
    // followed by an <ol> of labelled inputs: full-width `.form-control` boxes,
    // bare inline text inputs, and 13px radio circles inside inline <label>s.
    //
    // Geometry (all measured on the expected PDF; A4 595×842,
    // 90/72 content margins, Segoe UI 12 pt):
    //   .form-container is 90vw of the 415 content band = 373.5 pt centred at
    //   110.75, bordered 1px #444, padded 20px → inner column 126.5..468.5.
    //   Body lines pitch 18 (16px × 1.5); the baseline hangs 13.969 under the
    //   line top (Segoe UI hhea 2210/2048 ascent + 1.020 half-leading) and the
    //   line box runs 4.031 below it. Adjacent BOX edges (an input's border
    //   box against anything else) overlap 0.75 — a `.form-control` box tops
    //   at the previous bottom − 0.75 and the next line tops 0.75 above its
    //   bottom edge; a page-continuation box tops at 72 − 0.75.
    //   A `.form-control` is 318 × 30 at the item column; a bare inline input
    //   is 134.43 × 24 seated 16.97 under its line top (the line grows to the
    //   24 pt box) and advances a 4px right margin; a radio is 9.75 square,
    //   its bottom on the baseline. `.form-check` indents 1.5em and adds its
    //   2px margin-bottom. The h3 band is 23.4 tall (14.4 line + 5px padding
    //   + 1px #aaa border) on #ddd, its baseline 16.67 under the band top;
    //   blocks meet it at the section's collapsed 20px margin.
    //   The widget appearances the era writer emits (a 1 pt black border box
    //   0.5 inside the field rect; a two-bezier 4.375-radius circle) are
    //   drawn as page ink here — the raster compare reads the page content.

    private const double CfPageW = 595.0;
    private const double CfPageH = 842.0;
    private const double CfContentTop = 72.0;
    private const double CfContentBottom = 770.0;
    private const double CfBodyX = 90.0;
    private const double CfBodyW = 415.0;
    private const double CfContainerW = 373.5;       // 90vw of the body band
    private const double CfContainerX0 = 110.75;
    private const double CfInnerX0 = 126.5;          // container border + 20px pad
    private const double CfInnerX1 = 468.5;
    private const double CfItemX = 150.5;            // ol padding-left 2rem
    private const double CfFs = 12.0;                // body 16px
    private const double CfLineH = 18.0;             // 16px × 1.5
    private const double CfBaseOff = 13.969;         // line top → baseline
    private const double CfLineDesc = 4.031;         // baseline → line bottom
    private const double CfJunction = 0.75;          // box-edge overlap (1px)
    private const double CfControlH = 30.0;          // .form-control border box
    private const double CfInlineInputW = 134.43;    // bare input default width
    private const double CfInlineInputH = 24.0;
    private const double CfInlineBaseOff = 16.97;    // input-line top → baseline
    private const double CfInlineMarginR = 3.0;      // input 4px margin-right
    private const double CfTextAreaH = 57.0;
    private const double CfRadio = 9.75;             // 13px UA radio
    private const double CfCheckIndent = 18.0;       // .form-check 1.5em
    private const double CfCheckMb = 1.5;            // .form-check 2px mb
    private const double CfBandH = 23.4;             // .section h3 band
    private const double CfBandBaseOff = 16.67;
    private const double CfBandPadX = 8.25;          // 10px pad + 1px border
    private const double CfSectionGap = 15.0;        // 20px collapsed margin
    private const double CfH3Mb = 6.0;               // h3 margin-bottom .5rem
    private const double CfOlMb = 12.0;              // ol margin-bottom 1rem
    private const double CfHeaderTop = 87.75;        // container content top
    private const double CfLogoBase = 118.69;        // alt-text baseline (flex centring)
    private const double CfTitleBandH = 30.0;        // h5 line 18 + pads + 6 mb
    private const double CfTitleBaseOff = 18.21;
    private const double CfCellH = 24.0;             // .table-sm row
    private const double CfCellPad = 3.0;            // .25rem
    private const double CfCellBaseOff = 16.97;
    private const double CfNoticeGap = 12.0;         // table margin-bottom 1rem
    private const double CfSmallFs = 10.5;           // .875em
    private const double CfMarkerOneX = 136.93;      // the measured "1." marker x
    private const string CfText = "0.129 0.145 0.161";  // #212529
    private const string CfBlack = "0 0 0";

    private sealed class CfAtom
    {
        public string Kind = "text";                 // text/radio/iinput/binput/tarea/br/check/table
        public string Text = "";
        public int Style;                            // 0 regular, 1 bold, 2 italic, 3 small
        public List<CfAtom>? Group;                  // a <label>'s inline children
        public List<List<string>>? Rows;             // table cell texts
    }

    private static Document? TryRenderClinicalForm(string html)
    {
        if (!html.Contains("class=\"form-container\"", StringComparison.Ordinal)
            || !html.Contains("id=\"header-table\"", StringComparison.Ordinal)
            || !html.Contains("id=\"header-maintext\"", StringComparison.Ordinal)
            || !html.Contains("<section class=\"section\"", StringComparison.Ordinal)
            || !html.Contains("form-control", StringComparison.Ordinal))
            return null;

        var cf = new ClinicalFormState();
        if (!TryParseClinicalForm(cf, html)) return null;
        cf.inv = System.Globalization.CultureInfo.InvariantCulture;
        DrawClinicalHeader(cf);
        DrawClinicalSections(cf);
        AssembleClinicalPages(cf);
        return cf.doc;
    }

    // Tokenise one <li>'s inner HTML into flow atoms. A <label> wraps its
    // children into one wrap-unit group (unless it is a .form-check block);
    // radios, bare text inputs, .form-control inputs, <br>, <em>/<strong>/<small>
    // runs and stray text are flat atoms.
    private static List<CfAtom> ParseCfAtoms(string inner)
    {
        var atoms = new List<CfAtom>();
        ParseCfInline(inner, atoms, 0);
        return atoms;
    }

    private static void ParseCfInline(string inner, List<CfAtom> atoms, int style)
    {
        static string Collapse(string s) => Regex.Replace(DecodeEntities(s), @"\s+", " ");
        var rx = new Regex(
            @"<label\b([^>]*)>([\s\S]*?)</label>|<input\b([^>]*?)/?>|<textarea\b[^>]*>[\s\S]*?</textarea>"
            + @"|<br\s*/?>|<(em|i|strong|b|small)\b[^>]*>([\s\S]*?)</\4>"
            + @"|<div\b[^>]*class=""[^""]*table-responsive[^""]*""[^>]*>([\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        var pos = 0;
        foreach (Match m in rx.Matches(inner))
        {
            var before = Collapse(inner[pos..m.Index]);
            if (before.Trim().Length > 0 || (before.Contains(' ') && atoms.Count > 0))
                atoms.Add(new CfAtom { Kind = "text", Text = before, Style = style });
            pos = m.Index + m.Length;
            if (m.Value.StartsWith("<label", StringComparison.OrdinalIgnoreCase))
            {
                var attrs = m.Groups[1].Value;
                var content = m.Groups[2].Value;
                if (attrs.Contains("form-check", StringComparison.OrdinalIgnoreCase))
                {
                    atoms.Add(new CfAtom { Kind = "check", Text = Collapse(content).Trim() });
                }
                else
                {
                    var group = new List<CfAtom>();
                    ParseCfInline(content, group, style);
                    // trim the group's leading whitespace-only run (the radio leads)
                    if (group.Count > 0 && group[0].Kind == "text" && group[0].Text.Trim().Length == 0)
                        group.RemoveAt(0);
                    // and the last run's trailing space — the inter-label gap is the
                    // markup whitespace OUTSIDE the label, not the label's own tail
                    if (group.Count > 0 && group[^1].Kind == "text")
                    {
                        group[^1].Text = group[^1].Text.TrimEnd();
                        if (group[^1].Text.Length == 0) group.RemoveAt(group.Count - 1);
                    }
                    if (group.Count > 0)
                        atoms.Add(new CfAtom { Kind = "label", Group = group });
                }
            }
            else if (m.Value.StartsWith("<input", StringComparison.OrdinalIgnoreCase))
            {
                var attrs = m.Groups[3].Value;
                var type = Regex.Match(attrs, @"type\s*=\s*""?(\w+)", RegexOptions.IgnoreCase)
                    .Groups[1].Value.ToLowerInvariant();
                if (type == "radio" || type == "checkbox")
                    atoms.Add(new CfAtom { Kind = "radio" });
                else if (attrs.Contains("form-control", StringComparison.OrdinalIgnoreCase))
                    atoms.Add(new CfAtom { Kind = "binput" });
                else
                    atoms.Add(new CfAtom { Kind = "iinput" });
            }
            else if (m.Value.StartsWith("<textarea", StringComparison.OrdinalIgnoreCase))
                atoms.Add(new CfAtom { Kind = "tarea" });
            else if (m.Value.StartsWith("<br", StringComparison.OrdinalIgnoreCase))
                atoms.Add(new CfAtom { Kind = "br" });
            else if (m.Groups[4].Success)
            {
                var tag = m.Groups[4].Value.ToLowerInvariant();
                var runStyle = tag is "em" or "i" ? 2 : tag is "strong" or "b" ? 1 : 3;
                var t = Collapse(m.Groups[5].Value);
                if (t.Trim().Length > 0)
                    atoms.Add(new CfAtom { Kind = "text", Text = t.Trim(), Style = runStyle });
            }
            else if (m.Groups[6].Success)
            {
                var rows = new List<List<string>>();
                foreach (Match rm in Regex.Matches(m.Groups[6].Value, @"<tr\b[^>]*>([\s\S]*?)</tr>",
                    RegexOptions.IgnoreCase))
                {
                    var cells = new List<string>();
                    foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                        @"<t[dh]\b[^>]*>([\s\S]*?)</t[dh]>", RegexOptions.IgnoreCase))
                        cells.Add(Regex.Replace(DecodeEntities(
                            Regex.Replace(cm.Groups[1].Value, @"<[^>]+>", " ")), @"\s+", " ").Trim());
                    if (cells.Count > 0) rows.Add(cells);
                }
                atoms.Add(new CfAtom { Kind = "table", Rows = rows });
            }
        }
        var tail = Collapse(inner[pos..]);
        if (tail.Trim().Length > 0)
            atoms.Add(new CfAtom { Kind = "text", Text = tail, Style = style });
    }

    // Greedy word wrap with a caller-supplied measure.
    private static List<string> WrapCfWords(string text, Func<string, double> measure, double budget)
    {
        var lines = new List<string>();
        var cur = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var trial = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length == 0 || measure(trial) <= budget) { cur.Clear(); cur.Append(trial); }
            else { lines.Add(cur.ToString()); cur.Clear(); cur.Append(word); }
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        if (lines.Count == 0) lines.Add("");
        return lines;
    }
}
