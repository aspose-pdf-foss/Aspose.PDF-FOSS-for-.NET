using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf;

/// <summary>The one match timeout every regular expression of the library's own runs under.</summary>
internal static class RegexLimits
{
    /// <summary>Thirty seconds: only super-linear (catastrophic) backtracking gets there. A linear scan of a
    /// real document stays far below it - the longest measured, one lazy match over a 101 MB style block of
    /// embedded fonts (a fixed-layout HTML export read back), takes 2 s on an idle machine; a 2 s limit cut it
    /// short under load and dropped the fonts. In milliseconds for [GeneratedRegex], whose arguments must be
    /// constants.</summary>
    public const int TimeoutMilliseconds = 30000;

    /// <summary><see cref="TimeoutMilliseconds"/> as the TimeSpan the Regex API takes.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(TimeoutMilliseconds);
}

/// <summary>
/// The <see cref="Regex"/> calls of the library's own patterns (heuristics over markup, CSS, content streams),
/// run under <see cref="RegexLimits.Timeout"/>. A match that times out answers "no match" - IsMatch false,
/// <see cref="Match.Empty"/>, no further matches, the input unchanged - and the caller carries on, as it
/// does for any input the heuristic does not recognise. A pattern the CALLER supplied (regex text search and
/// replace) does not come through here: its <see cref="RegexMatchTimeoutException"/> reaches the caller.
/// The static calls match case-insensitively by the invariant culture's rules, as machine text is read:
/// under a Turkish culture a culture-aware match takes 'I' for no 'i', and "&lt;SCRIPT" for no "&lt;script".
/// </summary>
internal static class SafeRegex
{
    // Each name: the static call (pattern + options) first, then the calls on a Regex built with
    // RegexLimits.Timeout - those mirror their Regex counterpart exactly: the start-less ones start where
    // the Regex decides (the end, for RightToLeft).

    /// <summary>A pattern applied again to an input this long runs compiled: the interpreter walks
    /// a multi-megabyte document several times slower than compiled code, and the compile is paid
    /// once per pattern. Its first application is interpreted, so a pattern built for one element
    /// (a name interpolated into it) is never compiled. Shorter inputs keep the static calls and
    /// the Regex cache.</summary>
    private const int LongInputChars = 64 * 1024;

    /// <summary>The patterns remembered, compiled or seen once; past this many the set starts again.</summary>
    private const int PatternsKept = 512;

