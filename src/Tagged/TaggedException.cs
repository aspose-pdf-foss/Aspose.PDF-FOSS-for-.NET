namespace Aspose.Pdf.Tagged;

/// <summary>
/// Exception thrown when a tagged PDF structure operation violates PDF spec constraints.
/// For example, appending a non-table child to a Table element.
/// </summary>
public class TaggedException : Exception
{
    /// <summary>
    /// Creates a new TaggedException with the specified message.
    /// </summary>
    public TaggedException(string message) : base(message) { }

    /// <summary>
    /// Creates a new TaggedException with a message and inner exception.
    /// </summary>
    public TaggedException(string message, Exception innerException) : base(message, innerException) { }
}
