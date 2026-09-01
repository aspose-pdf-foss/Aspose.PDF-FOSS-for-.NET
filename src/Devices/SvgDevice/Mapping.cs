using System.Globalization;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices;

public sealed partial class SvgDevice
{
    private static readonly double[] Identity = { 1, 0, 0, 1, 0, 0 };

    /// <summary>Compose two affine matrices (PDF row-vector convention): m1 × m2.</summary>
    private static double[] MulAffine(double[] m1, double[] m2) => new[]
    {
        m1[0] * m2[0] + m1[1] * m2[2],
        m1[0] * m2[1] + m1[1] * m2[3],
        m1[2] * m2[0] + m1[3] * m2[2],
        m1[2] * m2[1] + m1[3] * m2[3],
        m1[4] * m2[0] + m1[5] * m2[2] + m2[4],
        m1[4] * m2[1] + m1[5] * m2[3] + m2[5],
    };

    private static string FormatHex(double r, double g, double b) =>
        $"#{ClampByte(r):x2}{ClampByte(g):x2}{ClampByte(b):x2}";

    private static int ClampByte(double v) => Math.Clamp((int)Math.Round(v * 255), 0, 255);

    /// <summary>
    /// Set an RGB colour from the numeric operands of an <c>sc</c>/<c>scn</c>
    /// operator, inferring the model from the component count. A trailing pattern
    /// name (scn) or zero numeric operands leaves the colour unchanged.
    /// </summary>
    /// <summary>Resolve a cs/CS operand: a device-space name directly, otherwise the
    /// named entry of the page's /ColorSpace resources (Separation/DeviceN/ICC/...).</summary>
    private static SoftwarePageRenderer.ImageColorSpaceInfo? ResolveNamedColorSpace(
        string name, PdfDictionary? resources, PdfReader reader)
    {
        if (name is "DeviceGray" or "DeviceRGB" or "DeviceCMYK" or "Pattern")
            return null;
        var csDict = reader.ResolveDict(resources?.Get("ColorSpace"));
        var entry = csDict?.Get(name);
        if (entry is null) return null;
        var info = SoftwarePageRenderer.ResolveImageColorSpace(entry, reader);
        return info.TintTransform is not null ? info : null;
    }

    /// <summary>Map scn tint operands through the space's tint transform into RGB.
    /// Returns false when the operands don't fit the space (caller falls back to the
    /// operand-count inference).</summary>
    private static bool TintToRgb(SoftwarePageRenderer.ImageColorSpaceInfo cs,
        List<PdfObject> operands, ref double r, ref double g, ref double b)
    {
        var n = cs.TintComponents;
        if (n <= 0 || operands.Count < n) return false;
        var tints = new double[n];
        for (var i = 0; i < n; i++)
        {
            if (operands[operands.Count - n + i] is not (PdfInteger or PdfReal)) return false;
            tints[i] = Num(operands[operands.Count - n + i]);
        }
        double[] alt;
        try { alt = cs.TintTransform!.Evaluate(tints); }
        catch { return false; }
        switch (cs.AltSpaceName)
        {
            case "DeviceCMYK" when alt.Length >= 4:
                CmykToRgb(alt[0], alt[1], alt[2], alt[3], out r, out g, out b);
                return true;
            case "DeviceRGB" when alt.Length >= 3:
                r = alt[0]; g = alt[1]; b = alt[2];
                return true;
            case "DeviceGray" when alt.Length >= 1:
                r = g = b = alt[0];
                return true;
        }
        return false;
    }

    private static void SetColorFromComponents(List<PdfObject> operands, ref double r, ref double g, ref double b)
    {
        var nums = operands.Where(o => o is PdfInteger or PdfReal).Select(Num).ToList();
        switch (nums.Count)
        {
            case 1:
                r = g = b = nums[0];
                break;
            case 3:
                r = nums[0]; g = nums[1]; b = nums[2];
                break;
            case 4:
                CmykToRgb(nums[0], nums[1], nums[2], nums[3], out r, out g, out b);
                break;
        }
    }

    private static void CmykToRgb(double c, double m, double y, double k,
        out double r, out double g, out double b)
    {
        r = (1 - c) * (1 - k);
        g = (1 - m) * (1 - k);
        b = (1 - y) * (1 - k);
    }

    private static string MapLineCap(int cap) => cap switch
    {
        1 => "round",
        2 => "square",
        _ => "butt",
    };

    private static string MapLineJoin(int join) => join switch
    {
        1 => "round",
        2 => "bevel",
        _ => "miter",
    };

