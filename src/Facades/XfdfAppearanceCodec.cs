using System;
using System.Globalization;
using System.Text;
using System.Xml;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Serializes an annotation's appearance object tree (/AP and everything it
/// references — form XObjects, resources, fonts, streams) into the XFDF
/// &lt;appearance&gt; payload, and rebuilds the tree from such a payload.
///
/// The payload is an XML document (carried base64-encoded in the XFDF): each PDF
/// object maps to an element — DICT, STREAM, ARRAY, INT, FIXED, NAME, BOOL,
/// STRING, NULL — with a KEY attribute inside dictionaries and positional
/// children inside arrays. Dictionary entries are written in ordinal key order.
/// A stream's payload rides in a DATA child: binary content keeps its encoded
/// bytes as wrapped uppercase hex (MODE="RAW" ENCODING="HEX"), while content
/// whose DECODED form is plain text is written decoded and XML-escaped
/// (MODE="FILTERED" ENCODING="ASCII").
/// </summary>
internal static class XfdfAppearanceCodec
{
    private const int HexLineWidth = 80;

    // ---------------------------------------------------------------- export

    public static string Serialize(IO.PdfReader reader, string rootKey, PdfObject root)
    {
        var sb = new StringBuilder();
        WriteNode(sb, reader, rootKey, root, depth: 0);
        return sb.ToString();
    }

    private static void WriteNode(StringBuilder sb, IO.PdfReader reader, string? key, PdfObject? obj, int depth)
    {
        // The appearance tree is written fully inline: indirect references are
        // resolved and their targets embedded in place. Depth-capped so a cyclic
        // reference (a malformed tree) cannot recurse forever.
        if (depth > 48) return;
        obj = reader.Resolve(obj);
        var keyAttr = key is null ? "" : $" KEY=\"{Esc(key)}\"";
        switch (obj)
        {
            case PdfDictionary d:
                sb.Append($"<DICT{keyAttr}>\r\n");
                WriteEntries(sb, reader, d, depth);
                sb.Append("</DICT>\r\n");
                break;
            case PdfStream s:
                WriteStream(sb, reader, keyAttr, s, depth);
                break;
            case PdfArray a:
                sb.Append($"<ARRAY{keyAttr}>\r\n");
                foreach (var item in a)
                    WriteNode(sb, reader, null, item, depth + 1);
                sb.Append("</ARRAY>\r\n");
                break;
            case PdfInteger i:
                sb.Append($"<INT{keyAttr} VAL=\"{i.Value.ToString(CultureInfo.InvariantCulture)}\" />\r\n");
                break;
            case PdfReal r:
                sb.Append($"<FIXED{keyAttr} VAL=\"{r}\" />\r\n");
                break;
            case PdfName n:
                sb.Append($"<NAME{keyAttr} VAL=\"{Esc(n.Value)}\" />\r\n");
                break;
            case PdfBoolean b:
                sb.Append($"<BOOL{keyAttr} VAL=\"{(b.Value ? "True" : "False")}\" />\r\n");
                break;
            case PdfString ps:
                if (IsTextSafe(ps.Value))
                    sb.Append($"<STRING{keyAttr} ENCODING=\"ASCII\">{Esc(Encoding.ASCII.GetString(ps.Value))}</STRING>\r\n");
                else
                    sb.Append($"<STRING{keyAttr} ENCODING=\"HEX\">{ToHex(ps.Value)}</STRING>\r\n");
                break;
            case PdfNull:
                sb.Append($"<NULL{keyAttr} />\r\n");
                break;
        }
    }

    private static void WriteEntries(StringBuilder sb, IO.PdfReader reader, PdfDictionary d, int depth)
    {
        var keys = new System.Collections.Generic.List<string>(d.Keys);
        keys.Sort(StringComparer.Ordinal);
        foreach (var k in keys)
            WriteNode(sb, reader, k, d.Get(k), depth + 1);
    }

    private static void WriteStream(StringBuilder sb, IO.PdfReader reader, string keyAttr, PdfStream s, int depth)
    {
        byte[]? decoded = null;
        try { decoded = reader.DecodeStream(s); } catch { /* keep encoded-only */ }

        bool filteredText = decoded is not null && IsTextSafe(decoded);
        sb.Append($"<STREAM{keyAttr} DEFINE=\"\">\r\n");
        WriteEntries(sb, reader, s.Dict, depth);
        if (filteredText)
        {
            sb.Append("<DATA MODE=\"FILTERED\" ENCODING=\"ASCII\">\n");
            sb.Append(Esc(Encoding.ASCII.GetString(decoded!)));
            sb.Append("\n</DATA>\r\n");
        }
        else
        {
            sb.Append("<DATA MODE=\"RAW\" ENCODING=\"HEX\">\n");
            AppendWrappedHex(sb, s.RawData);
            sb.Append("\n</DATA>\r\n");
        }
        sb.Append("</STREAM>\r\n");
    }

