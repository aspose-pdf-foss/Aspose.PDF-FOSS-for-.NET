using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal static partial class SystemFontResolver
{
// The family aliases of the system font resolver.

    /// <summary>The vendor-family arms of <see cref="MapFontName"/>: the Windows and vendor
    /// families that keep their own name, the ones aliased to Helvetica because they do not
    /// ship with the OS, and the Yu Gothic collection faces. Returns the file name and the
    /// family the candidate search runs with.</summary>
    private static (string? fileName, string family) AliasVendorFamily(string name, string family, string? fileName)
    {
        if (family.Equals("Tahoma", StringComparison.OrdinalIgnoreCase))
        {
            // Tahoma ships with Windows; nothing to do but keep family name as-is so
            // GetCandidateFiles tries tahoma.ttf / Tahoma.ttf.
        }
        else if (family.Equals("TrebuchetMS", StringComparison.OrdinalIgnoreCase) ||
                 family.Equals("Trebuchet MS", StringComparison.OrdinalIgnoreCase) ||
                 family.Equals("TrebuchetMS-Bold", StringComparison.OrdinalIgnoreCase))
        {
            family = "Trebuchet MS";
        }
        else if (family.StartsWith("Verdana", StringComparison.OrdinalIgnoreCase))
        {
            family = "Verdana";
        }
        else if (family.StartsWith("Calibri", StringComparison.OrdinalIgnoreCase))
        {
            family = "Calibri";
        }
        else if (family.StartsWith("Cambria", StringComparison.OrdinalIgnoreCase))
        {
            family = "Cambria";
        }
        else if (family.StartsWith("Georgia", StringComparison.OrdinalIgnoreCase))
        {
            family = "Georgia";
        }
        else if (family.Replace(" ", "").StartsWith("ComicSansMS", StringComparison.OrdinalIgnoreCase))
        {
            // "ComicSansMS", "Comic Sans MS" and "ComicSansMS-Bold" are one family, whose
            // Windows files (comic*.ttf) and Linux stand-in are found under this name.
            family = "Comic Sans MS";
        }
        else if (family.Replace(" ", "").StartsWith("MicrosoftSansSerif", StringComparison.OrdinalIgnoreCase))
        {
            // Microsoft Sans Serif (Windows micross.ttf, regular only) is metrically
            // close to Arial/Helvetica and lacks separate bold/italic files. Alias it to
            // Helvetica so every style resolves (arial*.ttf on Windows, Liberation/DejaVu
            // on Linux) instead of dropping the text when the named font can't be found.
            fileName = "Helvetica.ttc";
            family = "Helvetica";
        }
        else if (family.StartsWith("Univers", StringComparison.OrdinalIgnoreCase))
        {
            // Univers is a Linotype font that doesn't ship with Windows or macOS by
            // default. Fall back to Helvetica/Arial — metrically close enough that
            // the text is at least readable instead of rendering as a blank box.
            fileName = "Helvetica.ttc";
            family = "Helvetica";
        }
        else if (family.Replace(" ", "").StartsWith("CenturyGothic", StringComparison.OrdinalIgnoreCase))
        {
            // Century Gothic (a geometric sans) doesn't ship with Windows; without an
            // alias its non-embedded text drops entirely (the whole letter body
            // rendered blank). Fall back to Helvetica/Arial so the text
            // renders — another sans-serif, metrically close enough to be legible.
            fileName = "Helvetica.ttc";
            family = "Helvetica";
        }
        else if (family.Replace(" ", "").StartsWith("YuGothic", StringComparison.OrdinalIgnoreCase))
        {
            // Yu Gothic / Yu Gothic UI family. Each weight lives in a specific
            // YuGoth*.ttc, and the UI and non-UI faces WITHIN a collection have
            // different glyph orders — so for a Type0/Identity CID (content codes are
            // the authoring face's glyph ids) the EXACT face must be embedded, not just
            // one of the right weight. Map the PDF name to its collection file and the
            // precise face name; ExtractFromTtc then matches that face by name.
            var key = name.Replace(" ", "").Replace(",", "");
            (fileName, family) =
                  key.StartsWith("YuGothicUISemibold", StringComparison.OrdinalIgnoreCase) ? ("YuGothB.ttc", "Yu Gothic UI Semibold")
                : key.StartsWith("YuGothicUISemilight", StringComparison.OrdinalIgnoreCase) ? ("YuGothR.ttc", "Yu Gothic UI Semilight")
                : key.StartsWith("YuGothicUILight", StringComparison.OrdinalIgnoreCase) ? ("YuGothL.ttc", "Yu Gothic UI Light")
                : key.StartsWith("YuGothicUIBold", StringComparison.OrdinalIgnoreCase) ? ("YuGothB.ttc", "Yu Gothic UI Bold")
                : key.StartsWith("YuGothicUI", StringComparison.OrdinalIgnoreCase) ? ("YuGothM.ttc", "Yu Gothic UI Regular")
                : key.StartsWith("YuGothicMedium", StringComparison.OrdinalIgnoreCase) ? ("YuGothM.ttc", "Yu Gothic Medium")
                : key.StartsWith("YuGothicLight", StringComparison.OrdinalIgnoreCase) ? ("YuGothL.ttc", "Yu Gothic Light")
                : key.StartsWith("YuGothicBold", StringComparison.OrdinalIgnoreCase) ? ("YuGothB.ttc", "Yu Gothic Bold")
                : ("YuGothR.ttc", "Yu Gothic Regular");
        }

        return (fileName, family);
    }

    /// <summary>The free faces a Linux system ships in place of the Windows families, as the
    /// file names its font packages install: Liberation (metric-compatible with Arial, Times
    /// New Roman and Courier New), DejaVu Sans after it for Helvetica/Arial, Carlito and
    /// Caladea (metric-compatible with Calibri and Cambria), DejaVu Sans for Verdana, and
    /// Comic Neue for Comic Sans MS (a look-alike; no free face matches its metrics).</summary>
    internal static IEnumerable<string> LinuxEquivalents(string family, bool bold, bool italic)
    {
        var styled = bold ? (italic ? "BoldItalic" : "Bold") : (italic ? "Italic" : "Regular");
        var dejaVu = bold ? (italic ? "DejaVuSans-BoldOblique" : "DejaVuSans-Bold")
            : (italic ? "DejaVuSans-Oblique" : "DejaVuSans");
        switch (family)
        {
            case "Helvetica" or "Arial":
                yield return $"LiberationSans-{styled}.ttf";
                yield return $"{dejaVu}.ttf";
                break;
            case "Times" or "Times New Roman":
                yield return $"LiberationSerif-{styled}.ttf";
                break;
            case "Courier" or "Courier New":
                yield return $"LiberationMono-{styled}.ttf";
                break;
            case "Calibri":
                yield return $"Carlito-{styled}.ttf";
                break;
            case "Cambria":
                yield return $"Caladea-{styled}.ttf";
                break;
            case "Verdana":
                yield return $"{dejaVu}.ttf";
                break;
            case "Comic Sans MS":
                yield return $"ComicNeue-{styled}.otf";
                break;
        }
    }
}
