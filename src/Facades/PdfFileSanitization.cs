using System.IO;
using System.Text;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Repairs damaged PDF files at the byte level so they open again: trims waste
/// bytes before the <c>%PDF-</c> header and after the final <c>%%EOF</c>, and
/// rebuilds a broken cross-reference table / trailer by re-scanning the file
/// for its indirect objects. Unlike the document facades this class never
/// parses the file into a <see cref="Document"/> — the whole point is that the
/// input may be too damaged to load.
/// </summary>
public sealed class PdfFileSanitization : IDisposable
{
    private static readonly byte[] HeaderMarker = Encoding.ASCII.GetBytes("%PDF-");
    private static readonly byte[] EofMarker = Encoding.ASCII.GetBytes("%%EOF");

    private byte[]? _bytes;
    private Stream? _boundStream;

    /// <summary>Log of the recovery actions performed, one line per action.</summary>
    public List<string> Log { get; } = new();

    /// <summary>Whether <see cref="Recover"/> trims waste bytes before the
    /// <c>%PDF-</c> header. Default true.</summary>
    public bool UseTrimTop { get; set; } = true;

    /// <summary>Whether <see cref="Recover"/> trims waste bytes after the last
    /// <c>%%EOF</c>. Default true.</summary>
    public bool UseTrimBottom { get; set; } = true;

    /// <summary>Whether <see cref="Recover"/> also rebuilds the cross-reference
    /// table and trailer. Default false — xref repair is requested explicitly
    /// via <see cref="RebuildXrefAndTrailer"/>.</summary>
    public bool UseRebuildXrefAndTrailer { get; set; }

    /// <summary>Bind the PDF file to sanitize.</summary>
    public void BindPdf(string inputFile)
    {
        if (inputFile is null) throw new ArgumentNullException(nameof(inputFile));
        _bytes = File.ReadAllBytes(inputFile);
    }

    /// <summary>Bind the PDF content to sanitize. The stream is read to its end
    /// and kept open until <see cref="Close"/> / <see cref="Dispose"/>.</summary>
    public void BindPdf(Stream inputStream)
    {
        if (inputStream is null) throw new ArgumentNullException(nameof(inputStream));
        _boundStream = inputStream;
        using var ms = new MemoryStream();
        if (inputStream.CanSeek) inputStream.Position = 0;
        inputStream.CopyTo(ms);
        _bytes = ms.ToArray();
    }

    /// <summary>Bind an already-loaded document: its current serialized form is
    /// captured as the bytes to sanitize.</summary>
    public void BindPdf(Document srcDoc)
    {
        if (srcDoc is null) throw new ArgumentNullException(nameof(srcDoc));
        using var ms = new MemoryStream();
        srcDoc.Save(ms);
        _bytes = ms.ToArray();
    }

    /// <summary>Run the recovery steps selected by <see cref="UseTrimTop"/>,
    /// <see cref="UseTrimBottom"/> and <see cref="UseRebuildXrefAndTrailer"/>.</summary>
    public void Recover()
    {
        if (UseTrimTop) TrimTop();
        if (UseTrimBottom) TrimBottom();
        if (UseRebuildXrefAndTrailer) RebuildXrefAndTrailer();
    }

    /// <summary>Remove every byte before the first <c>%PDF-</c> header.</summary>
    public void TrimTop()
    {
        var data = BoundBytes();
        var start = IndexOf(data, HeaderMarker, 0);
        if (start <= 0) return;
        _bytes = data[start..];
        Log.Add($"TrimTop: removed {start} byte(s) before the %PDF header.");
    }

    /// <summary>Remove every byte after the last <c>%%EOF</c> (one end-of-line
    /// sequence after the marker is kept).</summary>
    public void TrimBottom()
    {
        var data = BoundBytes();
        var eof = LastIndexOf(data, EofMarker);
        if (eof < 0) return;
        var end = eof + EofMarker.Length;
        if (end < data.Length && data[end] == (byte)'\r') end++;
        if (end < data.Length && data[end] == (byte)'\n') end++;
        if (end >= data.Length) return;
        Log.Add($"TrimBottom: removed {data.Length - end} byte(s) after the final %%EOF.");
        _bytes = data[..end];
    }

