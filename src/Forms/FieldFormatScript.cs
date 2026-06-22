using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>
/// Minimal Acrobat-JS formatter that runs the small set of built-in
/// <c>AFNumber_Format</c> / <c>AFDate_FormatEx</c> / <c>AFTime_Format</c> /
/// <c>AFPercent_Format</c> calls embedded in PDF form fields'
/// <c>/AA/F</c> Format action. Doesn't run a full JS engine — pattern-
/// matches the well-known signatures and applies the equivalent .NET
/// format to the input value before the appearance stream is stamped.
///
/// Coverage is intentionally narrow: only the calls produced by Acrobat
/// when the user picks a built-in format from the field properties UI.
/// Custom JS that mutates <c>event.value</c> in arbitrary ways is not
/// emulated and the original value flows through unchanged.
/// </summary>
internal static class FieldFormatScript
{
    /// <summary>Apply the field's /AA/F format action to <paramref name="rawValue"/>
    /// if one is present. Returns <paramref name="rawValue"/> unchanged when no
    /// format action exists or the script isn't a recognised built-in.</summary>
    internal static string Apply(PdfDictionary fieldDict, PdfReader reader, string rawValue)
    {
        var script = ExtractFormatScript(fieldDict, reader);
        if (script is null) return rawValue;
        return TryApply(script, rawValue, out var formatted) ? formatted : rawValue;
    }

    /// <summary>Whether <paramref name="rawValue"/> is acceptable for the field's
    /// /AA/F format action. An empty value (clearing the field) is always allowed;
    /// a field with no format action — or one driven by custom JavaScript rather
    /// than a recognised built-in formatter — accepts any value. A recognised
    /// built-in formatter (AFDate/AFTime/AFNumber/AFPercent) rejects a value it
    /// cannot parse, matching the Aspose.PDF for .NET behaviour of leaving the prior value
    /// in place.</summary>
    internal static bool IsValueValid(PdfDictionary fieldDict, PdfReader? reader, string rawValue)
    {
        if (reader is null || string.IsNullOrEmpty(rawValue)) return true;
        var script = ExtractFormatScript(fieldDict, reader);
        if (script is null || !IsBuiltInFormatter(script)) return true;
        return TryApply(script, rawValue, out _);
    }

    private static bool IsBuiltInFormatter(string script)
        => Regex.IsMatch(script, @"AF(Date|Time|Number|Percent)_Format");

    private static string? ExtractFormatScript(PdfDictionary fieldDict, PdfReader reader)
    {
        // /AA on the field, fallback to single widget kid's /AA
        var aa = reader.ResolveDict(fieldDict.Get("AA"));
        if (aa is null)
        {
            var kids = reader.Resolve(fieldDict.Get("Kids")) as PdfArray;
            if (kids is { Count: 1 })
            {
                var kidDict = reader.ResolveDict(kids[0]);
                if (kidDict is not null)
                    aa = reader.ResolveDict(kidDict.Get("AA"));
            }
        }
        if (aa is null) return null;

        // /AA/F — the Format action
        var f = reader.ResolveDict(aa.Get("F"));
        if (f is null) return null;
        var jsObj = reader.Resolve(f.Get("JS"));
        return jsObj switch
        {
            PdfString s => s.ToText(),
            PdfStream stream => System.Text.Encoding.Latin1.GetString(reader.DecodeStream(stream)),
            _ => null,
        };
    }

    /// <summary>Recognise the built-in Acrobat formatters and apply them.</summary>
    internal static bool TryApply(string script, string rawValue, out string formatted)
    {
        formatted = rawValue;
        if (string.IsNullOrEmpty(script)) return false;

        // AFDate_FormatEx("<pattern>") — most common date formatter.
        var date = Regex.Match(script, @"AFDate_FormatEx\s*\(\s*""([^""]*)""\s*\)");
        if (date.Success)
            return TryFormatDate(date.Groups[1].Value, rawValue, out formatted);

        // AFDate_Format(idx) — index into Acrobat's built-in date format list.
        var dateIdx = Regex.Match(script, @"AFDate_Format\s*\(\s*(\d+)\s*\)");
        if (dateIdx.Success && int.TryParse(dateIdx.Groups[1].Value, out var di))
            return TryFormatDate(BuiltInDatePattern(di), rawValue, out formatted);

        // AFTime_Format(idx) — index into the built-in time format list.
        var timeIdx = Regex.Match(script, @"AFTime_Format\s*\(\s*(\d+)\s*\)");
        if (timeIdx.Success && int.TryParse(timeIdx.Groups[1].Value, out var ti))
            return TryFormatDate(BuiltInTimePattern(ti), rawValue, out formatted);

        // AFNumber_Format(nDec, sepStyle, ...) — first two args drive the layout.
        var num = Regex.Match(script, @"AFNumber_Format\s*\(\s*(-?\d+)\s*,\s*(-?\d+)");
        if (num.Success
            && int.TryParse(num.Groups[1].Value, out var nDec)
            && int.TryParse(num.Groups[2].Value, out var sepStyle))
            return TryFormatNumber(nDec, sepStyle, rawValue, out formatted);

        // AFPercent_Format(nDec, sepStyle)
        var pct = Regex.Match(script, @"AFPercent_Format\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)");
        if (pct.Success
            && int.TryParse(pct.Groups[1].Value, out var pDec)
            && int.TryParse(pct.Groups[2].Value, out var pSep))
            return TryFormatPercent(pDec, pSep, rawValue, out formatted);

        return false;
    }

