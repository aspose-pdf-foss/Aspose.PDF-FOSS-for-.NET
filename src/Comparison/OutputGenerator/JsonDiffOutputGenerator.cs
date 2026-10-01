using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aspose.Pdf.Comparison.Diff;

namespace Aspose.Pdf.Comparison
{
    /// <summary>Writes a comparison result as JSON: an array of edits, each carrying the
    /// operation name and the run of text it applies to.</summary>
    public sealed class JsonDiffOutputGenerator : IFileOutputGenerator
    {
        // The reference output is two-space indented, names its operations as strings
        // ("Equal", not 0), and escapes every non-ASCII character (the source text's
        // typographic apostrophe is written ’) - which is what the strict default
        // encoder does. Property order follows the DTO below: operation, then text.
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>One edit as it appears in the JSON output.</summary>
        private sealed class Entry
        {
            [JsonPropertyName("Operation")]
            public Operation Operation { get; init; }

            [JsonPropertyName("Text")]
            public string Text { get; init; } = string.Empty;

            public static Entry From(DiffOperation d) =>
                new() { Operation = d.Operation, Text = d.Text ?? string.Empty };
        }

        /// <inheritdoc/>
        public void GenerateOutput(List<DiffOperation> differences, string targetFilePath)
        {
            var entries = new List<Entry>();
            if (differences is not null)
                foreach (var d in differences)
                    if (d is not null) entries.Add(Entry.From(d));
            Write(entries, targetFilePath);
        }

        /// <inheritdoc/>
        public void GenerateOutput(List<List<DiffOperation>> differences, string targetFilePath)
        {
            // A grouped diff writes one array per group, so a reader can tell the pages apart.
            var groups = new List<List<Entry>>();
            if (differences is not null)
            {
                foreach (var group in differences)
                {
                    var entries = new List<Entry>();
                    if (group is not null)
                        foreach (var d in group)
                            if (d is not null) entries.Add(Entry.From(d));
                    groups.Add(entries);
                }
            }
            Write(groups, targetFilePath);
        }

        private static void Write<T>(T payload, string targetFilePath)
        {
            var dir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(targetFilePath, JsonSerializer.Serialize(payload, Options));
        }
    }
}
