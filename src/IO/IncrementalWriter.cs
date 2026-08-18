using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO;

/// <summary>
/// Appends modifications to a PDF document without rewriting the original bytes.
/// Spec: PDF32000_2008 §7.5.6 (Incremental Updates)
/// </summary>
internal sealed class IncrementalWriter
{
    private readonly Stream _output;
    private readonly byte[] _originalData;
    private readonly Dictionary<int, (long offset, PdfObject obj)> _modifiedObjects = new();
    private int _nextObjectNumber;

    /// <summary>Streams encountered as direct values during write are
    /// promoted to indirect objects (PDF spec requires it). This map keeps
    /// each PdfStream's assigned object number so multiple references to
    /// the same instance share one indirect object.</summary>
    private readonly Dictionary<PdfStream, int> _promotedStreams = new();

    /// <summary>Streams whose object body still needs to be written —
    /// drained at the end of <see cref="Flush"/>.</summary>
    private readonly Queue<(int objNum, PdfStream stream)> _deferredStreams = new();

    public IncrementalWriter(Stream output, byte[] originalData, int startObjectNumber)
    {
        _output = output;
        _originalData = originalData;
        _nextObjectNumber = startObjectNumber;
    }

    /// <summary>
    /// Register a modified or new object to be written.
    /// </summary>
    public void WriteObject(int objectNumber, PdfObject obj)
    {
        _modifiedObjects[objectNumber] = (0, obj); // offset computed during flush
    }

    /// <summary>
    /// Allocate a new object number.
    /// </summary>
    public int AllocateObjectNumber() => _nextObjectNumber++;

    /// <summary>
    /// Flush the incremental update: copy original bytes, append modified objects,
    /// write new xref + trailer.
    /// </summary>
    public void Flush(PdfDictionary originalTrailer, long originalStartXref)
    {
        // 1. Copy the entire original PDF
        _output.Write(_originalData);

        // 1b. Producer comment
        WriteLn("%   ");

        // 2. Write modified/new objects, track offsets
        var offsets = new Dictionary<int, long>();
        foreach (var (objNum, (_, obj)) in _modifiedObjects)
        {
            offsets[objNum] = _output.Position;
            WriteIndirectObject(objNum, 0, obj);
        }

        // 2b. Drain promoted-to-indirect streams (assigned during step 2 and
        //     possibly during this loop itself if a stream is reachable from
        //     another stream's dict).
        while (_deferredStreams.Count > 0)
        {
            var (objNum, stream) = _deferredStreams.Dequeue();
            offsets[objNum] = _output.Position;
            WriteIndirectObject(objNum, 0, stream);
        }

        // 3. Write new xref table (only modified entries)
        var xrefOffset = _output.Position;
        WriteLn("xref");

        // Group consecutive object numbers into subsections
        var sortedNums = offsets.Keys.OrderBy(k => k).ToList();
        var subsections = GroupConsecutive(sortedNums);

        foreach (var (start, count) in subsections)
        {
            WriteLn($"{start} {count}");
            for (var i = start; i < start + count; i++)
            {
                WriteLn($"{offsets[i]:D10} 00000 n ");
            }
        }

        // 4. Write new trailer
        var newTrailer = new PdfDictionary();
        // Copy /Root, /Info, /Encrypt, /ID from original
        foreach (var key in new[] { "Root", "Info", "Encrypt", "ID" })
        {
            var val = originalTrailer.Get(key);
            if (val is not null) newTrailer.Set(key, val);
        }

        // Set /Size to total objects, /Prev to original xref offset
        newTrailer.Set("Size", new PdfInteger(_nextObjectNumber));
        newTrailer.Set("Prev", new PdfInteger(originalStartXref));

        WriteLn("trailer");
        WriteObject(newTrailer);
        Write("\n");
        WriteLn("startxref");
        WriteLn(xrefOffset.ToString(CultureInfo.InvariantCulture));
        WriteLn("%%EOF");
    }

