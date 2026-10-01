using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    /// <summary>The stages of the XFA content emit: one item.</summary>
    private static bool EmitXfaItem(Content.ContentStreamBuilder b, Item it, Page page)
    {
        if (it.Kind == "fill")
        {
            b.SaveState().SetFillColor(it.Color[0], it.Color[1], it.Color[2]);
            b.Rectangle(it.X, it.Y, it.W, it.H).Fill();
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
            b.SaveState().SetStrokeColor(0, 0, 0).SetLineWidth(0.6);
            b.Rectangle(it.X, it.Y, it.W, it.H).Stroke();
            b.RestoreState();
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
            if (it.Kind == "dot") b.SetFillColor(0, 0, 0); else b.SetStrokeColor(0, 0, 0).SetLineWidth(0.6);
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
        var unicodeShown = false;
        if (NeedsUnicodeFont(it.Text))
        {
            // Text WinAnsi can't encode (Hebrew, Cyrillic, CJK, …) is shown with
            // an embedded Identity-H Type0 font instead of collapsing to '?'.
            var ttf = Text.SystemFontResolver.Resolve(it.Bold ? "Arial,Bold" : "Arial")
                      ?? Text.SystemFontResolver.Resolve("Arial");
            var fontRes = PageFontDict(page);
            if (ttf is not null && fontRes is not null)
            {
                var (resName, hexGlyphs) = Text.Type0FontEmbedder.Embed(
                    fontRes, ttf, it.Bold ? "Arial-Bold" : "Arial", OrderForExtraction(it.Text));
                b.SetFont(resName, it.FontSize);
                b.MoveTextPosition(it.X, it.Y);
                b.ShowTextHex(hexGlyphs);
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
                + (it.Bold && it.Italic ? "-BoldItalic" : it.Bold ? "-Bold" : it.Italic ? "-Italic" : "");
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
            if (it.HScale != 1.0) b.SetHorizontalScaling(it.HScale * 100.0);
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
            }
        }
        catch { /* undecodable image — skip */ }
    }
}
