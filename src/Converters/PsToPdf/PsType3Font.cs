using System;
using System.Collections.Generic;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Builds the Type 3 font the converter writes for a face that is not one of the PDF
/// base-14. The shape follows the PS/EPS emission spec: a font matrix with a NEGATIVE
/// vertical term, so glyph outlines are stored y-down; a 256-entry Differences array
/// naming every code the PostScript encoding vector defines; and one char proc per
/// named glyph, opening with <c>d1</c> and filling its outline.
/// </summary>
internal static class PsType3Font
{
    /// <summary>The glyph-space em the font matrix divides by.</summary>
    private const double GlyphUnitsPerEm = 1000.0;

    /// <summary>Build the font dictionary, or null when the program yields no glyph
    /// the encoding asks for — a face that contributes nothing must not be written.</summary>
    public static PdfDictionary? Build(PsFontProgram program, PsArray? encoding)
    {
        if (program is null) return null;
        var names = NamesByCode(encoding);
        var procs = new PdfDictionary();
        var widths = new List<PdfObject>();
        var written = new Dictionary<string, bool>(StringComparer.Ordinal);
        var any = false;
        for (var code = 0; code < PsEncodings.CodeCount; code++)
        {
            var name = names[code];
            var width = program.WidthFor(name);
            widths.Add(new PdfInteger((long)Math.Round(width < 0 ? 0 : width)));
            if (width < 0 || written.ContainsKey(name)) continue;
            var stream = BuildCharProc(program, name, width);
            if (stream is null) continue;
            procs.Set(name, stream);
            written[name] = true;
            any = true;
        }

        if (!any) return null;
        AddNotDef(procs);
        return Assemble(procs, widths, names, program);
    }

    private static PdfDictionary Assemble(PdfDictionary procs, List<PdfObject> widths,
        string[] names, PsFontProgram program)
    {
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type3"));
        font.Set("FontMatrix", new PdfArray(new List<PdfObject>
        {
            new PdfReal(1 / GlyphUnitsPerEm), new PdfInteger(0), new PdfInteger(0),
            new PdfReal(-1 / GlyphUnitsPerEm), new PdfInteger(0), new PdfInteger(0),
        }));
        font.Set("FontBBox", new PdfArray(new List<PdfObject>
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(0), new PdfInteger(0),
        }));
        font.Set("CharProcs", procs);
        font.Set("Encoding", BuildEncoding(names));
        font.Set("FirstChar", new PdfInteger(0));
        font.Set("LastChar", new PdfInteger(PsEncodings.CodeCount - 1));
        font.Set("Widths", new PdfArray(widths));
        font.Set("Resources", BuildResources());
        return font;
    }

    private static PdfDictionary BuildResources()
    {
        var resources = new PdfDictionary();
        resources.Set("ProcSet", new PdfArray(new List<PdfObject>
        {
            new PdfName("PDF"), new PdfName("Text"), new PdfName("ImageC"),
        }));
        return resources;
    }

    /// <summary>The glyph name each code maps to, from the PostScript encoding vector
    /// where it defines one and from the standard vector otherwise.</summary>
    private static string[] NamesByCode(PsArray? encoding)
    {
        var names = new string[PsEncodings.CodeCount];
        for (var code = 0; code < names.Length; code++)
            names[code] = PsEncodings.Lookup(encoding, code);
        return names;
    }

    private static PdfDictionary BuildEncoding(string[] names)
    {
        var encoding = new PdfDictionary();
        encoding.Set("Type", new PdfName("Encoding"));
        var differences = new List<PdfObject> { new PdfInteger(0) };
        foreach (var name in names) differences.Add(new PdfName(name));
        encoding.Set("Differences", new PdfArray(differences));
        return encoding;
    }

    /// <summary>One char proc: the glyph metrics, then the outline, filled. The
    /// outline is stored y-down to match the font matrix's negative vertical term.</summary>
    private static PdfStream BuildCharProc(PsFontProgram program,
        string name, double width)
    {
        var outline = program.OutlineFor(name);
        var content = new StringBuilder();
        AppendMetrics(content, program, outline, width);
        if (outline != null)
        {
            PsGlyphOutline.AppendToContent(content, outline, program.ToPdfUnits, flipY: true);
            content.Append("f\n");
        }

        return NewStream(content.ToString());
    }

    /// <summary>The <c>d1</c> line: the advance, then the glyph box in the same y-down
    /// space the outline is written in, so the box's low and high edges swap.</summary>
    private static void AppendMetrics(StringBuilder content, PsFontProgram program,
        GlyphOutline? outline, double width)
    {
        var scale = program.ToPdfUnits;
        double loX = 0, loY = 0, hiX = 0, hiY = 0;
        if (outline != null)
        {
            loX = outline.XMin * scale;
            hiX = outline.XMax * scale;
            loY = -outline.YMax * scale;
            hiY = -outline.YMin * scale;
        }

        content.Append(PsPageWriter.N(width)).Append(" 0 ")
            .Append(PsPageWriter.N(loX)).Append(' ').Append(PsPageWriter.N(loY)).Append(' ')
            .Append(PsPageWriter.N(hiX)).Append(' ').Append(PsPageWriter.N(hiY))
            .Append(" d1\n");
    }

    /// <summary>Every Type 3 font needs a notdef proc; an empty one paints nothing,
    /// which is what a code with no glyph should do.</summary>
    private static void AddNotDef(PdfDictionary procs)
    {
        if (procs.Get(PsEncodings.NotDefined) is not null) return;
        procs.Set(PsEncodings.NotDefined, NewStream("0 0 0 0 0 0 d1\n"));
    }

    private static PdfStream NewStream(string content)
    {
        var dict = new PdfDictionary();
        var data = Compat.Latin1.GetBytes(content);
        dict.Set("Length", new PdfInteger(data.Length));
        return new PdfStream(dict, data);
    }
}
