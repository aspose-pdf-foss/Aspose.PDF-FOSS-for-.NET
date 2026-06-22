namespace Aspose.Pdf.Annotations;

/// <summary>
/// XFDF XML element and attribute names used when annotations are
/// serialized to or parsed from XFDF (Adobe XML Forms Data Format).
/// Values follow the XFDF specification: element names are lowercase
/// of the PDF /Subtype, attribute names use lowercase or hyphen / camel
/// case forms as defined by the spec.
/// </summary>
internal static class XfdfTags
{
    // Annotation element names (lowercase PDF /Subtype).
    public const string Line = "line";
    public const string Circle = "circle";
    public const string Square = "square";
    public const string Polygon = "polygon";
    public const string PolyLine = "polyline";
    public const string Caret = "caret";
    public const string Sound = "sound";
    public const string Ink = "ink";
    public const string FileAttachment = "fileattachment";
    public const string Stamp = "stamp";
    public const string FreeText = "freetext";
    public const string Highlight = "highlight";
    public const string StrikeOut = "strikeout";
    public const string Squiggly = "squiggly";
    public const string Underline = "underline";
    public const string Text = "text";
    public const string Popup = "popup";

    // Sub-elements.
    public const string ContentsRichtext = "contents-richtext";
    public const string DefaultAppearance = "defaultappearance";
    public const string DefaultStyle = "defaultstyle";
    public const string Vertices = "vertices";
    public const string Coords = "coords";
    public const string InkList = "inklist";
    public const string Gesture = "gesture";
    public const string Data = "data";
    public const string Appearance = "appearance";
    public const string File = "file";

    // Common annotation attributes.
    public const string Page = "page";
    public const string Color = "color";
    public const string Date = "date";
    public const string Flags = "flags";
    public const string Name = "name";
    public const string Rect = "rect";
    public const string Title = "title";
    public const string CreationDate = "creationdate";
    public const string Opacity = "opacity";
    public const string Subject = "subject";
    public const string Modification = "modification";
    public const string Creation = "creation";
    public const string Width = "width";
    public const string Style = "style";
    public const string Dashes = "dashes";
    public const string IT = "IT"; // Adobe XFDF spells the FreeText intent attribute uppercase "IT"
    public const string Intent = "intent";
    public const string InReplyTo = "inreplyto";
    public const string ReplyType = "replyType";

    // Text / popup specific.
    public const string Open = "open";
    public const string Icon = "icon";
    public const string State = "state";
    public const string StateModel = "statemodel";

    // FreeText specific.
    public const string Justification = "justification";

    // Sound specific.
    public const string Mode = "mode";
    public const string Bits = "bits";
    public const string Channels = "channels";
    public const string Encoding = "encoding";
    public const string Filter = "filter";
    public const string Length = "length";
    public const string Rate = "rate";

    // FileAttachment / generic resources.
    public const string Size = "size";
    public const string CheckSum = "checksum";
    public const string MimeType = "mimetype";

    // Stamp / Caret.
    public const string Symbol = "symbol";

    // Square / Circle / Polygon shared.
    public const string Fringe = "fringe";
    public const string InteriorColor = "interior-color";

    // Highlight / Squiggly / StrikeOut / Underline.
    public const string Intensity = "intensity";

    // Line specific.
    public const string Start = "start";
    public const string End = "end";
    public const string Head = "head";
    public const string Tail = "tail";
    public const string LeaderLength = "leaderLength";
    public const string LeaderExtend = "leaderExtend";
    public const string LeaderOffset = "leaderOffset";
    public const string Caption = "caption";
    public const string CaptionStyle = "caption-style";
    public const string CaptionOffsetH = "caption-offset-h";
    public const string CaptionOffsetV = "caption-offset-v";
}
