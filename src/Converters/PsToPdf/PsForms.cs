using System;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// The form operator. A form is a piece of artwork a program describes once and then
/// paints wherever it likes: a dictionary carrying its extent, its own matrix and the
/// procedure that draws it. Painting one runs that procedure under the form's matrix,
/// clipped to its box, with the graphics state put back afterwards.
/// </summary>
internal static class PsForms
{
    /// <summary>How many numbers a bounding box holds.</summary>
    private const int BoxLength = 4;

    /// <summary>How deep forms may nest before one is refused, so a form that paints
    /// itself cannot recurse without end.</summary>
    private const int MaxDepth = 8;

    /// <summary>Paint a form.</summary>
    public static void Execute(PsInterpreter i)
    {
        var form = i.PopDict();
        if (!form.TryGet("PaintProc", out var proc) || proc.AsArray is null) return;
        if (i.FormDepth >= MaxDepth) return;
        var graphics = i.Graphics;
        graphics.GSave();
        i.FormDepth++;
        try
        {
            ApplyMatrix(graphics, form);
            ApplyBox(graphics, form);
            i.Push(PsValue.Dict(form));
            i.Invoke(proc);
        }
        catch (PsErrorSignal e)
        {
            i.RecordError(e.ErrorName);
        }
        finally
        {
            i.FormDepth--;
            graphics.GRestore();
        }
    }

    /// <summary>The form's own matrix maps its space onto the space it is painted in.</summary>
    private static void ApplyMatrix(PsGraphics graphics, PsDictionary form)
    {
        if (!form.TryGet("Matrix", out var m) || m.AsArray is null) return;
        if (m.AsArray.Length < PsMatrix.ElementCount) return;
        graphics.State.Ctm = PsInterpreter.MatrixFromArray(m.AsArray).Concat(graphics.State.Ctm);
    }

    /// <summary>A form paints nothing outside its declared box.</summary>
    private static void ApplyBox(PsGraphics graphics, PsDictionary form)
    {
        if (!form.TryGet("BBox", out var b) || b.AsArray is null) return;
        var box = b.AsArray;
        if (box.Length < BoxLength) return;
        for (var k = 0; k < BoxLength; k++)
            if (!box[k].IsNumber)
                return;
        var saved = graphics.Path;
        var path = new PsPath();
        graphics.SetPath(path);
        graphics.MoveTo(box[0].Number, box[1].Number);
        graphics.LineTo(box[2].Number, box[1].Number);
        graphics.LineTo(box[2].Number, box[3].Number);
        graphics.LineTo(box[0].Number, box[3].Number);
        path.ClosePath();
        graphics.Clip(evenOdd: false);
        graphics.SetPath(saved);
    }
}
