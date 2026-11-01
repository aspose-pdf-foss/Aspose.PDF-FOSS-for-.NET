using Aspose.Pdf.Core;
using Aspose.Pdf.Optimization;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>ISO 19005-1 clause 6.1.13: a PDF/A-1 file has no optional content. When
    /// every layer is shown by default the catalog entry is all there is to remove: content
    /// of a layer no configuration lists is always shown, which is how the document already
    /// looks. A document that hides anything through its layers is left as it is, because
    /// taking the entry away would show what the author hid.</summary>
    private void RemoveOptionalContentForPartOne(PdfAConvertState pa)
    {
        if (_reader.ResolveDict(_reader.Catalog.Get("OCProperties")) is not { } properties) return;
        if (AnyLayerStartsHidden(properties) || pa.membershipHides || AnyAnnotationHiddenByMembership()) return;

        pa.options.ConversionLog.Add(new PdfAViolation
        {
            Rule = "OptionalContent",
            Clause = "6.1.13",
            Description = "The catalog entry OCProperties is prohibited",
        });
        _reader.Catalog.Remove("OCProperties");
    }

    /// <summary>True when the default configuration turns a layer off, or a layer's usage
    /// can turn it off on screen, on paper or on export.</summary>
    private bool AnyLayerStartsHidden(PdfDictionary properties)
    {
        if (_reader.ResolveDict(properties.Get("D")) is { } config)
        {
            if (_reader.Resolve(config.Get("BaseState")) is PdfName { Value: not "ON" }) return true;
            if (_reader.Resolve(config.Get("OFF")) is PdfArray { Count: > 0 }) return true;
        }
        if (_reader.Resolve(properties.Get("OCGs")) is not PdfArray groups) return false;
        foreach (var group in groups)
        {
            if (_reader.ResolveDict(_reader.ResolveDict(group)?.Get("Usage")) is not { } usage) continue;
            if (UsageStateIsOff(usage, "View", "ViewState") || UsageStateIsOff(usage, "Print", "PrintState")
                || UsageStateIsOff(usage, "Export", "ExportState")) return true;
        }
        return false;
    }

    private bool UsageStateIsOff(PdfDictionary usage, string category, string state) =>
        _reader.ResolveDict(usage.Get(category)) is { } entry
        && _reader.Resolve(entry.Get(state)) is PdfName { Value: "OFF" };

    /// <summary>True when an annotation of any page belongs to a membership dictionary that
    /// shows it only while a layer is off. What the pages draw through their resources was
    /// looked at by the walk over their images.</summary>
    private bool AnyAnnotationHiddenByMembership()
    {
        foreach (var page in Pages)
            if (_reader.Resolve(page.Dict.Get("Annots")) is PdfArray annots)
                foreach (var annot in annots)
                    if (ShownOnlyWhileALayerIsOff(_reader.ResolveDict(annot)?.Get("OC"))) return true;
        return false;
    }

    /// <summary>A membership dictionary with an off-policy or a visibility expression: its
    /// content can be hidden while every layer is on.</summary>
    private bool ShownOnlyWhileALayerIsOff(PdfObject? membership) =>
        _reader.ResolveDict(membership) is { } dict
        && (dict.ContainsKey("VE") || _reader.Resolve(dict.Get("P")) is PdfName { Value: "AllOff" or "AnyOff" });
}
