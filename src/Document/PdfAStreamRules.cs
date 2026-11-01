using System.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.Optimization;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The filters whose output is the stream's own data, so a chain made of them
    /// alone can be decoded and stored again under another compression.</summary>
    private static readonly HashSet<string> LosslessStreamFilters = new(StringComparer.Ordinal)
    {
        "LZWDecode", "LZW", "FlateDecode", "Fl", "ASCII85Decode", "A85", "ASCIIHexDecode", "AHx",
        "RunLengthDecode", "RL",
    };

    /// <summary>ISO 19005-1 clause 6.1.10 (parts 2 and 3: 6.1.7): no stream is compressed with
    /// LZW. Whether any is, is read from the file's bytes first: a file that never names the
    /// filter pays one search, and none of its objects is loaded for the rule.</summary>
    private void ReplaceLzwCompressionForPdfA(PdfAConvertState pa)
    {
        if (_data.AsSpan().IndexOf("/LZWDecode"u8) < 0) return;

        var infrastructure = _reader.XRefTable.InfrastructureObjectNumbers();
        foreach (var entry in _reader.XRefTable.Entries.Values.OrderBy(e => e.ObjectNumber).ToList())
        {
            // An object stored inside an object stream is never a stream itself.
            if (!entry.InUse || entry.IsCompressed || entry.ObjectNumber == 0
                || infrastructure.Contains(entry.ObjectNumber)) continue;
            PdfStream? stream;
            try { stream = _reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation)) as PdfStream; }
            catch { continue; }
            if (stream is not null) ReplaceLzwCompression(stream, entry.ObjectNumber, entry.Generation, pa);
        }
    }

    /// <summary>An LZW-compressed stream is stored decoded and takes the writer's Flate
    /// compression. A chain that also runs an image codec is left alone, because its decode is
    /// not the stream's data.</summary>
    private void ReplaceLzwCompression(PdfStream stream, int objectNumber, int generation, PdfAConvertState pa)
    {
        var filters = _reader.Resolve(stream.Dict.Get("Filter")) switch
        {
            PdfName name => new List<string?> { name.Value },
            PdfArray chain => chain.Select(f => (_reader.Resolve(f) as PdfName)?.Value).ToList(),
            _ => null,
        };
        if (filters is null || !filters.Exists(f => f is "LZWDecode" or "LZW")) return;

        // A stream of an encrypted file is deciphered by its object number wherever it is read,
        // so it cannot hold plain data in place; it stays as the file has it.
        var rewritable = !_reader.IsDecrypted
            && filters.TrueForAll(f => f is not null && LosslessStreamFilters.Contains(f));
        pa.options.ConversionLog.Add(new PdfAViolation
        {
            Rule = "LzwFilter",
            Clause = pa.part == "1" ? "6.1.10" : "6.1.7",
            Description = "The LZWDecode filter is prohibited",
            ObjectId = objectNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convertable = rewritable,
        });
        if (!rewritable) return;

        byte[] data;
        try { data = _reader.DecodeStream(stream, objectNumber, generation); }
        catch { return; /* undecodable: the stream stays as the file has it */ }
        stream.ReplaceData(data);
        stream.Dict.Remove("Filter");
        stream.Dict.Remove("DecodeParms");
    }
}
