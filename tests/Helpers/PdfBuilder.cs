using System.Text;

namespace Aspose.Pdf.Tests.Helpers;

/// <summary>
/// Builds minimal valid PDF files for testing.
/// </summary>
internal static class PdfBuilder
{
    /// <summary>
    /// Build a minimal valid PDF with a single blank page.
    /// Returns the raw bytes.
    /// </summary>
    public static byte[] BuildMinimal()
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");

        // Object 1: Catalog
        var catalogOffset = sb.Length;
        sb.Append("1 0 obj\n");
        sb.Append("<< /Type /Catalog /Pages 2 0 R >>\n");
        sb.Append("endobj\n");

        // Object 2: Pages
        var pagesOffset = sb.Length;
        sb.Append("2 0 obj\n");
        sb.Append("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\n");
        sb.Append("endobj\n");

        // Object 3: Page
        var pageOffset = sb.Length;
        sb.Append("3 0 obj\n");
        sb.Append("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\n");
        sb.Append("endobj\n");

        // Xref
        var xrefOffset = sb.Length;
        sb.Append("xref\n");
        sb.Append("0 4\n");
        sb.AppendFormat("0000000000 65535 f \n");
        sb.AppendFormat("{0:D10} 00000 n \n", catalogOffset);
        sb.AppendFormat("{0:D10} 00000 n \n", pagesOffset);
        sb.AppendFormat("{0:D10} 00000 n \n", pageOffset);

