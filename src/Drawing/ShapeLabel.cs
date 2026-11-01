using Aspose.Pdf.Content;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Drawing;

/// <summary>Draws a shape's <see cref="Shape.Text"/> label inside the box the shape spans, in the label's standard
/// face, size and colour: wrapped by words to the box width (a word wider than the box broken by characters), each
/// line placed by its horizontal alignment (centred when it names none), the lines a font size apart and centred as
/// a block on the box's height, as many as the box height holds - the rest dropped - and clipped to the box's right
/// edge, as the reference draws them (measured).</summary>
internal static class ShapeLabel
{
    // The size a label is set at when its text state names none.
    private const double DefaultLabelFontPt = 12;

    private const double PerMille = 1000.0;

    /// <summary>The clip a label's block stands in, per line, in font sizes (measured: 11.6 for 10 pt).</summary>
    private const double ClipLineHeightEms = 1.16;

    /// <summary>Emit <paramref name="shape"/>'s label in the graph's local space; nothing for a
    /// shape without one, without a box, or when no page is there to hold the face.</summary>
    internal static void Render(ContentStreamBuilder builder, Page? page, Shape shape)
    {
        if (page is null || shape.Text is not { } label || string.IsNullOrEmpty(label.Text)) return;
        if (Box(shape) is not var (left, bottom, width, height)) return;
        var state = label.TextState;
        var size = state.FontSize > 0 ? state.FontSize : DefaultLabelFontPt;
        var face = Face(state);
        var lines = Wrap(label.Text, face, size, width);
        var capacity = Math.Max(1, (int)Math.Floor(height / size));
        if (lines.Count > capacity) lines.RemoveRange(capacity, lines.Count - capacity);
        // (a label that names no alignment centres)
        var alignment = state.HorizontalAlignmentTouched ? state.HorizontalAlignment : HorizontalAlignment.Center;
        var xs = lines.Select(line => alignment switch
        {
            HorizontalAlignment.Left or HorizontalAlignment.Justify or HorizontalAlignment.FullJustify => left,
            HorizontalAlignment.Right => left + width - TextWidth(line, face, size),
            _ => left + (width - TextWidth(line, face, size)) / 2,
        }).ToList();
        // (the block of lines stands centred on the box's middle, its last baseline one descent above its bottom)
        var blockBottom = bottom + (height - lines.Count * size) / 2;
        var lastBaseline = blockBottom - Standard14Fonts.GetDescent(face) * size / PerMille;
        var clipLeft = xs.Min();
        builder.SaveState().Rectangle(clipLeft, blockBottom, left + width - clipLeft, lines.Count * size * ClipLineHeightEms).Clip();
        var colour = state.ForegroundColor;
        builder.BeginText().SetFont(Table.RegisterFont(page, face), size);
        if (colour is not null && !colour.IsEmpty)
            builder.SetFillColor(colour.R / 255.0, colour.G / 255.0, colour.B / 255.0);
        else
            builder.SetFillColor(0, 0, 0);
        double penX = 0, penY = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var y = lastBaseline + (lines.Count - 1 - i) * size;
            builder.MoveTextPosition(xs[i] - penX, y - penY).ShowText(lines[i]);
            (penX, penY) = (xs[i], y);
        }
        builder.EndText().RestoreState();
    }

    /// <summary>The label's lines: words packed greedily into the width, a word wider than it broken by characters.</summary>
    private static List<string> Wrap(string text, string face, double size, double width)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (TextWidth(candidate, face, size) <= width) { current = candidate; continue; }
            if (current.Length > 0) lines.Add(current);
            current = word;
            // A word wider than the box: it is cut into pieces that each fit.
            while (TextWidth(current, face, size) > width && current.Length > 1)
            {
                var fit = 1;
                while (fit < current.Length && TextWidth(current.Substring(0, fit + 1), face, size) <= width) fit++;
                lines.Add(current.Substring(0, fit));
                current = current.Substring(fit);
            }
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    private static double TextWidth(string text, string face, double size)
    {
        var width = 0.0;
        foreach (var ch in text)
        {
            var w = Standard14Fonts.GetWidth(face, ch);
            width += (w > 0 ? w : Standard14Fonts.GetDefaultWidth(face)) * size / PerMille;
        }
        return width;
    }

    /// <summary>The standard face the label's state asks for, Helvetica for a face that is no
    /// standard one.</summary>
    private static string Face(TextState state)
    {
        var name = state.Font?.FontName ?? state.FontName;
        var styled = FontRepository.StandardStyledName(string.IsNullOrEmpty(name) ? "Helvetica" : name!,
            state.IsBold, state.IsItalic);
        return Standard14Fonts.IsStandard14(styled) ? styled
            : FontRepository.StandardStyledName("Helvetica", state.IsBold, state.IsItalic);
    }

    /// <summary>The box a shape spans in the graph's local space; null for a shape that
    /// carries no label box.</summary>
    private static (double left, double bottom, double width, double height)? Box(Shape shape) => shape switch
    {
        Rectangle r => (r.Left, r.Bottom, r.Width, r.Height),
        Ellipse e => (e.Left, e.Bottom, e.Width, e.Height),
        Circle c => (c.CenterX - c.Radius, c.CenterY - c.Radius, 2 * c.Radius, 2 * c.Radius),
        _ => null,
    };
}
