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
    /// <summary>
    /// Returns the PDF/A compliance level detected from XMP metadata, or null if not a PDF/A document.
    /// </summary>
    public PdfFormat? GetPdfACompliance()
    {
        var part = Metadata.Get("pdfaid:part");
        var conformance = Metadata.Get("pdfaid:conformance")?.ToUpperInvariant();

        if (string.IsNullOrEmpty(part))
            return null;

        return (part, conformance) switch
        {
            ("1", "A") => PdfFormat.PDF_A_1A,
            ("1", "B") => PdfFormat.PDF_A_1B,
            ("2", "A") => PdfFormat.PDF_A_2A,
            ("2", "B") => PdfFormat.PDF_A_2B,
            ("2", "U") => PdfFormat.PDF_A_2U,
            ("3", "A") => PdfFormat.PDF_A_3A,
            ("3", "B") => PdfFormat.PDF_A_3B,
            ("3", "U") => PdfFormat.PDF_A_3U,
            ("4", _) => PdfFormat.PDF_A_4,
            _ => null,
        };
    }

    /// <summary>
    /// Flatten all form fields — renders their visual appearance into page content
    /// and removes the interactive form. Convenience wrapper for Form.Flatten().
    /// </summary>
    public void Flatten()
    {
        // Flatten form fields first (removes AcroForm + widget annotations)
        Form?.Flatten(this);
        _form = null; // Reset cache so Form re-reads from (now removed) AcroForm

        // Flatten all remaining annotations on every page
        foreach (var page in Pages)
        {
            page.Flatten();
        }
    }

    /// <summary>Flatten with explicit settings. When
    /// <see cref="Forms.Form.FlattenSettings.HideButtons"/> is true the
    /// XFA template's button fields are marked presence="hidden" and the
    /// AcroForm/XFA structure is preserved (only the visuals are
    /// suppressed for downstream rendering); otherwise behaves like
    /// <see cref="Flatten()"/>.</summary>
    public void Flatten(Forms.Form.FlattenSettings flattenSettings)
    {
        // An XFA form keeps its template; HideButtons only marks the XFA button
        // fields presence="hidden" (the dynamic form is preserved, not folded
        // into page content) — see HideXfaButtons.
        if (flattenSettings is { HideButtons: true } && Form is { IsXfa: true })
        {
            HideXfaButtons();
            return;
        }
        // Forward the settings to Form.Flatten so UpdateAppearances /
        // ApplyRedactions / HideButtons are honoured. Without this the flag was
        // stored only and a flatten of a programmatically re-valued form rendered
        // stale appearances. For a
        // plain AcroForm, HideButtons drops push-button widgets while flattening
        // the rest into page content.
        Form?.FlattenWithSettings(this, flattenSettings);
        _form = null;
        foreach (var page in Pages) page.Flatten();
    }

    private void HideXfaButtons()
    {
        // Walk the XFA template and mark any <field> whose <ui> contains a
        // <button> as presence="hidden" so the flatten step (and downstream
        // template inspection) treats them as absent.
        if (Form is null || !Form.IsXfa) return;
        var xml = Form.GetXfaTemplateXml();
        if (xml is null) return;
        try
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);
            // Use the template packet's actual xfa-template namespace version (2.6 / 2.8 /
            // 3.0 / …) rather than a hard-coded 2.6, otherwise the button XPath matches
            // nothing on a non-2.6 form and no field is marked hidden.
            var tplNs = doc.DocumentElement?.NamespaceURI;
            if (string.IsNullOrEmpty(tplNs)) return;
            var nsm = new System.Xml.XmlNamespaceManager(doc.NameTable);
            nsm.AddNamespace("tpl", tplNs);
            var buttons = doc.SelectNodes("//tpl:field/tpl:ui/tpl:button", nsm);
            if (buttons is null) return;
            foreach (System.Xml.XmlNode btn in buttons)
            {
                var field = btn.ParentNode?.ParentNode as System.Xml.XmlElement;
                field?.SetAttribute("presence", "hidden");
            }
            Form.SetXfaTemplateXml(doc.OuterXml);
        }
        catch { /* malformed XFA — leave untouched */ }
    }

    /// <summary>
    /// Add a named destination to the document using the /Names → /Dests name tree.
    /// </summary>
    /// <param name="name">The destination name.</param>
    /// <param name="destination">A destination array created by <see cref="NamedDestination"/> factory methods.</param>
    public void AddNamedDestination(string name, DestinationArray destination)
    {
        // Get or create /Names dict in catalog
        var namesDict = _reader.ResolveDict(_reader.Catalog.Get("Names"));
        if (namesDict is null)
        {
            namesDict = new PdfDictionary();
            _reader.Catalog.Set("Names", namesDict);
        }

        // Get or create /Dests name tree
        var destsTree = _reader.ResolveDict(namesDict.Get("Dests"));
        PdfArray namesArray;
        if (destsTree is not null)
        {
            namesArray = _reader.Resolve(destsTree.Get("Names")) as PdfArray ?? new PdfArray();
        }
        else
        {
            destsTree = new PdfDictionary();
            namesDict.Set("Dests", destsTree);
            namesArray = new PdfArray();
        }

        namesArray.Add(new PdfString(Encoding.Latin1.GetBytes(name)));
        namesArray.Add(destination.Array);
        destsTree.Set("Names", namesArray);
    }

    /// <summary>
    /// Import specified pages from another document into this document.
    /// Deep-clones page dictionaries and all referenced resources.
    /// </summary>
    public void ImportPages(Document source, int[] pageNumbers, int insertAt = -1)
    {
        if (pageNumbers.Length == 0) return;

        foreach (var pn in pageNumbers)
        {
            if (pn < 1 || pn > source.PageCount)
                throw new ArgumentOutOfRangeException(nameof(pageNumbers),
                    $"Page number {pn} is out of range (1-{source.PageCount})");
        }

        var cloneMap = new Dictionary<int, int>();

        foreach (var pageNumber in pageNumbers)
        {
            var sourcePage = source.Pages.At(pageNumber);
            var clonedPageDict = DeepCloneDictExcluding(sourcePage.Dict, source._reader, cloneMap, "Parent");
            clonedPageDict.Set("Type", new PdfName("Page"));

            if (insertAt == -1)
                Pages.AddFromDict(clonedPageDict);
            else
            {
                Pages.InsertFromDict(insertAt, clonedPageDict);
                insertAt++;
            }
        }
    }

    /// <summary>
    /// Copy the logical-structure (/StructTreeRoot) elements of <paramref name="source"/>
    /// into this document's structure tree, under a single merged Document element so a
    /// concatenated tagged PDF keeps one structure root. Marked-content / page references
    /// are dropped (the element tree itself is preserved for tagging/reading tools); used
    /// by <c>PdfFileEditor.Concatenate</c> when <c>CopyLogicalStructure</c> is set.
    /// </summary>
    internal void MergeLogicalStructure(Document source)
    {
        if (source is null) return;
        var srcRoot = source._reader.ResolveDict(source._reader.Catalog.Get("StructTreeRoot"));
        if (srcRoot is null) return;
        var srcKids = ResolveStructKids(srcRoot, source._reader);
        if (srcKids.Count == 0) return;

        // Ensure this document has a /StructTreeRoot whose /K holds one Document
        // element that everything merged is appended under.
        var destRoot = _reader.ResolveDict(_reader.Catalog.Get("StructTreeRoot"));
        if (destRoot is null)
        {
            destRoot = new PdfDictionary();
            destRoot.Set("Type", new PdfName("StructTreeRoot"));
            _reader.Catalog.Set("StructTreeRoot", destRoot);
        }
        var rootK = _reader.Resolve(destRoot.Get("K")) as PdfArray;
        if (rootK is null) { rootK = new PdfArray(); destRoot.Set("K", rootK); }

        PdfDictionary mergedDoc;
        if (rootK.Count > 0 && _reader.ResolveDict(rootK[0]) is { } existing
            && existing.GetName("S") == "Document")
        {
            mergedDoc = existing;
        }
        else
        {
            mergedDoc = new PdfDictionary();
            mergedDoc.Set("Type", new PdfName("StructElem"));
            mergedDoc.Set("S", new PdfName("Document"));
            rootK.Insert(0, mergedDoc);
            // Remember that THIS merge created the container: later sources of the
            // same concatenate must not treat it as the destination's own tree.
            _syntheticMergedStructDoc = mergedDoc;
        }
        var mergedK = mergedDoc.Get("K") as PdfArray;
        if (mergedK is null) { mergedK = new PdfArray(); mergedDoc.Set("K", mergedK); }

        // Where the source subtrees land depends on what the merged Document IS.
        // An untagged destination gets a synthetic container and every source keeps
        // its own root beneath it — N tagged sources contribute their full subtrees.
        // A destination that carried its OWN Document element instead absorbs a
        // source whose root is also /S Document by grafting that root's CHILDREN —
        // the reader sees one document tree, not a document nested in a document.
        // Non-Document source roots (a bare Caption, a Part) always append whole.
        var collapseDocRoots = !ReferenceEquals(mergedDoc, _syntheticMergedStructDoc);
        foreach (var kid in srcKids)
        {
            if (collapseDocRoots && kid.GetName("S") == "Document")
            {
                foreach (var grand in ResolveStructKids(kid, source._reader))
                    mergedK.Add(CloneStructElem(grand, source._reader));
            }
            else
                mergedK.Add(CloneStructElem(kid, source._reader));
        }
    }

    /// <summary>Whether <paramref name="p"/> can join an inline-chained line, and
    /// the single-line text it contributes. Only simple runs join: an unpositioned,
    /// footnote-free, Standard-14 TextFragment, or an HtmlFragment whose content
    /// strips to plain single-line text (no tables, images or vector markup) —
    /// that one renders in the serif HTML body face.</summary>
    private static bool InlineJoinable(BaseParagraph p, out string text, out bool serif)
    {
        text = string.Empty;
        serif = false;
        if (p is Text.TextFragment f)
        {
            if (f.HasExplicitPosition || f.FootNote is not null) return false;
            if (f.TextState.FontData is not null || f.TextState.Font?.SourceFontData is not null)
                return false;
            if (f.HyperlinkValue is not null) return false;
            text = f.Text ?? string.Empty;
            return text.Length > 0 && text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0;
        }
        if (p is HtmlFragment h)
        {
            var content = h.HtmlContent ?? string.Empty;
            if (content.IndexOf("<table", StringComparison.OrdinalIgnoreCase) >= 0
                || content.IndexOf("<img", StringComparison.OrdinalIgnoreCase) >= 0
                || content.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0
                || content.IndexOf("<br", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            text = HtmlFragment.StripHtmlTags(content).Trim();
            serif = true;
            return text.Length > 0 && text.IndexOf('\n') < 0;
        }
        return false;
    }

    /// <summary>TJ adjustment array (thousandths of text space; positive pulls the following
    /// glyphs left) for the pair-kerning of <paramref name="s"/> under <paramref name="gp"/>,
    /// or null when no pair kerns.</summary>
    private static double[]? StepKernAdjustments(string s, Text.GlyphOutlineParser gp)
    {
        if (s.Length < 2) return null;
        var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0;
        double[]? adj = null;
        var prev = -1;
        for (var i = 0; i < s.Length; i++)
        {
            var gid = gp.CMap.TryGetValue(s[i], out var g) ? g : 0;
            if (prev >= 0)
            {
                var kern = gp.GetKernAdjustment(prev, gid);
                if (kern != 0)
                {
                    adj ??= new double[s.Length - 1];
                    adj[i - 1] = -kern * 1000.0 / upm;
                }
            }
            prev = gid;
        }
        return adj;
    }
}
