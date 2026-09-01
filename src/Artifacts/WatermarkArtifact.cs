using System.IO;
using Aspose.Pdf.Content;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Represents a watermark artifact that can be added to a PDF page.
/// Artifacts are marked content sequences that allow PDF processors to
/// distinguish page content from non-content elements like watermarks.
/// </summary>
public class WatermarkArtifact : Artifact
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
        // Apply page-number substitution: replace the configured token with the
        // 1-based page number. A null/empty token disables substitution.
        var renderText = Text;
        if (!string.IsNullOrEmpty(renderText) && !string.IsNullOrEmpty(PageNumberReplacementString))
            renderText = renderText.Replace(PageNumberReplacementString, page.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (string.IsNullOrEmpty(renderText)) return [];

        var pageWidth = page.Width;
        var pageHeight = page.Height;
        var fontSize = TextState?.FontSize ?? 12;

        // Estimate text dimensions
        var charWidth = fontSize * 0.5; // approximate
        var textWidth = renderText!.Length * charWidth;
        var textHeight = fontSize;

        // Calculate position based on alignment / explicit Position / margins
        double x, y;
        if (Position is { } pos)
        {
            x = pos.X;
            y = pos.Y;
        }
        else
        {
            switch (ArtifactHorizontalAlignment)
            {
                case HorizontalAlignment.Left:
                    x = LeftMargin > 0 ? LeftMargin : 36; break;
                case HorizontalAlignment.Right:
                    x = pageWidth - textWidth - (RightMargin > 0 ? RightMargin : 36); break;
                default: // Center / None
                    x = (pageWidth - textWidth) / 2; break;
            }
            switch (ArtifactVerticalAlignment)
            {
                case VerticalAlignment.Top:
                    y = pageHeight - fontSize - (TopMargin > 0 ? TopMargin : 36); break;
                case VerticalAlignment.Bottom:
                    y = BottomMargin > 0 ? BottomMargin : 36; break;
                default: // Center / None
                    y = (pageHeight - textHeight) / 2; break;
            }
        }

        // Compute bounding box for /BBox in the BDC properties dict.
        var bbox = ComputeBBox(x, y, textWidth, textHeight);
        Rectangle = bbox;

        var builder = new ContentStreamBuilder();
        builder.SaveState();

        // Compensate for the page's /Rotate. The position/alignment above is
        // computed in visual (display) coordinates via page.Width/page.Height,
        // which already account for rotation; this matrix maps those visual
        // coordinates into the page's raw content space so the watermark lands
        // at the intended visual location and reads upright (not rotated 90°/
        // mirrored) on /Rotate 90/180/270 pages.
        ApplyPageRotation(builder, page);

        // Apply opacity
        if (Opacity < 1.0)
        {
            var gs = new ExtGState
            {
                FillAlpha = Opacity,
                StrokeAlpha = Opacity,
            };
            var gsName = page.AddExtGState(gs);
            builder.SetExtGState(gsName);
        }

        // Set text color
        if (TextState?.ForegroundColor is { } fg)
            builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
        else
            builder.SetFillColor(0, 0, 0);

        // Begin marked content for artifact — use BDC with properties so the
        // /Type, /Subtype, and /BBox round-trip through ArtifactCollection.
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var bboxStr = $"[{bbox.LLX.ToString("0.##", ci)} {bbox.LLY.ToString("0.##", ci)} {bbox.URX.ToString("0.##", ci)} {bbox.URY.ToString("0.##", ci)}]";
        var dict = $"<</Type /{Type} /Subtype /{Subtype} /BBox {bboxStr}>>";
        builder.BeginMarkedContentWithProps("Artifact", dict);

        if (Math.Abs(Rotation) > 0.1)
        {
            var rad = Rotation * Math.PI / 180;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            var cx = pageWidth / 2;
            var cy = pageHeight / 2;

            builder.BeginText();
            builder.SetFont(fontResourceName, fontSize);
            builder.SetTextMatrix(cos, sin, -sin, cos,
                x * cos - y * sin + cx * (1 - cos) + cy * sin,
                x * sin + y * cos + cy * (1 - cos) - cx * sin);
            builder.ShowText(renderText);
            builder.EndText();
        }
        else
        {
            builder.BeginText();
            builder.SetFont(fontResourceName, fontSize);
            builder.MoveTextPosition(x, y);
            builder.ShowText(renderText);
            builder.EndText();
        }

        builder.EndMarkedContent();
        builder.RestoreState();

        return builder.Build();
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
        if (SourceImage is not null && OperatingSystem.IsWindows())
        {
            AddImageWatermark(page);
            return;
        }
        // Register the artifact's own font when it is a Standard-14 face (e.g. a
        // Courier watermark must not come out as Helvetica), falling back to
        // Helvetica otherwise. RegisterFont may return a name other than "F1"
        // when the page's existing resources already use that slot — a page may
        // reserve /F1 for an embedded subset that lacks our watermark glyphs
        // (g/p/q/y), so emitting SetFont("F1", ...) into our content stream
        // renders the text invisibly.
        var baseFont = TextState?.FontName is { Length: > 0 } fn && Standard14Fonts.IsStandard14(fn)
            ? fn : "Helvetica";
        var fontName = Table.RegisterFont(page, baseFont);

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

    /// <summary>Emit the text watermark with its glyphs inside a Form XObject:
    /// the page-level block is a clean <c>q … /Artifact «props» BDC /FrmN Do EMC Q</c>
    /// and the text (colour + BT…ET) lives in the form. Keeping the drawing in a
    /// form lets callers walk the page's Do operators and pull the watermark text
    /// out of <c>Resources.Forms[name]</c>.</summary>
    private byte[] BuildTextWatermark(Page page, string fontResourceName)
    {
        var renderText = Text;
        if (!string.IsNullOrEmpty(renderText) && !string.IsNullOrEmpty(PageNumberReplacementString))
            renderText = renderText.Replace(PageNumberReplacementString, page.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (string.IsNullOrEmpty(renderText)) return [];

        var pageWidth = page.Width;
        var pageHeight = page.Height;
        var fontSize = TextState?.FontSize ?? 12;
        var baseFont = TextState?.FontName ?? TextState?.Font?.FontName ?? "Helvetica";

        var textWidth = MeasureTextWidth(renderText!, baseFont, fontSize);

        // Vertical extent of the text line: the written face's ascent+descent box.
        var ascent = Math.Abs(Standard14Fonts.GetWrittenFaceAscent(baseFont)) * fontSize / 1000.0;
        var descent = Math.Abs(Standard14Fonts.GetWrittenFaceDescent(baseFont)) * fontSize / 1000.0;
        if (ascent <= 0) ascent = fontSize * 0.75;
        if (descent <= 0) descent = fontSize * 0.2;
        var textHeight = ascent + descent;

        double x, y;
        // An explicit Position gives the text BOX floor; the baseline sits one
        // descent above it.
        if (Position is { } pos) { x = pos.X; y = pos.Y + descent; }
        else
        {
            x = ArtifactHorizontalAlignment switch
            {
                HorizontalAlignment.Left => LeftMargin > 0 ? LeftMargin : 36,
                HorizontalAlignment.Right => pageWidth - textWidth - (RightMargin > 0 ? RightMargin : 36),
                _ => (pageWidth - textWidth) / 2,
            };
            // Baseline position: the centred case centres the ascent+descent box
            // and sets the baseline one descent above its floor.
            y = ArtifactVerticalAlignment switch
            {
                VerticalAlignment.Top => pageHeight - fontSize - (TopMargin > 0 ? TopMargin : 36),
                VerticalAlignment.Bottom => BottomMargin > 0 ? BottomMargin : 36,
                _ => (pageHeight - textHeight) / 2 + descent,
            };
        }

        var bbox = ComputeBBox(x, y - descent, textWidth, textHeight);
        Rectangle = bbox;

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.####", ci);

        // Form content: colour + text run at absolute page coordinates; the form's
        // BBox spans the page so no placement matrix is needed on the page side.
        var inner = new System.Text.StringBuilder();
        var fg = TextState?.ForegroundColor;
        inner.Append(fg is { } c
            ? $"{F(c.R / 255.0)} {F(c.G / 255.0)} {F(c.B / 255.0)} rg\n"
            : "0 0 0 rg\n");
        inner.Append("BT\n");
        inner.Append($"/{fontResourceName} {F(fontSize)} Tf\n");
        // A ROTATED watermark carries its rotation on the PAGE-LEVEL cm (composed
        // after the /Rotate compensation) with the form's text at the origin —
        // the output takes exactly this shape (q R·cm /Fm Do Q), and rotation
        // inside the form's Tm renders mirrored on /Rotate pages.
        string? rotationCm = null;
        if (Math.Abs(Rotation) > 0.1)
        {
            var rad = Rotation * Math.PI / 180;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            var cx = pageWidth / 2;
            var cy = pageHeight / 2;
            rotationCm = $"{F(cos)} {F(sin)} {F(-sin)} {F(cos)} " +
                $"{F(x * cos - y * sin + cx * (1 - cos) + cy * sin)} " +
                $"{F(x * sin + y * cos + cy * (1 - cos) - cx * sin)} cm";
            inner.Append("0 0 Td\n");
        }
        else
        {
            inner.Append($"{F(x)} {F(y)} Td\n");
        }
        inner.Append($"({EscapeTextLiteral(renderText)}) Tj\n");
        inner.Append("ET\n");

        var formName = page.AddStampForm(System.Text.Encoding.ASCII.GetBytes(inner.ToString()));

        var sb = new System.Text.StringBuilder("q\n");
        // Compose the /Rotate compensation and the watermark rotation into ONE cm —
        // a single composed matrix is emitted ahead of the form.
        if (rotationCm is not null && PageRotationMatrix(page) is { } pm)
        {
            var rad2 = Rotation * Math.PI / 180;
            var rc = Math.Cos(rad2); var rs = Math.Sin(rad2);
            var cx2 = pageWidth / 2; var cy2 = pageHeight / 2;
            double re = x * rc - y * rs + cx2 * (1 - rc) + cy2 * rs;
            double rf = x * rs + y * rc + cy2 * (1 - rc) - cx2 * rs;
            // [rotation] × [pageRot] (row-vector composition).
            double na = rc * pm[0] + rs * pm[2];
            double nb = rc * pm[1] + rs * pm[3];
            double nc = -rs * pm[0] + rc * pm[2];
            double nd = -rs * pm[1] + rc * pm[3];
            double ne = re * pm[0] + rf * pm[2] + pm[4];
            double nf = re * pm[1] + rf * pm[3] + pm[5];
            sb.Append($"{F(na)} {F(nb)} {F(nc)} {F(nd)} {F(ne)} {F(nf)} cm\n");
        }
        else
        {
            if (PageRotationCm(page) is { } rot) sb.Append(rot).Append('\n');
            if (rotationCm is not null) sb.Append(rotationCm).Append('\n');
        }
        if (Opacity < 1.0)
        {
            var gs = new ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity };
            sb.Append($"/{page.AddExtGState(gs)} gs\n");
        }
        var bboxStr = $"[{bbox.LLX.ToString("0.##", ci)} {bbox.LLY.ToString("0.##", ci)} {bbox.URX.ToString("0.##", ci)} {bbox.URY.ToString("0.##", ci)}]";
        sb.Append($"/Artifact <</Type /{Type} /Subtype /{Subtype} /BBox {bboxStr}>> BDC\n");
        sb.Append($"/{formName} Do\n");
        sb.Append("EMC\nQ\n");
        return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static string EscapeTextLiteral(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", "").Replace("\n", " ");
}
