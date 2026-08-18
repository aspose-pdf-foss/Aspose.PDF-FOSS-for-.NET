namespace Aspose.Pdf.Text;

/// <summary>
/// Minimal Unicode bidi support for right-to-left form values and stamps:
/// converts a LOGICAL-order string (as typed / stored in the PDF value) to
/// VISUAL order (as painted left-to-right by Tj/TJ). Implements the common
/// subset of UAX #9 that covers form-fill text: strong R runs (Hebrew,
/// Arabic and their presentation forms) reverse and re-order right-to-left,
/// strong L runs (Latin letters) and number runs keep their internal
/// left-to-right order, neutrals join the surrounding direction, and paired
/// brackets mirror inside reversed runs.
/// </summary>
internal static class BidiText
{
    internal static bool IsRtl(char c) =>
        (c >= 0x0590 && c <= 0x08FF) || (c >= 0xFB1D && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF);

    internal static bool HasRtl(string s)
    {
        foreach (var c in s)
            if (IsRtl(c)) return true;
        return false;
    }

    private static bool IsStrongL(char c) =>
        char.IsLetter(c) && !IsRtl(c);

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static char Mirror(char c) => c switch
    {
        '(' => ')', ')' => '(',
        '[' => ']', ']' => '[',
        '{' => '}', '}' => '{',
        '<' => '>', '>' => '<',
        _ => c,
    };

    /// <summary>Reorder a single line of logical text into visual order. Lines with
    /// no RTL character pass through unchanged.</summary>
    internal static string ToVisualOrder(string logical)
    {
        if (string.IsNullOrEmpty(logical) || !HasRtl(logical)) return logical;

        // Segment into direction runs: R (strong RTL), L (strong LTR letters or
        // number groups — digits plus separators BETWEEN digits), N (neutrals).
        var runs = new System.Collections.Generic.List<(char kind, int start, int len)>();
        int i = 0, n = logical.Length;
        while (i < n)
        {
            int start = i;
            char c = logical[i];
            if (IsRtl(c))
            {
                while (i < n && (IsRtl(logical[i]) || char.IsSurrogate(logical[i]))) i++;
                runs.Add(('R', start, i - start));
            }
            else if (IsStrongL(c) || IsDigit(c))
            {
                // A left run swallows letters, digits, and separator chars that sit
                // between two digits (dates 14/10/2018, sums 1,464.83, times 13:15).
                while (i < n)
                {
                    char ch = logical[i];
                    if (IsStrongL(ch) || IsDigit(ch)) { i++; continue; }
                    if (ch is '.' or ',' or ':' or '/' or '-'
                        && i > start && IsDigit(logical[i - 1])
                        && i + 1 < n && IsDigit(logical[i + 1])) { i++; continue; }
                    break;
                }
                runs.Add(('L', start, i - start));
            }
            else
            {
                while (i < n && !IsRtl(logical[i]) && !IsStrongL(logical[i]) && !IsDigit(logical[i])) i++;
                runs.Add(('N', start, i - start));
            }
        }

        // Resolve neutrals: a neutral run between equal strong kinds takes that
        // kind; anything else takes the paragraph direction (RTL here — the line
        // contains RTL text and forms use the value's first strong char, which in
        // these documents is RTL when any RTL is present).
        var kinds = new char[runs.Count];
        for (var r = 0; r < runs.Count; r++) kinds[r] = runs[r].kind;
        for (var r = 0; r < runs.Count; r++)
        {
            if (kinds[r] != 'N') continue;
            char prev = 'R', next = 'R';
            for (var p = r - 1; p >= 0; p--) if (kinds[p] != 'N') { prev = kinds[p]; break; }
            for (var q = r + 1; q < runs.Count; q++) if (kinds[q] != 'N') { next = kinds[q]; break; }
            kinds[r] = prev == next ? prev : 'R';
        }

        // Merge adjacent same-kind runs, then emit in reversed run order; R runs
        // additionally reverse their characters (mirroring brackets, keeping
        // surrogate pairs intact).
        var merged = new System.Collections.Generic.List<(char kind, string text)>();
        foreach (var (kind0, run) in System.Linq.Enumerable.Zip(kinds, runs, (k, u) => (k, u)))
        {
            var text = logical.Substring(run.start, run.len);
            if (merged.Count > 0 && merged[^1].kind == kind0)
                merged[^1] = (kind0, merged[^1].text + text);
            else
                merged.Add((kind0, text));
        }

        var sb = new System.Text.StringBuilder(n);
        for (var r = merged.Count - 1; r >= 0; r--)
        {
            var (kind, text) = merged[r];
            if (kind != 'R') { sb.Append(text); continue; }
            for (var k = text.Length - 1; k >= 0; k--)
            {
                if (char.IsLowSurrogate(text[k]) && k > 0 && char.IsHighSurrogate(text[k - 1]))
                {
                    sb.Append(text[k - 1]).Append(text[k]);
                    k--;
                    continue;
                }
                sb.Append(Mirror(text[k]));
            }
        }
        return sb.ToString();
    }
}
