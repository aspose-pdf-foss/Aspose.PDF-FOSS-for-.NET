using System;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Text;

/// <summary>
/// Global configuration for regular-expression text search (for example via
/// <see cref="TextFragmentAbsorber"/>). Lets callers bound how long a single match
/// may run and opt into the non-backtracking engine, which guarantees linear-time
/// matching and avoids catastrophic backtracking on hostile patterns.
/// </summary>
public static class RegexManager
{
    /// <summary>
    /// Maximum time a single regular-expression match may run before a
    /// <see cref="RegexMatchTimeoutException"/> is thrown. Defaults to
    /// <see cref="Regex.InfiniteMatchTimeout"/> (no limit).
    /// </summary>
    public static TimeSpan MatchTimeout { get; set; } = Regex.InfiniteMatchTimeout;

    /// <summary>
    /// When <see langword="true"/>, regex search runs with
    /// <see cref="RegexOptions.NonBacktracking"/>. This trades a few features
    /// (backreferences, atomic groups, look-arounds) for a guarantee of linear-time
    /// matching. Defaults to <see langword="false"/>.
    /// </summary>
    public static bool NonBacktracking { get; set; }
}
