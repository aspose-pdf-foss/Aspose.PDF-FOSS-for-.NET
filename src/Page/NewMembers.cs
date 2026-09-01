using Aspose.Pdf.Devices;
using Aspose.Pdf.Vector;

namespace Aspose.Pdf;

/// <summary>Public-API-shape additions to <see cref="Page"/> — every method
/// either delegates to a real working pipeline or throws
/// NotSupportedException with a clear message about the missing capability.</summary>
public sealed partial class Page
{
    /// <summary>Event payload — fired once per page just before the
    /// document writer serialises this page.</summary>
    public delegate void BeforePageGenerate(Page page);

    /// <summary>Fired once per page immediately before the page is generated — that is,
    /// before the paragraph-layout pass renders it, so a handler that assigns
    /// <see cref="Header"/>/<see cref="Footer"/> shapes the page the layout produces.
    /// Subscribers may also mutate the page dictionary, add annotations, and so on.</summary>
    public event BeforePageGenerate? OnBeforePageGenerate;

    /// <summary>Whether this page has any <see cref="OnBeforePageGenerate"/> subscriber —
    /// a page the layout generates copies the originating page's, so a per-page header
    /// handler runs for the overflow pages too.</summary>
    internal bool HasBeforePageGenerate => OnBeforePageGenerate is not null;

    /// <summary>Copy <paramref name="source"/>'s subscribers onto this page.</summary>
    internal void CopyBeforePageGenerateFrom(Page source)
    {
        if (source.OnBeforePageGenerate is { } d) OnBeforePageGenerate += d;
    }

    /// <summary>Internal hook: fires the event once, whether the caller is the layout
    /// pass or the save pipeline behind it. Public mutability isn't exposed — the event
    /// slot is reflection-only via the public-API-shape <c>event</c> declaration.</summary>
    internal void RaiseBeforePageGenerate()
    {
        if (_beforeGenerateRaised) return;
        _beforeGenerateRaised = true;
        OnBeforePageGenerate?.Invoke(this);
    }

    private bool _beforeGenerateRaised;

    /// <summary>Take the state a provisional page collected when the flow broke to
    /// it: the handler already ran there (it is not raised again here), and its
    /// margins, header and footer are this page's.</summary>
    internal void AdoptPreparedPage(Page prepared)
    {
        CopyBeforePageGenerateFrom(prepared);
        _beforeGenerateRaised = true;
        InheritPageInfoFrom(prepared);
        Header ??= prepared.Header;
        Footer ??= prepared.Footer;
    }

    /// <summary>A page the layout generates inherits the originating page's
    /// PageInfo (size and margins, each side's authored flag kept) as its own
    /// copy — page 1's margins are kept when a handler re-margins
    /// page 2, and page 2's footer band still sits on page 1's bottom margin.</summary>
    internal void InheritPageInfoFrom(Page source)
    {
        if (source._pageInfoCache is not { } src) return;
        var mine = PageInfo;
        mine.InheritFrom(src);
    }

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

    /// <summary>Raw /Artifact … EMC blocks stamp APIs wrote into this page's content
    /// THIS session. A page the flow generates is a continuation of its source page
    /// and inherits the artifacts that page was LOADED with (five parsed
    /// artifacts repeat on the spill page) — but a stamp added through the API stays
    /// on the page it was stamped on (the PdfPageStamp artifact does not
    /// repeat on the table's spill page), so those blocks are excluded from the
    /// continuation copy.</summary>
    internal System.Collections.Generic.List<byte[]> SessionStampBlocks { get; } = new();

    /// <summary>Append the given vector elements (typically produced by
    /// <see cref="Aspose.Pdf.Vector.GraphicsAbsorber"/>) to this page's content
    /// stream, reproducing each element's geometry in page space.</summary>
    public void AddGraphics(GraphicElementCollection elements)
    {
        if (elements is null || elements.Count == 0) return;
        var sb = new System.Text.StringBuilder();
        foreach (var element in elements)
            sb.Append(ReplayElement(element));
        if (sb.Length == 0) return;
        AddContentStream(System.Text.Encoding.ASCII.GetBytes(sb.ToString()));
    }

    /// <summary>Append vector elements; <paramref name="rectangle"/> is advisory
    /// (the elements are emitted in full, carrying their own geometry).</summary>
    public void AddGraphics(GraphicElementCollection elements, Rectangle rectangle)
    {
        _ = rectangle;
        AddGraphics(elements);
    }