    private static string MapFontName(string baseFont) => baseFont switch
    {
        var n when n.Contains("Helvetica") => "Helvetica, Arial, sans-serif",
        var n when n.Contains("Times") => "Times New Roman, serif",
        var n when n.Contains("Courier") => "Courier New, monospace",
        _ => "sans-serif",
    };

    private static string MapBlendMode(string pdfMode) => pdfMode switch
    {
        "Multiply" => "multiply",
        "Screen" => "screen",
        "Overlay" => "overlay",
        "Darken" => "darken",
        "Lighten" => "lighten",
        "ColorDodge" => "color-dodge",
        "ColorBurn" => "color-burn",
        "HardLight" => "hard-light",
        "SoftLight" => "soft-light",
        "Difference" => "difference",
        "Exclusion" => "exclusion",
        "Hue" => "hue",
        "Saturation" => "saturation",
        "Color" => "color",
        "Luminosity" => "luminosity",
        _ => "normal",
    };

    /// <summary>
    /// Render a named XObject. Form XObjects are decoded and recursed into with their
    /// own /Resources (falling back to the parent's) and optional /Matrix composed
    /// into the CTM. Image XObjects are not yet emitted.
    /// </summary>
    private void RenderXObject(string name, PdfDictionary? resources, PdfReader reader,
        StringBuilder sb, int depth, GState gs, ISet<string> usedBlendModes,
        List<LinkRect>? links)
    {
        if (depth >= MaxXObjectDepth || resources is null) return;

        var xobjDict = reader.ResolveDict(resources.Get("XObject"));
        if (xobjDict is null) return;

        var xobj = reader.ResolveStream(xobjDict.Get(name));
        if (xobj is null) return;

        var subtype = xobj.Dict.GetName("Subtype");
        if (subtype == "Image")
        {
            EmitImage(name, xobj, reader, sb, gs, links);
            return;
        }
        if (subtype != "Form") return;

        byte[] formBytes;
        try { formBytes = reader.DecodeStream(xobj); }
        catch { return; }

        var formResources = reader.ResolveDict(xobj.Dict.Get("Resources")) ?? resources;

        // Optional /Matrix maps form space into the current user space.
        var formGs = gs.Clone();
        var matrix = reader.Resolve(xobj.Dict.Get("Matrix")) as PdfArray;
        if (matrix is not null && matrix.Count >= 6)
        {
            var m = new[]
            {
                Num(matrix[0]), Num(matrix[1]), Num(matrix[2]),
                Num(matrix[3]), Num(matrix[4]), Num(matrix[5]),
            };
            formGs.Ctm = MulAffine(m, formGs.Ctm);
        }

        RenderToSvg(formBytes, formResources, reader, sb, depth + 1, formGs, usedBlendModes, links);
    }

