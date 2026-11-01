using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Submit-form action — sends form field data to a URL or remote file
/// (PDF 32000-1:2008 §12.7.5.2).
/// </summary>
public sealed class SubmitFormAction : PdfAction
{
    /// <summary>Treat the listed field array as an exclude-list rather than
    /// an include-list (bit 1 of /Flags per PDF spec table 237).</summary>
    public const int Exclude = 1;

    /// <summary>Submit empty fields as well as filled ones
    /// (bit 2 of /Flags).</summary>
    public const int IncludeNoValueFields = 2;

    /// <summary>Submit field names and values in HTML form-encoded
    /// format (bit 3 of /Flags).</summary>
    public const int ExportFormat = 4;

    /// <summary>Use HTTP GET instead of POST when submitting the form
    /// (bit 4 of /Flags).</summary>
    public const int GetMethod = 8;

    /// <summary>Include the submit-button click coordinates with the
    /// posted data (bit 5 of /Flags).</summary>
    public const int SubmitCoordinates = 16;

    /// <summary>Submit the field data as XFDF rather than FDF
    /// (bit 6 of /Flags). Spelled "Xfdf" to match the public member
    /// name; the wire format is uppercase XFDF.</summary>
    public const int Xfdf = 32;

    /// <summary>Include incremental-save update entries in the FDF
    /// payload (bit 7 of /Flags).</summary>
    public const int IncludeAppendSaves = 64;

    /// <summary>Include markup annotations in the FDF payload
    /// (bit 8 of /Flags).</summary>
    public const int IncludeAnnotations = 128;

    /// <summary>Submit the entire PDF as the request body
    /// (bit 9 of /Flags). Spelled "SubmitPdf" to match the public
    /// member name.</summary>
    public const int SubmitPdf = 256;

    /// <summary>Convert dates to standard PDF canonical format before
    /// submission (bit 10 of /Flags).</summary>
    public const int CanonicalFormat = 512;

    /// <summary>Exclude non-user-edited markup annotations when
    /// /IncludeAnnotations is set (bit 11 of /Flags).</summary>
    public const int ExclNonUserAnnots = 1024;

    /// <summary>Exclude the /F (font) entry from each annotation when
    /// /IncludeAnnotations is set (bit 12 of /Flags).</summary>
    public const int ExclFKey = 2048;

    /// <summary>Embed the PDF inside the submitted FDF payload
    /// (bit 14 of /Flags).</summary>
    public const int EmbedForm = 8192;

    internal SubmitFormAction(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create an empty SubmitForm action. The Url and Flags
    /// must be set before the action is attached to a button.</summary>
    public SubmitFormAction() : base(BuildDict()) { }

    private static PdfDictionary BuildDict()
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("SubmitForm"));
        return dict;
    }

    /// <summary>The destination URL/file specification (/F entry).</summary>
    public FileSpecification? Url
    {
        get
        {
            var fObj = Reader?.Resolve(Dict.Get("F")) ?? Dict.Get("F");
            return fObj switch
            {
                PdfString s => FileSpecification.CreateFromName(s.ToText()),
                PdfDictionary fsDict => FileSpecification.FromExistingDict(fsDict, Reader),
                _ => null,
            };
        }
        set
        {
            if (value is null) Dict.Remove("F");
            else Dict.Set("F", value.Dict);
        }
    }

    /// <summary>The flags integer (/Flags entry) — bitwise OR of
    /// submit-form flags per PDF spec table 237.</summary>
    public int Flags
    {
        get
        {
            var obj = Reader?.Resolve(Dict.Get("Flags")) ?? Dict.Get("Flags");
            return obj is PdfInteger n ? (int)n.Value : 0;
        }
        set => Dict.Set("Flags", new PdfInteger(value));
    }
}

/// <summary>An import-data action (/S /ImportData): imports field data from the
/// FDF/XFDF file identified by the action's /F file specification.</summary>
public sealed class ImportDataAction : PdfAction
{
    internal ImportDataAction(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>The file specification (/F) naming the data file to import.
    /// Accepts both the literal-string and the /Filespec-dictionary forms.</summary>
    public FileSpecification? Data
    {
        get
        {
            var f = Reader?.Resolve(Dict.Get("F")) ?? Dict.Get("F");
            return f switch
            {
                PdfDictionary fd => FileSpecification.FromExistingDict(fd, Reader),
                PdfString ps => FileSpecification.CreateFromName(ps.ToText()),
                _ => null,
            };
        }
    }
}
