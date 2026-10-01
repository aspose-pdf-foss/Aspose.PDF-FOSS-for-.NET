using System;
using System.Collections.Generic;
using System.Linq;

namespace Aspose.Pdf;

/// <summary>How a grid track's minimum or maximum is sized (CSS Grid Layout 7.2).</summary>
internal enum GridSizing
{
    Fixed,
    Auto,
    MinContent,
    MaxContent,
    /// <summary>A share of the free space (fr), maximum only.</summary>
    Flex,
    /// <summary>The content's size up to a limit (fit-content()), maximum only.</summary>
    FitContent,
}

/// <summary>One grid track: its minimum and maximum sizing functions, and, once sized, its size.</summary>
internal sealed class GridTrack
{
    public GridSizing MinSizing = GridSizing.Auto;
    public float MinValue;
    public GridSizing MaxSizing = GridSizing.Auto;
    /// <summary>The fixed maximum, the fr factor, or the fit-content limit.</summary>
    public float MaxValue;

    public float Base;
    public float Limit;
    internal bool InfinitelyGrowable;

    public static GridTrack Of(GridSizing sizing, float value = 0) => sizing switch
    {
        GridSizing.Flex => new GridTrack { MinSizing = GridSizing.Auto, MaxSizing = GridSizing.Flex, MaxValue = value },
        GridSizing.FitContent => new GridTrack { MinSizing = GridSizing.Auto, MaxSizing = GridSizing.FitContent, MaxValue = value },
        _ => new GridTrack { MinSizing = sizing, MinValue = value, MaxSizing = sizing, MaxValue = value },
    };

    internal bool IsFlexible => MaxSizing == GridSizing.Flex;
    internal bool HasIntrinsicMin => MinSizing is GridSizing.Auto or GridSizing.MinContent or GridSizing.MaxContent;
    internal bool HasIntrinsicMax => MaxSizing is GridSizing.Auto or GridSizing.MinContent or GridSizing.MaxContent or GridSizing.FitContent;
}

/// <summary>What an item asks of the tracks it spans on one axis: its span and its content sizes
/// (outer: margins, borders and paddings included).</summary>
internal readonly record struct GridContribution(int Start, int Span, float MinContent, float MaxContent);

/// <summary>Where an item asks to go on one axis: 1-based lines, negative ones counted back
/// from the end of the explicit grid (-1 the last line), or none; and a span.</summary>
internal readonly record struct GridLinePlacement(int? Start, int? End, int? Span)
{
    internal bool IsDefinite => Start is not null || End is not null;
}

/// <summary>
/// The grid layout algorithm of CSS Grid Layout Module Level 1 on numbers alone: placing items in
/// the grid (section 8.5, sparse or dense) and sizing the tracks of an axis (section 11: intrinsic
/// sizes, spanning items, maximizing, flexible tracks, stretching auto tracks). The caller measures
/// the items and places the boxes.
/// </summary>
internal static class GridLayout
{
    private const float Epsilon = 1e-4f;

