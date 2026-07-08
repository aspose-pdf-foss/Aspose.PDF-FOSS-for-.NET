using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>How the embedded 3D model becomes active (PDF 32000-1 §13.6.2,
/// /AAS entry of the 3D activation dictionary). The FOSS build accepts and
/// stores the value but does not yet emit 3D activation behaviour.</summary>
public enum PDF3DActivation
{
    activeWhenOpen,
    activeWhenVisible,
    activatedUserOrScriptAction,
}

/// <summary>Embedded-stream content for a 3D artwork (U3D or PRC). Acts as
/// a thin byte-buffer wrapper — the FOSS save path does not currently emit
/// the /3DD stream object.</summary>
public sealed class PDF3DContent
{
    private byte[] _bytes = System.Array.Empty<byte>();

    public PDF3DContent() { }

    public PDF3DContent(string filename)
    {
        Load(filename);
    }

    public string? Extension { get; private set; }

    /// <summary>Populate directly from a parsed 3D stream (reading path):
    /// keep the decoded content bytes and the format tag (e.g. "PRC"/"U3D")
    /// exactly as read, without the LoadAsPRC/U3D dotted-extension rewrite.</summary>
    internal void SetReadContent(byte[] bytes, string? extension)
    {
        _bytes = bytes ?? System.Array.Empty<byte>();
        Extension = extension;
    }

    public byte[] GetAsByteArray() => (byte[])_bytes.Clone();
    public Stream GetAsStream() => new MemoryStream(_bytes, writable: false);

    public void Load(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return;
        _bytes = System.IO.File.ReadAllBytes(filename);
        Extension = System.IO.Path.GetExtension(filename);
    }

    public void LoadAsPRC(byte[] stream) { _bytes = stream ?? System.Array.Empty<byte>(); Extension = ".prc"; }
    public void LoadAsPRC(Stream stream) { _bytes = ReadAll(stream); Extension = ".prc"; }
    public void LoadAsPRC(string filename) { Load(filename); Extension = ".prc"; }

    public void LoadAsU3D(byte[] stream) { _bytes = stream ?? System.Array.Empty<byte>(); Extension = ".u3d"; }
    public void LoadAsU3D(Stream stream) { _bytes = ReadAll(stream); Extension = ".u3d"; }
    public void LoadAsU3D(string filename) { Load(filename); Extension = ".u3d"; }

    public void SaveToFile(string filename) => System.IO.File.WriteAllBytes(filename, _bytes);

    private static byte[] ReadAll(Stream s)
    {
        if (s is null) return System.Array.Empty<byte>();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}

/// <summary>PDF /3DD stream wrapper — pairs a <see cref="PDF3DArtwork"/>
/// with its document-owned content stream. Stored only.</summary>
public sealed class PDF3DStream
{
    public PDF3DStream(Document doc, PDF3DArtwork pdf3DArtwork)
    {
        _ = doc;
        Content = pdf3DArtwork?.Content;
    }

    public PDF3DContent? Content { get; set; }
}

/// <summary>Lighting-scheme nominal type (PDF 32000-1 §13.6.4). Paired with
/// <see cref="PDF3DLightingScheme"/> so the static factory fields below
/// expose well-known schemes by enum value.</summary>
public enum LightingSchemeType
{
    Artwork,
    None,
    White,
    Day,
    Night,
    Hard,
    Primary,
    Blue,
    Red,
    Cube,
    CAD,
    Headlamp,
}

/// <summary>Lighting scheme applied to a PDF 3D artwork (Aspose.Pdf shape).
/// Stored only.</summary>
public sealed class PDF3DLightingScheme
{
    public PDF3DLightingScheme(LightingSchemeType type) { Type = type; }

    public PDF3DLightingScheme(string typeName)
    {
        if (System.Enum.TryParse<LightingSchemeType>(typeName, ignoreCase: true, out var t))
            Type = t;
    }

    public LightingSchemeType Type { get; }

    public static readonly PDF3DLightingScheme Artwork = new(LightingSchemeType.Artwork);
    public static readonly PDF3DLightingScheme None = new(LightingSchemeType.None);
    public static readonly PDF3DLightingScheme White = new(LightingSchemeType.White);
    public static readonly PDF3DLightingScheme Day = new(LightingSchemeType.Day);
    public static readonly PDF3DLightingScheme Night = new(LightingSchemeType.Night);
    public static readonly PDF3DLightingScheme Hard = new(LightingSchemeType.Hard);
    public static readonly PDF3DLightingScheme Primary = new(LightingSchemeType.Primary);
    public static readonly PDF3DLightingScheme Blue = new(LightingSchemeType.Blue);
    public static readonly PDF3DLightingScheme Red = new(LightingSchemeType.Red);
    public static readonly PDF3DLightingScheme Cube = new(LightingSchemeType.Cube);
    public static readonly PDF3DLightingScheme CAD = new(LightingSchemeType.CAD);
    public static readonly PDF3DLightingScheme Headlamp = new(LightingSchemeType.Headlamp);
}

/// <summary>Render-mode nominal type (PDF 32000-1 §13.6.5). Paired with
/// <see cref="PDF3DRenderMode"/>.</summary>
public enum RenderModeType
{
    Solid,
    SolidWireframe,
    Transparent,
    TransparentWareFrame,
    BoundingBox,
    TransparentBoundingBox,
    TransparentBoundingBoxOutline,
    Wireframe,
    ShadedWireframe,
    HiddenWireframe,
    Vertices,
    ShadedVertices,
    Illustration,
    SolidOutline,
    ShadedIllustration,
}

/// <summary>Render mode applied to a 3D artwork view (Aspose.Pdf shape).
/// Stored only.</summary>
public sealed class PDF3DRenderMode
{
    private double _crease;
    private double _opacity = 1.0;
    private Color? _faceColor;
    private Color? _auxColor;

    public PDF3DRenderMode(RenderModeType subtype) { Type = subtype; }

    public PDF3DRenderMode(string typeName)
    {
        if (System.Enum.TryParse<RenderModeType>(typeName, ignoreCase: true, out var t))
            Type = t;
    }

    public RenderModeType Type { get; }

    public static readonly PDF3DRenderMode Solid = new(RenderModeType.Solid);
    public static readonly PDF3DRenderMode SolidWireframe = new(RenderModeType.SolidWireframe);
    public static readonly PDF3DRenderMode Transparent = new(RenderModeType.Transparent);
    public static readonly PDF3DRenderMode TransparentWareFrame = new(RenderModeType.TransparentWareFrame);
    public static readonly PDF3DRenderMode BoundingBox = new(RenderModeType.BoundingBox);
    public static readonly PDF3DRenderMode TransparentBoundingBox = new(RenderModeType.TransparentBoundingBox);
    public static readonly PDF3DRenderMode TransparentBoundingBoxOutline = new(RenderModeType.TransparentBoundingBoxOutline);
    public static readonly PDF3DRenderMode Wireframe = new(RenderModeType.Wireframe);
    public static readonly PDF3DRenderMode ShadedWireframe = new(RenderModeType.ShadedWireframe);
    public static readonly PDF3DRenderMode Vertices = new(RenderModeType.Vertices);
    public static readonly PDF3DRenderMode ShadedVertices = new(RenderModeType.ShadedVertices);
    public static readonly PDF3DRenderMode Illustration = new(RenderModeType.Illustration);
    public static readonly PDF3DRenderMode SolidOutline = new(RenderModeType.SolidOutline);
    public static readonly PDF3DRenderMode ShadedIllustration = new(RenderModeType.ShadedIllustration);

    public Color GetAuxiliaryColour() => _auxColor ?? Color.Black;
    public double GetCreaseValue() => _crease;
    public object? GetFaceColor() => _faceColor;
    public double GetOpacity() => _opacity;

    public PDF3DRenderMode SetAuxiliaryColour(Color color) { _auxColor = color; return this; }
    public PDF3DRenderMode SetCreaseValue(double creaseValue) { _crease = creaseValue; return this; }
    public PDF3DRenderMode SetFaceColor(Color color) { _faceColor = color; return this; }
    public PDF3DRenderMode SetOpacity(double opacity) { _opacity = opacity; return this; }
}

/// <summary>Orientation angles for the cutting plane of a 3D cross-section
/// (Aspose.Pdf shape). Stored only.</summary>
public sealed class PDF3DCuttingPlaneOrientation
{
    public PDF3DCuttingPlaneOrientation() { }

    public PDF3DCuttingPlaneOrientation(double? angleX, double? angleY, double? angleZ)
    {
        AngleX = angleX;
        AngleY = angleY;
        AngleZ = angleZ;
    }

    public double? AngleX { get; set; }
    public double? AngleY { get; set; }
    public double? AngleZ { get; set; }

    public override string ToString()
        => $"({AngleX?.ToString() ?? "_"}, {AngleY?.ToString() ?? "_"}, {AngleZ?.ToString() ?? "_"})";
}

/// <summary>Single cross-section through a PDF 3D model (Aspose.Pdf shape).
/// Stored only.</summary>
public sealed class PDF3DCrossSection
{
    public PDF3DCrossSection(Document doc) { _ = doc; }

    public Point3D Center { get; set; } = new Point3D();
    public Color CuttingPlaneColor { get; set; } = Color.LightGray;
    public double CuttingPlaneOpacity { get; set; } = 1.0;
    public PDF3DCuttingPlaneOrientation CuttingPlaneOrientation { get; set; } = new PDF3DCuttingPlaneOrientation();
    public Color CuttingPlanesIntersectionColor { get; set; } = Color.Red;
    public bool Visibility { get; set; } = true;
}

/// <summary>Ordered collection of <see cref="PDF3DCrossSection"/> entries
/// applied to a PDF 3D view (Aspose.Pdf shape). Stored only.</summary>
public sealed class PDF3DCrossSectionArray
{
    private readonly List<PDF3DCrossSection> _items = new();

    public PDF3DCrossSectionArray(Document doc) { _ = doc; }

    public int Count => _items.Count;

    // Aspose.Pdf exposes this collection as 1-based (the first entry
    // is [1]); mirror that so read-back positions match the reference API.
    public PDF3DCrossSection this[int index]
    {
        get => _items[index - 1];
        set => _items[index - 1] = value;
    }

    public void Add(PDF3DCrossSection crossSection) => _items.Add(crossSection);
    public void RemoveAll() => _items.Clear();
    public void RemoveAt(int index) => _items.RemoveAt(index);
}

/// <summary>Camera + render-state snapshot of a 3D artwork (Aspose.Pdf
/// shape). Stored only.</summary>
public sealed class PDF3DView
{
    public PDF3DView(Document doc, PDF3DView view, string viewName)
    {
        _ = doc;
        if (view is not null)
        {
            BackGroundColor = view.BackGroundColor;
            CameraOrbit = view.CameraOrbit;
            CameraPosition = view.CameraPosition;
            LightingScheme = view.LightingScheme;
            RenderMode = view.RenderMode;
        }
        ViewName = viewName ?? string.Empty;
    }

    public PDF3DView(Document doc, Matrix3D cameraPosition, double cameraOrbit, string viewName)
    {
        _ = doc;
        CameraPosition = cameraPosition;
        CameraOrbit = cameraOrbit;
        ViewName = viewName ?? string.Empty;
    }

    public Color BackGroundColor { get; set; } = Color.White;
    public double CameraOrbit { get; set; }
    public Matrix3D? CameraPosition { get; set; }
    public PDF3DCrossSectionArray CrossSectionsArray { get; } = new PDF3DCrossSectionArray(null!);
    public PDF3DLightingScheme? LightingScheme { get; set; }
    public PDF3DRenderMode? RenderMode { get; set; }
    public string ViewName { get; set; } = string.Empty;
}

/// <summary>Indexed collection of <see cref="PDF3DView"/> snapshots
/// (Aspose.Pdf shape). Stored only.</summary>
public sealed class PDF3DViewArray
{
    private readonly List<PDF3DView> _items = new();

    internal PDF3DViewArray() { }

    public int Count => _items.Count;

    // Aspose.Pdf exposes this collection as 1-based (the first view
    // is [1]); mirror that so read-back positions match the reference API.
    public PDF3DView this[int index]
    {
        get => _items[index - 1];
        set => _items[index - 1] = value;
    }

    public void Add(PDF3DView view) => _items.Add(view);
    public void RemoveAll() => _items.Clear();
    public void RemoveAt(int index) => _items.RemoveAt(index);

    internal IEnumerable<PDF3DView> Items => _items;
}

/// <summary>A 3D artwork dictionary referenced by a PDF 3D annotation
/// (Aspose.Pdf shape). Stored only.</summary>
public sealed class PDF3DArtwork
{
    public PDF3DArtwork(Document doc, PDF3DContent content)
    {
        _ = doc;
        Content = content;
    }

    public PDF3DArtwork(Document doc, PDF3DContent content,
        PDF3DLightingScheme lightingScheme, PDF3DRenderMode renderMode)
        : this(doc, content)
    {
        LightingScheme = lightingScheme;
        RenderMode = renderMode;
    }

    public PDF3DContent? Content { get; set; }
    public PDF3DLightingScheme? LightingScheme { get; set; }
    public PDF3DRenderMode? RenderMode { get; set; }
    public PDF3DViewArray ViewArray { get; } = new PDF3DViewArray();

    public PDF3DView[] GetViewsArray() => System.Linq.Enumerable.ToArray(ViewArray.Items);
    public ReadOnlyCollection<PDF3DView> GetViewsList()
        => new ReadOnlyCollection<PDF3DView>(System.Linq.Enumerable.ToList(ViewArray.Items));
}
