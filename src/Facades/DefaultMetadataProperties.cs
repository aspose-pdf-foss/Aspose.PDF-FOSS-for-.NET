namespace Aspose.Pdf.Facades;

/// <summary>
/// Well-known XMP-basic-schema property keys exposed by
/// <see cref="PdfXmpMetadata"/> as a typed alternative to raw
/// <c>"xmp:&lt;name&gt;"</c> strings.
/// </summary>
public enum DefaultMetadataProperties
{
    /// <summary>An unordered array specifying properties that were edited
    /// outside the authoring application. Maps to <c>xmp:Advisory</c>.</summary>
    Advisory,

    /// <summary>The base URL for relative URLs in the document content. Maps to <c>xmp:BaseURL</c>.</summary>
    BaseURL,

    /// <summary>The date and time the resource was created. Maps to <c>xmp:CreateDate</c>.</summary>
    CreateDate,

    /// <summary>The name of the first known tool used to create the resource. Maps to <c>xmp:CreatorTool</c>.</summary>
    CreatorTool,

    /// <summary>An unordered array of text strings that unambiguously identify the resource. Maps to <c>xmp:Identifier</c>.</summary>
    Identifier,

    /// <summary>The date and time any metadata for this resource was last changed. Maps to <c>xmp:MetadataDate</c>.</summary>
    MetadataDate,

    /// <summary>The date and time the resource was last modified. Maps to <c>xmp:ModifyDate</c>.</summary>
    ModifyDate,

    /// <summary>A short informal name for the resource. Maps to <c>xmp:Nickname</c>.</summary>
    Nickname,

    /// <summary>An alternative array of thumbnail images for a file. Maps to <c>xmp:Thumbnails</c>.</summary>
    Thumbnails,
}