    /// <summary>
    /// Rebuild the cross-reference table and trailer: the file is scanned for
    /// its <c>N G obj</c> headers (skipping stream payloads, so binary data
    /// can't fake an object start) and a fresh classic xref + trailer is
    /// appended after the existing content. The appended table supersedes the
    /// damaged one — for an incrementally updated file the scan keeps the last
    /// occurrence of each object number, matching normal update semantics.
    /// </summary>
    public void RebuildXrefAndTrailer()
    {
        var data = BoundBytes();
        var offsets = ScanObjectOffsets(data);
        if (offsets.Count == 0) return;

        var rootRef = FindRootReference(data, offsets);
        var infoRef = FindTrailerReference(data, "Info");
        var idEntry = FindTrailerId(data);

        var maxNum = 0;
        foreach (var num in offsets.Keys)
            if (num > maxNum) maxNum = num;

        var sb = new StringBuilder();
        sb.Append("\r\nxref\r\n");
        // Emit contiguous subsections; entry 0 is the free-list head.
        var num2 = 0;
        while (num2 <= maxNum)
        {
            if (num2 != 0 && !offsets.ContainsKey(num2)) { num2++; continue; }
            var subStart = num2;
            var subCount = 0;
            while (num2 <= maxNum && (num2 == 0 || offsets.ContainsKey(num2))) { num2++; subCount++; }
            sb.Append(subStart).Append(' ').Append(subCount).Append("\r\n");
            for (var n = subStart; n < subStart + subCount; n++)
            {
                if (n == 0) sb.Append("0000000000 65535 f\r\n");
                else sb.Append(offsets[n].offset.ToString("D10")).Append(' ')
                       .Append(offsets[n].gen.ToString("D5")).Append(" n\r\n");
            }
        }

        sb.Append("trailer\r\n<</Size ").Append(maxNum + 1);
        if (rootRef is not null) sb.Append("/Root ").Append(rootRef);
        if (infoRef is not null) sb.Append("/Info ").Append(infoRef);
        if (idEntry is not null) sb.Append("/ID ").Append(idEntry);
        sb.Append(">>\r\nstartxref\r\n").Append(data.Length + 2 /* the leading \r\n */)
          .Append("\r\n%%EOF\r\n");

        var tail = Encoding.ASCII.GetBytes(sb.ToString());
        var result = new byte[data.Length + tail.Length];
        data.CopyTo(result, 0);
        tail.CopyTo(result, data.Length);
        _bytes = result;
        Log.Add($"RebuildXrefAndTrailer: rebuilt xref for {offsets.Count} object(s), Size {maxNum + 1}.");
    }

    /// <summary>Write the sanitized file.</summary>
    public void Save(string outputFile)
    {
        if (outputFile is null) throw new ArgumentNullException(nameof(outputFile));
        File.WriteAllBytes(outputFile, BoundBytes());
    }

    /// <summary>Write the sanitized content to a stream.</summary>
    public void Save(Stream outputStream)
    {
        if (outputStream is null) throw new ArgumentNullException(nameof(outputStream));
        var data = BoundBytes();
        outputStream.Write(data, 0, data.Length);
        outputStream.Flush();
    }

    /// <summary>Release the bound input.</summary>
    public void Close()
    {
        _boundStream?.Dispose();
        _boundStream = null;
        _bytes = null;
    }

    /// <summary>Release the bound input.</summary>
    public void Dispose() => Close();

    private byte[] BoundBytes()
        => _bytes ?? throw new InvalidOperationException("No PDF is bound. Call BindPdf first.");

    // ── File scanning ────────────────────────────────────────────────────

    /// <summary>Scan for top-level <c>N G obj</c> headers, skipping stream
    /// payloads. Later duplicates of an object number override earlier ones
    /// (incremental-update semantics).</summary>
    private static Dictionary<int, (long offset, int gen)> ScanObjectOffsets(byte[] data)
    {
        var offsets = new Dictionary<int, (long, int)>();
        var pos = 0;
        while (pos < data.Length)
        {
            var objKw = IndexOfToken(data, "obj", pos);
            if (objKw < 0) break;

            // Walk back over "N G" before the keyword.
            var (header, num, gen) = TryReadObjectHeader(data, objKw);
            var bodyStart = objKw + 3;
            if (header < 0) { pos = bodyStart; continue; }

            offsets[num] = (header, gen);

            // Advance past the object body: jump over any stream payload by its
            // literal "endstream" (broken files can't be trusted for /Length),
            // then to "endobj". If neither is found, resume right after "obj".
            var streamKw = IndexOfToken(data, "stream", bodyStart);
            var endobjKw = IndexOfToken(data, "endobj", bodyStart);
            if (streamKw >= 0 && (endobjKw < 0 || streamKw < endobjKw))
            {
                var endstream = IndexOfToken(data, "endstream", streamKw + 6);
                if (endstream >= 0) endobjKw = IndexOfToken(data, "endobj", endstream + 9);
            }
            pos = endobjKw >= 0 ? endobjKw + 6 : bodyStart;
        }
        return offsets;
    }

