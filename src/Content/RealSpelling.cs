namespace Aspose.Pdf.Content;

/// <summary>
/// How a real number is spelled in a content stream.
///
/// The format says a real is a run of digits with a point in it and nothing
/// else — no exponent, no thousands separator. It does NOT say how many places
/// to keep, and that is a real choice: every place kept is a byte in every
/// coordinate on every page, and every place dropped is a position a reader
/// cannot recover. A library picks one and lives with it.
///
/// It is settable because a file is not only read by a renderer. A caller
/// writing content that has to match another producer's bytes — to compare two
/// files, to sign one, to reproduce a document exactly — needs that producer's
/// spelling, and no amount of precision substitutes for the right one.
/// </summary>
public abstract class RealSpelling
{
    /// <summary>What this library writes for its own documents.</summary>
    public static RealSpelling Default { get; } = new PlainSpelling();

    /// <summary>A coordinate, a size, a matrix term, a spacing.</summary>
    public abstract string Geometry(double value);

    /// <summary>
    /// A colour component.
    ///
    /// Its own method because a component is a fraction of a range rather than a
    /// measurement, and a library may reasonably keep more of it: a third of
    /// full red is 0.333333 at six places and visibly a different colour at two.
    /// A spelling that draws no distinction answers both the same way.
    /// </summary>
    public abstract string Colour(double value);

    /// <summary>
    /// Plain decimal, six places for geometry and ten for colour, trailing zeros
    /// dropped.
    ///
    /// ⚠ Never the "G" format, which emits an exponent for very small or very
    /// large magnitudes: a conforming reader rejects it and drops the whole
    /// operator, so a page loses the mark rather than misplacing it.
    /// </summary>
    private sealed class PlainSpelling : RealSpelling
    {
        public override string Geometry(double value) => Written(value, "0.######");

        public override string Colour(double value) => Written(value, "0.##########");

        private static string Written(double value, string shape)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "0";
            var said = value.ToString(shape, System.Globalization.CultureInfo.InvariantCulture);
            // A tiny negative rounds to zero and would be written "-0".
            return said == "-0" ? "0" : said;
        }
    }
}
