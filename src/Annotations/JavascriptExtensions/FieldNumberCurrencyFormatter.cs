using System;
using System.Globalization;
using System.Text;

namespace Aspose.Pdf.Annotations.JavascriptExtensions;

/// <summary>
/// Formats a numeric string as a currency value.
/// Corresponds to the PDF JavaScript AF_Number_Format function.
/// </summary>
public class FieldNumberCurrencyFormatter
{
    /// <summary>
    /// Format <paramref name="fieldValue"/> as a currency string.
    /// </summary>
    /// <param name="precision">Number of decimal places.</param>
    /// <param name="sepStyle">Thousands/decimal separator style (0-4):
    /// 0 = comma/dot (US), 1 = none/dot, 2 = dot/comma (European), 3 = none/comma, 4 = apostrophe/dot (Swiss).</param>
    /// <param name="negStyle">Negative display style (0-3):
    /// 0 = minus sign, 1 = absolute value, 2 = parentheses, 3 = parentheses.</param>
    /// <param name="currencySymbol">Currency symbol (e.g. "$", "€").</param>
    /// <param name="isPrependCurrency">True to place symbol before number, false to place after.</param>
    /// <param name="fieldValue">Numeric string to format (may start with "-").</param>
    /// <returns>Formatted currency string.</returns>
    public string Format(int precision, int sepStyle, int negStyle, string currencySymbol, bool isPrependCurrency, string fieldValue)
    {
        double num = double.Parse(fieldValue, CultureInfo.InvariantCulture);
        bool isNeg = num < 0;
        double abs = Math.Abs(num);

        string body = FormatAbsNumber(abs, precision, sepStyle);

        // Attach currency symbol
        body = isPrependCurrency ? currencySymbol + body : body + currencySymbol;

        // Apply negative style
        if (isNeg)
        {
            switch (negStyle)
            {
                case 0:
                    body = "-" + body;
                    break;
                case 2:
                case 3:
                    body = "(" + body + ")";
                    break;
                // negStyle 1: absolute value — no sign
            }
        }

        return body;
    }

    internal static string FormatAbsNumber(double abs, int precision, int sepStyle)
    {
        char decimalChar = (sepStyle == 2 || sepStyle == 3) ? ',' : '.';
        string thousandsSep = sepStyle switch
        {
            0 => ",",
            2 => ".",
            4 => "'",
            _ => "" // styles 1 and 3 have no thousands separator
        };

        // Round to the requested precision
        string rounded;
        if (precision > 0)
        {
            rounded = abs.ToString("F" + precision, CultureInfo.InvariantCulture);
        }
        else
        {
            rounded = Math.Round(abs, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture);
        }

        int dotIdx = rounded.IndexOf('.');
        string intPart = dotIdx >= 0 ? rounded.Substring(0, dotIdx) : rounded;
        string fracPart = dotIdx >= 0 ? rounded.Substring(dotIdx + 1) : "";

        // Insert thousands separator into integer part
        var sb = new StringBuilder();
        for (int i = 0; i < intPart.Length; i++)
        {
            int remaining = intPart.Length - i;
            if (i > 0 && remaining % 3 == 0 && thousandsSep.Length > 0)
            {
                sb.Append(thousandsSep);
            }
            sb.Append(intPart[i]);
        }

        if (precision > 0)
        {
            sb.Append(decimalChar);
            sb.Append(fracPart.PadRight(precision, '0'));
        }

        return sb.ToString();
    }
}
