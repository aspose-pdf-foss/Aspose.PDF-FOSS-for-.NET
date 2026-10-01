using System;
using System.Collections.Generic;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Tiling patterns. A pattern dictionary carries the cell's extent, the step between
/// tiles and the procedure that paints one cell; making a pattern binds it to the
/// transformation in force, and setting it makes it the colour that later paints use.
/// The cell is built by running the program's own procedure into a writer of its own,
/// so a cell drawn with paths stays paths rather than becoming a picture of them.
/// </summary>
internal static class PsPatterns
{
    /// <summary>How many numbers a bounding box holds.</summary>
    private const int BoxLength = 4;

    /// <summary>A coloured cell carries its own colours; an uncoloured one is painted
    /// in whatever colour the caller supplies when it sets the pattern.</summary>
    private const int ColouredPaintType = 1;

    /// <summary>The key this converter records the bound matrix under. It is not part
    /// of the language's own dictionary, so it is spelled to avoid a program's keys.</summary>
    private const string BoundMatrixKey = "PsToPdfPatternMatrix";

    /// <summary>How deep a pattern cell may nest before one is refused, so a cell that
    /// paints with itself cannot recurse without end.</summary>
    private const int MaxDepth = 4;

    /// <summary><c>makepattern</c>: bind a pattern dictionary to a matrix and to the
    /// transformation in force, and hand back the bound instance.</summary>
    public static void MakePattern(PsInterpreter i)
    {
        var matrix = PsInterpreter.MatrixFromArray(i.PopArray());
        var source = i.PopDict();
        var bound = new PsDictionary(source.Count + 1);
        foreach (var pair in source.Entries) bound.Put(pair.Key, pair.Value);
        // Pattern space is fixed to the space in force when the pattern was made, not
        // to whatever transformation happens to be current when it is painted with.
        bound.Put(BoundMatrixKey, PsValue.Arr(PsInterpreter.MatrixToArray(
            null, matrix.Concat(i.Graphics.State.Ctm))));
        i.Push(PsValue.Dict(bound));
    }

    /// <summary><c>setpattern</c>: make a pattern the current colour, taking the
    /// components an uncoloured one paints with from underneath it.</summary>
    public static void SetPattern(PsInterpreter i) => Install(i, takeComponents: true);

    /// <summary><c>setcolor</c> in the pattern space names the PATTERN and nothing
    /// else. The reference conversion leaves the tint lying on the stack, where the
    /// rectangle that follows reads it as its own height — which is the thin bar its
    /// etalon draws beside the circle.</summary>
    public static void SetPatternColor(PsInterpreter i) => Install(i, takeComponents: false);

    private static void Install(PsInterpreter i, bool takeComponents)
    {
        var pattern = i.PopDict();
        // Only an UNCOLOURED pattern takes components, and only as many as its
        // underlying space has.
        var wanted = takeComponents && PaintTypeOf(pattern) != ColouredPaintType
            ? i.Graphics.State.PatternBaseComponents
            : 0;
        var components = new List<double>();
        for (var k = 0; k < wanted && i.OperandCount > 0 && i.Peek().IsNumber; k++)
            components.Insert(0, i.PopNumber());
        // Building the cell saves and restores the graphics state, and a restore
        // installs the saved COPY, so the state to write into is the one standing
        // afterwards — a reference taken before the build would be an orphan.
        var name = Register(i, pattern);
        var state = i.Graphics.State;
        state.Space = PsColorSpace.Pattern;
        state.Pattern = pattern;
        // The components are read off the stack because the language puts them there,
        // but nothing is written with them: an uncoloured cell is baked into a coloured
        // one, so the pattern is named on its own.
        state.PatternComponents = null;
        state.PatternName = name;
    }

    /// <summary>Write the pattern as a page resource, building its cell once, and
    /// report the name it was written under. A pattern that cannot be built leaves the
    /// colour plain rather than painting nothing.</summary>
    private static string? Register(PsInterpreter i, PsDictionary pattern)
    {
        if (i.PatternNames.TryGetValue(pattern, out var existing))
        {
            // A resource belongs to ONE page: a pattern first painted with on an
            // earlier page has to be written again on this one.
            if (existing != null && i.PatternStreams.TryGetValue(pattern, out var written))
                i.Graphics.Writer.AddResource("Pattern", existing, written);
            return existing;
        }

        var stream = BuildCell(i, pattern);
        if (stream is null)
        {
            i.PatternNames[pattern] = null;
            return null;
        }

        var name = "P" + (i.PatternNames.Count + 1);
        i.PatternNames[pattern] = name;
        i.PatternStreams[pattern] = stream;
        i.Graphics.Writer.AddResource("Pattern", name, stream);
        return name;
    }

