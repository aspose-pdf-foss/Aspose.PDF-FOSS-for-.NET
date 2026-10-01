using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>Coordinate-system, path-construction, painting and colour operators.</summary>
internal sealed partial class PsInterpreter
{
    private void RegisterGraphicsOperators()
    {
        RegisterMatrixOperators();
        RegisterPathOperators();
        RegisterPaintOperators();
        RegisterColorOperators();
        RegisterLineOperators();
        RegisterDeviceOperators();
    }

    private void RegisterMatrixOperators()
    {
        Def("matrix", i => i.Push(PsValue.Arr(MatrixToArray(null, PsMatrix.Identity))));
        Def("initmatrix", i => i.Graphics.State.Ctm = PsMatrix.Identity);
        Def("identmatrix", i => i.Push(PsValue.Arr(MatrixToArray(i.PopArray(), PsMatrix.Identity))));
        Def("defaultmatrix", i => i.Push(PsValue.Arr(MatrixToArray(i.PopArray(), PsMatrix.Identity))));
        Def("currentmatrix", i =>
            i.Push(PsValue.Arr(MatrixToArray(i.PopArray(), i.Graphics.State.Ctm))));
        Def("setmatrix", i => i.Graphics.State.Ctm = MatrixFromArray(i.PopArray()));
        Def("translate", i => ApplyOrStore(i, (x, y) => PsMatrix.Translation(x, y)));
        Def("scale", i => ApplyOrStore(i, (x, y) => PsMatrix.Scaling(x, y)));
        Def("rotate", RotateOperator);
        Def("concat", i => i.Graphics.State.Ctm = MatrixFromArray(i.PopArray()).Concat(i.Graphics.State.Ctm));
        Def("concatmatrix", ConcatMatrixOperator);
        Def("invertmatrix", i =>
        {
            var target = i.PopArray();
            var source = MatrixFromArray(i.PopArray());
            i.Push(PsValue.Arr(MatrixToArray(target, source.Invert())));
        });
        Def("transform", i => MapPoint(i, absolute: true, inverse: false));
        Def("itransform", i => MapPoint(i, absolute: true, inverse: true));
        Def("dtransform", i => MapPoint(i, absolute: false, inverse: false));
        Def("idtransform", i => MapPoint(i, absolute: false, inverse: true));
    }

    /// <summary><c>translate</c>, <c>scale</c> and <c>rotate</c> concatenate onto the
    /// CTM, unless a matrix operand is supplied, in which case they only fill it in.</summary>
    private static void ApplyOrStore(PsInterpreter i, Func<double, double, PsMatrix> build)
    {
        if (i.Peek().Type == PsType.Array)
        {
            var target = i.PopArray();
            var y = i.PopNumber();
            var x = i.PopNumber();
            i.Push(PsValue.Arr(MatrixToArray(target, build(x, y))));
            return;
        }

        var dy = i.PopNumber();
        var dx = i.PopNumber();
        i.Graphics.State.Ctm = build(dx, dy).Concat(i.Graphics.State.Ctm);
    }

    private static void RotateOperator(PsInterpreter i)
    {
        if (i.Peek().Type == PsType.Array)
        {
            var target = i.PopArray();
            i.Push(PsValue.Arr(MatrixToArray(target, PsMatrix.Rotation(i.PopNumber()))));
            return;
        }

        i.Graphics.State.Ctm = PsMatrix.Rotation(i.PopNumber()).Concat(i.Graphics.State.Ctm);
    }

    private static void ConcatMatrixOperator(PsInterpreter i)
    {
        var target = i.PopArray();
        var second = MatrixFromArray(i.PopArray());
        var first = MatrixFromArray(i.PopArray());
        i.Push(PsValue.Arr(MatrixToArray(target, first.Concat(second))));
    }

    /// <summary>Map a point or a distance through a matrix operand, or the CTM when
    /// none is given, in either direction.</summary>
    private static void MapPoint(PsInterpreter i, bool absolute, bool inverse)
    {
        var matrix = i.Peek().Type == PsType.Array
            ? MatrixFromArray(i.PopArray())
            : i.Graphics.State.Ctm;
        if (inverse) matrix = matrix.Invert();
        var y = i.PopNumber();
        var x = i.PopNumber();
        double outX, outY;
        if (absolute) matrix.Transform(x, y, out outX, out outY);
        else matrix.TransformDelta(x, y, out outX, out outY);
        i.Push(outX);
        i.Push(outY);
    }

