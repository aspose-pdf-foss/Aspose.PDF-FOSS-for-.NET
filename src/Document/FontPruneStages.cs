using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The stages of the font prune: the content scan, the font dictionary prune and the nested form prune.</summary>
    private void PruneNestedFormFonts(FontPruneState fp)
    {
        foreach (var name in fp.formNames)
        {
            var xstream = _reader.ResolveStream(fp.xobjects!.Get(name));
            if (xstream is null || xstream.Dict.GetName("Subtype") != "Form") continue;
            if (!fp.visitedForms.Add(xstream.Dict)) continue; // cycle / shared-form guard
            var formRes = _reader.ResolveDict(xstream.Dict.Get("Resources"));
            // Only prune a form's OWN /Font dict — a form inheriting the page's
            // resources shares that dict, handled at the page scope.
            if (formRes is not null && !ReferenceEquals(formRes, fp.resources))
            {
                var newForm = PruneFontsInScope(_reader.DecodeStream(xstream), formRes, fp.visitedForms);
                if (newForm is not null)
                {
                    xstream.Dict.Remove("Filter");
                    xstream.Dict.Remove("DecodeParms");
                    xstream.Dict.Set("Length", new PdfInteger(newForm.Length));
                    xstream.ReplaceData(newForm);
                }
            }
        }
    }

    /// <summary>The stages of the font prune: the content scan, the font dictionary prune and the nested form prune.</summary>
    private void PruneFontDictionary(FontPruneState fp)
    {
        var pruned = new List<string>();
        foreach (var key in fp.fontDict!.Keys!.ToList())
            if (!fp.usedFonts.Contains(key))
            {
                fp.fontDict.Remove(key);
                pruned.Add(key);
            }

        // Rename the replacement fonts (registered under an "AsRp…" key) to F0, F1, …,
        // avoiding collision with any surviving original font, and patch the content's
        // Tf operands to match.
        var survivors = fp.fontDict.Keys.ToList();
        var taken = new HashSet<string>(survivors.Where(k => !k.StartsWith("AsRp", StringComparison.Ordinal)),
            StringComparer.Ordinal);
        var renameMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var n = 0;
        foreach (var rk in survivors.Where(k => k.StartsWith("AsRp", StringComparison.Ordinal)))
        {
            string fn;
            do { fn = "F" + n++; } while (taken.Contains(fn));
            taken.Add(fn);
            renameMap[rk] = fn;
        }
        if (renameMap.Count > 0)
        {
            foreach (var (oldKey, newKey) in renameMap)
            {
                var val = fp.fontDict.Get(oldKey);
                fp.fontDict.Remove(oldKey);
                if (val is not null) fp.fontDict.Set(newKey, val);
            }
            // A pruned font is still SELECTED by the content: a `/F2 Tf` that shows
            // no text before the next Tf keeps its operator even though its resource
            // is gone, leaving a Tf pointing at nothing. Repoint those selections at
            // the replacement font so every Tf in the rewritten content names a font
            // that still exists.
            var replacement = renameMap.Values.OrderBy(v => v, StringComparer.Ordinal).First();
            foreach (var key in pruned)
                renameMap[key] = replacement;
            fp.rewritten = RepointTfNamesInContent(fp.content!, renameMap);
        }
    }

    /// <summary>The stages of the font prune: the content scan, the font dictionary prune and the nested form prune.</summary>
    private bool ScanContentForFonts(FontPruneState fp)
    {
        var token = fp.lexer.NextToken();
        if (token.Kind == IO.TokenKind.Eof) return false;
        switch (token.Kind)
        {
            case IO.TokenKind.Name when token.StringValue is { } n:
                fp.lastName = n;
                break;
            case IO.TokenKind.LiteralString:
            case IO.TokenKind.HexString:
                if (token.BytesValue is { Length: > 0 }) fp.sawGlyphs = true;
                break;
            case IO.TokenKind.Keyword:
                var kw = token.StringValue;
                if (kw == "BI") { SkipInlineImage(fp.lexer, fp.usedFonts); break; }
                if (kw == "Tf") fp.currentFont = fp.lastName;
                else if (kw == "Do" && fp.lastName is not null) fp.formNames.Add(fp.lastName);
                else if ((kw == "Tj" || kw == "TJ" || kw == "'" || kw == "\"")
                         && fp.sawGlyphs && fp.currentFont is not null)
                    fp.usedFonts.Add(fp.currentFont);
                fp.sawGlyphs = false; // operator boundary resets the operand scan
                break;
        }
        return true;
    }
}
