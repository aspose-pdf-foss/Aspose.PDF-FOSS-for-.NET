using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileMend
{
    private void AddImageToPage(Page page, byte[] imageData, float llx, float lly, float urx, float ury,
        BlendMode blend = BlendMode.Normal)
    {
        var width = urx - llx;
        var height = ury - lly;
        var rect = new Rectangle(llx, lly, urx, ury);
        var blendName = blend == BlendMode.Normal ? null : blend.ToString();
        // On a page with a /Rotate the facade rectangle is given in the AS-DISPLAYED
        // coordinate system (a landscape /Rotate 270 page takes a landscape rect), so
        // the stamp must map it back to media space and draw the image upright for the
        // viewer — the same CompensatePageRotation semantics as Page.AddImage.
        var compensateRotation = (((page.RotateDegrees % 360) + 360) % 360) != 0;

        // Detect image format and create appropriate stamp
        if (IsJpeg(imageData))
        {
            var stamp = ImageStamp.FromJpeg(imageData);
            var (fx, fy, fw, fh) = FitImageRect(stamp.DisplayWidth, stamp.DisplayHeight, llx, lly, width, height);
            stamp.X = fx;
            stamp.Y = fy;
            stamp.DisplayWidth = fw;
            stamp.DisplayHeight = fh;
            stamp.BlendMode = blendName;
            stamp.CompensatePageRotation = compensateRotation;
            stamp.ApplyTo(page);
        }
        else if (IsPng(imageData))
        {
            var (pixels, imgW, imgH, hasAlpha) = DecodePng(imageData);
            ImageStamp stamp;
            if (hasAlpha)
            {
                // Separate RGB and Alpha channels
                var rgb = new byte[imgW * imgH * 3];
                var alpha = new byte[imgW * imgH];
                for (var i = 0; i < imgW * imgH; i++)
                {
                    rgb[i * 3] = pixels[i * 4];
                    rgb[i * 3 + 1] = pixels[i * 4 + 1];
                    rgb[i * 3 + 2] = pixels[i * 4 + 2];
                    alpha[i] = pixels[i * 4 + 3];
                }
                // Embed the RGB and attach the alpha channel as a DeviceGray /SMask
                // so the source's transparency is honoured (a transparent PNG must
                // show the page behind it, not paint an opaque box over it).
                stamp = ImageStamp.FromRgb(rgb, imgW, imgH);
                stamp.SetAlphaMask(alpha);
            }
            else
            {
                stamp = ImageStamp.FromRgb(pixels, imgW, imgH);
            }
            var (fx, fy, fw, fh) = FitImageRect(imgW, imgH, llx, lly, width, height);
            stamp.X = fx;
            stamp.Y = fy;
            stamp.DisplayWidth = fw;
            stamp.DisplayHeight = fh;
            stamp.BlendMode = blendName;
            stamp.CompensatePageRotation = compensateRotation;
            stamp.ApplyTo(page);
        }
        else
        {
            // Try to treat as raw image data — use AddImage on Page
            page.AddImage(imageData, rect);
        }
    }

    private static string AllocateFontResourceName(Page page)
    {
        var pageDict = page.Dict;
        var resources = pageDict.Get("Resources") as PdfDictionary
            ?? page.Reader.ResolveDict(pageDict.Get("Resources"));
        var fontDict = resources is null ? null
            : (resources.Get("Font") as PdfDictionary
                ?? page.Reader.ResolveDict(resources.Get("Font")));
        var existingCount = fontDict is null ? 0 : fontDict.Keys.Count();
        return $"F{existingCount}";
    }

    /// <summary>The Tj operand for one line: a literal string for the WinAnsi path, or
    /// the hex-encoded Identity-H glyph ids when the fragment runs through an embedded
    /// Unicode face.</summary>
    private static string ShowOperand(string text, byte[]? unicodeTtf, string unicodeFace,
        PdfDictionary? unicodeFontDict)
    {
        // The writer treats a pure right-to-left line as VISUAL input (the
        // legacy facade convention: callers pass Hebrew pre-reversed) and stores it
        // REVERSED, i.e. in logical order — searching the visual string then matches
        // through the absorber's RTL needle handling exactly as it does against the
        // expected output.
        text = ReverseIfPureRtl(text);
        if (unicodeTtf is null || unicodeFontDict is null)
            return "(" + EscapePdfString(text) + ")";
        var (_, hexIds) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
            unicodeFontDict, unicodeTtf, unicodeFace, text, stripSpacesInBaseFont: true);
        var hex = new StringBuilder(hexIds.Length * 2 + 2);
        hex.Append('<');
        foreach (var b in hexIds) hex.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        hex.Append('>');
        return hex.ToString();
    }

    /// <summary>Reverse a line consisting only of raw Hebrew/Arabic letters and
    /// neutral punctuation/whitespace; any Latin letter (or other strong-LTR char)
    /// leaves the line untouched.</summary>
    private static string ReverseIfPureRtl(string text)
    {
        if (text.Length < 2) return text;
        var hasRtl = false;
        foreach (var c in text)
        {
            if ((c >= 0x0590 && c <= 0x05FF) || (c >= 0x0600 && c <= 0x06FF)
                || (c >= 0x0750 && c <= 0x077F) || (c >= 0xFB1D && c <= 0xFDFF)
                || (c >= 0xFE70 && c <= 0xFEFF))
                hasRtl = true;
            else if (c == ' ' || c == '\t'
                     || (c >= '!' && c <= '/') || (c >= ':' && c <= '@')
                     || (c >= '[' && c <= '`') || (c >= '{' && c <= '~'))
            { /* neutral — allowed inside an RTL run */ }
            else
                return text;
        }
        if (!hasRtl) return text;
        var arr = text.ToCharArray();
        System.Array.Reverse(arr);
        return new string(arr);
    }

    /// <summary>The system face used when the fragment needs glyphs beyond WinAnsi:
    /// the caller's requested named face when one was given, otherwise the standard
    /// substitution for the Standard-14 base font the FontStyle folded to.</summary>
    private static string UnicodeFaceFor(FormattedText ft)
    {
        if (ft.RequestedFontName is { Length: > 0 } req) return req;
        return ft.FontName switch
        {
            "Times-Roman" or "Times-Bold" or "Times-Italic" or "Times-BoldItalic" => "Times New Roman",
            "Helvetica" or "Helvetica-Bold" or "Helvetica-Oblique" or "Helvetica-BoldOblique" => "Arial",
            "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => "Courier New",
            { Length: > 0 } name => name,
            _ => "Arial",
        };
    }

    private static PdfDictionary GetOrCreatePageFontDict(Page page)
    {
        var pageDict = page.Dict;

        // A page's /Resources is frequently inherited from the /Pages tree (the page
        // dict itself carries no /Resources). Resolve the effective resources, and
        // when they are inherited give the page a private copy seeded with the
        // inherited entries — otherwise setting a fresh /Resources here would shadow
        // (and thereby drop from rendering) the page's existing embedded fonts.
        var resources = page.Reader.ResolveDict(pageDict.Get("Resources"));
        var resourcesWereInherited = false;
        if (resources is null)
        {
            var inherited = ResolveInheritedResources(page);
            resources = new PdfDictionary();
            if (inherited is not null)
                foreach (var key in inherited.Keys)
                {
                    var v = inherited.Get(key);
                    if (v is not null) resources.Set(key, v);
                }
            pageDict.Set("Resources", resources);
            resourcesWereInherited = true;
        }

        var fontDict = page.Reader.ResolveDict(resources.Get("Font"))
            ?? resources.Get("Font") as PdfDictionary;
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }
        else if (resourcesWereInherited)
        {
            // The /Font dict came from the inherited resources and is shared with
            // sibling pages — copy it page-local so the new font does not leak into
            // (or collide on) their inherited resources.
            var localFonts = new PdfDictionary();
            foreach (var key in fontDict.Keys)
            {
                var v = fontDict.Get(key);
                if (v is not null) localFonts.Set(key, v);
            }
            fontDict = localFonts;
            resources.Set("Font", fontDict);
        }
        return fontDict;
    }

    private static string EnsureFont(Page page, string fontName, bool trueType = false)
    {
        var fontDict = GetOrCreatePageFontDict(page);

        // Check if font already exists
        var count = 0;
        foreach (var key in fontDict.Keys)
        {
            count++;
            var existing = page.Reader.ResolveDict(fontDict.Get(key));
            if (existing is not null)
            {
                var baseName = existing.GetName("BaseFont");
                if (baseName == fontName || baseName == "/" + fontName)
                    return key;
            }
        }

        // Create new font entry (Type1 base font)
        var pdfFontName = $"F{count}";
        var newFont = new PdfDictionary();
        newFont.Set("Type", new PdfName("Font"));
        newFont.Set("Subtype", new PdfName(trueType ? "TrueType" : "Type1"));
        newFont.Set("BaseFont", new PdfName(fontName));
        // For Latin text, use WinAnsiEncoding
        newFont.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fontDict.Set(pdfFontName, newFont);

        return pdfFontName;
    }

    // Resolve a page's /Resources inherited from the /Pages tree, walking the
    // /Parent chain (mirrors Page's inherited-attribute lookup). Returns null when
    // no ancestor declares /Resources.
    private static PdfDictionary? ResolveInheritedResources(Page page)
    {
        var parentObj = page.Dict.Get("Parent");
        var visited = new HashSet<int>();
        while (parentObj is not null)
        {
            if (parentObj is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber))
                break;
            var parent = page.Reader.ResolveDict(parentObj);
            if (parent is null) break;
            var res = page.Reader.ResolveDict(parent.Get("Resources"));
            if (res is not null) return res;
            parentObj = parent.Get("Parent");
        }
        return null;
    }

    private static void AppendContent(Page page, byte[] contentBytes)
    {
        // Wrap the original page content in q...Q so any persistent CTM (a cm
        // operator at the top of the original stream that lacks a closing Q)
        // doesn't leak into the appended drawing. Otherwise the new text gets
        // drawn in the original stream's transformed user space.
        WrapExistingContentInSaveRestore(page);
        page.AddContentStream(contentBytes);
    }

    private static void WrapExistingContentInSaveRestore(Page page)
    {
        var existing = page.Reader.Resolve(page.Dict.Get("Contents"));
        if (existing is PdfStream stream)
        {
            var data = page.Reader.DecodeStream(stream);
            var wrapped = new byte[data.Length + 4];
            wrapped[0] = (byte)'q';
            wrapped[1] = (byte)'\n';
            data.CopyTo(wrapped, 2);
            wrapped[wrapped.Length - 2] = (byte)'\n';
            wrapped[wrapped.Length - 1] = (byte)'Q';
            stream.Dict.Remove("Filter");
            stream.Dict.Remove("DecodeParms");
            stream.ReplaceData(wrapped);
        }
        else if (existing is PdfArray arr && arr.Count > 0)
        {
            // Prepend q to first stream, append Q to last stream — preserves
            // intermediate boundaries and indirect refs.
            if (page.Reader.ResolveStream(arr[0]) is PdfStream first)
            {
                var data = page.Reader.DecodeStream(first);
                var wrapped = new byte[data.Length + 2];
                wrapped[0] = (byte)'q';
                wrapped[1] = (byte)'\n';
                data.CopyTo(wrapped, 2);
                first.Dict.Remove("Filter");
                first.Dict.Remove("DecodeParms");
                first.ReplaceData(wrapped);
            }
            if (page.Reader.ResolveStream(arr[arr.Count - 1]) is PdfStream last)
            {
                var data = page.Reader.DecodeStream(last);
                var wrapped = new byte[data.Length + 2];
                data.CopyTo(wrapped, 0);
                wrapped[wrapped.Length - 2] = (byte)'\n';
                wrapped[wrapped.Length - 1] = (byte)'Q';
                last.Dict.Remove("Filter");
                last.Dict.Remove("DecodeParms");
                last.ReplaceData(wrapped);
            }
        }
    }

    private static string EscapePdfString(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static bool IsJpeg(byte[] data) =>
        data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;

    internal static bool IsPng(byte[] data) =>
        data.Length >= 8 &&
        data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
        data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

    /// <summary>Unpack one palette/grayscale index per pixel from filtered scanlines,
    /// handling packed sub-byte depths (1/2/4-bit, MSB-first) as well as 8-bit.</summary>
    private static byte[] UnpackIndices(byte[] raw, int width, int height, int stride, int bitDepth)
    {
        var outp = new byte[width * height];
        if (bitDepth == 8)
        {
            for (var y = 0; y < height; y++)
                Array.Copy(raw, y * stride, outp, y * width, width);
            return outp;
        }
        var mask = (1 << bitDepth) - 1;
        for (var y = 0; y < height; y++)
        {
            var rowBase = y * stride;
            for (var x = 0; x < width; x++)
            {
                var bit = x * bitDepth;
                var shift = 8 - bitDepth - (bit % 8);
                outp[y * width + x] = (byte)((raw[rowBase + bit / 8] >> shift) & mask);
            }
        }
        return outp;
    }

    private static int PaethPredictor(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    private static int ReadInt32BE(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