    private void RegisterPathOperators()
    {
        Def("newpath", i => i.Graphics.Path.Clear());
        Def("currentpoint", CurrentPointOperator);
        Def("moveto", i => PointOperator(i, (g, x, y) => g.MoveTo(x, y)));
        Def("lineto", i => PointOperator(i, (g, x, y) => g.LineTo(x, y)));
        Def("rmoveto", i => RelativeOperator(i, (g, x, y) => g.MoveTo(x, y)));
        Def("rlineto", i => RelativeOperator(i, (g, x, y) => g.LineTo(x, y)));
        Def("curveto", i => CurveOperator(i, relative: false));
        Def("rcurveto", i => CurveOperator(i, relative: true));
        Def("arc", i => PsArcs.Arc(i, counterClockwise: true));
        Def("arcn", i => PsArcs.Arc(i, counterClockwise: false));
        Def("arct", i => PsArcs.ArcTo(i, leaveTangents: false));
        Def("arcto", i => PsArcs.ArcTo(i, leaveTangents: true));
        Def("closepath", i => i.Graphics.Path.ClosePath());
        Def("flattenpath", FlattenPathOperator);
        Def("reversepath", i => i.Graphics.SetPath(i.Graphics.Path.Reversed()));
        Def("strokepath", StrokePathOperator);
        Def("clippath", i => i.Graphics.SetPath(i.Graphics.ClipPathOutline()));
        Def("initclip", i => i.Graphics.InitClip());
        Def("clip", i => i.Graphics.Clip(evenOdd: false));
        Def("eoclip", i => i.Graphics.Clip(evenOdd: true));
        Def("rectclip", i => PsRects.RectOperator(i, PsRectAction.Clip));
        Def("pathbbox", PathBBoxOperator);
        Def("pathforall", PathForAllOperator);
        Def("setbbox", i =>
        {
            for (var k = 0; k < BoxOperandCount; k++) i.PopNumber();
        });
        Def("upath", PsUserPaths.Capture);
        Def("uappend", PsUserPaths.Append);
        Def("ucache", i => { });
    }

    /// <summary>How many numbers a bounding box is written with.</summary>
    private const int BoxOperandCount = 4;

    private static void CurrentPointOperator(PsInterpreter i)
    {
        if (!i.Graphics.TryGetCurrentPoint(out var x, out var y))
            throw new PsErrorSignal("nocurrentpoint");
        i.Push(x);
        i.Push(y);
    }

    private static void PointOperator(PsInterpreter i, Action<PsGraphics, double, double> add)
    {
        var y = i.PopNumber();
        var x = i.PopNumber();
        add(i.Graphics, x, y);
    }

    /// <summary>A relative move or line is measured from the current point in user
    /// space, so the device point is round-tripped back before the offset is added.</summary>
    private static void RelativeOperator(PsInterpreter i, Action<PsGraphics, double, double> add)
    {
        var dy = i.PopNumber();
        var dx = i.PopNumber();
        if (!i.Graphics.TryGetCurrentPoint(out var x, out var y))
            throw new PsErrorSignal("nocurrentpoint");
        add(i.Graphics, x + dx, y + dy);
    }

    private static void CurveOperator(PsInterpreter i, bool relative)
    {
        var y3 = i.PopNumber();
        var x3 = i.PopNumber();
        var y2 = i.PopNumber();
        var x2 = i.PopNumber();
        var y1 = i.PopNumber();
        var x1 = i.PopNumber();
        double ox = 0, oy = 0;
        if (relative && !i.Graphics.TryGetCurrentPoint(out ox, out oy))
            throw new PsErrorSignal("nocurrentpoint");
        i.Graphics.CurveTo(ox + x1, oy + y1, ox + x2, oy + y2, ox + x3, oy + y3);
    }

    /// <summary><c>flattenpath</c> cuts every curve into straight pieces no further
    /// from it than the flatness allows. The flatness is counted in DEVICE PIXELS
    /// while the path is held in points, so it is converted at the resolution the
    /// reference device works at — which is what decides how many pieces a program
    /// walking the result gets, and so how many characters it flows round a circle.</summary>
    private static void FlattenPathOperator(PsInterpreter i)
    {
        i.Graphics.SetPath(i.Graphics.Path.Flatten(
            i.Graphics.State.Flatness * PointsPerDevicePixel));
    }

