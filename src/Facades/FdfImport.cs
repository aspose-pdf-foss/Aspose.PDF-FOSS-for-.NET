using System;
using System.Collections.Generic;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

/// <summary>Object-level FDF import shared by <see cref="Form"/> (an FDF carrying
/// an /Annots array of full annotation dictionaries) and
/// <see cref="PdfAnnotationEditor"/> (an FDF whose /Fields array mixes field
/// values with annotation-shaped entries). The FDF file is parsed as a real PDF
/// object graph so appearance streams, popups and filespecs travel with their
/// annotations.</summary>
internal static class FdfImport
{
    /// <summary>Open FDF bytes as a PDF object graph and return the trailer
    /// /Root's /FDF dictionary with its reader. FDF ships without an xref —
    /// the reader's recovery scan carries it — and the %FDF header is patched
    /// to %PDF so the parser accepts the file.</summary>
    internal static (PdfReader reader, PdfDictionary fdf)? Open(byte[] fdfData)
    {
        if (fdfData is null || fdfData.Length < 8) return null;
        var data = fdfData;
        if (data[0] == (byte)'%' && data[1] == (byte)'F' && data[2] == (byte)'D' && data[3] == (byte)'F')
        {
            // "%FDF-1.2" → "%PDF-1.2": the two headers differ only in byte 1.
            data = (byte[])fdfData.Clone();
            data[1] = (byte)'P';
        }
        try
        {
            var reader = PdfReader.FromBytes(data);
            // Resolve the trailer /Root directly: an FDF catalog is
            // <</FDF<<…>>>> with no /Type and no /Pages, which the Catalog
            // property's PDF-catalog validation would reject.
            var root = reader.ResolveDict(reader.Trailer?.Get("Root"));
            var fdf = reader.ResolveDict(root?.Get("FDF"));
            // FDF ships without an xref, and the recovery scan drops /Root from a
            // trailer written across several lines (probed: the same file's objects
            // all resolve; only the trailer key is lost). The FDF catalog is the
            // dictionary carrying /FDF — find it by object number, catalog-first
            // convention makes it object 1 in practice.
            if (fdf is null)
            {
                for (var num = 1; num <= 64 && fdf is null; num++)
                    if (reader.Resolve(new PdfIndirectRef(num, 0)) is PdfDictionary cand)
                        fdf = reader.ResolveDict(cand.Get("FDF"));
            }
            if (fdf is null) return null;
            return (reader, fdf);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Import every /Annots entry onto the page its 0-based /Page
    /// addresses; an entry addressing a page the document does not have is
    /// DROPPED (a 3-page FDF imports into a 2-page document
    /// without its page-2 annotations). The whole referenced graph — popups,
    /// /AP appearance forms, filespec streams — clones once per FDF read via
    /// the document's shared clone map. Returns the number imported.</summary>
    internal static int ImportAnnots(Document doc, byte[] fdfData)
    {
        if (Open(fdfData) is not { } opened) return 0;
        var (reader, fdf) = opened;
        if (reader.Resolve(fdf.Get("Annots")) is not PdfArray annots || annots.Count == 0) return 0;
        var map = doc.GetSharedImportCloneMap(reader);
        var count = 0;
        foreach (var item in annots)
        {
            var src = reader.ResolveDict(item);
            if (src is null) continue;
            var pageIdx = (int)src.GetInt("Page"); // 0-based; absent reads 0
            if (pageIdx < 0 || pageIdx >= doc.Pages.Count) continue;
            var clone = doc.ImportDict(src, reader, map);
            // FDF-only addressing keys; a re-homed annotation must not carry them.
            clone.Remove("Page");
            clone.Remove("P");
            doc.Pages[pageIdx + 1].Annotations.AddImportedDict(clone);
            count++;
        }
        return count;
    }

    /// <summary>Import an FDF /Fields array the way the annotation
    /// editor does: an entry carrying /Subtype is a full annotation and lands on
    /// PAGE 1 (a fields array carries no page addressing); an entry without one
    /// is a field value — /V as string or name — set on the field its /T names
    /// (unknown names are ignored). Returns (annotations imported, values set).</summary>
    internal static (int annots, int values) ImportFieldsArray(Document doc, byte[] fdfData)
    {
        if (Open(fdfData) is not { } opened) return (0, 0);
        var (reader, fdf) = opened;
        if (reader.Resolve(fdf.Get("Fields")) is not PdfArray fields || fields.Count == 0)
            return (0, 0);
        var map = doc.GetSharedImportCloneMap(reader);
        var annots = 0;
        var values = 0;
        foreach (var item in fields)
        {
            var src = reader.ResolveDict(item);
            if (src is null) continue;
            if (src.GetName("Subtype") is not null)
            {
                if (doc.Pages.Count == 0) continue;
                var clone = doc.ImportDict(src, reader, map);
                clone.Remove("Page");
                clone.Remove("P");
                doc.Pages[1].Annotations.AddImportedDict(clone);
                annots++;
                continue;
            }
            var name = (reader.Resolve(src.Get("T")) as PdfString)?.ToText();
            if (string.IsNullOrEmpty(name)) continue;
            var valueObj = reader.Resolve(src.Get("V"));
            var value = valueObj switch
            {
                PdfString s => s.ToText(),
                PdfName n => n.Value,
                _ => null,
            };
            if (value is null) continue;
            foreach (Forms.Field f in doc.Form.Fields)
            {
                if (f.FullName != name && f.PartialName != name) continue;
                if (f is Forms.CheckboxField cb)
                {
                    if (cb.IsDeclaredOnState(value)) cb.Value = value;
                    else cb.Checked = false;
                }
                else
                {
                    f.Value = value;
                }
                values++;
                break;
            }
        }
        return (annots, values);
    }
}
