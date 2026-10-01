using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // A bold line at body size reads as a heading when it is no longer than this.
    private const int MaxBoldHeadingChars = 80;
    // An outline title matches a heading line that starts it with at least this many characters
    // (a heading wrapped over lines matches its first line).
    private const int MinOutlinePrefix = 8;

    /// <summary>How a line is recognised as a heading, from <see cref="AutoTaggingSettings"/>:
    /// by size above the body text (the default), with explicit size thresholds
    /// (<see cref="AutoTaggingSettings.HeadingLevels"/>), also by weight (a short bold line at
    /// body size), from the document's outlines, or not at all.</summary>
    private sealed class HeadingRule
    {
        public bool None;
        public double BodySize;
        public List<double> Sizes = [];      // the document's heading sizes, largest first
        public HeadingLevels? Levels;        // explicit size thresholds, when given
        public bool ByWeight;
        public Dictionary<string, (int Level, int Page)>? Outline; // outline title (normalised) → level and the page it leads to (0: unknown)
        public bool OutlineOnly;             // the outline alone says what is a heading

        // The rest of an outline title a line on this page began, and its level: the next line may go on with it.
        private (string Left, int Level, int Page)? _goingOn;

        /// <summary>The heading level of a line (1..6) on page <paramref name="page"/>, or 0 for body text.</summary>
        public int LevelOf(Line line, int page)
        {
            if (None || line.Frags.Count == 0) return 0;
            // A line the document's outline names is a heading at the outline's level; with the outline alone, no other is.
            if (Outline is not null)
            {
                var named = OutlineLevel(line, page);
                if (named > 0 || OutlineOnly) return named;
            }
            var size = Math.Round(line.Size, 1);
            if (size > BodySize + 0.5)
            {
                if (Levels is { AllLevels.Count: > 0 }) return Math.Min(Levels.EstimateLevel(size), 6);
                var idx = Sizes.IndexOf(size);
                if (idx >= 0) return Math.Min(idx + 1, 6);
            }
            if (ByWeight && IsBoldHeadingLine(line)) return Math.Min(Sizes.Count + 1, 6);
            return 0;
        }

        /// <summary>The level of the outline title a line is, or begins (a title wrapped over lines), or goes on with from
        /// the line before; 0 for none. A title is matched on the page its outline item leads to, where that is known: a
        /// table of contents names the titles too, on its own pages.</summary>
        private int OutlineLevel(Line line, int page)
        {
            // (read without its spaces: a heading's number and its title stand apart as pieces of their own)
            var text = NormTitle(string.Concat(line.Frags.Select(f => f.Text)));
            var goingOn = _goingOn;
            _goingOn = null;
            if (text.Length == 0) return 0;
            if (goingOn is { } rest && rest.Page == page && rest.Left.StartsWith(text, StringComparison.Ordinal))
            {
                if (rest.Left.Length > text.Length) _goingOn = (rest.Left.Substring(text.Length), rest.Level, page);
                return rest.Level;
            }
            bool OnItsPage(int target) => target == 0 || target == page;
            if (Outline!.TryGetValue(text, out var item) && OnItsPage(item.Page)) return Math.Min(item.Level, 6);
            foreach (var (title, (level, target)) in Outline)
                if (text.Length >= MinOutlinePrefix && OnItsPage(target) && title.StartsWith(text, StringComparison.Ordinal))
                {
                    _goingOn = (title.Substring(text.Length), Math.Min(level, 6), page);
                    return Math.Min(level, 6);
                }
            return 0;
        }

        private bool IsBoldHeadingLine(Line line)
        {
            if (Math.Abs(line.Size - BodySize) > 0.6) return false;
            if (!line.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)).All(f => IsBold(f.Font))) return false;
            var text = string.Concat(line.Frags.Select(f => f.Text)).Trim();
            return text.Length > 0 && text.Length <= MaxBoldHeadingChars && ",;:".IndexOf(text[^1]) < 0
                   && text.Any(char.IsLetterOrDigit);
        }
    }

    private static bool IsBold(string? font) =>
        font is not null && (font.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0
                             || font.IndexOf("Black", StringComparison.OrdinalIgnoreCase) >= 0
                             || font.IndexOf("Heavy", StringComparison.OrdinalIgnoreCase) >= 0);

    private static readonly Regex TitleSpace = new(@"\s+", RegexOptions.Compiled);

    private static string NormTitle(string s) => TitleSpace.Replace(s, string.Empty).ToLowerInvariant();

    private static HeadingRule MakeHeadingRule(Document document, AutoTaggingSettings settings, double bodySize, List<double> sizes)
    {
        var rule = new HeadingRule { BodySize = bodySize, Sizes = sizes };
        if (settings.HeadingLevels is { AllLevels.Count: > 0 } levels) rule.Levels = levels;
        switch (settings.HeadingRecognitionStrategy)
        {
            case HeadingRecognitionStrategy.None:
                rule.None = true;
                break;
            case HeadingRecognitionStrategy.FontWeight:
            case HeadingRecognitionStrategy.Heuristic:
                rule.ByWeight = true;
                break;
            case HeadingRecognitionStrategy.Auto:
                // The outline the document carries names its headings and their levels, whatever their size or weight;
                // the lines it does not name are read by size and weight.
                rule.ByWeight = true;
                rule.Outline = OutlineOf(document);
                break;
            case HeadingRecognitionStrategy.Outlines:
                rule.Outline = OutlineOf(document);
                rule.OutlineOnly = rule.Outline is not null;
                break;
        }
        return rule;
    }

    /// <summary>The document's outline titles (read without their spaces) and their levels; null when it has none.</summary>
    private static Dictionary<string, (int Level, int Page)>? OutlineOf(Document document)
    {
        var outline = new Dictionary<string, (int Level, int Page)>();
        try { CollectOutline(document.Outlines, 1, outline); } catch { /* unreadable outlines: none */ }
        return outline.Count > 0 ? outline : null;
    }

    private static void CollectOutline(IEnumerable<OutlineItemCollection> items, int level, Dictionary<string, (int Level, int Page)> into)
    {
        foreach (var item in items)
        {
            var title = NormTitle(item.Title ?? string.Empty);
            if (title.Length > 0 && !into.ContainsKey(title)) into[title] = (level, TargetPage(item));
            CollectOutline(item, level + 1, into);
        }
    }

    /// <summary>The page an outline item leads to; 0 when it names none (a named destination, another action).</summary>
    private static int TargetPage(OutlineItemCollection item)
        => (item.Destination ?? (item.Action as Annotations.GoToAction)?.Destination) is Annotations.ExplicitDestination { PageNumber: > 0 } d ? d.PageNumber : 0;
}
