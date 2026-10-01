using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Removes from a page everything it paints inside an area, for good: the codes of the glyphs
/// shown there, the parts of paths that run there, the image samples there, and the same inside
/// the forms the page draws. What lies outside the area is left where it was: a removed glyph
/// becomes a show-array adjustment of its advance, a path is cut at the area's edges, an image
/// keeps its size and placement. The content is rewritten operation by operation, copying the
/// bytes of every operation it leaves alone.
/// <para>An image or form is never changed in place - another page or form may draw it too: the
/// page gets a redacted copy in its own copy of its resources, under the original's name when
/// the page draws it once, else under a new name for each place it is drawn.</para>
/// </summary>
internal sealed partial class ContentRedactor
{
    // A form drawing a form drawing a form ... is looked into this deep; a deeper form under
    // the area is removed whole.
    private const int MaxFormDepth = 12;
    // A matrix whose determinant is this small maps everything to a line or a point.
    private const double Singular = 1e-12;

    private readonly PdfReader _reader;
    // The area on the page, in the page's default space.
    private readonly (double X, double Y)[] _corners;
    private readonly ConvexRegion _area;
    // What image pixels in the area are painted, RGB from 0 to 1.
    private readonly (double R, double G, double B) _fill;
    // The MCIDs of the page's marked content something was removed from.
    private readonly HashSet<int> _redactedMcids = new();

    private ContentRedactor(PdfReader reader, Rectangle area, Color fill)
    {
        _reader = reader;
        _fill = (fill.R / 255.0, fill.G / 255.0, fill.B / 255.0);
        _corners = [(area.LLX, area.LLY), (area.URX, area.LLY), (area.URX, area.URY), (area.LLX, area.URY)];
        _area = ConvexRegion.FromPolygon(_corners)!;
    }

    /// <summary>Remove what the page paints inside <paramref name="area"/>; true when its content
    /// changed. Image pixels there take <paramref name="fill"/>. An area that encloses nothing (no
    /// width or no height) removes nothing.</summary>
    internal static bool Apply(Page page, Rectangle area, Color fill)
    {
        if (area.URX <= area.LLX || area.URY <= area.LLY) return false;
        var bytes = page.GetContentStreamBytes();
        if (bytes is null || bytes.Length == 0) return false;

        var redactor = new ContentRedactor(page.Reader, area, fill);
        var resources = new ResourceCopy(page.Reader, Tagged.PageContentScan.InheritedResources(page.Reader, page.Dict));
        var rewritten = redactor.Rewrite(bytes, resources, [1, 0, 0, 1, 0, 0], 0);
        if (rewritten is null) return false;

        if (resources.Copy is { } copy) page.Dict.Set("Resources", copy);
        page.SetContentStream(rewritten);
        ScrubStructure(page, redactor._redactedMcids);
        // A thumbnail is a picture of the page as it was, and private application data may hold a
        // whole copy of it.
        page.Dict.Remove("Thumb");
        page.Dict.Remove("PieceInfo");
        // The page's old content, and any image or form only it drew, are no longer reachable:
        // without this the saved file would carry them, and what was removed with them.
        page.Reader.MayHaveOrphansOnSave = true;
        return true;
    }

    /// <summary>One operation's bytes to replace, and what replaces them.</summary>
    private readonly record struct Edit(int Start, int End, byte[] Replacement);

    /// <summary>The operation being parsed: its name, operands and where its bytes start.</summary>
    private sealed class Operation
    {
        public string Name = string.Empty;
        public List<PdfObject> Operands = new();
        public int Start;
    }

    private sealed class Capture(Walk walk) : IInstructionHandler
    {
        public bool Handled(string instruction, IReadOnlyList<PdfObject> operands, GraphicsState state)
        {
            var current = new Operation { Name = instruction, Operands = operands.ToList(), Start = walk.PreviousEnd };
            walk.Current = current;
            walk.Text = instruction is "Tj" or "TJ" or "'" or "\"" ? new TextShow(current) : null;
            if (instruction is "m" or "re" or "l" or "c" or "v" or "y" or "h" && walk.PathStart < 0)
                walk.PathStart = walk.PreviousEnd;
            else if (instruction is "W" or "W*")
                walk.PathClip = instruction;
            return false;
        }
    }

