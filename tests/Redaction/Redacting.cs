using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Tests.Redaction;

/// <summary>Redact page 1 of a PDF and read back what the saved file holds.</summary>
internal static class Redacting
{
    internal static byte[] Save(byte[] pdf, Rectangle rect, Color? fill = null)
    {
        using var doc = Document.Open(pdf);
        var annotation = new RedactionAnnotation(doc.Pages[1], rect) { FillColor = fill };
        doc.Pages[1].Annotations.Add(annotation);
        annotation.Redact();
        return doc.ToArray();
    }

    internal static string TextOf(Document doc)
    {
        var absorber = new TextAbsorber();
        doc.Pages[1].Accept(absorber);
        return absorber.Text;
    }

    /// <summary>Every content stream of page 1 as text, operators one per line.</summary>
    internal static string ContentOf(Document doc)
    {
        var sb = new StringBuilder();
        foreach (var op in doc.Pages[1].Contents) sb.Append(op).Append('\n');
        return sb.ToString();
    }

    /// <summary>The x of every point a path operator of page 1 names (m, l, and re corners).</summary>
    internal static List<double> PathXs(Document doc)
    {
        var xs = new List<double>();
        foreach (var line in ContentOf(doc).Split('\n'))
        {
            var parts = line.Trim().Split(' ');
            double N(int i) => double.Parse(parts[i], CultureInfo.InvariantCulture);
            switch (parts[parts.Length - 1])
            {
                case "m" or "l" when parts.Length == 3:
                    xs.Add(N(0));
                    break;
                case "re" when parts.Length == 5:
                    xs.Add(N(0));
                    xs.Add(N(0) + N(2));
                    break;
            }
        }
        return xs;
    }

    /// <summary>The names of the XObjects page 1 draws, in order.</summary>
    internal static List<string> DrawnNames(Document doc) =>
        Regex.Matches(ContentOf(doc), @"/(\S+) Do").Cast<Match>().Select(m => m.Groups[1].Value).ToList();

    /// <summary>The decoded samples of an XObject page 1 draws.</summary>
    internal static byte[] SamplesOf(Document doc, string name)
    {
        var page = doc.Pages[1];
        var reader = page.Reader;
        var resources = reader.ResolveDict(page.Dict.Get("Resources"))!;
        var stream = (PdfStream)reader.Resolve(reader.ResolveDict(resources.Get("XObject"))!.Get(name))!;
        return reader.DecodeStream(stream);
    }
}
