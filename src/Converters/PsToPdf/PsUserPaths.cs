using System;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// The user-path operators. A user path is a self-contained path description — a
/// procedure, or an array of numbers followed by the operator names that consume
/// them — which the painting operators build and paint in one step, without
/// disturbing the path the program was already building.
/// </summary>
internal static class PsUserPaths
{
    /// <summary>How many numbers a matrix operand holds.</summary>
    private const int MatrixLength = 6;

    /// <summary>Build a user path and paint it.</summary>
    public static void Paint(PsInterpreter i, PsPaintMode mode)
    {
        // `ustroke` alone may carry a trailing matrix, which transforms the pen.
        if (mode == PsPaintMode.Stroke && IsMatrix(i.Peek())) i.Pop();
        var path = i.Pop();
        var saved = i.Graphics.Path;
        i.Graphics.SetPath(new PsPath());
        Build(i, path);
        i.Graphics.Paint(mode);
        i.Graphics.SetPath(saved);
    }

    /// <summary>Append a user path to the path the program is building.</summary>
    public static void Append(PsInterpreter i)
    {
        var path = i.Pop();
        Build(i, path);
    }

    /// <summary>Run a user path's construction. Three shapes are accepted: a
    /// procedure, which runs as it stands; a plain array of numbers and names, whose
    /// names must be executed rather than pushed; and the encoded form, where the
    /// operands sit in one array and the operators in a string of numbers beside it.</summary>
    private static void Build(PsInterpreter i, PsValue path)
    {
        if (path.Type != PsType.Array || path.IsExecutable)
        {
            i.Invoke(path);
            return;
        }

        var array = path.AsArray;
        if (array is null) throw new PsErrorSignal("typecheck");
        if (TryBuildEncoded(i, array)) return;
        for (var k = 0; k < array.Length; k++)
        {
            var item = array[k];
            i.ExecuteObject(item.Type == PsType.Name ? item.WithExecutable(true) : item);
        }
    }

    /// <summary>The encoded form: an array whose last element is a string of operator
    /// numbers, the rest supplying their operands in order. The numbers may sit
    /// directly in the array or in a nested one.</summary>
    private static bool TryBuildEncoded(PsInterpreter i, PsArray array)
    {
        if (array.Length == 0) return false;
        var last = array[array.Length - 1];
        if (last.Type != PsType.String) return false;
        var operands = CollectOperands(array);
        var at = 0;
        var codes = last.AsString!;
        for (var k = 0; k < codes.Length; k++)
        {
            var op = PsEncodedPathOps.For(codes[k]);
            if (op is null) return false;
            for (var n = 0; n < op.Operands; n++)
                i.Push(PsValue.Num(at < operands.Count ? operands[at++] : 0));
            i.ExecuteObject(PsValue.ExecName(op.Name));
        }

        return true;
    }

    /// <summary>Every number the encoded form supplies, in order, whether the array
    /// holds them directly or groups them into nested arrays.</summary>
    private static System.Collections.Generic.List<double> CollectOperands(PsArray array)
    {
        var values = new System.Collections.Generic.List<double>();
        for (var k = 0; k < array.Length - 1; k++)
        {
            var item = array[k];
            if (item.IsNumber)
            {
                values.Add(item.Number);
                continue;
            }

            var inner = item.AsArray;
            if (inner is null) continue;
            for (var j = 0; j < inner.Length; j++)
                if (inner[j].IsNumber)
                    values.Add(inner[j].Number);
        }

        return values;
    }

    private static bool IsMatrix(PsValue value)
    {
        var array = value.AsArray;
        if (array is null || array.Length != MatrixLength) return false;
        for (var k = 0; k < MatrixLength; k++)
            if (!array[k].IsNumber)
                return false;
        return true;
    }

    /// <summary>Capture the current path as a user path: a procedure that rebuilds it,
    /// in the user space the program is working in.</summary>
    public static void Capture(PsInterpreter i)
    {
        // The boolean operand asks for a cache entry, which this converter has no use
        // for; the path itself is what the program will replay.
        if (i.Peek().Type == PsType.Boolean) i.Pop();
        var items = new System.Collections.Generic.List<PsValue>();
        var inverse = i.Graphics.State.Ctm.Invert();
        foreach (var s in i.Graphics.Path.Segments) Encode(items, s, inverse);
        i.Push(PsValue.Proc(new PsArray(items)));
    }

    private static void Encode(System.Collections.Generic.List<PsValue> items,
        PsSegment s, PsMatrix inverse)
    {
        switch (s.Kind)
        {
            case PsSegmentKind.Move:
                Point(items, inverse, s.X, s.Y);
                items.Add(PsValue.ExecName("moveto"));
                return;
            case PsSegmentKind.Line:
                Point(items, inverse, s.X, s.Y);
                items.Add(PsValue.ExecName("lineto"));
                return;
            case PsSegmentKind.Curve:
                Point(items, inverse, s.X1, s.Y1);
                Point(items, inverse, s.X2, s.Y2);
                Point(items, inverse, s.X, s.Y);
                items.Add(PsValue.ExecName("curveto"));
                return;
            default:
                items.Add(PsValue.ExecName("closepath"));
                return;
        }
    }

    private static void Point(System.Collections.Generic.List<PsValue> items,
        PsMatrix inverse, double x, double y)
    {
        inverse.Transform(x, y, out var ux, out var uy);
        items.Add(PsValue.Num(ux));
        items.Add(PsValue.Num(uy));
    }
}
