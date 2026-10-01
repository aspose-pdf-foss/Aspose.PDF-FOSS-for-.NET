namespace Aspose.Pdf.Annotations;

/// <summary>The kind of a rendition (PDF §13.2.3): the /S entry of its dictionary.</summary>
public enum RenditionType
{
    /// <summary>A media rendition (/S /MR): a media clip with playback parameters.</summary>
    Media,

    /// <summary>A selector rendition (/S /SR): a choice among alternative renditions.</summary>
    Selector,

    /// <summary>The dictionary carries no recognised /S entry.</summary>
    Undefined,
}