    /// <summary>Acrobat's built-in date format strings indexed 0–6 (matches the
    /// dropdown order in the field-properties UI).</summary>
    private static string BuiltInDatePattern(int idx) => idx switch
    {
        0 => "m/d",
        1 => "m/d/yy",
        2 => "m/d/yyyy",
        3 => "mm/dd/yy",
        4 => "mm/dd/yyyy",
        5 => "yy-mm-dd",
        6 => "yyyy-mm-dd",
        _ => "m/d/yy",
    };

    private static string BuiltInTimePattern(int idx) => idx switch
    {
        0 => "HH:MM",
        1 => "h:MM tt",
        2 => "HH:MM:ss",
        3 => "h:MM:ss tt",
        _ => "HH:MM",
    };

    private static bool TryFormatDate(string acrobatPattern, string raw, out string formatted)
    {
        formatted = raw;
        // Acrobat uses lowercase "m/d/yy" etc.; .NET uses "M/d/yy" (uppercase month).
        // Map the patterns: m → M (month, no zero pad), mm → MM (zero-pad), d → d, dd → dd,
        // yy → yy, yyyy → yyyy, HH/MM/ss/tt all map directly.
        var netPattern = ConvertAcrobatDatePattern(acrobatPattern);

        // Try a permissive set of input patterns (the user might type "05/15/2015"
        // or "5/15/15" or "2015-05-15") before falling back to general parse.
        var tryFormats = new[]
        {
            "M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "MM/dd/yy",
            "yyyy-MM-dd", "yyyy/MM/dd", "d/M/yyyy", "dd/MM/yyyy",
        };
        if (DateTime.TryParseExact(raw, tryFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var dt)
            || DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out dt))
        {
            formatted = dt.ToString(netPattern, CultureInfo.InvariantCulture);
            return true;
        }
        return false;
    }

    private static string ConvertAcrobatDatePattern(string p)
    {
        // Walk the pattern and map tokens. m/mm → M/MM, d/dd → d/dd, yy/yyyy → yy/yyyy,
        // H/HH/M/MM (time minutes use uppercase MM in Acrobat too — context-sensitive)
        // — for the simple date-only case, the only ambiguity is "MM" (Acrobat's month
        // vs .NET's minute). We treat any 'M' run as month; time patterns use H/HH:mm.
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < p.Length)
        {
            var c = p[i];
            if (c == 'm')
            {
                int n = 0;
                while (i < p.Length && p[i] == 'm') { n++; i++; }
                sb.Append(new string('M', n));
            }
            else if (c == 'M')
            {
                // Acrobat: minutes (in time patterns). .NET: minutes is "mm".
                int n = 0;
                while (i < p.Length && p[i] == 'M') { n++; i++; }
                sb.Append(new string('m', n));
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary>AFNumber_Format: nDec = decimals, sepStyle = thousands/decimal separator pair.
    /// 0 = "1,234.56", 1 = "1234.56", 2 = "1.234,56", 3 = "1234,56".</summary>
    private static bool TryFormatNumber(int nDec, int sepStyle, string raw, out string formatted)
    {
        formatted = raw;
        if (!double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var v))
            return false;
        formatted = FormatWithSeparators(v, nDec, sepStyle);
        return true;
    }

    private static bool TryFormatPercent(int nDec, int sepStyle, string raw, out string formatted)
    {
        formatted = raw;
        if (!double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var v))
            return false;
        formatted = FormatWithSeparators(v * 100.0, nDec, sepStyle) + "%";
        return true;
    }

    private static string FormatWithSeparators(double value, int nDec, int sepStyle)
    {
        var (thousands, dec) = sepStyle switch
        {
            0 => (",", "."),
            1 => ("",  "."),
            2 => (".", ","),
            3 => ("",  ","),
            _ => (",", "."),
        };
        var nfi = new NumberFormatInfo
        {
            NumberDecimalSeparator = dec,
            NumberGroupSeparator = thousands,
            NumberDecimalDigits = Math.Max(0, nDec),
        };
        return value.ToString("N", nfi);
    }
}
