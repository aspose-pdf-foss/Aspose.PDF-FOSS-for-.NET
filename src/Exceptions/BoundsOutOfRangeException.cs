namespace Aspose.Pdf;

/// <summary>
/// Thrown when an element is inserted into a parent container whose bounds
/// check mode is <see cref="BoundsCheckMode.ThrowExceptionIfDoesNotFit"/>
/// and the element does not fit the container rectangle.
/// </summary>
public class BoundsOutOfRangeException : PdfException
{
    /// <summary>Initializes a new instance of the <see cref="BoundsOutOfRangeException"/> class.</summary>
    public BoundsOutOfRangeException() { }

    /// <summary>Initializes a new instance of the <see cref="BoundsOutOfRangeException"/> class with a message.</summary>
    public BoundsOutOfRangeException(string message) : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="BoundsOutOfRangeException"/> class with a message and inner exception.</summary>
    public BoundsOutOfRangeException(string message, Exception innerException) : base(message, innerException) { }
}
