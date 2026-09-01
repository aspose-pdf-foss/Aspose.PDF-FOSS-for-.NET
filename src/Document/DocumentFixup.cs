namespace Aspose.Pdf;

/// <summary>Pre-defined PDF fixup operations accepted by
/// <see cref="Document.Convert(Fixup, System.IO.Stream, bool, object[])"/>.
/// Two values have real implementations in the FOSS build
/// (RotatePagesToLandscape / RotatePagesToPortrait); the others throw
/// <see cref="System.NotSupportedException"/> with a clear message
/// pointing at the missing capability.</summary>
public enum Fixup
{
    EmbedMissingFonts,
    ConvertFontsToOutlines,
    RotatePagesToLandscape,
    RotatePagesToPortrait,
    DerivePageGeometryBoxesFromCropMarks,
    ConvertAllPagesIntoCMYKImagesAndPreserveTextInformation,
}