    private static readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), Regex?> Compiled = new();

    private static Regex? CompiledFor(string input, string pattern, RegexOptions options)
    {
        if (input.Length < LongInputChars) return null;
        var key = (pattern, options);
        if (Compiled.TryGetValue(key, out var regex))
            return regex ?? Compiled.AddOrUpdate(key, _ => null,
                (k, seen) => seen ?? new Regex(k.Pattern, k.Options | RegexOptions.Compiled, RegexLimits.Timeout));
        if (Compiled.Count >= PatternsKept) Compiled.Clear();
        Compiled.TryAdd(key, null);
        return null;
    }

    public static bool IsMatch(string input, string pattern, RegexOptions options = RegexOptions.None)
    {
        options |= RegexOptions.CultureInvariant;
        if (CompiledFor(input, pattern, options) is { } compiled) return IsMatch(compiled, input);
        try { return Regex.IsMatch(input, pattern, options, RegexLimits.Timeout); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    public static bool IsMatch(Regex regex, string input)
    {
        try { return regex.IsMatch(input); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    public static bool IsMatch(Regex regex, string input, int startat)
    {
        try { return regex.IsMatch(input, startat); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    public static Match Match(string input, string pattern, RegexOptions options = RegexOptions.None)
    {
        options |= RegexOptions.CultureInvariant;
        if (CompiledFor(input, pattern, options) is { } compiled) return Match(compiled, input);
        try { return Regex.Match(input, pattern, options, RegexLimits.Timeout); }
        catch (RegexMatchTimeoutException) { return System.Text.RegularExpressions.Match.Empty; }
    }

    public static Match Match(Regex regex, string input)
    {
        try { return regex.Match(input); }
        catch (RegexMatchTimeoutException) { return System.Text.RegularExpressions.Match.Empty; }
    }

    public static Match Match(Regex regex, string input, int startat)
    {
        try { return regex.Match(input, startat); }
        catch (RegexMatchTimeoutException) { return System.Text.RegularExpressions.Match.Empty; }
    }

    public static Match Match(Regex regex, string input, int beginning, int length)
    {
        try { return regex.Match(input, beginning, length); }
        catch (RegexMatchTimeoutException) { return System.Text.RegularExpressions.Match.Empty; }
    }

    public static SafeMatchCollection Matches(string input, string pattern, RegexOptions options = RegexOptions.None)
    {
        options |= RegexOptions.CultureInvariant;
        if (CompiledFor(input, pattern, options) is { } compiled) return Matches(compiled, input);
        return new SafeMatchCollection(Regex.Matches(input, pattern, options, RegexLimits.Timeout));
    }

    public static SafeMatchCollection Matches(Regex regex, string input)
    {
        return new SafeMatchCollection(regex.Matches(input));
    }

    public static SafeMatchCollection Matches(Regex regex, string input, int startat)
    {
        return new SafeMatchCollection(regex.Matches(input, startat));
    }

    public static string Replace(string input, string pattern, string replacement, RegexOptions options = RegexOptions.None)
    {
        options |= RegexOptions.CultureInvariant;
        if (CompiledFor(input, pattern, options) is { } compiled) return Replace(compiled, input, replacement);
        try { return Regex.Replace(input, pattern, replacement, options, RegexLimits.Timeout); }
        catch (RegexMatchTimeoutException) { return input; }
    }

    public static string Replace(string input, string pattern, MatchEvaluator evaluator, RegexOptions options = RegexOptions.None)
    {
        options |= RegexOptions.CultureInvariant;
        if (CompiledFor(input, pattern, options) is { } compiled) return Replace(compiled, input, evaluator);
        try { return Regex.Replace(input, pattern, evaluator, options, RegexLimits.Timeout); }
        catch (RegexMatchTimeoutException) { return input; }
    }

    public static string Replace(Regex regex, string input, string replacement)
    {
        try { return regex.Replace(input, replacement); }
        catch (RegexMatchTimeoutException) { return input; }
    }

    public static string Replace(Regex regex, string input, string replacement, int count)
    {
        try { return regex.Replace(input, replacement, count); }
        catch (RegexMatchTimeoutException) { return input; }
    }

    public static string Replace(Regex regex, string input, MatchEvaluator evaluator)
    {
        try { return regex.Replace(input, evaluator); }
        catch (RegexMatchTimeoutException) { return input; }
    }

    public static string Replace(Regex regex, string input, MatchEvaluator evaluator, int count)
    {
        try { return regex.Replace(input, evaluator, count); }
        catch (RegexMatchTimeoutException) { return input; }
    }

    public static string[] Split(string input, string pattern, RegexOptions options = RegexOptions.None)
    {
        options |= RegexOptions.CultureInvariant;
        if (CompiledFor(input, pattern, options) is { } compiled) return Split(compiled, input);
        try { return Regex.Split(input, pattern, options, RegexLimits.Timeout); }
        catch (RegexMatchTimeoutException) { return new[] { input }; }
    }

    public static string[] Split(Regex regex, string input)
    {
        try { return regex.Split(input); }
        catch (RegexMatchTimeoutException) { return new[] { input }; }
    }

    /// <summary>The next match after <paramref name="match"/>; <see cref="Match.Empty"/> on a timeout.</summary>
    public static Match NextMatch(Match match)
    {
        try { return match.NextMatch(); }
        catch (RegexMatchTimeoutException) { return System.Text.RegularExpressions.Match.Empty; }
    }
}

/// <summary>
/// A <see cref="MatchCollection"/> that stays lazy - a loop that stops at its first hit runs no further
/// matches - and ends where a match times out: enumeration stops there, and from then on the collection
/// counts as empty.
/// </summary>
internal sealed class SafeMatchCollection : IReadOnlyList<Match>
{
    private readonly MatchCollection _all;
    private bool _timedOut;

    public SafeMatchCollection(MatchCollection all) => _all = all;

    public int Count
    {
        get
        {
            if (_timedOut) return 0;
            try { return _all.Count; }
            catch (RegexMatchTimeoutException) { _timedOut = true; return 0; }
        }
    }

    public Match this[int i]
    {
        get
        {
            if (_timedOut) throw new ArgumentOutOfRangeException(nameof(i));
            try { return _all[i]; }
            catch (RegexMatchTimeoutException) { _timedOut = true; throw new ArgumentOutOfRangeException(nameof(i)); }
        }
    }

    public IEnumerator<Match> GetEnumerator()
    {
        if (_timedOut) yield break;
        var e = ((IEnumerable)_all).GetEnumerator();
        while (true)
        {
            bool more;
            try { more = e.MoveNext(); }
            catch (RegexMatchTimeoutException) { _timedOut = true; yield break; }
            if (!more) yield break;
            yield return (Match)e.Current;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
