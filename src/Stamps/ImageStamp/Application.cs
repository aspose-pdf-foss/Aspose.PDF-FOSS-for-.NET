using System.IO.Compression;
using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public partial class ImageStamp
{
    /// <summary>
    /// Add this image to a page.
    /// </summary>
    public void ApplyTo(Page page)
    {
        var imgName = RegisterXObject(page);

        // Build content stream operators to place the image.
        var w = DisplayWidth;
        var h = DisplayHeight;

        // Anchor at XIndent/YIndent (the bottom-left placement) when set, else X/Y,
        // else derive it from Horizontal/VerticalAlignment against the page box
        // (a Right/Bottom-aligned image with no explicit
        // indent lands at pageWidth-imageWidth / 0).
        var pageBox = page.MediaBox;
        double ax = XIndent != 0 ? XIndent
            : X != 0 ? X
            : HorizontalAlignment switch
            {
                HorizontalAlignment.Right => pageBox.Width - w,
                HorizontalAlignment.Center => (pageBox.Width - w) / 2.0,
                _ => 0,
            };
        double ay = YIndent != 0 ? YIndent
            : Y != 0 ? Y
            : VerticalAlignment switch
            {
                VerticalAlignment.Top => pageBox.Height - h,
                VerticalAlignment.Center => (pageBox.Height - h) / 2.0,
                _ => 0,
            };

        // Compose scale + rotation into the cm matrix, then translate so the
        // rotated image's bounding box bottom-left lands at the anchor.
        double deg = RotateAngle != 0 ? RotateAngle : (double)Rotate;
        double rad = deg * System.Math.PI / 180.0;
        double cos = System.Math.Cos(rad), sin = System.Math.Sin(rad);
        double ma = w * cos, mb = w * sin, mc = -h * sin, md = h * cos;
        double minX = System.Math.Min(System.Math.Min(0, ma), System.Math.Min(mc, ma + mc));
        double minY = System.Math.Min(System.Math.Min(0, mb), System.Math.Min(md, mb + md));
        double me = ax - minX, mf = ay - minY;

        // Always emit a graphics-state operator (/GS gs) before placing the image so
        // the stamp composites against an explicit ExtGState rather than inheriting a
        // residual one from prior page content — otherwise a background image
        // watermark could hide the underlying content. The
        // ExtGState carries a non-default blend mode and/or partial opacity when
        // requested; otherwise it is empty (an /Type /ExtGState no-op).
        bool wantBlend = !string.IsNullOrEmpty(BlendMode) && BlendMode != "Normal";
        var gsName = RegisterGsExtGState(page, wantBlend ? BlendMode : null, Opacity);
        var gsOp = $"/{gsName} gs ";
        // A %StampId comment makes this stamp discoverable by PdfContentEditor.GetStamps
        // when an id was assigned via setStampId; the PdfFileStamp facade keeps its own
        // ImageStamp's StampId at 0 (it injects the id itself), so there is no double-mark.
        var idComment = (StampId != 0 || ForceStampIdComment) ? $"%StampId={StampId}\n" : "";
        var rectComment = MetaRect is { } mr
            ? $"%StampRect={Format(mr.LLX)} {Format(mr.LLY)} {Format(mr.URX)} {Format(mr.URY)}\n" : "";
        // A foreground stamp is appended after the page's existing content, so it
        // inherits whatever CTM that content leaves active. Pages that were
        // flattened or are slightly malformed can leave a residual CTM — a scale
        // (e.g. a page authored in 1/600" units with a leading "0.12 0 0 -0.12 0
        // 792 cm") and/or an unbalanced q — that would silently transform the
        // stamp, placing it at the wrong position and size. Undo that residual by
        // prefixing the inverse of the active CTM, so the stamp's anchor
        // coordinates are interpreted against the page's base coordinate system.
        var resetCm = string.Empty;
        if (!Background && TryGetResidualCtmInverse(page, out var ia, out var ib,
                out var ic, out var id, out var ie, out var iff))
        {
            resetCm = $"{Format(ia)} {Format(ib)} {Format(ic)} {Format(id)} {Format(ie)} {Format(iff)} cm ";
        }
        // Rotated-page compensation (AddImage semantics): map the as-displayed
        // coordinate system back onto page space so the anchor rect is where the
        // viewer sees it and the image is upright. For /Rotate 90 on a 612-wide box
        // that frame change is "0 1 -1 0 612 0".
        // ★ It is COMPOSED INTO the placement matrix rather than emitted as a
        // second `cm`: exactly one ConcatenateMatrix is written ahead of
        // the `Do`, and consumers that read back the image's placement take the
        // matrix immediately before the draw — a separate frame `cm` would leave
        // them the unrotated placement and report the stamp as axis-aligned.
        if (CompensatePageRotation)
        {
            var box = page.MediaBox;
            var rot = ((page.RotateDegrees % 360) + 360) % 360;
            (double ra, double rb, double rc, double rd, double re, double rf)? frame = rot switch
            {
                90 => (0, 1, -1, 0, box.URX, 0),
                180 => (-1, 0, 0, -1, box.URX, box.URY),
                270 => (0, -1, 1, 0, 0, box.URY),
                _ => null,
            };
            if (frame is { } f)
            {
                (ma, mb, mc, md, me, mf) = (
                    ma * f.ra + mb * f.rc, ma * f.rb + mb * f.rd,
                    mc * f.ra + md * f.rc, mc * f.rb + md * f.rd,
                    me * f.ra + mf * f.rc + f.re, me * f.rb + mf * f.rd + f.rf);
            }
        }
        var stampBody = $"q {resetCm}{gsOp}{Format(ma)} {Format(mb)} {Format(mc)} {Format(md)} {Format(me)} {Format(mf)} cm /{imgName} Do Q\n";

        // A foreground image stamp is a pagination artifact: wrap it in an
        // /Artifact BDC … EMC marked-content block.
        // The BDC/EMC sit outside the q…Q draw block, so GetStamps still recognises
        // the clean q gs cm /Im Do Q shape inside. Background stamps stay bare.
        var contentOps = Background
            ? $"{idComment}{rectComment}{stampBody}"
            : $"{idComment}{rectComment}/Artifact BDC\n{stampBody}EMC\n";
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(contentOps);

        // Add the stamp as a separate content stream so the page's existing
        // content is preserved — AddContentStream/PrependContentStream are
        // array-aware (a page whose /Contents is a stream array would otherwise
        // be overwritten). Background stamps go behind the page content.
        if (Background)
            page.PrependContentStream(contentBytes);
        else
        {
            // A session-stamped artifact belongs to THIS page alone — the flow's
            // continuation-page artifact copy must not repeat it (see
            // Page.SessionStampBlocks).
            page.SessionStampBlocks.Add(contentBytes);
            page.AddContentStream(contentBytes);
        }
    }

    /// <summary>Build the decoded image as a standalone /XObject /Image stream
    /// (carrying a DeviceGray /SMask when the source has transparency),
    /// independent of any page. Reused by page placement (<see cref="RegisterXObject"/>)
    /// and by form-field appearance streams (image-button fill).</summary>
    internal PdfStream BuildImageXObject()
    {
        // Honour Quality for DCTDecode (JPEG) images by re-encoding below the default.
        var imageData = _imageData;
        if (_filter == "DCTDecode" && Quality < 100)
            imageData = ReencodeJpeg(imageData, Quality);

        var imgDict = new PdfDictionary();
        imgDict.Set("Type", new PdfName("XObject"));
        imgDict.Set("Subtype", new PdfName("Image"));
        imgDict.Set("Width", new PdfInteger(_width));
        imgDict.Set("Height", new PdfInteger(_height));
        imgDict.Set("BitsPerComponent", new PdfInteger(_bitsPerComponent));
        imgDict.Set("ColorSpace", new PdfName(_colorSpace));
        imgDict.Set("Filter", new PdfName(_filter));
        if (_decodeParms is not null)
            imgDict.Set("DecodeParms", _decodeParms);
        if (_decodeArray is not null)
        {
            var decArr = new PdfArray();
            foreach (var v in _decodeArray) decArr.Add(new PdfInteger((long)v));
            imgDict.Set("Decode", decArr);
        }
        imgDict.Set("Length", new PdfInteger(imageData.Length));

        // A transparent source carries its alpha as a DeviceGray /SMask so the
        // renderer composites it (rather than a white box).
        if (_smaskData is not null)
        {
            var smDict = new PdfDictionary();
            smDict.Set("Type", new PdfName("XObject"));
            smDict.Set("Subtype", new PdfName("Image"));
            smDict.Set("Width", new PdfInteger(_width));
            smDict.Set("Height", new PdfInteger(_height));
            smDict.Set("BitsPerComponent", new PdfInteger(8));
            smDict.Set("ColorSpace", new PdfName("DeviceGray"));
            smDict.Set("Filter", new PdfName("FlateDecode"));
            smDict.Set("Length", new PdfInteger(_smaskData.Length));
            imgDict.Set("SMask", new PdfStream(smDict, _smaskData));
        }

        return new PdfStream(imgDict, imageData);
    }

    // Re-encode JPEG bytes at the given quality (1..100) to honour Quality. System.Drawing's
    // encoder is Windows-only, and off Windows this used to return the bytes UNCHANGED - so
    // Quality was silently a no-op there and two stamps of the same picture at quality 100 and
    // 10 landed in the document byte-for-byte identical. The managed encoder behind JpegDevice
    // takes the same 1..100 scale and runs everywhere, so it serves as the fallback; on any
    // failure the original bytes are still returned unchanged.
    private static byte[] ReencodeJpeg(byte[] data, int quality)
    {
        if (OperatingSystem.IsWindows())
        {
            try { return ReencodeJpegWindows(data, quality); }
            catch { return data; }
        }
        try { return ReencodeJpegManaged(data, quality); }
        catch { return data; }
    }

    /// <summary>Decode the JPEG and re-encode it through the managed encoder at the requested
    /// quality. Returns the input unchanged when the image cannot be decoded, so an exotic
    /// colour transform degrades to today's behaviour rather than to a broken image.</summary>
    private static byte[] ReencodeJpegManaged(byte[] data, int quality)
    {
        var (pixels, width, height, components) = IO.Filters.JpegDecoder.Decode(data);
        if (width <= 0 || height <= 0 || pixels.Length == 0) return data;

        // The managed encoder takes straight RGBA; the decoder hands back 1 component for
        // greyscale and 3 for colour (it converts CMYK/YCCK to RGB and reports 3).
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            byte r, g, b;
            if (components >= 3)
            {
                var o = i * 3;
                if (o + 2 >= pixels.Length) return data;
                r = pixels[o]; g = pixels[o + 1]; b = pixels[o + 2];
            }
            else
            {
                if (i >= pixels.Length) return data;
                r = g = b = pixels[i];
            }
            var d = i * 4;
            rgba[d] = r; rgba[d + 1] = g; rgba[d + 2] = b; rgba[d + 3] = 255;
        }
        return IO.JpegEncoderImpl.Encode(rgba, width, height, System.Math.Clamp(quality, 1, 100));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] ReencodeJpegWindows(byte[] data, int quality)
    {
        using var inMs = new MemoryStream(data);
        using var bmp = new System.Drawing.Bitmap(inMs);
        System.Drawing.Imaging.ImageCodecInfo? jpegCodec = null;
        foreach (var c in System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders())
            if (c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid) { jpegCodec = c; break; }
        if (jpegCodec is null) return data;
        using var ep = new System.Drawing.Imaging.EncoderParameters(1);
        ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality, (long)System.Math.Clamp(quality, 1, 100));
        using var outMs = new MemoryStream();
        bmp.Save(outMs, jpegCodec, ep);
        return outMs.ToArray();
    }

    /// <summary>Register the image as an XObject under a fresh /Im name in the
    /// page's resources and return that name, without emitting any placement
    /// operators. Lets callers (e.g. a watermark artifact) emit the <c>Do</c> inside
    /// their own marked-content block.</summary>
    internal string RegisterXObject(Page page)
    {
        var imgStream = BuildImageXObject();

        // Managed stamps record their id on the XObject dictionary so the stamp
        // facade can rediscover them after a save. The %StampId content-stream
        // comment alone does not survive re-serialisation (comments are dropped),
        // but a /StampId dict entry does — and PdfContentEditor.GetStamps reads it.
        if (StampId != 0 || ForceStampIdComment)
            imgStream.Dict.Set("StampId", new PdfInteger(StampId));

        var resources = GetOrCreateResources(page);
        var xobjectDict = GetOrCreateDict(page, resources, "XObject");

        var imgName = "Im0";
        var counter = 0;
        while (xobjectDict.ContainsKey(imgName))
            imgName = $"Im{++counter}";

        xobjectDict.Set(imgName, imgStream);
        return imgName;
    }

    /// <summary>Register an ExtGState resource carrying an optional /BM blend
    /// mode and (when <paramref name="opacity"/> &lt; 1) /ca + /CA alpha, under a
    /// fresh /GS name; return that name.</summary>
    private static string RegisterGsExtGState(Page page, string? blendMode, double opacity)
    {
        var resources = GetOrCreateResources(page);
        var gsDict = GetOrCreateDict(page, resources, "ExtGState");

        var gsName = "GS0";
        var counter = 0;
        while (gsDict.ContainsKey(gsName))
            gsName = $"GS{++counter}";

        var gs = new PdfDictionary();
        gs.Set("Type", new PdfName("ExtGState"));
        if (!string.IsNullOrEmpty(blendMode))
            gs.Set("BM", new PdfName(blendMode!));
        if (opacity < 0.999)
        {
            gs.Set("ca", new PdfReal(opacity));
            gs.Set("CA", new PdfReal(opacity));
        }
        gsDict.Set(gsName, gs);
        return gsName;
    }

    private static PdfDictionary GetOrCreateResources(Page page)
    {
        var pageDict = page.Dict;
        // /Resources is frequently an indirect reference; resolving it (rather
        // than a bare `as PdfDictionary` cast that yields null) avoids replacing
        // the real dictionary — and silently dropping its /Font, /ExtGState, … —
        // with a fresh empty one.
        var res = pageDict.Get("Resources") as PdfDictionary
            ?? page.Reader.ResolveDict(pageDict.Get("Resources"));
        if (res is null)
        {
            res = new PdfDictionary();
            // /Resources is inheritable: a page without its own dict draws with the
            // nearest ancestor's. A fresh page-level dict SHADOWS that one, so every
            // inherited entry (/Font above all) must carry over or the page's text
            // loses its fonts the moment the stamp adds its XObject.
            for (var anc = page.Reader.ResolveDict(pageDict.Get("Parent"));
                 anc is not null;
                 anc = page.Reader.ResolveDict(anc.Get("Parent")))
            {
                if (page.Reader.ResolveDict(anc.Get("Resources")) is { } inherited)
                {
                    foreach (var key in inherited.Keys)
                        if (inherited.Get(key) is { } entry)
                            res.Set(key, entry);
                    break;
                }
            }
            pageDict.Set("Resources", res);
        }
        return res;
    }

    private static PdfDictionary GetOrCreateDict(Page page, PdfDictionary parent, string key)
    {
        var dict = parent.Get(key) as PdfDictionary
            ?? page.Reader.ResolveDict(parent.Get(key));
        if (dict is null)
        {
            dict = new PdfDictionary();
            parent.Set(key, dict);
        }
        return dict;
    }

    private static byte[] CompressFlate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionMode.Compress, leaveOpen: true))
            zlib.Write(data);
        return ms.ToArray();
    }

    private static string Format(double v)
    {
        // Snap floating-point dust (e.g. cos(270°) ≈ -5.5e-14) to zero and emit
        // fixed-point text: PDF reals do not allow exponential notation, so a
        // "G"-formatted tiny value like "-5.5E-14" would be invalid syntax.
        if (System.Math.Abs(v) < 1e-6) v = 0;
        return v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Compute the inverse of the CTM left active at the end of the page's
    /// existing content, so a foreground stamp can cancel it and lay out against
    /// the page's base coordinate system. Returns false (no correction needed)
    /// when the active CTM is already the identity, or when it cannot be parsed
    /// or is singular.</summary>
    private static bool TryGetResidualCtmInverse(Page page, out double ia, out double ib,
        out double ic, out double id, out double ie, out double iff)
    {
        ia = id = 1; ib = ic = ie = iff = 0;
        try
        {
            var content = page.GetContentStreamBytes();
            if (content is null || content.Length == 0) return false;
            var (a, b, c, d, e, f) = ComputeActiveCtm(content);
            // Already identity → nothing to undo (the common, well-formed case).
            if (System.Math.Abs(a - 1) < 1e-6 && System.Math.Abs(b) < 1e-6
                && System.Math.Abs(c) < 1e-6 && System.Math.Abs(d - 1) < 1e-6
                && System.Math.Abs(e) < 1e-6 && System.Math.Abs(f) < 1e-6)
                return false;
            var det = a * d - b * c;
            if (System.Math.Abs(det) < 1e-9) return false;
            ia = d / det;
            ib = -b / det;
            ic = -c / det;
            id = a / det;
            ie = (c * f - d * e) / det;
            iff = (b * e - a * f) / det;
            return true;
        }
        catch { return false; }
    }

    private static bool IsDelimOrWs(byte b) =>
        b is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or (byte)'\f' or 0
        or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
        or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    private static int SkipInlineImage(byte[] data, int i)
    {
        // Advance to the EI operator (whitespace-delimited) that ends the image.
        while (i < data.Length - 1)
        {
            if ((data[i] == (byte)'E' && data[i + 1] == (byte)'I')
                && (i == 0 || IsDelimOrWs(data[i - 1]))
                && (i + 2 >= data.Length || IsDelimOrWs(data[i + 2])))
                return i + 2;
            i++;
        }
        return data.Length;
    }

    private static byte[] ReadAll(System.IO.Stream s)
    {
        // Read the whole image from the start. Callers commonly hand us a stream
        // they have just written to (e.g. Image.Save(ms, Png)), leaving the
        // position at the end; copying from there would yield no bytes and raise
        // a spurious "Invalid PNG data". Rewind when the stream supports it.
        if (s.CanSeek) s.Position = 0;
        using var ms = new System.IO.MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
