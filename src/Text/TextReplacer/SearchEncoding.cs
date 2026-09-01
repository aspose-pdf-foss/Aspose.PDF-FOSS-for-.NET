using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    private static string DecodeString(byte[] bytes, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict, PdfReader? reader = null)
    {
        // Delegate to TextAbsorber for consistent decoding (handles /Differences, named encodings, etc.)
        if (reader is not null)
            return TextAbsorber.DecodeStringPublic(bytes, toUnicode, fontDict, reader);

        if (toUnicode is not null)
        {
            var isCid = fontDict?.GetName("Subtype") == "Type0";
            var sb = new StringBuilder();
            if (isCid && bytes.Length >= 2)
            {
                for (var i = 0; i + 1 < bytes.Length; i += 2)
                {
                    var code = (bytes[i] << 8) | bytes[i + 1];
                    sb.Append(toUnicode.TryGetValue(code, out var mapped) ? mapped : "\uFFFD");
                }
            }
            else
            {
                foreach (var b in bytes)
                    sb.Append(toUnicode.TryGetValue(b, out var mapped) ? mapped : ((char)b).ToString());
            }
            return sb.ToString();
        }

        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    /// Normalize text for search comparison: apply NFKD decomposition to map
    /// Arabic presentation forms to base characters, matching TextFragmentAbsorber behavior.
    /// </summary>
    private static string NormalizeForSearch(string text)
    {
        bool hasPresentationForms = false;
        foreach (var ch in text)
        {
            if ((ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF'))
            {
                hasPresentationForms = true;
                break;
            }
        }
        if (!hasPresentationForms) return text;
        return text.Normalize(System.Text.NormalizationForm.FormKD);
    }

    /// <summary>True when <paramref name="s"/> contains a right-to-left script
    /// character (Hebrew / Arabic + presentation forms). Such text is frequently
    /// stored in the content stream in VISUAL (reversed) order, so a logical-order
    /// search term won't match the decoded run directly.</summary>
    private static bool IsRtlSearch(string s)
    {
        foreach (var c in s)
            if ((c >= '֐' && c <= '׿')   // Hebrew
                || (c >= '؀' && c <= 'ۿ') // Arabic
                || (c >= 'יִ' && c <= '﻿')) // Hebrew/Arabic presentation forms
                return true;
        return false;
    }

    /// <summary>Resolve the search variant actually present in <paramref name="runText"/>.
    /// Returns the original <paramref name="search"/> when it matches directly; for an
    /// RTL term that doesn't (the run is stored visually reversed) returns the reversed
    /// term when THAT is present, so the visual slice can be matched and replaced.
    /// Regex searches are returned unchanged (RTL-regex is not modelled).</summary>
    private string ResolveRtlSearch(string runText, string search)
    {
        if (_isRegex || string.IsNullOrEmpty(search)) return search;
        if (runText.Contains(search, StringComparison.Ordinal)) return search;
        if (IsRtlSearch(search))
        {
            var rev = new string(search.Reverse().ToArray());
            if (runText.Contains(rev, StringComparison.Ordinal)) return rev;
        }
        return search;
    }

    /// <summary>Check if <paramref name="text"/> contains a match for the current search.</summary>
    private bool MatchesSearch(string text, string normalizedSearch)
    {
        if (ReplaceFirstOnly && _replacementCount > 0) return false;
        if (MatchAnyOperator) return true;
        if (MatchWholeOperator)
            return string.Equals(text, normalizedSearch, StringComparison.Ordinal);
        if (_isRegex && _regexPattern is not null)
            return _regexPattern.IsMatch(text);
        return text.Contains(normalizedSearch, StringComparison.Ordinal);
    }

    private static byte[] EncodeString(string text, Dictionary<int, string>? toUnicode,
        PdfDictionary? fontDict)
    {
        if (toUnicode is not null)
        {
            // Build reverse map with NFKD fallback for Arabic presentation forms
            var reverseMap = BuildReverseMap(toUnicode);

            // If the font stores Arabic presentation forms in its ToUnicode map,
            // the content stream uses visual (reversed) order for RTL text.
            // Reverse RTL replacement text to match this visual-order convention.
            if (HasArabicPresentationForms(toUnicode) && IsArabicText(text))
            {
                var chars = text.ToCharArray();
                Array.Reverse(chars);
                text = new string(chars);
            }

            var isCid = fontDict?.GetName("Subtype") == "Type0";
            var result = new List<byte>();

            foreach (var ch in text)
            {
                var s = ch.ToString();
                if (reverseMap.TryGetValue(s, out var code))
                {
                    if (isCid)
                    {
                        result.Add((byte)((code >> 8) & 0xFF));
                        result.Add((byte)(code & 0xFF));
                    }
                    else
                    {
                        result.Add((byte)(code & 0xFF));
                    }
                }
                else
                {
                    // Fallback: use character value directly
                    if (isCid)
                    {
                        result.Add((byte)((ch >> 8) & 0xFF));
                        result.Add((byte)(ch & 0xFF));
                    }
                    else
                    {
                        result.Add((byte)ch);
                    }
                }
            }

            return result.ToArray();
        }

        return Encoding.Latin1.GetBytes(text);
    }

    /// <summary>
    /// Check if a ToUnicode map contains Arabic presentation form characters,
    /// indicating the font stores RTL text in visual (reversed) order.
    /// </summary>
    private static bool HasArabicPresentationForms(Dictionary<int, string> toUnicode)
    {
        foreach (var unicode in toUnicode.Values)
        {
            if (unicode.Length != 1) continue;
            var ch = unicode[0];
            if ((ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF'))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if text contains Arabic/Hebrew characters that would be rendered RTL.
    /// </summary>
    private static bool IsArabicText(string text)
    {
        foreach (var ch in text)
        {
            // Arabic block (U+0600-U+06FF), Arabic Supplement (U+0750-U+077F),
            // Arabic Extended (U+08A0-U+08FF), Arabic Presentation Forms
            if ((ch >= '\u0600' && ch <= '\u06FF') || (ch >= '\u0750' && ch <= '\u077F') ||
                (ch >= '\u08A0' && ch <= '\u08FF') ||
                (ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF'))
                return true;
        }
        return false;
    }

    private static void WriteStringOperand(MemoryStream ms, byte[] data, bool isHex)
    {
        if (isHex)
        {
            ms.WriteByte((byte)'<');
            ms.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(data)));
            ms.WriteByte((byte)'>');
        }
        else
        {
            ms.WriteByte((byte)'(');
            foreach (var b in data)
            {
                if (b == '(' || b == ')' || b == '\\')
                {
                    ms.WriteByte((byte)'\\');
                    ms.WriteByte(b);
                }
                else if (b == 0x0D) // CR — escape to prevent PdfLexer CR→LF normalization
                {
                    ms.WriteByte((byte)'\\');
                    ms.WriteByte((byte)'r');
                }
                else if (b == 0x0A) // LF
                {
                    ms.WriteByte((byte)'\\');
                    ms.WriteByte((byte)'n');
                }
                else
                {
                    ms.WriteByte(b);
                }
            }
            ms.WriteByte((byte)')');
        }
    }

    private static void WriteTJArray(MemoryStream ms, PdfArray arr)
    {
        ms.WriteByte((byte)'[');
        for (var i = 0; i < arr.Count; i++)
        {
            if (i > 0) ms.WriteByte((byte)' ');
            switch (arr[i])
            {
                case PdfString s:
                    WriteStringOperand(ms, s.Value, s.IsHex);
                    break;
                case PdfInteger n:
                    ms.Write(Encoding.ASCII.GetBytes(n.Value.ToString()));
                    break;
                case PdfReal r:
                    ms.Write(Encoding.ASCII.GetBytes(r.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)));
                    break;
            }
        }
        ms.WriteByte((byte)']');
    }

    private static int CountOccurrences(string text, string search)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(search, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += search.Length;
        }
        return count;
    }
}
