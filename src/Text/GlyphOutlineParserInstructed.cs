namespace Aspose.Pdf.Text;

internal sealed partial class GlyphOutlineParser
{
    private const int ComponentArgsAreWords = 0x0001;
    private const int ComponentArgsAreOffsets = 0x0002;
    private const int ComponentIsTransformed = 0x0008 | 0x0040 | 0x0080;
    private const int ComponentMore = 0x0020;
    private const int ComponentHasInstructions = 0x0100;
    private const int PhantomPoints = 4;

    /// <summary>A glyph's program is what assembles the glyph when it moves a point by more
    /// than this share of the em; grid fitting at one pixel per unit moves points by a unit or so.</summary>
    private const int AssemblyMoveDivisor = 20;

    private bool _storedOutlinesOnly;
    private bool[]? _storedIsRight;
    private Dictionary<int, GlyphOutline>? _assembled;
    private TrueTypeInterpreter? _interpreter;
    private readonly object _instructedLock = new();

    /// <summary>The outline of a glyph that its instructions assemble: some faces store a
    /// glyph's strokes - the components of a composite, the contours of a simple glyph - away
    /// from where they belong and let the glyph's program move each into place (drawn as
    /// stored, the strokes of a character lie scattered across its cell). Null when the program
    /// leaves the outline where it is stored, give or take grid fitting, and whenever it cannot
    /// be run; the caller then draws the glyph as stored. A glyph's program runs once: what it
    /// came to is remembered.</summary>
    private GlyphOutline? TryInstructedGlyph(int glyphId, int offset, int numContours)
    {
        if (_storedOutlinesOnly) return null;
        lock (_instructedLock)
        {
            if (_storedOutlinesOnly) return null;
            if (!_tables.ContainsKey("fpgm"))
            {
                _storedOutlinesOnly = true;
                return null;
            }
            _storedIsRight ??= new bool[_numGlyphs];
            if (_storedIsRight[glyphId]) return null;
            if (_assembled is not null && _assembled.TryGetValue(glyphId, out var known)) return known;
            var read = numContours < 0 ? ReadInstructedComposite(glyphId, offset) : ReadInstructedSimple(glyphId, offset, numContours);
            if (read is { } glyph)
            {
                _interpreter ??= CreateInterpreter();
                if (_interpreter is null || !_interpreter.TryRun(_data, glyph.ProgramStart, glyph.ProgramLength, glyph.Zone))
                {
                    _storedOutlinesOnly = true;
                    return null;
                }
                if (LargestMove(glyph.Zone) > UnitsPerEm * TrueTypeInterpreter.One / AssemblyMoveDivisor
                    && OutlineOf(glyph.Zone, glyph.OnCurve) is { } outline)
                {
                    (_assembled ??= new Dictionary<int, GlyphOutline>())[glyphId] = outline;
                    return outline;
                }
            }
            _storedIsRight[glyphId] = true;
            return null;
        }
    }

    /// <summary>The points of a simple glyph and where its program lies; null when it has none.</summary>
    private (TrueTypeInterpreter.GlyphZone Zone, bool[] OnCurve, int ProgramStart, int ProgramLength)? ReadInstructedSimple(int glyphId, int offset, int numContours)
    {
        var program = offset + 2 * numContours;
        if (numContours == 0 || program + 2 > _data.Length) return null;
        var length = ReadUInt16(program);
        if (length == 0 || program + 2 + length > _data.Length) return null;
        if (ParseSimpleGlyph(offset, numContours, 0, 0, 0, 0) is not { } outline) return null;
        var xs = new List<int>();
        var ys = new List<int>();
        var onCurve = new List<bool>();
        var ends = new List<int>();
        AddPoints(outline, 0, 0, xs, ys, onCurve, ends);
        AddPhantomPoints(glyphId, xs, ys);
        return (new TrueTypeInterpreter.GlyphZone(xs.ToArray(), ys.ToArray(), ends.ToArray()), onCurve.ToArray(), program + 2, length);
    }

    private static void AddPoints(GlyphOutline part, int dx, int dy, List<int> xs, List<int> ys, List<bool> onCurve, List<int> ends)
    {
        foreach (var contour in part.Contours)
        {
            foreach (var p in contour)
            {
                xs.Add(((int)p.X + dx) * TrueTypeInterpreter.One);
                ys.Add(((int)p.Y + dy) * TrueTypeInterpreter.One);
                onCurve.Add(p.OnCurve);
            }
            ends.Add(xs.Count - 1);
        }
    }

    /// <summary>The phantom points: the origin and the advance on the baseline, and their vertical pair.</summary>
    private void AddPhantomPoints(int glyphId, List<int> xs, List<int> ys)
    {
        for (var i = 0; i < PhantomPoints; i++)
        {
            xs.Add(i == 1 ? GetAdvanceWidth(glyphId) * TrueTypeInterpreter.One : 0);
            ys.Add(0);
        }
    }

