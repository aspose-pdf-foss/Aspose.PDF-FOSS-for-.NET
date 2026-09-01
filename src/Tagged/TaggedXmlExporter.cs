using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Tagged;

/// <summary>
/// Tagged PDF → XML conversion (SaveFormat.Xml): serialises the document's
/// logical structure tree as an XML document. Each structure element becomes
/// an element named by its /S structure type, its /A attribute dictionary
/// becomes XML attributes (alphabetical, names as bare values, numbers in
/// invariant format, arrays comma-joined), and a leaf element's marked-content
/// references are resolved to the text they mark in the content stream.
/// Output framing: UTF-8 (no BOM), CRLF line ends,
/// two-space indent, no trailing newline.
/// </summary>
internal static class TaggedXmlExporter
{
    internal static byte[] Export(Document document)
    {
        var reader = document.Reader;
        // XML conversion serialises the logical structure tree, so a document
        // without one cannot be converted at all — that is a document-level
        // condition callers catch as PdfException, not a programming error.
        var root = reader.ResolveDict(reader.Catalog.Get("StructTreeRoot"))
            ?? throw new PdfException(
                "Only tagged PDF documents are supported for XML conversion: the document has no structure tree (/StructTreeRoot).");

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n");
        var ctx = new ExportContext(reader);

        sb.Append("<StructTreeRoot>");
        var kids = KidList(reader, root);
        if (kids.Count > 0)
        {
            sb.Append("\r\n");
            foreach (var kid in kids)
                EmitElement(sb, ctx, kid, 1);
            sb.Append("</StructTreeRoot>");
        }
        else
        {
            sb.Append("</StructTreeRoot>");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private sealed class ExportContext(PdfReader reader)
    {
        public PdfReader Reader { get; } = reader;
        // MCID → text, computed once per distinct content stream.
        public Dictionary<PdfStream, Dictionary<int, string>> StreamTexts { get; } = new();
        public Dictionary<PdfDictionary, Dictionary<int, string>> PageTexts { get; } = new();
    }

    private static void EmitElement(StringBuilder sb, ExportContext ctx, PdfDictionary elem, int depth)
    {
        var reader = ctx.Reader;
        var name = elem.GetName("S") ?? "NonStruct";
        var indent = new string(' ', depth * 2);

        sb.Append(indent).Append('<').Append(name);
        foreach (var (key, value) in AttributePairs(reader, elem))
            sb.Append(' ').Append(key).Append("=\"").Append(EscapeAttr(value)).Append('"');

        // Split kids into nested structure elements vs marked-content leaves.
        var structKids = new List<PdfDictionary>();
        var text = new StringBuilder();
        foreach (var kidObj in RawKids(reader, elem))
        {
            var resolved = reader.Resolve(kidObj);
            if (resolved is PdfInteger mcid)
            {
                text.Append(McidText(ctx, PageDict(reader, elem), null, (int)mcid.Value));
            }
            else if (resolved is PdfDictionary kd)
            {
                var type = kd.GetName("Type");
                if (type == "MCR")
                {
                    var stm = reader.ResolveStream(kd.Get("Stm"));
                    var m = reader.Resolve(kd.Get("MCID")) as PdfInteger;
                    if (m is not null)
                        text.Append(McidText(ctx, PageDict(reader, kd) ?? PageDict(reader, elem), stm, (int)m.Value));
                }
                else if (type == "OBJR")
                {
                    // Object references (annotations etc.) carry no text.
                }
                else
                {
                    structKids.Add(kd);
                }
            }
        }

        if (structKids.Count == 0)
        {
            sb.Append('>').Append(EscapeText(text.ToString())).Append("</").Append(name).Append(">\r\n");
        }
        else
        {
            sb.Append(">\r\n");
            foreach (var kid in structKids)
                EmitElement(sb, ctx, kid, depth + 1);
            sb.Append(indent).Append("</").Append(name).Append(">\r\n");
        }
    }

    /// <summary>The element's /A attributes as (name, formatted value), merged over an
    /// attribute array in order and sorted alphabetically by name.</summary>
    private static List<(string Key, string Value)> AttributePairs(PdfReader reader, PdfDictionary elem)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Merge(PdfObject? attrObj)
        {
            if (reader.Resolve(attrObj) is not PdfDictionary attrs) return;
            foreach (var key in attrs.Keys)
                merged[key] = FormatValue(reader.Resolve(attrs.Get(key)));
        }

        var a = reader.Resolve(elem.Get("A"));
        if (a is PdfArray arr)
            foreach (var item in arr)
                Merge(item); // revision numbers (ints) resolve to non-dicts and are skipped
        else if (a is not null)
            Merge(a);
        return merged.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private static string FormatValue(PdfObject? value) => value switch
    {
        PdfName n => n.Value,
        PdfString s => s.ToText(),
        PdfInteger i => i.Value.ToString(CultureInfo.InvariantCulture),
        PdfReal r => FormatNumber(r.Value),
        PdfBoolean b => b.Value ? "true" : "false",
        PdfArray arr => string.Join(",", arr.Select(FormatValue)),
        _ => string.Empty,
    };

    private static string FormatNumber(double v)
        => v == Math.Floor(v) && Math.Abs(v) < long.MaxValue
            ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.####", CultureInfo.InvariantCulture);

    private static List<PdfDictionary> KidList(PdfReader reader, PdfDictionary elem)
    {
        var result = new List<PdfDictionary>();
        foreach (var kidObj in RawKids(reader, elem))
            if (reader.Resolve(kidObj) is PdfDictionary kd && kd.GetName("Type") is not ("MCR" or "OBJR"))
                result.Add(kd);
        return result;
    }

    private static IEnumerable<PdfObject> RawKids(PdfReader reader, PdfDictionary elem)
    {
        var k = reader.Resolve(elem.Get("K"));
        if (k is PdfArray arr)
        {
            foreach (var item in arr)
                if (item is not null)
                    yield return item;
        }
        else if (k is not null)
        {
            yield return k;
        }
    }

    /// <summary>The page dictionary a struct elem / MCR is associated with (/Pg,
    /// inherited by the caller when absent here).</summary>
    private static PdfDictionary? PageDict(PdfReader reader, PdfDictionary elem)
        => reader.ResolveDict(elem.Get("Pg"));

    /// <summary>Text shown inside marked content with the given MCID. The content is the
    /// referenced stream (/Stm) when present, else the page's /Contents.</summary>
    private static string McidText(ExportContext ctx, PdfDictionary? page, PdfStream? stm, int mcid)
    {
        var reader = ctx.Reader;
        Dictionary<int, string> map;
        if (stm is not null)
        {
            if (!ctx.StreamTexts.TryGetValue(stm, out map!))
            {
                map = BuildMcidTextMap(reader, new[] { stm },
                    reader.ResolveDict(stm.Dict.Get("Resources")));
                ctx.StreamTexts[stm] = map;
            }
        }
        else
        {
            if (page is null) return string.Empty;
            if (!ctx.PageTexts.TryGetValue(page, out map!))
            {
                var streams = new List<PdfStream>();
                var contents = reader.Resolve(page.Get("Contents"));
                if (contents is PdfStream single) streams.Add(single);
                else if (contents is PdfArray carr)
                    foreach (var item in carr)
                        if (reader.ResolveStream(item) is { } cs) streams.Add(cs);
                map = BuildMcidTextMap(reader, streams, reader.ResolveDict(page.Get("Resources")));
                ctx.PageTexts[page] = map;
            }
        }
        return map.TryGetValue(mcid, out var text) ? text : string.Empty;
    }

    private static Dictionary<int, string> BuildMcidTextMap(
        PdfReader reader, IEnumerable<PdfStream> streams, PdfDictionary? resources)
    {
        var result = new Dictionary<int, string>();
        var fonts = new Dictionary<string, PdfDictionary>();
        var fontsDict = resources is not null ? reader.ResolveDict(resources.Get("Font")) : null;
        if (fontsDict is not null)
            foreach (var key in fontsDict.Keys)
                if (reader.ResolveDict(fontsDict.Get(key)) is { } fd)
                    fonts[key] = fd;
        var properties = resources is not null ? reader.ResolveDict(resources.Get("Properties")) : null;

        // Innermost enclosing MCID receives the text; MCID-less frames push null so
        // EMC pops stay balanced.
        var stack = new List<int?>();
        var parser = new Content.ContentStreamParser(reader);
        parser.OnMarkedContentBegin += (_, props) =>
            stack.Add(props?.Get("MCID") is PdfInteger m ? (int)m.Value : null);
        parser.OnMarkedContentEnd += () =>
        {
            if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
        };
        parser.OnTextShown += (text, _, _) =>
        {
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (stack[i] is { } mcid)
                {
                    result[mcid] = result.TryGetValue(mcid, out var prev) ? prev + text : text;
                    return;
                }
            }
        };

        foreach (var s in streams)
        {
            try
            {
                parser.Parse(reader.DecodeStream(s), fonts, properties: properties);
            }
            catch
            {
                // Undecodable stream: its marked content simply yields no text.
            }
        }
        return result;
    }

    private static string EscapeAttr(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string EscapeText(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
