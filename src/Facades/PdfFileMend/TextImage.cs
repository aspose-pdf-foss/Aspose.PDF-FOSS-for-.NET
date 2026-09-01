using System.Globalization;
using System.IO.Compression;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileMend
{
    private void AddTextToPage(Page page, FormattedText ft, float llx, float lly, float urx, float ury)
    {
        var sb = new StringBuilder();
        sb.Append("q\n");

        // Register font in page resources. CustomFontFile takes precedence so a
        // .ttf path supplied via FormattedText is actually embedded; otherwise
        // EnsureFont falls back to a Type1 base font by name.
        string fontName;
        // A face chosen by the FontStyle enum (a Standard-14 base font) lays out on the
        // probed Standard-14 geometry below; a string-named system font or a custom font
        // file keeps the legacy placement (baseline lly - size + 13, x inset 0.2 * size).
        bool namedFace = false;
        // Text carrying characters WinAnsi cannot address (Hebrew, CJK, …) cannot be
        // written through a simple WinAnsi font — Cp1252 turns every such glyph into
        // '?'. The whole fragment is promoted to an embedded Identity-H
        // subset of the matching system face instead (Times-Roman text reads back
        // under "XXXXXX+TimesNewRoman"), so extraction round-trips the original text.
        bool needsUnicode = false;
        foreach (var probeLine in ft.Lines)
        {
            foreach (var ch in probeLine.Text)
                if (ch > 255) { needsUnicode = true; break; }
            if (needsUnicode) break;
        }
        byte[]? unicodeTtf = null;
        string unicodeFace = "";
        PdfDictionary? unicodeFontDict = null;
        if (needsUnicode)
        {
            unicodeFace = UnicodeFaceFor(ft);
            unicodeTtf = Aspose.Pdf.Text.FontRepository.GetTtfData(unicodeFace);
            if (unicodeTtf is not null) unicodeFontDict = GetOrCreatePageFontDict(page);
        }
        if (unicodeTtf is not null)
        {
            var (resName, _) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                unicodeFontDict!, unicodeTtf, unicodeFace, ft.Lines[0].Text, stripSpacesInBaseFont: true);
            fontName = resName;
        }
        else if (!string.IsNullOrEmpty(ft.CustomFontFile) && File.Exists(ft.CustomFontFile))
        {
            namedFace = true;
            var resourceName = AllocateFontResourceName(page);
            var embedder = FontEmbedder.EmbedFromFile(_document!, ft.CustomFontFile, resourceName);
            embedder.AddToPage(page);
            fontName = resourceName;
        }
        else if (ft.RequestedFontName is { } requested
            && requested.Replace(" ", "") is { Length: > 0 } strippedReq
            && !string.Equals(strippedReq, ft.FontName, StringComparison.Ordinal))
        {
            // The caller asked for a named system font (e.g. "Times New Roman") via the
            // string-font FormattedText constructor, and its name differs from the
            // Standard-14 base font it folds to for glyph metrics ("Times-Roman"). Emit it
            // as a TrueType font whose /BaseFont is the requested name with spaces removed
            // ("TimesNewRoman"), so text extraction reports that name rather than the fold.
            namedFace = true;
            fontName = EnsureFont(page, strippedReq, trueType: true);
        }
        else
        {
            fontName = EnsureFont(page, ft.FontName);
        }

        // Probed AddText geometry (Standard-14 faces, 10-30 pt, both the
        // rect and the point overloads; the facade rect is in the AS-DISPLAYED frame):
        //   bar bottom = lly - size + 11
        //   bar height = (Ascender - Descender)/1000 * size + 4   (the face's AFM metrics)
        //   baseline   = bar bottom + 2 + |Descender|/1000 * size
        //   text x     = llx + 2;  bar x = llx, bar width = urx - llx (page width when no rect)
        //   line pitch = size + 2 (+ the previous line's extra spacing)
        // On a /Rotate page the whole block is drawn under the display->media rotation
        // (the matrix Page.AddImage uses), and the bar additionally extends DOWN by the
        // descent, so its top and the baseline stay where the unrotated rule puts them.
        const double TextInsetX = 2;
        const double BarBottomRise = 11;
        const double BarPadding = 4;
        const double BaselineLift = 2;
        double size = ft.FontSize;
        double defaultLeading = size + 2;

        var metricsFont = string.IsNullOrEmpty(ft.FontName) ? "Helvetica" : ft.FontName!;
        bool std14 = Standard14Fonts.IsStandard14(metricsFont);
        double ascender = (std14 ? Standard14Fonts.GetAscent(metricsFont) : 718) / 1000.0;
        double descender = (std14 ? -Standard14Fonts.GetDescent(metricsFont) : 207) / 1000.0;

        var pageMediaBox = page.MediaBox;
        var rot = ((page.RotateDegrees % 360) + 360) % 360;
        bool swapAxes = rot == 90 || rot == 270;
        double mediaW = pageMediaBox.URX - pageMediaBox.LLX;
        double mediaH = pageMediaBox.URY - pageMediaBox.LLY;
        double displayW = swapAxes ? mediaH : mediaW;
        double displayH = swapAxes ? mediaW : mediaH;
        string rotCm = rot switch
        {
            90 => string.Format(CultureInfo.InvariantCulture, "0 1 -1 0 {0:F2} 0 cm\n", pageMediaBox.URX),
            180 => string.Format(CultureInfo.InvariantCulture, "-1 0 0 -1 {0:F2} {1:F2} cm\n", pageMediaBox.URX, pageMediaBox.URY),
            270 => string.Format(CultureInfo.InvariantCulture, "0 -1 1 0 0 {0:F2} cm\n", pageMediaBox.URY),
            _ => string.Empty,
        };
        sb.Append(rotCm);

        const double LegacyBaselineRise = 13;
        const double LegacyInsetFactor = 0.2;
        const double LegacyBarHeightFactor = 1.325;
        double barBottom, barH, startY, textX, bgX, bgW;
        if (!namedFace)
        {
            barBottom = lly - size + BarBottomRise;
            barH = (ascender + descender) * size + BarPadding;
            startY = barBottom + BaselineLift + descender * size;
            if (rot != 0)
            {
                barBottom -= descender * size;
                barH += descender * size;
            }
            textX = llx + TextInsetX;
            bgX = llx;
            bgW = urx > llx ? urx - llx : displayW;
        }
        else
        {
            // Legacy placement (string-named / custom faces): the first baseline sits at
            // lly - size + 13 (slope -1 in size, e.g. lly=600: 15pt -> 598, 20pt -> 593), the
            // text is inset 0.2 * size from llx, and the background is a page-wide bar per
            // line from baseline + descent - 0.2 * size, 1.325 * size tall.
            startY = lly - size + LegacyBaselineRise;
            textX = llx + size * LegacyInsetFactor;
            double pad = size * LegacyInsetFactor;
            barBottom = startY - descender * size - pad;
            barH = size * LegacyBarHeightFactor;
            bgX = pageMediaBox.LLX;
            bgW = displayW;
        }

        // Clamp to page bounds so off-page coordinates don't silently render nothing.
        if (startY > displayH - size)
            startY = displayH - size;

        // The background-colour bar behind each line (drawn before the glyphs so it
        // renders underneath). This makes PdfFileMend honour FormattedText.BackgroundColor.
        if (!ft.BackgroundColor.IsEmpty)
        {
            var bg = ft.BackgroundColor;
            double bottom = barBottom;
            for (var i = 0; i < ft.Lines.Count; i++)
            {
                if (i > 0)
                {
                    var extra = ft.Lines[i - 1].LineSpacing;
                    bottom -= defaultLeading + (extra > 0 ? extra : 0);
                }
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "{0:F3} {1:F3} {2:F3} rg\n{3:F2} {4:F2} {5:F2} {6:F2} re\nf\n",
                    bg.R / 255.0, bg.G / 255.0, bg.B / 255.0,
                    bgX, bottom, bgW, barH);
            }
        }

        // Set foreground color
        var fg = ft.ForegroundColor;
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "{0:F3} {1:F3} {2:F3} rg\n",
            fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);

        sb.Append("BT\n");
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "/{0} {1:G} Tf\n", fontName, ft.FontSize);

        // Position first line.
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "{0:F2} {1:F2} Td\n", textX, startY);

        // Emit first line
        sb.AppendFormat("{0} Tj\n", ShowOperand(ft.Lines[0].Text, unicodeTtf, unicodeFace, unicodeFontDict));

        // Emit subsequent lines with TL (text leading) and T* (next line). A line's
        // /lineSpacing is extra leading applied AFTER it (before the next line), so the
        // baseline-to-baseline pitch into line i is the default line height plus the
        // PREVIOUS line's spacing — matching FormattedText.AddNewLineText semantics.
        for (var i = 1; i < ft.Lines.Count; i++)
        {
            var line = ft.Lines[i];
            var extra = ft.Lines[i - 1].LineSpacing;
            var leading = defaultLeading + (extra > 0 ? extra : 0);
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "{0:F2} TL\n", leading);
            sb.Append("T*\n");
            sb.AppendFormat("{0} Tj\n", ShowOperand(line.Text, unicodeTtf, unicodeFace, unicodeFontDict));
        }

        sb.Append("ET\n");
        sb.Append("Q\n");

        // The font is declared /WinAnsiEncoding (see AddOrGetFont), so the glyph
        // bytes must follow Windows-1252, not Latin-1. They differ in 0x80-0x9F,
        // where WinAnsi carries the "smart" punctuation/symbols (' " – — ™ …).
        // Encoding the stream with Latin-1 turned those into '?' and lost the text;
        // Cp1252 maps them to their real bytes so extraction round-trips. All other
        // code points ≤ 0xFF (and the ASCII operators) encode identically.
        AppendContent(page, Cp1252.GetBytes(sb.ToString()));
    }

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
