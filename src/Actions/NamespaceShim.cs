// Namespace-compat shim: the public API places PdfAction and its
// subclasses in Aspose.Pdf.Annotations. Tests (and some older callers) still
// write `using Aspose.Pdf.Actions;`. A `using` against an empty or nonexistent
// namespace is a compile error, so this file keeps the Aspose.Pdf.Actions
// namespace alive with a single internal marker type. All action types live
// in Aspose.Pdf.Annotations (see PdfAction.cs).
namespace Aspose.Pdf.Actions
{
    internal static class NamespaceMarker { }
}
