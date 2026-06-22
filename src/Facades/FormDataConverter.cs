using System.Data;
using System.IO;
using System.Xml;

namespace Aspose.Pdf.Facades;

/// <summary>Form-data source / destination kind consumed by
/// <see cref="FormDataConverter"/>.</summary>
public enum DataType
{
    FDF,
    XFDF,
    XML,
    PDF,
    OLEDB,
    ODBC,
}

/// <summary>
/// Bridges PDF form-data formats (FDF / XFDF / XML) with
/// <see cref="System.Data.DataTable"/> and direct stream-to-stream
/// transforms. Database paths (ODBC / OLEDB) are signature-only — the
/// FOSS build doesn't link against System.Data.OleDb / Odbc.
/// </summary>
public class FormDataConverter
{
    /// <summary>Default constructor.</summary>
    public FormDataConverter() { }

    /// <summary>Backing DataTable used by the database-import / -export
    /// paths and the stream-conversion paths that target tabular data.</summary>
    public DataTable Table { get; set; } = new DataTable();

    /// <summary>When true, importing into a database table clears existing
    /// rows first. Stored only.</summary>
    public bool ClearTableBeforeExport { get; set; }

    /// <summary>When true, missing AcroForm fields are created during
    /// import. Stored only.</summary>
    public bool CreateMissingField { get; set; }

    /// <summary>When true, a missing destination table is created on
    /// database export. Stored only.</summary>
    public bool CreateMissingTable { get; set; }

    /// <summary>When true, an existing destination table is dropped + recreated
    /// rather than appended to. Stored only.</summary>
    public bool ReplaceExistingTable { get; set; }

    // ── Stream-based transforms ─────────────────────────────────────────────

    /// <summary>Convert XML form data to FDF.</summary>
    public void ConvertXmlToFdf(Stream sourceXml, Stream destFdf)
    {
        if (sourceXml is null) throw new System.ArgumentNullException(nameof(sourceXml));
        if (destFdf is null) throw new System.ArgumentNullException(nameof(destFdf));
        var doc = new XmlDocument();
        if (sourceXml.CanSeek) sourceXml.Position = 0;
        doc.Load(sourceXml);
        var sw = new StreamWriter(destFdf, System.Text.Encoding.UTF8);
        sw.WriteLine("%FDF-1.2");
        sw.WriteLine("1 0 obj");
        sw.WriteLine("<< /FDF << /Fields [");
        foreach (XmlNode field in doc.DocumentElement?.ChildNodes ?? doc.ChildNodes)
        {
            if (field.NodeType != XmlNodeType.Element) continue;
            sw.WriteLine($"<< /T ({field.Name}) /V ({field.InnerText}) >>");
        }
        sw.WriteLine("] >> >>");
        sw.WriteLine("endobj");
        sw.WriteLine("trailer << /Root 1 0 R >>");
        sw.WriteLine("%%EOF");
        sw.Flush();
    }

    /// <summary>Convert FDF form data to XML.</summary>
    public void ConvertFdfToXml(Stream sourceFdf, Stream destXml)
    {
        if (sourceFdf is null) throw new System.ArgumentNullException(nameof(sourceFdf));
        if (destXml is null) throw new System.ArgumentNullException(nameof(destXml));
        if (sourceFdf.CanSeek) sourceFdf.Position = 0;
        using var sr = new StreamReader(sourceFdf, System.Text.Encoding.UTF8, leaveOpen: true);
        var text = sr.ReadToEnd();
        var sw = new StreamWriter(destXml, System.Text.Encoding.UTF8);
        sw.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sw.WriteLine("<fields>");
        // Quick-and-dirty extractor: walk every "/T (name) /V (value)" pair.
        var idx = 0;
        while ((idx = text.IndexOf("/T (", idx, System.StringComparison.Ordinal)) >= 0)
        {
            var nameStart = idx + 4;
            var nameEnd = text.IndexOf(')', nameStart);
            if (nameEnd < 0) break;
            var name = text.Substring(nameStart, nameEnd - nameStart);
            var vIdx = text.IndexOf("/V (", nameEnd, System.StringComparison.Ordinal);
            string? value = null;
            if (vIdx >= 0 && vIdx < text.IndexOf("/T (", nameEnd, System.StringComparison.Ordinal) + 4 || vIdx < 0)
            {
                if (vIdx >= 0)
                {
                    var valueStart = vIdx + 4;
                    var valueEnd = text.IndexOf(')', valueStart);
                    if (valueEnd >= 0) value = text.Substring(valueStart, valueEnd - valueStart);
                }
            }
            sw.WriteLine($"  <{System.Security.SecurityElement.Escape(name)}>{System.Security.SecurityElement.Escape(value ?? string.Empty)}</{System.Security.SecurityElement.Escape(name)}>");
            idx = nameEnd + 1;
        }
        sw.WriteLine("</fields>");
        sw.Flush();
    }

