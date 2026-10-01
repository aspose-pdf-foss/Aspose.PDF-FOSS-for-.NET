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
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class SaveDocumentState
{
    // The Producer names the producing library, so every save stamps this
    // library's own string — the usual producer self-identification
    // convention. Only a producer the caller assigned explicitly (through
    // Info.Producer or the metadata mutators) survives; a value merely
    // carried by the loaded file does not. An existing XMP pdf:Producer is
    // restamped in step so the two representations stay equivalent.
    public bool producerExplicit;
    // Linearize ("optimize for fast web view", PDF 32000 Annex F) when the document was
    // explicitly linearized (Optimize()/LinearizeDocument()) or was loaded from a linearized
    // source. The body is serialized to a buffer with a traditional cross-reference table —
    // no object streams — so PdfLinearizer can re-lay-out the object bytes; the linearized
    // result is then written to the real output.
    //
    // OptimizeSize wins over linearization: a linearized file repeats the first-page
    // objects up front, carries a hint stream, and cannot pack objects into compressed
    // object streams — so it is always LARGER than the plain object-stream save. When the
    // caller asked to minimise size, skip linearization and keep the compact form
    // (a font-unembed + OptimizeSize save is 8.4 KB unlinearized vs 16 KB
    // linearized).
    public bool doLinearize;
    public System.IO.Stream writeTarget = null!;
    public IO.PdfWriter writer = null!;
    public IO.XRefTable xref = null!;
    public Aspose.Pdf.Core.PdfDictionary trailer = null!;
    // Enable object streams when the original PDF used them (reduces output size significantly).
    // Objects with inline PdfStream values are excluded from ObjStm packing by the writer.
    // Linearization needs each object at a top-level file offset, so it stays off there.
    public bool hasCompressedObjects;
    // PDF/A-1 (ISO 19005-1 §6.1.4) prohibits cross-reference streams — and object
    // streams require one — so a document converted to PDF/A-1 saves with a
    // classic xref table even when the source used compressed objects.
    public bool pdfA1Target;
    // The source file's cross-reference infrastructure — object streams (/Type /ObjStm)
    // and cross-reference streams (/Type /XRef) — is regenerated from scratch by the
    // writer. Carrying the originals over leaves them as dead, unreferenced streams in the
    // output, and because each save emits a fresh set, re-saving an already-saved file
    // would accumulate one dead ObjStm + one dead XRef per cycle and grow the file
    // monotonically. Skip them on write, and exclude their numbers from the object-number
    // ceiling below so the regenerated containers get the same numbers every cycle —
    // together this makes a load/save round-trip byte-stable.
    public HashSet<int> infraObjNums = null!;
    // Pre-allocate object number for XMP metadata (so catalog gets the reference)
    public int metaObjNum;
    public PdfStream? metaStream;
    public byte[]? xmpBytes;
    // Map each existing page's source object number to its authoritative
    // in-memory dictionary. The page renderer clears the reader's object cache
    // to free decoded streams, so re-resolving a page below would re-parse a
    // pristine dict and silently drop in-memory edits made after rendering
    // (e.g. an hOCR invisible-text overlay added via Convert). Writing the live
    // Page.Dict for these object numbers preserves those edits.
    public Dictionary<int, Aspose.Pdf.Core.PdfDictionary> livePageDicts = null!;
    // Build trailer dict
    public Aspose.Pdf.Core.PdfDictionary newTrailer = null!;
}
}
