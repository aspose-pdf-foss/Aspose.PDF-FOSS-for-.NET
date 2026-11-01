using System.Collections.Generic;
using System.IO;

namespace Aspose.Pdf.IO;

internal static partial class PdfLinearizer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class LinearizeState
{
    public IO.PdfReader reader = null!;
    public byte[] src = default!;
    // Every object of the plainly-saved file by its number there, except the linearization
    // dictionary and hint stream of an earlier linearized save - a fresh pair is written each
    // time, and copying the old ones forward would accumulate one hint stream per save cycle.
    public Dictionary<int, RawObject> objs = null!;
    public int maxNum;
    public int rootNum;
    public int infoNum;
    // The page tree: the pages in document order and the intermediate nodes.
    public List<int> pages = null!;
    public HashSet<int> pageSet = null!;
    public List<int> treeNodes = null!;

    // Which parts of the document use each object (indexed by its number in the saved file),
    // the way Annex F sorts objects into the first page, other pages and the rest.
    public int[] visitStamp = null!;
    public int traversal;
    public byte[] useFlags = null!;
    public int[] otherPageUses = null!;
    public int[] firstOtherPage = null!;
    public int[] thumbUses = null!;
    public int[] otherUses = null!;
    // The objects each page other than the first uses, in the order the page reaches them.
    public List<int>[] pageObjects = null!;

    // The sections of the linearized file (Annex F parts 4, 6, 7, 8 and 9).
    public List<RawObject> part4 = null!;
    public List<RawObject> part6 = null!;
    public List<RawObject> part7 = null!;
    public List<RawObject> part8 = null!;
    public List<RawObject> part9 = null!;
    // The objects each page's hint entry counts: its own page object first, then the objects
    // only it uses, numbered consecutively; the first page's entry covers all of part 6.
    public int[] pageObjectCounts = null!;
    // The outline hint table: the outline dictionary heads a consecutive run of this many objects.
    public RawObject? outlinesRoot;
    public int outlineObjectCount;

    // Whether the save packs objects into object streams, and the objects it packs.
    public bool packObjects;
    public List<RawObject> packed = new();
    // The packed objects of the first page's section, listed by the first-page cross-reference.
    public List<RawObject> packedFirst = new();
    public int firstXrefNum;
    public int mainXrefNum;
    public int linObjNum;
    public int hintObjNum;
    public int size;
    public int secondHalfCount;

    // Layout. Offsets in the hint tables leave the hint stream out (Annex F.4), so part 6 and
    // everything after it also has a hint-free offset: its file offset less the hint object.
    public RawObject[] byNewNum = null!;
    public long linDictPos;
    public long hintPos;
    public byte[] hintObject = null!;
    public long firstSectionEnd;
    public long mainXrefPos;
    public long firstXrefPos;
    public string? idStr;

    public MemoryStream ms = null!;
    /// <summary>Writes an ASCII token to the output stream.</summary>
    public void W(string s) { var b = System.Text.Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
}
}