    /// <summary>A device pixel measured in points. The reference device's resolution
    /// was read off the sample that counts the pieces — it flows one character of a
    /// string along each piece of a flattened circle, and the etalon's characters run
    /// out exactly where a 250-dot inch puts them. Every resolution from 240 to 265
    /// reproduces it; 270 and above cut too finely and the string runs out early.</summary>
    private const double PointsPerDevicePixel = 72.0 / 250.0;

    /// <summary>Replace the current path with the region a stroke of it would paint,
    /// so the program can fill that region, clip to it, or stroke its edge.</summary>
    private static void StrokePathOperator(PsInterpreter i)
    {
        var state = i.Graphics.State;
        // The path is held in device space, so the pen is measured there too.
        i.Graphics.SetPath(PsStrokeOutline.Outline(i.Graphics.Path,
            state.LineWidth * state.Ctm.ApproximateScale,
            state.LineCap, state.LineJoin, state.MiterLimit));
    }

    /// <summary>The current path's bounding box, reported in user space.</summary>
    private static void PathBBoxOperator(PsInterpreter i)
    {
        if (!i.Graphics.Path.TryGetBounds(out var minX, out var minY, out var maxX, out var maxY))
            throw new PsErrorSignal("nocurrentpoint");
        var inverse = i.Graphics.State.Ctm.Invert();
        double loX = double.MaxValue, loY = double.MaxValue;
        double hiX = double.MinValue, hiY = double.MinValue;
        var xs = new[] { minX, maxX, minX, maxX };
        var ys = new[] { minY, minY, maxY, maxY };
        for (var k = 0; k < xs.Length; k++)
        {
            inverse.Transform(xs[k], ys[k], out var ux, out var uy);
            loX = Math.Min(loX, ux);
            loY = Math.Min(loY, uy);
            hiX = Math.Max(hiX, ux);
            hiY = Math.Max(hiY, uy);
        }

        i.Push(loX);
        i.Push(loY);
        i.Push(hiX);
        i.Push(hiY);
    }

    /// <summary>Walk the path, running one of four procedures per segment kind, with
    /// the points handed back in user space.</summary>
    private static void PathForAllOperator(PsInterpreter i)
    {
        var close = i.PopProc();
        var curve = i.PopProc();
        var line = i.PopProc();
        var move = i.PopProc();
        var inverse = i.Graphics.State.Ctm.Invert();
        // Snapshot the segments: the procedures routinely build a NEW path as they
        // walk, and reading the live list while it grows would break the walk.
        var segments = new List<PsSegment>(i.Graphics.Path.Segments);
        try
        {
            foreach (var s in segments) WalkSegment(i, s, inverse, move, line, curve, close);
        }
        catch (PsExitSignal)
        {
        }
    }

    private static void WalkSegment(PsInterpreter i, PsSegment s, PsMatrix inverse,
        PsArray move, PsArray line, PsArray curve, PsArray close)
    {
        switch (s.Kind)
        {
            case PsSegmentKind.Move:
                PushUser(i, inverse, s.X, s.Y);
                i.ExecuteProcedure(move);
                return;
            case PsSegmentKind.Line:
                PushUser(i, inverse, s.X, s.Y);
                i.ExecuteProcedure(line);
                return;
            case PsSegmentKind.Curve:
                PushUser(i, inverse, s.X1, s.Y1);
                PushUser(i, inverse, s.X2, s.Y2);
                PushUser(i, inverse, s.X, s.Y);
                i.ExecuteProcedure(curve);
                return;
            default:
                i.ExecuteProcedure(close);
                return;
        }
    }

    private static void PushUser(PsInterpreter i, PsMatrix inverse, double x, double y)
    {
        inverse.Transform(x, y, out var ux, out var uy);
        // A path's coordinates are REALS, whole ones included: a program reading them
        // back out and printing them writes "594.0", not "594".
        i.Push(PsValue.Real(ux));
        i.Push(PsValue.Real(uy));
    }

    private void RegisterPaintOperators()
    {
        Def("stroke", i => i.Graphics.Paint(PsPaintMode.Stroke));
        Def("fill", i => i.Graphics.Paint(PsPaintMode.Fill));
        Def("eofill", i => i.Graphics.Paint(PsPaintMode.EoFill));
        Def("ufill", i => PsUserPaths.Paint(i, PsPaintMode.Fill));
        Def("ueofill", i => PsUserPaths.Paint(i, PsPaintMode.EoFill));
        Def("ustroke", i => PsUserPaths.Paint(i, PsPaintMode.Stroke));
        Def("ustrokepath", i => { });
        Def("rectfill", i => PsRects.RectOperator(i, PsRectAction.Fill));
        Def("rectstroke", i => PsRects.RectOperator(i, PsRectAction.Stroke));
        Def("erasepage", i => { });
        Def("shfill", i => i.Pop());
        Def("makepattern", PsPatterns.MakePattern);
        Def("setpattern", PsPatterns.SetPattern);
        Def("image", PsImages.ImageOperator);
        Def("imagemask", PsImages.ImageMaskOperator);
        Def("colorimage", PsImages.ColorImageOperator);
        Def("showpage", i => i.Graphics.ShowPage());
        Def("copypage", i => { });
    }

