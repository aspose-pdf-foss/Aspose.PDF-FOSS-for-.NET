using System.Collections.Generic;
using System.Text;

namespace Aspose.Pdf.Forms.Xfa;

/// <summary>EAN-13, EAN-8 and UPC-A (the XFA barcode types <c>ean13</c>, <c>ean8</c> and <c>upcA</c>) module
/// layout. A symbol is a guard, the left digits, a centre guard, the right digits and an end guard; every digit is
/// seven modules. In EAN-13 the first digit is carried by the parity pattern of the left six; UPC-A is EAN-13 with
/// a leading zero. The data is digits only; a symbol given without its check digit gets it computed.</summary>
internal static class EanSymbol
{
    private const string EdgeGuard = "101";
    private const string CentreGuard = "01010";
    private const int Ean13Digits = 13;
    private const int Ean8Digits = 8;
    private const int CheckModulus = 10;
    private const int OddPositionWeight = 3;

    /// <summary>The left-hand odd-parity (L) patterns; the right-hand (R) ones are their complements and the
    /// even-parity (G) ones the R ones reversed.</summary>
    private static readonly string[] LeftOdd =
        { "0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011" };

    /// <summary>The parity of the six left digits of an EAN-13 symbol ('G' even) for each first digit.</summary>
    private static readonly string[] FirstDigitParity =
        { "LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLL", "LGLGGL", "LGGLGL" };

    /// <summary>The modules ('1' a bar) of the symbol of the given XFA type, or null when the data is not a run
    /// of digits of the type's length (with or without its check digit).</summary>
    internal static string? Modules(string type, string data)
    {
        if (data.Length == 0 || data.Any(c => c is < '0' or > '9')) return null;
        int digits = type == "ean8" ? Ean8Digits : Ean13Digits;
        // A UPC-A symbol is the EAN-13 symbol of its digits behind a zero.
        if (type == "upcA") data = "0" + data;
        if (data.Length == digits - 1) data += CheckDigit(data);
        if (data.Length != digits) return null;
        var sb = new StringBuilder(EdgeGuard);
        int half = digits == Ean8Digits ? Ean8Digits / 2 : (Ean13Digits - 1) / 2;
        int first = digits == Ean8Digits ? 0 : 1;
        string parity = digits == Ean8Digits ? "LLLL" : FirstDigitParity[data[0] - '0'];
        for (int i = 0; i < half; i++)
            sb.Append(parity[i] == 'G' ? Even(data[first + i] - '0') : LeftOdd[data[first + i] - '0']);
        sb.Append(CentreGuard);
        for (int i = first + half; i < data.Length; i++)
            sb.Append(Right(data[i] - '0'));
        return sb.Append(EdgeGuard).ToString();
    }

    /// <summary>The check digit of the data digits: weights 3 and 1 alternating from the rightmost data digit.</summary>
    private static char CheckDigit(string data)
    {
        int sum = 0;
        for (int i = 0; i < data.Length; i++)
            sum += (data[data.Length - 1 - i] - '0') * (i % 2 == 0 ? OddPositionWeight : 1);
        return (char)('0' + (CheckModulus - sum % CheckModulus) % CheckModulus);
    }

    private static string Right(int digit) =>
        new string(LeftOdd[digit].Select(c => c == '1' ? '0' : '1').ToArray());

    private static string Even(int digit) =>
        new string(Right(digit).Reverse().ToArray());

    /// <summary>The bars of a module string as (left edge, width) pairs in modules.</summary>
    internal static List<(double left, double width)> Bars(string modules)
    {
        var bars = new List<(double, double)>();
        int i = 0;
        while (i < modules.Length)
        {
            if (modules[i] != '1') { i++; continue; }
            int start = i;
            while (i < modules.Length && modules[i] == '1') i++;
            bars.Add((start, i - start));
        }
        return bars;
    }
}