    /// <summary>Places the items (8.5): those with both positions first, then those locked to a row,
    /// then the rest with the auto-placement cursor. In column flow the axes swap. Answers each
    /// item's 0-based column, row and spans, and the column and row counts of the grid.</summary>
    public static (int Column, int ColumnSpan, int Row, int RowSpan)[] Place(IReadOnlyList<(GridLinePlacement Column, GridLinePlacement Row)> items,
        int explicitColumns, int explicitRows, bool columnFlow, bool dense, out int columns, out int rows)
    {
        // In column flow the "row" of the algorithm is the column: swap on the way in and out.
        var major = items.Select(i => columnFlow ? i.Column : i.Row).ToList();
        var minor = items.Select(i => columnFlow ? i.Row : i.Column).ToList();
        var explicitMinor = columnFlow ? explicitRows : explicitColumns;
        var explicitMajor = columnFlow ? explicitColumns : explicitRows;
        var placed = new (int Minor, int MinorSpan, int Major, int MajorSpan)[items.Count];
        var resolvedMinor = minor.Select(p => Resolve(p, explicitMinor)).ToList();
        var resolvedMajor = major.Select(p => Resolve(p, explicitMajor)).ToList();
        var minorCount = Math.Max(1, explicitMinor);
        foreach (var r in resolvedMinor) minorCount = Math.Max(minorCount, (r.Start ?? 0) + r.Span);
        var occupied = new HashSet<(int, int)>();
        bool Free(int minorStart, int minorSpan, int majorStart, int majorSpan)
        {
            if (minorStart < 0 || minorStart + minorSpan > minorCount) return false;
            for (var a = majorStart; a < majorStart + majorSpan; a++)
                for (var b = minorStart; b < minorStart + minorSpan; b++)
                    if (occupied.Contains((a, b))) return false;
            return true;
        }
        void Occupy(int index, int minorStart, int minorSpan, int majorStart, int majorSpan)
        {
            placed[index] = (minorStart, minorSpan, majorStart, majorSpan);
            for (var a = majorStart; a < majorStart + majorSpan; a++)
                for (var b = minorStart; b < minorStart + minorSpan; b++)
                    occupied.Add((a, b));
        }
        var done = new bool[items.Count];
        // 1. Both positions given.
        for (var i = 0; i < items.Count; i++)
            if (resolvedMinor[i].Start is { } m && resolvedMajor[i].Start is { } a)
            {
                Occupy(i, m, resolvedMinor[i].Span, a, resolvedMajor[i].Span);
                done[i] = true;
            }
        // 2. Locked to a major line (a row in row flow).
        var rowCursors = new Dictionary<int, int>();
        for (var i = 0; i < items.Count; i++)
        {
            if (done[i] || resolvedMajor[i].Start is not { } a) continue;
            var span = resolvedMinor[i].Span;
            var from = dense ? 0 : rowCursors.GetValueOrDefault(a);
            var m = from;
            while (!Free(m, span, a, resolvedMajor[i].Span) && m + span <= minorCount) m++;
            if (m + span > minorCount)
            {
                // No room in the line: the grid grows along the minor axis.
                m = minorCount;
                minorCount += span;
            }
            Occupy(i, m, span, a, resolvedMajor[i].Span);
            rowCursors[a] = m + span;
            done[i] = true;
        }
        // 3. The rest, with the cursor.
        int cursorMajor = 0, cursorMinor = 0;
        for (var i = 0; i < items.Count; i++)
        {
            if (done[i]) continue;
            var minorSpan = Math.Min(resolvedMinor[i].Span, minorCount);
            var majorSpan = resolvedMajor[i].Span;
            if (dense) (cursorMajor, cursorMinor) = (0, 0);
            if (resolvedMinor[i].Start is { } m)
            {
                if (!dense && m < cursorMinor) cursorMajor++;
                cursorMinor = m;
                while (!Free(m, minorSpan, cursorMajor, majorSpan)) cursorMajor++;
            }
            else
            {
                while (true)
                {
                    while (cursorMinor + minorSpan <= minorCount && !Free(cursorMinor, minorSpan, cursorMajor, majorSpan)) cursorMinor++;
                    if (cursorMinor + minorSpan <= minorCount) break;
                    cursorMajor++;
                    cursorMinor = 0;
                }
            }
            Occupy(i, cursorMinor, minorSpan, cursorMajor, majorSpan);
        }
        var majorCount = Math.Max(explicitMajor, 0);
        foreach (var p in placed) majorCount = Math.Max(majorCount, p.Major + p.MajorSpan);
        columns = columnFlow ? majorCount : minorCount;
        rows = columnFlow ? minorCount : majorCount;
        return placed.Select(p => columnFlow ? (p.Major, p.MajorSpan, p.Minor, p.MinorSpan) : (p.Minor, p.MinorSpan, p.Major, p.MajorSpan)).ToArray();
    }

    // A placement on one axis as a 0-based start (null for auto) and a span.
    private static (int? Start, int Span) Resolve(GridLinePlacement placement, int explicitTracks)
    {
        int? Line(int? line) => line switch
        {
            null or 0 => null,
            < 0 => Math.Max(0, explicitTracks + 1 + line.Value),
            _ => line.Value - 1,
        };
        var start = Line(placement.Start);
        var end = Line(placement.End);
        var span = Math.Max(1, placement.Span ?? 1);
        if (start is { } s && end is { } e)
            return e > s ? (s, e - s) : e < s ? (e, s - e) : (s, 1);
        if (start is not null) return (start, span);
        if (end is { } onlyEnd) return (Math.Max(0, onlyEnd - span), Math.Min(span, Math.Max(1, onlyEnd)));
        return (null, span);
    }

