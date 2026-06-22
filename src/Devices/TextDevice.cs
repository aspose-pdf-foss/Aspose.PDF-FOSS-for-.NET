using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

/// <summary>
/// Extracts text from PDF pages.
/// </summary>
public sealed class TextDevice
{
    /// <summary>Gets or sets the text encoding used when writing to a stream.</summary>
    public Encoding Encoding { get; set; } = Encoding.Unicode;

    /// <summary>Gets or sets the text extraction options.</summary>
    public TextExtractionOptions ExtractionOptions { get; set; } = new TextExtractionOptions();

    /// <summary>Initializes a TextDevice with default settings.</summary>
    public TextDevice() { }

    /// <summary>Initializes a TextDevice with the specified encoding.</summary>
    public TextDevice(Encoding encoding)
    {
        Encoding = encoding ?? Encoding.Unicode;
    }

    /// <summary>Initializes a TextDevice with the specified extraction options.</summary>
    public TextDevice(TextExtractionOptions extractionOptions)
    {
        ExtractionOptions = extractionOptions ?? new TextExtractionOptions();
    }

    /// <summary>Initializes a TextDevice with extraction options and encoding.</summary>
    public TextDevice(TextExtractionOptions extractionOptions, Encoding encoding)
    {
        ExtractionOptions = extractionOptions ?? new TextExtractionOptions();
        Encoding = encoding ?? Encoding.Unicode;
    }

    /// <summary>
    /// Extract text from a single page.
    /// </summary>
    public string Process(Page page)
    {
        // Honor the device's configured extraction options so Raw vs Pure
        // formatting-mode flags reach the TextAbsorber (which now uses them
        // to decide single vs proportional space insertion on inter-run gaps).
        var absorber = new TextAbsorber(ExtractionOptions);
        absorber.Visit(page);
        return absorber.Text;
    }

    /// <summary>
    /// Extract text from all pages of a document.
    /// </summary>
    public string Process(Document document)
    {
        var absorber = new TextAbsorber(ExtractionOptions);
        absorber.Visit(document);
        return absorber.Text;
    }

    /// <summary>
    /// Extract text and write to a stream.
    /// </summary>
    public void Process(Page page, Stream output)
    {
        var text = Process(page);
        var bytes = Encoding.GetBytes(text);
        output.Write(bytes);
    }

    /// <summary>
    /// Extract text and write to a file.
    /// </summary>
    public void Process(Page page, string outputFileName)
    {
        using var fs = new FileStream(outputFileName, FileMode.Create, FileAccess.Write);
        Process(page, fs);
    }
}
