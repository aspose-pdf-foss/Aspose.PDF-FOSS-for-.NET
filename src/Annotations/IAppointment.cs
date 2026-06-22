namespace Aspose.Pdf.Annotations;

/// <summary>
/// Marker interface for any object that can be stored in an outline item's
/// Destination, a link annotation's Destination, Document.OpenAction, or a
/// named-destination collection. Implemented by <see cref="ExplicitDestination"/>
/// (and its Fit/XYZ subclasses) and by <see cref="PdfAction"/> (and its GoTo/URI
/// subclasses).
/// </summary>
public interface IAppointment
{
    /// <summary>Human-readable representation of this appointment target.</summary>
    string? ToString();
}
