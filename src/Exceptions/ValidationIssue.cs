using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Represents a validation issue found in a PDF document.
/// </summary>
public sealed class ValidationIssue
{
    /// <summary>Issue severity: "error" for MUST requirements, "warning" for advisory.</summary>
    public string Severity { get; }

    /// <summary>Machine-readable issue code (e.g., "CATALOG_TYPE_INVALID").</summary>
    public string Code { get; }

    /// <summary>Human-readable description of the issue.</summary>
    public string Message { get; }

    /// <summary>Optional location hint (e.g., "Page 3").</summary>
    public string? Location { get; }

    internal ValidationIssue(string severity, string code, string message, string? location = null)
    {
        Severity = severity;
        Code = code;
        Message = message;
        Location = location;
    }
}

/// <summary>
/// Validates PDF document structure and conformance.
/// </summary>
internal static class DocumentValidator
{
    private static readonly HashSet<string> Standard14Fonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
        "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Symbol", "ZapfDingbats"
    };

    public static ValidationIssue[] Validate(Document doc)
    {
        var issues = new List<ValidationIssue>();
        var reader = doc.Reader;
        var catalog = reader.Catalog;

        CheckCatalog(catalog, issues);
        CheckPages(doc, reader, catalog, issues);
        CheckAnnotations(doc, issues);
        CheckFormFields(doc, issues);
        CheckFonts(doc, reader, issues);
        CheckInfo(doc, issues);

        return issues.ToArray();
    }

    private static void CheckCatalog(PdfDictionary catalog, List<ValidationIssue> issues)
    {
        var type = catalog.GetName("Type");
        if (type is not null && type != "Catalog")
        {
            issues.Add(new ValidationIssue("error", "CATALOG_TYPE_INVALID",
                $"Catalog /Type is '{type}', expected 'Catalog'"));
        }
        else if (type is null)
        {
            issues.Add(new ValidationIssue("error", "CATALOG_TYPE_INVALID",
                "Catalog is missing required /Type entry"));
        }

        if (!catalog.ContainsKey("Pages"))
        {
            issues.Add(new ValidationIssue("error", "CATALOG_MISSING_PAGES",
                "Catalog is missing required /Pages entry"));
        }
    }

    private static void CheckPages(Document doc, PdfReader reader, PdfDictionary catalog,
        List<ValidationIssue> issues)
    {
        try
        {
            for (var i = 1; i <= doc.PageCount; i++)
            {
                var page = doc.Pages.At(i);
                var mb = page.MediaBox;
                if (mb.Width <= 0 || mb.Height <= 0)
                {
                    issues.Add(new ValidationIssue("error", "PAGE_INVALID_MEDIABOX",
                        $"Page {i} has non-positive MediaBox dimensions ({mb.Width} × {mb.Height})",
                        $"Page {i}"));
                }
                else if (mb.Width > 14400 || mb.Height > 14400)
                {
                    issues.Add(new ValidationIssue("warning", "PAGE_UNUSUALLY_LARGE",
                        $"Page {i} MediaBox exceeds 200 inches ({mb.Width} × {mb.Height})",
                        $"Page {i}"));
                }
            }
        }
        catch
        {
            // If pages cannot be iterated, the catalog check already covers this
        }

        // Check page count mismatch — only for documents loaded from bytes,
        // not for dynamically-built documents where /Count may lag behind actual page list.
        try
        {
            var pagesDict = reader.ResolveDict(catalog.Get("Pages"));
            if (pagesDict is not null)
            {
                var countObj = pagesDict.Get("Count");
                if (countObj is PdfInteger countInt)
                {
                    var declared = (int)countInt.Value;
                    var actual = doc.PageCount;
                    // Only flag if declared > 0 and mismatches (skip when declared=0, common for newly-built docs)
                    if (declared > 0 && declared != actual)
                    {
                        issues.Add(new ValidationIssue("error", "PAGE_COUNT_MISMATCH",
                            $"Declared page count {declared} does not match actual {actual}"));
                    }
                }
            }
        }
        catch
        {
            // Ignore if pages tree is unreadable
        }
    }

    private static void CheckAnnotations(Document doc, List<ValidationIssue> issues)
    {
        try
        {
            for (var i = 1; i <= doc.PageCount; i++)
            {
                var page = doc.Pages.At(i);
                foreach (var annot in page.Annotations)
                {
                    var rect = annot.Rect;
                    if (rect is null) continue;
                    if (rect.Width < 0 || rect.Height < 0)
                    {
                        issues.Add(new ValidationIssue("warning", "ANNOT_INVALID_RECT",
                            $"Annotation has inverted rect on page {i}",
                            $"Page {i}"));
                    }
                    else if (rect.Width == 0 && rect.Height == 0)
                    {
                        issues.Add(new ValidationIssue("warning", "ANNOT_ZERO_AREA_RECT",
                            $"Annotation has zero-area rect on page {i}",
                            $"Page {i}"));
                    }
                }
            }
        }
        catch
        {
            // Skip if annotations cannot be read
        }
    }

    private static void CheckFormFields(Document doc, List<ValidationIssue> issues)
    {
        try
        {
            if (!doc.HasForm) return;
            var form = doc.Form;
            if (form is null) return;

            var names = new HashSet<string>();
            foreach (var field in form.Fields)
            {
                if (string.IsNullOrEmpty(field.FullName))
                {
                    issues.Add(new ValidationIssue("error", "FIELD_MISSING_NAME",
                        "Form field is missing /T (partial name)"));
                }
                else if (!names.Add(field.FullName))
                {
                    issues.Add(new ValidationIssue("warning", "FIELD_DUPLICATE_NAME",
                        $"Duplicate field name: '{field.FullName}'"));
                }
            }
        }
        catch
        {
            // Skip if form cannot be read
        }
    }

    private static void CheckFonts(Document doc, PdfReader reader, List<ValidationIssue> issues)
    {
        try
        {
            for (var i = 1; i <= doc.PageCount; i++)
            {
                var page = doc.Pages.At(i);
                foreach (var font in page.Fonts)
                {
                    if (!font.IsEmbedded && !Standard14Fonts.Contains(font.BaseFont))
                    {
                        issues.Add(new ValidationIssue("warning", "FONT_NOT_EMBEDDED",
                            $"Font '{font.BaseFont}' is not embedded",
                            $"Page {i}"));
                    }
                }
            }
        }
        catch
        {
            // Skip if fonts cannot be read
        }
    }

    private static void CheckInfo(Document doc, List<ValidationIssue> issues)
    {
        try
        {
            var info = doc.Info;
            if (string.IsNullOrEmpty(info.Title))
            {
                issues.Add(new ValidationIssue("warning", "INFO_MISSING_TITLE",
                    "Document has no /Title in info dictionary"));
            }
        }
        catch
        {
            // Skip if info cannot be read
        }
    }
}
