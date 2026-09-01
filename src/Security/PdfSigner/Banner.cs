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
internal static class SignatureBanner
{
    /// <summary>Lines the banner draws, in order. A line whose value is empty is
    /// dropped, so a signature with no reason shows no "Reason:" row.</summary>
    internal static List<string> Lines(string? signerName, DateTime signDate,
        string? reason, string? location, string? contact)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(signerName))
            lines.Add($"Digitally signed by '{signerName}'");
        if (signDate.Kind == DateTimeKind.Utc) signDate = signDate.ToLocalTime();
        lines.Add("Date: " + signDate.ToString("yyyy.MM.dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(reason)) lines.Add($"Reason: {reason}");
        if (!string.IsNullOrEmpty(location)) lines.Add($"Location: {location}");
        if (!string.IsNullOrEmpty(contact)) lines.Add($"Contact: {contact}");
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
            if (Aspose.Pdf.Stamps.TextStamp.TryResolveCjkTtf(all) is { } cjk)
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
        var xobj = new PdfDictionary();
        xobj.Set(invokeName, new PdfIndirectRef(targetObj, 0));
        var res = new PdfDictionary();
        res.Set("XObject", xobj);
        dict.Set("Resources", res);
        var content = Encoding.ASCII.GetBytes($"q\n1 0 0 1 0 0 cm\n/{invokeName} Do\nQ\n");
        dict.Set("Length", new PdfInteger(content.Length));
        dict.Set("__StreamData", new PdfString(content));
        return dict;
    }

    internal const string BannerFillRgb = "0.301960784313725 0.501960784313725 1";
    internal const string BannerStrokeRgb = "0.3 0.5 1";
}
