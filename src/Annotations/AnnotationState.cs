namespace Aspose.Pdf.Annotations;

/// <summary>
/// Review or marked-state of a markup annotation, as defined by PDF 32000
/// §12.5.6.3 (text annotations, /StateModel + /State entries).
/// </summary>
public enum AnnotationState
{
    /// <summary>No state was set.</summary>
    None,

    /// <summary>The annotation is marked.</summary>
    Marked,

    /// <summary>The annotation is unmarked.</summary>
    Unmarked,

    /// <summary>The annotation has been accepted.</summary>
    Accepted,

    /// <summary>The annotation has been rejected.</summary>
    Rejected,

    /// <summary>The annotation has been cancelled.</summary>
    Cancelled,

    /// <summary>The annotation has been completed.</summary>
    Completed,

    /// <summary>State value is missing or undefined.</summary>
    Undefined,
}

/// <summary>Which state model a <see cref="AnnotationState"/> belongs to.</summary>
public enum AnnotationStateModel
{
    /// <summary>The Marked model — values are Marked / Unmarked.</summary>
    Marked,

    /// <summary>The Review model — values are Accepted / Rejected / Cancelled / Completed / None.</summary>
    Review,

    /// <summary>State model is missing or undefined.</summary>
    Undefined,
}

/// <summary>Reply-relationship between a markup annotation and its
/// <see cref="MarkupAnnotation.InReplyTo"/> target.</summary>
public enum ReplyType
{
    /// <summary>A direct reply to the parent annotation.</summary>
    Reply,

    /// <summary>The annotation is grouped with the parent (shared appearance).</summary>
    Group,

    /// <summary>Reply type is missing or undefined.</summary>
    Undefined,
}