    /// <summary>Sizes the tracks of one axis (11.3-11.8) for the items' contributions, in a room of
    /// the size given or of none; fills in each track's <see cref="GridTrack.Base"/>, the size.</summary>
    public static void SizeTracks(IReadOnlyList<GridTrack> tracks, IReadOnlyList<GridContribution> items, float? room, float gap)
    {
        foreach (var track in tracks)
        {
            track.Base = track.MinSizing == GridSizing.Fixed ? track.MinValue : 0;
            track.Limit = track.MaxSizing == GridSizing.Fixed ? track.MaxValue
                : track.IsFlexible ? track.Base : float.PositiveInfinity;
            if (track.Limit < track.Base) track.Limit = track.Base;
        }
        ResolveIntrinsicSizes(tracks, items, gap);
        foreach (var track in tracks)
            if (float.IsPositiveInfinity(track.Limit)) track.Limit = track.Base;
        var gaps = gap * Math.Max(0, tracks.Count - 1);
        if (room is { } definite) MaximizeTracks(tracks, definite - gaps);
        else foreach (var track in tracks) if (!track.IsFlexible) track.Base = track.Limit;
        ExpandFlexibleTracks(tracks, items, room is { } r ? r - gaps : null);
        if (room is { } space) StretchAutoTracks(tracks, space - gaps);
    }

    private static void ResolveIntrinsicSizes(IReadOnlyList<GridTrack> tracks, IReadOnlyList<GridContribution> items, float gap)
    {
        foreach (var item in items.Where(i => i.Span == 1 && i.Start < tracks.Count))
        {
            var track = tracks[item.Start];
            if (track.MinSizing is GridSizing.Auto or GridSizing.MinContent) track.Base = Math.Max(track.Base, item.MinContent);
            else if (track.MinSizing == GridSizing.MaxContent) track.Base = Math.Max(track.Base, item.MaxContent);
            var limit = float.IsPositiveInfinity(track.Limit) ? 0 : track.Limit;
            switch (track.MaxSizing)
            {
                case GridSizing.MinContent:
                    track.Limit = Math.Max(limit, item.MinContent);
                    break;
                case GridSizing.Auto or GridSizing.MaxContent:
                    track.Limit = Math.Max(limit, item.MaxContent);
                    break;
                case GridSizing.FitContent:
                    track.Limit = Math.Max(limit, Math.Min(item.MaxContent, Math.Max(track.MaxValue, item.MinContent)));
                    break;
            }
            if (track.Limit < track.Base) track.Limit = track.Base;
        }
        var spanning = items.Where(i => i.Span > 1).OrderBy(i => i.Span).ToList();
        foreach (var item in spanning)
        {
            var spanned = Spanned(tracks, item);
            if (spanned.Count == 0 || spanned.Any(t => t.IsFlexible)) continue;
            var inner = gap * (item.Span - 1);
            // Base sizes: up to the growth limits, then beyond them to intrinsic maximums.
            DistributeToBases(spanned.Where(t => t.HasIntrinsicMin).ToList(), item.MinContent - inner - spanned.Sum(t => t.Base));
            foreach (var t in spanned) if (t.Limit < t.Base) t.Limit = t.Base;
            // Growth limits: infinite ones count as their base and grow without limit.
            var limits = spanned.Sum(t => float.IsPositiveInfinity(t.Limit) ? t.Base : t.Limit);
            DistributeToLimits(spanned.Where(t => t.HasIntrinsicMax).ToList(), item.MaxContent - inner - limits);
        }
    }

    private static List<GridTrack> Spanned(IReadOnlyList<GridTrack> tracks, GridContribution item)
    {
        var spanned = new List<GridTrack>();
        for (var i = item.Start; i < item.Start + item.Span && i < tracks.Count; i++) spanned.Add(tracks[i]);
        return spanned;
    }