    private void RegisterColorOperators()
    {
        Def("setgray", i => SetColor(i, PsColorSpace.Gray, PsColor.FromGray(i.PopNumber())));
        Def("currentgray", i => i.Push(i.Graphics.State.Color.Red));
        Def("setrgbcolor", i =>
        {
            var b = i.PopNumber();
            var g = i.PopNumber();
            SetColor(i, PsColorSpace.Rgb, PsColor.FromRgb(i.PopNumber(), g, b));
        });
        Def("currentrgbcolor", i => PushComponents(i, PsColorSpace.Rgb));
        Def("sethsbcolor", i =>
        {
            var brightness = i.PopNumber();
            var saturation = i.PopNumber();
            SetColor(i, PsColorSpace.Rgb, PsColor.FromHsb(i.PopNumber(), saturation, brightness));
        });
        Def("currenthsbcolor", i => PushComponents(i, PsColorSpace.Rgb));
        Def("setcmykcolor", SetCmykOperator);
        Def("currentcmykcolor", i => PushComponents(i, PsColorSpace.Cmyk));
        Def("setcolor", SetColorOperator);
        Def("currentcolor", i => PushComponents(i, i.Graphics.State.Space));
        Def("setcolorspace", SetColorSpaceOperator);
        Def("currentcolorspace", i => i.Push(PsValue.Arr(PsColorSpaces.Describe(i.Graphics.State.Space))));
        Def("setoverprint", i => i.Graphics.State.Overprint = i.PopBool());
        Def("currentoverprint", i => i.Push(i.Graphics.State.Overprint));
    }

    /// <summary>Selecting a space records both which space it is and how many
    /// components lie under a pattern in it, since that is how many an uncoloured
    /// pattern takes when it is set.</summary>
    private static void SetColorSpaceOperator(PsInterpreter i)
    {
        var space = i.Pop();
        i.Graphics.State.Space = PsColorSpaces.Classify(space);
        i.Graphics.State.PatternBaseComponents = PsColorSpaces.ComponentCount(space);
        i.Graphics.State.Tint = PsColorSpaces.AsTintSpace(space);
    }

    private static void SetCmykOperator(PsInterpreter i)
    {
        var k = i.PopNumber();
        var y = i.PopNumber();
        var m = i.PopNumber();
        SetColor(i, PsColorSpace.Cmyk, PsColor.FromCmyk(i.PopNumber(), m, y, k));
    }

    /// <summary><c>setcolor</c> takes as many components as the current space wants.</summary>
    private static void SetColorOperator(PsInterpreter i)
    {
        if (i.Graphics.State.Tint is { } tint)
        {
            SetTintColor(i, tint);
            return;
        }

        switch (i.Graphics.State.Space)
        {
            case PsColorSpace.Cmyk:
                SetCmykOperator(i);
                return;
            case PsColorSpace.Rgb:
                var b = i.PopNumber();
                var g = i.PopNumber();
                SetColor(i, PsColorSpace.Rgb, PsColor.FromRgb(i.PopNumber(), g, b));
                return;
            case PsColorSpace.Pattern:
                // In the pattern space the colour IS a pattern, though this spelling
                // of it names the pattern alone and leaves any tint where it lies.
                PsPatterns.SetPatternColor(i);
                return;
            default:
                SetColor(i, PsColorSpace.Gray, PsColor.FromGray(i.PopNumber()));
                return;
        }
    }

    /// <summary>A tint is turned into a colour by the space's own procedure: the tints
    /// go on the stack, the procedure runs, and what it leaves is a colour in the space
    /// underneath. The space stays selected, so a following colour is read the same
    /// way.</summary>
    private static void SetTintColor(PsInterpreter i, PsColorSpaces.PsTintSpace tint)
    {
        var tints = new double[tint.Components];
        for (var k = tint.Components - 1; k >= 0; k--) tints[k] = i.PopNumber();
        foreach (var t in tints) i.Push(t);
        i.ExecuteProcedure(tint.Transform);
        var state = i.Graphics.State;
        var saved = state.Tint;
        state.Tint = null;
        try
        {
            state.Space = tint.Alternate;
            SetColorOperator(i);
        }
        finally
        {
            state.Tint = saved;
            state.Space = tint.Alternate;
        }
    }

