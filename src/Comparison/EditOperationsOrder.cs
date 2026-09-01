namespace Aspose.Pdf.Comparison
{
    /// <summary>When a delete and an insert are emitted for the same position, which one an
    /// optimizer places first in the resulting operation sequence.</summary>
    public enum EditOperationsOrder
    {
        /// <summary>Emit the delete before the insert.</summary>
        DeleteFirst,

        /// <summary>Emit the insert before the delete.</summary>
        InsertFirst
    }
}
