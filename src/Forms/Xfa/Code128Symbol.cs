using System.Collections.Generic;

namespace Aspose.Pdf.Forms.Xfa;

/// <summary>Code 128 (the XFA barcode types <c>code128A</c>, <c>code128B</c>, <c>code128C</c> and
/// the plain <c>code128</c>) bar layout. A symbol is a start character that names the code set, the
/// data characters, a check character and the stop character; every character but the stop is three
/// bars and three spaces filling eleven modules, the stop four bars and three spaces filling thirteen.
/// Widths are in modules, so the caller scales the symbol to its field by choosing the module.</summary>
internal static class Code128Symbol
{
    private const int StartA = 103, StartB = 104, StartC = 105, Stop = 106;
    private const int CheckModulus = 103;
    private const int FirstPrintable = 32, FirstLowerCase = 96, SetAControlShift = 64, LastSevenBit = 127;
    /// <summary>The quiet zone each side of the symbol, in modules.</summary>
    internal const int QuietModules = 10;

    /// <summary>Element widths of each symbol value, bar first, bars and spaces alternating.</summary>
    internal static readonly string[] Patterns =
    {
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    };

    /// <summary>The symbol values of the data in one code set, framed by its start, check and stop
    /// characters. <paramref name="set"/> is 'A', 'B' or 'C'; any other value picks C for an even
    /// run of digits and B otherwise. A character the set cannot carry is left out; set C given an
    /// odd count of digits, or anything but digits, is encoded in set B.</summary>
    internal static List<int> Encode(string data, char set)
    {
        bool digitPairs = data.Length > 0 && data.Length % 2 == 0 && data.All(c => c >= '0' && c <= '9');
        if (set == 'C' && !digitPairs) set = 'B';
        if (set is not ('A' or 'B' or 'C')) set = digitPairs ? 'C' : 'B';
        var values = new List<int> { set switch { 'A' => StartA, 'C' => StartC, _ => StartB } };
        if (set == 'C')
        {
            for (int i = 0; i < data.Length; i += 2) values.Add((data[i] - '0') * 10 + (data[i + 1] - '0'));
        }
        else
        {
            foreach (char c in data)
            {
                if (set == 'B' && c >= FirstPrintable && c <= LastSevenBit) values.Add(c - FirstPrintable);
                else if (set == 'A' && c >= FirstPrintable && c < FirstLowerCase) values.Add(c - FirstPrintable);
                else if (set == 'A' && c < FirstPrintable) values.Add(c + SetAControlShift);
            }
        }
        // The check character weighs each data character by its position; the start character counts once.
        int sum = values[0];
        for (int i = 1; i < values.Count; i++) sum += values[i] * i;
        values.Add(sum % CheckModulus);
        values.Add(Stop);
        return values;
    }

    /// <summary>Total width of an encoded symbol in modules, without quiet zones.</summary>
    internal static int TotalModules(List<int> values)
    {
        int total = 0;
        foreach (int v in values)
            foreach (char element in Patterns[v]) total += element - '0';
        return total;
    }

    /// <summary>The bars of an encoded symbol as (left edge, width) pairs in modules.</summary>
    internal static List<(double left, double width)> Bars(List<int> values)
    {
        var bars = new List<(double, double)>();
        double x = 0;
        foreach (int v in values)
        {
            string pattern = Patterns[v];
            for (int i = 0; i < pattern.Length; i++)
            {
                int width = pattern[i] - '0';
                if ((i & 1) == 0) bars.Add((x, width));
                x += width;
            }
        }
        return bars;
    }
}
