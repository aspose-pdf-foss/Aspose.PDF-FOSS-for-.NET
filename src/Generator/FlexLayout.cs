using System;
using System.Collections.Generic;

namespace Aspose.Pdf;

/// <summary>Where the free space on an axis goes (CSS justify-content and align-content).</summary>
internal enum FlexDistribution
{
    Start,
    End,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
    /// <summary>The free space is shared out to the lines themselves (align-content only).</summary>
    Stretch,
}

/// <summary>Where an item sits across its line (CSS align-items and align-self).</summary>
internal enum FlexCrossPlacement
{
    Start,
    End,
    Center,
    Stretch,
}

/// <summary>One flex item along the main axis: sizes are inner (content-box) sizes, and
/// <see cref="Chrome"/> is what the margins, borders and paddings add on that axis.</summary>
internal sealed class FlexItem
{
    public float BaseSize;
    public float Chrome;
    public float MinSize;
    public float MaxSize = float.PositiveInfinity;
    public float Grow;
    public float Shrink = 1;

    /// <summary>The resolved inner main size, once the line's lengths are resolved.</summary>
    public float MainSize;

    internal float Hypothetical => Clamp(BaseSize);
    internal float Clamp(float size) => Math.Max(MinSize, Math.Min(MaxSize, size));
}

/// <summary>
/// The flex layout algorithm of CSS Flexible Box Layout Module Level 1, section 9, on numbers
/// alone: breaking items into lines, resolving flexible lengths, and distributing the free space
/// on the main and cross axes. The caller measures the items and places the boxes; positions are
/// offsets from the start of each axis.
/// </summary>
internal static class FlexLayout
{
    private const float Epsilon = 1e-4f;

