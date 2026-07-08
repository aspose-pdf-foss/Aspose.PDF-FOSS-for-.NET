using System.Collections.Generic;
using System.Text;

namespace Aspose.Pdf.Comparison.Diff
{
    /// <summary>Text helpers used by the diff engine: common prefix/suffix extraction and
    /// re-assembly of the source / destination text from a list of <see cref="DiffOperation"/>s.</summary>
    public static class DiffUtils
    {
        /// <summary>The longest common prefix shared by <paramref name="first"/> and
        /// <paramref name="second"/> (empty when they differ from the first character).</summary>
        public static string FindCommonStartParts(string? first, string? second)
        {
            first ??= string.Empty;
            second ??= string.Empty;
            var max = first.Length < second.Length ? first.Length : second.Length;
            var n = 0;
            while (n < max && first[n] == second[n]) n++;
            return first.Substring(0, n);
        }

        /// <summary>StringBuilder overload of <see cref="FindCommonStartParts(string,string)"/>.</summary>
        public static string FindCommonStartParts(StringBuilder? first, StringBuilder? second)
            => FindCommonStartParts(first?.ToString(), second?.ToString());

        /// <summary>The longest common suffix shared by <paramref name="first"/> and
        /// <paramref name="second"/>, without matching further back than index
        /// <paramref name="startIndex"/> in <paramref name="first"/> (so the result length is
        /// bounded by <c>first.Length - startIndex</c>).</summary>
        public static string FindCommonEndParts(string? first, string? second, int startIndex)
        {
            first ??= string.Empty;
            second ??= string.Empty;
            var limit = first.Length - startIndex;
            if (limit > second.Length) limit = second.Length;
            var n = 0;
            while (n < limit && first[first.Length - 1 - n] == second[second.Length - 1 - n]) n++;
            return n == 0 ? string.Empty : first.Substring(first.Length - n);
        }

        /// <summary>StringBuilder overload of <see cref="FindCommonEndParts(string,string,int)"/>
        /// with no lower bound (<c>startIndex = 0</c>).</summary>
        public static string FindCommonEndParts(StringBuilder? first, StringBuilder? second)
            => FindCommonEndParts(first?.ToString(), second?.ToString(), 0);

        /// <summary>Rebuild the original (source) text: the concatenation of every
        /// <see cref="Operation.Equal"/> and <see cref="Operation.Delete"/> run.</summary>
        public static string AssemblySourceText(IEnumerable<DiffOperation>? diffs)
            => Assembly(diffs, Operation.Insert);

        /// <summary>Rebuild the modified (destination) text: the concatenation of every
        /// <see cref="Operation.Equal"/> and <see cref="Operation.Insert"/> run.</summary>
        public static string AssemblyDestinationText(IEnumerable<DiffOperation>? diffs)
            => Assembly(diffs, Operation.Delete);

        private static string Assembly(IEnumerable<DiffOperation>? diffs, Operation skip)
        {
            var sb = new StringBuilder();
            if (diffs is not null)
                foreach (var d in diffs)
                    if (d is not null && d.Operation != skip)
                        sb.Append(d.Text);
            return sb.ToString();
        }
    }
}
