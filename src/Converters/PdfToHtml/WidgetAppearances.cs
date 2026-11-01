using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    // Each page's widget layer, built once per conversion: the font passes and the page render draw
    // the same one instead of each building the appearances again.
    private readonly Dictionary<PdfDictionary, (byte[] Content, PdfDictionary Resources)?> _widgetLayers = new(ReferenceEqualityComparer.Instance);

    /// <summary>The page's widget layer (see <see cref="WidgetAppearanceContent"/>), built on first use.</summary>
    private (byte[] Content, PdfDictionary Resources)? WidgetLayer(PdfDictionary pageDict, PdfReader reader)
    {
        if (!_widgetLayers.TryGetValue(pageDict, out var layer))
        {
            layer = WidgetAppearanceContent(pageDict, reader);
            _widgetLayers[pageDict] = layer;
        }
        return layer;
    }

    /// <summary>
    /// The page's form widgets as content: a stream that invokes each widget's appearance as a
    /// form XObject fitted into its rectangle (PDF 32000-1 §12.5.5), with the resources naming
    /// those forms, so the widgets go through the same rendering as the page's own forms. A
    /// field whose file carries no usable appearance is laid out from its value, as a viewer
    /// does. Null when the page shows no widget.
    /// </summary>
    private static (byte[] Content, PdfDictionary Resources)? WidgetAppearanceContent(PdfDictionary pageDict, PdfReader reader)
    {
        if (reader.Resolve(pageDict.Get("Annots")) is not PdfArray annots) return null;
        var forms = new PdfDictionary();
        var content = new StringBuilder();
        bool? needsAppearances = null;
        foreach (var item in annots)
        {
            var annot = reader.ResolveDict(item);
            if (annot is null || annot.GetName("Subtype") != "Widget") continue;
            var flags = (AnnotationFlags)annot.GetInt("F");
            if ((flags & (AnnotationFlags.Hidden | AnnotationFlags.NoView)) != 0) continue;
            needsAppearances ??= Forms.WidgetRenderAppearance.FormNeedsAppearances(reader);
            var form = Forms.WidgetRenderAppearance.Build(annot, reader, needsAppearances.Value)
                ?? AppearanceStreams.Normal(annot, reader);
            if (form is null) continue;
            var toRect = AppearanceStreams.FitToRect(annot, form, reader);
            if (toRect is null) continue;
            var name = $"Widget{forms.Count + 1}";
            forms.Set(name, form);
            content.Append("q ").Append(string.Join(" ", toRect.Select(F))).Append(" cm /").Append(name).Append(" Do Q\n");
        }
        if (forms.Count == 0) return null;
        var resources = new PdfDictionary();
        resources.Set("XObject", forms);
        return (Encoding.ASCII.GetBytes(content.ToString()), resources);
    }
}
