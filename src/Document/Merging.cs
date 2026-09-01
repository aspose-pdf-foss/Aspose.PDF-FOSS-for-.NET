using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    public static Document MergeDocuments(params byte[][] documents)
    {
        if (documents.Length == 0) return Create();
        var result = Open(documents[0]);
        for (int i = 1; i < documents.Length; i++)
        {
            using var source = Open(documents[i]);
            var pageNums = Enumerable.Range(1, source.PageCount).ToArray();
            result.ImportPages(source, pageNums);
        }
        return result;
    }

    public static Document MergeDocuments(params string[] files)
    {
        var bytes = new byte[files.Length][];
        for (int i = 0; i < files.Length; i++)
            bytes[i] = File.ReadAllBytes(files[i]);
        return MergeDocuments(bytes);
    }

    /// <summary>Merge every page of <paramref name="documents"/> into a new
    /// destination <see cref="Document"/>. Source documents are read but
    /// left unchanged.</summary>
    public static Document MergeDocuments(params Document[] documents)
    {
        var target = Create();
        target.Merge(documents);
        return target;
    }

    /// <summary>Merge every page of each <paramref name="documents"/> entry
    /// into this document, preserving source order.</summary>
    public void Merge(params Document[] documents)
    {
        if (documents is null) return;
        foreach (var d in documents)
            if (d is not null) Pages.Add(d.Pages);
    }

    /// <summary>Merge every page of each file in <paramref name="files"/>
    /// into this document. Sources are opened and disposed by this method.</summary>
    public void Merge(params string[] files)
    {
        if (files is null) return;
        foreach (var f in files)
        {
            using var d = new Document(f);
            Pages.Add(d.Pages);
        }
    }

    /// <summary>Merge with explicit options. Real — RemoveSignatures strips
    /// /V from every signature field; MergeDuplicateOutlines deduplicates
    /// catalog outline trees by title+page; KeepFieldsUnique appends
    /// "_2", "_3" suffixes to colliding form-field names.</summary>
    public void Merge(MergeOptions mergeOptions, params Document[] documents)
    {
        Merge(documents);
        ApplyMergeOptions(mergeOptions);
    }

    /// <summary>Merge files with explicit options — same semantics as
    /// the Document[] overload.</summary>
    public void Merge(MergeOptions mergeOptions, params string[] files)
    {
        Merge(files);
        ApplyMergeOptions(mergeOptions);
    }

    /// <summary>Static Merge: build a fresh Document containing every
    /// page from <paramref name="files"/>, then apply <paramref name="mergeOptions"/>.</summary>
    public static Document MergeDocuments(MergeOptions mergeOptions, params Document[] files)
    {
        var target = Create();
        target.Merge(mergeOptions, files);
        return target;
    }

    /// <summary>Static Merge: file-paths variant.</summary>
    public static Document MergeDocuments(MergeOptions mergeOptions, params string[] files)
    {
        var target = Create();
        target.Merge(mergeOptions, files);
        return target;
    }

    private void ApplyMergeOptions(MergeOptions? options)
    {
        if (options is null) return;
        if (options.RemoveSignatures)
        {
            var form = Form;
            if (form is not null)
            {
                foreach (var field in form.Fields)
                {
                    if (field.Type != Forms.FieldType.Signature) continue;
                    field.Dict.Remove("V");
                }
            }
        }
        if (options.MergeDuplicateOutlines)
            DeduplicateOutlines();
        if (options.KeepFieldsUnique)
            DisambiguateFormFieldNames();
        // RemoveUserRights: strip /Perms /UR / /UR3 from catalog.
        if (options.RemoveUserRights)
        {
            var perms = _reader.ResolveDict(_reader.Catalog.Get("Perms"));
            if (perms is not null)
            {
                perms.Remove("UR");
                perms.Remove("UR3");
                if (!perms.Keys.Any())
                    _reader.Catalog.Remove("Perms");
            }
        }
    }

    private void DeduplicateOutlines()
    {
        // The outline tree may contain duplicate entries with identical
        // /Title + /Dest after a merge. Walk /Outlines + /First/Next and
        // remove later items whose (Title, page-number) tuple matches an
        // earlier one.
        var outlinesObj = _reader.ResolveDict(_reader.Catalog.Get("Outlines"));
        if (outlinesObj is null) return;
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        DedupeOutlineList(outlinesObj, seen);
    }

    private void DedupeOutlineList(Aspose.Pdf.Core.PdfDictionary parent, HashSet<string> seen)
    {
        var current = _reader.ResolveDict(parent.Get("First"));
        Aspose.Pdf.Core.PdfDictionary? prev = null;
        while (current is not null)
        {
            var title = current.Get("Title") is Aspose.Pdf.Core.PdfString s ? s.ToText() : "";
            var key = title + "|" + DescribeDestination(current);
            var next = _reader.ResolveDict(current.Get("Next"));
            if (seen.Contains(key))
            {
                // Splice current out of the list.
                if (prev is null) parent.Set("First", current.Get("Next") ?? (Aspose.Pdf.Core.PdfObject)Aspose.Pdf.Core.PdfNull.Instance);
                else if (current.Get("Next") is { } nxt) prev.Set("Next", nxt);
                else prev.Remove("Next");
            }
            else
            {
                seen.Add(key);
                // Recurse into nested children.
                if (current.ContainsKey("First")) DedupeOutlineList(current, seen);
                prev = current;
            }
            current = next;
        }
    }

    private string DescribeDestination(Aspose.Pdf.Core.PdfDictionary outlineItem)
    {
        var dest = _reader.Resolve(outlineItem.Get("Dest"));
        return dest switch
        {
            Aspose.Pdf.Core.PdfArray arr when arr.Count > 0 => arr[0]?.ToString() ?? "",
            Aspose.Pdf.Core.PdfString s => s.ToText(),
            Aspose.Pdf.Core.PdfName n => n.Value,
            _ => "",
        };
    }

    private void DisambiguateFormFieldNames()
    {
        var form = Form;
        if (form is null) return;
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var field in form.Fields)
        {
            var name = field.FullName ?? string.Empty;
            if (string.IsNullOrEmpty(name)) continue;
            if (seen.Add(name)) continue;
            var idx = 2;
            string candidate;
            do { candidate = $"{name}_{idx++}"; } while (!seen.Add(candidate));
            field.SetPartialName(candidate);
        }
    }

    /// <summary>Render this document through <paramref name="device"/> into
    /// <paramref name="output"/> — delegates to <see cref="Devices.DocumentDevice.Process(Document, System.IO.Stream)"/>.</summary>
    public void SendTo(Devices.DocumentDevice device, Stream output)
        => device?.Process(this, output);

    /// <summary>File overload of <see cref="SendTo(Devices.DocumentDevice, Stream)"/>.</summary>
    public void SendTo(Devices.DocumentDevice device, string outputFileName)
        => device?.Process(this, outputFileName);

    /// <summary>Render a page range — delegates to the device's
    /// page-range Process overload.</summary>
    public void SendTo(Devices.DocumentDevice device, int fromPage, int toPage, Stream output)
        => device?.Process(this, fromPage, toPage, output);

    /// <summary>File-pair page-range overload.</summary>
    public void SendTo(Devices.DocumentDevice device, int fromPage, int toPage, string outputFileName)
        => device?.Process(this, fromPage, toPage, outputFileName);

    /// <summary>Merge tuning knobs honored by
    /// <see cref="Merge(MergeOptions, Document[])"/> and friends.</summary>
    public sealed class MergeOptions
    {
        /// <summary>Strip /V from every signature field after merge.</summary>
        public bool RemoveSignatures { get; set; }

        /// <summary>Deduplicate identical entries from the merged outline tree.</summary>
        public bool MergeDuplicateOutlines { get; set; }

        /// <summary>Append "_2", "_3", … suffixes to colliding form-field names.</summary>
        public bool KeepFieldsUnique { get; set; }

        /// <summary>Strip /Perms /UR /UR3 usage-rights entries after merge.</summary>
        public bool RemoveUserRights { get; set; }

        /// <summary>Merge duplicate optional-content groups (layers). Stored
        /// only — the FOSS writer does not yet emit a deduplicated /OCProperties
        /// tree.</summary>
        public bool MergeDuplicateLayers { get; set; }

        /// <summary>Streaming buffer size (in bytes) for the source-side
        /// reader during merge. Stored only — the FOSS merge path keeps
        /// the full document in memory and does not split reads into
        /// packets.</summary>
        public int ConcatenationPacketSize { get; set; }

        /// <summary>When true, the merged /Pages tree is balanced into a
        /// fixed-fanout subtree shape. Stored only — the FOSS merge
        /// emits a flat /Pages /Kids list and does not balance the tree.</summary>
        public bool IsNeedPageTreeBalance { get; set; }

        /// <summary>Maximum entries per /Pages subtree node when
        /// <see cref="IsNeedPageTreeBalance"/> is set. Stored only.</summary>
        public byte MaximumNodesInLevel { get; set; }

        /// <summary>Spill intermediate state to a temp file rather than
        /// keeping it in memory. Stored only — the FOSS merge path is
        /// always in-memory.</summary>
        public bool UseDiskBuffer { get; set; }
    }
}
