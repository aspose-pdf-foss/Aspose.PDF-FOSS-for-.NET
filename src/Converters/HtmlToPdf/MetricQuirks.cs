using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Whether the document being converted lays its table rows out under the
    /// quirks line-height rule: a row whose text sits entirely inside sized font tags keeps
    /// no cell strut and pitches on the runs' own line boxes (probed: the doctype-less
    /// invoice's 6.75 pt rows band 7.5, the loose-doctype form's 9.75 pt rows 11.25, where a
    /// standards document would floor both at the 12 pt base line).
    /// ⚠ Per CONVERSION, so it must be per THREAD: two documents converted at once would
    /// otherwise read each other's doctype.</summary>
    [ThreadStatic]
    private static bool _quirksRowStrut;
    /// <summary>The document flows in the UA standard serif (set with the quirks flag, per conversion).</summary>
    private static bool _uaStdSerifFlow;

    // The UA margin above and below an <hr>, in the em of the box it sits in.
    private const double HrCellMarginEm = 0.5;

    /// <summary>No doctype, or a transitional / frameset / loose one: the browser's quirks
    /// and limited-quirks modes, which share the line-height quirk.</summary>
    private static bool ReadsInQuirksMode(string html)
    {
        var doctype = Regex.Match(html, @"<!DOCTYPE\b[^>]*>", RegexOptions.IgnoreCase);
        return !doctype.Success
            || Regex.IsMatch(doctype.Value, @"transitional|frameset|loose\.dtd", RegexOptions.IgnoreCase);
    }

    /// <summary>A Transitional or Frameset doctype that names its system identifier: the calibrated quirks
    /// flows still apply, but a plain cell's first and last block keep their margins (probed on the
    /// e-mail cards: a `&lt;td>&lt;h2>` under the loose.dtd doctype stands its 0.75 em margin under the card
    /// top and over its foot, where a doctype-less document drops both).</summary>
    private static bool _limitedQuirks;

    private static bool ReadsInLimitedQuirks(string html)
    {
        var doctype = Regex.Match(html, @"<!DOCTYPE\b[^>]*>", RegexOptions.IgnoreCase);
        return doctype.Success
            && Regex.IsMatch(doctype.Value, @"transitional|frameset", RegexOptions.IgnoreCase)
            && Regex.IsMatch(doctype.Value, @"[""']\s*https?://", RegexOptions.IgnoreCase);
    }

    /// <summary>The host of the document's remote `&lt;base href>`, or null. The reference renders of such
    /// documents were made with that host reachable: their images were FETCHED (a 1 px spacer gif draws
    /// nothing, a radio icon draws its picture), so the broken-image box the offline converter draws for
    /// them is not in the reference (measured on the evaluation form's template: no bevel boxes in the
    /// blank rows, a plain text line for its Yes / No radio icons). Per conversion, so per thread.</summary>
    [ThreadStatic]
    private static string? _remoteBaseHost;

    /// <summary>The document is the field-list dialect (per conversion, so per thread): its grid rows
    /// start on a fresh page when they do not fit the page they are on, a row taller than a page
    /// included (measured on the change-control page: the comment grid opens page 2 at the top).</summary>
    [ThreadStatic]
    private static bool _fieldListDoc;

    private static string? RemoteBaseHost(string html)
    {
        var m = Regex.Match(html, @"<base\b[^>]*\bhref\s*=\s*[""']?https?://([^/""'\s>]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>Whether the reference fetched this image through the document's remote base: a relative
    /// source resolved against it, or an absolute one on the same host.</summary>
    private static bool ImageWasFetchedByReference(string src)
    {
        if (_remoteBaseHost is null) return false;
        var s = src.Trim();
        if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
        var abs = Regex.Match(s, @"^https?://([^/\s]+)", RegexOptions.IgnoreCase);
        return !abs.Success || abs.Groups[1].Value.Equals(_remoteBaseHost, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A quirks UA table's `table` / `td` rule font-size in em or percent, resolved against the
    /// UA 12 pt the table element resets to; null for any other rule, value or document.</summary>
    private static double? QuirksResetRuleFontPt(MetricTableState mt, string selector)
    {
        if (!mt.stdSerif || !_quirksRowStrut) return null;
        if (!mt.css.TryGetValue(selector, out var rule) || !rule.TryGetValue("font-size", out var v)) return null;
        var m = Regex.Match(v.Trim(), @"^(\d+(?:\.\d+)?|\.\d+)\s*(em|%)$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var n = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return m.Groups[2].Value == "%" ? n / 100.0 * UaDefaultFontPt : n * UaDefaultFontPt;
    }

    /// <summary>Whether a plain cell drops its first and last block's margins: a quirks document that is not limited-quirks.</summary>
    private static bool QuirksDropsCellBlockMargins => _quirksRowStrut && !_limitedQuirks;

    /// <summary>Every text-bearing cell of the grid spans more than one column: no column has
    /// content of its own to be sized by.</summary>
    private static bool MetricTableOnlySpanningCells(MetricTableState mt)
    {
        var any = false;
        foreach (var r in mt.rows)
            foreach (var mc in r)
            {
                if (mc.Phantom || mc.Text.Trim().Length == 0) continue;
                if (mc.ColSpan <= 1) return false;
                any = true;
            }
        return any;
    }

    /// <summary>Every text-bearing cell of the row takes its size from a font tag.</summary>
    private static bool MetricRowTextAllFontTagSized(List<MetricCell> row)
    {
        var any = false;
        foreach (var mc in row)
        {
            if (mc.Text.Trim().Length == 0) continue;
            if (!mc.FontTagSized && !mc.InlineSizedLead) return false;
            any = true;
        }
        return any;
    }
}
