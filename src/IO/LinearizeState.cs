using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO;

internal static partial class PdfLinearizer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class LinearizeState
{
    public IO.PdfReader reader = null!;
    // Drop the linearization infrastructure carried over from a previously-linearized save —
    // the stale linearization parameter dictionary (its /Linearized key) and the primary hint
    // stream this linearizer emitted (its private /AsposeHint marker, see below). A fresh pair
    // is written each time; copying the old ones forward would accumulate one hint stream per
    // save cycle (a size regression that breaks re-save idempotency).
    public HashSet<int> skip = null!;
    public Dictionary<int, Aspose.Pdf.IO.PdfLinearizer.RawObject> objs = null!;
    public Aspose.Pdf.Core.PdfDictionary catalog = null!;
    public Aspose.Pdf.Core.PdfDictionary? pagesRoot;
    public int pageCount;
    public int firstPageNum;
    public HashSet<int> firstPageSet = null!;
    public int maxNum;
    public int linObjNum;
    public int hintObjNum;
    public int size;
    public List<Aspose.Pdf.IO.PdfLinearizer.RawObject> firstSection = null!;
    public List<Aspose.Pdf.IO.PdfLinearizer.RawObject> restSection = null!;
    // The two tables PARTITION the file (PDF 32000-1 Annex F): the first-page table
    // carries the linearization dictionary, the hint stream and the first-page objects;
    // the main table carries everything else and opens on object 0's free entry. Listing
    // every object in BOTH still resolves - a reader finds the first-page entries before
    // it walks /Prev - but it writes each first-page object twice and puts the main
    // table's leading subsection at odds with the section it actually describes.
    public List<int> firstXrefNums = null!;
    public List<int> allNums = null!;
    public System.IO.MemoryStream ms = null!;
    public Dictionary<int, long> offsets = null!;
    public Dictionary<(int section, int num), long> xrefEntryPos = null!;
    public Dictionary<string, long> lin = null!;
    public long prevPos;
    // 3. First-page cross-reference section + trailer (/Prev patched later).
    public long firstXrefPos;
    public string? idStr;
    public long hintPos;
    public byte[] hintBytes = null!;
    public long hintStart;
    public long firstSectionEnd;
    // 7. Main cross-reference section + trailer. The main trailer carries /Size (and /ID,
    // /Encrypt where they apply) and nothing else: /Root and /Info belong to the
    // first-page trailer, which is the one the file-final startxref points at and the one
    // a reader reads first (PDF 32000-1 Annex F.3.7). Repeating them here is 26 bytes of
    // the only part of the file a reader is guaranteed to fetch.
    public long mainXrefPos;
    public byte[] buf = null!;
    public byte[] src = default!;
    /// <summary>Writes an ASCII token to the output stream; also handed to the
    /// section writers as their line sink, so it stays a method of the state.</summary>
    public void W(string s) { var b = System.Text.Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
    public long hintEnd;
}
}
