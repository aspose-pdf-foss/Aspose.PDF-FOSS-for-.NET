using Aspose.Pdf.Devices;
using Aspose.Pdf.Vector;

namespace Aspose.Pdf;

/// <summary>Aspose.PDF for .NET-shape additions to <see cref="Page"/> — every method
/// either delegates to a real working pipeline or throws
/// NotSupportedException with a clear message about the missing capability.</summary>
public sealed partial class Page
{
    /// <summary>Event payload — fired once per page just before the
    /// document writer serialises this page.</summary>
    public delegate void BeforePageGenerate(Page page);

    /// <summary>Fired by <see cref="Document.ToArray()"/> immediately before the
    /// writer serialises each page. Subscribers can mutate this page's
    /// dictionary, add annotations, etc. before the bytes are written.</summary>
    public event BeforePageGenerate? OnBeforePageGenerate;

    /// <summary>Internal hook called by <see cref="Document"/>'s save pipeline.
    /// Public mutability isn't exposed — the event slot is reflection-only
    /// via the Aspose.PDF for .NET-shape <c>event</c> declaration.</summary>
    internal void RaiseBeforePageGenerate() => OnBeforePageGenerate?.Invoke(this);

    /// <summary>Render this page through a <see cref="PageDevice"/> to a stream.</summary>
    public void SendTo(PageDevice device, Stream output)
    {
        if (device is null) throw new System.ArgumentNullException(nameof(device));
        device.Process(this, output);
    }

    /// <summary>Render this page through a <see cref="PageDevice"/> to a file.</summary>
    public void SendTo(PageDevice device, string outputFileName)
    {
        if (device is null) throw new System.ArgumentNullException(nameof(device));
        device.Process(this, outputFileName);
    }

    /// <summary>Apply <paramref name="stamp"/> via its
    /// <see cref="Stamp.Put(Page)"/> override.</summary>
    public void AddStamp(Stamp stamp)
    {
        if (stamp is null) return;
        stamp.Put(this);
    }

    /// <summary>Vector-element emission is not implemented in this FOSS
    /// branch — the renderer doesn't yet consume <see cref="GraphicElementCollection"/>.
    /// Throws so callers don't silently get no output.</summary>
    public void AddGraphics(GraphicElementCollection elements, Rectangle rectangle)
    {
        _ = elements; _ = rectangle;
        throw new System.NotSupportedException(
            "Page.AddGraphics is not implemented in this FOSS branch — vector-element emission requires a path-builder pass that hasn't been wired into the renderer.");
    }

    /// <summary>Same — vector-element removal isn't wired.</summary>
    public void DeleteGraphics(GraphicElementCollection elementsToDelete)
    {
        _ = elementsToDelete;
        throw new System.NotSupportedException(
            "Page.DeleteGraphics is not implemented in this FOSS branch — graphic-element tracking requires the vector pipeline that hasn't landed yet.");
    }

    private System.Collections.Generic.List<Layer>? _layerFacades;

    /// <summary>The page's layers (Optional Content Groups). Each entry is a
    /// <see cref="Layer"/> bound to the underlying OCG, so visibility, lock,
    /// delete and flatten changes round-trip through <see cref="OcgLayers"/> and
    /// survive a save. Adding a freshly-constructed <see cref="Layer"/>
    /// (<c>page.Layers.Add(layer)</c>) authors it onto the page — the OCG is
    /// registered and its <see cref="Layer.Contents"/> injected — when the
    /// document is saved.</summary>
    public System.Collections.Generic.List<Layer> Layers
    {
        get
        {
            if (_layerFacades is null)
            {
                _layerFacades = new System.Collections.Generic.List<Layer>();
                foreach (var group in OcgLayers)
                    _layerFacades.Add(new Layer(group, _layerFacades));
            }
            return _layerFacades;
        }
        set
        {
            // Replace the in-memory facade list. New (detached) layers in it are
            // authored onto the page on save; entries are otherwise OCG-backed.
            _layerFacades = value ?? new System.Collections.Generic.List<Layer>();
        }
    }

    /// <summary>Author any detached layers that were added through
    /// <see cref="Layers"/> onto the page (register the OCG, inject content),
    /// then bind them. Called automatically before the document is saved.</summary>
    internal void FlushPendingLayers()
    {
        if (_layerFacades is null) return;
        foreach (var layer in _layerFacades)
        {
            if (layer is null || layer.IsBound) continue;
            var ocg = new OptionalContentGroup(layer.Id ?? string.Empty, layer.Name ?? string.Empty)
            {
                IsVisible = layer.PendingDefaultState == DefaultState.Visible,
                IsLocked = layer.PendingLocked,
            };
            foreach (var op in layer.Contents)
                ocg.AddContent(op);
            OcgLayers.Add(ocg);
            layer.BindTo(ocg, _layerFacades);
        }
    }
}