    /// <summary>Run the cell procedure into a writer of its own and wrap what it
    /// painted as a tiling pattern.</summary>
    private static PdfStream? BuildCell(PsInterpreter i, PsDictionary pattern)
    {
        if (!pattern.TryGet("PaintProc", out var proc) || proc.AsArray is null) return null;
        if (!TryReadBox(pattern, out var box)) return null;
        if (i.PatternDepth >= MaxDepth) return null;
        var uncoloured = PaintTypeOf(pattern) != ColouredPaintType;
        if (uncoloured) box = GroundBox(pattern, box);
        var cell = new PsPageWriter(box[2] - box[0], box[3] - box[1]);
        cell.SuppressPagePrologue();
        var graphics = i.Graphics;
        var previous = graphics.BeginCapture(cell);
        graphics.GSave();
        i.PatternDepth++;
        try
        {
            // The cell is painted in pattern space: no page transformation, no page
            // clip — a clip stated in page coordinates would be read in cell
            // coordinates and cut the tile to a sliver — and no inherited colour,
            // since the procedure sets whatever it wants.
            graphics.State.Ctm = PsMatrix.Identity;
            graphics.InitClip();
            graphics.State.Space = PsColorSpace.Gray;
            graphics.State.Pattern = null;
            graphics.State.PatternName = null;
            graphics.SetPath(new PsPath());
            if (uncoloured) PaintGround(graphics, box);
            i.Push(PsValue.Dict(pattern));
            i.Invoke(proc);
        }
        catch (PsErrorSignal e)
        {
            i.RecordError(e.ErrorName);
        }
        finally
        {
            i.PatternDepth--;
            graphics.GRestore();
            graphics.EndCapture(previous);
        }

        return Wrap(pattern, box, cell);
    }

    /// <summary>The box an uncoloured cell's ground covers: a whole step, so the tiles
    /// meet with nothing showing between them, and never smaller than the cell's own
    /// bounding box, so the shape it paints stays whole.</summary>
    private static double[] GroundBox(PsDictionary pattern, double[] box) => new[]
    {
        box[0], box[1],
        box[0] + Math.Max(Math.Abs(StepOf(pattern, "XStep", box[2] - box[0])), box[2] - box[0]),
        box[1] + Math.Max(Math.Abs(StepOf(pattern, "YStep", box[3] - box[1])), box[3] - box[1]),
    };

    /// <summary>Lay the ground an uncoloured cell is painted over, and leave the colour
    /// its procedure paints in. The reference conversion turns such a cell into a
    /// PICTURE of itself whose unpainted pixels come out black and whose marks come out
    /// red, whatever tint the caller asked for, and it is that picture the etalons
    /// hold — so the cell is baked the same way and written as a coloured one.</summary>
    private static void PaintGround(PsGraphics graphics, double[] box)
    {
        var ground = new PsPath();
        ground.MoveTo(box[0], box[1]);
        ground.LineTo(box[2], box[1]);
        ground.LineTo(box[2], box[3]);
        ground.LineTo(box[0], box[3]);
        ground.ClosePath();
        graphics.SetPath(ground);
        graphics.State.Color = PsColor.FromGray(0);
        graphics.Paint(PsPaintMode.Fill);
        graphics.State.Space = PsColorSpace.Rgb;
        graphics.State.Color = PsColor.FromRgb(GroundMarkRed, 0, 0);
    }

    /// <summary>The red the reference conversion's picture of a cell paints its marks
    /// in, whatever tint the caller asked for.</summary>
    private const double GroundMarkRed = 1.0;

    private static PdfStream Wrap(PsDictionary pattern, double[] box, PsPageWriter cell)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Pattern"));
        dict.Set("PatternType", new PdfInteger(1));
        // Both kinds are written as coloured: an uncoloured cell has had its ground and
        // its mark colour baked in.
        dict.Set("PaintType", new PdfInteger(ColouredPaintType));
        dict.Set("TilingType", new PdfInteger(TilingTypeOf(pattern)));
        dict.Set("BBox", new PdfArray(new List<PdfObject>
        {
            new PdfReal(box[0]), new PdfReal(box[1]), new PdfReal(box[2]), new PdfReal(box[3]),
        }));
        dict.Set("XStep", new PdfReal(StepOf(pattern, "XStep", box[2] - box[0])));
        dict.Set("YStep", new PdfReal(StepOf(pattern, "YStep", box[3] - box[1])));
        dict.Set("Matrix", MatrixOf(pattern));
        dict.Set("Resources", cell.CollectedResources());
        var data = Compat.Latin1.GetBytes(cell.Content);
        dict.Set("Length", new PdfInteger(data.Length));
        return new PdfStream(dict, data);
    }

    private static int PaintTypeOf(PsDictionary pattern) =>
        pattern.TryGet("PaintType", out var v) && v.IsNumber ? v.ToInt() : ColouredPaintType;

    private static int TilingTypeOf(PsDictionary pattern) =>
        pattern.TryGet("TilingType", out var v) && v.IsNumber ? v.ToInt() : 1;

    private static double StepOf(PsDictionary pattern, string key, double fallback)
    {
        if (pattern.TryGet(key, out var v) && v.IsNumber && Math.Abs(v.Number) > 0)
            return v.Number;
        return Math.Abs(fallback) > 0 ? fallback : 1;
    }

    private static PdfArray MatrixOf(PsDictionary pattern)
    {
        var m = pattern.TryGet(BoundMatrixKey, out var v) && v.AsArray != null
            ? PsInterpreter.MatrixFromArray(v.AsArray)
            : PsMatrix.Identity;
        var e = m.ToElements();
        var items = new List<PdfObject>(e.Length);
        foreach (var x in e) items.Add(new PdfReal(x));
        return new PdfArray(items);
    }

    private static bool TryReadBox(PsDictionary pattern, out double[] box)
    {
        box = new double[BoxLength];
        if (!pattern.TryGet("BBox", out var b) || b.AsArray is null) return false;
        var array = b.AsArray;
        if (array.Length < BoxLength) return false;
        for (var k = 0; k < BoxLength; k++)
        {
            if (!array[k].IsNumber) return false;
            box[k] = array[k].Number;
        }

        return box[2] - box[0] != 0 && box[3] - box[1] != 0;
    }
}
