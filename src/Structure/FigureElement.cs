using System.Collections;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Structure;

/// <summary>A figure structure element (/S = "Figure") that wraps a
/// raster or vector picture.</summary>
public class FigureElement : Element
{
    internal FigureElement(PdfDictionary dict, PdfReader reader, Element? parent)
        : base(dict, reader, parent) { }

    /// <summary>The figure's image extracted from the page's /Resources
    /// /XObject dictionary, or null when the figure has no embedded
    /// image stream. Returned as a <see cref="System.Drawing.Image"/>
    /// matching the public type. Throws
    /// <see cref="System.PlatformNotSupportedException"/> on non-Windows
    /// runtimes (per System.Drawing.Common's runtime contract).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public System.Drawing.Image? Image
    {
        get
        {
            // Decide the platform up front. System.Drawing.Common is Windows-only from
            // .NET 7 on, but WHICH exception it raises off Windows varies (a
            // PlatformNotSupportedException, or a TypeInitializationException wrapping
            // Gdip) - and the catch-all at the end of this getter swallowed whichever it
            // was into a null that every caller then dereferenced. Throwing here honours
            // the documented contract exactly and cannot be eaten by that catch.
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException(
                    "FigureElement.Image returns a System.Drawing.Image, which is supported "
                    + "only on Windows.");
            if (_reader is null) return null;
            var stream = ResolveFirstImageStream();
            if (stream is null) return null;
            try
            {
                // Reconstruct a raster via ImageXObject, which decodes the full range
                // of image filters and colour spaces (FlateDecode/ICCBased/DCT/…) into
                // an encoded PNG or JPEG. Feeding raw DecodeStream bytes to
                // Image.FromStream only works for an embedded JPEG codestream and fails
                // for inflated raw samples.
                var xobj = new ImageXObject("Img", stream, _reader);
                using var ms = new MemoryStream();
                xobj.Save(ms);
                ms.Position = 0;
                using var loaded = System.Drawing.Image.FromStream(ms);
                return new System.Drawing.Bitmap(loaded);
            }
            catch
            {
                return null;
            }
        }
    }

    private PdfStream? ResolveFirstImageStream()
    {
        // Walk /K. An entry that resolves to a PdfStream with /Subtype /Image is the
        // figure's image. The PDF spec also allows the image to be referenced via an
        // /MCID marked-content sequence in a page content stream; resolve that too.
        var k = _reader!.Resolve(_dict.Get("K"));
        return FirstStream(k) ?? ResolveImageViaMarkedContent(k);
    }

    private PdfStream? FirstStream(PdfObject? obj)
    {
        switch (obj)
        {
            case PdfStream s when s.Dict.GetName("Subtype") == "Image":
                return s;
            case PdfArray arr:
                foreach (var item in arr)
                {
                    var resolved = _reader!.Resolve(item);
                    var found = FirstStream(resolved);
                    if (found is not null) return found;
                }
                break;
        }
        return null;
    }

    // ── Marked-content (MCID) image resolution ────────────────────────────────

