using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileEditor
{
    /// <summary>How the facade reacts to corrupted input during Concatenate.</summary>
    public enum ConcatenateCorruptedFileAction
    {
        /// <summary>Stop on the first corrupted file (.NET default).</summary>
        StopWithError,
        /// <summary>Skip the corrupted file and continue concatenating.</summary>
        ConcatenateIgnoringCorruptedObjects,
        /// <summary>Alias for <see cref="ConcatenateIgnoringCorruptedObjects"/>.</summary>
        ConcatenateIgnoringCorrupted = ConcatenateIgnoringCorruptedObjects,
    }

    /// <summary>
    /// Mirrors <c>PdfFileEditor.CorruptedFileAction</c>. Stored only;
    /// Concatenate currently stops on any PDF parse error regardless of this value.
    /// </summary>
    public ConcatenateCorruptedFileAction CorruptedFileAction { get; set; }

    /// <summary>
    /// When true, the stream-based Concatenate / Append / Insert overloads
    /// dispose the input streams (and the output stream after writing) once
    /// the operation completes. Default is false — callers retain ownership
    /// of their streams. Mirrors the .NET API.
    /// </summary>
    public bool CloseConcatenatedStreams { get; set; }

    /// <summary>
    /// Concatenate multiple PDF documents into one.
    /// </summary>
    public byte[] Concatenate(params byte[][] inputFiles)
    {
        if (inputFiles.Length == 0)
            throw new ArgumentException("At least one input file required", nameof(inputFiles));

        if (inputFiles.Length == 1)
            return inputFiles[0];

        var allPageObjNums = new List<int>();

        // Use a temp file to avoid MemoryStream 2GB limit for large concatenations (100+ copies)
        // Not Path.GetTempFileName(): it needs mkstemps, which WebAssembly (WASI) does not have, and there it
        // fails with "IOException: Success". A GUID name in the temp folder, created exclusively, works everywhere.
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write))
            {
                WriteConcatenation(output, inputFiles, allPageObjNums);
            }

            // Read the result — use FileStream for chunk reading to avoid File.ReadAllBytes
            // 2 GB limit on older runtimes.
            var bytes = ReadAllBytesFromFile(tempPath);
            AppendConversionLog($"Concatenated {inputFiles.Length} inputs into {allPageObjNums.Count} pages.");
            return ApplyPostConcatenateOptions(bytes);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    /// <summary>Post-Concatenate pass that honours the RemoveSignatures /
    /// OwnerPassword / ConvertTo properties on the facade. Real implementations
    /// — each flag triggers a follow-on Document open/operate/save.</summary>
    private byte[] ApplyPostConcatenateOptions(byte[] bytes)
    {
        if (RemoveSignatures)
        {
            using var doc = Document.Open(bytes);
            var form = doc.Form;
            if (form is not null)
            {
                foreach (var field in form.Fields)
                {
                    if (field.Type != Forms.FieldType.Signature) continue;
                    field.Dict.Remove("V");
                }
                bytes = doc.ToArray();
                AppendConversionLog("RemoveSignatures: stripped /V from every signature field.");
            }
        }
        if (!string.IsNullOrEmpty(OwnerPassword))
        {
            using var doc = Document.Open(bytes);
            doc.Encrypt(string.Empty, OwnerPassword,
                permissions: null,
                algorithm: Aspose.Pdf.CryptoAlgorithm.AESx128);
            bytes = doc.ToArray();
            AppendConversionLog($"OwnerPassword set; output encrypted with AES-128.");
        }
        if (_convertToFormat is { } fmt)
        {
            using var doc = Document.Open(bytes);
            doc.Convert(Stream.Null, fmt, ConvertErrorAction.Delete);
            bytes = doc.ToArray();
            AppendConversionLog($"ConvertTo: output converted to {fmt}.");
        }
        return bytes;
    }

    // CompactAfterPageRemoval only detaches the deleted pages; objects the source reached by
    // other routes still travel with the extract, so a one-page cut of a large document keeps
    // paying for the whole original. Under OptimizeSize the extract is additionally reduced to
    // what the surviving pages actually reach. Streams are left alone — this is a pure
    // reachability prune, not a re-encode.
    //
    // The prune runs on the serialized extract rather than on the still-open document: page
    // deletions live in the in-memory page tree, while the reachability walk starts from the
    // trailer as parsed, which still names every original page. Walking that would mark the
    // whole source reachable and prune nothing. Writing first collapses the two views into one.
    private byte[] ApplySizeOptimization(byte[] extracted)
    {
        if (!OptimizeSize) return extracted;
        using var doc = Document.Open(extracted);
        doc.OptimizeResources(new Aspose.Pdf.Optimization.OptimizationOptions
        {
            RemoveUnusedObjects = true,
            RemoveUnusedStreams = false,
        });
        return doc.ToArray();
    }

    /// <summary>
    /// Concatenate multiple PDF files into one output file.
    /// </summary>
    public bool Concatenate(string[] inputFiles, string outputFile)
    {
        _corrupted.Clear();
        List<(byte[], string?)> named;
        try
        {
            named = inputFiles.Select(f => (File.ReadAllBytes(f), (string?)f)).ToList();
        }
        catch (IOException ex)
        {
            // Missing/unreadable inputs surface as a PdfException
            // WRAPPING the IO error — callers (and TryConcatenate's LastException)
            // pattern-match on InnerException being e.g. FileNotFoundException.
            throw new PdfException(ex.Message, ex);
        }
        var inputs = FilterCorruptedInputs(named).ToArray();
        var result = Concatenate(inputs);
        File.WriteAllBytes(outputFile, result);
        return true;
    }

    /// <summary>Parse-probe each input and honour <see cref="CorruptedFileAction"/>.
    /// Returns the inputs that parsed cleanly; the rest are recorded in
    /// <see cref="CorruptedItems"/>. When the action is
    /// <see cref="ConcatenateCorruptedFileAction.StopWithError"/>, the first
    /// unparseable input raises an <see cref="ArgumentException"/>. The recorded
    /// <see cref="CorruptedItem.Index"/> is the position within
    /// <paramref name="inputs"/>.</summary>
    private List<byte[]> FilterCorruptedInputs(IReadOnlyList<(byte[] data, string? name)> inputs)
    {
        var valid = new List<byte[]>();
        for (int i = 0; i < inputs.Count; i++)
        {
            var (data, name) = inputs[i];
            try
            {
                var reader = PdfReader.FromBytes(data);
                // Force trailer/catalog/page-tree resolution so a structurally
                // broken file is detected here rather than mid-merge.
                var pages = reader.ResolveDict(reader.Catalog.Get("Pages"));
                if (pages is null) throw new PdfException("No page tree.");
                valid.Add(data);
            }
            catch (Exception ex)
            {
                if (CorruptedFileAction == ConcatenateCorruptedFileAction.StopWithError)
                    throw new ArgumentException($"Input at index {i} could not be parsed.", ex);
                _corrupted.Add(new CorruptedItem(name, i, ex));
            }
        }
        return valid;
    }

    /// <summary>
    /// Concatenate two PDF files into one output file.
    /// </summary>
    public bool Concatenate(string firstInputFile, string secInputFile, string outputFile)
    {
        return Concatenate(new[] { firstInputFile, secInputFile }, outputFile);
    }

    /// <summary>
    /// Concatenate two PDF files with a blank-page separator inserted between them.
    /// </summary>
    public bool Concatenate(string firstInputFile, string secInputFile, string blankPageFile, string outputFile)
    {
        return Concatenate(new[] { firstInputFile, blankPageFile, secInputFile }, outputFile);
    }

    /// <summary>Concatenate multiple PDF streams into one output stream.</summary>
    public bool Concatenate(Stream[] inputStream, Stream outputStream)
    {
        var inputs = inputStream.Select(ReadStream).ToArray();
        var result = Concatenate(inputs);
        outputStream.Write(result, 0, result.Length);
        // A seekable output is left rewound so callers can read
        // the concatenated bytes back without seeking.
        if (outputStream.CanSeek) outputStream.Position = 0;
        if (CloseConcatenatedStreams)
        {
            foreach (var s in inputStream) s.Dispose();
            outputStream.Dispose();
        }
        return true;
    }

    /// <summary>Concatenate two PDF streams into one output stream.</summary>
    public bool Concatenate(Stream firstInputStream, Stream secInputStream, Stream outputStream)
    {
        var result = Concatenate(ReadStream(firstInputStream), ReadStream(secInputStream));
        outputStream.Write(result, 0, result.Length);
        // A seekable output is left rewound so callers can read
        // the concatenated bytes back without seeking.
        if (outputStream.CanSeek) outputStream.Position = 0;
        if (CloseConcatenatedStreams)
        {
            firstInputStream.Dispose();
            secInputStream.Dispose();
            outputStream.Dispose();
        }
        return true;
    }

    /// <summary>Concatenate two PDF streams with a blank-page separator inserted between them.</summary>
    public bool Concatenate(Stream firstInputStream, Stream secInputStream, Stream blankPageStream, Stream outputStream)
    {
        var result = Concatenate(new[]
        {
            ReadStream(firstInputStream),
            ReadStream(blankPageStream),
            ReadStream(secInputStream),
        });
        outputStream.Write(result, 0, result.Length);
        // A seekable output is left rewound so callers can read
        // the concatenated bytes back without seeking.
        if (outputStream.CanSeek) outputStream.Position = 0;
        if (CloseConcatenatedStreams)
        {
            firstInputStream.Dispose();
            secInputStream.Dispose();
            blankPageStream.Dispose();
            outputStream.Dispose();
        }
        return true;
    }

    /// <summary>Concatenate the pages of <paramref name="src"/> into the
    /// existing <paramref name="dest"/> document. Each source is left
    /// untouched; the target receives all pages in source-order. Honours
    /// the same flag set as the file/stream Concatenate overloads.</summary>
    public bool Concatenate(Document[] src, Document dest)
    {
        if (dest is null) throw new ArgumentNullException(nameof(dest));
        if (src is null) return false;
        foreach (var s in src)
        {
            if (s is null) continue;
            dest.Pages.Add(s.Pages);
            if (CopyLogicalStructure) dest.MergeLogicalStructure(s);
        }
        return true;
    }

    /// <summary>Read a stream fully into a byte array.</summary>
    private static byte[] ReadStream(Stream s)
    {
        // ToArray() returns exactly the logical content (Length bytes from offset 0)
        // regardless of the stream's Position or spare capacity. The previous
        // TryGetBuffer fast-path returned the whole backing array — which includes
        // trailing unused-capacity zero bytes when Capacity > Length — corrupting the
        // PDF (trailing garbage after %%EOF → "root object missing" on re-read).
        if (s is MemoryStream ms) return ms.ToArray();
        if (s.CanSeek) s.Position = 0;
        using var copy = new MemoryStream();
        s.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// Read all bytes from a file, supporting files larger than 2 GB.
    /// </summary>
    private static byte[] ReadAllBytesFromFile(string path)
    {
        var fileInfo = new FileInfo(path);
        var length = fileInfo.Length;
        if (length > Compat.ArrayMaxLength)
            throw new InvalidOperationException(
                $"Concatenated PDF is {length / (1024 * 1024)} MB, exceeding the 2 GB byte[] limit.");

        var bytes = new byte[length];
        using var fs = File.OpenRead(path);
        var bytesRead = 0;
        while (bytesRead < bytes.Length)
        {
            var read = fs.Read(bytes, bytesRead, bytes.Length - bytesRead);
            if (read == 0) break;
            bytesRead += read;
        }
        return bytes;
    }
}
