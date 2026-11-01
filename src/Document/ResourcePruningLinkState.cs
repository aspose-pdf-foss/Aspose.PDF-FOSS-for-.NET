using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Working state of one <see cref="LinkDuplicateStreams"/> call. A stream is known by an id:
    /// its object number, or - for a stream held inline, which no save has numbered yet - a negative
    /// number of its own.</summary>
    private sealed class StreamLinkState
    {
        /// <summary>Every stream compared, in the order the first of equal ones becomes the one kept.</summary>
        public readonly List<(int id, int generation, PdfStream stream, string contentHash)> streams = [];

        /// <summary>The ids of streams held inline.</summary>
        public readonly Dictionary<PdfStream, int> inlineIds = new(ReferenceEqualityComparer.Instance);

        /// <summary>duplicate id → the id of the stream kept in its place.</summary>
        public Dictionary<int, int> redirections = new();

        /// <summary>duplicate id → what a reference to it becomes: a reference to the kept object, or the
        /// kept inline stream itself.</summary>
        public readonly Dictionary<int, PdfObject> targets = new();

        /// <summary>Containers whose references were already redirected: a direct dictionary may be held
        /// in several places, and a back reference (a widget's /Parent) closes a cycle.</summary>
        public readonly HashSet<PdfObject> redirected = new(ReferenceEqualityComparer.Instance);

        /// <summary>The id of an inline stream, given on first sight.</summary>
        public int IdOf(PdfStream inline)
        {
            if (!inlineIds.TryGetValue(inline, out var id))
            {
                id = -(inlineIds.Count + 1);
                inlineIds[inline] = id;
            }
            return id;
        }
    }
}