    // Operators of interest, scanned left-to-right: a marked-content point with a
    // property list (BDC), a tag-only marked-content point (BMC), its end (EMC), and an
    // XObject paint (Do). Group captures pick which one matched.
    private static readonly System.Text.RegularExpressions.Regex MarkedContentScanner =
        new(@"(?<bdc>/[\w.\-]+\s*(?<props><<[^>]*?>>|/[\w.\-]+)\s*BDC)" +
            @"|(?<bmc>/[\w.\-]+\s*BMC)" +
            @"|(?<emc>\bEMC\b)" +
            @"|/(?<doname>[\w.\-]+)\s+(?<do>Do)\b",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>Resolve the figure's image when its /K is (or contains) an MCID that
    /// points into a page content stream, by finding the image XObject painted inside
    /// that marked-content region.</summary>
    private PdfStream? ResolveImageViaMarkedContent(PdfObject? k)
    {
        foreach (var (mcid, pgOverride) in CollectMcids(k))
        {
            var page = _reader!.ResolveDict(pgOverride) ?? _reader!.ResolveDict(_dict.Get("Pg"));
            if (page is null) continue;
            var img = FindImageInMarkedContent(page, mcid);
            if (img is not null) return img;
        }
        return null;
    }

    /// <summary>Collect (MCID, optional page) pairs reachable from a structure element's
    /// /K: a bare integer, an /MCR marked-content reference, or an array of either.</summary>
    private IEnumerable<(int mcid, PdfObject? pg)> CollectMcids(PdfObject? k)
    {
        switch (_reader!.Resolve(k))
        {
            case PdfInteger pi:
                yield return ((int)pi.Value, null);
                break;
            case PdfDictionary d when d.GetName("Type") == "MCR":
                yield return ((int)d.GetInt("MCID", -1), d.Get("Pg"));
                break;
            case PdfArray arr:
                foreach (var item in arr)
                    foreach (var pair in CollectMcids(item))
                        yield return pair;
                break;
        }
    }

    private PdfStream? FindImageInMarkedContent(PdfDictionary page, int mcid)
    {
        if (mcid < 0) return null;
        var content = GetPageContentText(page);
        if (content is null) return null;
        var properties = _reader!.ResolveDict(GetInheritedResources(page)?.Get("Properties"));

        // Track the open marked-content stack; an image painted while the target MCID is
        // open (anywhere on the stack, to allow nested marked content) is the figure's.
        var stack = new List<int>();
        foreach (System.Text.RegularExpressions.Match m in MarkedContentScanner.Matches(content))
        {
            if (m.Groups["bdc"].Success)
                stack.Add(ResolveBdcMcid(m.Groups["props"].Value, properties));
            else if (m.Groups["bmc"].Success)
                stack.Add(-1);
            else if (m.Groups["emc"].Success)
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }
            else if (m.Groups["do"].Success)
            {
                if (!stack.Contains(mcid)) continue;
                var xobj = ResolveXObject(page, m.Groups["doname"].Value);
                if (xobj is not null && xobj.Dict.GetName("Subtype") == "Image")
                    return xobj;
            }
        }
        return null;
    }

    /// <summary>Read the MCID of a BDC operand: an inline <c>&lt;&lt;/MCID n&gt;&gt;</c>
    /// dictionary, or a named property list resolved through /Resources/Properties.</summary>
    private int ResolveBdcMcid(string props, PdfDictionary? properties)
    {
        props = props.Trim();
        if (props.StartsWith("<<", StringComparison.Ordinal))
        {
            var mm = System.Text.RegularExpressions.Regex.Match(props, @"/MCID\s+(\d+)");
            return mm.Success ? int.Parse(mm.Groups[1].Value) : -1;
        }
        if (props.StartsWith("/", StringComparison.Ordinal) && properties is not null)
        {
            var pd = _reader!.ResolveDict(properties.Get(props[1..]));
            return pd is not null ? (int)pd.GetInt("MCID", -1) : -1;
        }
        return -1;
    }

    private PdfStream? ResolveXObject(PdfDictionary page, string name)
    {
        var xobjects = _reader!.ResolveDict(GetInheritedResources(page)?.Get("XObject"));
        return _reader!.ResolveStream(xobjects?.Get(name));
    }

    /// <summary>The page's /Resources, walking up the /Pages tree when a page inherits
    /// them rather than carrying its own.</summary>
    private PdfDictionary? GetInheritedResources(PdfDictionary page)
    {
        var node = page;
        for (var depth = 0; node is not null && depth < 32; depth++)
        {
            var res = _reader!.ResolveDict(node.Get("Resources"));
            if (res is not null) return res;
            node = _reader!.ResolveDict(node.Get("Parent"));
        }
        return null;
    }

    private string? GetPageContentText(PdfDictionary page)
    {
        var contents = _reader!.Resolve(page.Get("Contents"));
        if (contents is PdfStream s)
            return Encoding.Latin1.GetString(_reader.DecodeStream(s));
        if (contents is PdfArray arr)
        {
            var sb = new StringBuilder();
            foreach (var item in arr)
                if (_reader.ResolveStream(item) is PdfStream cs)
                {
                    sb.Append(Encoding.Latin1.GetString(_reader.DecodeStream(cs)));
                    sb.Append('\n');
                }
            return sb.ToString();
        }
        return null;
    }
}
