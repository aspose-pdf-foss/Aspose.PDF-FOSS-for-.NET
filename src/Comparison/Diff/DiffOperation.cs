using System;

namespace Aspose.Pdf.Comparison.Diff
{
    /// <summary>A single edit produced when diffing two texts: an <see cref="Operation"/>
    /// (Equal / Delete / Insert) together with the run of text it applies to.</summary>
    public sealed class DiffOperation : IEquatable<DiffOperation>
    {
        /// <summary>The edit kind.</summary>
        public Operation Operation { get; set; }

        /// <summary>The text this edit applies to.</summary>
        public string Text { get; set; }

        /// <summary>Create an edit of the given kind over the given text.</summary>
        public DiffOperation(Operation operation, string text)
        {
            Operation = operation;
            Text = text ?? string.Empty;
        }

        /// <summary>Two edits are equal when they share the same operation and text.</summary>
        public bool Equals(DiffOperation? other)
            => other is not null && Operation == other.Operation
               && string.Equals(Text, other.Text, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as DiffOperation);

        /// <inheritdoc/>
        public override int GetHashCode()
            => ((int)Operation * 397) ^ (Text is null ? 0 : Text.GetHashCode());

        /// <inheritdoc/>
        public override string ToString() => $"{Operation}(\"{Text}\")";
    }
}