    /// <summary>What a pass over one content stream keeps track of.</summary>
    private sealed class Walk(byte[] bytes, ResourceCopy resources, int depth)
    {
        public readonly byte[] Bytes = bytes;
        public readonly ResourceCopy Resources = resources;
        public readonly int Depth = depth;
        public readonly List<Edit> Edits = new();
        public Operation? Current;
        public int PreviousEnd;
        // Where the path being built started (-1: none), and the clip it installs.
        public int PathStart = -1;
        public string? PathClip;
        // The replacement of the path painted by the operation now ending, if it changes.
        public string? PathReplacement;
        public TextShow? Text;
        // The replacement of the image or form drawn by the operation now ending, if it changes.
        public string? DrawReplacement;
        // The XObjects some Do still draws as they were, and those a Do no longer draws there.
        public readonly HashSet<string> KeptNames = new();
        public readonly HashSet<string> ReplacedNames = new();
        // How many times each XObject is drawn, and the redacted copies drawn in place of one:
        // the original's name, the copy's, and where the Do starts.
        public readonly Dictionary<string, int> DrawCounts = new();
        public readonly List<(string Name, string CopyName, int Start)> Redrawn = new();
        // Whether a form drawn here takes its resources from this stream (then it may draw any of them).
        public bool FormsInheritResources;
        // The replacement of the inline image now ending, if it changes (empty: removed).
        public byte[]? InlineReplacement;
        // A show's removed glyphs at its end, not yet settled: the edit and what it keeps without them.
        public (int Edit, string Kept)? TrailingGap;
        // The marked content open where the walk is, and the MCIDs of marked content something was
        // removed from.
        public readonly List<OpenMark> OpenMarks = new();
        public readonly HashSet<int> RedactedMcids = new();
        // Whether a named property list was scrubbed (the content itself may then be unchanged).
        public bool PropertiesChanged;

        public int End(ContentStreamParser parser) => (int)Math.Min(parser.OperatorEnd, Bytes.Length);

        public void Replace(int start, int end, string replacement) =>
            Edits.Add(new Edit(start, end, Encoding.ASCII.GetBytes("\n" + replacement + "\n")));
    }

    /// <summary>The stream with what lies inside the area removed, or null when nothing it paints
    /// lies there. <paramref name="ctm"/> maps the stream's space to the page's.</summary>
    private byte[]? Rewrite(byte[] bytes, ResourceCopy resources, double[] ctm, int depth)
    {
        var walk = new Walk(bytes, resources, depth);
        var fonts = resources.Fonts();
        var parser = new ContentStreamParser(_reader) { Handler = new Capture(walk) };
        parser.State.Ctm = ctm;

        parser.OnTextShown += (_, shown, state) => walk.Text?.Strings.Add(new ShownString { Bytes = shown });
        parser.OnTextCodeAdvances += (ends, state) => MarkCodes(walk, ends, state, fonts);
        parser.OnPathPainted += (op, state, commands) => walk.PathReplacement = PlanPath(walk, op, state, commands);
        parser.OnImageDrawn += (name, state) => PlanXObject(walk, name, state);
        parser.OnInlineImage += (dict, data) => PlanInlineImage(walk, dict, data, parser.State);
        parser.OnInlineImageEnd += () => EndInlineImage(walk, walk.End(parser));
        parser.OnOperator += (op, _, state) => EndOperation(walk, op, walk.End(parser));

        parser.Parse(bytes, fonts, extGStates: resources.ExtGStates(),
            colorSpaces: resources.Dict("ColorSpace"), properties: resources.Dict("Properties"),
            patterns: resources.Dict("Pattern"));
        SettleTrailingGap(walk, null);
        foreach (var mark in walk.OpenMarks.Where(m => m.Redacted)) ScrubMark(walk, mark);
        if (depth == 0) _redactedMcids.UnionWith(walk.RedactedMcids);
        var renamed = KeepNamesDrawnOnce(walk);
        if (walk.Edits.Count == 0 && renamed.Count == 0 && !walk.PropertiesChanged) return null;
        // An image or form no Do draws any more leaves the resources too: named there, it would
        // still be saved, and everything it held with it.
        if (!walk.FormsInheritResources)
            foreach (var name in walk.ReplacedNames.Where(n => !walk.KeptNames.Contains(n) && !renamed.Contains(n)))
                resources.RemoveXObject(name);
        return Splice(bytes, walk.Edits);
    }

