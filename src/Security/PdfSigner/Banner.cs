using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Security;

/// <summary>
/// The text banner a signature widget carries: "Digitally signed by …" over the date,
/// reason, location and contact, drawn in a face that covers them.
/// </summary>
/// <remarks>
/// This banner is written for EVERY signature, including one applied to a
/// pre-existing blank field through <c>PdfFileSignature.Sign(fieldName, …)</c> — the
/// field's AddField placeholder (a grey box with a dashed border) is replaced, not kept.
/// Probed on the same field signed with latin, CJK and mixed metadata: the appearance is
/// a form whose only content invokes /FRM, which invokes /n2, which holds the text; the
/// face is a Type0/Identity-H CIDFontType2 with the program embedded — Arial for latin
/// text, MS Gothic as soon as a character needs it.
/// </remarks>
/// <summary>Which metadata labels a caller-shaped appearance keeps even when their
/// value is empty.</summary>
internal sealed record BannerLabels(bool Reason, bool Location, bool Contact);

internal static class SignatureBanner
{
    /// <summary>Lines the banner draws, in order. A line whose value is empty is
    /// dropped, so a signature with no reason shows no "Reason:" row - unless the caller
    /// shaped the appearance (<paramref name="labels"/>), which keeps every label it did
    /// not switch off ("Reason: " with nothing after it). Under a shaped appearance the
    /// date carries its UTC offset only when it is a local wall-clock time: a date the
    /// caller set outright shows bare (measured: an explicit 2022-01-01 prints
    /// "Date: 2022.01.01 00:00:00", the signing moment "... +03:00"). The plain banner
    /// keeps the offset on every date - the templates of the era it is measured against
    /// carry it.</summary>
    internal static List<string> Lines(string? signerName, DateTime signDate,
        string? reason, string? location, string? contact, BannerLabels? labels = null)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(signerName))
            lines.Add($"Digitally signed by '{signerName}'");
        if (signDate.Kind == DateTimeKind.Utc) signDate = signDate.ToLocalTime();
        var bareDate = labels is not null && signDate.Kind != DateTimeKind.Local;
        var dateFormat = bareDate ? "yyyy.MM.dd HH:mm:ss" : "yyyy.MM.dd HH:mm:ss zzz";
        lines.Add("Date: " + signDate.ToString(dateFormat, CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(reason) || labels is { Reason: true }) lines.Add($"Reason: {reason}");
        if (!string.IsNullOrEmpty(location) || labels is { Location: true }) lines.Add($"Location: {location}");
        if (!string.IsNullOrEmpty(contact) || labels is { Contact: true }) lines.Add($"Contact: {contact}");
        return lines;
    }

    /// <summary>The face the banner draws in: the CJK script face as soon as one line
    /// carries a character beyond WinAnsi, else Arial. Returns null when neither
    /// resolves, and the caller falls back to a simple non-embedded font.</summary>
    internal static (byte[] Ttf, string Name)? ResolveFace(IEnumerable<string> lines,
        string? requestedFamily = null)
    {
        var all = string.Concat(lines);
        var beyondAnsi = false;
        foreach (var c in all) if (c > 'ÿ') { beyondAnsi = true; break; }
        // A custom appearance names its own family, and the /BaseFont written must be
        // that name with its spaces stripped ("Times New Roman" reads back as
        // TimesNewRoman). It only serves while it COVERS the text.
        if (!string.IsNullOrEmpty(requestedFamily))
        {
            var req = Aspose.Pdf.Text.SystemFontResolver.Resolve(requestedFamily!);
            if (req is { Length: > 12 } && (!beyondAnsi || CoversAll(req, all)))
                return (req, requestedFamily!.Replace(" ", ""));
        }
        if (beyondAnsi)
        {
            // MS Gothic first: every CJK banner is drawn in it (measured on
            // japanese and on mixed latin/japanese metadata), and the generic CJK chain
            // would answer a Han character with whichever script face it reaches first.
            var gothic = Aspose.Pdf.Text.SystemFontResolver.Resolve("MS Gothic");
            if (gothic is { Length: > 12 }) return (gothic, "MSGothic");
            if (TextStamp.TryResolveCjkTtf(all) is { } cjk)
                return (cjk.ttf, cjk.name.Replace(" ", ""));
        }
        var arial = Aspose.Pdf.Text.SystemFontResolver.Resolve("Arial");
        return arial is { Length: > 12 } ? (arial, "Arial") : null;
    }

    /// <summary>Does the face carry a glyph for every character of <paramref name="text"/>?</summary>
    private static bool CoversAll(byte[] ttf, string text)
    {
        try
        {
            var parser = new Aspose.Pdf.Text.GlyphOutlineParser(ttf);
            foreach (var c in text)
                if (c > ' ' && (!parser.CMap.TryGetValue(c, out var gid) || gid == 0)) return false;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Encode <paramref name="text"/> as 2-byte glyph ids for an Identity-H
    /// font, collecting each glyph's advance so the /W array can be written and the
    /// character it stands for so the /ToUnicode CMap can be.</summary>
    internal static string HexGlyphs(string text, Aspose.Pdf.Text.GlyphOutlineParser parser,
        SortedDictionary<int, int> widths, SortedDictionary<int, char>? toUnicode = null)
    {
        var upm = parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000;
        var sb = new StringBuilder(text.Length * 4);
        foreach (var ch in text)
        {
            if (!parser.CMap.TryGetValue(ch, out var gid)) gid = 0;
            sb.Append(gid.ToString("X4", CultureInfo.InvariantCulture));
            if (gid == 0) continue;
            if (!widths.ContainsKey(gid))
                widths[gid] = (int)Math.Round(parser.GetAdvanceWidth(gid) * 1000.0 / upm);
            if (toUnicode is not null) toUnicode[gid] = ch;
        }
        return sb.ToString();
    }

    /// <summary>The /ToUnicode CMap for the banner's Identity-H face: glyph id back to
    /// the character it was encoded from, so the signature's own words stay selectable,
    /// searchable and extractable. Every banner face ships one.</summary>
    internal static byte[] ToUnicodeCMap(SortedDictionary<int, char> map)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        // bfchar blocks cap at 100 entries (PDF 32000-1:2008 9.10.3).
        var entries = new List<KeyValuePair<int, char>>(map);
        for (var i = 0; i < entries.Count; i += 100)
        {
            var n = Math.Min(100, entries.Count - i);
            sb.Append(n.ToString(inv)).Append(" beginbfchar\n");
            for (var k = i; k < i + n; k++)
                sb.AppendFormat(inv, "<{0:X4}> <{1:X4}>\n", entries[k].Key, (int)entries[k].Value);
            sb.Append("endbfchar\n");
        }
        sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>The /n2 content stream: the banner text, one line per Tj, stepped by the
    /// leading and seated one font size below the box top — the expected shape,
    /// colours and matrix.</summary>
    internal static byte[] Content(List<string> hexLines, string fontRes,
        double fontSize, double height)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(BannerFillRgb).Append(" rg\n");
        sb.Append("q\n1 0 0 1 0 0 cm\nBT\n");
        sb.AppendFormat(inv, "1 0 0 1 0 {0:0.##} Tm\n", height - fontSize);
        sb.AppendFormat(inv, "{0:0.##} TL\n", fontSize * 1.2);
        sb.Append(BannerStrokeRgb).Append(" RG\n");
        sb.AppendFormat(inv, "/{0} {1:0.##} Tf\n", fontRes, fontSize);
        foreach (var hex in hexLines)
            sb.Append('<').Append(hex).Append("> Tj\nT*\n");
        sb.Append("ET\nQ\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>A form XObject that does nothing but invoke another by name — the /FRM
    /// and outer wrappers nested around the text.</summary>
    internal static PdfDictionary Wrapper(string invokeName, int targetObj,
        double w, double h, string? ownName)
        => Wrapper(new[] { (invokeName, targetObj) }, w, h, ownName);

    /// <summary>A form XObject that invokes each named child in turn, every one at the
    /// identity — the /FRM frame draws the picture layer (/n0) under the text layer
    /// (/n2) this way.</summary>
    internal static PdfDictionary Wrapper(IReadOnlyList<(string Name, int Obj)> invokes,
        double w, double h, string? ownName)
    {
        var dict = FormShell(w, h, ownName);
        var xobj = new PdfDictionary();
        var sb = new StringBuilder();
        foreach (var (name, obj) in invokes)
        {
            xobj.Set(name, new PdfIndirectRef(obj, 0));
            sb.Append("q\n1 0 0 1 0 0 cm\n/").Append(name).Append(" Do\nQ\n");
        }
        var res = new PdfDictionary();
        res.Set("XObject", xobj);
        dict.Set("Resources", res);
        var content = Encoding.ASCII.GetBytes(sb.ToString());
        dict.Set("Length", new PdfInteger(content.Length));
        dict.Set("__StreamData", new PdfString(content));
        return dict;
    }

    /// <summary>The picture layer of a signature: the caller's graphic as an image
    /// XObject stretched over the whole box (the reference fills the box, it does not
    /// keep the picture's aspect), invoked by a /n0 form. Returns the form's object
    /// number, or null when the bytes are no picture this writer can carry.</summary>
    internal static int? PictureForm(byte[] picture, double w, double h, ref int nextObj,
        List<(int Num, PdfDictionary Dict, bool IsStream)> objects)
    {
        if (PictureXObject(picture) is not { } image) return null;
        var imageObj = nextObj++;
        objects.Add((imageObj, image, true));

        var n0 = FormShell(w, h, "n0");
        var xobj = new PdfDictionary();
        xobj.Set("Im0", new PdfIndirectRef(imageObj, 0));
        var res = new PdfDictionary();
        res.Set("XObject", xobj);
        n0.Set("Resources", res);
        var inv = CultureInfo.InvariantCulture;
        var content = Encoding.ASCII.GetBytes(string.Format(inv, "q\n{0:0.##} 0 0 {1:0.##} 0 0 cm\n/Im0 Do\nQ\n", w, h));
        n0.Set("Length", new PdfInteger(content.Length));
        n0.Set("__StreamData", new PdfString(content));
        var n0Obj = nextObj++;
        objects.Add((n0Obj, n0, true));
        return n0Obj;
    }

    /// <summary>The image XObject behind the picture layer: a JPEG travels verbatim
    /// under DCTDecode (so the picture a reader extracts is the caller's own file);
    /// anything else is decoded and carried as flate RGB samples.</summary>
    private static PdfDictionary? PictureXObject(byte[] picture)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("XObject"));
        dict.Set("Subtype", new PdfName("Image"));
        dict.Set("BitsPerComponent", new PdfInteger(8));
        if (picture.Length > 2 && picture[0] == 0xFF && picture[1] == 0xD8
            && ImageXObject.TryParseJpegSize(picture) is { } jpeg)
        {
            dict.Set("Width", new PdfInteger(jpeg.width));
            dict.Set("Height", new PdfInteger(jpeg.height));
            dict.Set("ColorSpace", new PdfName(jpeg.nf switch { 1 => "DeviceGray", 4 => "DeviceCMYK", _ => "DeviceRGB" }));
            dict.Set("Filter", new PdfName("DCTDecode"));
            dict.Set("Length", new PdfInteger(picture.Length));
            dict.Set("__StreamData", new PdfString(picture));
            return dict;
        }
        if (DecodeRgb(picture) is not var (rgb, width, height)) return null;
        var packed = PdfSigner.DeflateBytes(rgb);
        dict.Set("Width", new PdfInteger(width));
        dict.Set("Height", new PdfInteger(height));
        dict.Set("ColorSpace", new PdfName("DeviceRGB"));
        dict.Set("Filter", new PdfName("FlateDecode"));
        dict.Set("Length", new PdfInteger(packed.Length));
        dict.Set("__StreamData", new PdfString(packed));
        return dict;
    }

    /// <summary>A non-JPEG picture as packed RGB rows, through the platform decoder;
    /// null where there is none (off Windows) or the bytes are no picture.</summary>
    private static (byte[] Rgb, int Width, int Height)? DecodeRgb(byte[] picture)
    {
        if (!Compat.IsWindows()) return null;
        try
        {
#pragma warning disable CA1416
            using var ms = new System.IO.MemoryStream(picture);
            using var src = System.Drawing.Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            using var bmp = new System.Drawing.Bitmap(src);
            var w = bmp.Width; var h = bmp.Height;
            var rgb = new byte[w * h * 3];
            var i = 0;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    rgb[i++] = c.R; rgb[i++] = c.G; rgb[i++] = c.B;
                }
            return (rgb, w, h);
#pragma warning restore CA1416
        }
        catch { return null; }
    }

    /// <summary>The dictionary every form of the appearance starts from: a unit-matrix
    /// form over the box, named when it is one of the reference's named layers.</summary>
    private static PdfDictionary FormShell(double w, double h, string? ownName)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("XObject"));
        dict.Set("Subtype", new PdfName("Form"));
        dict.Set("FormType", new PdfInteger(1));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
        bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        dict.Set("BBox", bbox);
        var matrix = new PdfArray();
        foreach (var v in new double[] { 1, 0, 0, 1, 0, 0 }) matrix.Add(new PdfReal(v));
        dict.Set("Matrix", matrix);
        if (ownName is not null) dict.Set("Name", new PdfName(ownName));
        return dict;
    }

    internal const string BannerFillRgb = "0.301960784313725 0.501960784313725 1";
    internal const string BannerStrokeRgb = "0.3 0.5 1";
}