        // Trailer
        sb.Append("trailer\n");
        sb.Append("<< /Size 4 /Root 1 0 R >>\n");
        sb.Append("startxref\n");
        sb.AppendFormat("{0}\n", xrefOffset);
        sb.Append("%%EOF\n");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Build a PDF with a FlateDecode stream (for filter testing).
    /// </summary>
    public static byte[] BuildWithFlateStream(byte[] streamContent)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed,
            System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            zlib.Write(streamContent);
        }
        var compressedBytes = compressed.ToArray();

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");

        // Object 1: Catalog
        var catalogOffset = sb.Length;
        sb.Append("1 0 obj\n");
        sb.Append("<< /Type /Catalog /Pages 2 0 R >>\n");
        sb.Append("endobj\n");

        // Object 2: Pages
        var pagesOffset = sb.Length;
        sb.Append("2 0 obj\n");
        sb.Append("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\n");
        sb.Append("endobj\n");

        // Object 3: Page
        var pageOffset = sb.Length;
        sb.Append("3 0 obj\n");
        sb.Append($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>\n");
        sb.Append("endobj\n");

        // Object 4: Stream
        var streamOffset = sb.Length;
        sb.Append("4 0 obj\n");
        sb.Append($"<< /Length {compressedBytes.Length} /Filter /FlateDecode >>\n");
        sb.Append("stream\n");

        // Build header as ASCII, then append binary data
        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());

        var rest = new StringBuilder();
        rest.Append("\nendstream\n");
        rest.Append("endobj\n");

        // Xref
        var xrefOffset = headerBytes.Length + compressedBytes.Length + Encoding.ASCII.GetByteCount(rest.ToString());
        // Recalculate with full content...

        // Actually, let's build it properly using byte arrays
        return BuildWithFlateStreamBytes(streamContent);
    }

    private static byte[] BuildWithFlateStreamBytes(byte[] streamContent)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed,
            System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            zlib.Write(streamContent);
        }
        var compressedBytes = compressed.ToArray();

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>\nendobj\n");

        var streamObjOffset = ms.Position;
        Write($"4 0 obj\n<< /Length {compressedBytes.Length} /Filter /FlateDecode >>\nstream\n");
        ms.Write(compressedBytes);
        Write("\nendstream\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 5\n");
        Write($"0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{streamObjOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a text annotation on the first page.
    /// </summary>
    public static byte[] BuildWithAnnotation()
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var annotOffset = ms.Position;
        Write("4 0 obj\n<< /Type /Annot /Subtype /Text /Rect [100 200 200 300] /Contents (A note) >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 5\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{annotOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a simple text form field.
    /// </summary>
    public static byte[] BuildWithFormField()
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm 5 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var fieldOffset = ms.Position;
        Write("4 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Tx /T (Name) /V (John) /Rect [100 700 300 720] >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>\nendobj\n");

        var formOffset = ms.Position;
        Write("5 0 obj\n<< /Fields [4 0 R] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{fieldOffset:D10} 00000 n \n");
        Write($"{formOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with multiple blank pages.
    /// </summary>
    public static byte[] BuildMultiPage(int pageCount)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        var kidRefs = string.Join(" ", Enumerable.Range(3, pageCount).Select(i => $"{i} 0 R"));
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write($"2 0 obj\n<< /Type /Pages /Kids [{kidRefs}] /Count {pageCount} >>\nendobj\n");

        var pageOffsets = new long[pageCount];
        for (var i = 0; i < pageCount; i++)
        {
            pageOffsets[i] = ms.Position;
            var w = 612 + i * 10; // vary width slightly per page
            Write($"{3 + i} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} 792] >>\nendobj\n");
        }

        var xrefOffset = ms.Position;
        Write($"xref\n0 {3 + pageCount}\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        for (var i = 0; i < pageCount; i++)
            Write($"{pageOffsets[i]:D10} 00000 n \n");

        Write($"trailer\n<< /Size {3 + pageCount} /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with document info dictionary (title, author, etc.).
    /// </summary>
    public static byte[] BuildWithDocumentInfo(string? title = null, string? author = null,
        string? subject = null, string? creator = null, string? creationDate = null)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        var infoOffset = ms.Position;
        var infoParts = new List<string>();
        if (title is not null) infoParts.Add($"/Title ({title})");
        if (author is not null) infoParts.Add($"/Author ({author})");
        if (subject is not null) infoParts.Add($"/Subject ({subject})");
        if (creator is not null) infoParts.Add($"/Creator ({creator})");
        if (creationDate is not null) infoParts.Add($"/CreationDate ({creationDate})");
        Write($"4 0 obj\n<< {string.Join(" ", infoParts)} >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 5\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{infoOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 5 /Root 1 0 R /Info 4 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with outline (bookmark) entries.
    /// </summary>
    public static byte[] BuildWithOutlines(params string[] titles)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        // Outline items start at obj 5
        var outlineItemStart = 5;
        var outlineOffset = ms.Position;
        if (titles.Length > 0)
        {
            Write($"4 0 obj\n<< /Type /Outlines /First {outlineItemStart} 0 R " +
                  $"/Last {outlineItemStart + titles.Length - 1} 0 R /Count {titles.Length} >>\nendobj\n");
        }
        else
        {
            Write("4 0 obj\n<< /Type /Outlines /Count 0 >>\nendobj\n");
        }

        var itemOffsets = new long[titles.Length];
        for (var i = 0; i < titles.Length; i++)
        {
            itemOffsets[i] = ms.Position;
            var objNum = outlineItemStart + i;
            var parts = $"/Title ({titles[i]}) /Parent 4 0 R";
            if (i < titles.Length - 1)
                parts += $" /Next {objNum + 1} 0 R";
            if (i > 0)
                parts += $" /Prev {objNum - 1} 0 R";
            Write($"{objNum} 0 obj\n<< {parts} >>\nendobj\n");
        }

        var totalObjs = outlineItemStart + titles.Length;
        var xrefOffset = ms.Position;
        Write($"xref\n0 {totalObjs}\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{outlineOffset:D10} 00000 n \n");
        for (var i = 0; i < titles.Length; i++)
            Write($"{itemOffsets[i]:D10} 00000 n \n");

        Write($"trailer\n<< /Size {totalObjs} /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a checkbox form field.
    /// </summary>
    public static byte[] BuildWithCheckboxField(bool isChecked = false)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm 5 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var value = isChecked ? "Yes" : "Off";
        var fieldOffset = ms.Position;
        Write($"4 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Btn /T (agree) /V /{value} /Rect [100 700 120 720] >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>\nendobj\n");

        var formOffset = ms.Position;
        Write("5 0 obj\n<< /Fields [4 0 R] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{fieldOffset:D10} 00000 n \n");
        Write($"{formOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a choice (combo/list) form field.
    /// </summary>
    public static byte[] BuildWithChoiceField(string[] options, string? selected = null, bool isCombo = true)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm 5 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var optStr = string.Join(" ", options.Select(o => $"({o})"));
        var flags = isCombo ? (1 << 17) : 0; // bit 18 = Combo
        var vPart = selected is not null ? $" /V ({selected})" : "";
        var fieldOffset = ms.Position;
        Write($"4 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Ch /T (color) /Ff {flags} /Opt [{optStr}]{vPart} /Rect [100 700 200 720] >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>\nendobj\n");

        var formOffset = ms.Position;
        Write("5 0 obj\n<< /Fields [4 0 R] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{fieldOffset:D10} 00000 n \n");
        Write($"{formOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with multiple form fields (text, checkbox, choice).
    /// </summary>
    public static byte[] BuildWithMultipleFields()
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm 7 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // Field 4: text
        var f4Offset = ms.Position;
        Write("4 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /V (Alice) /TU (Enter your name) /Rect [100 750 300 770] >>\nendobj\n");

        // Field 5: checkbox
        var f5Offset = ms.Position;
        Write("5 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Btn /T (agree) /V /Yes /Rect [100 720 120 740] >>\nendobj\n");

        // Field 6: choice (combo)
        var f6Offset = ms.Position;
        Write("6 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Ch /T (color) /Ff 131072 /Opt [(Red) (Green) (Blue)] /V (Green) /Rect [100 690 200 710] >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [4 0 R 5 0 R 6 0 R] >>\nendobj\n");

        var formOffset = ms.Position;
        Write("7 0 obj\n<< /Fields [4 0 R 5 0 R 6 0 R] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 8\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{f4Offset:D10} 00000 n \n");
        Write($"{f5Offset:D10} 00000 n \n");
        Write($"{f6Offset:D10} 00000 n \n");
        Write($"{formOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a link annotation (URI action).
    /// </summary>
    public static byte[] BuildWithLinkAnnotation(string uri = "https://example.com")
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var actionOffset = ms.Position;
        Write($"5 0 obj\n<< /S /URI /URI ({uri}) >>\nendobj\n");

        var annotOffset = ms.Position;
        Write("4 0 obj\n<< /Type /Annot /Subtype /Link /Rect [72 700 200 720] /A 5 0 R >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{annotOffset:D10} 00000 n \n");
        Write($"{actionOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a GoTo action link annotation targeting a specific page.
    /// </summary>
    public static byte[] BuildWithGoToAction(int targetPageIndex, int pageCount = 3)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        var kidRefs = string.Join(" ", Enumerable.Range(3, pageCount).Select(i => $"{i} 0 R"));
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write($"2 0 obj\n<< /Type /Pages /Kids [{kidRefs}] /Count {pageCount} >>\nendobj\n");

        var pageOffsets = new long[pageCount];
        var firstPageObjNum = 3;
        var actionObjNum = firstPageObjNum + pageCount;
        var annotObjNum = actionObjNum + 1;

        for (var i = 0; i < pageCount; i++)
        {
            pageOffsets[i] = ms.Position;
            var objNum = firstPageObjNum + i;
            if (i == 0)
                Write($"{objNum} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [{annotObjNum} 0 R] >>\nendobj\n");
            else
                Write($"{objNum} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");
        }

        var targetPageObjNum = firstPageObjNum + targetPageIndex;
        var actionOffset = ms.Position;
        Write($"{actionObjNum} 0 obj\n<< /S /GoTo /D [{targetPageObjNum} 0 R /Fit] >>\nendobj\n");

        var annotOffset = ms.Position;
        Write($"{annotObjNum} 0 obj\n<< /Type /Annot /Subtype /Link /Rect [72 700 200 720] /A {actionObjNum} 0 R >>\nendobj\n");

        var totalObjs = annotObjNum + 1;
        var xrefOffset = ms.Position;
        Write($"xref\n0 {totalObjs}\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        for (var i = 0; i < pageCount; i++)
            Write($"{pageOffsets[i]:D10} 00000 n \n");
        Write($"{actionOffset:D10} 00000 n \n");
        Write($"{annotOffset:D10} 00000 n \n");

        Write($"trailer\n<< /Size {totalObjs} /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a Named action link annotation.
    /// </summary>
    public static byte[] BuildWithNamedAction(string name)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var actionOffset = ms.Position;
        Write($"5 0 obj\n<< /S /Named /N /{name} >>\nendobj\n");

        var annotOffset = ms.Position;
        Write("4 0 obj\n<< /Type /Annot /Subtype /Link /Rect [72 700 200 720] /A 5 0 R >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{annotOffset:D10} 00000 n \n");
        Write($"{actionOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a JavaScript action link annotation.
    /// </summary>
    public static byte[] BuildWithJavaScriptAction(string script)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var actionOffset = ms.Position;
        Write($"5 0 obj\n<< /S /JavaScript /JS ({script}) >>\nendobj\n");

        var annotOffset = ms.Position;
        Write("4 0 obj\n<< /Type /Annot /Subtype /Link /Rect [72 700 200 720] /A 5 0 R >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{annotOffset:D10} 00000 n \n");
        Write($"{actionOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a Launch action link annotation.
    /// </summary>
    public static byte[] BuildWithLaunchAction(string filePath)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var actionOffset = ms.Position;
        Write($"5 0 obj\n<< /S /Launch /F ({filePath}) >>\nendobj\n");

        var annotOffset = ms.Position;
        Write("4 0 obj\n<< /Type /Annot /Subtype /Link /Rect [72 700 200 720] /A 5 0 R >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{annotOffset:D10} 00000 n \n");
        Write($"{actionOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with an encrypted flag (simulated — just the Encrypt key in trailer).
    /// </summary>
    public static byte[] BuildWithEncryptionDict(int v = 2, int r = 3, int length = 128, int permissions = -4)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        var encryptOffset = ms.Position;
        Write($"4 0 obj\n<< /Filter /Standard /V {v} /R {r} /Length {length} /P {permissions} /O <00> /U <00> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 5\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{encryptOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 5 /Root 1 0 R /Encrypt 4 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a tagged structure (MarkInfo + StructTreeRoot).
    /// </summary>
    public static byte[] BuildTagged()
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.7\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 4 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        // StructTreeRoot with a Document element containing a P element
        var paraOffset = ms.Position;
        Write("6 0 obj\n<< /Type /StructElem /S /P /P 5 0 R /Alt (A paragraph) >>\nendobj\n");

        var docElemOffset = ms.Position;
        Write("5 0 obj\n<< /Type /StructElem /S /Document /P 4 0 R /K [6 0 R] >>\nendobj\n");

        var structTreeOffset = ms.Position;
        Write("4 0 obj\n<< /Type /StructTreeRoot /K 5 0 R /ParentTree 7 0 R >>\nendobj\n");

        var parentTreeOffset = ms.Position;
        Write("7 0 obj\n<< /Type /NumberTree /Nums [] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 8\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{structTreeOffset:D10} 00000 n \n");
        Write($"{docElemOffset:D10} 00000 n \n");
        Write($"{paraOffset:D10} 00000 n \n");
        Write($"{parentTreeOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a tagged PDF with an Info dictionary.
    /// </summary>
    public static byte[] BuildTaggedWithInfo(string? title = null)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.7\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 4 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        var paraOffset = ms.Position;
        Write("6 0 obj\n<< /Type /StructElem /S /P /P 5 0 R /Alt (A paragraph) >>\nendobj\n");

        var docElemOffset = ms.Position;
        Write("5 0 obj\n<< /Type /StructElem /S /Document /P 4 0 R /K [6 0 R] >>\nendobj\n");

        var structTreeOffset = ms.Position;
        Write("4 0 obj\n<< /Type /StructTreeRoot /K 5 0 R /ParentTree 7 0 R >>\nendobj\n");

        var parentTreeOffset = ms.Position;
        Write("7 0 obj\n<< /Type /NumberTree /Nums [] >>\nendobj\n");

        var titlePart = title is not null ? $" /Title ({title})" : "";
        var infoOffset = ms.Position;
        Write($"8 0 obj\n<<{titlePart} >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 9\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{structTreeOffset:D10} 00000 n \n");
        Write($"{docElemOffset:D10} 00000 n \n");
        Write($"{paraOffset:D10} 00000 n \n");
        Write($"{parentTreeOffset:D10} 00000 n \n");
        Write($"{infoOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 9 /Root 1 0 R /Info 8 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a tagged PDF with a /Lang entry in the catalog.
    /// </summary>
    public static byte[] BuildTaggedWithLanguage(string language)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.7\n");

        var catalogOffset = ms.Position;
        Write($"1 0 obj\n<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 4 0 R /Lang ({language}) >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        var paraOffset = ms.Position;
        Write("6 0 obj\n<< /Type /StructElem /S /P /P 5 0 R /Alt (A paragraph) >>\nendobj\n");

        var docElemOffset = ms.Position;
        Write("5 0 obj\n<< /Type /StructElem /S /Document /P 4 0 R /K [6 0 R] >>\nendobj\n");

        var structTreeOffset = ms.Position;
        Write("4 0 obj\n<< /Type /StructTreeRoot /K 5 0 R /ParentTree 7 0 R >>\nendobj\n");

        var parentTreeOffset = ms.Position;
        Write("7 0 obj\n<< /Type /NumberTree /Nums [] >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 8\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{structTreeOffset:D10} 00000 n \n");
        Write($"{docElemOffset:D10} 00000 n \n");
        Write($"{paraOffset:D10} 00000 n \n");
        Write($"{parentTreeOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a page that has a rotation.
    /// </summary>
    public static byte[] BuildWithRotation(int rotate)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var pageOffset = ms.Position;
        Write($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Rotate {rotate} >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 4\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with uncompressed text content and a Helvetica font resource.
    /// </summary>
    public static byte[] BuildWithTextContent(byte[] contentBytes)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var fontOffset = ms.Position;
        Write("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");

        var streamObjOffset = ms.Position;
        Write($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        ms.Write(contentBytes);
        Write("\nendstream\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{streamObjOffset:D10} 00000 n \n");
        Write($"{fontOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with multiple named fonts and corresponding text content.
    /// Each entry maps a font resource key (e.g. "F1") to a (baseFont, contentBytes) pair.
    /// </summary>
    public static byte[] BuildWithMultipleFonts(
        params (string resourceKey, string baseFont, byte[] content)[] entries)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // Build all font objects starting at obj 5
        var fontOffsets = new long[entries.Length];
        var fontObjStart = 5;
        var fontResEntries = new StringBuilder();
        for (var i = 0; i < entries.Length; i++)
        {
            var objNum = fontObjStart + i;
            fontOffsets[i] = ms.Position;
            Write($"{objNum} 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /{entries[i].baseFont} /Encoding /WinAnsiEncoding >>\nendobj\n");
            fontResEntries.Append($" /{entries[i].resourceKey} {objNum} 0 R");
        }

        // Concatenate all content bytes
        var allContent = entries.SelectMany(e => e.content.Concat(new byte[] { (byte)'\n' })).ToArray();

        var contentObjNum = fontObjStart + entries.Length;
        var pageOffset = ms.Position;
        Write($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {contentObjNum} 0 R /Resources << /Font <<{fontResEntries}>> >> >>\nendobj\n");

        var streamObjOffset = ms.Position;
        Write($"{contentObjNum} 0 obj\n<< /Length {allContent.Length} >>\nstream\n");
        ms.Write(allContent);
        Write("\nendstream\nendobj\n");

        var totalObjs = contentObjNum + 1;
        var xrefOffset = ms.Position;
        Write($"xref\n0 {totalObjs}\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        // Object 4 is not used, but we need contiguous numbering
        // Actually, fonts start at 5. Let's fix: use obj 4 as a dummy or start fonts at 4.
        // To keep simple, let's just skip obj 4 in xref with a free entry.
        Write("0000000000 65535 f \n"); // obj 4 free
        for (var i = 0; i < entries.Length; i++)
            Write($"{fontOffsets[i]:D10} 00000 n \n");
        Write($"{streamObjOffset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {totalObjs} /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with text content and a link annotation overlapping the text area.
    /// </summary>
    public static byte[] BuildWithTextAndLink(byte[] contentBytes, string uri,
        double linkLlx = 0, double linkLly = 0, double linkUrx = 200, double linkUry = 800)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var fontOffset = ms.Position;
        Write("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        var actionOffset = ms.Position;
        Write($"7 0 obj\n<< /S /URI /URI ({uri}) >>\nendobj\n");

        var annotOffset = ms.Position;
        Write($"6 0 obj\n<< /Type /Annot /Subtype /Link /Rect [{linkLlx} {linkLly} {linkUrx} {linkUry}] /A 7 0 R >>\nendobj\n");

        var pageOffset = ms.Position;
        Write($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> /Annots [6 0 R] >>\nendobj\n");

        var streamObjOffset = ms.Position;
        Write($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        ms.Write(contentBytes);
        Write("\nendstream\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 8\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{streamObjOffset:D10} 00000 n \n");
        Write($"{fontOffset:D10} 00000 n \n");
        Write($"{annotOffset:D10} 00000 n \n");
        Write($"{actionOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with content that includes a horizontal rule (line spanning the page).
    /// </summary>
    public static byte[] BuildWithHorizontalRule()
    {
        // Content stream: draw a horizontal line from x=10 to x=600 at y=400
        var content = Encoding.ASCII.GetBytes("10 400 m 600 400 l S\nBT /F1 12 Tf 72 700 Td (Above the line) Tj ET\nBT /F1 12 Tf 72 300 Td (Below the line) Tj ET");
        return BuildWithTextContent(content);
    }

    /// <summary>
    /// Build a PDF with an embedded TrueType font (FontFile2) and text content.
    /// The font contains glyphs for ASCII printable characters (32-126).
    /// Returns a PDF that uses the embedded font with the given text string.
    /// </summary>
    public static byte[] BuildWithEmbeddedTrueTypeFont(string text)
    {
        var fontData = BuildMinimalTrueTypeFont();

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // FontFile2 stream (raw TrueType font)
        var fontFileOffset = ms.Position;
        Write($"6 0 obj\n<< /Length {fontData.Length} /Length1 {fontData.Length} >>\nstream\n");
        ms.Write(fontData);
        Write("\nendstream\nendobj\n");

        // Font descriptor
        var descriptorOffset = ms.Position;
        Write("7 0 obj\n<< /Type /FontDescriptor /FontName /TestFont /Flags 32 /FontBBox [0 -200 1000 800] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 6 0 R >>\nendobj\n");

        // Build widths array for chars 32-126
        var widthsSb = new StringBuilder("[");
        for (var i = 32; i <= 126; i++)
            widthsSb.Append("500 ");
        widthsSb.Append(']');

        // Font dictionary (TrueType with embedded data)
        var fontOffset = ms.Position;
        Write($"5 0 obj\n<< /Type /Font /Subtype /TrueType /BaseFont /TestFont /FirstChar 32 /LastChar 126 /Widths {widthsSb} /FontDescriptor 7 0 R /Encoding /WinAnsiEncoding >>\nendobj\n");

        // Content stream
        var contentStr = $"BT /F1 12 Tf 72 700 Td ({EscapePdfString(text)}) Tj ET";
        var contentBytes = Encoding.ASCII.GetBytes(contentStr);
        var contentOffset = ms.Position;
        Write($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        ms.Write(contentBytes);
        Write("\nendstream\nendobj\n");

        // Page
        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 8\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{contentOffset:D10} 00000 n \n");
        Write($"{fontOffset:D10} 00000 n \n");
        Write($"{fontFileOffset:D10} 00000 n \n");
        Write($"{descriptorOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a minimal valid TrueType font that maps ASCII printable chars (32-126)
    /// to simple rectangular glyphs. This is sufficient for PDF embedding tests.
    /// </summary>
    internal static byte[] BuildMinimalTrueTypeFont()
    {
        // We need these tables: head, hhea, maxp, OS/2, name, cmap, post, glyf, loca, hmtx
        var tables = new Dictionary<string, byte[]>();

        var numGlyphs = 96; // .notdef + chars 32-126
        var unitsPerEm = 1000;

        // head table (54 bytes)
        var head = new byte[54];
        WriteUInt32(head, 0, 0x00010000); // version
        WriteUInt32(head, 4, 0x00005000); // fontRevision
        WriteUInt32(head, 8, 0);          // checksumAdjust
        WriteUInt32(head, 12, 0x5F0F3CF5); // magicNumber
        WriteUInt16(head, 16, 0x000B);    // flags
        WriteUInt16(head, 18, (ushort)unitsPerEm); // unitsPerEm
        // created/modified (8 bytes each) - zeros
        WriteInt16(head, 36, 0);     // xMin
        WriteInt16(head, 38, -200);  // yMin
        WriteInt16(head, 40, 500);   // xMax
        WriteInt16(head, 42, 800);   // yMax
        WriteUInt16(head, 44, 0);    // macStyle
        WriteUInt16(head, 46, 8);    // lowestRecPPEM
        WriteInt16(head, 48, 2);     // fontDirectionHint
        WriteInt16(head, 50, 1);     // indexToLocFormat (long)
        WriteInt16(head, 52, 0);     // glyphDataFormat
        tables["head"] = head;

        // hhea table (36 bytes)
        var hhea = new byte[36];
        WriteUInt32(hhea, 0, 0x00010000); // version
        WriteInt16(hhea, 4, 800);   // ascent
        WriteInt16(hhea, 6, -200);  // descent
        WriteInt16(hhea, 8, 0);     // lineGap
        WriteUInt16(hhea, 10, 500); // advanceWidthMax
        WriteInt16(hhea, 12, 0);    // minLeftSideBearing
        WriteInt16(hhea, 14, 0);    // minRightSideBearing
        WriteInt16(hhea, 16, 500);  // xMaxExtent
        WriteInt16(hhea, 18, 1);    // caretSlopeRise
        WriteInt16(hhea, 20, 0);    // caretSlopeRun
        // 10 bytes reserved (zeros)
        WriteInt16(hhea, 32, 0);    // metricDataFormat
        WriteUInt16(hhea, 34, (ushort)numGlyphs); // numOfLongHorMetrics
        tables["hhea"] = hhea;

        // maxp table (32 bytes for TrueType)
        var maxp = new byte[32];
        WriteUInt32(maxp, 0, 0x00010000); // version
        WriteUInt16(maxp, 4, (ushort)numGlyphs);
        WriteUInt16(maxp, 6, 4);  // maxPoints
        WriteUInt16(maxp, 8, 1);  // maxContours
        WriteUInt16(maxp, 10, 0); // maxCompositePoints
        WriteUInt16(maxp, 12, 0); // maxCompositeContours
        WriteUInt16(maxp, 14, 1); // maxZones
        WriteUInt16(maxp, 16, 0); // maxTwilightPoints
        WriteUInt16(maxp, 18, 0); // maxStorage
        WriteUInt16(maxp, 20, 0); // maxFunctionDefs
        WriteUInt16(maxp, 22, 0); // maxInstructionDefs
        WriteUInt16(maxp, 24, 0); // maxStackElements
        WriteUInt16(maxp, 26, 0); // maxSizeOfInstructions
        WriteUInt16(maxp, 28, 0); // maxComponentElements
        WriteUInt16(maxp, 30, 0); // maxComponentDepth
        tables["maxp"] = maxp;

        // post table (32 bytes, format 3 = no glyph names)
        var post = new byte[32];
        WriteUInt32(post, 0, 0x00030000); // format 3.0
        WriteUInt32(post, 4, 0);          // italicAngle (Fixed 0.0)
        WriteInt16(post, 8, -100);        // underlinePosition
        WriteInt16(post, 10, 50);         // underlineThickness
        WriteUInt32(post, 12, 0);         // isFixedPitch (0 = no)
        tables["post"] = post;

        // Build glyf table: .notdef (empty) + simple rectangle for each glyph
        // Simple glyph: 4 points, 1 contour, rectangle 0,0 - 500,700
        var simpleGlyph = BuildSimpleRectGlyph(0, 0, 500, 700);
        var emptyGlyph = new byte[0]; // .notdef = empty

        using var glyfMs = new MemoryStream();
        var locaOffsets = new int[numGlyphs + 1];

        // Glyph 0 (.notdef) - empty
        locaOffsets[0] = (int)glyfMs.Position;
        glyfMs.Write(emptyGlyph);
        // Pad to 4 bytes
        while (glyfMs.Position % 4 != 0) glyfMs.WriteByte(0);

        // Glyphs 1-95 (chars 32-126)
        for (var i = 1; i < numGlyphs; i++)
        {
            locaOffsets[i] = (int)glyfMs.Position;
            glyfMs.Write(simpleGlyph);
            while (glyfMs.Position % 4 != 0) glyfMs.WriteByte(0);
        }
        locaOffsets[numGlyphs] = (int)glyfMs.Position;

        tables["glyf"] = glyfMs.ToArray();

        // loca table (long format)
        var loca = new byte[(numGlyphs + 1) * 4];
        for (var i = 0; i <= numGlyphs; i++)
            WriteUInt32(loca, i * 4, (uint)locaOffsets[i]);
        tables["loca"] = loca;

        // hmtx table
        var hmtx = new byte[numGlyphs * 4];
        for (var i = 0; i < numGlyphs; i++)
        {
            WriteUInt16(hmtx, i * 4, 500);  // advanceWidth
            WriteInt16(hmtx, i * 4 + 2, 0); // lsb
        }
        tables["hmtx"] = hmtx;

        // cmap table: format 4, mapping chars 32-126 to glyphs 1-95
        tables["cmap"] = BuildCmapTable(32, 126);

        // OS/2 table (78 bytes minimum)
        var os2 = new byte[96];
        WriteUInt16(os2, 0, 4);      // version
        WriteInt16(os2, 2, 500);     // xAvgCharWidth
        WriteUInt16(os2, 4, 400);    // usWeightClass
        WriteUInt16(os2, 6, 5);      // usWidthClass
        WriteInt16(os2, 68, 800);    // sTypoAscender
        WriteInt16(os2, 70, -200);   // sTypoDescender
        WriteInt16(os2, 72, 0);      // sTypoLineGap
        WriteUInt16(os2, 74, 800);   // usWinAscent
        WriteUInt16(os2, 76, 200);   // usWinDescent
        // version 2 fields
        WriteInt16(os2, 86, 500);    // sxHeight
        WriteInt16(os2, 88, 700);    // sCapHeight
        tables["OS/2"] = os2;

        // name table (minimal)
        tables["name"] = BuildNameTable("TestFont");

        return AssembleTrueTypeFont(tables);
    }

    private static byte[] BuildSimpleRectGlyph(int xMin, int yMin, int xMax, int yMax)
    {
        // Simple glyph with 1 contour, 4 points (rectangle)
        using var ms = new MemoryStream();

        // Header
        WriteInt16(ms, 1);       // numberOfContours
        WriteInt16(ms, (short)xMin);
        WriteInt16(ms, (short)yMin);
        WriteInt16(ms, (short)xMax);
        WriteInt16(ms, (short)yMax);

        // endPtsOfContours
        WriteUInt16(ms, 3);      // last point index = 3 (4 points)

        // instructionLength
        WriteUInt16(ms, 0);

        // flags (4 points, all on-curve, x and y are short)
        // flag: 0x01 (on-curve) | 0x02 (x-short) | 0x10 (x-positive) | 0x04 (y-short) | 0x20 (y-positive)
        ms.WriteByte(0x01 | 0x02 | 0x10 | 0x04 | 0x20); // (xMin, yMin) - both positive short
        ms.WriteByte(0x01 | 0x02 | 0x10 | 0x04);          // (xMax, yMin) - x positive, y = 0 (same)
        ms.WriteByte(0x01 | 0x02 | 0x04 | 0x20);          // (xMax, yMax) - x = 0 (same), y positive
        ms.WriteByte(0x01 | 0x02 | 0x04);                  // (xMin, yMax) - x negative, y = 0 (same)

        // x-coordinates (short, delta from previous)
        ms.WriteByte((byte)xMin);  // point 0: xMin
        ms.WriteByte((byte)(xMax - xMin)); // point 1: delta = xMax - xMin
        ms.WriteByte(0);                    // point 2: delta = 0
        ms.WriteByte((byte)(xMax - xMin)); // point 3: delta = -(xMax-xMin) but as positive with negative flag

        // y-coordinates (short, delta from previous)
        ms.WriteByte((byte)yMin);           // point 0: yMin
        ms.WriteByte(0);                     // point 1: delta = 0
        ms.WriteByte((byte)(yMax - yMin));  // point 2: delta = yMax - yMin
        ms.WriteByte(0);                     // point 3: delta = 0

        return ms.ToArray();
    }

    private static byte[] BuildCmapTable(int firstChar, int lastChar)
    {
        // Build cmap header + format 4 subtable
        var segCount = 2; // one segment for our range + sentinel

        // Format 4: 14 header + segCount*2 endCode + 2 reservedPad + segCount*2 startCode
        //           + segCount*2 idDelta + segCount*2 idRangeOffset
        var subtableLen = 14 + segCount * 8 + 2; // +2 for reservedPad
        var cmapLen = 4 + 8 + subtableLen;   // header + 1 encoding record + subtable

        var cmap = new byte[cmapLen];
        var o = 0;

        // Header
        WriteUInt16(cmap, o, 0); o += 2; // version
        WriteUInt16(cmap, o, 1); o += 2; // numTables

        // Encoding record: platform 3, encoding 1 (Windows Unicode BMP)
        WriteUInt16(cmap, o, 3); o += 2;  // platformID
        WriteUInt16(cmap, o, 1); o += 2;  // encodingID
        WriteUInt32(cmap, o, 12); o += 4; // offset to subtable

        // Format 4 subtable
        var searchRange = 4; // 2 * 2^floor(log2(2))
        WriteUInt16(cmap, o, 4); o += 2;               // format
        WriteUInt16(cmap, o, (ushort)subtableLen); o += 2; // length
        WriteUInt16(cmap, o, 0); o += 2;               // language
        WriteUInt16(cmap, o, (ushort)(segCount * 2)); o += 2; // segCountX2
        WriteUInt16(cmap, o, (ushort)searchRange); o += 2;
        WriteUInt16(cmap, o, 1); o += 2;               // entrySelector
        WriteUInt16(cmap, o, (ushort)(segCount * 2 - searchRange)); o += 2; // rangeShift

        // endCode array
        WriteUInt16(cmap, o, (ushort)lastChar); o += 2;
        WriteUInt16(cmap, o, 0xFFFF); o += 2;

        // reservedPad
        WriteUInt16(cmap, o, 0); o += 2;

        // startCode array
        WriteUInt16(cmap, o, (ushort)firstChar); o += 2;
        WriteUInt16(cmap, o, 0xFFFF); o += 2;

        // idDelta array: glyph = charCode - firstChar + 1
        var delta = (short)(1 - firstChar);
        WriteInt16(cmap, o, delta); o += 2;
        WriteUInt16(cmap, o, 1); o += 2; // sentinel delta

        // idRangeOffset array
        WriteUInt16(cmap, o, 0); o += 2;
        WriteUInt16(cmap, o, 0); o += 2;

        return cmap;
    }

    private static byte[] BuildNameTable(string fontName)
    {
        var nameBytes = Encoding.BigEndianUnicode.GetBytes(fontName);
        // 1 name record (nameID 6 = PostScript name)
        var table = new byte[6 + 12 + nameBytes.Length];
        var o = 0;

        WriteUInt16(table, o, 0); o += 2; // format
        WriteUInt16(table, o, 1); o += 2; // count
        WriteUInt16(table, o, (ushort)(6 + 12)); o += 2; // stringOffset

        // Record: platform 3, encoding 1, language 0x0409, nameID 6
        WriteUInt16(table, o, 3); o += 2;                 // platformID
        WriteUInt16(table, o, 1); o += 2;                 // encodingID
        WriteUInt16(table, o, 0x0409); o += 2;            // languageID
        WriteUInt16(table, o, 6); o += 2;                 // nameID (PostScript name)
        WriteUInt16(table, o, (ushort)nameBytes.Length); o += 2; // length
        WriteUInt16(table, o, 0); o += 2;                 // offset

        Array.Copy(nameBytes, 0, table, o, nameBytes.Length);
        return table;
    }

    private static byte[] AssembleTrueTypeFont(Dictionary<string, byte[]> tables)
    {
        var numTables = tables.Count;
        var entrySelector = (int)Math.Floor(Math.Log2(numTables));
        var searchRange = (int)Math.Pow(2, entrySelector) * 16;
        var rangeShift = numTables * 16 - searchRange;

        var headerSize = 12 + numTables * 16;
        var paddedSizes = new Dictionary<string, int>();
        foreach (var (tag, data) in tables)
            paddedSizes[tag] = (data.Length + 3) & ~3;

        var totalSize = headerSize;
        foreach (var size in paddedSizes.Values)
            totalSize += size;

        var result = new byte[totalSize];

        WriteUInt32(result, 0, 0x00010000); // sfVersion
        WriteUInt16(result, 4, (ushort)numTables);
        WriteUInt16(result, 6, (ushort)searchRange);
        WriteUInt16(result, 8, (ushort)entrySelector);
        WriteUInt16(result, 10, (ushort)rangeShift);

        var sortedTags = tables.Keys.OrderBy(t => t).ToList();
        var tableIdx = 0;
        var currentOffset = headerSize;

        foreach (var tag in sortedTags)
        {
            var data = tables[tag];
            var dirOffset = 12 + tableIdx * 16;

            var tagBytes = Encoding.ASCII.GetBytes(tag.PadRight(4));
            Array.Copy(tagBytes, 0, result, dirOffset, 4);

            // Checksum (simplified)
            uint checksum = 0;
            var nLongs = (data.Length + 3) / 4;
            for (var i = 0; i < nLongs; i++)
            {
                var off = i * 4;
                uint val = 0;
                for (var j = 0; j < 4 && off + j < data.Length; j++)
                    val = (val << 8) | data[off + j];
                checksum += val;
            }
            WriteUInt32(result, dirOffset + 4, checksum);
            WriteUInt32(result, dirOffset + 8, (uint)currentOffset);
            WriteUInt32(result, dirOffset + 12, (uint)data.Length);

            Array.Copy(data, 0, result, currentOffset, data.Length);
            currentOffset += paddedSizes[tag];
            tableIdx++;
        }

        return result;
    }

    private static string EscapePdfString(string text)
    {
        return text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)(value & 0xFF);
    }

    private static void WriteInt16(byte[] data, int offset, short value)
    {
        data[offset] = (byte)((ushort)value >> 8);
        data[offset + 1] = (byte)(value & 0xFF);
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)(value & 0xFF);
    }

    private static void WriteUInt16(MemoryStream ms, ushort value)
    {
        ms.WriteByte((byte)(value >> 8));
        ms.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteInt16(MemoryStream ms, short value)
    {
        ms.WriteByte((byte)((ushort)value >> 8));
        ms.WriteByte((byte)(value & 0xFF));
    }

    /// <summary>
    /// Build a PDF with an uncompressed RGB image XObject.
    /// </summary>
    public static byte[] BuildWithUncompressedImage(int width, int height)
    {
        // Create raw RGB pixel data (red gradient)
        var pixelData = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var idx = (y * width + x) * 3;
            pixelData[idx] = (byte)(x * 255 / Math.Max(width - 1, 1));     // R
            pixelData[idx + 1] = (byte)(y * 255 / Math.Max(height - 1, 1)); // G
            pixelData[idx + 2] = 128;                                        // B
        }

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // Image XObject (uncompressed)
        var imgOffset = ms.Position;
        Write($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length {pixelData.Length} >>\nstream\n");
        ms.Write(pixelData);
        Write("\nendstream\nendobj\n");

        // Content stream that draws the image
        var contentBytes = Encoding.ASCII.GetBytes($"q {width} 0 0 {height} 100 500 cm /Im1 Do Q");
        var contentOffset = ms.Position;
        Write($"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        ms.Write(contentBytes);
        Write("\nendstream\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /XObject << /Im1 4 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{imgOffset:D10} 00000 n \n");
        Write($"{contentOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a named image XObject resource and content stream that draws it.
    /// The image is a simple solid-color block for differentiation.
    /// </summary>
    public static byte[] BuildWithNamedImage(string xObjectName, byte fillR, byte fillG, byte fillB,
        int width = 4, int height = 4)
    {
        // Create raw RGB pixel data (solid color)
        var pixelData = new byte[width * height * 3];
        for (var i = 0; i < width * height; i++)
        {
            pixelData[i * 3] = fillR;
            pixelData[i * 3 + 1] = fillG;
            pixelData[i * 3 + 2] = fillB;
        }

        var contentText = $"q {width} 0 0 {height} 100 500 cm /{xObjectName} Do Q";
        var contentBytes = Encoding.ASCII.GetBytes(contentText);

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // Image XObject
        var imgOffset = ms.Position;
        Write($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length {pixelData.Length} >>\nstream\n");
        ms.Write(pixelData);
        Write("\nendstream\nendobj\n");

        // Content stream
        var contentOffset = ms.Position;
        Write($"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        ms.Write(contentBytes);
        Write("\nendstream\nendobj\n");

        var pageOffset = ms.Position;
        Write($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /XObject << /{xObjectName} 4 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{imgOffset:D10} 00000 n \n");
        Write($"{contentOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a named font resource and content stream that uses it.
    /// </summary>
    public static byte[] BuildWithNamedFont(string fontName, string baseFont, string text)
    {
        var contentText = $"BT /{fontName} 12 Tf ({text}) Tj ET";
        var contentBytes = Encoding.ASCII.GetBytes(contentText);

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // Font
        var fontOffset = ms.Position;
        Write($"4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont} /Encoding /WinAnsiEncoding >>\nendobj\n");

        // Content stream
        var contentObjOffset = ms.Position;
        Write($"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        ms.Write(contentBytes);
        Write("\nendstream\nendobj\n");

        var pageOffset = ms.Position;
        Write($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /{fontName} 4 0 R >> >> >>\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{fontOffset:D10} 00000 n \n");
        Write($"{contentObjOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with a font that has a custom encoding dictionary (with optional Differences array).
    /// The fontDictExtra is inserted into the font object raw (e.g. "/Encoding << /BaseEncoding /WinAnsiEncoding /Differences [32 /space /exclam] >>").
    /// </summary>
    public static byte[] BuildWithFontDict(byte[] contentBytes, string fontDictBody)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var fontOffset = ms.Position;
        Write($"5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica {fontDictBody} >>\nendobj\n");

        var pageOffset = ms.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");

        var streamObjOffset = ms.Position;
        Write($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        ms.Write(contentBytes);
        Write("\nendstream\nendobj\n");

        var xrefOffset = ms.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write($"{streamObjOffset:D10} 00000 n \n");
        Write($"{fontOffset:D10} 00000 n \n");
        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }

    /// <summary>
    /// Build a PDF with multiple fonts, each with its own custom font dict body.
    /// Each entry is (resourceKey, fontDictBody, contentBytes).
    /// </summary>
    public static byte[] BuildWithMultipleFontDicts(
        params (string resourceKey, string fontDictBody, byte[] content)[] entries)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");

        var catalogOffset = ms.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        var pagesOffset = ms.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        var fontObjStart = 5;
        var fontOffsets = new long[entries.Length];
        var fontResEntries = new StringBuilder();
        for (var i = 0; i < entries.Length; i++)
        {
            var objNum = fontObjStart + i;
            fontOffsets[i] = ms.Position;
            Write($"{objNum} 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica {entries[i].fontDictBody} >>\nendobj\n");
            fontResEntries.Append($" /{entries[i].resourceKey} {objNum} 0 R");
        }

        var allContent = entries.SelectMany(e => e.content.Concat(new byte[] { (byte)'\n' })).ToArray();

        var contentObjNum = fontObjStart + entries.Length;
        var pageOffset = ms.Position;
        Write($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {contentObjNum} 0 R /Resources << /Font <<{fontResEntries}>> >> >>\nendobj\n");

        var streamObjOffset = ms.Position;
        Write($"{contentObjNum} 0 obj\n<< /Length {allContent.Length} >>\nstream\n");
        ms.Write(allContent);
        Write("\nendstream\nendobj\n");

        var totalObjs = contentObjNum + 1;
        var xrefOffset = ms.Position;
        Write($"xref\n0 {totalObjs}\n");
        Write("0000000000 65535 f \n");
        Write($"{catalogOffset:D10} 00000 n \n");
        Write($"{pagesOffset:D10} 00000 n \n");
        Write($"{pageOffset:D10} 00000 n \n");
        Write("0000000000 65535 f \n"); // obj 4 free
        for (var i = 0; i < entries.Length; i++)
            Write($"{fontOffsets[i]:D10} 00000 n \n");
        Write($"{streamObjOffset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {totalObjs} /Root 1 0 R >>\nstartxref\n");
        Write($"{xrefOffset}\n%%EOF\n");

        return ms.ToArray();
    }
}
