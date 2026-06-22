using System.Globalization;

namespace Aspose.Pdf.Annotations.JavascriptExtensions;

/// <summary>
/// Formats a numeric string as a percentage (multiplies by 100 first).
/// Corresponds to the PDF JavaScript AF_Percent_Format function.
/// </summary>
public class FieldNumberPercentFormatter
{
    /// <summary>
    /// Format <paramref name="fieldValue"/> as a percentage string.
    /// </summary>
    /// <param name="precision">Number of decimal places.</param>
    /// <param name="sepStyle">Thousands/decimal separator style (0-4).</param>
    /// <param name="isPrependPercent">True to place "%" before number, false to place after.</param>
    /// <param name="fieldValue">Numeric string (will be multiplied by 100).</param>
    /// <returns>Formatted percentage string.</returns>
    public string Format(int precision, int sepStyle, bool isPrependPercent, string fieldValue)
    {
        double num = double.Parse(fieldValue, CultureInfo.InvariantCulture) * 100;
        bool isNeg = num < 0;
        double abs = System.Math.Abs(num);

        string body = FieldNumberCurrencyFormatter.FormatAbsNumber(abs, precision, sepStyle);

        if (isPrependPercent)
        {
            return isNeg ? "%-" + body : "%" + body;
        }
        else
        {
            return isNeg ? "-" + body + "%" : body + "%";
        }
    }
}
