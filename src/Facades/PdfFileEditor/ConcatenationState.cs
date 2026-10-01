using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileEditor
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ConcatenationState
{
    public IO.PdfWriter writer = null!;
    // Parse all inputs once, reuse readers for outlines/embedded files/AcroForm.
    // inputPageCounts preserves pages-per-input so MergeOutlines can shift
    // destinations by the sum of preceding page counts.
    public List<Aspose.Pdf.IO.PdfReader> inputReaders = null!;
    public List<int> inputPageCounts = null!;
    // Retain the first input's reader + object map so its catalog-level
    // /OpenAction can be preserved and remapped through
    // the same map — the open-action destination then still points at its
    // (already-written) page rather than a duplicate.
    public PdfReader? firstReader;
    public Dictionary<int, int>? firstObjRemap;
    // Per-input seed maps for MergeOutlines: the input's object map PLUS the
    // source-page-object → output-page-object pairs, so an outline /Dest that
    // references a page remaps onto the page ALREADY written into the tree
    // instead of cloning an orphan copy of it.
    public List<Dictionary<int, int>> inputOutlineSeeds = null!;
    // Per-input offset for the /StructParents key space: parent-tree keys of
    // all inputs share one numbering in the output, so each input's keys are
    // shifted by the key span of the inputs before it (the same shape as the
    // page-count shift MergeOutlines applies to destinations).
    public List<int> structParentBases = null!;
    public int structParentNext;
    // Write Pages object
    public Aspose.Pdf.Core.PdfArray kids = null!;
    public Aspose.Pdf.Core.PdfDictionary pagesObj = null!;
    // Merge embedded files from all input documents
    public List<(string name, Aspose.Pdf.Core.PdfObject fileSpec)> embeddedEntries = null!;
    // Write catalog
    public Aspose.Pdf.Core.PdfDictionary catalogDict = null!;
    // Merge AcroForm fields from all input documents. Two top-level fields
    // that share a fully-qualified name denote the same field: with
    // KeepFieldsUnique the later one is renamed via UniqueSuffix (%NUM% →
    // incrementing counter); otherwise their widgets are merged under one field.
    public PdfDictionary? acroFormDict;
    // When two or more inputs carry an XFA template, their AcroForm fields form a
    // hierarchical tree (top subform node → page-subform nodes → leaf widgets).
    // Re-parent each input's top-level field nodes under one synthetic "root" field,
    // applying the SAME name disambiguation as the /XFA template merge
    // (BuildMergedXfaArray) so FindByName("root[0].eApp[0]") resolves and the node
    // /Kids counts match. The flat per-widget merge (else branch) would both drop the
    // subtree (MergeFieldWidgets keeps only widget keys) and lose the /Parent chain
    // (RemapObject strips /Parent → bare leaf FullNames), so it is used only for
    // non-XFA / single-XFA concatenations.
    public List<Dictionary<string, string>>? xfaRenames;
    // Merge XFA packets (dynamic/static XFA forms). When two or more inputs
    // carry an XFA template, re-parent each input's top-level subform(s) under
    // a single synthetic "root" subform (datasets in parallel), disambiguating
    // colliding names. A pure dynamic XFA form has no AcroForm widget fields, so
    // the AcroForm dict may hold only /XFA (no /Fields).
    public Aspose.Pdf.Core.PdfArray? xfaArr;
    public Aspose.Pdf.Core.PdfDictionary trailer = null!;
    // A fresh file gets a fresh /ID pair (PDF/UA requires one; both halves
    // equal, the same shape the PDF/A converter writes).
    public byte[] fileId = null!;
    public Aspose.Pdf.Core.PdfArray idArr = null!;
}
}
