using System;
using System.Collections.Generic;
using System.Globalization;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>Which pages of a document a pagination artifact applies to.</summary>
public enum Subset
{
    /// <summary>Every page in the range.</summary>
    All,
    /// <summary>Odd page numbers only.</summary>
    Odd,
    /// <summary>Even page numbers only.</summary>
    Even,
}

/// <summary>
/// Base class for artifacts stamped across a page range by
/// <see cref="PageCollectionExtension.AddPagination"/> — e.g. Bates numbering.
/// The running number counts only the pages the artifact is actually applied
/// to (range and <see cref="Subset"/> filtered).
/// </summary>
public abstract class PaginationArtifact
{
    /// <summary>Horizontal placement of the stamped text.</summary>
    public HorizontalAlignment ArtifactHorizontalAlignment { get; set; } = HorizontalAlignment.Right;

    /// <summary>Vertical placement of the stamped text.</summary>
    public VerticalAlignment ArtifactVerticalAlignment { get; set; } = VerticalAlignment.Bottom;

    /// <summary>Which pages inside the range receive the artifact.</summary>
    public Subset Subset { get; set; } = Subset.All;

    /// <summary>First page (1-based) that receives the artifact. Values below 1 mean "from the first page".</summary>
    public int StartPage { get; set; } = 1;

    /// <summary>Last page (1-based) that receives the artifact. Values below 1 mean "to the last page".</summary>
    public int EndPage { get; set; }

    /// <summary>Text styling for the stamped number (font, size, colour).</summary>
    public TextState TextState { get; set; } = new();

    /// <summary>The text stamped for running number <paramref name="number"/>.</summary>
    internal abstract string FormatText(int number);

    /// <summary>The first running number. Values below 1 clamp to 1.</summary>
    internal virtual int FirstNumber => 1;
}

/// <summary>A Bates-numbering pagination artifact: a zero-padded running number
/// with optional prefix and suffix.</summary>
public sealed class BatesNArtifact : PaginationArtifact
{
    /// <summary>Text prepended to the number.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Text appended after the number.</summary>
    public string Suffix { get; set; } = string.Empty;

    /// <summary>The number stamped on the first included page. Values below 1 clamp to 1.</summary>
    public int StartNumber { get; set; } = 1;

    /// <summary>Zero-padded width of the number. Default 6 ("000001"); the effective
    /// width clamps to 3–15.</summary>
    public int NumberOfDigits { get; set; } = 6;

    internal override int FirstNumber => Math.Max(1, StartNumber);

    internal override string FormatText(int number)
        => Prefix + number.ToString("D" + Math.Clamp(NumberOfDigits, 3, 15), CultureInfo.InvariantCulture) + Suffix;
}

/// <summary>Pagination stamping over a <see cref="PageCollection"/> (Bates numbering etc.).</summary>
public static class PageCollectionExtension
{
    /// <summary>Stamp each artifact in <paramref name="artifacts"/> across the collection.
    /// Every included page receives a Pagination artifact whose running number counts
    /// only the included pages.</summary>
    public static void AddPagination(this PageCollection pages, List<PaginationArtifact> artifacts)
    {
        if (pages is null) throw new ArgumentNullException(nameof(pages));
        if (artifacts is null) throw new ArgumentNullException(nameof(artifacts));
        foreach (var spec in artifacts)
        {
            var number = spec.FirstNumber;
            var first = Math.Max(1, spec.StartPage);
            foreach (var page in pages)
            {
                if (page.Number < first) continue;
                if (spec.EndPage >= 1 && page.Number > spec.EndPage) continue;
                if (spec.Subset == Subset.Odd && page.Number % 2 == 0) continue;
                if (spec.Subset == Subset.Even && page.Number % 2 != 0) continue;

                var artifact = new Artifact(Artifact.ArtifactType.Pagination, Artifact.ArtifactSubtype.BatesN)
                {
                    TextState = spec.TextState,
                    Position = ComputePosition(page, spec, spec.FormatText(number)),
                };
                artifact.SetTextAndState(spec.FormatText(number), spec.TextState);
                page.Artifacts.Add(artifact);
                number++;
            }
        }
    }

    /// <summary>Stamp Bates numbering configured by <paramref name="configure"/> across the collection.</summary>
    public static void AddBatesNumbering(this PageCollection pages, Action<BatesNArtifact> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var bates = new BatesNArtifact();
        configure(bates);
        AddPagination(pages, new List<PaginationArtifact> { bates });
    }

    /// <summary>Stamp the given Bates-numbering artifact across the collection.</summary>
    public static void AddBatesNumbering(this PageCollection pages, BatesNArtifact bates)
    {
        if (bates is null) throw new ArgumentNullException(nameof(bates));
        AddPagination(pages, new List<PaginationArtifact> { bates });
    }

    /// <summary>Remove every Bates-numbering artifact from every page.</summary>
    public static void DeleteBatesNumbering(this PageCollection pages)
    {
        if (pages is null) throw new ArgumentNullException(nameof(pages));
        foreach (var page in pages)
        {
            for (var i = page.Artifacts.Count; i >= 1; i--)
            {
                if (page.Artifacts[i].Subtype == Artifact.ArtifactSubtype.BatesN)
                    page.Artifacts.Delete(i);
            }
        }
    }

    /// <summary>Explicit stamp position on the default A4 page: 72pt side
    /// margins; top text sits at pageHeight − 36; bottom text at 36 − 1.12·fontSize
    /// (24.8 for the default 10pt state).</summary>
    private static Point ComputePosition(Page page, PaginationArtifact spec, string text)
    {
        double fs = spec.TextState.FontSize > 0 ? spec.TextState.FontSize : 10;
        var measureFont = spec.TextState.FontName is { } fn && Standard14Fonts.IsStandard14(fn)
            ? fn
            : "Helvetica"; // Arial etc. share Helvetica-class digit metrics
        double textW = 0;
        foreach (var ch in text)
        {
            var w = ch <= 255 ? Standard14Fonts.GetWidth(measureFont, ch) : 0;
            if (w <= 0) w = Standard14Fonts.GetDefaultWidth(measureFont);
            textW += w;
        }
        textW = textW * fs / 1000.0;

        double x = spec.ArtifactHorizontalAlignment switch
        {
            HorizontalAlignment.Left => 72,
            HorizontalAlignment.Right => page.Width - textW - 72,
            _ => (page.Width - textW) / 2,
        };
        double y = spec.ArtifactVerticalAlignment switch
        {
            VerticalAlignment.Top => page.Height - 36,
            VerticalAlignment.Bottom => 36 - 1.12 * fs,
            _ => (page.Height - fs) / 2,
        };
        return new Point(x, y);
    }
}
