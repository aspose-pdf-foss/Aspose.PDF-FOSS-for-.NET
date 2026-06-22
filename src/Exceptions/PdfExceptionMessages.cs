namespace Aspose.Pdf;

/// <summary>
/// Centralised message strings surfaced by the library's exceptions and
/// security diagnostics.
/// </summary>
public static class PdfExceptionMessages
{
    /// <summary>
    /// Description assigned to an embedded file whose content was recognised as
    /// a known attack vector (for example a Windows <c>.SettingContent-ms</c>
    /// payload, CVE-2018-8414) and stripped when the document was opened.
    /// </summary>
    public const string DangerousFile =
        "The embedded file is potentially dangerous and its content has been removed.";
}