    /// <summary>Read the "N G" integers that precede an <c>obj</c> keyword.
    /// Returns (headerOffset, number, generation), headerOffset −1 when the
    /// preceding tokens are not two integers.</summary>
    private static (long header, int num, int gen) TryReadObjectHeader(byte[] data, int objKw)
    {
        var i = objKw - 1;
        while (i >= 0 && IsPdfWhitespace(data[i])) i--;
        var genEnd = i;
        while (i >= 0 && data[i] >= (byte)'0' && data[i] <= (byte)'9') i--;
        var genStart = i + 1;
        if (genStart > genEnd) return (-1, 0, 0);
        while (i >= 0 && IsPdfWhitespace(data[i])) i--;
        var numEnd = i;
        while (i >= 0 && data[i] >= (byte)'0' && data[i] <= (byte)'9') i--;
        var numStart = i + 1;
        if (numStart > numEnd) return (-1, 0, 0);
        // The number must start the line / follow a delimiter, not be the tail
        // of a longer token (e.g. a name like /F1 0 obj inside a dict is fine,
        // but "12 0 obj" glued to text is not an object header).
        if (numStart > 0 && !IsPdfWhitespace(data[numStart - 1]) && !IsPdfDelimiter(data[numStart - 1]))
            return (-1, 0, 0);

        var num = ParseInt(data, numStart, numEnd);
        var gen = ParseInt(data, genStart, genEnd);
        if (num <= 0 || gen < 0 || gen > 65535) return (-1, 0, 0);
        return (numStart, num, gen);
    }

    /// <summary>Find <c>/Root N G R</c> in the last trailer that has one; fall
    /// back to the object whose body declares <c>/Type /Catalog</c>.</summary>
    private static string? FindRootReference(byte[] data, Dictionary<int, (long offset, int gen)> offsets)
    {
        var fromTrailer = FindTrailerReference(data, "Root");
        if (fromTrailer is not null) return fromTrailer;

        foreach (var kv in offsets)
        {
            var start = (int)kv.Value.offset;
            var end = Math.Min(data.Length, start + 2048);
            var slice = Encoding.ASCII.GetString(data, start, end - start);
            if (System.Text.RegularExpressions.Regex.IsMatch(slice, @"/Type\s*/Catalog\b"))
                return $"{kv.Key} {kv.Value.gen} R";
        }
        return null;
    }

    /// <summary>The last <c>/«key» N G R</c> occurrence in the file (trailer
    /// dictionaries are the expected carriers), as "N G R" text.</summary>
    private static string? FindTrailerReference(byte[] data, string key)
    {
        var text = Encoding.ASCII.GetString(data);
        var matches = System.Text.RegularExpressions.Regex.Matches(text, $@"/{key}\s+(\d+)\s+(\d+)\s+R\b");
        if (matches.Count == 0) return null;
        var m = matches[^1];
        return $"{m.Groups[1].Value} {m.Groups[2].Value} R";
    }

    /// <summary>The last <c>/ID [«…»«…»]</c> entry in the file, verbatim.</summary>
    private static string? FindTrailerId(byte[] data)
    {
        var text = Encoding.ASCII.GetString(data);
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"/ID\s*(\[\s*<[0-9A-Fa-f]*>\s*<[0-9A-Fa-f]*>\s*\])");
        return matches.Count == 0 ? null : matches[^1].Groups[1].Value;
    }

    /// <summary>Index of a keyword bounded by PDF whitespace/delimiters on both sides.</summary>
    private static int IndexOfToken(byte[] data, string keyword, int from)
    {
        var kw = Encoding.ASCII.GetBytes(keyword);
        var pos = Math.Max(0, from);
        while (true)
        {
            var i = IndexOf(data, kw, pos);
            if (i < 0) return -1;
            var beforeOk = i == 0 || IsPdfWhitespace(data[i - 1]) || IsPdfDelimiter(data[i - 1]);
            var afterIdx = i + kw.Length;
            var afterOk = afterIdx >= data.Length || IsPdfWhitespace(data[afterIdx]) || IsPdfDelimiter(data[afterIdx]);
            if (beforeOk && afterOk) return i;
            pos = i + 1;
        }
    }

    private static int ParseInt(byte[] data, int start, int end)
    {
        var v = 0L;
        for (var i = start; i <= end; i++)
        {
            v = v * 10 + (data[i] - (byte)'0');
            if (v > int.MaxValue) return -1;
        }
        return (int)v;
    }

    private static bool IsPdfWhitespace(byte b) => b is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;

    private static bool IsPdfDelimiter(byte b) => b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>'
        or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        for (var i = Math.Max(0, from); i <= haystack.Length - needle.Length; i++)
        {
            var hit = true;
            for (var j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { hit = false; break; }
            if (hit) return i;
        }
        return -1;
    }

    private static int LastIndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = haystack.Length - needle.Length; i >= 0; i--)
        {
            var hit = true;
            for (var j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { hit = false; break; }
            if (hit) return i;
        }
        return -1;
    }
}
