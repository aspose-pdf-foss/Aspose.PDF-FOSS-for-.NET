using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>What one of the rectangle operators does with the rectangles it reads.</summary>
internal enum PsRectAction
{
    /// <summary>Fill them.</summary>
    Fill,

    /// <summary>Stroke them.</summary>
    Stroke,

    /// <summary>Clip to them.</summary>
    Clip,
}

/// <summary>
/// The level-2 rectangle operators. Each accepts three operand forms — four numbers,
/// a numeric array, or a string of packed numbers — and each replaces the current
/// path rather than adding to it.
/// </summary>
internal static class PsRects
{
    /// <summary>How many numbers describe one rectangle.</summary>
    private const int NumbersPerRect = 4;

    /// <summary>How many numbers a matrix operand holds.</summary>
    private const int MatrixLength = 6;

    /// <summary>Read the rectangles and act on them.</summary>
    public static void RectOperator(PsInterpreter i, PsRectAction action)
    {
        // `rectstroke` alone accepts a trailing matrix, which shapes the pen and not
        // the path: the rectangles are built in the space standing now and the matrix
        // is concatenated afterwards, exactly as the corpus's own emulation of the
        // operator writes it. Without this check the matrix would be read as a
        // rectangle list.
        PsMatrix? pen = null;
        if (action == PsRectAction.Stroke && IsMatrixOperand(i.Peek()))
            pen = PsInterpreter.MatrixFromArray(i.PopArray());
        var rects = ReadRects(i);
        var path = new PsPath();
        foreach (var r in rects) AppendRect(i.Graphics, path, r);
        i.Graphics.SetPath(path);
        switch (action)
        {
            case PsRectAction.Fill:
                i.Graphics.Paint(PsPaintMode.Fill);
                return;
            case PsRectAction.Stroke:
                StrokeUnderPen(i, pen);
                return;
            default:
                i.Graphics.Clip(evenOdd: false);
                i.Graphics.SetPath(new PsPath());
                return;
        }
    }

    /// <summary>Stroke the rectangles, under the pen matrix where one was given.</summary>
    private static void StrokeUnderPen(PsInterpreter i, PsMatrix? pen)
    {
        var state = i.Graphics.State;
        var saved = state.Ctm;
        if (pen.HasValue) state.Ctm = pen.Value.Concat(saved);
        try
        {
            i.Graphics.Paint(PsPaintMode.Stroke);
        }
        finally
        {
            state.Ctm = saved;
        }
    }

    private static void AppendRect(PsGraphics g, PsPath path, double[] r)
    {
        AddCorner(g, path, r[0], r[1], move: true);
        AddCorner(g, path, r[0] + r[2], r[1], move: false);
        AddCorner(g, path, r[0] + r[2], r[1] + r[3], move: false);
        AddCorner(g, path, r[0], r[1] + r[3], move: false);
        path.ClosePath();
    }

    private static void AddCorner(PsGraphics g, PsPath path, double x, double y, bool move)
    {
        g.ToDevice(x, y, out var dx, out var dy);
        if (move) path.MoveTo(dx, dy);
        else path.LineTo(dx, dy);
    }

    /// <summary>Whether an operand is a six-number array, the only shape a matrix
    /// operand can take. A rectangle list is a multiple of four, so a six-element
    /// numeric array is never ambiguous.</summary>
    private static bool IsMatrixOperand(PsValue value)
    {
        var array = value.AsArray;
        if (array is null || array.Length != MatrixLength) return false;
        for (var k = 0; k < MatrixLength; k++)
            if (!array[k].IsNumber)
                return false;
        return true;
    }

    /// <summary>Read one or more rectangles from the operand stack.</summary>
    private static List<double[]> ReadRects(PsInterpreter i)
    {
        var top = i.Peek();
        if (top.IsNumber) return new List<double[]> { ReadFourNumbers(i) };
        if (top.Type == PsType.Array) return FromArray(i.PopArray());
        if (top.Type == PsType.String) return FromString(i.PopString());
        throw new PsErrorSignal("typecheck");
    }

    private static double[] ReadFourNumbers(PsInterpreter i)
    {
        var height = i.PopNumber();
        var width = i.PopNumber();
        var y = i.PopNumber();
        var x = i.PopNumber();
        return new[] { x, y, width, height };
    }

    private static List<double[]> FromArray(PsArray array)
    {
        var rects = new List<double[]>();
        for (var k = 0; k + NumbersPerRect <= array.Length; k += NumbersPerRect)
        {
            var r = new double[NumbersPerRect];
            for (var j = 0; j < NumbersPerRect; j++)
            {
                if (!array[k + j].IsNumber) throw new PsErrorSignal("typecheck");
                r[j] = array[k + j].Number;
            }

            rects.Add(r);
        }

        return rects;
    }

    /// <summary>The packed form: pairs of signed 16-bit numbers scaled by a leading
    /// pair that gives the scale and origin. Rare enough in practice that an
    /// unreadable buffer yields no rectangles rather than an error.</summary>
    private static List<double[]> FromString(PsString text)
    {
        var rects = new List<double[]>();
        const int BytesPerNumber = 2;
        var stride = NumbersPerRect * BytesPerNumber;
        for (var k = 0; k + stride <= text.Length; k += stride)
        {
            var r = new double[NumbersPerRect];
            for (var j = 0; j < NumbersPerRect; j++)
                r[j] = (short)((text[k + j * BytesPerNumber] << 8) | text[k + j * BytesPerNumber + 1]);
            rects.Add(r);
        }

        return rects;
    }
}
