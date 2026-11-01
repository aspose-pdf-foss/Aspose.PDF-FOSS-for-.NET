using System.Globalization;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class TestCultureTests
{
    private const string LeftToRightMark = "\u200E";

    /// <summary>What a whole run under each culture is for: the culture data its numbers, signs, dates and case
    /// rules must carry, or the run tests nothing of it. NLS - .NET Framework, or .NET told to use it - writes
    /// ar-SA with '.' and '-', and sv-SE and he-IL with '-', so those runs fail here rather than pass empty.</summary>
    private static readonly Dictionary<string, (string What, Func<CultureInfo, bool> Holds)[]> Legs = new()
    {
        ["uk-UA"] = new[] { DecimalComma },
        ["sv-SE"] = new (string, Func<CultureInfo, bool>)[] { DecimalComma, ("a minus sign other than '-'", c => c.NumberFormat.NegativeSign != "-") },
        ["th-TH"] = new[] { BuddhistYears },
        ["ar-SA"] = new (string, Func<CultureInfo, bool>)[]
        {
            ("a decimal separator other than '.'", c => c.NumberFormat.NumberDecimalSeparator != "."),
            ("a mark before the minus sign", c => c.NumberFormat.NegativeSign.Length > 1),
            ("a calendar other than the Gregorian one", c => c.DateTimeFormat.Calendar is not GregorianCalendar),
        },
        ["he-IL"] = new (string, Func<CultureInfo, bool>)[] { ("a mark before the minus sign", c => c.NumberFormat.NegativeSign.Length > 1) },
        ["tr-TR"] = new (string, Func<CultureInfo, bool>)[] { ("'I' lower-cased to a dotless i", c => "I".ToLower(c) == "\u0131") },
        [CultureScope.SyntheticName] = new (string, Func<CultureInfo, bool>)[]
        {
            DecimalComma, BuddhistYears,
            ("signs behind a mark", c => c.NumberFormat.NegativeSign == LeftToRightMark + "-" && c.NumberFormat.PositiveSign == LeftToRightMark + "+"),
            ("'.' between thousands", c => c.NumberFormat.NumberGroupSeparator == "."),
            ("'.' between hours, minutes and seconds", c => c.DateTimeFormat.TimeSeparator == "."),
        },
    };

    private static (string, Func<CultureInfo, bool>) DecimalComma => ("a decimal comma", c => c.NumberFormat.NumberDecimalSeparator == ",");

    private static (string, Func<CultureInfo, bool>) BuddhistYears => ("the Thai Buddhist calendar", c => c.DateTimeFormat.Calendar is ThaiBuddhistCalendar);

    /// <summary>What tells a culture apart from another of its name: the synthetic one is named th-TH.</summary>
    private static string Fingerprint(CultureInfo c)
        => string.Join(" ", c.Name, c.NumberFormat.NumberDecimalSeparator, c.NumberFormat.NegativeSign, c.DateTimeFormat.Calendar.GetType().Name);

    [Fact]
    public async Task TheCultureTheRunIsAskedToUseIsTheOneEveryTestThreadSees()
    {
        if (TestCulture.Requested is not { } name) return;
        var expected = Fingerprint(CultureScope.Resolve(name));

        Assert.Equal(expected, Fingerprint(CultureInfo.CurrentCulture));
        Assert.Equal(expected, Fingerprint(CultureInfo.CurrentUICulture));
        var pooled = await Task.Run(() => Fingerprint(CultureInfo.CurrentCulture));
        Assert.Equal(expected, pooled);
        var fresh = string.Empty;
        var thread = new Thread(() => fresh = Fingerprint(CultureInfo.CurrentCulture));
        thread.Start();
        thread.Join();
        Assert.Equal(expected, fresh);
    }

    [Fact]
    public void TheCultureTheRunIsAskedToUseCarriesWhatItsRunIsFor()
    {
        if (TestCulture.Requested is not { } name || !Legs.TryGetValue(name, out var leg)) return;

        var culture = CultureScope.Resolve(name);
        var missing = leg.Where(l => !l.Holds(culture)).Select(l => l.What).ToList();

        Assert.True(missing.Count == 0,
            $"{name} on this runtime lacks {string.Join(" and ", missing)}: a run under it tests none of that.");
    }

    [Fact]
    public void TheSyntheticCultureBreaksEachAssumptionOnEveryRuntime()
    {
        var culture = CultureScope.Synthetic;
        var when = new DateTime(2026, 10, 19, 12, 30, 45);

        Assert.Equal(LeftToRightMark + "-1", (-1).ToString(culture));
        Assert.Equal("0,5", 0.5.ToString(culture));
        Assert.False(int.TryParse("-1", NumberStyles.Integer, culture, out _));
        Assert.False(int.TryParse("+1", NumberStyles.Integer, culture, out _));
        Assert.Equal(335, double.Parse("33.5", NumberStyles.Float | NumberStyles.AllowThousands, culture));
        Assert.Equal("2569-10-19T12.30.45", when.ToString("yyyy-MM-ddTHH:mm:ss", culture));
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData(CultureScope.SyntheticName)]
    public void ACultureScopeRestoresTheCultureItReplaced(string name)
    {
        var before = Fingerprint(CultureInfo.CurrentCulture);
        using (new CultureScope(name))
            Assert.Equal(Fingerprint(CultureScope.Resolve(name)), Fingerprint(CultureInfo.CurrentCulture));
        Assert.Equal(before, Fingerprint(CultureInfo.CurrentCulture));
    }
}