    /// <summary>Remove the given absorbed elements from their source page: the
    /// content is rewritten without the elements' own operators; text and other
    /// non-vector content stays.</summary>
    public void DeleteGraphics(GraphicElementCollection elementsToDelete)
    {
        if (elementsToDelete is null) return;
        // Batch: a bulk delete rewrites the source page ONCE at the end, not per
        // element — each un-suppressed Remove() would re-serialize the whole
        // content stream, turning a large page into an O(n²) rewrite.
        Vector.GraphicsEditState? state = null;
        foreach (var element in elementsToDelete)
        {
            if (element.SourceEditState is { IsSuppressed: false } s) { state = s; break; }
        }
        state?.Suppress();
        foreach (var element in elementsToDelete)
            element.Remove();
        state?.Resume();
    }

    /// <summary>Emit the content that replays one absorbed element onto THIS
    /// page: the element's own content (a sub-path's operators under its
    /// composed CTM, or an imported form invocation), pre-translated by the
    /// ancestors' accumulated Position moves so a child of a moved placement
    /// lands where the moved parent would draw it.</summary>
    internal string ReplayElement(GraphicElement element)
    {
        if (element is null || element.SourceRemoved) return string.Empty;
        var body = element is XFormPlacement placement
            ? ReplayFormPlacement(placement)
            : element.ToContent();
        if (string.IsNullOrEmpty(body)) return body;
        var (adx, ady) = element.AncestorTranslation();
        if (adx == 0 && ady == 0) return body;
        string F(double v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        return $"q 1 0 0 1 {F(adx)} {F(ady)} cm\n{body}Q\n";
    }

    /// <summary>Replay a form invocation onto THIS page: import the form object
    /// (cross-document via the page-collection remapper; same-document objects
    /// are referenced directly), register it under a fresh /XObject name in this
    /// page's resources, and emit its placement CTM + <c>Do</c>.</summary>
    private string ReplayFormPlacement(XFormPlacement placement)
    {
        if (placement.FormStream is null || placement.FormRef is null) return string.Empty;

        Core.PdfObject formRef = new Core.PdfIndirectRef(
            placement.FormRef.ObjectNumber, placement.FormRef.Generation);
        if (!ReferenceEquals(placement.SourceReader, Reader))
        {
            var pages = Reader.OwnerDocument?.Pages;
            if (pages is null) return string.Empty;
            formRef = pages.ImportForeignObject(formRef, placement.SourceReader);
        }

        var resources = GetOrCreateOwnResources();
        var xobjects = resources.Get("XObject") as Core.PdfDictionary
            ?? Reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null)
        {
            xobjects = new Core.PdfDictionary();
            resources.Set("XObject", xobjects);
        }
        var name = "GAFm0";
        var counter = 0;
        while (xobjects.ContainsKey(name)) name = $"GAFm{++counter}";
        xobjects.Set(name, formRef);

        string F(double v)
        {
            if (System.Math.Abs(v) < 1e-6) v = 0;
            return v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        }
        var m = placement.PlacementCtm;
        var (dx, dy) = placement.SourceTranslation;
        var sb = new System.Text.StringBuilder();
        sb.Append("q ");
        if (dx != 0 || dy != 0)
            sb.Append($"1 0 0 1 {F(dx)} {F(dy)} cm ");
        sb.Append($"{F(m.A)} {F(m.B)} {F(m.C)} {F(m.D)} {F(m.E)} {F(m.F)} cm /{name} Do Q\n");
        return sb.ToString();
    }

    /// <summary>This page's own /Resources dictionary, creating one seeded from
    /// the inherited entries when the page draws off an ancestor's resources —
    /// a fresh empty dict would shadow the inherited fonts/images.</summary>
    private Core.PdfDictionary GetOrCreateOwnResources()
    {
        var res = Dict.Get("Resources") as Core.PdfDictionary
            ?? Reader.ResolveDict(Dict.Get("Resources"));
        if (res is null)
        {
            res = new Core.PdfDictionary();
            for (var anc = Reader.ResolveDict(Dict.Get("Parent"));
                 anc is not null;
                 anc = Reader.ResolveDict(anc.Get("Parent")))
            {
                if (Reader.ResolveDict(anc.Get("Resources")) is { } inherited)
                {
                    foreach (var key in inherited.Keys)
                        if (inherited.Get(key) is { } entry)
                            res.Set(key, entry);
                    break;
                }
            }
            Dict.Set("Resources", res);
        }
        return res;
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
            else
            {
                // Purge layers deleted/flattened since the last access. Done here
                // (between enumerations) rather than inside Delete/Flatten so that
                // `foreach (var l in page.Layers) l.Flatten()` stays valid.
                _layerFacades.RemoveAll(l => l is not null && l.IsRemoved);
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
