using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>Abstract base for illustration-kind structure elements
/// (Figure, Formula). Lets callers treat any illustration uniformly.</summary>
public abstract class IllustrationElement : StructureElement
{
    internal IllustrationElement(string structureType) : base(structureType) { }
    internal IllustrationElement(PdfDictionary dict, PdfReader? reader) : base(dict, reader) { }

    /// <summary>Image file backing this illustration. FOSS-extra — stored only.</summary>
    public string? ImagePath { get; private set; }

    /// <summary>Image-width override (0 = auto). FOSS-extra.</summary>
    public double ImageWidth { get; private set; }

    /// <summary>Image-height override (0 = auto). FOSS-extra.</summary>
    public double ImageHeight { get; private set; }

    /// <summary>Image resolution (DPI) override (0 = auto). FOSS-extra.</summary>
    public int Resolution { get; private set; }

    /// <summary>Bind a raster/vector picture to this illustration. FOSS-extra
    /// mirroring the Tagged-side authoring helper. Stored only.</summary>
    public void SetImage(string imagePath)
    {
        ImagePath = imagePath;
        ImageWidth = 0;
        ImageHeight = 0;
    }

    /// <summary>Bind a picture with explicit dimensions.</summary>
    public void SetImage(string imagePath, double width, double height)
    {
        ImagePath = imagePath;
        ImageWidth = width;
        ImageHeight = height;
    }

    /// <summary>Bind a picture with an explicit resolution (DPI).</summary>
    public void SetImage(string imagePath, int resolution)
    {
        ImagePath = imagePath;
        ImageWidth = 0;
        ImageHeight = 0;
        Resolution = resolution;
    }
}