    /// <summary>A redacted copy of an image or form the stream draws only once takes the original's
    /// name in the stream's resources (its own copy of them), so the stream reads as before; the
    /// names it took over.</summary>
    private static HashSet<string> KeepNamesDrawnOnce(Walk walk)
    {
        var renamed = new HashSet<string>();
        foreach (var (name, copyName, start) in walk.Redrawn)
        {
            if (walk.DrawCounts[name] != 1) continue;
            walk.Resources.RenameXObject(copyName, name);
            walk.Edits.RemoveAll(e => e.Start == start);
            renamed.Add(name);
        }
        return renamed;
    }

    private static void EndOperation(Walk walk, string op, int end)
    {
        var current = walk.Current;
        SettleTrailingGap(walk, op);
        var edits = walk.Edits.Count;
        if (current is not null && walk.Text is { } show && show.Removes)
        {
            var (kept, trailing) = RewriteShow(show);
            walk.Replace(current.Start, end, trailing is null ? kept : kept + "\n" + trailing);
            if (trailing is not null) walk.TrailingGap = (walk.Edits.Count - 1, kept);
        }
        else if (op is "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "n")
        {
            if (walk.PathReplacement is { } replacement && walk.PathStart >= 0)
                walk.Replace(walk.PathStart, end, replacement);
            walk.PathStart = -1;
            walk.PathClip = null;
            walk.PathReplacement = null;
        }
        else if (current is not null && walk.DrawReplacement is { } drawn)
            walk.Replace(current.Start, end, drawn);
        walk.DrawReplacement = null;
        TrackMarks(walk, op, current, end, edited: walk.Edits.Count > edits);
        walk.Text = null;
        walk.Current = null;
        walk.PreviousEnd = end;
    }

    /// <summary>A show that ended with removed glyphs moved the pen past them. The next operation that
    /// uses the pen decides whether that move stays: another show on the same line needs it; one
    /// that places the text anew (a line move, a new text matrix, the end of the text object) does
    /// not, and the move goes - the extracted text then reads the removed glyphs as a gap, as it
    /// reads any jump of the pen. <paramref name="op"/> null: the stream ended.</summary>
    private static void SettleTrailingGap(Walk walk, string? op)
    {
        if (walk.TrailingGap is not { } gap) return;
        if (op is "Tj" or "TJ")
            walk.TrailingGap = null;
        else if (op is null or "Tm" or "Td" or "TD" or "T*" or "'" or "\"" or "ET" or "BT")
        {
            var edit = walk.Edits[gap.Edit];
            walk.Edits[gap.Edit] = edit with { Replacement = Encoding.ASCII.GetBytes("\n" + gap.Kept + "\n") };
            walk.TrailingGap = null;
        }
    }

    private static byte[] Splice(byte[] bytes, List<Edit> edits)
    {
        edits.Sort((a, b) => a.Start.CompareTo(b.Start));
        using var output = new MemoryStream();
        var at = 0;
        foreach (var edit in edits)
        {
            if (edit.Start < at) continue;
            output.Write(bytes, at, edit.Start - at);
            output.Write(edit.Replacement, 0, edit.Replacement.Length);
            at = edit.End;
        }
        output.Write(bytes, at, bytes.Length - at);
        return output.ToArray();
    }

    /// <summary>The area in the space <paramref name="ctm"/> maps to the page, or null when that
    /// space collapses (nothing drawn in it is visible).</summary>
    private ConvexRegion? AreaIn(double[] ctm)
    {
        var inverse = Invert(ctm);
        if (inverse is null) return null;
        return ConvexRegion.FromPolygon(_corners.Select(c => Transform(inverse, c.X, c.Y)).ToList());
    }

    /// <summary>How a quadrilateral given in page space meets the area.</summary>
    private Coverage CoverageOf(IReadOnlyList<(double X, double Y)> quad)
    {
        if (quad.All(p => _area.Contains(p.X, p.Y))) return Coverage.Inside;
        var inside = _area.Clip(quad);
        return inside.Count >= 3 && ConvexRegion.Area(inside) > 0 ? Coverage.Partly : Coverage.Outside;
    }

    private enum Coverage { Outside, Partly, Inside }

    private static (double X, double Y) Transform(double[] m, double x, double y) =>
        (m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]);

    private static double[]? Invert(double[] m)
    {
        var det = m[0] * m[3] - m[1] * m[2];
        if (Math.Abs(det) < Singular) return null;
        return
        [
            m[3] / det, -m[1] / det, -m[2] / det, m[0] / det,
            (m[2] * m[5] - m[3] * m[4]) / det, (m[1] * m[4] - m[0] * m[5]) / det,
        ];
    }

    private static string Number(double value) =>
        Math.Round(value, 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