    private void WriteIndirectObject(int objNum, int gen, PdfObject obj)
    {
        WriteLn($"{objNum} {gen} obj");
        WriteObject(obj);
        Write("\n");
        WriteLn("endobj");
    }

    /// <summary>Containers currently being written — detects direct-object cycles
    /// that would otherwise recurse to a StackOverflow and kill the process.</summary>
    private readonly HashSet<PdfObject> _inFlight = new(ReferenceEqualityComparer.Instance);

    private void WriteObject(PdfObject obj)
    {
        switch (obj)
        {
            case PdfNull:
                Write("null");
                break;
            case PdfBoolean b:
                Write(b.Value ? "true" : "false");
                break;
            case PdfInteger i:
                Write(i.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case PdfReal r:
                // Plain decimal only — PDF forbids exponential reals like
                // "6.10352E-05" (PdfReal.ToString handles the expansion).
                Write(r.ToString());
                break;
            case PdfString s:
                if (s.IsHex)
                {
                    Write($"<{Convert.ToHexString(s.Value)}>");
                }
                else
                {
                    Write("(");
                    foreach (var c in s.Value)
                    {
                        if (c is (byte)'(' or (byte)')' or (byte)'\\')
                            _output.WriteByte((byte)'\\');
                        _output.WriteByte(c);
                    }
                    Write(")");
                }
                break;
            case PdfName n:
                Write($"/{n.Value}");
                break;
            case PdfArray arr:
                // A DIRECT container cycle (self-referential graph without indirect refs)
                // must not recurse forever: emit null for the back-edge.
                if (!_inFlight.Add(arr)) { Write("null"); break; }
                try
                {
                    Write("[");
                    for (var i = 0; i < arr.Count; i++)
                    {
                        if (i > 0) Write(" ");
                        if (arr[i] is PdfStream s) WriteObject(PromoteStream(s));
                        else WriteObject(arr[i]);
                    }
                    Write("]");
                }
                finally { _inFlight.Remove(arr); }
                break;
            case PdfDictionary dict:
                if (!_inFlight.Add(dict)) { Write("null"); break; }
                try
                {
                    Write("<< ");
                    foreach (var key in dict.Keys)
                    {
                        Write($"/{key} ");
                        var v = dict.Get(key)!;
                        if (v is PdfStream embedded) WriteObject(PromoteStream(embedded));
                        else WriteObject(v);
                        Write(" ");
                    }
                    Write(">>");
                }
                finally { _inFlight.Remove(dict); }
                break;
            case PdfStream stream:
                stream.Dict.Set("Length", new PdfInteger(stream.RawData.Length));
                WriteObject(stream.Dict);
                Write("\nstream\n");
                _output.Write(stream.RawData);
                Write("\nendstream");
                break;
            case PdfIndirectRef iref:
                Write($"{iref.ObjectNumber} {iref.Generation} R");
                break;
        }
    }

    /// <summary>Assign (or look up) an indirect object number for a stream
    /// encountered as a direct value, and queue its body for later writing.
    /// Returns the indirect-ref token to splice into the calling context.</summary>
    private PdfIndirectRef PromoteStream(PdfStream stream)
    {
        if (!_promotedStreams.TryGetValue(stream, out var num))
        {
            num = _nextObjectNumber++;
            _promotedStreams[stream] = num;
            _deferredStreams.Enqueue((num, stream));
        }
        return new PdfIndirectRef(num, 0);
    }

    private void Write(string text)
    {
        _output.Write(Encoding.ASCII.GetBytes(text));
    }

    private void WriteLn(string text)
    {
        Write(text);
        _output.WriteByte((byte)'\n');
    }

    private static List<(int start, int count)> GroupConsecutive(List<int> numbers)
    {
        var result = new List<(int, int)>();
        if (numbers.Count == 0) return result;

        var start = numbers[0];
        var count = 1;

        for (var i = 1; i < numbers.Count; i++)
        {
            if (numbers[i] == start + count)
            {
                count++;
            }
            else
            {
                result.Add((start, count));
                start = numbers[i];
                count = 1;
            }
        }
        result.Add((start, count));
        return result;
    }
}