    private static void DistributeToBases(List<GridTrack> tracks, float extra)
    {
        if (extra <= Epsilon || tracks.Count == 0) return;
        extra = ShareUpTo(tracks, extra, t => t.Base, (t, v) => t.Base = v, t => t.Limit);
        if (extra <= Epsilon) return;
        var beyond = tracks.Where(t => t.HasIntrinsicMax).ToList();
        if (beyond.Count == 0) beyond = tracks;
        foreach (var track in beyond) track.Base += extra / beyond.Count;
    }

    private static void DistributeToLimits(List<GridTrack> tracks, float extra)
    {
        if (tracks.Count == 0) return;
        foreach (var track in tracks)
            if (float.IsPositiveInfinity(track.Limit))
            {
                track.Limit = track.Base;
                track.InfinitelyGrowable = true;
            }
        if (extra <= Epsilon) return;
        var growable = tracks.Where(t => t.InfinitelyGrowable).ToList();
        if (growable.Count > 0)
        {
            foreach (var track in growable) track.Limit += extra / growable.Count;
            return;
        }
        foreach (var track in tracks) track.Limit += extra / tracks.Count;
    }

    // Shares the space out equally, each track stopping at its cap; answers what is left.
    private static float ShareUpTo(List<GridTrack> tracks, float space, Func<GridTrack, float> get, Action<GridTrack, float> set, Func<GridTrack, float> cap)
    {
        var open = tracks.Where(t => cap(t) - get(t) > Epsilon).ToList();
        while (space > Epsilon && open.Count > 0)
        {
            var share = space / open.Count;
            var next = new List<GridTrack>();
            foreach (var track in open)
            {
                var room = cap(track) - get(track);
                var grant = Math.Min(share, room);
                set(track, get(track) + grant);
                space -= grant;
                if (room - grant > Epsilon) next.Add(track);
            }
            if (next.Count == open.Count) break;
            open = next;
        }
        return space;
    }

    private static void MaximizeTracks(IReadOnlyList<GridTrack> tracks, float room)
    {
        var free = room - tracks.Sum(t => t.Base);
        if (free <= Epsilon) return;
        ShareUpTo(tracks.Where(t => !t.IsFlexible).ToList(), free, t => t.Base, (t, v) => t.Base = v, t => t.Limit);
    }

    // The fr tracks share what the others leave (12.7), a track whose share falls below its base
    // keeping its base; with no room, each takes its content's size per fr.
    private static void ExpandFlexibleTracks(IReadOnlyList<GridTrack> tracks, IReadOnlyList<GridContribution> items, float? room)
    {
        var flexible = tracks.Where(t => t.IsFlexible).ToList();
        if (flexible.Count == 0) return;
        float frSize;
        if (room is { } definite)
        {
            var inflexible = new HashSet<GridTrack>();
            while (true)
            {
                var leftover = definite - tracks.Where(t => !t.IsFlexible || inflexible.Contains(t)).Sum(t => t.Base);
                var factors = Math.Max(1, flexible.Where(t => !inflexible.Contains(t)).Sum(t => t.MaxValue));
                frSize = Math.Max(0, leftover) / factors;
                var shrunk = flexible.Where(t => !inflexible.Contains(t) && t.MaxValue * frSize < t.Base).ToList();
                if (shrunk.Count == 0) break;
                foreach (var track in shrunk) inflexible.Add(track);
            }
        }
        else
        {
            frSize = 0;
            foreach (var track in flexible)
                frSize = Math.Max(frSize, track.MaxValue > 1 ? track.Base / track.MaxValue : track.Base);
            foreach (var item in items.Where(i => i.Span == 1 && i.Start < tracks.Count && tracks[i.Start].IsFlexible))
            {
                var factor = tracks[item.Start].MaxValue;
                frSize = Math.Max(frSize, factor > 1 ? item.MaxContent / factor : item.MaxContent);
            }
        }
        foreach (var track in flexible) track.Base = Math.Max(track.Base, track.MaxValue * frSize);
    }

    private static void StretchAutoTracks(IReadOnlyList<GridTrack> tracks, float room)
    {
        var free = room - tracks.Sum(t => t.Base);
        var auto = tracks.Where(t => t.MaxSizing == GridSizing.Auto).ToList();
        if (free <= Epsilon || auto.Count == 0) return;
        foreach (var track in auto) track.Base += free / auto.Count;
    }
}