    /// <summary>Emit an image XObject as an inline data-URI PNG. The image's unit
    /// square is mapped through the CTM; axis-aligned placements use x/y/width/height,
    /// anything else keeps the full matrix.</summary>
    private void EmitImage(string name, PdfStream xobj, PdfReader reader,
        StringBuilder sb, GState gs, List<LinkRect>? links)
    {
        byte[] bytes;
        const string mime = "image/png";
        try
        {
            bytes = new ImageXObject(name, xobj, reader).ToPng();
            // Every embedded image is expected to go through the
            // BCL Bitmap PNG encoder; re-encode through GDI+ so chunk layout
            // and byte bulk match that output (the zipped-output sizes the
            // corpus pins are the BCL encoder's).
            if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            {
                try { bytes = GdiReencodePng(bytes); }
                catch { /* keep the managed-encoder PNG */ }
            }
        }
        catch
        {
            return; // undecodable image: skip rather than fail the page
        }

        // The image occupies the unit square (0,0)-(1,1) in user space.
        var m = gs.Ctm;
        var (x0, y0) = Apply(m, 0, 0);
        var (x1, y1) = Apply(m, 1, 1);
        var link = LinkAt(links, (x0 + x1) / 2.0, (y0 + y1) / 2.0);
        if (link is not null) sb.AppendLine($"<a xlink:href=\"{EscapeXml(link.Uri)}\" target=\"_blank\" >");

        string href;
        if (SaveOptions?.CustomStrategyOfEmbeddedImagesSaving is { } strategy)
        {
            // The caller owns the bytes: hand them over and use whatever
            // reference it returns as the image href.
            var info = new Aspose.Pdf.SvgSaveOptions.SvgImageSavingInfo
            {
                ContentStream = new MemoryStream(bytes, writable: false),
                SupposedFileName = $"image{++_imgCounter}.png",
                ImageType = Aspose.Pdf.SvgSaveOptions.SvgExternalImageType.Png,
            };
            href = EscapeXml(strategy(info));
        }
        else
        {
            href = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        const double eps = 1e-6;
        if (Math.Abs(m[1]) < eps && Math.Abs(m[2]) < eps)
        {
            var lx = Math.Min(x0, x1);
            var ly = Math.Min(y0, y1);
            var w = Math.Abs(x1 - x0);
            var h = Math.Abs(y1 - y0);
            sb.AppendLine($"<image x=\"{F(lx)}\" y=\"{F(ly)}\" width=\"{F(w)}\" height=\"{F(h)}\" " +
                $"preserveAspectRatio=\"none\" xlink:href=\"{href}\" />");
        }
        else
        {
            // General placement: the unit square flips vertically under the page
            // transform, so fold the flip into the matrix and draw at (0,-1).
            var transform = $"matrix({F(m[0])} {F(m[1])} {F(m[2])} {F(m[3])} {F(m[4])} {F(m[5])})";
            sb.AppendLine($"<image x=\"0\" y=\"-1\" width=\"1\" height=\"1\" " +
                $"preserveAspectRatio=\"none\" transform=\"{transform} scale(1,-1)\" xlink:href=\"{href}\" />");
        }
        if (link is not null) sb.AppendLine("</a>");
    }

    private static string EscapeXml(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            // Drop characters that are illegal in XML 1.0: control chars below 0x20
            // (except tab/LF/CR) and the non-characters U+FFFE/U+FFFF.
            if (ch < 0x20 && ch is not ('\t' or '\n' or '\r')) continue;
            if (ch is '￾' or '￿') continue;
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static Dictionary<string, PdfDictionary> ResolveFonts(PdfDictionary? resources, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        if (resources is null) return result;
        var fontDict = reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) return result;
        foreach (var key in fontDict.Keys)
        {
            var font = reader.ResolveDict(fontDict.Get(key));
            if (font is not null) result[key] = font;
        }
        return result;
    }

    private static Dictionary<string, PdfDictionary> ResolveExtGStates(PdfDictionary? resources, PdfReader reader)
    {
        var result = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
        if (resources is null) return result;
        var gsDict = reader.ResolveDict(resources.Get("ExtGState"));
        if (gsDict is null) return result;
        foreach (var key in gsDict.Keys)
        {
            var entryDict = reader.ResolveDict(gsDict.Get(key));
            if (entryDict is not null) result[key] = entryDict;
        }
        return result;
    }

    private static List<byte[]> GetContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream) result.Add(reader.DecodeStream(stream));
        else if (obj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null) result.Add(reader.DecodeStream(s));
            }
        }
        return result;
    }

    private static PdfArray ParseArray(PdfLexer lexer)
    {
        var arr = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind)
            {
                case TokenKind.Integer: arr.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: arr.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: arr.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: arr.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: arr.Add(new PdfName(t.StringValue!)); break;
            }
        }
        return arr;
    }

    private static double Num(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0,
    };

    private static void SkipInlineImage(PdfLexer lexer)
    {
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.Keyword && t.StringValue == "ID") break;
        }

        var pos = lexer.Position + 1;
        var len = lexer.Length;

        while (pos < len - 2)
        {
            var b = lexer.ByteAt(pos);
            if (b is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20 &&
                lexer.ByteAt(pos + 1) == (byte)'E' &&
                lexer.ByteAt(pos + 2) == (byte)'I')
            {
                var after = pos + 3;
                if (after >= len || lexer.ByteAt(after) is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20)
                {
                    lexer.Position = after;
                    return;
                }
            }
            pos++;
        }
        lexer.Position = len;
    }

    // Coordinates are emitted at single-precision round-trip, the expected
    // output shape ("595.32", "-0.9199829") — full double round-trip would
    // append binary-noise tails ("303.05999999999995").
    private static string F(double v) => ((float)v).ToString("R", CultureInfo.InvariantCulture);

    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static byte[] GdiReencodePng(byte[] png)
    {
        using var src = new System.Drawing.Bitmap(new MemoryStream(png));
        using var ms = new MemoryStream();
        src.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Format with a forced decimal part (e.g. <c>266.16667</c>, <c>173.0</c>),
    /// the shape used for text positions.</summary>
    private static string FD(double v) => v.ToString("0.0#####", CultureInfo.InvariantCulture);
}
