using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf;

/// <summary>
/// Creates optional content groups (layers) in a PDF document.
/// Spec: PDF32000_2008 §8.11
/// </summary>
public sealed class OptionalContentBuilder
{
    private readonly Document _document;
    private readonly List<LayerEntry> _layers = [];

    public OptionalContentBuilder(Document document)
    {
        _document = document;
    }

    /// <summary>
    /// Add a new layer (Optional Content Group).
    /// Returns the object number of the OCG dictionary for use in content streams.
    /// </summary>
    public LayerEntry AddLayer(string name, bool visible = true)
    {
        var entry = new LayerEntry(name, visible);
        _layers.Add(entry);
        return entry;
    }

    /// <summary>
    /// Finalize and write the OCProperties to the document catalog.
    /// Must be called before saving.
    /// </summary>
    public void Build()
    {
        if (_layers.Count == 0) return;

        var ocgsArray = new PdfArray();
        var orderArray = new PdfArray();
        var offArray = new PdfArray();

        foreach (var layer in _layers)
        {
            // Create OCG dictionary
            var ocgDict = new PdfDictionary();
            ocgDict.Set("Type", new PdfName("OCG"));
            ocgDict.Set("Name", new PdfString(Encoding.Latin1.GetBytes(layer.Name)));

            var objNum = _document.AllocateObjectNumber();
            layer.ObjectNumber = objNum;
            _document.AddNewObject(objNum, ocgDict);

            var ocgRef = new PdfIndirectRef(objNum, 0);
            ocgsArray.Add(ocgRef);
            orderArray.Add(ocgRef);

            if (!layer.Visible)
                offArray.Add(ocgRef);
        }

        // Build /D (default configuration)
        var configDict = new PdfDictionary();
        configDict.Set("Order", orderArray);
        if (offArray.Count > 0)
            configDict.Set("OFF", offArray);

        // Build /OCProperties
        var ocProps = new PdfDictionary();
        ocProps.Set("OCGs", ocgsArray);
        ocProps.Set("D", configDict);

        _document.Catalog.Set("OCProperties", ocProps);
    }

    /// <summary>
    /// Get the BDC/EMC content stream operators to wrap content in a layer.
    /// </summary>
    public static string BeginLayer(LayerEntry layer) =>
        $"/OC /MC{layer.ObjectNumber} BDC\n";

    /// <summary>
    /// End marker for a layer content sequence.
    /// </summary>
    public static string EndLayer() => "EMC\n";
}

/// <summary>
/// Represents a layer being built by OptionalContentBuilder.
/// </summary>
public sealed class LayerEntry
{
    internal LayerEntry(string name, bool visible)
    {
        Name = name;
        Visible = visible;
    }

    /// <summary>The layer name.</summary>
    public string Name { get; }

    /// <summary>Whether the layer is initially visible.</summary>
    public bool Visible { get; }

    /// <summary>The allocated object number for the OCG dict (set during Build).</summary>
    internal int ObjectNumber { get; set; }
}
