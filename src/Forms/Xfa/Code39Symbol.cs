using System.Collections.Generic;
using System.Text;

namespace Aspose.Pdf.Forms.Xfa;

/// <summary>Code 39 (the XFA barcode type <c>code3Of9</c>) bar layout. Every symbol
/// character is nine elements — five bars and four spaces, alternating, three of them
/// wide — followed by one narrow inter-character gap; the data is framed by the '*'
/// start/stop character. Widths are in narrow-module units, so the caller scales the
/// symbol to its field by choosing the module.</summary>
internal static class Code39Symbol
{
    private const int ElementsPerCharacter = 9;
    private const int WideElementsPerCharacter = 3;
    private const char FrameCharacter = '*';

    /// <summary>Bars ('b') and spaces ('s') alternate; 'w' marks the wide elements.</summary>
    private static readonly Dictionary<char, string> Patterns = new()
    {
        ['0'] = "nnnwwnwnn", ['1'] = "wnnwnnnnw", ['2'] = "nnwwnnnnw", ['3'] = "wnwwnnnnn",
        ['4'] = "nnnwwnnnw", ['5'] = "wnnwwnnnn", ['6'] = "nnwwwnnnn", ['7'] = "nnnwnnwnw",
        ['8'] = "wnnwnnwnn", ['9'] = "nnwwnnwnn", ['A'] = "wnnnnwnnw", ['B'] = "nnwnnwnnw",
        ['C'] = "wnwnnwnnn", ['D'] = "nnnnwwnnw", ['E'] = "wnnnwwnnn", ['F'] = "nnwnwwnnn",
        ['G'] = "nnnnnwwnw", ['H'] = "wnnnnwwnn", ['I'] = "nnwnnwwnn", ['J'] = "nnnnwwwnn",
        ['K'] = "wnnnnnnww", ['L'] = "nnwnnnnww", ['M'] = "wnwnnnnwn", ['N'] = "nnnnwnnww",
        ['O'] = "wnnnwnnwn", ['P'] = "nnwnwnnwn", ['Q'] = "nnnnnnwww", ['R'] = "wnnnnnwwn",
        ['S'] = "nnwnnnwwn", ['T'] = "nnnnwnwwn", ['U'] = "wwnnnnnnw", ['V'] = "nwwnnnnnw",
        ['W'] = "wwwnnnnnn", ['X'] = "nwnnwnnnw", ['Y'] = "wwnnwnnnn", ['Z'] = "nwwnwnnnn",
        ['-'] = "nwnnnnwnw", ['.'] = "wwnnnnwnn", [' '] = "nwwnnnwnn", ['$'] = "nwnwnwnnn",
        ['/'] = "nwnwnnnwn", ['+'] = "nwnnnwnwn", ['%'] = "nnnwnwnwn", ['*'] = "nwnnwnwnn",
    };

    /// <summary>Narrow modules one character occupies at the given wide:narrow ratio,
    /// counting the gap that follows it (after the stop character that gap is the
    /// trailing quiet zone).</summary>
    internal static double ModulesPerCharacter(double wideNarrowRatio) =>
        WideElementsPerCharacter * wideNarrowRatio + (ElementsPerCharacter - WideElementsPerCharacter) + 1;

    /// <summary>The data as the symbol carries it: upper-cased, characters outside the
    /// Code 39 set dropped, framed by the start/stop character.</summary>
    internal static string Frame(string data)
    {
        var sb = new StringBuilder(data.Length + 2).Append(FrameCharacter);
        foreach (char c in data.ToUpperInvariant())
            if (c != FrameCharacter && Patterns.ContainsKey(c)) sb.Append(c);
        return sb.Append(FrameCharacter).ToString();
    }

    /// <summary>Total width of a framed symbol in narrow modules.</summary>
    internal static double TotalModules(string framed, double wideNarrowRatio) =>
        framed.Length * ModulesPerCharacter(wideNarrowRatio);

    /// <summary>The bars of a framed symbol as (left edge, width) pairs in narrow modules.</summary>
    internal static List<(double left, double width)> Bars(string framed, double wideNarrowRatio)
    {
        var bars = new List<(double, double)>();
        double x = 0;
        foreach (char c in framed)
        {
            string pattern = Patterns[c];
            for (int i = 0; i < pattern.Length; i++)
            {
                double width = pattern[i] == 'w' ? wideNarrowRatio : 1;
                if ((i & 1) == 0) bars.Add((x, width));
                x += width;
            }
            x += 1;
        }
        return bars;
    }
}
