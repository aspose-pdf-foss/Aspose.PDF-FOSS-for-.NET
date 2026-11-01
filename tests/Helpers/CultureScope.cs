using System.Globalization;

namespace Aspose.Pdf.Tests.Helpers;

/// <summary>
/// Sets the current thread's culture and UI culture for the span of a <c>using</c> and restores
/// the previous ones on dispose. Thread-local, so tests running in parallel are not affected.
/// </summary>
internal sealed class CultureScope : IDisposable
{
    /// <summary>The name that stands for <see cref="Synthetic"/> wherever a test or the run takes a culture name.</summary>
    internal const string SyntheticName = "synthetic";

    // he-IL writes its signs behind this mark on ICU, ar-SA behind an Arabic letter mark.
    private const string LeftToRightMark = "\u200E";

    /// <summary>
    /// A culture no machine has, built on every runtime from the same few settings, that breaks at once each
    /// assumption a writer or reader of PDF syntax can make about the machine's culture: a comma decimal separator
    /// with '.' between thousands, signs behind a left-to-right mark, the Thai Buddhist calendar and '.' between
    /// hours, minutes and seconds. A real culture carries these only where its data say so: on .NET Framework
    /// (NLS) ar-SA has '.' and '-', and sv-SE and he-IL have '-'.
    /// </summary>
    internal static CultureInfo Synthetic { get; } = BuildSynthetic();

    private readonly CultureInfo _culture;
    private readonly CultureInfo _uiCulture;

    public CultureScope(string name) : this(Resolve(name)) { }

    public CultureScope(CultureInfo culture)
    {
        _culture = CultureInfo.CurrentCulture;
        _uiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>The culture <paramref name="name"/> stands for: <see cref="Synthetic"/> for <see cref="SyntheticName"/>.</summary>
    internal static CultureInfo Resolve(string name)
        => name == SyntheticName ? Synthetic : CultureInfo.GetCultureInfo(name);

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

    private static CultureInfo BuildSynthetic()
    {
        // th-TH is the culture whose calendars include the Thai Buddhist one.
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("th-TH").Clone();
        culture.DateTimeFormat.Calendar = new ThaiBuddhistCalendar();
        culture.DateTimeFormat.TimeSeparator = ".";
        var number = culture.NumberFormat;
        number.NumberDecimalSeparator = ",";
        number.NumberGroupSeparator = ".";
        number.NegativeSign = LeftToRightMark + "-";
        number.PositiveSign = LeftToRightMark + "+";
        return CultureInfo.ReadOnly(culture);
    }
}