    /// <summary>Collects the items into lines (9.3): each line takes items while their outer
    /// hypothetical sizes and the gaps between them fit the room; a single-line container keeps
    /// them all on one line. Answers the index ranges of the lines.</summary>
    public static List<(int Start, int Count)> CollectLines(IReadOnlyList<FlexItem> items, float room, float gap, bool wrap)
    {
        var lines = new List<(int, int)>();
        if (items.Count == 0) return lines;
        if (!wrap)
        {
            lines.Add((0, items.Count));
            return lines;
        }
        int start = 0, count = 0;
        float used = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var outer = items[i].Hypothetical + items[i].Chrome;
            var needed = count == 0 ? outer : used + gap + outer;
            if (count > 0 && needed > room + Epsilon)
            {
                lines.Add((start, count));
                start = i;
                count = 1;
                used = outer;
                continue;
            }
            used = needed;
            count++;
        }
        lines.Add((start, count));
        return lines;
    }

    /// <summary>Resolves the flexible lengths of one line (9.7): the items grow into free space
    /// by their grow factors or shrink out of overflow by their shrink factors scaled by their
    /// base sizes, an item clamped by its minimum or maximum frozen there and the rest resolved
    /// again. A sum of factors under one hands out only that fraction of the space.</summary>
    public static void ResolveLengths(IReadOnlyList<FlexItem> line, float room, float gap)
    {
        var count = line.Count;
        if (count == 0) return;
        float hypotheticalSum = gap * (count - 1);
        foreach (var item in line) hypotheticalSum += item.Hypothetical + item.Chrome;
        var growing = hypotheticalSum < room;
        var frozen = new bool[count];
        var target = new float[count];
        for (var i = 0; i < count; i++)
        {
            var item = line[i];
            var factor = growing ? item.Grow : item.Shrink;
            if (factor == 0 || growing && item.BaseSize > item.Hypothetical || !growing && item.BaseSize < item.Hypothetical)
            {
                frozen[i] = true;
                target[i] = item.Hypothetical;
            }
            else target[i] = item.BaseSize;
        }
        var initialFree = FreeSpace(line, frozen, target, room, gap);
        while (true)
        {
            var anyUnfrozen = false;
            float factorSum = 0;
            for (var i = 0; i < count; i++)
                if (!frozen[i])
                {
                    anyUnfrozen = true;
                    factorSum += growing ? line[i].Grow : line[i].Shrink;
                }
            if (!anyUnfrozen) break;
            var free = FreeSpace(line, frozen, target, room, gap);
            if (factorSum < 1 && Math.Abs(initialFree * factorSum) < Math.Abs(free)) free = initialFree * factorSum;
            if (growing)
            {
                for (var i = 0; i < count; i++)
                    if (!frozen[i]) target[i] = line[i].BaseSize + free * line[i].Grow / factorSum;
            }
            else
            {
                float scaledSum = 0;
                for (var i = 0; i < count; i++)
                    if (!frozen[i]) scaledSum += line[i].Shrink * line[i].BaseSize;
                for (var i = 0; i < count; i++)
                    if (!frozen[i])
                        target[i] = scaledSum == 0 ? line[i].BaseSize
                            : line[i].BaseSize - Math.Abs(free) * line[i].Shrink * line[i].BaseSize / scaledSum;
            }
            // Fix the min/max violations: freeze all of one kind, whichever the total favours.
            float violation = 0;
            var clamped = new float[count];
            for (var i = 0; i < count; i++)
            {
                if (frozen[i]) continue;
                clamped[i] = Math.Max(0, line[i].Clamp(target[i]));
                violation += clamped[i] - target[i];
            }
            var froze = false;
            for (var i = 0; i < count; i++)
            {
                if (frozen[i]) continue;
                var delta = clamped[i] - target[i];
                var freeze = Math.Abs(violation) < Epsilon || violation > 0 && delta > 0 || violation < 0 && delta < 0;
                target[i] = clamped[i];
                if (freeze)
                {
                    frozen[i] = true;
                    froze = true;
                }
            }
            if (!froze) break;
        }
        for (var i = 0; i < count; i++) line[i].MainSize = target[i];
    }

    private static float FreeSpace(IReadOnlyList<FlexItem> line, bool[] frozen, float[] target, float room, float gap)
    {
        var free = room - gap * (line.Count - 1);
        for (var i = 0; i < line.Count; i++)
            free -= line[i].Chrome + (frozen[i] ? target[i] : line[i].BaseSize);
        return free;
    }

    /// <summary>Places boxes of the outer sizes given along an axis of the room (9.5, 9.6):
    /// answers each box's start. With no free space, or with space to share among a single box,
    /// the spacing distributions start at the start.</summary>
    public static float[] Distribute(IReadOnlyList<float> sizes, float room, float gap, FlexDistribution distribution)
    {
        var count = sizes.Count;
        var positions = new float[count];
        if (count == 0) return positions;
        var free = room - gap * (count - 1);
        foreach (var size in sizes) free -= size;
        float lead = 0, between = gap;
        switch (distribution)
        {
            case FlexDistribution.End:
                lead = free;
                break;
            case FlexDistribution.Center:
                lead = free / 2;
                break;
            case FlexDistribution.SpaceBetween when free > 0 && count > 1:
                between += free / (count - 1);
                break;
            case FlexDistribution.SpaceAround when free > 0:
                lead = free / count / 2;
                between += free / count;
                break;
            case FlexDistribution.SpaceEvenly when free > 0:
                lead = free / (count + 1);
                between += lead;
                break;
        }
        var at = lead;
        for (var i = 0; i < count; i++)
        {
            positions[i] = at;
            at += sizes[i] + between;
        }
        return positions;
    }

    /// <summary>The lines across the container (9.4, 9.6): with a definite room, stretch shares
    /// the free space out equally to the lines, and the other distributions place them as boxes.
    /// Answers each line's start and final size.</summary>
    public static (float Start, float Size)[] PlaceLines(IReadOnlyList<float> lineSizes, float? room, float gap, FlexDistribution distribution)
    {
        var count = lineSizes.Count;
        var sizes = new float[count];
        for (var i = 0; i < count; i++) sizes[i] = lineSizes[i];
        if (room is not { } definite) definite = Total(sizes, gap);
        if (distribution == FlexDistribution.Stretch)
        {
            var free = definite - Total(sizes, gap);
            if (free > 0 && count > 0)
                for (var i = 0; i < count; i++) sizes[i] += free / count;
            distribution = FlexDistribution.Start;
        }
        var starts = Distribute(sizes, definite, gap, distribution);
        var placed = new (float, float)[count];
        for (var i = 0; i < count; i++) placed[i] = (starts[i], sizes[i]);
        return placed;
    }

    /// <summary>An item's start across its line (9.6 align-self): stretch and start at the line's
    /// start, end at its end, center in its middle.</summary>
    public static float PlaceAcross(float itemSize, float lineSize, FlexCrossPlacement placement) => placement switch
    {
        FlexCrossPlacement.End => lineSize - itemSize,
        FlexCrossPlacement.Center => (lineSize - itemSize) / 2,
        _ => 0,
    };

    private static float Total(float[] sizes, float gap)
    {
        float total = sizes.Length > 1 ? gap * (sizes.Length - 1) : 0;
        foreach (var size in sizes) total += size;
        return total;
    }
}