    private TrueTypeInterpreter? CreateInterpreter()
    {
        var cvt = Array.Empty<int>();
        if (_tables.TryGetValue("cvt ", out var t) && t.offset + t.length <= _data.Length)
        {
            cvt = new int[t.length / 2];
            for (var i = 0; i < cvt.Length; i++) cvt[i] = ReadInt16(t.offset + i * 2);
        }
        return TrueTypeInterpreter.TryCreate(TableBytes("fpgm"), TableBytes("prep"), cvt, UnitsPerEm);
    }

    private byte[] TableBytes(string tag)
    {
        if (!_tables.TryGetValue(tag, out var t) || t.offset < 0 || t.length <= 0 || t.offset + t.length > _data.Length)
            return Array.Empty<byte>();
        var bytes = new byte[t.length];
        Array.Copy(_data, t.offset, bytes, 0, t.length);
        return bytes;
    }

    private static int LargestMove(TrueTypeInterpreter.GlyphZone zone)
    {
        var largest = 0;
        for (var i = 0; i < zone.CurX.Length - PhantomPoints; i++)
        {
            largest = Math.Max(largest, Math.Abs(zone.CurX[i] - zone.OrgX[i]));
            largest = Math.Max(largest, Math.Abs(zone.CurY[i] - zone.OrgY[i]));
        }
        return largest;
    }

    /// <summary>The points of a composite whose components are plain simple glyphs placed by
    /// offsets, and where its program lies; null for any other composite.</summary>
    private (TrueTypeInterpreter.GlyphZone Zone, bool[] OnCurve, int ProgramStart, int ProgramLength)? ReadInstructedComposite(int glyphId, int offset)
    {
        var xs = new List<int>();
        var ys = new List<int>();
        var onCurve = new List<bool>();
        var ends = new List<int>();
        int flags;
        do
        {
            if (offset + 4 > _data.Length) return null;
            flags = ReadUInt16(offset);
            var component = ReadUInt16(offset + 2);
            offset += 4;
            if ((flags & ComponentArgsAreOffsets) == 0 || (flags & ComponentIsTransformed) != 0) return null;
            int dx, dy;
            if ((flags & ComponentArgsAreWords) != 0)
            {
                if (offset + 4 > _data.Length) return null;
                dx = ReadInt16(offset);
                dy = ReadInt16(offset + 2);
                offset += 4;
            }
            else
            {
                if (offset + 2 > _data.Length) return null;
                dx = (sbyte)_data[offset];
                dy = (sbyte)_data[offset + 1];
                offset += 2;
            }
            if (!IsSimpleGlyph(component) || ParseGlyph(component, 1) is not { } part) return null;
            AddPoints(part, dx, dy, xs, ys, onCurve, ends);
        }
        while ((flags & ComponentMore) != 0);

        if ((flags & ComponentHasInstructions) == 0 || offset + 2 > _data.Length || xs.Count == 0) return null;
        var length = ReadUInt16(offset);
        if (length == 0 || offset + 2 + length > _data.Length) return null;
        AddPhantomPoints(glyphId, xs, ys);
        return (new TrueTypeInterpreter.GlyphZone(xs.ToArray(), ys.ToArray(), ends.ToArray()), onCurve.ToArray(), offset + 2, length);
    }

    private bool IsSimpleGlyph(int glyphId)
    {
        if (_locaOffsets is null || glyphId < 0 || glyphId + 1 >= _locaOffsets.Length) return false;
        var start = _glyfOffset + _locaOffsets[glyphId];
        return start + 10 <= _data.Length && _locaOffsets[glyphId + 1] > _locaOffsets[glyphId] && ReadInt16(start) >= 0;
    }

    private static GlyphOutline? OutlineOf(TrueTypeInterpreter.GlyphZone zone, bool[] onCurve)
    {
        var contours = new ContourPoint[zone.ContourEnds.Length][];
        double xMin = double.MaxValue, yMin = double.MaxValue, xMax = double.MinValue, yMax = double.MinValue;
        var start = 0;
        for (var c = 0; c < contours.Length; c++)
        {
            var end = zone.ContourEnds[c];
            var points = new ContourPoint[end - start + 1];
            for (var i = start; i <= end; i++)
            {
                var x = zone.CurX[i] / (double)TrueTypeInterpreter.One;
                var y = zone.CurY[i] / (double)TrueTypeInterpreter.One;
                points[i - start] = new ContourPoint(x, y, onCurve[i]);
                xMin = Math.Min(xMin, x);
                yMin = Math.Min(yMin, y);
                xMax = Math.Max(xMax, x);
                yMax = Math.Max(yMax, y);
            }
            contours[c] = points;
            start = end + 1;
        }
        return xMin > xMax ? null : new GlyphOutline(contours, xMin, yMin, xMax, yMax);
    }
}
