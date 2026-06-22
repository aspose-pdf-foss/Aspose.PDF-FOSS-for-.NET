using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Tagged;

/// <summary>
/// Read-only view of a tagged document's structure-tree root
/// (/StructTreeRoot). Walks the structure-element hierarchy so callers
/// can inspect the logical structure of an existing document.
/// </summary>
public sealed class StructTreeRoot
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader _reader;
    private List<StructTreeElement>? _children;

    internal StructTreeRoot(PdfDictionary dict, PdfReader reader)
    {
        _dict = dict;
        _reader = reader;
    }

    /// <summary>Top-level structure elements directly under the root.</summary>
    public IReadOnlyList<StructTreeElement> Children
    {
        get
        {
            if (_children is not null) return _children;
            _children = new List<StructTreeElement>();

            var kids = _reader.Resolve(_dict.Get("K"));
            if (kids is PdfArray arr)
            {
                foreach (var item in arr)
                {
                    var childDict = _reader.ResolveDict(item);
                    if (childDict is not null && childDict.GetName("Type") is null or "StructElem")
                        _children.Add(new StructTreeElement(childDict, _reader));
                }
            }
            else if (kids is PdfDictionary singleChild &&
                     singleChild.GetName("Type") is null or "StructElem")
            {
                _children.Add(new StructTreeElement(singleChild, _reader));
            }

            return _children;
        }
    }

    /// <summary>The role map (/RoleMap) that maps custom roles to standard types.</summary>
    public IReadOnlyDictionary<string, string> RoleMap
    {
        get
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var roleMapDict = _reader.ResolveDict(_dict.Get("RoleMap"));
            if (roleMapDict is not null)
            {
                foreach (var key in roleMapDict.Keys)
                {
                    var value = roleMapDict.GetName(key);
                    if (value is not null)
                        map[key] = value;
                }
            }
            return map;
        }
    }
}

/// <summary>
/// Read-only view of a single structure element in a tagged document's
/// structure tree: its role, accessibility text, attributes,
/// marked-content references, and child elements.
/// </summary>
public sealed class StructTreeElement
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader? _reader;
    private List<StructTreeElement>? _children;

    internal StructTreeElement(PdfDictionary dict, PdfReader? reader)
    {
        _dict = dict;
        _reader = reader;
    }

    /// <summary>The structure type / role (/S), e.g. "Document", "P", "Table".</summary>
    public string? StructureType => _dict.GetName("S");

    /// <summary>The element title (/T), or null when none is set.</summary>
    public string? Title => ReadString("T");

    /// <summary>The element language (/Lang), or null when none is set.</summary>
    public string? Language => ReadString("Lang");

    /// <summary>The replacement text (/ActualText), or null when none is set.</summary>
    public string? ActualText => ReadString("ActualText");

    private string? ReadString(string key)
    {
        var obj = _reader?.Resolve(_dict.Get(key)) ?? _dict.Get(key);
        return obj is PdfString s ? s.ToText() : null;
    }

    /// <summary>The alternate description (/Alt) used by assistive technology,
    /// or null when none is set.</summary>
    public string? AltText
    {
        get
        {
            var obj = _reader?.Resolve(_dict.Get("Alt")) ?? _dict.Get("Alt");
            return obj is PdfString s ? s.ToText() : null;
        }
    }

    /// <summary>The structure attributes (/A) as a string-valued map, or null
    /// when the element has no attributes. Numbers, names, and booleans are
    /// surfaced as their textual form.</summary>
    public IReadOnlyDictionary<string, string>? Attributes
    {
        get
        {
            var attrObj = _reader?.Resolve(_dict.Get("A")) ?? _dict.Get("A");
            if (attrObj is not PdfDictionary attrDict) return null;

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in attrDict.Keys)
            {
                var val = attrDict.Get(key);
                var text = val switch
                {
                    PdfName n => n.Value,
                    PdfString s => s.ToText(),
                    PdfInteger i => i.Value.ToString(),
                    PdfReal r => r.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    PdfBoolean b => b == PdfBoolean.True ? "true" : "false",
                    _ => val?.ToString()
                };
                if (text is not null)
                    result[key] = text;
            }
            return result;
        }
    }

    /// <summary>The marked-content identifiers (MCIDs) referenced by this
    /// element's /K entry, connecting it to marked-content sequences in the
    /// page content streams.</summary>
    public IReadOnlyList<int> MarkedContentIds
    {
        get
        {
            var ids = new List<int>();
            var kids = _reader?.Resolve(_dict.Get("K")) ?? _dict.Get("K");

            switch (kids)
            {
                case PdfInteger singleMcid:
                    ids.Add((int)singleMcid.Value);
                    break;
                case PdfDictionary mcDict when mcDict.GetName("Type") == "MCR":
                    ids.Add((int)mcDict.GetInt("MCID"));
                    break;
                case PdfArray arr:
                    foreach (var item in arr)
                    {
                        var resolved = _reader?.Resolve(item) ?? item;
                        if (resolved is PdfInteger mcid)
                            ids.Add((int)mcid.Value);
                        else if (resolved is PdfDictionary d && d.GetName("Type") == "MCR")
                            ids.Add((int)d.GetInt("MCID"));
                    }
                    break;
            }

            return ids;
        }
    }

    /// <summary>Child structure elements (read-only).</summary>
    public IReadOnlyList<StructTreeElement> Children
    {
        get
        {
            if (_children is not null) return _children;
            _children = new List<StructTreeElement>();
            if (_reader is null) return _children;

            var kids = _reader.Resolve(_dict.Get("K"));
            if (kids is PdfArray arr)
            {
                foreach (var item in arr)
                {
                    var resolved = _reader.Resolve(item);
                    if (resolved is PdfDictionary childDict &&
                        childDict.GetName("Type") is null or "StructElem")
                        _children.Add(new StructTreeElement(childDict, _reader));
                }
            }
            else if (kids is PdfDictionary singleChild &&
                     singleChild.GetName("Type") is null or "StructElem")
            {
                _children.Add(new StructTreeElement(singleChild, _reader));
            }

            return _children;
        }
    }
}
