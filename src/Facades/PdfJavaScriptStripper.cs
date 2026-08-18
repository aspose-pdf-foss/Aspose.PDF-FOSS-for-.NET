using System.IO;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Removes all JavaScript from a PDF document.
/// </summary>
public sealed class PdfJavaScriptStripper
{
    /// <summary>
    /// Strip all JavaScript from the PDF.
    /// Returns true if any JavaScript was found and removed.
    /// </summary>
    public bool Strip(byte[] input, out byte[] output)
    {
        using var doc = Document.Open(input);
        var removed = StripJavaScript(doc);
        output = doc.ToArray();
        return removed;
    }

    /// <summary>
    /// Strip all JavaScript from <paramref name="inputPath"/> and write the
    /// result to <paramref name="outputPath"/>. Mirrors the
    /// <c>Aspose.Pdf.Facades.PdfJavaScriptStripper.Strip(string, string)</c> overload.
    /// </summary>
    public bool Strip(string inputFile, string outputFile)
    {
        var bytes = File.ReadAllBytes(inputFile);
        var ok = Strip(bytes, out var output);
        File.WriteAllBytes(outputFile, output);
        return ok;
    }

    /// <summary>Stream-based Strip overload.</summary>
    public bool Strip(Stream inStream, Stream outStream)
    {
        using var ms = new MemoryStream();
        inStream.CopyTo(ms);
        var ok = Strip(ms.ToArray(), out var bytes);
        outStream.Write(bytes, 0, bytes.Length);
        return ok;
    }

    /// <summary>
    /// Strip all JavaScript from a document in-place.
    /// Returns true if any JavaScript was found and removed.
    /// </summary>
    public static bool StripJavaScript(Document doc)
    {
        var reader = doc.Reader;
        var catalog = reader.Catalog;
        bool removed = false;

        // Remove /AA (Additional Actions) from catalog
        if (catalog.ContainsKey("AA"))
        {
            catalog.Remove("AA");
            removed = true;
        }

        // Remove JavaScript name tree from /Names
        var names = reader.ResolveDict(catalog.Get("Names"));
        if (names is not null && names.ContainsKey("JavaScript"))
        {
            names.Remove("JavaScript");
            removed = true;
        }

        // Remove /OpenAction if it's a JavaScript action
        var openAction = reader.ResolveDict(catalog.Get("OpenAction"));
        if (openAction is not null && openAction.GetName("S") == "JavaScript")
        {
            catalog.Remove("OpenAction");
            removed = true;
        }

        // Remove JavaScript from page-level actions
        foreach (var page in doc.Pages)
        {
            var pageDict = page.Dict;

            // Remove /AA from page
            if (pageDict.ContainsKey("AA"))
            {
                pageDict.Remove("AA");
                removed = true;
            }

            // Remove JavaScript actions from annotations
            var annots = reader.Resolve(pageDict.Get("Annots")) as PdfArray;
            if (annots is null) continue;

            foreach (var annotRef in annots)
            {
                var annotDict = reader.ResolveDict(annotRef);
                if (annotDict is null) continue;

                // Neutralise a JavaScript /A action rather than dropping it: the
                // widget/field keeps its action structure (so Field.OnActivated still
                // resolves to a JavascriptAction) but the script body is emptied,
                // leaving a zero-length script
                // in place instead of removing the trigger outright.
                var action = reader.ResolveDict(annotDict.Get("A"));
                if (action is not null && action.GetName("S") == "JavaScript")
                {
                    action.Set("JS", new PdfString(System.Array.Empty<byte>()));
                    removed = true;
                }

                // Remove /AA
                if (annotDict.ContainsKey("AA"))
                {
                    annotDict.Remove("AA");
                    removed = true;
                }
            }
        }

        return removed;
    }
}
