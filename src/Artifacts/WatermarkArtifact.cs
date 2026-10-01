using System.IO;
using Aspose.Pdf.Content;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Represents a watermark artifact that can be added to a PDF page.
/// Artifacts are marked content sequences that allow PDF processors to
/// distinguish page content from non-content elements like watermarks.
/// </summary>
public partial class WatermarkArtifact : Artifact
{
    /// <summary>Creates an instance of a Watermark artifact.</summary>
    public WatermarkArtifact() : base(ArtifactType.Pagination, ArtifactSubtype.Watermark)
    {
        ArtifactHorizontalAlignment = HorizontalAlignment.Center;
        ArtifactVerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Source bitmap for an image watermark, used only when emitting the
    /// artifact. The round-tripped image is surfaced via the inherited
    /// <see cref="Artifact.Image"/> (an <see cref="XImage"/> over the embedded XObject).</summary>
    internal System.Drawing.Image? SourceImage { get; set; }

    /// <summary>
    /// Set the text and text state for the watermark.
    /// </summary>
    public new void SetTextAndState(string text, TextState state)
    {
        Text = text;
        TextState = state;
    }

    /// <summary>
    /// Set watermark text from a FormattedText object (Facades API-style helper).
    /// </summary>
    public new void SetText(Aspose.Pdf.Facades.FormattedText formattedText)
    {
        if (formattedText is null) return;
        Text = formattedText.Text;
        TextState = new TextState
        {
            FontName = formattedText.FontName,
            FontSize = (float)formattedText.FontSize,
            ForegroundColor = formattedText.ForegroundColor,
        };
    }

    /// <summary>
    /// Build the content stream for rendering this artifact on a page.
    /// </summary>
    internal byte[] BuildContentStream(Page page) => BuildContentStream(page, "F1");

    internal byte[] BuildContentStream(Page page, string fontResourceName)
    {
        var wc = new WatermarkContentState();
        wc.page = page;
        wc.fontResourceName = fontResourceName;
        wc.renderText = Text!;
        if (!string.IsNullOrEmpty(wc.renderText) && !string.IsNullOrEmpty(PageNumberReplacementString))
            wc.renderText = wc.renderText.Replace(PageNumberReplacementString, wc.page.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (string.IsNullOrEmpty(wc.renderText)) return [];

        wc.pageWidth = wc.page.Width;
        wc.pageHeight = wc.page.Height;
        wc.fontSize = TextState?.FontSize ?? 12;

        wc.charWidth = wc.fontSize * 0.5; // approximate
        wc.textWidth = wc.renderText!.Length * wc.charWidth;
        wc.textHeight = wc.fontSize;

        PlaceWatermark(wc);

        wc.bbox = ComputeBBox(wc.x, wc.y, wc.textWidth, wc.textHeight);
        Rectangle = wc.bbox;

        wc.builder = new ContentStreamBuilder();
        wc.builder.SaveState();

        // Compensate for the page's /Rotate. The position/alignment above is
        // computed in visual (display) coordinates via page.Width/page.Height,
        // which already account for rotation; this matrix maps those visual
        // coordinates into the page's raw content space so the watermark lands
        // at the intended visual location and reads upright (not rotated 90°/
        // mirrored) on /Rotate 90/180/270 pages.
        ApplyPageRotation(wc.builder, wc.page);

        ApplyWatermarkPaint(wc);

        wc.ci = System.Globalization.CultureInfo.InvariantCulture;
        wc.bboxStr = $"[{wc.bbox.LLX.ToString("0.##", wc.ci)} {wc.bbox.LLY.ToString("0.##", wc.ci)} {wc.bbox.URX.ToString("0.##", wc.ci)} {wc.bbox.URY.ToString("0.##", wc.ci)}]";
        wc.dict = $"<</Type /{Type} /Subtype /{Subtype} /BBox {wc.bboxStr}>>";
        wc.builder.BeginMarkedContentWithProps("Artifact", wc.dict);

        ShowWatermarkText(wc);

        wc.builder.EndMarkedContent();
        wc.builder.RestoreState();

        return wc.builder.Build();
    }

    private static Rectangle ComputeBBox(double x, double y, double width, double height)
    {
        return new Rectangle(x, y, x + width, y + height);
    }

    /// <summary>Prepend a matrix that maps visual (display) coordinates into the
    /// page's raw content space, compensating for the page's /Rotate so a
    /// watermark positioned via page.Width/page.Height appears at the intended
    /// visual location and upright. Identity for an unrotated page.</summary>
    internal static void ApplyPageRotation(ContentStreamBuilder builder, Page page)
    {
        var mb = page.MediaBox;
        double wm = mb.Width, hm = mb.Height;
        switch (page.Rotate)
        {
            case Aspose.Pdf.Rotation.on90: builder.SetMatrix(0, 1, -1, 0, wm, 0); break;
            case Aspose.Pdf.Rotation.on180: builder.SetMatrix(-1, 0, 0, -1, wm, hm); break;
            case Aspose.Pdf.Rotation.on270: builder.SetMatrix(0, -1, 1, 0, 0, hm); break;
            default: break; // None / on360 — identity
        }
    }

    /// <summary>The raw-content-space `cm` operands compensating for the page's
    /// /Rotate, or null when no rotation. Used by the inline (string-built)
    /// image-watermark path.</summary>
    /// <summary>The /Rotate compensation as raw matrix components [a b c d e f],
    /// or null when the page is unrotated.</summary>
    internal static double[]? PageRotationMatrix(Page page)
    {
        var mb = page.MediaBox;
        return page.Rotate switch
        {
            Aspose.Pdf.Rotation.on90 => new double[] { 0, 1, -1, 0, mb.Width, 0 },
            Aspose.Pdf.Rotation.on180 => new double[] { -1, 0, 0, -1, mb.Width, mb.Height },
            Aspose.Pdf.Rotation.on270 => new double[] { 0, -1, 1, 0, 0, mb.Height },
            _ => null,
        };
    }

    internal static string? PageRotationCm(Page page)
    {
        var mb = page.MediaBox;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.####", ci);
        return page.Rotate switch
        {
            Aspose.Pdf.Rotation.on90 => $"0 1 -1 0 {F(mb.Width)} 0 cm",
            Aspose.Pdf.Rotation.on180 => $"-1 0 0 -1 {F(mb.Width)} {F(mb.Height)} cm",
            Aspose.Pdf.Rotation.on270 => $"0 -1 1 0 0 {F(mb.Height)} cm",
            _ => null,
        };
    }

    /// <summary>Embed <see cref="Image"/> as an image XObject and place it inside an
    /// /Artifact marked-content block tagged /Subtype /Watermark, so it round-trips
    /// through <see cref="ArtifactCollection"/> as a watermark.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void AddImageWatermark(Page page)
    {
        byte[] png;
        using (var ms = new MemoryStream())
        {
            SourceImage!.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            png = ms.ToArray();
        }
        var stamp = ImageStamp.FromPngData(png);
        int iw = stamp.PixelWidth, ih = stamp.PixelHeight;
        var imgName = stamp.RegisterXObject(page);

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.##", ci);
        double x = (page.Width - iw) / 2, y = (page.Height - ih) / 2;
        var bbox = $"[{F(x)} {F(y)} {F(x + iw)} {F(y + ih)}]";
        Rectangle = new Rectangle(x, y, x + iw, y + ih);

        var sb = new System.Text.StringBuilder();
        sb.Append("q\n");
        // Compensate for the page's /Rotate so the centred image lands at the
        // intended visual location (placement above uses page.Width/page.Height,
        // which are visual dimensions).
        if (PageRotationCm(page) is { } rot) sb.Append(rot).Append('\n');
        if (Opacity < 1.0)
        {
            var gs = new ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity };
            sb.Append($"/{page.AddExtGState(gs)} gs\n");
        }
        sb.Append($"/Artifact <</Type /{Type} /Subtype /{Subtype} /BBox {bbox}>> BDC\n");
        sb.Append($"q {F(iw)} 0 0 {F(ih)} {F(x)} {F(y)} cm /{imgName} Do Q\n");
        sb.Append("EMC\nQ\n");
        var content = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
        if (IsBackground) page.PrependContentStream(content);
        else page.AddContentStream(content);
    }

    /// <summary>
    /// Add this artifact to a page.
    /// </summary>
    public void AddToPage(Page page)
    {
        if (SourceImage is not null && Compat.IsWindows())
        {
            AddImageWatermark(page);
            return;
        }
        // Register the face the watermark is written with. RegisterFont may return
        // a name other than "F1" when the page's existing resources already use that
        // slot — a page may reserve /F1 for an embedded subset that lacks our
        // watermark glyphs (g/p/q/y), so emitting SetFont("F1", ...) into our content
        // stream renders the text invisibly.
        var fontName = Table.RegisterFont(page, WrittenFace);

        var content = BuildTextWatermark(page, fontName);
        if (IsBackground)
            page.PrependContentStream(content);
        else
        {
            // A foreground watermark must not inherit the page content's residual
            // CTM (a printout's top-level flip matrix mirrors and shrinks it).
            page.WrapExistingContentInGraphicsState();
            page.AddContentStream(content);
        }
    }

    /// <summary>The Standard-14 face the watermark is written with: the text state's
    /// family and style mapped onto the core faces (Arial + Bold is Helvetica-Bold, a
    /// Courier watermark stays Courier); Helvetica without a text state.</summary>
    private string WrittenFace =>
        TextState is null ? "Helvetica" : TextBuilder.MapToStandard14Public(TextState);

    /// <summary>The distance between the baselines of stacked watermark lines: the
    /// named face's hhea ascent + descent + line gap when the repository holds it (Arial:
    /// 1.15 em), else the written face's ascent + descent (the AFM faces carry no gap).</summary>
    private static double LinePitch(string baseFont, double fontSize, double ascent, double descent)
    {
        var ttf = FontRepository.FindFontData(baseFont)?.TtfData;
        if (ttf is { Length: > 12 } && FontRepository.ReadTtfHheaExtent(ttf) is { } extent)
            return (extent.ascent + extent.descent + extent.lineGap) * fontSize / 1000.0;
        return ascent + descent;
    }

    /// <summary>Emit the text watermark with its glyphs inside a Form XObject:
    /// the page-level block is a clean <c>q … /Artifact «props» BDC /FrmN Do EMC Q</c>
    /// and the text (colour + BT…ET) lives in the form. Keeping the drawing in a
    /// form lets callers walk the page's Do operators and pull the watermark text
    /// out of <c>Resources.Forms[name]</c>.</summary>
    private byte[] BuildTextWatermark(Page page, string fontResourceName)
    {
        var tw = new TextWatermarkBuildState();
        tw.page = page;
        tw.fontResourceName = fontResourceName;
        tw.renderText = Text;
        if (!string.IsNullOrEmpty(tw.renderText) && !string.IsNullOrEmpty(PageNumberReplacementString))
            tw.renderText = tw.renderText.Replace(PageNumberReplacementString, tw.page.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (string.IsNullOrEmpty(tw.renderText)) return [];

        tw.pageWidth = tw.page.Width;
        tw.pageHeight = tw.page.Height;
        tw.fontSize = TextState?.FontSize ?? 12;
        tw.baseFont = TextState?.FontName ?? TextState?.Font?.FontName ?? "Helvetica";

        tw.lines = tw.renderText!.Replace("\r\n", "\n").Split('\n');
        tw.writtenFace = WrittenFace;
        tw.textWidth = 0;
        foreach (var line in tw.lines) tw.textWidth = Math.Max(tw.textWidth, MeasureTextWidth(line, tw.writtenFace, tw.fontSize));

        tw.ascent = Math.Abs(Standard14Fonts.GetWrittenFaceAscent(tw.baseFont)) * tw.fontSize / 1000.0;
        tw.descent = Math.Abs(Standard14Fonts.GetWrittenFaceDescent(tw.baseFont)) * tw.fontSize / 1000.0;
        if (tw.ascent <= 0) tw.ascent = tw.fontSize * 0.75;
        if (tw.descent <= 0) tw.descent = tw.fontSize * 0.2;
        tw.pitch = LinePitch(tw.baseFont, tw.fontSize, tw.ascent, tw.descent);
        tw.textHeight = tw.ascent + tw.descent + (tw.lines.Length - 1) * tw.pitch;

        // An explicit Position gives the text BOX floor; the baseline of the last
        // line sits one descent above it.
        if (Position is { } pos) { tw.x = pos.X; tw.y = pos.Y + tw.descent; }
        else
        {
            tw.x = ArtifactHorizontalAlignment switch
            {
                HorizontalAlignment.Left => LeftMargin > 0 ? LeftMargin : 36,
                HorizontalAlignment.Right => tw.pageWidth - tw.textWidth - (RightMargin > 0 ? RightMargin : 36),
                _ => (tw.pageWidth - tw.textWidth) / 2,
            };
            // Baseline position: the centred case centres the ascent+descent box
            // and sets the baseline one descent above its floor.
            tw.y = ArtifactVerticalAlignment switch
            {
                VerticalAlignment.Top => tw.pageHeight - tw.fontSize - (TopMargin > 0 ? TopMargin : 36),
                VerticalAlignment.Bottom => BottomMargin > 0 ? BottomMargin : 36,
                _ => (tw.pageHeight - tw.textHeight) / 2 + tw.descent,
            };
        }

        tw.bbox = ComputeBBox(tw.x, tw.y - tw.descent, tw.textWidth, tw.textHeight);
        Rectangle = tw.bbox;

        tw.ci = System.Globalization.CultureInfo.InvariantCulture;
        tw.inner = new System.Text.StringBuilder();
        tw.fg = TextState?.ForegroundColor;
        tw.inner.Append(tw.fg is { } c
            ? $"{WatermarkNum(tw, c.R / 255.0)} {WatermarkNum(tw, c.G / 255.0)} {WatermarkNum(tw, c.B / 255.0)} rg\n"
            : "0 0 0 rg\n");
        tw.inner.Append("BT\n");
        tw.inner.Append($"/{tw.fontResourceName} {WatermarkNum(tw, tw.fontSize)} Tf\n");
        tw.rotationCm = null;
        PlaceTextWatermarkLines(tw);
        tw.inner.Append("ET\n");

        tw.formName = tw.page.AddStampForm(System.Text.Encoding.ASCII.GetBytes(tw.inner.ToString()));
        ComposeTextWatermarkForm(tw);
        tw.bboxStr = $"[{tw.bbox.LLX.ToString("0.##", tw.ci)} {tw.bbox.LLY.ToString("0.##", tw.ci)} {tw.bbox.URX.ToString("0.##", tw.ci)} {tw.bbox.URY.ToString("0.##", tw.ci)}]";
        tw.sb.Append($"/Artifact <</Type /{Type} /Subtype /{Subtype} /BBox {tw.bboxStr}>> BDC\n");
        tw.sb.Append($"/{tw.formName} Do\n");
        tw.sb.Append("EMC\nQ\n");
        return System.Text.Encoding.ASCII.GetBytes(tw.sb.ToString());
    }

    private static string EscapeTextLiteral(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", "").Replace("\n", " ");
}
