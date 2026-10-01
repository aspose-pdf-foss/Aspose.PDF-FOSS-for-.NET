using System.Runtime.Versioning;
using Aspose.Pdf.Printing;
using Aspose.Pdf.Tests.Helpers;
using Xunit;
using Margins = Aspose.Pdf.Devices.Margins;
using NativeDuplex = System.Drawing.Printing.Duplex;
using NativeMargins = System.Drawing.Printing.Margins;
using NativePageSettings = System.Drawing.Printing.PageSettings;
using NativePaperSize = System.Drawing.Printing.PaperSize;
using NativePaperSource = System.Drawing.Printing.PaperSource;
using NativePrinterResolution = System.Drawing.Printing.PrinterResolution;
using NativePrinterResolutionKind = System.Drawing.Printing.PrinterResolutionKind;
using NativePrinterSettings = System.Drawing.Printing.PrinterSettings;
using NativePrintRange = System.Drawing.Printing.PrintRange;

namespace Aspose.Pdf.Tests.Printing;

/// <summary>
/// The printing settings convert to and from the System.Drawing types a printer driver
/// speaks without losing a field.
/// </summary>
[SupportedOSPlatform("windows")]
public class PrintingConversionTests
{
    [WindowsOnlyFact]
    public void ToNativePaperSize_PredefinedSize_CarriesKindAndNoName()
    {
        var paperSize = PaperSizes.A4;
        var nativeSize = paperSize.ToNativePaperSize();

        Assert.Equal((int)paperSize.Kind, nativeSize.RawKind);
        // A standard size is named by its kind, and the driver supplies the name.
        Assert.Equal(string.Empty, nativeSize.PaperName);
        Assert.Equal(827, paperSize.Width);
        Assert.Equal(1169, paperSize.Height);
    }

    [WindowsOnlyFact]
    public void ToNativePaperSize_CustomSize_KeepsItsOwnName()
    {
        var paperSize = new PaperSize("A4", 827, 1169);
        var nativeSize = paperSize.ToNativePaperSize();

        Assert.Equal((int)PaperKind.Custom, nativeSize.RawKind);
        Assert.Equal("A4", nativeSize.PaperName);
        Assert.Equal(827, nativeSize.Width);
        Assert.Equal(1169, nativeSize.Height);
    }

    [WindowsOnlyFact]
    public void ToAsposePaperSize_CopiesEveryProperty()
    {
        var nativeSize = new NativePaperSize("A4", 827, 1169) { RawKind = (int)PaperKind.A4 };

        var paperSize = nativeSize.ToAsposePaperSize();

        Assert.Equal(PaperKind.A4, paperSize.Kind);
        Assert.Equal("A4", paperSize.PaperName);
        Assert.Equal(827, paperSize.Width);
        Assert.Equal(1169, paperSize.Height);
    }

    [WindowsOnlyFact]
    public void PaperSource_DriverNumberedBin_ReadsBackAsCustom()
    {
        var paperSource = new PaperSource { RawKind = (int)PaperSourceKind.Custom + 3 };

        Assert.Equal(PaperSourceKind.Custom, paperSource.Kind);
        Assert.Equal((int)PaperSourceKind.Custom + 3, paperSource.RawKind);
    }

    [WindowsOnlyFact]
    public void PaperSource_RoundTripsThroughNative()
    {
        var paperSource = new PaperSource(PaperSourceKind.AutomaticFeed, "TestSource");

        var native = paperSource.ToNativePaperSource();
        var back = native.ToAsposePaperSource();

        Assert.Equal((int)paperSource.Kind, native.RawKind);
        Assert.Equal(paperSource.SourceName, native.SourceName);
        Assert.Equal(paperSource.Kind, back.Kind);
        Assert.Equal(paperSource.SourceName, back.SourceName);
    }

    [WindowsOnlyFact]
    public void PrinterResolution_RoundTripsThroughNative()
    {
        var resolution = new PrinterResolution { Kind = PrinterResolutionKind.Custom, X = 300, Y = 300 };

        var native = resolution.ToNativePrinterResolution();
        var back = native.ToAsposePrinterResolution();

        Assert.Equal((NativePrinterResolutionKind)resolution.Kind, native.Kind);
        Assert.Equal(300, native.X);
        Assert.Equal(300, native.Y);
        Assert.Equal(resolution.Kind, back.Kind);
        Assert.Equal(300, back.X);
        Assert.Equal(300, back.Y);
    }