    /// <summary>Read form data from one or more streams into <see cref="Table"/>.
    /// Each entry in the source becomes a row; field names become columns.
    /// Currently handles FDF and XML source types; XFDF / PDF / database
    /// types are stored-only stubs.</summary>
    public void ConvertToDataTable(Stream[] sourceStreams, DataType sourceType)
    {
        if (sourceStreams is null) return;
        Table = new DataTable();
        foreach (var s in sourceStreams)
        {
            if (s is null) continue;
            if (sourceType == DataType.XML)
            {
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                ms.Position = 0;
                var doc = new XmlDocument();
                doc.Load(ms);
                var row = Table.NewRow();
                foreach (XmlNode field in doc.DocumentElement?.ChildNodes ?? doc.ChildNodes)
                {
                    if (field.NodeType != XmlNodeType.Element) continue;
                    if (!Table.Columns.Contains(field.Name)) Table.Columns.Add(field.Name);
                    row[field.Name] = field.InnerText;
                }
                Table.Rows.Add(row);
            }
        }
    }

    /// <summary>Serialize <see cref="Table"/>'s rows into the destination
    /// streams in the requested format (one row per stream when the array
    /// length matches; otherwise one stream gets everything).</summary>
    public void ConvertToStreams(Stream[] destStream, DataType destType)
    {
        if (destStream is null || destStream.Length == 0) return;
        for (var i = 0; i < destStream.Length; i++)
        {
            var s = destStream[i];
            if (s is null) continue;
            if (destType == DataType.XML)
            {
                var sw = new StreamWriter(s, System.Text.Encoding.UTF8);
                sw.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                sw.WriteLine("<fields>");
                if (i < Table.Rows.Count)
                {
                    var row = Table.Rows[i];
                    foreach (DataColumn c in Table.Columns)
                    {
                        sw.WriteLine($"  <{System.Security.SecurityElement.Escape(c.ColumnName)}>{System.Security.SecurityElement.Escape((row[c] ?? "").ToString() ?? string.Empty)}</{System.Security.SecurityElement.Escape(c.ColumnName)}>");
                    }
                }
                sw.WriteLine("</fields>");
                sw.Flush();
            }
        }
    }

    /// <summary>Public-API-typo'd alias for <see cref="ConvertToStreams"/>
    /// (yes, single 't'). Forwards to the correctly-named impl.</summary>
    public void ConverToStreams(Stream[] destStream, DataType destType)
        => ConvertToStreams(destStream, destType);

    // ── Database paths (signature-only stubs) ─────────────────────────────

    /// <summary>Export form data from a database connection into
    /// <see cref="Table"/>. Stored-only stub — the FOSS build doesn't link
    /// against System.Data.OleDb / System.Data.Odbc.</summary>
    public void ExportFromDataBase(string connectString, DataType dbType)
    {
        _ = connectString; _ = dbType;
        throw new System.NotImplementedException(
            "FormDataConverter.ExportFromDataBase is not implemented in FOSS (no OLEDB/ODBC dependency).");
    }

    /// <summary>Import <see cref="Table"/> into a database. See
    /// <see cref="ExportFromDataBase"/>.</summary>
    public void ImportIntoDataBase(string connectString, DataType dbType)
    {
        _ = connectString; _ = dbType;
        throw new System.NotImplementedException(
            "FormDataConverter.ImportIntoDataBase is not implemented in FOSS (no OLEDB/ODBC dependency).");
    }
}