    private static void AppendWrappedHex(StringBuilder sb, byte[] data)
    {
        var hex = ToHex(data);
        for (int i = 0; i < hex.Length; i += HexLineWidth)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(hex, i, Math.Min(HexLineWidth, hex.Length - i));
        }
    }

    private static string ToHex(byte[] data)
        => Convert.ToHexString(data);

    private static bool IsTextSafe(byte[] data)
    {
        foreach (var b in data)
            if (b is not (9 or 10 or 13) && (b < 0x20 || b > 0x7E)) return false;
        return true;
    }

    private static string Esc(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    // ---------------------------------------------------------------- import

    /// <summary>Rebuild the appearance object from a serialized payload; returns
    /// null when the text is not an appearance-tree document.</summary>
    public static PdfObject? Deserialize(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        var doc = new XmlDocument();
        try { doc.LoadXml(xml); } catch { return null; }
        return doc.DocumentElement is { } rootEl ? ReadNode(rootEl) : null;
    }

    private static PdfObject? ReadNode(XmlElement el)
    {
        switch (el.Name)
        {
            case "DICT":
            {
                var d = new PdfDictionary();
                foreach (XmlNode child in el.ChildNodes)
                    if (child is XmlElement ce && ce.GetAttribute("KEY") is { Length: > 0 } k)
                        { var v = ReadNode(ce); if (v is not null) d.Set(k, v); }
                return d;
            }
            case "ARRAY":
            {
                var a = new PdfArray();
                foreach (XmlNode child in el.ChildNodes)
                    if (child is XmlElement ce)
                        { var v = ReadNode(ce); if (v is not null) a.Add(v); }
                return a;
            }
            case "STREAM":
                return ReadStream(el);
            case "INT":
                return long.TryParse(el.GetAttribute("VAL"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                    ? new PdfInteger(i) : null;
            case "FIXED":
                return double.TryParse(el.GetAttribute("VAL"), NumberStyles.Float, CultureInfo.InvariantCulture, out var r)
                    ? new PdfReal(r) : null;
            case "NAME":
                return new PdfName(el.GetAttribute("VAL"));
            case "BOOL":
                return string.Equals(el.GetAttribute("VAL"), "true", StringComparison.OrdinalIgnoreCase)
                    ? PdfBoolean.True : PdfBoolean.False;
            case "STRING":
                return el.GetAttribute("ENCODING") == "HEX"
                    ? new PdfString(FromHex(el.InnerText))
                    : new PdfString(Encoding.ASCII.GetBytes(el.InnerText));
            case "NULL":
                return PdfNull.Instance;
            default:
                return null;
        }
    }

    private static PdfObject ReadStream(XmlElement el)
    {
        var dict = new PdfDictionary();
        byte[] data = Array.Empty<byte>();
        bool filtered = false;
        foreach (XmlNode child in el.ChildNodes)
        {
            if (child is not XmlElement ce) continue;
            if (ce.Name == "DATA")
            {
                filtered = ce.GetAttribute("MODE") == "FILTERED";
                var text = ce.InnerText;
                if (ce.GetAttribute("ENCODING") == "HEX")
                    data = FromHex(text);
                else
                {
                    // A FILTERED payload opens/closes with the wrap newline the
                    // writer added around the content; strip exactly that pair.
                    if (text.StartsWith('\n')) text = text[1..];
                    if (text.EndsWith('\n')) text = text[..^1];
                    data = Encoding.ASCII.GetBytes(text);
                }
            }
            else if (ce.GetAttribute("KEY") is { Length: > 0 } k)
            {
                var v = ReadNode(ce);
                if (v is not null) dict.Set(k, v);
            }
        }
        if (filtered)
        {
            // The payload carries DECODED content — the declared source filter no
            // longer applies; the writer re-compresses plain streams on save.
            dict.Remove("Filter");
            dict.Remove("DecodeParms");
            dict.Remove("DP");
        }
        dict.Set("Length", new PdfInteger(data.Length));
        return new PdfStream(dict, data);
    }

    private static byte[] FromHex(string text)
    {
        var compact = new StringBuilder(text.Length);
        foreach (var c in text)
            if (!char.IsWhiteSpace(c)) compact.Append(c);
        return Convert.FromHexString(compact.ToString());
    }
}
