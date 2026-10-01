namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>A cell spanning several columns, with the boxes its content asks
    /// for: its widest word and its longest line, chrome included.</summary>
    internal readonly struct SpanBoxes
    {
        public readonly int Column;
        public readonly int Span;
        public readonly double Min;
        public readonly double Max;
        public SpanBoxes(int column, int span, double min, double max)
        {
            Column = column;
            Span = span;
            Min = min;
            Max = max;
        }
    }

    /// <summary>The column pitches of a content-sized grid (<see cref="SizesColumnsToContent"/>)
    /// some of whose columns declare a SHARE of the grid rather than a width.
    ///
    /// The grid is as wide as its shares need: for every share column, its widest
    /// box divided by its share (a span over share columns likewise, by the sum of
    /// theirs), and for the columns that declare no share, what they need divided by
    /// the share left to them. Wider than the band, or told to stretch, the grid
    /// fills the band instead. A share column is then its share of that width; a
    /// column with a width keeps it and an auto column its widest box, unless the
    /// grid fills the band, when what is left goes to the auto columns in
    /// proportion to their widest boxes, or with none to the width columns in
    /// proportion to their room above their narrowest.
    ///
    /// Shares that do not add up to the whole grid are stretched to it when no
    /// other column is there to take the rest, and shares that add up to more are
    /// clipped in turn. A share column cannot go below its narrowest box: among
    /// share columns the shortfall comes off the others in proportion to their
    /// shares, a width column gives it up, and beside an auto column the shares are
    /// abandoned and every column takes its widest box's proportion of the band.</summary>
    private static double[] PercentSizedColumnWidths(double band, double?[] declared, double?[] percents,
        ContentBoxes[] boxes, List<SpanBoxes> spans, bool stretches)
    {
        var n = boxes.Length;
        var share = new double[n];
        var natural = new double[n];
        var isShare = new bool[n];
        var isAuto = new bool[n];
        double shareSum = 0, naturalSum = 0;
        var autos = 0;
        for (var i = 0; i < n; i++)
        {
            if (i < percents.Length && percents[i] is { } p && p > 0)
            {
                isShare[i] = true;
                share[i] = p;
                shareSum += p;
                continue;
            }
            var declaredWidth = i < declared.Length && declared[i] is { } d && d >= boxes[i].Min ? d : (double?)null;
            isAuto[i] = declaredWidth is null;
            if (isAuto[i]) autos++;
            natural[i] = declaredWidth ?? boxes[i].Max;
            naturalSum += natural[i];
        }
        var others = n - CountTrue(isShare);
        var effective = EffectiveShares(share, isShare, shareSum, others > 0);

        // The width the shares ask for. Shares short of the whole ask by what they
        // DECLARED (their stretched split then fills that width); shares over it
        // ask by what they were clipped to.
        var asking = shareSum > 1 ? effective : share;
        var want = 0.0;
        for (var i = 0; i < n; i++)
            if (isShare[i] && asking[i] > 0) want = Math.Max(want, boxes[i].Max / asking[i]);
        foreach (var span in spans)
        {
            var spanShare = 0.0;
            var allShare = true;
            for (var k = span.Column; k < span.Column + span.Span && k < n; k++)
            {
                if (!isShare[k]) { allShare = false; break; }
                spanShare += asking[k];
            }
            if (allShare && spanShare > 0) want = Math.Max(want, span.Max / spanShare);
        }
        if (others > 0 && shareSum < 1) want = Math.Max(want, naturalSum / (1 - shareSum));

        var fills = stretches || want >= band;
        var width = fills ? band : want;
        var widths = new double[n];
        for (var i = 0; i < n; i++)
            if (isShare[i]) widths[i] = effective[i] * width;

        if (others == 0)
        {
            FloorShareColumns(widths, effective, boxes, width);
            return widths;
        }
        if (!fills)
        {
            for (var i = 0; i < n; i++) if (!isShare[i]) widths[i] = natural[i];
            return widths;
        }
        if (autos > 0)
        {
            for (var i = 0; i < n; i++)
                if (isShare[i] && boxes[i].Min > widths[i] + 1e-9) return WidestBoxShares(boxes, band);
            FillRestByWidestBox(widths, isAuto, natural, boxes, width);
            return widths;
        }
        for (var i = 0; i < n; i++)
            if (isShare[i]) widths[i] = Math.Max(widths[i], boxes[i].Min);
        FillRestByRoom(widths, isShare, natural, boxes, width);
        return widths;
    }

    /// <summary>The share each column is laid at. With other columns beside them
    /// the shares stand as declared; alone, shares short of the whole are stretched
    /// to it and shares over it are clipped one after another.</summary>
    private static double[] EffectiveShares(double[] share, bool[] isShare, double shareSum, bool withOthers)
    {
        var effective = (double[])share.Clone();
        if (withOthers || shareSum <= 0) return effective;
        if (shareSum < 1)
        {
            for (var i = 0; i < effective.Length; i++) effective[i] /= shareSum;
            return effective;
        }
        var left = 1.0;
        for (var i = 0; i < effective.Length; i++)
        {
            if (!isShare[i]) continue;
            effective[i] = Math.Min(effective[i], Math.Max(0, left));
            left -= effective[i];
        }
        return effective;
    }

    /// <summary>Raises every share column to its narrowest box and takes the
    /// difference off the columns still above theirs, in proportion to their
    /// shares, until none is below.</summary>
    private static void FloorShareColumns(double[] widths, double[] effective, ContentBoxes[] boxes, double width)
    {
        var floored = new bool[widths.Length];
        for (var round = 0; round < widths.Length; round++)
        {
            var moved = false;
            for (var i = 0; i < widths.Length; i++)
                if (!floored[i] && widths[i] < boxes[i].Min)
                {
                    widths[i] = boxes[i].Min;
                    floored[i] = true;
                    moved = true;
                }
            if (!moved) return;
            double total = 0, free = 0;
            for (var i = 0; i < widths.Length; i++)
            {
                total += widths[i];
                if (!floored[i]) free += effective[i];
            }
            var deficit = total - width;
            if (deficit <= 1e-9 || free <= 0) return;
            for (var i = 0; i < widths.Length; i++)
                if (!floored[i]) widths[i] -= deficit * effective[i] / free;
        }
    }

    /// <summary>Every column takes the band in proportion to its widest box.</summary>
    private static double[] WidestBoxShares(ContentBoxes[] boxes, double band)
    {
        var widths = new double[boxes.Length];
        double total = 0;
        foreach (var box in boxes) total += box.Max;
        if (total <= 0) return widths;
        for (var i = 0; i < boxes.Length; i++) widths[i] = band * boxes[i].Max / total;
        return widths;
    }

    /// <summary>What the share columns leave goes to the auto columns in proportion
    /// to their widest boxes; a width column keeps its width.</summary>
    private static void FillRestByWidestBox(double[] widths, bool[] isAuto, double[] natural, ContentBoxes[] boxes, double width)
    {
        double taken = 0, autoMax = 0;
        for (var i = 0; i < widths.Length; i++)
        {
            if (isAuto[i]) { autoMax += boxes[i].Max; continue; }
            if (widths[i] <= 0) widths[i] = natural[i];
            taken += widths[i];
        }
        var rest = Math.Max(0, width - taken);
        for (var i = 0; i < widths.Length; i++)
            if (isAuto[i]) widths[i] = autoMax > 0 ? rest * boxes[i].Max / autoMax : 0;
    }

    /// <summary>What the share columns leave goes to the width columns: each keeps
    /// its width, and the difference moves them in proportion to their room above
    /// their narrowest box.</summary>
    private static void FillRestByRoom(double[] widths, bool[] isShare, double[] natural, ContentBoxes[] boxes, double width)
    {
        double shareTotal = 0, fixedTotal = 0, room = 0;
        for (var i = 0; i < widths.Length; i++)
        {
            if (isShare[i]) { shareTotal += widths[i]; continue; }
            widths[i] = natural[i];
            fixedTotal += natural[i];
            room += natural[i] - boxes[i].Min;
        }
        var change = width - shareTotal - fixedTotal;
        if (room <= 0) return;
        for (var i = 0; i < widths.Length; i++)
            if (!isShare[i]) widths[i] += change * (natural[i] - boxes[i].Min) / room;
    }

    private static int CountTrue(bool[] flags)
    {
        var count = 0;
        foreach (var flag in flags) if (flag) count++;
        return count;
    }

    /// <summary>The share of the grid each column declares, as a fraction; null
    /// where a column declares a width or nothing.</summary>
    private double?[] DeclaredPercentWidths()
    {
        if (string.IsNullOrWhiteSpace(ColumnWidths)) return Array.Empty<double?>();
        var tokens = ColumnWidths!.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var shares = new double?[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
            shares[i] = tokens[i].EndsWith("%", StringComparison.Ordinal)
                && TryParseWidthToken(tokens[i].Substring(0, tokens[i].Length - 1)) is { } pct
                ? pct / 100
                : null;
        return shares;
    }
}