    /// <summary>Setting a plain colour also drops the pattern the colour may have
    /// been, so a later paint is not still painted with it.</summary>
    private static void SetColor(PsInterpreter i, PsColorSpace space, PsColor color)
    {
        var state = i.Graphics.State;
        state.Color = color;
        state.Space = space;
        state.Tint = null;
        state.Pattern = null;
        state.PatternName = null;
        state.PatternComponents = null;
    }

    /// <summary>Read the current colour back in the space asked for, converting when
    /// it was set in a different one.</summary>
    private static void PushComponents(PsInterpreter i, PsColorSpace space)
    {
        var c = i.Graphics.State.Color;
        switch (space)
        {
            case PsColorSpace.Rgb:
                i.Push(c.Red);
                i.Push(c.Green);
                i.Push(c.Blue);
                return;
            case PsColorSpace.Cmyk:
                PushCmyk(i, c);
                return;
            case PsColorSpace.Pattern:
                i.Push(PsValue.Null);
                return;
            default:
                i.Push(c.Red);
                return;
        }
    }

    private static void PushCmyk(PsInterpreter i, PsColor c)
    {
        var black = 1 - Math.Max(c.Red, Math.Max(c.Green, c.Blue));
        var scale = 1 - black;
        if (scale <= 0)
        {
            i.Push(0);
            i.Push(0);
            i.Push(0);
            i.Push(1);
            return;
        }

        i.Push((scale - c.Red) / scale);
        i.Push((scale - c.Green) / scale);
        i.Push((scale - c.Blue) / scale);
        i.Push(black);
    }

    private void RegisterLineOperators()
    {
        Def("setlinewidth", i => i.Graphics.State.LineWidth = Math.Abs(i.PopNumber()));
        Def("currentlinewidth", i => i.Push(i.Graphics.State.LineWidth));
        Def("setlinecap", i => i.Graphics.State.LineCap = ClampStyle(i.PopInt()));
        Def("currentlinecap", i => i.Push(i.Graphics.State.LineCap));
        Def("setlinejoin", i => i.Graphics.State.LineJoin = ClampStyle(i.PopInt()));
        Def("currentlinejoin", i => i.Push(i.Graphics.State.LineJoin));
        Def("setmiterlimit", i => i.Graphics.State.MiterLimit = i.PopNumber());
        Def("currentmiterlimit", i => i.Push(i.Graphics.State.MiterLimit));
        Def("setflat", i => i.Graphics.State.Flatness = i.PopNumber());
        Def("currentflat", i => i.Push(i.Graphics.State.Flatness));
        Def("setstrokeadjust", i => i.Graphics.State.StrokeAdjust = i.PopBool());
        Def("currentstrokeadjust", i => i.Push(i.Graphics.State.StrokeAdjust));
        Def("setdash", SetDashOperator);
        Def("currentdash", CurrentDashOperator);
    }

    /// <summary>Cap and join styles run 0 to 2; anything else is clamped rather than
    /// faulted, because a stray value must not lose the rest of the page.</summary>
    private static int ClampStyle(int value) => value < 0 ? 0 : value > MaxLineStyle ? MaxLineStyle : value;

    private const int MaxLineStyle = 2;

    private static void SetDashOperator(PsInterpreter i)
    {
        var phase = i.PopNumber();
        var array = i.PopArray();
        var lengths = new List<double>(array.Length);
        var total = 0.0;
        for (var k = 0; k < array.Length; k++)
        {
            if (!array[k].IsNumber) throw new PsErrorSignal("typecheck");
            var v = Math.Abs(array[k].Number);
            lengths.Add(v);
            total += v;
        }

        // An all-zero dash array means a solid line, not an invisible one.
        i.Graphics.State.DashArray = total > 0 ? lengths.ToArray() : new double[0];
        i.Graphics.State.DashPhase = phase;
    }

    private static void CurrentDashOperator(PsInterpreter i)
    {
        var state = i.Graphics.State;
        var array = new PsArray(state.DashArray.Length);
        for (var k = 0; k < state.DashArray.Length; k++) array[k] = PsValue.Num(state.DashArray[k]);
        i.Push(PsValue.Arr(array));
        i.Push(state.DashPhase);
    }
}
