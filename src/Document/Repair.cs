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
    /// Whether a license is currently applied. Always returns <c>true</c> —
    /// no evaluation-watermark restrictions are imposed by this library.
    /// </summary>
    public static bool IsLicensed => true;

    /// <summary>
    /// Check whether the document needs repair.
    /// </summary>
    /// <param name="options">When repair is needed, receives the repair options describing the issues.</param>
    /// <returns>True if the document has structural issues that can be repaired.</returns>
    public bool IsRepairNeeded(out RepairOptions options)
    {
        options = new RepairOptions();

        // Structural ERRORS only: advisory warnings (a missing /Info title, say)
        // are not damage and no re-serialisation removes them - counting them made
        // IsRepairNeeded stick true forever on perfectly healthy files.
        try
        {
            var errors = 0;
            foreach (var issue in Validate().Where(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)))
                errors++;
            if (errors > 0)
            {
                options.HasValidationIssues = true;
                options.IssueCount = errors;
                return true;
            }
        }
        catch
        {
            options.HasValidationIssues = true;
            return true;
        }

        // Xref damage the loader papered over: a table only recovered by scanning
        // the file, or an in-use uncompressed entry whose declared offset is 0
        // (a broken writer's tombstone - object 0 is the only legitimate zero) or
        // does not lead to that object's header (the reader looks for it elsewhere).
        try
        {
            if (_reader.XRefTable.RecoveredByScan)
            {
                options.HasXRefIssues = true;
                return true;
            }
            foreach (var kv in _reader.XRefTable.Entries)
            {
                var entry = kv.Value;
                if (kv.Key == 0 || !entry.InUse || entry.IsCompressed) continue;
                if (entry.Offset == 0 || !IsObjectHeaderAt(entry.Offset, kv.Key))
                {
                    options.HasXRefIssues = true;
                    return true;
                }
            }
        }
        catch
        {
            options.HasXRefIssues = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Repair the document by re-serializing it.
    /// Rebuilds xref table, fixes object numbering, and normalizes structure.
    /// After repair, Save() will produce a clean PDF.
    /// </summary>
    public void Repair()
    {
        // Re-serialization through Save() inherently repairs the document:
        // - Rebuilds xref table from scratch
        // - Re-numbers all objects sequentially
        // - Normalizes the page tree structure
        // - Drops orphaned/corrupt objects
        _linearize = false; // ensure full rewrite
        RepairPageTree();

        // Fix oversized annotation rects (clamp to short.MaxValue per PDF spec recommendation)
        const double maxCoord = short.MaxValue; // 32767
        foreach (var page in Pages)
        {
            foreach (var annot in page.Annotations)
            {
                var rect = annot.Rect;
                if (rect is null) continue;
                bool needsFix = Math.Abs(rect.LLX) > maxCoord || Math.Abs(rect.LLY) > maxCoord ||
                                Math.Abs(rect.URX) > maxCoord || Math.Abs(rect.URY) > maxCoord;
                if (needsFix)
                {
                    var mb = page.MediaBox;
                    annot.Rect = new Rectangle(
                        Math.Max(rect.LLX, mb.LLX),
                        Math.Max(rect.LLY, mb.LLY),
                        Math.Min(rect.URX, mb.URX),
                        Math.Min(rect.URY, mb.URY));
                }
            }
        }
    }

    /// <summary>Whether "objectNumber G obj" starts at the offset of the bytes the xref was read from,
    /// after any white-space.</summary>
    private bool IsObjectHeaderAt(long offset, int objectNumber)
    {
        var data = _reader.RawData;
        var pos = offset;
        SkipWhitespace(data, ref pos);
        if (!ReadNumber(data, ref pos, out var number) || number != objectNumber) return false;
        if (!SkipWhitespace(data, ref pos) || !ReadNumber(data, ref pos, out _) || !SkipWhitespace(data, ref pos)) return false;
        return pos + 3 <= data.Length && data[pos] == 'o' && data[pos + 1] == 'b' && data[pos + 2] == 'j';
    }

    private static bool ReadNumber(byte[] data, ref long pos, out long value)
    {
        value = 0;
        var start = pos;
        while (pos < data.Length && data[pos] >= '0' && data[pos] <= '9' && pos - start < MaxObjectNumberDigits)
            value = value * 10 + (data[pos++] - '0');
        return pos > start;
    }

    private static bool SkipWhitespace(byte[] data, ref long pos)
    {
        var start = pos;
        while (pos < data.Length && PdfLexer.IsWhitespace(data[pos])) pos++;
        return pos > start;
    }

    // int.MaxValue has ten digits; a longer run is no object number.
    private const int MaxObjectNumberDigits = 10;

    /// <summary>The page tree a save writes holds real pages only, under a root that is no node's kid: a kid
    /// the file holds nowhere is dropped (a save would write it as a reference to nothing under a /Count
    /// counting it), and a /Parent on the root is removed (it leads readers into whatever tree it names,
    /// often a dead one whose /Parent chain loops). Objects only those references kept are dropped too.</summary>
    private void RepairPageTree()
    {
        var dropped = Pages.DropUnresolvedPages();
        var root = _reader.ResolveDict(_reader.Catalog.Get("Pages"));
        if (root is not null && root.ContainsKey("Parent"))
        {
            root.Remove("Parent");
            dropped = true;
        }
        if (dropped) _reader.MayHaveOrphansOnSave = true;
    }

    /// <summary>
    /// Whether the document is encrypted.
    /// </summary>
    public bool IsEncrypted => _reader.Trailer.ContainsKey("Encrypt") || _encryptor is not null;

    /// <summary>
    /// Whether the document has been successfully decrypted (i.e., the decryptor was initialised).
    /// False for encrypted documents that were opened without (or with incorrect) password.
    /// </summary>
    public bool IsDecrypted => _reader.IsDecrypted;

    private bool RemoveFromNameTree(PdfDictionary node, string name)
    {
        var namesArr = _reader.Resolve(node.Get("Names")) as PdfArray;
        if (namesArr is not null)
        {
            for (var i = 0; i + 1 < namesArr.Count; i += 2)
            {
                var nameObj = namesArr[i];
                var entryName = nameObj is PdfString s ? s.ToText() : nameObj.ToString() ?? "";
                if (entryName == name)
                {
                    // Rebuild array without the pair at i, i+1
                    var newArr = new PdfArray();
                    for (var j = 0; j < namesArr.Count; j++)
                    {
                        if (j == i || j == i + 1) continue;
                        newArr.Add(namesArr[j]);
                    }
                    node.Set("Names", newArr);
                    return true;
                }
            }
        }

        // Recurse into /Kids
        var kids = _reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is not null)
        {
            foreach (var kid in kids)
            {
                var kidDict = _reader.ResolveDict(kid);
                if (kidDict is not null && RemoveFromNameTree(kidDict, name))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if this PDF uses incremental updates (has multiple %%EOF markers).
    /// </summary>
    public bool HasIncrementalUpdate()
    {
        // Count occurrences of %%EOF in the raw data
        int eofCount = 0;
        var marker = "%%EOF"u8;
        int i = 0;
        while (i <= _data.Length - marker.Length)
        {
            bool match = true;
            for (int j = 0; j < marker.Length; j++)
            {
                if (_data[i + j] != marker[j]) { match = false; break; }
            }
            if (match)
            {
                eofCount++;
                i += marker.Length - 1;
            }
            i++;
        }

        if (eofCount < 2) return false;

        // Hybrid-reference PDFs have 2 %%EOF markers (one for the traditional xref table,
        // one for the xref stream) but are NOT incrementally updated.
        bool isHybrid = _reader.Trailer.GetInt("XRefStm", -1) >= 0;

        // A linearized ("fast web view") PDF likewise has 2 %%EOF markers — the first-page
        // cross-reference section and the main one, linked by a /Prev — yet it is a single
        // generation file produced by Optimize(), not an incremental update. Its
        // linearization parameter dictionary (/Linearized) is the first body object, so
        // detect it near the start of the raw data (which is what the %%EOF count reflects).
        bool isLinearized = ContainsMarker(_data, "/Linearized"u8,
            System.Math.Min(_data.Length, 2048));

        int threshold = (isHybrid || isLinearized) ? 2 : 1;
        return eofCount > threshold;
    }
}
