using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for common PDF file editing operations: concatenate, split, extract, delete pages.
/// </summary>
public sealed partial class PdfFileEditor
{
    /// <summary>
    /// Obsolete property kept for API compatibility. Always throws NotSupportedException.
    /// </summary>
    public bool AllowConcatenateExceptions
    {
        get => throw new NotSupportedException("AllowConcatenateExceptions is not supported.");
        set => throw new NotSupportedException("AllowConcatenateExceptions is not supported.");
    }

    private bool? _keepFieldsUnique;

    /// <summary>When true, Concatenate appends pages to the destination
    /// using PDF incremental updates instead of rewriting the entire file.
    /// Stored only; the writer always full-rewrites.</summary>
    public bool IncrementalUpdates { get; set; }

    /// <summary>When true, Concatenate runs an optimization pass on the
    /// output before saving. Stored only; no automatic optimization is performed.</summary>
    public bool OptimizeSize { get; set; }

    /// <summary>Buffer-size hint (bytes) used by streaming Concatenate
    /// implementations. Stored only; the implementation always reads full inputs.</summary>
    public int ConcatenationPacketSize { get; set; }

    /// <summary>When true, Concatenate buffers intermediate pages on disk
    /// instead of in memory. Stored only; the implementation always works in-memory.</summary>
    public bool UseDiskBuffer { get; set; }


    /// <summary>Error message for null/empty page ranges.</summary>
    public const string E_EMPTY_PAGE_RANGE = "Page ranges must not be null or empty";
    /// <summary>Error message for a page range with fewer than 2 elements.</summary>
    public const string E_SMALL_PAGE_RANGE = "Each page range must have at least 2 elements (start and end)";
    /// <summary>Error message for a page range where start > end.</summary>
    public const string E_WRONG_PAGE_RANGE = "Page range start must not be greater than end";

    // ── ResizeContents ────────────────────────────────────────────────────────

    // ── File-path overloads ─────────────────────────────────────────────────

    // ── Stream-based overloads ────────────────────────────────────────

    // ── XFA form merge ──────────────────────────────────────────────────────
    // When a Concatenate combines two or more XFA forms, each input's top-level
    // template subform(s) are re-parented under one synthetic "root" subform (with
    // the datasets data nodes wrapped in a matching <root> element), disambiguating
    // colliding names. The XFA merge rules:
    //   • KeepFieldsUnique explicitly false      → keep duplicate names as occurrences
    //   • UniqueSuffix explicitly set             → rename duplicates with that suffix
    //   • otherwise (default)                     → identical subtree kept as an
    //                                               occurrence, differing subtree renamed
    //                                               name → name+N (plain occurrence index)

    /// <summary>Split an AcroForm field /T like "eApp[0]" into its base name ("eApp") and
    /// trailing occurrence index ("[0]"). The XFA rename map is keyed by the base name; the
    /// index is preserved so a renamed field becomes e.g. "eApp1[0]".</summary>
    private static (string baseName, string indexSuffix) SplitFieldNameIndex(string t)
    {
        int b = t.LastIndexOf('[');
        if (b > 0 && t.EndsWith("]", StringComparison.Ordinal))
            return (t.Substring(0, b), t.Substring(b));
        return (t, string.Empty);
    }

    /// <summary>Widget-level dictionary keys (the visual annotation) that are moved off a
    /// field dict into a /Kids entry when two same-named fields are merged; field-level keys
    /// (/T, /FT, /V, /DA, /Ff, …) stay on the parent.</summary>
    private static readonly HashSet<string> s_widgetKeys = new()
    { "Rect", "AP", "AS", "MK", "BS", "Border", "F", "H" };

    /// <summary>The top structure elements an input contributes: the root's /K elements,
    /// except that a top element which is itself a Document contributes its CHILDREN.
    /// Each is returned with the source object number its /K reference carried (or -1 for
    /// an inline dictionary), so the caller can pre-seed the remap.</summary>
    private static IEnumerable<(PdfDictionary Dict, int SrcNum)> TopStructElements(
        PdfDictionary structRoot, PdfReader reader)
    {
        foreach (var (dict, srcNum) in KidEntries(structRoot, reader))
        {
            if (dict.GetName("S") == "Document")
            {
                foreach (var inner in KidEntries(dict, reader))
                    yield return inner;
            }
            else
            {
                yield return (dict, srcNum);
            }
        }
    }

    /// <summary>The /K entries of a structure dictionary that are themselves structure
    /// elements, each with the object number of the reference that reached it (-1 when
    /// the entry was an inline dictionary).</summary>
    private static IEnumerable<(PdfDictionary Dict, int SrcNum)> KidEntries(
        PdfDictionary structDict, PdfReader reader)
    {
        var k = structDict.Get("K");
        var entries = new List<PdfObject>();
        if (reader.Resolve(k) is PdfArray arr) entries.AddRange(arr);
        else if (k is not null) entries.Add(k);
        foreach (var entry in entries)
        {
            var srcNum = entry is PdfIndirectRef r ? r.ObjectNumber : -1;
            if (reader.ResolveDict(entry) is { } d && d.GetName("Type") is null or "StructElem")
                yield return (d, srcNum);
        }
    }

    /// <summary>Flatten a number tree (leaf /Nums, intermediate /Kids) into key-value pairs.</summary>
    private static IEnumerable<(int Key, PdfObject Value)> NumberTreeEntries(
        PdfDictionary? node, PdfReader reader)
    {
        if (node is null) yield break;
        if (reader.Resolve(node.Get("Nums")) is PdfArray nums)
            for (var i = 0; i + 1 < nums.Count; i += 2)
                if (reader.Resolve(nums[i]) is PdfInteger key)
                    yield return ((int)key.Value, nums[i + 1]);
        if (reader.Resolve(node.Get("Kids")) is PdfArray kids)
            foreach (var kid in kids)
                foreach (var entry in NumberTreeEntries(reader.ResolveDict(kid), reader))
                    yield return entry;
    }

}
