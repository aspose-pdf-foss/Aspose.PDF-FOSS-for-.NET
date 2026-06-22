using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Annotations.JavascriptExtensions;

/// <summary>
/// Formats a date/time value string according to an Acrobat-style date format.
/// Corresponds to the PDF JavaScript AF_Date_Format function.
/// </summary>
public static class FieldDateTimeFormatter
{
    private static readonly string[] MonthShort =
        { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    private static readonly string[] MonthLong =
        { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

    /// <summary>
    /// Format a date value string according to the given Acrobat date format.
    /// </summary>
    /// <param name="dateFormat">Acrobat date format (e.g. "mm/dd/yyyy", "dd-mmm-yyyy").</param>
    /// <param name="dateValue">Input date string (e.g. "05/15/2015", "051515").</param>
    /// <returns>Formatted date string.</returns>
    /// <exception cref="ArgumentException">When the resolved date is invalid (e.g. day &gt; 31, month &gt; 12).</exception>
    /// <exception cref="FormatException">When the input cannot be parsed.</exception>
    public static string Format(string dateFormat, string dateValue)
    {
        var allTokens = TokeniseFormat(dateFormat);
        var components = new List<ComponentToken>();
        foreach (var t in allTokens)
        {
            if (t is ComponentToken ct)
                components.Add(ct);
        }

        var rawVals = ParseInputValues(dateValue, components);

        // Per-token final numeric values
        var finalVals = new int?[rawVals.Length];
        for (int i = 0; i < rawVals.Length; i++)
            finalVals[i] = rawVals[i] == int.MinValue ? null : rawVals[i];

        // Expand 2-digit years where the token is yyyy
        for (int i = 0; i < components.Count; i++)
        {
            var tok = components[i];
            if (tok.Kind == TokenKind.Year && tok.FourDigit && finalVals[i] != null)
            {
                finalVals[i] = ExpandYear(finalVals[i]!.Value);
            }
        }

        // Locate the first month and first day component indices
        int monthIdx = -1, dayIdx = -1;
        for (int i = 0; i < components.Count; i++)
        {
            if (components[i].Kind == TokenKind.Month && monthIdx < 0) monthIdx = i;
            if (components[i].Kind == TokenKind.Day && dayIdx < 0) dayIdx = i;
        }

        // Auto-correct: if first month > 12 and first day <= 12, swap
        bool didSwap = false;
        if (monthIdx >= 0 && dayIdx >= 0)
        {
            int? m = finalVals[monthIdx];
            int? d = finalVals[dayIdx];
            if (m != null && d != null && m > 12 && d <= 12)
            {
                finalVals[monthIdx] = d;
                finalVals[dayIdx] = m;
                didSwap = true;
            }
        }

        // Validate first month and first day
        if (monthIdx >= 0)
        {
            int? m = finalVals[monthIdx];
            if (m != null && (m < 1 || m > 12))
                throw new ArgumentException($"Invalid month {m} in date value \"{dateValue}\"");
        }
        if (dayIdx >= 0)
        {
            int? d = finalVals[dayIdx];
            if (d != null && (d < 1 || d > 31))
            {
                // If we swapped and the day is still invalid, the input is unparseable
                if (didSwap)
                    throw new FormatException($"Cannot parse date value \"{dateValue}\" with format \"{dateFormat}\"");
                throw new ArgumentException($"Invalid day {d} in date value \"{dateValue}\"");
            }
        }

        // Render output
        var sb = new StringBuilder();
        int compIdx = 0;
        foreach (var tok in allTokens)
        {
            if (tok is LiteralToken lt)
            {
                sb.Append(lt.Text);
                continue;
            }

            var ct2 = (ComponentToken)tok;
            int? val = compIdx < finalVals.Length ? finalVals[compIdx] : null;
            compIdx++;

            if (val == null)
            {
                sb.Append('0');
                continue;
            }

            if (ct2.Kind == TokenKind.Month && (ct2.Abbrev || ct2.FullName))
            {
                int idx = val.Value - 1;
                if (idx >= 0 && idx < 12)
                    sb.Append(ct2.FullName ? MonthLong[idx] : MonthShort[idx]);
            }
            else if (ct2.Kind == TokenKind.Year && ct2.FourDigit)
            {
                sb.Append(val.Value.ToString().PadLeft(4, '0'));
            }
            else if (ct2.PadWidth > 0)
            {
                int display = ct2.Kind == TokenKind.Year ? val.Value % 100 : val.Value;
                sb.Append(display.ToString().PadLeft(ct2.PadWidth, '0'));
            }
            else
            {
                sb.Append(val.Value.ToString());
            }
        }

        return sb.ToString();
    }

    #region Tokeniser

    private enum TokenKind { Month, Day, Year, Hour, Minute, Literal }

    private abstract class FormatToken { }

    private class ComponentToken : FormatToken
    {
        public TokenKind Kind;
        public int CompactDigits;
        public int PadWidth;
        public bool Abbrev;
        public bool FullName;
        public bool FourDigit;
    }

    private class LiteralToken : FormatToken
    {
        public string Text = "";
    }

    private static List<FormatToken> TokeniseFormat(string fmt)
    {
        var tokens = new List<FormatToken>();
        int i = 0;

        while (i < fmt.Length)
        {
            if (i + 4 <= fmt.Length && fmt.Substring(i, 4) == "mmmm")
            {
                tokens.Add(new ComponentToken { Kind = TokenKind.Month, CompactDigits = 2, PadWidth = 0, Abbrev = false, FullName = true, FourDigit = false });
                i += 4;
            }
            else if (i + 3 <= fmt.Length && fmt.Substring(i, 3) == "mmm")
            {
                tokens.Add(new ComponentToken { Kind = TokenKind.Month, CompactDigits = 2, PadWidth = 0, Abbrev = true, FullName = false, FourDigit = false });
                i += 3;
            }
            else if (i + 2 <= fmt.Length && fmt.Substring(i, 2) == "mm")
            {
                tokens.Add(new ComponentToken { Kind = TokenKind.Month, CompactDigits = 2, PadWidth = 2, Abbrev = false, FullName = false, FourDigit = false });
                i += 2;
            }
            else if (fmt[i] == 'm')
            {
                tokens.Add(new ComponentToken { Kind = TokenKind.Month, CompactDigits = 1, PadWidth = 0, Abbrev = false, FullName = false, FourDigit = false });
                i += 1;
            }
            else if (i + 2 <= fmt.Length && fmt.Substring(i, 2) == "dd")
            {
                tokens.Add(new ComponentToken { Kind = TokenKind.Day, CompactDigits = 2, PadWidth = 2, Abbrev = false, FullName = false, FourDigit = false });
                i += 2;
            }
            else if (fmt[i] == 'd')
            {
                tokens.Add(new ComponentToken { Kind = TokenKind.Day, CompactDigits = 1, PadWidth = 0, Abbrev = false, FullName = false, FourDigit = false });
                i += 1;
            }
            else if (i + 4 <= fmt.Length && fmt.Substring(i, 4) == "yyyy")
            {
                tokens.Add(new ComponentToken { Kind = TokenKind.Year, CompactDigits = 4, PadWidth = 4, Abbrev = false, FullName = false, FourDigit = true });
                i += 4;
            }
            else if (i + 2 <= fmt.Length && fmt.Substring(i, 2) == "yy")
            {
                tokens.Add(new ComponentToken { Kind = TokenKind.Year, CompactDigits = 2, PadWidth = 2, Abbrev = false, FullName = false, FourDigit = false });
                i += 2;
            }
            else if (i + 2 <= fmt.Length && fmt.Substring(i, 2) == "HH")
            {
                tokens.Add(new ComponentToken { Kind = TokenKind.Hour, CompactDigits = 2, PadWidth = 2, Abbrev = false, FullName = false, FourDigit = false });
                i += 2;
            }
            else if (fmt[i] == 'H')
            {
                tokens.Add(new ComponentToken { Kind = TokenKind.Hour, CompactDigits = 1, PadWidth = 0, Abbrev = false, FullName = false, FourDigit = false });
                i += 1;
            }
            else if (i + 2 <= fmt.Length && fmt.Substring(i, 2) == "MM")
            {
                tokens.Add(new ComponentToken { Kind = TokenKind.Minute, CompactDigits = 2, PadWidth = 2, Abbrev = false, FullName = false, FourDigit = false });
                i += 2;
            }
            else if (fmt[i] == 'M')
            {
                tokens.Add(new ComponentToken { Kind = TokenKind.Minute, CompactDigits = 1, PadWidth = 0, Abbrev = false, FullName = false, FourDigit = false });
                i += 1;
            }
            else
            {
                // Literal character — merge with previous literal if possible
                if (tokens.Count > 0 && tokens[tokens.Count - 1] is LiteralToken prev)
                {
                    prev.Text += fmt[i];
                }
                else
                {
                    tokens.Add(new LiteralToken { Text = fmt[i].ToString() });
                }
                i += 1;
            }
        }

        return tokens;
    }

    #endregion

    #region Input parsing

    private static int? MonthNameToNumber(string s)
    {
        string lower = s.ToLowerInvariant();
        for (int i = 0; i < MonthShort.Length; i++)
        {
            if (lower == MonthShort[i].ToLowerInvariant()) return i + 1;
            if (lower == MonthLong[i].ToLowerInvariant()) return i + 1;
            if (MonthLong[i].ToLowerInvariant().StartsWith(lower) && lower.Length >= 3) return i + 1;
        }
        return null;
    }

    private static int ExpandYear(int raw)
    {
        if (raw >= 100) return raw;
        return raw < 30 ? 2000 + raw : 1900 + raw;
    }

    private static (int month, string rest)? ExtractMonthName(string input)
    {
        // Try full names first (longest match)
        for (int i = 0; i < MonthLong.Length; i++)
        {
            var match = Regex.Match(input, MonthLong[i], RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string rest = input.Remove(match.Index, match.Length).Insert(match.Index, " ");
                return (i + 1, rest);
            }
        }
        // Try abbreviated names
        for (int i = 0; i < MonthShort.Length; i++)
        {
            var match = Regex.Match(input, @"\b" + MonthShort[i] + @"\b", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string rest = input.Remove(match.Index, match.Length).Insert(match.Index, " ");
                return (i + 1, rest);
            }
        }
        return null;
    }

    private static int[] ParseInputValues(string input, List<ComponentToken> components)
    {
        bool hasMthName = false;
        foreach (var t in components)
        {
            if (t.Abbrev || t.FullName) { hasMthName = true; break; }
        }

        int? monthFromName = null;
        string workInput = input;

        if (hasMthName)
        {
            var result = ExtractMonthName(input);
            if (result != null)
            {
                monthFromName = result.Value.month;
                workInput = result.Value.rest;
            }
        }

        var digitMatches = Regex.Matches(workInput, @"\d+");
        var digitGroups = new List<string>();
        foreach (Match m in digitMatches)
            digitGroups.Add(m.Value);

        var values = new int[components.Count];
        for (int i = 0; i < values.Length; i++)
            values[i] = int.MinValue; // sentinel for "no value"

        // Compact mode: exactly ONE digit run and more than one component
        bool isCompact = digitGroups.Count == 1
            && digitGroups[0].Length > 1
            && !hasMthName
            && components.Count > 1;

        if (isCompact)
        {
            string run = digitGroups[0];
            int pos = 0;
            for (int ci = 0; ci < components.Count; ci++)
            {
                if (pos >= run.Length) continue;
                int take = components[ci].CompactDigits;
                int end = Math.Min(pos + take, run.Length);
                string slice = run.Substring(pos, end - pos);
                if (slice.Length > 0)
                    values[ci] = int.Parse(slice);
                pos += take;
            }
        }
        else
        {
            int gi = 0;
            for (int ci = 0; ci < components.Count; ci++)
            {
                if (monthFromName != null && (components[ci].Abbrev || components[ci].FullName))
                {
                    values[ci] = monthFromName.Value;
                    monthFromName = null;
                }
                else
                {
                    if (gi < digitGroups.Count)
                    {
                        values[ci] = int.Parse(digitGroups[gi]);
                        gi++;
                    }
                    else
                    {
                        // Not enough digit groups to satisfy all components
                        throw new FormatException($"Not enough values in input to satisfy format components.");
                    }
                }
            }
        }

        return values;
    }

    #endregion
}
