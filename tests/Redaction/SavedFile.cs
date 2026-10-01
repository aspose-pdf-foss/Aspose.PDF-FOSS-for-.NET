using System.IO.Compression;
using System.Text;

namespace Aspose.Pdf.Tests.Redaction;

/// <summary>Looks through the bytes of a saved PDF - every stream inflated as well as read as it
/// is - for what a redaction was meant to remove, whatever object still carries it.</summary>
internal static class SavedFile
{
    /// <summary>Whether the file holds <paramref name="text"/> as a literal or as hex (either case).</summary>
    internal static bool Holds(byte[] pdf, string text)
    {
        var hex = string.Concat(Encoding.ASCII.GetBytes(text).Select(b => b.ToString("X2")));
        foreach (var chunk in Chunks(pdf))
        {
            var s = Encoding.GetEncoding("ISO-8859-1").GetString(chunk);
            var squeezed = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (s.Contains(text) || squeezed.IndexOf(hex, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    /// <summary>Whether any stream in the file inflates to bytes holding <paramref name="run"/>.</summary>
    internal static bool HoldsBytes(byte[] pdf, byte[] run)
    {
        foreach (var chunk in Chunks(pdf))
            for (var i = 0; i + run.Length <= chunk.Length; i++)
                if (chunk.AsSpan(i, run.Length).SequenceEqual(run)) return true;
        return false;
    }

    private static IEnumerable<byte[]> Chunks(byte[] pdf)
    {
        yield return pdf;
        var file = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
        for (var at = file.IndexOf("stream", StringComparison.Ordinal); at >= 0;
             at = file.IndexOf("stream", at + 6, StringComparison.Ordinal))
        {
            if (at >= 3 && file.Substring(at - 3, 3) == "end") continue;
            var start = at + 6;
            if (start < file.Length && file[start] == '\r') start++;
            if (start < file.Length && file[start] == '\n') start++;
            var end = file.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) break;
            if (Inflate(pdf, start, end - start) is { } inflated) yield return inflated;
        }
    }

    private static byte[]? Inflate(byte[] pdf, int start, int length)
    {
        if (length < 3) return null;
        try
        {
            using var input = new MemoryStream(pdf, start + 2, length - 2);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.Length > 0 ? output.ToArray() : null;
        }
        catch (InvalidDataException) { return null; }
    }
}
