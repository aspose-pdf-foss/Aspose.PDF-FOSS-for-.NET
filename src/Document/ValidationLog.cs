using Aspose.Pdf.Optimization;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Serialise a validation or conversion result in the established log schema:
    /// <c>&lt;Compliance&gt;&lt;File&gt;…&lt;Problem Severity Clause Code Convertable&gt;</c>.
    /// PDF/UA-1 sorts its problems into the nineteen fixed sections a UA reader looks up by
    /// name; PDF/A and PDF/X keep the flatter layout their own readers expect.</summary>
    private void WriteValidationLogXml(TextWriter writer, PdfFormat format,
        PdfAValidationResult result, string operation = "Validation")
    {
        int pages;
        try { pages = Pages.Count; } catch { pages = 0; }
        writer.Write(
            $"<Compliance Name=\"Log\" Operation=\"{operation}\" Target=\"{EscapeXml(GetVersionString(format))}\">" +
            "<Version>1.0</Version>" +
            $"<Date>{DateTime.Now}</Date>" +
            $"<File Version=\"{EscapeXml(PdfVersion ?? string.Empty)}\" Name=\"{EscapeXml(Path.GetFileName(FileName ?? string.Empty))}\" Pages=\"{pages}\">");
        if (format == PdfFormat.PDF_UA_1) WriteUaSections(writer, result);
        else WritePdfASections(writer, result);
        writer.Write("</File></Compliance>");
    }

    /// <summary>One <c>&lt;Problem&gt;</c> element. The Clause names the ISO requirement and
    /// the Code the individual check inside it; a violation that carries no explicit code
    /// repeats its clause, which is what the single-code PDF/A families do.</summary>
    private static string Problem(PdfAViolation v)
    {
        var clause = v.Clause ?? PdfAClauseFor(v.Rule);
        var code = v.Code ?? clause;
        var page = v.PageNumber is int p ? $" Page=\"{p}\"" : "";
        var objId = v.ObjectId is not null ? $" ObjectID=\"{EscapeXml(v.ObjectId)}\"" : "";
        var severity = v.Severity switch
        {
            PdfAProblemSeverity.Warning => "Warning",
            PdfAProblemSeverity.NeedManualCheck => "Need manual check",
            _ => "Error",
        };
        // Convertable defaults to true — every regular violation class this validator
        // reports is either repaired structurally (fonts, metadata, OutputIntent,
        // version, file ID, xref form) or stripped under ConvertErrorAction.Delete.
        // Implementation-limit violations baked into the content mark themselves
        // unconvertable instead.
        var convertable = v.Convertable ? "True" : "False";
        return $"<Problem Severity=\"{severity}\" Clause=\"{clause}\" Code=\"{EscapeXml(code)}\"{objId} Convertable=\"{convertable}\"{page}>{EscapeXml(v.Description)}</Problem>";
    }

    /// <summary>The conformance clause a PDF/A violation belongs to when the check itself
    /// did not name one.</summary>
    private static string PdfAClauseFor(string rule) => rule switch
    {
        "FontCmap" => "7.21.4.2",
        "FontEmbedding" or "FontNotEmbedded" => "6.2.11.4",
        "MetadataPdfAId" or "MetadataPdfAConformance" or "Metadata" => "6.6.4",
        "TaggedPdf" or "StructureTree" => "6.7.3.3",
        "DocumentTitle" => "7.1",
        _ => "",
    };

    /// <summary>PDF/A and PDF/X layout: font problems nest under &lt;Fonts&gt;, whole-document
    /// refusals under &lt;Catalog&gt;, everything else sits directly under &lt;File&gt;
    /// alongside the empty section markers.</summary>
    private static void WritePdfASections(TextWriter writer, PdfAValidationResult result)
    {
        var fontProblems = new System.Text.StringBuilder();
        var catalogProblems = new System.Text.StringBuilder();
        var otherProblems = new System.Text.StringBuilder();
        foreach (var v in result.Violations)
        {
            if (v.Rule.StartsWith("Font", StringComparison.Ordinal)) fontProblems.Append(Problem(v));
            // Whole-document refusals live in the log's Catalog section (the shape
            // used for a signed-file refusal).
            else if (v.Rule == "SignedFile") catalogProblems.Append(Problem(v));
            else otherProblems.Append(Problem(v));
        }

        writer.Write(
            "<Security />" +
            (catalogProblems.Length > 0 ? $"<Catalog>{catalogProblems}</Catalog>" : "<Catalog />") +
            "<Header /><Annotations />" +
            (fontProblems.Length > 0 ? $"<Fonts>{fontProblems}</Fonts>" : "<Fonts />") +
            "<trailer />" + otherProblems +
            "<Metadata /><objects /><xObjects /><actions /><xmpmeta /><EmbeddedFiles />");
    }

    /// <summary>PDF/UA-1 layout: the nineteen sections in their fixed order, every one
    /// written whether or not it holds a problem.</summary>
    private static void WriteUaSections(TextWriter writer, PdfAValidationResult result)
    {
        var bySection = new Dictionary<string, System.Text.StringBuilder>(StringComparer.Ordinal);
        foreach (var v in result.Violations)
        {
            // A violation with no section of its own is a general document-level
            // problem — that is where the pre-vocabulary checks land.
            var section = v.Section ?? UaProblems.General;
            if (!bySection.TryGetValue(section, out var sb))
                bySection[section] = sb = new System.Text.StringBuilder();
            sb.Append(Problem(v));
        }

        foreach (var section in UaProblems.SectionOrder)
        {
            if (bySection.TryGetValue(section, out var sb))
                writer.Write($"<{section}>{sb}</{section}>");
            else
                writer.Write($"<{section} />");
        }
    }
}
