namespace Aspose.Pdf.Comparison.Diff
{
    /// <summary>The kind of edit a <see cref="DiffOperation"/> represents when diffing two
    /// texts: a run that is unchanged, deleted from the source, or inserted in the destination.</summary>
    public enum Operation
    {
        /// <summary>Text present in both source and destination (unchanged).</summary>
        Equal,

        /// <summary>Text present in the source but removed from the destination.</summary>
        Delete,

        /// <summary>Text absent from the source but added in the destination.</summary>
        Insert
    }
}