    [WindowsOnlyFact]
    public void PrinterSettings_RoundTripsThroughNative()
    {
        var printerSettings = new PrinterSettings
        {
            Collate = true,
            Copies = 1,
            Duplex = Duplex.Simplex,
            FromPage = 1,
            MaximumPage = 100,
            MinimumPage = 0,
            PrinterName = "Test Printer",
            PrintFileName = "test.ps",
            PrintRange = PrintRange.AllPages,
            PrintToFile = true,
            ToPage = 10,
        };

        var native = printerSettings.ToNativePrinterSettings();

        Assert.True(native.Collate);
        Assert.Equal(1, native.Copies);
        Assert.Equal(NativeDuplex.Simplex, native.Duplex);
        Assert.Equal(1, native.FromPage);
        Assert.Equal(100, native.MaximumPage);
        Assert.Equal(0, native.MinimumPage);
        Assert.Equal("Test Printer", native.PrinterName);
        Assert.Equal("test.ps", native.PrintFileName);
        Assert.Equal(NativePrintRange.AllPages, native.PrintRange);
        Assert.True(native.PrintToFile);
        Assert.Equal(10, native.ToPage);
    }

    [WindowsOnlyFact]
    public void ToAsposePrinterSettings_CopiesEveryProperty()
    {
        var native = new NativePrinterSettings
        {
            Collate = true,
            Copies = 1,
            Duplex = NativeDuplex.Simplex,
            FromPage = 1,
            MaximumPage = 100,
            MinimumPage = 0,
            PrinterName = "Test Printer",
            PrintFileName = "test.ps",
            PrintRange = NativePrintRange.AllPages,
            PrintToFile = true,
            ToPage = 10,
        };

        var printerSettings = native.ToAsposePrinterSettings();

        Assert.True(printerSettings.Collate);
        Assert.Equal(1, printerSettings.Copies);
        Assert.Equal(Duplex.Simplex, printerSettings.Duplex);
        Assert.Equal(1, printerSettings.FromPage);
        Assert.Equal(100, printerSettings.MaximumPage);
        Assert.Equal(0, printerSettings.MinimumPage);
        Assert.Equal("Test Printer", printerSettings.PrinterName);
        Assert.Equal("test.ps", printerSettings.PrintFileName);
        Assert.Equal(PrintRange.AllPages, printerSettings.PrintRange);
        Assert.True(printerSettings.PrintToFile);
        Assert.Equal(10, printerSettings.ToPage);
    }

    [WindowsOnlyFact]
    public void PageSettings_MarginsAndFlagsRoundTripThroughNative()
    {
        var pageSettings = new PageSettings
        {
            Color = true,
            Landscape = true,
            Margins = new Margins(10, 20, 30, 40),
            PaperSize = PaperSizes.A4,
            PaperSource = PaperSources.AutomaticFeed,
            PrinterResolution = new PrinterResolution(),
            PrinterSettings = new PrinterSettings(),
        };

        var native = pageSettings.ToNativePageSettings();

        Assert.True(native.Color);
        Assert.True(native.Landscape);
        Assert.Equal(10, native.Margins.Left);
        Assert.Equal(20, native.Margins.Right);
        Assert.Equal(30, native.Margins.Top);
        Assert.Equal(40, native.Margins.Bottom);
    }

    [WindowsOnlyFact]
    public void ToAsposePageSettings_CopiesMarginsAndFlags()
    {
        var native = new NativePageSettings
        {
            Color = true,
            Landscape = true,
            Margins = new NativeMargins(10, 20, 30, 40),
            PaperSize = new NativePaperSize(),
            PaperSource = new NativePaperSource(),
            PrinterResolution = new NativePrinterResolution(),
            PrinterSettings = new NativePrinterSettings(),
        };

        var pageSettings = native.ToAsposePageSettings();

        Assert.True(pageSettings.Color);
        Assert.True(pageSettings.Landscape);
        Assert.Equal(10, pageSettings.Margins.Left);
        Assert.Equal(20, pageSettings.Margins.Right);
        Assert.Equal(30, pageSettings.Margins.Top);
        Assert.Equal(40, pageSettings.Margins.Bottom);
    }

    [WindowsOnlyFact]
    public void PrinterSettings_WithNoNames_ConvertsWithoutRaising()
    {
        // The native settings reject a blank printer or file name, so a set that names
        // neither has to leave both alone rather than assign an empty string.
        var native = new PrinterSettings().ToNativePrinterSettings();

        Assert.False(native.PrintToFile);
    }
}
