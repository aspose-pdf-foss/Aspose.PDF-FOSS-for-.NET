using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    /// <summary>The stages of the XFA content emit: one item.</summary>
    private static bool EmitXfaItem(Content.ContentStreamBuilder b, Item it, Page page)
    {
        if (it.Matrix is { } turned)
        {
            // An item of a turned box is drawn upright under the matrix that turns it.
            b.SaveState().SetMatrix(turned[0], turned[1], turned[2], turned[3], turned[4], turned[5]);
            it.Matrix = null;
            EmitXfaItem(b, it, page);
            it.Matrix = turned;
            b.RestoreState();
            return true;
        }
        if (it.Kind == "fill")
        {
            b.SaveState().SetFillColor(it.Color[0], it.Color[1], it.Color[2]);
            BoxPath(b, it);
            b.Fill();
            b.RestoreState();
        }
        else if (it.Kind == "line")
        {
            b.SaveState().SetStrokeColor(0, 0, 0).SetLineWidth(0.5);
            b.MoveTo(it.X, it.Y).LineTo(it.X + it.W, it.Y).Stroke();
            b.RestoreState();
        }
        else if (it.Kind == "box")
        {
            b.SaveState().SetStrokeColor(it.Color[0], it.Color[1], it.Color[2]).SetLineWidth(0.6);
            BoxPath(b, it);
            b.Stroke();
            b.RestoreState();
        }
        else if (it.Kind == "mark")
        {
            EmitMark(b, it);
        }
        else if (it.Kind == "diag")
        {
            // Diagonal rule: slope "/" (Stretch=true) runs bottom-left to
            // top-right; the default runs top-left to bottom-right.
            b.SaveState().SetStrokeColor(it.Color[0], it.Color[1], it.Color[2])
             .SetLineWidth(it.FontSize > 0 ? it.FontSize : 1);
            if (it.Stretch)
                b.MoveTo(it.X, it.Y).LineTo(it.X + it.W, it.Y + it.H);
            else
                b.MoveTo(it.X, it.Y + it.H).LineTo(it.X + it.W, it.Y);
            b.Stroke();
            b.RestoreState();
        }
        else if (it.Kind is "circle" or "dot")
        {
            // Circle via four Bézier quadrants; "dot" is filled (radio selection).
            double r = it.W / 2, cx = it.X + r, cy = it.Y + r, k = r * 0.5523;
            b.SaveState();
            if (it.Kind == "dot") b.SetFillColor(0, 0, 0); else b.SetStrokeColor(it.Color[0], it.Color[1], it.Color[2]).SetLineWidth(0.6);
            b.MoveTo(cx + r, cy)
             .CurveTo(cx + r, cy + k, cx + k, cy + r, cx, cy + r)
             .CurveTo(cx - k, cy + r, cx - r, cy + k, cx - r, cy)
             .CurveTo(cx - r, cy - k, cx - k, cy - r, cx, cy - r)
             .CurveTo(cx + k, cy - r, cx + r, cy - k, cx + r, cy);
            if (it.Kind == "dot") b.Fill(); else b.Stroke();
            b.RestoreState();
        }
        else if (it.Kind == "image" && it.ImageData is not null)
        {
            EmitXfaImageItem(b, it, page);
        }
        else if (it.Kind == "text")
        {
            EmitXfaTextItem(b, it, page);
        }
        return true;
    }

    /// <summary></summary>
    private static void EmitXfaTextItem(Content.ContentStreamBuilder b, Item it, Page page)
    {
        b.SaveState().SetFillColor(it.Color[0], it.Color[1], it.Color[2]);
        b.BeginText();
        // A face set condensed or widened keeps that scale whichever font shows it.
#pragma warning disable S1244 // only the exact default value is skipped - any other value is written as is
        if (it.HScale != 1.0) b.SetHorizontalScaling(it.HScale * 100.0);
#pragma warning restore S1244
        var unicodeShown = false;
        if (NeedsUnicodeFont(it.Text))
        {
            // Text WinAnsi can't encode (Hebrew, Cyrillic, CJK, …) is shown with
            // an embedded Identity-H Type0 font instead of collapsing to '?'.
            // A line Arial has no glyphs for (CJK) is shown in an installed face that has them.
            var face = CoveringUnicodeFace(it.Text, it.Bold);
            var fontRes = PageFontDict(page);
            if (face is var (ttf, faceName) && fontRes is not null)
            {
                var runs = DirectionalRuns(it.Text) ?? [OrderForExtraction(it.Text)];
                var shown = runs.Select(run => Text.Type0FontEmbedder.Embed(fontRes, ttf, faceName, run)).ToList();
                b.SetFont(shown[0].resName, it.FontSize);
                b.MoveTextPosition(it.X, it.Y);
                foreach (var (_, hexGlyphs) in shown) b.ShowTextHex(hexGlyphs);
                unicodeShown = true;
            }
        }
        // A resolvable non-default template face (Verdana, Tahoma …) is
        // embedded so painted advances equal the widths the wrap measured.
        if (!unicodeShown && it.Family is { } fam
            && FamilyFont(fam, it.Bold, it.Italic) is { } famf
            && PageFontDict(page) is { } famFd)
        {
            var famName = fam.Replace(" ", "")
                + ((it.Bold, it.Italic) switch { (true, true) => "-BoldItalic", (true, _) => "-Bold", (_, true) => "-Italic", _ => "" });
            var (resName, hexGlyphs) = Text.Type0FontEmbedder.Embed(famFd, famf.ttf, famName, it.Text);
            b.SetFont(resName, it.FontSize);
            if (it.CharSpacing != 0) b.SetCharSpacing(it.CharSpacing);
            b.MoveTextPosition(it.X, it.Y);
            b.ShowTextHex(hexGlyphs);
            unicodeShown = true;
        }
        // A rich-text run renders with the real (embedded) system font so drawn
        // glyph shapes and advances match the widths the wrap was measured with.
        if (!unicodeShown && it.Rich && RtFont(it.Serif, it.Bold, it.Italic) is { } rtf
            && PageFontDict(page) is { } fd)
        {
            var (resName, hexGlyphs) = Text.Type0FontEmbedder.Embed(
                fd, rtf.ttf, RtFontName(it.Serif, it.Bold, it.Italic), it.Text);
            b.SetFont(resName, it.FontSize);
            if (it.CharSpacing != 0) b.SetCharSpacing(it.CharSpacing);
            b.MoveTextPosition(it.X, it.Y);
            b.ShowTextHex(hexGlyphs);
            unicodeShown = true;
        }
        if (!unicodeShown)
        {
            b.SetFont(StandardFontRes(page, it.Serif, it.Bold, it.Italic), it.FontSize);
            if (it.CharSpacing != 0) b.SetCharSpacing(it.CharSpacing);
            b.MoveTextPosition(it.X, it.Y);
            b.ShowText(ToWinAnsi(it.Text));
        }
        b.EndText();
        b.RestoreState();
    }

    /// <summary></summary>
    private static void EmitXfaImageItem(Content.ContentStreamBuilder b, Item it, Page page)
    {
        var imageData = it.ImageData!;   // the caller enters only for an item with image bytes
        try
        {
            page.Resources.Images.Add(new System.IO.MemoryStream(imageData));
            var name = page.Resources.Images[page.Resources.Images.Count].Name;
            if (name is not null)
            {
                // Default XFA aspect ("fit"): preserve the image's natural
                // ratio inside the box, anchored at the box's top-left.
                double dw = it.W, dh = it.H, dy = it.Y;
                if (!it.Stretch
                    && Document.TryGetImageNaturalSizePt(imageData) is (var nw, var nh)
                    && nw > 0 && nh > 0)
                {
                    var s = Math.Min(it.W / nw, it.H / nh);
                    dw = nw * s; dh = nh * s;
                    dy = it.Y + it.H - dh;
                }
                b.SaveState().SetMatrix(dw, 0, 0, dh, it.X, dy);
                b.DrawXObject(name);
                b.RestoreState();
                if (it.Field is { } field)
                {
                    // The field's widget shows the very image object the page draws, drawn where the page draws it.
                    field.Image = page.Resources.Images[page.Resources.Images.Count].Stream;
                    field.ImageName = name;
                    field.ImageAppearance = new Content.ContentStreamBuilder().SaveState().SetMatrix(dw, 0, 0, dh, it.X, dy).DrawXObject(name).RestoreState().Build();
                }
            }
        }
        catch { /* undecodable image — skip */ }
    }
}
